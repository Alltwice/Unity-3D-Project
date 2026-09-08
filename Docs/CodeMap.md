# CodeMap

> 本文记录当前项目功能、模块与文件位置的对应关系  
> 最后核对的运行时代码基线：当前工作树（2026-09-08，包含未提交的落地简化改动）

## 1. 功能与实现位置

| 功能 / 信息 | 实现位置 |
|---|---|
| 玩家系统每帧执行顺序 | `Assets/Scripts/Player/PlayerSimulation/PlayerSimulationDriver.cs` |
| Idle / Walk / Run / Dodge / Air / Landing 状态转换 | `Assets/Scripts/Player/PlayerState/PlayerStateController.cs` + 对应 State |
| 状态共享的输入与模拟事实 | `Assets/Scripts/Player/PlayerState/PlayerContext.cs` |
| Start / Stop / Turn / DodgeToIdle Motion 选择 | `Assets/Scripts/Player/PlayerSimulation/PlayerMotionPlanner.cs` |
| 烘焙 Motion 的时间推进 | `Assets/Scripts/Player/PlayerMotion/PlayerMotionRuntime.cs` |
| Motion 的位移 / 旋转 / Entry/Exit Handoff / Transition Lock 策略 | `Assets/Scripts/Player/PlayerMotion/PlayerMotionDefinition.cs` |
| 动画烘焙数据、Foot Motion Channel 与 Foot Marker | `Assets/Scripts/Player/PlayerMotion/PlayerMotionProfile.cs` + `PlayerFoot.cs` |
| Foot Plant 自动检测算法 | `Assets/Tools/AnimationPreview/Editor/PlayerFootPlantDetector.cs` |
| Foot Phase 推进 | `Assets/Scripts/Player/PlayerMotion/PlayerLocomotionPhaseRuntime.cs` |
| Gameplay 与 Motion 合成为实际移动 | `Assets/Scripts/Player/PlayerMotion/PlayerMotionComposer.cs` |
| CharacterController 执行 | `Assets/Scripts/Player/PlayerAbility/PlayerMotor.cs` |
| 地面检测 / Ground Snap | `Assets/Scripts/Player/PlayerAbility/PlayerGroundProbe.cs` |
| 落地等级与落地事实 | `Assets/Scripts/Player/PlayerSimulation/Landing/PlayerLandingTracker.cs` |
| 动画语义到具体 Clip 的映射 | `Assets/Scripts/Player/PlayerAnimation/PlayerAnimationSet.cs` |
| Animancer 播放 / Fade / 手动采样 | `Assets/Scripts/Player/PlayerAnimation/PlayerAnimationController.cs` |
| MotionProfile 生成 | `Assets/Tools/AnimationPreview/Editor/PlayerMotionBaker.cs` |
| Player 组件装配与 SO 引用 | `Assets/Prefabs/Player.prefab` + `Assets/Settings/Player/Motion/` |

## 2. 仓库入口与装配

### `Assets/Prefabs/Player.prefab`

当前玩家对象的组件装配与序列化引用 Source of Truth。

### `Assets/Scripts/InputSystem_Actions.inputactions`

Unity Input System 的输入资产。

### `Assets/Scripts/InputSystem_Actions.cs`

由 Input System 生成的 C# Wrapper。

## 3. Input

目录：`Assets/Scripts/Player/Input/`

| 文件 | 职责 | 主要关系 |
|---|---|---|
| `IPlayerInputSource.cs` | 持续玩家输入的稳定读取接口 | State / Simulation / Camera 依赖接口，不直接依赖 Unity Input 回调 |
| `IPlayerActionBuffer.cs` | 离散动作缓冲接口 | Input 写入，Jump / Dodge 等 Gameplay 状态消费 |
| `PlayerInputReader.cs` | Unity Input System 边界；读取持续输入并把离散操作写入共享 Buffer | 实现 `IPlayerInputSource`，写入 `IPlayerActionBuffer` |

相关配置：

- `Assets/Scripts/Player/Config/PlayerActionBufferConfig.cs`

## 4. Bootstrap / Shared Input

目录：`Assets/Scripts/Player/Other/`

### `PlayerActionBuffer.cs`

离散输入的短时缓冲实现。负责：

