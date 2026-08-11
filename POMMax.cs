using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using ADOFAI;
using DG.Tweening;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Profiling;
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
        private const uint EsContinuous = 0x80000000;
        private const uint EsSystemRequired = 0x00000001;
        private const uint EsDisplayRequired = 0x00000002;

        public static UnityModManager.ModEntry modEntry;
        public static Settings settings;

        private static Harmony harmony;
        private static bool modEnabled;
        private static bool capturedQualitySettings;
        private static int originalTargetFrameRate;
        private static bool originalRunInBackground;
        private static UnityEngine.ThreadPriority originalBackgroundLoadingPriority;
        private static int originalVSync;
        private static int originalAntiAliasing;
        private static int originalAsyncUploadTimeSlice;
        private static int originalAsyncUploadBufferSize;
        private static bool originalAsyncUploadPersistentBuffer;
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
        private static int loadingDepth;
        private static float loadingStartedAt;
        private static string loadingLabel = "";
        private static float nextRuntimeApply;

        private static bool capturedOwnPriority;
        private static ProcessPriorityClass originalOwnPriority;
        private static int configuredTweenerCapacity;
        private static int configuredSequenceCapacity;
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
                        ApplyHarmonyPatches();
                    }

                    modEnabled = true;
                    OptimizationNotificationOverlay.Ensure();
                    RestoreRuntimeDecorations();
                    TextureOptimization.ResetSession(true);
                    CaptureQualitySettings();
                    ApplyRuntimePerformance(true);
                    ApplyProcessBoost(true);
                    TextureOptimization.RefreshCompatibilityState(true);
                    CompatibilityDiagnostics.Report();
                    entry.Logger.Log("POM Max enabled. " + GetProfileDiagnosticText());
                }
                else
                {
                    modEnabled = false;
                    OptimizationNotificationOverlay.Hide();
                    loadingActive = false;
                    loadingDepth = 0;
                    RestoreRuntimeDecorations();
                    TextureOptimization.ResetSession(true);

                    RestoreRuntimeSettings();
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
                SafeDisableAfterFailure();
                return false;
            }
        }

        private static bool OnUnload(UnityModManager.ModEntry entry)
        {
            modEnabled = false;
            RestoreRuntimeDecorations();
            TextureOptimization.ResetSession(true);
            RestoreRuntimeSettings();
            ApplyProcessBoost(false);
            if (harmony != null)
            {
                harmony.UnpatchAll(entry.Info.Id);
                harmony = null;
            }
            GuiTheme.Dispose();
            OptimizationNotificationOverlay.Dispose();
            return true;
        }

        private static void ApplyHarmonyPatches()
        {
            int applied = 0;
            int skipped = 0;
            Type[] types = Assembly.GetExecutingAssembly().GetTypes();
            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                if (type.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0)
                {
                    continue;
                }

                try
                {
                    IEnumerable<MethodBase> originals = harmony.CreateClassProcessor(type).Patch();
                    int count = 0;
                    if (originals != null)
                    {
                        foreach (MethodBase unused in originals)
                        {
                            count++;
                        }
                    }
                    if (count > 0)
                    {
                        applied += count;
                    }
                    else
                    {
                        skipped++;
                        modEntry.Logger.Warning("[Compatibility] Optional patch skipped: " + type.FullName + ".");
                    }
                }
                catch (Exception ex)
                {
                    skipped++;
                    modEntry.Logger.Warning("[Compatibility] Optional patch failed and was isolated: " + type.FullName + ".");
                    modEntry.Logger.LogException(ex);
                }
            }

            if (applied == 0)
            {
                throw new InvalidOperationException("No compatible POM-Max patches were found for this game build.");
            }
            modEntry.Logger.Log("[Compatibility] Harmony patches applied=" + applied + ", skipped=" + skipped + ".");
        }

        private static void SafeDisableAfterFailure()
        {
            try
            {
                loadingActive = false;
                loadingDepth = 0;
                RestoreRuntimeDecorations();
                TextureOptimization.ResetSession(true);
                RestoreRuntimeSettings();
                ApplyProcessBoost(false);
                OptimizationNotificationOverlay.Hide();
                if (harmony != null && modEntry != null)
                {
                    harmony.UnpatchAll(modEntry.Info.Id);
                    harmony = null;
                }
            }
            catch
            {
            }
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
                nextRuntimeApply = now + 10f;
                ApplyRuntimePerformance(false);
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
        }

        private static void RestoreRuntimeDecorations()
        {
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
            bool editorData = ADOBase.isLevelEditor && ADOBase.editor != null;
            TextureOptimization.RecordLevelData(
                beforeEvents,
                beforeEvents,
                beforeDecorations,
                beforeDecorations,
                editorData);

            // Never mutate decoded chart data. Removing visual events made high-effect
            // charts faster at the cost of broken animations and black decorations.
            Verbose("Level data preserved. Events=" + beforeEvents + ", decorations=" + beforeDecorations + ".");
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
            loadingStartedAt = Time.realtimeSinceStartup;
            loadingLabel = label ?? "Loading";
            ApplyLoadingPolicy(true);
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
            ApplyLoadingPolicy(false);
            TextureOptimization.RecordLoadingDuration(elapsed);
            Verbose("Loading finished: " + (label ?? loadingLabel) + " in " + elapsed.ToString("0.00") + "s.");
        }

        public static IEnumerator WrapLoadingCoroutine(IEnumerator inner, string label)
        {
            BeginLoading(label);
            try
            {
                while (true)
                {
                    object current = null;
                    bool moved = inner != null && inner.MoveNext();
                    if (moved)
                    {
                        current = inner.Current;
                    }

                    if (!moved)
                    {
                        break;
                    }

                    yield return current;
                }
            }
            finally
            {
                EndLoading(label);
            }
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

        private static void CaptureQualitySettings()
        {
            if (capturedQualitySettings)
            {
                return;
            }

            capturedQualitySettings = true;
            originalTargetFrameRate = Application.targetFrameRate;
            originalRunInBackground = Application.runInBackground;
            originalBackgroundLoadingPriority = Application.backgroundLoadingPriority;
            originalVSync = QualitySettings.vSyncCount;
            originalAntiAliasing = QualitySettings.antiAliasing;
            originalAsyncUploadTimeSlice = QualitySettings.asyncUploadTimeSlice;
            originalAsyncUploadBufferSize = QualitySettings.asyncUploadBufferSize;
            originalAsyncUploadPersistentBuffer = QualitySettings.asyncUploadPersistentBuffer;
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

                int uploadBuffer = IsOverdriveMode() ? 64 : 32;
                if (QualitySettings.asyncUploadBufferSize != uploadBuffer)
                {
                    QualitySettings.asyncUploadBufferSize = uploadBuffer;
                }
                QualitySettings.asyncUploadPersistentBuffer = true;
                ApplyLoadingPolicy(loadingActive);
                ConfigureTweenCapacity();

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
                    Application.runInBackground = originalRunInBackground;
                    Application.backgroundLoadingPriority = originalBackgroundLoadingPriority;
                    QualitySettings.vSyncCount = originalVSync;
                    QualitySettings.antiAliasing = originalAntiAliasing;
                    QualitySettings.asyncUploadTimeSlice = originalAsyncUploadTimeSlice;
                    QualitySettings.asyncUploadBufferSize = originalAsyncUploadBufferSize;
                    QualitySettings.asyncUploadPersistentBuffer = originalAsyncUploadPersistentBuffer;
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

                    // AboveNormal is enough to protect frame pacing without starving OBS,
                    // audio services, Steam networking, or controller input threads.
                    ProcessPriorityClass desired = ProcessPriorityClass.AboveNormal;
                    if (current.PriorityClass != desired)
                    {
                        current.PriorityClass = desired;
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
                    SetThreadExecutionState(EsContinuous);
                }
            }
            catch (Exception ex)
            {
                Verbose("ApplyProcessBoost failed: " + ex.Message);
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

        private static void ConfigureTweenCapacity()
        {
            int tweeners = IsOverdriveMode() ? 4000 : 1500;
            int sequences = IsOverdriveMode() ? 1000 : 400;
            if (configuredTweenerCapacity >= tweeners && configuredSequenceCapacity >= sequences)
            {
                return;
            }

            try
            {
                DOTween.SetTweensCapacity(tweeners, sequences);
                configuredTweenerCapacity = tweeners;
                configuredSequenceCapacity = sequences;
                if (modEntry != null)
                {
                    modEntry.Logger.Log("[TweenCapacity] Preallocated " + tweeners + " tweeners and " + sequences + " sequences.");
                }
            }
            catch (Exception ex)
            {
                Verbose("Tween capacity configuration was skipped: " + ex.Message);
            }
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

        private static void ApplyLoadingPolicy(bool loading)
        {
            if (!modEnabled || settings == null || EffectiveProfile() == ProfileOff)
            {
                return;
            }

            try
            {
                Application.backgroundLoadingPriority = loading
                    ? UnityEngine.ThreadPriority.High
                    : UnityEngine.ThreadPriority.Normal;
                QualitySettings.asyncUploadTimeSlice = loading
                    ? (IsOverdriveMode() ? 8 : 4)
                    : 2;
            }
            catch (Exception ex)
            {
                Verbose("ApplyLoadingPolicy failed: " + ex.Message);
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
            settings.skipPreloadCleanup = false;
            settings.optimizeLevelEvents = false;
            settings.disableBackgroundVideo = false;
            settings.capDecorations = false;
            settings.maxDecorations = 100000;
            settings.simplifyDecorationShaders = false;
            settings.throttleDecorationUpdates = false;
            settings.decorationUpdateStride = 2;
            settings.enableLoadTimeout = false;
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
            settings.boostGamePriority = true;
            settings.preventSleepDuringGameplay = true;
            settings.throttleBackgroundProcesses = false;
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
                + ", textureDivisor=" + settings.textureScaleDivisor.ToString("0.##", CultureInfo.InvariantCulture)
                + ", asyncUploadBufferMB=" + (settings.overdriveMode ? 64 : 32)
                + ", tweenCapacity=" + (settings.overdriveMode ? 4000 : 1500)
                + ", chartDataPreserved=true"
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
                case "profileMaxDescription": return "Stable frame pacing and faster custom-texture loading without changing chart events or decorations.";
                case "profileExtremeDescription": return "Maximum safe runtime tuning with stronger texture limits and preallocated effect capacity.";
                case "profileOffConfiguration": return "Use this profile for compatibility checks.";
                case "profileMaxConfiguration": return "2x large-texture limit, 32 MB async upload buffer, 1,500 preallocated tweens; all chart data remains intact.";
                case "profileExtremeConfiguration": return "8x large-texture limit with a 32px safety floor, 64 MB upload buffer and 4,000 preallocated tweens.";
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
                case "note": return "Chart events and decorations are never deleted. If another texture optimizer is active, POM-Max disables only its texture module.";
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
                case "profileMaxDescription": return "稳定改善帧时间与自定义纹理加载，不改动谱面事件和装饰物。";
                case "profileExtremeDescription": return "在保留谱面逻辑的前提下启用更强纹理限制与特效容量预分配。";
                case "profileOffConfiguration": return "适合排查兼容性问题。";
                case "profileMaxConfiguration": return "大纹理限制为 2 倍、异步上传缓冲 32 MB、预分配 1500 个 Tween；谱面数据完整保留。";
                case "profileExtremeConfiguration": return "大纹理最多限制 8 倍并保留 32 像素短边、上传缓冲 64 MB、预分配 4000 个 Tween。";
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
                case "note": return "不会删除谱面事件或装饰物；检测到其他纹理优化器时，仅自动停用本模组的纹理模块。";
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
                case "profileMaxDescription": return "차트 이벤트와 장식을 변경하지 않고 프레임 페이싱과 커스텀 텍스처 로딩을 개선합니다.";
                case "profileExtremeDescription": return "차트 로직을 유지하면서 더 강한 텍스처 제한과 이펙트 용량 사전 할당을 적용합니다.";
                case "profileOffConfiguration": return "호환성 문제를 확인할 때 사용합니다.";
                case "profileMaxConfiguration": return "대형 텍스처 2배 제한, 32 MB 업로드 버퍼, Tween 1,500개 사전 할당; 차트 데이터는 유지됩니다.";
                case "profileExtremeConfiguration": return "대형 텍스처 최대 8배 및 32픽셀 하한, 64 MB 업로드 버퍼, Tween 4,000개 사전 할당.";
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
                case "note": return "차트 이벤트와 장식을 삭제하지 않습니다. 다른 텍스처 최적화가 감지되면 POM-Max 텍스처 모듈만 비활성화됩니다.";
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
        public sealed class TextureLoadState
        {
            public bool eligible;
            public bool pomApplied;
            public string filePath;
            public int originalWidth;
            public int originalHeight;
            public int requestedMaxSide;
        }

        private const int TextureFailureLimit = 3;

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
        private static bool compatibilityKnown;
        private static bool textureConflict;
        private static string textureConflictOwner = "";
        private static bool textureModuleDisabled;
        private static int consecutiveTextureFailures;
        private static string lastReport = "";
        private static string currentLoadPath = "";
        private static string lastReportedLoadPath = "";
        private static float lastReportedAt = -1000f;
        private static bool editorLevelDataSkipped;
        private static int levelEventsBefore;
        private static int levelDecorationsBefore;
        private static float loadingDuration;
        private static float reportFallbackAt = -1f;

        public static TextureLoadState PrepareTextureLoad(string filePath, ref int maxSideSize)
        {
            TextureLoadState state = new TextureLoadState();
            state.filePath = filePath ?? "";

            if (!ShouldOptimize()
                || !ShouldOptimizePath(filePath))
            {
                return state;
            }

            if (!compatibilityKnown)
            {
                RefreshCompatibilityState(false);
            }

            if (textureConflict || textureModuleDisabled)
            {
                skippedCount++;
                return state;
            }

            try
            {
                int originalWidth;
                int originalHeight;
                if (!TryReadImageDimensions(filePath, out originalWidth, out originalHeight))
                {
                    skippedCount++;
                    return state;
                }

                state.eligible = true;
                state.originalWidth = originalWidth;
                state.originalHeight = originalHeight;

                float divisor = Mathf.Max(1f, Main.settings.textureScaleDivisor);
                if (divisor <= 1f)
                {
                    skippedCount++;
                    return state;
                }

                int maximumSide = Math.Max(originalWidth, originalHeight);
                int minimumSide = Math.Min(originalWidth, originalHeight);
                float scale = 1f / divisor;
                if (minimumSide > 0 && minimumSide * scale < 32f)
                {
                    scale = Math.Min(1f, 32f / minimumSide);
                }

                int targetMaximumSide = Math.Max(4, Mathf.RoundToInt(maximumSide * scale));
                if (Main.settings.roundTextureDimensionsToMultipleOf4 && targetMaximumSide >= 4)
                {
                    targetMaximumSide = Math.Min(maximumSide, NearestMultipleOf4(targetMaximumSide));
                }

                if (targetMaximumSide >= maximumSide)
                {
                    skippedCount++;
                    return state;
                }

                if (maxSideSize < 0 || targetMaximumSide < maxSideSize)
                {
                    maxSideSize = targetMaximumSide;
                    state.pomApplied = true;
                    state.requestedMaxSide = targetMaximumSide;
                }
                else
                {
                    skippedCount++;
                }
            }
            catch (Exception ex)
            {
                HandleTextureLoadFailure(state, ex);
            }

            return state;
        }

        public static void ProcessTexture(Texture2D texture, TextureLoadState state)
        {
            if (state == null || !state.eligible || texture == null || texture.width <= 0 || texture.height <= 0)
            {
                return;
            }

            int textureId = texture.GetInstanceID();
            if (!processedTextureIds.Add(textureId))
            {
                duplicateCount++;
                return;
            }

            loadedCount++;
            consecutiveTextureFailures = 0;
            long beforeBytes = Math.Max(0L, (long)state.originalWidth * state.originalHeight * 4L);
            long afterBytes = MeasureTextureBytes(texture);

            if (state.pomApplied && (texture.width < state.originalWidth || texture.height < state.originalHeight))
            {
                resizedCount++;
                textureRatios[textureId] = new Vector3(
                    (float)state.originalWidth / texture.width,
                    (float)state.originalHeight / texture.height,
                    1f);
                measuredBeforeBytes += beforeBytes;
                measuredAfterBytes += Math.Max(0L, afterBytes);
            }
            else
            {
                skippedCount++;
                textureRatios[textureId] = Vector3.one;
            }
        }

        public static void HandleTextureLoadFailure(TextureLoadState state, Exception exception)
        {
            if (state == null || !state.eligible || exception == null)
            {
                return;
            }

            errorCount++;
            consecutiveTextureFailures++;
            if (Main.modEntry != null)
            {
                Main.modEntry.Logger.Warning("[TextureOptimizer] Pre-limit failed for " + (string.IsNullOrEmpty(state.filePath) ? "<unknown>" : state.filePath));
                Main.modEntry.Logger.LogException(exception);
            }

            if (consecutiveTextureFailures >= TextureFailureLimit && !textureModuleDisabled)
            {
                textureModuleDisabled = true;
                if (Main.modEntry != null)
                {
                    Main.modEntry.Logger.Error("[TextureOptimizer] Disabled for this session after repeated failures. Original game loading remains active.");
                }
            }
        }

        public static void RefreshCompatibilityState(bool force)
        {
            if (compatibilityKnown && !force)
            {
                return;
            }

            compatibilityKnown = true;
            textureConflict = false;
            textureConflictOwner = "";

            MethodInfo target = AccessTools.Method(
                typeof(TextureManager),
                "LoadTexture",
                new Type[] { typeof(string), typeof(LoadResult).MakeByRefType(), typeof(int) });
            HarmonyLib.Patches patchInfo = target != null ? Harmony.GetPatchInfo(target) : null;
            if (patchInfo != null)
            {
                FindTextureConflict(patchInfo.Prefixes);
                FindTextureConflict(patchInfo.Postfixes);
                FindTextureConflict(patchInfo.Transpilers);
                FindTextureConflict(patchInfo.Finalizers);
            }

            if (textureConflict)
            {
                if (Main.modEntry != null)
                {
                    Main.modEntry.Logger.Warning(
                        "[TextureOptimizer] Another texture optimizer owns TextureManager.LoadTexture ("
                        + textureConflictOwner
                        + "). POM-Max texture scaling is disabled to prevent double processing.");
                }
                return;
            }

            if (force)
            {
                textureModuleDisabled = false;
                consecutiveTextureFailures = 0;
            }
        }

        private static void FindTextureConflict(IEnumerable<Patch> patches)
        {
            if (textureConflict || patches == null)
            {
                return;
            }

            string ownId = Main.modEntry != null ? Main.modEntry.Info.Id : "POMMax";
            foreach (Patch patch in patches)
            {
                string owner = patch.owner ?? "";
                Type declaringType = patch.PatchMethod != null ? patch.PatchMethod.DeclaringType : null;
                string typeName = declaringType != null ? declaringType.FullName ?? "" : "";
                if (string.Equals(owner, ownId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (owner.IndexOf("optimiz", StringComparison.OrdinalIgnoreCase) >= 0
                    || owner.IndexOf("iridium", StringComparison.OrdinalIgnoreCase) >= 0
                    || typeName.StartsWith("SANSMASTER.", StringComparison.Ordinal)
                    || typeName.IndexOf("Iridium", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    textureConflict = true;
                    textureConflictOwner = string.IsNullOrEmpty(owner) ? typeName : owner;
                    return;
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
            editorLevelDataSkipped = false;
            levelEventsBefore = 0;
            levelDecorationsBefore = 0;
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
            editorLevelDataSkipped = skippedInEditor;
            levelEventsBefore = Math.Max(0, eventsBefore);
            levelDecorationsBefore = Math.Max(0, decorationsBefore);
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

        private static bool TryReadImageDimensions(string filePath, out int width, out int height)
        {
            width = 0;
            height = 0;
            try
            {
                string extension = Path.GetExtension(filePath);
                using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
                    {
                        byte[] header = new byte[24];
                        if (stream.Read(header, 0, header.Length) != header.Length
                            || header[0] != 0x89
                            || header[1] != 0x50
                            || header[2] != 0x4E
                            || header[3] != 0x47
                            || header[12] != 0x49
                            || header[13] != 0x48
                            || header[14] != 0x44
                            || header[15] != 0x52)
                        {
                            return false;
                        }

                        width = ReadBigEndianInt32(header, 16);
                        height = ReadBigEndianInt32(header, 20);
                        return IsValidImageSize(width, height);
                    }

                    if (!string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    if (stream.ReadByte() != 0xFF || stream.ReadByte() != 0xD8)
                    {
                        return false;
                    }

                    while (stream.Position < stream.Length)
                    {
                        int markerPrefix = stream.ReadByte();
                        while (markerPrefix != 0xFF && markerPrefix >= 0)
                        {
                            markerPrefix = stream.ReadByte();
                        }
                        if (markerPrefix < 0)
                        {
                            break;
                        }

                        int marker = stream.ReadByte();
                        while (marker == 0xFF)
                        {
                            marker = stream.ReadByte();
                        }
                        if (marker < 0 || marker == 0xD9 || marker == 0xDA)
                        {
                            break;
                        }
                        if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                        {
                            continue;
                        }

                        int segmentLength = ReadBigEndianUInt16(stream);
                        if (segmentLength < 2)
                        {
                            return false;
                        }

                        if (IsJpegStartOfFrame(marker))
                        {
                            if (segmentLength < 7 || stream.ReadByte() < 0)
                            {
                                return false;
                            }
                            height = ReadBigEndianUInt16(stream);
                            width = ReadBigEndianUInt16(stream);
                            return IsValidImageSize(width, height);
                        }

                        long next = stream.Position + segmentLength - 2L;
                        if (next < stream.Position || next > stream.Length)
                        {
                            return false;
                        }
                        stream.Position = next;
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        private static int ReadBigEndianInt32(byte[] value, int offset)
        {
            return (value[offset] << 24)
                | (value[offset + 1] << 16)
                | (value[offset + 2] << 8)
                | value[offset + 3];
        }

        private static int ReadBigEndianUInt16(Stream stream)
        {
            int high = stream.ReadByte();
            int low = stream.ReadByte();
            return high < 0 || low < 0 ? -1 : (high << 8) | low;
        }

        private static bool IsValidImageSize(int width, int height)
        {
            return width > 0 && height > 0 && width <= 131072 && height <= 131072;
        }

        private static bool IsJpegStartOfFrame(int marker)
        {
            return marker == 0xC0
                || marker == 0xC1
                || marker == 0xC2
                || marker == 0xC3
                || marker == 0xC5
                || marker == 0xC6
                || marker == 0xC7
                || marker == 0xC9
                || marker == 0xCA
                || marker == 0xCB
                || marker == 0xCD
                || marker == 0xCE
                || marker == 0xCF;
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
            return "chartEventsPreserved=" + levelEventsBefore
                + ", levelDecorationsPreserved=" + levelDecorationsBefore
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
            string textureRatio = FormatPercent(GetTextureReductionRatio());
            string amount = FormatMegabytes(Math.Max(0L, measuredBeforeBytes - measuredAfterBytes));
            int uploadBuffer = Main.IsOverdriveMode() ? 64 : 32;
            int tweenCapacity = Main.IsOverdriveMode() ? 4000 : 1500;

            if (language == 0)
            {
                if (multiline)
                {
                    return "谱面事件与装饰完整保留  ·  纹理 " + resizedCount + "/" + loadedCount + " 张已限制"
                        + "  ·  节省 " + amount + " MB  ·  优化比 " + textureRatio
                        + "\n上传缓冲 " + uploadBuffer + " MB  ·  Tween 容量 " + tweenCapacity
                        + "  ·  加载 " + loadingDuration.ToString("0.00", CultureInfo.InvariantCulture) + " 秒";
                }
                return "优化完成｜谱面数据完整保留｜纹理 " + resizedCount + "/" + loadedCount
                    + "\n节省 " + amount + " MB｜优化比 " + textureRatio
                    + "｜上传 " + uploadBuffer + " MB"
                    + "｜加载 " + loadingDuration.ToString("0.00", CultureInfo.InvariantCulture) + " 秒";
            }

            if (language == 2)
            {
                if (multiline)
                {
                    return "차트 이벤트와 장식 보존  ·  텍스처 " + resizedCount + "/" + loadedCount + "개 제한"
                        + "  ·  " + amount + " MB 절감  ·  최적화 " + textureRatio
                        + "\n업로드 버퍼 " + uploadBuffer + " MB  ·  Tween 용량 " + tweenCapacity
                        + "  ·  로드 " + loadingDuration.ToString("0.00", CultureInfo.InvariantCulture) + "초";
                }
                return "최적화 완료 | 차트 데이터 보존 | 텍스처 " + resizedCount + "/" + loadedCount
                    + "\n" + amount + " MB 절감 | 최적화 " + textureRatio
                    + " | 업로드 " + uploadBuffer + " MB"
                    + " | 로드 " + loadingDuration.ToString("0.00", CultureInfo.InvariantCulture) + "초";
            }

            if (multiline)
            {
                return "Chart events and decorations preserved  ·  textures " + resizedCount + "/" + loadedCount + " limited"
                    + "  ·  saved " + amount + " MB  ·  reduction " + textureRatio
                    + "\nUpload buffer " + uploadBuffer + " MB  ·  tween capacity " + tweenCapacity
                    + "  ·  load " + loadingDuration.ToString("0.00", CultureInfo.InvariantCulture) + " s";
            }
            return "Optimized | chart data preserved | textures " + resizedCount + "/" + loadedCount
                + "\nSaved " + amount + " MB | reduction " + textureRatio
                + " | upload " + uploadBuffer + " MB"
                + " | load " + loadingDuration.ToString("0.00", CultureInfo.InvariantCulture) + " s";
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

    public static class CompatibilityDiagnostics
    {
        public static void Report()
        {
            if (Main.modEntry == null)
            {
                return;
            }

            int missing = 0;
            missing += Check(
                typeof(LevelData),
                "Decode",
                new Type[] { typeof(Dictionary<string, object>), typeof(LoadResult).MakeByRefType() });
            missing += Check(typeof(scnGame), "LoadAndPlayLevel", new Type[] { typeof(string) });
            missing += Check(
                typeof(scnGame),
                "LoadLevel",
                new Type[] { typeof(string), typeof(LoadResult).MakeByRefType() });
            missing += Check(typeof(scnGame), "ReloadAssets", new Type[] { typeof(bool), typeof(bool) });
            missing += Check(typeof(scnGame), "ResetScene", new Type[] { typeof(bool) });
            missing += Check(typeof(scrController), "ResetCustomLevel", new Type[] { typeof(bool) });
            missing += Check(
                typeof(TextureManager),
                "LoadTexture",
                new Type[] { typeof(string), typeof(LoadResult).MakeByRefType(), typeof(int) });
            missing += Check(typeof(scnGame), "UpdateDecorationObjects", new Type[] { typeof(bool) });

            if (missing == 0)
            {
                Main.modEntry.Logger.Log(
                    "[Compatibility] Game " + Application.version + ": all required supported method signatures were verified.");
            }
            else
            {
                Main.modEntry.Logger.Warning(
                    "[Compatibility] Game " + Application.version + ": " + missing
                    + " required method signature(s) are unavailable. Affected optional patches were skipped or will remain inactive.");
            }
        }

        private static int Check(Type type, string methodName, Type[] argumentTypes)
        {
            MethodInfo method = AccessTools.Method(type, methodName, argumentTypes);
            if (method != null)
            {
                return 0;
            }

            Main.modEntry.Logger.Warning(
                "[Compatibility] Missing method: " + type.FullName + "." + methodName + ".");
            return 1;
        }
    }

    [HarmonyPatch]
    public static class LevelDataDecodePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(LevelData),
                "Decode",
                new Type[] { typeof(Dictionary<string, object>), typeof(LoadResult).MakeByRefType() });
        }

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

    [HarmonyPatch]
    public static class LoadAndPlayLevelPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(scnGame), "LoadAndPlayLevel", new Type[] { typeof(string) });
        }

        public static void Prefix(string levelPath)
        {
            if (!Main.IsLoadingLevel())
            {
                Main.ResetRuntimeDecorationBudget();
                TextureOptimization.BeginLevelLoad(levelPath, true);
            }
            Main.BeginLoading("LoadAndPlayLevel");
        }

        public static Exception Finalizer(Exception __exception)
        {
            Main.EndLoading("LoadAndPlayLevel");
            return __exception;
        }
    }

    [HarmonyPatch]
    public static class ScnGameLoadLevelPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(scnGame),
                "LoadLevel",
                new Type[] { typeof(string), typeof(LoadResult).MakeByRefType() });
        }

        public static void Prefix(string levelPath)
        {
            if (!Main.IsLoadingLevel())
            {
                Main.ResetRuntimeDecorationBudget();
                TextureOptimization.BeginLevelLoad(levelPath, false);
            }
            Main.BeginLoading("LoadLevel");
        }

        public static Exception Finalizer(Exception __exception)
        {
            Main.EndLoading("LoadLevel");
            return __exception;
        }
    }

    [HarmonyPatch]
    public static class ReloadAssetsPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(scnGame), "ReloadAssets", new Type[] { typeof(bool), typeof(bool) });
        }

        public static void Prefix()
        {
            Main.BeginLoading("ReloadAssets");
        }

        public static Exception Finalizer(Exception __exception)
        {
            Main.EndLoading("ReloadAssets");
            return __exception;
        }
    }

    [HarmonyPatch]
    public static class ResetScenePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(scnGame), "ResetScene", new Type[] { typeof(bool) });
        }

        public static void Prefix()
        {
            Main.BeginLoading("ResetScene");
        }

        public static Exception Finalizer(Exception __exception)
        {
            Main.EndLoading("ResetScene");
            return __exception;
        }
    }

    [HarmonyPatch]
    public static class ResetCustomLevelPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(scrController), "ResetCustomLevel", new Type[] { typeof(bool) });
        }

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

    [HarmonyPatch]
    public static class CustomTextureOptimizationPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(TextureManager),
                "LoadTexture",
                new Type[] { typeof(string), typeof(LoadResult).MakeByRefType(), typeof(int) });
        }

        public static void Prefix(string filePath, ref int maxSideSize, out TextureOptimization.TextureLoadState __state)
        {
            __state = TextureOptimization.PrepareTextureLoad(filePath, ref maxSideSize);
        }

        public static void Postfix(Texture2D __result, TextureOptimization.TextureLoadState __state)
        {
            TextureOptimization.ProcessTexture(__result, __state);
        }

        public static Exception Finalizer(Exception __exception, TextureOptimization.TextureLoadState __state)
        {
            TextureOptimization.HandleTextureLoadFailure(__state, __exception);
            return __exception;
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

    [HarmonyPatch]
    public static class TextureOptimizationReportPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(scnGame), "UpdateDecorationObjects", new Type[] { typeof(bool) });
        }

        public static void Postfix(bool reloadDecorations)
        {
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
