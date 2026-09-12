# Source Exit Handoff 迁移记录

2026-09-12。旧节点关系保留为 Resolver 代码，资产仅保存来源退出秒数与单曲线。Profile/Clip GUID、运动策略、Basis、锁定窗口未改动。

## 参数选择

Motion 优先采用原自然后继的平移曲线和时长；比例时长按默认 Profile 换算为固定秒数。Loop 采用主要停止动作的旧进入配置；Idle 采用 IdleToRun 的旧 Idle 来源覆盖。所有 Pose 改为跟随同一平移曲线。

| Source 资产 | Exit 秒数 | 初值来源 |
|---|---:|---|
| IdleToWalkDefinition | 0.42 | 原自然后继平移曲线；时长按默认 Profile 换算 |
| WalkToIdleDefinition | 0.6 | 原自然后继平移曲线；时长按默认 Profile 换算 |
| IdleToRunDefinition | 0.34 | 原自然后继平移曲线；时长按默认 Profile 换算 |
| RunToIdleDefinition | 0.6 | 原自然后继平移曲线；时长按默认 Profile 换算 |
| FastRunToIdleDefinition | 0.8 | 原自然后继平移曲线；时长按默认 Profile 换算 |
| WalkStart180LeftDefinition | 0.42 | 原自然后继平移曲线；时长按默认 Profile 换算 |
| WalkStart180RightDefinition | 0.42 | 原自然后继平移曲线；时长按默认 Profile 换算 |
| RunStart180LeftDefinition | 0.34 | 原自然后继平移曲线；时长按默认 Profile 换算 |
| RunStart180RightDefinition | 0.34 | 原自然后继平移曲线；时长按默认 Profile 换算 |
| WalkTurn180LeftDefinition | 0.53 | 原自然后继平移曲线；时长按默认 Profile 换算 |
| WalkTurn180RightDefinition | 0.59 | 原自然后继平移曲线；时长按默认 Profile 换算 |
| RunTurn180LeftDefinition | 0.43 | 原自然后继平移曲线；时长按默认 Profile 换算 |
| RunTurn180RightDefinition | 0.41 | 原自然后继平移曲线；时长按默认 Profile 换算 |
| FastRunTurn180LeftDefinition | 0.33 | 原自然后继平移曲线；时长按默认 Profile 换算 |
| FastRunTurn180RightDefinition | 0.33 | 原自然后继平移曲线；时长按默认 Profile 换算 |
| DodgeToIdleDefinition | 0.3 | 原自然后继平移曲线；时长按默认 Profile 换算 |
| IdleLocomotionDefinition | 0.5 | 沿用主要出口 IdleToRunDefinition 的平移混合参数 |
| WalkLocomotionDefinition | 0.24 | 沿用主要出口 WalkToIdleDefinition 的平移混合参数 |
| RunLocomotionDefinition | 0.4 | 沿用主要出口 RunToIdleDefinition 的平移混合参数 |
| FastRunLocomotionDefinition | 0.533333 | 沿用主要出口 FastRunToIdleDefinition 的平移混合参数 |

## 行为差异

- 同一 Source 的所有出口共用 Exit 参数。Start→End 不再读取 End 的进入配置，采用 Start 的退出配置。
- Walk/Run/FastRun Loop 的主停止路径继承原平移时长；其他出口也使用该退出值。
- 原 Stop 进入的平滑 Pose 覆盖被删除，Pose 与位移统一，观感需要人工检查。
- 原 SourceMotionRatio 按实际左右脚 Profile 换算；现在按默认 Profile 一次换算为秒，切换脚变体不会改变退出时长。
- 混合中请求 C 继续保留原 Source，旧 Target 被替换，新目标立即承接剩余权重，不保证中断瞬间姿态/速度连续。
- End→Idle、Turn→Loop、DodgeToIdle→Idle 保留生产后继，不因移除序列化关系而删除。

## Review 顺序

1. PlayerHandoffSettings / PlayerLocomotionDefinition / PlayerMotionDefinition：固定秒数、曲线端点、Loop Profile 引用。
2. PlayerMotionHandoffResolver：步态停止目标、0.7 自然触发、DodgeToIdle 的 0.8 触发。
3. PlayerMotionPlanner.TryResolveSourceExitMotion → PlayerHandoffRuntime.Request：两类触发共用入口、反向复用、重复目标不重启。
4. PlayerHandoffRuntime.Advance：精确触发、剩余帧时间、Source 完成后零位移、混合结束释放。
5. PlayerMotionComposer / PlayerAnimationController：已有双端消费，位移采用段内平均权重，Pose 采用帧末权重。

## 验证

既有测试更新了退出参数所有权、单曲线及代码后继契约，没有新增测试文件/用例。原“资产禁止 Loop 进度后继”检查改为“Resolver 不为 Loop 提供自然后继”。运行测试仍检查双节点中断保留进度、重复目标、返回来源、精确触发与零时长。

默认资产原有 SteeredLocalTrajectory 配置与 ProfileYaw + EntryFacing 约束不匹配，导致两项资产整体校验失败；本次未改变这些运动策略，未放宽校验。

- 静态检查：`git diff --check` 通过；旧 Handoff 类型/字段在代码与资产中无剩余引用。16 份 Motion 资产的原运动字段与 Git HEAD 一致。
- `dotnet build Unity-3D-Project.sln --no-restore`：0 错误，21 项依赖程序集冲突警告。
- Unity EditMode `Project.PlayerMotion.Tests`：52 项通过，2 项失败（上述既有资产策略冲突），0 跳过。工具汇总总节点为 75；不把该数作为叶测试数量。
- Unity 实际资源检查：16 份 Motion Exit 配置与 4 份 Loop SO 有效，57 组 Motion/Loop Clip 与 Profile 源 GUID/fileID 一致。
- PlayMode、动画观感、滑步和输入手感未执行人工检查。
