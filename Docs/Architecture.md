# Architecture

> 本文记录项目当前较稳定的职责边界、依赖方向和核心运行流  
> 最后核对的运行时代码基线：当前工作树（2026-09-08，包含未提交的落地简化改动）

## 1. 当前架构概览

当前项目的核心运行时集中在 `Assets/Scripts/Player/`。玩家系统把 **Gameplay 状态、Motion 数据模拟、实际移动和动画表现** 分开，由 `PlayerSimulationDriver` 统一编排一帧。

```text
Unity Input System
        │
        ▼
PlayerInputReader ──► PlayerActionBuffer
        │                    │
        └────── interfaces ──┘
                 │
          PlayerInstaller
                 │
                 ▼
        PlayerSimulationDriver
                 │
        ┌────────┼───────────────┐
        ▼        ▼               ▼
 PlayerState   Motion         Ability facts
 Controller   Planner        Jump / Dodge
        │        │
        │        ▼
        │   PlayerMotionRuntime
        │   PlayerLocomotionPhaseRuntime
        │        │
        └──► PlayerGameplayIntent
                 │
                 ▼
        PlayerMotionComposer
                 │
                 ▼
          PlayerMotorCommand
                 │
                 ▼
             PlayerMotor
                 │
        CharacterController
                 │
                 ▼
          PlayerMotorResult
                 │
        ┌────────┴─────────┐
        ▼                  ▼
 LandingTracker      State facts / transitions
        │                  │
        └────────┬─────────┘
                 ▼
       committed Motion / Phase
                 │
                 ▼
    PlayerAnimationController
                 │
       PlayerAnimationSet
                 │
              Animancer
```

当前职责关系：

- **状态机决定玩家处于什么 Gameplay 状态**
- **Motion 系统决定特殊运动如何按数据演进**
- **Motor 是唯一实际执行 CharacterController 移动的模块**
- **Animation 消费已经产生的状态、Motion 与相位事实并表现为 Pose**
- **Editor 工具把动画离线加工为 Runtime 可消费的数据，Runtime 当前不反向依赖 Editor**

## 2. 每帧控制流

`PlayerSimulationDriver` 是当前玩家每帧唯一的高层编排入口。当前顺序为：

```text
Action Buffer / Motion 帧事件 / Dodge Cooldown 更新
    ↓
解析世界移动方向并更新地面移动意图
    ↓
写入上一帧 Motor / Motion 事实
    ↓
状态机 Pre-Tick 转换
    ↓
建立 PlayerGameplayIntent
    ↓
Motion Planner 解析状态转换
    ↓
状态 Tick 补充 Gameplay Intent
    ↓
Motion Planner 解析连续 Motion
    ↓
PlayerMotionRuntime 推进烘焙 Motion
    ↓
PlayerMotionComposer 合成最终 MotorCommand
    ↓
PlayerMotor 执行 CharacterController 移动
    ↓
生成 MotorResult + LandingSnapshot
    ↓
状态机 Post-Tick 转换
    ↓
处理 Post-Tick Motion 与落地表现语义
    ↓
提交 Locomotion Phase
    ↓
AnimationController 消费最终事实并手动评估 Animancer
```

`HandleStateTransition` 位于状态 Tick 之前；`ResolveContinuousMotion` 位于状态 Tick 之后，因此连续 Motion 可以读取本帧状态补充后的 `PlayerGameplayIntent`。

`PlayerMotorResult` 在 Motor 执行后产生，再反馈给状态、落地检测、Foot Phase 和后续决策。

`PlayerAnimationController` 位于模拟之后，接收最终的 Gameplay State、Transition、Motion Snapshot、Phase Snapshot 和 Landing Presentation，当前不参与本帧移动裁决。

## 3. 输入与依赖注入

### Input

`PlayerInputReader` 负责 Unity Input System 边界：

- 持续输入通过 `IPlayerInputSource` 暴露
- Jump / Dodge 等离散操作写入 `IPlayerActionBuffer`
- `PlayerActionBuffer` 在输入回调时机与 Gameplay 模拟时机之间保存短时动作请求

当前上层 Gameplay 通过 `IPlayerInputSource` / `IPlayerActionBuffer` 与输入实现交互。

### Bootstrap

