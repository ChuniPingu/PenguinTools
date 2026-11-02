# C2S note types

Field reference for C2S records. The category IDs and reference names below describe the original format.

| #   | Code                          | Reference name   | Meaning                      |
| --- | ----------------------------- | ---------------- | ---------------------------- |
| 0   | `NON`                         | —                | Empty                        |
| 1   | `TAP`                         | `TapNote`        | Tap                          |
| 2   | `CHR`                         | `CharaTapNote`   | ExTap                        |
| 3   | `HLD` / `HXD`                 | `HoldNote`       | Hold / ExHold                |
| 4   | `SLD` / `SLC` / `SXD` / `SXC` | `SlideNote`      | Slide (step / control)       |
| 5   | `ALD`                         | `AirLadderNote`  | Air Ladder (Air Crush)       |
| 6   | `ASO`                         | `AirSolidNote`   | Air Solid                    |
| 7   | `FLK`                         | `FlickNote`      | Flick                        |
| 8   | `MNE`                         | `MineNote`       | Damage                       |
| 9   | `SLA`                         | —                | Soflan attribute region      |
| 10  | `HHD` / `HHX`                 | `HeavenHoldNote` | Heaven Hold / Ex Heaven Hold |
| 11  | `AHD` / `AHX`                 | `AirHoldNote`    | Air Hold (step / control)    |
| 12  | `ASD` / `ASC`                 | `AirSlideNote`   | Air Slide (step / control)   |

Air arrows (`AIR` / `AUL` / `AUR` / `ADW` / `ADL` / `ADR`) are not in that category list; they pair onto a parent note.

## Universal note schema

| Note Type | Measure | Offset | Cell | Width |
| --------- | ------- | ------ | ---- | ----- |

- **Measure / Offset** — placement in chart ticks (`RESOLUTION`, usually 384 per measure).
- **Cell** — leftmost column (0–15; `1.14.00+` may use values outside that range).
- **Width** — columns occupied, clamped to 1–16 before matching paths and AIR parents. Extended lane bounds are applied after those relationships are established.

## TAP

| `"TAP"` | Measure | Offset | Cell | Width |
| ------- | ------- | ------ | ---- | ----- |

## CHR (ExTap)

| `"CHR"` | Measure | Offset | Cell | Width | Effect |
| ------- | ------- | ------ | ---- | ----- | ------ |

- **Effect** — `UP` / `DW` / `CE` / `LC` / `RC` / `LS` / `RS` / `BS`.

## HLD / HXD (Hold)

| `"HLD"` | Measure | Offset | Cell | Width | Duration |
| ------- | ------- | ------ | ---- | ----- | -------- |

| `"HXD"` | Measure | Offset | Cell | Width | Duration | Effect |
| ------- | ------- | ------ | ---- | ----- | -------- | ------ |

- **Effect** — same codes as `CHR`.

## SLD / SLC / SXD / SXC (Slide)

| `"SLD"` / `"SLC"` | Measure | Offset | Cell | Width | Duration | End Cell | End Width | Link |
| ----------------- | ------- | ------ | ---- | ----- | -------- | -------- | --------- | ---- |

| `"SXD"` / `"SXC"` | Measure | Offset | Cell | Width | Duration | End Cell | End Width | Link | Effect |
| ----------------- | ------- | ------ | ---- | ----- | -------- | -------- | --------- | ---- | ------ |

- **`SXD` / `SXC`** — ExSlide.
- **Link** — usually `SLD`; `NCL` = no line.

Conversion and C2S writing share a scheduler that orders complete SLIDE/AIR paths by parent and continuation dependencies while preserving unrelated statement order. Records advance paths in statement order. A continuation matches the endpoint time, cell, width, and line type; ties use path creation order. Changing `SLD` to `NCL` starts a separate path. Independent slides touching end to start require the later slide's first record before the earlier slide's final segment; sorting these records by time merges the paths. AIR attachment and AIR-SLIDE continuation also depend on order; AIR-SLIDE additionally matches native height in integer tenths and the parent node type.

An AIR declaration can precede later continuations of its ground SLIDE. Its parent is matched against the endpoint available at that point in record order, while native playback anchors AIR to the completed long note's end. Conversion preserves this ordering when multiple ground paths share temporary endpoints; this does not make intermediate SLIDE joints editable AIR attachment points.

When all segments have positive duration at C2S precision and each ground path has at most one AIR path attached at its final endpoint, these ordering constraints resolve overlapping paths without changing their associations.

## FLK (Flick)

| `"FLK"` | Measure | Offset | Cell | Width | `"L"` |
| ------- | ------- | ------ | ---- | ----- | ----- |

- Trailing token is always `L` in official charts.

## MNE (Mine / Damage)

| `"MNE"` | Measure | Offset | Cell | Width |
| ------- | ------- | ------ | ---- | ----- |

