Based on the existing English translation of [kb10uy's SUS specification](https://gist.github.com/kb10uy/c171c175ba913dc40a73c6ce69da9859). Examples below correct source typos; the [alternate translation](references/sus-waki285.md) is retained as reference material. Implementation coverage is determined by [the parser](../../PenguinTools.Chart/Parser/sus/) and its tests.

# SUS Format Specification v2.7 (rev2)

Note: **SUS** now stands for _Sliding Universal Score_, not _SeaUrchin Score_.

## 1. Overview

- It is text data composed entirely of printable characters.
- File extension: `*.sus`
- EOL: **CRLF** or **LF**
- Encoding: always **UTF-8**
- Lines beginning with `#` have meaning as data; all other lines are treated as comments and ignored.
- Parts that specify string data are quoted with `" ~ "`.

## 2. Metadata Lines

- Various commands listed below are written following `#`.
- For items marked `(ASCII)`, only ASCII characters may be used in the content.
- For items marked `(UTF-8)`, non-ASCII characters may also be used in the content.

### `#TITLE` Song title (UTF-8)

- `#TITLE "Song Title"`

### `#SUBTITLE` Song subtitle (UTF-8)

- `#SUBTITLE "Song Subtitle"`

### `#ARTIST` Song artist (UTF-8)

- `#ARTIST "Artist"`

### `#GENRE` Song genre (UTF-8)

- `#GENRE "Genre"`

### `#DESIGNER` Chart designer (UTF-8)

- `#DESIGNER "Designer"`

### `#DIFFICULTY` Chart difficulty type (ASCII/UTF-8)

- Specifies the type of chart difficulty as either an integer or a string.
- For numeric values, the following five are reserved by the specification:
  - `#DIFFICULTY 0`
  - `#DIFFICULTY 1`
  - `#DIFFICULTY 2`
  - `#DIFFICULTY 3`
  - `#DIFFICULTY 4`
- It may also be specified as a string, but how that is handled depends on the application.

### `#PLAYLEVEL` Chart level (ASCII)

- Specifies the chart level as an integer.
  - `#PLAYLEVEL 10`
- A trailing `+` may also be specified.
  - `#PLAYLEVEL 14+`

### `#SONGID` Song ID (ASCII)

- How this value is handled depends on the application.
- `#SONGID "songid"`

### `#WAVE` Audio file

- Specified as a relative path from the sus file.
- Supported file formats depend on the application.
- `#WAVE "filename.wav"`

### `#WAVEOFFSET` Audio file offset

- Specifies the difference between chart playback start and audio file playback timing.
- Unit: seconds. Decimal values may be used.
- If a positive value is specified, the chart starts first.
- If a negative value is specified, the audio starts first.
- `#WAVEOFFSET 0.5`

### `#JACKET` Song jacket image

- Specified as a relative path from the sus file.
- Supported file formats depend on the application.
- `#JACKET "jacket.jpg"`

### `#BACKGROUND` Background image file

- Specified as a relative path from the sus file.
- Supported file formats depend on the application.
- `#BACKGROUND "jacket.jpg"`

### `#MOVIE` Background video file

- Specified as a relative path from the sus file.
- Supported file formats depend on the application.
- `#MOVIE "movie.mp4"`

### `#MOVIEOFFSET` Background video file offset

- Specifies the difference between chart playback start and video file playback timing.
- Unit: seconds. Decimal values may be used.
- If a positive value is specified, the chart starts first.
- If a negative value is specified, the video starts first.
- `#MOVIEOFFSET 0.5`

### `#BASEBPM` Base tempo for scroll speed calculation

- Specifies the tempo used as the basis for scroll speed calculation.
- The actual scroll speed changes as a ratio relative to this value.
- If omitted, the value of the first BPM change is used.
- `#BASEBPM 120.0`

### `#REQUEST` Special attributes

- Sends special commands to the application.
- Described later in section 4.
- `#REQUEST "ticks_per_beat 480"`

## 3. Chart Data Lines

- Each line consists of a “header part”, `:`, and a “data part”.
- A `:` must be appended after the header part.
- The data part is made up of sets of 2 digits; the number of sets divides the measure, and each division represents a
  timing point.
  - For example, if `11111111` is specified, notes are placed at quarter-note intervals.
  - The maximum number of divisions depends on the application, but it should support at least 512 divisions (data
    part 1024 bytes).
  - For each 2-digit data value, the meaning of the first digit depends on the data type, but `0` always means nothing
    is placed.
  - The second digit always represents note width, where `1` to `z` indicate widths 1 to 35.
  - Therefore, positions where no note exists should be filled with `00`.

- `mmm`
  - For certain specific strings, it represents special data.
  - Otherwise, it is general data and indicates the measure number.
    - Measure numbers start from 0.
- `x`
  - Specifies the leftmost lane of the note.
  - From the left: 0, 1, 2, ..., 9, a, b, c, ...
  - Case-insensitive.
- `y`
  - Specifies the channel for the note.
  - As with `x`, `0` to `z` may be used.
  - Case-insensitive.
- `zz`
  - Specifies the number of special data.
  - Base36 values from 01 to zz may be used.

### `#mmm02` Measure length

- Specifies the measure length in beats from that measure onward.
- Decimal values may be used. However, values of the form **M / 2^n (M, n ∈ N)** are preferred.

### `#BPMzz`, `#mmm08` BPM definition / change

- Specifies the tempo from that point onward by referencing a BPM definition.
- Tempo values may be decimals.
- `#BPM01: 140.0`
- `#00008: 01`

### `#ATRzz`, `#ATTRIBUTE zz`, `#NOATTRIBUTE` Note attribute definitions

- Use ATR to define a set of note attributes.
  - Defined as a string, with multiple values separated by commas.
  - `rh:<decimal>` directional roll speed
  - `h:<decimal>` note height
  - `pr:<integer>` note rendering priority
- Writing `#ATTRIBUTE zz` applies note attribute `zz` to data from that line onward.
- Writing `#NOATTRIBUTE` removes note attributes from data from that line onward.

```text
#ATR01: "pr:100, h: 1.5"
#ATTRIBUTE 01
#00010: 14141414
#NOATTRIBUTE
```

### `#TILzz`, `#HISPEED zz`, `#NOSPEED` Speed change definitions

- Different speeds can be applied per note (hereafter called high-speed definitions).
- A high-speed definition is written as a string, with multiple values separated by commas.
  - String format: `meas'tick:speed`
  - `meas`: measure number (integer)
  - `tick`: tick (integer)
    (A unit that further divides a measure. By default, 1 beat = 480 ticks, so 1 measure = 1920 ticks.)
  - `speed`: speed (decimal; negative values are also allowed)
- Writing `#HISPEED zz` applies high-speed definition `zz` to data from that line onward.
- Writing `#NOSPEED` removes high-speed definitions from data from that line onward.

```text
#TIL01: "0'0:1.0, 0'960:2.0"
#HISPEED 01
#00010: 14141414
#NOSPEED
```

### `#MEASUREBS` Measure number base value

- From the point where this is specified onward, the given value is always added to the measure number in data lines.
- If specified multiple times, the last specified value overwrites the previous added value.

```text
... 0-999
#MEASUREBS 1000
... 1000-1999
#MEASUREBS 2000
... 2000-2999
```

### `#MEASUREHS` Measure line speed change specification

- If the application supports displaying measure lines, this can specify speed changes for those lines.
- The value specified is the same as for `#TIL`.

```text
#MEASUREHS 01
```

### `#mmm1x` Tap

- A single note that does not move position.
- The following six tap codes are reserved by the specification:
  - `1?` Tap 1
  - `2?` Tap 2
  - `3?` Tap 3
  - `4?` Tap 4
  - `5?` Tap 5
  - `6?` Tap 6
- `#00010: 2414141434141414`

### `#mmm2xy` Hold

- A long note whose position does not move.
- The same width must be specified at all points.
- Notes with the same channel are connected.
  - `1?` Start point
  - `2?` End point
  - `3?` Relay point
- `#00020a: 14002400`

### `#mmm3xy` Slide 1

- A long note whose position moves.
- Different widths may be set for each point.
- Shape can be smoothed using Bézier curves.
- The curve shape is defined by continuous line segments connecting the centers of relay-point and control-point notes.
- Notes with the same channel are connected.
  - `1?` Start point
  - `2?` End point
  - `3?` Relay point
  - `4?` Bézier curve control point
  - `5?` Invisible relay point
- `#00030a: 14340024`

### `#mmm4xy` Slide 2

- A long note whose position moves.
- The basic specification is equivalent to Slide 1, so it is omitted here.

### `#mmm5x` Directional

- A note definition with direction.
- It does not necessarily need to overlap another note and may also be placed independently.
  - `1?` Up
  - `2?` Down
  - `3?` Upper-left
  - `4?` Upper-right
  - `5?` Lower-left
  - `6?` Lower-right
- `#00050: 14241424`

## 4. Special attributes that can be specified with `#REQUEST`

The following are defined by the specification.

### `ticks_per_beat` Change the number of ticks per beat

- `#REQUEST "ticks_per_beat <integer>"`
- When using \(n\)-th notes in a chart, this should be set so that it is a divisor of measure beat count × tick count.

### `enable_priority` Enable/disable priority-based note rendering

- `#REQUEST "enable_priority true/false"`