`PlayerInstaller` 当前承担轻量装配职责：

- 把输入源与动作缓冲注入 `PlayerSimulationDriver`
- 把输入源注入 `PlayerCameraOrbitTarget`
- 把动作缓冲注入 `PlayerInputReader`

当前依赖链：

```text
Concrete Input / Action Buffer
        │
        ├──► IPlayerInputSource / IPlayerActionBuffer ──► Simulation
        └──► IPlayerInputSource ────────────────────────► Camera Orbit Target
```

## 4. Gameplay 状态层

### PlayerStateController

`PlayerStateController` 是**唯一 Gameplay State 切换裁决者**。

当前注册状态：

- `PlayerIdleState`
- `PlayerWalkState`
- `PlayerRunState`
- `PlayerFastRunState`
- `PlayerDodgeState`
- `PlayerAirState`
- `PlayerHardLandingState`

状态对象提出 `PlayerStateTransitionRequest`；Exit / 切换 / Enter 由 Controller 统一执行。

状态更新分为：

```text
EvaluateInputTransition
        ↓
       Tick
        ↓
EvaluateResultTransition
```

输入导致的转换和执行移动后才能确认的转换（例如落地）分阶段处理。

### PlayerContext

`PlayerContext` 是状态对象共享的稳定依赖与事实容器，目前包含：

- `PlayerJump` / `PlayerDodge`
- `IPlayerInputSource` / `IPlayerActionBuffer`
- `PlayerMovementConfig`
- `PlayerMotorResult`
- `PlayerMotionSnapshot`
- `PlayerLandingSnapshot`
- Walk / FastRun 等需要跨状态保存的移动意图状态
- 待应用的垂直冲量

### 地面移动意图与 Dodge

`PlayerContext` 维护零输入宽限计时及 `HasGroundMoveContinuationIntent`，当前默认宽限为 0.1 秒；`PlayerStateController` 暴露该事实。Driver 在 Pre-Tick 状态转换后解析有效移动方向：Walk / Run / FastRun 中有原始输入时缓存完整世界方向（保留输入幅值），短暂零输入且仍有延续意图时复用缓存；退出这些模式、宽限失效、Init 或 OnDisable 时清理缓存。Post-Tick 转换后以新状态再次解析方向。原始输入仍由 InputSource 保留，Dodge 完成后的 Idle / FastRun 选择读取原始输入。

`PlayerDodgeState` 调用 `PlayerDodge.Begin / Tick / End`，以能力的 Duration 决定完成，以 Progress 提供表现时间。能力在有输入时更新方向，无输入时保持已选方向（首次使用朝向），并向 Intent 写入速度与本帧有效移动时长；Composer 优先生成 ImmediateVelocityDriven 命令，Motor 执行移动。最后一帧仅积分剩余 Dodge 时长，退出时开始冷却。Dodge 本体不经过 MotionProfile 推进。

### Motion Lock 与状态所有权

`PlayerMotionDefinition` 可以定义 `TransitionLockEndProgress`，由 `PlayerMotionRuntime` 通过 `PlayerMotionSnapshot.IsTransitionLocked` 暴露。

Motion Lock 是状态切换约束，不拥有状态切换权。

普通候选转换可以在承诺窗口内被 `PlayerStateController` 拒绝；初始化、跌落、落地、重落地等强制转换由状态层裁决。Motion 数据用于表达当前运动的普通打断窗口，Gameplay State 的切换权仍位于状态层。

## 5. Motion：数据驱动的特殊运动

`PlayerMotion` 是当前架构中的独立 Runtime 数据层之一。

### 数据链

```text
AnimationClip
    │  Editor Bake
    ▼
PlayerMotionProfile
    │
    ▼
PlayerMotionDefinition
    │
    ▼
PlayerMotionCatalog
    │
    ▼
PlayerMotionPlanner
    │
    ▼
PlayerMotionRuntime
    │
    ▼
PlayerMotionFrame / PlayerMotionSnapshot
```

### PlayerMotionProfile

`PlayerMotionProfile` 保存从动画离线烘焙得到的运动数据、Foot Motion Channel 与 Foot Plant Marker。Runtime 读取这些结果，不在 Gameplay 运行时重新分析 AnimationClip。

