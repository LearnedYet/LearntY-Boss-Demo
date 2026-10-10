# 暗黑风格表现提升实施计划

**目标：** 用 Mixamo 角色和动画、有重量感的移动与打击、动画驱动的击倒、暗黑特效、哥特式环境和统一 HUD 替换原型表现。

**架构：** 授权美术通过代码管理的 `AssetPostprocessor` 导入；角色美术表 `CharacterArtSet` 为各角色选择骨架、动画和缩放。授权资源目录不存在时回退到 CC0 骨架。Animator Controller 根据表格生成。手感改造叠加在现有 `CharacterAnimationBridge`、Motor 和时间服务上，玩法命中依据仍保留在攻击判定时间轴中。

**技术栈：** Unity 6000.3.20f1、URP 17、C# 9、NUnit EditMode / PlayMode、批处理验证 `scratchpad/verify.sh`、Windows `-perfShot` 截图，以及 Python / Pillow 图像证据。

**设计说明：** `Docs/specs/2026-09-15-grim-dark-quality-design.md`

**计划细化说明：** 阶段 1 已细化到步骤。阶段 2–5 暂时只列任务，在各阶段开始时再细化，因为动画名、帧号和接入点依赖阶段 1 的资源清单和动画姿态拼图。

本计划包含已调整、跳过或尚未实现的任务。计划项和阶段记录不代表当前完成情况；是否实现应结合源码、配置和实际验证确认。步骤以 `- [ ]` 复选框跟踪。

## 全局约束

- Boss 不能作弊：`AdaptiveBossArena.AI` 和 `.Learning` 不引用 `.Player` 或 `Unity.InputSystem`。
- 场景、预制体和控制器由代码生成。
- 不调用 `UnityEngine.Random`，只能由 `TimeService` 写入 `Time.timeScale`。
- 命中时机由攻击判定时间轴决定，动画只负责呈现。
- Mixamo 文件放在 `Assets/_Project/Art/Licensed/Mixamo/`，由 Git 忽略；缺少该目录时使用 CC0 骨架，初始化、构建和测试仍应通过。
- 性能预算：Windows p95 ≤ 8.3 ms、Draw Call ≤ 600；WebGL Draw Call ≤ 250。
- 视觉修改以 Windows 构建截图评估，运动使用 GIF，同时结合测试。
- XML 注释解释设计原因。

## 验证命令

- 完整链：`bash scratchpad/verify.sh <tag> setup` → 编译、RunFullSetup、EditMode、PlayMode。
- 截图：构建 Windows（`BuildScript.BuildWindows`），运行 `AdaptiveBossArena.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -perfCapture 8 -perfShot <raw> -perfQuit`，然后执行 `python scratchpad/raw2png.py <raw> <png>`。
- 运动：`-perfShots <folder> -perfShotEvery 0.1`（任务 3 新增），再执行 `python scratchpad/frames2gif.py <folder> <gif>`。

---

## 阶段 1：角色和动画

### 任务 1：导入并盘点 Mixamo 资源

**文件：**

- 新建 `Assets/_Project/Scripts/Editor/Art/LicensedArtPostprocessor.cs`
- 新建 `Assets/_Project/Scripts/Editor/Art/LicensedArtInventory.cs`
- 测试 `Assets/Tests/EditMode/LicensedArtImportTests.cs`

**接口：**

- 提供 `LicensedArtPostprocessor.MixamoFolder`（`"Assets/_Project/Art/Licensed/Mixamo/"`）、`LicensedArtInventory.Available`（bool）和 `LicensedArtInventory.Write(string path)`。文本清单记录各动画的文件、Take 名、长度、循环、Avatar 有效性，以及各角色的网格、材质、内嵌纹理和已识别人形骨骼。

- [ ] 编写 EditMode 测试：两个人物导入为有效 Humanoid Avatar；每个动画文件恰有一个 Humanoid 动画，其 Avatar 从对应角色复制。骑士和共享动画来自 Paladin，Boss 动画来自 Warrok。动画为原地动画；目录缺失时通过 `Assume.That(LicensedArtInventory.Available)` 跳过。
- [ ] 运行，预期失败：原始导入可能为 Generic 且缺少 Avatar 来源。
- [ ] 实现后处理器：角色使用 `Human` + `CreateFromThisModel`，通过材质描述生成材质并在旁边提取内嵌纹理；动画使用 `Human` + `CopyFromOther` 和对应 Avatar，不导入材质。`OnPreprocessAnimation` 将根运动烘焙到姿态，对待机、走、跑、横移和防守待机启用循环；`GetVersion()` = 1。
- [ ] 运行测试并确认通过，写出并阅读资源清单。
- [ ] 提交。

