# Project architecture

The CLI is a command adapter around application services. Chart and media projects handle conversion, workflow projects coordinate exports, and infrastructure supplies file-system and external-tool implementations.

| Project                       | Responsibility                                                     |
| ----------------------------- | ------------------------------------------------------------------ |
| `PenguinTools.CLI`            | Command definitions, exit codes, progress, and JSON/text output    |
| `PenguinTools.Application`    | Application entry points and request/result contracts              |
| `PenguinTools.Core`           | Shared metadata, diagnostics, messages, IO helpers, and interfaces |
| `PenguinTools.Chart`          | Parse, model, validate, post-process, and write charts             |
| `PenguinTools.Media`          | Audio, jacket, stage, and container operations                     |
| `PenguinTools.Workflow`       | Scan charts and coordinate music/option exports and caches         |
| `PenguinTools.Infrastructure` | Asset storage, paths, and native tool execution                    |
| `PenguinTools.Assets`         | Asset build/copy configuration                                     |
| `PenguinTools.CRI`            | In-process CRI audio conversion and extraction                     |
| `PenguinTools.Tests`          | Unit tests and optional sample/native integration tests            |

Treat application request/result records and CLI JSON output as interfaces for callers. Preserve message keys and named arguments, diagnostic locations, cancellation, exit codes, and numeric identifier representation when changing them.

## Charts

Parsers and writers live in `PenguinTools.Chart/Parser/` and `Writer/`. Shared models and post-processing connect the supported formats.

Conversion tests cover metadata, timing, note relationships, payloads, and C2S round-trip information stored in MGXC bookmarks. A change to parsing or writing needs tests for the affected behavior and round-trip preservation where relevant.

[Format references](formats/README.md) describe syntax and provenance. A source specification does not imply that every command is implemented; check the parser and its tests.

## Messages

`PenguinTools.Core` defines `MessageDescriptor`, `Msg`, and `MsgKeys`. Diagnostics carry message keys and named arguments rather than already translated text.

English and Simplified Chinese catalogs are in `docs/locales/`. Keep keys and placeholders aligned across catalogs. Callers can translate these descriptors without parsing CLI prose.

## Native dependencies

`External/` contains independent Git submodules for mua, FFmpeg, SonicAudioTools, and VGAudio. The Windows publish pipeline supplies native executables under the CLI's `assets/` directory. CRI conversion and extraction run through the managed CRI library in the CLI process, return typed results, and propagate cancellation through codec progress callbacks. HCA extraction disables codec console logging to preserve CLI JSON output.

Dependency source changes and submodule reference updates are separate reviewable changes. Build output and proprietary local samples do not belong in source control. See [development](development.md) and [testing](testing.md).
