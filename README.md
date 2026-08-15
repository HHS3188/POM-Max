# Performance Optimization MAX

Performance Optimization MAX is a UnityModManager performance mod for **A Dance of Fire and Ice 3.2.0-3.3.1**. It focuses on heavy custom levels while preserving chart events, decorations, hitboxes, timing, and editor data.

## v4 architecture

- Keeps the `Off`, `MAX`, and `EXTREME` starting profiles while exposing reviewed parameters in a separate Advanced tab.
- Collects allocation-bounded average, P95, P99, maximum frame-time, memory, draw, batch, and triangle telemetry through Unity's release-player profiler counters.
- Adapts only POM-Max-owned asynchronous texture-upload time slices. It does not change input, judgement, audio timing, or chart clocks.
- Skips repeated transform work only for decorations proven static in the current frame. Tweened, planet-following, floor-attached, parallax, component-driven, and editor decorations keep the original game path.
- Caches unchanged decoration shader state only when filters, masks, blend modes, and active event tweens are absent.
- Replaces the per-frame all-decoration hitbox scan with a periodically revalidated list of actual decoration-target hitboxes.
- Limits oversized PNG/JPEG textures before Unity completes the game-owned decode path. It does not allocate a second full RGBA replacement texture.
- Caches unchanged image-header metadata by normalized path, write time, and file length.
- Preserves aspect ratio and compensates custom backgrounds, visual decorations, borders, and hitboxes when a texture is limited.
- Uses 32 MB (`MAX`) or 64 MB (`EXTREME`) asynchronous upload buffers and raises upload time slices only during level loading.
- Preallocates DOTween capacity to reduce collection growth on effect-heavy levels without replacing the game's tween engine.
- Blocks level `SetFrameRate` overrides, disables VSync/anti-aliasing, and applies reversible Unity quality settings.
- Isolates Harmony patches and gives each V4 hot path a three-failure circuit breaker. A missing method or external patch owner disables only the affected optimization.
- Detects another owner of `TextureManager.LoadTexture` and disables only POM-Max texture scaling to avoid double processing.
- Reports the game version, Assembly-CSharp MVID, actual optimization hits, and one runtime evidence sample per played level.

## UI and tuning

The UMM page has four focused tabs: live overview, profiles, advanced parameters, and diagnostics. Chinese is the default language; English and Korean remain available. Profile changes load reviewed defaults, while later advanced edits are retained when saved.

Adjustable V4 controls include telemetry window, loading/gameplay upload slices, static transform and shader caches, hitbox-list audit interval, texture short-side safety floor, image-header cache capacity, frame target, and reversible Unity/system switches.

## Safety boundaries

POM-Max 4.0.0 does **not** delete chart events or decorations, replace `scrDecorationManager.LateUpdate`, skip game cleanup/scene transitions, force garbage collection during texture loading, create placeholder textures, throttle unrelated processes, change the Windows power plan, modify game assemblies, or alter input and judgement behavior.

## Profiles

- `Off`: restores captured Unity and process settings and disables POM-Max behavior.
- `MAX`: stable strong profile, 240 FPS target, 2x maximum-side limit, 32 MB upload buffer, 1500/400 tweener/sequence capacity, and conservative V4 hot paths.
- `EXTREME`: maximum reversible profile, 10000 FPS target, 8x maximum-side limit, 64 MB upload buffer, lower Unity quality costs, 4000/1000 tweener/sequence capacity, and shorter cache audit intervals.

## Installation

1. Download `POM-Max-4.0.0.zip` or the directly runnable `POM-Max-4.0.0-OneClick-Installer.cmd` from GitHub Releases.
2. Install the ZIP with UnityModManager, or run the CMD and follow the Chinese progress log.
3. Enable **Performance Optimization MAX** in UMM and select a profile.

The CMD locates Steam libraries, closes only the matching game process when necessary, prints the V4 changes, validates embedded SHA-256 hashes, backs up existing core files, preserves `Settings.xml`, writes files atomically, and rolls back on failure.

## Verification and comparisons

Run `Verify.ps1` after building, or run `Build-Release.ps1` to rebuild, verify, and generate the ZIP, one-click CMD, and SHA-256 manifest together. `Test-CmdInstaller.ps1` exercises process termination, backup, configuration preservation, atomic installation, and payload verification in an isolated temporary game directory. Release claims must use the same chart, resolution, render settings, warm-up interval, and measurement window. POM-Max reports frame-time percentiles and actual fast-path hits so improvements can be reproduced; it does not claim to outperform another Mod without matched runtime data.

## Documentation

- [中文使用说明](docs/使用说明.md)
- [Changelog](CHANGELOG.md)

## 中文摘要

POM-Max 4.0.0 面向《冰与火之舞》3.2.0-3.3.1 的重特效自定义谱面。新版加入低开销帧时间遥测、自适应上传预算、静态装饰状态缓存、有效碰撞列表和纹理头缓存；不删除谱面事件或装饰，不修改游戏文件，也不改输入与判定。

## 한국어 요약

POM-Max 4.0.0은 ADOFAI 3.2.0-3.3.1용 성능 모드입니다. 프레임 시간 측정, 적응형 업로드 예산, 안전한 장식 상태 캐시, 유효 충돌 목록 및 텍스처 헤더 캐시를 추가하며 입력, 판정, 차트 데이터와 게임 파일을 변경하지 않습니다.
