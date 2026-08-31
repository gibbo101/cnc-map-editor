# C&C Remastered Map Editor (Linux-native)

A Linux-native map editor for Command & Conquer Remastered (Red Alert and Tiberian Dawn) that
understands mods: vanilla, Tiberian Factions, or any mod that ships an editor manifest.

The core (file formats, codecs, map model, renderer) is derived from Nyerguds' Mobius Map
Editor, itself derived from EA's released editor source. Licence: GPL v3 with EA's Section 7
additional terms — see `LICENSE.txt`. The WinForms shell is replaced, not ported.

See `PLAN.md` for the decision record and `CLAUDE.md` for working conventions.

Verified against the original editor: the map-layer render matches pixel for pixel (bounded
only by blend rounding under partially transparent sprites), and saving reproduces the original
editor's output byte for byte on all 31 Tiberian Factions maps and all 230 official RA maps.

## Headless CLI

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet run --project MobiusCli -- info <map> [--mod <dir>]...        # describe a map (game, theater, bounds, counts, unknown entries)
dotnet run --project MobiusCli -- validate <map>                      # exit 0 when nothing would be lost (legacy conversions are notes)
dotnet run --project MobiusCli -- render <map> out.png --scale 0.25 --bounds-only
dotnet run --project MobiusCli -- save <map> <out>                    # never overwrites the input
dotnet run --project MobiusCli -- edit <map> --out <path> [--place <tile>@<x>,<y>] [--erase <x>,<y>] [--place-overlay <name>@<x>,<y>]...
dotnet run --project MobiusCli -- expand-mission <map> <spec.json> --out <path>   # scaffold triggers/teamtypes from a pattern spec
dotnet run --project MobiusCli -- mods                                # installed mods (Mods root + Workshop cache)
```

`--game <dir>` overrides Steam autodetection; `--game-type TD` for Tiberian Dawn (INI + BIN);
`--mod <dir>` may repeat and is applied in order, last wins, like the game.

`expand-mission` reads a JSON spec (Newtonsoft, `//` comments allowed) holding a flat list of
patterns — `win`, `lose`, `reinforce`, `attack-wave`, plus a `raw` escape hatch of literal
`[Trigs]`/`[TeamTypes]` rows in the game's own encoding. Times are the game's TIME units
(tenths of a minute). Expansion is all-or-nothing scaffolding: tweak the result in the editor
afterwards. Red Alert only.

## GUI

```bash
dotnet run --project MobiusEditor.App -- [map] [--game <dir>] [--mod <dir>]...
```

Tool tabs (Tiles / Terrain / Overlay / Buildings / Units / Infantry / Smudge / Cell trig. /
Waypoints) with one brush active at a time — left-click paints, right-click erases what that
brush would paint. Objects place with the toolbar's House. A left click with **no** brush
selects the object under the cell for the properties panel (house, strength, direction, order,
trigger, and the building base/prebuilt/sellable/rebuild extras, with the original editor's
eligibility rules); holding that click and releasing elsewhere moves the object. Triggers…
and Teams… open the scripting dialogs — working-copy editing, committed on OK as a single
undo step, with a trigger filter box and an in-dialog Check run.

## Steam Deck

`./deploy-deck.sh [--smoke]` publishes self-contained linux-x64 builds (no .NET needed on the
Deck) and deploys them over Tailscale. On the Deck, desktop mode:
`~/cnc-map-editor/run-editor.sh` — the game install is autodetected and a deployed Tiberian
Factions mod is loaded automatically (`--no-mod` opts out).