- 保存动作请求
- 按 Simulation Clock 推进缓冲
- 向 Gameplay 提供查询、消费与清理语义

它用于解耦 Input callback 时机和 Player Simulation 时机。

### `PlayerInstaller.cs`

当前 Player 的轻量装配入口：

- 把 `PlayerActionBuffer` 注入 `PlayerInputReader`
- 把 `PlayerInputReader` 与 `PlayerActionBuffer` 注入 `PlayerSimulationDriver`
- 把 `PlayerInputReader` 注入 `PlayerCameraOrbitTarget`

## 5. Camera

目录：`Assets/Scripts/Player/Camera/`

### `PlayerCameraOrbitTarget.cs`

玩家相机 Orbit Target 相关控制，通过 `IPlayerInputSource` 获取 Look 输入。

相关配置：

- `Assets/Scripts/Player/Config/PlayerCameraConfig.cs`

`PlayerSimulationDriver` 使用 `movementReference` 将二维移动输入转换到世界移动方向；未显式配置时使用 `Camera.main.transform`。

## 6. Gameplay State

目录：`Assets/Scripts/Player/PlayerState/`

### `PlayerStateController.cs`

Gameplay State 总入口和唯一状态切换裁决者。

主要负责：

- 注册当前 State
- 初始化 Idle
- Pre-Tick 输入转换
- State Tick
- Post-Tick 结果转换
- 应用 Motion transition lock
- 维护当前 `PlayerLocomotionMode`

### `PlayerContext.cs`

State 共享依赖和模拟事实：

- Input / Action Buffer
- Jump / Dodge
- Movement Config
- Motor / Motion / Landing Snapshot
- Walk 模式、FastRun latch、零输入宽限计时与 HasGroundMoveContinuationIntent（默认宽限 0.1 秒）
- Pending vertical impulse

### `PlayerStateBase.cs`

所有 Player State 的公共基类，定义 Enter / Exit / Tick / Transition Evaluation 等稳定接口。

### `PlayerStateTransitionRequest.cs`

State 提出的候选转换，只描述目标状态与原因，不执行切换副作用。

### `PlayerStateTransition.cs`

已发生状态转换的事实，记录 Previous / Current State Type 与 Transition Reason。主要由 `PlayerMotionPlanner`、`PlayerAnimationController` 和 `PlayerSimulationDriver` 消费；Landing Runtime 自身不直接消费该类型。

### 当前 State

| 文件 | 主要语义 |
|---|---|
| `PlayerIdleState.cs` | 接地无移动意图 |
| `PlayerWalkState.cs` | 接地慢速移动 |
| `PlayerRunState.cs` | 接地常规跑动 |
| `PlayerFastRunState.cs` | 疾跑；与 Dodge 后 FastRun latch 相关 |
| `PlayerDodgeState.cs` | Dodge Gameplay 生命周期 |
| `PlayerAirState.cs` | Jump / Fall / Air 与落地结果转换 |
| `PlayerHardLandingState.cs` | 重落地 Gameplay 锁定与自身 Presentation Progress |

## 7. Simulation Orchestration

目录：`Assets/Scripts/Player/PlayerSimulation/`

### `PlayerSimulationDriver.cs`

当前玩家每帧唯一高层执行顺序。

直接协作对象：

- `PlayerStateController`
- `PlayerMotionPlanner`
- `PlayerMotor`
- `PlayerAnimationController`
- `PlayerDodge`
- `PlayerLandingTracker`
- `IPlayerInputSource`
- `IPlayerActionBuffer`

关键顺序：`HandleStateTransition` 在 State Tick 之前，`ResolveContinuousMotion` 在 State Tick 之后；Motor 执行和 LandingSnapshot 产生之后才进行 Post-Tick 状态转换。落地表现由 Driver 结合 Snapshot 与 Post-Tick Transition 提交，不再调用 Planner 启动落地 Motion。

`ResolveEffectiveMoveDirection` 在 Pre-Tick 转换后及 Post-Tick 转换后调用：地面移动模式中缓存原始世界方向（含幅值），零输入宽限内复用；其他模式或宽限失效时清理。缓存由 Driver 持有，宽限计时与延续意图由 Context 持有，原始输入仍独立供状态决策使用。

