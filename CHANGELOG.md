# Changelog

## v3.0.0

- Updated method-signature verification and patch targeting for ADOFAI 3.2.0-3.3.1 / Unity 6000.3.10f1.
- Isolated Harmony patch installation so one missing optional target no longer prevents the entire Mod from loading.
- Replaced post-decode duplicate RGBA texture creation with PNG/JPEG header inspection and the game's native `maxSideSize` loading path.
- Added aspect-safe texture limits, a 32-pixel short-side floor, geometry compensation, conflict detection, and a three-failure session circuit breaker.
- Added load-phase scheduling with reversible `backgroundLoadingPriority`, asynchronous upload time slices, and 32/64 MB upload buffers.
- Added conservative DOTween capacity preallocation without replacing the game tween engine or enabling recyclable Tween behavior.
- Removed chart-event mutation, decoration caps, background-video deletion, skipped cleanup, forced load timeout, background-process throttling, Windows power-plan changes, CPU affinity changes, and timer-resolution requests from active profiles.
- Preserved all chart events, decorations, hitboxes, timing, editor data, and the original game texture loader on failure.
- Added Steam multi-library discovery to the source build and refreshed the hash-verified, backup-first Chinese one-click installer.

## v2.1.5

- Removed unsafe runtime decoration deactivation so linked decorations and effect controllers remain intact.
- Preserved `MoveDecorations` events in EXTREME to prevent frozen, misplaced, or incomplete animation chains.
- Replaced runtime block compression with alpha-safe RGBA32 downsampling while retaining the 8x large-texture reduction.
- Added a 32-pixel minimum output dimension and preserved source filtering, wrap mode, anisotropy, and alpha metadata.
- Disabled destructive decoration caps in EXTREME while retaining frame pacing, quality, process, background-video, and texture optimizations.

## v2.1.4

- Replaced the game-owned notification with a compact, resolution-aware overlay that avoids the editor's upper-left controls.
- Reduced the optimization summary font size and constrained its width at 720p through 1440p.
- Added a custom-level load fallback so every completed `.adofai` load reports even when the decoration refresh callback is skipped.
- Removed the dependency on the game's private `Notification.SetupNotification` method.

## v2.1.3

- Added one aggregate optimization notification for every custom-level load in active profiles.
- Expanded per-level results with event, decoration, texture-memory, loading-time, and workload-reduction statistics.
- Rebuilt the UMM settings page as a compact YCH-style dark panel with profile-only controls and shorter descriptions.
- Replaced unstable decoration position/Shader frame skipping with a one-time runtime decoration budget.
- Protected text, judgement, result, hitbox, planet-following, floor-attached, and UI-related decorations from the extreme runtime budget.
- Reduced recurring process-management overhead and balanced the Windows timer-period lifecycle.

## v2.1.2

- Protected result-screen text and judgement UI from extreme-profile global mipmap and LOD degradation.
- Preserved custom backgrounds, text decorations, and camera/UI-tagged decorations.
- Suspended shader simplification and decoration throttling while the result screen is visible.
- Restricted custom texture processing away from game and Mod UI asset paths.
- Restored the texture-loading memory saving notification in the extreme profile.

## v2.1.1

- Replaced the individual UMM tuning controls with three deterministic profiles and concise profile descriptions.
- Added fixed parameter normalization so legacy `Settings.xml` values cannot leave a selected profile partially configured.
- `MAX` now uses 2x custom-texture downsampling as its stable performance preset.
- `EXTREME` now applies every aggressive optimization automatically, including the maximum 8x texture downsampling, a 100-decoration cap, 8-frame visual update stride, and maximum process/quality reductions.
- Profile changes restore the previous runtime/process state before applying the new preset.

## v2.1.0

- Integrated the custom-level texture resizing, compression, geometry compensation, and load statistics previously provided by `optimiz`.
- Added Unity runtime texture memory measurements, per-texture instance ratio tracking, duplicate-processing protection, and legacy `optimiz` conflict detection.
- Fixed render-target restoration and temporary texture cleanup during texture resizing.
- Added English, Chinese, and Korean texture optimization settings.
- Updated the build script for the current ADOFAI 3.2.0 assemblies.

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
