# Project architecture

The CLI is a command adapter around application services. Chart and media projects handle conversion, workflow projects coordinate exports, and infrastructure supplies file-system and external-tool implementations.

| Project                       | Responsibility                                                     |
| ----------------------------- | ------------------------------------------------------------------ |
| `PenguinTools.CLI`            | Command definitions, exit codes, progress, and JSON/text output    |
| `PenguinTools.Application`    | Application entry points and request/result contracts              |
| `PenguinTools.Core`           | Shared metadata, diagnostics, messages, IO helpers, and interfaces |
| `PenguinTools.Chart`          | Parse, model, validate, post-process, and write charts             |
| `PenguinTools.Media`          | Audio and image operations, CRI codecs, DDS, and AFB handling        |
| `PenguinTools.Workflow`       | Scan charts and coordinate option/media exports and caches         |
| `PenguinTools.Infrastructure` | Asset storage, paths, and media integration                        |
| `PenguinTools.Assets`         | Asset build/copy configuration                                     |
| `PenguinTools.Tests`          | Unit tests and optional sample/native integration tests            |

Treat application request/result records and [CLI JSON output](cli.md#json-output-and-scripts) as interfaces for callers. Preserve message keys and named arguments, diagnostic locations, cancellation, exit codes, and numeric identifier representation when changing them.

Media operations are split between `IAudioService` and `IImageService`. `AudioService` supplies FFmpeg and CRI operations; `ImageService` handles image-tools, texconv, and AFB operations directly. DDS extraction treats game texture colors as sRGB and preserves their stored values when writing PNGs. `ConvertRasterToPngAsync` normalizes raster color and orientation, preserves dimensions and alpha, and writes sRGB PNGs. Raster inputs use embedded ICC/sRGB declarations before standalone PNG gamma; resizing uses linear light with premultiplied alpha. The application composes both services, and converters depend on the service they use. Chart parsers depend only on `IMediaValidator`; `MediaValidator` combines the two services' checks, and `ScanMediaValidator` shares their results within a directory scan.

## Charts

[Parsers](../PenguinTools.Chart/Parser/) and [writers](../PenguinTools.Chart/Writer/) connect the [editor models](../PenguinTools.Chart/Models/umgr/) and [C2S models](../PenguinTools.Chart/Models/c2s/) through conversion and post-processing. `ChartExtras` carries editor extensions and round-trip state. UGC and C2S share curve expansion, and writers operate on a copy of the chart.

[Chart conversion](chart-conversion.md) defines supported conversions, timing, approximation, and round-trip behavior. [Format references](formats/README.md) describe file syntax and provenance; [testing](testing.md) covers regression and fixture requirements.

Directory scans share audio and image validation tasks for files with the same full path, length, and last-write time. This reuse is scoped to one scan; each chart retains its own diagnostics, and scan cancellation reaches the shared decoder. Option export caching separately verifies input and output content hashes.

## Incremental builds

`IPenguinToolsApplication.CreateBuildSession()` creates a disposable, single-document `IncrementalBuildSession`. The application must outlive its sessions. `PrepareAsync` parses one chart and uses that model for the summary, metadata, and option export. UGC content is parsed in memory with a virtual chart path below `BaseDirectory`, so relative media references retain their source directory. Saved UGC, MGXC, and SUS inputs use `InputPath`.

Each successful build owns a complete private cache tree. `OptionConversionCache.ForSnapshot` clones cache state and copies verified outputs into an empty candidate tree when input hashes and conversion recipes match. The existing recipes cover audio offsets, preview bounds, encryption keys, and image settings. BPM and meter changes that alter the leading audio offset therefore invalidate audio. Successful media validation is cached by path, media type, and SHA-256 content within the application's fixed service configuration. Missing or changed files are checked again.

C2S reuse requires identical input bytes and source identity within the session. Chart conversion diagnostics accompany that cached output. An identical in-memory UGC request can reuse the entire completed build when configuration bytes, overrides, calendar date, and all successfully validated media hashes match. Failed media validation disables this shortcut, so missing resources that appear later are discovered. Other requests collect parsing diagnostics again. Changes inside chart text conservatively run C2S conversion, including edits to media paths. Generated metadata and C2S have independent artifact hashes, so clients can retain a loaded chart when its resulting bytes are unchanged.

Publication copies the completed tree into the requested empty destination using exclusive file creation. A caller consumes that tree after the successful result; the result follows completion and closure of all writes. The session commits its cache after publication succeeds. Failure preserves the preceding cache and published snapshots; a failed destination is not a usable snapshot. Only artifacts requested by the current build enter the candidate tree; removed optional resources are absent. Disposal removes session-owned work files and leaves caller-owned snapshots intact. The [session protocol](cli.md#incremental-build-session) exposes the same service over JSONL.

### Persistent media reuse

`PersistentMediaCache` stores `OptionConversionCache` and audio, jacket, stage, and wall artifacts below a caller-owned cache directory. The checkpoint records a schema version, caller-supplied converter identity, conversion recipes, input and output content hashes, and relative artifact paths. Source-generated JSON serialization supports managed and NativeAOT builds. Chart models, C2S caches, diagnostics, and media validation results remain session-local.

Each cold request parses the current chart and reads current configuration. The exporter recomputes media recipes from that model, including timing and encryption settings, and checks source and cached output SHA-256 hashes before copying a hit into its private candidate. The checkpoint cannot bypass current chart validation or retain a removed optional resource. A missing or invalid checkpoint is a cache miss. Artifact paths must remain inside their generation, and reparse points are rejected.

Storage copies media into a distinct generation and replaces the checkpoint after every write closes. A failed build cannot update the checkpoint. Optional cache I/O failure preserves the usable published result. Cleanup removes recognized generations only; caller sidecars remain intact. The [caller lease and identity contract](cli.md#incremental-build-session) separates cache lifecycle from snapshot ownership.

## Messages

`PenguinTools.Core` defines `MessageDescriptor`, `Msg`, and `MsgKeys`. Diagnostics carry message keys and named arguments rather than already translated text.

English and Simplified Chinese catalogs are in `locales/`. Keep keys and placeholders aligned across catalogs. Callers can translate these descriptors without parsing CLI prose. The `titles` section mirrors message key paths for compact UI headings, using the same descriptor arguments. The original message remains the explanation. If a title is absent, callers fall back to the original message.

Write diagnostic text for chart users. Titles name the problem; explanations state the relevant input or the effect on the result in plain language. Keep the note names and settings users need to find the problem, but avoid implementation terms such as rounding or parsing when a direct description is sufficient. Do not repeat the title or add generic advice to every message. Describe only effects supported by the diagnostic's context.

Context-specific display variants also belong in the catalogs. `titles.diag.mg.length_smaller_than_unit_typed` accepts a display `type` when the note type is available. `details.diag.mg.length_smaller_than_unit` describes coincident endpoints and applies only when the segment's converted length is zero; other short segments use the original message.

`diag.error.invalid_header` is a legacy key also used for invalid or incompatible chart values. Its arguments are the value and the expected value or format; it does not always refer to a file header.

## External dependencies

`External/` contains independent Git submodules for SonicAudioTools and VGAudio.

Dependency source changes and submodule reference updates are separate reviewable changes. Build output and proprietary local samples do not belong in source control. See [development](development.md) and [testing](testing.md).
