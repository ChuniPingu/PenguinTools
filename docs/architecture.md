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

Conversion tests cover metadata, timing, note relationships, payloads, and C2S round-trip information stored in MGXC bookmarks. A change to parsing or writing needs tests for the affected behavior and round-trip preservation where relevant.

For native MGXC/UGC charts with `sofs`/`SOFFSET` enabled, audio export adds one initial measure of silence. C2S conversion shifts notes, long-note endpoints, speed intervals, and later tempo/meter events by the same measure; the initial tempo and meter stay at tick zero. The measure length uses the initial time signature, independently of the manual audio offset. C2S imports retain their existing coordinates, including after edits invalidate saved snapshots. This origin is recorded in `ChartExtras.C2sCoordinateOrigin` and persists in MGXC/UGC round-trip metadata; older imports are recognized by their source key or source snapshot.

[Format references](formats/README.md) describe syntax and provenance. A source specification does not imply that every command is implemented; check the parser and its tests.

## Messages

`PenguinTools.Core` defines `MessageDescriptor`, `Msg`, and `MsgKeys`. Diagnostics carry message keys and named arguments rather than already translated text.

English and Simplified Chinese catalogs are in `docs/locales/`. Keep keys and placeholders aligned across catalogs. Callers can translate these descriptors without parsing CLI prose.

## External dependencies

`External/` contains independent Git submodules for SonicAudioTools and VGAudio.

Dependency source changes and submodule reference updates are separate reviewable changes. Build output and proprietary local samples do not belong in source control. See [development](development.md) and [testing](testing.md).
