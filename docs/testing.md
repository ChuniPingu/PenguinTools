# Testing

## Run the suite

From the repository root:

```powershell
dotnet build PenguinTools.slnx -c Release
dotnet run --project PenguinTools.Tests
```

For targeted runs, pass the test runner's options after `--`; use `dotnet run --project PenguinTools.Tests -- --help` to inspect the current options.

Synthetic parser and workflow tests do not need local chart samples. Native tool and sample integration tests have additional requirements; report skipped tests separately from passed tests.

## Chart samples

Optional paired samples live in `PenguinTools.Tests/Assets/`. A pair shares its base filename, for example `Sample.ugc` and `Sample.mgxc`.

`ChartTestPaths.AssetsDirectory` resolves the default directory relative to the test output. To use a different local sample directory:

```powershell
$env:PENGUINTOOLS_TEST_CHARTS = 'C:\local-chart-samples'
dotnet run --project PenguinTools.Tests
```

The sample theories enumerate during discovery, so empty optional sample sets are reported as skips. Do not make missing proprietary samples fail synthetic tests or commit those samples.

## Native and asset tests

Media and CRI integration tests need the corresponding native tool output and local assets. Build tools using the [development commands](development.md#native-tools-and-publishing), then check the paths resolved by `TestAssets` and `TestMediaTool`.

Prefer a synthetic fixture for parser regressions. If a real sample is needed, keep it local and describe the missing coverage in the validation result.
