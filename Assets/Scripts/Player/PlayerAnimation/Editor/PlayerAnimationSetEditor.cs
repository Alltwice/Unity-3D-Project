using System.Collections.Generic;
using Animancer;
using ProjectTools.AnimationPreview;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlayerAnimationSet))]
public class PlayerAnimationSetEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        if (!GUILayout.Button("Validate Motion Bindings")) return;
        PlayerAnimationSet animationSet = (PlayerAnimationSet)target;
        List<string> errors = new List<string>();
        bool valid = animationSet.Validate(errors);
        HashSet<PlayerMotionProfile> validatedProfiles = new HashSet<PlayerMotionProfile>();
        foreach (PlayerMotionAnimationBinding binding in animationSet.MotionBindings)
        {
            if (binding?.Definition == null) continue;
            valid &= ValidateProfile(binding.Definition.Profile, binding.Definition.name + ".Default", errors, validatedProfiles);
            if (binding.Definition.RequiresFootProfiles)
            {
                valid &= ValidateProfile(binding.Definition.LeftFootProfile, binding.Definition.name + ".Left", errors, validatedProfiles);
                valid &= ValidateProfile(binding.Definition.RightFootProfile, binding.Definition.name + ".Right", errors, validatedProfiles);
            }
        }
        if (animationSet.MotionCatalog == null)
        {
            errors.Add("MotionCatalog 缺失。");
            valid = false;
        }
        else
        {
            valid &= animationSet.MotionCatalog.Validate(errors);
            valid &= ValidateCycle(animationSet, PlayerLocomotionMode.Walk, "Walk", errors, validatedProfiles);
            valid &= ValidateCycle(animationSet, PlayerLocomotionMode.Run, "Run", errors, validatedProfiles);
            valid &= ValidateCycle(animationSet, PlayerLocomotionMode.FastRun, "Sprint", errors, validatedProfiles);
        }
        valid &= ValidateCue(animationSet, PlayerAnimationCue.JumpStart, "Jump.JumpStart", errors);
        WarnIfCueUnbound(animationSet, PlayerAnimationCue.LandingLv1, "Landing.Land1");
        WarnIfCueUnbound(animationSet, PlayerAnimationCue.LandingLv2, "Landing.Land2");
        WarnIfCueUnbound(animationSet, PlayerAnimationCue.LandingLv3, "Landing.Land3");
        WarnIfCueUnbound(animationSet, PlayerAnimationCue.HardLanding, "Landing.Land4");
        if (valid) Debug.Log(animationSet.name + ": Motion bindings and baked sources valid.", animationSet);
        else Debug.LogError(string.Join("\n", errors), animationSet);
    }

    private static bool ValidateCycle(PlayerAnimationSet animationSet, PlayerLocomotionMode mode, string label, ICollection<string> errors, ISet<PlayerMotionProfile> validatedProfiles)
    {
        if (!animationSet.MotionCatalog.TryGetLocomotion(mode, out PlayerLocomotionDefinition cycle))
        {
            errors.Add(label + ": Catalog Cycle 查询失败。");
            return false;
        }
        bool valid = true;
        PlayerFoot[] feet = { PlayerFoot.Unknown, PlayerFoot.Left, PlayerFoot.Right };
        for (int i = 0; i < feet.Length; i++)
        {
            PlayerFoot foot = feet[i];
            if (!animationSet.TryResolveLoop(mode, foot, out _))
            {
                errors.Add(label + ": 语义 Loop 查询失败。");
                valid = false;
                continue;
            }
            if (!cycle.TryResolveProfile(foot, out PlayerMotionProfile profile, out _))
            {
                errors.Add(label + "." + foot + ": Catalog Loop Profile 查询失败。");
                valid = false;
                continue;
            }
            valid &= ValidateProfile(profile, label + "." + foot, errors, validatedProfiles);
        }
        return valid;
    }

    private static bool ValidateCue(PlayerAnimationSet animationSet, PlayerAnimationCue cue, string label, ICollection<string> errors)
    {
        if (animationSet.TryResolveCue(cue, out ClipTransition transition) && transition != null && transition.Clip != null) return true;
        errors.Add(label + ": 语义 Cue 查询失败。");
        return false;
    }

    private static void WarnIfCueUnbound(PlayerAnimationSet animationSet, PlayerAnimationCue cue, string label)
    {
        if (animationSet.TryResolveCue(cue, out ClipTransition transition) && transition != null && transition.Clip != null) return;
        Debug.LogWarning(animationSet.name + ": " + label + " 未绑定，落地时将跳过该表现过渡。", animationSet);
    }

    private static bool ValidateProfile(PlayerMotionProfile profile, string label, ICollection<string> errors, ISet<PlayerMotionProfile> validatedProfiles)
    {
        if (profile == null)
        {
            errors.Add(label + ": 缺少 MotionProfile。");
            return false;
        }
        return validatedProfiles.Add(profile) ? PlayerMotionBaker.Validate(profile, errors) : true;
    }
}
