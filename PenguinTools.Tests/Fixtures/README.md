# Fixtures

Integration tests use paired `.ugc` / `.mgxc` files under `PenguinTools.Tests/Assets` (same base name, e.g.
`Sample.ugc` and `Sample.mgxc`).

Add or replace samples there locally; paths are resolved from the test project directory at runtime (
`ChartTestPaths.AssetsDirectory`), not from machine-specific locations.

Run the suite with `dotnet run --project PenguinTools.Tests`. Optional sample tests
report skips when their files are absent; synthetic parser tests run without those samples.
The runner enumerates theories during discovery so xUnit 4.0.0 reports empty optional
sample sets as skips rather than counting them as failures during deferred enumeration.
