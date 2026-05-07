# Changelog

## v2.0.0

- Removed the old `Performance` profile and reordered the profiles to `Off`, `Normal`, `MAX`, and the separate `OVERDRIVE` mode.
- Moved decoration shader simplification and decoration update throttling to `OVERDRIVE` only to reduce blank decoration rendering issues in normal `MAX`.
- Added an aggressive `OVERDRIVE` mode with stronger Windows process priority, background process throttling, high-performance power-plan requests, and extra quality reductions.
- Added UMM usage/source buttons and expanded English, Chinese, and Korean setting text.
- Added detailed usage documentation under `docs/使用说明.md`.

## v1.0.0

- Initial release.
- Added UMM settings UI with English, Chinese, and Korean text.
- Added frame pacing controls, load timeout protection, level data optimization, decoration shader simplification, decoration update throttling, and optional Windows priority mode.
- Added UMM source/homepage link and update repository metadata.
