# Image pipeline

`PenguinTools.Image` uses NetVips for rasters and pinned texconv for DDS on Windows x64, including NativeAOT.

| Output | Size | Format | Child processes |
| --- | --- | --- | --- |
| Jacket | 300 × 300 | BC1 / DXT1 | 1 |
| Stage background + atlas | 1920 × 1080 + 512 × 512 | BC1 + BC3 | 2 sequential |
| DDS decode | Original size, first surface | RGBA8 PNG | 1 |
| Validation / extraction | Unchanged | No encoding | 0 |

Rasters use the first frame/page, EXIF orientation, ICC-to-sRGB conversion, and linear-light resizing.
Jackets/backgrounds are opaque; effects retain alpha. Background offset defaults to 160 pixels upward.
DDS output uses legacy headers and one mip. Image cache version 5 invalidates older image entries.

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

Linear-light processing, child processes, temporary I/O, and bounded concurrency add cost.
Repeated raster evaluation was removed during profiling. [Measurements](benchmarks/image-migration-win-x64.json)
include process-tree memory and logical I/O sampled every 10 ms; short-lived processes may be missed.
