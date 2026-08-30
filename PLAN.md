# A native Linux RA map editor, with an agent-drivable core

Decision record and plan, 2026-08-23. Written at the end of a session that set out to edit the
official RA skirmish maps and instead spent most of its time fighting the mono runtime.

**Status (end of 2026-08-30): spike passed, headless surface real.** `MobiusCore` builds and
renders on net8/Linux; `cncmap` (info / validate / render / save / mods) drives it for RA and
TD; saves are byte-identical to the mono editor on every map tried; unknown entities survive
load/save; installed mods are discovered from the Mods root and the Workshop cache. Next:
the per-mod editor manifest and map/profile binding (names are provisional — Luke to confirm),
then the Avalonia shell with triggers designed in. Repo: `cnc-map-editor/` (this file); the
mono fork `../mobius-editor/` is the reference and oracle.

---

## The decision

Build a **new Linux-native editor that reuses the Windows editor's core**, rather than continuing
to patch mono quirks in the WinForms fork.

Luke's framing, in his words: *"we build a new linux version based on the windows one"*, for
*"greater flexibility"* and a *"new gui, friendlier to use"*, and one that also gives Claude
programmatic access so it can help author campaign missions.

### Why, in one paragraph

The valuable part of the Mobius editor is not its UI. It is the reverse-engineered knowledge:
the LCW codec, the MIX and MEG readers, tileset XML resolution, template and occupancy models,
placement rules, house and team colours. Plus our own layer on top: `TemplateTypesTF`,
`BuildingTypesTF`, the corrected `OverlayTypes`, and the `[TFTDTiles]` round-trip. **All of that
already compiles on net8** (measured 2026-07-16: only ~10 peripheral files excluded, 140 errors,
all of them UI or Steam leakage, and zero in the file-format or map-logic code). The part that is
Windows-locked is the UI shell, which is also the part that keeps breaking on us. Rewrite what is
broken, keep what works.

### The evidence that patching mono is a treadmill

One short session on 2026-08-23 hit three more quirks, all fixed, all the same class:

- `EnhNumericUpDown.CurrentInternalValue` reflected `currentValue`, a .NET Framework field name.
  Mono calls it `dvalue` and has no `currentValueChanged` at all, so the first click on any
  spinner threw. This also killed the PageUp/PageDown brush-size keys.
- Seven unguarded `ToolTip.SetTool` reflection sites. That method does not exist under mono
  either, so each was a crash waiting on a tooltip.
- Tool dialogs that construct at sane on-screen geometry but never map (`IsUnMapped`). Still
  open, see the handover.

That is on top of the July batch (0-dpi bitmap crash, shlwapi natural-sort, user32 no-op guards,
path case-sensitivity). Each fix is small. The tail is long enough that content work keeps
getting interrupted by runtime archaeology.

---

## The one spike that gates everything

**Render a map to PNG on net8 with the imaging layer swapped off `System.Drawing`.**

Nothing else. This is the only genuine unknown. `MapRenderer` is heavy `System.Drawing` / GDI+,
and `System.Drawing.Common` is **Windows-only at runtime** on .NET 7+. That risk sits inside the
core we are counting on reusing, not in the UI we are planning to replace.

- Candidates: SkiaSharp, or KGySoft.Drawing.
- Reference harness: `tools/probes/RenderProbe.cs` already renders a full map headlessly to
  `/tmp/map-render.png` under mono. Port that probe first and compare output.
- **If a map renders correctly, everything after it is grunt work with no unknowns. If it fights
  us, we learn that in a day rather than a week.**

**Pass criterion (agreed 2026-08-30).** Oracle = the mono fork's `RenderProbe` PNG.
- Map layers at scale 1.0 (`MapLayerFlag.MapLayers`): **pixel-identical**, zero tolerance. This
  is where codec, palette, remap and frame-index bugs live.
- Annotation layers (labels, outlines): perceptual tolerance, numbers recorded in the test
  (start ±8/255 per channel, <0.5% differing pixels, tune down). AA fringe passes, a misplaced
  label fails.
