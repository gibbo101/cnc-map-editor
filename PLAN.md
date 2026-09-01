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
destroy-all win, briefing. **DECIDED (Luke, 2026-08-30 evening): templates are generators over
real raw rows, never a replacement.** The foundation is a complete raw trigger/teamtype model,
byte-round-trip tested against real maps; mission-pattern templates expand into ordinary raw
triggers the user can still open, tweak or delete. Generators are pure functions (spec in →
trigger lines out): headless-testable and CLI-drivable, which is how campaign scaffolding gets
generated from a mission spec. Raw rows stay visible in an advanced view by choice.

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

## RESUME HERE (2026-09-01 continued — coast walker + resource flavors SHIPPED)

**⭐ THE COAST WALKER IS REAL (4c5e604, pushed):** Luke's "train on existing maps" idea
works end-to-end. Corpus baked into `MobiusCore/Resources/mined-transitions.txt` (14,138
idioms, 6 families, 2,119 cliff↔beach splices; regenerate via the oracle-tier miner test).
`TransitionGraph` (reverse edges, frequency-sorted) + `CoastWalker.PlanPath` (deterministic
A*, common idioms cheaper, water-side lock ±45°, edge-sharing seal) + `ExtendToEdge`,
mask-seal by growth-from-water, 3-seed flood, `PatchBareContacts` (sh55 shallow tile),
`ExtendWaterIntoBorder` (terrain continues under the map frame — Luke spotted it).
**Ocean style now walks its coast** (verified temperate+snow, both orientations —
showcase-quality natural shorelines); block shoreline = fallback (TD). Strict invariant
pinned: ZERO flood-water cells touching bare clear land. KEY LESSON: authored pieces paint
their own internal grass-water transitions — the seam invariant must count only
FILL-water (w1/w2) contacts, not piece-internal ones.

