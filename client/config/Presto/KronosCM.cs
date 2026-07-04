//==============================================
// KronosCM.cs (1.3) - ScriptGL quick command menu, reading the live Presto Menu::
//==============================================
// The Ctrl+V DeusRPGPack menu (Menu::Display(MenuDeus)) renders through the engine
// chat menu (chatDisplayHud), which the Kronos overlay hides - so it's invisible.
// This reimplements it as a ScriptGL overlay that READS the live Presto Menu::
// structure (Presto\Menu.cs + Chat.cs), so it always matches the mod's menu
// (AutoCast, all spells, transports, etc.) with no duplication.
//
// Each menu entry is read via Menu::GetLetter/GetText/GetAction (entries are
// 0..GetNumChoices-1). A submenu is just an action of the form "Menu::Display(X);";
// any other action is a leaf that we eval() on select (exactly like Menu::Choose).
// Keys ride the existing glTextInput seam (kronos_textinput.dll), like the chat
// composer. Ctrl+V opens/closes; a letter descends a submenu or fires a leaf;
// Esc / Backspace go up a level (and close at the root).
//==============================================

$KCM::root = MenuDeus;     // the Ctrl+V root menu (bareword -> the string "MenuDeus")

$KCM::open = false;
$KCM::navN = 0;            // depth; $KCM::navMenu[0..navN-1] = menu-name stack
$KCM::rowN = 0;            // rows for the current level

function KronosCM::curMenu()
{
	if($KCM::navN <= 0)
		return $KCM::root;
	return $KCM::navMenu[$KCM::navN - 1];
}

// Detect a submenu link: action "Menu::Display(MenuX);" -> returns "MenuX", else "".
function KronosCM::subTarget(%action)
{
	%p = String::findSubStr(%action, "Menu::Display(");
	if(%p == -1)
		return "";
	%rest = String::getSubStr(%action, %p + 14, 99999);   // after "Menu::Display("
	%end = String::findSubStr(%rest, ")");
	if(%end == -1)
		return "";
	%name = String::getSubStr(%rest, 0, %end);
	%comma = String::findSubStr(%name, ",");               // Menu::Display(X, sort)
	if(%comma != -1)
		%name = String::getSubStr(%name, 0, %comma);
	// trim surrounding spaces
	while(String::getSubStr(%name, 0, 1) == " ")
		%name = String::getSubStr(%name, 1, 99999);
	while(String::len(%name) > 0 && String::getSubStr(%name, String::len(%name) - 1, 1) == " ")
		%name = String::getSubStr(%name, 0, String::len(%name) - 1);
	return %name;
}

// Read the current menu's enabled entries into the row arrays.
function KronosCM::buildRows()
{
	%menu = KronosCM::curMenu();
	%n = Menu::GetNumChoices(%menu);
	$KCM::rowN = 0;
	for(%e = 0; %e < %n; %e++)
	{
		if(!Menu::GetEnabled(%menu, %e))
			continue;
		%text = Menu::GetText(%menu, %e);
		if(%text == "")            // single-letter (hidden) entries: not shown
			continue;
		%letter = Menu::GetLetter(%menu, %e);
		%action = Menu::GetAction(%menu, %e);
		%sub = KronosCM::subTarget(%action);
		%rk = $KCM::rowN;
		$KCM::rowKey[%rk]   = %letter;
		$KCM::rowLabel[%rk] = %text;
		if(%sub != "")
		{
			$KCM::rowLeaf[%rk] = false;
			$KCM::rowSub[%rk]  = %sub;
		}
		else
		{
			$KCM::rowLeaf[%rk] = true;
			$KCM::rowAct[%rk]  = %action;
		}
		$KCM::rowN++;
	}
}

