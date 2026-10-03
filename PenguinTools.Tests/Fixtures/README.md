# Test fixtures

Optional paired `.ugc` / `.mgxc` samples belong in `PenguinTools.Tests/Assets/` with matching base filenames.

Run tests from the repository root:

```powershell
dotnet run --project PenguinTools.Tests
```

Synthetic tests run without local samples; optional sample tests report skips when files are absent. See [testing](../../docs/testing.md) for path overrides and native integration requirements.
