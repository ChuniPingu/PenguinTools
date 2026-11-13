# Testing

## Run the suite

From the repository root:

```powershell
dotnet build PenguinTools.slnx -c Release
dotnet run --project PenguinTools.Tests -c Release --no-build
```

For targeted runs, pass the test runner's options after `--`; use `dotnet run --project PenguinTools.Tests -- --help` to inspect the current options.

Synthetic parser and workflow tests do not need local chart samples. Native tool and sample integration tests have additional requirements; report skipped tests separately from passed tests.

`IncrementalSession_*` application tests cover immutable snapshots, resource reuse, timing dependencies, changed and removed media, failure recovery, configuration changes, and the JSONL session contract. Their injected media services count validation and conversion calls, so an unchanged output hash alone cannot hide redundant media conversion.

The session also retries once when source media or settings change during export. Its concurrent-change tests verify that an unstable candidate cannot replace the committed revision, and that changes preserving source timestamps are detected. Cache-copy tests verify output hashes and independent ownership of published snapshots.

`PersistentMediaCache_*` tests create separate sessions sharing a disk checkpoint. They cover note edits, changed content with preserved timestamps, timing and configuration changes, converter identity changes, corrupt outputs and checkpoints, unsafe paths, failed builds, and eviction without affecting published snapshots.

Parser and writer changes need regression tests for the affected metadata, timing, note relationships, or payloads, including round-trip preservation where relevant. Prefer small synthetic fixtures for distinct behavior; add format combinations or repeated conversions when they exercise a different code path or state change. Keep proprietary samples local and report any missing coverage.

## Chart samples

Optional paired samples live in `PenguinTools.Tests/Assets/`. A pair shares its base filename, for example `Sample.ugc` and `Sample.mgxc`.

`ChartTestPaths.AssetsDirectory` resolves the default directory relative to the test output. To use a different local sample directory:

```powershell
$env:PENGUINTOOLS_TEST_CHARTS = 'C:\local-chart-samples'
dotnet run --project PenguinTools.Tests
```

The sample theories enumerate during discovery, so empty optional sample sets are reported as skips. Do not make missing proprietary samples fail synthetic tests or commit those samples.

## Native and asset tests

Windows builds prepare and copy native tools into the test output; see [development](development.md#windows-prerequisites) for setup.

Tests using the shared asset catalog locate `assets.json` by walking upward from the test output; see [TestAssets](../PenguinTools.Tests/TestAssets.cs) for asset overrides.
