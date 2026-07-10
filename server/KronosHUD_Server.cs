//==============================================
// KronosHUD_Server.cs - Server-side HUD stat push
//==============================================
// Pushes RPG stats to clients for the ScriptGL KronosHUD.
// Vitals (HP, Mana, EXP, Gold, Level) are sent via remoteEval
// when they change. Metadata (Class, Zone) sent in a second call
// to avoid space-in-string issues with remoteEval arg parsing.
//
//----------------------------------------------
// HOOKS / INTEGRATIONS:
//----------------------------------------------
// Server.cs           - exec(KronosHUD_Server); added to load this script
// rpgstats.cs         - Game::refreshClientScore() calls KronosHUD_Push()
// rpgfunk.cs          - RefreshAll() calls KronosHUD_Push()
// playerdamage.cs     - Damage path calls KronosHUD_PushTarget()
//==============================================

// ============================================
// Client handshake - KronosHUD clients announce themselves (sent
// from the client's remoteKronosHUD handler, re-affirmed every 30s)
// so features that REPLACE stock displays only apply to HUD users.
// Vanilla clients never send this and keep stock behavior.
// ============================================

function remoteKHudOn(%clientId)
{
	%clientId.hasKronosHUD = true;
}

// Weapon mount info (weapons.cs / newstuff.cs onMount handlers).
// HUD clients get a ~3s popup; everyone else keeps the bottomprint.
function KronosWeaponInfo(%client, %text)
{
	if(%client.hasKronosHUD)
		remoteEval(%client, "KronosWeapon", %text);
	else
		bottomprint(%client, %text);
}

// Item examine info (WhatIs text - backpack/belt/shop "Examine").
// HUD clients get a ~10s overlay on the bottom-right info panel;
// everyone else keeps the stock bottomprint with its duration.
function KronosExamineInfo(%client, %text, %dur)
{
	if(%client.hasKronosHUD)
		remoteEval(%client, "KronosExamine", %text);
	else
		bottomprint(%client, %text, %dur);
}

// ============================================
// Kronos Shop / Inventory screen (HUD clients only)
// ============================================
// Replaces the stock CmdInventory gui mode for HUD clients; vanilla
// clients keep the stock screen (gates live in shopping.cs SetupShop
// and remote.cs remoteToggleInventoryMode). The client sends the SAME
// remoteEval buyItem/sellItem/useItem/dropItem messages the stock gui
// buttons send, so all economy logic (haggled prices, two-click sell
// quotes, wait delays) is unchanged. The score dialog is opened for
// the mouse cursor (stock score controls are off-screen on HUD
// clients); closing the score screen closes the shop.

$KronosShop::MaxRows = 48; // per pane

function KronosShop_Open(%clientId, %mode, %shopName)
{
	%clientId.kshopOpen = %mode;
	remoteEval(%clientId, "KShopOpen", %mode, %shopName);
	KronosShop_PushInv(%clientId);
	if(%mode == "shop")
		KronosShop_PushStock(%clientId);
	else
		remoteEval(%clientId, "KShopStockCount", 0);

	// Cursor: open the score dialog. Scheduled because menu picks that
	// lead here are followed by remoteMenuSelect's own
	// setMenuScoreVis(false) when the menu closes - ours must land after.
	schedule("if(" @ %clientId @ ".kshopOpen != \"\") Client::setMenuScoreVis(" @ %clientId @ ", true);", 0.15);
}

// Display heading for belt categories - the first char is the sort
// key (like ItemData headings "aArmor"/"bWeapons"), so belt rows
// interleave with standard rows under merged category headers.
// Belt Armor deliberately maps to "aArmor" to merge with ItemData armor.
function KronosShop_BeltHeading(%cat)
{
	if(%cat == "Armor")        return "aArmor";
	if(%cat == "Consumables")  return "cConsumables";
	if(%cat == "Deployables")  return "dDeployables";
	if(%cat == "Accessories")  return "eAccessories";
	if(%cat == "KeyItems")     return "kKey Items";
	if(%cat == "Other")        return "oOther";
	if(%cat == "QuestItems")   return "qQuest Items";
	return "o" @ %cat;
}

// Own carried items - ItemData entries (mirrors the stock
// FearGuiInventory enumeration: showInventory + nonzero count) PLUS
// all belt/backpack items, merged into one list. Row kind: "d" =
// ItemData (ref = index, stock buyItem/sellItem/... protocol),
// "b" = belt item (ref = belt item name, KShopBelt* protocol).
function KronosShop_PushInv(%clientId)
{
	%sent = 0;
	%max = getNumItems();
	for(%z = 0; %z < %max && %sent < $KronosShop::MaxRows; %z++)
	{
		%item = getItemData(%z);
		if(!%item.showInventory)
			continue;
		%cnt = Player::getItemCount(%clientId, %item);
		if(%cnt < 1)
			continue;
		// description last - it may contain spaces
		remoteEval(%clientId, "KShopInv", %sent, "d", %z, %cnt, %item.heading, %item.description);
		%sent++;
	}

	// belt/backpack items: per-category player lists are
	// stuff-strings of "item count" pairs
	for(%c = 1; $Belt::Categories[%c] != ""; %c++)
	{
		%cat = $Belt::Categories[%c];
		%bhead = KronosShop_BeltHeading(%cat);
		%list = fetchData(%clientId, %cat);
		%more = true;
		for(%w = 0; %more && %sent < $KronosShop::MaxRows; %w += 2)
		{
			%bitem = GetWord(%list, %w);
			if(%bitem == -1 || %bitem == "" || %bitem == "0")
				%more = false;
			else
			{
				%bcnt = GetWord(%list, %w + 1);
				if(%bcnt >= 1)
				{
					%bname = $BeltItem[%bitem, "Name"];
					if(%bname == "")
						%bname = %bitem;
					remoteEval(%clientId, "KShopInv", %sent, "b", %bitem, %bcnt, %bhead, %bname);
					%sent++;
				}
			}
		}
	}

	remoteEval(%clientId, "KShopInvCount", %sent);
}

