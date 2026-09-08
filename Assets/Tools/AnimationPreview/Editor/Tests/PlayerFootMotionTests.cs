using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ProjectTools.AnimationPreview;

public class PlayerFootMotionTests
{
        [Test]
        public void ClipSamplingIsStableAfterPreviewTimeChanges()
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Model/X Bot.fbx");
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath("Assets/Animation/Player/Walk/Walk_Lfoot.fbx").OfType<AnimationClip>().First(candidate => !candidate.name.StartsWith("__preview__", StringComparison.Ordinal));
            PlayerFootCalibration calibration = AssetDatabase.LoadAssetAtPath<PlayerFootCalibration>("Assets/Settings/Player/Motion/FootCalibration/XBotFootCalibration.asset");
            using AnimationPreviewSession session = new AnimationPreviewSession();
            Assert.That(session.SetModel(model), Is.True);
            Assert.That(session.SetClip(new AnimationPreviewClipEntry(clip, AssetDatabase.GetAssetPath(clip))), Is.True);
            PlayerMotionBakeResult initial = session.SampleMotion(60, calibration);
            session.SetTime(session.Length);
            PlayerMotionBakeResult afterTimeChange = session.SampleMotion(60, calibration);
            Assert.That(Vector3.Distance(initial.LeftFootPositions[0], afterTimeChange.LeftFootPositions[0]), Is.LessThan(0.001f));
            Assert.That(Vector3.Distance(initial.LeftFootPositions[initial.LeftFootPositions.Length - 1], afterTimeChange.LeftFootPositions[afterTimeChange.LeftFootPositions.Length - 1]), Is.LessThan(0.001f));
        }

        [Test]
        public void SampleFootMotionProducesThreeFiniteEqualLengthArrays()
        {
            PlayerFootCalibration calibration = CreateCalibration();
            Vector3[] positions = new Vector3[36];
            for (int index = 0; index < positions.Length; index++) positions[index] = new Vector3(index * 0.01f, Mathf.Sin(index * 0.2f) * 0.03f, index * 0.005f);
            PlayerFootMotionBakeData data = PlayerMotionBaker.SampleFootMotion(positions, 60, calibration);
            Assert.That(data.SoleHeight, Is.Not.Null);
            Assert.That(data.VerticalSpeed, Is.Not.Null);
            Assert.That(data.HorizontalSpeed, Is.Not.Null);
            Assert.That(data.SoleHeight.Length, Is.EqualTo(positions.Length));
            Assert.That(data.VerticalSpeed.Length, Is.EqualTo(positions.Length));
            Assert.That(data.HorizontalSpeed.Length, Is.EqualTo(positions.Length));
            Assert.That(data.SoleHeight.All(IsFinite), Is.True);
            Assert.That(data.VerticalSpeed.All(IsFinite), Is.True);
            Assert.That(data.HorizontalSpeed.All(IsFinite), Is.True);
            UnityEngine.Object.DestroyImmediate(calibration);
        }

        [Test]
        public void SampleFootMotionUsesActualClipDuration()
        {
            PlayerFootCalibration calibration = CreateCalibration();
            Vector3[] positions = { Vector3.zero, Vector3.right, Vector3.right * 2f };
            PlayerFootMotionBakeData data = PlayerMotionBaker.SampleFootMotion(positions, 2f, calibration);
            Assert.That(data.HorizontalSpeed, Is.All.EqualTo(1f).Within(0.0001f));
            UnityEngine.Object.DestroyImmediate(calibration);
        }

        [Test]
        public void AutoApplyReplacesExistingMarkersAndPreservesPayloadSampleRate()
        {
            PlayerMotionProfile profile = CreateProfile(1f, 3);
            profile.ReplacePlantMarkers(new[] { new PlayerFootPlantMarker(PlayerFoot.Right, 0.8f, 1f) }, 0);
            PlayerMotionBakePayload payload = new PlayerMotionBakePayload(1f, 30, CreatePositions(3), CreateFloats(3), CreateFloats(3), CreateFootData(3), CreateFootData(3), "clip", 1L, "model", "dependency", "calibration", "settings", PlayerFootPlantDetectionMode.Turn, new List<PlayerFootPlantMarker> { new PlayerFootPlantMarker(PlayerFoot.Left, 0.25f, 0.6f) });
            PlayerMotionBaker.Apply(profile, payload, PlayerPlantMarkerMode.Auto);
            Assert.That(profile.SampleRate, Is.EqualTo(30));
            Assert.That(profile.PlantMarkerMode, Is.EqualTo(PlayerPlantMarkerMode.Auto));
            Assert.That(profile.FootPlantDetectionMode, Is.EqualTo(PlayerFootPlantDetectionMode.Turn));
            Assert.That(profile.FootPlantDetectionVersion, Is.EqualTo(PlayerMotionProfile.CurrentFootPlantDetectionVersion));
            Assert.That(profile.PlantMarkers.Count, Is.EqualTo(1));
            Assert.That(profile.PlantMarkers[0].Foot, Is.EqualTo(PlayerFoot.Left));
            Assert.That(profile.PlantMarkers[0].NormalizedTime, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(profile.PlantMarkers[0].Confidence, Is.EqualTo(0.6f).Within(0.0001f));
            UnityEngine.Object.DestroyImmediate(profile);
        }

        [Test]
        public void AutoValidationReportsLowConfidenceAsWarning()
        {
            PlayerMotionProfile profile = CreateProfile(1f, 11);
            profile.SetPlantAuthoringSettings(PlayerFootPlantDetectionMode.Start, PlayerPlantMarkerMode.Auto);
            profile.SetBakedData(1f, 60, CreatePositions(11), CreateFloats(11), CreateFloats(11), "missingClip", 1L, "missingModel", "dependency", CreateFootData(), CreateFootData(), "missingCalibration", "settings");
            profile.ReplacePlantMarkers(new[] { new PlayerFootPlantMarker(PlayerFoot.Left, 0.4f, 0.5f) }, PlayerMotionProfile.CurrentFootPlantDetectionVersion);
            List<string> errors = new List<string>();
            List<string> warnings = new List<string>();
            Assert.That(PlayerMotionBaker.Validate(profile, errors, warnings), Is.False);
            Assert.That(warnings.Any(message => message.Contains("Confidence")), Is.True);
            UnityEngine.Object.DestroyImmediate(profile);
        }

        [Test]
        public void ManualValidationIgnoresLegacyMarkerConfidence()
        {
            PlayerMotionProfile profile = CreateProfile(1f, 11);
            profile.ReplacePlantMarkers(new[] { new PlayerFootPlantMarker(PlayerFoot.Left, 0.4f, 0f) }, 0);
            List<string> warnings = new List<string>();
            PlayerMotionBaker.Validate(profile, new List<string>(), warnings);
            Assert.That(warnings, Is.Empty);
            UnityEngine.Object.DestroyImmediate(profile);
        }

        [Test]
        public void EmptyNonLoopAutoResultIsWarningOnly()
        {
            PlayerMotionProfile source = AssetDatabase.LoadAssetAtPath<PlayerMotionProfile>("Assets/Settings/Player/Motion/Profiles/RunStopRightFootMotionProfile.asset");
            Assert.That(source, Is.Not.Null);
            PlayerMotionProfile profile = ScriptableObject.CreateInstance<PlayerMotionProfile>();
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(source), profile);
            profile.SetPlantAuthoringSettings(PlayerFootPlantDetectionMode.Start, PlayerPlantMarkerMode.Auto);
            profile.ReplacePlantMarkers(Array.Empty<PlayerFootPlantMarker>(), PlayerMotionProfile.CurrentFootPlantDetectionVersion);
            List<string> errors = new List<string>();
            List<string> warnings = new List<string>();
            Assert.That(PlayerMotionBaker.Validate(profile, errors, warnings), Is.True, string.Join("\n", errors));
            Assert.That(warnings.Any(message => message.Contains("未检测到 Plant Marker")), Is.True);
            UnityEngine.Object.DestroyImmediate(profile);
        }

        [Test]
        public void StartDetectionSuppressesInitialContactAndEmitsSwingToContact()
        {
            float[] leftHeight = { 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f, 1f, 0f, 0f, 0f, 0f, 0f, 0f };
            List<PlayerFootPlantDetection> detections = PlayerFootPlantDetector.Detect(CreateDetectorFootData(leftHeight), CreateDetectorFootData(Constant(leftHeight.Length, 1f)), 0.28f, leftHeight.Length, PlayerFootPlantDetectionMode.Start);
            Assert.That(detections.Count, Is.EqualTo(1));
            Assert.That(detections[0].Foot, Is.EqualTo(PlayerFoot.Left));
            Assert.That(detections[0].NormalizedTime, Is.GreaterThan(0.5f));
        }

        [Test]
        public void StopDetectionKeepsTerminalContactWindow()
        {
            float[] leftHeight = { 1f, 1f, 1f, 1f, 1f, 0f, 0f, 0f, 0f, 0f };
            List<PlayerFootPlantDetection> detections = PlayerFootPlantDetector.Detect(CreateDetectorFootData(leftHeight), CreateDetectorFootData(Constant(leftHeight.Length, 1f)), 0.18f, leftHeight.Length, PlayerFootPlantDetectionMode.Stop);
            Assert.That(detections.Count, Is.EqualTo(1));
            Assert.That(detections[0].Foot, Is.EqualTo(PlayerFoot.Left));
        }

        [Test]
        public void LoopDetectionMergesSeamAndProducesAlternatingFeet()
        {
            float[] leftHeight = { 0f, 0f, 0f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 0f, 0f, 0f, 0f, 0f, 0f };
            float[] rightHeight = { 1f, 1f, 1f, 1f, 1f, 1f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };
            List<PlayerFootPlantDetection> detections = PlayerFootPlantDetector.Detect(CreateDetectorFootData(leftHeight), CreateDetectorFootData(rightHeight), 0.4f, leftHeight.Length, PlayerFootPlantDetectionMode.Loop);
            Assert.That(detections.Count, Is.EqualTo(2));
            Assert.That(detections[0].Foot, Is.Not.EqualTo(detections[1].Foot));
            Assert.That(detections.All(detection => detection.NormalizedTime > 0f && detection.NormalizedTime < 1f), Is.True);
        }

        [Test]
        public void TurnDetectionReducesHorizontalSpeedPenalty()
        {
            float[] height = { 1f, 1f, 1f, 1f, 1f, 0f, 0f, 0f, 0f, 0f };
            float[] horizontal = { 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f, 1f };
            PlayerFootMotionBakeData left = CreateDetectorFootData(height, null, horizontal);
            PlayerFootMotionBakeData right = CreateDetectorFootData(Constant(height.Length, 1f));
            Assert.That(PlayerFootPlantDetector.Detect(left, right, 0.18f, height.Length, PlayerFootPlantDetectionMode.Start), Is.Empty);
            Assert.That(PlayerFootPlantDetector.Detect(left, right, 0.18f, height.Length, PlayerFootPlantDetectionMode.Turn).Count, Is.EqualTo(1));
        }

        [Test]
        public void VerticalSpeedSignDoesNotChangeDetection()
        {
            float[] height = { 1f, 1f, 1f, 1f, 1f, 0f, 0f, 0f, 0f, 0f };
            float[] vertical = { 1f, 1f, 1f, 1f, 1f, 0f, 0f, 0f, 0f, 0f };
            PlayerFootMotionBakeData right = CreateDetectorFootData(Constant(height.Length, 1f));
            List<PlayerFootPlantDetection> positive = PlayerFootPlantDetector.Detect(CreateDetectorFootData(height, vertical), right, 0.18f, height.Length, PlayerFootPlantDetectionMode.Start);
            List<PlayerFootPlantDetection> negative = PlayerFootPlantDetector.Detect(CreateDetectorFootData(height, vertical.Select(value => -value).ToArray()), right, 0.18f, height.Length, PlayerFootPlantDetectionMode.Start);
            Assert.That(negative.Select(value => value.NormalizedTime), Is.EqualTo(positive.Select(value => value.NormalizedTime)).Within(0.0001f));
        }

        [Test]
        public void ShortContactScoreGapDoesNotCreateDuplicatePlant()
        {
            float[] height = { 1f, 1f, 1f, 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f };
            List<PlayerFootPlantDetection> detections = PlayerFootPlantDetector.Detect(CreateDetectorFootData(height), CreateDetectorFootData(Constant(height.Length, 1f)), 0.22f, height.Length, PlayerFootPlantDetectionMode.Start);
            Assert.That(detections.Count, Is.EqualTo(1));
        }

        [Test]
        public void PlantMarkersAreSortedAndPreserveRepeatedFootMarkers()
        {
            PlayerMotionProfile profile = CreateProfile(1f, 11);
            Assert.That(PlayerFootPlantMarkerEditor.TryAdd(profile, PlayerFoot.Right, 0.8f), Is.True);
            Assert.That(PlayerFootPlantMarkerEditor.TryAdd(profile, PlayerFoot.Left, 0.6f), Is.True);
            Assert.That(PlayerFootPlantMarkerEditor.TryAdd(profile, PlayerFoot.Right, 0.89f), Is.True);
            Assert.That(PlayerFootPlantMarkerEditor.TryAdd(profile, PlayerFoot.Left, 0.55f), Is.True);
            List<PlayerFootPlantMarkerEditor.MarkerValue> markers = PlayerFootPlantMarkerEditor.Read(profile);
            Assert.That(markers.Count, Is.EqualTo(4));
            Assert.That(markers[0].Foot, Is.EqualTo(PlayerFoot.Left));
            Assert.That(markers[0].NormalizedTime, Is.EqualTo(0.55f).Within(0.0001f));
            Assert.That(markers[1].Foot, Is.EqualTo(PlayerFoot.Left));
            Assert.That(markers[1].NormalizedTime, Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(markers[2].Foot, Is.EqualTo(PlayerFoot.Right));
            Assert.That(markers[2].NormalizedTime, Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(markers[3].Foot, Is.EqualTo(PlayerFoot.Right));
            Assert.That(markers[3].NormalizedTime, Is.EqualTo(0.89f).Within(0.0001f));
            Assert.That(markers.All(marker => Mathf.Approximately(marker.Confidence, 1f)), Is.True);
            UnityEngine.Object.DestroyImmediate(profile);
        }

        [Test]
        public void DeletePlantRemovesNearestMarkerOnlyWithinOneSampleInterval()
        {
            PlayerMotionProfile profile = CreateProfile(1f, 21);
            PlayerFootPlantMarkerEditor.TryAdd(profile, PlayerFoot.Left, 0.2f);
            PlayerFootPlantMarkerEditor.TryAdd(profile, PlayerFoot.Right, 0.5f);
            PlayerFootPlantMarkerEditor.TryAdd(profile, PlayerFoot.Left, 0.8f);
            Assert.That(PlayerFootPlantMarkerEditor.TryRemoveNearest(profile, 0.52f, out PlayerFoot removedFoot), Is.True);
            Assert.That(removedFoot, Is.EqualTo(PlayerFoot.Right));
            Assert.That(PlayerFootPlantMarkerEditor.Read(profile).Count, Is.EqualTo(2));
            Assert.That(PlayerFootPlantMarkerEditor.TryRemoveNearest(profile, 0.95f, out _), Is.False);
            Assert.That(PlayerFootPlantMarkerEditor.Read(profile).Count, Is.EqualTo(2));
            UnityEngine.Object.DestroyImmediate(profile);
        }

        [Test]
        public void RebakePreservesPlantMarkers()
        {
            PlayerMotionProfile profile = CreateProfile(1f, 11);
            PlayerFootPlantMarkerEditor.TryAdd(profile, PlayerFoot.Left, 0.2f);
            PlayerFootPlantMarkerEditor.TryAdd(profile, PlayerFoot.Right, 0.8f);
            List<PlayerFootPlantMarkerEditor.MarkerValue> before = PlayerFootPlantMarkerEditor.Read(profile);
            profile.SetBakedData(1f, 60, CreatePositions(11), CreateFloats(11), CreateFloats(11), "clip", 1L, "model", "dependency", CreateFootData(), CreateFootData(), "calibration", "settings");
            List<PlayerFootPlantMarkerEditor.MarkerValue> after = PlayerFootPlantMarkerEditor.Read(profile);
            Assert.That(after.Select(marker => marker.Foot), Is.EqualTo(before.Select(marker => marker.Foot)));
            Assert.That(after.Select(marker => marker.NormalizedTime), Is.EqualTo(before.Select(marker => marker.NormalizedTime)));
            UnityEngine.Object.DestroyImmediate(profile);
        }

        [Test]
        public void BatchSourceResolutionFailureDoesNotCommitProfiles()
        {
            PlayerMotionProfile target = AssetDatabase.LoadAssetAtPath<PlayerMotionProfile>("Assets/Settings/Player/Motion/Profiles/RunStopRightFootMotionProfile.asset");
            PlayerMotionProfile unaffected = AssetDatabase.LoadAssetAtPath<PlayerMotionProfile>("Assets/Settings/Player/Motion/Profiles/WalkStopRightFootMotionProfile.asset");
            Assert.That(target, Is.Not.Null);
            Assert.That(unaffected, Is.Not.Null);
            string targetBefore = EditorJsonUtility.ToJson(target);
            string unaffectedBefore = EditorJsonUtility.ToJson(unaffected);
            SerializedObject serialized = new SerializedObject(target);
            serialized.Update();
            serialized.FindProperty("editorMetadata").FindPropertyRelative("sourceClipGuid").stringValue = "missing-source-guid";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            string invalidTarget = EditorJsonUtility.ToJson(target);
            try
            {
                PlayerMotionProfileBatchBakeReport report = PlayerMotionProfileBatchBaker.RebakeAll();
                Assert.That(report.Committed, Is.False);
                Assert.That(report.Errors.Any(error => error.Contains("Source Clip")), Is.True);
                Assert.That(EditorJsonUtility.ToJson(target), Is.EqualTo(invalidTarget));
                Assert.That(EditorJsonUtility.ToJson(unaffected), Is.EqualTo(unaffectedBefore));
            }
            finally
            {
                EditorJsonUtility.FromJsonOverwrite(targetBefore, target);
                EditorUtility.ClearDirty(target);
            }
        }

        [Test]
        public void BatchDefinitionValidationFailureDoesNotCommitProfiles()
        {
            PlayerMotionDefinition definition = AssetDatabase.LoadAssetAtPath<PlayerMotionDefinition>("Assets/Settings/Player/Motion/Definitions/RunToIdleDefinition.asset");
            PlayerMotionProfile unaffected = AssetDatabase.LoadAssetAtPath<PlayerMotionProfile>("Assets/Settings/Player/Motion/Profiles/WalkStopRightFootMotionProfile.asset");
            Assert.That(definition, Is.Not.Null);
            Assert.That(unaffected, Is.Not.Null);
            string definitionBefore = EditorJsonUtility.ToJson(definition);
            string unaffectedBefore = EditorJsonUtility.ToJson(unaffected);
            SerializedObject serialized = new SerializedObject(definition);
            serialized.Update();
            serialized.FindProperty("translationScale").floatValue = float.NaN;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            try
            {
                PlayerMotionProfileBatchBakeReport report = PlayerMotionProfileBatchBaker.RebakeAll();
                Assert.That(report.Committed, Is.False);
                Assert.That(report.Errors.Any(error => error.Contains("TranslationScale")), Is.True);
                Assert.That(EditorJsonUtility.ToJson(unaffected), Is.EqualTo(unaffectedBefore));
            }
            finally
            {
                EditorJsonUtility.FromJsonOverwrite(definitionBefore, definition);
                EditorUtility.ClearDirty(definition);
            }
        }

        private static PlayerFootCalibration CreateCalibration()
        {
            PlayerFootCalibration calibration = ScriptableObject.CreateInstance<PlayerFootCalibration>();
            calibration.Configure(null, Vector3.zero, Vector3.zero, 0f);
            return calibration;
        }

        private static PlayerMotionProfile CreateProfile(float duration, int sampleCount = 3)
        {
            PlayerMotionProfile profile = ScriptableObject.CreateInstance<PlayerMotionProfile>();
            profile.SetBakedData(duration, 60, CreatePositions(sampleCount), CreateFloats(sampleCount), CreateFloats(sampleCount), "clip", 1L, "model", "dependency");
            return profile;
        }

        private static Vector2[] CreatePositions(int count = 3)
        {
            return Enumerable.Range(0, count).Select(index => new Vector2(index, 0f)).ToArray();
        }

        private static float[] CreateFloats(int count = 3)
        {
            return Enumerable.Range(0, count).Select(index => index / (float)Math.Max(1, count - 1)).ToArray();
        }

        private static PlayerFootMotionBakeData CreateFootData(int count = 11)
        {
            return new PlayerFootMotionBakeData
            {
                SoleHeight = CreateFloats(count),
                VerticalSpeed = CreateFloats(count),
                HorizontalSpeed = CreateFloats(count)
            };
        }

        private static PlayerFootMotionBakeData CreateDetectorFootData(float[] heights, float[] vertical = null, float[] horizontal = null)
        {
            return new PlayerFootMotionBakeData
            {
                SoleHeight = heights,
                VerticalSpeed = vertical ?? Constant(heights.Length, 0f),
                HorizontalSpeed = horizontal ?? Constant(heights.Length, 0f)
            };
        }

        private static float[] Constant(int count, float value)
        {
            return Enumerable.Repeat(value, count).ToArray();
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
