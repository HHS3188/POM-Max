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
- `OVERDRIVE` is a separate extreme mode for maximum performance release: background process throttling, high game priority, high-performance Windows power-plan request, and extra quality reductions.
- UMM settings include English, Chinese, and Korean UI text.

## Profiles

- `Off`: no optimization.
- `Normal`: safer frame pacing, load cleanup skip, load timeout, background video blocking, and frame-rate event blocking.
- `MAX`: stronger level event optimization and decoration count limiting. It no longer runs decoration shader simplification by default.
- `OVERDRIVE`: separate extreme mode. It can create visual side effects and restrict background apps more aggressively.

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
- `OVERDRIVE` 是独立的极限模式，用于最大化性能释放：后台进程节流、提高游戏优先级、请求 Windows 高性能电源计划，并进一步降低高开销画质设置。
- UMM 设置页支持英文、中文、韩文。

## 档位

- `Off`: 不进行优化。
- `Normal`: 更稳妥的帧率策略、跳过加载前清理、加载超时、禁用背景视频、阻止强制帧率事件。
- `MAX`: 更强的谱面事件优化和装饰数量限制。默认不再执行装饰 shader 简化。
- `OVERDRIVE`: 独立极限模式。可能带来视觉副作用，并更强地限制后台应用。

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
- `OVERDRIVE`는 별도의 극한 모드입니다. 백그라운드 프로세스 제한, 게임 우선순위 상승, Windows 고성능 전원 계획 요청, 추가 품질 감소를 적용합니다.
- UMM 설정은 영어, 중국어, 한국어 UI 텍스트를 포함합니다.

## 프로필

- `Off`: 최적화하지 않습니다.
- `Normal`: 안전한 프레임 설정, 로딩 전 정리 생략, 로딩 시간 초과, 배경 비디오 비활성화, 강제 프레임 이벤트 차단.
- `MAX`: 더 강한 레벨 이벤트 최적화와 장식 수 제한. 기본적으로 장식 셰이더 단순화는 실행하지 않습니다.
- `OVERDRIVE`: 별도 극한 모드입니다. 시각적 부작용이 생길 수 있고 백그라운드 앱을 더 강하게 제한합니다.

## 설치

1. GitHub Releases에서 최신 `POM-Max-*.zip`을 다운로드합니다.
2. UnityModManager로 설치합니다.
3. UMM 모드 목록에서 **Performance Optimization MAX**를 활성화합니다.
