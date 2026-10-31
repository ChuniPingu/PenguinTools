# Project architecture

The CLI is a command adapter around application services. Chart and media projects handle conversion, workflow projects coordinate exports, and infrastructure supplies file-system and external-tool implementations.

| Project                       | Responsibility                                                     |
| ----------------------------- | ------------------------------------------------------------------ |
| `PenguinTools.CLI`            | Command definitions, exit codes, progress, and JSON/text output    |
| `PenguinTools.Application`    | Application entry points and request/result contracts              |
| `PenguinTools.Core`           | Shared metadata, diagnostics, messages, IO helpers, and interfaces |
| `PenguinTools.Chart`          | Parse, model, validate, post-process, and write charts             |
| `PenguinTools.Media`          | Audio, jacket, stage, and container operations                     |
| `PenguinTools.Image`          | In-process raster processing, DDS conversion, and AFB handling     |
| `PenguinTools.Workflow`       | Scan charts and coordinate music/option exports and caches         |
| `PenguinTools.Infrastructure` | Asset storage, paths, and native tool execution                    |
| `PenguinTools.Assets`         | Asset build/copy configuration                                     |
| `PenguinTools.CRI`            | In-process CRI audio conversion and extraction                     |
| `PenguinTools.Tests`          | Unit tests and optional sample/native integration tests            |

Treat application request/result records and CLI JSON output as interfaces for callers. Preserve message keys and named arguments, diagnostic locations, cancellation, exit codes, and numeric identifier representation when changing them.

## Charts

Parsers and writers live in `PenguinTools.Chart/Parser/` and `Writer/`. Shared models and post-processing connect the supported formats.

MGXC, UGC, and SUS inputs can be written as MGXC, UGC, or C2S. C2S inputs can be written as MGXC or UGC.

Conversion tests cover metadata, timing, note relationships, payloads, and C2S round-trip information stored in MGXC bookmarks. A change to parsing or writing needs tests for the affected behavior and round-trip preservation where relevant.

MGXC header versions above 2 and UGC versions above 8 fail with `diag.error.chart_version_too_new` before note parsing, even when an input dialect is specified. UGC checks the version before the creator and dialect. Failed conversions preserve existing output files.

`chart inspect` and `chart convert` default to `--input-dialect Auto`, matching the application API. Auto honors embedded format metadata and supported legacy markers; ordinary unmarked editor charts use Original. Explicit `--input-dialect Original` or `Extended` overrides detection. Conversion selects the output mode independently with `--output-dialect`; its Auto default preserves the detected editor mode and uses Extended for C2S input. This lets default CLI conversions read their own output using the same coordinate system, including non-4/4 UGC charts.

For an Original MGXC/UGC chart with `sofs`/`SOFFSET` enabled, audio export inserts one measure of silence. Its length uses the initial time signature, independently of the manual audio offset.

C2S conversion shifts notes, long-note endpoints, speed intervals, and later tempo/meter events by that measure; the initial tempo and meter stay at tick zero. Speed intervals starting at zero remain active during the silence and extend to the shifted endpoint.

Extended mode uses fixed zero-based coordinates without this extra measure. C2S imports retain their coordinates even after edits invalidate saved snapshots. `ChartExtras.C2sCoordinateOrigin` records the origin in MGXC/UGC round-trip metadata; older imports are recognized by their source key or snapshot.

[Format references](formats/README.md) describe syntax and provenance. A source specification does not imply that every command is implemented; check the parser and its tests.

## Option scanning

`--chart-file-discovery` accepts a comma-separated list of MGXC, UGC, and SUS formats. If a song ID and difficulty occur in several formats, the first format in the list takes priority. `--batch-size` sets the maximum parallelism; `-1` selects automatic parallelism.

`option build` reads its option name and other settings from the configuration unless they are overridden on the command line. Without a configured name, provide a four-character `--option-name`. Set a song's main difficulty with `--main-difficulty 1234:Master`, using its numeric song ID. Invalid format lists and main-difficulty values produce structured diagnostics.

## Messages

`PenguinTools.Core` defines `MessageDescriptor`, `Msg`, and `MsgKeys`. Diagnostics carry message keys and named arguments rather than already translated text.

English and Simplified Chinese catalogs are in `docs/locales/`. Keep keys and placeholders aligned across catalogs. Callers can translate these descriptors without parsing CLI prose.

`diag.error.invalid_header` is a legacy key also used for invalid or incompatible chart values. Its arguments are the value and the expected value or format; it does not always refer to a file header.

## External dependencies

`External/` contains independent Git submodules for SonicAudioTools and VGAudio.

Dependency source changes and submodule reference updates are separate reviewable changes. Build output and proprietary local samples do not belong in source control. See [development](development.md) and [testing](testing.md).
