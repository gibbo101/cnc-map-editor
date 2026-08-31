#!/usr/bin/env bash
# Publishes the self-contained editor + CLI and deploys them to the Steam Deck.
# Target defaults to Luke's Deck; NEVER point this at deck2 without being told.
# DECK_HOST=deck@<deck-ip> ./deploy-deck.sh   # raw IP when MagicDNS won't resolve
# ./deploy-deck.sh --smoke                       # also run the CLI on the Deck afterwards
set -euo pipefail
cd "$(dirname "$0")"
DECK_HOST="${DECK_HOST:-deck@steamdeck}"
export PATH="$HOME/.dotnet:$PATH"

echo "== publishing (linux-x64, self-contained) =="
dotnet publish MobiusEditor.App -c Release -r linux-x64 --self-contained -o publish/deck/editor -v q
dotnet publish MobiusCli -c Release -r linux-x64 --self-contained -o publish/deck/cli -v q

echo "== deploying to $DECK_HOST =="
ssh "$DECK_HOST" "mkdir -p cnc-map-editor"
rsync -a --delete publish/deck/editor/ "$DECK_HOST:cnc-map-editor/editor/"
rsync -a --delete publish/deck/cli/ "$DECK_HOST:cnc-map-editor/cli/"
scp -q deck/run-editor.sh "$DECK_HOST:cnc-map-editor/run-editor.sh"
ssh "$DECK_HOST" "chmod +x cnc-map-editor/run-editor.sh cnc-map-editor/editor/cncmap-editor cnc-map-editor/cli/cncmap"
echo "deployed; on the Deck (desktop mode): ~/cnc-map-editor/run-editor.sh [map] [--mod <dir>]"

if [ "${1:-}" = "--smoke" ]; then
    echo "== smoke: cncmap info on a map found on the Deck =="
    ssh "$DECK_HOST" '
        map=$(find ~/.steam/steam/steamapps/compatdata/1213210/pfx/drive_c/users/steamuser/Documents/CnCRemastered -iname "*.mpr" 2>/dev/null | head -1)
        if [ -z "$map" ]; then echo "no .mpr found to smoke-test with; skipping"; exit 0; fi
        ~/cnc-map-editor/cli/cncmap info "$map"'
fi
