//==============================================
// KronosMenu.cs - modern ScriptGL TAB menu for Kingdom of Kronos
//==============================================
// ARCHITECTURE
//
// Transport: 100% stock. remoteNewMenu/remoteAddMenuItem build the
// engine ChatMenu "CurServerMenu" (number/letter hotkeys + the
// menuSelect round-trip unchanged), and additionally record the items
// so a modern panel can be drawn via ScriptGL onPostDraw.
//
// Stock visuals: every control in the score-screen gui files
// (base\gui\Score.gui and lr_score.gui) is moved off-screen by a
// one-time binary patch (positions 8000,8000). The score DIALOG still
// opens on TAB - invisible - which keeps the engine cursor on. The
// live files are plain copies of the patched templates
// (KM_score_base.gui / KM_lr_base.gui).
//
// Mouse clicks: Hudbot mouse callbacks ($MouseEnable). The engine GUI
// is NOT used for input. Earlier versions baked SimGui::ActiveCtrl
// click zones into the gui files; that broke TAB-to-close, because
// ActiveCtrl::wantsTabListMembership() is unconditionally true, the
// canvas consumes any key event whenever a control holds keyboard
// focus (simGuiCanvas.cpp processEvent), and Canvas::onKeyDown turns
// TAB into tabNext() focus cycling - so the action map binding that
// sends scoresOff never fired. With zero ActiveCtrls in the dialog
// the tab list is empty, focus stays on the canvas, and TAB falls
// through to the stock close path. Hudbot reports cursor position
// and button state while the GUI cursor is active (exactly when the
// score dialog is up); rows are hit-tested in script against the
// same $KML layout the renderer uses.
//
// Player list: REQUEST-DRIVEN server push. On every NewMenu the
// client sends remoteEval(2048, KMGetPlayers); the server answers
// with KMPlayer rows + KMPlayerCount (KronosHUD_Server.cs). Vanilla
// clients never ask, never get pushed, and keep their stock engine
// scoreboard. Clicking a row sends the SAME message the stock
// scoreboard sent - remoteEval(2048, SelectClient, id) - so all the
// server-side selClient machinery (player-specific menu options,
// KronosMenu_SendPlayerInfo) works unchanged.
//
// Character info: the server feeds the stock bottom info box via
// remoteEval(client, "setInfoLine", n, text). remoteSetInfoLine is a
// base-script function (client.cs), overridden here to capture the
// text for our info panel. Vanilla clients keep the stock InfoCtrlBox.
//
// All panels are TOP-ANCHORED with fixed row slots so a row's hit
// rectangle is always at the same screen position no matter the
// item count.
//
// KronosMenu::probe() - run from console while TAB is open - reports
// menu/mouse state. KronosMenu::disable() stops the panel (debug only).
//==============================================

if($KM::enabled == "")
	$KM::enabled = true;

// UI scale knob (persists - it's a $pref). 1.0 = GUI sized for a 1080p
// reference; lower = smaller share of the screen at high resolutions.
// Tune live with KronosMenu::setScale(0.85) etc.
if($pref::Kronos::UiScale == "")
	$pref::Kronos::UiScale = 1.0;
if($pref::Kronos::UiRefH == "")
	$pref::Kronos::UiRefH = 1080;

// Movable panel positions (persist - fractions of the screen). Drag a
// panel by its title bar to move it; KronosMenu::resetLayout() restores
// these. infoX = "c" means the character-info box stays centered until
// it's dragged.
if($pref::Kronos::menuX == "")    $pref::Kronos::menuX = 0.08;
if($pref::Kronos::menuY == "")    $pref::Kronos::menuY = 0.16;
if($pref::Kronos::playersX == "") $pref::Kronos::playersX = 0.54;
if($pref::Kronos::playersY == "") $pref::Kronos::playersY = 0.16;
if($pref::Kronos::infoX == "")    $pref::Kronos::infoX = "c";
if($pref::Kronos::infoY == "")    $pref::Kronos::infoY = 0.75;
if($pref::Kronos::sliderX == "")  $pref::Kronos::sliderX = "c";   // UI-scale slider pos ("c" = centered)
if($pref::Kronos::sliderY == "")  $pref::Kronos::sliderY = 0.015;

// Hudbot: enables onMouseActive/onMouseMove/onMouseLMB callbacks
// (see Hudbot\Docs\prefs.html). Reported while the GUI cursor is up.
$MouseEnable = true;

$KM::MaxZones = 15;   // menu rows
$KM::MaxPRows = 16;   // player-list rows (matches $KronosMenu::MaxListRows server-side)

// ============================================
// Shared layout - single source of truth, in real screen pixels.
// Used by BOTH the panel renderer and the click hit-testing.
// ============================================

// UI scale factor (height-based). Maps the original proportional design
// DOWN toward a reference height so the GUI takes a smaller share of the
// screen as resolution climbs (more game world visible at 1440p/4K),
// instead of always filling the same percentage. $pref::Kronos::UiScale
// is the user knob (1.0 = sized for the reference height; lower = smaller).
// Capped at 1.0 (the original proportional size) so it can never overflow
// a small window like 809x597 - on screens at or below the reference the
// factor naturally falls back to full proportional.
function KronosMenu::uiScale(%sh)
{
	%scale = $pref::Kronos::UiScale;
	if(%scale == "" || %scale <= 0)
		%scale = 1.0;
	%ref = $pref::Kronos::UiRefH;
	if(%ref == "" || %ref < 100)
		%ref = 1080;

	%base = %ref / %sh;     // <1 on screens taller than the reference
	if(%base > 1.0)
		%base = 1.0;        // never larger than the original proportional size
	%k = %base * %scale;
	if(%k > 1.0)
		%k = 1.0;
	return %k;
}

function KronosMenu::computeLayout(%sw, %sh)
{
	// SIZES scale toward the reference (szW/szH); POSITIONS come from the
	// movable, persisted per-panel fractions (drag to reposition).
	%k = KronosMenu::uiScale(%sh);
	%szW = %sw * %k;
	%szH = %sh * %k;
	$KML::k = %k;

	$KML::pad    = floor(%szW * 0.012);
	$KML::w      = floor(%szW * 0.38);
	$KML::wMenu  = $KML::w; // menu panel may widen in render to fit long items
	$KML::wPlayers = $KML::w; // player panel may widen in render too
	$KML::titleH = floor(%szH * 0.05);
	$KML::rowH   = floor(%szH * 0.034);
	$KML::lineH  = floor(%szH * 0.026);

	// movable panel positions (persisted as fractions of the screen)
	$KML::mx     = floor($pref::Kronos::menuX * %sw);
	$KML::menuY  = floor($pref::Kronos::menuY * %sh);
	$KML::px     = floor($pref::Kronos::playersX * %sw);
	$KML::plY    = floor($pref::Kronos::playersY * %sh);
	$KML::iy     = floor($pref::Kronos::infoY * %sh);

	$KML::menuRowY0 = $KML::menuY + $KML::titleH + floor($KML::pad / 2);
	$KML::plRowY0   = $KML::plY   + $KML::titleH + floor($KML::pad / 2);

	// aliases for the menu panel (probe / legacy refs)
	$KML::y      = $KML::menuY;
	$KML::rowY0  = $KML::menuRowY0;
	$KML::ix     = floor((%sw - $KML::w) / 2);
}

