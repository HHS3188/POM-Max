# Performance Optimization MAX

Performance Optimization MAX is a UnityModManager mod for **A Dance of Fire and Ice**. It targets heavy custom levels with many decorations, filters, particles, background video, or forced frame-rate events.

The UMM mod name is **Performance Optimization MAX**. The repository name is **POM Max**.

## Features

- Force a high target frame rate, disable VSync, disable anti-aliasing, and block level `SetFrameRate` events.
- Reduce blocking load/restart work by skipping pre-load cleanup that often stalls large custom levels.
- Add a configurable load timeout that returns to a safe scene when loading takes too long.
- Optimize decoded custom level data in non-editor scenes.
- Performance/MAX profiles can remove expensive visual events, background video, particles, filter/bloom/screen effects, and cap visual decorations.
- Runtime shader simplification and visual-only decoration update throttling for heavy decoration maps.
- Optional Windows performance mode raises the game process priority and can lower other user-session process priorities.

## Install

1. Download `POM-Max-1.0.0.zip` from the latest GitHub release.
2. Install it with UnityModManager.
3. Enable **Performance Optimization MAX** in the UMM mod list.

## Profiles

- `Off`: no optimization.
- `Balanced`: frame pacing, load cleanup skip, load timeout, and frame-rate event blocking.
- `Performance`: Balanced plus heavy filter/particle/shader/decor optimization.
- `MAX`: Performance plus stronger visual reduction and decoration update throttling.

## Notes

- Compatibility is documented per GitHub release, not in this README.
- Performance/MAX profiles can change how a level looks because expensive visual effects may be reduced.
- Editor scene level data is not optimized, so opening and saving a level in the editor will not write reduced map data.
- The Windows performance mode changes process priorities only while the mod is active. It does not overclock hardware or change permanent power-plan settings.

---

# 中文

Performance Optimization MAX 是用于 **冰与火之舞** 的 UnityModManager 性能优化 mod，主要面向装饰、滤镜、粒子、背景视频或强制帧率事件很多的自定义谱面。

UMM 中显示的 mod 名称是 **Performance Optimization MAX**，仓库名称是 **POM Max**。

## 功能

- 强制目标帧率、关闭垂直同步、关闭抗锯齿，并阻止谱面的 `SetFrameRate` 事件。
- 跳过容易阻塞的大谱面加载前清理，降低加载和死亡重开的卡顿。
- 支持自定义最大加载时间，超时后自动退回安全场景。
- 在非编辑器场景优化解码后的自定义谱面数据。
- Performance/MAX 档可移除高开销视觉事件、背景视频、粒子、滤镜、Bloom、屏幕效果，并限制视觉装饰数量。
- 针对大量装饰谱面，支持运行时简化装饰 shader 和节流纯视觉装饰更新。
- 可选 Windows 性能模式会提高游戏进程优先级，并可降低同一用户会话下其他进程优先级。

## 安装

1. 从 GitHub 最新 Release 下载 `POM-Max-1.0.0.zip`。
2. 用 UnityModManager 安装。
3. 在 UMM mod 列表启用 **Performance Optimization MAX**。

## 档位

- `Off`: 不进行优化。
- `Balanced`: 帧率策略、跳过加载前清理、加载超时、阻止强制帧率事件。
- `Performance`: 在 Balanced 基础上增加滤镜、粒子、shader、装饰优化。
- `MAX`: 在 Performance 基础上进一步削减视觉效果，并节流装饰更新。

## 注意

- 兼容性写在每个 GitHub Release 中，不写在 README。
- Performance/MAX 档可能改变谱面视觉表现，因为高开销视觉效果会被削减。
- 编辑器场景不会优化谱面数据，避免打开并保存谱面时写入被削减的数据。
- Windows 性能模式只在 mod 启用时调整进程优先级，不会超频，也不会永久修改电源计划。

---

# 한국어

Performance Optimization MAX는 **A Dance of Fire and Ice**용 UnityModManager 성능 최적화 모드입니다. 장식, 필터, 파티클, 배경 비디오, 강제 프레임 이벤트가 많은 커스텀 레벨을 대상으로 합니다.

UMM 모드 이름은 **Performance Optimization MAX**이고, 저장소 이름은 **POM Max**입니다.

## 기능

- 높은 목표 FPS 강제, VSync 비활성화, 안티앨리어싱 비활성화, 레벨 `SetFrameRate` 이벤트 차단.
- 큰 레벨 로딩 전에 발생하는 정리 작업을 건너뛰어 로딩 및 사망 후 재시작 지연을 줄입니다.
- 사용자 지정 최대 로딩 시간을 지원하며, 초과 시 안전한 장면으로 자동 복귀합니다.
- 에디터가 아닌 장면에서 디코딩된 커스텀 레벨 데이터를 최적화합니다.
- Performance/MAX 프로필은 무거운 시각 이벤트, 배경 비디오, 파티클, 필터, Bloom, 화면 효과, 과도한 장식을 줄일 수 있습니다.
- 장식이 많은 레벨을 위해 런타임 장식 셰이더 단순화와 순수 시각 장식 업데이트 절감을 제공합니다.
- 선택적 Windows 성능 모드는 게임 프로세스 우선순위를 높이고 다른 사용자 세션 프로세스의 우선순위를 낮출 수 있습니다.

## 설치

1. GitHub 최신 Release에서 `POM-Max-1.0.0.zip`을 다운로드합니다.
2. UnityModManager로 설치합니다.
3. UMM 모드 목록에서 **Performance Optimization MAX**를 활성화합니다.

## 프로필

- `Off`: 최적화 없음.
- `Balanced`: 프레임 설정, 로딩 전 정리 건너뛰기, 로딩 시간 초과, 프레임 이벤트 차단.
- `Performance`: Balanced에 필터, 파티클, 셰이더, 장식 최적화를 추가합니다.
- `MAX`: Performance에 더 강한 시각 효과 감소와 장식 업데이트 절감을 추가합니다.

## 참고

- 호환성 정보는 README가 아니라 각 GitHub Release에 기록합니다.
- Performance/MAX 프로필은 무거운 시각 효과를 줄이므로 레벨의 외형이 달라질 수 있습니다.
- 저장 손상을 막기 위해 에디터 장면의 레벨 데이터는 최적화하지 않습니다.
- Windows 성능 모드는 모드가 활성화된 동안 프로세스 우선순위만 조정하며, 오버클럭이나 영구 전원 설정 변경을 하지 않습니다.
