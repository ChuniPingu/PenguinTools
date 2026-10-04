# Image pipeline

`PenguinTools.Image` runs in-process on Windows x64 and supports NativeAOT.
NetVips 3.2.0 / native 8.18.7 handles rasters; pinned texconv handles DDS codecs.

| Output | Size | Format | Child processes |
| --- | --- | --- | --- |
| Jacket | 300 × 300 | BC1 / DXT1 | 1 |
| Stage background + atlas | 1920 × 1080 + 512 × 512 | BC1 + BC3 | 2 sequential |
| DDS decode | Original size, first surface | RGBA8 PNG | 1 |
| Validation / extraction | Unchanged | No encoding | 0 |

- Content-based raster loading; first frame/page; SVG excluded.
- EXIF orientation, ICC-to-sRGB conversion, linear-light filtering and premultiplied alpha.
- Opaque black jackets/backgrounds; straight-alpha effects in top-left, top-right, bottom-left, bottom-right order.
- Background offset defaults to 160; positive moves upward and exposed rows are black.
- Legacy DDS headers, one mip, BGRA TGA handoff; container size and nontexture bytes preserved.
- Shared job limit: `max(1, min(4, ProcessorCount / 2))`. Temporary directories are isolated; cancellation kills child processes.
- Image cache version **5** invalidates older image entries while preserving audio entries.

Builds verify and cache pinned native tools automatically. Publish the complete output folder;
runtime needs no network. `ImageService` takes the texconv path and temporary directory in its constructor.

```powershell
dotnet test --project PenguinTools.Tests/PenguinTools.Tests.csproj -c Release
dotnet publish PenguinTools.CLI/PenguinTools.CLI.csproj -p:PublishProfile=WinX64-NativeAOT
```

## Synthetic benchmark

Windows, Ryzen 9 8945HX, 32 logical processors; three warm trials, median time.
Inputs: generated 4K PNG/JPEG and partial-alpha effects. Batches contain 16 conversions with
eight callers; the library limits active jobs to four.

| Operation | Previous implementation | Library |
| --- | ---: | ---: |
| Jacket | 116 ms | 159 ms |
| 16 jackets | 429 ms | 849 ms |
| Stage | 207 ms | 356 ms |
| 16 stages | 920 ms | 2,130 ms |

The remaining slowdown includes linear-light processing, subprocess and temporary-file costs,
and bounded concurrency. Repeated upstream raster evaluation was removed during profiling.
Intermediate I/O is 405,146 bytes per jacket and 10,642,212 per stage.
[Measurements](benchmarks/image-migration-win-x64.json) include sampled process-tree memory and
logical I/O; 10 ms sampling can miss short-lived processes and is not physical disk I/O.
