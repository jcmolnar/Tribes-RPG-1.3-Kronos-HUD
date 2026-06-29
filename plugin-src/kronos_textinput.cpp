//============================================================================
// kronos_textinput.dll — CLIENT-side ScriptGL keyboard text input for the 1.3
// Kronos client (C:\Dynamix\Tribes\Tribes.exe, Borland, base 0x400000, no ASLR).
//
// ScriptGL is draw-only; this adds the keyboard half so the Kronos chat composer
// (KronosInput.cs / KronosChat.cs) can capture typing:
//   - registers the `glTextInput(0|1)` console command (KronosInput.cs calls it
//     when a text field is focused / blurred), and
//   - detours the engine's keyboard->bind dispatch (FUN_0050d62c) so that, while
//     text input is ON, each keyboard MAKE is forwarded to ScriptGL::onChar /
//     onKey AND SWALLOWED (so it doesn't also fire a weapon/move bind).
//
// Mirrors the proven kronos_playermanager.dll / kronos_aiteardown.dll plugin
// pattern (getPlugin descriptor + StringCallback registration via 0x5f4138 +
// byte-verified inline detour). RE/addresses: re/keydispatch_findings.md,
// re/asm_keydispatch_T1Vista.txt (verified against THIS client's bytes 2026-06-23:
// keydispatch prologue 53 56 8B F0 8B 42 04 @0x50d62c, unpatched).
//
// ABIs (Borland, from the disassembled call sites):
//   keydispatch FUN_0050d62c entry: EAX=this, EDX=event; returns plain `ret`.
//   evaluate    FUN_005f41a8:  EAX=console(*0x6583c4), EDX=cmd, ECX=1, PUSH 0,
//                              PUSH 0 ; callee-cleans (ret 8).
//   addCommand  FUN_005f4138:  StringCallback template (PUSH handler;PUSH 0;
//                              MOV ECX,name;XOR EDX,EDX;CALL). RET 8 self-cleans.
//   command handler:  ECX=argc, [ESP+4]=argv, return char* in EAX, RET 4.
//
// Event field offsets (confirmed in FUN_0050d62c): +0x04 type(==0x416),
// +0x20 deviceInst(==0), +0x28 deviceType(==3 keyboard), +0x29 objType(==0x0A
// SI_KEY), +0x2a objInst(DIK), +0x2b action(==1 SI_MAKE), +0x2c ascii.
//
// Deploy: build x86, drop kronos_textinput.dll in the client's Plugins\, enable
// with `$PluginLoader::kronos_textinput = true;` (client config). Self-pinned;
// install ABORTS + logs on any byte mismatch so it can't corrupt a non-matching
// binary.
//============================================================================
#include <windows.h>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <cstdarg>

#define FUN_ADDCMD     0x005f4138        // engine StringCallback addCommand
#define KD_SITE        0x0050d62c        // keyboard->bind dispatch entry (hook here)
#define KD_BACK        0x0050d633        // after the relocated 7-byte prologue

static void* const pKDBack = (void*)KD_BACK;

static volatile unsigned char g_textInput = 0;   // 1 while a ScriptGL field is focused
static volatile unsigned char g_hotkeyDik = 0;   // DIK of the "open composer" key (0=none). The hook
                                                 // SWALLOWS this key (so it never reaches the engine's
                                                 // action map / IDACTION_CHAT) and flags the script.
static volatile unsigned char g_hotkeyHit = 0;   // set on a hotkey make while NOT typing; script polls glPollHotkey()
static bool g_registered = false;

static void flog(const char* fmt, ...) {
    FILE* f = fopen("kronos_textinput.log", "a"); if (!f) return;
    va_list ap; va_start(ap, fmt); vfprintf(f, fmt, ap); va_end(ap); fputc('\n', f); fclose(f);
}

// ---- captured-key QUEUE -------------------------------------------------------
// CRITICAL: the keyboard dispatch hook runs deep inside the engine's input pump
// (and sometimes mid-console-eval). Calling the engine's evaluate() from there
// re-enters the console and crashes. So the hook does ZERO engine calls - it only
// appends the keystroke to this plain-C ring buffer. The SCRIPT drains it on its
// own frame via glTextPoll() and calls ScriptGL::onChar/onKey from safe context.
#define QN 256
static volatile int g_qhead = 0, g_qtail = 0;
static int g_qascii[QN], g_qdik[QN];

extern "C" void __cdecl queueKey(int ascii, int dik) {   // hook -> here: pure memory op, no engine call
    int n = (g_qtail + 1) % QN;
    if (n == g_qhead) return;                 // full: drop (never blocks/crashes)
    g_qascii[g_qtail] = ascii; g_qdik[g_qtail] = dik;
    g_qtail = n;
}

