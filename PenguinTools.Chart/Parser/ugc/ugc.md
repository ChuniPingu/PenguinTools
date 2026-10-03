File translated from https://gist.github.com/inonote/5c01e73781cab17765a1d93641d52298

# Umiguri Chart v8 Specification

- Basics
    - The file extension is `.ugc`.
    - The character encoding is UTF-8, and line endings may be LF or CRLF.
    - Each line represents one command / note.
    - Any line that does not start with `@` or `#` is ignored.
    - Any line with missing parameters is ignored.
    - Comment lines begin with `'`.
    - Time is specified using the `Bar'Tick` format. This is referred to here as the BarTick format.
      Example: measure 0, tick 240 → `0'240`

- Header lines
    - Parameters are separated by horizontal tabs.
    - Command list and arguments are as follows:

    - VER Version specification
        - Version (always 8)

    - EXVER Extended version specification
        - ExVersion: 0 or 1
            - If set to `1`, it behaves the same as when `FLAG:EXLONG` is forcibly set to `TRUE`.
            - In UMIGURI NEXT, this is always `1`.

    - TITLE Song title
        - Title: song title

    - SORT Sort key specification
        - Key: sort key
            - This is the key used to determine ordering when sorting by song title. Specify a value obtained by
              converting the song title according to the rules below.
                1. Convert Latin letters to uppercase.  
                   `magnet` → `MAGNET`
                2. Remove symbols, including spaces.  
                   `Miracle∞Hinacle` → `MIRACLEHINACLE`
                3. Convert hiragana to katakana, remove dakuten/handakuten, convert small kana to full-size kana, and
                   replace long vowel marks with `ウ`.  
                   `かーてんこーる!!!!!` → `カウテンコウル`
                4. Convert kanji to their reading, then apply the above rules.  
                   `幻想症候群` → `ケンソウシントロウム`
                5. Convert leetspeak, including symbols, back to the original spelling, then apply the above rules.  
                   `^/7(L?[_(L#<>+&l^(o)` → `NYARLATHOTEP`
                6. For languages other than Japanese and English, transliterate into Japanese, then apply the above
                   rules.  
                   `슈퍼히어로` → `シュポヒオロ` → `シユホヒオロ`

    - ARTIST Artist name
        - Artist: artist name

    - GENRE Genre name
        - Genre: genre name
        - Note: If omitted, the name of the parent folder of the song folder is used.

    - DESIGN Chart designer
        - Designer: chart creator

    - DIFF Difficulty
        - Difficulty: BASIC: 0, ADVANCED: 1, EXPERT: 2, MASTER: 3, WORLD'S END: 4, ULTIMA: 5

    - LEVEL Play level
        - Level: play level
            - For WORLD'S END difficulty, this is the number of stars.

    - WEATTR WORLD'S END attribute
        - Attribute: attribute
            - Specify as a single kanji or full-width symbol character.

    - CONST Chart constant
        - Constant: chart constant

    - SONGID Song ID
        - SongId: song ID
            - Different difficulties of the same song should use the same song ID. However, WORLD'S END must use a
              different ID.

    - RLDATE Song addition date
        - Date: addition date in `YYYYMMDD` format

    - BGM Audio file
        - FileName
            - Supported formats: WAV, MP3, OGG, M4A
            - M4A is highly recommended. If the file size is kept around 3 MB, loading speed is much better.

    - BGMOFS Audio offset
        - OffsetTime: offset in seconds
            - Positive: delay playback
            - Negative: start playback earlier
            - It is recommended to set the chart offset to 0 and adjust the offset at the waveform level in the audio
              file instead.

    - BGMPRV Audio preview range
        - StartTime: start position (seconds)
        - EndTime: start position (seconds)

    - JACKET Jacket image
        - FileName
            - Supported formats: PNG, BMP, JPEG, GIF
            - GPU-compressed formats are also supported: DDS (BC1, no mipmaps recommended)
            - A resolution of 400x400 is highly recommended.

    - BGIMG Background image
        - FileName
            - Supported formats: PNG, BMP, JPEG, GIF
            - Video files can also be specified: MP4, AVI

    - BGSCENE Background 3D scene
        - SceneId

    - BGMODE Background mode settings
        - AttrName: attribute name
            - PASSIVE: whether to play ignoring the playback position of the audio source. If the media is too short, it
              will loop.
        - Value: setting value `TRUE` / `FALSE`

    - FLDCOL Field divider line color
        - ColorIndex
            - -1 Auto
            - 0 White
            - 1 Red
            - 2 Orange
            - 3 Yellow
            - 4 Lime
            - 5 Green
            - 6 Teal
            - 7 Blue
            - 8 Purple

    - FLDSCENE Field background 3D scene
        - SceneId

    - TICKS Time resolution
        - Resolution: always 480

    - MAINBPM Base BPM
        - Bpm

    - MAINTIL Base timeline
        - TimelineId: timeline ID (`0` recommended)

    - CLKCNT Number of click sounds
        - Count: number of clicks
        - Note: If omitted, the click sounds play as many times as the numerator of the time signature of the first
          measure.

    - FLAG Flag settings
        - AttrName: attribute name
            - `DIFFTTL`: whether this is a tutorial chart (always specify `FALSE`)
            - `SOFFSET`: whether to insert one blank measure at the beginning
            - `CLICK`: whether to play click sounds
            - `EXLONG`: whether to use ExLong
            - `BGMWCMP`: whether to wait until audio playback finishes
            - `HIPRECISION`: whether to use high-resolution values for AIR notes
        - Value: setting value `TRUE` / `FALSE`

    - BPM BPM definition
        - BarTick
        - Bpm

    - BEAT Time signature definition
        - Bar: measure position
        - Numer: numerator
        - Denom: denominator

    - TIL Timeline definition
        - TimelineId
        - BarTick
        - Speed

    - SPDMOD Note speed definition
        - BarTick
        - Speed

    - SPDDEF Extended note speed definition (UMGR v2.01)
        - SpdId: definition ID
        - OffsetTick
        - Speed

    - SPDFLD Extended note speed application field (UMGR v2.01)
        - SpdId: definition ID
        - BarTick
        - X: target X position
        - Width: target width
        - Length: target length

    - USETIL Timeline ID specification
        - TimelineId
        - Note: This is a special command that specifies the timeline ID to which subsequent note lines belong.