### `PlayerMotionPlanner.cs`

Gameplay → Motion 的规划器。

主要负责：

- State Transition → MotionId / MotionDefinition
- Start / Stop
- DodgeCompleted → Idle 的 DodgeToIdle
- 180° Start / Turn
- Foot Phase 驱动的左右脚 Profile 选择
- 所有 Motion Begin 共用 Entry Source 捕获：Definition 启用 Entry Handoff 且 Phase 有地面 Loop 时，读取 `PhaseSnapshot.Mode` 与 `MotorResult.HorizontalVelocity`
- `PlayerMotionRuntime` 生命周期
- 持有并提交 `PlayerLocomotionPhaseRuntime`

当前不负责 Animancer、AnimationClip 选择、CharacterController 移动或 Gameplay State 的最终切换。

## 8. Landing Runtime

目录：`Assets/Scripts/Player/PlayerSimulation/Landing/`

### `Project.PlayerLanding.Runtime.asmdef`

独立 Runtime 程序集，依赖：

```text
Project.PlayerLanding.Runtime
    ↓
Project.PlayerMotion.Runtime
```

### `PlayerLandingTracker.cs`

跨帧追踪空中生命周期，在落地帧生成 `PlayerLandingSnapshot`。

主要输入：

- `PlayerMotorResult`
- 当前高度

落地帧只依据空中最高高度与当前高度的差值划分 Lv1～Lv4；默认 Lv2 / Lv3 / Lv4 阈值为 1 / 2 / 3 米。

### `PlayerLandingSnapshot.cs`

一次落地事件的稳定数据快照，仅包含 Sequence、Severity、FallDistance 与派生的 IsLandingEvent，供 Gameplay 状态事实、`PlayerSimulationDriver` 与 `PlayerLandingPresentationResolver` 消费；Driver 再依据状态转换和解析结果提交 AnimationController。

### `PlayerLandingPresentationResolver.cs`

仅根据 `PlayerLandingSnapshot` 解析落地表现语义，不接收状态转换，也不直接播放动画。`PlayerSimulationDriver` 负责把落地快照、已发生的状态转换和 Resolver 结果组合起来。

语义分为：

- 普通 Edge：`Land1` / `Land2` / `Land3`
- 重落地：`HardLand`，其 Clip 存储在 `PlayerAnimationSet` 的 `Land4` 资源槽

当前落地仅使用上述表现语义，不启动 Motion。

## 9. PlayerMotion Runtime

目录：`Assets/Scripts/Player/PlayerMotion/`

### `Project.PlayerMotion.Runtime.asmdef`

Motion 基础 Runtime 程序集，自身 `references` 列表为空，是数据与运行时契约的底层边界；Landing、Editor Tooling 和测试程序集可以单向引用它。

### `PlayerSimulationData.cs`

Player Simulation 跨层基础数据契约：

- `PlayerLocomotionMode`
- `PlayerGameplayIntent`：含平面速度覆盖、有效移动时长与垂直冲量
- `PlayerMotorCommand`
- `PlayerMotorResult`
- `PlayerMotionEntrySource`
- Motor translation / rotation mode
- `PlayerMotorKinematics`

### `PlayerMovementConfig.cs`

玩家移动核心 ScriptableObject 配置。

覆盖常规 Locomotion、Motor Physics、Landing 等运行参数。默认资产：

`Assets/Settings/Player/Motion/DefaultPlayerMovementConfig.asset`

### `PlayerMotionCatalog.cs`

Motion 语义总索引。

核心类型：

- `PlayerMotionId`
- `PlayerMotionCatalogEntry`
- `PlayerMotionCatalog`

保存：

- `MotionId → PlayerMotionDefinition`
- `PlayerLocomotionMode → PlayerLocomotionCycleDefinition`
- 180° Turn Threshold

默认资产：

`Assets/Settings/Player/Motion/DefaultPlayerMotionCatalog.asset`

### `PlayerMotionDefinition.cs`

一类特殊 Motion 的运行语义。

字段 / 策略：

