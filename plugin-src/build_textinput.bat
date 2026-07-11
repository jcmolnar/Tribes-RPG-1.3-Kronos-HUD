@echo off
REM Build kronos_textinput.dll — 32-bit CLIENT ScriptGL keyboard text-input plugin for the 1.3 Kronos client.
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat" x86
if errorlevel 1 ( echo vcvarsall failed & exit /b 1 )
cd /d "%~dp0"
cl /nologo /LD /EHsc /O2 kronos_textinput.cpp /Fe:kronos_textinput.dll
if errorlevel 1 ( echo BUILD FAILED & exit /b 1 )
echo BUILD OK: kronos_textinput.dll
