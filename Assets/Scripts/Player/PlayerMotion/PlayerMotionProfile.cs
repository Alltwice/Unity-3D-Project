using System;
using System.Collections.Generic;
using UnityEngine;
/// <summary>
/// Bake后的数据证明，用于数据校验
/// </summary>
[Serializable]
public class PlayerMotionProfileMetadata
{
    //bake算法版本控制，用于数据校验
    [SerializeField] private int bakeVersion;
    [SerializeField] private int sampleRate;
    [SerializeField] private string sourceClipGuid;
    [SerializeField] private long sourceClipLocalId;
    [SerializeField] private string modelGuid;
    [SerializeField] private string sourceDependencyHash;
    [SerializeField] private string footCalibrationGuid;
    [SerializeField] private string footCalibrationHash;

    public int BakeVersion => bakeVersion;
    public int SampleRate => sampleRate;
    public string SourceClipGuid => sourceClipGuid;
    public long SourceClipLocalId => sourceClipLocalId;
    public string ModelGuid => modelGuid;
    public string SourceDependencyHash => sourceDependencyHash;
    public string FootCalibrationGuid => footCalibrationGuid;
    public string FootCalibrationHash => footCalibrationHash;

#if UNITY_EDITOR
    //保存设定数据
    public void Set(int version, int bakedSampleRate, string clipGuid, long clipLocalId, string bakedModelGuid, string dependencyHash, string calibrationGuid, string calibrationHash)
    {
        bakeVersion = version;
        sampleRate = bakedSampleRate;
        sourceClipGuid = clipGuid;
        sourceClipLocalId = clipLocalId;
        modelGuid = bakedModelGuid;
        sourceDependencyHash = dependencyHash;
        footCalibrationGuid = calibrationGuid;
        footCalibrationHash = calibrationHash;
    }
#endif
}
/// <summary>
/// 烘焙运动数据和自动或人工 Plant Marker 文件
/// </summary>
[CreateAssetMenu(fileName = "PlayerMotionProfile", menuName = "Player/Motion/Profile")]
public class PlayerMotionProfile : ScriptableObject
{
    public const int CurrentBakeVersion = 4;
    public const int CurrentFootPlantDetectionVersion = 1;
    //动画持续时间
    [Min(0f)] [SerializeField] private float duration;
    //一轮动画位移
    [Min(0f)] [SerializeField] private float cycleDistance;
    [Min(1)] [SerializeField] private int sampleRate = 60;
    [SerializeField] private Vector2[] cumulativePlanarPosition = Array.Empty<Vector2>();
    [SerializeField] private float[] cumulativeTravelDistance = Array.Empty<float>();
    [SerializeField] private float[] cumulativeYaw = Array.Empty<float>();
    [SerializeField] private PlayerFootMotionChannel leftFoot = new PlayerFootMotionChannel();
    [SerializeField] private PlayerFootMotionChannel rightFoot = new PlayerFootMotionChannel();
    [SerializeField] private List<PlayerFootPlantMarker> plantMarkers = new List<PlayerFootPlantMarker>();
    [SerializeField] private PlayerFootPlantDetectionMode footPlantDetectionMode;
    [SerializeField] private PlayerPlantMarkerMode plantMarkerMode;
    //区分旧资产的默认枚举值，批量迁移只对未持久化的旧资产执行一次名称推断
    [SerializeField] private bool detectionModePersisted;
    [SerializeField] private int footPlantDetectionVersion;
    //保存元数据
    [SerializeField] private PlayerMotionProfileMetadata editorMetadata = new PlayerMotionProfileMetadata();