- Profile / LeftFootProfile / RightFootProfile
- Translation Policy
- Rotation Policy
- Basis Policy
- Duration / Translation Scale
- Entry Handoff end progress 与 Target Translation Weight 曲线
- Exit Handoff start/end progress 与 Translation Authority 曲线
- Transition Lock
- Interrupted Exit Policy
- Phase Foot Selection
- Requires Presentation

对象关系：

```text
PlayerMotionCatalog
    └── MotionId ──► PlayerMotionDefinition asset
                          ├──► PlayerMotionProfile asset
                          ├──► PlayerMotionPlanner / PlayerMotionRuntime
                          └──► PlayerAnimationSet binding
```

### `PlayerMotionProfile.cs`

离线烘焙后的运动数据、Foot Motion Channel、Foot Plant Marker 与源资产元数据。

运行时主要提供：

- Duration / Sample Rate
- Planar Position / Travel Distance
- Yaw
- Plant Markers
- Loop Phase 解析
- 对应数据采样与校验

Profile 由 Editor 工具生成和维护；Gameplay Runtime 消费烘焙结果，不重新分析 AnimationClip。

### `PlayerMotionRuntime.cs`

当前 Active Motion 的纯运行时演进。

其中还定义：

- `PlayerMotionFrame`：本帧 Motion 对位移、Yaw 与控制权的贡献
- `PlayerMotionSnapshot`：对 Planner、State、Phase 与 Animation 暴露的 Motion 状态事实

主要处理：

- Begin / Advance / Cancel
- Progress / InstanceId
- 烘焙位移、Yaw
- Entry Source 捕获与 Entry Handoff 进度/目标位移权重
- Exit Handoff 进度与 Translation Authority
- Completion / Cancellation：结束帧保留快照引用，下一次 BeginFrame 清理
- Transition Lock

### `PlayerMotionComposer.cs`

`SteeredLocalTrajectory = 5` 由 `PlayerMotionDefinition` 配置并校验 `ProfileYaw + EntryFacing` 及主/左右脚 Profile 的 XZ、Yaw 数据。`PlayerMotionRuntime` 记录开始 Yaw，并在 `PlayerMotionFrame.AuthoredFacingBeforeStep` 提供帧前理论世界朝向；Composer 先解析旋转，再用已有朝向偏差与本帧额外修正的中点旋转动画位移项，之后进入原有三路 Handoff 混合。原有位移策略和资产选择保持不变。

将 `PlayerGameplayIntent + PlayerMotionFrame + previous PlayerMotorResult` 合成为最终 `PlayerMotorCommand`，其输出只流向 `PlayerMotor`，不直接流向 `PlayerAnimationSet`。

优先处理 Intent 的平面速度覆盖，生成 `ImmediateVelocityDriven` 命令（含有效时长）；该分支用于 Dodge。当 Motion 使用烘焙位移时，Composer 按 Entry Source 速度、Authored Motion 位移和目标 Locomotion 预测速度的统一三路权重合成平面位移。

### Foot / Phase

| 文件 | 职责 |
|---|---|
| `PlayerFoot.cs` | `PlayerFoot`、Foot Plant Marker、Foot Motion Channel 与自动检测模式等基础语义 |
| `PlayerFootCalibration.cs` | 角色脚部检测/烘焙所需校准数据 |
| `PlayerLocomotionCycleDefinition.cs` | Walk / Run / FastRun 的循环相位数据定义 |
| `PlayerLocomotionPhaseRuntime.cs` | 根据实际位移推进相位；Entry Handoff 优先保留 Source Loop；其他 Active Motion 在 Exit 前暂停 Loop，进入 Exit 时建立目标 Cycle |
| `PlayerLocomotionPhaseSnapshot.cs` | 对 Planner 与 AnimationController 暴露稳定相位事实 |

默认 Foot Calibration 资产位于：

`Assets/Settings/Player/Motion/FootCalibration/`

## 10. Ability / World Movement

目录：`Assets/Scripts/Player/PlayerAbility/`

### `PlayerMotor.cs`

唯一 CharacterController 执行器。

输入：

- `PlayerMotorCommand`

输出：

- `PlayerMotorResult`

主要职责：

- VelocityDriven / ImmediateVelocityDriven / DisplacementDriven 三种平移方式；立即速度按本帧有效时长积分
- Gravity / vertical velocity
- vertical impulse
- CharacterController.Move
- Ground Snap
- FaceDirection / YawDelta
- 真实移动结果计算