// ============================================
// Authoritative screen size for ScriptGL layout
// ============================================
// The %dimensions ScriptGL passes to the draw hooks can be stale or
// wrong (notably in windowed OpenGL), which makes every panel render at
// a fixed pixel size in a screen corner instead of scaling with the
// resolution. ScriptGL always draws into the engine's live canvas
// ortho, whose size is the PlayGui extent - so that extent is the
// correct basis for both layout AND drawing. Fall back to the passed
// dims only if the extent isn't sane yet (mirrors the >100 guard in
// Presto::ScreenSize). If %dimensions was already correct the extent
// matches it, so this never changes a correctly-scaled GUI.
function KronosMenu::screenDim(%fallback)
{
	%ext = Control::getExtent(PlayGui);
	if(getWord(%ext, 0) > 100 && getWord(%ext, 1) > 100)
	{
		$KM::dimSrc = "extent";
		$KM::dim = %ext;
		$KM::dimSGL = %fallback;
		return %ext;
	}
	$KM::dimSrc = "scriptgl";
	$KM::dim = %fallback;
	$KM::dimSGL = %fallback;
	return %fallback;
}

// ============================================
// Menu transport overrides (base: scripts.vol menu.cs)
// ============================================

function remoteNewMenu(%server, %title)
{
	if(%server != 2048)
		return;

	if(isObject(CurServerMenu))
		deleteObject(CurServerMenu);

	newObject(CurServerMenu, ChatMenu, %title);
	if(isObject(PlayChatMenu))
		setCMMode(PlayChatMenu, 0);
	setCMMode(CurServerMenu, 1);

	$KM::title = %title;
	$KM::count = 0;
	$KM::active = true;
	$KM::measureDirty = true;

	// ask the server for the player list (vanilla-safe: only clients
	// running this script ever send the request)
	remoteEval(2048, KMGetPlayers);
}

function remoteAddMenuItem(%server, %title, %code)
{
	if(%server != 2048)
		return;

	addCMCommand(CurServerMenu, %title, clientMenuSelect, %code);

	// First character of the label is the engine hotkey
	%idx = $KM::count;
	$KM::key[%idx] = String::getSubStr(%title, 0, 1);
	$KM::label[%idx] = String::getSubStr(%title, 1, 999);
	$KM::code[%idx] = %code;
	$KM::count++;
	$KM::measureDirty = true;
}

function remoteCancelMenu(%server)
{
	if(%server != 2048)
		return;

	if(isObject(CurServerMenu))
		deleteObject(CurServerMenu);
	$KM::active = false;
	$KM::selId = "";
	for(%i = 1; %i <= 6; %i++)
		$KM::info[%i] = "";
}

// Called by the engine ChatMenu on hotkey press, and by clickOption
function clientMenuSelect(%code)
{
	if(isObject(CurServerMenu))
		deleteObject(CurServerMenu);
	$KM::active = false;

	// "Back" on the selected-player menu (server: Admin.cs "deselect")
	// - drop the player-list highlight along with the selection
	if(%code == "deselect")
		$KM::selId = "";

	remoteEval(2048, menuSelect, %code);
}

// ============================================
// Hudbot mouse input
// ============================================

function onMouseActive(%isActive)
{
	$KM::mouseOn = %isActive;
	if(!%isActive)
	{
		$KSlider::drag = false;    // cursor went away mid-drag
		KronosMenu::dragEnd();
		$KC::scroll = 0;           // chat jumps back to newest when cursor hides

		// drop chat-composer keyboard capture if it was open via a click
		// (a key-bound beginSay during gameplay manages its own blur on Enter/Esc)
		if(KronosInput::isFocused("kchat"))
			KronosInput::blur();

		// cursor gone (TAB / score closed) - dismiss an open NPC dialogue
		// and let the server know so it clears its side too
		if($KNPC::open != "")
		{
			remoteEval(2048, KNPCClose);
			$KNPC::open = "";
		}
	}
}

function onMouseMove(%x, %y)
{
	$KM::mouseX = %x;
	$KM::mouseY = %y;

	// live drag of the UI-scale slider, or of a panel being moved
	if($KSlider::drag)
		KronosMenu::sliderSet(%x);
	else if($Drag::active)
		KronosMenu::dragMove(%x, %y);
}

function onMouseLMB(%isDown)
{
	$KM::lmbDown = %isDown;

	if(!%isDown)
	{
		$KSlider::drag = false;   // release ends any slider/panel drag
		KronosMenu::dragEnd();
		return;
	}

	// UI-scale slider takes priority (it sits clear of the panels, at
	// top-center, and is only live while the cursor is up)
	if($KM::mouseOn && $KM::enabled && KronosMenu::sliderHit($KM::mouseX, $KM::mouseY))
	{
		$KSlider::drag = true;
		KronosMenu::sliderSet($KM::mouseX);
		return;
	}

	// Chat A-/A+ text-size buttons (click, not drag) - check before the
	// drag handles so the buttons aren't swallowed by the box move handle
	if($KM::mouseOn && $KM::enabled && KronosChat::handleClick($KM::mouseX, $KM::mouseY))
		return;

	// NPC dialogue option rows (click) - before the drag handles
	if($KM::mouseOn && $KM::enabled && KronosNPC::handleClick($KM::mouseX, $KM::mouseY))
		return;

	// Grab a panel by its title bar to move it (works in menu and shop)
	if($KM::mouseOn && $KM::enabled)
	{
		%grab = KronosMenu::dragHit($KM::mouseX, $KM::mouseY);
		if(%grab != "")
		{
			KronosMenu::dragStart(%grab, $KM::mouseX, $KM::mouseY);
			return;
		}
	}

	// Kronos shop/inventory screen takes priority (KronosShop.cs)
	if($KS::open != "" && $KS::open != false)
	{
		KronosShop::handleClick($KM::mouseX, $KM::mouseY);
		return;
	}

	if(!$KM::active || !$KM::enabled)
		return;
	KronosMenu::handleClick($KM::mouseX, $KM::mouseY);
}

// Hit-test a click against the fixed row slots. $KML is refreshed
// every frame by the renderer while the menu is open.
function KronosMenu::handleClick(%x, %y)
{
	if($KML::rowH < 1)
		return;

	// menu option rows (left panel - dynamic width, set by render)
	if(%x >= $KML::mx + $KML::pad && %x < $KML::mx + $KML::wMenu - $KML::pad
		&& %y >= $KML::menuRowY0)
	{
		%row = floor((%y - $KML::menuRowY0) / $KML::rowH);
		if(%row < $KM::count && %row < $KM::MaxZones)
			KronosMenu::clickOption(%row);
		return;
	}

	// player list rows (right panel - dynamic width, set by render)
	if(%x >= $KML::px + $KML::pad && %x < $KML::px + $KML::wPlayers - $KML::pad
		&& %y >= $KML::plRowY0)
	{
		%row = floor((%y - $KML::plRowY0) / $KML::rowH);
		if(%row < $KM::plCount && %row < $KM::MaxPRows)
			KronosMenu::clickPlayer(%row);
	}
}

function KronosMenu::clickOption(%idx)
{
	if(!$KM::active || !$KM::enabled)
		return;
	if(%idx < 0 || %idx >= $KM::count)
		return;
	if($KM::code[%idx] == "")
		return;

	clientMenuSelect($KM::code[%idx]);
}

