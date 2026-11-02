# CLI usage

Run these PowerShell examples from a [published CLI folder](development.md#native-tools-and-publishing), with its bundled assets and native libraries present. Relative paths are resolved from the current working directory.

```powershell
$cli = '.\PenguinTools.CLI.exe'
& $cli --help
& $cli option build --help
```

Use command-specific `--help` for the full parameter list. For scripts, invoke the built executable directly so build output is separate from CLI output.

## Common workflows

### Inspect and convert a chart

```powershell
& $cli chart inspect .\charts\sample.ugc
& $cli chart convert .\charts\sample.ugc .\out\sample.c2s --no-progress
```

Inspection fully parses the chart and reports metadata and diagnostics. Conversion writes the requested output file; its extension selects the output format. See [chart conversion](chart-conversion.md) for supported formats, timing, and round-trip behavior.

### Scan and build an option

```powershell
& $cli option scan .\charts --chart-file-discovery mgxc,ugc,sus
& $cli option build .\charts .\out --option-name A001 --save-config
```

For a chart-only build, first save the configuration below as `charts/options.json`. Otherwise, enabled audio, jacket, and background exports need their source media.

The bundle is written under `out/A001/`; an output directory already named `A001` is used directly. Generated music files are under `music/`, with other enabled exports under `stage/`, `cueFile/`, `event/`, and `releaseTag/`. The result lists artifact paths.

Discovery order chooses the preferred format when a song ID and difficulty occur in several formats. Set a song's main difficulty with `--main-difficulty 1234:Master`. `--batch-size` controls maximum parallelism; `-1` selects automatic parallelism. `--ignore-cache` forces audio, jacket, and stage reconversion.

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