当前不认识具体 Gameplay State 或动画资源。

### `PlayerGroundProbe.cs`

集中处理地面探测和 Snap 所需事实。

### `PlayerJump.cs`

Jump 能力参数/规则，向状态层提供是否可跳与跳跃冲量计算；实际垂直移动由 Intent → Motor 执行。

### `PlayerDodge.cs`

Dodge 能力维护 Active、方向、Speed、Duration、Progress 与退出后的 Cooldown；Tick 向 Intent 写入平面速度覆盖及本帧剩余有效时长。`PlayerDodgeState` 拥有 Begin / Tick / End，并以 IsComplete 裁决完成后的转换：无原始输入进入 Idle，否则进入 FastRun。Dodge 本体通过 Composer → Motor 移动，通过 Dodge Cue 表现；Planner 只在 DodgeCompleted → Idle 时选择 DodgeToIdle Motion。

相关配置：

- `Assets/Scripts/Player/Config/PlayerDodgeConfig.cs`
- `Assets/Settings/Player/DefaultPlayerDodgeConfig.asset`：当前 Duration 0.3 秒、Speed 12、Cooldown 0.35 秒

## 11. Animation Presentation

目录：`Assets/Scripts/Player/PlayerAnimation/`

### `PlayerAnimationController.cs`

Animancer 表现入口。

输入事实：

- Current Gameplay State Type
- `PlayerStateTransition`
- `PlayerMotionSnapshot`
- `PlayerLocomotionPhaseSnapshot`
- State Presentation Progress
- Landing Presentation Key

主要负责：

- Boundary Motion 播放与按 Motion Progress 手动采样
- 普通 Motion 开始时取消 Animancer Fade、停止未拥有的活动 State，并统一写入所持 State 权重
- `PlayDodgeToIdleMotion`：保留 Dodge Pose，以 FixedDuration Fade 进入结束 Motion；Fade 期间暂停手动权重，最迟在 Exit Handoff 开始时完成 Fade，再交给 Exit 权重
- Dodge 按 State PresentationProgress 采样；Walk / Run 互切使用 FixedDuration Fade
- Entry Handoff：有 Motion Entry Source 时按 Phase Snapshot 采样源 Loop；无有效 Source 时按 Clip FadeDuration 建立回退 Entry Pose 区间
- Exit Handoff：按 Exit Pose Weight 将 Boundary Pose 混合到 Locomotion Loop
- Entry / Exit 重叠时按 `1-Entry`、`Entry×(1-Exit)`、`Entry×Exit` 组合 Source、Boundary、Target 三路姿态权重
- Loop 按 Phase NormalizedTime 手动采样
- Jump / Landing Presentation Edge
- HardLanding Presentation Progress
- Animancer Graph Manual Evaluation

当前不生产 Motion、Foot Phase 或实际角色位移。

### `PlayerAnimationSet.cs`

动画资源组织与语义映射 ScriptableObject。

核心映射：

```text
MotionDefinition + selected Profile
    → ClipTransition

LocomotionMode + PlayerFoot
    → Loop ClipTransition

Presentation Cue / Landing Key
    → ClipTransition
```

Motion Binding 同时保存 Entry Pose Weight 与 Exit Pose Weight 曲线；默认 WalkToIdle / RunToIdle / FastRunToIdle 的 Entry Pose 为端点零切线的平滑曲线，其余默认 Entry 与默认 Exit 为线性。Dodge Cue 独立映射 Other.Dodge，DodgeToIdle 为 Other 中的 Motion Binding；Landing 组只有 Land1～Land4 ClipTransition。

默认资产：

`Assets/Settings/Player/Motion/DefaultPlayerAnimationSet.asset`

### `Editor/PlayerAnimationSetEditor.cs`

`PlayerAnimationSet` 的 Inspector 校验入口，用于检查 Motion Catalog / Definition / Profile / Clip Binding 一致性。

## 12. Animation Preview / Bake Toolchain

目录：`Assets/Tools/AnimationPreview/Editor/`

### `Project.AnimationPreview.Editor.asmdef`