// Same message the stock scoreboard click sent (FearGuiScoreList).
function KronosMenu::clickPlayer(%idx)
{
	if(!$KM::active || !$KM::enabled)
		return;
	if(%idx < 0 || %idx >= $KM::plCount)
		return;
	%id = $KM::plId[%idx];
	if(%id == "" || %id == -1)
		return;
	$KM::selId = %id;

	remoteEval(2048, SelectClient, %id);
}

// ============================================
// Server data handlers
// ============================================

// Player list rows (KronosHUD_Server.cs remoteKMGetPlayers)
function remoteKMPlayer(%server, %idx, %id, %lvl, %remort, %class, %name)
{
	if(%server != 2048)
		return;
	$KM::plId[%idx] = %id;
	$KM::plLvl[%idx] = %lvl;
	$KM::plRL[%idx] = %remort;
	$KM::plClass[%idx] = %class;
	$KM::plName[%idx] = %name;
}

function remoteKMPlayerCount(%server, %sent, %total)
{
	if(%server != 2048)
		return;
	$KM::plCount = %sent;
	$KM::plTotal = %total;
	$KM::plDirty = true;
}

// Character info lines. Stock base client.cs writes these into the
// (now hidden) InfoCtrlBox; we capture them for the info panel
// instead. The server sends them for own stats (Game::menuRequest)
// and for a selected player (remoteSelectClient).
function remoteSetInfoLine(%server, %lineNum, %text)
{
	if(%server != 2048)
		return;
	if(%lineNum < 1 || %lineNum > 6)
		return;
	$KM::info[%lineNum] = %text;
	$KM::infoDirty = true;
}

// ============================================
// ScriptGL rendering (panels draw UNDER the dialog, but every
// stock dialog control is off-screen, so nothing covers them)
// ============================================