// Shop stock - the ItemData list captured by SetupShop (matches the
// stock gui exactly) plus the merchant's belt items (previously only
// reachable through the Buy Accessories / Buy Consumables menus)
function KronosShop_PushStock(%clientId)
{
	%sent = 0;
	for(%i = 0; %i < %clientId.kshopCount && %sent < $KronosShop::MaxRows; %i++)
	{
		%z = %clientId.kshopIdx[%i];
		%item = getItemData(%z);
		remoteEval(%clientId, "KShopStock", %sent, "d", %z, getBuyCost(%clientId, %item), %item.heading, %item.description);
		%sent++;
	}

	for(%i = 0; %i < %clientId.kshopBeltCnt && %sent < $KronosShop::MaxRows; %i++)
	{
		%bitem = %clientId.kshopBeltItem[%i];
		%bname = $BeltItem[%bitem, "Name"];
		if(%bname == "")
			%bname = %bitem;
		%bhead = KronosShop_BeltHeading($BeltItem[%bitem, "Type"]);
		remoteEval(%clientId, "KShopStock", %sent, "b", %bitem, Belt::GetBuyCost(%clientId, %bitem), %bhead, %bname);
		%sent++;
	}

	remoteEval(%clientId, "KShopStockCount", %sent);
}

// ============================================
// Belt item actions - replicate the exact flows of the stock belt
// menus (Belt.cs processMenuBuyBeltItem / processMenuSellBeltItemFinal
// / processMenuBeltDrop), validated against shop state
// ============================================

// Authoritative action timegate (0.25s). A gated-out click does
// NOTHING (no deduction), so spam can never lose items. The client
// paces its buttons at 0.3s, so normally-paced clicks always pass.
function KronosShop_ActGate(%clientId)
{
	%now = getSimTime();
	if(%clientId.kshopActTime != "" && (%now - %clientId.kshopActTime) < 0.25)
		return false;
	%clientId.kshopActTime = %now;
	return true;
}

function KronosShop_BeltInShop(%clientId, %item)
{
	for(%i = 0; %i < %clientId.kshopBeltCnt; %i++)
		if(%clientId.kshopBeltItem[%i] == %item)
			return true;
	return false;
}

function remoteKShopBeltBuy(%clientId, %item)
{
	if(!%clientId.hasKronosHUD)
		return;
	if(!KronosShop_ActGate(%clientId))
		return;
	if(%clientId.kshopOpen != "shop")
		return;
	if(!KronosShop_BeltInShop(%clientId, %item))
		return;

	%cost = Belt::GetBuyCost(%clientId, %item);
	if(fetchData(%clientId, "COINS") < %cost)
	{
		Client::sendMessage(%clientId, $MsgRed, "You cannot afford this purchase.~wC_BuySell.wav");
		return;
	}
	storeData(%clientId, "COINS", %cost, "dec");
	Belt::GiveThisStuff(%clientId, %item, 1);
	%name = $BeltItem[%item, "Name"];
	if(%name == "")
		%name = %item;
	Client::sendMessage(%clientId, $MsgWhite, "You purchased 1 " @ %name @ ".~wbuysellsound.wav");
	UseSkill(%clientId, $SkillHaggling, True, True);
	RefreshAll(%clientId);
}

function remoteKShopBeltSell(%clientId, %item)
{
	if(!%clientId.hasKronosHUD)
		return;
	if(!KronosShop_ActGate(%clientId))
		return;
	if(%clientId.kshopOpen != "shop")
		return;
	if(IsDead(%clientId))
		return;
	if(Belt::HasThisStuff(%clientId, %item) < 1)
		return;

	// auto-unequip like the stock sell flow
	%category = $BeltItem[%item, "Type"];
	if(%category == "Accessories")
	{
		if(Belt::IsAccessoryEquipped(%clientId, %item))
			Belt::UnequipAccessory(%clientId, %item);
	}
	else if(%category == "Armor" && fetchData(%clientId, "EquippedBeltArmor") == %item)
		Belt::UnequipArmor(%clientId, %item);

	%cost = Belt::GetSellCost(%clientId, %item);
	%name = $BeltItem[%item, "Name"];
	if(%name == "")
		%name = %item;
	Client::sendMessage(%clientId, $MsgWhite, "You sold 1 " @ %name @ " for " @ Number::Beautify(%cost, -3) @ " coins.");
	UseSkill(%clientId, $SkillHaggling, true, true);
	storeData(%clientId, "COINS", %cost, "inc");
	Belt::TakeThisStuff(%clientId, %item, 1);
	RefreshAll(%clientId);
	SaveCharacter(%clientId);
}

function remoteKShopBeltUse(%clientId, %item)
{
	if(!%clientId.hasKronosHUD)
		return;
	if(!KronosShop_ActGate(%clientId))
		return;
	if(%clientId.kshopOpen == "")
		return;
	if(Belt::HasThisStuff(%clientId, %item) < 1)
		return;
	Belt::UseItem(%clientId, %item, $BeltItem[%item, "Type"]);
}

