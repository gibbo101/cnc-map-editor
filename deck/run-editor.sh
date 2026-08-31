#!/usr/bin/env bash
# Launches the C&C map editor on the Steam Deck (desktop mode).
# The game install is autodetected from the Steam library. If the Tiberian Factions mod
# is deployed on this Deck, it is loaded automatically so TF maps open with their types —
# pass your own --mod to override, or --no-mod for a vanilla session.
cd "$(dirname "$0")"
args=()
skip_auto_mod=false
for a in "$@"; do
    if [ "$a" = "--no-mod" ]; then skip_auto_mod=true; continue; fi
    if [ "$a" = "--mod" ]; then skip_auto_mod=true; fi
    args+=("$a")
done
if [ "$skip_auto_mod" = false ]; then
    tf=$(dirname "$(find "$HOME/.steam/steam/steamapps/compatdata/1213210/pfx/drive_c/users/steamuser/Documents/CnCRemastered/Mods/Red_Alert" -name ccmod.json 2>/dev/null | head -1)")
    if [ -n "$tf" ] && [ "$tf" != "." ]; then
        args+=(--mod "$tf")
    fi
fi
exec ./editor/cncmap-editor "${args[@]}"