## SLA (Soflan region)

| `"SLA"` | Measure | Offset | Cell | Width | Duration | Timeline |
| ------- | ------- | ------ | ---- | ----- | -------- | -------- |

- **Timeline** — SLP timeline id this region belongs to.
- A region applies at `start ≤ tick < end` when it contains the object's full lane span. Overlaps select the largest timeline id; row order does not affect this choice. AIR-CRUSH rungs are checked at their interpolated positions.

## AIR / AUL / AUR / ADW / ADL / ADR

| `"AIR"` / … | Measure | Offset | Cell | Width | Target Note | Color |
| ----------- | ------- | ------ | ---- | ----- | ----------- | ----- |

| Code  | Direction  |
| ----- | ---------- |
| `AIR` | Up         |
| `AUL` | Up-left    |
| `AUR` | Up-right   |
| `ADW` | Down       |
| `ADL` | Down-left  |
| `ADR` | Down-right |

- **Target Note** — selects the first previously created, unoccupied parent of that type whose current endpoint matches the AIR time, cell, and width. AIR, AIR-HOLD, and AIR-SLIDE share one attachment slot per ground path. Long-note parents match their current end; forward references and reuse of occupied parents are rejected.
- **Color** — optional; see [Colors](#colors).

## AHD / AHX (Air Hold)

| `"AHD"` / `"AHX"` | Measure | Offset | Cell | Width | Target Note | Duration | Color |
| ----------------- | ------- | ------ | ---- | ----- | ----------- | -------- | ----- |

- **Color** — optional; see [Colors](#colors).

## ASD / ASC (Air Slide)

| `"ASD"` / `"ASC"` | Measure | Offset | Cell | Width | Target Note | Height | Duration | End Cell | End Width | End Height | Color |
| ----------------- | ------- | ------ | ---- | ----- | ----------- | ------ | -------- | -------- | --------- | ---------- | ----- |

## ALD (Air Ladder / Air Crush)

| `"ALD"` | Measure | Offset | Cell | Width | Density | Height | Duration | End Cell | End Width | End Height | Color | Attr |
| ------- | ------- | ------ | ---- | ----- | ------- | ------ | -------- | -------- | --------- | ---------- | ----- | ---- |

- Continuations join the first earlier path with matching endpoint time, cell, width, quantized height, density, and color. The root's axis applies to the whole path. Independent editor paths are ordered to keep touching endpoints separate.
- **Color** — see [Colors](#colors).
- **Attr** — ladder mode (`1.15.00+`):

| Attr    | Id  | Notes                   |
| ------- | --- | ----------------------- |
| `DEF`   | 0   | X-axis ribbon (default) |
| `AxisY` | 1   | Y-axis ribbon           |
| `AxisZ` | 2   | Z-axis ribbon           |
| `Trace` | 3   | Thin tube               |

`Attr` controls line appearance; `Density = 0` independently disables judgements.

## ASO (Air Solid)

| `"ASO"` | Measure | Offset | Cell | Width | Height A | Height B | Duration | End Cell | End Width | End Height A | End Height B | Color |
| ------- | ------- | ------ | ---- | ----- | -------- | -------- | -------- | -------- | --------- | ------------ | ------------ | ----- |

- Dual start/end heights (`%3.1f` each).

## HHD / HHX (Heaven Hold)

| `"HHD"` | Measure | Offset | Cell | Width | Height | Duration | End Cell | End Width | End Height | Extra |
| ------- | ------- | ------ | ---- | ----- | ------ | -------- | -------- | --------- | ---------- | ----- |

| `"HHX"` | Measure | Offset | Cell | Width | Height | Duration | End Cell | End Width | End Height | Extra | Effect |
| ------- | ------- | ------ | ---- | ----- | ------ | -------- | -------- | --------- | ---------- | ----- | ------ |

- **Extra** is an integer in the official format.
- **Effect** on `HHX` uses the same codes as `CHR`.

## Colors

Used by air / ladder / solid notes:

| Code  | Meaning     |
| ----- | ----------- |
| `DEF` | Default     |
| `NON` | None        |
| `PNK` | Pink        |
| `GRN` | Green       |
| `LIM` | Lime        |
| `RED` | Red         |
| `BLK` | Black       |
| `VLT` | Violet      |
| `BLU` | Blue        |
| `DGR` | Dodger blue |
| `AQA` | Aqua        |
| `CYN` | Cyan        |
| `YEL` | Yellow      |
| `ORN` | Orange      |
| `GRY` | Gray        |
| `PPL` | Purple      |

## ExTap effects

| Code | Meaning      |
| ---- | ------------ |
| `UP` | Up           |
| `DW` | Down         |
| `CE` | Center       |
| `LC` | Left         |
| `RC` | Right        |
| `LS` | Rotate left  |
| `RS` | Rotate right |
| `BS` | In / out     |