// ---- keyboard->bind dispatch detour (FUN_0050d62c; EAX=this, EDX=event) ----
// While text input is ON, forward the key + swallow it (return 1). Otherwise run
// the original prologue inline and jump back to 0x50d633 (no separate trampoline).
static const unsigned char KD_ORIG[7] = { 0x53,0x56, 0x8b,0xf0, 0x8b,0x42,0x04 };  // PUSH EBX;PUSH ESI;MOV ESI,EAX;MOV EAX,[EDX+4]
__declspec(naked) void keydispatch_hook() {
    __asm {
        cmp  byte ptr g_textInput, 0
        je   check_hotkey                    // not typing -> maybe the composer hotkey, else passthru
        cmp  dword ptr [edx+0x04], 0x416     // SimInputEventType
        jne  passthru
        cmp  byte ptr [edx+0x28], 3          // deviceType == SI_KEYBOARD
        jne  passthru
        cmp  dword ptr [edx+0x20], 0         // deviceInst == 0
        jne  passthru
        cmp  byte ptr [edx+0x29], 0x0A       // objType == SI_KEY
        jne  passthru
        // a keyboard key event while typing -> swallow; forward makes
        cmp  byte ptr [edx+0x2b], 1          // action == SI_MAKE ?
        jne  swallow
        movzx eax, byte ptr [edx+0x2a]       // DIK scancode (arg2)
        movzx ecx, byte ptr [edx+0x2c]       // ascii (arg1)
        push eax
        push ecx
        call queueKey                        // __cdecl(ascii, dik) - pure C, NO engine call
        add  esp, 8
    swallow:
        mov  eax, 1                          // handled -> caller skips the bind
        ret                                  // Borland: register args, plain ret
    check_hotkey:
        // NOT typing: intercept the composer hotkey BEFORE the action map, so the
        // engine never turns it into IDACTION_CHAT (stock chat). Only ECX/CL is
        // clobbered here (free reg); EAX(this)/EDX(event) stay intact for passthru.
        cmp  byte ptr g_hotkeyDik, 0         // no hotkey set -> normal passthru
        je   passthru
        cmp  dword ptr [edx+0x04], 0x416
        jne  passthru
        cmp  byte ptr [edx+0x28], 3          // keyboard
        jne  passthru
        cmp  byte ptr [edx+0x2b], 1          // make
        jne  passthru
        mov  cl, byte ptr [edx+0x2a]         // DIK scancode
        cmp  cl, byte ptr g_hotkeyDik
        jne  passthru
        mov  byte ptr g_hotkeyHit, 1         // signal the script to open the composer
        mov  eax, 1                          // swallow -> stock chat does NOT open
        ret
    passthru:
        // EAX(this) and EDX(event) are still intact here -> run original prologue
        push ebx                             // 0x50d62c
        push esi                             // 0x50d62d
        mov  esi, eax                        // 0x50d62e
        mov  eax, [edx+4]                    // 0x50d630
        jmp  dword ptr [pKDBack]             // -> 0x50d633
    }
}

// ---- glTextInput(0|1): focus on/off (also clears the queue on a fresh focus) ----
extern "C" char* __cdecl c_glTextInput(int argc, char** argv) {
    g_textInput = (argc >= 2 && argv[1] && argv[1][0] != '0') ? 1 : 0;
    if (g_textInput) { g_qhead = g_qtail = 0; }   // drop stale keys from before focus
    flog("glTextInput -> %d", g_textInput);
    return (char*)"";
}

// ---- glTextPoll(): dequeue ONE captured key for the script to apply, or "" if
// empty. Encoding: "c<char>" = a literal printable character (TorqueScript can't
// turn an ascii code back into a char, so we hand it the char itself); "k<dik>" =
// a non-printable key by DIK scancode. The script loops this each frame and calls
// ScriptGL::onChar(<char>) / ScriptGL::onKey(<dik>).
extern "C" char* __cdecl c_glTextPoll(int argc, char** argv) {
    static char buf[16];
    if (g_qhead == g_qtail) return (char*)"";       // empty
    int a = g_qascii[g_qhead], d = g_qdik[g_qhead];
    g_qhead = (g_qhead + 1) % QN;
    if (a >= 32 && a < 127) { buf[0] = 'c'; buf[1] = (char)a; buf[2] = 0; }
    else { _snprintf(buf, sizeof(buf), "k%d", d); buf[sizeof(buf)-1] = 0; }
    return buf;
}

// ---- glSetTalkKey(dik): set the "open composer" hotkey by DIK scancode (0 = off).
// The hook swallows this key before the engine's action map, so it can't trigger
// stock chat; the script polls glPollHotkey() and opens the composer instead.
extern "C" char* __cdecl c_glSetTalkKey(int argc, char** argv) {
    g_hotkeyDik = (unsigned char)((argc >= 2 && argv[1]) ? atoi(argv[1]) : 0);
    g_hotkeyHit = 0;
    flog("glSetTalkKey -> dik %d", (int)g_hotkeyDik);
    return (char*)"";
}

// ---- glPollHotkey(): "1" once after the hotkey was pressed (and clears), else "".
extern "C" char* __cdecl c_glPollHotkey(int argc, char** argv) {
    if (g_hotkeyHit) { g_hotkeyHit = 0; return (char*)"1"; }
    return (char*)"";
}