Editor-only 工具程序集，依赖 `Project.PlayerMotion.Runtime`。

### 主要入口与辅助类型

| 文件 | 职责 |
|---|---|
| `AnimationPreviewWindow.cs` | 动画预览工具窗口入口 |
| `AnimationPreviewSession.cs` | 预览会话与采样；Humanoid 优先读取完整 RootT / RootQ 曲线，否则从模型 Transform 采样根运动；脚部从采样后的骨骼读取 |
| `AnimationPreviewViewport.cs` | 预览视口 |
| `AnimationPreviewProfile.cs` | 预览配置数据 |
| `AnimationPreviewProfileEditor.cs` | Profile Inspector 与打开预览窗口的入口 |
| `AnimationPreviewSequence.cs` | 预览序列描述 |
| `AnimationPreviewClipLibrary.cs` | 预览 Clip 组织 |
| `PlayerMotionBaker.cs` | 从 AnimationClip 烘焙 `PlayerMotionProfile` 数据 |
| `PlayerMotionProfileBatchBaker.cs` | 按发现的 Profile 批量生成/刷新并校验 Motion 相关资产，不限定固定 Profile 数量 |
| `PlayerFootPlantDetector.cs` | 根据 Foot Motion Channel 自动检测 Plant Marker |
| `PlayerFootPlantMarkerEditor.cs` | Foot Plant Marker 的编辑、自动生成与写回辅助 |
| `AssemblyInfo.cs` | 向 Editor Tests 暴露程序集 internal 类型 |

工具输出的 Runtime 数据主要落在：

`Assets/Settings/Player/Motion/Profiles/`

Runtime 当前不依赖这些 Editor 类型。

## 13. Tests

### PlayerMotion / Landing Tests

目录：`Assets/Scripts/Player/PlayerMotion/Tests/`

| 文件 | 主要覆盖范围 |
|---|---|
| `PlayerMotionRuntimeTests.cs` | Motion 生命周期、帧率无关性、取消/替换、Entry Source、方向与 Yaw |
| `PlayerMotionContractTests.cs` | Definition、Profile、Composer 数值边界、三路位移权重与默认 Catalog 合法性 |
| `PlayerLocomotionPhaseRuntimeTests.cs` | Phase 推进、Entry/Exit Handoff、循环暂停、恢复与必要 Cycle 资产契约 |
| `PlayerLandingTrackerTests.cs` | 空中生命周期、严重度、一次性 Snapshot 与 Reset |
| `Project.PlayerMotion.Tests.asmdef` | Editor 测试程序集定义 |

### Animation Preview Editor Tests

目录：`Assets/Tools/AnimationPreview/Editor/Tests/`

| 文件 | 主要覆盖范围 |
|---|---|
| `PlayerFootMotionTests.cs` | Foot Motion 采样、Plant 检测算法、Marker 编辑与批量烘焙原子性 |
| `AnimationPreviewClipLibraryTests.cs` | Clip 扫描、Preview Graph 创建与 Sequence 混合 |
| `Project.AnimationPreview.Editor.Tests.asmdef` | Editor 工具测试程序集定义 |

这些测试只保护稳定的输入、输出和数据契约，不通过反射检查 PlayerAnimationController、PlayerSimulationDriver 等默认程序集类型的私有实现。动画 State 所有权和实际衔接效果由人工场景检查，动画资产引用完整性由 `PlayerAnimationSetEditor` 校验。

## 14. Motion 配置资产

根目录：

`Assets/Settings/Player/Motion/`

| 位置 | 作用 |
|---|---|
| `DefaultPlayerMovementConfig.asset` | 默认移动 / Physics / Landing 参数 |
| `DefaultPlayerMotionCatalog.asset` | 默认 Motion 与 Locomotion Cycle 索引 |
| `DefaultPlayerAnimationSet.asset` | 默认动画资源映射 |
| `Definitions/` | 各 Start / Stop / Turn / DodgeToIdle MotionDefinition |
| `Profiles/` | 烘焙 MotionProfile |
| `FootCalibration/` | Foot 检测/烘焙校准资产 |

