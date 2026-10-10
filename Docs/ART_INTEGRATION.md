# 美术资源接入指南

本文说明如何通过配置接入角色、武器、技能、环境和音效资源。生成器能够提供基础角色、刀剑、竞技场和程序化天空作为回退；实际外观取决于当前配置和可用资源。

> **资源许可。** 只使用有权使用和分发的美术资源。商业游戏、电影或动漫中的可识别角色、商标武器等不能因为能下载到就直接使用。下文列出可供接入的资源来源；下载、使用和分发前仍需遵守每项资源的许可。

项目预留五个独立接入点。它们采用增量配置并允许空值：没有指定资源时保持原有行为；环境、角色、武器、技能和单个音效可以分别替换，无需修改代码。

> 美术只负责表现。不能向 Boss 提供额外信息，也不能改变战斗时序。攻击判定由时间轴驱动；新武器可以改变挥击的外观，但不能擅自改变命中的时机。

---

## 接入点概览

| 资源类型 | 接入位置 | 配置行为 |
|----------|----------|------------------|
| **角色** | `CharacterAnimationConfig.RigPrefab`，以及模型上的 Animator Controller | 留空时生成基础角色 |
| **武器** | `WeaponDefinition.ModelPrefab` | 生成器提供刀剑模型，可通过配置替换 |
| **技能** | `AttackDefinition.CastVfxPrefab` | 留空时不生成施放特效 |
| **背景** | `ArenaConfig.EnvironmentPrefab` | 留空时生成基础竞技场 |
| **音效** | `AudioService` ▸ Cue Overrides | 留空时使用合成音效库 |

指定资源后，重新执行 **`Adaptive Boss Arena/Setup/Run Full Setup`**，让预制体和场景按配置生成。

> **通过配置资源指定美术，不要直接修改预制体。**
> `PlayerPrefabBuilder` 和 `BossPrefabBuilder` 使用 `SaveAsPrefabAsset`，会替换整个预制体。手工拖进 `Player.prefab` 的模型会在下一次初始化时丢失；`RigPrefab` 字段让生成器每次都能恢复你的模型。武器、技能和环境接入点位于 `.asset` 配置中，生成器会保留这些资源，可直接指定。音效也应通过其配置接入。

---

## 暗黑幻想资源来源

资源方向：免费现成资源包、骨骼动画、暗黑幻想风格。

### 角色：[Mixamo](https://www.mixamo.com)（免费，需要 Adobe 账号）

- **玩家**：轻巧、灵活的人形角色，例如 *Paladin J Nordstrom* 或 *Knight*，可调整成带兜帽剑士的轮廓。下载 **FBX for Unity**，选择 **with skin**。
- **Boss**：体型更大、更重，例如 *Mutant*、*Warrok* 或大型重甲骑士，与玩家形成明显体量差。
- **动画集**：逐个选择 **In Place**、FBX for Unity 下载，再重定向到两个骨架。包括 `Idle`、`Walking` / `Running`（移动混合）、`Standing Melee Attack Downward` 和 `Slash` / `Great Sword Slash`（轻重攻击）、`Cast` 或 `Magic Attack`（技能）、`Dodge` / `Roll`（冲刺）、`Blocking` / `Bracing`（防守）、`Standing React`（受击）、`Dying` / `Death From Front`（死亡）。它们对应下文 Animator 触发器。

### 背景：CC0 环境和天空盒

