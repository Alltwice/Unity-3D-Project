using System;
using System.Collections.Generic;
using Animancer;
using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 动画数据和动画片段的绑定
/// </summary>
[Serializable]
public class PlayerMotionAnimationBinding
{
    [SerializeField] private PlayerMotionDefinition definition;
    [SerializeField] private ClipTransition defaultTransition = new ClipTransition();
    [SerializeField] private ClipTransition leftTransition = new ClipTransition();
    [SerializeField] private ClipTransition rightTransition = new ClipTransition();
    [FormerlySerializedAs("poseFadeWeight")]
    [SerializeField] private AnimationCurve exitPoseWeight = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    [SerializeField] private AnimationCurve entryPoseWeight = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    public PlayerMotionDefinition Definition => definition;
    public ClipTransition Transition => defaultTransition;
    public ClipTransition LeftTransition => leftTransition;
    public ClipTransition RightTransition => rightTransition;
    public float EvaluateExitPoseWeight(float handoffProgress) => Mathf.Clamp01(exitPoseWeight == null ? handoffProgress : exitPoseWeight.Evaluate(handoffProgress));
    public float EvaluateEntryPoseWeight(float handoffProgress) => Mathf.Clamp01(entryPoseWeight == null ? handoffProgress : entryPoseWeight.Evaluate(handoffProgress));

    public ClipTransition ResolveTransition(PlayerMotionProfile selectedProfile)
    {
        if (definition != null && selectedProfile != null)
        {
            if (selectedProfile == definition.LeftFootProfile && leftTransition != null && leftTransition.Clip != null) return leftTransition;
            if (selectedProfile == definition.RightFootProfile && rightTransition != null && rightTransition.Clip != null) return rightTransition;
        }
        return defaultTransition;
    }

    internal bool Validate(string label, ICollection<string> errors)
    {
        bool valid = true;
        if (definition == null)
        {
            errors?.Add(label + ": 缺少 Definition。");
            return false;
        }
        valid &= ValidateTransition(defaultTransition, label + ".Default", errors);
        valid &= ValidateWeightCurve(exitPoseWeight, label + ".ExitPoseWeight", 0f, 1f, errors);
        valid &= ValidateWeightCurve(entryPoseWeight, label + ".EntryPoseWeight", 0f, 1f, errors);
#if UNITY_EDITOR
        valid &= PlayerAnimationSet.ValidateProfileClip(definition.Profile, defaultTransition, label + ".Default", errors);
#endif
        if (!definition.RequiresFootProfiles) return valid;
        valid &= PlayerAnimationSet.ValidateProfilePlantMarkers(definition.Profile, label + ".Default", errors);
        valid &= ValidateTransition(leftTransition, label + ".Left", errors);
        valid &= ValidateTransition(rightTransition, label + ".Right", errors);
        valid &= PlayerAnimationSet.ValidateProfilePlantMarkers(definition.LeftFootProfile, label + ".Left", errors);
        valid &= PlayerAnimationSet.ValidateProfilePlantMarkers(definition.RightFootProfile, label + ".Right", errors);
#if UNITY_EDITOR
        valid &= PlayerAnimationSet.ValidateProfileClip(definition.LeftFootProfile, leftTransition, label + ".Left", errors);
        valid &= PlayerAnimationSet.ValidateProfileClip(definition.RightFootProfile, rightTransition, label + ".Right", errors);
#endif
        return valid;
    }

    private static bool ValidateTransition(ClipTransition transition, string label, ICollection<string> errors)
    {
        if (transition != null && transition.Clip != null) return true;
        errors?.Add(label + ": 缺少 Clip。");
        return false;
    }

#if UNITY_EDITOR
    public void Configure(PlayerMotionDefinition motionDefinition, AnimationClip clip, float fadeDuration)
    {
        definition = motionDefinition;
        ConfigureTransition(ref defaultTransition, clip, fadeDuration);
        exitPoseWeight = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        entryPoseWeight = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    }

    public void ConfigureFoot(PlayerFoot foot, AnimationClip clip, float fadeDuration)
    {
        if (foot == PlayerFoot.Left) ConfigureTransition(ref leftTransition, clip, fadeDuration);
        else if (foot == PlayerFoot.Right) ConfigureTransition(ref rightTransition, clip, fadeDuration);
    }

