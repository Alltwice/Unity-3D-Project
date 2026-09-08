using NUnit.Framework;
using UnityEngine;

public class PlayerMotionRuntimeTests
{
    [TestCase(30)]
    [TestCase(60)]
    [TestCase(120)]
    public void TotalDisplacementIsFrameRateIndependent(int fps)
    {
        PlayerMotionDefinition definition = CreateDefinition(out PlayerMotionProfile profile);
        PlayerMotionRuntime runtime = new PlayerMotionRuntime();
        runtime.Begin(definition, Vector3.forward);
        Vector3 total = Vector3.zero;
        int guard = fps * 3;
        while (!runtime.Snapshot.JustCompleted && guard-- > 0) total += runtime.Advance(1f / fps).AuthoredPlanarDisplacement;
        Vector3 expected = profile.EvaluatePlanarPosition(1f);
        Assert.That(total.x, Is.EqualTo(expected.x).Within(0.0001f));
        Assert.That(total.z, Is.EqualTo(expected.z).Within(0.0001f));
        Destroy(definition, profile);
    }

    [Test]
    public void ReplacementGivesCompletionOwnershipToNewInstance()
    {
        PlayerMotionDefinition first = CreateDefinition(out PlayerMotionProfile firstProfile);
        PlayerMotionDefinition second = CreateDefinition(out PlayerMotionProfile secondProfile);
        PlayerMotionRuntime runtime = new PlayerMotionRuntime();
        ulong oldId = runtime.Begin(first, Vector3.forward);
        runtime.Advance(0.5f);
        ulong newId = runtime.Begin(second, Vector3.forward);
        Assert.That(newId, Is.Not.EqualTo(oldId));
        Assert.That(runtime.Snapshot.ActiveDefinition, Is.SameAs(second));
        Assert.That(runtime.Snapshot.JustCancelled, Is.True);
        runtime.Advance(1f);
        Assert.That(runtime.Snapshot.InstanceId, Is.EqualTo(newId));
        Assert.That(runtime.Snapshot.JustCompleted, Is.True);
        Destroy(first, firstProfile, second, secondProfile);
    }

    [Test]
    public void CancellationFlagLivesForOneSnapshotFrame()
    {
        PlayerMotionDefinition definition = CreateDefinition(out PlayerMotionProfile profile);
        PlayerMotionRuntime runtime = new PlayerMotionRuntime();
        runtime.Begin(definition, Vector3.forward);
        runtime.Cancel();
        Assert.That(runtime.Snapshot.JustCancelled, Is.True);
        runtime.BeginFrame();
        Assert.That(runtime.Snapshot.JustCancelled, Is.False);
        Assert.That(runtime.Snapshot.ActiveDefinition, Is.Null);
        Destroy(definition, profile);
    }

    [Test]
    public void CompletionFlagLivesForOneSnapshotFrame()
    {
        PlayerMotionDefinition definition = CreateDefinition(out PlayerMotionProfile profile);
        PlayerMotionRuntime runtime = new PlayerMotionRuntime();
        runtime.Begin(definition, Vector3.forward);
        runtime.Advance(1f);
        Assert.That(runtime.Snapshot.JustCompleted, Is.True);
        Assert.That(runtime.Snapshot.ActiveDefinition, Is.SameAs(definition));
        runtime.BeginFrame();
        Assert.That(runtime.Snapshot.JustCompleted, Is.False);
        Assert.That(runtime.Snapshot.ActiveDefinition, Is.Null);
        Destroy(definition, profile);
    }

    [Test]
    public void TransitionLockEndsAtConfiguredProgress()
    {
        PlayerMotionDefinition definition = CreateDefinition(out PlayerMotionProfile profile, 0.8f, 1f, 0.6f);
        PlayerMotionRuntime runtime = new PlayerMotionRuntime();
        runtime.Begin(definition, Vector3.forward);
        Assert.That(runtime.Snapshot.IsTransitionLocked, Is.True);
        runtime.Advance(0.5f);
        Assert.That(runtime.Snapshot.IsTransitionLocked, Is.True);
        runtime.Advance(0.11f);
        Assert.That(runtime.Snapshot.IsTransitionLocked, Is.False);
        Destroy(definition, profile);
    }

