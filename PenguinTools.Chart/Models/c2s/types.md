# C2S note types

| #   | Code                          | Internal name    | Meaning                      |
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

Air arrows (`AIR` / `AUL` / `AUR` / `ADW` / `ADL` / `ADR`) are not in that enum; they pair onto a parent note.

## Universal note schema

| Note Type | Measure | Offset | Cell | Width |
| --------- | ------- | ------ | ---- | ----- |

- **Measure / Offset** — placement in chart ticks (`RESOLUTION`, usually 384 per measure).
- **Cell** — leftmost column (0–15; `1.14.00+` may use values outside that range).
- **Width** — columns occupied (1–16 typically).

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

- **Target Note** — parent type token at the same time.
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

- **Color** — see [Colors](#colors).
- **Attr** — ladder mode (`1.15.00+`):

| Attr    | Id  | Notes                 |
| ------- | --- | --------------------- |
| `DEF`   | 0   | Default               |
| `AxisY` | 1   | Orient along Y        |
| `AxisZ` | 2   | Orient along Z        |
| `Trace` | 3   | Trace / trail variant |

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
