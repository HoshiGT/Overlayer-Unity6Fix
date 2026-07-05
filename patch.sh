#!/bin/bash
# Full pipeline: build compat shim -> build patcher -> patch Overlayer.dll.
#
# Prerequisites:
#   - mono + mcs (patcher build/run), dotnet SDK (compat shim build)
#   - Mono.Cecil.dll (net40, from the NuGet package) next to Patcher/OverlayerPatcher.cs
#   - orig/Overlayer.dll  = untouched Overlayer 3.49.0 (with its lib/ next to it if you
#     point ORIG elsewhere; the resolver looks in <orig dir>/lib for Jint etc.)
#
# Env overrides:
#   GAME_MANAGED  path to ADOFAI's Managed directory
#   ORIG          input Overlayer.dll   (default: orig/Overlayer.dll)
#   OUT           output patched dll    (default: Overlayer.patched.dll)
set -e

cd "$(dirname "$0")"

GAME_MANAGED="${GAME_MANAGED:-$HOME/adofaimods/A Dance of Fire and Ice/A Dance of Fire and Ice_Data/Managed}"
ORIG="${ORIG:-orig/Overlayer.dll}"
OUT="${OUT:-Overlayer.patched.dll}"
CECIL="${CECIL:-Patcher/Mono.Cecil.dll}"

[ -f "$CECIL" ] || { echo "Mono.Cecil.dll not found at $CECIL — grab lib/net40/Mono.Cecil.dll from the Mono.Cecil NuGet package" >&2; exit 1; }
[ -f "$ORIG" ] || { echo "original Overlayer.dll not found at $ORIG" >&2; exit 1; }

echo "=== 1/3 compat shim ==="
GAME_MANAGED="$GAME_MANAGED" bash Compat/build.sh

echo "=== 2/3 patcher ==="
mcs -r:"$CECIL" Patcher/OverlayerPatcher.cs -out:Patcher/OverlayerPatcher.exe

echo "=== 3/3 patch ==="
MONO_PATH="$(dirname "$CECIL")" mono Patcher/OverlayerPatcher.exe \
  "$ORIG" "$OUT" Compat/bin/Overlayer.Unity6Compat.dll "$GAME_MANAGED"

echo
echo "Done. Install:"
echo "  $OUT                         -> Mods/Overlayer/Overlayer.dll"
echo "  Compat/bin/Overlayer.Unity6Compat.dll -> Mods/Overlayer/lib/"