function remoteKShopBeltDrop(%clientId, %item)
{
	if(!%clientId.hasKronosHUD)
		return;
	if(!KronosShop_ActGate(%clientId))
		return;
	if(%clientId.kshopOpen == "")
		return;
	if(Belt::HasThisStuff(%clientId, %item) < 1)
		return;

	%type = $BeltItem[%item, "Type"];

	// Rapid drops of the same item MERGE into the bag dropped moments
	// ago instead of spawning a new one. Spammed single-item bags
	// spawn interpenetrating at the same spot and some clip through
	// the floor and vanish; merging also avoids a SaveCharacter /
	// world-save storm. The bag's loot string is rebuilt outright
	// ("owner namelist item count") with a count we track ourselves -
	// player names may contain spaces, so it can't be word-parsed.
	%bag = %clientId.kdropBag;
	%nearBag = false;
	if(isObject(%bag))
	{
		%player = Client::getOwnedObject(%clientId);
		if(%player != "" && %player != -1 && isObject(%player))
			%nearBag = (Vector::getDistance(GameBase::getPosition(%bag), GameBase::getPosition(%player)) < 12);
	}
	if(%clientId.kdropItem == %item
		&& (getSimTime() - %clientId.kdropTime) < 5
		&& %nearBag
		&& $loot[%bag] != "" && $loot[%bag] != -1)
	{
		// same auto-unequip semantics as Belt::DropItem
		if(%type == "Accessories" && Belt::IsAccessoryEquipped(%clientId, %item))
			Belt::UnequipAccessory(%clientId, %item);
		else if(%type == "Armor" && fetchData(%clientId, "EquippedBeltArmor") == %item)
			Belt::UnequipArmor(%clientId, %item);

		Belt::TakeThisStuff(%clientId, %item, 1);
		%clientId.kdropCnt++;
		$loot[%bag] = Client::getName(%clientId) @ " * " @ %item @ " " @ %clientId.kdropCnt;
		SaveCharacter(%clientId);
		%clientId.kdropTime = getSimTime();
		return;
	}

	Belt::DropItem(%clientId, %item, 1, %type);
	// TossLootbag recorded the new bag in %clientId.lastLootbag
	%clientId.kdropBag = %clientId.lastLootbag;
	%clientId.kdropItem = %item;
	%clientId.kdropCnt = 1;
	%clientId.kdropTime = getSimTime();
}

function KronosShop_Close(%clientId, %fromCancelMenu)
{
	if(%clientId.kshopOpen == "")
		return;
	%clientId.kshopOpen = "";

	// same cleanup the stock flow does when leaving the shop gui
	Client::clearItemShopping(%clientId);
	Client::clearItemBuying(%clientId);
	ClearCurrentShopVars(%clientId);

	remoteEval(%clientId, "KShopClose");
	if(!%fromCancelMenu)
		Client::setMenuScoreVis(%clientId, false);
}

// Client requests a refresh after buy/sell/use/drop (counts changed)
function remoteKShopSync(%clientId)
{
	if(!%clientId.hasKronosHUD)
		return;
	if(%clientId.kshopOpen == "")
		return;
	if(%clientId.kshopOpen == "bank")
	{
		KronosBank_PushInv(%clientId);
		KronosBank_PushStorage(%clientId);
		KronosBank_PushCoins(%clientId);
		return;
	}
	KronosShop_PushInv(%clientId);
	if(%clientId.kshopOpen == "shop")
		KronosShop_PushStock(%clientId);
}

// ============================================
// Item tooltip (hover in the client shop/bank/inventory panes)
// ============================================

// Thousands separators for display ("1234567" -> "1,234,567"). Also defined
// in the Kronos rpgfunk.cs; duplicated here so this file works standalone.
function Commafy(%n)
{
	%n = floor(%n);
	if(%n < 1000)
		return %n;
	%out = "";
	while(%n >= 1000)
	{
		%r = %n - (floor(%n / 1000) * 1000);
		%n = floor(%n / 1000);
		if(%r < 10)
			%r = "00" @ %r;
		else if(%r < 100)
			%r = "0" @ %r;
		%out = "," @ %r @ %out;
	}
	return %n @ %out;
}
// The client asks for ONE item's examine text after hovering a row; we answer
// with the same WhatIs text the two-click buy flow / #examine shows. %kind and
// %ref use the row scheme of the pushes: "d" = ItemData index, "b" = belt item
// name. Read-only (no economy state is touched), vanilla-safe (HUD gate).
function remoteKShopTip(%clientId, %kind, %ref)
{
	if(!%clientId.hasKronosHUD)
		return;
	if(%clientId.kshopOpen == "")
		return;

	if(%kind == "b")
	{
		// belt items: Belt::WhatIs builds this text but SENDS it as the 10s
		// examine overlay - the tooltip needs the string, so mirror its body
		%item = %ref;
		%desc = %item.description;
		if(%desc == "" || %desc == "0" || %desc == -1)
			%desc = $BeltItem[%item, "Name"];
		if(%desc == "")
			%desc = %item;
		%w = GetAccessoryVar(%item, $Weight);
		%c = GetItemCost(%item);
		%t = Belt::Display($BeltItem[%item, "Type"]);
		if($AccessoryVar[%item, $MiscInfo] != "")
			%nfo = $AccessoryVar[%item, $MiscInfo];
		else
			%nfo = "There is no further information available.";
		%msg = "<f1>" @ %desc;
		if(%w != "")
			%msg = %msg @ "\nWeight: " @ %w;
		if(%t != "")
			%msg = %msg @ "\nType: " @ %t;
		if(%c != "")
			%msg = %msg @ "\nPrice: $" @ Commafy(%c);
		%msg = %msg @ "\n<f0>" @ %nfo;
	}
	else
	{
		%item = getItemData(%ref);
		if(%item == "" || %item == -1)
			return;
		%msg = WhatIs(%item);
	}

	if(%msg == "")
		return;
	// The engine caps ONE remoteEval string arg at 255 bytes on the wire, so
	// long examine texts arrived truncated. Chunk it; the client reassembles
	// (KShopTipBegin/Part/Done in KronosShop.cs).
	remoteEval(%clientId, "KShopTipBegin");
	%len = String::len(%msg);
	for(%p = 0; %p < %len; %p = %p + 150)
		remoteEval(%clientId, "KShopTipPart", String::getSubStr(%msg, %p, 150));
	remoteEval(%clientId, "KShopTipDone");
}

// ============================================
// Bank STORAGE screen (HUD clients) - the custom inventory UI for bankers.
// Reuses the shop's two-pane window: LEFT = the player's carried ItemData
// equipment (depositable), RIGHT = items already in bank storage
// (withdrawable). Mirrors the economy.cs sellItem/buyItem BankStorage logic
// directly (so we DON'T call SetupBank, which would pop the stock GuiMode-4
// gui over our ScriptGL UI). Belt/backpack bank storage is a separate
// system - not in this version.
// ============================================