    public float Duration => duration;
    public float CycleDistance => cycleDistance;
    public int SampleRate => sampleRate;
    public int SampleCount => cumulativePlanarPosition?.Length ?? 0;
    public bool HasPlanarPosition => SampleCount >= 2;
    public bool HasTravelDistance => cumulativeTravelDistance != null && cumulativeTravelDistance.Length == SampleCount;
    public bool HasYaw => cumulativeYaw != null && cumulativeYaw.Length == SampleCount;
    public PlayerFootMotionChannel LeftFoot => leftFoot;
    public PlayerFootMotionChannel RightFoot => rightFoot;
    public bool HasFootData => leftFoot != null && rightFoot != null && leftFoot.HasData && rightFoot.HasData;
    public IReadOnlyList<PlayerFootPlantMarker> PlantMarkers => plantMarkers == null ? (IReadOnlyList<PlayerFootPlantMarker>)Array.Empty<PlayerFootPlantMarker>() : plantMarkers;
    public bool HasPlantMarkers => plantMarkers != null && plantMarkers.Count > 0;
    public PlayerFootPlantDetectionMode FootPlantDetectionMode => footPlantDetectionMode;
    public bool HasPersistedDetectionMode => detectionModePersisted;
    public PlayerPlantMarkerMode PlantMarkerMode => plantMarkerMode;
    public int FootPlantDetectionVersion => footPlantDetectionVersion;
    public PlayerMotionProfileMetadata EditorMetadata => editorMetadata;
    /// <summary>
    /// 查询此刻的移动数据
    /// </summary>
    public Vector3 EvaluatePlanarPosition(float progress)
    {
        Vector2 value = Evaluate(cumulativePlanarPosition, progress);
        return new Vector3(value.x, 0f, value.y);
    }

    public float EvaluateTravelDistance(float progress) => Evaluate(cumulativeTravelDistance, progress);
    public float EvaluateYaw(float progress) => Evaluate(cumulativeYaw, progress);

    /// <summary>
    /// <summary>
    /// 通过动画时间查找最近的脚步落点
    /// </summary>
    public PlayerFoot ResolveLastPlantFoot(float time, PlayerFoot fallback)
    {
        if (!HasPlantMarkers || !IsFinite(time)) return fallback;
        float normalizedTime = Mathf.Clamp01(time);
        PlayerFoot resolved = fallback;
        float latestTime = float.NegativeInfinity;
        for (int index = 0; index < plantMarkers.Count; index++)
        {
            PlayerFootPlantMarker marker = plantMarkers[index];
            if (!IsValidPlantMarker(marker) || marker.NormalizedTime > normalizedTime || marker.NormalizedTime < latestTime) continue;
            latestTime = marker.NormalizedTime;
            resolved = marker.Foot;
        }
        return resolved;
    }

    /// <summary>
    /// 按 Simulation 提交的归一化循环相位解析当前两次 Plant 之间的关系
    /// </summary>
    public bool TryEvaluateLoopPhase(float normalizedPhase, out PlayerFoot lastPlantFoot, out PlayerFoot nextPlantFoot, out float stepProgress)
    {
        lastPlantFoot = PlayerFoot.Unknown;
        nextPlantFoot = PlayerFoot.Unknown;
        stepProgress = 0f;
        if (!IsFinite(normalizedPhase) || !TryValidateLoopPhaseConfiguration(out int markerCount)) return false;
        float currentTime = Mathf.Repeat(normalizedPhase, 1f);
        int previousIndex;
        int nextIndex;
        float previousTime;
        float nextTime;
        //当前时间在第一个标记点之前
        if (currentTime < plantMarkers[0].NormalizedTime)
        {
            //拿到最后一个标记点
            previousIndex = markerCount - 1;
            nextIndex = 0;
            previousTime = plantMarkers[previousIndex].NormalizedTime - 1f;
            nextTime = plantMarkers[nextIndex].NormalizedTime;
        }
        //否则循环寻找最后一个不晚于当前时间的标记
        else
        {
            previousIndex = 0;
            while (previousIndex + 1 < markerCount && plantMarkers[previousIndex + 1].NormalizedTime <= currentTime) previousIndex++;
            nextIndex = previousIndex + 1 < markerCount ? previousIndex + 1 : 0;
            previousTime = plantMarkers[previousIndex].NormalizedTime;
            nextTime = nextIndex == 0 ? plantMarkers[0].NormalizedTime + 1f : plantMarkers[nextIndex].NormalizedTime;
        }
        //整段时间
        float segmentLength = nextTime - previousTime;
        if (!(segmentLength > 0f)) return false;
        //脚步相位
        stepProgress = (currentTime - previousTime) / segmentLength;
        if (!IsFinite(stepProgress) || stepProgress < 0f || stepProgress >= 1f) return false;
        lastPlantFoot = plantMarkers[previousIndex].Foot;
        nextPlantFoot = plantMarkers[nextIndex].Foot;
        return true;
    }

