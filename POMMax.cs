using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime;
using System.Runtime.InteropServices;
using ADOFAI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityModManagerNet;

namespace POMMax
{
    public class Settings : UnityModManager.ModSettings
    {
        public int language = 1;
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
        public int maxDecorations = 5000;
        public bool simplifyDecorationShaders = true;
        public bool throttleDecorationUpdates = true;
        public int decorationUpdateStride = 2;
        public bool enableLoadTimeout = true;
        public int maxLoadSeconds = 45;
        public bool boostGamePriority = true;
        public bool preventSleepDuringGameplay = true;
        public bool maxPerformanceMode = false;
        public bool throttleBackgroundProcesses = false;
        public bool verboseLogging = false;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            UnityModManager.ModSettings.Save(this, modEntry);
        }
    }

    public static class Main
    {
        private const int ProfileOff = 0;
        private const int ProfileBalanced = 1;
        private const int ProfilePerformance = 2;
        private const int ProfileMax = 3;

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
        private static GCLatencyMode originalGcLatencyMode;

        private static bool loadingActive;
        private static bool abortingLoad;
        private static int loadingDepth;
        private static float loadingStartedAt;
        private static string loadingLabel = "";
        private static float nextRuntimeApply;
        private static float nextBackgroundThrottle;

        private static bool capturedOwnPriority;
        private static ProcessPriorityClass originalOwnPriority;
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
                    abortingLoad = false;
                    CaptureQualitySettings();
                    ApplyRuntimePerformance(true);
                    ApplyProcessBoost(true);
                    entry.Logger.Log("POM Max enabled.");
                }
                else
                {
                    modEnabled = false;
                    loadingActive = false;
                    loadingDepth = 0;
                    abortingLoad = false;

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
            RestoreRuntimeSettings();
            RestoreBackgroundPriorities();
            ApplyProcessBoost(false);
            return true;
        }

        private static void OnUpdate(UnityModManager.ModEntry entry, float deltaTime)
        {
            if (!modEnabled || settings == null)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now >= nextRuntimeApply)
            {
                nextRuntimeApply = now + 1f;
                ApplyRuntimePerformance(false);
                ApplyProcessBoost(true);
            }

            if (settings.maxPerformanceMode && settings.throttleBackgroundProcesses && now >= nextBackgroundThrottle)
            {
                nextBackgroundThrottle = now + 5f;
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

            GUILayout.BeginVertical();
            DrawLanguageSelector();
            GUILayout.Space(6f);
            GUILayout.Label("<b>" + Text("title") + "</b>");
            GUILayout.Label(Text("summary"));
            GUILayout.Space(6f);

            GUILayout.Label("<b>" + Text("profile") + "</b>");
            GUILayout.BeginHorizontal();
            DrawProfileButton(ProfileOff, "Off");
            DrawProfileButton(ProfileBalanced, "Balanced");
            DrawProfileButton(ProfilePerformance, "Performance");
            DrawProfileButton(ProfileMax, "MAX");
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("<b>" + Text("runtime") + "</b>");
            settings.forceTargetFrameRate = GUILayout.Toggle(settings.forceTargetFrameRate, Text("forceFps"));
            DrawIntField(Text("targetFps"), ref settings.targetFps, 30, 10000, 90);
            settings.disableVSync = GUILayout.Toggle(settings.disableVSync, Text("disableVsync"));
            settings.disableAntiAliasing = GUILayout.Toggle(settings.disableAntiAliasing, Text("disableAa"));
            settings.disableCustomFrameRateEvents = GUILayout.Toggle(settings.disableCustomFrameRateEvents, Text("blockFrameEvents"));

            GUILayout.Space(6f);
            GUILayout.Label("<b>" + Text("load") + "</b>");
            settings.skipPreloadCleanup = GUILayout.Toggle(settings.skipPreloadCleanup, Text("skipCleanup"));
            settings.enableLoadTimeout = GUILayout.Toggle(settings.enableLoadTimeout, Text("enableTimeout"));
            DrawIntField(Text("timeout"), ref settings.maxLoadSeconds, 5, 600, 90);

            GUILayout.Space(6f);
            GUILayout.Label("<b>" + Text("levelData") + "</b>");
            settings.optimizeLevelEvents = GUILayout.Toggle(settings.optimizeLevelEvents, Text("optimizeEvents"));
            settings.disableBackgroundVideo = GUILayout.Toggle(settings.disableBackgroundVideo, Text("disableVideo"));
            settings.capDecorations = GUILayout.Toggle(settings.capDecorations, Text("capDecorations"));
            DrawIntField(Text("maxDecorations"), ref settings.maxDecorations, 100, 100000, 90);
            settings.simplifyDecorationShaders = GUILayout.Toggle(settings.simplifyDecorationShaders, Text("simpleShaders"));
            settings.throttleDecorationUpdates = GUILayout.Toggle(settings.throttleDecorationUpdates, Text("throttleDecorations"));
            DrawIntField(Text("stride"), ref settings.decorationUpdateStride, 1, 8, 90);

            GUILayout.Space(6f);
            GUILayout.Label("<b>" + Text("system") + "</b>");
            settings.boostGamePriority = GUILayout.Toggle(settings.boostGamePriority, Text("boostPriority"));
            settings.preventSleepDuringGameplay = GUILayout.Toggle(settings.preventSleepDuringGameplay, Text("preventSleep"));
            settings.maxPerformanceMode = GUILayout.Toggle(settings.maxPerformanceMode, Text("maxMode"));
            settings.throttleBackgroundProcesses = GUILayout.Toggle(settings.throttleBackgroundProcesses, Text("backgroundThrottle"));
            settings.verboseLogging = GUILayout.Toggle(settings.verboseLogging, Text("verbose"));

            GUILayout.Space(6f);
            GUILayout.Label(Text("note"));
            GUILayout.EndVertical();
        }

        private static void OnSaveGUI(UnityModManager.ModEntry entry)
        {
            ClampSettings();
            settings.Save(entry);
        }

        public static int EffectiveProfile()
        {
            if (!modEnabled || settings == null)
            {
                return ProfileOff;
            }

            return settings.optimizationProfile;
        }

        public static bool ShouldSimplifyDecorationShaders()
        {
            return EffectiveProfile() >= ProfilePerformance && settings.simplifyDecorationShaders;
        }

        public static bool ShouldThrottleDecoration(scrDecoration decoration)
        {
            if (EffectiveProfile() < ProfileMax || !settings.throttleDecorationUpdates)
            {
                return false;
            }

            if (decoration == null || decoration.useHitbox || decoration.followPlanet != null || decoration.stickToFloor)
            {
                return false;
            }

            if (ADOBase.isLevelEditor && ADOBase.editor != null && ADOBase.editor.inStrictlyEditingMode)
            {
                return false;
            }

            int stride = Math.Max(1, settings.decorationUpdateStride);
            return Time.frameCount % stride != 0;
        }

        public static bool ShouldBlockCustomFrameRate(bool enable)
        {
            return modEnabled && settings != null && settings.disableCustomFrameRateEvents && enable;
        }

        public static void OptimizeLevelData(LevelData data)
        {
            if (data == null || EffectiveProfile() == ProfileOff || !settings.optimizeLevelEvents)
            {
                return;
            }

            if (ADOBase.isLevelEditor && ADOBase.editor != null)
            {
                Verbose("Skipped LevelData optimization in editor scene to avoid saving reduced maps.");
                return;
            }

            int profile = EffectiveProfile();
            int beforeEvents = data.levelEvents.Count;
            int beforeDecorations = data.decorations.Count;

            data.levelEvents.RemoveAll(delegate(LevelEvent ev)
            {
                return ShouldRemoveActionEvent(ev, profile);
            });

            data.decorations.RemoveAll(delegate(LevelEvent ev)
            {
                return ShouldRemoveDecorationEvent(ev, profile);
            });

            if (settings.disableBackgroundVideo && profile >= ProfileBalanced)
            {
                TrySetEventValue(data.miscSettings, "bgVideo", "");
            }

            if (settings.simplifyDecorationShaders && profile >= ProfilePerformance)
            {
                data.disableV15Features = true;
            }

            if (settings.capDecorations && profile >= ProfilePerformance)
            {
                CapDecorations(data, settings.maxDecorations);
            }

            int removedEvents = beforeEvents - data.levelEvents.Count;
            int removedDecorations = beforeDecorations - data.decorations.Count;
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

        private static bool ShouldRemoveActionEvent(LevelEvent ev, int profile)
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

            if (profile >= ProfilePerformance)
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

            if (profile >= ProfileMax)
            {
                if (type == LevelEventType.CustomBackground ||
                    type == LevelEventType.Flash ||
                    type == LevelEventType.ShakeScreen ||
                    type == LevelEventType.MoveDecorations)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ShouldRemoveDecorationEvent(LevelEvent ev, int profile)
        {
            if (ev == null)
            {
                return false;
            }

            if (profile >= ProfilePerformance && ev.eventType == LevelEventType.AddParticle)
            {
                return true;
            }

            if (profile >= ProfileMax && !IsProtectedDecoration(ev))
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
            if (ev == null || ev.data == null)
            {
                return false;
            }

            if (HasNonNoneHitbox(ev))
            {
                return true;
            }

            object value;
            if (ev.data.TryGetValue("components", out value) && value != null && value.ToString().Trim().Length > 0)
            {
                return true;
            }

            if (ev.data.TryGetValue("tag", out value) && value != null)
            {
                string tag = value.ToString();
                if (tag.IndexOf("[attachToTile]", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasNonNoneHitbox(LevelEvent ev)
        {
            object value;
            if (ev.data == null || !ev.data.TryGetValue("hitbox", out value) || value == null)
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
            if (ev == null || ev.data == null)
            {
                return;
            }

            if (ev.data.ContainsKey(key))
            {
                ev.data[key] = value;
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

                if (settings.disableAntiAliasing && profile >= ProfileBalanced)
                {
                    QualitySettings.antiAliasing = 0;
                }

                if (profile >= ProfileMax)
                {
                    QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
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
                    try
                    {
                        GCSettings.LatencyMode = originalGcLatencyMode;
                    }
                    catch
                    {
                    }
                }

                SetThreadExecutionState(EsContinuous);
                timeEndPeriod(1);
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
                    if (!capturedOwnPriority)
                    {
                        originalOwnPriority = current.PriorityClass;
                        capturedOwnPriority = true;
                    }

                    ProcessPriorityClass desired = settings.maxPerformanceMode ? ProcessPriorityClass.High : ProcessPriorityClass.AboveNormal;
                    if (current.PriorityClass != desired)
                    {
                        current.PriorityClass = desired;
                    }

                    try
                    {
                        System.Threading.Thread.CurrentThread.Priority = System.Threading.ThreadPriority.Highest;
                    }
                    catch
                    {
                    }

                    timeBeginPeriod(1);
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
                    SetThreadExecutionState(EsContinuous);
                    timeEndPeriod(1);
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

                    if (p.PriorityClass != ProcessPriorityClass.Idle && p.PriorityClass != ProcessPriorityClass.BelowNormal)
                    {
                        p.PriorityClass = ProcessPriorityClass.BelowNormal;
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

        private static void DrawLanguageSelector()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Text("language"), GUILayout.Width(100));
            if (GUILayout.Toggle(settings.language == 0, "CN", "Button", GUILayout.Width(80)))
            {
                settings.language = 0;
            }
            if (GUILayout.Toggle(settings.language == 1, "EN", "Button", GUILayout.Width(80)))
            {
                settings.language = 1;
            }
            if (GUILayout.Toggle(settings.language == 2, "KR", "Button", GUILayout.Width(80)))
            {
                settings.language = 2;
            }
            GUILayout.EndHorizontal();
        }

        private static void DrawProfileButton(int profile, string label)
        {
            if (GUILayout.Toggle(settings.optimizationProfile == profile, label, "Button", GUILayout.Width(130)))
            {
                settings.optimizationProfile = profile;
            }
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
                case "summary": return "Optimizes frame pacing, level load/restart cost, and optional Windows process priority for heavy custom levels.";
                case "profile": return "Optimization profile";
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
                case "capDecorations": return "Cap visual decorations in Performance/MAX profiles";
                case "maxDecorations": return "Max decorations:";
                case "simpleShaders": return "Simplify decoration shaders in Performance/MAX profiles";
                case "throttleDecorations": return "Throttle visual-only decoration updates in MAX profile";
                case "stride": return "Decoration update stride:";
                case "system": return "Windows performance mode";
                case "boostPriority": return "Raise game process priority";
                case "preventSleep": return "Prevent sleep and use 1 ms timer while active";
                case "maxMode": return "MAX mode: stronger game priority";
                case "backgroundThrottle": return "Lower priority of other user-session processes";
                case "verbose": return "Verbose log optimization details";
                case "range": return "Range: ";
                case "note": return "Performance/MAX profiles may reduce visual effects. Editor scene level data is not optimized to avoid saving reduced maps.";
                default: return key;
            }
        }

        private static string TextChinese(string key)
        {
            switch (key)
            {
                case "language": return "语言:";
                case "title": return "性能优化 MAX";
                case "summary": return "优化重特效自定义谱面的帧率、加载/重开耗时，并提供可选 Windows 进程优先级策略。";
                case "profile": return "优化档位";
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
                case "capDecorations": return "Performance/MAX 档限制视觉装饰数量";
                case "maxDecorations": return "最大装饰数量:";
                case "simpleShaders": return "Performance/MAX 档简化装饰 shader";
                case "throttleDecorations": return "MAX 档节流纯视觉装饰更新";
                case "stride": return "装饰更新间隔:";
                case "system": return "Windows 性能模式";
                case "boostPriority": return "提高游戏进程优先级";
                case "preventSleep": return "启用时防止睡眠并使用 1ms 计时器";
                case "maxMode": return "MAX 模式：更强游戏优先级";
                case "backgroundThrottle": return "降低同一用户会话中其他进程优先级";
                case "verbose": return "记录详细优化日志";
                case "range": return "范围: ";
                case "note": return "Performance/MAX 档可能减少视觉特效。编辑器场景不会优化谱面数据，避免保存成被削减的谱面。";
                default: return key;
            }
        }

        private static string TextKorean(string key)
        {
            switch (key)
            {
                case "language": return "언어:";
                case "title": return "성능 최적화 MAX";
                case "summary": return "무거운 커스텀 레벨의 프레임, 로딩/재시작 비용, Windows 프로세스 우선순위를 최적화합니다.";
                case "profile": return "최적화 프로필";
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
                case "capDecorations": return "Performance/MAX 프로필에서 장식 수 제한";
                case "maxDecorations": return "최대 장식 수:";
                case "simpleShaders": return "Performance/MAX 프로필에서 장식 셰이더 단순화";
                case "throttleDecorations": return "MAX 프로필에서 순수 시각 장식 업데이트 절감";
                case "stride": return "장식 업데이트 간격:";
                case "system": return "Windows 성능 모드";
                case "boostPriority": return "게임 프로세스 우선순위 상승";
                case "preventSleep": return "활성화 중 절전 방지 및 1ms 타이머 사용";
                case "maxMode": return "MAX 모드: 더 강한 게임 우선순위";
                case "backgroundThrottle": return "같은 사용자 세션의 다른 프로세스 우선순위 낮춤";
                case "verbose": return "자세한 최적화 로그";
                case "range": return "범위: ";
                case "note": return "Performance/MAX 프로필은 시각 효과를 줄일 수 있습니다. 저장 손상을 막기 위해 에디터 장면의 데이터는 최적화하지 않습니다.";
                default: return key;
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

    [HarmonyPatch(typeof(scrVisualDecoration), "UpdateShader")]
    public static class VisualDecorationUpdateShaderPatch
    {
        public static bool Prefix(scrVisualDecoration __instance)
        {
            if (!Main.ShouldSimplifyDecorationShaders())
            {
                return true;
            }

            try
            {
                if (__instance != null)
                {
                    if (__instance.meshRendererObj != null && __instance.meshRendererObj.activeSelf)
                    {
                        __instance.meshRendererObj.SetActive(false);
                    }
                    if (__instance.spriteRenderer != null)
                    {
                        __instance.spriteRenderer.enabled = __instance.GetVisible();
                    }
                }
            }
            catch
            {
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(scrDecoration), "UpdatePosition")]
    public static class DecorationUpdatePositionPatch
    {
        public static bool Prefix(scrDecoration __instance)
        {
            return !Main.ShouldThrottleDecoration(__instance);
        }
    }
}