    private static void ConfigureTransition(ref ClipTransition transition, AnimationClip clip, float fadeDuration)
    {
        transition ??= new ClipTransition();
        transition.Clip = clip;
        transition.FadeDuration = fadeDuration;
        transition.Speed = 1f;
    }

    public void ConfigureEntryPoseWeight(AnimationCurve targetPoseWeight)
    {
        entryPoseWeight = targetPoseWeight ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);
    }
#endif

    private static bool ValidateWeightCurve(AnimationCurve curve, string label, float expectedStart, float expectedEnd, ICollection<string> errors)
    {
        if (curve == null)
        {
            errors?.Add(label + ": 曲线不能为空。");
            return false;
        }
        float start = curve.Evaluate(0f);
        float end = curve.Evaluate(1f);
        if (float.IsNaN(start) || float.IsInfinity(start) || float.IsNaN(end) || float.IsInfinity(end) || !Mathf.Approximately(start, expectedStart) || !Mathf.Approximately(end, expectedEnd))
        {
            errors?.Add(label + ": 曲线端点必须为 " + expectedStart + " 和 " + expectedEnd + "。");
            return false;
        }
        return true;
    }
}

/// <summary>
/// Idle 表现资源
/// </summary>
[Serializable]
public class PlayerIdleAnimationGroup
{
    [SerializeField] private ClipTransition loop = new ClipTransition();

    public ClipTransition Loop => loop;
}

/// <summary>
/// 地面运动表现资源及其 MotionDefinition 绑定
/// </summary>
[Serializable]
public class PlayerLocomotionAnimationGroup
{
    [SerializeField] private PlayerLoopAnimationPair loop = new PlayerLoopAnimationPair();
    [SerializeField] private List<PlayerMotionAnimationBinding> motionBindings = new List<PlayerMotionAnimationBinding>();

    public PlayerLoopAnimationPair Loop => loop;
    public List<PlayerMotionAnimationBinding> MotionBindings => motionBindings;
}

/// <summary>
/// 跳跃和空中表现资源
/// </summary>
[Serializable]
public class PlayerJumpAnimationGroup
{
    [SerializeField] private ClipTransition jumpStart = new ClipTransition();
    [SerializeField] private ClipTransition airLoop = new ClipTransition();

    public ClipTransition JumpStart => jumpStart;
    public ClipTransition AirLoop => airLoop;
}

/// <summary>
/// 普通落地表现资源，以及移动落地 MotionDefinition 的表现绑定。
/// </summary>
[Serializable]
public class PlayerLandingAnimationGroup
{
    [SerializeField] private ClipTransition land1 = new ClipTransition();
    [SerializeField] private ClipTransition land2 = new ClipTransition();
    [SerializeField] private ClipTransition land3 = new ClipTransition();
    [SerializeField] private ClipTransition land4 = new ClipTransition();
    [SerializeField] private PlayerMotionAnimationBinding landWalk = new PlayerMotionAnimationBinding();
    [SerializeField] private PlayerMotionAnimationBinding landRun = new PlayerMotionAnimationBinding();
    [SerializeField] private PlayerMotionAnimationBinding landRoll = new PlayerMotionAnimationBinding();

    public ClipTransition Land1 => land1;
    public ClipTransition Land2 => land2;
    public ClipTransition Land3 => land3;
    public ClipTransition Land4 => land4;
    public PlayerMotionAnimationBinding LandWalk => landWalk;
    public PlayerMotionAnimationBinding LandRun => landRun;
    public PlayerMotionAnimationBinding LandRoll => landRoll;

    public IEnumerable<PlayerMotionAnimationBinding> MotionBindings
    {
        get
        {
            yield return landWalk;
            yield return landRun;
            yield return landRoll;
        }
    }

#if UNITY_EDITOR
    public void ConfigureStandard(PlayerLandingPresentationKey presentation, AnimationClip clip, float fadeDuration)
    {
        ClipTransition transition = presentation switch
        {
            PlayerLandingPresentationKey.Land1 => land1,
            PlayerLandingPresentationKey.Land2 => land2,
            PlayerLandingPresentationKey.Land3 => land3,
            PlayerLandingPresentationKey.HardLand => land4,
            _ => null
        };
        if (transition == null) return;
        transition.Clip = clip;
        transition.FadeDuration = fadeDuration;
        transition.Speed = 1f;
    }

