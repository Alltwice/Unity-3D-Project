# Architecture

> 本文记录项目当前较稳定的职责边界、依赖方向和核心运行流  
> 最后核对的运行时代码基线：当前工作树（2026-09-12，包含 Source Exit Handoff 与 Loop Definition 资产化）

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
读取 FacingMode 与相机水平基准
    ↓
Motion Planner 同步 FacingMode，清理不兼容的地面 Motion
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
- `IPlayerInputSource.FacingMode` 由 `PlayerInputReader` 以 `Player/FacingModeToggle` 的一次按下切换，默认是 `MovementAligned`，禁用时重置
- Jump / Dodge 等离散操作写入 `IPlayerActionBuffer`
- `PlayerActionBuffer` 在输入回调时机与 Gameplay 模拟时机之间保存短时动作请求

`Player/FacingModeToggle` 使用 `<Mouse>/rightButton` 的 Press 绑定；`UI/RightClick` 仍是独立的 UI Action，不参与 Gameplay 朝向模式切换。

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

Driver 每帧读取一次 `FacingMode` 和相机水平 Forward / Right，并由统一的意图入口分别生成 `DesiredMoveDirection` 与 `DesiredFacingDirection`：`MovementAligned` 在有有效移动时面向移动方向、无移动时保持角色水平朝向；`Independent` 始终面向相机水平 Forward。移动和朝向共用同一相机水平基准；相机 Forward 水平投影接近零时由相机 Right 推导。Pre-Tick 与 Post-Tick 使用同一基准，Post-Tick 按新 Gameplay 状态重新应用地面零输入宽限结果。

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
- 自身 ExitHandoff 保存固定秒数与统一位移/姿态曲线；后继目标与触发进度由 Resolver 代码保存
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

它同时索引 Idle / Walk / Run / FastRun 的 `PlayerLocomotionDefinition` SO。定义保存默认及左右脚 Loop Profile 和自身 ExitHandoff；Idle 不采样移动周期。

Catalog 通过 `TryGet` / `TryGetLocomotion` 查找两类 Definition，并校验重复标识、Profile、退出曲线和代码后继所需 Loop。Catalog 不解析动画配对或混合关系。

### PlayerMotionPlanner

`PlayerMotionPlanner` 位于 Simulation 层，把 **Gameplay Transition / Intent 转换为 Motion 选择**。

它负责：

- 状态进入 / 退出 Motion 解析
- Start / Stop / 180° Turn，以及 Dodge 完成后的 `DodgeToIdle` Motion 或 `FastRunLoop` 目标选择
- Independent 下地面状态直接请求对应 Loop，跳过 Start / Stop / Turn180；进入该模式时清理 Handoff Source / Target 中仍在途的地面 Motion
- 根据 Foot Phase 选择对应 Foot Profile
- 根据已批准的状态转换请求目标，HandoffRuntime 保留源采样实例及切换时平面速度
- 驱动 `PlayerMotionRuntime`
- 持有并提交 `PlayerLocomotionPhaseRuntime`

当前不负责：

- Animancer 播放
- AnimationClip 选择
- CharacterController 移动
- Gameplay State 的最终切换裁决

### 统一 Handoff

`PlayerMotionNodeKey.cs` 标识 Motion / Loop 节点。`PlayerHandoffSettings.cs` 仅保存 Duration（秒）与 Curve（0→1）；配置分别位于 MotionDefinition 与 LocomotionDefinition 的 ExitHandoff。

`PlayerMotionHandoffResolver.cs` 集中保存 Start / Turn → Loop、End → Idle、DodgeToIdle → Idle 的自然后继及触发进度，并根据 Gameplay 步态选择停止 Motion。自然进度通常为 0.7，DodgeToIdle 为 0.8。Planner 解析状态与意图，把停止目标选择交给 Resolver；输入模式和状态裁决仍属于原有层。

`PlayerHandoffRuntime.cs` 持有至多两个独立采样实例。Gameplay 直接 Request，自然后继在帧内推进至 Resolver 给出的触发点后调用同一个 Request，再消费本帧剩余时间。混合参数取自保留 Source 的 ExitHandoff，SourceWeight = InitialWeight × (1-Curve(u))，TargetWeight = 1-SourceWeight；Pose 与位移使用同一权重函数。

A→B 混合中请求 C，保留 A 的采样进度与当前权重，丢弃 B，创建从头采样的 C，并按 A 的完整退出时长重新计时。C 立即接替 B 的权重。重复请求目标不重启；请求来源时交换两端、复用实例，采用新 Source 的退出参数。Source 不接收输入或触发后继，目标提供 Gameplay Motion 事实；已消费的自然后继在返回该实例时不再次触发。

Source Motion 完成后保留最终姿态、后续烘焙位移为零，直到混合结束释放；零时长直接释放来源。帧内按自然触发点和混合结束点分段，输出 PlayerHandoffStep 与 PlayerHandoffSnapshot。Loop 周期由实际 Motor 结果推进。

### PlayerMotionRuntime

`PlayerMotionRuntime` 演进当前已选择的 `PlayerMotionDefinition` / `PlayerMotionProfile`：

