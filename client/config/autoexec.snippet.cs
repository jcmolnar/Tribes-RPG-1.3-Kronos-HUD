// =====================================================================
// Kronos modern GUI — autoexec.snippet.cs
// ---------------------------------------------------------------------
// These exec lines load the Kronos HUD. ADD them to the END of your
// existing  config\autoexec.cs  (do NOT replace the whole file — the
// Presto repack maintains autoexec.cs and may overwrite a full replace).
// LOAD ORDER MATTERS — keep it exactly as below.
// =====================================================================

// Stock Hudbot vhud framework (resolution-scaling HUD base). NOT a Kronos
// file; ships with the Presto/ScriptGL repack. Included here for convenience.
exec("scriptgl2.cs");

// vhud HUD: HP/MP/XP, Lv/Gold, cast bar, weapon popup, target frame. Loads first.
exec("Presto\\KronosHUD.cs");

// Chat category filters (ads/cast/loot toggle buttons + persistence).
exec("Presto\\ChatFilter.cs");

// Shared ScriptGL text-input field (keyboard capture). Load before consumers.
// Requires the kronos_textinput plugin (Plugins\kronos_textinput.dll).
exec("Presto\\KronosInput.cs");

// Modern ScriptGL TAB menu + UI-scale slider + drag framework.
exec("Presto\\KronosMenu.cs");

// Modern shop / inventory / bank screen (AFTER KronosMenu).
exec("Presto\\KronosShop.cs");

// Custom ScriptGL chat overlay + composer (AFTER the others).
exec("Presto\\KronosChat.cs");

// Modern NPC dialogue window.
exec("Presto\\KronosNPC.cs");

// ScriptGL quick command menu (Ctrl+V) - replaces the engine chat menu that the
// Kronos overlay's chat-hiding made invisible. Self-disables off-mod (gated on
// $KH::hasData) so it never hijacks Ctrl+V on non-Kronos servers.
exec("Presto\\KronosCM.cs");

// Session stats panel (XP/hr, gold/hr) - client-side, draggable.
exec("Presto\\KronosStats.cs");
