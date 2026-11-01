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

Editor output uses MGXC v4 or UGC v9. `music extract` defaults to MGXC; use `--chart-format ugc` for UGC. `chart inspect` fully parses supported versions. Newer versions fail with `diag.error.chart_version_too_new`.

`--input-dialect` selects legacy Original/Extended input semantics. MGXC v3/v4 and UGC v9 use their version's official semantics.

MGXC retains Bezier handles; UGC and C2S share a curve expander. Writers operate on a copy. Unsupported geometry, AIR colors, and meter changes are approximated with `diag.warn.editor_approximation`; original values are retained in checksum-validated metadata. SLIDE line visibility follows the start without a warning.

Saved C2S record order is restored only when it preserves paths and AIR relationships. Compressed round-trip metadata has a 64 MiB decoded-size limit.

For an Original MGXC/UGC chart with `sofs`/`SOFFSET` enabled, audio export inserts one measure of silence. Its length uses the initial time signature, independently of the manual audio offset.

C2S conversion shifts notes, long-note endpoints, speed intervals, and later tempo/meter events by that measure; the initial tempo and meter stay at tick zero. Speed intervals starting at zero remain active during the silence and extend to the shifted endpoint.

Legacy Extended input uses fixed zero-based coordinates without this extra measure. C2S imports retain their coordinates even after edits invalidate saved snapshots. `ChartExtras.C2sCoordinateOrigin` records the origin in MGXC/UGC round-trip metadata; older imports are recognized by their source key or snapshot.

[Format references](formats/README.md) describe syntax and provenance. A source specification does not imply that every command is implemented; check the parser and its tests.

## Option scanning

During option scanning and building, an unsupported chart version is skipped with a path-specific `diag.warn.chart_skipped_version`. Supported charts continue through the batch. Scan/build results expose `skippedCharts` and `isPartial`; an incomplete batch has `Succeeded = false` and CLI exit code 1 while retaining its usable result data and exported artifacts. The CLI reports `cli.msg.option_partial` instead of the completion message. If every input is skipped, building fails with the per-file reasons and the no-charts diagnostic. These fields count version-related skips; other existing diagnostics retain their behavior.

`--chart-file-discovery` accepts a comma-separated list of MGXC, UGC, and SUS formats. If a song ID and difficulty occur in several formats, the first format in the list takes priority. `--batch-size` sets the maximum parallelism; `-1` selects automatic parallelism.

`option build` reads its option name and other settings from the configuration unless they are overridden on the command line. Without a configured name, provide a four-character `--option-name`. Set a song's main difficulty with `--main-difficulty 1234:Master`, using its numeric song ID. Invalid format lists and main-difficulty values produce structured diagnostics.

## Messages

`PenguinTools.Core` defines `MessageDescriptor`, `Msg`, and `MsgKeys`. Diagnostics carry message keys and named arguments rather than already translated text.

English and Simplified Chinese catalogs are in `docs/locales/`. Keep keys and placeholders aligned across catalogs. Callers can translate these descriptors without parsing CLI prose.

`diag.error.invalid_header` is a legacy key also used for invalid or incompatible chart values. Its arguments are the value and the expected value or format; it does not always refer to a file header.

## External dependencies

`External/` contains independent Git submodules for SonicAudioTools and VGAudio.

Dependency source changes and submodule reference updates are separate reviewable changes. Build output and proprietary local samples do not belong in source control. See [development](development.md) and [testing](testing.md).