function KronosBank_Open(%clientId, %bankerId)
{
	if(!%clientId.hasKronosHUD)
		return;

	%clientId.currentBank = %bankerId;
	%clientId.bulkNum = "";
	%clientId.kshopOpen = "bank";

	remoteEval(%clientId, "KShopOpen", "bank", "Bank Storage");
	KronosBank_PushInv(%clientId);
	KronosBank_PushStorage(%clientId);
	KronosBank_PushCoins(%clientId);

	schedule("if(" @ %clientId @ ".kshopOpen != \"\") Client::setMenuScoreVis(" @ %clientId @ ", true);", 0.15);
}

// LEFT pane: the player's carried ItemData equipment AND belt/backpack items -
// combined into one list, exactly like KronosShop_PushInv (the "I" inventory), so
// the banker shows everything you carry. (Belt rows use kind "b"; the client routes
// their deposit through the belt-aware path.)
function KronosBank_PushInv(%clientId)
{
	%sent = 0;
	%max = getNumItems();
	for(%z = 0; %z < %max && %sent < $KronosShop::MaxRows; %z++)
	{
		%item = getItemData(%z);
		if(!%item.showInventory)
			continue;
		%cnt = Player::getItemCount(%clientId, %item);
		if(%cnt < 1)
			continue;
		remoteEval(%clientId, "KShopInv", %sent, "d", %z, %cnt, %item.heading, %item.description);
		%sent++;
	}

	// belt/backpack items: per-category player lists of "item count" pairs
	for(%c = 1; $Belt::Categories[%c] != ""; %c++)
	{
		%cat = $Belt::Categories[%c];
		%bhead = KronosShop_BeltHeading(%cat);
		%list = fetchData(%clientId, %cat);
		%more = true;
		for(%w = 0; %more && %sent < $KronosShop::MaxRows; %w += 2)
		{
			%bitem = GetWord(%list, %w);
			if(%bitem == -1 || %bitem == "" || %bitem == "0")
				%more = false;
			else
			{
				%bcnt = GetWord(%list, %w + 1);
				if(%bcnt >= 1)
				{
					%bname = $BeltItem[%bitem, "Name"];
					if(%bname == "")
						%bname = %bitem;
					remoteEval(%clientId, "KShopInv", %sent, "b", %bitem, %bcnt, %bhead, %bname);
					%sent++;
				}
			}
		}
	}

	remoteEval(%clientId, "KShopInvCount", %sent);
}

// RIGHT pane: items in bank storage. The "price" field carries the stored
// COUNT (the client renders it as a count in bank mode). Shows banked ItemData
// equipment (BankStorage) AND banked belt/backpack items (BeltStorage), so belt
// items deposited via the HUD can be withdrawn here.
function KronosBank_PushStorage(%clientId)
{
	%sent = 0;
	%max = getNumItems();
	for(%z = 0; %z < %max && %sent < $KronosShop::MaxRows; %z++)
	{
		%item = getItemData(%z);
		%cnt = GetStuffStringCount(fetchData(%clientId, "BankStorage"), %item);
		if(%cnt < 1)
			continue;
		remoteEval(%clientId, "KShopStock", %sent, "d", %z, %cnt, %item.heading, %item.description);
		%sent++;
	}

	// banked belt/backpack items live in BeltStorage as "registeredItem count" pairs
	%belt = fetchData(%clientId, "BeltStorage");
	%more = true;
	for(%w = 0; %more && %sent < $KronosShop::MaxRows; %w += 2)
	{
		%bitem = GetWord(%belt, %w);
		if(%bitem == -1 || %bitem == "" || %bitem == "0")
			%more = false;
		else
		{
			%bcnt = GetWord(%belt, %w + 1);
			if(%bcnt >= 1)
			{
				%bname = $BeltItem[%bitem, "Name"];
				if(%bname == "")
					%bname = %bitem;
				%bhead = KronosShop_BeltHeading($BeltItem[%bitem, "Type"]);
				remoteEval(%clientId, "KShopStock", %sent, "b", %bitem, %bcnt, %bhead, %bname);
				%sent++;
			}
		}
	}

	remoteEval(%clientId, "KShopStockCount", %sent);
}

// ----- belt/backpack item deposit & withdraw (HUD bank) ----------------------
// Isolated from the stock belt chat-menu (processMenuSellBeltItemFinal) - mirrors
// only its data move (no menu UI): BeltStorage is the single source of truth and
// Stored<type> is the persisted mirror, so we write BOTH and keep them in sync,
// exactly like the stock belt-banker store/withdraw. Deployables are refused (they
// have no persisted StoredDeployables field and would be lost on save).

// Rebuild a "name count name count" stuff-string keeping only valid positive pairs.
function KronosBelt_Clean(%list)
{
	if(%list == "0" || %list == " ")
		return "";
	%out = "";
	for(%i = 0; GetWord(%list, %i) != -1; %i += 2)
	{
		%nm = GetWord(%list, %i);
		%ct = GetWord(%list, %i + 1);
		%ctn = %ct * 1;
		if(%nm != "" && %nm != -1 && %nm != "0" && %ct != "" && %ct != -1 && %ct != "-1" && %ct != "0" && %ctn > 0)
			%out = %out @ %nm @ " " @ %ct @ " ";
	}
	%len = String::len(%out);
	if(%len > 0 && String::getSubStr(%out, %len - 1, 1) == " ")
		%out = String::getSubStr(%out, 0, %len - 1);
	return %out;
}

// Count unique items (pairs) in a cleaned stuff-string.
function KronosBelt_UniqueCount(%list)
{
	%n = 0;
	for(%i = 0; GetWord(%list, %i) != -1; %i += 2)
	{
		%nm = GetWord(%list, %i);
		if(%nm != "" && %nm != -1 && %nm != "0")
			%n++;
	}
	return %n;
}