    public void ConfigureMotion(PlayerLandingPresentationKey presentation, PlayerMotionDefinition definition, AnimationClip clip, float fadeDuration)
    {
        switch (presentation)
        {
            case PlayerLandingPresentationKey.LandWalk:
                landWalk.Configure(definition, clip, fadeDuration);
                break;
            case PlayerLandingPresentationKey.LandRun:
                landRun.Configure(definition, clip, fadeDuration);
                break;
            case PlayerLandingPresentationKey.LandRoll:
                landRoll.Configure(definition, clip, fadeDuration);
                break;
        }
    }
#endif
}

/// <summary>
/// 不属于地面运动分类的 MotionDefinition 绑定
/// </summary>
[Serializable]
public class PlayerOtherAnimationGroup
{
    [SerializeField] private List<PlayerMotionAnimationBinding> motionBindings = new List<PlayerMotionAnimationBinding>();

    public List<PlayerMotionAnimationBinding> MotionBindings => motionBindings;
}

/// <summary>
/// 按 Simulation 选定的循环变体选择表现 Transition。
/// </summary>
[Serializable]
public class PlayerLoopAnimationPair
{
    [SerializeField] private ClipTransition defaultTransition = new ClipTransition();
    [SerializeField] private ClipTransition leftTransition = new ClipTransition();
    [SerializeField] private ClipTransition rightTransition = new ClipTransition();

    public ClipTransition DefaultTransition => defaultTransition;
    public ClipTransition LeftTransition => leftTransition;
    public ClipTransition RightTransition => rightTransition;

    public PlayerAnimationSelection Resolve(PlayerFoot foot)
    {
        if (foot == PlayerFoot.Left) return new PlayerAnimationSelection(leftTransition);
        if (foot == PlayerFoot.Right) return new PlayerAnimationSelection(rightTransition);
        return new PlayerAnimationSelection(defaultTransition);
    }

    public bool Validate(string label, ICollection<string> errors)
    {
        bool valid = true;
        valid &= ValidateSlot(defaultTransition, label + ".Default", errors);
        valid &= ValidateSlot(leftTransition, label + ".Left", errors);
        valid &= ValidateSlot(rightTransition, label + ".Right", errors);
        return valid;
    }

    private static bool ValidateSlot(ClipTransition transition, string label, ICollection<string> errors)
    {
        if (transition != null && transition.Clip != null) return true;
        errors?.Add(label + ": 缺少循环 Clip。");
        return false;
    }

#if UNITY_EDITOR
    public void Configure(PlayerFoot foot, AnimationClip clip, float fadeDuration)
    {
        if (foot == PlayerFoot.Left)
        {
            ConfigureTransition(ref leftTransition, clip, fadeDuration);
        }
        else if (foot == PlayerFoot.Right)
        {
            ConfigureTransition(ref rightTransition, clip, fadeDuration);
        }
        else
        {
            ConfigureTransition(ref defaultTransition, clip, fadeDuration);
        }
    }

    private static void ConfigureTransition(ref ClipTransition transition, AnimationClip clip, float fadeDuration)
    {
        transition ??= new ClipTransition();
        transition.Clip = clip;
        transition.FadeDuration = fadeDuration;
        transition.Speed = 1f;
    }
#endif
}
/// <summary>
/// 动画
/// </summary>
public struct PlayerAnimationSelection
{
    public PlayerAnimationSelection(ClipTransition transition)
    {
        Transition = transition;
    }

    public ClipTransition Transition { get; }
    public bool IsValid => Transition != null && Transition.Clip != null;
}

public enum PlayerAnimationCue
{
    JumpStart,
    LandingLv1,
    LandingLv2,
    LandingLv3,
    HardLanding,
    Dodge
}

[CreateAssetMenu(fileName = "PlayerAnimationSet", menuName = "Player/Animation Set")]
public class PlayerAnimationSet : ScriptableObject
{
    [SerializeField] private PlayerMotionCatalog motionCatalog;
    [SerializeField] private PlayerIdleAnimationGroup idle = new PlayerIdleAnimationGroup();
    [SerializeField] private PlayerLocomotionAnimationGroup walk = new PlayerLocomotionAnimationGroup();
    [SerializeField] private PlayerLocomotionAnimationGroup run = new PlayerLocomotionAnimationGroup();
    [SerializeField] private PlayerLocomotionAnimationGroup sprint = new PlayerLocomotionAnimationGroup();
    [SerializeField] private PlayerJumpAnimationGroup jump = new PlayerJumpAnimationGroup();
    [SerializeField] private PlayerLandingAnimationGroup landing = new PlayerLandingAnimationGroup();
    [SerializeField] private ClipTransition dodge = new ClipTransition();
    [SerializeField] private PlayerOtherAnimationGroup other = new PlayerOtherAnimationGroup();