function KronosMenu::render(%dimensions)
{
	if(!$KM::active || !$KM::enabled)
	{
		// nothing drawn -> clear drag rects so a stale rect can't be grabbed
		// (the shop, if open, re-stashes its own panes right after this)
		$Panel::menuW = 0;
		$Panel::plW = 0;
		$Panel::infoShown = false;
		return;
	}

	%sw = getword(%dimensions, 0);
	%sh = getword(%dimensions, 1);

	KronosMenu::computeLayout(%sw, %sh);
	%pad = $KML::pad;
	%w = $KML::w;
	%titleH = $KML::titleH;
	%rowH = $KML::rowH;
	%y = $KML::y;

	%chipW = floor(%rowH * 1.1);
	%fontTitle = floor(%titleH * 0.62);
	%fontItem = floor(%rowH * 0.62);

	// Auto-width: widen the menu panel (up to 45% of screen - the
	// player panel starts at 54%) to fit the longest item label;
	// beyond that, shrink the item font to fit. Label widths are
	// measured once per menu (and re-measured on resolution change).
	if($KM::measureDirty || $KM::measuredFont != %fontItem)
	{
		glSetFont("Verdana", %fontItem, $GLEX_SMOOTH, 0);
		%maxText = 0;
		for(%i = 0; %i < $KM::count; %i++)
		{
			%tw = getword(glGetStringDimensions($KM::label[%i]), 0);
			if(%tw > %maxText)
				%maxText = %tw;
		}
		$KM::menuTextW = %maxText;
		$KM::measuredFont = %fontItem;
		$KM::measureDirty = false;
	}

	%fixed = (%pad * 2) + %chipW + floor(%pad * 0.7);
	%needW = %fixed + $KM::menuTextW + %pad;
	%wMax = floor(%sw * 0.45);
	%wm = %w;
	if(%needW > %wm)
		%wm = %needW;
	if(%wm > %wMax)
		%wm = %wMax;
	$KML::wMenu = %wm;

	%fontItemM = %fontItem;
	if(%needW > %wMax && $KM::menuTextW > 0)
	{
		%avail = %wMax - %fixed - %pad;
		%fontItemM = floor(%fontItem * %avail / $KM::menuTextW);
		if(%fontItemM < 9)
			%fontItemM = 9;
	}

	// Player panel auto-width: measure the widest name / level / class
	// and widen the panel so each column fits its slot (name 0-42%,
	// level 42-74%, class 74-100%). Capped so it stays on screen.
	if($KM::plDirty || $KM::plMeasuredFont != %fontItem)
	{
		glSetFont("Verdana", %fontItem, $GLEX_SMOOTH, 0);
		%nw = 0;
		%lw = 0;
		%cw = 0;
		for(%i = 0; %i < $KM::plCount; %i++)
		{
			%t = getword(glGetStringDimensions($KM::plName[%i]), 0);
			if(%t > %nw)
				%nw = %t;
			%lvText = "Lv " @ $KM::plLvl[%i];
			if($KM::plRL[%i] > 0)
				%lvText = %lvText @ " R" @ $KM::plRL[%i];
			%t = getword(glGetStringDimensions(%lvText), 0);
			if(%t > %lw)
				%lw = %t;
			%t = getword(glGetStringDimensions($KM::plClass[%i]), 0);
			if(%t > %cw)
				%cw = %t;
		}
		$KM::plNameW = %nw;
		$KM::plLvW = %lw;
		$KM::plClW = %cw;
		$KM::plMeasuredFont = %fontItem;
		$KM::plDirty = false;
	}

	%wp = %w;
	%n = floor(($KM::plNameW + (%pad * 2)) / 0.42);
	if(%n > %wp)
		%wp = %n;
	%n = floor(($KM::plLvW + %pad) / 0.32);
	if(%n > %wp)
		%wp = %n;
	%n = floor(($KM::plClW + (%pad * 2)) / 0.26);
	if(%n > %wp)
		%wp = %n;
	%wpMax = floor(%sw * 0.43);
	if(%wp > %wpMax)
		%wp = %wpMax;
	$KML::wPlayers = %wp;

	// stash panel rects for drag hit-testing (title bar = top titleH)
	$Panel::menuX = $KML::mx;   $Panel::menuY = %y;         $Panel::menuW = %wm;  $Panel::menuTH = %titleH;
	$Panel::plX   = $KML::px;   $Panel::plY   = $KML::plY;   $Panel::plW   = %wp;  $Panel::plTH   = %titleH;
	$Panel::infoShown = false;

	// hovered row (per-panel, same math as handleClick)
	%hovRow = -1;
	%hovPanel = "";
	if($KM::mouseOn && %rowH >= 1)
	{
		if($KM::mouseX >= $KML::mx + %pad && $KM::mouseX < $KML::mx + %wm - %pad
			&& $KM::mouseY >= $KML::menuRowY0)
		{
			%hovPanel = "menu";
			%hovRow = floor(($KM::mouseY - $KML::menuRowY0) / %rowH);
		}
		else if($KM::mouseX >= $KML::px + %pad && $KM::mouseX < $KML::px + %wp - %pad
			&& $KM::mouseY >= $KML::plRowY0)
		{
			%hovPanel = "players";
			%hovRow = floor(($KM::mouseY - $KML::plRowY0) / %rowH);
		}
	}

	// extra "+N more" row on the player panel when the list overflows
	%pRows = $KM::plCount;
	%overflow = 0;
	if($KM::plTotal > $KM::plCount)
		%overflow = 1;
	if(%pRows < 1)
		%pRows = 1;

	%mh = %titleH + (%rowH * $KM::count) + %pad;
	%ph = %titleH + (%rowH * (%pRows + %overflow)) + %pad;

	// ---- Pass 1: all rectangles (texture state stays off) ----
	glDisable($GL_TEXTURE_2D);
	glBlendFunc($GL_SRC_ALPHA, $GL_ONE_MINUS_SRC_ALPHA);

	KronosMenu::drawPanelBody($KML::mx, %y, %wm, %mh, %pad, %titleH);
	KronosMenu::drawPanelBody($KML::px, $KML::plY, %wp, %ph, %pad, %titleH);

	// menu row tints + hover + hotkey chips
	%iy = $KML::rowY0;
	for(%i = 0; %i < $KM::count; %i++)
	{
		if(%hovPanel == "menu" && %i == %hovRow)
		{
			glColor4ub(120, 170, 235, 55);
			glRectangle($KML::mx + 2, %iy, %wm - 4, %rowH);
		}
		else
		{
			%half = floor(%i / 2);
			if(%i - (%half * 2) == 1)
			{
				glColor4ub(255, 255, 255, 9);
				glRectangle($KML::mx + 2, %iy, %wm - 4, %rowH);
			}
		}
		glColor4ub(70, 115, 180, 150);
		glRectangle($KML::mx + %pad, %iy + 2, %chipW, %rowH - 4);
		%iy += %rowH;
	}

	// player row tints + hover + selection highlight
	%iy = $KML::plRowY0;
	for(%i = 0; %i < $KM::plCount; %i++)
	{
		if($KM::selId != "" && $KM::plId[%i] == $KM::selId)
		{
			glColor4ub(85, 140, 210, 70);
			glRectangle($KML::px + 2, %iy, %wp - 4, %rowH);
		}
		else if(%hovPanel == "players" && %i == %hovRow)
		{
			glColor4ub(120, 170, 235, 55);
			glRectangle($KML::px + 2, %iy, %wp - 4, %rowH);
		}
		else
		{
			%half = floor(%i / 2);
			if(%i - (%half * 2) == 1)
			{
				glColor4ub(255, 255, 255, 9);
				glRectangle($KML::px + 2, %iy, %wp - 4, %rowH);
			}
		}
		%iy += %rowH;
	}

	// character info panel body (suppressed while the KronosHUD item
	// examine overlay occupies this spot - see onPostDraw below)
	%hasInfo = false;
	if($KM::info[1] != "")
		%hasInfo = true;
	if($KH::exTime != "" && (GetSimTime() - $KH::exTime) < 10.0)
		%hasInfo = false;
	if(%hasInfo)
	{
		%infoLines = 0;
		for(%i = 1; %i <= 6; %i++)
			if($KM::info[%i] != "")
				%infoLines++;

		// auto-width: fit the longest info line (panel stays centered)
		%fontInfo = floor($KML::lineH * 0.78);
		if($KM::infoDirty || $KM::infoMeasuredFont != %fontInfo)
		{
			glSetFont("Verdana", %fontInfo, $GLEX_SMOOTH, 0);
			%mwi = 0;
			for(%i = 1; %i <= 6; %i++)
			{
				if($KM::info[%i] == "")
					continue;
				%t = getword(glGetStringDimensions($KM::info[%i]), 0);
				if(%t > %mwi)
					%mwi = %t;
			}
			$KM::infoTextW = %mwi;
			$KM::infoMeasuredFont = %fontInfo;
			$KM::infoDirty = false;
		}
		%wi = $KM::infoTextW + (%pad * 2);
		if(%wi < %w)
			%wi = %w;
		%wiMax = floor(%sw * 0.6);
		if(%wi > %wiMax)
			%wi = %wiMax;
		if($pref::Kronos::infoX == "c" || $pref::Kronos::infoX == "")
			%ixA = floor((%sw - %wi) / 2);
		else
			%ixA = floor($pref::Kronos::infoX * %sw);

		%ih = ($KML::lineH * %infoLines) + (%pad * 2);
		KronosMenu::drawPanelBody(%ixA, $KML::iy, %wi, %ih, %pad, 0);

		// stash for drag (whole info box is the drag handle)
		$Panel::infoX = %ixA;
		$Panel::infoY = $KML::iy;
		$Panel::infoW = %wi;
		$Panel::infoH = %ih;
		$Panel::infoShown = true;
	}

	// ---- Pass 2: all text ----
	// menu title + items
	glColor4ub(235, 240, 255, 245);
	glSetFont("Verdana", %fontTitle, $GLEX_SMOOTH, 4);
	glDrawString($KML::mx + %pad, %y + floor(%titleH * 0.16), $KM::title);

	glSetFont("Verdana", %fontItemM, $GLEX_SMOOTH, 0);
	%iy = $KML::rowY0;
	for(%i = 0; %i < $KM::count; %i++)
	{
		%ty = %iy + floor((%rowH - %fontItemM) / 2) - 1;
		glColor4ub(255, 255, 255, 235);
		glDrawString($KML::mx + %pad + floor(%chipW * 0.32), %ty, $KM::key[%i]);
		glColor4ub(225, 230, 240, 225);
		glDrawString($KML::mx + %pad + %chipW + floor(%pad * 0.7), %ty, $KM::label[%i]);
		%iy += %rowH;
	}

	// player list title + rows
	glColor4ub(235, 240, 255, 245);
	glSetFont("Verdana", %fontTitle, $GLEX_SMOOTH, 4);
	glDrawString($KML::px + %pad, $KML::plY + floor(%titleH * 0.16), "Players (" @ $KM::plTotal @ ")");

	glSetFont("Verdana", %fontItem, $GLEX_SMOOTH, 0);
	%lvX = $KML::px + floor(%wp * 0.42);
	%clX = $KML::px + floor(%wp * 0.74);
	%iy = $KML::plRowY0;
	if($KM::plCount < 1)
	{
		glColor4ub(160, 170, 190, 180);
		glDrawString($KML::px + %pad, %iy + floor((%rowH - %fontItem) / 2) - 1, "(no players)");
	}
	for(%i = 0; %i < $KM::plCount; %i++)
	{
		%ty = %iy + floor((%rowH - %fontItem) / 2) - 1;
		glColor4ub(255, 255, 255, 235);
		glDrawString($KML::px + %pad, %ty, $KM::plName[%i]);

		%lvText = "Lv " @ $KM::plLvl[%i];
		if($KM::plRL[%i] > 0)
			%lvText = %lvText @ " R" @ $KM::plRL[%i];
		glColor4ub(170, 200, 240, 220);
		glDrawString(%lvX, %ty, %lvText);

		glColor4ub(200, 210, 225, 210);
		glDrawString(%clX, %ty, $KM::plClass[%i]);
		%iy += %rowH;
	}
	if(%overflow)
	{
		glColor4ub(160, 170, 190, 180);
		glDrawString($KML::px + %pad, %iy + floor((%rowH - %fontItem) / 2) - 1, "+ " @ ($KM::plTotal - $KM::plCount) @ " more...");
	}

	// character info text
	if(%hasInfo)
	{
		%fontInfo = floor($KML::lineH * 0.78);
		%ty = $KML::iy + %pad;
		for(%i = 1; %i <= 6; %i++)
		{
			if($KM::info[%i] == "")
				continue;
			if(%i == 1)
			{
				glColor4ub(170, 200, 240, 245);
				glSetFont("Verdana", %fontInfo, $GLEX_SMOOTH, 4);
			}
			else
			{
				glColor4ub(225, 230, 240, 225);
				glSetFont("Verdana", %fontInfo, $GLEX_SMOOTH, 0);
			}
			glDrawString(%ixA + %pad, %ty, $KM::info[%i]);
			%ty += $KML::lineH;
		}
	}
}