// Deposit %amt of the belt item %item (the active-belt key from the inv pane) into
// backpack bank storage. Mirrors the stock belt-banker "store" branch.
function remoteKBankBeltDeposit(%clientId, %item, %amt)
{
	if(!%clientId.hasKronosHUD)
		return;
	if(%clientId.currentBank == "")
		return;
	if(!KronosShop_ActGate(%clientId))
		return;

	%registeredItem = $BeltItem[%item, "Item"];
	if(%registeredItem == "")
		return;
	%type = $BeltItem[%item, "Type"];
	if(%type == "" || %type == -1)
		return;
	if(%type == "Deployables")   // no persisted StoredDeployables field -> never bank
	{
		Client::sendMessage(%clientId, $MsgRed, "Deployables can't be stored in the bank.~wC_BuySell.wav");
		return;
	}

	%have = Belt::HasThisStuff(%clientId, %item);
	if(%have < 1)
		return;
	%n = %amt;
	if(%n == "" || %n < 1)
		%n = 1;
	if(%n > %have)
		%n = %have;

	%belt = KronosBelt_Clean(fetchData(%clientId, "BeltStorage"));
	// 25 unique-item cap (matches the stock belt banker)
	if(Belt::ItemCount(%registeredItem, %belt) < 1 && KronosBelt_UniqueCount(%belt) >= 25)
	{
		Client::sendMessage(%clientId, $MsgRed, "You can only place 25 different items into backpack storage.~wC_BuySell.wav");
		return;
	}

	// auto-unequip equipped accessories/armor being stored (mirror stock)
	if(%type == "Accessories")
	{
		for(%u = 0; %u < %n; %u++)
			if(Belt::IsAccessoryEquipped(%clientId, %item))
				Belt::UnequipAccessory(%clientId, %item);
	}
	else if(%type == "Armor" && fetchData(%clientId, "EquippedBeltArmor") == %item)
		Belt::UnequipArmor(%clientId, %item);

	// write BeltStorage (source of truth) + Stored<type> (persisted), kept in sync
	storeData(%clientId, "BeltStorage", KronosBelt_Clean(SetStuffString(%belt, %registeredItem, %n)));
	%sf = "Stored" @ %type;
	storeData(%clientId, %sf, KronosBelt_Clean(SetStuffString(fetchData(%clientId, %sf), %registeredItem, %n)));

	Belt::TakeThisStuff(%clientId, %item, %n);   // remove from the active belt

	%nm = $BeltItem[%registeredItem, "Name"];
	if(%nm == "")
		%nm = %registeredItem;
	Client::sendMessage(%clientId, $MsgGreen, "Deposited " @ %n @ " " @ %nm @ " into backpack storage.");

	RefreshAll(%clientId);
	SaveCharacter(%clientId);
	KronosBank_PushInv(%clientId);
	KronosBank_PushStorage(%clientId);
}

// Withdraw %amt of the banked belt item %item (the registered key from the storage
// pane) back into the active belt. Mirrors the stock belt-banker "withdraw" branch.
function remoteKBankBeltWithdraw(%clientId, %item, %amt)
{
	if(!%clientId.hasKronosHUD)
		return;
	if(%clientId.currentBank == "")
		return;
	if(!KronosShop_ActGate(%clientId))
		return;

	%registeredItem = $BeltItem[%item, "Item"];
	if(%registeredItem == "")
		%registeredItem = %item;
	%type = $BeltItem[%registeredItem, "Type"];
	if(%type == "" || %type == -1)
		%type = $BeltItem[%item, "Type"];

	%belt = KronosBelt_Clean(fetchData(%clientId, "BeltStorage"));
	%have = Belt::ItemCount(%registeredItem, %belt);
	if(%have < 1)
		return;
	%n = %amt;
	if(%n == "" || %n < 1)
		%n = 1;
	if(%n > %have)
		%n = %have;

	%belt = KronosBelt_Clean(SetStuffString(%belt, %registeredItem, -%n));
	storeData(%clientId, "BeltStorage", %belt);
	if(%type != "" && %type != -1)
	{
		%sf = "Stored" @ %type;
		storeData(%clientId, %sf, KronosBelt_Clean(SetStuffString(fetchData(%clientId, %sf), %registeredItem, -%n)));
	}
	// if BeltStorage emptied, clear ALL stored categories so SaveCharacter can't
	// rebuild it from stale data (mirror the stock withdraw safety)
	if(%belt == "")
	{
		storeData(%clientId, "StoredQuestItems", "");
		storeData(%clientId, "StoredKeyItems", "");
		storeData(%clientId, "StoredConsumables", "");
		storeData(%clientId, "StoredArmor", "");
		storeData(%clientId, "StoredAccessories", "");
		storeData(%clientId, "StoredOther", "");
	}

	Belt::GiveThisStuff(%clientId, %item, %n, 1);   // back into the active belt

	%nm = $BeltItem[%registeredItem, "Name"];
	if(%nm == "")
		%nm = %registeredItem;
	Client::sendMessage(%clientId, $MsgGreen, "Withdrew " @ %n @ " " @ %nm @ " from backpack storage.");

	RefreshAll(%clientId);
	SaveCharacter(%clientId);
	KronosBank_PushInv(%clientId);
	KronosBank_PushStorage(%clientId);
}

// Deposit %amt of %type from the player into bank storage (mirrors the
// sellItem currentBank branch, minus the SetupBank gui refresh).
// %amt is OPTIONAL: HUD clients with the amount UI send a count; older clients
// (and any future caller) omit it, in which case it defaults to 1 - so this is
// backward compatible. Non-HUD players never reach here (gated below).
function remoteKBankDeposit(%clientId, %type, %amt)
{
	if(!%clientId.hasKronosHUD)
		return;
	if(%clientId.currentBank == "")
		return;
	if(!KronosShop_ActGate(%clientId))
		return;

	%item = getItemData(%type);
	if(CountObjInList(fetchData(%clientId, "BankStorage")) / 2 >= 50)
	{
		Client::sendMessage(%clientId, $MsgRed, "You can only store 50 different types of items.~wC_BuySell.wav");
		return;
	}
	%cnt = Player::getItemCount(%clientId, %item);
	if(%cnt < 1)
		return;
	if(%item.className == Equipped)
	{
		Client::sendMessage(%clientId, $MsgRed, "Unequip this item before storing it.~wC_BuySell.wav");
		return;
	}
	// amount: default 1 when omitted/invalid; never more than the player carries
	%n = %amt;
	if(%n == "" || %n < 1)
		%n = 1;
	if(%n > %cnt)
		%n = %cnt;
	Player::decItemCount(%clientId, %item, %n);
	storeData(%clientId, "BankStorage", SetStuffString(fetchData(%clientId, "BankStorage"), %item, %n));
	RefreshAll(%clientId);
	KronosBank_PushInv(%clientId);
	KronosBank_PushStorage(%clientId);
}

