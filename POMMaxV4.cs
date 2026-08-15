using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace POMMax
{
    internal static class V4Runtime
    {
        private static bool enabled;
        private static bool loading;
        private static float nextPatchAudit;
        private static float gameplayEvidenceAt;
        private static bool gameplayEvidenceLogged;
        private static string loadingLabel = "";
        private static float lastLoadSeconds;

        public static void Enable()
        {
            enabled = true;
            loading = false;
            nextPatchAudit = Time.realtimeSinceStartup + 30f;
            gameplayEvidenceAt = 0f;
            gameplayEvidenceLogged = false;
            StaticDecorationOptimizer.Enable();
            V4TextureHeaderCache.OnSettingsChanged();
            PerformanceTelemetry.Start();
            AdaptiveRuntimeScheduler.Reset();
            V4PatchDiagnostics.ReportRuntimeIdentity();
            AuditPatchSafety(true);
        }

        public static void Disable()
        {
            enabled = false;
            loading = false;
            PerformanceTelemetry.Stop();
            AdaptiveRuntimeScheduler.Reset();
            StaticDecorationOptimizer.Disable();
            V4TextureHeaderCache.Reset();
        }

        public static void Tick(float deltaTime, float now)
        {
            if (!enabled || Main.settings == null)
            {
                return;
            }

            PerformanceTelemetry.Tick(deltaTime, now);
            AdaptiveRuntimeScheduler.Tick(now);
            if (now >= nextPatchAudit
                && (ADOBase.customLevel == null || ADOBase.isEditingLevel))
            {
                nextPatchAudit = now + 30f;
                AuditPatchSafety(false);
            }

            if (!gameplayEvidenceLogged
                && gameplayEvidenceAt > 0f
                && now >= gameplayEvidenceAt
                && ADOBase.customLevel != null
                && !ADOBase.isEditingLevel)
            {
                gameplayEvidenceLogged = true;
                Main.modEntry.Logger.Log("[V4/Evidence] "
                    + PerformanceTelemetry.GetDetailedSummary().Replace('\n', ' ')
                    + " | "
                    + StaticDecorationOptimizer.GetDiagnostics().Replace('\n', ' '));
            }
        }

        public static void BeginLevelLoad(string label)
        {
            loading = true;
            loadingLabel = label ?? "";
            gameplayEvidenceAt = 0f;
            gameplayEvidenceLogged = false;
            StaticDecorationOptimizer.BeginLevelState();
            PerformanceTelemetry.BeginLevelLoad();
            AdaptiveRuntimeScheduler.SetLoading(true);
        }

        public static void EndLevelLoad(float elapsed)
        {
            loading = false;
            lastLoadSeconds = elapsed;
            PerformanceTelemetry.EndLevelLoad(elapsed);
            AdaptiveRuntimeScheduler.SetLoading(false);
            StaticDecorationOptimizer.ResetLevelState();
            gameplayEvidenceAt = Time.realtimeSinceStartup + 5f;
        }

        public static void ResetLevelState()
        {
            StaticDecorationOptimizer.ResetLevelState();
            AdaptiveRuntimeScheduler.SetLoading(false);
        }

        public static void OnSettingsChanged()
        {
            if (Main.settings == null)
            {
                return;
            }

            V4TextureHeaderCache.OnSettingsChanged();
            StaticDecorationOptimizer.ResetLevelState();
            AdaptiveRuntimeScheduler.Reset();
            PerformanceTelemetry.Reconfigure();
            if (enabled)
            {
                AuditPatchSafety(true);
            }
        }

        public static int GetAsyncUploadTimeSlice(bool isLoading)
        {
            if (Main.settings == null)
            {
                return isLoading ? 8 : 2;
            }

            if (isLoading)
            {
                return Main.settings.loadingUploadTimeSlice;
            }

            if (!Main.settings.adaptiveUploadBudget)
            {
                return Main.settings.gameplayUploadTimeSlice;
            }

            return AdaptiveRuntimeScheduler.CurrentSlice;
        }

        public static string GetLiveSummary()
        {
            if (!enabled || Main.EffectiveProfile() <= 0)
            {
                return Text("runtimeInactive");
            }

            StringBuilder builder = new StringBuilder(256);
            builder.Append(PerformanceTelemetry.GetSummary());
            builder.Append('\n');
            builder.Append(Text("adaptiveSlice"));
            builder.Append(AdaptiveRuntimeScheduler.CurrentSlice.ToString(CultureInfo.InvariantCulture));
            builder.Append(" ms");
            if (loading)
            {
                builder.Append("  |  ");
                builder.Append(Text("loading"));
                if (!string.IsNullOrEmpty(loadingLabel))
                {
                    builder.Append(loadingLabel);
                }
            }
            return builder.ToString();
        }

        public static string GetDiagnosticsText()
        {
            StringBuilder builder = new StringBuilder(768);
            builder.Append(Text("identity"));
            builder.Append(V4PatchDiagnostics.RuntimeIdentity);
            builder.Append('\n');
            builder.Append(PerformanceTelemetry.GetDetailedSummary());
            builder.Append('\n');
            builder.Append(StaticDecorationOptimizer.GetDiagnostics());
            builder.Append('\n');
            builder.Append(V4TextureHeaderCache.GetDiagnostics());
            builder.Append('\n');
            builder.Append(Text("lastLoad"));
            builder.Append(lastLoadSeconds.ToString("0.00", CultureInfo.InvariantCulture));
            builder.Append(" s");
            string conflicts = StaticDecorationOptimizer.PatchConflictText;
            if (!string.IsNullOrEmpty(conflicts))
            {
                builder.Append('\n');
                builder.Append(Text("patchConflict"));
                builder.Append(conflicts);
            }
            return builder.ToString();
        }

        public static string Text(string key)
        {
            int language = Main.settings != null ? Main.settings.language : 0;
            if (language == 1)
            {
                return TextEnglish(key);
            }
            if (language == 2)
            {
                return TextKorean(key);
            }
            return TextChinese(key);
        }

        private static void AuditPatchSafety(bool forceLog)
        {
            if (!enabled)
            {
                return;
            }
            StaticDecorationOptimizer.AuditPatchOwners(forceLog && Main.settings.reportPatchConflicts);
        }

        private static string TextChinese(string key)
        {
            switch (key)
            {
                case "tabOverview": return "运行概览";
                case "tabProfiles": return "性能档位";
                case "tabAdvanced": return "高级参数";
                case "tabDiagnostics": return "诊断信息";
                case "overviewTitle": return "实时性能概览";
                case "profileAdvancedNote": return "切换档位会载入推荐值；高级参数可在保存前单独调整。";
                case "advancedTitle": return "高级参数";
                case "runtimeGroup": return "运行时调度";
                case "telemetry": return "低开销性能采样";
                case "telemetryWindow": return "统计窗口（秒）";
                case "adaptiveUpload": return "自适应上传预算";
                case "loadingSlice": return "加载阶段上传时间片（毫秒）";
                case "gameplaySlice": return "游玩阶段上传时间片（毫秒）";
                case "decorationGroup": return "装饰热路径";
                case "staticPosition": return "跳过静态装饰重复定位";
                case "staticShader": return "缓存静态装饰着色状态";
                case "hitboxFilter": return "只检查有效装饰碰撞体";
                case "hitboxRefresh": return "碰撞列表复核间隔（毫秒）";
                case "decorationSafety": return "仅处理状态可证明不变的装饰；检测到动态变化、异常或补丁冲突时自动回退游戏原逻辑。";
                case "textureGroup": return "纹理与载入";
                case "textureFloor": return "纹理最短边下限（像素）";
                case "textureCache": return "图片头缓存上限";
                case "systemGroup": return "系统设置";
                case "patchConflicts": return "报告 Harmony 补丁冲突";
                case "diagnosticsTitle": return "运行诊断";
                case "runtimeDiagnostics": return "模块状态与实际命中";
                case "enabled": return "已开启";
                case "disabled": return "已关闭";
                case "collecting": return "正在采集";
                case "runtimeInactive": return "当前档位未启用运行时优化";
                case "adaptiveSlice": return "当前上传时间片：";
                case "loading": return "正在加载：";
                case "identity": return "运行环境：";
                case "lastLoad": return "最近一次加载耗时：";
                case "patchConflict": return "补丁冲突：";
                default: return key;
            }
        }

        private static string TextEnglish(string key)
        {
            switch (key)
            {
                case "tabOverview": return "Overview";
                case "tabProfiles": return "Profiles";
                case "tabAdvanced": return "Advanced";
                case "tabDiagnostics": return "Diagnostics";
                case "overviewTitle": return "Live performance";
                case "profileAdvancedNote": return "Changing a profile loads its recommended values. Advanced values remain editable before saving.";
                case "advancedTitle": return "Advanced parameters";
                case "runtimeGroup": return "Runtime scheduler";
                case "telemetry": return "Low-overhead telemetry";
                case "telemetryWindow": return "Telemetry window (seconds)";
                case "adaptiveUpload": return "Adaptive upload budget";
                case "loadingSlice": return "Loading upload slice (ms)";
                case "gameplaySlice": return "Gameplay upload slice (ms)";
                case "decorationGroup": return "Decoration hot paths";
                case "staticPosition": return "Skip unchanged decoration transforms";
                case "staticShader": return "Cache unchanged decoration shaders";
                case "hitboxFilter": return "Filter decoration hitbox checks";
                case "hitboxRefresh": return "Hitbox list audit interval (ms)";
                case "decorationSafety": return "Only provably unchanged states are skipped. Dynamic changes, failures, and patch conflicts fall back to the original game code.";
                case "textureGroup": return "Textures and loading";
                case "textureFloor": return "Minimum texture short side (px)";
                case "textureCache": return "Image header cache entries";
                case "systemGroup": return "System";
                case "patchConflicts": return "Report Harmony patch conflicts";
                case "diagnosticsTitle": return "Runtime diagnostics";
                case "runtimeDiagnostics": return "Module state and actual hits";
                case "enabled": return "Enabled";
                case "disabled": return "Disabled";
                case "collecting": return "Collecting";
                case "runtimeInactive": return "Runtime optimization is inactive for the selected profile";
                case "adaptiveSlice": return "Current upload slice: ";
                case "loading": return "Loading: ";
                case "identity": return "Runtime: ";
                case "lastLoad": return "Latest load time: ";
                case "patchConflict": return "Patch conflict: ";
                default: return key;
            }
        }

        private static string TextKorean(string key)
        {
            switch (key)
            {
                case "tabOverview": return "실행 개요";
                case "tabProfiles": return "성능 프로필";
                case "tabAdvanced": return "고급 설정";
                case "tabDiagnostics": return "진단";
                case "overviewTitle": return "실시간 성능";
                case "profileAdvancedNote": return "프로필 변경 시 권장값을 불러오며 저장 전 고급 값을 개별 조정할 수 있습니다.";
                case "advancedTitle": return "고급 설정";
                case "runtimeGroup": return "런타임 스케줄러";
                case "telemetry": return "저부하 성능 측정";
                case "telemetryWindow": return "측정 구간(초)";
                case "adaptiveUpload": return "적응형 업로드 예산";
                case "loadingSlice": return "로딩 업로드 시간(ms)";
                case "gameplaySlice": return "플레이 업로드 시간(ms)";
                case "decorationGroup": return "장식 핫패스";
                case "staticPosition": return "정적 장식 위치 계산 생략";
                case "staticShader": return "정적 장식 셰이더 상태 캐시";
                case "hitboxFilter": return "유효 장식 충돌만 검사";
                case "hitboxRefresh": return "충돌 목록 재검사 간격(ms)";
                case "decorationSafety": return "변하지 않았음이 확인된 상태만 생략하며 동적 변화, 오류, 패치 충돌 시 원본 코드로 복귀합니다.";
                case "textureGroup": return "텍스처와 로딩";
                case "textureFloor": return "텍스처 최소 짧은 변(px)";
                case "textureCache": return "이미지 헤더 캐시 수";
                case "systemGroup": return "시스템";
                case "patchConflicts": return "Harmony 패치 충돌 보고";
                case "diagnosticsTitle": return "실행 진단";
                case "runtimeDiagnostics": return "모듈 상태 및 실제 적용";
                case "enabled": return "켜짐";
                case "disabled": return "꺼짐";
                case "collecting": return "수집 중";
                case "runtimeInactive": return "선택한 프로필에서 런타임 최적화가 꺼져 있습니다";
                case "adaptiveSlice": return "현재 업로드 시간: ";
                case "loading": return "로딩 중: ";
                case "identity": return "실행 환경: ";
                case "lastLoad": return "최근 로딩 시간: ";
                case "patchConflict": return "패치 충돌: ";
                default: return key;
            }
        }
    }

    internal static class PerformanceTelemetry
    {
        private const int MaximumSamples = 4096;
        private static readonly float[] frameSamples = new float[MaximumSamples];
        private static readonly float[] sortedSamples = new float[MaximumSamples];
        private static int capacity = 1200;
        private static int writeIndex;
        private static int sampleCount;
        private static float nextAggregateAt;
        private static bool running;
        private static float averageMs;
        private static float p95Ms;
        private static float p99Ms;
        private static float maximumMs;
        private static long managedBytes;
        private static long allocatedBytes;
        private static long reservedBytes;
        private static long gcAllocatedFrame;
        private static long drawCalls;
        private static long batches;
        private static long triangles;
        private static float lastLevelLoadSeconds;
        private static ProfilerRecorder gcAllocRecorder;
        private static ProfilerRecorder drawCallRecorder;
        private static ProfilerRecorder batchRecorder;
        private static ProfilerRecorder triangleRecorder;

        public static float P95Milliseconds { get { return p95Ms; } }

        public static void Start()
        {
            Stop();
            running = ShouldCollect();
            ResetSamples();
            if (!running)
            {
                return;
            }

            gcAllocRecorder = TryStartRecorder(ProfilerCategory.Memory, "GC Allocated In Frame");
            drawCallRecorder = TryStartRecorder(ProfilerCategory.Render, "Draw Calls Count");
            batchRecorder = TryStartRecorder(ProfilerCategory.Render, "Batches Count");
            triangleRecorder = TryStartRecorder(ProfilerCategory.Render, "Triangles Count");
        }

        public static void Stop()
        {
            DisposeRecorder(ref gcAllocRecorder);
            DisposeRecorder(ref drawCallRecorder);
            DisposeRecorder(ref batchRecorder);
            DisposeRecorder(ref triangleRecorder);
            running = false;
        }

        public static void Reconfigure()
        {
            bool shouldCollect = ShouldCollect();
            if (shouldCollect != running)
            {
                if (shouldCollect)
                {
                    Start();
                }
                else
                {
                    Stop();
                    ResetSamples();
                }
                return;
            }

            capacity = CalculateCapacity();
            if (sampleCount > capacity)
            {
                ResetSamples();
            }
        }

        public static void Tick(float deltaTime, float now)
        {
            if (!running)
            {
                return;
            }

            if (deltaTime > 0f && deltaTime < 10f)
            {
                frameSamples[writeIndex] = deltaTime * 1000f;
                writeIndex++;
                if (writeIndex >= capacity)
                {
                    writeIndex = 0;
                }
                if (sampleCount < capacity)
                {
                    sampleCount++;
                }
            }

            if (now >= nextAggregateAt)
            {
                nextAggregateAt = now + 1f;
                Aggregate();
            }
        }

        public static void BeginLevelLoad()
        {
            ResetSamples();
        }

        public static void EndLevelLoad(float seconds)
        {
            lastLevelLoadSeconds = seconds;
            ResetSamples();
        }

        public static string GetSummary()
        {
            if (!running)
            {
                return V4Runtime.Text("telemetry") + ": " + V4Runtime.Text("disabled");
            }
            if (sampleCount == 0)
            {
                return V4Runtime.Text("telemetry") + ": " + V4Runtime.Text("collecting");
            }

            float averageFps = averageMs > 0f ? 1000f / averageMs : 0f;
            return "FPS " + averageFps.ToString("0", CultureInfo.InvariantCulture)
                + "  |  P95 " + p95Ms.ToString("0.00", CultureInfo.InvariantCulture) + " ms"
                + "  |  P99 " + p99Ms.ToString("0.00", CultureInfo.InvariantCulture) + " ms"
                + "  |  GC " + ToMegabytes(gcAllocatedFrame).ToString("0.00", CultureInfo.InvariantCulture) + " MB/f";
        }

        public static string GetDetailedSummary()
        {
            if (!running)
            {
                return "Telemetry: disabled";
            }

            StringBuilder builder = new StringBuilder(320);
            builder.Append("Frame avg/P95/P99/max: ");
            builder.Append(averageMs.ToString("0.00", CultureInfo.InvariantCulture));
            builder.Append(" / ");
            builder.Append(p95Ms.ToString("0.00", CultureInfo.InvariantCulture));
            builder.Append(" / ");
            builder.Append(p99Ms.ToString("0.00", CultureInfo.InvariantCulture));
            builder.Append(" / ");
            builder.Append(maximumMs.ToString("0.00", CultureInfo.InvariantCulture));
            builder.Append(" ms\nMemory managed/allocated/reserved: ");
            builder.Append(ToMegabytes(managedBytes).ToString("0.0", CultureInfo.InvariantCulture));
            builder.Append(" / ");
            builder.Append(ToMegabytes(allocatedBytes).ToString("0.0", CultureInfo.InvariantCulture));
            builder.Append(" / ");
            builder.Append(ToMegabytes(reservedBytes).ToString("0.0", CultureInfo.InvariantCulture));
            builder.Append(" MB\nRender draw/batch/triangles: ");
            builder.Append(drawCalls.ToString(CultureInfo.InvariantCulture));
            builder.Append(" / ");
            builder.Append(batches.ToString(CultureInfo.InvariantCulture));
            builder.Append(" / ");
            builder.Append(triangles.ToString(CultureInfo.InvariantCulture));
            builder.Append("  |  load ");
            builder.Append(lastLevelLoadSeconds.ToString("0.00", CultureInfo.InvariantCulture));
            builder.Append(" s");
            return builder.ToString();
        }

        private static bool ShouldCollect()
        {
            return Main.settings != null
                && Main.EffectiveProfile() > 0
                && (Main.settings.enableTelemetry || Main.settings.adaptiveUploadBudget);
        }

        private static int CalculateCapacity()
        {
            int fps = Main.settings != null ? Main.settings.targetFps : 240;
            int seconds = Main.settings != null ? Main.settings.telemetryWindowSeconds : 5;
            fps = Clamp(fps, 30, 240);
            return Clamp(fps * seconds, 120, MaximumSamples);
        }

        private static void ResetSamples()
        {
            capacity = CalculateCapacity();
            writeIndex = 0;
            sampleCount = 0;
            nextAggregateAt = 0f;
            averageMs = 0f;
            p95Ms = 0f;
            p99Ms = 0f;
            maximumMs = 0f;
        }

        private static void Aggregate()
        {
            int count = sampleCount;
            if (count <= 0)
            {
                return;
            }

            double total = 0d;
            float max = 0f;
            for (int i = 0; i < count; i++)
            {
                float value = frameSamples[i];
                sortedSamples[i] = value;
                total += value;
                if (value > max)
                {
                    max = value;
                }
            }
            Array.Sort(sortedSamples, 0, count);
            averageMs = (float)(total / count);
            p95Ms = sortedSamples[PercentileIndex(count, 0.95f)];
            p99Ms = sortedSamples[PercentileIndex(count, 0.99f)];
            maximumMs = max;

            try
            {
                managedBytes = Profiler.GetMonoUsedSizeLong();
                allocatedBytes = Profiler.GetTotalAllocatedMemoryLong();
                reservedBytes = Profiler.GetTotalReservedMemoryLong();
            }
            catch
            {
                managedBytes = GC.GetTotalMemory(false);
            }

            gcAllocatedFrame = ReadRecorder(gcAllocRecorder);
            drawCalls = ReadRecorder(drawCallRecorder);
            batches = ReadRecorder(batchRecorder);
            triangles = ReadRecorder(triangleRecorder);
        }

        private static int PercentileIndex(int count, float percentile)
        {
            int index = (int)Math.Ceiling(count * percentile) - 1;
            return Clamp(index, 0, count - 1);
        }

        private static ProfilerRecorder TryStartRecorder(ProfilerCategory category, string name)
        {
            try
            {
                return ProfilerRecorder.StartNew(category, name, 1);
            }
            catch
            {
                return default(ProfilerRecorder);
            }
        }

        private static void DisposeRecorder(ref ProfilerRecorder recorder)
        {
            try
            {
                if (recorder.Valid)
                {
                    recorder.Dispose();
                }
            }
            catch
            {
            }
            recorder = default(ProfilerRecorder);
        }

        private static long ReadRecorder(ProfilerRecorder recorder)
        {
            try
            {
                return recorder.Valid ? recorder.LastValue : 0L;
            }
            catch
            {
                return 0L;
            }
        }

        private static float ToMegabytes(long bytes)
        {
            return bytes / 1048576f;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }

    internal static class AdaptiveRuntimeScheduler
    {
        private static float nextAdjustmentAt;
        private static bool loading;
        private static int currentSlice = 2;

        public static int CurrentSlice
        {
            get
            {
                if (Main.settings == null)
                {
                    return 2;
                }
                return loading ? Main.settings.loadingUploadTimeSlice : currentSlice;
            }
        }

        public static void Reset()
        {
            nextAdjustmentAt = 0f;
            loading = false;
            currentSlice = Main.settings != null ? Main.settings.gameplayUploadTimeSlice : 2;
        }

        public static void SetLoading(bool value)
        {
            loading = value;
            if (!value && Main.settings != null)
            {
                currentSlice = Main.settings.gameplayUploadTimeSlice;
                nextAdjustmentAt = 0f;
            }
        }

        public static void Tick(float now)
        {
            if (loading || Main.settings == null || !Main.settings.adaptiveUploadBudget || Main.EffectiveProfile() <= 0)
            {
                return;
            }
            if (now < nextAdjustmentAt)
            {
                return;
            }
            nextAdjustmentAt = now + 2f;

            float p95 = PerformanceTelemetry.P95Milliseconds;
            if (p95 <= 0f)
            {
                currentSlice = Main.settings.gameplayUploadTimeSlice;
                return;
            }

            int fps = Main.settings.targetFps;
            if (fps < 30) fps = 30;
            if (fps > 240) fps = 240;
            float targetMilliseconds = 1000f / fps;
            if (p95 > targetMilliseconds * 1.35f)
            {
                currentSlice = 1;
            }
            else if (p95 < targetMilliseconds * 0.85f)
            {
                currentSlice = Main.settings.gameplayUploadTimeSlice;
            }
            else
            {
                currentSlice = Math.Min(Main.settings.gameplayUploadTimeSlice, 2);
            }

            try
            {
                if (!Main.IsLoadingLevel())
                {
                    QualitySettings.asyncUploadTimeSlice = currentSlice;
                }
            }
            catch
            {
            }
        }
    }

    internal static class V4TextureHeaderCache
    {
        private sealed class Entry
        {
            public long LastWriteTicks;
            public long Length;
            public int Width;
            public int Height;
        }

        private static readonly object sync = new object();
        private static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static long hits;
        private static long misses;

        public static bool TryGetDimensions(string filePath, out int width, out int height)
        {
            width = 0;
            height = 0;
            try
            {
                string fullPath = Path.GetFullPath(filePath);
                FileInfo info = new FileInfo(fullPath);
                if (!info.Exists)
                {
                    return false;
                }

                long ticks = info.LastWriteTimeUtc.Ticks;
                long length = info.Length;
                lock (sync)
                {
                    Entry cached;
                    if (entries.TryGetValue(fullPath, out cached)
                        && cached.LastWriteTicks == ticks
                        && cached.Length == length)
                    {
                        hits++;
                        width = cached.Width;
                        height = cached.Height;
                        return true;
                    }
                }

                misses++;
                if (!TextureOptimization.TryReadImageDimensionsUncached(fullPath, out width, out height))
                {
                    return false;
                }

                lock (sync)
                {
                    int maximum = Main.settings != null ? Main.settings.textureHeaderCacheEntries : 4096;
                    if (entries.Count >= maximum && !entries.ContainsKey(fullPath))
                    {
                        entries.Clear();
                    }
                    entries[fullPath] = new Entry
                    {
                        LastWriteTicks = ticks,
                        Length = length,
                        Width = width,
                        Height = height
                    };
                }
                return true;
            }
            catch
            {
                misses++;
                return TextureOptimization.TryReadImageDimensionsUncached(filePath, out width, out height);
            }
        }

        public static void OnSettingsChanged()
        {
            int maximum = Main.settings != null ? Main.settings.textureHeaderCacheEntries : 4096;
            lock (sync)
            {
                if (entries.Count > maximum)
                {
                    entries.Clear();
                }
            }
        }

        public static void Reset()
        {
            lock (sync)
            {
                entries.Clear();
                hits = 0L;
                misses = 0L;
            }
        }

        public static string GetDiagnostics()
        {
            lock (sync)
            {
                return "Texture header cache hit/miss/entries: "
                    + hits.ToString(CultureInfo.InvariantCulture) + " / "
                    + misses.ToString(CultureInfo.InvariantCulture) + " / "
                    + entries.Count.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    internal static class StaticDecorationOptimizer
    {
        internal struct ShaderPatchState
        {
            public bool Candidate;
            public bool RanOriginal;
            public ShaderFingerprint Fingerprint;
        }

        private struct ShaderCacheEntry
        {
            public scrVisualDecoration Owner;
            public ShaderFingerprint Fingerprint;
        }

        internal struct ShaderFingerprint : IEquatable<ShaderFingerprint>
        {
            public bool Disable;
            public int SpriteId;
            public int TextureId;
            public Color Color;
            public float Opacity;
            public float RepeatX;
            public float RepeatY;
            public bool Smoothing;
            public bool Visible;
            public bool ForceHide;
            public bool SpriteRendererEnabled;
            public bool MeshObjectActive;
            public bool MeshRendererVisible;

            public bool Equals(ShaderFingerprint other)
            {
                return Disable == other.Disable
                    && SpriteId == other.SpriteId
                    && TextureId == other.TextureId
                    && Color.Equals(other.Color)
                    && Opacity.Equals(other.Opacity)
                    && RepeatX.Equals(other.RepeatX)
                    && RepeatY.Equals(other.RepeatY)
                    && Smoothing == other.Smoothing
                    && Visible == other.Visible
                    && ForceHide == other.ForceHide
                    && SpriteRendererEnabled == other.SpriteRendererEnabled
                    && MeshObjectActive == other.MeshObjectActive
                    && MeshRendererVisible == other.MeshRendererVisible;
            }
        }

        private const int FailureLimit = 3;
        private static readonly Dictionary<int, ShaderCacheEntry> shaderStates = new Dictionary<int, ShaderCacheEntry>();
        private static readonly List<scrDecoration> filteredHitboxes = new List<scrDecoration>();
        private static bool enabled;
        private static bool positionDisabled;
        private static bool shaderDisabled;
        private static bool hitboxDisabled;
        private static int positionFailures;
        private static int shaderFailures;
        private static int hitboxFailures;
        private static long positionSkips;
        private static long shaderSkips;
        private static long hitboxCandidatesAvoided;
        private static long hitboxListRebuilds;
        private static scrDecorationManager cachedManager;
        private static int cachedDecorationCount = -1;
        private static float nextHitboxRefresh;
        private static string patchConflictText = "";

        public static string PatchConflictText { get { return patchConflictText; } }

        public static void Enable()
        {
            enabled = true;
            positionDisabled = false;
            shaderDisabled = false;
            hitboxDisabled = false;
            positionFailures = 0;
            shaderFailures = 0;
            hitboxFailures = 0;
            positionSkips = 0L;
            shaderSkips = 0L;
            hitboxCandidatesAvoided = 0L;
            hitboxListRebuilds = 0L;
            patchConflictText = "";
            ResetLevelState();
        }

        public static void Disable()
        {
            enabled = false;
            ResetLevelState();
        }

        public static void ResetLevelState()
        {
            shaderStates.Clear();
            filteredHitboxes.Clear();
            cachedManager = null;
            cachedDecorationCount = -1;
            nextHitboxRefresh = 0f;
        }

        public static void BeginLevelState()
        {
            ResetLevelState();
            positionSkips = 0L;
            shaderSkips = 0L;
            hitboxCandidatesAvoided = 0L;
            hitboxListRebuilds = 0L;
        }

        public static bool PrefixLogicUpdate(scrDecoration instance, bool disableUpdateShader)
        {
            if (!CanSkipPosition(instance))
            {
                return true;
            }

            try
            {
                if (ADOBase.customLevel != null)
                {
                    instance.UpdateShader(disableUpdateShader);
                }
                if (instance.useHitbox)
                {
                    instance.UpdateHitboxState();
                }
                positionSkips++;
                return false;
            }
            catch (Exception ex)
            {
                RegisterPositionFailure(ex);
                return true;
            }
        }

        public static bool PrefixUpdateShader(
            scrVisualDecoration instance,
            bool disable,
            DecorationBlendMode blendMode,
            MaskingType maskingType,
            ref ShaderPatchState state)
        {
            state = default(ShaderPatchState);
            if (!CanCacheShader(instance, blendMode, maskingType))
            {
                return true;
            }

            try
            {
                ShaderFingerprint fingerprint = CaptureShaderFingerprint(instance, disable);
                state.Candidate = true;
                state.Fingerprint = fingerprint;
                ShaderCacheEntry previous;
                if (shaderStates.TryGetValue(instance.GetInstanceID(), out previous)
                    && previous.Owner == instance
                    && previous.Fingerprint.Equals(fingerprint))
                {
                    shaderSkips++;
                    return false;
                }
                state.RanOriginal = true;
                return true;
            }
            catch (Exception ex)
            {
                RegisterShaderFailure(ex);
                state = default(ShaderPatchState);
                return true;
            }
        }

        public static void PostfixUpdateShader(scrVisualDecoration instance, ShaderPatchState state)
        {
            if (!state.Candidate || !state.RanOriginal || instance == null || shaderDisabled)
            {
                return;
            }
            try
            {
                shaderStates[instance.GetInstanceID()] = new ShaderCacheEntry
                {
                    Owner = instance,
                    Fingerprint = CaptureShaderFingerprint(instance, state.Fingerprint.Disable)
                };
            }
            catch (Exception ex)
            {
                RegisterShaderFailure(ex);
            }
        }

        public static bool PrefixDecorationManagerUpdate(scrDecorationManager manager)
        {
            if (!CanFilterHitboxes(manager))
            {
                return true;
            }

            try
            {
                RefreshHitboxListIfNeeded(manager);
                int count = filteredHitboxes.Count;
                hitboxCandidatesAvoided += Math.Max(0, manager.allDecorations.Count - count);
                for (int i = 0; i < count; i++)
                {
                    scrDecoration decoration = filteredHitboxes[i];
                    if (decoration != null)
                    {
                        decoration.CheckHitboxHit();
                    }
                }
                if (scrCamera.instance != null)
                {
                    scrCamera.instance.lockCustomFrameUpdate = true;
                }
                return false;
            }
            catch (Exception ex)
            {
                RegisterHitboxFailure(ex);
                return true;
            }
        }

        public static void AuditPatchOwners(bool logSafeState)
        {
            if (!enabled || Main.modEntry == null)
            {
                return;
            }

            string positionOwners = FindExternalOwners(AccessTools.Method(typeof(scrDecoration), "LogicUpdate", new Type[] { typeof(bool) }));
            string shaderOwners = FindExternalOwners(AccessTools.Method(typeof(scrVisualDecoration), "UpdateShader", new Type[] { typeof(bool) }));
            string hitboxOwners = FindExternalOwners(AccessTools.Method(typeof(scrDecorationManager), "Update"));
            List<string> conflicts = new List<string>();
            if (!string.IsNullOrEmpty(positionOwners))
            {
                positionDisabled = true;
                conflicts.Add("LogicUpdate=" + positionOwners);
            }
            if (!string.IsNullOrEmpty(shaderOwners))
            {
                shaderDisabled = true;
                conflicts.Add("UpdateShader=" + shaderOwners);
            }
            if (!string.IsNullOrEmpty(hitboxOwners))
            {
                hitboxDisabled = true;
                conflicts.Add("DecorationManager.Update=" + hitboxOwners);
            }

            string joined = string.Join("; ", conflicts.ToArray());
            if (!string.Equals(joined, patchConflictText, StringComparison.Ordinal))
            {
                patchConflictText = joined;
                if (!string.IsNullOrEmpty(joined))
                {
                    Main.modEntry.Logger.Warning("[V4/PatchSafety] External patch owner detected. Affected fast path disabled: " + joined + ".");
                }
            }
            else if (logSafeState && string.IsNullOrEmpty(joined))
            {
                Main.modEntry.Logger.Log("[V4/PatchSafety] Decoration hot-path ownership verified.");
            }
        }

        public static string GetDiagnostics()
        {
            return "Decoration position/shader skips: "
                + positionSkips.ToString(CultureInfo.InvariantCulture) + " / "
                + shaderSkips.ToString(CultureInfo.InvariantCulture)
                + "\nHitbox checks avoided: " + hitboxCandidatesAvoided.ToString(CultureInfo.InvariantCulture)
                + "  |  list rebuilds: " + hitboxListRebuilds.ToString(CultureInfo.InvariantCulture)
                + "  |  breakers pos/shader/hitbox: "
                + (positionDisabled ? "open" : "closed") + " / "
                + (shaderDisabled ? "open" : "closed") + " / "
                + (hitboxDisabled ? "open" : "closed");
        }

        private static bool IsGameplayReady()
        {
            return enabled
                && Main.EffectiveProfile() > 0
                && ADOBase.customLevel != null
                && !ADOBase.isEditingLevel
                && ADOBase.controller != null;
        }

        private static bool CanSkipPosition(scrDecoration instance)
        {
            if (!IsGameplayReady()
                || positionDisabled
                || Main.settings == null
                || !Main.settings.optimizeStaticDecorationPosition
                || instance == null
                || instance.pivotTrans == null
                || instance.parallax == null
                || instance.followPlanet != null
                || instance.stickToFloor
                || instance.lockRotation
                || instance.lockScale
                || instance.parallax.clampToScreen
                || !Mathf.Approximately(instance.parallax.multiplier_x, 0f)
                || !Mathf.Approximately(instance.parallax.multiplier_y, 0f))
            {
                return false;
            }
            if (instance.eventTweens != null && instance.eventTweens.Count != 0)
            {
                return false;
            }
            return instance.sourceLevelEvent == null
                || string.IsNullOrEmpty(instance.sourceLevelEvent.GetString("components"));
        }

        private static bool CanCacheShader(scrVisualDecoration instance, DecorationBlendMode blendMode, MaskingType maskingType)
        {
            if (!IsGameplayReady()
                || shaderDisabled
                || Main.settings == null
                || !Main.settings.cacheStaticDecorationShaders
                || instance == null
                || instance.stickToFloor
                || blendMode != DecorationBlendMode.None
                || maskingType != MaskingType.None)
            {
                return false;
            }
            if (instance.eventTweens != null && instance.eventTweens.Count != 0)
            {
                return false;
            }
            if (instance.sourceLevelEvent != null
                && !string.IsNullOrEmpty(instance.sourceLevelEvent.GetString("components")))
            {
                return false;
            }
            return instance.cfpCache == null || instance.cfpCache.Length == 0;
        }

        private static bool CanFilterHitboxes(scrDecorationManager manager)
        {
            return IsGameplayReady()
                && !hitboxDisabled
                && Main.settings != null
                && Main.settings.filterDecorationHitboxLoop
                && manager != null
                && manager.allDecorations != null;
        }

        private static ShaderFingerprint CaptureShaderFingerprint(scrVisualDecoration instance, bool disable)
        {
            SpriteRenderer spriteRenderer = instance.spriteRenderer;
            Sprite sprite = spriteRenderer != null ? spriteRenderer.sprite : null;
            Texture texture = sprite != null ? sprite.texture : null;
            return new ShaderFingerprint
            {
                Disable = disable,
                SpriteId = sprite != null ? sprite.GetInstanceID() : 0,
                TextureId = texture != null ? texture.GetInstanceID() : 0,
                Color = instance.color,
                Opacity = instance.opacity,
                RepeatX = instance.repeatX,
                RepeatY = instance.repeatY,
                Smoothing = instance.smoothing,
                Visible = instance.GetVisible(),
                ForceHide = instance.forceHide,
                SpriteRendererEnabled = spriteRenderer != null && spriteRenderer.enabled,
                MeshObjectActive = instance.meshRendererObj != null && instance.meshRendererObj.activeSelf,
                MeshRendererVisible = instance.meshRenderer != null && instance.meshRenderer.isVisible
            };
        }

        private static void RefreshHitboxListIfNeeded(scrDecorationManager manager)
        {
            List<scrDecoration> source = manager.allDecorations;
            float now = Time.realtimeSinceStartup;
            bool rebuild = cachedManager != manager
                || cachedDecorationCount != source.Count
                || now >= nextHitboxRefresh;
            if (!rebuild)
            {
                return;
            }

            cachedManager = manager;
            cachedDecorationCount = source.Count;
            nextHitboxRefresh = now + (Main.settings.hitboxRefreshMilliseconds / 1000f);
            filteredHitboxes.Clear();
            hitboxListRebuilds++;
            for (int i = 0; i < source.Count; i++)
            {
                scrDecoration decoration = source[i];
                if (decoration != null
                    && decoration.useHitbox
                    && decoration.hitboxDetectTarget == HitboxDetectTarget.Decoration)
                {
                    filteredHitboxes.Add(decoration);
                }
            }
        }

        private static string FindExternalOwners(MethodBase method)
        {
            if (method == null || Main.modEntry == null)
            {
                return "missing-method";
            }
            Patches patches = Harmony.GetPatchInfo(method);
            if (patches == null)
            {
                return "";
            }

            List<string> owners = new List<string>();
            for (int i = 0; i < patches.Owners.Count; i++)
            {
                string owner = patches.Owners[i];
                if (!string.Equals(owner, Main.modEntry.Info.Id, StringComparison.Ordinal)
                    && !owners.Contains(owner))
                {
                    owners.Add(owner);
                }
            }
            owners.Sort(StringComparer.Ordinal);
            return string.Join(",", owners.ToArray());
        }

        private static void RegisterPositionFailure(Exception ex)
        {
            positionFailures++;
            if (positionFailures >= FailureLimit)
            {
                positionDisabled = true;
                LogBreaker("position", ex);
            }
        }

        private static void RegisterShaderFailure(Exception ex)
        {
            shaderFailures++;
            if (shaderFailures >= FailureLimit)
            {
                shaderDisabled = true;
                shaderStates.Clear();
                LogBreaker("shader", ex);
            }
        }

        private static void RegisterHitboxFailure(Exception ex)
        {
            hitboxFailures++;
            if (hitboxFailures >= FailureLimit)
            {
                hitboxDisabled = true;
                filteredHitboxes.Clear();
                LogBreaker("hitbox", ex);
            }
        }

        private static void LogBreaker(string module, Exception ex)
        {
            if (Main.modEntry != null)
            {
                Main.modEntry.Logger.Warning("[V4/CircuitBreaker] " + module + " fast path disabled for this session: " + ex.Message);
            }
        }
    }

    internal static class V4PatchDiagnostics
    {
        private static string runtimeIdentity = "unknown";
        public static string RuntimeIdentity { get { return runtimeIdentity; } }

        public static void ReportRuntimeIdentity()
        {
            try
            {
                Guid mvid = typeof(scrController).Assembly.ManifestModule.ModuleVersionId;
                runtimeIdentity = "ADOFAI " + Application.version + ", Assembly-CSharp MVID " + mvid.ToString("D");
                if (Main.modEntry != null)
                {
                    Main.modEntry.Logger.Log("[V4/Runtime] " + runtimeIdentity + ".");
                }
            }
            catch (Exception ex)
            {
                runtimeIdentity = "ADOFAI " + Application.version + ", MVID unavailable";
                if (Main.modEntry != null)
                {
                    Main.modEntry.Logger.Warning("[V4/Runtime] Assembly identity unavailable: " + ex.Message);
                }
            }
        }
    }

    [HarmonyPatch(typeof(scrDecoration), "LogicUpdate", new Type[] { typeof(bool) })]
    internal static class V4DecorationLogicUpdatePatch
    {
        private static bool Prefix(scrDecoration __instance, bool disableUpdateShader)
        {
            return StaticDecorationOptimizer.PrefixLogicUpdate(__instance, disableUpdateShader);
        }
    }

    [HarmonyPatch(typeof(scrVisualDecoration), "UpdateShader", new Type[] { typeof(bool) })]
    internal static class V4VisualDecorationShaderPatch
    {
        private static bool Prefix(
            scrVisualDecoration __instance,
            bool disable,
            DecorationBlendMode ___blendMode,
            MaskingType ___maskingType,
            ref StaticDecorationOptimizer.ShaderPatchState __state)
        {
            return StaticDecorationOptimizer.PrefixUpdateShader(
                __instance,
                disable,
                ___blendMode,
                ___maskingType,
                ref __state);
        }

        private static void Postfix(
            scrVisualDecoration __instance,
            StaticDecorationOptimizer.ShaderPatchState __state)
        {
            StaticDecorationOptimizer.PostfixUpdateShader(__instance, __state);
        }
    }

    [HarmonyPatch(typeof(scrDecorationManager), "Update")]
    internal static class V4DecorationManagerUpdatePatch
    {
        private static bool Prefix(scrDecorationManager __instance)
        {
            return StaticDecorationOptimizer.PrefixDecorationManagerUpdate(__instance);
        }
    }
}
