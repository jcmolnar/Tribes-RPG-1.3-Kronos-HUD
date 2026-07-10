# Tribes RPG 1.3 — Kronos modern GUI / HUD

A modern, resolution-scaling, draggable HUD for **Starsiege: Tribes 1.3** playing on the
**Kingdom of Kronos** RPG server. Replaces the stock fixed-size HUD/chat with a
ScriptGL-drawn interface: scalable vitals bars (HP/MP/XP), Lv/Gold, cast bar, weapon
popup, target frame, a modern TAB menu with a live **player list (level / class /
location)**, a shop/inventory/bank screen with **search boxes, hover item tooltips,
mouse-wheel scrolling, and right-click quick bank transfers**, an NPC dialogue window,
a **quick command menu (Ctrl+V)**, a **session stats panel** (XP/hr, gold/hr), and a
custom chat overlay with a **typed chat composer**, color-coded player chat, and
saveable category filters.

All the Kronos overlays are **vanilla-safe off-mod**: on servers that don't run the
Kronos HUD they self-disable (gated on the server handshake), so the stock HUD and
keybinds behave normally everywhere else.

Everything draws through **ScriptGL** (the `gl*` console commands provided by the Presto
repack's `GraphicPlugin.dll`) plus the **Hudbot vhud** framework (`scriptgl2.cs`). The
only native code in this project is a small input plugin (`kronos_textinput.dll`) that
adds keyboard text capture, which ScriptGL alone cannot do.

> These are **client-side** scripts. You connect to a Kronos server that already runs the
> matching server scripts (included under `server/` for server operators). The HUD data
> (stats, NPC lines, bank contents) is pushed by the server via `remoteEval`; a client
> running these scripts answers the `KHudOn` handshake and renders it.

---

## Quick install (recommended)

Grab **[`KronosHUD-1.3-Client.zip`](KronosHUD-1.3-Client.zip)** — it mirrors the Tribes
folder layout. Close Tribes, copy the zip's `config` and `Plugins` folders into your
Tribes folder (next to `Tribes.exe`), overwrite when asked, and launch. See the zip's
`INSTALL.txt` for the two files it replaces (`autoexec.cs`, `PluginLoader.cs`) in case
you had customized them.

The manual, merge-friendly install is described below.

---

## Requirements

- **Tribes 1.3 client** (the T1Vista / "Tribes Repack" Borland build — the one that loads
  `Plugins\` via `mem.dll`). This is **not** the 1.40 client.
- The **Presto / ScriptGL repack** installed, specifically:
  - `Plugins\GraphicPlugin.dll` — provides the ScriptGL `gl*` draw commands.
  - `mem.dll` plugin loader + `Plugins\Scripts\PluginLoader.cs`.
  - `config\Presto\` script tree (Presto HUD base) and `config\scriptgl2.cs`.
- Access to the **Kingdom of Kronos** server (the HUD shows real data only when connected
  to a Kronos server running the `server/` scripts).

If you already play Kronos with the Presto repack, you have everything except the files in
this repo.

---

## Install (client)

1. **Scripts** — copy the contents of `client/config/` into your Tribes `config\` folder:
   - `client/config/Presto/*.cs`  →  `config\Presto\`
   - `client/config/scriptgl2.cs` →  `config\` (only if you don't already have it)

2. **Plugin** — copy the text-input plugin:
   - `client/Plugins/kronos_textinput.dll` → `Plugins\`

3. **Enable the plugin** — merge the block from
   `client/Plugins/Scripts/PluginLoader.snippet.cs` into your
   `Plugins\Scripts\PluginLoader.cs` (a full reference copy is
   `PluginLoader.reference.cs`). It gates the plugin to **client-only**:
   ```c
   if(!$dedicated)
       $PluginLoader::kronos_textinput = true;
   else
       $PluginLoader::kronos_textinput = false;
   ```

4. **Load the scripts** — append the `exec` lines from
   `client/config/autoexec.snippet.cs` to the **end** of your `config\autoexec.cs`.
   Keep the load order exactly as given (HUD → Input → Menu → Shop → Chat → NPC).

5. Launch the 1.3 client and connect to Kronos. Press **TAB** for the menu, **I** for
   inventory; chat appears in the new overlay. Press your global-chat key (**Y** by
   default) to type into the composer, and **Ctrl+V** for the quick command menu.
   With the cursor up: hover an item for its tooltip, use the **mouse wheel** to scroll
   lists/chat, and **right-click** a bank row to move the whole stack.

> **Note on `autoexec.cs` / `config.cs`:** the repack and the game itself rewrite these
> on exit. Only **append** to `autoexec.cs`; never paste your whole copy over the repack's.

---

## What's included

```
client/
  config/
    autoexec.snippet.cs            exec lines to append to your autoexec.cs
    scriptgl2.cs                   Hudbot vhud framework (repack file, bundled for convenience)
    Presto/
      KronosHUD.cs                 vhud HUD: vitals/Lv/Gold/cast/weapon/target
      KronosMenu.cs                TAB menu + player list + UI-scale slider + scale/drag framework
      KronosShop.cs                shop / inventory / bank screen (search, tooltips, wheel, RMB)
      KronosChat.cs                custom chat overlay + typed composer + color + filters
      KronosInput.cs               reusable ScriptGL text-input field (needs the plugin)
      KronosNPC.cs                 modern NPC dialogue window
      KronosCM.cs                  quick command menu (Ctrl+V) over the live Presto menu
      KronosStats.cs               session stats panel (XP/hr, gold/hr), draggable
      ChatFilter.cs                chat category filter toggles (ads/cast/loot), persisted
      KronosGUI_README.md          deep design notes, prefs list, console helpers
  Plugins/
    kronos_textinput.dll           native text-input plugin (keyboard capture for the composer)
    Scripts/
      PluginLoader.snippet.cs      the client-only gate to add to PluginLoader.cs
      PluginLoader.reference.cs    full reference PluginLoader.cs

plugin-src/
  kronos_textinput.cpp             plugin source (32-bit; detours the 1.3 keyboard dispatch)
  build_textinput.bat              MSVC build script

server/                            (for the Kronos SERVER operator only — optional)
  KronosHUD_Server.cs              pushes stats/bank to HUD clients
  KronosNPC_Server.cs              mirrors NPC dialogue lines/options to the NPC window
```

See **`client/config/Presto/KronosGUI_README.md`** for full design notes: the scaling
model, the movable-panel/drag framework, every persisted `$pref::Kronos::*` variable, and
the console helper commands (`KronosMenu::setScale`, `KronosChat::beginSay`, etc.).

---

## The text-input plugin (`kronos_textinput.dll`)

ScriptGL is **draw-only** — it has no way to read the keyboard, and Tribes grabs the
keyboard with exclusive DirectInput, so window messages can't see keystrokes either. This
plugin adds that missing capability so the chat composer (and future amount/search boxes)
can capture typed text.

It is an injected DLL loaded by the repack's `mem.dll`. It installs an inline x86 detour on
the 1.3 client's keyboard-dispatch routine (`0x0050d62c`) and:

- registers the console commands `glTextInput`, `glTextPoll`, `glSetTalkKey`,
  `glPollHotkey`, `glPollWheel`, `glMouseRMB`;
- **mouse wheel**: a low-level mouse hook (`WH_MOUSE_LL`) on a dedicated message-loop
  thread accumulates wheel ticks (DirectInput swallows `WM_MOUSEWHEEL` before the game
  window ever sees it, and hooking from the game's own thread would lag the whole
  system's mouse); the script drains whole notches per frame via `glPollWheel`;
- **right mouse button**: `glMouseRMB` reports the hardware button state
  (`GetAsyncKeyState`) for the bank quick-transfer clicks;
- while text-input is active, **queues** each keystroke into a plain C ring buffer and
  swallows it (so movement/weapon binds don't fire while you type) — the hook does **zero**
  engine calls (calling back into the console from the dispatch hook crashes it); the script
  drains the queue each frame via `glTextPoll`;
- lets a hotkey (default **Y**, `glSetTalkKey(21)`) open the composer by swallowing the key
  before the action map (the only reliable way past the engine's built-in `IDACTION_CHAT`
  binding, which the game re-saves into `config.cs`).

The descriptor reports `flags=3`, so it loads under the loader's side-detect on both the
client and a dedicated server — which is why the `PluginLoader` gate **must** restrict it to
`!$dedicated`. It patches client-only keyboard code and would hang a dedicated server.

### Building the plugin
You don't need to build it — `client/Plugins/kronos_textinput.dll` is ready to use. To
rebuild, run `plugin-src/build_textinput.bat` from a 32-bit MSVC environment (`vcvarsall
x86`). It produces a 32-bit DLL; the detour byte-checks the 1.3 client prologue at
`0x50d62c` and self-pins.

---

## Server side (operators only)

If you run a Kronos server and want connected HUD clients to receive live data, install the
two scripts under `server/` into your server's `rpg/scripts/` and `exec` them from
`Server.cs`:

- **KronosHUD_Server.cs** — answers the `KHudOn` handshake and pushes vitals, Lv/Gold, and
  bank contents to HUD clients. Vanilla clients are unaffected.
- **KronosNPC_Server.cs** — mirrors each NPC's real spoken line + parsed dialogue options to
  the client's NPC window (hooks `AI::sayLater`); the actual dialogue logic runs unchanged.

Plain players who join **without** the HUD see the stock interface — nothing here changes
gameplay or is required server-side for normal play.

---

## Credits

Built on the Tribes 1.x Presto/ScriptGL repack (`GraphicPlugin.dll`) and the Hudbot vhud
framework (`scriptgl2.cs` / team5150). The Kronos GUI scripts, the text-input plugin, and
the chat/shop/NPC/bank windows are this project's work.
