# CLI usage

Run these PowerShell examples from a [published CLI folder](development.md#native-tools-and-publishing), with its bundled assets and native libraries present. Relative paths are resolved from the current working directory.

```powershell
$cli = '.\PenguinTools.CLI.exe'
& $cli --help
& $cli option build --help
```

Use command-specific `--help` for the full parameter list. For scripts, invoke the built executable directly so build output is separate from CLI output.

Song IDs must be integers from `0` to `2000000000`, including when resource IDs are set explicitly. Unlock event and wall IDs allow `0` to `2147483647`; background, genre, World's End tag, CueFile, and release tag IDs accept signed 32-bit integers. Unknown field line indices produce a warning when exporting a background and are preserved. Invalid explicit IDs stop the export; malformed optional `#meta` IDs are ignored with a warning. Editor song identifiers may contain text, but building game resources requires a numeric song ID.

## Common workflows

### Inspect and convert a chart

```powershell
& $cli chart inspect .\charts\sample.ugc
& $cli chart convert .\charts\sample.ugc .\out\sample.c2s --no-progress
```

Inspection fully parses the chart and reports metadata and diagnostics. Conversion writes the requested output file; its extension selects the output format. See [chart conversion](chart-conversion.md) for supported formats, timing, and round-trip behavior.

### Scan and build an option

Use `option build` to export songs with their difficulties and media. The input folder can contain a single song.

```powershell
& $cli option scan .\charts --chart-file-discovery mgxc,ugc,sus
& $cli option build .\charts .\out --option-name A001 --save-config
```

For a chart-only build, first save the configuration below as `charts/options.json`. Otherwise, enabled audio, jacket, and background exports need their source media.

The bundle is written under `out/A001/`; an output directory already named `A001` is used directly. Generated music files are under `music/`, with other enabled exports under `stage/`, `cueFile/`, `event/`, `releaseTag/`, and `ddsFieldWall/`. Audio is encoded as stereo 48 kHz HCA at 384 kbps. Wall settings are saved in the bundle root's `wallbind.json`. The result lists artifact paths.

Discovery order chooses the preferred format when a song ID and difficulty occur in several formats. Set a song's main difficulty with `--main-difficulty 1234:Master`. `--batch-size` controls maximum parallelism; `-1` selects automatic parallelism. `--ignore-cache` forces audio, jacket, stage, and wall reconversion. `--convert-wall false` skips wall export and keeps existing wall settings.

### Incremental build session

Run `PenguinTools.CLI.exe session` to keep one application and asset catalog alive for repeated builds. Standard input and output carry one JSON object per line, encoded as UTF-8. The worker first writes `{"ready":true,"protocol":1,"capabilities":["persistentMediaCache"]}`. Clients can check `capabilities` before using optional request fields. Requests execute serially, and each response echoes its numeric `id`. No progress records are written.

Send `prepare` with in-memory UGC `content`, or with `inputPath` and `format` set to `ugc`, `mgxc`, or `sus`. Supply exactly one of `content` and `inputPath`. Relative input paths start at `baseDirectory`; saved charts resolve their media from their own directory. In-memory UGC resolves media from `baseDirectory`.

```json
{"id":1,"method":"prepare","documentId":"editor-document","baseDirectory":"C:/charts/song","format":"ugc","content":"@VER\t8\n@TICKS\t480\n@SONGID\t1234\n@BPM\t0'0\t120\n@BEAT\t0\t4\t4\n#0'0:t02\n","outputDirectory":"C:/preview/revision-1","overrides":{"optionName":"A001","convertChart":true,"convertAudio":false,"convertJacket":false,"convertBackground":false,"convertWall":false}}
```

`baseDirectory/options.json` is read for every request, then `overrides` applies the camelCase fields of `OptionBuildOverrides`. `hcaEncryptionKey` accepts an unsigned integer or decimal string. `outputDirectory` must be absent or empty; the complete option is published below it using `optionName`. Each request needs a distinct directory. Consume the output after success and discard it after failure. The source chart, media, and configuration are read without modification.

To reuse media across worker lifetimes, supply both `cacheDirectory` and `cacheIdentity`. `cacheDirectory` is an absolute, dedicated directory owned by the caller. `cacheIdentity` is a nonempty opaque identifier for the converter implementation, native tools, templates, and service configuration; the caller must change it whenever those dependencies change. A stable source-based directory can be shared by requests with different `documentId` values. The caller must hold an exclusive cross-process lease on the directory throughout each request and any eviction or clearing operation. PenguinTools does not acquire that external lease.

The directory contains `checkpoint.json` and `g-<GUID>` media generations; these are private versioned cache data. Cache content is optional: missing, corrupt, incompatible, or inaccessible data causes normal conversion, and cache storage failure does not reject a completed export. The caller manages capacity and eviction. Published snapshots and active session work files are independent copies, so deleting an idle disk cache cannot invalidate them. See [persistent media reuse](architecture.md#persistent-media-reuse) for validation and storage rules.

A response has `id`, `success`, `diagnostics`, and a `result` on success. Diagnostics use the existing message descriptors and locations. The result contains:

| Field | Meaning |
| --- | --- |
| `revision` | Increasing successful-build number; failed requests do not advance it. |
| `optionDirectory` | Absolute path to the published option. |
| `chartPath` | Absolute C2S path; omitted when chart export is disabled. |
| `chart`, `metadata` | The same chart summary and conversion metadata shapes as inspection. |
| `artifacts` | Every live output file plus records for files removed since the preceding successful build. |
| `changes` | Distinct kinds whose artifact paths or hashes changed. |

Each artifact has `kind`, absolute `path`, SHA-256 `hash` in uppercase hex, and `status` (`changed`, `reused`, or `removed`). Kinds are `chart`, `audio`, `jacket`, `stage`, `wall`, and `metadata`. Comparison uses paths relative to the option root and file contents. Removed records use the absent path in the requested snapshot and its preceding hash. C2S and metadata are compared independently. Clients that discard intermediate responses should compare the complete live manifest with their active snapshot instead of relying on `changes` alone.

A failure leaves the preceding session state usable; the next corrected request can reuse its artifacts. Changing `documentId` or `baseDirectory` starts a separate cache lineage within the worker. `{"id":2,"method":"close"}` receives a successful response and ends the session. End-of-input also ends it. Both dispose internal working files; published snapshots belong to the caller. Requests are limited to 32 Mi characters per line. See [incremental build architecture](architecture.md#incremental-builds) for cache dependencies and publication semantics.

### Build field walls

In the chart's Comment field, add:

```text
#meta wall "images/wall.png" wide
#meta wall <image-path> [normal|wide] [wall-id]
```

The image path starts from the chart's folder; use quotes if it contains spaces. Share the image with the chart. Width defaults to `wide`. Omit the wall ID to use `1_000_000 + song ID`, or set a fixed ID.

```powershell
& $cli wall build .\charts\sample.mgxc .\out --song-id 9978
& $cli wall build-files .\images\wall.png .\out --song-id 9978 --wall-width normal
```

Use `--wall-input`, `--wall-id`, or `--wall-width` to override the chart's settings. `build-files` takes the image directly. In `option build`, all difficulties share the main chart's wall.

| Width | Stretched image size |
| --- | --- |
| `normal` | 640 × 480 |
| `wide` | 2048 × 512 |

Images are stretched to the selected size and saved as DXT1 DDS with one mip level. `wall build` and `wall build-files` save:

```text
out/
  wallbind.json
  ddsFieldWall1009978/
    DDSFieldWall.xml
    CHU_UI_Fieldwall_1009978.dds
```

Install the [wallbind plugin](https://github.com/ChuniPingu/wallbind#install) to display the wall in-game. The plugin is a separate download for MATE 2.50 x86. Put the `ddsFieldWall1009978/` folder inside the option's `ddsFieldWall/` folder, and `wallbind.json` in the option root. `option build` arranges these files automatically.

Each build replaces `wallbind.json` with the bindings from its current inputs, including an empty list when no walls are configured. Standalone wall commands replace the list with one binding. Wall settings and images are published after all charts and enabled conversions finish successfully. Failed conversions, skipped unsupported charts, or invalid wall settings keep the previous wall files. Media caches still apply; unused asset files are not deleted. Songs sharing a wall ID must use the same image and width.

### Extract charts and media

```powershell
& $cli music extract .\game\music\music1234\Music.xml .\extracted --chart-format ugc --no-audio --no-jacket
& $cli audio extract .\game\cueFile\music1234.acb .\wav
```

Music extraction reads the chart paths in `Music.xml` and writes under `extracted/<song ID>/`. Its default chart format is MGXC; `--chart-format ugc` selects UGC. `--no-audio` and `--no-jacket` skip those media outputs. To include them, omit those flags and provide accessible media, using `--acb`, `--awb`, or `--jacket` when discovery needs an explicit path. Extracted jackets are sRGB PNGs; raster overrides such as JPEG are decoded and converted with orientation, dimensions, and transparency preserved. Standalone audio extraction writes decoded WAV files to its output directory.

### Use an additional asset catalog

```powershell
& $cli assets collect .\game --output .\assets.user.json
& $cli --user-assets .\assets.user.json chart inspect .\charts\sample.ugc
```

The collected JSON supplements the bundled asset catalog. Without `--user-assets`, only bundled assets are used.

## Option configuration

A chart-only `charts/options.json`:

```json
{
  "optionName": "A001",
  "chartFileDiscovery": ["mgxc", "ugc", "sus"],
  "convertChart": true,
  "convertAudio": false,
  "convertJacket": false,
  "convertBackground": false,
  "convertWall": false,
  "generateEventXml": false
}
```

For `option build`, explicit command-line settings override the loaded file; omitted properties use built-in defaults. Supply a four-character option name through the file or `--option-name`.

| Build invocation | Configuration loaded |
| ---------------- | -------------------- |
| No configuration flag | `<input>/options.json`, if present |
| `--config path.json` | The specified file, which must exist |
| `--no-config` | Built-in defaults plus command-line settings |

`--config` and `--no-config` are mutually exclusive. An invalid configuration fails a build.

`option scan` reads `<input>/options.json` automatically. Discovery order comes from the command line, then the file, then MGXC/UGC/SUS. Scan parallelism comes from `--batch-size` (default 8), independently of the saved value. An unreadable or malformed file produces a warning during a scan; requesting `--save-config` with that file fails instead.

Configuration is saved only when requested:

- `scan --save-config` writes `<input>/options.json` **before scanning**, updating discovery when explicitly supplied and saving the scan batch size.
- `build --save-config` writes the option settings and conversion cache **after export completes**. It updates the explicit or loaded file, or creates `<input>/options.json`. This can happen even when the final result is partial and exits with code 1.
- `--main-difficulty` selections apply to that build; they are not saved in `options.json`.

The configuration file contains option settings. The CLI's returned JSON envelope and scan summary have different structures; retain the configuration file separately.

## JSON output and scripts

Operational commands write newline-delimited JSON to stdout: zero or more `progress` events followed by a `result` event on normal completion. Parse each line separately. Keep stderr separate from the JSON stream. `--help` and `--version` produce text.

Commands offering `--no-progress` suppress progress events while retaining the final result. Progress events have `type: "progress"`, `operation`, and optional `item`, `label`, `completed`, `total`, and `percent` fields.

The result envelope currently uses schema version 4:

| Field | Meaning |
| ----- | ------- |
| `type`, `schemaVersion`, `operation` | `"result"`, protocol version, and operation such as `"chart.convert"` |
| `success`, `exitCode` | Operation outcome and process exit code |
| `message` | Optional message descriptor with a `key` and optional named `args` |
| `data` | Operation-specific result; may be present on failure |
| `diagnostics` | Diagnostics with severity, message descriptor, and optional location or target |

Null properties are omitted. Translate message descriptors using [the catalogs](../locales/); consumers should use keys and arguments rather than parse translated prose. Preserve JSON value types: for example, a scan's `data.config.hcaEncryptionKey` is a decimal string.

Scan diagnostics may also be attached to `data.books[].charts[].diagnostics` or `data.unmatchedDiagnostics`.

Diagnostic `path` identifies the problem's source file; `line`, `time`, and `timePosition` carry its location separately. Musical positions are one-based and refer to the source chart, before any C2S preroll is inserted. Note targets include `type`, `tick`, zero-based `lane`, `width`, `timeline`, and an optional `timePosition`. Pair targets have `left` and `right` notes with their own positions. Short-segment targets have `start` and `end` notes, `sourceLengthTicks` in editor ticks, and `convertedLengthTicks` in C2S ticks after endpoint rounding. Text parser targets may include `text` containing the original line. Automatic sort-name diagnostics include `title` and `sortName` message arguments. Targets remain optional; consumers must tolerate unknown shapes and older payloads without these details.

```powershell
$jsonLines = @(& $cli option scan .\charts --no-progress)
$exitCode = $LASTEXITCODE
$result = $jsonLines |
    ForEach-Object { $_ | ConvertFrom-Json } |
    Where-Object type -eq 'result' |
    Select-Object -Last 1
if ($null -eq $result) { throw "CLI exited with code $exitCode without a result." }
$result
```

Check both the process exit code and the result before using its data:

| Exit code | Meaning |
| --------- | ------- |
| 0 | Success; warnings may still be present |
| 1 | Operation failure, including a partial option batch |
| 2 | Command-line syntax error; result operation is `"parse"` |
| 130 | Cancellation handled by the CLI |

## Partial option batches

An unsupported chart version is skipped with `diag.warn.chart_skipped_version`; supported charts continue through the scan or build. A partial result has `success: false`, exit code 1, `data.isPartial: true`, `data.skippedCharts`, and message key `cli.msg.option_partial`. These fields track version-related skips. Usable result data and exported artifacts are retained.

If every chart is skipped, building fails with the per-file reasons and the no-charts diagnostic. Inspect the returned data and diagnostics to decide whether usable output remains; exit code 1 alone does not distinguish partial output from other failures.
