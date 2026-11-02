# Optional runtime engines

A plain build compiles the app without downloading anything. To enable every advertised route, assemble this layout beside `DropShift.exe`, or pass a complete existing runtime folder to `build.ps1 -RuntimeRoot`:

```text
Codecs/cwebp.exe
Codecs/dwebp.exe
Codecs/ffmpeg.exe
pdfium.dll
Office/program/soffice.exe
Office/program/soffice.com
Office/program/python-core-*/bin/python.exe
Office/program/... (complete LibreOffice runtime and share directory)
Tesseract.dll
x64/... (Tesseract 5.2.0 package native DLLs)
tessdata/eng.traineddata
tessdata/osd.traineddata
PDFium-Licenses/...
OCR-Licenses/...
```

The build compiles `DropShift.Ocr.exe` when `Tesseract.dll` is present. OCR requires the Microsoft Visual C++ 2015–2022 x64 runtime.

Upstream acquisition points:

- WebP tools: https://developers.google.com/speed/webp/download
- PDFium Windows x64 builds: https://github.com/bblanchon/pdfium-binaries (use a non-V8 build with the exported APIs used by `PdfEngine.cs`).
- FFmpeg: https://ffmpeg.org/download.html . Required encoders include libmp3lame, libvorbis, AAC, libx264, libvpx-vp9 and libopus; license terms depend on the build.
- LibreOffice: https://www.libreoffice.org/download/download-libreoffice/ . Keep its full runtime, Python/UNO bridge, share files, dictionaries and licenses. A `soffice.exe` alone is insufficient. This branch preserves the tested 0.3.2 worker; newer engine versions require full regression testing.
- Tesseract wrapper/native files: https://www.nuget.org/packages/Tesseract/5.2.0 . Extract `lib/net48/Tesseract.dll` and the package's x64 native content.
- OCR models: https://github.com/tesseract-ocr/tessdata_fast/tree/4.1.0 . Use `eng.traineddata` and `osd.traineddata`.

Verify signatures/hashes from your chosen suppliers, retain dependency notices, and test each route after upgrading. Runtime acquisition is currently manual; there is no tested one-command fresh-install dependency bootstrap in this repository. The author's working private engine bundle has not yet been cleared/prepared for public redistribution.
