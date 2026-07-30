# Performance Optimization MAX

Performance Optimization MAX is a UnityModManager mod for **A Dance of Fire and Ice**. It targets heavy custom `.adofai` levels with many decorations, filters, particles, background video, or forced frame-rate events.

The UMM mod name is **Performance Optimization MAX**. The repository name is **POM Max**.

## Documentation

- [Usage guide](docs/使用说明.md)
- Compatibility is documented per GitHub release, not in this README.

## Features

- Force target FPS, disable VSync/anti-aliasing, and block level `SetFrameRate` events.
- Reduce blocking load/restart work by skipping pre-load cleanup.
- Add a configurable load timeout that returns to a safe scene when loading takes too long.
- Optimize decoded custom level data outside the editor scene.
- `MAX` removes expensive visual events more conservatively than v1 to reduce blank decoration issues.
- `EXTREME` is the maximum-performance profile: background process throttling, high game priority, high-performance Windows power-plan request, and alpha-safe 8x texture downsampling.
- Compress custom-level textures, optionally lower their resolution, and compensate related background, decoration, and collider sizes.
- Show one aggregate result after every custom-level load, including event/decorations reductions, texture-memory savings, load time, and workload-reduction ratio.
- The UMM panel exposes only fixed profiles and their descriptions; internal parameters are applied automatically.

## Profiles

- `Off`: no optimization.
- `MAX`: stable strong preset with 240 FPS target, a 20,000-decoration cap, and 2x custom-texture downsampling.
- `EXTREME`: maximum-performance preset with an 8x texture divisor, maximum nonessential visual-event reduction, aggressive process scheduling, and no destructive runtime decoration cap or position/Shader frame skipping.

## Install

1. Download the latest `POM-Max-*.zip` from GitHub Releases.
2. Install it with UnityModManager.
3. Enable **Performance Optimization MAX** in the UMM mod list.

---

# 中文

Performance Optimization MAX 是用于 **冰与火之舞** 的 UnityModManager 性能优化 mod，主要面向装饰、滤镜、粒子、背景视频或强制帧率事件很多的 `.adofai` 自定义谱面。

UMM 中显示的 mod 名称是 **Performance Optimization MAX**，仓库名称是 **POM Max**。

## 文档

- [使用说明](docs/使用说明.md)
- 兼容性写在每个 GitHub Release 中，不写在 README。

## 功能

- 强制目标 FPS、关闭垂直同步/抗锯齿，并阻止谱面的 `SetFrameRate` 事件。
- 跳过容易阻塞的加载前清理，降低加载和死亡重开时的等待。
- 支持自定义最大加载时间，超时后自动退回安全场景。
- 在非编辑器场景优化解码后的自定义谱面数据。
- `MAX` 相比 v1 更保守地处理高开销视觉事件，减少装饰物空白显示问题。
- `极限性能` 是最高性能档：后台进程节流、提高游戏优先级、请求 Windows 高性能电源计划，以及带透明通道保护的 8 倍大纹理降采样。
- 安全降低自定义谱面纹理分辨率，并补偿对应的背景、装饰与碰撞区域尺寸；不再使用可能破坏透明遮罩的运行时块压缩。
- 每次加载自定义谱面后显示一次汇总结果，包括事件、装饰、纹理内存、加载耗时和负载优化比。
- UMM 设置页仅保留固定档位和档位说明，内部参数由模组自动套用。

## 档位

- `Off`: 不进行优化。
- `MAX`: 日常强优化档，目标 240 FPS、装饰上限 20000，并对自定义纹理进行 2 倍降采样。
- `极限性能`: 大纹理尺寸除数固定为最高 8 倍，保留全部运行时装饰和结构性动画事件，同时启用激进进程调度与质量优化。

## 安装

1. 从 GitHub Releases 下载最新 `POM-Max-*.zip`。
2. 用 UnityModManager 安装。
3. 在 UMM mod 列表启用 **Performance Optimization MAX**。

---

# 한국어

Performance Optimization MAX는 **A Dance of Fire and Ice**용 UnityModManager 성능 최적화 모드입니다. 장식, 필터, 파티클, 배경 비디오, 강제 프레임 이벤트가 많은 `.adofai` 커스텀 레벨을 대상으로 합니다.

UMM 모드 이름은 **Performance Optimization MAX**이고, 저장소 이름은 **POM Max**입니다.

## 문서

- [사용 설명](docs/使用说明.md)
- 호환성 정보는 README가 아니라 각 GitHub Release에 적습니다.

## 기능

- 목표 FPS 강제, VSync/안티앨리어싱 비활성화, 레벨 `SetFrameRate` 이벤트 차단.
- 로딩 전 정리 작업을 건너뛰어 로딩 및 사망 후 재시작 지연을 줄입니다.
- 최대 로딩 시간을 설정할 수 있으며, 초과 시 안전한 장면으로 자동 복귀합니다.
- 에디터가 아닌 장면에서 디코딩된 커스텀 레벨 데이터를 최적화합니다.
- `MAX`는 v1보다 보수적으로 무거운 시각 이벤트를 처리하여 장식이 빈 상태로 보이는 문제를 줄입니다.
- `EXTREME`은 최고 성능 프로필입니다. 백그라운드 프로세스 제한, 게임 우선순위 상승, Windows 고성능 전원 계획 및 알파 채널을 보존하는 8배 텍스처 다운샘플링을 적용합니다.
- 커스텀 레벨 텍스처를 압축하고 선택적으로 해상도를 낮추며 배경, 장식 및 충돌 영역 크기를 보정합니다.
- 커스텀 레벨을 로드할 때마다 이벤트, 장식, 텍스처 메모리, 로드 시간 및 부하 감소율을 한 번 요약합니다.
- UMM 설정은 영어, 중국어, 한국어 UI 텍스트를 포함합니다.

## 프로필

- `Off`: 최적화하지 않습니다.
- `MAX`: 더 강한 레벨 이벤트 최적화와 장식 수 제한. 기본적으로 장식 셰이더 단순화는 실행하지 않습니다.
- `EXTREME`: 8배 텍스처 축소와 공격적인 프로세스 최적화를 사용하는 최고 성능 프로필이며, 파괴적인 런타임 장식 제한이나 위치/셰이더 프레임 건너뛰기는 사용하지 않습니다.

## 설치

1. GitHub Releases에서 최신 `POM-Max-*.zip`을 다운로드합니다.
2. UnityModManager로 설치합니다.
3. UMM 모드 목록에서 **Performance Optimization MAX**를 활성화합니다.