// Panel chrome shared by all three panels (rect pass only).
// %titleH = 0 means no title underline.
function KronosMenu::drawPanelBody(%x, %y, %w, %h, %pad, %titleH)
{
	// body
	glColor4ub(12, 14, 22, 238);
	glRectangle(%x, %y, %w, %h);

	// accent border: top bar + thin sides/bottom
	glColor4ub(85, 140, 210, 220);
	glRectangle(%x, %y, %w, 2);
	glColor4ub(85, 140, 210, 90);
	glRectangle(%x, %y + %h - 1, %w, 1);
	glRectangle(%x, %y, 1, %h);
	glRectangle(%x + %w - 1, %y, 1, %h);

	if(%titleH > 0)
	{
		glColor4ub(85, 140, 210, 140);
		glRectangle(%x + %pad, %y + %titleH - 2, %w - (%pad * 2), 1);
	}
}

// ============================================
// UI-scale slider (drag to resize the whole GUI)
// ============================================
// A small click-and-drag widget at top-center, shown whenever the GUI
// cursor is up (TAB menu or shop open). Dragging it sets
// $pref::Kronos::UiScale live, so the menu/shop/info panels resize as
// you slide. Geometry computed here is stashed for sliderHit/sliderSet.
function KronosMenu::renderSlider(%sw, %sh)
{
	if(!$KM::enabled || !$KM::mouseOn)
	{
		$Panel::uisShown = false;
		return;
	}

	// The slider widget is sized by resolution ONLY (the reference base,
	// without the UiScale knob) - so dragging it doesn't resize the slider
	// itself under the cursor, only the rest of the GUI.
	%ref = $pref::Kronos::UiRefH;
	if(%ref == "" || %ref < 100)
		%ref = 1080;
	%k = %ref / %sh;
	if(%k > 1.0)
		%k = 1.0;

	%w     = floor(%sw * 0.18 * %k);
	%lineH = floor(%sh * 0.030 * %k);
	%pad   = floor(%sw * 0.008 * %k);
	if(%pad < 4)
		%pad = 4;
	%h = (%lineH * 2) + (%pad * 2);
	// movable position: X defaults to centered ("c") until dragged
	if($pref::Kronos::sliderX == "c" || $pref::Kronos::sliderX == "")
		%x = floor((%sw - %w) / 2);
	else
		%x = floor($pref::Kronos::sliderX * %sw);
	%y = floor($pref::Kronos::sliderY * %sh);
	%font = floor(%lineH * 0.62);
	if(%font < 9)
		%font = 9;

	// stash the widget rect as a move handle (the track is grabbed first
	// for scale-adjust in onMouseLMB, so the rest of the box moves it)
	$Panel::uisX = %x;  $Panel::uisY = %y;  $Panel::uisW = %w;  $Panel::uisH = %h;
	$Panel::uisShown = true;

	// track + knob
	%trackX = %x + %pad;
	%trackW = %w - (%pad * 2);
	%trackH = floor(%lineH * 0.28);
	if(%trackH < 3)
		%trackH = 3;
	%trackY = %y + %pad + %lineH + floor((%lineH - %trackH) / 2);

	%min = $KSlider::min;
	%max = $KSlider::max;
	%val = $pref::Kronos::UiScale;
	if(%val == "")
		%val = 1.0;
	if(%val < %min)
		%val = %min;
	if(%val > %max)
		%val = %max;
	%frac = (%val - %min) / (%max - %min);

	%knobW = floor(%lineH * 0.45);
	if(%knobW < 6)
		%knobW = 6;
	%knobH = floor(%lineH * 0.95);
	%knobX = %trackX + floor((%trackW - %knobW) * %frac);
	%knobY = %trackY + floor(%trackH / 2) - floor(%knobH / 2);

	// stash hit geometry (generous band around the track for easy grab)
	$KSlider::trackX = %trackX;
	$KSlider::trackW = %trackW;
	$KSlider::hitX0  = %trackX - %knobW;
	$KSlider::hitX1  = %trackX + %trackW + %knobW;
	$KSlider::hitY0  = %knobY - %pad;
	$KSlider::hitY1  = %knobY + %knobH + %pad;

	// hot when dragging or hovering
	%hot = false;
	if($KSlider::drag)
		%hot = true;
	else if($KM::mouseX >= $KSlider::hitX0 && $KM::mouseX <= $KSlider::hitX1
		&& $KM::mouseY >= $KSlider::hitY0 && $KM::mouseY <= $KSlider::hitY1)
		%hot = true;

	// ---- rect pass ----
	glDisable($GL_TEXTURE_2D);
	glBlendFunc($GL_SRC_ALPHA, $GL_ONE_MINUS_SRC_ALPHA);

	KronosMenu::drawPanelBody(%x, %y, %w, %h, %pad, 0);

	glColor4ub(0, 0, 0, 150);
	glRectangle(%trackX, %trackY, %trackW, %trackH);
	glColor4ub(85, 140, 210, 180);
	glRectangle(%trackX, %trackY, floor(%trackW * %frac), %trackH);

	if(%hot)
		glColor4ub(150, 195, 245, 245);
	else
		glColor4ub(120, 170, 235, 220);
	glRectangle(%knobX, %knobY, %knobW, %knobH);

	// ---- text pass ----
	glColor4ub(235, 240, 255, 240);
	glSetFont("Verdana", %font, $GLEX_SMOOTH, 4);
	glDrawString(%x + %pad, %y + floor(%pad * 0.5), "UI Scale  " @ floor((%val * 100) + 0.5) @ "%");
}

// Is (x,y) on the slider? Uses the geometry stashed by renderSlider.
function KronosMenu::sliderHit(%x, %y)
{
	if($KSlider::hitX1 <= $KSlider::hitX0)
		return false;
	if(%x >= $KSlider::hitX0 && %x <= $KSlider::hitX1
		&& %y >= $KSlider::hitY0 && %y <= $KSlider::hitY1)
		return true;
	return false;
}

// Map a mouse x to a scale value (snapped to 5% steps) and apply it live.
function KronosMenu::sliderSet(%x)
{
	if($KSlider::trackW < 1)
		return;
	%frac = (%x - $KSlider::trackX) / $KSlider::trackW;
	if(%frac < 0)
		%frac = 0;
	if(%frac > 1)
		%frac = 1;
	%val = $KSlider::min + (%frac * ($KSlider::max - $KSlider::min));
	%val = floor((%val / 0.05) + 0.5) * 0.05;   // snap to 0.05
	$pref::Kronos::UiScale = %val;
}

// ============================================
// Movable panels (drag a panel by its title bar)
// ============================================
// Panels are positioned from persisted screen fractions
// ($pref::Kronos::menuX/menuY, playersX/playersY, infoX/infoY) and store
// their on-screen rects each frame (in render). Grabbing a title bar
// starts a drag that rewrites those prefs live, so the move persists.