// Withdraw %amt of %type from bank storage to the player (mirrors buyItem).
// %amt is OPTIONAL (defaults to 1 when omitted) - backward compatible.
function remoteKBankWithdraw(%clientId, %type, %amt)
{
	if(!%clientId.hasKronosHUD)
		return;
	if(%clientId.currentBank == "")
		return;
	if(!KronosShop_ActGate(%clientId))
		return;

	%item = getItemData(%type);
	%cnt = GetStuffStringCount(fetchData(%clientId, "BankStorage"), %item);
	if(%cnt < 1)
		return;
	// amount: default 1 when omitted/invalid; never more than is stored
	%n = %amt;
	if(%n == "" || %n < 1)
		%n = 1;
	if(%n > %cnt)
		%n = %cnt;
	Player::incItemCount(%clientId, %item, %n);
	storeData(%clientId, "BankStorage", SetStuffString(fetchData(%clientId, "BankStorage"), %item, -%n));
	RefreshAll(%clientId);
	KronosBank_PushInv(%clientId);
	KronosBank_PushStorage(%clientId);
}

// Coin balances (carried + banked) for the pane headers.
function KronosBank_PushCoins(%clientId)
{
	remoteEval(%clientId, "KBankCoins", fetchData(%clientId, "COINS"), fetchData(%clientId, "BANK"));
}

// Deposit coins into the bank. %amt is OPTIONAL: HUD clients with the amount UI
// send a count; when omitted/invalid (the old "Dep All $" button) it defaults to
// ALL carried coins - so this stays backward compatible. Clamped to what's carried.
function remoteKBankCoinsDeposit(%clientId, %amt)
{
	if(!%clientId.hasKronosHUD)
		return;
	if(%clientId.currentBank == "")
		return;
	if(!KronosShop_ActGate(%clientId))
		return;
	%coins = fetchData(%clientId, "COINS");
	if(%coins <= 0)
		return;
	%n = %amt;
	if(%n == "" || %n <= 0 || %n > %coins)
		%n = %coins;
	storeData(%clientId, "BANK", %n, "inc");
	storeData(%clientId, "COINS", -%n, "inc");
	Client::sendMessage(%clientId, $MsgWhite, "Deposited " @ Number::Beautify(%n, -3) @ " coins.~wbuysellsound.wav");
	RefreshAll(%clientId);
	KronosBank_PushCoins(%clientId);
}

// Withdraw coins from the bank. %amt is OPTIONAL: defaults to ALL banked coins
// when omitted/invalid (the old "W/D All $" button) - backward compatible.
function remoteKBankCoinsWithdraw(%clientId, %amt)
{
	if(!%clientId.hasKronosHUD)
		return;
	if(%clientId.currentBank == "")
		return;
	if(!KronosShop_ActGate(%clientId))
		return;
	%bank = fetchData(%clientId, "BANK");
	if(%bank <= 0)
		return;
	%n = %amt;
	if(%n == "" || %n <= 0 || %n > %bank)
		%n = %bank;
	storeData(%clientId, "COINS", %n, "inc");
	storeData(%clientId, "BANK", -%n, "inc");
	Client::sendMessage(%clientId, $MsgWhite, "Withdrew " @ Number::Beautify(%n, -3) @ " coins.~wbuysellsound.wav");
	RefreshAll(%clientId);
	KronosBank_PushCoins(%clientId);
}

// Client closed the panel (Close button / I key toggle client request)
function remoteKShopClose(%clientId)
{
	if(!%clientId.hasKronosHUD)
		return;
	KronosShop_Close(%clientId);
}

// ============================================
// Main stat push - sends all RPG stats to client
// ============================================

function KronosHUD_Push(%clientId)
{
	// Watchdog: revive the LOS scan loop if it isn't running (mission
	// load flushes schedules, killing any loop started before/during it)
	if(getSimTime() - $KronosHUD::LOSLastTick > 5)
		KronosHUD_StartLOSScan();

	// Skip bots entirely
	if(Player::isAiControlled(%clientId))
		return;
	if(isRPGAI(%clientId))
		return;

	// Skip disconnected clients
	%clientName = Client::getName(%clientId);
	if(%clientName == "" || %clientName == -1)
		return;

	// Skip if player hasn't loaded and spawned yet
	if(!fetchData(%clientId, "HasLoadedAndSpawned"))
		return;

	// Gather vitals
	%hp = fetchData(%clientId, "HP");
	%maxHp = fetchData(%clientId, "MaxHP");
	%mana = fetchData(%clientId, "MANA");
	%maxMana = fetchData(%clientId, "MaxMANA");
	%exp = fetchData(%clientId, "EXP");
	%lvl = fetchData(%clientId, "LVL");
	%xpCur = GetExp(%lvl, %clientId);
	%nextLvl = %lvl + 1;
	%xpNext = GetExp(%nextLvl, %clientId);
	%gold = fetchData(%clientId, "COINS");
	%remort = fetchData(%clientId, "RemortStep");
	if(%remort == "" || %remort == -1)
		%remort = 0;

	// Push vitals (all numeric - safe for remoteEval)
	remoteEval(%clientId, "KronosHUD", %hp, %maxHp, %mana, %maxMana, %exp, %xpCur, %xpNext, %gold, %lvl, %remort);

	// Push metadata (class and zone may contain spaces - sent separately).
	// Gated on the handshake; the vitals push above stays UNGATED because
	// it's the discovery channel that triggers the client handshake.
	if(%clientId.hasKronosHUD)
	{
		%class = getFinalCLASS(%clientId);
		if(%class == "" || %class == -1)
			%class = "Unknown";
		%zone = Zone::getDesc(fetchData(%clientId, "zone"));
		if(%zone == "" || %zone == -1)
			%zone = "Unknown";

		remoteEval(%clientId, "KronosHUD2", %class, %zone);
	}
}

// ============================================
// Target frame push - sends enemy info to attacker
// ============================================

