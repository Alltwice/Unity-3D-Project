# Handoff 迁移报告

状态：成功（Unity AssetDatabase 迁移、等价复核和迁移后解析校验完成）

## 统计

| 项目 | 数量 |
|---|---:|
| 实际 Catalog | 1 |
| 迁移前 Request 关系 | 361 |
| 迁移前 Motion 默认后继关系 | 16 |
| 已迁移旧 `.asset` | 377 |
| 已删除旧 `.meta` | 377 |
| 新 Loop 进入配置 | 4 |
| 新 Motion 内嵌配置 | 16 |
| 保留来源覆盖 | 15 |

迁移前关系引用来自 `Assets/Settings/Player/Motion/DefaultPlayerMotionCatalog.asset` 的旧 `handoffs` 列表。旧关系目录已清空，Catalog 已重新序列化并移除旧列表字段。

## 迁移前快照摘要

默认参数按完整参数组出现次数最多选择；数量相同时按来源 NodeKey 排序选择，保证迁移结果稳定。

旧关系按目标节点分组。所有曲线复制均保留完整 Key、时间和值、入/出切线、tangentMode、WeightedMode、入/出权重以及 Pre/Post WrapMode；没有按端点或采样值做降级比较。

| 目标节点 | Request 数 | 选择的默认参数 | 默认关系数 | 覆盖数 |
|---|---:|---|---:|---:|
| IdleLoop | 19 | Seconds 0.5 | 19 | 0 |
| WalkLoop | 19 | Seconds 0.4 | 19 | 0 |
| RunLoop | 19 | Seconds 0.4 | 19 | 0 |
| FastRunLoop | 19 | Seconds 0.2 | 19 | 0 |
| IdleToWalk | 19 | TargetMotionRatio 0.2 | 18 | 1 |
| WalkToIdle | 19 | TargetMotionRatio 0.12 | 18 | 1 |
| IdleToRun | 19 | TargetMotionRatio 0.2 | 18 | 1 |
| RunToIdle | 19 | TargetMotionRatio 0.2 | 18 | 1 |
| FastRunToIdle | 19 | TargetMotionRatio 0.2 | 18 | 1 |
| WalkStart180Left | 19 | TargetMotionRatio 0.2 | 18 | 1 |
| WalkStart180Right | 19 | TargetMotionRatio 0.2 | 18 | 1 |
| RunStart180Left | 19 | TargetMotionRatio 0.2 | 18 | 1 |
| RunStart180Right | 19 | TargetMotionRatio 0.2 | 18 | 1 |
| WalkTurn180Left | 19 | TargetMotionRatio 0.2 | 18 | 1 |
| WalkTurn180Right | 19 | TargetMotionRatio 0.2 | 18 | 1 |
| RunTurn180Left | 19 | TargetMotionRatio 0.2 | 18 | 1 |
| RunTurn180Right | 19 | TargetMotionRatio 0.2 | 18 | 1 |
| FastRunTurn180Left | 19 | TargetMotionRatio 0.2 | 18 | 1 |
| FastRunTurn180Right | 19 | TargetMotionRatio 0.2 | 18 | 1 |
| DodgeToIdle | 0 | 禁用请求进入 | 0 | 0 |

## 保留的来源覆盖

15 个 Motion 目标均保留 `IdleLoop` 精确来源覆盖；其余来源使用上表默认参数。

| 目标节点 | 覆盖来源 | 覆盖参数 |
|---|---|---|
| IdleToWalk | IdleLoop | Seconds 0.08 |
| WalkToIdle | IdleLoop | Seconds 0.3 |
| IdleToRun | IdleLoop | Seconds 0.5 |
| RunToIdle | IdleLoop | Seconds 0.12 |
| FastRunToIdle | IdleLoop | Seconds 0.08 |
| WalkStart180Left / WalkStart180Right | IdleLoop | Seconds 0.1 |
| RunStart180Left / RunStart180Right | IdleLoop | Seconds 0.1 |
| WalkTurn180Left / WalkTurn180Right | IdleLoop | Seconds 0.1 |
| RunTurn180Left / RunTurn180Right | IdleLoop | Seconds 0.1 |
| FastRunTurn180Left / FastRunTurn180Right | IdleLoop | Seconds 0.1 |

`DodgeToIdle` 没有旧 Request 目标关系，因此没有被擅自启用；其原有直接建立路径不由进入配置替代。

## 默认后继快照

| 来源 Motion | 目标节点 | 源触发进度 | 混合参数 |
|---|---|---:|---|
| IdleToWalk | WalkLoop | 0.7 | SourceMotionRatio 0.3 |
| WalkToIdle | IdleLoop | 0.7 | SourceMotionRatio 0.3 |
| IdleToRun | RunLoop | 0.7 | SourceMotionRatio 0.3 |
| RunToIdle | IdleLoop | 0.7 | SourceMotionRatio 0.3 |
| FastRunToIdle | IdleLoop | 0.7 | SourceMotionRatio 0.3 |
| WalkStart180Left / WalkStart180Right | WalkLoop | 0.7 | SourceMotionRatio 0.3 |
| RunStart180Left / RunStart180Right | RunLoop | 0.7 | SourceMotionRatio 0.3 |
| WalkTurn180Left / WalkTurn180Right | WalkLoop | 0.7 | SourceMotionRatio 0.3 |
| RunTurn180Left / RunTurn180Right | RunLoop | 0.7 | SourceMotionRatio 0.3 |
| FastRunTurn180Left / FastRunTurn180Right | FastRunLoop | 0.7 | SourceMotionRatio 0.3 |
| DodgeToIdle | IdleLoop | 0.8 | SourceMotionRatio 0.2 |

每条默认后继的姿态曲线、平移曲线、目标和触发进度均与旧关系等价。

## 一致性检查

- 迁移预检：1 个 Catalog、361 条 Request、16 条默认后继；无共享 MotionDefinition 冲突，无外部旧关系引用。
- 迁移时逐条解析：361 条 Request 和 16 条默认后继均通过新 Catalog 等价校验。
- 迁移后实际 Catalog 校验：4 个 Loop 配置、16 个 Motion 配置、361 条有效 Request、19 条因 `DodgeToIdle` 禁用而明确失败的 Request、16 条有效默认后继。
- `StartBlend` 仍从解析结果的实际两端 Duration 换算比例时长；Runtime 仍由唯一 `PlayerHandoffRuntime` 消费。
- Motion、Locomotion Cycle、Profile、Clip、移动策略和 Basis 字段未由迁移工具改写；Catalog 序列化仅新增节点 Handoff 配置并保留原 Cycle 引用。
