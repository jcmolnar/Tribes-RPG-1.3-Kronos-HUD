//==============================================
// KronosNPC_Server.cs - bridge the EXISTING NPC dialogue into a window
//==============================================
// Kronos NPCs already have full dialogue (the #say state machine in
// comchat.cs: quest hand-ins, enemy spawns, teleporters, generic SAY/NSAY,
// etc.). This does NOT add new dialogue - it just mirrors that dialogue
// into the modern KronosNPC window for HUD clients and lets them click the
// bracketed [keyword] options instead of typing #say.
//
// HOW IT WORKS
//   - Player bumps a non-banker/non-shop town bot -> Player::onCollision
//     calls KronosNPC_Open (HUD clients only).
//   - KronosNPC_Open just opens the client window + cursor and flags
//     %client.knpcWinOpen. The client then sends "#say hi" through the
//     normal path, so the bot's real greeting fires.
//   - AI::sayLater (Ai.cs, the function EVERY bot uses to talk) is hooked
//     to also remoteEval "KNPCLine" to the client while the window is open,
//     so the real responses show in the window.
//   - Clicking a [keyword] option just sends "#say keyword" - the existing
//     comchat dialogue logic runs unchanged.
//
// VANILLA-SAFE: only clients with $client.hasKronosHUD ever set
// knpcWinOpen or get KNPC* pushes; everyone else uses stock #say.
//
// Server.cs - exec(KronosNPC_Server); added to load this.
// Player.cs - Player::onCollision generic-townbot branch calls KronosNPC_Open.
//==============================================

// Open the dialogue window for a HUD client bumping a generic town bot.
// (No dialogue is generated here - the client greets via #say and the
// AI::sayLater hook feeds the responses back.)
function KronosNPC_Open(%client, %botId)
{
	if(!%client.hasKronosHUD)
		return;
	if(Player::isAiControlled(%client))
		return;

	// collisions fire every tick - don't reopen while already conversing,
	// and don't immediately reopen the bot you just left (3s) while still
	// standing on it. Walking away and back reopens it.
	%now = getSimTime();
	// don't reopen while a conversation is genuinely active; the 60s guard
	// recovers from a stale flag (e.g. client closed without notifying)
	if(%client.knpcWinOpen != "" && (%now - %client.knpcTime) < 60)
		return;
	if(%client.knpcCloseBot == %botId && (%now - %client.knpcCloseTime) < 3.0)
		return;

	%display = Client::getName(%botId);
	if(%display == "" || %display == -1 || %display == "0")
		%display = "Stranger";

	%client.knpcWinOpen = true;
	%client.knpcBot = %botId;
	%client.knpcTime = %now;

	// open the window (client clears it, then auto-sends "#say hi")
	remoteEval(%client, "KNPCBegin", %display);

	// raise the mouse cursor (score dialog), same as the shop
	schedule("if(" @ %client @ ".knpcWinOpen != \"\") Client::setMenuScoreVis(" @ %client @ ", true);", 0.15);
}

function KronosNPC_Close(%client, %fromCancelMenu)
{
	if(%client.knpcWinOpen == "")
		return;
	%client.knpcWinOpen = "";
	%client.knpcCloseBot = %client.knpcBot;
	%client.knpcCloseTime = getSimTime();
	remoteEval(%client, "KNPCClose");
	if(!%fromCancelMenu)
		Client::setMenuScoreVis(%client, false);
}

// Client closed the window (Goodbye / TAB / cursor away)
function remoteKNPCClose(%client)
{
	if(!%client.hasKronosHUD)
		return;
	KronosNPC_Close(%client, true);
}

// ============================================
// Option extraction (server-side, so it's reliable)
// ============================================
// NPCs present the phrases the player can say either bracketed - [loop],
// [yuliple], quest cues - or as ALL-CAPS words (ENTER, YES, NO, BUY,
// DEPOSIT...). We extract both here and send the client an explicit option
// list, so the client never has to guess. Detecting upper case must use
// String::Compare (case-sensitive engine compare): String::findSubStr is
// case-INSENSITIVE here, and the "==" operator numerically coerces letters
// like "E" (the classic 'E'=='0' bug), both of which corrupt the result.

function KronosNPC_AddOpt(%list, %kw)
{
	%kw = String::toLower(%kw);
	// strip trailing punctuation the prompt text may attach (e.g. "enter!",
	// "housecurama.", "yes)") so the keyword matches the real trigger
	%more = true;
	while(%more && String::len(%kw) > 0)
	{
		%last = String::getSubStr(%kw, String::len(%kw) - 1, 1);
		if(String::findSubStr(".,!?():;", %last) != -1)
			%kw = String::getSubStr(%kw, 0, String::len(%kw) - 1);
		else
			%more = false;
	}
	if(%kw == "")
		return %list;
	for(%i = 0; (%w = GetWord(%list, %i)) != -1; %i++)
		if(String::Compare(%w, %kw) == 0)
			return %list;   // dedupe
	if(%list == "")
		return %kw;
	return %list @ " " @ %kw;
}

// Returns a space-separated, lowercased, deduped option list (or "").
function KronosNPC_ExtractOpts(%text)
{
	%out = "";

	// bracketed [keyword] - the preferred, 100%-reliable form
	%rest = %text;
	%g = 0;
	while(%g < 40 && (%lb = String::findSubStr(%rest, "[")) != -1)
	{
		%g++;
		%rest = String::getSubStr(%rest, %lb + 1, 99999);
		%rb = String::findSubStr(%rest, "]");
		if(%rb == -1)
			break;
		%kw = String::getSubStr(%rest, 0, %rb);
		%rest = String::getSubStr(%rest, %rb + 1, 99999);
		// a single bracket may hold a comma/space list, e.g. "[A, B, C]" -
		// split it into one option per token
		%kw = String::replace(%kw, ",", " ");
		for(%wi = 0; (%w = GetWord(%kw, %wi)) != -1; %wi++)
			%out = KronosNPC_AddOpt(%out, %w);
	}

	// If the line had ANY bracketed options, trust ONLY those (so stray
	// ALL-CAPS acronyms in a bracketed prompt aren't picked up as junk).
	// Un-bracketed prompts fall back to ALL-CAPS detection below.
	if(%out != "")
		return %out;

	// ALL-CAPS words (>= 2 letters)
	%word = "";
	%len = String::len(%text);
	for(%i = 0; %i <= %len; %i++)
	{
		if(%i < %len)
			%c = String::getSubStr(%text, %i, 1);
		else
			%c = " ";   // force a final flush
		// uppercase letter <=> it differs from its lowercase form
		if(String::Compare(%c, String::toLower(%c)) != 0)
			%word = %word @ %c;
		else
		{
			if(String::len(%word) >= 2)
				%out = KronosNPC_AddOpt(%out, %word);
			%word = "";
		}
	}

	return %out;
}

echo("KronosNPC_Server: NPC dialogue window bridge loaded");
