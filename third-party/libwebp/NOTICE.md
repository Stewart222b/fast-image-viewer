# libwebp 1.6.0

Source: https://storage.googleapis.com/downloads.webmproject.org/releases/webp/libwebp-1.6.0.tar.gz

Source archive SHA256: `E4AB7009BF0629FD11982D4C2AA83964CF244CFFBA7347ECD39019A9E38C4564`.

The application embeds `FastImageViewerWebp.dll` (438272 bytes), containing the
libwebp decoder and demuxer linked statically into the wrapper in `native/webp`.
The encoder, command-line tools, and animation player are excluded.
See `LICENSE`, `PATENTS`, and `AUTHORS` in this directory. These notices must
accompany binary redistribution; do not replace them with the project MIT license.

The DLL was built with MinGW-w64 GCC 16.2.0, CMake, Ninja, Release mode, x64,
SIMD enabled, threading disabled, and static libgcc. It imports only Windows
system/UCRT libraries; no separate MinGW or Visual C++ runtime DLL is shipped.
Every normal application/test build embeds this checked-in DLL using csc's
`/resource` option, so contributors do not need a native compiler.

To rebuild, download the source archive above and run:

```powershell
powershell -NoProfile -File native/webp/build-codec.ps1 -SourceArchive path/to/libwebp-1.6.0.tar.gz
```

The script verifies the source archive, builds the wrapper, copies the DLL, and
updates its SHA256 in `src/WebpDecoder.cs`. Changing the compiler may change the
binary hash. Rebuild the application afterward.

On first WebP load, the DLL is extracted into
`%LOCALAPPDATA%/FastImageViewer/codecs/libwebp-1.6.0-<hash>/` and verified against
its embedded SHA256 before loading by absolute path. Automated tests redirect
this directory to `artifacts/test-codecs` with
`FAST_IMAGE_VIEWER_CODEC_DIRECTORY`. Only the exact version is loaded; old
versions are never used as a fallback.

Static WebP preserves alpha; animated WebP displays the first frame on the
original transparent canvas. Output above 32 megapixels is decoded to a bounded
preview. Compressed input is limited to 256 MiB. WebP EXIF orientation is read
and exposed to the viewer; ICC color profile conversion is not implemented.

API documentation: https://developers.google.com/speed/webp/docs/api