// Which panel's drag handle is under (x,y)?  "" if none.  Title bar =
// top titleH of the menu/players panels; the whole box for the info panel.
function KronosMenu::dragHit(%x, %y)
{
	// chat grip tab (explicit handle for the engine chat control)
	if($Panel::chatGripShown
		&& %x >= $Panel::chatGripX && %x < $Panel::chatGripX + $Panel::chatGripW
		&& %y >= $Panel::chatGripY && %y < $Panel::chatGripY + $Panel::chatGripH)
		return "chat";

	if($Panel::infoShown
		&& %x >= $Panel::infoX && %x < $Panel::infoX + $Panel::infoW
		&& %y >= $Panel::infoY && %y < $Panel::infoY + $Panel::infoH)
		return "info";

	if($Panel::menuW > 0
		&& %x >= $Panel::menuX && %x < $Panel::menuX + $Panel::menuW
		&& %y >= $Panel::menuY && %y < $Panel::menuY + $Panel::menuTH)
		return "menu";

	if($Panel::plW > 0
		&& %x >= $Panel::plX && %x < $Panel::plX + $Panel::plW
		&& %y >= $Panel::plY && %y < $Panel::plY + $Panel::plTH)
		return "players";

	// shop/inventory scrollbars (KronosShop.cs panes)
	if($Panel::sbInvShown
		&& %x >= $Panel::sbInvX && %x < $Panel::sbInvX + $Panel::sbInvW
		&& %y >= $Panel::sbInvY && %y < $Panel::sbInvY + $Panel::sbInvH)
		return "sbinv";
	if($Panel::sbStShown
		&& %x >= $Panel::sbStX && %x < $Panel::sbStX + $Panel::sbStW
		&& %y >= $Panel::sbStY && %y < $Panel::sbStY + $Panel::sbStH)
		return "sbst";

	// vhud HUD panels (HP/MP/XP, Lv/Gold, weapon bar) - whole panel is the
	// handle; rects come from the vhud render cache computed each onPreDraw
	for(%i = 0; %i < $Drag::hudN; %i++)
	{
		%nm = $Drag::hudName[%i];
		%rp = $vhud[%nm, render, pos];
		%rs = $vhud[%nm, render, size];
		%hx = getword(%rp, 0);
		%hy = getword(%rp, 1);
		%hw = getword(%rs, 0);
		%hh = getword(%rs, 1);
		if(%hw > 0 && %x >= %hx && %x < %hx + %hw && %y >= %hy && %y < %hy + %hh)
			return "hud" @ %i;
	}

	// chat overlay resize grip (bottom-right corner) - checked before the
	// body so the corner resizes and the rest of the box moves
	if($Panel::kchatSzShown
		&& %x >= $Panel::kchatSzX && %x < $Panel::kchatSzX + $Panel::kchatSzW
		&& %y >= $Panel::kchatSzY && %y < $Panel::kchatSzY + $Panel::kchatSzH)
		return "kchatsz";

	// chat scrollbar track (right gutter)
	if($Panel::kchatScrShown
		&& %x >= $Panel::kchatScrX && %x < $Panel::kchatScrX + $Panel::kchatScrW
		&& %y >= $Panel::kchatScrY && %y < $Panel::kchatScrY + $Panel::kchatScrTrackH)
		return "kchatscr";

	// custom chat overlay (KronosChat.cs) - whole box is the move handle
	if($Panel::kchatShown
		&& %x >= $Panel::kchatX && %x < $Panel::kchatX + $Panel::kchatW
		&& %y >= $Panel::kchatY && %y < $Panel::kchatY + $Panel::kchatH)
		return "kchat";

	// UI-scale slider widget - move handle (the track is grabbed earlier
	// in onMouseLMB for scale-adjust, so only the rest of it moves)
	if($Panel::uisShown
		&& %x >= $Panel::uisX && %x < $Panel::uisX + $Panel::uisW
		&& %y >= $Panel::uisY && %y < $Panel::uisY + $Panel::uisH)
		return "uislider";

	// NPC dialogue window - title bar moves it (option rows are clicked
	// earlier in onMouseLMB, so they aren't swallowed here)
	if($Panel::knpcShown
		&& %x >= $Panel::knpcX && %x < $Panel::knpcX + $Panel::knpcW
		&& %y >= $Panel::knpcY && %y < $Panel::knpcY + $Panel::knpcTH)
		return "knpcwin";

	return "";
}

function KronosMenu::dragStart(%id, %x, %y)
{
	$Drag::id = %id;
	$Drag::active = true;
	if(%id == "menu")
	{
		$Drag::dx = %x - $Panel::menuX;
		$Drag::dy = %y - $Panel::menuY;
	}
	else if(%id == "players")
	{
		$Drag::dx = %x - $Panel::plX;
		$Drag::dy = %y - $Panel::plY;
	}
	else if(%id == "info")
	{
		$Drag::dx = %x - $Panel::infoX;
		$Drag::dy = %y - $Panel::infoY;
	}
	else if(String::getSubStr(%id, 0, 3) == "hud")
	{
		%i = String::getSubStr(%id, 3, 9);
		%rp = $vhud[$Drag::hudName[%i], render, pos];
		$Drag::dx = %x - getword(%rp, 0);
		$Drag::dy = %y - getword(%rp, 1);
	}
	else if(%id == "chat")
	{
		%cp = Control::getPosition("chatDisplayHud");
		$Drag::dx = %x - getword(%cp, 0);
		$Drag::dy = %y - getword(%cp, 1);
	}
	else if(%id == "kchat")
	{
		$Drag::dx = %x - $Panel::kchatX;
		$Drag::dy = %y - $Panel::kchatY;
	}
	else if(%id == "kchatsz")
	{
		// offset from the box's bottom-right corner, so it tracks the cursor
		$Drag::dx = %x - ($Panel::kchatX + $Panel::kchatW);
		$Drag::dy = %y - ($Panel::kchatY + $Panel::kchatH);
	}
	else if(%id == "uislider")
	{
		$Drag::dx = %x - $Panel::uisX;
		$Drag::dy = %y - $Panel::uisY;
	}
	else if(%id == "knpcwin")
	{
		$Drag::dx = %x - $Panel::knpcX;
		$Drag::dy = %y - $Panel::knpcY;
	}
}

