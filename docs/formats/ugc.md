# Umiguri Chart v8

Based on the existing English translation of [inonote's Umiguri Chart v8 specification](https://gist.github.com/inonote/5c01e73781cab17765a1d93641d52298). AIR-HOLD height forms below also document PenguinTools behavior; this reference does not imply full implementation of the upstream specification.

## File structure

UGC uses the `.ugc` extension, UTF-8, and LF or CRLF line endings. Each line contains one command or note. Lines not starting with `@` or `#`, and lines with missing parameters, are ignored. Comments begin with `'`.

Time uses `Bar'Tick`: `0'240` means measure 0, tick 240. Header arguments are separated by horizontal tabs.

## Metadata commands

| Command  | Arguments and meaning                                                                         |
| -------- | --------------------------------------------------------------------------------------------- |
| `VER`    | Version, always 8                                                                             |
| `EXVER`  | Extended version, 0 or 1; 1 forces `FLAG:EXLONG` to `TRUE` and is always used in UMIGURI NEXT |
| `TITLE`  | Song title                                                                                    |
| `SORT`   | Sort key; see the rules below                                                                 |
| `ARTIST` | Artist name                                                                                   |
| `GENRE`  | Genre; defaults to the song folder's parent folder name                                       |
| `DESIGN` | Chart designer                                                                                |
| `DIFF`   | BASIC = 0, ADVANCED = 1, EXPERT = 2, MASTER = 3, WORLD'S END = 4, ULTIMA = 5                  |
| `LEVEL`  | Play level; star count for WORLD'S END                                                        |
| `WEATTR` | WORLD'S END attribute, one kanji or full-width symbol                                         |
| `CONST`  | Chart constant                                                                                |
| `SONGID` | Song ID; share across difficulties of a song, except WORLD'S END                              |
| `RLDATE` | Addition date in `YYYYMMDD` format                                                            |

The source's sort-key rules are:

1. Uppercase Latin letters: `magnet` becomes `MAGNET`.
2. Remove symbols and spaces: `Miracle∞Hinacle` becomes `MIRACLEHINACLE`.
3. Convert hiragana to katakana, remove dakuten/handakuten, expand small kana, and replace long vowel marks with `ウ`: `かーてんこーる!!!!!` becomes `カウテンコウル`.
4. Read kanji and apply the preceding rules: `幻想症候群` becomes `ケンソウシントロウム`.
5. Decode leetspeak before applying the rules: `^/7(L?[_(L#<>+&l^(o)` becomes `NYARLATHOTEP`.
6. Transliterate other languages into Japanese first: `슈퍼히어로` becomes `シュポヒオロ`, then `シユホヒオロ`.

## Media and field commands

| Command    | Arguments and meaning                                                                                       |
| ---------- | ----------------------------------------------------------------------------------------------------------- |
| `BGM`      | Audio filename; WAV, MP3, OGG, or M4A                                                                       |
| `BGMOFS`   | Offset in seconds; positive delays playback, negative starts earlier                                        |
| `BGMPRV`   | Preview start and end positions in seconds                                                                  |
| `JACKET`   | Jacket filename; PNG, BMP, JPEG, GIF, or GPU-compressed DDS                                                 |
| `BGIMG`    | Background filename; PNG, BMP, JPEG, GIF, MP4, or AVI                                                       |
| `BGSCENE`  | Background 3D scene ID                                                                                      |
| `BGMODE`   | Attribute and `TRUE` / `FALSE`; `PASSIVE` ignores audio playback position and loops short media             |
| `FLDCOL`   | Divider color index: -1 Auto, 0 White, 1 Red, 2 Orange, 3 Yellow, 4 Lime, 5 Green, 6 Teal, 7 Blue, 8 Purple |
| `FLDSCENE` | Field background 3D scene ID                                                                                |

The source recommends M4A around 3 MB for loading speed, jacket images at 400×400, and DDS BC1 without mipmaps. It also recommends keeping the chart offset at 0 and adjusting the audio waveform instead. These are upstream authoring recommendations.

## Timing and flags

| Command   | Arguments and meaning                                                 |
| --------- | --------------------------------------------------------------------- |
| `TICKS`   | Time resolution, 480 in the v8 specification                          |
| `MAINBPM` | Base BPM                                                              |
| `MAINTIL` | Base timeline ID; 0 recommended                                       |
| `CLKCNT`  | Click count; defaults to the first measure's time-signature numerator |
| `FLAG`    | Attribute name and `TRUE` / `FALSE`                                   |
| `BPM`     | BarTick, BPM                                                          |
| `BEAT`    | Bar, numerator, denominator                                           |
| `TIL`     | Timeline ID, BarTick, speed                                           |
| `SPDMOD`  | BarTick, speed                                                        |
| `SPDDEF`  | Speed-definition ID, offset tick, speed; UMGR v2.01 extension         |
| `SPDFLD`  | Speed-definition ID, BarTick, X, width, length; UMGR v2.01 extension  |
| `USETIL`  | Timeline ID for subsequent note lines                                 |

`FLAG` attributes:

| Attribute     | Meaning                                 |
| ------------- | --------------------------------------- |
| `DIFFTTL`     | Tutorial chart; always specify `FALSE`  |
| `SOFFSET`     | Insert a blank measure at the beginning |
| `CLICK`       | Play click sounds                       |
| `EXLONG`      | Use ExLong                              |
| `BGMWCMP`     | Wait for audio playback to finish       |
| `HIPRECISION` | Use high-resolution AIR values          |

## Note records

Parent notes use `#BarTick:txw`. Children use `#OffsetTick>txw`; `#OffsetTick:txw` is also accepted. Here, `t` is the note type, `x` is the horizontal position, and `w` is the width. Positions and widths use base 36.

| Note      | Parent format                        | Type and extra fields                          |
| --------- | ------------------------------------ | ---------------------------------------------- |
| CLICK     | `#BarTick:t`                         | `t = c`                                        |
| TAP       | `#BarTick:txw`                       | `t = t`                                        |
| EXTAP     | `#BarTick:txwd`                      | `t = x`; `d` selects the effect                |
| FLICK     | `#BarTick:txwd`                      | `t = f`; `d` selects the autoplay direction    |
| DAMAGE    | `#BarTick:txw`                       | `t = d`                                        |
| HOLD      | `#BarTick:txw`                       | `t = h`                                        |
| SLIDE     | `#BarTick:txw`                       | `t = s`                                        |
| AIR       | `#BarTick:txwddc`                    | `t = a`; `dd` is direction, `c` is color       |
| AIR-HOLD  | `#BarTick:txwc` or `#BarTick:txwhhc` | `t = H`; optional `hh` is height, `c` is color |
| AIR-SLIDE | `#BarTick:txwhhc`                    | `t = S`; `hh` is height, `c` is color          |
| AIR-CRUSH | `#BarTick:txwhhc,{interval}`         | `t = C`; `hh` is height, `c` is crush color    |

Height `hh` is a two-digit base-36 value for the original height multiplied by 10. The short AIR-HOLD form uses raw height 80. Its children inherit that height unless an explicit height is present.

| Parent    | Child format                        | Meaning                                                  |
| --------- | ----------------------------------- | -------------------------------------------------------- |
| HOLD      | `#OffsetTick>t`, `t = s`            | Endpoint                                                 |
| SLIDE     | `#OffsetTick>txw`, `t = s`          | Intermediate point or endpoint                           |
| SLIDE     | `#OffsetTick>txw`, `t = c`          | Control point                                            |
| AIR-HOLD  | `#OffsetTick>t`, `t = s`            | Intermediate point or endpoint, inheriting parent height |
| AIR-HOLD  | `#OffsetTick>t`, `t = c`            | Endpoint without AIR-ACTION, inheriting parent height    |
| AIR-HOLD  | `#OffsetTick>txwhh`, `t = s` or `c` | Corresponding child with explicit height                 |
| AIR-SLIDE | `#OffsetTick>txwhh`, `t = s`        | Intermediate point or endpoint                           |
| AIR-SLIDE | `#OffsetTick>txwhh`, `t = c`        | Control point or endpoint without AIR-ACTION             |
| AIR-CRUSH | `#OffsetTick>txwhh`, `t = c`        | Endpoint                                                 |

AIR-CRUSH `{interval}` is a decimal placement interval. `0` means AIR-TRACE; `$` creates notes that generate combo only at the starting point.

## Effects, directions, and colors

EXTAP effects are `U` Up, `D` Down, `C` Center, `A` Clockwise, `W` Counterclockwise, `L` Right, `R` Left, and `I` In/Out.

FLICK autoplay direction is `A` Auto, `L` Right, or `R` Left.

AIR directions are `UC` Up, `UL` Up-left, `UR` Up-right, `DC` Down, `DL` Down-left, and `DR` Down-right. AIR, AIR-HOLD, and AIR-SLIDE colors are `N` Normal or `I` Inverted.

AIR-CRUSH colors are:

| Code | Color        |
| ---- | ------------ |
| `0`  | Normal       |
| `1`  | Red          |
| `2`  | Orange       |
| `3`  | Yellow       |
| `4`  | Yellow-green |
| `5`  | Green        |
| `6`  | Aqua         |
| `7`  | Sky blue     |
| `8`  | Cyan         |
| `9`  | Blue         |
| `A`  | Blue-violet  |
| `Y`  | Red-violet   |
| `B`  | Pink         |
| `C`  | White        |
| `D`  | Black        |
| `Z`  | Transparent  |

Implementation: [UGC parser](../../PenguinTools.Chart/Parser/ugc/) and [UGC writer](../../PenguinTools.Chart/Writer/ugc/). Related references: [C2S](c2s.md) and [SUS](sus.md).
