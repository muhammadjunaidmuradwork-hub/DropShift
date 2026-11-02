# Contributing

Open an issue describing the input extension, requested output, Windows version and reproducible steps. Use a small synthetic file where possible. Do not upload private documents, passwords or unredacted logs.

For a change, build with `build.ps1` and run `test.ps1`. Conversion changes also need output-content validation, repeated conversion/collision checks, SHA-256 comparisons of originals, and a real Explorer drag + Shift test. Never implement conversion by renaming an extension or overwriting a source.

Keep dependency changes separate from UI work. Document new filters and limitations. Changes to macros, external links, temporary paths, process cleanup and file publication need careful review. Contributors license their original contributions under this project's MIT license.
