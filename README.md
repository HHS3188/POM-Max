# Performance Optimization MAX

Performance Optimization MAX is a UnityModManager performance mod for **A Dance of Fire and Ice 3.2.0-3.3.1**. It focuses on heavy custom levels while preserving chart events, decorations, hitboxes, timing, and editor data.

## v3 architecture

- Applies fixed `Off`, `MAX`, and `EXTREME` profiles instead of exposing unsafe partial combinations.
- Limits oversized PNG/JPEG textures before Unity completes the game-owned decode path. It does not allocate a second full RGBA replacement texture.
- Preserves aspect ratio and compensates custom backgrounds, visual decorations, borders, and hitboxes when a texture is limited.
- Uses 32 MB (`MAX`) or 64 MB (`EXTREME`) asynchronous upload buffers and raises upload time slices only during level loading.
- Preallocates DOTween capacity to reduce collection growth on effect-heavy levels without replacing the game's tween engine.
- Blocks level `SetFrameRate` overrides, disables VSync/anti-aliasing, and applies reversible Unity quality settings.
- Isolates Harmony patches. A missing game method disables only that optional patch instead of preventing the Mod from loading.
- Detects another owner of `TextureManager.LoadTexture` and disables only POM-Max texture scaling to avoid double processing.
- Stops its texture module for the session after repeated preprocessing failures; the original game loader remains active.

## Safety boundaries

POM-Max 3.0.0 does **not** delete chart events or decorations, replace `scrDecorationManager.LateUpdate`, skip game cleanup/scene transitions, force garbage collection during texture loading, create placeholder textures, throttle unrelated processes, change the Windows power plan, or force CPU affinity/timer resolution.

## Profiles

- `Off`: restores captured Unity and process settings and disables POM-Max behavior.
- `MAX`: stable strong profile, 240 FPS target, 2x maximum-side limit for oversized custom textures, 32 MB upload buffer, and a 1500/400 tweener/sequence capacity.
- `EXTREME`: maximum reversible profile, 10000 FPS target, 8x maximum-side limit, 64 MB upload buffer, lower Unity quality costs, and a 4000/1000 tweener/sequence capacity.

## Installation

1. Download `POM-Max-3.0.0.zip` or `POM-Max-3.0.0-一键安装.cmd` from GitHub Releases.
2. Exit the game.
3. Install the ZIP with UnityModManager, or run the CMD and follow the Chinese prompts.
4. Enable **Performance Optimization MAX** in UMM and select a profile.

The CMD locates Steam libraries, validates embedded SHA-256 hashes, backs up existing core files, preserves `Settings.xml`, writes files atomically, and rolls back on failure.

## Documentation

- [中文使用说明](docs/使用说明.md)
- [Changelog](CHANGELOG.md)

## 中文摘要

POM-Max 3.0.0 面向《冰与火之舞》3.2.0-3.3.1 的重特效自定义谱面。新版使用游戏原生纹理解码限尺寸、加载阶段调度、异步上传缓冲和 DOTween 容量预分配；不删除谱面事件或装饰，不修改游戏文件，也不调整 Windows 电源计划、CPU 亲和性或其他进程优先级。

## 한국어 요약

POM-Max 3.0.0은 ADOFAI 3.2.0-3.3.1용 성능 모드입니다. 차트 이벤트와 장식을 유지하면서 텍스처 사전 크기 제한, 로딩 단계 스케줄링, 비동기 업로드 버퍼 및 DOTween 용량 사전 할당을 적용합니다. 게임 파일, Windows 전원 계획, CPU 선호도 및 다른 프로세스 우선순위는 변경하지 않습니다.