function KronosCM::open()
{
	// Non-Kronos servers: MenuDeus is a client-side DeusRPGPack menu, so it
	// still exists off-mod and Ctrl+V would pop a useless RPG spell menu on
	// a stock/other server. Gate on $KH::hasData (set true only once a Kronos
	// server pushes remoteKronosHUD) - the same "are we on Kronos" signal the
	// HUD/Stats self-disable on. Off-mod, Ctrl+V falls away with no overlay.
	if(!$KH::hasData)
		return;
	if(KronosInput::anyFocused())     // we own the key seam while open
		KronosInput::blur();
	$KCM::open = true;
	$KCM::navN = 0;
	KronosCM::buildRows();
	glTextInput(1);                   // capture hotkeys + Esc (kronos_textinput.dll)
}

function KronosCM::close()
{
	if(!$KCM::open)
		return;
	$KCM::open = false;
	glTextInput(0);
}

function KronosCM::toggle()
{
	if($KCM::open)
		KronosCM::close();
	else
		KronosCM::open();
}

// Up one level; at root, close.
function KronosCM::up()
{
	if($KCM::navN <= 0)
	{
		KronosCM::close();
		return;
	}
	$KCM::navN--;
	KronosCM::buildRows();
}

// A hotkey letter was pressed: descend a submenu, or eval + close a leaf.
function KronosCM::hotkey(%ch)
{
	for(%r = 0; %r < $KCM::rowN; %r++)
	{
		if(String::Compare($KCM::rowKey[%r], %ch) == 0)
		{
			if($KCM::rowLeaf[%r])
			{
				eval($KCM::rowAct[%r]);   // same as Menu::Choose
				KronosCM::close();
			}
			else
			{
				$KCM::navMenu[$KCM::navN] = $KCM::rowSub[%r];
				$KCM::navN++;
				KronosCM::buildRows();
			}
			return;
		}
	}
}

// ============================================
// Per-frame key pump (drain the glTextInput queue while open)
// ============================================
function KronosCM::pump()
{
	if(!$KCM::open)
		return;
	if(KronosInput::anyFocused())   // a text field owns the seam - don't eat its keys
		return;
	%guard = 0;
	while(%guard < 200)
	{
		%guard++;
		%ev = glTextPoll();
		if(String::len(%ev) < 1)
			return;
		%kind = String::getSubStr(%ev, 0, 1);
		%val  = String::getSubStr(%ev, 1, 99999);
		if(String::Compare(%kind, "c") == 0)
			KronosCM::hotkey(%val);       // printable char = hotkey
		else
		{
			if(%val == 1 || %val == 14)   // Esc / Backspace = up a level
				KronosCM::up();
		}
		if(!$KCM::open)                   // a leaf closed us mid-drain
			return;
	}
}