    public PlayerMotionCatalog MotionCatalog => motionCatalog;
    public PlayerJumpAnimationGroup Jump => jump;
    public PlayerLandingAnimationGroup Landing => landing;

    public IEnumerable<PlayerMotionAnimationBinding> MotionBindings => EnumerateMotionBindings();

    public bool TryGetBinding(PlayerMotionDefinition definition, PlayerMotionProfile selectedProfile, out PlayerMotionAnimationBinding binding, out ClipTransition transition)
    {
        foreach (PlayerMotionAnimationBinding candidate in MotionBindings)
        {
            if (candidate == null || candidate.Definition != definition) continue;
            binding = candidate;
            transition = candidate.ResolveTransition(selectedProfile);
            return transition != null && transition.Clip != null;
        }
        binding = null;
        transition = null;
        return false;
    }
    /// <summary>
    /// 对于移动模式，脚步返回正确的动画
    /// </summary>
    public bool TryResolveLoop(PlayerLocomotionMode locomotionMode, PlayerFoot foot, out PlayerAnimationSelection selection)
    {
        switch (locomotionMode)
        {
            case PlayerLocomotionMode.Idle:
            case PlayerLocomotionMode.HardLanding:
                selection = new PlayerAnimationSelection(idle?.Loop);
                return selection.IsValid;
            case PlayerLocomotionMode.Walk:
                selection = walk == null || walk.Loop == null ? default : walk.Loop.Resolve(foot);
                return selection.IsValid;
            case PlayerLocomotionMode.Run:
                selection = run == null || run.Loop == null ? default : run.Loop.Resolve(foot);
                return selection.IsValid;
            case PlayerLocomotionMode.FastRun:
                selection = sprint == null || sprint.Loop == null ? default : sprint.Loop.Resolve(foot);
                return selection.IsValid;
            case PlayerLocomotionMode.Air:
                selection = new PlayerAnimationSelection(jump?.AirLoop);
                return selection.IsValid;
            default:
                selection = default;
                return false;
        }
    }

    public bool TryResolveCue(PlayerAnimationCue cue, out ClipTransition transition)
    {
        transition = cue switch
        {
            PlayerAnimationCue.Dodge => dodge,
            PlayerAnimationCue.JumpStart => jump?.JumpStart,
            PlayerAnimationCue.LandingLv1 => landing?.Land1,
            PlayerAnimationCue.LandingLv2 => landing?.Land2,
            PlayerAnimationCue.LandingLv3 => landing?.Land3,
            PlayerAnimationCue.HardLanding => landing?.Land4,
            _ => null
        };
        if (transition != null && transition.Clip != null) return true;
        transition = null;
        return false;
    }

    public bool TryResolveLandingPresentation(PlayerLandingPresentationKey presentation, out ClipTransition transition)
    {
        transition = presentation switch
        {
            PlayerLandingPresentationKey.Land1 => landing?.Land1,
            PlayerLandingPresentationKey.Land2 => landing?.Land2,
            PlayerLandingPresentationKey.Land3 => landing?.Land3,
            PlayerLandingPresentationKey.HardLand => landing?.Land4,
            _ => null
        };
        if (transition != null && transition.Clip != null) return true;
        transition = null;
        return false;
    }

