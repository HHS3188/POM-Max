using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ADOFAI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityModManagerNet;

namespace POMMax
{
    public class Settings : UnityModManager.ModSettings
    {
        public int language = 0;
        public int optimizationProfile = 1;
        public int targetFps = 240;
        public bool forceTargetFrameRate = true;
        public bool disableVSync = true;
        public bool disableAntiAliasing = true;
        public bool disableCustomFrameRateEvents = true;
        public bool skipPreloadCleanup = true;
        public bool optimizeLevelEvents = true;
        public bool disableBackgroundVideo = true;
        public bool capDecorations = true;
        public int maxDecorations = 20000;
        public bool simplifyDecorationShaders = false;
        public bool throttleDecorationUpdates = true;
        public int decorationUpdateStride = 2;
        public bool enableLoadTimeout = true;
        public int maxLoadSeconds = 45;
        public bool boostGamePriority = true;
        public bool preventSleepDuringGameplay = true;
        public bool overdriveMode = false;
        public bool throttleBackgroundProcesses = true;
        public bool verboseLogging = false;
        public bool optimizeCustomTextures = true;
        public float textureScaleDivisor = 1f;
        public bool compressCustomTextures = false;
        public bool roundTextureDimensionsToMultipleOf4 = true;
        public bool adjustTextureGeometry = true;
        public bool showTextureOptimizationStats = true;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            UnityModManager.ModSettings.Save(this, modEntry);
        }
    }

    public static class Main
    {
        private const int ProfileOff = 0;
        private const int ProfileMax = 1;

        private const string SourceUrl = "https://github.com/HHS3188/POM-Max";
        private const string DocsUrl = "https://github.com/HHS3188/POM-Max/blob/main/docs/%E4%BD%BF%E7%94%A8%E8%AF%B4%E6%98%8E.md";
        private const string UltimatePerformanceScheme = "e9a42b02-d5df-448d-aa00-03f14749eb61";
        private const string HighPerformanceScheme = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

        private const uint EsContinuous = 0x80000000;
        private const uint EsSystemRequired = 0x00000001;
        private const uint EsDisplayRequired = 0x00000002;

        public static UnityModManager.ModEntry modEntry;
        public static Settings settings;

        private static Harmony harmony;
        private static bool modEnabled;
        private static bool capturedQualitySettings;
        private static int originalTargetFrameRate;
        private static int originalVSync;
        private static int originalAntiAliasing;
        private static AnisotropicFiltering originalAnisotropicFiltering;
        private static int originalPixelLightCount;
        private static float originalShadowDistance;
        private static int originalShadowCascades;
        private static float originalLodBias;
        private static int originalMaximumLodLevel;
        private static bool originalRealtimeReflectionProbes;
        private static bool originalSoftParticles;
        private static int originalGlobalTextureMipmapLimit;
        private static GCLatencyMode originalGcLatencyMode;

        private static bool loadingActive;
        private static bool abortingLoad;
        private static int loadingDepth;
        private static float loadingStartedAt;
        private static string loadingLabel = "";
        private static float nextRuntimeApply;
        private static float nextBackgroundThrottle;
        private static bool runtimeDecorationBudgetAnalyzed;
        private static int runtimeDecorationManagerId;

        private static bool capturedOwnPriority;
        private static ProcessPriorityClass originalOwnPriority;
        private static bool capturedProcessorAffinity;
        private static IntPtr originalProcessorAffinity;
        private static bool powerPlanCaptured;
        private static bool powerPlanApplied;
        private static bool timerPeriodApplied;
        private static string originalPowerSchemeGuid;
        private static readonly Dictionary<int, ProcessPriorityClass> backgroundPriorities = new Dictionary<int, ProcessPriorityClass>();
        private static readonly HashSet<string> protectedProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Idle", "System", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass",
            "svchost", "fontdrvhost", "dwm", "audiodg", "explorer", "steam", "steamwebhelper",
            "A Dance of Fire and Ice", "UnityCrashHandler64", "UnityModManager", "OpenAI.Codex", "Codex"
        };

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint period);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint period);

        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint flags);

        public static bool Load(UnityModManager.ModEntry entry)
        {
            modEntry = entry;
            settings = UnityModManager.ModSettings.Load<Settings>(entry);
            ClampSettings();
            ApplySelectedProfilePreset(false);

            entry.OnToggle = OnToggle;
            entry.OnGUI = OnGUI;
            entry.OnSaveGUI = OnSaveGUI;
            entry.OnUpdate = OnUpdate;
            entry.OnUnload = OnUnload;

            entry.Logger.Log("POM Max loaded.");
            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry entry, bool value)
        {
            try
            {
                if (value)
                {
                    if (harmony == null)
                    {
                        harmony = new Harmony(entry.Info.Id);
                        harmony.PatchAll(Assembly.GetExecutingAssembly());
                    }

                    modEnabled = true;
                    OptimizationNotificationOverlay.Ensure();
                    abortingLoad = false;
                    RestoreRuntimeDecorations();
                    TextureOptimization.ResetSession(true);
                    CaptureQualitySettings();
                    ApplyRuntimePerformance(true);
                    ApplyProcessBoost(true);
                    entry.Logger.Log("POM Max enabled. " + GetProfileDiagnosticText());
                }
                else
                {
                    modEnabled = false;
                    OptimizationNotificationOverlay.Hide();
                    loadingActive = false;
                    loadingDepth = 0;
                    abortingLoad = false;
                    RestoreRuntimeDecorations();
                    TextureOptimization.ResetSession(true);

                    RestoreRuntimeSettings();
                    RestoreBackgroundPriorities();
                    ApplyProcessBoost(false);

                    if (harmony != null)
                    {
                        harmony.UnpatchAll(entry.Info.Id);
                        harmony = null;
                    }

                    entry.Logger.Log("POM Max disabled.");
                }

                return true;
            }
            catch (Exception ex)
            {
                modEnabled = false;
                entry.Logger.Error("POM Max failed to toggle.");
                entry.Logger.LogException(ex);
                return false;
            }
        }

        private static bool OnUnload(UnityModManager.ModEntry entry)
        {
            RestoreRuntimeDecorations();
            TextureOptimization.ResetSession(true);
            RestoreRuntimeSettings();
            RestoreBackgroundPriorities();
            ApplyProcessBoost(false);
            GuiTheme.Dispose();
            OptimizationNotificationOverlay.Dispose();
            return true;
        }

        private static void OnUpdate(UnityModManager.ModEntry entry, float deltaTime)
        {
            if (!modEnabled || settings == null)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            TextureOptimization.Tick(now);
            if (now >= nextRuntimeApply)
            {
                nextRuntimeApply = now + 1f;
                ApplyRuntimePerformance(false);
                ApplyProcessBoost(true);
                RefreshRuntimeDecorationBudget(false);
            }

            if (IsOverdriveMode() && settings.throttleBackgroundProcesses && now >= nextBackgroundThrottle)
            {
                nextBackgroundThrottle = now + 30f;
                ThrottleBackgroundProcesses();
            }

            if (settings.enableLoadTimeout && loadingActive && !abortingLoad)
            {
                float elapsed = now - loadingStartedAt;
                if (elapsed > settings.maxLoadSeconds)
                {
                    AbortLoading(elapsed);
                }
            }
        }

        private static void OnGUI(UnityModManager.ModEntry entry)
        {
            ClampSettings();
            GuiTheme.Ensure();

            Color oldColor = GUI.color;
            Color oldContentColor = GUI.contentColor;
            Color oldBackgroundColor = GUI.backgroundColor;
            try
            {
                GUILayout.BeginVertical(GuiTheme.Outer, GUILayout.ExpandWidth(true));
                GUILayout.BeginHorizontal();
                GUILayout.Label(Text("title"), GuiTheme.Title);
                GUILayout.FlexibleSpace();
                DrawLanguageButton(0, "中文");
                DrawLanguageButton(1, "EN");
                DrawLanguageButton(2, "한국어");
                GUILayout.EndHorizontal();

                GUILayout.Space(12f);
                GUILayout.BeginVertical(GuiTheme.Panel, GUILayout.ExpandWidth(true));
                GUILayout.Label(Text("profile"), GuiTheme.Subtitle);
                GUILayout.Space(8f);
                GUILayout.BeginHorizontal();
                DrawProfileButton(ProfileOff, Text("profileOff"));
                DrawProfileButton(ProfileMax, Text("profileMax"));
                DrawOverdriveButton();
                GUILayout.EndHorizontal();

                GUILayout.Space(14f);
                GUILayout.BeginVertical(GuiTheme.Card, GUILayout.ExpandWidth(true));
                GUILayout.Label(GetSelectedProfileName(), GuiTheme.CardTitle);
                GUILayout.Space(3f);
                GUILayout.Label(GetSelectedProfileDescription(), GuiTheme.Body);
                GUILayout.Space(3f);
                GUILayout.Label(GetSelectedProfileConfiguration(), GuiTheme.Secondary);
                GUILayout.EndVertical();

                GUILayout.Space(10f);
                GUILayout.BeginVertical(GuiTheme.Card, GUILayout.ExpandWidth(true));
                GUILayout.Label(Text("latestResult"), GuiTheme.CardTitle);
                GUILayout.Space(4f);
                GUILayout.Label(TextureOptimization.GetStatusText(settings.language), GuiTheme.Body);
                GUILayout.EndVertical();

                GUILayout.Space(9f);
                GUILayout.Label(Text("profileApplyNote"), GuiTheme.Secondary);
                GUILayout.EndVertical();
                GUILayout.EndVertical();
            }
            finally
            {
                GUI.color = oldColor;
                GUI.contentColor = oldContentColor;
                GUI.backgroundColor = oldBackgroundColor;
            }
        }

        private static void OnSaveGUI(UnityModManager.ModEntry entry)
        {
            ApplySelectedProfilePreset(false);
            ClampSettings();
            settings.Save(entry);
        }

        public static int EffectiveProfile()
        {
            if (!modEnabled || settings == null)
            {
                return ProfileOff;
            }

            if (IsOverdriveMode())
            {
                return ProfileMax;
            }

            return settings.optimizationProfile;
        }

        public static bool IsLoadingLevel()
        {
            return loadingActive;
        }

        public static bool IsOverdriveMode()
        {
            return modEnabled && settings != null && settings.overdriveMode;
        }

        public static void ResetRuntimeDecorationBudget()
        {
            RestoreRuntimeDecorations();
            runtimeDecorationBudgetAnalyzed = false;
            runtimeDecorationManagerId = 0;
        }

        public static void RefreshRuntimeDecorationBudget(bool rebuild)
        {
            if (!modEnabled || settings == null || !IsOverdriveMode())
            {
                RestoreRuntimeDecorations();
                return;
            }

            scrDecorationManager manager;
            try
            {
                manager = scrDecorationManager.instance;
            }
            catch
            {
                return;
            }

            if (manager == null || manager.allDecorations == null)
            {
                return;
            }

            int managerId = manager.GetInstanceID();
            if (!rebuild
                && runtimeDecorationBudgetAnalyzed
                && runtimeDecorationManagerId == managerId)
            {
                return;
            }

            RestoreRuntimeDecorations();

            int activeCount = 0;
            int protectedCount = 0;
            for (int i = 0; i < manager.allDecorations.Count; i++)
            {
                scrDecoration decoration = manager.allDecorations[i];
                if (decoration == null || decoration.gameObject == null || !decoration.gameObject.activeSelf)
                {
                    continue;
                }

                activeCount++;
                if (IsProtectedRuntimeDecoration(decoration))
                {
                    protectedCount++;
                }
            }

            runtimeDecorationBudgetAnalyzed = true;
            runtimeDecorationManagerId = managerId;
            TextureOptimization.RecordRuntimeDecorations(
                activeCount,
                0,
                protectedCount,
                activeCount);

            if (rebuild && modEntry != null)
            {
                modEntry.Logger.Log(
                    "[RuntimeDecorationBudget] compatibility guard: active=" + activeCount
                    + ", remaining=" + activeCount
                    + ", optimized=0"
                    + ", protected=" + protectedCount
                    + ", preservedAll=true"
                    + ".");
            }
        }

        private static bool IsProtectedRuntimeDecoration(scrDecoration decoration)
        {
            if (decoration == null || !(decoration is scrVisualDecoration))
            {
                return true;
            }

            if (decoration.sourceLevelEvent == null
                || decoration.useHitbox
                || decoration.followPlanet != null
                || decoration.stickToFloor
                || decoration.placementType == DecPlacementType.Camera
                || decoration.placementType == DecPlacementType.CameraAspect)
            {
                return true;
            }

            if ((decoration.cfpCache != null && decoration.cfpCache.Length > 0)
                || (decoration.hitEffects != null && decoration.hitEffects.Count > 0)
                || (decoration.hitboxEvents != null && decoration.hitboxEvents.Count > 0))
            {
                return true;
            }

            if (IsProtectedDecoration(decoration.sourceLevelEvent)
                || ContainsProtectedUiToken(decoration.decorationTag)
                || ContainsProtectedUiToken(decoration.gameObjectName)
                || ContainsProtectedUiToken(decoration.gameObject.name))
            {
                return true;
            }

            return false;
        }

        private static void RestoreRuntimeDecorations()
        {
            runtimeDecorationBudgetAnalyzed = false;
            runtimeDecorationManagerId = 0;
        }

        public static bool ShouldBlockCustomFrameRate(bool enable)
        {
            return modEnabled && settings != null && settings.disableCustomFrameRateEvents && enable;
        }

        public static void OptimizeLevelData(LevelData data)
        {
            if (data == null)
            {
                return;
            }

            int beforeEvents = data.levelEvents != null ? data.levelEvents.Count : 0;
            int beforeDecorations = data.decorations != null ? data.decorations.Count : 0;
            if (EffectiveProfile() == ProfileOff || !settings.optimizeLevelEvents)
            {
                TextureOptimization.RecordLevelData(
                    beforeEvents,
                    beforeEvents,
                    beforeDecorations,
                    beforeDecorations,
                    false);
                return;
            }

            if (ADOBase.isLevelEditor && ADOBase.editor != null)
            {
                Verbose("Skipped LevelData optimization in editor scene to avoid saving reduced maps.");
                TextureOptimization.RecordLevelData(
                    beforeEvents,
                    beforeEvents,
                    beforeDecorations,
                    beforeDecorations,
                    true);
                return;
            }

            int profile = EffectiveProfile();
            bool overdrive = IsOverdriveMode();

            data.levelEvents.RemoveAll(delegate(LevelEvent ev)
            {
                return ShouldRemoveActionEvent(ev, profile, overdrive);
            });

            data.decorations.RemoveAll(delegate(LevelEvent ev)
            {
                return ShouldRemoveDecorationEvent(ev, profile, overdrive);
            });

            if (settings.disableBackgroundVideo && profile > ProfileOff)
            {
                TrySetEventValue(data.miscSettings, "bgVideo", "");
            }

            if (settings.capDecorations && profile >= ProfileMax)
            {
                CapDecorations(data, settings.maxDecorations);
            }

            int removedEvents = beforeEvents - data.levelEvents.Count;
            int removedDecorations = beforeDecorations - data.decorations.Count;
            TextureOptimization.RecordLevelData(
                beforeEvents,
                data.levelEvents.Count,
                beforeDecorations,
                data.decorations.Count,
                false);
            if (removedEvents > 0 || removedDecorations > 0)
            {
                Verbose("Optimized level data. Removed events=" + removedEvents + ", decorations=" + removedDecorations + ".");
            }
        }

        public static void BeginLoading(string label)
        {
            if (!modEnabled || settings == null)
            {
                return;
            }

            loadingDepth++;
            if (loadingActive)
            {
                return;
            }

            loadingActive = true;
            abortingLoad = false;
            loadingStartedAt = Time.realtimeSinceStartup;
            loadingLabel = label ?? "Loading";
            Verbose("Loading started: " + loadingLabel);
        }

        public static void EndLoading(string label)
        {
            if (!modEnabled)
            {
                return;
            }

            if (loadingDepth > 0)
            {
                loadingDepth--;
            }

            if (loadingDepth > 0 || !loadingActive)
            {
                return;
            }

            float elapsed = Time.realtimeSinceStartup - loadingStartedAt;
            loadingActive = false;
            abortingLoad = false;
            TextureOptimization.RecordLoadingDuration(elapsed);
            Verbose("Loading finished: " + (label ?? loadingLabel) + " in " + elapsed.ToString("0.00") + "s.");
        }

        public static IEnumerator WrapLoadingCoroutine(IEnumerator inner, string label)
        {
            BeginLoading(label);
            while (true)
            {
                object current = null;
                bool moved = false;
                try
                {
                    moved = inner != null && inner.MoveNext();
                    if (moved)
                    {
                        current = inner.Current;
                    }
                }
                catch
                {
                    EndLoading(label);
                    throw;
                }

                if (!moved)
                {
                    break;
                }

                yield return current;
            }

            EndLoading(label);
        }

        public static void MaybeFlushUnusedMemory(ADOBase instance)
        {
            if (modEnabled && settings != null && settings.skipPreloadCleanup)
            {
                Verbose("Skipped audio memory flush before level load.");
                return;
            }

            if (instance != null)
            {
                instance.FlushUnusedMemory();
            }
        }

        public static AsyncOperation MaybeUnloadUnusedAssets()
        {
            if (modEnabled && settings != null && settings.skipPreloadCleanup)
            {
                Verbose("Skipped Resources.UnloadUnusedAssets before level load.");
                return null;
            }

            return Resources.UnloadUnusedAssets();
        }

        private static bool ShouldRemoveActionEvent(LevelEvent ev, int profile, bool overdrive)
        {
            if (ev == null)
            {
                return false;
            }

            LevelEventType type = ev.eventType;
            if (settings.disableCustomFrameRateEvents && type == LevelEventType.SetFrameRate)
            {
                return true;
            }

            if (profile >= ProfileMax)
            {
                if (type == LevelEventType.SetFilter ||
                    type == LevelEventType.SetFilterAdvanced ||
                    type == LevelEventType.Bloom ||
                    type == LevelEventType.HallOfMirrors ||
                    type == LevelEventType.ScreenTile ||
                    type == LevelEventType.ScreenScroll ||
                    type == LevelEventType.SetParticle ||
                    type == LevelEventType.EmitParticle)
                {
                    return true;
                }
            }

            if (overdrive)
            {
                if (type == LevelEventType.Flash ||
                    type == LevelEventType.ShakeScreen)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ShouldRemoveDecorationEvent(LevelEvent ev, int profile, bool overdrive)
        {
            if (ev == null)
            {
                return false;
            }

            if (profile >= ProfileMax && ev.eventType == LevelEventType.AddParticle)
            {
                return true;
            }

            if (overdrive && !IsProtectedDecoration(ev))
            {
                if (ev.eventType == LevelEventType.AddParticle)
                {
                    return true;
                }
            }

            return false;
        }

        private static void CapDecorations(LevelData data, int max)
        {
            if (data == null || data.decorations == null || max <= 0 || data.decorations.Count <= max)
            {
                return;
            }

            List<LevelEvent> protectedDecorations = new List<LevelEvent>();
            List<LevelEvent> removableDecorations = new List<LevelEvent>();
            for (int i = 0; i < data.decorations.Count; i++)
            {
                LevelEvent ev = data.decorations[i];
                if (IsProtectedDecoration(ev))
                {
                    protectedDecorations.Add(ev);
                }
                else
                {
                    removableDecorations.Add(ev);
                }
            }

            int slots = max - protectedDecorations.Count;
            HashSet<LevelEvent> keep = new HashSet<LevelEvent>();
            for (int i = 0; i < protectedDecorations.Count; i++)
            {
                keep.Add(protectedDecorations[i]);
            }

            if (slots > 0 && removableDecorations.Count > 0)
            {
                double step = (double)removableDecorations.Count / (double)slots;
                for (int i = 0; i < slots; i++)
                {
                    int index = (int)Math.Floor(i * step);
                    if (index < 0)
                    {
                        index = 0;
                    }
                    if (index >= removableDecorations.Count)
                    {
                        index = removableDecorations.Count - 1;
                    }
                    keep.Add(removableDecorations[index]);
                }
            }

            for (int i = data.decorations.Count - 1; i >= 0; i--)
            {
                if (!keep.Contains(data.decorations[i]))
                {
                    data.decorations.RemoveAt(i);
                }
            }
        }

        private static bool IsProtectedDecoration(LevelEvent ev)
        {
            Dictionary<string, object> eventData = ev != null ? ev.GetData() : null;
            if (eventData == null)
            {
                return false;
            }

            if (ev.eventType == LevelEventType.AddText || ev.eventType == LevelEventType.SetText)
            {
                return true;
            }

            if (HasNonNoneHitbox(ev))
            {
                return true;
            }

            object value;
            if (eventData.TryGetValue("components", out value) && value != null && value.ToString().Trim().Length > 0)
            {
                return true;
            }

            if (eventData.TryGetValue("tag", out value) && value != null)
            {
                string tag = value.ToString();
                if (tag.IndexOf("[attachToTile]", StringComparison.OrdinalIgnoreCase) >= 0
                    || ContainsProtectedUiToken(tag))
                {
                    return true;
                }
            }

            string[] protectedKeys =
            {
                "relativeTo",
                "decorationImage",
                "image",
                "text",
                "id"
            };
            for (int i = 0; i < protectedKeys.Length; i++)
            {
                if (eventData.TryGetValue(protectedKeys[i], out value)
                    && value != null
                    && ContainsProtectedUiToken(value.ToString()))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsProtectedUiToken(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            string normalized = value.ToLowerInvariant();
            string[] tokens =
            {
                "camera",
                "screen",
                "ui",
                "hud",
                "judge",
                "judgement",
                "judgment",
                "result",
                "score",
                "accuracy",
                "timing",
                "判定",
                "结算",
                "成绩"
            };
            for (int i = 0; i < tokens.Length; i++)
            {
                if (normalized.IndexOf(tokens[i], StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasNonNoneHitbox(LevelEvent ev)
        {
            Dictionary<string, object> eventData = ev != null ? ev.GetData() : null;
            object value;
            if (eventData == null || !eventData.TryGetValue("hitbox", out value) || value == null)
            {
                return false;
            }

            try
            {
                return Convert.ToInt32(value) != (int)HitboxType.None;
            }
            catch
            {
                return value.ToString() != "None" && value.ToString() != "0";
            }
        }

        private static void TrySetEventValue(LevelEvent ev, string key, object value)
        {
            Dictionary<string, object> eventData = ev != null ? ev.GetData() : null;
            if (eventData == null)
            {
                return;
            }

            if (eventData.ContainsKey(key))
            {
                eventData[key] = value;
            }
        }

        private static void AbortLoading(float elapsed)
        {
            abortingLoad = true;
            loadingActive = false;
            loadingDepth = 0;
            string message = "Loading timeout after " + elapsed.ToString("0.0") + "s during " + loadingLabel + ". Returning to a safe scene.";
            if (modEntry != null)
            {
                modEntry.Logger.Warning(message);
            }

            try
            {
                Time.timeScale = 1f;
                AudioListener.pause = false;
                if (ADOBase.isLevelEditor && ADOBase.editor != null && ADOBase.controller != null)
                {
                    ADOBase.editor.SwitchToEditMode(false);
                    return;
                }
            }
            catch (Exception ex)
            {
                if (modEntry != null)
                {
                    modEntry.Logger.LogException(ex);
                }
            }

            try
            {
                SceneManager.LoadScene("scnCLS");
            }
            catch (Exception ex)
            {
                if (modEntry != null)
                {
                    modEntry.Logger.LogException(ex);
                }
            }
        }

        private static void CaptureQualitySettings()
        {
            if (capturedQualitySettings)
            {
                return;
            }

            capturedQualitySettings = true;
            originalTargetFrameRate = Application.targetFrameRate;
            originalVSync = QualitySettings.vSyncCount;
            originalAntiAliasing = QualitySettings.antiAliasing;
            originalAnisotropicFiltering = QualitySettings.anisotropicFiltering;
            originalPixelLightCount = QualitySettings.pixelLightCount;
            originalShadowDistance = QualitySettings.shadowDistance;
            originalShadowCascades = QualitySettings.shadowCascades;
            originalLodBias = QualitySettings.lodBias;
            originalMaximumLodLevel = QualitySettings.maximumLODLevel;
            originalRealtimeReflectionProbes = QualitySettings.realtimeReflectionProbes;
            originalSoftParticles = QualitySettings.softParticles;
            originalGlobalTextureMipmapLimit = QualitySettings.globalTextureMipmapLimit;
            try
            {
                originalGcLatencyMode = GCSettings.LatencyMode;
            }
            catch
            {
                originalGcLatencyMode = GCLatencyMode.Interactive;
            }
        }

        private static void ApplyRuntimePerformance(bool force)
        {
            if (!modEnabled || settings == null)
            {
                return;
            }

            int profile = EffectiveProfile();
            if (profile == ProfileOff)
            {
                return;
            }

            try
            {
                Application.runInBackground = true;
                Application.backgroundLoadingPriority = UnityEngine.ThreadPriority.High;
                if (settings.forceTargetFrameRate)
                {
                    int target = Math.Max(30, settings.targetFps);
                    if (force || Application.targetFrameRate != target)
                    {
                        Application.targetFrameRate = target;
                        RDUtils.targetFrameRate = target;
                    }
                }

                if (settings.disableVSync)
                {
                    QualitySettings.vSyncCount = 0;
                }

                if (settings.disableAntiAliasing && profile > ProfileOff)
                {
                    QualitySettings.antiAliasing = 0;
                }

                if (profile >= ProfileMax)
                {
                    QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
                }

                if (IsOverdriveMode())
                {
                    QualitySettings.pixelLightCount = 0;
                    QualitySettings.shadowDistance = 0f;
                    QualitySettings.shadowCascades = 0;
                    QualitySettings.realtimeReflectionProbes = false;
                    QualitySettings.softParticles = false;
                }

                try
                {
                    GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
                }
                catch
                {
                }
            }
            catch (Exception ex)
            {
                Verbose("ApplyRuntimePerformance failed: " + ex.Message);
            }
        }

        private static void RestoreRuntimeSettings()
        {
            try
            {
                if (capturedQualitySettings)
                {
                    Application.targetFrameRate = originalTargetFrameRate;
                    RDUtils.targetFrameRate = originalTargetFrameRate;
                    QualitySettings.vSyncCount = originalVSync;
                    QualitySettings.antiAliasing = originalAntiAliasing;
                    QualitySettings.anisotropicFiltering = originalAnisotropicFiltering;
                    QualitySettings.pixelLightCount = originalPixelLightCount;
                    QualitySettings.shadowDistance = originalShadowDistance;
                    QualitySettings.shadowCascades = originalShadowCascades;
                    QualitySettings.lodBias = originalLodBias;
                    QualitySettings.maximumLODLevel = originalMaximumLodLevel;
                    QualitySettings.realtimeReflectionProbes = originalRealtimeReflectionProbes;
                    QualitySettings.softParticles = originalSoftParticles;
                    QualitySettings.globalTextureMipmapLimit = originalGlobalTextureMipmapLimit;
                    try
                    {
                        GCSettings.LatencyMode = originalGcLatencyMode;
                    }
                    catch
                    {
                    }
                }

                SetThreadExecutionState(EsContinuous);
                if (timerPeriodApplied)
                {
                    timeEndPeriod(1);
                    timerPeriodApplied = false;
                }
                RestorePowerPlan();
            }
            catch
            {
            }
        }

        private static void ApplyProcessBoost(bool enable)
        {
            try
            {
                Process current = Process.GetCurrentProcess();
                if (enable && modEnabled && settings != null && settings.boostGamePriority && EffectiveProfile() > ProfileOff)
                {
                    bool overdrive = IsOverdriveMode();
                    if (!capturedOwnPriority)
                    {
                        originalOwnPriority = current.PriorityClass;
                        capturedOwnPriority = true;
                    }

                    ProcessPriorityClass desired = overdrive ? ProcessPriorityClass.High : ProcessPriorityClass.AboveNormal;
                    if (current.PriorityClass != desired)
                    {
                        current.PriorityClass = desired;
                    }

                    if (overdrive)
                    {
                        try
                        {
                            if (!capturedProcessorAffinity)
                            {
                                originalProcessorAffinity = current.ProcessorAffinity;
                                capturedProcessorAffinity = true;
                            }
                            current.ProcessorAffinity = BuildAffinityMask();
                        }
                        catch
                        {
                        }

                        TryApplyPowerPlan();
                    }
                    else
                    {
                        RestorePowerPlan();
                    }

                    try
                    {
                        System.Threading.Thread.CurrentThread.Priority = System.Threading.ThreadPriority.Highest;
                    }
                    catch
                    {
                    }

                    if (!timerPeriodApplied)
                    {
                        timerPeriodApplied = timeBeginPeriod(1) == 0;
                    }
                    if (settings.preventSleepDuringGameplay)
                    {
                        SetThreadExecutionState(EsContinuous | EsSystemRequired | EsDisplayRequired);
                    }
                }
                else
                {
                    if (capturedOwnPriority)
                    {
                        current.PriorityClass = originalOwnPriority;
                    }
                    if (capturedProcessorAffinity)
                    {
                        try
                        {
                            current.ProcessorAffinity = originalProcessorAffinity;
                        }
                        catch
                        {
                        }
                        capturedProcessorAffinity = false;
                    }
                    RestorePowerPlan();
                    SetThreadExecutionState(EsContinuous);
                    if (timerPeriodApplied)
                    {
                        timeEndPeriod(1);
                        timerPeriodApplied = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Verbose("ApplyProcessBoost failed: " + ex.Message);
            }
        }

        private static void ThrottleBackgroundProcesses()
        {
            Process current = null;
            try
            {
                current = Process.GetCurrentProcess();
            }
            catch
            {
                return;
            }

            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch
            {
                return;
            }

            for (int i = 0; i < processes.Length; i++)
            {
                Process p = processes[i];
                try
                {
                    if (p == null || p.Id == current.Id || p.SessionId != current.SessionId)
                    {
                        continue;
                    }

                    string name = p.ProcessName;
                    if (protectedProcessNames.Contains(name))
                    {
                        continue;
                    }

                    if (!backgroundPriorities.ContainsKey(p.Id))
                    {
                        backgroundPriorities[p.Id] = p.PriorityClass;
                    }

                    ProcessPriorityClass desired = IsOverdriveMode() ? ProcessPriorityClass.Idle : ProcessPriorityClass.BelowNormal;
                    if (p.PriorityClass != ProcessPriorityClass.Idle && p.PriorityClass != desired)
                    {
                        p.PriorityClass = desired;
                    }
                }
                catch
                {
                }
            }
        }

        private static void RestoreBackgroundPriorities()
        {
            foreach (KeyValuePair<int, ProcessPriorityClass> pair in new List<KeyValuePair<int, ProcessPriorityClass>>(backgroundPriorities))
            {
                try
                {
                    Process p = Process.GetProcessById(pair.Key);
                    p.PriorityClass = pair.Value;
                }
                catch
                {
                }
            }
            backgroundPriorities.Clear();
        }

        private static IntPtr BuildAffinityMask()
        {
            int processors = Math.Max(1, Environment.ProcessorCount);
            if (IntPtr.Size == 4)
            {
                int mask32 = processors >= 31 ? -1 : ((1 << processors) - 1);
                return new IntPtr(mask32);
            }

            long mask64 = processors >= 63 ? -1L : ((1L << processors) - 1L);
            return new IntPtr(mask64);
        }

        private static void TryApplyPowerPlan()
        {
            if (powerPlanApplied)
            {
                return;
            }

            if (!powerPlanCaptured)
            {
                string output;
                int exitCode;
                if (RunPowerCfg("/getactivescheme", out output, out exitCode) && exitCode == 0)
                {
                    Match match = Regex.Match(output ?? "", "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
                    if (match.Success)
                    {
                        originalPowerSchemeGuid = match.Value;
                    }
                }
                powerPlanCaptured = true;
            }

            string ignored;
            int code;
            if (RunPowerCfg("/setactive " + UltimatePerformanceScheme, out ignored, out code) && code == 0)
            {
                powerPlanApplied = true;
                return;
            }

            if (RunPowerCfg("/setactive " + HighPerformanceScheme, out ignored, out code) && code == 0)
            {
                powerPlanApplied = true;
            }
        }

        private static void RestorePowerPlan()
        {
            if (!powerPlanApplied || string.IsNullOrEmpty(originalPowerSchemeGuid))
            {
                powerPlanApplied = false;
                return;
            }

            string ignored;
            int code;
            RunPowerCfg("/setactive " + originalPowerSchemeGuid, out ignored, out code);
            powerPlanApplied = false;
        }

        private static bool RunPowerCfg(string arguments, out string output, out int exitCode)
        {
            output = "";
            exitCode = -1;
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = "powercfg.exe";
                startInfo.Arguments = arguments;
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                Process process = Process.Start(startInfo);
                if (process == null)
                {
                    return false;
                }

                if (!process.WaitForExit(2500))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                    }
                    return false;
                }

                output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                exitCode = process.ExitCode;
                return true;
            }
            catch (Exception ex)
            {
                Verbose("powercfg failed: " + ex.Message);
                return false;
            }
        }

        private static void ClampSettings()
        {
            if (settings == null)
            {
                settings = new Settings();
            }

            settings.language = Clamp(settings.language, 0, 2);
            settings.optimizationProfile = Clamp(settings.optimizationProfile, ProfileOff, ProfileMax);
            settings.targetFps = Clamp(settings.targetFps, 30, 10000);
            settings.maxLoadSeconds = Clamp(settings.maxLoadSeconds, 5, 600);
            settings.maxDecorations = Clamp(settings.maxDecorations, 100, 100000);
            settings.decorationUpdateStride = Clamp(settings.decorationUpdateStride, 1, 8);
            settings.textureScaleDivisor = Clamp(settings.textureScaleDivisor, 1f, 8f);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }
            if (value > max)
            {
                return max;
            }
            return value;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return min;
            }
            if (value < min)
            {
                return min;
            }
            if (value > max)
            {
                return max;
            }
            return value;
        }

        private static void DrawLanguageButton(int language, string label)
        {
            bool selected = settings.language == language;
            if (GUILayout.Button(
                label,
                selected ? GuiTheme.SmallButtonSelected : GuiTheme.SmallButton,
                GUILayout.Width(72f),
                GUILayout.Height(32f)))
            {
                settings.language = language;
            }
        }

        private static void DrawProfileButton(int profile, string label)
        {
            bool selected = !settings.overdriveMode && settings.optimizationProfile == profile;
            if (GUILayout.Button(
                label,
                selected ? GuiTheme.ProfileButtonSelected : GuiTheme.ProfileButton,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(44f))
                && !selected)
            {
                SelectProfile(profile, false);
            }
        }

        private static void DrawOverdriveButton()
        {
            bool selected = settings.overdriveMode;
            if (GUILayout.Button(
                Text("profileExtreme"),
                selected ? GuiTheme.ProfileButtonSelected : GuiTheme.ProfileButton,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(44f))
                && !selected)
            {
                SelectProfile(ProfileMax, true);
            }
        }

        private static void SelectProfile(int profile, bool overdrive)
        {
            settings.optimizationProfile = profile;
            settings.overdriveMode = overdrive;
            ApplySelectedProfilePreset(true);
        }

        private static void ApplySelectedProfilePreset(bool applyRuntime)
        {
            if (settings == null)
            {
                settings = new Settings();
            }

            if (settings.overdriveMode)
            {
                ApplyExtremePreset();
            }
            else if (settings.optimizationProfile >= ProfileMax)
            {
                ApplyMaxPreset();
            }
            else
            {
                ApplyOffPreset();
            }

            ClampSettings();
            if (!applyRuntime || !modEnabled)
            {
                return;
            }

            RestoreRuntimeDecorations();
            TextureOptimization.ResetSession(true);
            RestoreBackgroundPriorities();
            ApplyProcessBoost(false);
            RestoreRuntimeSettings();

            if (EffectiveProfile() > ProfileOff)
            {
                ApplyRuntimePerformance(true);
                ApplyProcessBoost(true);
            }
        }

        private static void ApplyOffPreset()
        {
            settings.optimizationProfile = ProfileOff;
            settings.overdriveMode = false;
            settings.targetFps = 240;
            settings.forceTargetFrameRate = false;
            settings.disableVSync = false;
            settings.disableAntiAliasing = false;
            settings.disableCustomFrameRateEvents = false;
            settings.skipPreloadCleanup = false;
            settings.optimizeLevelEvents = false;
            settings.disableBackgroundVideo = false;
            settings.capDecorations = false;
            settings.maxDecorations = 100000;
            settings.simplifyDecorationShaders = false;
            settings.throttleDecorationUpdates = false;
            settings.decorationUpdateStride = 1;
            settings.enableLoadTimeout = false;
            settings.maxLoadSeconds = 45;
            settings.boostGamePriority = false;
            settings.preventSleepDuringGameplay = false;
            settings.throttleBackgroundProcesses = false;
            settings.verboseLogging = false;
            settings.optimizeCustomTextures = false;
            settings.textureScaleDivisor = 1f;
            settings.compressCustomTextures = false;
            settings.roundTextureDimensionsToMultipleOf4 = false;
            settings.adjustTextureGeometry = true;
            settings.showTextureOptimizationStats = false;
        }

        private static void ApplyMaxPreset()
        {
            settings.optimizationProfile = ProfileMax;
            settings.overdriveMode = false;
            settings.targetFps = 240;
            settings.forceTargetFrameRate = true;
            settings.disableVSync = true;
            settings.disableAntiAliasing = true;
            settings.disableCustomFrameRateEvents = true;
            settings.skipPreloadCleanup = true;
            settings.optimizeLevelEvents = true;
            settings.disableBackgroundVideo = true;
            settings.capDecorations = true;
            settings.maxDecorations = 20000;
            settings.simplifyDecorationShaders = false;
            settings.throttleDecorationUpdates = false;
            settings.decorationUpdateStride = 2;
            settings.enableLoadTimeout = true;
            settings.maxLoadSeconds = 45;
            settings.boostGamePriority = true;
            settings.preventSleepDuringGameplay = true;
            settings.throttleBackgroundProcesses = false;
            settings.verboseLogging = false;
            settings.optimizeCustomTextures = true;
            settings.textureScaleDivisor = 2f;
            settings.compressCustomTextures = false;
            settings.roundTextureDimensionsToMultipleOf4 = true;
            settings.adjustTextureGeometry = true;
            settings.showTextureOptimizationStats = true;
        }

        private static void ApplyExtremePreset()
        {
            settings.optimizationProfile = ProfileMax;
            settings.overdriveMode = true;
            settings.targetFps = 10000;
            settings.forceTargetFrameRate = true;
            settings.disableVSync = true;
            settings.disableAntiAliasing = true;
            settings.disableCustomFrameRateEvents = true;
            settings.skipPreloadCleanup = true;
            settings.optimizeLevelEvents = true;
            settings.disableBackgroundVideo = true;
            settings.capDecorations = false;
            settings.maxDecorations = 100000;
            settings.simplifyDecorationShaders = false;
            settings.throttleDecorationUpdates = false;
            settings.decorationUpdateStride = 1;
            settings.enableLoadTimeout = true;
            settings.maxLoadSeconds = 45;
            settings.boostGamePriority = true;
            settings.preventSleepDuringGameplay = true;
            settings.throttleBackgroundProcesses = true;
            settings.verboseLogging = false;
            settings.optimizeCustomTextures = true;
            settings.textureScaleDivisor = 8f;
            settings.compressCustomTextures = false;
            settings.roundTextureDimensionsToMultipleOf4 = true;
            settings.adjustTextureGeometry = true;
            settings.showTextureOptimizationStats = true;
        }

        private static string GetSelectedProfileName()
        {
            if (settings.overdriveMode)
            {
                return Text("profileExtreme");
            }
            return settings.optimizationProfile >= ProfileMax ? Text("profileMax") : Text("profileOff");
        }

        private static string GetSelectedProfileDescription()
        {
            if (settings.overdriveMode)
            {
                return Text("profileExtremeDescription");
            }
            return settings.optimizationProfile >= ProfileMax
                ? Text("profileMaxDescription")
                : Text("profileOffDescription");
        }

        private static string GetSelectedProfileConfiguration()
        {
            if (settings.overdriveMode)
            {
                return Text("profileExtremeConfiguration");
            }
            return settings.optimizationProfile >= ProfileMax
                ? Text("profileMaxConfiguration")
                : Text("profileOffConfiguration");
        }

        private static string GetProfileDiagnosticText()
        {
            return "Profile=" + (settings.overdriveMode ? "EXTREME" : settings.optimizationProfile >= ProfileMax ? "MAX" : "OFF")
                + ", targetFps=" + settings.targetFps
                + ", maxDecorations=" + settings.maxDecorations
                + ", decorationStride=" + settings.decorationUpdateStride
                + ", textureDivisor=" + settings.textureScaleDivisor.ToString("0.##", CultureInfo.InvariantCulture)
                + ", textureCompression=" + settings.compressCustomTextures
                + ".";
        }

        private static void DrawIntField(string label, ref int value, int min, int max, int width)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(260));
            string text = GUILayout.TextField(value.ToString(), GUILayout.Width(width));
            int parsed;
            if (int.TryParse(text, out parsed))
            {
                value = Clamp(parsed, min, max);
            }
            GUILayout.Label(Text("range") + min + "-" + max);
            GUILayout.EndHorizontal();
        }

        private static void DrawFloatField(string label, ref float value, float min, float max, int width)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(260));
            string text = GUILayout.TextField(value.ToString("0.00", CultureInfo.InvariantCulture), GUILayout.Width(width));
            float parsed;
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                || float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
            {
                value = Clamp(parsed, min, max);
            }
            GUILayout.Label(Text("range") + min.ToString("0.00", CultureInfo.InvariantCulture) + "-"
                + max.ToString("0.00", CultureInfo.InvariantCulture));
            GUILayout.EndHorizontal();
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch (Exception ex)
            {
                if (modEntry != null)
                {
                    modEntry.Logger.Warning("Failed to open URL: " + url);
                    modEntry.Logger.LogException(ex);
                }
            }
        }

        private static string Text(string key)
        {
            int lang = settings != null ? settings.language : 1;
            if (lang == 0)
            {
                return TextChinese(key);
            }
            if (lang == 2)
            {
                return TextKorean(key);
            }
            return TextEnglish(key);
        }

        private static string TextEnglish(string key)
        {
            switch (key)
            {
                case "language": return "Language:";
                case "title": return "Performance Optimization MAX";
                case "profile": return "Optimization profile";
                case "profileOff": return "OFF";
                case "profileMax": return "MAX";
                case "profileExtreme": return "EXTREME";
                case "profileOffDescription": return "Disables all optimization and restores the captured game settings.";
                case "profileMaxDescription": return "Stable strong optimization with balanced visuals and performance.";
                case "profileExtremeDescription": return "Maximum performance with alpha-safe large-texture reduction and intact dynamic decorations.";
                case "profileOffConfiguration": return "Use this profile for compatibility checks.";
                case "profileMaxConfiguration": return "2x texture downsampling and high-cost event reduction; runtime decorations remain intact.";
                case "profileExtremeConfiguration": return "8x large-texture downsampling with a 32px safety floor; linked decorations and motion events remain intact.";
                case "profileApplyNote": return "Runtime changes apply now. Level optimization refreshes on the next load.";
                case "latestResult": return "Latest level optimization";
                case "runtime": return "Runtime frame pacing";
                case "forceFps": return "Force target frame rate";
                case "targetFps": return "Target FPS:";
                case "disableVsync": return "Disable VSync";
                case "disableAa": return "Disable anti-aliasing";
                case "blockFrameEvents": return "Block level SetFrameRate events";
                case "load": return "Load and restart";
                case "skipCleanup": return "Skip pre-load memory cleanup to reduce blocking load time";
                case "enableTimeout": return "Enable load timeout auto-return";
                case "timeout": return "Max load time (seconds):";
                case "levelData": return "Level data optimizer";
                case "optimizeEvents": return "Optimize decoded level events";
                case "disableVideo": return "Disable background video on optimized loads";
                case "capDecorations": return "Cap visual decorations in MAX/OVERDRIVE";
                case "maxDecorations": return "Max decorations:";
                case "simpleShaders": return "Simplify decoration shaders in OVERDRIVE only";
                case "throttleDecorations": return "Throttle visual-only decoration updates in OVERDRIVE only";
                case "stride": return "Decoration update stride:";
                case "textures": return "Custom texture optimizer";
                case "optimizeTextures": return "Optimize custom-level textures";
                case "textureDivisor": return "Texture scale divisor:";
                case "compressTextures": return "Compress textures after loading";
                case "roundTextures": return "Round texture dimensions to multiples of 4";
                case "adjustTextureGeometry": return "Compensate background, decoration, and collider sizes";
                case "showTextureStats": return "Show texture optimization statistics";
                case "textureWarning": return "Texture compression is lossy. Values above 1.00 reduce resolution; disable this section if a level displays incorrectly.";
                case "system": return "Windows performance mode";
                case "boostPriority": return "Raise game process priority";
                case "preventSleep": return "Prevent sleep and use 1 ms timer while active";
                case "backgroundThrottle": return "Lower priority of other user-session processes";
                case "verbose": return "Verbose log optimization details";
                case "range": return "Range: ";
                case "note": return "MAX/OVERDRIVE may reduce visual effects. Editor scene level data is not optimized to avoid saving reduced maps.";
                case "docsLink": return "Open usage guide";
                case "sourceLink": return "Open source code";
                default: return key;
            }
        }

        private static string TextChinese(string key)
        {
            switch (key)
            {
                case "language": return "语言:";
                case "title": return "性能优化 MAX";
                case "profile": return "优化档位";
                case "profileOff": return "关闭";
                case "profileMax": return "MAX";
                case "profileExtreme": return "极限性能";
                case "profileOffDescription": return "停用全部优化，并恢复已捕获的游戏设置。";
                case "profileMaxDescription": return "稳定强优化，兼顾画面表现与性能。";
                case "profileExtremeDescription": return "以稳定为前提追求极限性能，安全缩减大纹理并保留动态装饰。";
                case "profileOffConfiguration": return "适合排查兼容性问题。";
                case "profileMaxConfiguration": return "纹理降采样 2 倍并削减高开销事件；不裁剪运行时装饰。";
                case "profileExtremeConfiguration": return "大纹理最多降采样 8 倍并保留 32 像素安全下限；动态装饰与移动事件完整保留。";
                case "profileApplyNote": return "运行设置立即生效；谱面优化在下次加载时刷新。";
                case "latestResult": return "最近一次谱面优化";
                case "runtime": return "运行帧率策略";
                case "forceFps": return "强制目标帧率";
                case "targetFps": return "目标 FPS:";
                case "disableVsync": return "关闭垂直同步";
                case "disableAa": return "关闭抗锯齿";
                case "blockFrameEvents": return "阻止谱面 SetFrameRate 事件";
                case "load": return "加载与重开";
                case "skipCleanup": return "跳过加载前内存清理，减少阻塞加载时间";
                case "enableTimeout": return "启用加载超时自动退回";
                case "timeout": return "最大加载时间（秒）:";
                case "levelData": return "谱面数据优化";
                case "optimizeEvents": return "优化解码后的谱面事件";
                case "disableVideo": return "优化加载时禁用背景视频";
                case "capDecorations": return "MAX/OVERDRIVE 档限制视觉装饰数量";
                case "maxDecorations": return "最大装饰数量:";
                case "simpleShaders": return "仅 OVERDRIVE 简化装饰 shader";
                case "throttleDecorations": return "仅 OVERDRIVE 节流纯视觉装饰更新";
                case "stride": return "装饰更新间隔:";
                case "textures": return "自定义谱面纹理优化";
                case "optimizeTextures": return "优化自定义谱面加载的纹理";
                case "textureDivisor": return "纹理尺寸除数:";
                case "compressTextures": return "加载后压缩纹理";
                case "roundTextures": return "将纹理尺寸修正为 4 的倍数";
                case "adjustTextureGeometry": return "补偿背景、装饰与碰撞区域尺寸";
                case "showTextureStats": return "显示纹理优化统计";
                case "textureWarning": return "纹理压缩为有损处理；尺寸除数高于 1.00 会降低清晰度。若谱面显示异常，请关闭本项优化。";
                case "system": return "Windows 性能模式";
                case "boostPriority": return "提高游戏进程优先级";
                case "preventSleep": return "启用时防止睡眠并使用 1ms 计时器";
                case "backgroundThrottle": return "降低同一用户会话中其他进程优先级";
                case "verbose": return "记录详细优化日志";
                case "range": return "范围: ";
                case "note": return "MAX/OVERDRIVE 可能减少视觉特效。编辑器场景不会优化谱面数据，避免保存成被削减的谱面。";
                case "docsLink": return "打开使用说明";
                case "sourceLink": return "打开源代码";
                default: return key;
            }
        }

        private static string TextKorean(string key)
        {
            switch (key)
            {
                case "language": return "언어:";
                case "title": return "성능 최적화 MAX";
                case "profile": return "최적화 프로필";
                case "profileOff": return "끄기";
                case "profileMax": return "MAX";
                case "profileExtreme": return "EXTREME";
                case "profileOffDescription": return "모든 최적화를 끄고 캡처된 게임 설정을 복원합니다.";
                case "profileMaxDescription": return "화면 호환성과 성능을 균형 있게 유지하는 안정적인 강한 최적화입니다.";
                case "profileExtremeDescription": return "알파 안전 대형 텍스처 축소와 동적 장식 보존을 적용한 최고 성능 프로필입니다.";
                case "profileOffConfiguration": return "호환성 문제를 확인할 때 사용합니다.";
                case "profileMaxConfiguration": return "텍스처 2배 다운샘플링 및 고비용 이벤트 축소; 런타임 장식은 유지합니다.";
                case "profileExtremeConfiguration": return "대형 텍스처는 최대 8배로 축소하고 32픽셀 안전 하한, 동적 장식과 이동 이벤트를 유지합니다.";
                case "profileApplyNote": return "런타임 설정은 즉시 적용되며 레벨 최적화는 다음 로드에서 갱신됩니다.";
                case "latestResult": return "최근 레벨 최적화";
                case "runtime": return "런타임 프레임 설정";
                case "forceFps": return "목표 프레임 강제";
                case "targetFps": return "목표 FPS:";
                case "disableVsync": return "VSync 끄기";
                case "disableAa": return "안티앨리어싱 끄기";
                case "blockFrameEvents": return "레벨 SetFrameRate 이벤트 차단";
                case "load": return "로딩 및 재시작";
                case "skipCleanup": return "로딩 전 메모리 정리를 건너뛰어 대기 시간 감소";
                case "enableTimeout": return "로딩 시간 초과 시 자동 복귀";
                case "timeout": return "최대 로딩 시간(초):";
                case "levelData": return "레벨 데이터 최적화";
                case "optimizeEvents": return "디코딩된 레벨 이벤트 최적화";
                case "disableVideo": return "최적화 로딩에서 배경 비디오 비활성화";
                case "capDecorations": return "MAX/OVERDRIVE에서 시각 장식 수 제한";
                case "maxDecorations": return "최대 장식 수:";
                case "simpleShaders": return "OVERDRIVE에서만 장식 셰이더 단순화";
                case "throttleDecorations": return "OVERDRIVE에서만 순수 시각 장식 업데이트 절감";
                case "stride": return "장식 업데이트 간격:";
                case "textures": return "커스텀 레벨 텍스처 최적화";
                case "optimizeTextures": return "커스텀 레벨 텍스처 최적화";
                case "textureDivisor": return "텍스처 크기 나눗수:";
                case "compressTextures": return "로드 후 텍스처 압축";
                case "roundTextures": return "텍스처 크기를 4의 배수로 보정";
                case "adjustTextureGeometry": return "배경, 장식 및 충돌 영역 크기 보정";
                case "showTextureStats": return "텍스처 최적화 통계 표시";
                case "textureWarning": return "텍스처 압축은 손실 압축입니다. 1.00보다 큰 값은 해상도를 낮추므로 표시 문제가 있으면 이 기능을 끄십시오.";
                case "system": return "Windows 성능 모드";
                case "boostPriority": return "게임 프로세스 우선순위 상승";
                case "preventSleep": return "활성화 중 절전 방지 및 1ms 타이머 사용";
                case "backgroundThrottle": return "같은 사용자 세션의 다른 프로세스 우선순위 낮춤";
                case "verbose": return "자세한 최적화 로그";
                case "range": return "범위: ";
                case "note": return "MAX/OVERDRIVE는 시각 효과를 줄일 수 있습니다. 저장 손상을 막기 위해 에디터 장면의 데이터는 최적화하지 않습니다.";
                case "docsLink": return "사용 설명 열기";
                case "sourceLink": return "소스 코드 열기";
                default: return key;
            }
        }

        private static class GuiTheme
        {
            private static readonly List<Texture2D> textures = new List<Texture2D>();
            private static bool initialized;

            public static GUIStyle Outer { get; private set; }
            public static GUIStyle Panel { get; private set; }
            public static GUIStyle Card { get; private set; }
            public static GUIStyle Title { get; private set; }
            public static GUIStyle Subtitle { get; private set; }
            public static GUIStyle CardTitle { get; private set; }
            public static GUIStyle Body { get; private set; }
            public static GUIStyle Secondary { get; private set; }
            public static GUIStyle ProfileButton { get; private set; }
            public static GUIStyle ProfileButtonSelected { get; private set; }
            public static GUIStyle SmallButton { get; private set; }
            public static GUIStyle SmallButtonSelected { get; private set; }

            public static void Ensure()
            {
                if (initialized && textures.Count > 0 && textures[0] != null)
                {
                    return;
                }

                Dispose();

                Texture2D outer = RoundedTexture(new Color32(13, 15, 18, 245), 12);
                Texture2D panel = RoundedTexture(new Color32(10, 12, 15, 250), 16);
                Texture2D card = RoundedTexture(new Color32(21, 25, 31, 255), 12);
                Texture2D element = RoundedTexture(new Color32(45, 51, 60, 255), 9);
                Texture2D elementHover = RoundedTexture(new Color32(55, 64, 76, 255), 9);
                Texture2D elementActive = RoundedTexture(new Color32(38, 45, 54, 255), 9);
                Texture2D primary = RoundedTexture(new Color32(30, 111, 232, 255), 9);
                Texture2D primaryHover = RoundedTexture(new Color32(48, 128, 240, 255), 9);
                Texture2D primaryActive = RoundedTexture(new Color32(23, 91, 194, 255), 9);

                Outer = ContainerStyle(outer, 14);
                Panel = ContainerStyle(panel, 18);
                Card = ContainerStyle(card, 16);

                Title = LabelStyle(24, new Color32(245, 247, 250, 255), FontStyle.Bold);
                Subtitle = LabelStyle(18, new Color32(245, 247, 250, 255), FontStyle.Bold);
                CardTitle = LabelStyle(16, new Color32(224, 233, 245, 255), FontStyle.Bold);
                Body = LabelStyle(14, new Color32(218, 224, 232, 255), FontStyle.Normal);
                Secondary = LabelStyle(12, new Color32(134, 147, 164, 255), FontStyle.Normal);

                ProfileButton = ButtonStyle(element, elementHover, elementActive, 16, FontStyle.Bold);
                ProfileButtonSelected = ButtonStyle(primary, primaryHover, primaryActive, 16, FontStyle.Bold);
                SmallButton = ButtonStyle(element, elementHover, elementActive, 12, FontStyle.Bold);
                SmallButtonSelected = ButtonStyle(primary, primaryHover, primaryActive, 12, FontStyle.Bold);
                initialized = true;
            }

            public static void Dispose()
            {
                for (int i = 0; i < textures.Count; i++)
                {
                    try
                    {
                        if (textures[i] != null)
                        {
                            UnityEngine.Object.Destroy(textures[i]);
                        }
                    }
                    catch
                    {
                    }
                }

                textures.Clear();
                initialized = false;
            }

            private static GUIStyle ContainerStyle(Texture2D texture, int padding)
            {
                GUIStyle style = new GUIStyle(GUI.skin.box);
                style.normal.background = texture;
                style.border = new RectOffset(12, 12, 12, 12);
                style.padding = new RectOffset(padding, padding, padding, padding);
                style.margin = new RectOffset(0, 0, 0, 0);
                return style;
            }

            private static GUIStyle LabelStyle(int fontSize, Color color, FontStyle fontStyle)
            {
                GUIStyle style = new GUIStyle(GUI.skin.label);
                style.fontSize = fontSize;
                style.fontStyle = fontStyle;
                style.normal.textColor = color;
                style.wordWrap = true;
                style.richText = true;
                style.padding = new RectOffset(0, 0, 0, 0);
                style.margin = new RectOffset(0, 0, 0, 0);
                return style;
            }

            private static GUIStyle ButtonStyle(
                Texture2D normal,
                Texture2D hover,
                Texture2D active,
                int fontSize,
                FontStyle fontStyle)
            {
                GUIStyle style = new GUIStyle(GUI.skin.button);
                style.normal.background = normal;
                style.hover.background = hover;
                style.active.background = active;
                style.focused.background = normal;
                style.onNormal.background = normal;
                style.onHover.background = hover;
                style.onActive.background = active;
                style.onFocused.background = normal;
                style.normal.textColor = new Color32(245, 247, 250, 255);
                style.hover.textColor = Color.white;
                style.active.textColor = Color.white;
                style.focused.textColor = new Color32(245, 247, 250, 255);
                style.onNormal.textColor = new Color32(245, 247, 250, 255);
                style.onHover.textColor = Color.white;
                style.onActive.textColor = Color.white;
                style.onFocused.textColor = new Color32(245, 247, 250, 255);
                style.fontSize = fontSize;
                style.fontStyle = fontStyle;
                style.alignment = TextAnchor.MiddleCenter;
                style.border = new RectOffset(9, 9, 9, 9);
                style.padding = new RectOffset(12, 12, 7, 7);
                style.margin = new RectOffset(3, 3, 0, 0);
                return style;
            }

            private static Texture2D RoundedTexture(Color32 color, int radius)
            {
                const int size = 32;
                Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                texture.name = "POMMax.Gui";
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                texture.hideFlags = HideFlags.HideAndDontSave;

                Color32 transparent = new Color32(color.r, color.g, color.b, 0);
                Color32[] pixels = new Color32[size * size];
                float corner = radius - 0.5f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = x < radius ? corner - x : (x >= size - radius ? x - (size - radius - 0.5f) : 0f);
                        float dy = y < radius ? corner - y : (y >= size - radius ? y - (size - radius - 0.5f) : 0f);
                        pixels[(y * size) + x] = dx * dx + dy * dy <= radius * radius ? color : transparent;
                    }
                }

                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                textures.Add(texture);
                return texture;
            }
        }

        private static void Verbose(string message)
        {
            if (settings != null && settings.verboseLogging && modEntry != null)
            {
                modEntry.Logger.Log(message);
            }
        }
    }

    public sealed class OptimizationNotificationOverlay : MonoBehaviour
    {
        private const float VisibleSeconds = 5.5f;
        private const float FadeInSeconds = 0.18f;
        private const float FadeOutSeconds = 0.45f;

        private static OptimizationNotificationOverlay instance;
        private static GameObject host;

        private string message = "";
        private float shownAt;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private int styleFontSize;

        public static void Ensure()
        {
            if (instance != null)
            {
                return;
            }

            host = new GameObject("POMMax.OptimizationNotification");
            host.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(host);
            instance = host.AddComponent<OptimizationNotificationOverlay>();
            instance.enabled = false;
        }

        public static bool Show(string text)
        {
            try
            {
                Ensure();
                if (instance == null)
                {
                    return false;
                }

                instance.message = text ?? "";
                instance.shownAt = Time.realtimeSinceStartup;
                instance.enabled = !string.IsNullOrEmpty(instance.message);
                return instance.enabled;
            }
            catch (Exception ex)
            {
                if (Main.modEntry != null && Main.settings != null && Main.settings.verboseLogging)
                {
                    Main.modEntry.Logger.LogException(ex);
                }
                return false;
            }
        }

        public static void Hide()
        {
            if (instance == null)
            {
                return;
            }

            instance.message = "";
            instance.enabled = false;
        }

        public static void Dispose()
        {
            Hide();
            if (host != null)
            {
                Destroy(host);
            }
            host = null;
            instance = null;
        }

        private void OnGUI()
        {
            float elapsed = Time.realtimeSinceStartup - shownAt;
            if (elapsed < 0f || elapsed >= VisibleSeconds || string.IsNullOrEmpty(message))
            {
                Hide();
                return;
            }

            float scale = Mathf.Clamp(Screen.height / 1080f, 0.90f, 1.20f);
            int fontSize = Mathf.Clamp(Mathf.RoundToInt(13f * scale), 12, 15);
            EnsureStyles(fontSize);

            float width = Mathf.Clamp(Screen.width * 0.38f, 440f * scale, 680f);
            float height = 72f * scale;
            float top = (ADOBase.isLevelEditor ? 76f : 24f) * scale;
            Rect panel = new Rect((Screen.width - width) * 0.5f, top, width, height);

            float alpha = 1f;
            if (elapsed < FadeInSeconds)
            {
                alpha = Mathf.Clamp01(elapsed / FadeInSeconds);
            }
            else if (elapsed > VisibleSeconds - FadeOutSeconds)
            {
                alpha = Mathf.Clamp01((VisibleSeconds - elapsed) / FadeOutSeconds);
            }

            int oldDepth = GUI.depth;
            Color oldColor = GUI.color;
            Color oldContentColor = GUI.contentColor;
            try
            {
                GUI.depth = -9000;

                GUI.color = new Color(0.035f, 0.055f, 0.075f, 0.90f * alpha);
                GUI.DrawTexture(panel, Texture2D.whiteTexture);

                GUI.color = new Color(0.12f, 0.55f, 0.95f, alpha);
                GUI.DrawTexture(new Rect(panel.x, panel.y, 4f * scale, panel.height), Texture2D.whiteTexture);

                GUI.contentColor = new Color(0.82f, 0.92f, 1f, alpha);
                GUI.Label(
                    new Rect(panel.x + 18f * scale, panel.y + 8f * scale, panel.width - 34f * scale, 22f * scale),
                    GetTitle(),
                    titleStyle);

                GUI.contentColor = new Color(0.92f, 0.95f, 0.98f, alpha);
                GUI.Label(
                    new Rect(panel.x + 18f * scale, panel.y + 30f * scale, panel.width - 34f * scale, 38f * scale),
                    message,
                    bodyStyle);
            }
            finally
            {
                GUI.depth = oldDepth;
                GUI.color = oldColor;
                GUI.contentColor = oldContentColor;
            }
        }

        private void EnsureStyles(int fontSize)
        {
            if (titleStyle != null && bodyStyle != null && styleFontSize == fontSize)
            {
                return;
            }

            styleFontSize = fontSize;
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                clipping = TextClipping.Clip,
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
                wordWrap = false
            };
            bodyStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                clipping = TextClipping.Clip,
                fontSize = Math.Max(11, fontSize - 2),
                fontStyle = FontStyle.Normal,
                wordWrap = true
            };
        }

        private static string GetTitle()
        {
            if (RDString.language == SystemLanguage.ChineseSimplified
                || RDString.language == SystemLanguage.ChineseTraditional)
            {
                return "本次谱面优化完成";
            }
            if (RDString.language == SystemLanguage.Korean)
            {
                return "레벨 최적화 완료";
            }
            return "Level optimization complete";
        }
    }

    public static class TextureOptimization
    {
        private sealed class BackgroundScaleState
        {
            public int textureId;
            public Vector2 baseSize;
            public Vector2 lastSize;
        }

        private sealed class DecorationScaleState
        {
            public int textureId;
            public Vector2 baseEditorCollider;
            public Vector2 lastEditorCollider;
            public Vector2 baseSpriteUnscaled;
            public Vector2 lastSpriteUnscaled;
            public Vector2 baseBorder;
            public Vector2 lastBorder;
            public Vector2 baseHitboxRenderer;
            public Vector2 lastHitboxRenderer;
            public Vector2 baseDamageBox;
            public Vector2 lastDamageBox;
            public Vector2 baseDamageCapsule;
            public Vector2 lastDamageCapsule;
            public float baseDamageCircle;
            public float lastDamageCircle;
            public bool hasEditorCollider;
            public bool hasSpriteUnscaled;
            public bool hasBorder;
            public bool hasHitboxRenderer;
            public bool hasDamageBox;
            public bool hasDamageCapsule;
            public bool hasDamageCircle;
        }

        private static readonly Dictionary<int, Vector3> textureRatios = new Dictionary<int, Vector3>();
        private static readonly HashSet<int> processedTextureIds = new HashSet<int>();
        private static readonly Dictionary<int, BackgroundScaleState> backgroundStates = new Dictionary<int, BackgroundScaleState>();
        private static readonly Dictionary<int, DecorationScaleState> decorationStates = new Dictionary<int, DecorationScaleState>();
        private static readonly System.Reflection.PropertyInfo spriteUnscaledSize = typeof(scrVisualDecoration).GetProperty(
            "spriteUnscaledSize",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static int loadedCount;
        private static int resizedCount;
        private static int compressedCount;
        private static int skippedCount;
        private static int duplicateCount;
        private static int errorCount;
        private static long measuredBeforeBytes;
        private static long measuredAfterBytes;
        private static bool reported;
        private static bool notificationPending;
        private static bool legacyWarningLogged;
        private static string lastReport = "";
        private static string currentLoadPath = "";
        private static string lastReportedLoadPath = "";
        private static float lastReportedAt = -1000f;
        private static bool levelDataRecorded;
        private static bool editorLevelDataSkipped;
        private static int levelEventsBefore;
        private static int levelEventsAfter;
        private static int levelDecorationsBefore;
        private static int levelDecorationsAfter;
        private static int runtimeDecorationsSeen;
        private static int runtimeDecorationsOptimized;
        private static int runtimeDecorationsProtected;
        private static int runtimeDecorationsRemaining;
        private static float loadingDuration;
        private static float reportFallbackAt = -1f;

        public static void ProcessTexture(ref Texture2D texture, string filePath)
        {
            if (!ShouldOptimize()
                || !ShouldOptimizePath(filePath)
                || texture == null
                || texture.width <= 0
                || texture.height <= 0)
            {
                return;
            }

            if (HasLegacyOptimizerPatch())
            {
                skippedCount++;
                if (!legacyWarningLogged && Main.modEntry != null)
                {
                    legacyWarningLogged = true;
                    Main.modEntry.Logger.Warning(
                        "[TextureOptimizer] Legacy optimiz patch detected. POM-Max skipped texture processing to prevent double compression.");
                }
                return;
            }

            int originalId = texture.GetInstanceID();
            if (processedTextureIds.Contains(originalId))
            {
                duplicateCount++;
                return;
            }

            loadedCount++;
            Texture2D original = texture;
            Texture2D candidate = original;
            bool replacementCreated = false;
            int originalWidth = original.width;
            int originalHeight = original.height;
            long beforeBytes = MeasureTextureBytes(original);

            try
            {
                float divisor = Mathf.Max(1f, Main.settings.textureScaleDivisor);
                int targetWidth = Math.Max(
                    Math.Min(originalWidth, 32),
                    Mathf.RoundToInt(originalWidth / divisor));
                int targetHeight = Math.Max(
                    Math.Min(originalHeight, 32),
                    Mathf.RoundToInt(originalHeight / divisor));

                if (targetWidth >= 4 && targetHeight >= 4 && Main.settings.roundTextureDimensionsToMultipleOf4)
                {
                    targetWidth = Math.Min(originalWidth, NearestMultipleOf4(targetWidth));
                    targetHeight = Math.Min(originalHeight, NearestMultipleOf4(targetHeight));
                }
                else if (targetWidth < 4 || targetHeight < 4)
                {
                    targetWidth = originalWidth;
                    targetHeight = originalHeight;
                }

                bool resize = targetWidth != originalWidth || targetHeight != originalHeight;
                if (resize)
                {
                    candidate = CreateReadableTexture(original, targetWidth, targetHeight);
                    replacementCreated = true;
                }

                if (replacementCreated)
                {
                    candidate.Apply(false, true);
                }

                if (resize)
                {
                    resizedCount++;
                }
                else
                {
                    skippedCount++;
                }

                Vector3 ratio = new Vector3(
                    (float)originalWidth / candidate.width,
                    (float)originalHeight / candidate.height,
                    1f);

                texture = candidate;
                int candidateId = candidate.GetInstanceID();
                textureRatios[candidateId] = ratio;
                processedTextureIds.Add(originalId);
                processedTextureIds.Add(candidateId);

                long afterBytes = MeasureTextureBytes(candidate);
                measuredBeforeBytes += Math.Max(0L, beforeBytes);
                measuredAfterBytes += Math.Max(0L, afterBytes);

                if (replacementCreated && original != candidate)
                {
                    UnityEngine.Object.Destroy(original);
                }
            }
            catch (Exception ex)
            {
                errorCount++;
                if (replacementCreated && candidate != null && candidate != original)
                {
                    UnityEngine.Object.Destroy(candidate);
                }
                texture = original;
                processedTextureIds.Add(originalId);
                textureRatios[originalId] = Vector3.one;
                if (Main.modEntry != null)
                {
                    Main.modEntry.Logger.Warning("[TextureOptimizer] Failed to optimize " + SafeTextureLabel(original, filePath));
                    Main.modEntry.Logger.LogException(ex);
                }
            }
        }

        public static void ApplyBackgroundScale(scrCustomBackgroundSprite instance)
        {
            if (!ShouldAdjustGeometry() || instance == null || instance.displayedSprite == null
                || instance.displayedSprite.sprite == null || instance.displayedSprite.sprite.texture == null)
            {
                return;
            }

            Texture2D texture = instance.displayedSprite.sprite.texture;
            Vector3 ratio;
            if (!TryGetRatio(texture, out ratio))
            {
                return;
            }

            int instanceId = instance.GetInstanceID();
            BackgroundScaleState state;
            Vector2 current = instance.imgSize;
            Vector2 baseSize = current;
            if (backgroundStates.TryGetValue(instanceId, out state)
                && state.textureId == texture.GetInstanceID()
                && Approximately(current, state.lastSize))
            {
                baseSize = state.baseSize;
            }
            else
            {
                state = new BackgroundScaleState();
            }

            Vector2 output = Multiply(baseSize, ratio);
            instance.imgSize = output;
            state.textureId = texture.GetInstanceID();
            state.baseSize = baseSize;
            state.lastSize = output;
            backgroundStates[instanceId] = state;
        }

        public static void ApplyDecorationScale(scrVisualDecoration instance)
        {
            if (!ShouldAdjustGeometry() || instance == null || instance.spriteRenderer == null
                || instance.spriteRenderer.sprite == null || instance.spriteRenderer.sprite.texture == null)
            {
                return;
            }

            Texture2D texture = instance.spriteRenderer.sprite.texture;
            Vector3 ratio;
            if (!TryGetRatio(texture, out ratio))
            {
                return;
            }

            int instanceId = instance.GetInstanceID();
            int textureId = texture.GetInstanceID();
            DecorationScaleState state = GetDecorationState(instanceId, textureId);
            instance.spriteRenderer.transform.localScale = ratio;

            if (instance.editorCollider != null)
            {
                Vector2 current = instance.editorCollider.size;
                Vector2 baseValue = state.hasEditorCollider && Approximately(current, state.lastEditorCollider)
                    ? state.baseEditorCollider
                    : current;
                Vector2 output = Multiply(baseValue, ratio);
                instance.editorCollider.size = output;
                state.baseEditorCollider = baseValue;
                state.lastEditorCollider = output;
                state.hasEditorCollider = true;
            }

            if (spriteUnscaledSize != null)
            {
                object raw = spriteUnscaledSize.GetValue(instance, null);
                if (raw is Vector2)
                {
                    Vector2 current = (Vector2)raw;
                    Vector2 baseValue = state.hasSpriteUnscaled && Approximately(current, state.lastSpriteUnscaled)
                        ? state.baseSpriteUnscaled
                        : current;
                    Vector2 output = Multiply(baseValue, ratio);
                    spriteUnscaledSize.SetValue(instance, output, null);
                    state.baseSpriteUnscaled = baseValue;
                    state.lastSpriteUnscaled = output;
                    state.hasSpriteUnscaled = true;
                }
            }
        }

        public static void ApplySelectedBorderScales(scrDecorationManager manager)
        {
            if (!ShouldAdjustGeometry() || manager == null || ADOBase.editor == null)
            {
                return;
            }

            List<LevelEvent> targets = new List<LevelEvent>();
            targets.AddRange(ADOBase.editor.selectedDecorations);
            if (manager.hoveredDecoration != null && !targets.Contains(manager.hoveredDecoration))
            {
                targets.Add(manager.hoveredDecoration);
            }

            foreach (LevelEvent levelEvent in targets)
            {
                if (levelEvent == null)
                {
                    continue;
                }

                scrVisualDecoration decoration = scrDecorationManager.GetDecoration(levelEvent) as scrVisualDecoration;
                if (decoration == null || decoration.spriteRenderer == null || decoration.spriteRenderer.sprite == null
                    || decoration.spriteRenderer.sprite.texture == null || decoration.bordersRenderer == null)
                {
                    continue;
                }

                Vector3 ratio;
                Texture2D texture = decoration.spriteRenderer.sprite.texture;
                if (!TryGetRatio(texture, out ratio))
                {
                    continue;
                }

                DecorationScaleState state = GetDecorationState(decoration.GetInstanceID(), texture.GetInstanceID());
                Vector2 current = decoration.bordersRenderer.size;
                Vector2 baseValue = state.hasBorder && Approximately(current, state.lastBorder)
                    ? state.baseBorder
                    : current;
                float pixelsPerUnit = decoration.spriteRenderer.sprite.pixelsPerUnit;
                float halfPixel = pixelsPerUnit > 0f ? 1f / pixelsPerUnit / 2f : 0f;
                Vector3 objectScale = decoration.transform.localScale;
                Vector2 edge = new Vector2(halfPixel * Mathf.Sign(objectScale.x), halfPixel * Mathf.Sign(objectScale.y));
                Vector2 output = Multiply(baseValue - edge, ratio) + edge;
                decoration.bordersRenderer.size = output;
                decoration.cachedBorderSize = output;
                state.baseBorder = baseValue;
                state.lastBorder = output;
                state.hasBorder = true;
            }
        }

        public static void ApplySelectedHitboxScales()
        {
            if (!ShouldAdjustGeometry() || ADOBase.editor == null)
            {
                return;
            }

            foreach (LevelEvent levelEvent in ADOBase.editor.selectedDecorations)
            {
                if (levelEvent == null)
                {
                    continue;
                }

                scrVisualDecoration decoration = scrDecorationManager.GetDecoration(levelEvent) as scrVisualDecoration;
                if (decoration == null || decoration.spriteRenderer == null || decoration.spriteRenderer.sprite == null
                    || decoration.spriteRenderer.sprite.texture == null || decoration.hitboxRenderer == null)
                {
                    continue;
                }

                Texture2D texture = decoration.spriteRenderer.sprite.texture;
                Vector3 ratio;
                if (!TryGetRatio(texture, out ratio))
                {
                    continue;
                }

                DecorationScaleState state = GetDecorationState(decoration.GetInstanceID(), texture.GetInstanceID());
                Vector2 current = decoration.hitboxRenderer.size;
                Vector2 baseValue = state.hasHitboxRenderer && Approximately(current, state.lastHitboxRenderer)
                    ? state.baseHitboxRenderer
                    : current;
                Vector2 output = Multiply(baseValue, ratio);
                decoration.hitboxRenderer.size = output;
                state.baseHitboxRenderer = baseValue;
                state.lastHitboxRenderer = output;
                state.hasHitboxRenderer = true;
            }
        }

        public static void ApplyWorldSizeScale(scrVisualDecoration instance, ref Vector2 result)
        {
            if (!ShouldAdjustGeometry() || instance == null || instance.spriteRenderer == null
                || instance.spriteRenderer.sprite == null || instance.spriteRenderer.sprite.texture == null)
            {
                return;
            }

            Vector3 ratio;
            if (TryGetRatio(instance.spriteRenderer.sprite.texture, out ratio))
            {
                result = Multiply(result, ratio);
            }
        }

        public static void ApplyDamageColliderScale(scrVisualDecoration instance)
        {
            if (!ShouldAdjustGeometry() || instance == null || !instance.useHitbox || instance.spriteRenderer == null
                || instance.spriteRenderer.sprite == null || instance.spriteRenderer.sprite.texture == null)
            {
                return;
            }

            Texture2D texture = instance.spriteRenderer.sprite.texture;
            Vector3 ratio;
            if (!TryGetRatio(texture, out ratio))
            {
                return;
            }

            DecorationScaleState state = GetDecorationState(instance.GetInstanceID(), texture.GetInstanceID());
            if (instance.hitboxType == Hitbox.Box && instance.damageBox != null)
            {
                Vector2 current = instance.damageBox.size;
                Vector2 baseValue = state.hasDamageBox && Approximately(current, state.lastDamageBox)
                    ? state.baseDamageBox
                    : current;
                Vector2 output = Multiply(baseValue, ratio);
                instance.damageBox.size = output;
                state.baseDamageBox = baseValue;
                state.lastDamageBox = output;
                state.hasDamageBox = true;
            }
            else if (instance.hitboxType == Hitbox.Capsule && instance.damageCapsule != null)
            {
                Vector2 current = instance.damageCapsule.size;
                Vector2 baseValue = state.hasDamageCapsule && Approximately(current, state.lastDamageCapsule)
                    ? state.baseDamageCapsule
                    : current;
                Vector2 output = Multiply(baseValue, ratio);
                instance.damageCapsule.size = output;
                state.baseDamageCapsule = baseValue;
                state.lastDamageCapsule = output;
                state.hasDamageCapsule = true;
            }
            else if (instance.damageCircle != null)
            {
                float current = instance.damageCircle.radius;
                float baseValue = state.hasDamageCircle && Mathf.Approximately(current, state.lastDamageCircle)
                    ? state.baseDamageCircle
                    : current;
                float output = baseValue * ratio.x;
                instance.damageCircle.radius = output;
                state.baseDamageCircle = baseValue;
                state.lastDamageCircle = output;
                state.hasDamageCircle = true;
            }
        }

        public static void ResetSession(bool clearTextures)
        {
            loadedCount = 0;
            resizedCount = 0;
            compressedCount = 0;
            skippedCount = 0;
            duplicateCount = 0;
            errorCount = 0;
            measuredBeforeBytes = 0L;
            measuredAfterBytes = 0L;
            reported = false;
            notificationPending = false;
            lastReport = "";
            levelDataRecorded = false;
            editorLevelDataSkipped = false;
            levelEventsBefore = 0;
            levelEventsAfter = 0;
            levelDecorationsBefore = 0;
            levelDecorationsAfter = 0;
            runtimeDecorationsSeen = 0;
            runtimeDecorationsOptimized = 0;
            runtimeDecorationsProtected = 0;
            runtimeDecorationsRemaining = 0;
            loadingDuration = 0f;
            reportFallbackAt = -1f;
            currentLoadPath = "";
            backgroundStates.Clear();
            decorationStates.Clear();
            if (clearTextures)
            {
                textureRatios.Clear();
                processedTextureIds.Clear();
            }
        }

        public static bool BeginLevelLoad(string levelPath, bool clearTextures)
        {
            string normalizedPath = NormalizeLevelPath(levelPath);
            float now = Time.realtimeSinceStartup;
            if (reported
                && string.Equals(normalizedPath, lastReportedLoadPath, StringComparison.OrdinalIgnoreCase)
                && now - lastReportedAt < 2.5f)
            {
                if (Main.modEntry != null)
                {
                    Main.modEntry.Logger.Log(
                        "[LevelOptimization] suppressed duplicate internal load: "
                        + (string.IsNullOrEmpty(normalizedPath) ? "<unknown>" : normalizedPath)
                        + ".");
                }
                return false;
            }

            ResetSession(clearTextures);
            currentLoadPath = normalizedPath;
            reportFallbackAt = now + 1.5f;
            return true;
        }

        public static void RecordLevelData(
            int eventsBefore,
            int eventsAfter,
            int decorationsBefore,
            int decorationsAfter,
            bool skippedInEditor)
        {
            levelDataRecorded = true;
            editorLevelDataSkipped = skippedInEditor;
            levelEventsBefore = Math.Max(0, eventsBefore);
            levelEventsAfter = Math.Max(0, eventsAfter);
            levelDecorationsBefore = Math.Max(0, decorationsBefore);
            levelDecorationsAfter = Math.Max(0, decorationsAfter);
        }

        public static void RecordRuntimeDecorations(int seen, int optimized, int protectedCount, int remaining)
        {
            runtimeDecorationsSeen = Math.Max(0, seen);
            runtimeDecorationsOptimized = Math.Max(0, optimized);
            runtimeDecorationsProtected = Math.Max(0, protectedCount);
            runtimeDecorationsRemaining = Math.Max(0, remaining);
        }

        public static void RecordLoadingDuration(float seconds)
        {
            if (!float.IsNaN(seconds) && !float.IsInfinity(seconds) && seconds >= 0f)
            {
                loadingDuration = seconds;
                if (reported)
                {
                    long reduction = Math.Max(0L, measuredBeforeBytes - measuredAfterBytes);
                    lastReport = BuildReport(reduction);
                    if (Main.modEntry != null)
                    {
                        Main.modEntry.Logger.Log("[LevelOptimization] finalized: " + lastReport);
                    }
                }
            }
        }

        public static void ReportIfReady(bool reloadDecorations)
        {
            if (Main.EffectiveProfile() <= 0
                || GCS.internalLevelName != null
                || ADOBase.isBundleLevel
                || !reloadDecorations
                || reported)
            {
                return;
            }

            CompleteReport("decoration refresh");
        }

        public static void Tick(float now)
        {
            if (!reported
                && reportFallbackAt >= 0f
                && now >= reportFallbackAt
                && !Main.IsLoadingLevel()
                && LooksLikeCustomLevel(currentLoadPath)
                && Main.EffectiveProfile() > 0)
            {
                CompleteReport("load fallback");
            }

            TryShowPendingNotification();
        }

        private static void CompleteReport(string source)
        {
            if (reported)
            {
                return;
            }

            reported = true;
            reportFallbackAt = -1f;
            lastReportedLoadPath = currentLoadPath;
            lastReportedAt = Time.realtimeSinceStartup;
            long reduction = Math.Max(0L, measuredBeforeBytes - measuredAfterBytes);
            lastReport = BuildReport(reduction);
            if (Main.modEntry != null)
            {
                Main.modEntry.Logger.Log("[LevelOptimization] " + source + ": " + lastReport);
            }

            if (Main.settings.showTextureOptimizationStats)
            {
                notificationPending = true;
                TryShowPendingNotification();
            }
        }

        private static bool LooksLikeCustomLevel(string levelPath)
        {
            return !string.IsNullOrEmpty(levelPath)
                && levelPath.EndsWith(".adofai", StringComparison.OrdinalIgnoreCase);
        }

        public static void TryShowPendingNotification()
        {
            if (!notificationPending || !reported || Main.settings == null || !Main.settings.showTextureOptimizationStats)
            {
                return;
            }

            if (ShowNotification())
            {
                notificationPending = false;
            }
        }

        public static string GetStatusText(int language)
        {
            if (string.IsNullOrEmpty(lastReport))
            {
                if (language == 0)
                {
                    return "加载自定义谱面后显示本次优化结果。";
                }
                if (language == 2)
                {
                    return "커스텀 레벨을 로드하면 최적화 결과가 표시됩니다.";
                }
                return "Load a custom level to display optimization results.";
            }

            return BuildSummary(language, true);
        }

        public static string GetNotificationText()
        {
            if (RDString.language == SystemLanguage.ChineseSimplified || RDString.language == SystemLanguage.ChineseTraditional)
            {
                return BuildSummary(0, false);
            }
            if (RDString.language == SystemLanguage.Korean)
            {
                return BuildSummary(2, false);
            }
            return BuildSummary(1, false);
        }

        private static bool ShouldOptimize()
        {
            return Main.settings != null
                && Main.EffectiveProfile() > 0
                && Main.settings.optimizeCustomTextures
                && GCS.internalLevelName == null
                && !ADOBase.isBundleLevel;
        }

        private static bool ShouldOptimizePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            try
            {
                string extension = Path.GetExtension(filePath);
                if (!string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                string fullPath = Path.GetFullPath(filePath);
                string gameRoot = Path.GetFullPath(Environment.CurrentDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                string modsRoot = Path.Combine(gameRoot, "Mods")
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                string dataRoot = Path.Combine(gameRoot, "A Dance of Fire and Ice_Data")
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;

                return !fullPath.StartsWith(modsRoot, StringComparison.OrdinalIgnoreCase)
                    && !fullPath.StartsWith(dataRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool ShouldAdjustGeometry()
        {
            return ShouldOptimize() && Main.settings.adjustTextureGeometry;
        }

        private static bool HasLegacyOptimizerPatch()
        {
            MethodInfo target = AccessTools.Method(typeof(TextureManager), "LoadTexture");
            HarmonyLib.Patches patchInfo = target != null ? Harmony.GetPatchInfo(target) : null;
            if (patchInfo == null)
            {
                return false;
            }

            foreach (Patch patch in patchInfo.Postfixes)
            {
                string owner = patch.owner ?? "";
                Type declaringType = patch.PatchMethod != null ? patch.PatchMethod.DeclaringType : null;
                string typeName = declaringType != null ? declaringType.FullName : "";
                if (!string.Equals(owner, Main.modEntry != null ? Main.modEntry.Info.Id : "POMMax", StringComparison.Ordinal)
                    && (owner.IndexOf("optimiz", StringComparison.OrdinalIgnoreCase) >= 0
                        || typeName.StartsWith("SANSMASTER.", StringComparison.Ordinal)))
                {
                    return true;
                }
            }
            return false;
        }

        private static Texture2D CreateReadableTexture(Texture2D source, int width, int height)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture temporary = RenderTexture.GetTemporary(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            try
            {
                temporary.filterMode = source.filterMode;
                temporary.wrapMode = source.wrapMode;
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                result.name = source.name;
                result.filterMode = source.filterMode;
                result.wrapMode = source.wrapMode;
                result.anisoLevel = source.anisoLevel;
                result.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                result.Apply(false, false);
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private static long MeasureTextureBytes(Texture2D texture)
        {
            if (texture == null)
            {
                return 0L;
            }

            try
            {
                long measured = Profiler.GetRuntimeMemorySizeLong(texture);
                if (measured > 0L)
                {
                    return measured;
                }
            }
            catch
            {
            }

            return Math.Max(0L, (long)texture.width * texture.height * 4L);
        }

        private static int NearestMultipleOf4(int value)
        {
            return Math.Max(4, Mathf.RoundToInt(value / 4f) * 4);
        }

        private static bool TryGetRatio(Texture2D texture, out Vector3 ratio)
        {
            if (texture != null && textureRatios.TryGetValue(texture.GetInstanceID(), out ratio))
            {
                return true;
            }
            ratio = Vector3.one;
            return false;
        }

        private static DecorationScaleState GetDecorationState(int instanceId, int textureId)
        {
            DecorationScaleState state;
            if (!decorationStates.TryGetValue(instanceId, out state) || state.textureId != textureId)
            {
                state = new DecorationScaleState();
                state.textureId = textureId;
                decorationStates[instanceId] = state;
            }
            return state;
        }

        private static Vector2 Multiply(Vector2 value, Vector3 ratio)
        {
            return new Vector2(value.x * ratio.x, value.y * ratio.y);
        }

        private static bool Approximately(Vector2 left, Vector2 right)
        {
            return Mathf.Approximately(left.x, right.x) && Mathf.Approximately(left.y, right.y);
        }

        private static string SafeTextureLabel(Texture2D texture, string filePath)
        {
            if (texture != null && !string.IsNullOrEmpty(texture.name))
            {
                return texture.name;
            }
            return string.IsNullOrEmpty(filePath) ? "<unknown>" : filePath;
        }

        private static string BuildReport(long reduction)
        {
            return "workloadReduction=" + FormatPercent(GetWorkloadReductionRatio())
                + ", events=" + levelEventsBefore + " -> " + levelEventsAfter
                + " (removed " + GetRemovedEvents() + ")"
                + ", levelDecorations=" + levelDecorationsBefore + " -> " + levelDecorationsAfter
                + " (removed " + GetRemovedLevelDecorations() + ")"
                + ", runtimeDecorations=" + runtimeDecorationsSeen + " -> " + runtimeDecorationsRemaining
                + " (optimized " + runtimeDecorationsOptimized + ", protected " + runtimeDecorationsProtected + ")"
                + ", editorDataPreserved=" + editorLevelDataSkipped
                + ", texturesLoaded=" + loadedCount
                + ", resized=" + resizedCount
                + ", compressed=" + compressedCount
                + ", skipped=" + skippedCount
                + ", duplicates=" + duplicateCount
                + ", errors=" + errorCount
                + ", textureBytes=" + measuredBeforeBytes + " -> " + measuredAfterBytes
                + ", textureReduction=" + FormatMegabytes(reduction) + " MB (" + FormatPercent(GetTextureReductionRatio()) + ")"
                + ", loadTime=" + loadingDuration.ToString("0.00", CultureInfo.InvariantCulture) + "s";
        }

        private static string FormatMegabytes(long bytes)
        {
            return (bytes / 1048576d).ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string NormalizeLevelPath(string levelPath)
        {
            if (string.IsNullOrWhiteSpace(levelPath))
            {
                return "";
            }

            try
            {
                return Path.GetFullPath(levelPath).Trim();
            }
            catch
            {
                return levelPath.Trim();
            }
        }

        private static string BuildSummary(int language, bool multiline)
        {
            string workload = FormatPercent(GetWorkloadReductionRatio());
            string textureRatio = FormatPercent(GetTextureReductionRatio());
            string amount = FormatMegabytes(Math.Max(0L, measuredBeforeBytes - measuredAfterBytes));
            int removedEvents = GetRemovedEvents();
            int optimizedDecorations = GetRemovedLevelDecorations() + runtimeDecorationsOptimized;

            if (language == 0)
            {
                if (multiline)
                {
                    return "负载优化比 " + workload
                        + "  ·  事件精简 " + removedEvents
                        + "  ·  装饰精简 " + optimizedDecorations
                        + "\n纹理 " + loadedCount + " 张  ·  节省 " + amount + " MB"
                        + "  ·  纹理优化比 " + textureRatio
                        + "  ·  加载 " + loadingDuration.ToString("0.00", CultureInfo.InvariantCulture) + " 秒";
                }
                return "优化完成｜负载优化比 " + workload
                    + "｜事件 " + removedEvents
                    + "｜装饰 " + optimizedDecorations
                    + "\n纹理节省 " + amount + " MB"
                    + "｜纹理优化比 " + textureRatio
                    + "｜加载 " + loadingDuration.ToString("0.00", CultureInfo.InvariantCulture) + " 秒";
            }

            if (language == 2)
            {
                if (multiline)
                {
                    return "부하 최적화 " + workload
                        + "  ·  이벤트 " + removedEvents
                        + "  ·  장식 " + optimizedDecorations
                        + "\n텍스처 " + loadedCount + "개  ·  " + amount + " MB 절감"
                        + "  ·  텍스처 최적화 " + textureRatio
                        + "  ·  로드 " + loadingDuration.ToString("0.00", CultureInfo.InvariantCulture) + "초";
                }
                return "최적화 완료 | 부하 감소 " + workload
                    + " | 이벤트 " + removedEvents
                    + " | 장식 " + optimizedDecorations
                    + "\n텍스처 " + amount + " MB 절감"
                    + " | 감소율 " + textureRatio
                    + " | 로드 " + loadingDuration.ToString("0.00", CultureInfo.InvariantCulture) + "초";
            }

            if (multiline)
            {
                return "Workload reduction " + workload
                    + "  ·  events " + removedEvents
                    + "  ·  decorations " + optimizedDecorations
                    + "\nTextures " + loadedCount + "  ·  saved " + amount + " MB"
                    + "  ·  texture reduction " + textureRatio
                    + "  ·  load " + loadingDuration.ToString("0.00", CultureInfo.InvariantCulture) + " s";
            }
            return "Optimized | workload reduction " + workload
                + " | events " + removedEvents
                + " | decorations " + optimizedDecorations
                + "\nTextures saved " + amount + " MB"
                + " | reduction " + textureRatio
                + " | load " + loadingDuration.ToString("0.00", CultureInfo.InvariantCulture) + " s";
        }

        private static int GetRemovedEvents()
        {
            return Math.Max(0, levelEventsBefore - levelEventsAfter);
        }

        private static int GetRemovedLevelDecorations()
        {
            return Math.Max(0, levelDecorationsBefore - levelDecorationsAfter);
        }

        private static double GetWorkloadReductionRatio()
        {
            int decorationBase = levelDataRecorded
                ? Math.Max(levelDecorationsBefore, runtimeDecorationsSeen + GetRemovedLevelDecorations())
                : runtimeDecorationsSeen;
            int workloadBase = Math.Max(0, levelEventsBefore) + Math.Max(0, decorationBase);
            if (workloadBase <= 0)
            {
                return 0d;
            }

            int reduced = GetRemovedEvents() + GetRemovedLevelDecorations() + runtimeDecorationsOptimized;
            return Math.Min(100d, Math.Max(0d, reduced * 100d / workloadBase));
        }

        private static double GetTextureReductionRatio()
        {
            if (measuredBeforeBytes <= 0L)
            {
                return 0d;
            }
            long reduction = Math.Max(0L, measuredBeforeBytes - measuredAfterBytes);
            return Math.Min(100d, reduction * 100d / measuredBeforeBytes);
        }

        private static string FormatPercent(double value)
        {
            return value.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }

        private static bool ShowNotification()
        {
            bool displayed = OptimizationNotificationOverlay.Show(GetNotificationText());
            if (displayed && Main.modEntry != null)
            {
                Main.modEntry.Logger.Log("[LevelOptimization] Independent summary overlay displayed.");
            }
            return displayed;
        }
    }

    [HarmonyPatch(typeof(LevelData), "Decode")]
    public static class LevelDataDecodePatch
    {
        public static void Postfix(LevelData __instance)
        {
            try
            {
                Main.OptimizeLevelData(__instance);
            }
            catch (Exception ex)
            {
                if (Main.modEntry != null)
                {
                    Main.modEntry.Logger.LogException(ex);
                }
            }
        }
    }

    [HarmonyPatch(typeof(scnGame), "LoadAndPlayLevel")]
    public static class LoadAndPlayLevelPatch
    {
        public static void Prefix(string levelPath)
        {
            if (!Main.IsLoadingLevel())
            {
                Main.ResetRuntimeDecorationBudget();
                TextureOptimization.BeginLevelLoad(levelPath, true);
            }
            Main.BeginLoading("LoadAndPlayLevel");
        }

        public static void Postfix()
        {
            Main.EndLoading("LoadAndPlayLevel");
        }
    }

    [HarmonyPatch(typeof(scnGame), "LoadLevel")]
    public static class ScnGameLoadLevelPatch
    {
        public static void Prefix(string levelPath)
        {
            if (!Main.IsLoadingLevel())
            {
                Main.ResetRuntimeDecorationBudget();
                TextureOptimization.BeginLevelLoad(levelPath, false);
            }
            Main.BeginLoading("LoadLevel");
        }

        public static void Postfix()
        {
            Main.EndLoading("LoadLevel");
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo flush = AccessTools.Method(typeof(ADOBase), "FlushUnusedMemory");
            MethodInfo unload = AccessTools.Method(typeof(Resources), "UnloadUnusedAssets", new Type[0]);
            MethodInfo maybeFlush = AccessTools.Method(typeof(Main), "MaybeFlushUnusedMemory");
            MethodInfo maybeUnload = AccessTools.Method(typeof(Main), "MaybeUnloadUnusedAssets");

            foreach (CodeInstruction code in instructions)
            {
                MethodInfo operand = code.operand as MethodInfo;
                if (operand == flush)
                {
                    yield return new CodeInstruction(OpCodes.Call, maybeFlush);
                }
                else if (operand == unload)
                {
                    yield return new CodeInstruction(OpCodes.Call, maybeUnload);
                }
                else
                {
                    yield return code;
                }
            }
        }
    }

    [HarmonyPatch(typeof(scnGame), "ReloadAssets")]
    public static class ReloadAssetsPatch
    {
        public static void Prefix()
        {
            Main.BeginLoading("ReloadAssets");
        }

        public static void Postfix()
        {
            Main.EndLoading("ReloadAssets");
        }
    }

    [HarmonyPatch(typeof(scnGame), "ResetScene")]
    public static class ResetScenePatch
    {
        public static void Prefix()
        {
            Main.BeginLoading("ResetScene");
        }

        public static void Postfix()
        {
            Main.EndLoading("ResetScene");
        }
    }

    [HarmonyPatch(typeof(scrController), "ResetCustomLevel")]
    public static class ResetCustomLevelPatch
    {
        public static void Postfix(ref IEnumerator __result)
        {
            __result = Main.WrapLoadingCoroutine(__result, "ResetCustomLevel");
        }
    }

    [HarmonyPatch(typeof(scrCamera), "SetCustomFrameRate")]
    public static class SetCustomFrameRatePatch
    {
        public static bool Prefix(bool enable)
        {
            return !Main.ShouldBlockCustomFrameRate(enable);
        }
    }

    [HarmonyPatch(typeof(ffxSetFrameRatePlus), "StartEffect")]
    public static class SetFrameRateEventPatch
    {
        public static bool Prefix(ffxSetFrameRatePlus __instance)
        {
            return !Main.ShouldBlockCustomFrameRate(__instance != null && __instance.enableCustomFrameRate);
        }
    }

    [HarmonyPatch(typeof(TextureManager), "LoadTexture")]
    public static class CustomTextureOptimizationPatch
    {
        public static void Postfix(ref Texture2D __result, string filePath)
        {
            TextureOptimization.ProcessTexture(ref __result, filePath);
        }
    }

    [HarmonyPatch(typeof(scrCustomBackgroundSprite), "SetCustomBG")]
    public static class CustomBackgroundTextureScalePatch
    {
        public static void Postfix(scrCustomBackgroundSprite __instance)
        {
            TextureOptimization.ApplyBackgroundScale(__instance);
        }
    }

    [HarmonyPatch(typeof(scrVisualDecoration), "SetSprite", new Type[]
    {
        typeof(TextureManager.CustomSprite),
        typeof(bool)
    })]
    public static class VisualDecorationTextureScalePatch
    {
        public static void Postfix(scrVisualDecoration __instance)
        {
            TextureOptimization.ApplyDecorationScale(__instance);
        }
    }

    [HarmonyPatch(typeof(scrDecorationManager), "UpdateBordersSizes")]
    public static class DecorationBorderTextureScalePatch
    {
        public static void Postfix(scrDecorationManager __instance)
        {
            TextureOptimization.ApplySelectedBorderScales(__instance);
        }
    }

    [HarmonyPatch(typeof(scrDecorationManager), "UpdateHitboxSizes")]
    public static class DecorationHitboxRendererTextureScalePatch
    {
        public static void Postfix()
        {
            TextureOptimization.ApplySelectedHitboxScales();
        }
    }

    [HarmonyPatch(typeof(scrVisualDecoration), "GetDecorationWorldSize")]
    public static class DecorationWorldSizeTextureScalePatch
    {
        public static void Postfix(scrVisualDecoration __instance, ref Vector2 __result)
        {
            TextureOptimization.ApplyWorldSizeScale(__instance, ref __result);
        }
    }

    [HarmonyPatch(typeof(scrVisualDecoration), "UpdateHitbox")]
    public static class DecorationDamageColliderTextureScalePatch
    {
        public static void Postfix(scrVisualDecoration __instance)
        {
            TextureOptimization.ApplyDamageColliderScale(__instance);
        }
    }

    [HarmonyPatch(typeof(scnGame), "UpdateDecorationObjects")]
    public static class TextureOptimizationReportPatch
    {
        public static void Postfix(bool reloadDecorations)
        {
            Main.RefreshRuntimeDecorationBudget(reloadDecorations);
            TextureOptimization.ReportIfReady(reloadDecorations);
        }
    }

    [HarmonyPatch(typeof(scnGame), "OnDestroy")]
    public static class TextureOptimizationSceneDestroyPatch
    {
        public static void Prefix()
        {
            Main.ResetRuntimeDecorationBudget();
            TextureOptimization.ResetSession(true);
        }
    }

    [HarmonyPatch(typeof(RDString), "Get")]
    public static class TextureOptimizationTranslationPatch
    {
        public static bool Prefix(string key, ref string __result)
        {
            if (key != "pommax.textureMemory")
            {
                return true;
            }

            __result = TextureOptimization.GetNotificationText();
            return false;
        }
    }
}
