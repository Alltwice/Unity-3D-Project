using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class PlayerMotionContractTests
{
    [Test]
    public void EntryHandoffMapsConfiguredRangeToZeroAndOne()
    {
        PlayerMotionDefinition definition = CreateDefinition(out PlayerMotionProfile profile);
        definition.ConfigureEntryHandoff(0.2f);
        Assert.That(definition.CalculateEntryHandoffProgress(0f), Is.Zero);
        Assert.That(definition.CalculateEntryHandoffProgress(0.1f), Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(definition.CalculateEntryHandoffProgress(0.2f), Is.EqualTo(1f).Within(0.0001f));
        Assert.That(definition.EvaluateEntryTranslationWeight(0f), Is.Zero);
        Assert.That(definition.EvaluateEntryTranslationWeight(0.2f), Is.EqualTo(1f).Within(0.0001f));
        Destroy(definition, profile);
    }

    [Test]
    public void DefinitionRejectsOverlappingHandoffRanges()
    {
        PlayerMotionDefinition definition = CreateDefinition(out PlayerMotionProfile profile, 0.7f, 1f);
        definition.ConfigureEntryHandoff(0.8f);
        List<string> errors = new List<string>();
        Assert.That(definition.Validate(errors), Is.False);
        Assert.That(errors.Exists(error => error.Contains("Handoff")), Is.True);
        Destroy(definition, profile);
    }

    [Test]
    public void DefinitionRejectsTransitionLockOutsideNormalizedRange()
    {
        PlayerMotionDefinition definition = CreateDefinition(out PlayerMotionProfile profile, 0.8f, 1f, -0.1f);
        List<string> errors = new List<string>();
        Assert.That(definition.Validate(errors), Is.False);
        Assert.That(errors.Exists(error => error.Contains("TransitionLockEndProgress")), Is.True);
        Destroy(definition, profile);
    }

    [TestCase(0.25f, PlayerFoot.Left)]
    [TestCase(0.499f, PlayerFoot.Left)]
    [TestCase(0.5f, PlayerFoot.Right)]
    [TestCase(0.75f, PlayerFoot.Right)]
    public void EntryFootUsesConfiguredPhaseThreshold(float stepProgress, PlayerFoot expected)
    {
        PlayerMotionDefinition definition = CreateDefinition(out PlayerMotionProfile profile);
        PlayerMotionProfile left = CreateProfile(1f);
        PlayerMotionProfile right = CreateProfile(1f);
        left.ReplacePlantMarkers(new[] { new PlayerFootPlantMarker(PlayerFoot.Right, 0.2f, 1f) }, PlayerMotionProfile.CurrentFootPlantDetectionVersion);
        right.ReplacePlantMarkers(new[] { new PlayerFootPlantMarker(PlayerFoot.Left, 0.2f, 1f) }, PlayerMotionProfile.CurrentFootPlantDetectionVersion);
        definition.ConfigureFootProfiles(left, right, true);
        SerializedObject serialized = new SerializedObject(definition);
        serialized.FindProperty("usePhaseFootSelection").boolValue = true;
        serialized.FindProperty("nextPlantFootThreshold").floatValue = 0.5f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        PlayerLocomotionPhaseSnapshot phase = new PlayerLocomotionPhaseSnapshot(true, true, PlayerLocomotionMode.Walk, PlayerFoot.Right, 0f, PlayerFoot.Left, PlayerFoot.Right, stepProgress);
        Assert.That(definition.ResolveEntryFoot(phase), Is.EqualTo(expected));
        Destroy(definition, profile, left, right);
    }

    [Test]
    public void ComposerUsesAuthoredDisplacementAtFullAuthorityAndVelocityAtZeroAuthority()
    {
        PlayerMotionDefinition definition = CreateDefinition(out PlayerMotionProfile profile);
        PlayerMovementConfig config = ScriptableObject.CreateInstance<PlayerMovementConfig>();
        PlayerGameplayIntent intent = PlayerGameplayIntent.Create(Vector3.forward, Vector3.forward);
        intent.LocomotionMode = PlayerLocomotionMode.Run;
        PlayerMotorResult result = MotorResult(Vector3.forward * 2f);
        PlayerMotorCommand authored = PlayerMotionComposer.Compose(intent, new PlayerMotionFrame(definition, Vector3.forward, 0f, 0f, 0f, 0f, 1f), result, config, 0.1f, Vector3.forward);
        PlayerMotorCommand locomotion = PlayerMotionComposer.Compose(intent, new PlayerMotionFrame(definition, Vector3.forward, 0f, 0f, 0f, 0f, 0f), result, config, 0.1f, Vector3.forward);
        Assert.That(authored.TranslationMode, Is.EqualTo(PlayerMotorTranslationMode.DisplacementDriven));
        Assert.That(authored.PlanarDisplacement.z, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(locomotion.TranslationMode, Is.EqualTo(PlayerMotorTranslationMode.VelocityDriven));
        Destroy(config, definition, profile);
    }

    [Test]
    public void ComposerCombinesEntrySourceAuthoredMotionAndTargetLocomotion()
    {
        PlayerMotionDefinition definition = CreateDefinition(out PlayerMotionProfile profile);
        PlayerMovementConfig config = ScriptableObject.CreateInstance<PlayerMovementConfig>();
        PlayerGameplayIntent intent = PlayerGameplayIntent.Create(Vector3.forward, Vector3.forward);
        intent.LocomotionMode = PlayerLocomotionMode.Run;
        PlayerMotionFrame frame = new PlayerMotionFrame(definition, profile, PlayerFoot.Left, Vector3.forward * 4f, 0f, 0f, 0f, 0.5f, 0.25f, true, 0.5f, Vector3.forward * 2f);
        PlayerMotorCommand command = PlayerMotionComposer.Compose(intent, frame, MotorResult(Vector3.zero), config, 1f, Vector3.forward);
        float expected = 2f * 0.5f + 4f * 0.5f * 0.25f + config.Locomotion.RunSpeed * 0.5f * 0.75f;
        Assert.That(command.PlanarDisplacement.z, Is.EqualTo(expected).Within(0.0001f));
        Assert.That(command.TranslationMode, Is.EqualTo(PlayerMotorTranslationMode.DisplacementDriven));
        Destroy(config, definition, profile);
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(120)]
    public void ZeroLengthExitHandoffPreservesTotalAuthoredDistance(int fps)
    {
        PlayerMotionDefinition definition = CreateDefinition(out PlayerMotionProfile profile, 1f, 1f);
        PlayerMovementConfig config = ScriptableObject.CreateInstance<PlayerMovementConfig>();
        PlayerMotionRuntime runtime = new PlayerMotionRuntime();
        PlayerGameplayIntent intent = PlayerGameplayIntent.Create(Vector3.forward, Vector3.forward);
        intent.LocomotionMode = PlayerLocomotionMode.Idle;
        runtime.Begin(definition, Vector3.forward, Vector3.forward);
        Vector3 total = Vector3.zero;
        int guard = fps * 3;
        while (!runtime.Snapshot.JustCompleted && guard-- > 0)
        {
            PlayerMotionFrame frame = runtime.Advance(1f / fps, intent);
            total += PlayerMotionComposer.Compose(intent, frame, MotorResult(Vector3.zero), config, 1f / fps, Vector3.forward).PlanarDisplacement;
        }
        Assert.That(total.z, Is.EqualTo(profile.EvaluateTravelDistance(1f)).Within(0.0001f));
        Destroy(config, definition, profile);
    }

    [Test]
    public void MotorKinematicsUsesActualDisplacement()
    {
        Vector3 velocity = PlayerMotorKinematics.CalculateActualPlanarVelocity(new Vector3(0.2f, 0f, 0f), 0.1f);
        Assert.That(velocity.x, Is.EqualTo(2f).Within(0.0001f));
    }

    [Test]
    public void ProfileResolvesMostRecentPlantForFiniteMotion()
    {
        PlayerMotionProfile profile = CreateProfile(2f);
        profile.ReplacePlantMarkers(new[]
        {
            new PlayerFootPlantMarker(PlayerFoot.Left, 0.2f, 1f),
            new PlayerFootPlantMarker(PlayerFoot.Right, 0.6f, 1f),
            new PlayerFootPlantMarker(PlayerFoot.Left, 0.85f, 1f)
        }, PlayerMotionProfile.CurrentFootPlantDetectionVersion);
        Assert.That(profile.ResolveLastPlantFoot(0.1f, PlayerFoot.Unknown), Is.EqualTo(PlayerFoot.Unknown));
        Assert.That(profile.ResolveLastPlantFoot(0.6f, PlayerFoot.Left), Is.EqualTo(PlayerFoot.Right));
        Assert.That(profile.ResolveLastPlantFoot(1f, PlayerFoot.Right), Is.EqualTo(PlayerFoot.Left));
        Destroy(profile);
    }

    [Test]
    public void ProfileEvaluatesLoopPhaseAcrossMarkerAndSeam()
    {
        PlayerMotionProfile profile = CreateProfile(2f);
        profile.SetPlantAuthoringSettings(PlayerFootPlantDetectionMode.Loop, PlayerPlantMarkerMode.ManualOverride);
        profile.ReplacePlantMarkers(new[]
        {
            new PlayerFootPlantMarker(PlayerFoot.Left, 0.25f, 1f),
            new PlayerFootPlantMarker(PlayerFoot.Right, 0.75f, 1f)
        }, PlayerMotionProfile.CurrentFootPlantDetectionVersion);
        Assert.That(profile.TryEvaluateLoopPhase(0.5f, out PlayerFoot lastFoot, out PlayerFoot nextFoot, out float stepProgress), Is.True);
        Assert.That(lastFoot, Is.EqualTo(PlayerFoot.Left));
        Assert.That(nextFoot, Is.EqualTo(PlayerFoot.Right));
        Assert.That(stepProgress, Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(profile.TryEvaluateLoopPhase(0f, out lastFoot, out nextFoot, out stepProgress), Is.True);
        Assert.That(lastFoot, Is.EqualTo(PlayerFoot.Right));
        Assert.That(nextFoot, Is.EqualTo(PlayerFoot.Left));
        Assert.That(stepProgress, Is.EqualTo(0.5f).Within(0.0001f));
        Destroy(profile);
    }

    [Test]
    public void ProfileRejectsInvalidLoopInputsAndMarkers()
    {
        PlayerMotionProfile profile = CreateProfile(2f);
        profile.SetPlantAuthoringSettings(PlayerFootPlantDetectionMode.Loop, PlayerPlantMarkerMode.ManualOverride);
        profile.ReplacePlantMarkers(new[]
        {
            new PlayerFootPlantMarker(PlayerFoot.Left, 0.25f, 1f),
            new PlayerFootPlantMarker(PlayerFoot.Left, 0.75f, 1f)
        }, PlayerMotionProfile.CurrentFootPlantDetectionVersion);
        Assert.That(profile.TryEvaluateLoopPhase(float.NaN, out _, out _, out _), Is.False);
        Assert.That(profile.TryEvaluateLoopPhase(0.5f, out _, out _, out _), Is.False);
        Assert.That(profile.ValidateLoopPhase(new List<string>()), Is.False);
        Destroy(profile);
    }

    [Test]
    public void DefaultCatalogContainsRequiredModesAndValidData()
    {
        PlayerMotionCatalog catalog = AssetDatabase.LoadAssetAtPath<PlayerMotionCatalog>("Assets/Settings/Player/Motion/DefaultPlayerMotionCatalog.asset");
        Assert.That(catalog, Is.Not.Null);
        Assert.That(catalog.TryGetCycle(PlayerLocomotionMode.Walk, out _), Is.True);
        Assert.That(catalog.TryGetCycle(PlayerLocomotionMode.Run, out _), Is.True);
        Assert.That(catalog.TryGetCycle(PlayerLocomotionMode.FastRun, out _), Is.True);
        List<string> errors = new List<string>();
        Assert.That(catalog.Validate(errors), Is.True, string.Join("\n", errors));
    }

    private static PlayerMotionDefinition CreateDefinition(out PlayerMotionProfile profile, float exitStart = 0.8f, float exitEnd = 1f, float transitionLockEnd = 0f)
    {
        profile = CreateProfile(2f);
        PlayerMotionDefinition definition = ScriptableObject.CreateInstance<PlayerMotionDefinition>();
        definition.Configure(profile, PlayerMotionTranslationPolicy.TravelAlongCapturedDirection, PlayerMotionRotationPolicy.FaceDirection, PlayerMotionBasisPolicy.DesiredDirection, 0f, 1f, exitStart, exitEnd, true, transitionLockEnd);
        return definition;
    }

    private static PlayerMotionProfile CreateProfile(float travelDistance)
    {
        PlayerMotionProfile profile = ScriptableObject.CreateInstance<PlayerMotionProfile>();
        profile.SetBakedData(1f, 2, new[] { Vector2.zero, new Vector2(0f, travelDistance * 0.5f), new Vector2(0f, travelDistance) }, new[] { 0f, travelDistance * 0.5f, travelDistance }, new[] { 0f, 0f, 0f }, string.Empty, 0, string.Empty, string.Empty);
        return profile;
    }

    private static PlayerMotorResult MotorResult(Vector3 horizontalVelocity)
    {
        return new PlayerMotorResult(Vector3.zero, Vector3.zero, horizontalVelocity, 0f, true, false, 0f, CollisionFlags.None);
    }

    private static void Destroy(params Object[] objects)
    {
        for (int i = 0; i < objects.Length; i++) Object.DestroyImmediate(objects[i]);
    }
}
