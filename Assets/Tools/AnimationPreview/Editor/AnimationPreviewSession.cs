using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace ProjectTools.AnimationPreview
{
    /// <summary>
    /// 动画预览采样数据，当继承了IDisposable意味着必须实现Dispose()，清理资源滞空引用
    /// </summary>
    internal class AnimationPreviewSession : IDisposable
    {
        //独立渲染预览工具
        private PreviewRenderUtility preview;
        //模型实例
        private GameObject previewInstance;
        private GameObject gridObject;
        //底侧的辅助绘制
        private Mesh gridMesh;
        //配合mesh绘制地面
        private Material lineMaterial;
        //动画播放支持
        private Animator animator;
        //动画播放管线，比AnimatorController更底层的动画组织方式
        private PlayableGraph graph;
        private AnimationClipPlayable clipPlayable;
        private AnimationMixerPlayable sequenceMixer;
        private readonly List<AnimationClipPlayable> sequencePlayables = new List<AnimationClipPlayable>();
        private AnimationClip clip;
        private AnimationPreviewSequence sequence;
        private double sequenceLength;
        //创建一个盒子，表示空间信息
        private Bounds modelBounds;
        private Vector3 modelOrigin;
        private Vector3 trajectoryEnd;
        //预览窗口的坐标点
        private readonly List<Vector3> trajectoryPoints = new List<Vector3>();
        private Quaternion modelRotation;
        private Vector2 orbit = new Vector2(135f, 12f);
        private Vector2 lightRotation = new Vector2(35f, -35f);
        private Vector3 pan;
        private float distance = 3f;
        private float lightIntensity = 1.2f;
        private double time;
        private bool playing;
        private bool loop = true;
        private float playbackSpeed = 1f;
        private Color backgroundColor = new Color(0.105f, 0.115f, 0.13f, 1f);
        private AnimationPreviewRootMotionMode rootMotionMode;
        private PlayerFootCalibration footCalibration;

        public GameObject ModelAsset { get; private set; }
        public PlayerFootCalibration FootCalibration => footCalibration;
        public AnimationClip Clip => clip;
        public AnimationPreviewSequence Sequence => sequence;
        public Animator Animator => animator;
        public bool IsPlaying => playing;
        public bool IsSequence => sequence != null;
        public bool IsReady => animator != null && graph.IsValid() && (clip != null || sequence != null);
        public bool HasFiniteLength => !double.IsInfinity(Length);
        public bool ShowFootProbes { get; set; }
        public double Time => time;
        public double Length => sequence != null ? sequenceLength : clip == null ? 0d : clip.length;
        public Bounds ModelBounds => modelBounds;
        public int RendererCount { get; private set; }
        public int TransformCount { get; private set; }
        public int BoneCount { get; private set; }
        public string ModelError { get; private set; }
        public string CompatibilityMessage { get; private set; }
        public ModelImporterAnimationType? ModelImportType { get; private set; }
        internal int SequenceInputCount => sequencePlayables.Count;
        internal float GetSequenceInputWeight(int index) => sequenceMixer.IsValid() && index >= 0 && index < sequencePlayables.Count ? sequenceMixer.GetInputWeight(index) : 0f;
        /// <summary>
        /// 清理资源，当使用using字样时异常或正常结束均会触发或被手动调用
        /// </summary>
        public void Dispose()
        {
            DestroyGraph();
            DestroyGuideGeometry();
            if (preview != null)
            {
                preview.Cleanup();
                preview = null;
            }
            previewInstance = null;
            animator = null;
        }
        /// <summary>
        /// 设定模型
        /// </summary>
        public bool SetModel(GameObject modelAsset)
        {
            ModelAsset = modelAsset;
            footCalibration = PlayerMotionBaker.FindCalibration(modelAsset);
            ModelError = null;
            CompatibilityMessage = null;
            //清理场地
            DestroyGraph();
            DestroyGuideGeometry();
            if (preview != null) preview.Cleanup();
            preview = new PreviewRenderUtility();
            //摄像机视野角度
            preview.cameraFieldOfView = 30f;
            ConfigureLights();
            //重置模型状态
            previewInstance = null;
            animator = null;
            RendererCount = 0;
            TransformCount = 0;
            BoneCount = 0;
            ModelImportType = null;
            if (modelAsset == null)
            {
                ModelError = "请拖入一个模型 FBX 或 Prefab";
                return false;
            }
            //检查是否时持久化的Asset
            if (!EditorUtility.IsPersistent(modelAsset))
            {
                ModelError = "只接受 Project 中的模型资源，不接受场景实例";
                return false;
            }
            //拿到文件路径
            string path = AssetDatabase.GetAssetPath(modelAsset);
            //外部文件导入时会存在Importer，这里获取文件并尝试将其转为ModelImporter
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            //模型类型Legacy，Humanoid等
            if (importer != null) ModelImportType = importer.animationType;
            previewInstance = preview.InstantiatePrefabInScene(modelAsset);
            if (previewInstance == null)
            {
                ModelError = "无法在预览场景中实例化该资源";
                return false;
            }
            //控制对对象的行为，HideAndDontSave表示不显示也不保存为临时对象
            previewInstance.hideFlags = HideFlags.HideAndDontSave;
            StripNonPreviewComponents(previewInstance);
            //true表示能拿到失活节点，统计渲染数和节点数
            Renderer[] renderers = previewInstance.GetComponentsInChildren<Renderer>(true);
            RendererCount = renderers.Length;
            TransformCount = previewInstance.GetComponentsInChildren<Transform>(true).Length;
            if (RendererCount == 0)
            {
                ModelError = "该资源不包含可渲染的模型";
                return false;
            }
            animator = previewInstance.GetComponentInChildren<Animator>(true);
            Avatar avatar = FindAvatar(path, animator);
            if (animator == null)
            {
                animator = previewInstance.AddComponent<Animator>();
                animator.avatar = avatar;
            }
            else if (animator.avatar == null)
            {
                animator.avatar = avatar;
            }
            if (animator.avatar == null)
            {
                ModelError = "模型没有 Avatar请在 ModelImporter 的 Rig 页面配置动画类型和 Avatar";
                return false;
            }
            if (!animator.avatar.isValid)
            {
                ModelError = "模型 Avatar 无效，请检查骨骼映射";
                return false;
            }
            //不使用AnimatorController
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            animator.Update(0f);
            //统计骨骼数量
            BoneCount = animator.isHuman ? Enumerable.Range(0, (int)HumanBodyBones.LastBone).Count(index => animator.GetBoneTransform((HumanBodyBones)index) != null) : TransformCount;
            modelOrigin = previewInstance.transform.position;
            trajectoryEnd = modelOrigin;
            modelRotation = previewInstance.transform.rotation;
            //整个角色的总骨骼
            modelBounds = CalculateBounds(renderers);
            Focus();
            return true;
        }

        public bool SetClip(AnimationPreviewClipEntry entry)
        {
            DestroyGraph();
            sequence = null;
            sequenceLength = 0d;
            clip = entry?.Clip;
            CompatibilityMessage = null;
            time = 0d;
            playing = false;
            if (animator == null || clip == null) return false;
            if (!IsCompatibleWithModel(clip, entry.ImportType, out string message)) { CompatibilityMessage = message; return false; }
            graph = PlayableGraph.Create("Model Animation Preview");
            //设置时间推进模式为手动
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            //放动画节点，关闭IK
            clipPlayable = AnimationClipPlayable.Create(graph, clip);
            clipPlayable.SetApplyFootIK(false);
            clipPlayable.SetApplyPlayableIK(false);
            //PlayableGraph的动画输出端口
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Animation", animator);
            //设置了数据来源
            output.SetSourcePlayable(clipPlayable);
            graph.Play();
            EvaluatePose();
            return true;
        }

        public bool SetSequence(AnimationPreviewSequence value)
        {
            DestroyGraph();
            clip = null;
            sequence = value;
            sequenceLength = 0d;
            CompatibilityMessage = null;
            time = 0d;
            playing = false;
            if (animator == null || sequence == null) return false;
            if (!ValidateSequence(sequence)) return false;
            sequenceLength = CalculateSequenceLength(sequence.Entries);
            graph = PlayableGraph.Create("Model Animation Sequence Preview");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            sequenceMixer = AnimationMixerPlayable.Create(graph, sequence.Entries.Count);
            for (int index = 0; index < sequence.Entries.Count; index++)
            {
                AnimationClipPlayable playable = AnimationClipPlayable.Create(graph, sequence.Entries[index].Clip);
                playable.SetApplyFootIK(false);
                playable.SetApplyPlayableIK(false);
                graph.Connect(playable, 0, sequenceMixer, index);
                sequenceMixer.SetInputWeight(index, 0f);
                sequencePlayables.Add(playable);
            }
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Animation", animator);
            output.SetSourcePlayable(sequenceMixer);
            graph.Play();
            EvaluatePose();
            return true;
        }

        public void TogglePlayback()
        {
            if (IsReady) playing = !playing;
        }

        public void SetPlaying(bool value)
        {
            playing = value && IsReady;
        }

        public void ResetPlayback()
        {
            playing = false;
            SetTime(0d);
        }

        public void Step(int direction)
        {
            if (!IsReady) return;
            playing = false;
            AnimationClip activeClip = GetActiveClip();
            double frameDuration = 1d / Math.Max(1d, activeClip == null ? 60f : activeClip.frameRate);
            SetTime(time + frameDuration * Math.Sign(direction));
        }

        public void SetTime(double value)
        {
            if (!IsReady) return;
            time = Math.Max(0d, value);
            if (HasFiniteLength) time = Math.Min(Length, time);
            EvaluatePose();
        }
        /// <summary>
        /// 正真的烘焙算法
        /// </summary>
        internal PlayerMotionBakeResult SampleMotion(int requestedSampleRate)
        {
            return SampleMotion(requestedSampleRate, PlayerMotionBaker.FindCalibration(ModelAsset));
        }

        internal PlayerMotionBakeResult SampleMotion(int requestedSampleRate, PlayerFootCalibration calibration)
        {
            if (!IsReady) throw new InvalidOperationException("请先选择有效 Model/Avatar 和 AnimationClip");
            if (IsSequence) throw new InvalidOperationException("Animation Sequence 暂不支持 Motion Profile Bake，请切换到 Single Clip");
            if (!animator.isHuman) throw new InvalidOperationException("Motion Profile 脚步烘焙只接受有效 Humanoid Avatar");
            //足部骨骼
            Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (leftFoot == null || rightFoot == null) throw new InvalidOperationException("Humanoid Avatar 缺少 LeftFoot 或 RightFoot 骨骼，已终止脚步烘焙");
            if (calibration == null) throw new InvalidOperationException("当前模型缺少 PlayerFootCalibration，请先创建并配置左右 Foot 校准资源");
            List<string> calibrationErrors = new List<string>();
            if (!calibration.Validate(ModelAsset, calibrationErrors)) throw new InvalidOperationException(string.Join("\n", calibrationErrors));
            footCalibration = calibration;
            int rate = Math.Max(1, requestedSampleRate);
            //被采样的数量，+1意味着算上起始点，CeilToInt向上取整，确保动画非整时也能取得精确的开始点和结束点从而控制取样间隔均等
            int count = Math.Max(2, Mathf.CeilToInt(clip.length * rate) + 1);
            //XZ
            Vector2[] positions = new Vector2[count];
            //距离
            float[] distances = new float[count];
            //角度
            float[] yaws = new float[count];
            //左右足落点
            Vector3[] leftFootPositions = new Vector3[count];
            Vector3[] rightFootPositions = new Vector3[count];
            //给预览窗口用的，bake后能保持原行为
            double previousSessionTime = time;
            bool wasPlaying = playing;
            playing = false;
            previewInstance.transform.SetPositionAndRotation(modelOrigin, modelRotation);
            //使用 AnimationClip 的绝对时间采样将动画模型强制设为启动时姿态
            clip.SampleAnimation(previewInstance, 0f);
            leftFootPositions[0] = CaptureFootPosition(leftFoot, calibration.LeftFootSoleOffset);
            rightFootPositions[0] = CaptureFootPosition(rightFoot, calibration.RightFootSoleOffset);
            AnimationCurve rootPositionX = null;
            AnimationCurve rootPositionY = null;
            AnimationCurve rootPositionZ = null;
            AnimationCurve rootRotationX = null;
            AnimationCurve rootRotationY = null;
            AnimationCurve rootRotationZ = null;
            AnimationCurve rootRotationW = null;
            bool useHumanoidRootMotionCurves = clip.humanMotion && TryGetHumanoidRootMotionCurves(clip, out rootPositionX, out rootPositionY, out rootPositionZ, out rootRotationX, out rootRotationY, out rootRotationZ, out rootRotationW);
            Vector3 previousRootPosition = useHumanoidRootMotionCurves ? EvaluateRootPosition(rootPositionX, rootPositionY, rootPositionZ, 0f) : animator.transform.position;
            Quaternion previousRootRotation = useHumanoidRootMotionCurves ? EvaluateRootRotation(rootRotationX, rootRotationY, rootRotationZ, rootRotationW, 0f) : animator.transform.rotation;
            //变化量置0
            Vector3 accumulatedPosition = Vector3.zero;
            float accumulatedYaw = 0f;
            trajectoryPoints.Clear();
            trajectoryPoints.Add(modelOrigin);
            for (int i = 1; i < count; i++)
            {
                //每一份动画长度除以采样点从而平均铺满整个动画
                double sampleTime = clip.length * i / (count - 1);
                clip.SampleAnimation(previewInstance, (float)sampleTime);
                Vector3 currentRootPosition = useHumanoidRootMotionCurves ? EvaluateRootPosition(rootPositionX, rootPositionY, rootPositionZ, (float)sampleTime) : animator.transform.position;
                Quaternion currentRootRotation = useHumanoidRootMotionCurves ? EvaluateRootRotation(rootRotationX, rootRotationY, rootRotationZ, rootRotationW, (float)sampleTime) : animator.transform.rotation;
                Vector3 frameDeltaPosition = currentRootPosition - previousRootPosition;
                Quaternion frameDeltaRotation = Quaternion.Inverse(previousRootRotation) * currentRootRotation;
                //RootT 曲线已经是动画本地空间；只有回退到模型采样时才撤销模型初始旋转
                Vector3 localDelta = useHumanoidRootMotionCurves ? frameDeltaPosition : Quaternion.Inverse(modelRotation) * frameDeltaPosition;
                //去除y轴分量影响投影到xz平面上
                accumulatedPosition += Vector3.ProjectOnPlane(localDelta, Vector3.up);
                //四元数转为欧拉角并用DeltaAngle始终记录有符号的最小值（350°和-10°取后者）
                accumulatedYaw += Mathf.DeltaAngle(0f, frameDeltaRotation.eulerAngles.y);
                positions[i] = new Vector2(accumulatedPosition.x, accumulatedPosition.z);
                //Vector3.ProjectOnPlane将三维向量投影到法线平面上并开方以计算距离
                distances[i] = distances[i - 1] + Vector3.ProjectOnPlane(localDelta, Vector3.up).magnitude;
                yaws[i] = accumulatedYaw;
                //逐帧采样足部落点
                leftFootPositions[i] = CaptureFootPosition(leftFoot, calibration.LeftFootSoleOffset);
                rightFootPositions[i] = CaptureFootPosition(rightFoot, calibration.RightFootSoleOffset);
                //这里不是乘法，四元数*Vector3代表将Vector3向四元数方向旋转
                trajectoryPoints.Add(modelOrigin + modelRotation * accumulatedPosition);
                previousRootPosition = currentRootPosition;
                previousRootRotation = currentRootRotation;
            }
            //记录轨迹终点
            trajectoryEnd = trajectoryPoints[trajectoryPoints.Count - 1];
            //恢复模型到起点
            previewInstance.transform.SetPositionAndRotation(modelOrigin, modelRotation);
            //回复之前时间
            time = Math.Max(0d, Math.Min(Length, previousSessionTime));
            clipPlayable.SetTime(time);
            clipPlayable.SetDone(time >= Length);
            graph.Evaluate(0f);
            animator.Update(0f);
            if (rootMotionMode != AnimationPreviewRootMotionMode.Actual) previewInstance.transform.SetPositionAndRotation(modelOrigin, modelRotation);
            playing = wasPlaying;
            return new PlayerMotionBakeResult(clip.length, rate, positions, distances, yaws, leftFootPositions, rightFootPositions);
        }

        public void SetFootCalibration(PlayerFootCalibration calibration)
        {
            footCalibration = calibration;
        }

        internal bool TryGetSoleProbePositions(out Vector3 left, out Vector3 right)
        {
            left = right = Vector3.zero;
            if (!ShowFootProbes || footCalibration == null || animator == null || !animator.isHuman) return false;
            Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (leftFoot == null || rightFoot == null) return false;
            left = CaptureFootPosition(leftFoot, footCalibration.LeftFootSoleOffset);
            right = CaptureFootPosition(rightFoot, footCalibration.RightFootSoleOffset);
            return true;
        }

        public bool Update(double deltaTime)
        {
            if (!playing || !IsReady) return false;
            time += deltaTime * playbackSpeed;
            if (HasFiniteLength && time >= Length)
            {
                if (loop && Length > 0d) time %= Length;
                else
                {
                    time = Length;
                    playing = false;
                }
            }
            EvaluatePose();
            return true;
        }

        public Texture Render(Rect rect, bool showGrid)
        {
            if (preview == null) return null;
            preview.BeginPreview(rect, GUIStyle.none);
            Camera camera = preview.camera;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = backgroundColor;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = Math.Max(100f, distance * 20f);
            Vector3 target = modelBounds.center + pan;
            Quaternion cameraRotation = Quaternion.Euler(-orbit.y, orbit.x, 0f);
            camera.transform.position = target + cameraRotation * new Vector3(0f, 0f, -distance);
            camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position, Vector3.up);
            ConfigureLights();
            UpdateGuideGeometry(showGrid);
            preview.Render(true);
            return preview.EndPreview();
        }

        public void Orbit(Vector2 delta)
        {
            orbit.x += delta.x * 0.4f;
            orbit.y = Mathf.Clamp(orbit.y - delta.y * 0.3f, -85f, 85f);
        }

        public void Pan(Vector2 delta)
        {
            if (preview == null) return;
            float scale = distance * 0.0025f;
            pan += (-preview.camera.transform.right * delta.x + preview.camera.transform.up * delta.y) * scale;
        }

        public void Zoom(float delta)
        {
            distance = Mathf.Clamp(distance * Mathf.Exp(delta * 0.03f), 0.05f, 500f);
        }
        /// <summary>
        /// 设定距离
        /// </summary>
        public void Focus()
        {
            pan = Vector3.zero;
            //计算从中心到一个盒子角落的距离，也就是让相机在一个外接圆的范围内移动
            float radius = Math.Max(0.1f, modelBounds.extents.magnitude);
            //tan为正切，此刻在求三角的邻边，Deg2Rad三角函数单位弧度
            distance = radius / Mathf.Tan(preview.cameraFieldOfView * 0.5f * Mathf.Deg2Rad) * 1.15f;
        }

        public void SetView(Vector2 viewOrbit)
        {
            orbit = viewOrbit;
            Focus();
        }

        public void Configure(Color background, float intensity, Vector2 rotation, bool shouldLoop, float speed, AnimationPreviewRootMotionMode motionMode)
        {
            backgroundColor = background;
            lightIntensity = intensity;
            lightRotation = rotation;
            loop = shouldLoop;
            playbackSpeed = speed;
            rootMotionMode = motionMode;
            ConfigureLights();
            EvaluatePose();
        }
        /// <summary>
        /// 将动画设定为0秒位置
        /// </summary>
        private void EvaluatePose()
        {
            if (!IsReady) return;
            //不实际移动时设定其到原位置
            if (rootMotionMode != AnimationPreviewRootMotionMode.Actual)
            {
                previewInstance.transform.SetPositionAndRotation(modelOrigin, modelRotation);
            }
            if (IsSequence) EvaluateSequence();
            else
            {
                clipPlayable.SetTime(time);
                clipPlayable.SetDone(time >= Length);
            }
            graph.Evaluate(0f);
            animator.Update(0f);
            trajectoryEnd = previewInstance.transform.position;
            if (rootMotionMode != AnimationPreviewRootMotionMode.Actual) previewInstance.transform.SetPositionAndRotation(modelOrigin, modelRotation);
        }

        private void DestroyGraph()
        {
            if (graph.IsValid()) graph.Destroy();
            clipPlayable = default;
            sequenceMixer = default;
            sequencePlayables.Clear();
        }

        private bool ValidateSequence(AnimationPreviewSequence value)
        {
            if (value.Entries.Count == 0)
            {
                CompatibilityMessage = "Animation Sequence 至少需要一个条目。";
                return false;
            }
            double previousEnd = 0d;
            for (int index = 0; index < value.Entries.Count; index++)
            {
                AnimationPreviewSequenceEntry entry = value.Entries[index];
                if (entry == null || !AnimationPreviewClipLibrary.IsUsableClip(entry.Clip))
                {
                    CompatibilityMessage = $"Sequence 第 {index + 1} 个条目没有可用 AnimationClip。";
                    return false;
                }
                if (float.IsNaN(entry.StartTime) || float.IsInfinity(entry.StartTime) || entry.StartTime < 0f || float.IsNaN(entry.Duration) || entry.Duration <= 0f || float.IsNegativeInfinity(entry.Duration))
                {
                    CompatibilityMessage = $"Sequence 第 {index + 1} 个条目的 StartTime 或 Duration 无效。";
                    return false;
                }
                if (float.IsNaN(entry.BlendDuration) || float.IsInfinity(entry.BlendDuration) || entry.BlendDuration < 0f || (!float.IsPositiveInfinity(entry.Duration) && entry.BlendDuration > entry.Duration))
                {
                    CompatibilityMessage = $"Sequence 第 {index + 1} 个条目的 BlendDuration 无效。";
                    return false;
                }
                if (index == 0 && !Mathf.Approximately(entry.StartTime, 0f))
                {
                    CompatibilityMessage = "Sequence 的第一个条目必须从 0 秒开始。";
                    return false;
                }
                if (index > 0 && (double.IsPositiveInfinity(previousEnd) || Math.Abs(entry.StartTime - previousEnd) > 0.0001d))
                {
                    CompatibilityMessage = $"Sequence 第 {index + 1} 个条目的 StartTime 必须衔接前一条目的结束时间。";
                    return false;
                }
                if (!IsCompatibleWithModel(entry.Clip, GetImportType(entry), out string message))
                {
                    CompatibilityMessage = $"Sequence 第 {index + 1} 个条目：{message}";
                    return false;
                }
                previousEnd = float.IsPositiveInfinity(entry.Duration) ? double.PositiveInfinity : entry.StartTime + entry.Duration;
            }
            return true;
        }

        private bool IsCompatibleWithModel(AnimationClip candidate, ModelImporterAnimationType? importType, out string message)
        {
            message = null;
            if (ModelImportType == ModelImporterAnimationType.Generic && importType == ModelImporterAnimationType.Generic && !AnimationPreviewClipLibrary.HasMatchingTransformBindings(candidate, animator.transform, out string missingPath))
            {
                message = "Generic 骨架路径不匹配：" + missingPath;
                return false;
            }
            if (ModelImportType == ModelImporterAnimationType.Human && !animator.isHuman)
            {
                message = "模型标记为 Humanoid，但当前 Avatar 无法生成人形 Animator";
                return false;
            }
            if (importType == ModelImporterAnimationType.Human && !animator.isHuman)
            {
                message = "该动画需要有效的 Humanoid 模型";
                return false;
            }
            return true;
        }

        private void EvaluateSequence()
        {
            int currentIndex = GetSequenceEntryIndex(time);
            int nextIndex = -1;
            float blend = GetOutgoingBlendDuration(currentIndex);
            float blendProgress = 0f;
            if (blend > 0f && currentIndex < sequence.Entries.Count - 1)
            {
                AnimationPreviewSequenceEntry current = sequence.Entries[currentIndex];
                double blendStart = current.StartTime + current.Duration - blend;
                if (time >= blendStart && time < current.StartTime + current.Duration)
                {
                    nextIndex = currentIndex + 1;
                    blendProgress = Mathf.InverseLerp((float)blendStart, current.StartTime + current.Duration, (float)time);
                }
            }
            for (int index = 0; index < sequencePlayables.Count; index++)
            {
                AnimationPreviewSequenceEntry entry = sequence.Entries[index];
                double localTime = index < currentIndex ? entry.Duration : index > nextIndex && index != currentIndex ? 0d : Math.Max(0d, time - GetEntryPlaybackStartTime(index));
                sequencePlayables[index].SetTime(GetClipSampleTime(entry.Clip, localTime));
                sequencePlayables[index].SetDone(false);
                sequenceMixer.SetInputWeight(index, 0f);
            }
            sequenceMixer.SetInputWeight(currentIndex, nextIndex >= 0 ? Mathf.Lerp(1f, 0f, blendProgress) : 1f);
            if (nextIndex >= 0) sequenceMixer.SetInputWeight(nextIndex, Mathf.Lerp(0f, 1f, blendProgress));
        }

        private int GetSequenceEntryIndex(double value)
        {
            int index = 0;
            for (int candidate = 1; candidate < sequence.Entries.Count; candidate++)
            {
                if (value < sequence.Entries[candidate].StartTime) break;
                index = candidate;
            }
            return index;
        }

        private float GetOutgoingBlendDuration(int index)
        {
            if (index < 0 || index >= sequence.Entries.Count - 1) return 0f;
            AnimationPreviewSequenceEntry entry = sequence.Entries[index];
            return float.IsPositiveInfinity(entry.Duration) ? 0f : Mathf.Min(entry.BlendDuration, entry.Duration);
        }

        private double GetEntryPlaybackStartTime(int index)
        {
            if (index == 0) return sequence.Entries[index].StartTime;
            return sequence.Entries[index].StartTime - GetOutgoingBlendDuration(index - 1);
        }

        private static double GetClipSampleTime(AnimationClip animationClip, double localTime)
        {
            if (animationClip == null || animationClip.length <= 0f) return 0d;
            if (animationClip.isLooping && localTime > animationClip.length) return localTime % animationClip.length;
            return Math.Min(animationClip.length, localTime);
        }

        private AnimationClip GetActiveClip()
        {
            return IsSequence ? sequence.Entries[GetSequenceEntryIndex(time)].Clip : clip;
        }

        private static ModelImporterAnimationType? GetImportType(AnimationPreviewSequenceEntry entry)
        {
            string path = AssetDatabase.GetAssetPath(entry.Source ?? entry.Clip);
            return AssetImporter.GetAtPath(path) is ModelImporter importer ? importer.animationType : null;
        }

        private static double CalculateSequenceLength(IReadOnlyList<AnimationPreviewSequenceEntry> entries)
        {
            AnimationPreviewSequenceEntry last = entries[entries.Count - 1];
            return float.IsPositiveInfinity(last.Duration) ? double.PositiveInfinity : last.StartTime + last.Duration;
        }
        /// <summary>
        /// 设置灯光
        /// </summary>
        private void ConfigureLights()
        {
            if (preview?.lights == null || preview.lights.Length < 2) return;
            preview.lights[0].intensity = lightIntensity;
            preview.lights[0].transform.rotation = Quaternion.Euler(lightRotation.x, lightRotation.y, 0f);
            preview.lights[1].intensity = lightIntensity * 0.45f;
            preview.lights[1].transform.rotation = Quaternion.Euler(340f, 135f, 0f);
            preview.ambientColor = new Color(0.25f, 0.25f, 0.25f);
        }

        private void UpdateGuideGeometry(bool showGrid)
        {
            if (gridObject == null) CreateGuideGeometry();
            if (gridObject == null) return;
            gridObject.SetActive(showGrid || rootMotionMode == AnimationPreviewRootMotionMode.Trajectory || ShowFootProbes);
            if (!gridObject.activeSelf) return;
            List<Vector3> vertices = new List<Vector3>();
            List<int> indices = new List<int>();
            float height = modelBounds.min.y;
            float radius = Math.Max(1f, modelBounds.extents.magnitude);
            int lines = 10;
            float step = Math.Max(0.1f, radius / 5f);
            float extent = lines * step;
            if (showGrid)
            {
                for (int index = -lines; index <= lines; index++)
                {
                    float offset = index * step;
                    AddLine(vertices, indices, new Vector3(-extent, height, offset), new Vector3(extent, height, offset));
                    AddLine(vertices, indices, new Vector3(offset, height, -extent), new Vector3(offset, height, extent));
                }
            }
            if (rootMotionMode == AnimationPreviewRootMotionMode.Trajectory && previewInstance != null)
            {
                if (trajectoryPoints.Count > 1)
                {
                    for (int i = 1; i < trajectoryPoints.Count; i++) AddLine(vertices, indices, trajectoryPoints[i - 1], trajectoryPoints[i]);
                }
                else AddLine(vertices, indices, modelOrigin, trajectoryEnd);
            }
            if (TryGetSoleProbePositions(out Vector3 leftSole, out Vector3 rightSole))
            {
                Vector3 ground = modelOrigin + modelRotation * (Vector3.up * footCalibration.VirtualGroundHeight);
                AddLine(vertices, indices, leftSole, leftSole + Vector3.up * 0.12f);
                AddLine(vertices, indices, rightSole, rightSole + Vector3.up * 0.12f);
                AddProbeCross(vertices, indices, leftSole, 0.04f);
                AddProbeCross(vertices, indices, rightSole, 0.04f);
                AddLine(vertices, indices, ground + modelRotation * new Vector3(-0.1f, 0f, -0.1f), ground + modelRotation * new Vector3(0.1f, 0f, 0.1f));
                AddLine(vertices, indices, ground + modelRotation * new Vector3(-0.1f, 0f, 0.1f), ground + modelRotation * new Vector3(0.1f, 0f, -0.1f));
            }
            gridMesh.Clear();
            gridMesh.SetVertices(vertices);
            gridMesh.SetIndices(indices, MeshTopology.Lines, 0);
            gridMesh.RecalculateBounds();
        }

        private void CreateGuideGeometry()
        {
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) return;
            lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            lineMaterial.SetColor("_Color", new Color(0.32f, 0.36f, 0.42f, 0.65f));
            gridMesh = new Mesh { name = "Animation Preview Guides", hideFlags = HideFlags.HideAndDontSave };
            gridObject = new GameObject("Animation Preview Guides") { hideFlags = HideFlags.HideAndDontSave };
            MeshFilter filter = gridObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = gridObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = gridMesh;
            renderer.sharedMaterial = lineMaterial;
            preview.AddSingleGO(gridObject);
        }

        private void DestroyGuideGeometry()
        {
            if (gridMesh != null) UnityEngine.Object.DestroyImmediate(gridMesh);
            if (lineMaterial != null) UnityEngine.Object.DestroyImmediate(lineMaterial);
            gridMesh = null;
            lineMaterial = null;
            gridObject = null;
        }

        private static void AddLine(ICollection<Vector3> vertices, ICollection<int> indices, Vector3 start, Vector3 end)
        {
            int index = vertices.Count;
            vertices.Add(start);
            vertices.Add(end);
            indices.Add(index);
            indices.Add(index + 1);
        }

        private static void AddProbeCross(ICollection<Vector3> vertices, ICollection<int> indices, Vector3 center, float radius)
        {
            AddLine(vertices, indices, center + Vector3.left * radius, center + Vector3.right * radius);
            AddLine(vertices, indices, center + Vector3.forward * radius, center + Vector3.back * radius);
        }
        /// <summary>
        /// 将足部的局部坐标转化为空间坐标
        /// </summary>
        private static Vector3 CaptureFootPosition(Transform foot, Vector3 soleOffset)
        {
            return foot.TransformPoint(soleOffset);
        }

        private static bool TryGetHumanoidRootMotionCurves(AnimationClip sourceClip, out AnimationCurve positionX, out AnimationCurve positionY, out AnimationCurve positionZ, out AnimationCurve rotationX, out AnimationCurve rotationY, out AnimationCurve rotationZ, out AnimationCurve rotationW)
        {
            positionX = AnimationUtility.GetEditorCurve(sourceClip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootT.x"));
            positionY = AnimationUtility.GetEditorCurve(sourceClip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootT.y"));
            positionZ = AnimationUtility.GetEditorCurve(sourceClip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootT.z"));
            rotationX = AnimationUtility.GetEditorCurve(sourceClip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootQ.x"));
            rotationY = AnimationUtility.GetEditorCurve(sourceClip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootQ.y"));
            rotationZ = AnimationUtility.GetEditorCurve(sourceClip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootQ.z"));
            rotationW = AnimationUtility.GetEditorCurve(sourceClip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootQ.w"));
            return positionX != null && positionY != null && positionZ != null && rotationX != null && rotationY != null && rotationZ != null && rotationW != null;
        }

        private static Vector3 EvaluateRootPosition(AnimationCurve positionX, AnimationCurve positionY, AnimationCurve positionZ, float time)
        {
            return new Vector3(positionX.Evaluate(time), positionY.Evaluate(time), positionZ.Evaluate(time));
        }

        private static Quaternion EvaluateRootRotation(AnimationCurve rotationX, AnimationCurve rotationY, AnimationCurve rotationZ, AnimationCurve rotationW, float time)
        {
            Quaternion value = new Quaternion(rotationX.Evaluate(time), rotationY.Evaluate(time), rotationZ.Evaluate(time), rotationW.Evaluate(time));
            float magnitudeSquared = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
            return magnitudeSquared > 0.0001f ? value.normalized : Quaternion.identity;
        }
        /// <summary>
        /// 拿Avatar，优先模型的
        /// </summary>
        private static Avatar FindAvatar(string assetPath, Animator existingAnimator)
        {
            if (existingAnimator != null && existingAnimator.avatar != null) return existingAnimator.avatar;
            return AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Avatar>().FirstOrDefault(avatar => avatar.isValid);
        }

        private static Bounds CalculateBounds(IReadOnlyList<Renderer> renderers)
        {
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Count; index++) bounds.Encapsulate(renderers[index].bounds);
            return bounds;
        }
        //剥除所有组件，保留Animator
        private static void StripNonPreviewComponents(GameObject root)
        {
            foreach (Behaviour behaviour in root.GetComponentsInChildren<Behaviour>(true))
            {
                if (behaviour is Animator) continue;
                UnityEngine.Object.DestroyImmediate(behaviour);
            }
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
            foreach (Rigidbody rigidbody in root.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(rigidbody);
        }
    }
}