    public bool Validate(ICollection<string> errors)
    {
        bool valid = true;
        if (!IsFinite(duration) || duration <= 0f) { errors?.Add(name + ": Duration 必须是大于 0 的有限值。"); valid = false; }
        if (sampleRate <= 0 || SampleCount < 2) { errors?.Add(name + ": SampleRate / SampleCount 无效。"); valid = false; }
        if (!HasTravelDistance || !HasYaw) { errors?.Add(name + ": Motion channel 的采样数量不一致。"); valid = false; }
        if (leftFoot != null && rightFoot != null && (leftFoot.HasData || rightFoot.HasData))
        {
            valid &= leftFoot.Validate(name + ".LeftFoot", SampleCount, errors);
            valid &= rightFoot.Validate(name + ".RightFoot", SampleCount, errors);
        }
        float previousDistance = float.NegativeInfinity;
        for (int i = 0; i < SampleCount; i++)
        {
            Vector2 position = cumulativePlanarPosition[i];
            float distance = cumulativeTravelDistance[i];
            float yaw = cumulativeYaw[i];
            if (!IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(distance) || !IsFinite(yaw)) { errors?.Add(name + ": 采样数据包含 NaN 或 Infinity。"); valid = false; break; }
            if (distance + 0.0001f < previousDistance) { errors?.Add(name + ": CumulativeTravelDistance 出现反向下降。"); valid = false; break; }
            if (i > 0 && Mathf.Abs(yaw - cumulativeYaw[i - 1]) > 180.001f) { errors?.Add(name + ": CumulativeYaw 存在未展开的 ±180 跳变。"); valid = false; break; }
            previousDistance = distance;
        }
        valid &= ValidatePlantMarkers(errors);
        return valid;
    }

    /// <summary>
    /// 校验循环相位查询所需的 Plant Marker 配置和采样间隔约束
    /// </summary>
    public bool ValidateLoopPhase(ICollection<string> errors)
    {
        bool valid = true;
        if (!IsFinite(duration) || duration <= 0f) { errors?.Add(name + ": Loop Phase 的 Duration 必须是大于 0 的有限值。"); valid = false; }
        if (!IsFinite(cycleDistance) || cycleDistance <= 0f) { errors?.Add(name + ": Loop Phase 的 CycleDistance 必须是大于 0 的有限值。"); valid = false; }
        if (SampleCount < 2) { errors?.Add(name + ": Loop Phase 至少需要两个采样点。"); valid = false; }
        if (plantMarkers == null || plantMarkers.Count < 2) { errors?.Add(name + ": Loop Phase 至少需要两个 Plant Marker。"); valid = false; }
        if (plantMarkers == null || plantMarkers.Count == 0) return valid;
        float sampleInterval = SampleCount > 1 ? 1f / (SampleCount - 1) : 0f;
        for (int index = 0; index < plantMarkers.Count; index++)
        {
            PlayerFootPlantMarker marker = plantMarkers[index];
            if (!IsValidLoopPlantMarker(marker)) { errors?.Add(name + ": Loop Plant Marker 必须使用 Left/Right，且时间位于 (0, 1) 的有限值。"); valid = false; }
            if (index == 0 || !IsValidLoopPlantMarker(plantMarkers[index - 1]) || !IsValidLoopPlantMarker(marker)) continue;
            PlayerFootPlantMarker previous = plantMarkers[index - 1];
            float segmentLength = marker.NormalizedTime - previous.NormalizedTime;
            if (!(segmentLength > 0f)) { errors?.Add(name + ": Loop Plant Marker 时间必须严格递增。"); valid = false; }
            if (previous.Foot == marker.Foot) { errors?.Add(name + ": 相邻 Loop Plant Marker 必须左右脚交替。"); valid = false; }
            if (sampleInterval > 0f && segmentLength < sampleInterval) { errors?.Add(name + ": Loop Plant Marker 之间至少需要跨越一个采样区间。"); valid = false; }
        }
        PlayerFootPlantMarker first = plantMarkers[0];
        PlayerFootPlantMarker last = plantMarkers[plantMarkers.Count - 1];
        if (IsValidLoopPlantMarker(first) && IsValidLoopPlantMarker(last))
        {
            float seamLength = 1f - last.NormalizedTime + first.NormalizedTime;
            if (!(seamLength > 0f)) { errors?.Add(name + ": Loop Plant Marker 的跨接缝步段长度必须大于 0。"); valid = false; }
            if (last.Foot == first.Foot) { errors?.Add(name + ": 跨接缝 Loop Plant Marker 必须左右脚交替。"); valid = false; }
            if (sampleInterval > 0f && seamLength < sampleInterval) { errors?.Add(name + ": 跨接缝 Loop Plant Marker 至少需要跨越一个采样区间。"); valid = false; }
        }
        return valid;
    }

