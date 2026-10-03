# Alternate SUS translation

Source material from [waki285's English translation](https://gist.github.com/waki285/dafa254a9de56ba43d177ae0913d4263). The original text and annotations are retained below, including source typos. Use [the primary SUS reference](../sus.md) for corrected examples.

---

File fetch from https://gist.github.com/waki285/dafa254a9de56ba43d177ae0913d4263

# SUS Format Specification v2.7 (rev2)
- **SUS** stands for *Sliding Universal Score*, not *SeaUrchin Score* anymore.  
*(Annotation: SUS files were originally the proprietary format of `SeaUrchin`, a tool to play custom chart)*

## 1. Overview
* Plain Text data, consisting entirely of printable characters.
* File extension: `*.sus`
* EOL: **CRLF** or **LF**
* Encode: Always **UTF-8**
* Lines beginning with `#` are meaningful as data; thus other lines are ignored as comments.
* Use double quotes (`"something"`) for parts specifying string data.

## 2. Metadata lines
* The commands listed below must be prefixed with `#`. No space is allowed between the `#` and the commands.
* For commands marked `(ASCII)`, non-ASCII characters CANNOT be used for the content.
* For commands marked `(UTF-8)`, non-ASCII characters can be used for the contents.

### `#TITLE` Song title (UTF-8)
* `#TITLE "Song Title"`

### `#SUBTITLE` Song Subtitle (UTF-8)
* `#SUBTITLE "Song Subtitle"`

### `#ARTIST` Song Artist (UTF-8)
* `#ARTIST "Artist"`

### `#GENRE` Song genre (UTF-8)
* `#GENRE "Genre"`

### `#DESINGER` Chart designer (UTF-8)
* `#DESIGNER "Designer"`

### `#DIFFICULTY` Chart difficulty category (ASCII/UTF-8)
* An integer value or string specifying the category of difficulty of the chart.
* The following five are reserved for numerical specifications.
    * `#DIFFICULTY 0`
    * `#DIFFICULTY 1`
    * `#DIFFICULTY 2`
    * `#DIFFICULTY 3`
    * `#DIFFICULTY 4`
* It can also be specified as a string, but its processing varies from application to application.

### `#PLAYLEVEL` Chart difficulty level (ASCII)
* Specify the level of the chart by an integer value.
    * `#PLAYLEVEL 10`
* Can also be suffixed with `+`.
    * `#PLAYLEVEL 14+`  
*(Annotation: As for `+`, it is the specification of the rhythm game on which SeaUrchin is based.)*

### `#SONGID` Song ID (ASCII)
* The processing of this value varies from application to application.
* `#SONGID "songid"`

### `#WAVE` Audio file
* Specifies by relative path from SUS file.
* Supported file formats vary from application to application.
* `#WAVE "filename.wav"`

### `#WAVEOFFSET` Audio file offset
* Specifies the difference between the start of chart playback and the playback timing of the audio file.
* Units are in seconds and can be specified as a decimal (float)
* A positive value causes the chart to start first.
* A negative value causes the audio to start first.
* `#WAVEOFFSET 0.5`

### `#JACKET` Song jacket
* Specifies by relative path from SUS file.
* Supported file formats vary from application to application.
* `#JACKET "jacket.jpg"`

### `#BACKGROUND` Background image file
* Specifies by relative path from SUS file.
* Supported file formats vary from application to application.
* `#BACKGROUND "jacket.jpg"`

### `#MOVIE` Background movie file
* Specifies by relative path from SUS file.
* Supported file formats vary from application to application.
* `#MOVIE "movie.mp4"`

### `#MOVIEOFFSET` Background movie file offset
* Specifies the difference between the start of chart playback and the playback timing of the movie file.
* Units are in seconds and can be specified as a decimal (float)
* A positive value causes the chart to start first.
* A negative value causes the movie to start first.
* `#MOVIEOFFSET 0.5`

### `#BASEBPM` Base tempo for calculating scroll speed
* Specifies the base tempo for scroll speed.
* The actual scroll speed varies as a percentage of this value.
* If not specified, it will be the value of the first BPM change.
* `#BASEBPM 120.0`

### `#REQUEST` Special attributes
* Send special commands to the application.
* It is described below in Chapter 4.
* `#REQUEST "ticks_per_beat 480"`

## 3. Chart data lines
* Each line is in the form "header part, `:`, data part".
* The header part must be followed by `:`.
* The data part is a set of two digits, and the measure is divided by the number of sets, each of which is a timing.
    * For example, `1111111111` will be placed at quarter note intervals.
    * The maximum number of divisions varies from application to application, but at least 512 divisions (1024 bytes of data part) should be supported.
    * For 2-digit data, the first digit is different for each data type. However, `0` is always unassigned.
    * The second digit always represents the width of the note, similarly 1 to z represents 1 to 35 widths.
    * Therefore, positions where no notes exist should be filled with `00`.

`mmmcxyzz: (number data)`

* `mmm` 
    * If it is not numeric, it is special data.
    * For numerical values, this is general data and is the measure number.
        * Measure numbers start at 0 (i.e., 000).  
        *(Annotation: If you wish to create measures 1000 and beyond, see `#MEASUREBS` below.)*
* `c`
    * Specifies the type for each note.
    * Types are described below.
    * 0 is a special type. See below.
* `x`
    * Specifies the leftmost lane of notes.
    * From left to right: 0, 1, 2, ... , 9, a, b, c, ... and so on.
    * It is case-insensitive.
* `y`
    * Specifies the channel for each note. **Not present for some note types.**
    * Like `x`, it can be from 0 to z.
    * It is case-insensitive.
* `zz`
    * Specifies the number of special data. **Not present in many cases.**
    * 01 to zz available in Base36.

### `#mmm02` Beat (measure length) designation
* Specify the measure length after that measure number in beats per measure.
* Decimal values can be specified. However, a value such that **M / 2^n (M, n ∈ N)** is preferred.
* `00002: 4`

### `#BPMzz`, `#mmm08` BPM definition/change
* The tempo after that point is specified by referring to the BPM definition.
* The tempo value can be a decimal value.
* `#BPM01: 140.0`
* `#00008: 01`

### `#ATRzz`, `#ATTRIBUTE zz`, `#NOATTRIBUTE` Notes attribute definition
* Define a set of notes attributes in ATR.
    * Defined as a string, with commas separating multiple values.
    * `rh:<小数>` Directional roll speed
    * `h:<小数>` Height of Notes
    * `pr:<整数>` Priority of drawing of notes
* If `#ATTRIBUTE zz` is specified, the notes attribute of `zz` is applied to the data after that line.
* If `#NOATTRIBUTE` is specified, the notes attribute will not be applied to the data after that line.
```
#ATR01: "pr:100, h: 1.5"
#ATTRIBUTE 01
#00010: 14141414
#NOATTIRBUTE
```

### `#TILzz`, `#HISPEED zz`, `#NOSPEED` Hi-Speed definition
* Different speeds can be applied to each note (hereafter referred to as "hi-speed definition").
* A hi-speed definition is defined as a string, with commas separating multiple values.
    * String in `meas'tick:speed` format.。
    * `meas` Measure number (integer) *(Annotation: need not be zero-filled)*
    * `tick` Tick (integer) (Unit to further divide a measure. By default, one beat is 480 ticks, so one measure is 1920 ticks)
    * `speed` Speed (float) (negative values are also possible)
* If `#HISPEED zz` is specified, the hi-speed definition of `zz` is applied to the data after the line.
* If `#NOSPEED` is specified, the hi-speed definition is not applied to the data after the line.

```
#TIL01: "0'0:1.0, 0'960:2.0"
#HISPEED 01
#00010: 14141414
#NOSPEED
```

### `#MEASUREBS` Measure number base value
* The specified value is always added to the measure number of the data line from the time it is specified.
* If specified multiple times, the last value specified will overwrite the added value.

```
... 0-999
#MEASUREBS 1000
... 1000-1999
#MEASUREBS 2000
... 2000-2999
```

### `#MEASUREHS` Measure line speed change definition
* If the application supports the display of bar lines, you can specify the speed change of those bar lines.
* The value to be specified is the same as `#TIL`.

```
#MEASUREHS 01
```

### `#mmm1x` Tap
* Single-pressed notes that do not move in position.
* The following six specifications are reserved.
    * `1?` Tap 1
    * `2?` Tap 2
    * `3?` Tap 3
    * `4?` Tap 4
    * `5?` Tap 5
    * `6?` Tap 6
* `#00010: 2414141434141414`

### `#mmm2xy` Hold
* Long-pressed notes that do not move in position.
* The same width must be specified at all points.
* Channels with the same channel are connected to each other.
    * `1?` Start
    * `2?` End
    * `3?` Relay
* `#00020a: 14002400`

### `#mmm3xy` Slide 1
* Long-pressed notes that move in position.
* Different widths can be set for each point.
* The Bézier curve allows the shape to be set smoothly.
* As for the shape of the curve, it is defined by successive line segments connecting the centers of the notes at each relay point and control point.
* Channels with the same channel are connected to each other.
    * `1?` Start
    * `2?` End
    * `3?` Relay
    * `4?` Bézier curve control
    * `5?` Relay (Invisible)
* `#00030a: 14340024`

### `#mmm4xy` Slide 2
* Long-pressed notes that move in position.
* The basic specifications are the same as those on slide 1.

### `#mmm5x` Directional
* Notes Definition with Direction.
* It does not necessarily have to be placed on top of other notes, but can be placed by itself.
    * `1?` Upper
    * `2?` Downer
    * `3?` Upper Left
    * `4?` Upper Right
    * `5?` Downer Left
    * `6?` Downer Right
* `#00050: 14241424`

## 4. Special attributes that can be specified with `#REQUEST`
The following are defined as specifications.

### `ticks_per_beat`: change the number of ticks per beat
* `#REQUEST "ticks_per_beat <integer>`.
* If nth notes are used in a chart, they should be specified to be an integral number of beats per measure * number of ticks.

### `enable_priority`: enable/disable prioritized note drawing.
* `#REQUEST "enable_priority true/false"`