function KronosMenu::dragMove(%x, %y)
{
	if(!$Drag::active)
		return;
	%sw = getword($KM::dim, 0);
	%sh = getword($KM::dim, 1);
	if(%sw < 1 || %sh < 1)
		return;

	%fx = (%x - $Drag::dx) / %sw;
	%fy = (%y - $Drag::dy) / %sh;
	if(%fx < 0)    %fx = 0;
	if(%fx > 0.95) %fx = 0.95;
	if(%fy < 0)    %fy = 0;
	if(%fy > 0.96) %fy = 0.96;

	if($Drag::id == "menu")
	{
		$pref::Kronos::menuX = %fx;
		$pref::Kronos::menuY = %fy;
	}
	else if($Drag::id == "players")
	{
		$pref::Kronos::playersX = %fx;
		$pref::Kronos::playersY = %fy;
	}
	else if($Drag::id == "info")
	{
		$pref::Kronos::infoX = %fx;
		$pref::Kronos::infoY = %fy;
	}
	else if($Drag::id == "kchat")
	{
		$pref::Kronos::chatPosX = %fx;
		$pref::Kronos::chatPosY = %fy;
	}
	else if($Drag::id == "uislider")
	{
		$pref::Kronos::sliderX = %fx;
		$pref::Kronos::sliderY = %fy;
	}
	else if($Drag::id == "knpcwin")
	{
		$pref::Kronos::npcX = %fx;
		$pref::Kronos::npcY = %fy;
	}
	else if($Drag::id == "kchatsz")
	{
		// bottom-right corner: resize the WINDOW only - width from X,
		// height from Y (text size is separate, via the A-/A+ buttons).
		// The corner tracks the cursor.
		%right = %x - $Drag::dx;
		%bottom = %y - $Drag::dy;

		%wf = (%right - $Panel::kchatX) / %sw;
		if(%wf < 0.12) %wf = 0.12;
		if(%wf > 0.80) %wf = 0.80;
		$pref::Kronos::chatW = %wf;
		$KC::lastW = -1;        // width affects wrapping -> rewrap

		%hf = (%bottom - $Panel::kchatY) / %sh;
		if(%hf < 0.04) %hf = 0.04;
		if(%hf > 0.85) %hf = 0.85;
		$pref::Kronos::chatH = %hf;
	}
	else if($Drag::id == "kchatscr")
	{
		// scrollbar: map cursor Y on the track to a history scroll offset
		%vis = $Panel::kchatScrVisible;
		%tot = $Panel::kchatScrTotal;
		%max = %tot - %vis;
		if(%max > 0)
		{
			%th = $Panel::kchatScrTrackH;
			%thumbH = floor(%th * %vis / %tot);
			if(%thumbH < 14) %thumbH = 14;
			%travel = %th - %thumbH;
			if(%travel < 1) %travel = 1;
			%frac = (%y - floor(%thumbH / 2) - $Panel::kchatScrY) / %travel;
			if(%frac < 0) %frac = 0;
			if(%frac > 1) %frac = 1;
			$KC::scroll = floor(((1.0 - %frac) * %max) + 0.5);   // top = oldest
		}
	}
	else if($Drag::id == "sbinv" || $Drag::id == "sbst")
	{
		// shop/inventory scrollbar: cursor Y on the track -> list scroll
		// offset (top of track = top of list)
		if($Drag::id == "sbinv")
		{
			%trY = $Panel::sbInvY;  %trH = $Panel::sbInvH;
			%vis = $Panel::sbInvVis;  %tot = $Panel::sbInvTot;
		}
		else
		{
			%trY = $Panel::sbStY;  %trH = $Panel::sbStH;
			%vis = $Panel::sbStVis;  %tot = $Panel::sbStTot;
		}
		%max = %tot - %vis;
		if(%max > 0)
		{
			%thumbH = floor(%trH * %vis / %tot);
			if(%thumbH < 14) %thumbH = 14;
			%travel = %trH - %thumbH;
			if(%travel < 1) %travel = 1;
			%frac = (%y - floor(%thumbH / 2) - %trY) / %travel;
			if(%frac < 0) %frac = 0;
			if(%frac > 1) %frac = 1;
			%off = floor((%frac * %max) + 0.5);
			if($Drag::id == "sbinv")
				$KS::scroll[inv] = %off;
			else
				$KS::scroll[st] = %off;
		}
	}
	else if(String::getSubStr($Drag::id, 0, 3) == "hud")
	{
		// vhud panels store "x y" PERCENT and are recomputed by vhud, so
		// rewrite the pos + bust vhud's per-panel dimension cache
		%i = String::getSubStr($Drag::id, 3, 9);
		%nm = $Drag::hudName[%i];
		%posStr = (%fx * 100) @ " " @ (%fy * 100);
		$vhud[%nm, pos] = %posStr;
		$vhud[%nm, lastdimensions] = "";
		if(%nm == "kh_vitals")
			$pref::Kronos::vitalsPos = %posStr;
		else if(%nm == "kh_info")
			$pref::Kronos::infoHudPos = %posStr;
		else if(%nm == "kh_wbar")
			$pref::Kronos::wbarPos = %posStr;
	}
	else if($Drag::id == "chat")
	{
		// chat is an engine control - move it directly (pixels) and store
		// the position as fractions so it scales / persists
		%nx = %x - $Drag::dx;
		%ny = %y - $Drag::dy;
		if(%nx < 0) %nx = 0;
		if(%ny < 0) %ny = 0;
		if(%nx > %sw - 40) %nx = %sw - 40;
		if(%ny > %sh - 20) %ny = %sh - 20;
		Control::setPosition("chatDisplayHud", %nx, %ny);
		$pref::Kronos::chatX = %nx / %sw;
		$pref::Kronos::chatY = %ny / %sh;
	}
}

function KronosMenu::dragEnd()
{
	$Drag::active = false;
	$Drag::id = "";
}

// Small "Chat" grip tab at the chat window's top-left, shown while the
// cursor is up. Grabbing it drags the engine chat control (chatDisplayHud)
// - a dedicated handle so it never steals clicks from the chat or menus.
function KronosMenu::renderChatGrip(%sw, %sh)
{
	$Panel::chatGripShown = false;
	// the custom chat overlay (KronosChat.cs) handles chat + its own drag
	if($pref::Kronos::chatEnabled)
		return;
	if(!$KM::enabled || !$KM::mouseOn)
		return;

	%cp = Control::getPosition("chatDisplayHud");
	if(%cp == "")
		return;
	%cx = getword(%cp, 0);
	%cy = getword(%cp, 1);

	%k = KronosMenu::uiScale(%sh);
	%gw = floor(%sw * 0.05 * %k);
	if(%gw < 46)
		%gw = 46;
	%gh = floor(%sh * 0.026 * %k);
	if(%gh < 14)
		%gh = 14;

	$Panel::chatGripX = %cx;
	$Panel::chatGripY = %cy;
	$Panel::chatGripW = %gw;
	$Panel::chatGripH = %gh;
	$Panel::chatGripShown = true;

	glDisable($GL_TEXTURE_2D);
	glBlendFunc($GL_SRC_ALPHA, $GL_ONE_MINUS_SRC_ALPHA);
	if($Drag::active && $Drag::id == "chat")
		glColor4ub(120, 170, 235, 230);
	else
		glColor4ub(70, 115, 180, 200);
	glRectangle(%cx, %cy, %gw, %gh);
	glColor4ub(85, 140, 210, 235);
	glRectangle(%cx, %cy, %gw, 2);

	glColor4ub(235, 240, 255, 240);
	glSetFont("Verdana", floor(%gh * 0.6), $GLEX_SMOOTH, 0);
	glDrawString(%cx + floor(%gh * 0.3), %cy + floor(%gh * 0.14), "Chat");
}

// Re-apply the saved chat position (engine resets it to stock on gui load /
// resolution change). Captures the stock position once for resetLayout.
function KronosMenu::applyChatPos()
{
	%ext = Control::getExtent(PlayGui);
	%sw = getword(%ext, 0);
	%sh = getword(%ext, 1);
	if(%sw < 100 || %sh < 100)
		return;

	%cur = Control::getPosition("chatDisplayHud");
	if($pref::Kronos::chatStockX == "" && %cur != "")
	{
		$pref::Kronos::chatStockX = getword(%cur, 0) / %sw;
		$pref::Kronos::chatStockY = getword(%cur, 1) / %sh;
	}

	if($pref::Kronos::chatX == "")
		return;   // never moved - leave it at stock
	Control::setPosition("chatDisplayHud", floor($pref::Kronos::chatX * %sw), floor($pref::Kronos::chatY * %sh));
}

