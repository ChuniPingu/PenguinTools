# Third-party notices

Retain these notices with redistributed source and binaries. Native publish scripts also copy dependency notices into their output.

## Included MIT notice

> The MIT License (MIT)

Copyright (c) 2019 Skyth

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## [DirectXTex / texconv](https://github.com/microsoft/DirectXTex)

The Windows x64 texconv executable is pinned to the `may2026` GitHub release and verified against
`PenguinTools.Assets/native-tools.json`. MIT License. See `PenguinTools.Assets/Licenses/DirectXTex.txt`.

## [NetVips](https://github.com/kleisauke/net-vips)

NetVips 3.2.0 is used under the MIT license. See `PenguinTools.Assets/Licenses/NetVips.txt`.

## [libvips and native dependencies](https://github.com/kleisauke/net-vips)

The dynamically loaded, replaceable `libvips-42.dll` is supplied by NetVips.Native.win-x64 8.18.7 under LGPL-3.0-or-later.
The package's third-party notices, dependency versions, and LGPL/GPL license texts are distributed under `assets/licenses`.
Source and reproducible native-build instructions are available from
[NetVips native packaging](https://github.com/kleisauke/net-vips/tree/master/.github) and
[libvips 8.18.7](https://github.com/libvips/libvips/tree/v8.18.7).

## [SonicAudioLib](https://github.com/Foahh/SonicAudioTools)

MIT License. Copyright (c) Skyth / blueskythlikesclouds.

## [VGAudio](https://github.com/Foahh/vgaudio)

MIT License. Copyright (c) Alex Barney.

## [FFmpeg](https://ffmpeg.org/)

FFmpeg is distributed as a standalone executable built from a custom LGPL vcpkg overlay. Redistribution obligations
are documented in [FFmpeg notice](External/ffmpeg/legal/NOTICE.md), [source information](External/ffmpeg/legal/FFMPEG-SOURCE-OFFER.md), and the FFmpeg
copyright notice copied during `External/ffmpeg/scripts/build.ps1`.