### PlayerMotionDefinition

`PlayerMotionDefinition` 给一份 Profile 增加运行语义，主要包括：

- 平移策略
- 旋转策略
- Basis 选择
- 运行时持续时间与位移倍率
- Entry Handoff（源地面循环 → Finite Motion）与 Exit Handoff（Finite Motion → 目标 Locomotion）的区间和权重曲线
- Transition Lock 承诺窗口
- 被打断后的退出策略
- 是否按 Foot Phase 选择左右脚 Profile
- 是否需要对应动画表现

Profile 描述动画中的运动数据，Definition 描述这些数据在 Gameplay 中的使用方式。

### PlayerMotionCatalog

`PlayerMotionCatalog` 是 Motion 语义索引：

```text
PlayerMotionId
    ↓
PlayerMotionDefinition
```

它同时保存 Walk / Run / FastRun 的 `PlayerLocomotionCycleDefinition`。

### PlayerMotionPlanner

`PlayerMotionPlanner` 位于 Simulation 层，把 **Gameplay Transition / Intent 转换为 Motion 选择**。

它负责：

- 状态进入 / 退出 Motion 解析
- Start / Stop / 180° Turn，以及 Dodge 完成进入 Idle 时的 DodgeToIdle Motion 选择
- 根据 Foot Phase 选择对应 Foot Profile
- Motion 启动时，若 Definition 启用 Entry Handoff 且 Phase 有有效地面 Loop，从 `PhaseSnapshot.Mode` 与 `MotorResult.HorizontalVelocity` 捕获 Entry Source
- 驱动 `PlayerMotionRuntime`
- 持有并提交 `PlayerLocomotionPhaseRuntime`

当前不负责：

- Animancer 播放
- AnimationClip 选择
- CharacterController 移动
- Gameplay State 的最终切换裁决

### PlayerMotionRuntime

`PlayerMotionRuntime` 演进当前已选择的 `PlayerMotionDefinition` / `PlayerMotionProfile`：

- 管理 Motion instance 与 progress
- 采样烘焙位移、Yaw
- 捕获并保留 Entry Source 的地面模式与平面速度
- 计算 Entry / Exit Handoff 进度、位移权重与 Translation Authority
- 暴露完成 / 取消 / Transition Lock 快照
- 产出本帧 `PlayerMotionFrame`
- 完成帧保留 Definition / Profile 与 JustCompleted 供 Phase / Animation 消费，下一次 BeginFrame 清理已结束实例的引用和帧事件

## 6. Gameplay Intent → 实际移动

跨层移动数据通过显式结构传递：

```text
PlayerGameplayIntent
        +
PlayerMotionFrame
        +
previous PlayerMotorResult
        ↓
PlayerMotionComposer
        ↓
PlayerMotorCommand
        ↓
PlayerMotor
        ↓
PlayerMotorResult
```

### PlayerMotionComposer

`PlayerMotionComposer` 是 Gameplay 常规移动与烘焙 Motion 之间的合成边界。

`SteeredLocalTrajectory` 使用 `ProfileYaw + EntryFacing`：Runtime 保留原始局部轨迹位移，并通过 Frame 提供相对本次开始 Yaw 的帧前理论世界朝向。Composer 用实际朝向与理论朝向之差恢复已有修正，叠加本帧额外 Yaw 修正的一半来旋转动画位移增量；动画自身 Yaw 不重复应用，Entry Source 与目标 Locomotion 位移不参与该旋转。Composer 不保存跨帧修正状态，现有资产不自动切换此策略。

状态层表达移动意图，Motion 提供当前特殊运动的本帧贡献，Composer 生成最终 Motor 参数：

- Entry Source 速度、Authored Motion 位移与目标 Locomotion 预测速度使用统一三路权重合成
- Immediate Velocity Driven：优先消费 Gameplay 的平面速度覆盖与本帧有效时长
- Velocity Driven
- Displacement Driven
- Face Direction
- Yaw Delta
- 垂直冲量

Entry / Exit Handoff 的跨层数据流为：

