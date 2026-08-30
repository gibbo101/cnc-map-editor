# `mapeditor.json` — the per-mod editor type manifest (format 1)

A mod that adds entity types ships a `mapeditor.json` beside its `ccmod.json`. The editor
reads it per active mod (`--mod <dir>`, or discovery via `cncmap mods`) and constructs the
mod's building / unit / infantry / terrain-template types at runtime — nothing mod-specific
is compiled into the editor. Tiberian Factions generates its manifest with the mod repo's
`scripts/editor_manifest.py`; that script's embedded tables and derivations are the source
of truth for TF's data, this document is the source of truth for the format.

Parsing never throws and never half-applies: a file with an unsupported `format` is skipped
whole, a malformed entry or unknown flag/kind/z-order name skips just that entry, and every
skip surfaces as a load warning (`cncmap validate`, `EditorSession.ManifestLoadWarnings`).

## Top level

```json
{ "format": 1, "game_type": "RA",
  "buildings": [ ... ], "units": [ ... ], "infantry": [ ... ], "templates": [ ... ] }
```

- `format` — integer, must be `1`. Anything else skips the file (with a warning).
- `game_type` — `"RA"` or `"TD"`; a manifest for the other game is ignored by the session.
- Every section is optional and defaults to empty.

## Merge semantics

Types merge after the vanilla tables: vanilla declaration order first, then manifest entries
by (mod load order, id) — units grouped vehicle / aircraft / vessel. On an id or
case-insensitive name collision the first-loaded type wins and the dropped entry leaves a
warning; vanilla always beats a manifest, an earlier mod beats a later one. The editor's
`Globals` filters apply to manifest types exactly as to vanilla ones (wall buildings,
disabled air units).

## `buildings`

```json
{ "id": 91, "name": "tdsilo", "text_id": "TEXT_STRUCTURE_TITLE_GDI_SILO",
  "power_production": 0, "power_usage": 10, "storage": 1500, "capturable": true,
  "width": 2, "height": 1, "occupy_mask": "10", "owner": "GoodGuy",
  "factory_overlay": null, "frame_offset": 0, "graphics_source": null,
  "z_order": "default", "flags": ["Bib"] }
```

- `id` — the engine's `StructType` enum ordinal. `name` — IniName (lowercase). `text_id` —
  localisation key. `owner` — house name (`GoodGuy`, `BadGuy`, `Neutral`, ...).
- `width`/`height` — footprint in cells; `occupy_mask` — rows separated by spaces, `'0'` a
  free cell, anything else occupied; `null`/omitted = fully occupied.
- `graphics_source` — art name when it differs from `name` (null falls back to `name`);
  `factory_overlay` — overlay art for factories; `frame_offset` — first art frame.
- `z_order` — `"default"`, `"paved"` or `"flat"`.
- `flags` — array of `BuildingTypeFlag` member names: `Factory`, `Bib`, `Fake`, `Turret`,
  `SingleFrame`, `NoRemap`, `GapGenerator`, `TheaterDependent`, `Wall`, `NoRules`.
- Defaults when omitted: powers/storage/frame_offset `0`, `capturable` false, masks and
  string fields `null`, `flags` empty.

## `units`

```json
{ "id": 24, "kind": "vehicle", "name": "tdmtnk", "text_id": "TEXT_UNIT_TITLE_GDI_MED_TANK",
  "owner": "GoodGuy", "body_frames": ["Frames32Full"], "turret_frames": ["Frames32Full"],
  "turret": null, "turret2": null, "turret_offset": 0, "turret_y": 0,
  "flags": ["Armed", "Turret"] }
```

- `kind` — `"vehicle"`, `"aircraft"` or `"vessel"`; picks the concrete engine type. Unit ids
  are **per-kind namespaces** (separate engine enums), so an aircraft and a vessel may share
  an id.
- `body_frames` / `turret_frames` — arrays of `FrameUsage` member names, OR-combined:
  `Frames32Full`, `Frames16Simple`, `Frames16Symmetrical`, `Frames08Cardinal`,
  `Frames01Single`, `DamageStates`, `HasUnloadFrames`, `Rotor`, `OnFlatBed`.
  `body_frames` is required; `turret_frames` defaults to none.
- `turret` / `turret2` — separate turret art names (only honoured with the `Turret` /
  `DoubleTurret` flags, matching the engine); `turret_offset` / `turret_y` — turret draw
  offsets.
- `flags` — array of `UnitTypeFlag` member names: `FixedWing`, `Turret`, `DoubleTurret`,
  `Armed`, `Harvester`, `NoRemap`, `BuildingRemap`, `ExpansionOnly`, `GapGenerator`,
  `Jammer`, `NoRules`.

## `infantry`

```json
{ "id": 26, "name": "tde1", "text_id": "TEXT_UNIT_TITLE_GDI_MINIGUNNER",
  "owner": "GoodGuy", "flags": ["Armed"] }
```

Same `flags` alphabet as units. Reserved for a future format (documented, not parsed in
format 1): `remap_table`, `no_image_rule`.

## `templates`

```json
{ "id": 401, "name": "tdsh1", "width": 3, "height": 3, "lands": "BBB BBB WWW", "mask": null }
```

- `id` — the engine `TemplateType` id (TF's TD range starts at 401, which the `[TFTDTiles]`
  save logic keys on). `width`/`height` — icons.
- `lands` — one character per icon, rows separated by spaces: `C` clear, `B` beach, `I`
  rock, `R` road, `W` water, `V` river, `H` rough; `X` (filler) and any unknown character
  read as clear, characters beyond `width*height` are ignored — the editor's historical
  parse, which the TF generator relies on for a few damaged source strings.
- `mask` — icon usage, `'0'` removes an icon, rows separated by spaces; `null` = all icons.
- **No theatre data**: availability is resolved at runtime from the tileset XML
  (`TemplateType.Init`), so a template exists exactly where the mod ships art for it.

Reserved for a future format: unit `graphics_source`.
