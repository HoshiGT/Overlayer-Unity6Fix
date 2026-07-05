#!/bin/bash
# Builds Overlayer.Unity6Compat.dll (assembly name is fixed — the patcher rewrites
# Overlayer.dll references against it).
#
# Env overrides:
#   GAME_MANAGED  path to ADOFAI's Managed directory
#   MONO_LIB      path to mono 4.5 profile (for mscorlib.dll)
#   CSC           path to Roslyn csc.dll (run via `dotnet exec`)
set -e

cd "$(dirname "$0")"

GAME_MANAGED="${GAME_MANAGED:-$HOME/adofaimods/A Dance of Fire and Ice/A Dance of Fire and Ice_Data/Managed}"
MONO_LIB="${MONO_LIB:-/usr/lib/mono/4.5}"
DOTNET="${DOTNET:-$HOME/.dotnet}"
if [ -z "$CSC" ]; then
  CSC="$(ls -d "$DOTNET"/sdk/*/Roslyn/bincore/csc.dll 2>/dev/null | sort -V | tail -1)"
fi
export PATH="$DOTNET:$PATH"

OUTPUT="bin/Overlayer.Unity6Compat.dll"
mkdir -p bin

REFS=(
  "$MONO_LIB/mscorlib.dll"
  "$GAME_MANAGED/netstandard.dll"
  "$GAME_MANAGED/System.dll"
  "$GAME_MANAGED/System.Core.dll"
  "$GAME_MANAGED/UnityEngine.dll"
  "$GAME_MANAGED/UnityEngine.CoreModule.dll"
  "$GAME_MANAGED/UnityEngine.TextRenderingModule.dll"
  "$GAME_MANAGED/UnityEngine.UI.dll"
  "$GAME_MANAGED/UnityEngine.UIModule.dll"
  "$GAME_MANAGED/Unity.TextMeshPro.dll"
  "$GAME_MANAGED/Assembly-CSharp.dll"
  "$GAME_MANAGED/Assembly-CSharp-firstpass.dll"
  "$GAME_MANAGED/RDTools.dll"
  "$GAME_MANAGED/UnityFileDialog.dll"
  "$GAME_MANAGED/UnityModManager/0Harmony.dll"
)

RSP_FILE="build.rsp"
{
  echo "-nostdlib+"
  echo "-langversion:preview"
  echo "-target:library"
  echo "-out:$OUTPUT"
  echo "-optimize+"
  echo "-nowarn:CS1701,CS1702,CS0436"
  for ref in "${REFS[@]}"; do
    if [ -f "$ref" ]; then
      echo "-r:\"$ref\""
    else
      echo "MISSING REF: $ref" >&2
    fi
  done
  echo "\"Compat.cs\""
} > "$RSP_FILE"

dotnet exec "$CSC" "@$RSP_FILE" 2>&1

echo "=== Build SUCCESS ==="
ls -la "$OUTPUT"
