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
        runtime.Begin(definition, Vector3.forward, Vector3.forward);
        Vector3 total = Vector3.zero;
        PlayerGameplayIntent intent = PlayerGameplayIntent.Create(Vector3.forward, Vector3.forward);
        int guard = fps * 3;
        while (!runtime.Snapshot.JustCompleted && guard-- > 0) total += runtime.Advance(1f / fps, intent).AuthoredPlanarDisplacement;
        Assert.That(total.z, Is.EqualTo(profile.EvaluateTravelDistance(1f)).Within(0.0001f));
        Destroy(definition, profile);
    }

    [Test]
    public void ReplacementGivesCompletionOwnershipToNewInstance()
    {
        PlayerMotionDefinition first = CreateDefinition(out PlayerMotionProfile firstProfile);
        PlayerMotionDefinition second = CreateDefinition(out PlayerMotionProfile secondProfile);
        PlayerMotionRuntime runtime = new PlayerMotionRuntime();
        ulong oldId = runtime.Begin(first, Vector3.forward, Vector3.forward);
        runtime.Advance(0.5f, PlayerGameplayIntent.Create(Vector3.forward, Vector3.forward));
        ulong newId = runtime.Begin(second, Vector3.forward, Vector3.forward);
        Assert.That(newId, Is.Not.EqualTo(oldId));
        Assert.That(runtime.Snapshot.ActiveDefinition, Is.SameAs(second));
        Assert.That(runtime.Snapshot.JustCancelled, Is.True);
        runtime.Advance(1f, PlayerGameplayIntent.Create(Vector3.forward, Vector3.forward));
        Assert.That(runtime.Snapshot.InstanceId, Is.EqualTo(newId));
        Assert.That(runtime.Snapshot.JustCompleted, Is.True);
        Destroy(first, firstProfile, second, secondProfile);
    }

    [Test]
    public void CancellationFlagLivesForOneSnapshotFrame()
    {
        PlayerMotionDefinition definition = CreateDefinition(out PlayerMotionProfile profile);
        PlayerMotionRuntime runtime = new PlayerMotionRuntime();
        runtime.Begin(definition, Vector3.forward, Vector3.forward);
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
        runtime.Begin(definition, Vector3.forward, Vector3.forward);
        runtime.Advance(1f, default);
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
        PlayerMotionDefinition definition = CreateDefinition(out PlayerMotionProfile profile, 0.6f);
        PlayerMotionRuntime runtime = new PlayerMotionRuntime();
        runtime.Begin(definition, Vector3.forward, Vector3.forward);
        Assert.That(runtime.Snapshot.IsTransitionLocked, Is.True);
        runtime.Advance(0.5f, default);
        Assert.That(runtime.Snapshot.IsTransitionLocked, Is.True);
        runtime.Advance(0.11f, default);
        Assert.That(runtime.Snapshot.IsTransitionLocked, Is.False);
        Destroy(definition, profile);
    }

    [Test]
    public void EntrySourceIsCapturedAndItsPlanarVelocityStaysConstant()
    {
        PlayerMotionDefinition definition = CreateDefinition(out PlayerMotionProfile profile);
        PlayerMotionCatalog catalog = ScriptableObject.CreateInstance<PlayerMotionCatalog>();
        PlayerMotionProfile loopProfile = CreateProfile(1f);
        PlayerLocomotionDefinition cycle = ScriptableObject.CreateInstance<PlayerLocomotionDefinition>();
        var loop = PlayerMotionNodeKey.ForLoop(PlayerLocomotionMode.Run);
        var edge = PlayerMotionNodeKey.ForMotion(PlayerMotionId.RunToIdle);
        cycle.Configure(PlayerLocomotionMode.Run, loopProfile, loopProfile, loopProfile);
        cycle.ConfigureExitHandoff(new PlayerHandoffSettings(0.2f, AnimationCurve.Linear(0f, 0f, 1f, 1f)));
        catalog.Configure(new[] { new PlayerMotionCatalogEntry(PlayerMotionId.RunToIdle, definition) }, new[] { cycle }, 150f);
        PlayerHandoffRuntime runtime = new PlayerHandoffRuntime(catalog);
        runtime.SetImmediate(loop, PlayerFoot.Unknown, Vector3.forward, Vector3.forward, Vector3.forward * 4f);
        runtime.Request(edge, PlayerFoot.Unknown, Vector3.forward, Vector3.forward, Vector3.forward * 4f);
        var steps = runtime.Advance(0.1f, default, Vector3.forward);
        Assert.That(runtime.Snapshot.SourceTranslationWeight, Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(steps[0].SourceVelocity, Is.EqualTo(Vector3.forward * 4f));
        runtime.Advance(0.1f, default, Vector3.forward);
        Assert.That(runtime.Snapshot.IsActive, Is.False);
        Destroy(definition, profile, loopProfile, cycle, catalog);
    }

    [Test]
    public void SelectedProfileAndEntryFootAreExposedBySnapshot()
    {
        PlayerMotionDefinition definition = CreateDefinition(out PlayerMotionProfile defaultProfile);
        PlayerMotionProfile selectedProfile = CreateProfile(3f);
        PlayerMotionRuntime runtime = new PlayerMotionRuntime();
        runtime.Begin(definition, selectedProfile, PlayerFoot.Left, Vector3.forward, Vector3.forward);
        Assert.That(runtime.Snapshot.ActiveProfile, Is.SameAs(selectedProfile));
        Assert.That(runtime.Snapshot.EntryLastPlantFoot, Is.EqualTo(PlayerFoot.Left));
        Destroy(definition, defaultProfile, selectedProfile);
    }

    [Test]
    public void NegativeDeltaTimeDoesNotAdvanceProgress()
    {
        PlayerMotionDefinition definition = CreateDefinition(out PlayerMotionProfile profile);
        PlayerMotionRuntime runtime = new PlayerMotionRuntime();
        runtime.Begin(definition, Vector3.forward, Vector3.forward);
        PlayerMotionFrame frame = runtime.Advance(-1f, default);
        Assert.That(frame.PreviousProgress, Is.Zero);
        Assert.That(frame.CurrentProgress, Is.Zero);
        Assert.That(runtime.Snapshot.IsActive, Is.True);
        Destroy(definition, profile);
    }

    [Test]
    public void DesiredDirectionMotionKeepsCapturedDirectionWithoutInput()
    {
        PlayerMotionDefinition definition = CreateTurnDefinition(out PlayerMotionProfile profile);
        PlayerMotionRuntime runtime = new PlayerMotionRuntime();
        runtime.Begin(definition, Vector3.forward, Vector3.back);
        PlayerMotionFrame frame = runtime.Advance(0.1f, PlayerGameplayIntent.Create(Vector3.zero, Vector3.forward));
        Assert.That(frame.AuthoredPlanarDisplacement.z, Is.LessThan(0f));
        Assert.That(runtime.Snapshot.IsActive, Is.True);
        Destroy(definition, profile);
    }

    [Test]
    public void DesiredDirectionMotionSteersWithoutCancelling()
    {
        PlayerMotionDefinition definition = CreateTurnDefinition(out PlayerMotionProfile profile);
        PlayerMotionRuntime runtime = new PlayerMotionRuntime();
        runtime.Begin(definition, Vector3.forward, Vector3.back);
        PlayerMotionFrame frame = runtime.Advance(0.1f, PlayerGameplayIntent.Create(Vector3.right, Vector3.forward));
        Assert.That(frame.AuthoredPlanarDisplacement.x, Is.GreaterThan(0f));
        Assert.That(runtime.Snapshot.JustCancelled, Is.False);
        Assert.That(runtime.Snapshot.IsActive, Is.True);
        Destroy(definition, profile);
    }

    [Test]
    public void ProfileYawReportsCurrentAndRemainingAuthoredRotation()
    {
        PlayerMotionDefinition definition = CreateTurnDefinition(out PlayerMotionProfile profile);
        PlayerMotionRuntime runtime = new PlayerMotionRuntime();
        runtime.Begin(definition, Vector3.forward, Vector3.back);
        PlayerMotionFrame frame = runtime.Advance(0.25f, PlayerGameplayIntent.Create(Vector3.back, Vector3.forward));
        Assert.That(frame.AuthoredYawDelta, Is.EqualTo(-45f).Within(0.0001f));
        Assert.That(frame.RemainingAuthoredYaw, Is.EqualTo(-135f).Within(0.0001f));
        Destroy(definition, profile);
    }

    [Test]
    public void HandoffRetargetKeepsSourceProgressAndUnifiedWeight()
    {
        using HandoffFixture fixture = new HandoffFixture();
        fixture.Runtime.Advance(0.8f, default, Vector3.forward);
        PlayerHandoffSnapshot before = fixture.Runtime.Snapshot;
        Assert.That(before.IsActive, Is.True);
        fixture.Runtime.Request(fixture.End, PlayerFoot.Unknown, Vector3.forward, Vector3.forward, Vector3.zero);
        PlayerHandoffSnapshot after = fixture.Runtime.Snapshot;
        Assert.That(after.Source.InstanceId, Is.EqualTo(before.Source.InstanceId));
        Assert.That(after.SourcePoseWeight, Is.EqualTo(before.SourcePoseWeight).Within(0.0001f));
        Assert.That(after.SourceTranslationWeight, Is.EqualTo(before.SourceTranslationWeight).Within(0.0001f));
        Assert.That(after.Source.Motion.Progress, Is.EqualTo(before.Source.Motion.Progress));
        Assert.That(after.SourcePoseWeight, Is.EqualTo(after.SourceTranslationWeight));
        Assert.That(after.Target.Motion.Progress, Is.Zero);
        Assert.That(after.Target.Key, Is.EqualTo(fixture.End));
        Assert.That(after.Target.InstanceId, Is.Not.EqualTo(before.Target.InstanceId));
        fixture.Runtime.Advance(0.1f, default, Vector3.forward);
        Assert.That(fixture.Runtime.Snapshot.Source.Motion.Progress, Is.GreaterThan(after.Source.Motion.Progress));
        Assert.That(fixture.Runtime.Snapshot.SourcePoseWeight, Is.EqualTo(after.SourcePoseWeight * (1f - 0.1f / 0.3f)).Within(0.0001f));
    }

    [TestCase(1f)]
    [TestCase(1.1f)]
    [TestCase(1.4333334f)]
    [TestCase(1.7666668f)]
    [TestCase(1.9666668f)]
    public void HandoffCrossingConsumesOnlyRemainingFrameTime(float motionDuration)
    {
        using HandoffFixture fixture = new HandoffFixture(motionDuration: motionDuration);
        var steps = fixture.Runtime.Advance(0.7f * motionDuration + 0.1f, default, Vector3.forward);
        Assert.That(fixture.Runtime.Snapshot.Target.ElapsedTime, Is.EqualTo(0.1f).Within(0.0001f));
        float totalTime = 0f;
        foreach (PlayerHandoffStep step in steps) totalTime += step.DeltaTime;
        Assert.That(totalTime, Is.EqualTo(0.7f * motionDuration + 0.1f).Within(0.0001f));
        fixture.Runtime.Advance(0.3f, default, Vector3.forward);
        Assert.That(fixture.Runtime.Snapshot.IsActive, Is.False);
        Assert.That(fixture.Runtime.Snapshot.Target.ElapsedTime, Is.EqualTo(0.4f).Within(0.0001f));
    }

    [Test]
    public void HandoffExactTriggerStartsTargetWithoutAdvancingIt()
    {
        using HandoffFixture fixture = new HandoffFixture();
        fixture.Runtime.Advance(0.7f, default, Vector3.forward);
        Assert.That(fixture.Runtime.Snapshot.IsActive, Is.True);
        Assert.That(fixture.Runtime.Snapshot.Target.ElapsedTime, Is.Zero);
    }

    [Test]
    public void HandoffRepeatedTargetDoesNotRestartAndReturnReusesSource()
    {
        using HandoffFixture fixture = new HandoffFixture();
        fixture.Runtime.Advance(0.8f, default, Vector3.forward);
        var before = fixture.Runtime.Snapshot;
        fixture.Runtime.Request(before.Target.Key, PlayerFoot.Unknown, Vector3.forward, Vector3.forward, Vector3.zero);
        Assert.That(fixture.Runtime.Snapshot.Target.InstanceId, Is.EqualTo(before.Target.InstanceId));
        fixture.Runtime.Request(before.Source.Key, PlayerFoot.Unknown, Vector3.forward, Vector3.forward, Vector3.zero);
        Assert.That(fixture.Runtime.Snapshot.Target.InstanceId, Is.EqualTo(before.Source.InstanceId));
        Assert.That(1f - fixture.Runtime.Snapshot.SourcePoseWeight, Is.EqualTo(before.SourcePoseWeight).Within(0.0001f));
    }

    [Test]
    public void ZeroDurationHandoffImmediatelyReleasesSource()
    {
        using HandoffFixture fixture = new HandoffFixture(0f);
        fixture.Runtime.Advance(0.7f, default, Vector3.forward);
        Assert.That(fixture.Runtime.Snapshot.IsActive, Is.False);
        Assert.That(fixture.Runtime.Snapshot.Target.Key.IsMotion, Is.False);
    }

    private class HandoffFixture : System.IDisposable
    {
        public PlayerHandoffRuntime Runtime;
        public PlayerMotionNodeKey End = PlayerMotionNodeKey.ForMotion(PlayerMotionId.RunToIdle);
        private Object[] owned;
        public HandoffFixture(float duration = 0.3f, float motionDuration = 1f)
        {
            var start = PlayerMotionNodeKey.ForMotion(PlayerMotionId.IdleToRun);
            var loop = PlayerMotionNodeKey.ForLoop(PlayerLocomotionMode.Run);
            var startDefinition = CreateDefinition(out var startProfile);
            var endDefinition = CreateDefinition(out var endProfile);
            startDefinition.Configure(startProfile, PlayerMotionTranslationPolicy.TravelAlongCapturedDirection, PlayerMotionRotationPolicy.FaceDirection, PlayerMotionBasisPolicy.DesiredDirection, motionDuration, 1f);
            PlayerMotionProfile loopProfile = CreateProfile(1f);
            PlayerLocomotionDefinition cycle = ScriptableObject.CreateInstance<PlayerLocomotionDefinition>();
            cycle.Configure(PlayerLocomotionMode.Run, loopProfile, loopProfile, loopProfile);
            var catalog = ScriptableObject.CreateInstance<PlayerMotionCatalog>();
            catalog.Configure(new[] { new PlayerMotionCatalogEntry(PlayerMotionId.IdleToRun, startDefinition), new PlayerMotionCatalogEntry(PlayerMotionId.RunToIdle, endDefinition) }, new[] { cycle }, 150f);
            var linear = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            startDefinition.ConfigureExitHandoff(new PlayerHandoffSettings(duration, linear));
            endDefinition.ConfigureExitHandoff(new PlayerHandoffSettings(0.4f, linear));
            cycle.ConfigureExitHandoff(new PlayerHandoffSettings(0.2f, linear));
            Runtime = new PlayerHandoffRuntime(catalog);
            Runtime.SetImmediate(start, PlayerFoot.Unknown, Vector3.forward, Vector3.forward, Vector3.zero);
            owned = new Object[] { startDefinition, startProfile, endDefinition, endProfile, loopProfile, cycle, catalog };
        }
        public void Dispose() => Destroy(owned);
    }

    private static PlayerMotionDefinition CreateDefinition(out PlayerMotionProfile profile, float transitionLockEnd = 0f)
    {
        profile = CreateProfile(2f);
        PlayerMotionDefinition definition = ScriptableObject.CreateInstance<PlayerMotionDefinition>();
        definition.Configure(profile, PlayerMotionTranslationPolicy.TravelAlongCapturedDirection, PlayerMotionRotationPolicy.FaceDirection, PlayerMotionBasisPolicy.DesiredDirection, 0f, 1f, true, transitionLockEnd);
        return definition;
    }

    private static PlayerMotionDefinition CreateTurnDefinition(out PlayerMotionProfile profile)
    {
        profile = ScriptableObject.CreateInstance<PlayerMotionProfile>();
        profile.SetBakedData(1f, 2, new[] { Vector2.zero, new Vector2(0.5f, 0f), new Vector2(1f, 0f) }, new[] { 0f, 0.5f, 1f }, new[] { 0f, -90f, -180f }, string.Empty, 0, string.Empty, string.Empty);
        PlayerMotionDefinition definition = ScriptableObject.CreateInstance<PlayerMotionDefinition>();
        definition.Configure(profile, PlayerMotionTranslationPolicy.TravelAlongDesiredDirection, PlayerMotionRotationPolicy.ProfileYaw, PlayerMotionBasisPolicy.EntryFacing, 0f, 1f);
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