```text
Motion Begin + PhaseSnapshot + MotorResult.HorizontalVelocity
                              │
                              ▼
                     PlayerMotionPlanner
                              │ PlayerMotionEntrySource
                              ▼
                     PlayerMotionRuntime
                       ├──► PlayerMotionFrame
                       │       └──► PlayerMotionComposer
                       └──► PlayerMotionSnapshot
                               ├──► PlayerLocomotionPhaseRuntime
                               └──► PlayerAnimationController
```

`PlayerMotionEntrySource` 只包含地面 Loop 模式与平面速度等 Simulation 数据；Planner、Runtime 和 Composer 不持有 Animancer State 或 AnimationClip。位移由 Entry Source 速度、Authored Motion 位移和目标 Locomotion 预测速度三路权重合成。

### PlayerMotor

`PlayerMotor` 是当前**唯一 CharacterController 执行器**。

它解释 `PlayerMotorCommand`，负责：

- 常规加减速、立即速度或直接位移；立即速度按命令有效时长（限制在本帧内）积分
- 重力与垂直速度
- Jump 垂直冲量
- Ground Snap
- 实际 CharacterController.Move
- 旋转
- 输出真实 `PlayerMotorResult`

当前不包含 Dodge、Jump State、Stop Animation 或 Animancer 等高层语义。

## 7. Ground 与 Landing

### PlayerGroundProbe

`PlayerGroundProbe` 封装地面探测和 Ground Snap 所需事实，由 `PlayerMotor` 消费。

### PlayerLandingTracker

`PlayerLandingTracker` 记录空中最高高度，在 `MotorResult.JustLanded` 时用最高高度与当前高度之差生成一次性的 `PlayerLandingSnapshot`。快照包含 Sequence、Severity 与 FallDistance；等级仅由坠落高度决定，默认 Lv2 / Lv3 / Lv4 阈值分别为 1 / 2 / 3 米。

### Landing Presentation

```text
MotorResult + 当前高度
    ↓
LandingTracker → LandingSnapshot
    ├──► PlayerContext / AirState → StateController → 普通地面状态或 HardLanding
    └──► PlayerSimulationDriver + Post-Tick Transition
              ↓
        PresentationResolver / HardLanding 分支
              ↓
        AnimationController
              ├── Land1 / Land2 / Land3：Presentation Edge → 目标 Loop
              └── HardLand（Land4 资源槽）：按状态 PresentationProgress 采样
```

`PlayerLandingPresentationResolver` 只消费 Snapshot，按 Severity 映射 Land1 / Land2 / Land3 / HardLand。Driver 结合本帧 Post-Tick 转换决定是否提交表现；落地立即再次 Jump 时不提交落地表现。当前落地不启动 Motion，移动输入不参与落地等级和 Clip 语义选择；普通移动仍由当前地面状态的 Intent 驱动。

## 8. Locomotion Phase 与脚步语义

Foot Phase 是 **Simulation Fact**，不是 AnimationController 内部状态。

当前数据流：

```text
PlayerMotorResult
    +
Current LocomotionMode
    +
Active Motion Snapshot
        ↓
PlayerLocomotionPhaseRuntime
        ↓
PlayerLocomotionPhaseSnapshot
        ├──► PlayerMotionDefinition.ResolveEntryFoot
        │      选择对应的左右脚 Motion Profile
        │
        └──► PlayerAnimationController
               用于选择 Loop 变体与手动采样 NormalizedTime
```

当前相位关系：

- Phase 的推进依据实际运动结果与 Motion 状态
- Entry Handoff Active 时，即使 Gameplay 已切换到 Idle，仍保留当前 Source Loop 的 Profile、VariantFoot 与 NormalizedPhase，并用本帧实际平面位移推进
- 有效 Entry Source 且接地时优先保留；不满足保留条件时，离地或非地面 Loop 模式会关闭 Cycle
- 其余情况下，Active Motion 在 Exit Handoff 前暂停 Loop；进入 Exit Handoff 或地面模式变化时建立目标 Cycle，随后按实际平面位移推进。相位所有权始终在 Phase Runtime
- AnimationController 不生产 Phase
- Motion Definition 消费 Phase 选择左右脚版本
- AnimationController 把同一份 Phase Fact 转换为 Pose

## 9. Animation 表现层

### PlayerAnimationSet

`PlayerAnimationSet` 是运行语义与具体 Animancer `ClipTransition` 的资源映射边界。