// engine command handler ABI: argc in ECX, argv on stack ([ESP+4]); ret 4; EAX=char*
#define HANDLER(NAME, IMPL) __declspec(naked) void NAME(){ \
    __asm mov eax,[esp+4]   /*argv*/ \
    __asm push eax          /*arg2 argv*/ \
    __asm push ecx          /*arg1 argc*/ \
    __asm call IMPL \
    __asm add esp,8 \
    __asm ret 4 }
HANDLER(h_glTextInput, c_glTextInput)
HANDLER(h_glTextPoll,  c_glTextPoll)
HANDLER(h_glSetTalkKey, c_glSetTalkKey)
HANDLER(h_glPollHotkey, c_glPollHotkey)

// regCmd(name, handler): engine getNumClients StringCallback registration, byte-for-byte.
__declspec(naked) void regCmd(const char* /*name*/, void* /*handler*/) {
    __asm {
        mov  eax, [esp+8]       // handler
        push eax                // param_5 = handler
        push 0                  // param_4 = 0 (no arg-count check; handler validates)
        mov  ecx, [esp+0xc]     // name (orig [esp+4] + 8)
        xor  edx, edx
        mov  eax, FUN_ADDCMD
        call eax                // RET 8 self-cleans the 2 pushes
        ret                     // cdecl: caller cleans name+handler
    }
}

extern "C" void __cdecl doRegister() {
    flog("doRegister entered (vtable[0]/init was called)");   // DIAG
    if (g_registered) return;
    g_registered = true;
    regCmd("glTextInput",  (void*)&h_glTextInput);
    regCmd("glTextPoll",   (void*)&h_glTextPoll);
    regCmd("glSetTalkKey", (void*)&h_glSetTalkKey);
    regCmd("glPollHotkey", (void*)&h_glPollHotkey);
    flog("registered glTextInput + glTextPoll + glSetTalkKey + glPollHotkey");
}

// plugin descriptor vtable[0] = init; loader calls it (desc in EAX) AFTER console ready
__declspec(naked) void myInit() {
    __asm {
        pushad
        call doRegister
        popad
        mov  eax, 1
        ret
    }
}

static bool install(unsigned site, const unsigned char* exp, int len, void* handler, const char* name) {
    unsigned char* p = (unsigned char*)site;
    if (memcmp(p, exp, len) != 0) {
        flog("install ABORT %s @0x%08x (have %02x %02x %02x, want %02x %02x %02x)",
             name, site, p[0], p[1], p[2], exp[0], exp[1], exp[2]);
        return false;
    }
    int rel = (int)((unsigned)handler - (site + 5));
    DWORD old; VirtualProtect(p, len, PAGE_EXECUTE_READWRITE, &old);
    p[0] = 0xE9; *(int*)(p + 1) = rel;
    for (int i = 5; i < len; ++i) p[i] = 0x90;
    VirtualProtect(p, len, old, &old);
    FlushInstructionCache(GetCurrentProcess(), p, len);
    flog("install OK %s @0x%08x -> 0x%08x (%d bytes)", name, site, (unsigned)handler, len);
    return true;
}

// ---- Kronos plugin descriptor (40 bytes) returned by getPlugin ----
static unsigned g_vtable[8];
static unsigned g_desc[10];
extern "C" __declspec(dllexport) void* getPlugin() {
    g_vtable[0] = (unsigned)&myInit;
    g_vtable[1] = 0x005f40d8; g_vtable[2] = 0x005f450c; g_vtable[3] = 0x005f3ff8;
    g_vtable[4] = 0x005f3f10; g_vtable[5] = 0x005f4138; g_vtable[6] = 0x005f41a8;
    g_vtable[7] = 0x005f3ddc;
    g_desc[0] = (unsigned)g_vtable;
    g_desc[1] = 0xbaadf00d;
    g_desc[2] = 0x00000000;                 // version double 4.0
    g_desc[3] = 0x40100000;
    g_desc[4] = (unsigned)"KronosTextInput";
    g_desc[5] = (unsigned)"ScriptGL keyboard text input (client)";
    g_desc[6] = 0;
    g_desc[7] = 3;                           // flags: client|server (bit0|bit1). MUST be 3: the loader's
                                             // side-detect (FUN_10001095) can report "server" on the client,
                                             // and flags=1 fails its `&2` check -> init never called.
    g_desc[8] = 0xbaadf00d;
    g_desc[9] = 0xbaadf00d;
    flog("getPlugin -> desc=%p init=%p", (void*)g_desc, (void*)&myInit);
    return g_desc;
}

BOOL WINAPI DllMain(HINSTANCE hinst, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(hinst);
        HMODULE self = NULL;
        GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
                           (LPCWSTR)(void*)&myInit, &self);
        flog("--- kronos_textinput loaded (pinned=%p) ---", (void*)self);
        // code hook is safe in DllMain (VirtualProtect on static code, no engine calls);
        // command registration happens in myInit (vtable[0]) when the console is ready.
        install(KD_SITE, KD_ORIG, 7, (void*)&keydispatch_hook, "keydispatch");
    }
    return TRUE;
}