    public bool Validate(ICollection<string> errors)
    {
        bool valid = true;
        HashSet<PlayerMotionDefinition> seenDefinitions = new HashSet<PlayerMotionDefinition>();
        valid &= ValidateBindingGroup("Walk", walk?.MotionBindings, seenDefinitions, errors);
        valid &= ValidateBindingGroup("Run", run?.MotionBindings, seenDefinitions, errors);
        valid &= ValidateBindingGroup("Sprint", sprint?.MotionBindings, seenDefinitions, errors);
        valid &= ValidateBindingGroup("Landing", landing?.MotionBindings, seenDefinitions, errors);
        valid &= ValidateBindingGroup("Other", other?.MotionBindings, seenDefinitions, errors);

        if (motionCatalog == null)
        {
            errors?.Add(name + ": 缺少 MotionCatalog。");
            valid = false;
        }
        else
        {
            valid &= motionCatalog.Validate(errors);
            for (int i = 0; i < motionCatalog.Motions.Count; i++)
            {
                PlayerMotionDefinition definition = motionCatalog.Motions[i].Definition;
                if (definition == null)
                {
                    errors?.Add(name + ": Catalog Entry " + i + " 缺少 Definition。");
                    valid = false;
                    continue;
                }
                valid &= definition.Validate(errors);
                if (definition.RequiresPresentation)
                {
                    int bindingCount = CountBindings(definition);
                    if (bindingCount != 1)
                    {
                        errors?.Add(name + ": " + definition.name + " 需要且只能有一个 Animation Binding，当前为 " + bindingCount + "。");
                        valid = false;
                    }
                }
            }
        }

        valid &= ValidateTransition(idle?.Loop, "Idle.Loop", errors);
        if (walk == null) { errors?.Add(name + ": Walk 分类缺失。"); valid = false; }
        else valid &= ValidateLoop(walk.Loop, "Walk.Loop", errors);
        if (run == null) { errors?.Add(name + ": Run 分类缺失。"); valid = false; }
        else valid &= ValidateLoop(run.Loop, "Run.Loop", errors);
        if (sprint == null) { errors?.Add(name + ": Sprint 分类缺失。"); valid = false; }
        else valid &= ValidateLoop(sprint.Loop, "Sprint.Loop", errors);
        valid &= ValidateCycleBindings(PlayerLocomotionMode.Walk, walk?.Loop, "Walk.Loop", errors);
        valid &= ValidateCycleBindings(PlayerLocomotionMode.Run, run?.Loop, "Run.Loop", errors);
        valid &= ValidateCycleBindings(PlayerLocomotionMode.FastRun, sprint?.Loop, "Sprint.Loop", errors);
        valid &= ValidateTransition(dodge, "Dodge", errors);
        valid &= ValidateTransition(jump?.JumpStart, "Jump.JumpStart", errors);
        valid &= ValidateTransition(jump?.AirLoop, "Jump.AirLoop", errors);
        // 落地表现槽位允许缺省，未绑定时由运行时直接进入目标地面循环。
        if (jump == null) { errors?.Add(name + ": Jump 分类缺失。"); valid = false; }
        if (other == null) { errors?.Add(name + ": Other 分类缺失。"); valid = false; }
        return valid;
    }

    private IEnumerable<PlayerMotionAnimationBinding> EnumerateMotionBindings()
    {
        if (walk?.MotionBindings != null)
        {
            foreach (PlayerMotionAnimationBinding binding in walk.MotionBindings) yield return binding;
        }
        if (run?.MotionBindings != null)
        {
            foreach (PlayerMotionAnimationBinding binding in run.MotionBindings) yield return binding;
        }
        if (sprint?.MotionBindings != null)
        {
            foreach (PlayerMotionAnimationBinding binding in sprint.MotionBindings) yield return binding;
        }
        if (landing?.MotionBindings != null)
        {
            foreach (PlayerMotionAnimationBinding binding in landing.MotionBindings) yield return binding;
        }
        if (other?.MotionBindings != null)
        {
            foreach (PlayerMotionAnimationBinding binding in other.MotionBindings) yield return binding;
        }
    }

    private bool ValidateBindingGroup(string category, IEnumerable<PlayerMotionAnimationBinding> bindings, HashSet<PlayerMotionDefinition> seenDefinitions, ICollection<string> errors)
    {
        if (bindings == null)
        {
            errors?.Add(name + ": " + category + ".MotionBindings 缺失。");
            return false;
        }
        bool valid = true;
        int index = 0;
        foreach (PlayerMotionAnimationBinding binding in bindings)
        {
            string label = category + ".MotionBindings[" + index + "]";
            index++;
            if (binding == null)
            {
                errors?.Add(name + ": " + label + " 缺少 Binding。");
                valid = false;
                continue;
            }
            valid &= binding.Validate(label, errors);
            if (binding.Definition != null && !seenDefinitions.Add(binding.Definition))
            {
                errors?.Add(name + ": " + label + " 的 Definition " + binding.Definition.name + " 在分类之间重复。");
                valid = false;
            }
        }
        return valid;
    }

