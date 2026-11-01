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

Treat application request/result records and [CLI JSON output](cli.md#json-output-and-scripts) as interfaces for callers. Preserve message keys and named arguments, diagnostic locations, cancellation, exit codes, and numeric identifier representation when changing them.

## Charts

[Parsers](../PenguinTools.Chart/Parser/) and [writers](../PenguinTools.Chart/Writer/) connect the [editor models](../PenguinTools.Chart/Models/umgr/) and [C2S models](../PenguinTools.Chart/Models/c2s/) through conversion and post-processing. `ChartExtras` carries editor extensions and round-trip state. UGC and C2S share curve expansion, and writers operate on a copy of the chart.

[Chart conversion](chart-conversion.md) defines supported conversions, timing, approximation, and round-trip behavior. [Format references](formats/README.md) describe file syntax and provenance; [testing](testing.md) covers regression and fixture requirements.

Directory scans share audio and image validation tasks for files with the same full path, length, and last-write time. This reuse is scoped to one scan; each chart retains its own diagnostics, and scan cancellation reaches the shared decoder. Option export caching separately verifies input and output content hashes.

## Messages

`PenguinTools.Core` defines `MessageDescriptor`, `Msg`, and `MsgKeys`. Diagnostics carry message keys and named arguments rather than already translated text.

English and Simplified Chinese catalogs are in `locales/`. Keep keys and placeholders aligned across catalogs. Callers can translate these descriptors without parsing CLI prose.

`diag.error.invalid_header` is a legacy key also used for invalid or incompatible chart values. Its arguments are the value and the expected value or format; it does not always refer to a file header.

## External dependencies

`External/` contains independent Git submodules for SonicAudioTools and VGAudio.

Dependency source changes and submodule reference updates are separate reviewable changes. Build output and proprietary local samples do not belong in source control. See [development](development.md) and [testing](testing.md).