    private bool ValidatePlantMarkers(ICollection<string> errors)
    {
        if (plantMarkers == null || plantMarkers.Count == 0) return true;
        bool valid = true;
        float sampleInterval = SampleCount > 1 ? 1f / (SampleCount - 1) : 1f;
        for (int index = 0; index < plantMarkers.Count; index++)
        {
            PlayerFootPlantMarker marker = plantMarkers[index];
            if (!IsValidPlantFoot(marker.Foot)) { errors?.Add(name + ": Plant Marker 的脚必须是 Left 或 Right。"); valid = false; }
            if (!IsFinite(marker.NormalizedTime) || marker.NormalizedTime < 0f || marker.NormalizedTime > 1f) { errors?.Add(name + ": Plant Marker 时间必须是 0 到 1 的有限值。"); valid = false; }
            if (!IsFinite(marker.Confidence) || marker.Confidence < 0f || marker.Confidence > 1f) { errors?.Add(name + ": Plant Marker Confidence 必须是 0 到 1 的有限值。"); valid = false; }
            if (index > 0 && IsFinite(plantMarkers[index - 1].NormalizedTime) && IsFinite(marker.NormalizedTime) && marker.NormalizedTime < plantMarkers[index - 1].NormalizedTime)
            {
                errors?.Add(name + ": Plant Marker 必须按时间排序。");
                valid = false;
            }
        }
        for (int leftIndex = 0; leftIndex < plantMarkers.Count; leftIndex++)
        {
            PlayerFootPlantMarker left = plantMarkers[leftIndex];
            if (!IsValidPlantMarker(left)) continue;
            for (int rightIndex = leftIndex + 1; rightIndex < plantMarkers.Count; rightIndex++)
            {
                PlayerFootPlantMarker right = plantMarkers[rightIndex];
                if (!IsValidPlantMarker(right) || left.Foot != right.Foot || Mathf.Abs(left.NormalizedTime - right.NormalizedTime) > sampleInterval) continue;
                errors?.Add(name + ": 同一脚的 Plant Marker 不能位于同一采样区间。");
                valid = false;
            }
        }
        return valid;
    }

#if UNITY_EDITOR
    public void SetBakedData(float bakedDuration, int bakedSampleRate, Vector2[] planarPosition, float[] travelDistance, float[] yaw, string clipGuid, long clipLocalId, string modelGuid, string dependencyHash)
    {
        SetBakedData(bakedDuration, bakedSampleRate, planarPosition, travelDistance, yaw, clipGuid, clipLocalId, modelGuid, dependencyHash, null, null, null, null);
    }

    public void SetBakedData(float bakedDuration, int bakedSampleRate, Vector2[] planarPosition, float[] travelDistance, float[] yaw, string clipGuid, long clipLocalId, string modelGuid, string dependencyHash, PlayerFootMotionBakeData leftFootData, PlayerFootMotionBakeData rightFootData, string calibrationGuid, string calibrationHash)
    {
        duration = bakedDuration;
        sampleRate = bakedSampleRate;
        cumulativePlanarPosition = planarPosition ?? Array.Empty<Vector2>();
        cumulativeTravelDistance = travelDistance ?? Array.Empty<float>();
        cumulativeYaw = yaw ?? Array.Empty<float>();
        if (footPlantDetectionMode == PlayerFootPlantDetectionMode.Loop && cumulativeTravelDistance.Length > 0) cycleDistance = cumulativeTravelDistance[cumulativeTravelDistance.Length - 1];
        leftFoot ??= new PlayerFootMotionChannel();
        rightFoot ??= new PlayerFootMotionChannel();
        if (leftFootData != null) leftFoot.SetBakedData(leftFootData);
        if (rightFootData != null) rightFoot.SetBakedData(rightFootData);
        editorMetadata ??= new PlayerMotionProfileMetadata();
        editorMetadata.Set(CurrentBakeVersion, bakedSampleRate, clipGuid, clipLocalId, modelGuid, dependencyHash, calibrationGuid, calibrationHash);
    }