// %damage (optional): damage just dealt - shown as a hit number on the
// client's target frame. Empty for LOS-scan pushes, "LCK" for LCK hits.
function KronosHUD_PushTarget(%shooterClient, %damagedClient, %damage)
{
	// Skip if shooter is a bot
	if(Player::isAiControlled(%shooterClient))
		return;
	if(isRPGAI(%shooterClient))
		return;

	// Skip disconnected clients
	if(Client::getName(%shooterClient) == "" || Client::getName(%shooterClient) == -1)
		return;

	// HUD clients only (handshake) - vanilla clients have no handler
	if(!%shooterClient.hasKronosHUD)
		return;

	// Get target name (GetClientOrBotName resolves bot names properly)
	%targetName = GetClientOrBotName(%damagedClient);
	if(%targetName == "" || %targetName == -1)
		%targetName = "Unknown";

	// Calculate target HP percentage
	%hp = fetchData(%damagedClient, "HP");
	%maxHp = fetchData(%damagedClient, "MaxHP");
	%targetHpPct = 0;
	if(%maxHp > 0)
		%targetHpPct = floor((%hp * 100) / %maxHp);
	if(%targetHpPct < 0)
		%targetHpPct = 0;
	if(%targetHpPct > 100)
		%targetHpPct = 100;

	remoteEval(%shooterClient, "KronosTarget", %targetName, %targetHpPct, %damage);
}

// ============================================
// TAB menu info box (bottom score-screen window)
// ============================================
// Fills the stock InfoCtrlBox (6 setInfoLine rows) - an engine
// control every client has, so this works for vanilla clients too.
// Own stats show when the menu opens; clicking a player name on the
// scoreboard shows that player instead (via remoteSelectClient).

// Player's own stats - called from Game::menuRequest when no player
// is selected on the scoreboard
function KronosMenu_SendOwnInfo(%clientId)
{
	if(Player::isAiControlled(%clientId))
		return;
	if(Client::getName(%clientId) == "" || Client::getName(%clientId) == -1)
		return;

	%remort = fetchData(%clientId, "RemortStep");
	if(%remort == "" || %remort == -1)
		%remort = 0;

	%exp = fetchData(%clientId, "EXP");
	%expNeed = GetExp(GetLevel(%exp, %clientId) + 1, %clientId) - %exp;

	%coins = fetchData(%clientId, "COINS");
	%bank = fetchData(%clientId, "BANK");

	// FixDecimals builds the "N.d" string by hand - round(x*10)/10 stringifies
	// with binary-float noise (weight showed 12 decimals on the TAB menu)
	%weight = FixDecimals(fetchData(%clientId, "Weight"));
	%maxWeight = FixDecimals(fetchData(%clientId, "MaxWeight"));

	remoteEval(%clientId, "setInfoLine", 1, Client::getName(%clientId) @ " - Lv " @ fetchData(%clientId, "LVL") @ " " @ getFinalCLASS(%clientId) @ " RL" @ %remort);
	remoteEval(%clientId, "setInfoLine", 2, "ATK " @ fetchData(%clientId, "ATK") @ "   DEF " @ fetchData(%clientId, "DEF") @ "   MDEF " @ fetchData(%clientId, "MDEF") @ "   LCK " @ fetchData(%clientId, "LCK"));
	remoteEval(%clientId, "setInfoLine", 3, "HP " @ fetchData(%clientId, "HP") @ "/" @ fetchData(%clientId, "MaxHP") @ "   MP " @ fetchData(%clientId, "MANA") @ "/" @ fetchData(%clientId, "MaxMANA"));
	remoteEval(%clientId, "setInfoLine", 4, "EXP " @ Number::Beautify(%exp, -3) @ "   (Need " @ Number::Beautify(%expNeed, -3) @ ")");
	remoteEval(%clientId, "setInfoLine", 5, "Coins " @ Number::Beautify(%coins, -3) @ "   Bank " @ Number::Beautify(%bank, -3) @ "   Total " @ Number::Beautify(%coins + %bank, -3));
	remoteEval(%clientId, "setInfoLine", 6, "Weight " @ %weight @ " / " @ %maxWeight);
}

// Player list for the modern TAB menu - REQUEST-DRIVEN: only clients
// running KronosMenu.cs ask for it (remoteEval(2048, KMGetPlayers) on
// every menu open), so vanilla clients never receive these pushes
// (their stock engine scoreboard still works untouched).
$KronosMenu::MaxListRows = 16; // must match $KM::MaxPRows client-side

function remoteKMGetPlayers(%clientId)
{
	if(Player::isAiControlled(%clientId))
		return;
	if(isRPGAI(%clientId))
		return;
	if(Client::getName(%clientId) == "" || Client::getName(%clientId) == -1)
		return;

	%sent = 0;
	%total = 0;
	for(%cl = Client::getFirst(); %cl != -1; %cl = Client::getNext(%cl))
	{
		if(Player::isAiControlled(%cl))
			continue;
		if(isRPGAI(%cl))
			continue;
		if(Client::getName(%cl) == "" || Client::getName(%cl) == -1)
			continue;

		%total++;
		if(%sent >= $KronosMenu::MaxListRows)
			continue;

		%remort = fetchData(%cl, "RemortStep");
		if(%remort == "" || %remort == -1)
			%remort = 0;

		// location (zone) for the list's location column
		%zone = Zone::getDesc(fetchData(%cl, "zone"));
		if(%zone == "" || %zone == -1)
			%zone = "Unknown";

		remoteEval(%clientId, "KMPlayer", %sent, %cl, fetchData(%cl, "LVL"), %remort, getFinalCLASS(%cl), Client::getName(%cl), %zone);
		%sent++;
	}
	remoteEval(%clientId, "KMPlayerCount", %sent, %total);
}

