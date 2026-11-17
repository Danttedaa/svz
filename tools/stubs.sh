#!/usr/bin/env bash
# regenerate NotImplemented stubs for icalls not implemented in godot/src/Engine
cd "$(dirname "$0")/.." && .venv/bin/python -I tools/gen_stubs.py godot/src/UnityEngine godot/src/Engine godot/src/Engine/Stubs.g.cs "$@"
