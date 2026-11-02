# Third-party software

The repository contains DropShift source only. Its MIT license does not replace dependency licenses.

| Engine | Purpose | License information |
| --- | --- | --- |
| libwebp 1.6.0 command-line tools | WebP | BSD-style; preserve upstream notices and PATENTS file. https://chromium.googlesource.com/webm/libwebp/ |
| PDFium | PDF rendering and validation | BSD-style plus bundled dependency notices. https://pdfium.googlesource.com/pdfium/ |
| FFmpeg | Audio/video | Configuration-dependent LGPL/GPL. The privately tested 7.1 essentials binary enables GPL components. https://ffmpeg.org/legal.html |
| LibreOffice | Office documents | MPL 2.0 and additional component licenses in its installation. https://www.libreoffice.org/licenses/ |
| Tesseract .NET wrapper 5.2.0 | OCR helper | Apache 2.0. https://github.com/charlesw/tesseract |
| Tesseract / Leptonica | OCR engine | Apache 2.0 / BSD-style. https://github.com/tesseract-ocr/tesseract and https://github.com/DanBloomberg/leptonica |
| tessdata_fast 4.1.0 | English/orientation models | Apache 2.0. https://github.com/tesseract-ocr/tessdata_fast |

Before publishing an engine-inclusive download, identify the exact binaries/configuration, retain every applicable notice/license, and satisfy their source-distribution requirements. In particular, a link to a generic FFmpeg project/tag is not by itself evidence that exact corresponding source, patches and dependency build materials are available for the distributed GPL binary. Keep this a source-only distribution until those materials are assembled and checked.

The app invokes FFmpeg, WebP tools and LibreOffice as separate local processes. It loads PDFium and uses the separately compiled Tesseract wrapper helper. The relationships and each component's license must be considered when assembling a distribution.