- 管理 Motion instance 与 progress
- 采样烘焙位移、Yaw
- 单实例保留 Profile、Basis、方向和采样时间
- 交接计时和权重由 PlayerHandoffRuntime 计算
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

`PlayerGameplayIntent` 分别保存 `LocomotionMode`、`DesiredMoveDirection` 与 `DesiredFacingDirection`；意图创建时两个方向都由 Driver 传入，`Create` 不再用移动方向隐式覆盖目标朝向。

`SteeredLocalTrajectory` 使用 `ProfileYaw + EntryFacing`：Runtime 保留原始局部轨迹位移，并通过 Frame 提供相对本次开始 Yaw 的帧前理论世界朝向。Composer 用实际朝向与理论朝向之差恢复已有修正，叠加本帧额外 Yaw 修正的一半来旋转动画位移增量；动画自身 Yaw 不重复应用，源 SteeredLocalTrajectory 保留降为来源时的朝向修正。Composer 不保存跨帧修正状态，现有资产不自动切换此策略。

状态层表达移动意图，Motion 提供当前特殊运动的本帧贡献，Composer 生成最终 Motor 参数：

- 按 Handoff 两端平移权重合成运动贡献；目标立即接管旋转
- Immediate Velocity Driven：优先消费 Gameplay 的平面速度覆盖与本帧有效时长
- Velocity Driven
- Displacement Driven
- Face Direction
- Yaw Delta
- 垂直冲量

统一 Handoff 的跨层数据流：

```text
Planner（已选定的请求目标）→ HandoffRuntime
  ├→ Resolver（代码触发/目标）+ Source Definition.ExitHandoff（退出参数）
  └→ 唯一混合计时、至多两个节点
       ├→ 双端 MotionFrame → Composer → Motor
       └→ HandoffSnapshot → Phase / AnimationController
```

Planner、HandoffRuntime、MotionRuntime 与 Composer 不持有 Animancer State 或 AnimationClip。

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
- 活动 Handoff 中的 Loop 节点分别持有 PhaseRuntime，按 Motor 实际位移推进
- 地面 Loop 在激活帧建立相位，离地时关闭 Cycle
- 没有 Loop 节点参与时，Phase 保留边动画的脚步事实
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

Controller 按 Handoff 节点实例创建独立 Animancer State，即使 Clip 相同也不共用采样时间。每帧从快照读取两端姿态权重；旧目标被替换或源淡出完成后销毁对应 State。Controller 不推进地面混合时钟。

A→B 混合中请求 C 时，Runtime 保留 A 的采样进度及当前姿态/平移权重，立即释放 B，按来源 A 的退出配置重新计时。源权重为捕获权重乘 `1-Curve(u)`，目标补足到 1；旧目标的姿态与速度贡献被立即替换。请求回到来源时交换两端，复用来源实例恢复权重。

Dodge 完成进入地面时，`PlayerAnimationController` 对 `DodgeToIdle` Motion 和 `FastRunLoop` 共用独立 FixedDuration 姿态 Fade：完成转换时补齐 Dodge 最后姿态，按目标节点实际解析出的 `ClipTransition` 将目标从零权重淡入；目标节点实例未改变时继续当前 Fade，目标被替换或统一 Handoff 开始时先结束专用 Fade，再应用地面两节点快照。再次 Dodge 或进入空中会取消并清理专用 Fade，零时长直接完成。该 Fade 只控制姿态权重，FastRun 的移动与相位仍由 Simulation 立即交给目标；Jump、Landing 和 Dodge 本体仍走独立表现路径。

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
- `PlayerMotionCatalog`：MotionId → MotionDefinition、LocomotionMode → LocomotionDefinition 索引与合法性校验
- `PlayerMotionDefinition`：Motion 运行策略与自身 ExitHandoff
- `PlayerLocomotionDefinition`：Loop Profile、起步脚变体与自身 ExitHandoff
- `PlayerMotionHandoffResolver`：代码中的自然触发/后继目标及 Gameplay 停止目标
- `PlayerMotionProfile`：烘焙运动 / Foot Motion Channel / Foot Marker 数据
- `PlayerAnimationSet`：运行语义 → 具体动画资源
- `PlayerFootCalibration`：Foot Marker / 烘焙相关角色校准数据

`Assets/Prefabs/Player.prefab` 是当前 Player 组件装配和序列化引用的重要 Source of Truth。

当前默认 Catalog 索引 Motion 与 Idle / Walk / Run / FastRun 独立 Loop SO，资产位于 `Assets/Settings/Player/Motion/Definitions/`。原进入配置、来源覆盖和序列化后继已删除；退出时长为固定秒数，曲线从原平移配置迁移，迁移结果记录在 `Docs/HandoffExitMigration.md`。

`Assets/Settings/Player/DefaultPlayerDodgeConfig.asset` 保存 Dodge 参数，当前默认 Duration 为 0.3 秒、Speed 为 12、Cooldown 为 0.35 秒。Handoff 的姿态与位移共用来源退出曲线；Dodge 本体及专用退出姿态 Fade 仍走原有独立路径。

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

Motion / Loop 数据和 Handoff 执行位于 `Assets/Scripts/Player/PlayerMotion/`；Planner 与 Driver 位于 `PlayerSimulation/`；动画映射及播放位于 `PlayerAnimation/`。