### 任务 2：通过动画姿态拼图选择动画

**文件：**

- 新建 `Assets/_Project/Scripts/Editor/Art/ClipContactSheet.cs`，只在支持 GPU 的批处理模式使用
- 新建 `scratchpad/contact_sheet.py`，不提交

**接口：**

- `ClipContactSheet.Render()` 使用 `AnimationClip.SampleAnimation`，在每个动画的 0%、25%、50%、75%、100% 位置摆出姿态，再用临时镜头输出 256×256 原始 RGBA 文件，命名为 `<clip>_<n>.raw`，保存在 scratchpad。

- [ ] 实现；当 `SystemInfo.graphicsDeviceType == Null` 时退出码为 2。
- [ ] 在 GPU 批处理编辑器中运行，每个目录拼成一张带标签 PNG 并查看。
- [ ] 为两名角色的每个 `CharacterAction` 选择动画，在任务 4 的表中用注释记录选择和原因。
- [ ] 提交工具。

### 任务 3：从运行构建采集运动

**文件：**

- 修改 `Assets/_Project/Scripts/Utilities/Statistics/PerfCaptureRequest.cs`，从 `-perfShots`、`-perfShotEvery` 读取 `ShotFolder`、`ShotEverySeconds`
- 修改 `Assets/_Project/Scripts/Managers/PerfProbe.cs`，采样期间写出编号原始帧
- 测试 `Assets/Tests/EditMode/PerfCaptureRequestTests.cs`

- [ ] 测试两个参数解析及缺省情况；先失败，再实现并通过。
- [ ] 添加 `scratchpad/frames2gif.py`，使用 Pillow、10 fps、640 px 宽度。
- [ ] 提交。

### 任务 4：角色美术表和回退

**文件：**

- 新建 `Assets/_Project/Scripts/Combat/Feel/CharacterArtSet.cs`，ScriptableObject，文件名与类名一致
- 修改 `Assets/_Project/Scripts/Editor/DefaultAssetGenerator.cs`，生成 `KnightArt` 和 `BruteArt`；有 Mixamo 时使用它，否则使用 Quaternius
- 修改 `Assets/_Project/Scripts/Editor/Art/AnimatorControllerBuilder.cs`，按美术表为每套资源生成一个控制器
- 修改 `Assets/_Project/Scripts/Editor/AssetIntegrityValidator.cs`，添加 `CharacterArtSet` 规则
- 测试 `Assets/Tests/EditMode/CharacterArtSetTests.cs`，同时保持 `RigFacingTests` 通过

**接口：**

- `CharacterArtSet` 包含 `RigPrefab`、`RigScale`、`AnimatorController`、`ClipFor(CharacterAction)`；动作枚举为 `enum CharacterAction { Idle, Walk, Run, TurnLeft, TurnRight, Light1, Light2, Light3, Heavy, Special, Block, BlockIdle, HitFront, HitBack, Stun, FallBack, FallForward, GetUp, Death, Roll }`。

- [ ] 测试两套资源的每个动作都能获得动画；授权目录缺失时都指向 Quaternius 骨架；骑士动画来自 Knight 目录。
- [ ] 根据任务 2 的选择实现。
- [ ] 初始化并截图，确认双方已使用新角色并面向彼此。
- [ ] 提交。

### 任务 5：材质、武器和比例

**文件：**

- 修改 `Assets/_Project/Scripts/Editor/SilhouetteBuilder.cs`，根据提取的漫反射、法线和高光贴图生成 URP Lit 材质；授权骨架不覆盖身体
- 修改 `Assets/_Project/Scripts/Editor/PlayerPrefabBuilder.cs` 和 `BossPrefabBuilder.cs`；骨架自带武器时不生成额外刀刃；Boss 持与骨架比例相符的大砍刀
- 测试 `Assets/Tests/PlayMode/RiggedCharacterTests.cs`，检查渲染器使用贴图材质、武器插槽位于右手下

- [ ] 测试、实现、截图、提交。

### 任务 6：让攻击时序匹配新动画

**文件：**

- 修改 `Assets/_Project/Scripts/Combat/Feel/CharacterAnimationConfig.cs`，为各绑定配置接触比例
- 修改 `Assets/_Project/Scripts/Editor/DefaultAssetGenerator.cs`，从动画姿态拼图读取接触比例
- 测试 `Assets/Tests/EditMode/AttackClipTimeWarpTests.cs`，检查每个绑定的接触发生在 Active 阶段