- Scaled renders: not compared to GDI+ at all; a Skia-only golden reviewed by eye then locked.
- If a map-layer pixel refuses to match, suspect the oracle as well as the port (the blossom
  tree that never draws is a candidate) and say which it was.

**RESULT (2026-08-30): PASSED.** `MobiusCore/` (the fork's core, copied once and owned) builds
on net8 with a Skia-backed `System.Drawing` shim (`MobiusCore/Drawing/`, ~15 types, only the
surface the core calls). `MobiusCore.Tests/MapRenderOracleTests` renders `scm05ea` (official)
and TF CustomMap 06 (mod tiles + entities) at scale 1.0 and compares against the mono
`RenderProbe` PNGs: **zero pixels anywhere differ by more than 3, and zero pixels outside
partial-alpha sprite coverage differ at all.** 0.17% of pixels differ by ≤3, all under a
partially transparent sprite pixel. 19 tests, ~1 minute.

Findings on the way to green, each of which would have been a silent bug:
- **Pfim version:** the fork pins Pfim 0.10.1; 0.11.2 decodes DXT with different rounding →
  two-thirds of all terrain pixels off by one. The core must pin the same version as any oracle.
- **`Point.GetHashCode` is randomised per process on modern .NET.** `Map.UpdateResourceOverlays`
  seeded `Random` with it to pick ore/gem variants, so variants would change every run.
  Replaced with `DeterministicRandom` (the Framework Knuth generator, pinned by tests) seeded
  from the corefx point hash the mono editor computes (`((X << 5) + X) ^ Y`).
- **libgdiplus is the lossy one.** Its drawn bitmaps live as premultiplied cairo surfaces; a
  brute-force search showed two truncating premultiply/unpremultiply round trips on the sprite
  plus pixman's OVER reproduce 99.3% of the oracle's semi-transparent pixels. Our 1:1 blit uses
  pixman rounding on the untouched source art, so those pixels are more faithful than the
  oracle. Criterion adjusted to bound them (≤3, only under partial alpha) rather than emulate.
- The shim's 1:1 `DrawImage` path is a C# blit (also faster); Skia handles scaled draws, shapes
  and text. `PixelOffsetMode.Half` maps to a −0.5 translate for scaled draws.

Same de-risking pattern as the TS asset spike and the desert theatre spike: prove the one hard
thing in isolation before committing to the surrounding work.

---

## Scope

### Keep (already net8-clean)

`Model/`, `Utility/`, `RedAlert/`, `TiberianDawn/`, `Render/` logic, `Interface/`, plus all file
format I/O. **Do not reimplement the correctness that lives in `Model/`** (bib handling,
occupancy masks, placement rules, house colours). It comes along for free.

### Rewrite

The UI shell. ~90 files in EA's tree (`Dialogs/` 28, `Controls/` 27, `Tools/Dialogs/` 23,
`Tools/` 12), but that is a count of *their* files, not of the functionality we need.

### Triggers and TeamTypes are IN SCOPE from day one

