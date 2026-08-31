#!/usr/bin/env bash
# Launch the map editor GUI. Rebuilds when sources changed; arguments pass through
# (a map path, --game <dir>, --mod <dir>). When no --mod is given and the Tiberian
# Factions build exists beside this repo, it is loaded automatically.
set -e
cd "$(dirname "$0")"
export PATH="$HOME/.dotnet:$PATH"
ARGS=("$@")
if [[ ! " $* " =~ " --mod " ]]; then
    TF="$(cd .. && pwd)/cnc-ra-tiberian-factions/build/remaster/Vanilla_RA"
    [[ -d "$TF" ]] && ARGS+=(--mod "$TF")
fi
exec dotnet run --project MobiusEditor.App -c Release -- "${ARGS[@]}"
