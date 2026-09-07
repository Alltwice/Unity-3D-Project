using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ProjectTools.AnimationPreview
{
    public static class PlayerMotionProfileBatchBaker
    {
        internal const string ProfilesPath = "Assets/Settings/Player/Motion/Profiles";
        private const string MotionRootPath = "Assets/Settings/Player/Motion";
        private const string MenuPath = "Tools/Animation Preview/Rebake All Player Motion Profiles";

        [MenuItem(MenuPath)]
        public static void RebakeAllPlayerMotionProfiles()
        {
            PlayerMotionProfileBatchBakeReport report = RebakeAll();
            LogReport(report);
        }

        internal static PlayerMotionProfileBatchBakeReport RebakeAll()
        {
            PlayerMotionProfileBatchBakeReport report = new PlayerMotionProfileBatchBakeReport();
            List<string> profilePaths = FindProfilePaths();
            report.DiscoveredCount = profilePaths.Count;

            List<StagedProfile> stagedProfiles = new List<StagedProfile>(profilePaths.Count);
            for (int index = 0; index < profilePaths.Count; index++)
            {
                string path = profilePaths[index];
                PlayerMotionProfile profile = AssetDatabase.LoadAssetAtPath<PlayerMotionProfile>(path);
                if (profile == null)
                {
                    AddError(report, path + ": 无法加载 PlayerMotionProfile。");
                    continue;
                }
                if (!TryResolveDetectionMode(profile, out PlayerFootPlantDetectionMode mode))
                {
                    AddError(report, path + ": DetectionMode 未持久化且无法按初次迁移规则推断。");
                    continue;
                }
                report.IncrementMode(mode);
                if (TryStage(profile, path, mode, report, out StagedProfile staged)) stagedProfiles.Add(staged);
            }
            if (report.Errors.Count > 0 || stagedProfiles.Count != profilePaths.Count) return report;

            List<ProfileSnapshot> snapshots = stagedProfiles.Select(staged => new ProfileSnapshot(staged.Profile, EditorJsonUtility.ToJson(staged.Profile))).ToList();
            try
            {
                for (int index = 0; index < stagedProfiles.Count; index++) PlayerMotionBaker.Apply(stagedProfiles[index].Profile, stagedProfiles[index].Payload, PlayerPlantMarkerMode.Auto);
                List<string> validationErrors = new List<string>();
                List<string> validationWarnings = new List<string>();
                ValidateAppliedProfiles(stagedProfiles, validationErrors, validationWarnings);
                ValidateMotionDefinitions(validationErrors);
                ValidateMotionCatalogs(validationErrors);
                ValidateAnimationSets(validationErrors);
                AddWarnings(report, validationWarnings);
                if (validationErrors.Count > 0)
                {
                    RestoreSnapshots(snapshots, report);
                    AddErrors(report, validationErrors);
                    return report;
                }
                for (int index = 0; index < stagedProfiles.Count; index++) EditorUtility.SetDirty(stagedProfiles[index].Profile);
                AssetDatabase.SaveAssets();
                report.Committed = true;
                return report;
            }
            catch (Exception exception)
            {
                RestoreSnapshots(snapshots, report);
                AddError(report, "批量 Rebake 在提交阶段失败，已恢复内存快照：" + exception.Message);
                return report;
            }
        }

        internal static List<string> FindProfilePaths()
        {
            return AssetDatabase.FindAssets("t:PlayerMotionProfile", new[] { ProfilesPath })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrEmpty(path) && path.StartsWith(ProfilesPath + "/", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static bool TryResolveDetectionMode(PlayerMotionProfile profile, out PlayerFootPlantDetectionMode mode)
        {
            mode = default;
            if (profile == null) return false;
            if (profile.HasPersistedDetectionMode)
            {
                if (!Enum.IsDefined(typeof(PlayerFootPlantDetectionMode), profile.FootPlantDetectionMode)) return false;
                mode = profile.FootPlantDetectionMode;
                return true;
            }
            return TryInferInitialDetectionMode(profile.name, out mode);
        }

        internal static bool TryInferInitialDetectionMode(string profileName, out PlayerFootPlantDetectionMode mode)
        {
            mode = default;
            if (string.IsNullOrEmpty(profileName)) return false;
            if (profileName.IndexOf("Loop", StringComparison.OrdinalIgnoreCase) >= 0) { mode = PlayerFootPlantDetectionMode.Loop; return true; }
            if (profileName.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0) { mode = PlayerFootPlantDetectionMode.Stop; return true; }
            if (profileName.IndexOf("Turn", StringComparison.OrdinalIgnoreCase) >= 0) { mode = PlayerFootPlantDetectionMode.Turn; return true; }
            if (profileName.IndexOf("Start", StringComparison.OrdinalIgnoreCase) >= 0 || profileName.IndexOf("Land", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                mode = PlayerFootPlantDetectionMode.Start;
                return true;
            }
            return false;
        }

        private static bool TryStage(PlayerMotionProfile profile, string profilePath, PlayerFootPlantDetectionMode mode, PlayerMotionProfileBatchBakeReport report, out StagedProfile staged)
        {
            staged = null;
            PlayerMotionProfileMetadata metadata = profile.EditorMetadata;
            if (metadata == null)
            {
                AddError(report, profilePath + ": 缺少 Bake 元数据。");
                return false;
            }
            if (metadata.SampleRate <= 0)
            {
                AddError(report, profilePath + ": Metadata SampleRate 无效，无法恢复原采样率。");
                return false;
            }
            string clipPath = AssetDatabase.GUIDToAssetPath(metadata.SourceClipGuid);
            AnimationClip clip = PlayerMotionBaker.ResolveSourceClip(profile);
            if (string.IsNullOrEmpty(clipPath) || clip == null)
            {
                AddError(report, profilePath + ": Source Clip 或 Local ID 已丢失。");
                return false;
            }
            string modelPath = AssetDatabase.GUIDToAssetPath(metadata.ModelGuid);
            GameObject model = string.IsNullOrEmpty(modelPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                AddError(report, profilePath + ": Model 已丢失。");
                return false;
            }
            string calibrationPath = AssetDatabase.GUIDToAssetPath(metadata.FootCalibrationGuid);
            PlayerFootCalibration calibration = string.IsNullOrEmpty(calibrationPath) ? null : AssetDatabase.LoadAssetAtPath<PlayerFootCalibration>(calibrationPath);
            if (calibration == null)
            {
                AddError(report, profilePath + ": Foot Calibration 已丢失。");
                return false;
            }
            List<string> calibrationErrors = new List<string>();
            if (!calibration.Validate(model, calibrationErrors))
            {
                AddErrors(report, calibrationErrors.Select(error => profilePath + ": " + error));
                return false;
            }
            try
            {
                using AnimationPreviewSession session = new AnimationPreviewSession();
                if (!session.SetModel(model))
                {
                    AddError(report, profilePath + ": " + (session.ModelError ?? "Model/Avatar 无效。"));
                    return false;
                }
                if (!session.SetClip(new AnimationPreviewClipEntry(clip, clipPath)))
                {
                    AddError(report, profilePath + ": " + (session.CompatibilityMessage ?? "AnimationClip 与 Model/Avatar 不兼容。"));
                    return false;
                }
                PlayerMotionBakePayload payload = PlayerMotionBaker.Build(session, metadata.SampleRate, mode, calibration);
                PlayerMotionProfile temporaryProfile = ScriptableObject.CreateInstance<PlayerMotionProfile>();
                temporaryProfile.name = profile.name;
                try
                {
                    PlayerMotionBaker.Apply(temporaryProfile, payload, PlayerPlantMarkerMode.Auto);
                    List<string> validationErrors = new List<string>();
                    List<string> validationWarnings = new List<string>();
                    if (!PlayerMotionBaker.Validate(temporaryProfile, validationErrors, validationWarnings))
                    {
                        AddErrors(report, validationErrors.Select(error => profilePath + ": " + error));
                        AddWarnings(report, validationWarnings);
                        return false;
                    }
                    AddWarnings(report, validationWarnings);
                    report.StagedCount++;
                    report.MarkerCount += payload.AutoPlantMarkers.Count;
                    report.LowConfidenceMarkerCount += payload.AutoPlantMarkers.Count(marker => marker.Confidence < PlayerFootPlantDetector.LowConfidenceThreshold);
                    if (payload.AutoPlantMarkers.Count == 0) report.NoMarkerProfileCount++;
                    staged = new StagedProfile(profile, payload);
                    return true;
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(temporaryProfile);
                }
            }
            catch (Exception exception)
            {
                AddError(report, profilePath + ": 预检/暂存失败：" + exception.Message);
                return false;
            }
        }

        private static void ValidateAppliedProfiles(IEnumerable<StagedProfile> stagedProfiles, ICollection<string> errors, ICollection<string> warnings)
        {
            foreach (StagedProfile staged in stagedProfiles) PlayerMotionBaker.Validate(staged.Profile, errors, warnings);
        }

        private static void ValidateMotionDefinitions(ICollection<string> errors)
        {
            string[] definitionGuids = AssetDatabase.FindAssets("t:PlayerMotionDefinition", new[] { MotionRootPath });
            foreach (string guid in definitionGuids.OrderBy(value => AssetDatabase.GUIDToAssetPath(value), StringComparer.OrdinalIgnoreCase))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                PlayerMotionDefinition definition = AssetDatabase.LoadAssetAtPath<PlayerMotionDefinition>(path);
                if (definition == null) { errors.Add(path + ": 无法加载 Motion Definition。"); continue; }
                List<string> definitionErrors = new List<string>();
                if (!definition.Validate(definitionErrors)) foreach (string error in definitionErrors) errors.Add(path + ": " + error);
            }
        }

        private static void ValidateMotionCatalogs(ICollection<string> errors)
        {
            string[] catalogGuids = AssetDatabase.FindAssets("t:PlayerMotionCatalog", new[] { MotionRootPath });
            foreach (string guid in catalogGuids.OrderBy(value => AssetDatabase.GUIDToAssetPath(value), StringComparer.OrdinalIgnoreCase))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                PlayerMotionCatalog catalog = AssetDatabase.LoadAssetAtPath<PlayerMotionCatalog>(path);
                if (catalog == null) { errors.Add(path + ": 无法加载 Motion Catalog。"); continue; }
                List<string> catalogErrors = new List<string>();
                if (!catalog.Validate(catalogErrors)) foreach (string error in catalogErrors) errors.Add(path + ": " + error);
            }
        }

        private static void ValidateAnimationSets(ICollection<string> errors)
        {
            string[] animationSetGuids = AssetDatabase.FindAssets("t:PlayerAnimationSet", new[] { MotionRootPath });
            foreach (string guid in animationSetGuids.OrderBy(value => AssetDatabase.GUIDToAssetPath(value), StringComparer.OrdinalIgnoreCase))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                UnityEngine.Object animationSet = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (animationSet == null) { errors.Add(path + ": 无法加载 PlayerAnimationSet。"); continue; }
                MethodInfo validate = animationSet.GetType().GetMethod("Validate", BindingFlags.Instance | BindingFlags.Public);
                if (validate == null) { errors.Add(path + ": PlayerAnimationSet 缺少 Validate 方法。"); continue; }
                try
                {
                    List<string> animationSetErrors = new List<string>();
                    bool valid = (bool)validate.Invoke(animationSet, new object[] { animationSetErrors });
                    if (!valid) foreach (string error in animationSetErrors) errors.Add(path + ": " + error);
                }
                catch (TargetInvocationException exception)
                {
                    errors.Add(path + ": PlayerAnimationSet 验证抛出异常：" + (exception.InnerException?.Message ?? exception.Message));
                }
            }
        }

        private static void RestoreSnapshots(IEnumerable<ProfileSnapshot> snapshots, PlayerMotionProfileBatchBakeReport report)
        {
            foreach (ProfileSnapshot snapshot in snapshots)
            {
                try
                {
                    EditorJsonUtility.FromJsonOverwrite(snapshot.Json, snapshot.Profile);
                    EditorUtility.ClearDirty(snapshot.Profile);
                }
                catch (Exception exception)
                {
                    AddError(report, snapshot.Profile.name + ": 恢复内存快照失败：" + exception.Message);
                }
            }
        }

        private static void LogReport(PlayerMotionProfileBatchBakeReport report)
        {
            string summary = "Player Motion Profile Rebake：处理 " + report.StagedCount + "/" + report.DiscoveredCount + "，提交=" + (report.Committed ? "成功" : "未提交") + "；Loop=" + report.GetModeCount(PlayerFootPlantDetectionMode.Loop) + "，Start=" + report.GetModeCount(PlayerFootPlantDetectionMode.Start) + "，Stop=" + report.GetModeCount(PlayerFootPlantDetectionMode.Stop) + "，Turn=" + report.GetModeCount(PlayerFootPlantDetectionMode.Turn) + "；Marker=" + report.MarkerCount + "，低置信度=" + report.LowConfidenceMarkerCount + "，无 Marker Profile=" + report.NoMarkerProfileCount + "，Warnings=" + report.Warnings.Count + "，Errors=" + report.Errors.Count;
            if (report.Errors.Count == 0) Debug.Log(summary);
            else Debug.LogError(summary + "\n" + string.Join("\n", report.Errors));
            if (report.Warnings.Count > 0) Debug.LogWarning(string.Join("\n", report.Warnings));
        }

        private static void AddError(PlayerMotionProfileBatchBakeReport report, string message)
        {
            AddUnique(report.Errors, message);
        }

        private static void AddErrors(PlayerMotionProfileBatchBakeReport report, IEnumerable<string> messages)
        {
            foreach (string message in messages) AddError(report, message);
        }

        private static void AddWarnings(PlayerMotionProfileBatchBakeReport report, IEnumerable<string> messages)
        {
            foreach (string message in messages) AddUnique(report.Warnings, message);
        }

        private static void AddUnique(ICollection<string> messages, string message)
        {
            if (!string.IsNullOrEmpty(message) && !messages.Contains(message)) messages.Add(message);
        }

        private sealed class StagedProfile
        {
            public StagedProfile(PlayerMotionProfile profile, PlayerMotionBakePayload payload)
            {
                Profile = profile;
                Payload = payload;
            }

            public PlayerMotionProfile Profile { get; }
            public PlayerMotionBakePayload Payload { get; }
        }

        private sealed class ProfileSnapshot
        {
            public ProfileSnapshot(PlayerMotionProfile profile, string json)
            {
                Profile = profile;
                Json = json;
            }

            public PlayerMotionProfile Profile { get; }
            public string Json { get; }
        }
    }

    internal sealed class PlayerMotionProfileBatchBakeReport
    {
        private readonly Dictionary<PlayerFootPlantDetectionMode, int> modeCounts = new Dictionary<PlayerFootPlantDetectionMode, int>();

        public int DiscoveredCount { get; internal set; }
        public int StagedCount { get; internal set; }
        public int MarkerCount { get; internal set; }
        public int LowConfidenceMarkerCount { get; internal set; }
        public int NoMarkerProfileCount { get; internal set; }
        public bool Committed { get; internal set; }
        public List<string> Warnings { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();

        internal void IncrementMode(PlayerFootPlantDetectionMode mode)
        {
            modeCounts[mode] = GetModeCount(mode) + 1;
        }

        internal int GetModeCount(PlayerFootPlantDetectionMode mode)
        {
            return modeCounts.TryGetValue(mode, out int count) ? count : 0;
        }
    }
}
