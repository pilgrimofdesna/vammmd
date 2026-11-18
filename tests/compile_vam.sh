#!/bin/sh
# Compile check of the whole plugin against VaM's own assemblies, the way VaM's plugin loader
# sees it (C# 6, .NET 3.5 class library), with warnings as errors. Then looks for references to
# namespaces VaM refuses to load.
#   VAM_MANAGED=/path/to/VaM_Data/Managed sh tests/compile_vam.sh
set -e
cd "$(dirname "$0")/../Custom/Scripts/Usermade/VamMmd"
: "${VAM_MANAGED:?set VAM_MANAGED to the VaM_Data/Managed folder}"
OUT="${TMPDIR:-/tmp}/vammmd-check.dll"
mcs -nologo -nostdlib -noconfig -target:library -langversion:6 -warn:4 -warnaserror -out:"$OUT" \
    $(ls "$VAM_MANAGED"/*.dll | grep -v /mcs.dll | sed 's/^/-r:/') \
    $(sed 's/\r$//' VamMmd.cslist | grep -v '^$')
# a type directly in System.IO or System.Reflection (System.IO.Ports is a different namespace and allowed)
if monodis --typeref "$OUT" 2>/dev/null | grep -E '\](System\.IO|System\.Reflection|System\.Net|UnityEngine\.Networking)\.[A-Za-z0-9_`]+$|\]System\.AppDomain$|\]UnityEngine\.Network$'; then
    echo "FAIL: the plugin references a namespace VaM refuses (see above)"
    exit 1
fi
echo "PASS: compiles against VaM, no refused namespaces"