- [ ] 测试、实现、为每个攻击制作 GIF、提交。

## 阶段 2：重量感和移动

2026-09-16 状态：任务 7、8 完成；任务 9 采用替代方案；任务 10 有意跳过。

- **任务 9 的实际方案：** 资源包只有原地动画，没有值得接入的启停和转身动画，因此移动混合改为方向混合，包括横移和后退；通过支撑脚测量的位移确定各动画的位置。此过程也发现了巨兽使用倒退动画奔跑的问题。未实现脚部 IK。
- **任务 10 跳过原因：** Boss 较慢的转向速度是绕背难度的主要设计参数，再降低动画速度会与攻击时间映射冲突，因此有意保留现状。
- **任务 7：方向受击叠加层。** 上半身叠加层使用 HitFront / HitBack；两个控制器的伤害流程调用 `CharacterAnimationBridge.PlayHit(Vector3 worldDirection, float weight)`。PlayMode 检查命中后三帧内层权重超过 0.5，基础状态不变。
- **任务 8：更重的打击。** 纯逻辑 `ImpactWeight.HitStopSeconds(damage, poiseDamage, cap)`、`CameraShaker.PunchDirection(Vector3, float)`；架势破坏时通过 `ITimeService` 施加 0.35 倍速度、持续 0.4 秒的慢动作；纯规则使用 EditMode 测试。
- **任务 9 的目标方案：脚踏实地的移动。** 使用启停动画，期望方向偏差超过 120° 时播放 180° 转身，速度低于 0.1 时使用 `Combat/Feel/FootPlanting.cs` 脚部 IK。PlayMode 检查待机一秒内脚骨移动小于 2 cm。当前阶段记录采用了上文所述的方向混合替代方案。
- **任务 10 的目标方案：Boss 节奏。** 通过 `BossConfig` 调整更沉重的转向和制动，动画速度设为 0.85，用接近和挥击 GIF 评估。此任务在阶段记录中被跳过。

## 阶段 3：击倒和布娃娃

2026-09-16 状态：任务 12 完成；现有资源无法支持任务 11。

- **任务 11 阻塞原因：** 两个资源包都没有向前倒地动画，无法让背后受击角色趴倒。需要额外获取有适当授权的 Mixamo 资源。
- **任务 11：动画驱动倒地和起身。** 按受击方向相对朝向选择 FallBack / FallForward，使用 Getting Up 起身；PlayMode 检查背后受击播放 FallForward。
- **任务 12：从姿态和速度进入布娃娃。** `RagdollActivator` 用最近两个动画姿态计算各刚体速度，再启用物理；PlayMode 检查激活时髋部速度与动画速度偏差不超过 30%。

## 阶段 4：特效

2026-09-16 状态：任务 13、14、15 完成；任务 16 部分完成，巨兽胸腔有低沉余烬效果，未实现热浪扭曲。额外完成渐隐地面血迹、脚步浮尘，以及让后处理实际生效。

- **任务 13：** 打击特效，包括暗色血液、火花、石尘和余烬。
- **任务 14：** 带纹理、逐渐淡出的刀刃拖尾。
- **任务 15：** 砸地裂缝贴花和微弱裂纹预警。
- **任务 16：** Boss 内部发光，Windows 热浪效果。

每项涉及逻辑时使用纯规则；PlayMode 测试生成和对象池行为；提供截图或 GIF 并提交。

## 阶段 5：环境和 HUD

2026-09-16 状态：任务 17、18、19 完成，包括尖拱窗头、旗帜、地面污渍和污垢、低饱和调色、铁框 HUD、量表上方 Boss 名称，以及重新设计的标题和结果界面。未实现肋拱和烛台；拱门高于常见镜头范围，只在抬头时明显可见。

- **任务 17：** 哥特式尖拱和肋拱替换方形门楣。
- **任务 18：** 杂物、旗帜、烛台、污垢和积水贴花，以及更暗、更暖的调色。
- **任务 19：** 暗黑幻想 HUD、Boss 名牌和失败界面。

每项由代码生成，提供截图，Draw Call 控制在预算内，并提交。

## 阶段收尾

- **任务 20：** `verify.sh final setup` 全部通过；构建只包含网页档的 WebGL 版本，在浏览器验证、部署并确认 Pages 可访问；制作 Windows 发布压缩包，采集 60 秒性能数据并记录一行性能日志，创建 GitHub Release 并通知。
