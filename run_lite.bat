@echo off
REM Open Chamber - Week 5 Lite Launcher (Low-End / Laptop Edition)
echo Starting Open Chamber in Lite Mode (Compatibility / OpenGL 3.3)...
if exist "godot-mono.exe" (
    godot-mono.exe --rendering-method gl_compatibility %*
) else if exist "OpenChamber.exe" (
    OpenChamber.exe --rendering-method gl_compatibility %*
) else (
    godot.exe --rendering-method gl_compatibility %*
)
