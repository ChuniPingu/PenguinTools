# Chart conversion

PenguinTools conversion behavior and compatibility rules. See [CLI usage](cli.md) for commands and configuration, [format references](formats/README.md) for file syntax, and [architecture](architecture.md#charts) for implementation structure.

## Formats and versions

| Input | Available outputs |
| ----- | ----------------- |
| MGXC, UGC, SUS | MGXC, UGC, C2S |
| C2S | MGXC, UGC |

Editor output uses MGXC v4 or UGC v9. MGXC versions above 4 and UGC versions above 9 fail with `diag.error.chart_version_too_new`; [option batches](cli.md#partial-option-batches) can continue with the supported inputs.

The format references describe the source specifications. Implementation coverage is determined by the parsers, writers, and their tests.

Writing MGXC or UGC to another directory rebases relative media paths to keep the same referenced files. MGXC text fields exceeding 65,535 UTF-8 bytes are truncated at a complete character boundary with a warning.

Use binary MGXC only. Do not use text MGXC: PenguinTools does not support it, and Margrete has deprecated it.

## Input compatibility

MGXC and UGC inputs use the official semantics of their declared versions, including older official versions.

Invalid optional metadata (C2S statistics and BPM summaries, metronome settings, and `#meta` arguments) is skipped with a warning.

MGXC heights use `HSCL` (default 1) to convert to internal tenths. MGXC v4 limits rescaled heights to 0–360 when `HSCL` differs from 10, matching Margrete; its usual `HSCL=10` representation retains larger heights. Nonpositive, nonfinite, or unrepresentable scales fail with `diag.mgcrit.invalid_height_scale`.

C2S `ASO`, `HHD`, and `HHX` objects are not supported. They are skipped with `diag.c2s.unsupported_note_type` warnings; conversion continues with the remaining objects.

UGC output uses `HIPRECISION` (internal heights 0–1295) and silently clamps unencodable heights, keeping their originals in validated metadata.

UGC `SPDDEF`/`SPDFLD` relative note-speed definitions are retained when writing UGC and in MGXC metadata. Their playback effects are not reproduced by MGXC or C2S output; conversion reports `diag.warn.ugc_relative_speed_not_reproduced` when a non-default definition applies to a note. Unused definitions and lossless UGC saves do not produce this warning.

## Timing and coordinate origins

Editor BPM and speed input/output preserves native float32 rounding at timing and judgement thresholds. UGC numbers first use the native double interpretation. Editor output retains original higher precision values in metadata validated separately for each numeric group; editing a group's native values prevents its restoration.

Numeric values outside the supported range or precision are adjusted with `diag.warn.number_adjusted`, which reports the original and replacement values. Representable speeds, including 10000 and 100000, are preserved. Only when a speed cannot be represented, an excessive magnitude falls back to ±100000, `NaN` to 1, and a tiny nonzero value to ±1e-28. Keeping the sign avoids C2S treating a zero speed modifier differently. Unrepresentable large positive BPM falls back to 100000; other unrepresentable BPM uses 150. Unrepresentable audio offsets and preview positions use 0. Adjusted C2S source records are normalized before retaining round-trip metadata so the original unrepresentable values cannot reappear on export. Malformed numeric text in notes and timing events remains an error.

C2S files without `CLK_DEF` keep the metronome disabled during conversion.

For an MGXC/UGC chart with `sofs`/`SOFFSET` enabled, audio export inserts one measure of silence. Its length uses the initial time signature, independently of the manual audio offset.

C2S conversion shifts notes, long-note endpoints, speed intervals, and later tempo/meter events by that measure; the initial tempo and meter stay at tick zero. Speed intervals starting at zero remain active during the silence and extend to the shifted endpoint. Conversion diagnostic times refer to the source chart, before this shift; short-segment warnings are determined from the rounded C2S endpoints.

C2S imports retain their coordinates even after edits invalidate saved snapshots. The C2S coordinate origin is retained in MGXC/UGC round-trip metadata.

For example, at an initial BPM of 120 with `SOFFSET` enabled, a 4/4 chart receives 2 seconds of opening silence; a 3/4 chart receives 1.5 seconds. Notes and later timing events move by the corresponding measure. The initial BPM and meter remain at the start, and the manual audio offset is applied independently.

Editor saves retain speed events after the last note and on unused timelines.

Overlapping C2S SLP/SFL/STP intervals become TIL speeds of `1 + Σ(speed − 1)` at each interval boundary. SLA placement includes generated AIR-CRUSH rungs as well as explicit note endpoints. C2S output assigns higher timeline IDs to nested objects when needed, copying their complete scroll curves without changing editor timelines. Conversion warns only when the resulting scroll curve differs, such as coincident objects with incompatible curves.

Overlapping DCM intervals use the first active nonzero speed in start-time order, retaining statement order for equal starts. Editor SPDMOD events follow that effective speed; unedited round trips retain the original intervals. Native editor `SPDMOD 0` can stop a note's scrolling, whereas C2S ignores `DCM 0`. C2S conversion reports `diag.warn.zero_note_speed_not_reproduced` when that zero modifier affects a note on a defined, nonzero TIL segment; MGXC and UGC saves preserve it without this warning.

UMIGURI ignores SPDMOD on a timeline without a TIL definition. Margrete can create this state by assigning notes to another timeline without adding scroll-speed events to that timeline, then setting a note-speed modifier. For MGXC and UGC input, C2S conversion reports `diag.warn.editor_undefined_timeline_speed` if a nonzero, nonunit modifier would become active in C2S and change a note's scrolling.

## Approximation and round trips

MGXC retains Bezier handles; UGC and C2S expand curves. Unsupported geometry, AIR colors, and meter changes are approximated with `diag.warn.editor_approximation`; original values are retained in checksum-validated metadata. SLIDE line visibility follows the start without a warning.

For AIR-HOLD and AIR-SLIDE, Invert is set on the AIR arrow and applies to the entire long note. MGXC input uses the leading AIR record's inversion, and MGXC, UGC, and C2S output retain it for the whole note. The long-note record does not provide an independent body-color setting.

Saved C2S record order is restored only when it preserves paths and AIR relationships. C2S judgement totals are recalculated from the final emitted records, including accepted AIR attachments, `PROGJUDGE_AER`, and tutorial stop exclusions. Compressed round-trip metadata has a 64 MiB decoded-size limit.

AIR-CRUSH axes `N/Y/Z` map to C2S `DEF/AxisY/AxisZ`. The separate C2S `Trace` style uses default X-axis lines in MGXC/UGC, with the original attribute restored from validated metadata. Colors follow palette appearance, including `PNK → Y` and `VLT → B`.

MGXC stores EXTAP roles in validated metadata without repurposing native height fields. Note edits invalidate roles in the same timing group; title and artist edits preserve them.

In UGC output, PenguinTools retains original C2S roles in validated metadata. Title and author edits preserve restoration; note edits invalidate the affected group, including AIR attachments and EX coverage. Timing changes invalidate all note snapshots. Meters and SLA regions have separate checks, so unrelated note edits can preserve them.

Two examples illustrate which information survives edits:

- After C2S → MGXC, moving a note and converting back to C2S retains the edited position without inserting an additional opening measure, provided the origin metadata remains present.
- After C2S → UGC, unchanged speed events can restore their saved C2S intervals. Editing a speed or reordering events at the same position invalidates the matching snapshot, so conversion uses the edited events.
