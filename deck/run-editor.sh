#!/usr/bin/env bash
# Launches the C&C map editor on the Steam Deck (desktop mode).
# The game install is autodetected from the Steam library; pass --game <dir> if that
# fails, and --mod <dir> to load a mod's types (e.g. the deployed Tiberian Factions
# folder under .../CnCRemastered/Mods/Red_Alert/Vanilla_RA).
cd "$(dirname "$0")"
exec ./editor/cncmap-editor "$@"