- Note lines
    - Basics
        - Parent notes
            - Format: `#BarTick:txw`
                - t: note type
                - x: horizontal note position (base 36)
                - w: note width (base 36)
        - Child notes
            - Format: `#OffsetTick>txw` (also accepted: `#OffsetTick:txw`)
                - t: note type
                - x: horizontal note position (base 36)
                - w: note width (base 36)

    - CLICK
        - `#BarTick:t`
            - t = `c`

    - TAP
        - `#BarTick:txw`
            - t = `t`

    - EXTAP
        - `#BarTick:txwd`
            - t = `x`
            - d: effect
                - `U` Up
                - `D` Down
                - `C` Center
                - `A` Clockwise
                - `W` Counterclockwise
                - `L` Right
                - `R` Left
                - `I` In/Out

    - FLICK
        - `#BarTick:txwd`
            - t = `f`
            - d: effect direction during autoplay
                - `A` Auto
                - `L` Right
                - `R` Left

    - DAMAGE
        - `#BarTick:txw`
            - t = `d`

    - HOLD
        - `#BarTick:txw`
            - t = `h`
        - Child notes
            - Endpoint
                - `#OffsetTick>t`
                - t = `s`

    - SLIDE
        - `#BarTick:txw`
            - t = `s`
        - Child notes
            - Intermediate point / endpoint
                - `#OffsetTick>txw`
                - t = `s`
            - Control point
                - `#OffsetTick>txw`
                - t = `c`

    - AIR
        - `#BarTick:txwddc`
            - t = `a`
            - dd: direction
                - `UC` Up
              - `UL` Up-left
              - `UR` Up-right
                - `DC` Down
              - `DL` Down-left
              - `DR` Down-right
            - c: color
                - `N` Normal
                - `I` Inverted

    - AIR-HOLD
        - `#BarTick:txwc` or `#BarTick:txwhhc`
            - t = `H`
          - The `txwc` form uses the default AIR-HOLD height, raw value `80`
          - hh: optional height, represented as a 2-digit base-36 value equal to the original value multiplied by 10
            - c: color
                - `N` Normal
                - `I` Inverted
        - Child notes
            - Intermediate point / endpoint
                - `#OffsetTick>t`
                - t = `s`
              - Inherits the parent AIR-HOLD height
              - `#OffsetTick>txwhh` when height is present
              - hh: height, represented as a 2-digit base-36 value equal to the original value multiplied by 10
            - Endpoint without AIR-ACTION
                - `#OffsetTick>t`
                - t = `c`
              - Inherits the parent AIR-HOLD height
              - `#OffsetTick>txwhh` when height is present
              - hh: height, represented as a 2-digit base-36 value equal to the original value multiplied by 10

    - AIR-SLIDE
        - `#BarTick:txwhhc`
            - t = `S`
            - hh: height, represented as a 2-digit base-36 value equal to the original value multiplied by 10
            - c: color
                - `N` Normal
                - `I` Inverted
        - Child notes
            - Intermediate point / endpoint
                - `#OffsetTick>txwhh`
                - t = `s`
                - hh: height, represented as a 2-digit base-36 value equal to the original value multiplied by 10
            - Control point / endpoint without AIR-ACTION
                - `#OffsetTick>txwhh`
                - t = `c`
                - hh: height, represented as a 2-digit base-36 value equal to the original value multiplied by 10

    - AIR-CRUSH
        - `#BarTick:txwhhc,{interval}`
            - t = `C`
            - hh: height, represented as a 2-digit base-36 value equal to the original value multiplied by 10
            - c: color
                - `0`: Normal
                - `1`: Red
                - `2`: Orange
                - `3`: Yellow
                - `4`: Yellow-green
                - `5`: Green
                - `6`: Aqua
                - `7`: Sky blue
                - `8`: Cyan
                - `9`: Blue
                - `A`: Blue-violet
                - `Y`: Red-violet
                - `B`: Pink
                - `C`: White
                - `D`: Black
                - `Z`: Transparent
            - {interval}: decimal note placement interval
                - If `0`, it becomes AIR-TRACE; if `$`, notes that generate combo only at the starting point are placed.
        - Child notes
            - Endpoint
                - `#OffsetTick>txwhh`
                - t = `c`
                - hh: height, represented as a 2-digit base-36 value equal to the original value multiplied by 10