**TIBERIUM DIAL (3e8318a + mod repo 9c6cbfc5):** manifest `resources` section (flavor/
overlays/spawner) — TF declares tiberium/tib01/tdblossom; generator fields: ore seeds an
ORE MINE, tiberium a BLOSSOM TREE (Luke's ecology rule); `--tiberium 0..1` share + GUI
slider. Patches avoid building cells.

**WALKER NEXT (same machinery, in rough order):** closed-loop lakes (segment ring, strict
closure = final piece has a mined transition back to the start piece), meandering river
banks (two walked coasts), island coasts, landing-beach splices on cliff coasts (Luke's
amphibious rule: cliff coasts MUST carry sh landing beaches — splice idioms are mined,
make count/spacing a tunable), cliff-vs-beach coast flavor tunable, then the rf land-cliff
family (cliffs setting) and mined road curves.

## Previous block (2026-09-01, daytime session — the generator terrain arc)

**TERRAIN SUITE SHIPPED (b871bd2, pushed):** the generator grew real water, villages and
roads, all seeded/deterministic/tunable (CLI flags + New-dialog controls):
- **Shore machinery:** `ShoreCatalog` classifies RA's 58 sh pieces from their per-icon
  LandTypes (quadrant dominance → Straight/Diagonal per water side; the game's own
  equivalence groups override ragged variants; Rock counts as land). `LakeBuilder` draws
  with 3x3 blocks: straights on edges, land-dominant (convex) diagonals as lake corner
  cuts, water-dominant (concave) as island corners; w2 patches scattered through w1 fill
  (official-map idiom), pure-water variants preferred near open sea.
- **Styles:** lakes (showcase-quality: beach ring, ragged waterline, rock outcrops, no
  seams), river (straight strait + ford crossings — fords are `ford1/2` templates whose
  road strip crosses the waterline), ocean (edge band + continuous shoreline), islands
  (BETA: flood bounds, carve same-sized islands in a grid, serpentine chain of
  punched-doorway causeways; small corner blemishes remain).
- **Villages + roads:** `SettlementBuilder` — neutral `vNN` building clusters; roads as
  d07/d08 + d11/d12 runs with REAL tileset bends (d14/d16/d22/d31) stamped over elbows.
  Junction render verified — the track curves continuously.
- **Tunables:** water dial + `--water-style none|lakes|river|ocean|islands`, plus
  `--lakes --islands --causeways --river-width --fords --ocean-depth --ocean-edge
  --villages --roads`; GUI has style combo + water slider + villages/roads.
- Land-awareness everywhere: starts/ore/trees/villages sit on Clear ground only; starts
  fall back to the nearest buildable cell (islands maps).

**⭐ NEXT ARC — THE TRANSITION-MINED COAST WALKER (Luke's "train on existing maps" idea):**
the 3x3 block approach cannot do watery quarter-turns (the sh set lacks them in 2 of 4
quadrants — root cause of the island corner blemishes and why the river is straight, not
meandering). Ground truth from scm03ea shows hand maps chain the LARGER diagonal pieces at
cell offsets (sh52→sh03→sh49 etc.). `TransitionMiner` (shipped) reconstructs piece
instances from any map's template grid and accumulates (from,to,offset,count) transitions;
`TransitionMinerTests.MinesShoreIdiomsFromTheOfficialCorpus` (oracle-tier) mines all 230
official maps. **FIRST CORPUS RUN (2026-09-01): 214/230 maps contribute, 8,404 distinct
transitions.** KEY FINDING: official shores are dominated by the `wc` WATER-CLIFF family
(rocky coastlines — top idiom wc02→wc03 @2,1 ×149; dense 2-cell chains), not the sandy
`sh` beaches (which chain too: sh53→sh54 @0,3 ×89 vertical runs). The walker should learn
per-family sub-graphs (wc coasts, sh beaches, rv rivers, d roads) and offer coast
flavor (cliff vs beach) as a tunable. BUILD NEXT: a coast walker that plans closed loops/paths through the mined
transition graph (A* over piece+position states) → organic lakes, meandering rivers,
proper island corners, and then the cliffs setting Luke asked for (rf rock-face family,
same mined-transition treatment) plus richer road curves.

**Deck deployed this morning** (08:26, retry loop over hotspot; smoke-tested incl.
`generate` on-Deck). The terrain suite is NOT yet deployed to the Deck — redeploy
(`DECK_HOST=deck@<deck-ip> ./deploy-deck.sh`) when Luke wants it there.

## Previous resume block (2026-09-01, overnight session)

**WALKER CHANGE FULLY VERIFIED:** the b8dbe89 walk_frames change is green on every tier —
CLI 23, App, Shell, fast Core 108, full oracle 372/372 (16 min). The pending-verification
note from the previous session-end is closed.

**LUKE'S UI FEEDBACK ROUND (before bed) — SHIPPED (8c8be06):**
- **Palette search box** (`PaletteSearch` above the tool tabs): narrows every palette at
  once, matching display name OR INI name (`PaletteFilter` in Shell — headless-tested;
  `PaletteItem` grew `IniName`). Group headers survive only with entries; a surviving
  selection keeps its brush. TRAP FIXED with it: `SelectedWaypoint` was the ListBox
  index — filtered lists broke the mapping; it now parses the row's leading number.
- **Scrollable top tab strip**: the nine tool tabs sit in one horizontally scrollable
  strip (TabControl retemplated: ScrollViewer + horizontal StackPanel items panel,
  compact TabItem style) instead of wrapping into a block in the 220px sidebar.
- Luke also 100%-approved the interaction scheme (right-click deselect / eraser toggle /
  wheel zoom / WASD pan) — treat it as settled, extend rather than rework.

**⭐ REAL BUG FOUND: `plugin.Initialize()` was never called.** The fork's LoadNewPlugin
calls it between CreatePlugin and map load; our EditorSession skipped it, so the base
RULES.INI stat layers and the team-color XML never loaded — every unit/building rendered
UNREMAPPED (no house colors) on desktop and Deck since day one. The oracle never caught it
because our own probes (RenderProbe/RoundTripProbe/ObjectProbe in the fork's tools/probes)
skip Initialize the same way. Fix: `EditorSession.New/Load` now run Initialize (+
`Map.FlagColors = GetFlagColors()`, fork order) and surface its errors; all three probes
patched to match; oracle fixtures REGENERATED (renders + saves + saves-official; the
fork sweep round-trips all 31 shipped maps byte-exact WITH Initialize, and our 261-map
save oracle stays byte-identical). Found via TDD on the ghost preview:
`BrushPreviewTests.UnitPreviewRemapsToThePlacementHouse` (Greece vs USSR pixels) was the
red test. Render-oracle blend bound widened 3→5 (team-color remap adds a truncating
premultiply pass on the mono side; the strict outside-partial-alpha invariant still
exact). **SESSION-END: EVERY tier green on the committed tree — Core+oracle 378/378,
Shell 83, App 31, CLI 24.** Commits: 8c8be06 search+tabs, c9cc6a9 Initialize, 22ed7cd
generator, 2fb2e97 ghost+dialog; fork repo 77fc58a probes. main is 56 ahead of origin —
pushing has not been part of this repo's flow, left for Luke.

**GHOST POLISH SHIPPED:** the placement ghost is now a live render —
`MapDocument.RenderBrushPreview(type)` renders the brush type at the current tile size
through the renderer's own preview path (`IsPreview` alpha, placement-house remap;
buildings/units/infantry/terrain/overlay/smudge; templates return null — their thumbnails
are already exact). The App caches one preview per (type, house, scale); thumbnail
fallback keeps the old dimmed-stretch look. `BrushPreviewTests` + updated ghost UI test.

**RANDOM MAP GENERATOR SHIPPED (Luke's stretch-goal request):**
`Headless/MapGenerator.cs` — deterministic skirmish synthesis: best-candidate-spaced
player starts, value-noise tree cover (budgeted under `GameInfo.MaxTerrain`, clump cores
fill first so capped budgets give solid forests), an ore field beside every start, gem
patches between neighbours; vanilla types only, placed through the brush guard paths.
`cncmap generate <out> [--theater] [--seed] [--players] [--trees 0..1] [--ore 0..1]`;
GUI: New… dialog "Random skirmish fill" checkbox (seed/players/trees/ore) →
`MapDocument.NewRandomMap` (fill is starting state — undo history empty).
`MapGeneratorTests` (5: byte-identical same seed, spacing/bounds, density dials, validate+
save+reload, player clamp) + CLI + headless dialog tests. Verified visually by rendering a
generated map. NOT YET: water/lakes (needs the shore-tile matching problem — deliberate
v1 cut), TD generate (RA-shaped resource names work, TD untested).

**Deck is STALE tonight by Luke's instruction** ("overnight, work on linux; tomorrow
morning I'll wake deck again for the latest build") — when the Deck is up in the morning:
md5-check the adjacent DLL, push `build/remaster/Vanilla_RA/mapeditor.json` if the mod
session's manifest drifted, and `DECK_HOST=deck@<deck-ip> ./deploy-deck.sh`.

Blossom-tree mature-frame thumbnail: WON'T DO — `FrameOffset` drives map rendering too,
and frame 0 (young tree) is what the game shows on placement; a thumbnail-only frame
would need a new manifest field for a cosmetic nicety.

## Previous resume block (2026-08-31, end of the trigger-arc session)

**The trigger arc SHIPPED steps 1–3 (2026-08-31 overnight, all committed on main):**
1. ~~The structured event/action parameter-type table~~ DONE: `TriggerArgType` enum
   (`Model/TriggerArg.cs`) + per-game tables (`RedAlert/TriggerArgTypes.cs`,
   `TiberianDawn/TriggerArgTypes.cs`, SS inherits TD), exposed through six new IGamePlugin
   members (GetEventArgType / GetActionArgType / GetArgOptions / GetArgRange /
   CoerceEventArg / CoerceActionArg). Full 33-event/37-action RA mapping pinned in
   `TriggerArgTypesTests` against the fork's dialog switches. Coercion asymmetries pinned
   deliberately: RA no-arg EVENTS blank Data+Team, no-arg ACTIONS leave them; TD no-arg
   events blank Data only; matched team refs take the list's casing, trigger refs keep
   their own.
2. ~~The mission-pattern generators~~ DONE: `Headless/MissionSpec.cs` (Newtonsoft, //
   comments OK, flat pattern list) + `Headless/MissionExpander.cs` → `cncmap
   expand-mission <map> <spec.json> --out <path>`. v1 patterns win/lose/reinforce/
   attack-wave (times in game TIME units = tenths of a minute) + `raw` rows parsed by
   `GamePluginRA.ParseRawScriptRows` (reuses the private load path; index refs resolve
   within the pattern's own rows). All-or-nothing: build errors or CheckTriggers fatals →
   nothing committed, CLI writes nothing. Grounded against scu01ea.ini (extracted from
   MAIN.MIX → general.mix — the workspace's ra_mix_extract.py can NOT read the Remastered
   MAIN.MIX; our own MixFile can).
3. ~~Trigger GUI panel~~ DONE: `MapDocument.BeginTriggerEdit`/`BeginTeamTypeEdit`
   interactive sessions (working copy open across a dialog's life, Commit = one undo
   step, Cancel = no trace; EditTriggers/EditTeamTypes now wrap them),
   `Shell/TriggerArgPresenter` (headless Update*Controls: coerce then say
   None/Number/DataList/NameList + options/range/value), and Avalonia `TriggersWindow` +
   `TeamTypesWindow` off the toolbar. Event 2 hides+resets outside multi-event styles.
   Teamtype dialog first pass: flags/numbers/house/trigger link + plain class & order
   rows; the ORDER ARGUMENT is a raw number with an ArgType hint line — a per-ArgType
   value control (waypoint list, options dropdown) is the known polish gap.

4. ~~Docked tool panels + dirty-cell rendering~~ BOTH DONE (same session): `Render()` keeps
   a cached bitmap and repaints only touched cells via the renderer's location-set (dirty
   set expanded 1 cell — Overlay self-heals neighbors unjournaled); invariant pinned:
   incremental render == full render pixel-exact (`DirtyCellRenderTests`). Left dock is now
   tool TABS (Terrain / Overlay / Cell trig. / Waypoints); routing stays selection-based,
   one brush across all four palettes. Cell-trigger brush (fork guards: empty cell only,
   cell-eligible triggers only, stroke-batched) + waypoint brush (one undo step per move,
   click-only placement) ship with their indicator layers auto-toggled. NOTE:
   `CellGrid[a,b]` is **[y,x]** — use the Point indexer in tests.
5. ~~Teamtype order-argument value controls~~ DONE: `TriggerArgPresenter.ForTeamMission`
   (fork's MissionItemControl switch); the dialog's order row shows waypoint list /
   options dropdown / ranged spinner per `TeamMissionArgType`.

**OBJECT TOOLS ALSO SHIPPED (same session, commits 6ccf114 → 3b81a70): every fork tool
family now has a brush.** Terrain (trees/rocks through the occupier set; occupy masks
needn't claim the origin cell — erase from an OCCUPIED cell), units (fork defaults:
toolbar placement house, 256 strength, north, default mission), infantry (InfantryGroup
of five stops, one man per placement, group appears with the first man and leaves with
the last), buildings (map auto-manages bibs; eaten hand-placed smudge and wall-overlay
restored on undo; BasePriority -1 — rebuild-base plumbing not started), and smudge
(`MobiusCore/Model/SmudgeEdit.cs`, full fork port incl. multi-cell bibs + attached-bib
protection + RestoreNearbySmudge). All placements undoable, all pinned to keep
incremental rendering pixel-identical, and `PlacedObjectSaveTests` pins that
editor-CREATED objects survive save/reload. Tool tabs: Tiles / Terrain / Overlay /
Buildings / Units / Infantry / Smudge / Cell trig. / Waypoints + a toolbar House combo.

**PROPERTY EDITING + POLISH SHIPPED (2026-08-31 morning, commits a6b191c → 969a5a6):**
- ~~Object property editing~~ DONE: brushless left click selects the object under the cell
  (sub-cell aware for infantry); a docked properties panel edits it with the fork's
  ObjectProperties rules (`Shell/ObjectPropertiesPresenter` — aircraft never get triggers,
  buildings gate house/strength/direction/trigger behind IsPrebuilt, turret-only direction,
  RA extras with NormalizeBuilding invariants). One undo step per change via CloneDataFrom
  snapshots; the panel drops the selection when undo retires the object.
- ~~Sub-cell infantry stops~~ DONE: place/erase/select use `ClosestStoppingTypes` over the
  pointer's 24x24 in-cell position.
- ~~Trigger dialog polish~~ DONE: Check button (CheckTriggers over the working list,
  results in-dialog), filter box (name/house/event/action match), teamtype order rows
  update in place.

**DRAG-MOVE + BASE PRIORITIES + DECK DEPLOY SHIPPED (2026-08-31 while Luke away,
commits bc9bbd3 → 1309629):**
- ~~Drag-moving placed objects~~ DONE: hold the brushless selection click, release on the
  target cell — one undo step, blocked moves change nothing, buildings carry bibs and give
  back eaten smudge, infantry land in the pointer-closest stop.
- ~~Rebuild-base priority editor~~ DONE: fork's AdjustBuildPriorities ported — base
  priorities stay consecutive 0..n-1 through edits, base-leaves and erases; undo restores
  every affected building's priority via a before/after priority map on the undo step.
- ~~Per-control tooltips~~ RESOLVED N/A: the fork's OWN RA plugin returns null from
  TriggerEventInfo/TriggerActionInfo (only TD has the description tables) — our null stubs
  are fork-faithful; RA's useful hover content is TriggerSummary, already shown inline.
- **STEAM DECK DEPLOYED (2026-08-31): `deploy-deck.sh`** publishes self-contained linux-x64
  editor+CLI and rsyncs to Luke's Deck (`~/cnc-map-editor/`, desktop-mode
  `run-editor.sh` auto-loads the deployed TF mod, `--no-mod` opts out). Verified ON the
  Deck: deployed CLI loaded a TF CustomMaps .mpr with the deployed mod's manifest. Deploy
  is CURRENT as of accddb2 (drag-move, base priorities, New maps) — REDEPLOY
  (`DECK_HOST=deck@<deck-ip> ./deploy-deck.sh`) after further changes; the Deck comes
  online intermittently. **GAME MODE (Luke's request, 2026-08-31): "C&C Map Editor" is a
  Non-Steam Game on Luke's Deck user (<steam-id>)** — added via `steamos-add-to-steam` on a
  `.desktop` entry (`~/.local/share/applications/cnc-map-editor.desktop`) because that
  registers through the LIVE client: editing shortcuts.vdf directly requires a Steam
  restart and is clobbered if Steam is running (a game was in progress at the time).

**NEW MAPS SHIPPED (accddb2):** `EditorSession.New` + GUI New… theater picker + `cncmap
new <out> [--theater X]` (scaffolds 2 player-start waypoints — the game REFUSES to save a
skirmish map without them, and plugin.Save writes NOTHING on validation failure, which
`MapDocument.Save` now surfaces as an error instead).

**MAP SETTINGS SHIPPED (ee183f4):** Settings… dialog edits [Basic] + briefing as one undo
step (`MapDocument.EditMapSettings`); the SoloMission flag flips the save rules (Home
waypoint instead of 2 player starts — pinned). THEATER FACTS: the core defines 8 RA
theaters but only Temperate/Snow/Interior are real — Winter/Desert/Jungle/Barren/Cave are
`IsModTheater` CnCNet extras with no Remastered art (TD: Desert/Temperate/Winter real;
Jungle/Snow/Caribbean extras). The New dialog filters to `!IsModTheater`. Deck deploy
current as of ee183f4.

**DECK LAG FIX (b6d569e, after Luke reported "really slow and laggy"):** every op was
raw-copying the whole map bitmap twice + allocating a fresh GPU frame (2×64MB + 4096px
texture upload per painted cell at the old fixed 25% zoom). Now: `MapDocument.
UpdateRenderCache(out dirtyPixels)` exposes the cache + changed region; the window keeps
ONE reused WriteableBitmap, copies only that region, InvalidateVisual; fresh maps open
FITTED to the viewport (Deck = 1/16 scale, 16× smaller surface), first render deferred
until layout. **VERIFIED LIVE ON THE DECK IN GAME MODE** (Luke authorized remote runs):
launched via `steam steam://rungameid/13165712167015546880` (shortcut appid 3065381238),
screenshot via `gamescopectl screenshot <path>` — TF map rendered fitted at 6.3%, palettes
+ status live. REMAINING KNOWN SLOWNESS: session startup ≈15-20s (MEG/archive load, same
cost as CLI) — a splash/async load is the follow-up if it bothers.
TRAP: `pgrep -f "reaper SteamLaunch"` over ssh matches its own command line — use
`pgrep -af` and eyeball, or exact names.

~~Async session startup~~ DONE (03c9e95): session + map loads run off the UI thread behind
status lines; the window paints immediately (verified live: fully interactive UI 4s after
Game Mode launch). Deck deploy current as of 03c9e95.

**FIRST UI FEEDBACK ROUND (Luke, from the Deck, 2026-08-31 evening — commits accddb2…8d2cd83):**
Luke: New→Snow froze a long time (→ New now creates on a background thread; first use of
a theater loads its tileset, that was the cost); "the rest feels snappier already" (lag
fix confirmed on-device); palettes were bare INI names with no preview and placement was
blind (→ palettes now show each type's rendered thumbnail + display name via
`Shell/PaletteItem` + `App/PaletteEntry`; templates label as `code (W×H)`; hovering with
a brush shows a footprint-sized ghost of the selected type snapped to the hovered cell,
plain highlight box for cell-trigger/waypoint brushes). Palette thumbnails verified by
Deck screenshot. Deck deploy current as of 8d2cd83.

**LIVE FEEDBACK ROUNDS 2-4 (2026-08-31 late evening, Luke testing on desktop via
editor.sh — commits bcf804d, 38ba086; crash fix 071dbae):**
- 100% zoom CRASHED the Deck (SIGABRT in journal: 16384² = 1 GB bitmap) → **viewport
  rendering** past a 4096² surface budget: `MapDocument.RenderBlock(cellBlock)` renders
  only the cells around the viewport (pixel-identical to the full render's crop within 1
  channel unit of Skia transform rounding — tolerance-1 in the test), positioned inside a
  full-size panel so scrollbars span the map; pointer handling moved to the panel
  (map-absolute coords in both modes).
- Interaction redesign per Luke: RIGHT CLICK DESELECTS (brush, eraser, selection);
  erase = toolbar **Eraser toggle** + left click/drag (brush picks the category, active
  tab when no brush; red ghost box). Wheel zooms without modifier, pivoting on the
  viewport center; WASD/arrows pan (unless an input control has focus); grid splitters
  resize both sidebars; playable-bounds cyan indicator (map edge ≠ boundary); New… takes
  a playable size (centered on the fixed grid) and shows progress IN the dialog (the main
  overlay sat hidden exactly behind it); dialogs open CenterOwner.
- Palettes group with headers: Allies/Soviets (vanilla owner houses), GDI (mod)/Nod (mod)
  from TF's GoodGuy/BadGuy manifest owners, ts- name prefix → TS GDI (mod); Civilian,
  Misc; Resources leads Overlay (ore/gems/tib) and Terrain (ore mine, blossom trees).
  Headers unselectable.
- Bugs from screenshots: raw TEXT_ overlay labels (EditorSession never wired the fork's
  `AddMissing` text patches; RA also needed TEXT_OVERLAY_TIBERIUM — vanilla RA has no
  Tiberium, TF does) and white-square placements (palette offered overlays with no
  theater art — now `ExistsInTheater`-filtered). NOTE: already-placed white tib01 squares
  on saved maps still render white (the map really holds them); eraser with a resource
  brush removes them.

**MANIFEST BUILDINGS DRIFT CLOSED (2026-08-31, Luke: "No TS buildings?"):** the mod's
`editor_manifest.py` BUILDINGS table now runs to id 136 — all 25 buildings added since the
seed (TS tree, faction-split yards/factories/helipads), committed on the mod repo's
`drop-pods` branch (a000ce6a) with sizes/occupancy re-verified against the bdata.cpp ctors
(the defines.h enum comments were STALE: TSPROC is 4×3, TSWEAP 5×3). None have vanilla
text ids, so the manifest grew an optional `display_name` (editor: parse + NameOverride;
one of text_id/display_name required). TRAP FIXED: a rules pass reset `NameOverride=null`,
wiping manifest names — types now carry `DefaultNameOverride` and resets restore it (and
`BuildingType.Clone()` now copies both). Grouping: mod types owned by a vanilla side
(afact/sweap…) group under Allies/Soviets, not the mod bucket. Deck: new manifest pushed
(md5-verified, adjacent DLL confirmed = local build), editor redeployed.

**UNITS DRIFT ALSO CLOSED (2026-09-01, editor commit b8dbe89):** 14 vehicles (ids 37–51:
TS wave + the four split MCVs — SMCV 40 and AMCV 43 were missing from the original list;
TSMDIV 49 is a purchase token, deliberately absent). Vessels and aircraft turned out to
already be in the manifest. The walkers needed a real feature: manifest `walk_frames`
(TSTITN/TSSMEC 8 facings × 12, TSHMEC 32 × 8) — walker tilesets pack facing BLOCKS of
walk stages (unit.cpp gait contract; TSTITN's shared-image turret block at 96–127), and
the renderer now multiplies the facing index by the stride (`WalkerFrameTests` pins east
= shape 72/192). NOTE: Luke's mod-repo session swept the 14 uncommitted vehicle entries
into its own commit 691e3db6 ("Pod dome...") on main — content landed, just not under its
own commit. SESSION-END STATE: core fast + full Shell tiers green with the walker change;
CLI + App tiers and a final oracle run were NOT yet re-run after it (walker guard is
`WalkFrames > 1`, vanilla types 0 → no-op, so low risk); Deck still has the 28-unit
manifest and the pre-walk_frames editor — push `build/remaster/Vanilla_RA/mapeditor.json`
(md5-check the adjacent DLL first; Luke's session deployed a new DLL 2026-08-31 night)
and `./deploy-deck.sh`.

Next candidates (no committed order):
- More UI feedback from Luke as he uses it (desktop editor.sh always builds fresh).
- Ghost polish: live semi-transparent RENDER preview (fork-style) instead of the
  thumbnail stretch. Blossom-tree thumbnail could show the mature frame (frame_offset).
- Random map generator — Luke asked 2026-08-31 (stretch goal). Nothing exists in the
  fork to port; would be our own feature (terrain synthesis onto the template tables +
  resource/start-point seeding, deterministic via DeterministicRandom).
- TD mission patterns for `expand-mission` — agreed LOW priority (Luke, 2026-08-31:
  feature-completeness only; defer until a real TD authoring need appears).
Full oracle tier re-verified green mid-session (363/363, 14m50s) after the IGamePlugin
additions and `ParseRawScriptRows`, and again at session end after SmudgeEdit landed.

## Order of work for the next session

1. ~~The renderer spike.~~ PASSED 2026-08-30.
2. ~~Scaffold the headless CLI~~ STARTED 2026-08-30: `MobiusCore/Headless/EditorSession` +
   `MobiusCli` (`cncmap info|render|save`, `--game` autodetected from Steam, `--mod` repeatable
   and ordered). Save path is **byte-identical to the mono editor's saves on all 31 CustomMaps**
   (`SaveRoundTripTests`, oracle in `oracle/saves/`). `OfficialMaps.Extract` pulls the official
   skirmish maps out of `MAIN.MIX → general.mix`: **230 files** (130 numeric + 100 letter-coded
   `scmd0`–`scmm9`), not the 124 the display list shows; **all 230 round-trip byte-identical**
   to the mono editor's saves (`oracle/saves-official/`, ~12 min, oracle tier). `validate`
   distinguishes notes (legacy conversions) from problems (lost content, blocking checks).
   Still to add: query/mutate commands.
3. Mod profiles + manifest format. **DECIDED (Luke, 2026-08-30 evening): no `[MapEditor]`
   section in saves — the editor computes "vanilla-safe / needs mod X" from map content, and
   the 261-map byte-exact save oracle stays pristine. And the manifest seam comes before more
   GUI: mod tables move from compiled-in C# to mod-build-time data (`mapeditor.json`), TF being
   the first mod that ships one.** **THE MANIFEST SEAM SHIPPED 2026-08-31 (overnight):** the
   four compiled `*TF.cs` tables are DELETED; a mod ships `mapeditor.json` beside `ccmod.json`
   (schema: `docs/mapeditor-json.md`, format 1 — buildings/units/infantry/templates; templates
   carry no theatre data, availability stays tileset-resolved). Editor side:
   `Headless/ModManifest` (never-throwing parser, per-entry skip + warning), `ModTypeFactory`
   (fresh instances per plugin; merge = vanilla order then (load order, id), units grouped
   vehicle/aircraft/vessel, first-loaded-wins collisions), `Globals.TheModManifests` keyed
   **per game** (an RA and a TD session coexist in-process — a single slot cost 33 oracle
   tests before `ModManifestSessionTests` pinned it). TF side:
   `scripts/editor_manifest.py` in the mod repo generates the manifest — buildings/units/
   infantry as reviewed embedded tables, templates derived from `td_ra_tile_map.json` + the
   **fork's TD `TemplateTypes.cs`** (proven provenance of the old generated table, quirks and
   all: rv13's comma, sh27 over-length, sh51 zeros, clear1 as ordinary 4x4). Field-level
   parity with the compiled tables was proven pre-deletion (`ModManifestParityTests`' first
   revision, in git history); the full oracle tier is the permanent proof. `cncmap info` /
   `validate` now report `requires mods: <names>` vs `vanilla-safe` from placed content
   (`ModSource` provenance on the four type classes). The oracle also flushed out a
   **pre-existing cross-game session bug**: theater probes (smudge availability, template
   tiles) go through `Globals.TheArchiveManager`, which whichever session's CTOR ran last
   owned — so an RA load after a TD load checked `cr1.tem` against TD-desert archives and
   silently dropped scm06ea's five craters from the save (order-dependent: only visible in a
   full-suite run). Fix: `EditorSession.Load` re-points the session-scoped globals (archive /
   team-color / game-text managers) the way it already re-pointed the tileset manager —
   the loading session owns the process globals. Pinned by `ModManifestSessionTests`. NOTE: the manifest ships TF's
   editor-known types — the DLL's drifted extras (TDGFACT/TDNFACT/TSPOWR, the TS tree,
   TDNMCV/TDGMCV…) and the `building-upgrades` branch's new types are the data follow-up in
   `editor_manifest.py`. Map remembering its profile: dead by decision (content-derived).
   ~~Lossless loading of unknown entities~~ DONE 2026-08-30:
   `Map.UnknownEntries` keeps unknown structures/units/infantry/aircraft/ships/terrain/smudge
   verbatim and the RA save writes them back after the known entries (`LosslessLoadTests`);
   `cncmap info` lists them. Not yet covered: `[OVERLAY]` text entries, unknown trigger/team
   references, TD plugin.
4. The Avalonia shell with triggers designed in. STARTED 2026-08-30: `MobiusEditor.Shell`
   (headless view-model layer, `MapDocument`: open / render at scale / cell hit-test / describe /
   save; tested without a window) + `MobiusEditor.App` (Avalonia 12, net8): open dialog, zoom
   (buttons + Ctrl+wheel), scrollable map canvas, cell status line, save-as that refuses to
   overwrite the open map. Verified with Avalonia headless UI tests (`MobiusEditor.App.Tests`,
   xunit v3): the window opens a map from its arguments and its painted frame matches the CLI
   render pixel-mean for pixel-mean — never launched on the desktop. **EDITING CORE STARTED
   2026-08-31:** `TemplateEdit`/`OverlayEdit` in MobiusCore port the fork's tool semantics
   headlessly (row-major stamps, masked-icon skip, theater-unavailable all-false-mask no-op,
   clip at edges, group/random resolution via injected `DeterministicRandom`, wall/resource
   category guards — the Overlay grid self-heals adjacency icons and resource density on
   assignment). `MapDocument` gains PlaceTemplate/EraseTemplate/PlaceOverlay/EraseOverlay with
   per-stroke undo/redo over the already-ported `UndoRedoList` (BeginStroke/EndStroke batches
   a drag into one step). Invariant pinned: edits + undo leave a save byte-identical.
   **`cncmap edit` SHIPPED** (ordered `--place <tile>@<x>,<y>` / `--erase` / `--place-overlay`
   / `--erase-overlay` ops, never overwrites its input, theater-unavailable template = error
   not silent no-op) and the **first GUI brush SHIPPED**: theater-filtered template palette
   (docked left), left-click/drag paints with stroke batching, right-click erases the
   footprint, Ctrl+Z/Ctrl+Y + toolbar Undo/Redo — proven by headless pointer/keyboard tests
   (`PaintingTests`), still never launched on a desktop. Known perf debt: every op re-renders
   the whole map bitmap; fine at 128x128, wants dirty-cell rendering before bigger brushes.
   **Overlay/wall/resource brushes SHIPPED in the GUI** (second palette, one active brush at
   a time, same category-guarded core ops as the CLI). **TRIGGER FOUNDATION STARTED
   2026-08-31:** `TriggerEditor`/`TeamTypeEditor` in MobiusCore port the fork's dialog
   semantics headlessly — working copy + rename ledger, the fork's name invariant ladder
   (non-empty / length cap / not "None" / INI-safe / case-insensitively unique), add/clone/
   remove/rename with in-list action-reference rewriting, one-shot Commit through the
   already-ported `Map.ApplyTriggerNameChanges` / `ApplyTeamTypeRenames` + cleanup sweeps,
   committed list sorted (saves write trigger/team references as LIST INDICES — order is
   file format). Not yet: undo integration into MapDocument, a trigger GUI, the structured
   event/action parameter-type table (lift from `GetEventString`/`GetActionString` + the
   fork's `Update*Controls` coercion switches), and the mission-pattern GENERATORS (pure
   functions, spec in → raw trigger/teamtype rows out). **SPEC FORMAT DECIDED (Luke,
   2026-08-31): JSON** — the toolchain already speaks Newtonsoft everywhere, `//` comments
   are tolerated for inline design notes, and flexibility comes from the spec's shape, not
   the format: a flat pattern list (new patterns = new generator functions, never format
   changes) plus a `raw` escape hatch of literal trigger/teamtype rows, so the spec's floor
   is the raw format itself. Expansion is ONE-TIME scaffolding via a cncmap command (tweak
   in the editor afterwards; no two-way sync). Next: those, plus docked tool panels.

Open shim gaps to close as they are hit: `RotateFlip` rotations, sub-byte indexed writes,
text metrics are approximate (annotation layers only), `Region.Exclude` on infinite regions.

Current blocker if you go back to the existing editor first: tool dialogs never become visible.
Diagnosis and the prime suspect are at the top of `NATIVE_PORT_HANDOVER.md`.

## Backlog

- ~~Unlisted official maps~~ CORRECTED (Luke, 2026-08-30 evening): the 124-entry `[Missions]`
  section is a legacy *name table*, not the Remaster's roster — the game's skirmish list already
  offers the letter-coded Aftermath maps ("C&C" among them), and CONFIG.MEG ships tile patches
  for them. All 230 files are game content. The contact sheet stands as a catalogue of the pool
  (curation reference + editor fixtures), not as hidden content to surface.
- Generalise the fork's RenderProbe to TD so TD rendering gets an oracle, not just eyeballs.