它负责：

- `PlayerMotionDefinition + selected PlayerMotionProfile → Motion ClipTransition`
- `PlayerLocomotionMode + PlayerFoot → Loop ClipTransition`
- Jump / Dodge Presentation Cue 与 Landing Key → ClipTransition
- 校验 Catalog、Definition、Profile 与 Animation Binding 的一致性

### PlayerAnimationController

`PlayerAnimationController` 将 Gameplay、Motion 和 Simulation Phase Fact 表现为 Pose。

当前 Animancer Graph 使用 Manual Update。

主要时间来源：

- **Boundary Motion**：按 `PlayerMotionSnapshot.Progress` 手动采样
- **Ground Loop**：按 `PlayerLocomotionPhaseSnapshot.NormalizedTime` 手动采样
- **Dodge / HardLanding**：按对应 Gameplay State 的 PresentationProgress 手动采样
- **Jump / 普通 Landing Edge**：由 Animancer 推进，结束事件切回目标 Loop

Controller 使用 `GetOrCreateState` 建立手动播放状态，取消 Animancer Fade，并在新 Motion 开始时停止自身未持有的活动 State。普通 Motion 播放期间 Source Loop、Boundary Motion 与 Target Loop 的权重统一由 Controller 写入。DodgeToIdle 入口使用下述独立 Fade 分支。

存在有效 Motion Entry Source 时，Controller 从当前 `stableLoopState` 转移并拥有 `entrySourceLoopState`，按 Phase Snapshot 继续采样源 Loop，并使用 Motion Definition 的 Entry 区间计算姿态权重。不存在有效 Entry Source 时，如果当前稳定 Loop 存在，则使用 `ClipTransition.FadeDuration / Motion Duration` 形成回退 Entry Pose 区间；区间结束、Motion 取消或被替换时清理 Source State。

Exit Handoff 期间，Controller 使用 `exitPoseWeight` 将 Boundary Pose 移交到目标 Locomotion Loop。Entry 与 Exit 区间重叠时，三路姿态权重统一组合：

```text
Source Loop    = 1 - EntryTargetWeight
Boundary Pose  = EntryTargetWeight × (1 - ExitTargetWeight)
Target Loop    = EntryTargetWeight × ExitTargetWeight
```

Dodge 完成且无原始移动输入时，Planner 启动 `DodgeToIdle`。Controller 保留 Dodge Pose，以 `FadeMode.FixedDuration` 混合到按 Motion Progress 采样的结束 Clip；Fade 时长不超过到 Exit Handoff 起点的剩余时间。Fade 期间暂停三路手动权重写入，源 State 失活或 Motion 进入 Exit / 完成时结束 Fade 并停止源 State，再恢复 Exit Pose 权重。被取消、替换或发生其他状态转换时清理该 Fade。Dodge 完成且仍有输入则进入 FastRun，直接播放目标 Loop。Walk / Run 互切也使用 FixedDuration Fade。

位移侧对应由 `PlayerMotionComposer` 使用 Entry Translation Weight 与 Exit Translation Authority 组合 Entry Source、Authored Motion 和目标 Locomotion 三路位移。

## 10. Editor 烘焙与预览工具链

运行时数据由 `Assets/Tools/AnimationPreview/Editor/` 下的 Editor 工具生成和检查。

当前数据方向：

```text
AnimationClip / Preview Config
        ↓
Animation Preview / Baker / Foot Plant Detector
        ↓
PlayerMotionProfile
        ↓
Definitions / Catalog / AnimationSet validation
        ↓
Runtime
```

`AnimationPreviewSession.SampleMotion` 对含完整 RootT / RootQ 曲线的 Humanoid Clip 直接采样本地根运动曲线；其他情况从模型 Transform 采样并撤销模型初始旋转。脚部位置仍由逐帧 Clip.SampleAnimation 后的骨骼采样产生。批量 Baker 按发现的 Profile 处理，不限定固定数量。

`Project.AnimationPreview.Editor.asmdef` 是 Editor-only，并依赖 `Project.PlayerMotion.Runtime`。

当前依赖方向：

```text
Editor Tooling ──► PlayerMotion Runtime
PlayerMotion Runtime -X-> Editor Tooling
```