- 环境：来自 **[Poly Pizza](https://poly.pizza)**、**[Kenney](https://kenney.nl/assets)** 或 **[Quaternius](https://quaternius.com)** 的 CC0 废弃大教堂、地下墓室或破败庭院。围绕半径约 **16 单位**的圆形战斗区域布置，对应 `ArenaConfig.Radius`。
- 天空盒：来自 **[Poly Haven](https://polyhaven.com/hdris)** 的 CC0 风暴或夜间 HDRI，用来替换生成的 `ArenaSky.mat`，见接入点 4。

### 音效：CC0 音频

- 从 **[Freesound](https://freesound.org)**（筛选 CC0）或 **[Kenney](https://kenney.nl/assets)** 选择打击、挥击和背景音频。逐个替换音效提示，见接入点 5。

### 武器：CC0 武器包

- 从 Kenney 或 Quaternius 选择 CC0 大剑、单手剑和发光 / 符文刀刃，分别对应 `WeaponGreatsword`、`WeaponBlade` 和 `WeaponEnergyBlade`。

---

## 接入点 1：角色骨骼动画

**代码：** `Combat/Feel/CharacterAnimationBridge.cs`、`CharacterAnimatorParameters.cs` 和 `Editor/Art/AnimatorControllerBuilder.cs`。桥接组件和 `CharacterAnimator` 位于视觉根节点，接收同样的状态流。没有启用且配置有效控制器的 Animator 时，桥接不驱动骨骼；存在有效 Animator 时，通过状态切换和浮点参数驱动动画，程序姿态组件避免覆盖骨骼表现。

### Animator 参数契约

生成的 Animator Controller 与运行时桥接共享以下浮点参数：

| 参数 | 类型 | 含义 |
|------|------|------|
| `Speed` | float | 平面速度占最高速度的比例（0–1），用于移动混合树 |
| `MoveX` | float | 按速度缩放的局部横向移动，右侧为正 |
| `MoveZ` | float | 按速度缩放的局部纵向移动，前方为正 |
| `AttackTime` | float | 攻击或咆哮动画的归一化时间；攻击时由时间轴映射 |
| `ReactionTime` | float | 倒地及起身动画的归一化进度，倒地时保持为零 |

动作通过 `CrossFadeInFixedTime` 直接切换到约定的状态，例如 `Locomotion`、`Roll`、`Guard`、`Parry`、`Stagger` 和 `Death`。攻击状态来自动画配置；连续播放同名攻击时切换到带 ` B` 后缀的副本。受击反馈使用 `Hits` 叠加层中的 `Hit` 状态。状态和参数名称必须与 `CharacterAnimatorParameters`、动画配置及生成器一致；不能只配置触发器就认为已完成接入。

### 步骤

1. 导入 Mixamo FBX，设置 **Rig ▸ Animation Type = Humanoid**、**Avatar = Create From This Model**。
2. 使用 `AnimatorControllerBuilder` 根据动画表生成 **Animator Controller**，或创建满足上述参数、状态和叠加层契约的控制器。移动由 `Speed`、`MoveX` 和 `MoveZ` 驱动，攻击及反应进度由对应时间参数驱动。
3. 将绑定骨架、已经设置 `Animator.Controller` 的模型保存为独立预制体。
4. 将其指定到 `DefaultPlayerAnimation.asset` 或 `DefaultBossAnimation.asset` 的 **Rig Prefab** 字段。
5. 执行 **Run Full Setup**。生成器在 `Visual` 下实例化骨架并跳过生成的基础身体；以后重新运行也会保留此接入方式。
6. `CharacterAnimationBridge` 在 Awake 中查找子节点 Animator；运行时传入动作状态后，桥接驱动对应动画，并与程序姿态组件协调，避免竞争姿态。

> 不要直接把模型加入 `Player.prefab`，生成器每次都会替换整个预制体。模型应通过 **Rig Prefab** 接入。角色骨架上的碰撞体会自动剥离，避免与 CharacterController 冲突。

---

## 接入点 2：手持武器模型

**代码：** `Combat/Feel/WeaponSocket.cs`，各视觉根节点下已存在 `WeaponSocket` 对象；配置字段为 `WeaponDefinition.ModelPrefab`。每次装备和换武器时，`PlayerController.EquipWeapon` 调用 `socket.Equip(weapon.ModelPrefab)`：空值清除模型，预制体则实例化到手中。

### 步骤

1. 将武器模型保存为预制体，轴心位于握柄，刀刃朝向 +Z。
2. 指定到以下武器资源的 **Model Prefab** 字段：
   - `Assets/_Project/ScriptableObjects/Weapons/WeaponGreatsword.asset`
   - `Assets/_Project/ScriptableObjects/Weapons/WeaponBlade.asset`
   - `Assets/_Project/ScriptableObjects/Weapons/WeaponEnergyBlade.asset`
3. 运行游戏，装备和切换时应显示对应模型。
4. 接入骨架后，将 `WeaponSocket` 的 Transform 放到右手骨骼下并清除局部偏移。控制器只要求插槽切换武器，无需改变代码。

> Boss 的武器包含在导入模型中，Boss 不换武器，因此没有需要配置的 Boss 武器插槽。

---

## 接入点 3：技能施放特效

**代码：** `AttackDefinition.CastVfxPrefab`。`Combat/AttackExecutor.Begin` 在攻击者位置生成特效，并设置父节点使其跟随突进；超过安全生存时间后自动销毁。玩家和 Boss 共用攻击执行器，因此共用此接入方式。

### 步骤

1. 为技能创建粒子系统预制体，例如符文闪光、冲击波或余烬爆发。特效应自行停止并清理；执行器的 4 秒销毁只是兜底。
2. 指定到攻击资源的 **Cast Vfx Prefab** 字段，例如：
   - `Assets/_Project/ScriptableObjects/Attacks/PlayerSpecial.asset`
   - `Assets/_Project/ScriptableObjects/Attacks/EnergySpecial.asset`
   - `Assets/_Project/ScriptableObjects/Attacks/GreatswordSpecial.asset`
   - Boss 技能：`BossShockwave.asset`、`BossSlam.asset`、`BossCharge.asset`
3. 运行游戏，对应攻击开始时应立即生成特效。

---

## 接入点 4：环境背景

**代码：** `ArenaConfig.EnvironmentPrefab` 和 `ArenaConfig.HideProceduralMeshesWithEnvironment`，由 `Editor/ArenaSceneBuilder.BuildEnvironment` 在竞技场根节点下实例化。程序生成的地板和墙面碰撞体继续保留，用来约束战斗范围；指定环境时只隐藏原网格。对齐时可以暂时关闭隐藏。

### 步骤

1. 将环境组装成一个以原点为中心的预制体，可行走地面匹配 `ArenaConfig.Radius`（约 16）。只包含渲染器，实际边界仍由原有碰撞体定义。
2. 指定到 `Assets/_Project/ScriptableObjects/Config/DefaultArenaConfig.asset` 的 **Environment Prefab** 字段。启用 **Hide Procedural Meshes With Environment** 替换占位外观；临时关闭可检查地面与碰撞圆盘是否对齐。
3. 执行 **Run Full Setup**，重建带有环境资源的场景。
4. 替换天空时，将 HDRI 指定到天空盒材质，再设置 `RenderSettings.skybox`。生成器也会设置该字段，最简单的方法是替换生成的 `ArenaSky.mat`。镜头已经使用天空盒清屏。环境光继续使用 `ArenaAtmosphere` 按 Boss 阶段驱动的三段 Trilight，以保留阶段氛围；只有愿意放弃该效果时，才将 `RenderSettings.ambientMode` 改为 `Skybox`。

---

## 接入点 5：录制音效

**代码：** `Managers/AudioService.cs` 的 **Cue Overrides**。音效库支持由 `Utilities/Audio/ToneGenerator.cs` 合成提示音，也支持录制音频覆盖。每个覆盖项只替换一个提示音，无需改动调用方；实际使用的音频以资源配置为准。

### 步骤

1. 导入音频。短促单次音效使用 **Decompress On Load**，音乐使用 **Streaming**。
2. 在竞技场的 `--- Managers ---` 对象上找到 **Audio Service ▸ Cue Overrides**，添加条目，将 **Cue Id** 设置为 `AudioService.Cues` 中的标识符，例如 `hit.heavy`、`guard.deflect`、`boss.roar`、`player.death`，并指定音频。
3. 运行游戏。不匹配任何提示音的 ID 会输出警告，避免拼写错误导致音效悄无声息地失效。

混音配置与音频文件分开。`AudioService.CueGains` 决定各提示音的相对音量；替换录音过响或过轻时，在这里调整即可，无需重新导出文件。

---

## 接入之后

- 执行批处理链：编译 → **Run Full Setup** → EditMode 测试 → **PlayMode 测试**。与美术无关的测试应继续通过；依赖边界和资源完整性校验确认没有越界；运行模式测试确认接入真实美术后场景仍能正确组装。
- 在运行模式下检查：攻击仍在原有时机命中，Boss 仍会适应，未配置的接入点仍保持原有行为。
- 注意下载资源自带的碰撞体。角色骨架会自动剥离碰撞体，但环境和武器预制体不会自动剥离。竞技场地板或角色控制器内部的多余碰撞体可能改变战斗。测试可以保证生成内容的规则，不能替你保证新资源的正确性。