// Another player's public info - called from remoteSelectClient when
// a player name is clicked on the scoreboard
function KronosMenu_SendPlayerInfo(%clientId, %selId)
{
	if(Client::getName(%clientId) == "" || Client::getName(%clientId) == -1)
		return;

	%remort = fetchData(%selId, "RemortStep");
	if(%remort == "" || %remort == -1)
		%remort = 0;

	%house = fetchData(%selId, "MyHouse");
	if(%house == "" || %house == "0" || %house == "House0" || %house == "house0")
		%house = "None";

	%zone = Zone::getDesc(fetchData(%selId, "zone"));
	if(%zone == "" || %zone == -1)
		%zone = "Unknown";

	remoteEval(%clientId, "setInfoLine", 1, "Player: " @ Client::getName(%selId));
	remoteEval(%clientId, "setInfoLine", 2, "Lv " @ fetchData(%selId, "LVL") @ " " @ getFinalCLASS(%selId) @ " RL" @ %remort);
	remoteEval(%clientId, "setInfoLine", 3, "House: " @ %house);
	remoteEval(%clientId, "setInfoLine", 4, "Zone: " @ %zone);
	remoteEval(%clientId, "setInfoLine", 5, "HP " @ fetchData(%selId, "HP") @ "/" @ fetchData(%selId, "MaxHP") @ "   MP " @ fetchData(%selId, "MANA") @ "/" @ fetchData(%selId, "MaxMANA"));
	remoteEval(%clientId, "setInfoLine", 6, "");
}

// ============================================
// LOS target scan - target frame appears when
// a player looks at another player or enemy bot
// ============================================
// Periodic raycast down each player's view. Throttled via
// $KronosHUD::LOSScanPeriod. Town bots get a nameplate-only push
// ("NPC" in the HP slot - immune to damage, so no HP bar).

$KronosHUD::LOSScanPeriod = 0.5; // seconds between scans
$KronosHUD::LOSRange = 120;      // meters
$KronosHUD::LOSDebug = false;    // set true at server console to trace the scan

function KronosHUD_LOSScan(%gen)
{
	// Generation guard: re-exec'ing this file bumps the generation,
	// which kills any previously scheduled scan loop
	if(%gen != $KronosHUD::LOSGen)
		return;
	schedule("KronosHUD_LOSScan(" @ %gen @ ");", $KronosHUD::LOSScanPeriod);
	$KronosHUD::LOSLastTick = getSimTime();

	for(%cl = Client::getFirst(); %cl != -1; %cl = Client::getNext(%cl))
	{
		// HUD clients only (handshake) - also skips the raycast work
		if(!%cl.hasKronosHUD)
			continue;

		// Real, spawned, living players only
		if(Player::isAiControlled(%cl))
			continue;
		if(!fetchData(%cl, "HasLoadedAndSpawned"))
			continue;
		if(IsDead(%cl))
			continue;
		%pobj = Client::getOwnedObject(%cl);
		if(%pobj == "" || %pobj == -1 || %pobj == 0)
			continue;

		// Vitals freshness: the HP/MP NUMBERS are only pushed on stat-refresh
		// events (RefreshAll / refreshClientScore), so plain damage, heals and
		// mana drain moved the engine-driven bars but left the numeric readout
		// stale. Piggyback a change-gated push on this 0.5s loop - it only
		// sends when HP or MANA actually changed, so idle players cost nothing.
		%vhp = fetchData(%cl, "HP");
		%vmana = fetchData(%cl, "MANA");
		if(%vhp != %cl.khudLastHP || %vmana != %cl.khudLastMana)
		{
			%cl.khudLastHP = %vhp;
			%cl.khudLastMana = %vmana;
			KronosHUD_Push(%cl);
		}

		// Raycast down the player's view
		if(!GameBase::getLOSInfo(%pobj, $KronosHUD::LOSRange))
		{
			if($KronosHUD::LOSDebug)
				echo("[KronosHUD LOS] " @ %cl @ ": no LOS hit within range");
			continue;
		}
		if(getObjectType($los::object) != "Player")
		{
			if($KronosHUD::LOSDebug)
				echo("[KronosHUD LOS] " @ %cl @ ": hit " @ getObjectType($los::object) @ " (" @ $los::object @ ")");
			continue;
		}

		// GetClientIdFromPlayerObject works for bots too
		// (Player::getClient returns -1 for AI)
		%targetId = GetClientIdFromPlayerObject($los::object);
		if(%targetId == "" || %targetId == -1 || %targetId == %cl)
			continue;
		if(IsDead(%targetId))
			continue;
		if(isTownBot(%targetId))
		{
			// Damage-immune NPC: nameplate-only frame. "NPC" in the HP
			// slot tells the client to style it friendly and skip the
			// HP bar (a red enemy frame would be misleading).
			// Client::getName holds the bot's display name; the
			// GetClientOrBotName fast path would return the internal
			// "TownBot_xxx" BotInfoAiName instead.
			%npcName = Client::getName(%targetId);
			if(%npcName == "" || %npcName == -1 || %npcName == "0")
				%npcName = GetClientOrBotName(%targetId);
			if(%npcName != "" && %npcName != -1)
				remoteEval(%cl, "KronosTarget", %npcName, "NPC", "");
			if($KronosHUD::LOSDebug)
				echo("[KronosHUD LOS] " @ %cl @ ": town bot nameplate " @ %npcName);
			continue;
		}

		if($KronosHUD::LOSDebug)
			echo("[KronosHUD LOS] " @ %cl @ " -> target " @ %targetId @ " (" @ GetClientOrBotName(%targetId) @ ")");
		KronosHUD_PushTarget(%cl, %targetId);
	}
}

// Start (or restart) the scan loop. Generation counter kills any
// previously scheduled loop, so calling this twice is safe.
function KronosHUD_StartLOSScan()
{
	$KronosHUD::LOSGen++;
	$KronosHUD::LOSLastTick = getSimTime();
	echo("KronosHUD: LOS scan starting (gen " @ $KronosHUD::LOSGen @ ")");
	KronosHUD_LOSScan($KronosHUD::LOSGen);
}

// NOTE: Do NOT schedule the scan at exec time - Tribes flushes pending
// schedules when the mission loads, which silently kills the loop.
// Instead the loop is started/revived by the watchdog in KronosHUD_Push,
// which fires constantly once players are in the game.

echo("KronosHUD_Server: stat push system loaded");