当前默认 Catalog 包含 16 个 Motion Definition 与 Walk / Run / FastRun 三个 Locomotion Cycle。15 个 Start / Stop / Turn Definition 的 Entry 通常为 `0→0.2`，`WalkToIdle` 为 `0→0.12`，Exit 为 `0.7→1`；`DodgeToIdle` 关闭 Entry Handoff，Exit 为 `0.8→1`。Dodge 本体使用能力速度与独立 Clip，Catalog 中没有 Dodge 或移动落地 Motion。

## 15. 非主链路代码

仓库中还存在资源包和演示脚本，例如：

- `Assets/FemaleRunnerAnimset/`
- `Assets/CommonMaleMovementAnimSet/`
- `Assets/DoubleL/Demo Scenes/`

它们不属于当前 Player 主运行链路。当前主链路位于：

```text
Assets/Scripts/Player/
Assets/Settings/Player/
Assets/Prefabs/Player.prefab
```

## 16. 模块关系

本节箭头表示运行数据或控制调用方向；资产引用、所有权和测试关系另行标注，不混入运行链。

### 状态转换

```text
具体 PlayerState
    └──提出请求──► PlayerStateController
                       └──产生已提交 Transition──► PlayerSimulationDriver
                                                     ├──► PlayerMotionPlanner
                                                     └──► PlayerAnimationController
```

### Motion 与实际移动

```text
PlayerMotionCatalog ──索引──► PlayerMotionDefinition ──引用──► PlayerMotionProfile
        │                              │
        └──────────────► PlayerMotionPlanner
                                │
                                ▼
                       PlayerMotionRuntime
                                │ PlayerMotionFrame / Snapshot
                                ▼
                       PlayerMotionComposer
                                │ PlayerMotorCommand
                                ▼
                           PlayerMotor
                                │ PlayerMotorResult
                                ▼
                      State / Landing / Phase
```

`PlayerAnimationSet` 与 `PlayerMotionCatalog`、Definition/Profile 和 Clip 建立资产绑定；`PlayerAnimationController` 使用 Motion Snapshot 与 AnimationSet 选择并采样表现。它不是 `PlayerMotionComposer` 的下游。

Entry / Exit Handoff 的关键数据流：

```text
Motion Begin + PhaseSnapshot + MotorResult.HorizontalVelocity
                         │
                         ▼
                PlayerMotionPlanner
                         │ PlayerMotionEntrySource
                         ▼
                PlayerMotionRuntime
                  ├──► Frame ──► PlayerMotionComposer ──► PlayerMotor
                  └──► Snapshot
                          ├──► PlayerLocomotionPhaseRuntime
                          └──► PlayerAnimationController
```

Planner 只传递 Simulation 数据；Entry Source Loop State 由 AnimationController 从当前 stable Loop 内部转移并自行清理。

### Foot Phase / Stop 选脚

```text
PlayerMotorResult + LocomotionMode + MotionSnapshot
                         │
                         ▼
            PlayerLocomotionPhaseRuntime
                         │
                         ▼
           PlayerLocomotionPhaseSnapshot
                  ┌──────┴─────────┐
                  ▼                ▼
PlayerMotionDefinition       PlayerAnimationController
   .ResolveEntryFoot                 │
          │                          ▼
          ▼                  PlayerAnimationSet
 PlayerMotionPlanner
```

### 落地

```text
PlayerMotor / GroundProbe
          │
          ▼
PlayerLandingTracker
          │
          ▼
PlayerLandingSnapshot
    ┌─────┴────────────────────┐
    ▼                          ▼
PlayerContext / AirState   PlayerSimulationDriver
    │                          │
    ▼                          ▼
StateController        PresentationResolver
    │                          │
    └──── Transition ──────────┤
                               └── Presentation ───► PlayerAnimationController
```

### 资产与装配关系

```text
Definitions / Profiles / Clips
             │
             ▼
DefaultPlayerMotionCatalog.asset ─────┐
DefaultPlayerAnimationSet.asset ──────┼──► Player.prefab 序列化引用
DefaultPlayerMovementConfig.asset ────┘
                                             │
                                             ▼
                                      Runtime Components
```

测试程序集验证 Runtime、Editor 工具和必要默认资产契约，不参与运行时数据流，也不替代动画观感、卡顿、滑步与输入手感的人工检查。
