// =====================================================================
// Kronos text-input plugin — PluginLoader gate
// ---------------------------------------------------------------------
// ADD this block to your existing  Plugins\Scripts\PluginLoader.cs
// (a full reference copy is in PluginLoader.reference.cs).
//
// The plugin's descriptor reports flags=3 (loads on both client and
// server side-detect), so it MUST be gated to CLIENT-ONLY here: it
// patches the keyboard-dispatch code, which must NOT run on a dedicated
// server sharing this Plugins\ folder (it would hang the server on
// connect).
// =====================================================================
if(!$dedicated)
	$PluginLoader::kronos_textinput = true;
else
	$PluginLoader::kronos_textinput = false;
