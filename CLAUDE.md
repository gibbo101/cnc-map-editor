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
MobiusCli/           `cncmap` console tool over MobiusCore/Headless/EditorSession (info, validate, render, save, mods).
MobiusCli.Tests/     xUnit, drives Cli.Run in-process.
MobiusEditor.Shell/  GUI view-models with no toolkit types (MapDocument); tested headless in MobiusEditor.Shell.Tests.
MobiusEditor.App/    Avalonia 12 window over the Shell. `dotnet run --project MobiusEditor.App -- [map] [--mod <dir>]`.
cnc-map-editor.slnx  solution for Rider.
MobiusCore.Tests/    xUnit. EditorHost brings the core up headlessly; MapRenderOracleTests is the
                     renderer oracle; DrawingShimTests pin GDI+ semantics.
oracle/              regen.sh generates every oracle fixture with the fork's mono probes: the
                     reference PNGs (50-70 MB each) and the saves/ + saves-official/ reference
                     saves. ALL fixtures are gitignored — the saves derive from EA map data, so
                     they must never be committed; each machine regenerates them from its own
                     game install (and again whenever the fork changes).
artifacts/           test output (gitignored).
docs/                format contracts. mapeditor-json.md = the per-mod type manifest a mod
                     ships beside ccmod.json (TF generates its own with the mod repo's
                     scripts/editor_manifest.py); nothing mod-specific is compiled in.
```

## Build / test

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet build MobiusCore
env -u DISPLAY dotnet test MobiusCore.Tests --filter Category!=Oracle   # inner loop, seconds
env -u DISPLAY dotnet test MobiusCli.Tests                              # CLI, ~1 min
env -u DISPLAY dotnet test MobiusEditor.Shell.Tests                     # GUI view-models, seconds
env -u DISPLAY dotnet test MobiusEditor.App.Tests                       # Avalonia headless UI, seconds
env -u DISPLAY dotnet test MobiusCore.Tests                              # + oracle tier (~11 min): renders + 261 byte-exact round trips
dotnet run --project MobiusCli -- info <map> [--mod <dir>]              # the CLI itself
```

Tests read the game from `~/.steam/steam/steamapps/common/CnCRemastered` and the mod build from
`../cnc-ra-tiberian-factions/build/remaster/Vanilla_RA` (override with `CNC_GAME_DIR` /
`CNC_TF_MOD_DIR`, see `TestPaths.cs`). Always run with `DISPLAY` unset: nothing here needs a
display and anything that tries to open one is a bug.

- **Background `dotnet test` runs get killed on this machine** (observed 3/3 on 2026-09-02;
  cause unknown). Run tiers in the foreground in <10-minute pieces. The oracle tier splits by
  class: `OfficialMapsTests|TransitionMinerTests` (~9 min), `SaveRoundTripTests` (~14 min —
  the one chunk that must go long), `MapRenderOracleTests` (~1 min).
- **Never run two `dotnet test` invocations of the same project concurrently** — they race on
  build outputs and `artifacts/test-output/`, producing phantom failures that vanish on rerun.
- The mined corpus (`MobiusCore/Resources/mined-transitions.txt`) regenerates via the
  oracle-tier miner test, then copy `artifacts/test-output/mined-transitions.txt` over the
  resource, keeping the 3-line header. Its family list lives in `TransitionMinerTests` — a
  family missing there (how fords/bridges went unmined until 2026-09-02) silently reads as
  "the corpus has none".

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
  `CoreDiagnostics` hooks. GUI logic lives in `MobiusEditor.Shell` (no Avalonia types) so it is
  tested headless; `MobiusEditor.App` views stay thin. Don't launch the app on Luke's desktop to
  check it — use the Avalonia headless tests.
- Unknown ≠ delete: loading stays lossless (see PLAN.md, "Mod awareness").
- Licence GPL v3 + EA Section 7; keep upstream attribution. No `Co-Authored-By` trailers.
