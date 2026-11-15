#!/usr/bin/env bash
# Open Chamber - Week 5 Lite Launcher (Low-End / Laptop Edition)
echo "Starting Open Chamber in Lite Mode (Compatibility / OpenGL 3.3)..."
if command -v godot-mono &> /dev/null; then
    godot-mono --rendering-method gl_compatibility "$@"
elif [ -f "./godot-mono" ]; then
    ./godot-mono --rendering-method gl_compatibility "$@"
else
    godot --rendering-method gl_compatibility "$@"
fi
