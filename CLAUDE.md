# cnc-map-editor — Claude context

Linux-native map editor for C&C Remastered (RA + TD), mod-aware. Decision record and status:
`PLAN.md` (read it first). Workspace rules live in `../CLAUDE.md`; the mono-hosted fork at
`../mobius-editor/` is the **reference and oracle**, not a place to develop.

## Layout

```
MobiusCore/          net8 class library: the Mobius editor core (Model/ Utility/ RedAlert/
                     TiberianDawn/ SoleSurvivor/ Render/ Interface/), copied once from the fork
                     and owned here. Drawing/ = Skia-backed System.Drawing shim. Properties/ =
                     settings + embedded resources. Shell hooks: Utility/CoreDiagnostics.cs.
MobiusCli/           `cncmap` console tool over MobiusCore/Headless/EditorSession (info, render, save).
MobiusCli.Tests/     xUnit, drives Cli.Run in-process.
MobiusCore.Tests/    xUnit. EditorHost brings the core up headlessly; MapRenderOracleTests is the
                     renderer oracle; DrawingShimTests pin GDI+ semantics.
oracle/              regen.sh renders reference PNGs with the fork's mono RenderProbe (PNGs are
                     gitignored, 50-70 MB each; regenerate when the fork changes).
artifacts/           test output (gitignored).
```

## Build / test

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet build MobiusCore
env -u DISPLAY dotnet test MobiusCore.Tests --filter Category!=Oracle   # inner loop, seconds
env -u DISPLAY dotnet test MobiusCli.Tests                              # CLI, ~30 s
env -u DISPLAY dotnet test MobiusCore.Tests                              # + oracle tier (~3 min): 2 renders, 31 round trips
dotnet run --project MobiusCli -- info <map> [--mod <dir>]              # the CLI itself
```

Tests read the game from `~/.steam/steam/steamapps/common/CnCRemastered` and the mod build from
`../cnc-ra-tiberian-factions/build/remaster/Vanilla_RA` (override with `CNC_GAME_DIR` /
`CNC_TF_MOD_DIR`, see `TestPaths.cs`). Always run with `DISPLAY` unset: nothing here needs a
display and anything that tries to open one is a bug.

## Rules

- **TDD, red before green.** Write the failing test, watch it fail, then the smallest change.
  Golden/oracle tests against the mono fork's output are the backbone; unit tests pin semantics.
- **Pin dependency versions to the fork's** where the fork is the oracle (Pfim 0.10.1 — a newer
  Pfim decodes DXT differently).
- **Nothing runtime-dependent in map content.** No `GetHashCode` of structs as seeds, no
  `System.Random` for anything that reaches a file; use `DeterministicRandom`.
- The shim implements only what the core calls; extend it when a new call site appears, with a
  test in `DrawingShimTests`. Keep the 1:1 blit path exact (pixman rounding).
- Core must never reference WinForms, Steamworks, or a display. Shell concerns go through
  `CoreDiagnostics` hooks.
- Unknown ≠ delete: loading stays lossless (see PLAN.md, "Mod awareness").
- Licence GPL v3 + EA Section 7; keep upstream attribution. No `Co-Authored-By` trailers.