// ============================================
// Render (ScriptGL overlay; called from the onPostDraw chain)
// ============================================
function KronosCM::render(%sw, %sh)
{
	if(!$KCM::open)
		return;

	%font   = floor(%sh * 0.024);
	if(%font < 12)
		%font = 12;
	%pad    = floor(%font * 0.55);
	%rowH   = %font + floor(%pad * 0.7);
	%titleH = %rowH + %pad;

	// column width: fit the widest row
	glSetFont("Verdana", %font, $GLEX_SMOOTH, 0);
	%colW = floor(%sw * 0.20);
	for(%r = 0; %r < $KCM::rowN; %r++)
	{
		%lw = getWord(glGetStringDimensions($KCM::rowLabel[%r] @ "      "), 0) + floor(%font * 1.6);
		if(%lw + (%pad * 2) > %colW)
			%colW = %lw + (%pad * 2);
	}
	if(%colW > floor(%sw * 0.45))
		%colW = floor(%sw * 0.45);

	// wrap into extra columns when the list is taller than the screen allows
	// (long submenus like the 17-row transports clipped at low resolutions)
	%availH = floor(%sh * 0.84) - %titleH - %rowH - %pad;   // between the 8% top margin and the bottom
	%maxRows = floor(%availH / %rowH);
	if(%maxRows < 4)
		%maxRows = 4;
	%cols = 1;
	%perCol = $KCM::rowN;
	if($KCM::rowN > %maxRows)
	{
		%cols = floor(($KCM::rowN + %maxRows - 1) / %maxRows);
		%perCol = floor(($KCM::rowN + %cols - 1) / %cols);
	}
	if(%perCol < 1)
		%perCol = 1;
	%w = %colW * %cols;
	if(%w > floor(%sw * 0.90))
		%w = floor(%sw * 0.90);

	%h = %titleH + (%perCol * %rowH) + %rowH + %pad;   // title + rows + hint + pad
	%x = floor(%sw * 0.03);
	%y = floor((%sh - %h) / 2);
	if(%y < floor(%sh * 0.08))
		%y = floor(%sh * 0.08);

	glDisable($GL_TEXTURE_2D);
	glBlendFunc($GL_SRC_ALPHA, $GL_ONE_MINUS_SRC_ALPHA);

	// backdrop + border
	glColor4ub(14, 18, 30, 242);
	glRectangle(%x, %y, %w, %h);
	glColor4ub(110, 165, 235, 235);
	glRectangle(%x, %y, %w, 2);
	glColor4ub(110, 165, 235, 110);
	glRectangle(%x, %y + %h - 1, %w, 1);
	glRectangle(%x, %y, 1, %h);
	glRectangle(%x + %w - 1, %y, 1, %h);

	// title = the current menu's Presto title
	%title = Menu::GetTitle(KronosCM::curMenu());
	if(%title == "")
		%title = "Quick Menu";
	glColor4ub(235, 240, 255, 245);
	glSetFont("Verdana", floor(%font * 0.92), $GLEX_SMOOTH, 0);
	glDrawString(%x + %pad, %y + floor(%pad * 0.6), %title);
	glColor4ub(85, 140, 210, 140);
	glRectangle(%x + %pad, %y + %titleH - 2, %w - (%pad * 2), 1);

	// rows: hotkey letter (gold) + label (white leaf / blue submenu, with a ">"
	// arrow); rows flow top-to-bottom then wrap into the next column
	for(%r = 0; %r < $KCM::rowN; %r++)
	{
		%col = floor(%r / %perCol);
		%cx = %x + (%col * %colW);
		%iy = %y + %titleH + ((%r - (%col * %perCol)) * %rowH);
		%ty = %iy + floor((%rowH - %font) / 2) - 1;
		glSetFont("Verdana", %font, $GLEX_SMOOTH, 0);
		glColor4ub(255, 210, 75, 235);
		glDrawString(%cx + %pad, %ty, $KCM::rowKey[%r]);
		if($KCM::rowLeaf[%r])
			glColor4ub(225, 232, 245, 235);
		else
			glColor4ub(150, 215, 255, 240);
		glDrawString(%cx + %pad + floor(%font * 1.3), %ty, $KCM::rowLabel[%r]);
		if(!$KCM::rowLeaf[%r])
		{
			glColor4ub(120, 160, 210, 200);
			glDrawString(%cx + %colW - %pad - floor(%font * 0.55), %ty, ">");
		}
	}

	// faint separators between columns
	if(%cols > 1)
	{
		glDisable($GL_TEXTURE_2D);
		glColor4ub(85, 140, 210, 60);
		for(%c = 1; %c < %cols; %c++)
			glRectangle(%x + (%c * %colW), %y + %titleH + 2, 1, (%perCol * %rowH) - 4);
	}

	// hint
	%iy = %y + %titleH + (%perCol * %rowH);
	glColor4ub(150, 160, 185, 190);
	glSetFont("Verdana", floor(%font * 0.66), $GLEX_SMOOTH, 0);
	glDrawString(%x + %pad, %iy + floor(%pad * 0.3), "press a letter    Esc = back");
}

// ============================================
// Bind Ctrl+V to our menu (overrides Chat.cs's Menu::Display bind; re-runs each launch)
// ============================================
bindCommand(keyboard0, make, control, "v", TO, "KronosCM::toggle();");

echo("KronosCM: ScriptGL quick menu loaded - reads live Presto Menu (Ctrl+V)");