    private int CountBindings(PlayerMotionDefinition definition)
    {
        int count = 0;
        foreach (PlayerMotionAnimationBinding binding in MotionBindings)
        {
            if (binding != null && binding.Definition == definition) count++;
        }
        return count;
    }

    private static bool ValidateTransition(ClipTransition transition, string label, ICollection<string> errors)
    {
        if (transition != null && transition.Clip != null) return true;
        errors?.Add(label + ": 缺少 Clip。");
        return false;
    }

    private static bool ValidateLoop(PlayerLoopAnimationPair loop, string label, ICollection<string> errors)
    {
        return loop != null && loop.Validate(label, errors);
    }

    private bool ValidateCycleBindings(PlayerLocomotionMode locomotionMode, PlayerLoopAnimationPair loop, string label, ICollection<string> errors)
    {
        if (motionCatalog == null || loop == null || !motionCatalog.TryGetCycle(locomotionMode, out PlayerLocomotionCycleDefinition cycle))
        {
            errors?.Add(name + ": " + label + " 缺少对应的 Catalog Cycle。");
            return false;
        }
        bool valid = true;
        PlayerFoot[] feet = { PlayerFoot.Unknown, PlayerFoot.Left, PlayerFoot.Right };
        for (int i = 0; i < feet.Length; i++)
        {
            PlayerFoot foot = feet[i];
            if (!cycle.TryResolveProfile(foot, out PlayerMotionProfile profile, out _))
            {
                errors?.Add(name + ": " + label + "." + foot + " 缺少 Catalog Loop Profile。");
                valid = false;
                continue;
            }
#if UNITY_EDITOR
            valid &= ValidateProfileClip(profile, loop.Resolve(foot).Transition, label + "." + foot, errors);
#endif
        }
        return valid;
    }

    internal static bool ValidateProfilePlantMarkers(PlayerMotionProfile profile, string label, ICollection<string> errors)
    {
        if (profile == null)
        {
            errors?.Add(label + ": 缺少 MotionProfile。");
            return false;
        }
        if (profile.HasPlantMarkers) return true;
        errors?.Add(label + ": MotionProfile 缺少人工 Plant Marker。");
        return false;
    }

#if UNITY_EDITOR
    internal static bool ValidateProfileClip(PlayerMotionProfile profile, ClipTransition transition, string label, ICollection<string> errors)
    {
        if (profile == null || transition == null || transition.Clip == null) return false;
        PlayerMotionProfileMetadata metadata = profile.EditorMetadata;
        if (metadata == null || string.IsNullOrEmpty(metadata.SourceClipGuid) || metadata.SourceClipLocalId == 0)
        {
            errors?.Add(label + ": Profile 缺少源动画元数据。");
            return false;
        }
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(transition.Clip, out string clipGuid, out long clipLocalId);
        if (clipGuid != metadata.SourceClipGuid || clipLocalId != metadata.SourceClipLocalId)
        {
            errors?.Add(label + ": Clip 与 MotionProfile 源动画不一致。");
            return false;
        }
        return true;
    }
#endif

#if UNITY_EDITOR
    public void Configure(PlayerMotionCatalog catalog)
    {
        motionCatalog = catalog;
    }

    public void ConfigureLoop(PlayerLocomotionMode locomotionMode, PlayerFoot foot, AnimationClip clip, float fadeDuration)
    {
        PlayerLocomotionAnimationGroup group = locomotionMode == PlayerLocomotionMode.Walk ? walk : locomotionMode == PlayerLocomotionMode.Run ? run : sprint;
        group ??= new PlayerLocomotionAnimationGroup();
        if (locomotionMode == PlayerLocomotionMode.Walk) walk = group;
        else if (locomotionMode == PlayerLocomotionMode.Run) run = group;
        else sprint = group;
        group.Loop.Configure(foot, clip, fadeDuration);
    }

    public void ConfigureLanding(PlayerLandingPresentationKey presentation, PlayerMotionDefinition definition, AnimationClip clip, float fadeDuration)
    {
        landing ??= new PlayerLandingAnimationGroup();
        landing.ConfigureMotion(presentation, definition, clip, fadeDuration);
    }

    public void ConfigureLandingTransition(PlayerLandingPresentationKey presentation, AnimationClip clip, float fadeDuration)
    {
        landing ??= new PlayerLandingAnimationGroup();
        landing.ConfigureStandard(presentation, clip, fadeDuration);
    }
#endif
}
