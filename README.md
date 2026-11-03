# DropShift

Drag a file, tap **Shift**, and drop it onto a compatible output format.

DropShift is an offline Windows tray application. It writes converted copies beside the source and never intentionally overwrites originals or earlier results. The wheel disappears after dropping or cancelling; the tray listener remains available for the next gesture.

Current source: **0.3.2.1**, the working application with optional Windows sign-in startup. This is a development project, not a claim that every file can convert into every other format. Office cold starts can take tens of seconds. Complex layouts, fonts and media streams have limitations.

## Download and run

There is **no ready-to-run Windows download** on the Releases page yet. GitHub's **Code → Download ZIP** downloads the source code, not a built app. Do not look for `DropShift.exe` inside that ZIP.

To build and try the available Windows-native image conversions:

1. On this repository page, choose **Code → Download ZIP** (or download the [source ZIP](https://github.com/muhammadjunaidmuradwork-hub/DropShift/archive/refs/heads/main.zip) directly).
2. In Windows Explorer, right-click the downloaded ZIP, choose **Extract All**, and open the extracted `DropShift-main` folder.
3. On 64-bit Windows 10/11 with .NET Framework 4.8 installed, open PowerShell in that folder and run:

   ```powershell
   .\build.ps1
   .\test.ps1
   ```

4. Launch `dist\DropShift.exe`. Keep it running in the tray while using the drag-and-Shift gesture below.

If PowerShell blocks the local script, use `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` and the same command with `test.ps1`. This changes policy for that command only. Downloading the ZIP does not install DropShift or set it to start with Windows.

The plain build does **not** include the external engines for WebP, PDF, Office, OCR, audio or video. To use those conversions, follow [runtime setup](RUNTIME.md) and build with a complete engine folder. The complete engine bundle is not publicly downloadable yet.

## Use

1. Launch `DropShift.exe`.
2. Hold the left mouse button and start dragging a local file in Explorer.
3. While still holding the mouse, tap Shift.
4. Drop on an output format. The center or Escape cancels.

A click followed by Shift is not the activation gesture. Normal Shift typing/selection is preserved. Close the home window to keep the app in the tray; **Exit DropShift** ends the listener. Choose **Start with Windows** in the tray menu to enable or disable sign-in startup.

For an explicitly requested unattended setup: `DropShift.exe --enable-startup --background`. `--background` alone hides the home window without changing startup preferences. Startup is opt-in for people building this repository.

## Supported conversions

Same-format outputs are omitted. Extensions are case-insensitive; JPEG and JPG, and TIF and TIFF, are aliases.

| Input files | Output formats |
| --- | --- |
| JPG/JPEG, PNG, WebP, BMP, TIFF/TIF, static GIF | JPG, PNG, WebP, BMP, TIFF, PDF |
| PDF | JPG pages, PNG pages, TXT, DOCX |
| TXT | PDF |
| DOC, DOCX, DOCM, ODT, RTF | PDF, DOCX, ODT, TXT |
| XLS, XLSX, XLSM, CSV, TSV, ODS | PDF, XLSX, ODS |
| PPT, PPTX, PPTM, ODP | PDF, PPTX, ODP |
| MP3, WAV, FLAC, OGG, M4A, AAC, WMA, OPUS, AIFF | MP3, WAV, FLAC, OGG, M4A |
| MP4, MOV, MKV, AVI, WebM, M4V, WMV, MPEG, MPG | MP4, MKV, WebM, MP3, WAV |

The exact per-extension mapping is generated from the app in [formats.json](formats.json). Video-to-audio needs an audio track. GIF is input-only and must be static. PDF-to-TXT needs a text layer; scanned PDF-to-DOCX uses offline English OCR. PDF-to-DOCX does not guarantee exact layout. Word/Excel/PowerPoint output is macro-free. Mixed selections offer only common targets.

## Build on Windows

Requires 64-bit Windows 10/11 and .NET Framework 4.8. Open PowerShell in this repository:

```powershell
.\build.ps1
.\test.ps1
```

The compiler ships with .NET Framework. A basic source build supports the Windows-native static image conversions; advanced paths need external engines. No engines, private files or third-party binaries are included in this source repository. There is currently no public full-engine release attached.

If you already have a complete DropShift runtime folder, reuse its engines locally:

```powershell
.\build.ps1 -RuntimeRoot 'C:\path\to\DropShift-runtime'
```

This copies only the explicit engine files/folders listed in the build script, compiles the OCR helper, and retains available license directories. It does not upload anything. See [runtime setup](RUNTIME.md) for the required layout and upstream projects. Do not interpret a successful local build as completion of the binary redistribution requirements.

## Safety and limitations

- Inputs are opened read-only; outputs use unused `(converted)` names. An unexpected failure must not destroy the source.
- Output folders must be writable. Download cloud placeholders before dragging; virtual mail attachments are not supported.
- Images: 50 million pixels maximum; animations and multipage TIFFs are rejected. JPG/BMP flatten transparency onto white. Metadata and color profiles are not guaranteed to survive.
- PDFs: up to 200 MB / 200 pages. Page images export at 144 DPI. Image/TXT-to-PDF is image-based, not searchable text. Password-protected or damaged PDFs are rejected.
- Office: 200 MB input limit. Private workers disable macros and automatic link updates; startup and complex files can be slow. Fonts/layout may differ from Microsoft Office.
- OCR: printed English, not guaranteed handwriting/table/layout reconstruction. Review names and numbers.
- Media: first video/audio streams only; subtitles, extra tracks and metadata are not retained. Lossy outputs can lose quality.
- Files are untrusted inputs. Keep native conversion engines patched; this application is not a hardened sandbox.
- No uploads or telemetry. Local activity logs contain file paths and errors; redact them before opening an issue.

## Contribute and license

See [CONTRIBUTING.md](CONTRIBUTING.md). DropShift-authored source is [MIT licensed](LICENSE). Dependencies retain their own licenses; see [THIRD-PARTY.md](THIRD-PARTY.md). The unreleased Office optimization experiments are intentionally excluded.
