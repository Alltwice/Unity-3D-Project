using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class PlayerLocomotionPhaseRuntimeTests
{
    [Test]
    public void ActualPlanarDisplacement_AdvancesAndWrapsNormalizedPhase()
    {
        using PhaseFixture fixture = new PhaseFixture();
        fixture.Runtime.Commit(PlayerLocomotionMode.Walk, MotorResult(0f), default);
        fixture.Runtime.Commit(PlayerLocomotionMode.Walk, MotorResult(1f), default);
        Assert.That(fixture.Runtime.Snapshot.NormalizedTime, Is.EqualTo(0.5f).Within(0.0001f));
        fixture.Runtime.Commit(PlayerLocomotionMode.Walk, MotorResult(1.2f), default);
        Assert.That(fixture.Runtime.Snapshot.NormalizedTime, Is.EqualTo(0.1f).Within(0.0001f));
    }

    [Test]
    public void ZeroDisplacement_PreservesActivePhaseAndPlantResolution()
    {
        using PhaseFixture fixture = new PhaseFixture();
        fixture.Runtime.Commit(PlayerLocomotionMode.Walk, MotorResult(0f), default);
        PlayerLocomotionPhaseSnapshot initial = fixture.Runtime.Snapshot;
        fixture.Runtime.Commit(PlayerLocomotionMode.Walk, MotorResult(0f), default);
        PlayerLocomotionPhaseSnapshot current = fixture.Runtime.Snapshot;
        Assert.That(current.HasLoop, Is.True);
        Assert.That(current.HasPhase, Is.True);
        Assert.That(current.NormalizedTime, Is.EqualTo(initial.NormalizedTime));
        Assert.That(current.LastPlantFoot, Is.EqualTo(initial.LastPlantFoot));
        Assert.That(current.NextPlantFoot, Is.EqualTo(initial.NextPlantFoot));
    }

    [Test]
    public void BoundaryBeforeHandoff_PausesLoopAndUsesBoundaryPlantFoot()
    {
        using PhaseFixture fixture = new PhaseFixture();
        fixture.Runtime.Commit(PlayerLocomotionMode.Walk, MotorResult(0f), default);
        fixture.Runtime.Commit(PlayerLocomotionMode.Walk, MotorResult(0.8f), BoundaryMotion(fixture, 7, 0.6f, false, true, false, false));
        PlayerLocomotionPhaseSnapshot phase = fixture.Runtime.Snapshot;
        Assert.That(phase.HasLoop, Is.False);
        Assert.That(phase.LastPlantFoot, Is.EqualTo(PlayerFoot.Left));
    }

    [Test]
    public void Handoff_ChoosesBoundaryFootVariantAndDoesNotConsumeActivationFrameDisplacement()
    {
        using PhaseFixture fixture = new PhaseFixture();
        fixture.Runtime.Commit(PlayerLocomotionMode.Walk, MotorResult(0f), default);
        fixture.Runtime.Commit(PlayerLocomotionMode.Walk, MotorResult(0f), BoundaryMotion(fixture, 7, 0.6f, false, true, false, false));
        fixture.Runtime.Commit(PlayerLocomotionMode.Walk, MotorResult(1f), BoundaryMotion(fixture, 7, 0.8f, true, true, false, false));
        PlayerLocomotionPhaseSnapshot activated = fixture.Runtime.Snapshot;
        Assert.That(activated.HasLoop, Is.True);
        Assert.That(activated.VariantFoot, Is.EqualTo(PlayerFoot.Right));
        Assert.That(activated.NormalizedTime, Is.Zero);
        fixture.Runtime.Commit(PlayerLocomotionMode.Walk, MotorResult(1f), BoundaryMotion(fixture, 7, 0.9f, true, true, false, false));
        Assert.That(fixture.Runtime.Snapshot.NormalizedTime, Is.EqualTo(0.5f).Within(0.0001f));
        fixture.Runtime.Commit(PlayerLocomotionMode.Walk, MotorResult(0.5f), BoundaryMotion(fixture, 7, 1f, true, false, true, false));
        Assert.That(fixture.Runtime.Snapshot.NormalizedTime, Is.EqualTo(0.75f).Within(0.0001f));
    }

    [Test]
    public void EntryHandoff_RetainsAndAdvancesExistingSourceLoopAfterIdleTransition()
    {
        using PhaseFixture fixture = new PhaseFixture();
        fixture.Runtime.Commit(PlayerLocomotionMode.Walk, MotorResult(0f), default);
        PlayerLocomotionPhaseSnapshot beforeEntry = fixture.Runtime.Snapshot;
        PlayerMotionSnapshot entry = new PlayerMotionSnapshot(fixture.BoundaryDefinition, fixture.BoundaryProfile, PlayerFoot.Right, 17, 0.05f, 0f, false, true, true, 1f / 3f, PlayerLocomotionMode.Walk, true, false, false);
        fixture.Runtime.Commit(PlayerLocomotionMode.Idle, MotorResult(1f), entry);
        PlayerLocomotionPhaseSnapshot duringEntry = fixture.Runtime.Snapshot;
        Assert.That(duringEntry.HasLoop, Is.True);
        Assert.That(duringEntry.Mode, Is.EqualTo(beforeEntry.Mode));
        Assert.That(duringEntry.VariantFoot, Is.EqualTo(beforeEntry.VariantFoot));
        Assert.That(duringEntry.NormalizedTime, Is.GreaterThan(beforeEntry.NormalizedTime));

        PlayerMotionSnapshot completed = new PlayerMotionSnapshot(fixture.BoundaryDefinition, fixture.BoundaryProfile, PlayerFoot.Right, 17, 0.15f, 0f, false, true, false, 1f, PlayerLocomotionMode.Walk, false, true, false);
        fixture.Runtime.Commit(PlayerLocomotionMode.Idle, MotorResult(0f), completed);
        Assert.That(fixture.Runtime.Snapshot.HasLoop, Is.False);
    }

    [Test]
    public void CompletionAfterHandoff_PreservesPhaseAndVariant()
    {
        using PhaseFixture fixture = new PhaseFixture();
        fixture.Runtime.Commit(PlayerLocomotionMode.Walk, MotorResult(0f), default);
        fixture.Runtime.Commit(PlayerLocomotionMode.Walk, MotorResult(0f), BoundaryMotion(fixture, 7, 0.6f, false, true, false, false));
        fixture.Runtime.Commit(PlayerLocomotionMode.Walk, MotorResult(0f), BoundaryMotion(fixture, 7, 0.8f, true, true, false, false));
        fixture.Runtime.Commit(PlayerLocomotionMode.Walk, MotorResult(1f), BoundaryMotion(fixture, 7, 0.9f, true, true, false, false));
        PlayerLocomotionPhaseSnapshot beforeCompletion = fixture.Runtime.Snapshot;
        fixture.Runtime.Commit(PlayerLocomotionMode.Walk, MotorResult(0.5f), BoundaryMotion(fixture, 7, 1f, true, false, true, false));
        PlayerLocomotionPhaseSnapshot completed = fixture.Runtime.Snapshot;
        Assert.That(completed.HasLoop, Is.True);
        Assert.That(completed.VariantFoot, Is.EqualTo(beforeCompletion.VariantFoot));
        Assert.That(completed.NormalizedTime, Is.Not.Zero);
        Assert.That(completed.NormalizedTime, Is.EqualTo(0.75f).Within(0.0001f));
    }

    [Test]
    public void CancellationAfterHandoff_PreservesPhaseAndVariant()
    {
        using PhaseFixture fixture = new PhaseFixture();
        fixture.Runtime.Commit(PlayerLocomotionMode.Walk, MotorResult(0f), default);
        fixture.Runtime.Commit(PlayerLocomotionMode.Walk, MotorResult(0f), BoundaryMotion(fixture, 7, 0.6f, false, true, false, false));
        fixture.Runtime.Commit(PlayerLocomotionMode.Walk, MotorResult(0f), BoundaryMotion(fixture, 7, 0.8f, true, true, false, false));
        fixture.Runtime.Commit(PlayerLocomotionMode.Walk, MotorResult(1f), BoundaryMotion(fixture, 7, 0.9f, true, true, false, false));
        PlayerLocomotionPhaseSnapshot beforeCancellation = fixture.Runtime.Snapshot;
        fixture.Runtime.Commit(PlayerLocomotionMode.Walk, MotorResult(0.5f), BoundaryMotion(fixture, 7, 0.9f, true, false, false, true));
        PlayerLocomotionPhaseSnapshot cancelled = fixture.Runtime.Snapshot;
        Assert.That(cancelled.HasLoop, Is.True);
        Assert.That(cancelled.VariantFoot, Is.EqualTo(beforeCancellation.VariantFoot));
        Assert.That(cancelled.NormalizedTime, Is.Not.Zero);
        Assert.That(cancelled.NormalizedTime, Is.EqualTo(0.75f).Within(0.0001f));
    }

    [Test]
    public void CompletionWithoutActiveLoop_ActivatesCycleAtPhaseZero()
    {
        using PhaseFixture fixture = new PhaseFixture();
        Assert.That(fixture.Runtime.Snapshot.HasLoop, Is.False);
        fixture.Runtime.Commit(PlayerLocomotionMode.Walk, MotorResult(0f), BoundaryMotion(fixture, 11, 0.6f, false, false, true, false));
        PlayerLocomotionPhaseSnapshot completed = fixture.Runtime.Snapshot;
        Assert.That(completed.HasLoop, Is.True);
        Assert.That(completed.VariantFoot, Is.EqualTo(PlayerFoot.Left));
        Assert.That(completed.NormalizedTime, Is.Zero);
    }

    [Test]
    public void GroundModeSwitchAndNonLoopModes_ResetCycleAndRetainLastPlantFoot()
    {
        using PhaseFixture fixture = new PhaseFixture();
        fixture.Runtime.Commit(PlayerLocomotionMode.Walk, MotorResult(0f), BoundaryMotion(fixture, 13, 0.6f, true, false, true, false));
        fixture.Runtime.Commit(PlayerLocomotionMode.Idle, MotorResult(0f), default);
        Assert.That(fixture.Runtime.Snapshot.HasLoop, Is.False);
        Assert.That(fixture.Runtime.Snapshot.LastPlantFoot, Is.EqualTo(PlayerFoot.Left));
        fixture.Runtime.Commit(PlayerLocomotionMode.Run, MotorResult(2f), default);
        PlayerLocomotionPhaseSnapshot run = fixture.Runtime.Snapshot;
        Assert.That(run.Mode, Is.EqualTo(PlayerLocomotionMode.Run));
        Assert.That(run.VariantFoot, Is.EqualTo(PlayerFoot.Left));
        Assert.That(run.NormalizedTime, Is.Zero);
        fixture.Runtime.Commit(PlayerLocomotionMode.FastRun, MotorResult(2f), default);
        PlayerLocomotionPhaseSnapshot fastRun = fixture.Runtime.Snapshot;
        Assert.That(fastRun.Mode, Is.EqualTo(PlayerLocomotionMode.FastRun));
        Assert.That(fastRun.VariantFoot, Is.EqualTo(PlayerFoot.Left));
        Assert.That(fastRun.NormalizedTime, Is.Zero);
        fixture.Runtime.Commit(PlayerLocomotionMode.Air, MotorResult(0f, false), default);
        Assert.That(fixture.Runtime.Snapshot.HasLoop, Is.False);
        Assert.That(fixture.Runtime.Snapshot.LastPlantFoot, Is.EqualTo(PlayerFoot.Left));
        fixture.Runtime.Commit(PlayerLocomotionMode.HardLanding, MotorResult(0f), default);
        Assert.That(fixture.Runtime.Snapshot.HasLoop, Is.False);
        Assert.That(fixture.Runtime.Snapshot.LastPlantFoot, Is.EqualTo(PlayerFoot.Left));
    }

    [Test]
    public void DefaultCatalogContainsRequiredCyclesAndValidData()
    {
        PlayerMotionCatalog catalog = AssetDatabase.LoadAssetAtPath<PlayerMotionCatalog>("Assets/Settings/Player/Motion/DefaultPlayerMotionCatalog.asset");
        Assert.That(catalog, Is.Not.Null);
        PlayerLocomotionMode[] modes = { PlayerLocomotionMode.Walk, PlayerLocomotionMode.Run, PlayerLocomotionMode.FastRun };
        for (int modeIndex = 0; modeIndex < modes.Length; modeIndex++)
        {
            Assert.That(catalog.TryGetCycle(modes[modeIndex], out PlayerLocomotionCycleDefinition cycle), Is.True);
            PlayerFoot[] feet = { PlayerFoot.Unknown, PlayerFoot.Left, PlayerFoot.Right };
            for (int footIndex = 0; footIndex < feet.Length; footIndex++)
            {
                Assert.That(cycle.TryResolveProfile(feet[footIndex], out PlayerMotionProfile profile, out _), Is.True);
                Assert.That(profile.CycleDistance, Is.GreaterThan(0f));
                Assert.That(profile.ValidateLoopPhase(new List<string>()), Is.True, profile.name);
            }
        }
        List<string> errors = new List<string>();
        Assert.That(catalog.Validate(errors), Is.True, string.Join("\n", errors));
    }

    private static PlayerMotionSnapshot BoundaryMotion(PhaseFixture fixture, ulong instanceId, float progress, bool handoffActive, bool isActive, bool justCompleted, bool justCancelled)
    {
        return new PlayerMotionSnapshot(fixture.BoundaryDefinition, fixture.BoundaryProfile, PlayerFoot.Unknown, instanceId, progress, handoffActive ? 1f : 0f, handoffActive, isActive, justCompleted, justCancelled);
    }

    private static PlayerMotorResult MotorResult(float planarDistance, bool grounded = true)
    {
        Vector3 displacement = Vector3.forward * planarDistance;
        return new PlayerMotorResult(displacement, displacement, Vector3.zero, 0f, grounded, false, CollisionFlags.None);
    }

    private sealed class PhaseFixture : IDisposable
    {
        private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();

        public PlayerLocomotionPhaseRuntime Runtime { get; }
        public PlayerMotionProfile BoundaryProfile { get; }
        public PlayerMotionDefinition BoundaryDefinition { get; }

        public PhaseFixture()
        {
            PlayerMotionProfile walkRight = CreateLoopProfile("WalkRight", 2f, PlayerFoot.Left, PlayerFoot.Right);
            PlayerMotionProfile walkLeft = CreateLoopProfile("WalkLeft", 4f, PlayerFoot.Right, PlayerFoot.Left);
            PlayerMotionProfile runRight = CreateLoopProfile("RunRight", 3f, PlayerFoot.Left, PlayerFoot.Right);
            PlayerMotionProfile runLeft = CreateLoopProfile("RunLeft", 5f, PlayerFoot.Right, PlayerFoot.Left);
            PlayerMotionProfile fastRunRight = CreateLoopProfile("FastRunRight", 6f, PlayerFoot.Left, PlayerFoot.Right);
            PlayerMotionProfile fastRunLeft = CreateLoopProfile("FastRunLeft", 8f, PlayerFoot.Right, PlayerFoot.Left);
            PlayerLocomotionCycleDefinition walk = new PlayerLocomotionCycleDefinition();
            PlayerLocomotionCycleDefinition run = new PlayerLocomotionCycleDefinition();
            PlayerLocomotionCycleDefinition fastRun = new PlayerLocomotionCycleDefinition();
            walk.Configure(PlayerLocomotionMode.Walk, walkRight, walkLeft, walkRight);
            run.Configure(PlayerLocomotionMode.Run, runRight, runLeft, runRight);
            fastRun.Configure(PlayerLocomotionMode.FastRun, fastRunRight, fastRunLeft, fastRunRight);
            PlayerMotionCatalog catalog = Create<PlayerMotionCatalog>();
            catalog.Configure(Array.Empty<PlayerMotionCatalogEntry>(), new[] { walk, run, fastRun }, 150f);
            BoundaryProfile = CreateLoopProfile("Boundary", 1f, PlayerFoot.Left, PlayerFoot.Right);
            BoundaryDefinition = Create<PlayerMotionDefinition>();
            BoundaryDefinition.Configure(BoundaryProfile, PlayerMotionTranslationPolicy.None, PlayerMotionRotationPolicy.KeepFacing, PlayerMotionBasisPolicy.EntryFacing, 0f, 1f, 0.8f, 1f);
            Runtime = new PlayerLocomotionPhaseRuntime(catalog);
        }

        public void Dispose()
        {
            for (int index = objects.Count - 1; index >= 0; index--) UnityEngine.Object.DestroyImmediate(objects[index]);
        }

        private PlayerMotionProfile CreateLoopProfile(string profileName, float cycleDistance, PlayerFoot firstFoot, PlayerFoot secondFoot)
        {
            PlayerMotionProfile profile = Create<PlayerMotionProfile>();
            profile.name = profileName;
            profile.SetBakedData(1f, 2, new[] { Vector2.zero, Vector2.up * (cycleDistance * 0.5f), Vector2.up * cycleDistance }, new[] { 0f, cycleDistance * 0.5f, cycleDistance }, new[] { 0f, 0f, 0f }, string.Empty, 0L, string.Empty, string.Empty);
            profile.SetPlantAuthoringSettings(PlayerFootPlantDetectionMode.Loop, PlayerPlantMarkerMode.ManualOverride);
            profile.ReplacePlantMarkers(new[] { new PlayerFootPlantMarker(firstFoot, 0.25f, 1f), new PlayerFootPlantMarker(secondFoot, 0.75f, 1f) }, PlayerMotionProfile.CurrentFootPlantDetectionVersion);
            return profile;
        }

        private T Create<T>() where T : ScriptableObject
        {
            T value = ScriptableObject.CreateInstance<T>();
            objects.Add(value);
            return value;
        }
    }
}
