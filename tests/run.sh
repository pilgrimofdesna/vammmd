#!/bin/sh
# Unity-free tests of the playback core. Needs mono (mcs + mono). Must end with "PASS: N checks, 0 failed".
set -e
cd "$(dirname "$0")/.."
OUT="${TMPDIR:-/tmp}/vammmd-coretests.exe"
mcs -nologo -langversion:6 -warn:4 -warnaserror -out:"$OUT" Custom/Scripts/Usermade/VamMmd/src/Core/*.cs tests/CoreTests.cs
mono "$OUT"