    [Test]
    public void EntrySourceIsCapturedAndItsPlanarVelocityStaysConstant()
    {
        PlayerMotionDefinition definition = CreateDefinition(out PlayerMotionProfile profile);
        definition.ConfigureEntryHandoff(0.2f);
        PlayerMotionRuntime runtime = new PlayerMotionRuntime();
        runtime.Begin(definition, new PlayerMotionEntrySource(PlayerLocomotionMode.Run, new Vector3(0f, 7f, 4f)), Vector3.forward);
        Assert.That(runtime.Snapshot.EntrySourceLocomotionMode, Is.EqualTo(PlayerLocomotionMode.Run));
        PlayerMotionFrame middle = runtime.Advance(0.1f);
        Assert.That(middle.EntryHandoffActive, Is.True);
        Assert.That(middle.EntryTargetTranslationWeight, Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(middle.EntrySourcePlanarVelocity, Is.EqualTo(new Vector3(0f, 0f, 4f)));
        PlayerMotionFrame end = runtime.Advance(0.1f);
        Assert.That(end.EntryHandoffActive, Is.False);
        Assert.That(end.EntryTargetTranslationWeight, Is.EqualTo(1f).Within(0.0001f));
        Destroy(definition, profile);
    }

    [Test]
    public void SelectedProfileAndEntryFootAreExposedBySnapshot()
    {
        PlayerMotionDefinition definition = CreateDefinition(out PlayerMotionProfile defaultProfile);
        PlayerMotionProfile selectedProfile = CreateProfile(3f);
        PlayerMotionRuntime runtime = new PlayerMotionRuntime();
        runtime.Begin(definition, selectedProfile, PlayerFoot.Left, Vector3.forward);
        Assert.That(runtime.Snapshot.ActiveProfile, Is.SameAs(selectedProfile));
        Assert.That(runtime.Snapshot.EntryLastPlantFoot, Is.EqualTo(PlayerFoot.Left));
        Destroy(definition, defaultProfile, selectedProfile);
    }

    [Test]
    public void NegativeDeltaTimeDoesNotAdvanceProgress()
    {
        PlayerMotionDefinition definition = CreateDefinition(out PlayerMotionProfile profile);
        PlayerMotionRuntime runtime = new PlayerMotionRuntime();
        runtime.Begin(definition, Vector3.forward);
        PlayerMotionFrame frame = runtime.Advance(-1f);
        Assert.That(frame.PreviousProgress, Is.Zero);
        Assert.That(frame.CurrentProgress, Is.Zero);
        Assert.That(runtime.Snapshot.IsActive, Is.True);
        Destroy(definition, profile);
    }

    [Test]
    public void ProfileYawReportsCurrentAndRemainingAuthoredRotation()
    {
        PlayerMotionDefinition definition = CreateTurnDefinition(out PlayerMotionProfile profile);
        PlayerMotionRuntime runtime = new PlayerMotionRuntime();
        runtime.Begin(definition, Vector3.forward);
        PlayerMotionFrame frame = runtime.Advance(0.25f);
        Assert.That(frame.AuthoredYawDelta, Is.EqualTo(-45f).Within(0.0001f));
        Assert.That(frame.RemainingAuthoredYaw, Is.EqualTo(-135f).Within(0.0001f));
        Destroy(definition, profile);
    }

    private static PlayerMotionDefinition CreateDefinition(out PlayerMotionProfile profile, float exitStart = 0.8f, float exitEnd = 1f, float transitionLockEnd = 0f)
    {
        profile = CreateProfile(2f);
        PlayerMotionDefinition definition = ScriptableObject.CreateInstance<PlayerMotionDefinition>();
        definition.Configure(profile, PlayerMotionRotationPolicy.FaceDirection, 0f, 1f, exitStart, exitEnd, true, transitionLockEnd);
        return definition;
    }

    private static PlayerMotionDefinition CreateTurnDefinition(out PlayerMotionProfile profile)
    {
        profile = ScriptableObject.CreateInstance<PlayerMotionProfile>();
        profile.SetBakedData(1f, 2, new[] { Vector2.zero, new Vector2(0.5f, 0f), new Vector2(1f, 0f) }, new[] { 0f, 0.5f, 1f }, new[] { 0f, -90f, -180f }, string.Empty, 0, string.Empty, string.Empty);
        PlayerMotionDefinition definition = ScriptableObject.CreateInstance<PlayerMotionDefinition>();
        definition.Configure(profile, PlayerMotionRotationPolicy.ProfileYaw, 0f, 1f, 0.8f, 1f);
        return definition;
    }

    private static PlayerMotionProfile CreateProfile(float travelDistance)
    {
        PlayerMotionProfile profile = ScriptableObject.CreateInstance<PlayerMotionProfile>();
        profile.SetBakedData(1f, 2, new[] { Vector2.zero, new Vector2(0f, travelDistance * 0.5f), new Vector2(0f, travelDistance) }, new[] { 0f, travelDistance * 0.5f, travelDistance }, new[] { 0f, 0f, 0f }, string.Empty, 0, string.Empty, string.Empty);
        return profile;
    }

    private static void Destroy(params Object[] objects)
    {
        for (int i = 0; i < objects.Length; i++) Object.DestroyImmediate(objects[i]);
    }
}
