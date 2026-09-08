using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class PlayerLandingTrackerTests
{
    private PlayerMovementConfig config;
    private PlayerLandingTracker tracker;

    [SetUp]
    public void SetUp()
    {
        config = ScriptableObject.CreateInstance<PlayerMovementConfig>();
        tracker = new PlayerLandingTracker(config.Landing);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(config);
    }

    [Test]
    public void Advance_TracksPeakAndEmitsOneLandingSnapshot()
    {
        Assert.That(tracker.Advance(Result(true), 0f).IsLandingEvent, Is.False);
        tracker.Advance(Result(false), 0.5f);
        tracker.Advance(Result(false), 2.5f);
        tracker.Advance(Result(false), 1f);

        PlayerLandingSnapshot landing = tracker.Advance(Result(true, true), 0f);

        Assert.That(landing.IsLandingEvent, Is.True);
        Assert.That(landing.Sequence, Is.EqualTo(1));
        Assert.That(landing.Severity, Is.EqualTo(PlayerLandingSeverity.Lv3));
        Assert.That(landing.FallDistance, Is.EqualTo(2.5f).Within(0.0001f));
        Assert.That(tracker.Advance(Result(true), 0f).IsLandingEvent, Is.False);
    }

    [Test]
    public void Advance_UsesHeightThresholdsOnly()
    {
        tracker.Advance(Result(false), 0.5f);
        PlayerLandingSnapshot lv1 = tracker.Advance(Result(true, true), 0f);
        tracker.Advance(Result(false), 1f);
        PlayerLandingSnapshot lv2 = tracker.Advance(Result(true, true), 0f);
        tracker.Advance(Result(false), 2f);
        PlayerLandingSnapshot lv3 = tracker.Advance(Result(true, true), 0f);
        tracker.Advance(Result(false), 3f);
        PlayerLandingSnapshot lv4 = tracker.Advance(Result(true, true), 0f);

        Assert.That(lv1.Severity, Is.EqualTo(PlayerLandingSeverity.Lv1));
        Assert.That(lv2.Severity, Is.EqualTo(PlayerLandingSeverity.Lv2));
        Assert.That(lv3.Severity, Is.EqualTo(PlayerLandingSeverity.Lv3));
        Assert.That(lv4.Severity, Is.EqualTo(PlayerLandingSeverity.Lv4));
    }

    [Test]
    public void Advance_ZeroDistanceAirLifecycleIsLevelOne()
    {
        tracker.Advance(Result(false), 1f);
        PlayerLandingSnapshot landing = tracker.Advance(Result(true, true), 1f);

        Assert.That(landing.FallDistance, Is.Zero);
        Assert.That(landing.Severity, Is.EqualTo(PlayerLandingSeverity.Lv1));
    }

    [Test]
    public void Reset_DiscardsCurrentAirLifecycleAndKeepsSequenceMonotonic()
    {
        tracker.Advance(Result(false), 4f);
        PlayerLandingSnapshot first = tracker.Advance(Result(true, true), 0f);
        tracker.Advance(Result(false), 5f);
        tracker.Reset();
        tracker.Advance(Result(false), 1f);
        PlayerLandingSnapshot second = tracker.Advance(Result(true, true), 1f);

        Assert.That(first.Sequence, Is.EqualTo(1));
        Assert.That(second.Sequence, Is.EqualTo(2));
        Assert.That(first.FallDistance, Is.EqualTo(4f).Within(0.0001f));
        Assert.That(second.FallDistance, Is.Zero);
        Assert.That(second.Severity, Is.EqualTo(PlayerLandingSeverity.Lv1));
    }

    [Test]
    public void LandingSettings_DefaultThresholdsAreOrdered()
    {
        List<string> errors = new List<string>();
        Assert.That(config.Landing.Validate(errors), Is.True);
        Assert.That(errors, Is.Empty);
    }

    private static PlayerMotorResult Result(bool isGrounded, bool justLanded = false)
    {
        return new PlayerMotorResult(Vector3.zero, Vector3.zero, Vector3.zero, 0f, isGrounded, justLanded, CollisionFlags.None);
    }
}
