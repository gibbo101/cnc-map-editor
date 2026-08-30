# C&C Remastered Map Editor (Linux-native)

A Linux-native map editor for Command & Conquer Remastered (Red Alert and Tiberian Dawn) that
understands mods: vanilla, Tiberian Factions, or any mod that ships an editor manifest.

The core (file formats, codecs, map model, renderer) is derived from Nyerguds' Mobius Map
Editor, itself derived from EA's released editor source. Licence: GPL v3 with EA's Section 7
additional terms — see `LICENSE.txt`. The WinForms shell is replaced, not ported.

See `PLAN.md` for the decision record and `CLAUDE.md` for working conventions.

## Headless CLI

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet run --project MobiusCli -- info <map> [--mod <dir>]...        # describe a map (game, theater, bounds, counts, unknown entries)
dotnet run --project MobiusCli -- validate <map>                      # exit 0 when nothing would be lost (legacy conversions are notes)
dotnet run --project MobiusCli -- render <map> out.png --scale 0.25 --bounds-only
dotnet run --project MobiusCli -- save <map> <out>                    # never overwrites the input
dotnet run --project MobiusCli -- mods                                # installed mods (Mods root + Workshop cache)
```

`--game <dir>` overrides Steam autodetection; `--game-type TD` for Tiberian Dawn (INI + BIN);
`--mod <dir>` may repeat and is applied in order, last wins, like the game.