function ScriptGL::playGui::onPostDraw(%dimensions)
{
	%dim = KronosMenu::screenDim(%dimensions);

	KronosMenu::render(%dim);

	// Item examine overlay (KronosHUD.cs) - drawn here so it sits at
	// the menu's info-panel spot and shows whether or not the TAB
	// menu is currently open
	if($KH::exTime != "" && (GetSimTime() - $KH::exTime) < 10.0)
		kronos::examine_render(getword(%dim, 0), getword(%dim, 1));

	KronosChat::render(getword(%dim, 0), getword(%dim, 1));
	KronosMenu::renderSlider(getword(%dim, 0), getword(%dim, 1));
	KronosMenu::renderChatGrip(getword(%dim, 0), getword(%dim, 1));
}

// ============================================
// Console helpers
// ============================================

// Run from console WHILE the TAB menu is open.
function KronosMenu::probe()
{
	echo("--- KronosMenu::probe ---");
	echo("  $KM::active = " @ $KM::active @ "  count = " @ $KM::count @ "  players = " @ $KM::plCount @ "/" @ $KM::plTotal);
	echo("  mouseOn = " @ $KM::mouseOn @ "  mouse = " @ $KM::mouseX @ "," @ $KM::mouseY);
	echo("  rowY0 = " @ $KML::rowY0 @ "  rowH = " @ $KML::rowH @ "  menu x = " @ $KML::mx @ "  players x = " @ $KML::px);
	echo("  scale basis = " @ $KM::dimSrc @ "   used dims = " @ $KM::dim @ "   ScriptGL reported = " @ $KM::dimSGL);
	echo("  UiScale = " @ $pref::Kronos::UiScale @ "  refH = " @ $pref::Kronos::UiRefH @ "  -> factor k = " @ $KML::k);
	echo("  pos menu=" @ $pref::Kronos::menuX @ "," @ $pref::Kronos::menuY @ "  players=" @ $pref::Kronos::playersX @ "," @ $pref::Kronos::playersY @ "  info=" @ $pref::Kronos::infoX @ "," @ $pref::Kronos::infoY);
	echo("  info[1] = " @ $KM::info[1]);
	echo("-------------------------");
}

// Set the GUI scale live (and persist it). 1.0 = sized for the reference
// height; lower shrinks the whole GUI's screen-share, higher grows it back
// up to the original proportional size (capped there). Affects the TAB
// menu, the shop/inventory, and the item-examine overlay together.
function KronosMenu::setScale(%s)
{
	if(%s == "" || %s <= 0)
	{
		echo("usage: KronosMenu::setScale(0.85)  - current = " @ $pref::Kronos::UiScale);
		return;
	}
	$pref::Kronos::UiScale = %s;
	echo("KronosMenu: UI scale = " @ %s @ " (1.0 = sized for " @ $pref::Kronos::UiRefH @ "p; lower = smaller)");
}

// Restore the default panel positions (and the centered info box). Leaves
// the UI scale alone - use KronosMenu::setScale to reset that.
function KronosMenu::resetLayout()
{
	$pref::Kronos::menuX = 0.08;
	$pref::Kronos::menuY = 0.16;
	$pref::Kronos::playersX = 0.54;
	$pref::Kronos::playersY = 0.16;
	$pref::Kronos::infoX = "c";
	$pref::Kronos::infoY = 0.75;

	// vhud HUD panels (push the value back into vhud + bust its cache)
	$pref::Kronos::vitalsPos = "1.5 84";
	$pref::Kronos::infoHudPos = "81.5 84";
	$pref::Kronos::wbarPos = "25 96.3";
	$vhud["kh_vitals", pos] = $pref::Kronos::vitalsPos;  $vhud["kh_vitals", lastdimensions] = "";
	$vhud["kh_info", pos]   = $pref::Kronos::infoHudPos; $vhud["kh_info", lastdimensions] = "";
	$vhud["kh_wbar", pos]   = $pref::Kronos::wbarPos;    $vhud["kh_wbar", lastdimensions] = "";

	// chat window back to its captured stock position (stock chat, if used)
	$pref::Kronos::chatX = $pref::Kronos::chatStockX;
	$pref::Kronos::chatY = $pref::Kronos::chatStockY;
	KronosMenu::applyChatPos();

	// custom chat overlay back to default spot
	$pref::Kronos::chatPosX = 0.015;
	$pref::Kronos::chatPosY = 0.60;

	// UI-scale slider back to centered-top
	$pref::Kronos::sliderX = "c";
	$pref::Kronos::sliderY = 0.015;

	KronosMenu::dragEnd();
	echo("KronosMenu: panel positions reset to defaults");
}

function KronosMenu::disable()
{
	$KM::enabled = false;
	$KM::active = false;
	echo("KronosMenu: panel disabled. NOTE: the stock menu is moved");
	echo("  off-screen in the score gui files, so no menu will be visible.");
	echo("  Restore the .stockbak files for the stock menu back.");
}

function KronosMenu::enable()
{
	$KM::enabled = true;
	echo("KronosMenu: enabled");
}

// ============================================
// Initialize
// ============================================

$KM::active = false;
$KM::count = 0;
$KM::measureDirty = false;
$KM::menuTextW = 0;
$KM::measuredFont = "";
$KM::plDirty = false;
$KM::plNameW = 0;
$KM::plLvW = 0;
$KM::plClW = 0;
$KM::plMeasuredFont = "";
$KM::infoDirty = false;
$KM::infoTextW = 0;
$KM::infoMeasuredFont = "";
$KM::plCount = 0;
$KM::plTotal = 0;
$KM::selId = "";
$KM::mouseOn = false;
$KM::mouseX = -1;
$KM::mouseY = -1;
$KM::lmbDown = false;

// UI-scale slider state
$KSlider::min = 0.5;     // slider left end  (50%)
$KSlider::max = 1.5;     // slider right end (150%)
$KSlider::drag = false;
$KSlider::trackX = 0;
$KSlider::trackW = 0;
$KSlider::hitX0 = 0;
$KSlider::hitX1 = 0;
$KSlider::hitY0 = 0;
$KSlider::hitY1 = 0;

// panel-drag state
$Drag::active = false;
$Drag::id = "";
$Panel::menuW = 0;
$Panel::plW = 0;
$Panel::infoShown = false;
$Panel::chatGripShown = false;
$Panel::kchatShown = false;
$Panel::uisShown = false;

// draggable vhud HUD panels (KronosHUD.cs) - hit-tested via their vhud
// render rects; positions persist in their own $pref vars
$Drag::hudN = 3;
$Drag::hudName[0] = "kh_vitals";
$Drag::hudName[1] = "kh_info";
$Drag::hudName[2] = "kh_wbar";

// if exec'd mid-game, PlayGui is already up - apply the saved chat position
// now (and capture the stock position for resetLayout)
if($Mode::PlayMode)
	KronosMenu::applyChatPos();

echo("KronosMenu: modern TAB menu loaded (Hudbot mouse input)");
