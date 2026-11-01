# Chart conversion

PenguinTools conversion behavior and compatibility rules. See [CLI usage](cli.md) for commands and configuration, [format references](formats/README.md) for file syntax, and [architecture](architecture.md#charts) for implementation structure.

## Formats and versions

| Input | Available outputs |
| ----- | ----------------- |
| MGXC, UGC, SUS | MGXC, UGC, C2S |
| C2S | MGXC, UGC |

Editor output uses MGXC v4 or UGC v9. MGXC versions above 4 and UGC versions above 9 fail with `diag.error.chart_version_too_new`; [option batches](cli.md#partial-option-batches) can continue with the supported inputs.

The format references describe the source specifications. Implementation coverage is determined by the parsers, writers, and their tests.

## Input compatibility

MGXC and UGC inputs use the official semantics of their declared versions, including older official versions.

For UGC height values, PenguinTools maps the decoded value to its internal height units directly when `HIPRECISION` is enabled, and multiplies it by 10 otherwise.

## Timing and coordinate origins

For an MGXC/UGC chart with `sofs`/`SOFFSET` enabled, audio export inserts one measure of silence. Its length uses the initial time signature, independently of the manual audio offset.

C2S conversion shifts notes, long-note endpoints, speed intervals, and later tempo/meter events by that measure; the initial tempo and meter stay at tick zero. Speed intervals starting at zero remain active during the silence and extend to the shifted endpoint.

C2S imports retain their coordinates even after edits invalidate saved snapshots. The C2S coordinate origin is retained in MGXC/UGC round-trip metadata.

For example, at an initial BPM of 120 with `SOFFSET` enabled, a 4/4 chart receives 2 seconds of opening silence; a 3/4 chart receives 1.5 seconds. Notes and later timing events move by the corresponding measure. The initial BPM and meter remain at the start, and the manual audio offset is applied independently.

Overlapping C2S SLP/SFL/STP intervals become TIL speeds of `1 + Σ(speed − 1)` at each interval boundary. SLA placement includes generated AIR-CRUSH rungs as well as explicit note endpoints. C2S output assigns higher timeline IDs to nested objects when needed, copying their complete scroll curves without changing editor timelines. Conversion warns only when the resulting scroll curve differs, such as coincident objects with incompatible curves.

Overlapping DCM intervals use the first active nonzero speed in start-time order, retaining statement order for equal starts. Editor SPDMOD events follow that effective speed; unedited round trips retain the original intervals.

## Approximation and round trips

MGXC retains Bezier handles; UGC and C2S expand curves. Unsupported geometry, AIR colors, and meter changes are approximated with `diag.warn.editor_approximation`; original values are retained in checksum-validated metadata. SLIDE line visibility follows the start without a warning.

Saved C2S record order is restored only when it preserves paths and AIR relationships. C2S judgement totals are recalculated from the final emitted records, including accepted AIR attachments, `PROGJUDGE_AER`, and tutorial stop exclusions. Compressed round-trip metadata has a 64 MiB decoded-size limit.

MGXC stores EXTAP roles in validated metadata without repurposing native height fields. Note edits invalidate roles in the same timing group; title and artist edits preserve them.

In UGC output, PenguinTools retains original C2S roles and Trace attributes in validated metadata. Title and author edits preserve restoration; note edits invalidate the affected group, including AIR attachments and EX coverage. Timing changes invalidate all note snapshots. Meters and SLA regions have separate checks, so unrelated note edits can preserve them.

Two examples illustrate which information survives edits:

- After C2S → MGXC, moving a note and converting back to C2S retains the edited position without inserting an additional opening measure, provided the origin metadata remains present.
- After C2S → UGC, unchanged speed events can restore their saved C2S intervals. Editing a speed or reordering events at the same position invalidates the matching snapshot, so conversion uses the edited events.
