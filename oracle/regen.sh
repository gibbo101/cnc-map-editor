#!/usr/bin/env bash
# Regenerate the reference renders from the mono fork's RenderProbe (the oracle for the
# renderer tests). Full-size PNGs are gitignored; the committed goldens are crops of these.
set -e
FORK=${FORK:-$HOME/Documents/development/cnc-remastered-mods/mobius-editor}
REL=$FORK/CnCTDRAMapEditor/bin/Release
OUT=$(cd "$(dirname "$0")" && pwd)
MAPS=$HOME/Documents/development/cnc-remastered-mods/map-edits
TF=$HOME/Documents/development/cnc-remastered-mods/cnc-ra-tiberian-factions/build/remaster/Vanilla_RA/CustomMaps
mono-csc -nologo -r:$REL/MobiusMapEditor.exe -r:System.Drawing.dll -out:/tmp/renderprobe.exe $FORK/tools/probes/RenderProbe.cs
cd $REL
run() { env -u DISPLAY MONO_PATH=$REL:$REL/bin mono /tmp/renderprobe.exe "$1" "$OUT/$2" "${3:-1.0}" | tail -1; }
run $MAPS/scm05ea.ini scm05ea-maplayers-1.0.png
run $TF/UGC_F1BE000000000006_0000000000000006_MAPDATA.MPR tf-map06-maplayers-1.0.png
# Reference saves: every mod CustomMap loaded and saved by the mono editor (byte-exact oracle
# for the new core's save path). Produced by the fork's sweep into /tmp/sweep-out.
env -u DISPLAY $FORK/tools/probes/sweep_maps.sh | tail -1
mkdir -p "$OUT/saves" && cp /tmp/sweep-out/*.MPR "$OUT/saves/"