## 11. 程序集边界

当前已存在的关键 asmdef：

```text
Project.PlayerMotion.Runtime
    references: none

Project.PlayerLanding.Runtime
    └──► Project.PlayerMotion.Runtime

Project.AnimationPreview.Editor
    └──► Project.PlayerMotion.Runtime
    Editor only
```

`PlayerMotion` 数据类型承担底层运行时契约职责。

需要注意：Input、State、Simulation、Ability、Animation 等多数玩家模块当前没有各自独立的 asmdef，主要编译在默认 `Assembly-CSharp`；`PlayerAnimationSetEditor` 等对应 Editor 代码编译在默认 `Assembly-CSharp-Editor`。因此这些层之间的职责边界主要由设计和显式数据契约维护，只有 Motion、Landing 与 Animation Preview Editor 的部分依赖由 asmdef 强制约束。

测试程序集：

```text
Project.PlayerMotion.Tests
    ├──► Project.PlayerMotion.Runtime
    └──► Project.PlayerLanding.Runtime

Project.AnimationPreview.Editor.Tests
    ├──► Project.AnimationPreview.Editor
    └──► Project.PlayerMotion.Runtime
```

测试程序集只验证稳定的行为契约：Motion/Phase/Landing 的纯运行时演进、Definition/Profile/Composer 数值约束、必要的默认资产合法性，以及 Editor 采样、检测和批量烘焙的原子性。测试不通过反射锁定默认程序集的私有结构，也不参与玩家运行时数据流。

动画 State 所有权、场景衔接、卡顿、滑步和输入手感由人工场景检查与 `PlayerAnimationSetEditor` 资产校验承担；测试程序集编译成功也不等同于 Unity EditMode 测试已经实际执行。

## 12. 配置与资产 Source of Truth

核心运行配置位于：

```text
Assets/Settings/Player/Motion/
├── DefaultPlayerMovementConfig.asset
├── DefaultPlayerMotionCatalog.asset
├── DefaultPlayerAnimationSet.asset
├── Definitions/
├── Profiles/
└── FootCalibration/
```

职责分别是：

- `PlayerMovementConfig`：常规 Locomotion、Motor Physics、Landing 等 Gameplay/Physics 参数
- `PlayerMotionCatalog`：MotionId → Definition 与 Locomotion Cycle 索引
- `PlayerMotionDefinition`：Motion 运行策略
- `PlayerMotionProfile`：烘焙运动 / Foot Motion Channel / Foot Marker 数据
- `PlayerAnimationSet`：运行语义 → 具体动画资源
- `PlayerFootCalibration`：Foot Marker / 烘焙相关角色校准数据

`Assets/Prefabs/Player.prefab` 是当前 Player 组件装配和序列化引用的重要 Source of Truth。

当前默认 Catalog 包含 16 个 Motion Definition 与 Walk / Run / FastRun 三个 Locomotion Cycle。15 个 Start / Stop / Turn Definition 的 Entry 通常为 `0→0.2`，`WalkToIdle` 为 `0→0.12`，Exit 为 `0.7→1`；`DodgeToIdle` 关闭 Entry Handoff，Exit 为 `0.8→1`。Dodge 本体使用能力速度与独立 Clip，Catalog 中没有 Dodge 或移动落地 Motion。

`Assets/Settings/Player/DefaultPlayerDodgeConfig.asset` 保存 Dodge 参数，当前默认 Duration 为 0.3 秒、Speed 为 12、Cooldown 为 0.35 秒。AnimationSet 中 WalkToIdle / RunToIdle / FastRunToIdle 的 Entry Pose 使用端点零切线的平滑曲线，其余默认 Entry 与默认 Exit Pose 曲线为线性。

## 13. 当前依赖关系

```text
Input Implementation
    ↓ interfaces
Simulation / State / Camera

State
    ↓ intent / transition semantics
Motion Planner

Motion Data / Runtime
    ↓ frame / snapshot
Composer / Simulation / Presentation

Composer
    ↓ command
Motor
    ↓ result
State / Landing / Phase

Simulation Facts
    ↓
Animation Presentation

Editor Tooling
    ↓
Runtime Data
```

具体文件位置记录在 `Docs/CodeMap.md`。