    public void SetPlantAuthoringSettings(PlayerFootPlantDetectionMode detectionMode, PlayerPlantMarkerMode markerMode)
    {
        footPlantDetectionMode = detectionMode;
        plantMarkerMode = markerMode;
        if (detectionMode == PlayerFootPlantDetectionMode.Loop && cycleDistance <= 0f && cumulativeTravelDistance != null && cumulativeTravelDistance.Length > 0) cycleDistance = cumulativeTravelDistance[cumulativeTravelDistance.Length - 1];
        detectionModePersisted = true;
    }

    public void ReplacePlantMarkers(IEnumerable<PlayerFootPlantMarker> markers, int detectionVersion)
    {
        if (markers == null) throw new ArgumentNullException(nameof(markers));
        plantMarkers = new List<PlayerFootPlantMarker>(markers);
        plantMarkers.Sort((left, right) =>
        {
            int timeComparison = left.NormalizedTime.CompareTo(right.NormalizedTime);
            return timeComparison != 0 ? timeComparison : ((int)left.Foot).CompareTo((int)right.Foot);
        });
        footPlantDetectionVersion = detectionVersion;
    }

#endif
    /// <summary>
    /// 拿到实际的移动位置
    /// </summary>
    private static Vector2 Evaluate(Vector2[] samples, float progress)
    {
        if (samples == null || samples.Length == 0) return Vector2.zero;
        if (samples.Length == 1) return samples[0];
        //动画播放进程归一化后和采样点相乘得到更加细致的播放比例
        float sample = Mathf.Clamp01(progress) * (samples.Length - 1);
        //拿到左右两个下标
        int index = Mathf.Min(Mathf.FloorToInt(sample), samples.Length - 2);
        //表示在左右两个中间的第0.x的位置
        return Vector2.LerpUnclamped(samples[index], samples[index + 1], sample - index);
    }
    /// <summary>
    /// 拿到移动距离和角度
    /// </summary>
    private static float Evaluate(float[] samples, float progress)
    {
        if (samples == null || samples.Length == 0) return 0f;
        if (samples.Length == 1) return samples[0];
        float sample = Mathf.Clamp01(progress) * (samples.Length - 1);
        int index = Mathf.Min(Mathf.FloorToInt(sample), samples.Length - 2);
        return Mathf.LerpUnclamped(samples[index], samples[index + 1], sample - index);
    }
    /// <summary>
    /// 验证某个数字是否是有效数据
    /// </summary>
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool IsValidPlantFoot(PlayerFoot foot) => foot == PlayerFoot.Left || foot == PlayerFoot.Right;

    private bool TryValidateLoopPhaseConfiguration(out int markerCount)
    {
        markerCount = plantMarkers == null ? 0 : plantMarkers.Count;
        if (markerCount < 2 || !IsFinite(duration) || duration <= 0f || !IsFinite(cycleDistance) || cycleDistance <= 0f) return false;
        for (int index = 0; index < markerCount; index++)
        {
            PlayerFootPlantMarker marker = plantMarkers[index];
            if (!IsValidLoopPlantMarker(marker)) return false;
            if (index > 0)
            {
                PlayerFootPlantMarker previous = plantMarkers[index - 1];
                if (!(marker.NormalizedTime > previous.NormalizedTime) || marker.Foot == previous.Foot) return false;
            }
        }
        PlayerFootPlantMarker first = plantMarkers[0];
        PlayerFootPlantMarker last = plantMarkers[markerCount - 1];
        return last.Foot != first.Foot && 1f - last.NormalizedTime + first.NormalizedTime > 0f;
    }

    private static bool IsValidLoopPlantMarker(PlayerFootPlantMarker marker)
    {
        return IsValidPlantFoot(marker.Foot) && IsFinite(marker.NormalizedTime) && marker.NormalizedTime > 0f && marker.NormalizedTime < 1f;
    }

    private static bool IsValidPlantMarker(PlayerFootPlantMarker marker)
    {
        return IsValidPlantFoot(marker.Foot) && IsFinite(marker.NormalizedTime) && marker.NormalizedTime >= 0f && marker.NormalizedTime <= 1f;
    }

}
