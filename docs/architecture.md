# Project architecture

The CLI is a command adapter around application services. Chart and media projects handle conversion, workflow projects coordinate exports, and infrastructure supplies file-system and external-tool implementations.

| Project                       | Responsibility                                                     |
| ----------------------------- | ------------------------------------------------------------------ |
| `PenguinTools.CLI`            | Command definitions, exit codes, progress, and JSON/text output    |
| `PenguinTools.Application`    | Application entry points and request/result contracts              |
| `PenguinTools.Core`           | Shared metadata, diagnostics, messages, IO helpers, and interfaces |
| `PenguinTools.Chart`          | Parse, model, validate, post-process, and write charts             |
| `PenguinTools.Media`          | Audio and image operations, CRI codecs, DDS, and AFB handling        |
| `PenguinTools.Workflow`       | Scan charts and coordinate music/option exports and caches         |
| `PenguinTools.Infrastructure` | Asset storage, paths, and media integration                        |
| `PenguinTools.Assets`         | Asset build/copy configuration                                     |
| `PenguinTools.Tests`          | Unit tests and optional sample/native integration tests            |

Treat application request/result records and [CLI JSON output](cli.md#json-output-and-scripts) as interfaces for callers. Preserve message keys and named arguments, diagnostic locations, cancellation, exit codes, and numeric identifier representation when changing them.

Media operations are split between `IAudioService` and `IImageService`. `AudioService` supplies FFmpeg and CRI operations; `ImageService` handles image-tools, texconv, and AFB operations directly. DDS extraction treats game texture colors as sRGB and preserves their stored values when writing PNGs. `ConvertRasterToPngAsync` normalizes raster color and orientation, preserves dimensions and alpha, and writes sRGB PNGs. Raster inputs use embedded ICC/sRGB declarations before standalone PNG gamma; resizing uses linear light with premultiplied alpha. The application composes both services, and converters depend on the service they use. Chart parsers depend only on `IMediaValidator`; `MediaValidator` combines the two services' checks, and `ScanMediaValidator` shares their results within a directory scan.

## Charts

[Parsers](../PenguinTools.Chart/Parser/) and [writers](../PenguinTools.Chart/Writer/) connect the [editor models](../PenguinTools.Chart/Models/umgr/) and [C2S models](../PenguinTools.Chart/Models/c2s/) through conversion and post-processing. `ChartExtras` carries editor extensions and round-trip state. UGC and C2S share curve expansion, and writers operate on a copy of the chart.

[Chart conversion](chart-conversion.md) defines supported conversions, timing, approximation, and round-trip behavior. [Format references](formats/README.md) describe file syntax and provenance; [testing](testing.md) covers regression and fixture requirements.

Directory scans share audio and image validation tasks for files with the same full path, length, and last-write time. This reuse is scoped to one scan; each chart retains its own diagnostics, and scan cancellation reaches the shared decoder. Option export caching separately verifies input and output content hashes.

## Messages

`PenguinTools.Core` defines `MessageDescriptor`, `Msg`, and `MsgKeys`. Diagnostics carry message keys and named arguments rather than already translated text.

English and Simplified Chinese catalogs are in `locales/`. Keep keys and placeholders aligned across catalogs. Callers can translate these descriptors without parsing CLI prose. The `titles` section mirrors message key paths for compact UI headings, using the same descriptor arguments. The original message remains the explanation. If a title is absent, callers fall back to the original message.

Write diagnostic text for chart users. Titles name the problem; explanations state the relevant input or the effect on the result in plain language. Keep the note names and settings users need to find the problem, but avoid implementation terms such as rounding or parsing when a direct description is sufficient. Do not repeat the title or add generic advice to every message. Describe only effects supported by the diagnostic's context.

Context-specific display variants also belong in the catalogs. `titles.diag.mg.length_smaller_than_unit_typed` accepts a display `type` when the note type is available. `details.diag.mg.length_smaller_than_unit` describes coincident endpoints and applies only when the segment's converted length is zero; other short segments use the original message.

`diag.error.invalid_header` is a legacy key also used for invalid or incompatible chart values. Its arguments are the value and the expected value or format; it does not always refer to a file header.

## External dependencies

`External/` contains independent Git submodules for SonicAudioTools and VGAudio.

Dependency source changes and submodule reference updates are separate reviewable changes. Build output and proprietary local samples do not belong in source control. See [development](development.md) and [testing](testing.md).