An earlier draft of this plan proposed a first cut without them. **That was wrong and Luke
corrected it.** `gdi-nod-campaign-story.md` is 17 missions (9 GDI "First Light", 8 Nod "Out of
the Shadows"), and `coop-missions-design.md:154` states each needs hand-authored Teamtypes and
Triggers plus briefing and scripted win/lose. Trigger authoring is the single biggest planned use
of this editor. A resources-and-buildings first cut would hit a wall the moment campaign work
starts.

Note this cuts both ways. `TriggersDialog` is the largest and fiddliest thing in EA's UI, and a
half-done trigger editor that silently writes bad triggers is worse than WinForms under Wine.
But EA's trigger UI is also raw event and action rows, and our 17 missions use a narrow,
repeating pattern set: pre-placed enemy base, triggered attack waves, reinforcements,
destroy-all win, briefing. A purpose-built UI could offer **mission-pattern templates** instead
of raw trigger rows. That is the strongest version of the flexibility argument: genuinely better
than EA's, not just the same thing on Linux.

Timing gives us runway. Campaign authoring is deferred behind the AI milestone, so nothing is
blocked today. That is runway, not permission to descope triggers.

---

## Architecture: core library, thin GUI, headless surface

The "Claude support" is not a feature to build. It falls out of this split, and it is worth
designing for explicitly because it also improves the GUI's testability.

```
  core library  (formats, map model, renderer, mod tables, validation)
       |
       +-- GUI          (docked panels, canvas, tools)
       +-- headless CLI (open / query / mutate / validate / render / save)
```

**It already half-works.** Everything verified on 2026-08-23 drove the editor core with no GUI:
loading `scm05ea.ini`, round-tripping it, diffing decoded overlay bytes, probing blossom sprite
frames. What is missing is that these are ad-hoc probe programs rather than a real surface.

**The correctness argument for a shared core.** Decoding that OverlayPack meant hand-rolling LCW
in Python, and `scripts/td_map_to_ra.py` carries yet another implementation of the same codec.
Those are second and third implementations that can drift from what the editor actually writes.
A shared core means an agent touching a map uses the identical codec the GUI uses. This is the
same single-source-of-truth argument as generating the mod tables rather than transcribing them.

**Division of labour for the campaign:** Claude generates the trigger and teamtype scaffolding
from a mission spec; Luke shapes terrain, placement and pacing in the GUI. Same core, same
validation, same file.

**Constraint:** the editor must stay fully useful to Luke solo. Agent access is an addition,
never a dependency.

---

## GUI improvements worth designing in

Each of these is grounded in a real problem hit in practice, not speculative polish.

- **Docked panels, not floating tool dialogs.** EA's tool windows are separate always-on-top
  forms that remember positions across sessions. That design is exactly why the `IsUnMapped` bug
  was possible. A docked panel cannot be unmapped or drift off-screen. Better even on Windows.
- **Drift-proof mod tables.** `OverlayTypes`, `BuildingTypesTF` and `TemplateTypesTF` are
  hand-transcribed from the mod's C++. On 2026-08-23 they were five days stale and would have
  silently written Tiberium as haystacks. **Generate them from `redalert/` at build time, or
  validate against the DLL on load and refuse to open a map on mismatch.** This kills an entire
  bug class and would have prevented today's blocker outright.
- **Tools shaped around the actual workflow.** A one-step "convert ore field to Tiberium"
  (today it is erase-then-repaint, because `AddResource` skips non-empty cells). A blossom-tree
  brush that seeds a field. A "deploy to CCDATA and verify overlay bytes" action.
- **Our renderer, our bugs.** The blossom tree that never draws on the canvas is effectively
  unfixable today because it is buried in someone else's `MapRenderer`.

---

## Mod awareness is generic, not TF-specific (Luke, 2026-08-30)

The editor must work for vanilla RA and TD, for Tiberian Factions, and for other people's mods.
Hardwiring it to `redalert/` C++ was the wrong shape.

**Profiles.** The game loads base install + an ordered list of mod folders; the editor mirrors
that. A profile = game (RA or TD) + zero or more mods, discovered by reading `ccmod.json` under
the `Mods/` root (inside the Proton prefix on Linux) and the Workshop cache
(`steamapps/workshop/content/1213210/`, read-only inspection of other authors' mods). Vanilla
is the empty profile. **The user chooses, in a picker**: which mods to load assets for and in
what order, at startup or from a menu at any time; switching re-resolves the open map (entities
that stop being known become placeholders, kept not deleted). Assets and tables resolve through
the ordered list last-write-wins, exactly as the game does, so the editor shows what that load
order would show in-game. Ordered from day one even if v1 only ever holds one mod.

**Mod manifests, generated at mod build time.** A mod's added entities come from a manifest in
the mod folder (working name `mapeditor.json`: templates, overlays, buildings, units, theatre
slot overrides, custom sections such as `[TFTDTiles]`). TF generates it from the C++ in the
mod's build (`generate_tf_templates.py` already extracts the data; it emits C# today and will
emit the manifest instead). Other authors can hand-write one. The manifest records the hash of
the DLL it was generated from; the editor compares it to the DLL actually present and warns on
drift. This replaces the "generate tables at editor build time" idea above.

**Unknown ≠ delete. Loading is lossless by default.** Today's editor drops unknown structures
silently and then saves without them, which is the data-loss hazard in the handover. The new
core keeps unknown template ids, overlays and objects as opaque entries, renders a marked
placeholder, lists them in a Problems panel, and writes them back byte-for-byte. Stripping them
is an explicit user action. Known entry with missing art gets placeholder art, data intact.

**Maps remember their profile.** A custom INI section (the game ignores unknown sections;
`[TFTDTiles]` proves it) records the mod names + versions the map was authored against. Loading
under a different profile offers: switch profile / load anyway. That turns "loaded with a
different mod in mind" from silent corruption into a one-click fix.

**Where a saved map is usable is a game fact; the editor makes it visible.** The validator
classifies every map as *vanilla-safe* or *needs mod X* by checking entities against the base
tables. Maps saved into `<mod>/CustomMaps/` ship with the mod and exist only when it is loaded;
maps in `Local_Custom_Maps/` appear regardless but degrade or crash without their mod. The save
dialog knows the target kind and warns when a mod-dependent map goes to a non-mod location.

## Licensing

The Mobius source is GPL with EA's Section 7 additional restrictions. Anything reusing that core
inherits it. This blocks nothing (the mod DLL is already GPL v3) but it shapes how the editor
would be published, so decide it up front rather than at release.

---

## Wine: dropped (2026-08-30)

Wine 9.0 is installed but running the prebuilt exe under real .NET Framework has never been
tested, and on 2026-08-30 Luke confirmed the earlier discussion: **build native, do not lean on
Wine.** It would not inform the native build at all, nothing is blocked today that a stopgap
would unblock (campaign authoring is deferred behind the AI milestone), and a working stopgap
would only tempt the native editor to slip. Not part of the plan.

## Order of work for the next session

1. ~~The renderer spike.~~ PASSED 2026-08-30.
2. ~~Scaffold the headless CLI~~ STARTED 2026-08-30: `MobiusCore/Headless/EditorSession` +
   `MobiusCli` (`cncmap info|render|save`, `--game` autodetected from Steam, `--mod` repeatable
   and ordered). Save path is **byte-identical to the mono editor's saves on all 31 CustomMaps**
   (`SaveRoundTripTests`, oracle in `oracle/saves/`). `OfficialMaps.Extract` pulls the official
   skirmish maps out of `MAIN.MIX → general.mix`: **230 files** (130 numeric + 100 letter-coded
   `scmd0`–`scmm9`), not the 124 the display list shows; they join the round-trip oracle
   (`oracle/saves-official/`). Still to add: `validate`, query/mutate commands.
3. Mod profiles + manifest format (contract with the mod repo; provisional names
   `mapeditor.json` / `[MapEditor]`). STARTED: `ModDiscovery` reads `ccmod.json` under a Mods
   root (Proton prefix Documents on Linux) and the Workshop cache; `cncmap mods` lists them in
   load order; `EditorSession` takes the ordered mod list; `cncmap validate` = load errors +
   unknown entries + the plugin's blocking-save check. `EditorSession` is game-aware
   (`--game-type RA|TD`): TD INI+BIN community maps load and render. Not yet: per-mod editor
   manifest, map remembering its profile, vanilla-safe classification, TD lossless retention. ~~Lossless loading of unknown entities~~ DONE 2026-08-30:
   `Map.UnknownEntries` keeps unknown structures/units/infantry/aircraft/ships/terrain/smudge
   verbatim and the RA save writes them back after the known entries (`LosslessLoadTests`);
   `cncmap info` lists them. Not yet covered: `[OVERLAY]` text entries, unknown trigger/team
   references, TD plugin.
4. The Avalonia shell with triggers designed in.

Open shim gaps to close as they are hit: `RotateFlip` rotations, sub-byte indexed writes,
text metrics are approximate (annotation layers only), `Region.Exclude` on infinite regions.

Current blocker if you go back to the existing editor first: tool dialogs never become visible.
Diagnosis and the prime suspect are at the top of `NATIVE_PORT_HANDOVER.md`.
