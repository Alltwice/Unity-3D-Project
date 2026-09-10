# Player Motion Code Map

## Handoff 配置归属

| 文件 | 类型 | 当前职责 |
|---|---|---|
| `Assets/Scripts/Player/PlayerMotion/PlayerMotionNodeKey.cs` | `PlayerMotionNodeKey`, `PlayerMotionNodeKind` | 标识 Loop 或 Motion 节点 |
| `Assets/Scripts/Player/PlayerMotion/PlayerHandoffSettings.cs` | `PlayerHandoffBlendSettings` | 时长模式、时长值、姿态曲线与平移曲线 |
| 同上 | `PlayerHandoffEntrySettings`, `PlayerHandoffSourceOverride` | 目标节点请求进入默认参数与精确来源覆盖 |
| 同上 | `PlayerHandoffSuccessorSettings` | Motion 的默认后继目标、触发进度与混合参数 |
| 同上 | `PlayerHandoffResolution` | Catalog 返回的普通解析结果及配置来源标识 |
| `Assets/Scripts/Player/PlayerMotion/PlayerMotionDefinition.cs` | `PlayerMotionDefinition` | 保存 Motion 运行策略、内嵌进入配置和默认后继 |
| `Assets/Scripts/Player/PlayerMotion/PlayerMotionCatalog.cs` | `PlayerMotionCatalog` | 保存 Motion 索引、Loop Cycle 与四种 Loop 进入配置；统一解析 Request 和默认后继 |
| `Assets/Scripts/Player/PlayerMotion/PlayerHandoffRuntime.cs` | `PlayerHandoffRuntime` | 唯一 Handoff 执行器，保留至多两个节点并消费解析结果 |

`PlayerLocomotionCycleDefinition` 位于 `Assets/Scripts/Player/PlayerMotion/PlayerLocomotionCycleDefinition.cs`，只保存 Walk、Run、FastRun 的脚步 Profile，不承载 Handoff 进入配置。

## 解析与消费调用链

```text
PlayerMotionPlanner
  → PlayerHandoffRuntime.Request(target)
      → PlayerMotionCatalog.ResolveRequest(source, target)
          → MotionDefinition.HandoffEntry 或 Catalog LoopHandoffEntries
          → 精确来源覆盖 / 目标默认进入
      → PlayerHandoffRuntime.StartBlend(resolution)
          → resolution.Blend.ResolveDuration(sourceDuration, targetDuration)
          → 双端 PlayerMotionFrame / PlayerHandoffStep
              → PlayerMotionComposer → PlayerMotor
          → PlayerHandoffSnapshot
              → PlayerLocomotionPhaseRuntime / PlayerAnimationController
```

```text
PlayerHandoffRuntime.Advance
  → PlayerMotionCatalog.TryGetSuccessor(target.Key)
      → target MotionDefinition.DefaultSuccessor
      → 在源进度边界创建后继节点
```

请求来源使用 Runtime 当前保留的节点实例对应的 `PlayerMotionNodeKey`。Runtime 的两端采样、目标替换、来源权重继承、旋转接管、事件消费和时间边界处理仍由 `PlayerHandoffRuntime` 负责。

## Inspector 映射

| 文件 | 类型 | 展示内容 |
|---|---|---|
| `Assets/Scripts/Player/PlayerMotion/Editor/PlayerMotionDefinitionEditor.cs` | `PlayerMotionDefinitionEditor` | Motion 普通字段、进入混合、来源覆盖、默认后继 |
| `Assets/Scripts/Player/PlayerMotion/Editor/PlayerMotionCatalogEditor.cs` | `PlayerMotionCatalogEditor` | Loop 模式进入配置、Catalog 普通字段、Request 解析预览 |
| `Assets/Scripts/Player/PlayerMotion/Editor/PlayerHandoffInspectorUtility.cs` | `PlayerHandoffInspectorUtility` | 共享混合字段绘制和比例时长说明 |

Catalog 解析预览直接调用 `PlayerMotionCatalog.ResolveRequest`；比例时长通过 `TryGetNodeDuration` 读取可用 Profile，缺少实际 Profile 时显示未确定，不启动动画或改变运行状态。

## 资产映射

```text
Assets/Settings/Player/Motion/DefaultPlayerMotionCatalog.asset
  ├── motions → Assets/Settings/Player/Motion/Definitions/*.asset
  ├── locomotionCycles → Assets/Settings/Player/Motion/Profiles/*.asset
  └── loopHandoffEntries → Idle / Walk / Run / FastRun 进入配置

Assets/Settings/Player/Motion/Definitions/*.asset
  ├── handoffEntry → 目标 Motion 默认进入与来源覆盖
  └── defaultSuccessor → 源 Motion 默认后继
```

一次性迁移结果和等价检查记录在 `Docs/HandoffMigrationReport.md`。旧 `PlayerHandoffDefinition` 类型、Catalog 旧关系列表、旧关系资产及其 `.meta` 已删除。
