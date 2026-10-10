# Adaptive Boss Arena — 开发约定

这是一款使用 Unity 6 制作的单场遭遇战动作游戏。玩家与一个 Boss 对战，Boss 根据玩家习惯形成统计画像，并逐步调整反制策略。目标是规模小、打磨充分的垂直切片。

## 优先级最高的规则

**Boss 绝不能作弊。** 它不能读取玩家输入、输入缓冲、体力、冷却时间或无敌状态。它只能根据有经验的人类对手通过画面能够感知的信息作出反应，并经过符合人类反应的延迟。

通过结构约束实施这条规则：

1. `AdaptiveBossArena.AI` 和 `AdaptiveBossArena.Learning` 不引用 `AdaptiveBossArena.Player` 或 `Unity.InputSystem`，访问这些程序集的作弊代码无法编译。
2. 玩家状态通过 `IObservablePlayer` / `PlayerObservation` 传给 AI，只暴露可见信息。
3. `DelayedPerceptionSource` 为观察数据施加感知延迟，让 Boss 基于稍旧的数据行动，使佯攻能够奏效。
4. 如果依赖图突破边界，`ArchitectureValidator` 和 `ArchitectureFirewallTests` 会使构建失败。

如果 AI 侧任务看似需要玩家内部状态，不要添加程序集引用。先确认人类能否观察到，再考虑通过 `IObservablePlayer` 暴露，或者寻找其他方案。

## 架构

```text
Utilities  （纯算法，无依赖）
    ↑
   Core     （接口、数据类型、服务）
    ↑
  Combat
    ↑                 ↑
 Player            Learning ← AI          UI
    ↑                 ↑       ↑           ↑
    └────────────── Game（系统组装入口）───┘
                        ↑
                     Editor
```

- Unity 的程序集引用不具有传递性，每个程序集都要列出自己直接使用的依赖。
- `Game` 能看到所有模块，并在场景启动时把系统连接起来。
- 除非 Unity 的对象创建方式没有留下可注入的入口，否则 `Game` 之外的代码不应直接访问 `ServiceRegistry.Current`。

## 编码约定

- 每个公开类型和成员都要有 XML 文档注释，解释设计原因，尤其是阈值和执行顺序背后的理由。
- 私有字段使用 `_camelCase`；序列化字段保持私有，通过公开只读属性暴露。
- 避免魔法数字。结构性常量放在 `GameplayConstants`，可调数值放在 ScriptableObject 中，便于在运行模式下调整。
- 方法保持简短、职责单一，避免重复逻辑。
- 尽量少用 `region`。
- `DamageInfo` 等高频数据优先使用 `readonly struct`，并通过 `in` 传递。
- `Utilities/Compat` 中的 `IsExternalInit` 兼容类型支持仅 `init` 的访问器和 `record`。不要在其他位置重复声明该类型。

## 时间、随机数和事件

- 除 `TimeService` 外，绝不能写入 `Time.timeScale`。通过 `ITimeService` 请求打击停顿和慢动作，让效果统一组合。
- 绝不能调用 `UnityEngine.Random`。注入 `IRandomProvider`，使概率型 AI 可复现、可测试。
- 战斗逻辑使用 `ITimeService.DeltaTime`，在打击停顿期间冻结；UI、镜头震动和暂停菜单使用 `UnscaledDeltaTime`。
- 跨系统和 UI 信号使用 ScriptableObject 事件通道；每帧战斗通信使用接口上的直接 C# 事件。

## 编辑器工作流程

编写代码不需要运行 Unity，实际运行游戏需要 Unity。打开项目后执行 `Adaptive Boss Arena/Setup/Run Full Setup`。其硬性依赖顺序为：项目配置 → 渲染管线 → 资源 → 输入动作 → 预制体 → 场景。

以下两个问题需要通过实际运行发现，单靠编译和测试不容易察觉：

- Unity 基础几何体的碰撞体不一定适合非均匀缩放。`Cylinder` 自带 **CapsuleCollider**，压扁后会退化成球状；应替换为盒碰撞体。
- `Cylinder` 的默认尺寸是直径一单位，不是半径一单位，应按 `radius * 2` 缩放。

场景、预制体、材质、输入绑定和 `.asset` 文件由代码生成，不能手写 YAML。场景对象加入 `ArenaSceneBuilder`，角色对象加入对应预制体生成器。生成器优先加载已有资源，避免重新运行时覆盖手工调参；删除资源后才会重新生成。

## 测试

编辑模式测试放在 `Assets/Tests/EditMode`，不能依赖运行模式。学习系统使用纯 C#，便于注入合成战斗事件流并断言适应行为。给 `IRandomProvider` 固定种子，使概率选择能够确定性复现。

## 学习系统规则

- 战斗事件总线即时分发，只能用于统计。Boss 对玩家的行为响应必须经过实施感知延迟的 `IPerceptionSource`；直接把行为响应绑定到战斗事件，会重新引入瞬时反应。
- 适应只改变数值，不改变规则。`BossTuningParameter` 通过平滑过渡调整，不能直接跳变；Boss 不能获得此前没有的能力。
- 每个 `CounterStrategy` 必须有 `TellMessage`。玩家无法察觉的适应会被理解为作弊。
- 每个行为特征都带有根据样本数计算的置信度。使用特征值之前必须检查置信度。
