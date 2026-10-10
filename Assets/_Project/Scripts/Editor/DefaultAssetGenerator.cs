using AdaptiveBossArena.AI;
using AdaptiveBossArena.Combat;
using AdaptiveBossArena.Combat.Feel;
using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Core.Events;
using AdaptiveBossArena.Core.Perception;
using AdaptiveBossArena.Game;
using AdaptiveBossArena.Learning;
using AdaptiveBossArena.Player;
using UnityEditor;
using UnityEngine;

namespace AdaptiveBossArena.Editor
{
    /// <summary>
    /// 生成项目默认配置资产，并连接它们的引用。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 多数数值来自配置类型自身的初始化器，因此生成器主要处理初始化器无法表达的内容：
    /// 每个攻击资产的差异，以及连接资产之间的引用。
    /// </para>
    /// <para>
    /// 已有资产会加载而非替换，因此手动调参后再运行不会丢失修改。删除资产才会按默认值重新生成。
    /// </para>
    /// <para>
    /// 这种保护也有代价：修改本文件数值不会影响已有资产，所以此处调参可能悄然无效。
    /// “Retune Generated Assets”正用于此情况，它把调参数值写入磁盘上的已有资产而非重建；
    /// 重建会生成新 GUID，让所有引用悬空。本项目曾因此经历首领悄然停止攻击的排查。
    /// </para>
    /// </remarks>
    public static class DefaultAssetGenerator
    {
        private const string ConfigFolder = EditorMenus.GeneratedAssetFolder + "/Config";
        private const string AttackFolder = EditorMenus.GeneratedAssetFolder + "/Attacks";
        private const string StrategyFolder = EditorMenus.GeneratedAssetFolder + "/Strategies";
        private const string EventFolder = EditorMenus.GeneratedAssetFolder + "/Events";
        private const string WeaponFolder = EditorMenus.GeneratedAssetFolder + "/Weapons";

        /// <summary>
        /// 为 true 时，即使资产已经存在也写入调参数值。
        /// </summary>
        /// <remarks>
        /// 使用字段而非参数，因为它需要传达给十多个没有共同调用路径的私有辅助方法。
        /// 在同一次同步调用中设置并清除，不会暴露不一致状态。
        /// </remarks>
        private static bool _rewriteTuning;

        /// <summary>根据资产创建状态判断是否应写入调参数值。</summary>
        private static bool ShouldWriteTuning(bool created) => created || _rewriteTuning;

        /// <summary>
        /// 用本文件中的数值重写所有生成资产的调参内容。
        /// </summary>
        /// <remarks>
        /// 刻意设为独立、明确选择的操作，不属于正常流程。它会丢弃 Inspector 中的调整，
        /// 绝不能作为重建项目的副作用发生。
        /// </remarks>
        [MenuItem(EditorMenus.Setup + "Retune Generated Assets", priority = EditorMenus.SetupPriorityGenerateAssets + 2)]
        public static void RetuneGeneratedAssets()
        {
            _rewriteTuning = true;

            try
            {
                GenerateDefaultAssets();
            }
            finally
            {
                _rewriteTuning = false;
            }

            Debug.Log("[Adaptive Boss Arena] Generated assets retuned from code defaults.");
        }

        /// <summary>创建所有尚不存在的默认配置资产。</summary>
        [MenuItem(EditorMenus.Setup + "2. Generate Default Assets", priority = EditorMenus.SetupPriorityGenerateAssets)]
        public static void GenerateDefaultAssets()
        {
            // 刻意不使用 StartAssetEditing/S topAssetEditing 包裹。数百资产时批量导入有价值，
            // 但也会暂停导入，使块内新资产直到结束后才能加载。此生成器只有数十个互相引用的资产，
            // 正确性比节省的几毫秒重要得多。
            try
            {
                AssetAuthoring.EnsureFolderExists(ConfigFolder);
                AssetAuthoring.EnsureFolderExists(AttackFolder);
                AssetAuthoring.EnsureFolderExists(StrategyFolder);
                AssetAuthoring.EnsureFolderExists(EventFolder);
                AssetAuthoring.EnsureFolderExists(WeaponFolder);

                ReactionProfile reactionProfile = CreateReactionProfile();
                PlayerAttacks playerAttacks = CreatePlayerAttacks();
                BossAttacks bossAttacks = CreateBossAttacks();
                CounterStrategy[] strategies = CreateCounterStrategies();

                WeaponDefinition[] weapons = CreateWeapons(playerAttacks);

                CreatePlayerConfig(playerAttacks, weapons);
                CreateBossConfig(reactionProfile, bossAttacks, strategies);
                CreateAnimationConfigs();
                CreateArenaConfig();
                CreateEventChannels();
            }
            finally
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[Adaptive Boss Arena] Default assets generated under '{EditorMenus.GeneratedAssetFolder}'.");
        }

        /// <summary>玩家攻击集，分组以连接到玩家配置。</summary>
        private readonly struct PlayerAttacks
        {
            public PlayerAttacks(AttackDefinition[] lightChain, AttackDefinition heavy, AttackDefinition special)
            {
                LightChain = lightChain;
                Heavy = heavy;
                Special = special;
            }

            public AttackDefinition[] LightChain { get; }

            public AttackDefinition Heavy { get; }

            public AttackDefinition Special { get; }
        }

        /// <summary>首领攻击集，按各攻击开放的阶段分组。</summary>
        private readonly struct BossAttacks
        {
            public BossAttacks(
                AttackDefinition sweep,
                AttackDefinition slam,
                AttackDefinition charge,
                AttackDefinition shockwave,
                AttackDefinition jab,
                AttackDefinition perilous,
                AttackDefinition phaseShockwave,
                AttackDefinition delayedOverhead,
                AttackDefinition spin,
                AttackDefinition kick,
                AttackDefinition grab,
                AttackDefinition leap)
            {
                Sweep = sweep;
                Slam = slam;
                Charge = charge;
                Shockwave = shockwave;
                Jab = jab;
                Perilous = perilous;
                PhaseShockwave = phaseShockwave;
                DelayedOverhead = delayedOverhead;
                Spin = spin;
                Kick = kick;
                Grab = grab;
                Leap = leap;
            }

            public AttackDefinition Sweep { get; }

            public AttackDefinition Slam { get; }

            public AttackDefinition Charge { get; }

            public AttackDefinition Shockwave { get; }

            public AttackDefinition Jab { get; }

            /// <summary>无法格挡的危险下劈，必须闪避，不能格挡。</summary>
            public AttackDefinition Perilous { get; }

            /// <summary>用于阶段转换和决心豪赌的全场不可格挡冲击波。</summary>
            public AttackDefinition PhaseShockwave { get; }

            /// <summary>惩罚条件反射闪避的蓄停下劈。</summary>
            public AttackDefinition DelayedOverhead { get; }

            /// <summary>同时覆盖所有方向的旋转斩。</summary>
            public AttackDefinition Spin { get; }

            /// <summary>打破持续格挡的蹬踢。</summary>
            public AttackDefinition Kick { get; }

            /// <summary>抓取：既不能格挡也不能招架，只能靠距离应对。</summary>
            public AttackDefinition Grab { get; }

            /// <summary>跨越竞技场并在落点留下伤痕的跃击。</summary>
            public AttackDefinition Leap { get; }
        }

        private static ReactionProfile CreateReactionProfile()
        {
            var profile = AssetAuthoring.CreateOrLoad<ReactionProfile>(
                $"{ConfigFolder}/DefaultReactionProfile.asset", out bool created);

            if (created)
            {
                using (AssetAuthoring.AssetWriter writer = AssetAuthoring.Edit(profile))
                {
                    writer.String("_id", "reaction.default")
                        .String("_designerNotes",
                            "Baseline human-plausible reaction limits. Later phases tighten these " +
                            "toward the fast end but must never go below it.");
                }
            }

            return profile;
        }

        /// <summary>
        /// 创建双方各自的程序动画配置。
        /// </summary>
        /// <remarks>
        /// 使用两个资产而非共享一个，因为手感必须不同：玩家轻快敏捷，首领沉重坚定。
        /// 玩家资产完全采用类型字段默认值；首领覆盖少量幅度值来增加质量感。两者仅影响幅度，
        /// 都不能改变命中框开放时机。
        /// </remarks>
        private static void CreateAnimationConfigs()
        {
            CreateAnimationConfig(
                "DefaultPlayerAnimation", "animation.player", isBoss: false);
            CreateAnimationConfig(
                "DefaultBossAnimation", "animation.boss", isBoss: true);

            AssignRigs();
            AssignStrikers();
        }

        /// <summary>玩家各攻击播放的攻击状态。</summary>
        /// <remarks>
        /// 三段连击依次使用动作库三段剑术组合，使连击表现为一串连续打击而非同一挥击重复三次。
        /// 重攻击和特殊攻击按伤害类型回退，无需条目。
        /// </remarks>
        private static readonly (string Attack, string State)[] PlayerClipBindings =
        {
            ("PlayerLight1", "Light1"), ("PlayerLight2", "Light2"), ("PlayerLight3", "Light3"),
            ("GreatswordLight1", "Light1"), ("GreatswordLight2", "Light3"),
            ("EnergyLight1", "Light1"), ("EnergyLight2", "Light2"), ("EnergyLight3", "Light3"), ("EnergyLight4", "Light1"),
            ("EnergyHeavy", "Dash"),
            // 处决是双手驱动的刺击，不是又一次掠过目标的斩击。
            ("PlayerExecution", "Riposte")
        };

        /// <summary>首领各攻击播放的攻击状态。</summary>
        /// <remarks>
        /// 使攻击能从身体动作读取，除砸地和冲击波外都以此作为预警：横扫大幅挥动、刺探短促勾斩、
        /// 冲锋猛烈突进、砸地将武器落下、两种冲击波蓄势后向外释放，让地面警告和身体动作表达相同信息。
        /// </remarks>
        private static readonly (string Attack, string State)[] BossClipBindings =
        {
            ("BossSweep", "Light2"), ("BossSlam", "Overhead"), ("BossCharge", "Dash"),
            ("BossShockwave", "Cast"), ("BossJab", "Hook"), ("BossPerilousOverhead", "Heavy"),
            ("BossPhaseShockwave", "Cast"),

            // 蓄停下劈与危险下劈采用相同高举刀刃姿态，通过时间扭曲拉长前摇。这正是辨识重点：姿势熟悉，
            // 时机不同。
            ("BossDelayedOverhead", "Heavy"), ("BossSpinCleave", "Spin"), ("BossKick", "Kick"),
            ("BossGrab", "Grab"), ("BossLeapSmash", "Leap")
        };

        /// <summary>各攻击使用的身体部位。</summary>
        /// <remarks>
        /// <para>
        /// 斩击按刀刃判定，踢击按脚，抓取按双手，跃击按身体落地。未列出的攻击刻意继续使用设定体积：
        /// 砸地和冲击波是范围攻击，区域已画在地面；特殊攻击是围绕骑士的旋风。
        /// </para>
        /// <para>
        /// 冲锋同时带动刀刃与身体，任一都能击中骑士。狂暴时巨兽没有刀刃，所有武器攻击改由拳头命中。
        /// </para>
        /// </remarks>
        private static readonly (string Attack, StrikerParts Parts)[] StrikerBindings =
        {
            ("PlayerLight1", StrikerParts.Weapon), ("PlayerLight2", StrikerParts.Weapon),
            ("PlayerLight3", StrikerParts.Weapon), ("PlayerHeavy", StrikerParts.Weapon),
            ("PlayerExecution", StrikerParts.Weapon),
            ("GreatswordLight1", StrikerParts.Weapon), ("GreatswordLight2", StrikerParts.Weapon),
            ("GreatswordHeavy", StrikerParts.Weapon),
            ("EnergyLight1", StrikerParts.Weapon), ("EnergyLight2", StrikerParts.Weapon),
            ("EnergyLight3", StrikerParts.Weapon), ("EnergyLight4", StrikerParts.Weapon),
            ("EnergyHeavy", StrikerParts.Weapon),

            ("BossSweep", StrikerParts.Weapon), ("BossJab", StrikerParts.Weapon),
            ("BossPerilousOverhead", StrikerParts.Weapon), ("BossDelayedOverhead", StrikerParts.Weapon),
            ("BossSpinCleave", StrikerParts.Weapon),
            ("BossCharge", StrikerParts.Weapon | StrikerParts.Body),
            ("BossKick", StrikerParts.RightFoot),
            ("BossGrab", StrikerParts.RightHand | StrikerParts.LeftHand),
            ("BossLeapSmash", StrikerParts.Body)
        };

        /// <summary>按资产名返回攻击部位；不在表中时不指定部位。</summary>
        private static StrikerParts StrikersFor(string attackName)
        {
            foreach ((string name, StrikerParts parts) in StrikerBindings)
            {
                if (name == attackName)
                {
                    return parts;
                }
            }

            return StrikerParts.None;
        }

        /// <summary>
        /// 写入各攻击使用的部位。
        /// </summary>
        /// <remarks>
        /// 属于结构，因此像片段绑定一样每次都写入，不仅在新建资产时写。
        /// 若表中已指定部位而攻击仍使用体积判定，就会悄然继续造成远距离伤害。
        /// </remarks>
        private static void AssignStrikers()
        {
            foreach ((string attackName, StrikerParts parts) in StrikerBindings)
            {
                var attack = AssetDatabase.LoadAssetAtPath<AttackDefinition>($"{AttackFolder}/{attackName}.asset");

                if (attack == null)
                {
                    continue;
                }

                using (AssetAuthoring.AssetWriter writer = AssetAuthoring.Edit(attack))
                {
                    writer.Int("_strikers", (int)parts);
                }
            }
        }

        /// <summary>
        /// 为双方角色配置导入骨架、控制器、缩放和片段绑定。
        /// </summary>
        /// <remarks>
        /// 这些是引用而非调参，因此每次都写入：缺少引用是应自动修复的资产错误。
        /// 没有导入美术时字段保持空，基础身体仍按以前方式生成。
        /// </remarks>
        private static void AssignRigs()
        {
            // 工作副本有许可资源时使用 Mixamo 骑士和巨兽，
            // 否则双方使用公开仓库克隆总会具备的 CC0 人偶。巨兽回退缩放让同一骨架看起来高出一个半头。
            bool licensed = Art.LicensedArtPostprocessor.Available;

            Art.CharacterClipTable knight = licensed ? Art.CharacterClipTable.Knight : Art.CharacterClipTable.Fallback("KnightController", 1f);
            Art.CharacterClipTable brute = licensed ? Art.CharacterClipTable.Brute : Art.CharacterClipTable.Fallback("BruteController", 1.55f);

            AssignRig("DefaultPlayerAnimation", knight, PlayerClipBindings);

            // 仅巨兽有第二形态：同一骨架，在最后阶段徒手战斗。
            AssignRig(
                "DefaultBossAnimation", brute, BossClipBindings,
                licensed ? Art.CharacterClipTable.BruteFrenzy : null);
        }

        private static void AssignRig(
            string configName,
            Art.CharacterClipTable table,
            (string Attack, string State)[] bindings,
            Art.CharacterClipTable frenzyTable = null)
        {
            GameObject rig = Art.AnimatorControllerBuilder.LoadRigModel(table);
            RuntimeAnimatorController controller = rig != null ? Art.AnimatorControllerBuilder.Build(table) : null;

            RuntimeAnimatorController frenzy = rig != null && frenzyTable != null
                ? Art.AnimatorControllerBuilder.Build(frenzyTable)
                : null;
            float scale = table.RigScale;

            var config = AssetDatabase.LoadAssetAtPath<CharacterAnimationConfig>($"{ConfigFolder}/{configName}.asset");

            if (config == null)
            {
                return;
            }

            var serialized = new SerializedObject(config);
            serialized.FindProperty("_rigPrefab").objectReferenceValue = controller != null ? rig : null;
            serialized.FindProperty("_animatorController").objectReferenceValue = controller;
            serialized.FindProperty("_frenzyController").objectReferenceValue = frenzy;
            serialized.FindProperty("_rigScale").floatValue = scale;

            SerializedProperty list = serialized.FindProperty("_attackClips");
            list.arraySize = bindings.Length;

            for (int i = 0; i < bindings.Length; i++)
            {
                SerializedProperty entry = list.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("_attack").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<AttackDefinition>($"{AttackFolder}/{bindings[i].Attack}.asset");
                entry.FindPropertyRelative("_state").stringValue = bindings[i].State;
                HumanBodyBones bone = Art.ClipContactMeasure.BoneFor(StrikersFor(bindings[i].Attack));

                entry.FindPropertyRelative("_contactFraction").floatValue =
                    Art.ClipContactMeasure.ContactFor(table, bindings[i].State, bone);

                // 狂暴形态没有刀刃，武器攻击按挥出的拳头计时。
                entry.FindPropertyRelative("_frenzyContactFraction").floatValue = frenzyTable != null
                    ? Art.ClipContactMeasure.ContactFor(frenzyTable, bindings[i].State, bone)
                    : 0f;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
        }

        /// <summary>创建或重新调整单个动画配置。</summary>
        private static void CreateAnimationConfig(string assetName, string id, bool isBoss)
        {
            var config = AssetAuthoring.CreateOrLoad<CharacterAnimationConfig>(
                $"{ConfigFolder}/{assetName}.asset", out bool created);

            // 玩家全部采用类型默认值。首领仅覆盖使其更沉重的数值：更慢平滑、更深前摇、更长突进、
            // 更明显的重攻击倍率，同时受击位移更小。
            bool writeBossTuning = isBoss && ShouldWriteTuning(created);

            if (!created && !writeBossTuning)
            {
                return;
            }

            using (AssetAuthoring.AssetWriter writer = AssetAuthoring.Edit(config))
            {
                if (created)
                {
                    writer.String("_id", id);
                }

                if (writeBossTuning)
                {
                    writer.Float("_positionHalfLife", 0.09f)
                        .Float("_rotationHalfLife", 0.08f)
                        .Float("_scaleHalfLife", 0.1f)
                        .Float("_anticipationCrouch", 0.2f)
                        .Float("_anticipationLeanDegrees", 18f)
                        .Float("_strikeLunge", 0.55f)
                        .Float("_strikeLeanDegrees", 22f)
                        .Float("_heavyMultiplier", 1.7f)
                        .Float("_moveLeanDegrees", 8f)
                        .Float("_idleBobFrequency", 1f)
                        .Float("_recoilDistance", 0.22f);
                }
            }
        }

        /// <summary>玩家挥击采用冷色，使双方一眼可分。</summary>
        private static readonly Color PlayerSwingColor = new Color(0.55f, 0.85f, 1f, 0.6f);

        private static PlayerAttacks CreatePlayerAttacks()
        {
            // 递进连击：每一步投入更多、回报更高，让继续连击成为真正的决策，而不是免费的后续攻击。
            AttackDefinition light1 = CreateAttack(
                "PlayerLight1", "Slash", DamageType.Light,
                damage: 12f, startup: 0.09f, active: 0.07f, recovery: 0.20f,
                range: 2.1f, arc: 110f, poise: 12f, knockback: 2.5f, hitStop: 0.045f, trauma: 0.12f, overlayColor: PlayerSwingColor);

            AttackDefinition light2 = CreateAttack(
                "PlayerLight2", "Backslash", DamageType.Light,
                damage: 15f, startup: 0.08f, active: 0.07f, recovery: 0.22f,
                range: 2.2f, arc: 120f, poise: 14f, knockback: 2.8f, hitStop: 0.05f, trauma: 0.14f, overlayColor: PlayerSwingColor);

            AttackDefinition light3 = CreateAttack(
                "PlayerLight3", "Rising Cut", DamageType.Light,
                damage: 22f, startup: 0.12f, active: 0.09f, recovery: 0.34f,
                range: 2.4f, arc: 130f, poise: 22f, knockback: 4.5f, hitStop: 0.075f, trauma: 0.22f, overlayColor: PlayerSwingColor);

            // 长后摇是设计重点：挥空重攻击必须可被惩罚，首领的招架适应才有值得针对的内容。
            AttackDefinition heavy = CreateAttack(
                "PlayerHeavy", "Overhead", DamageType.Heavy,
                damage: 42f, startup: 0.34f, active: 0.10f, recovery: 0.52f,
                range: 2.8f, arc: 90f, poise: 45f, knockback: 6f, hitStop: 0.11f, trauma: 0.35f,
                staminaCost: 26f, lungeSpeed: 3.5f, stagger: StaggerStrength.Interrupt, overlayColor: PlayerSwingColor);

            AttackDefinition special = CreateAttack(
                "PlayerSpecial", "Whirlwind", DamageType.Special,
                damage: 30f, startup: 0.20f, active: 0.30f, recovery: 0.40f,
                range: 3.4f, arc: 360f, poise: 30f, knockback: 5f, hitStop: 0.08f, trauma: 0.3f,
                staminaCost: 30f, shape: AttackShape.Sphere, overlayColor: PlayerSwingColor);

            return new PlayerAttacks(new[] { light1, light2, light3 }, heavy, special);
        }

        private static BossAttacks CreateBossAttacks()
        {
            // 地面警告仅用于攻击地面或无法格挡的攻击：砸地、两种冲击波和危险下劈。
            // 普通挥击改由身体动画辨识，符合该类型首领的观察方式；
            // 横扫下方彩色扇形会教玩家盯地板而非首领。角度从 160降至 120 度。
            // 四米范围的 160 度扇形几乎覆盖首领正面，只能退到范围外，距离重要而方位不重要；
            // 120 度留出可以移动到的侧翼，这是战斗辨识的基础。
            AttackDefinition sweep = CreateAttack(
                "BossSweep", "Wide Sweep", DamageType.BossMelee,
                damage: 12f, startup: 0.45f, active: 0.12f, recovery: 0.45f,
                range: 4f, arc: 120f, poise: 20f, knockback: 5f, hitStop: 0.06f, trauma: 0.25f,
                telegraph: false, reaction: ImpactReaction.Knockback);

            AttackDefinition slam = CreateAttack(
                "BossSlam", "Ground Slam", DamageType.BossMelee,
                damage: 18f, startup: 0.62f, active: 0.14f, recovery: 0.70f,
                range: 3.6f, arc: 360f, poise: 40f, knockback: 8f, hitStop: 0.10f, trauma: 0.45f,
                shape: AttackShape.Sphere, stagger: StaggerStrength.Interrupt, telegraph: true,
                leavesHazard: true, hazardRadius: 3f, hazardDamagePerTick: 3f, hazardDuration: 3.5f,
                reaction: ImpactReaction.Knockdown);

            // 拉近距离的攻击。对已被首领识别为保持距离的玩家频繁采用，作为设计中应对风筝打法的方案。
            AttackDefinition charge = CreateAttack(
                "BossCharge", "Charge", DamageType.BossMelee,
                damage: 15f, startup: 0.55f, active: 0.30f, recovery: 0.55f,
                range: 2.6f, arc: 100f, poise: 35f, knockback: 9f, hitStop: 0.09f, trauma: 0.4f,
                lungeSpeed: 16f, stagger: StaggerStrength.Interrupt, telegraph: false,
                reaction: ImpactReaction.Knockdown);

            AttackDefinition shockwave = CreateAttack(
                "BossShockwave", "Shockwave", DamageType.BossProjectile,
                damage: 14f, startup: 0.70f, active: 0.45f, recovery: 0.60f,
                range: 7f, arc: 360f, poise: 25f, knockback: 6f, hitStop: 0.07f, trauma: 0.35f,
                shape: AttackShape.Sphere, telegraph: true,
                leavesHazard: true, hazardRadius: 3.5f, hazardDamagePerTick: 3f, hazardDuration: 4f,
                reaction: ImpactReaction.Knockback);

            // 快速、低成本、短前摇。单独几乎无威胁，作用是作为连击开头或结尾的快拍，
            // 让连击有节奏而不是连续两次重挥。
            AttackDefinition jab = CreateAttack(
                "BossJab", "Jab", DamageType.BossMelee,
                damage: 8f, startup: 0.28f, active: 0.08f, recovery: 0.32f,
                range: 3f, arc: 70f, poise: 12f, knockback: 3f, hitStop: 0.05f, trauma: 0.18f,
                telegraph: false);

            // 危险下劈：长前摇、明确预警、不可格挡，惩罚本能持续持盾。缓慢前摇是公平的，
            // 玩家有时间看懂红色脉冲并翻滚；高架势伤害让持盾硬吃成为严重错误。
            AttackDefinition perilous = CreateAttack(
                "BossPerilousOverhead", "Perilous Overhead", DamageType.BossMelee,
                damage: 26f, startup: 0.85f, active: 0.12f, recovery: 0.72f,
                range: 3.4f, arc: 120f, poise: 45f, knockback: 7f, hitStop: 0.12f, trauma: 0.5f,
                stagger: StaggerStrength.Interrupt, telegraph: true, unblockable: true,
                reaction: ImpactReaction.Knockdown);

            // 演出大招：全场不可格挡冲击波，带长而真实的前摇，用于阶段转换和 Resolve 豪赌。
            // 在落点留下地面伤痕，将战斗最强节拍中的招式辨识和空间压迫结合起来。
            AttackDefinition phaseShockwave = CreateAttack(
                "BossPhaseShockwave", "Cataclysmic Wave", DamageType.BossProjectile,
                damage: 22f, startup: 0.95f, active: 0.40f, recovery: 0.70f,
                range: 8.5f, arc: 360f, poise: 30f, knockback: 9f, hitStop: 0.12f, trauma: 0.6f,
                shape: AttackShape.Sphere, telegraph: true, unblockable: true,
                leavesHazard: true, hazardRadius: 4f, hazardDamagePerTick: 3f, hazardDuration: 4.5f,
                reaction: ImpactReaction.Launch);

            // 应对条件反射闪避。前摇与危险下劈相同，但在最高点多停近半秒再快速落刃。
            // 玩家见手臂抬起就翻滚，会在命中时仍处于恢复状态。它可以格挡，因此正确辨识后有两种应对。
            AttackDefinition delayedOverhead = CreateAttack(
                "BossDelayedOverhead", "Held Overhead", DamageType.BossMelee,
                damage: 22f, startup: 1.15f, active: 0.10f, recovery: 0.62f,
                range: 3.4f, arc: 110f, poise: 40f, knockback: 6f, hitStop: 0.11f, trauma: 0.45f,
                stagger: StaggerStrength.Interrupt, telegraph: true,
                reaction: ImpactReaction.Knockdown);

            // 应对绕圈。普通横扫留有侧翼，这次没有，玩家必须退出范围而非绕到旁边。长有效窗口是代价：
            // 巨兽投入旋转四分之一秒，之后暴露破绽。
            AttackDefinition spin = CreateAttack(
                "BossSpinCleave", "Spin Cleave", DamageType.BossMelee,
                damage: 14f, startup: 0.52f, active: 0.26f, recovery: 0.62f,
                range: 3.8f, arc: 360f, poise: 28f, knockback: 6f, hitStop: 0.07f, trauma: 0.32f,
                telegraph: false, reaction: ImpactReaction.Knockback);

            // 应对持续格挡。生命伤害很小，却削减一半架势：盾后等待的玩家会被打开防御而非直接击杀，
            // 这是它提供的交换。
            AttackDefinition kick = CreateAttack(
                "BossKick", "Guard Breaker", DamageType.BossMelee,
                damage: 6f, startup: 0.34f, active: 0.08f, recovery: 0.44f,
                range: 2.6f, arc: 60f, poise: 55f, knockback: 7f, hitStop: 0.06f, trauma: 0.26f,
                stagger: StaggerStrength.Interrupt, telegraph: false,
                reaction: ImpactReaction.Knockback);

            // 应对贴脸站立。不可格挡、不可招架，唯一办法是离开该位置；短范围和长后摇确保后退总能应对。
            AttackDefinition grab = CreateAttack(
                "BossGrab", "Seize", DamageType.BossMelee,
                damage: 24f, startup: 0.55f, active: 0.12f, recovery: 0.95f,
                range: 2.4f, arc: 60f, poise: 30f, knockback: 3f, hitStop: 0.14f, trauma: 0.5f,
                lungeSpeed: 7f, stagger: StaggerStrength.Interrupt, telegraph: true,
                unblockable: true, unparryable: true, reaction: ImpactReaction.Launch);

            // 应对逃跑。一跳跨越竞技场，并在落点留下地面伤痕，让距离不再等于安全，而不必让巨兽全程冲刺。
            AttackDefinition leap = CreateAttack(
                "BossLeapSmash", "Leap Smash", DamageType.BossMelee,
                damage: 20f, startup: 0.78f, active: 0.16f, recovery: 0.82f,
                range: 3.2f, arc: 360f, poise: 42f, knockback: 9f, hitStop: 0.12f, trauma: 0.55f,
                lungeSpeed: 12f, shape: AttackShape.Sphere, stagger: StaggerStrength.Interrupt,
                telegraph: true, leavesHazard: true, hazardRadius: 3f, hazardDamagePerTick: 3f,
                hazardDuration: 3.5f, reaction: ImpactReaction.Knockdown);

            return new BossAttacks(
                sweep, slam, charge, shockwave, jab, perilous, phaseShockwave,
                delayedOverhead, spin, kick, grab, leap);
        }

        private static AttackDefinition CreateAttack(
            string assetName,
            string displayName,
            DamageType damageType,
            float damage,
            float startup,
            float active,
            float recovery,
            float range,
            float arc,
            float poise,
            float knockback,
            float hitStop,
            float trauma,
            float staminaCost = 0f,
            float lungeSpeed = 0f,
            AttackShape shape = AttackShape.Arc,
            StaggerStrength stagger = StaggerStrength.Flinch,
            bool telegraph = false,
            Color? overlayColor = null,
            bool unblockable = false,
            bool unparryable = false,
            bool leavesHazard = false,
            float hazardRadius = 2.5f,
            float hazardDamagePerTick = 4f,
            float hazardDuration = 3f,
            Vector3? offset = null,
            Vector3? boxHalfExtents = null,
            ImpactReaction reaction = ImpactReaction.None)
        {
            var attack = AssetAuthoring.CreateOrLoad<AttackDefinition>(
                $"{AttackFolder}/{assetName}.asset", out bool created);

            if (!ShouldWriteTuning(created))
            {
                return attack;
            }

            using (AssetAuthoring.AssetWriter writer = AssetAuthoring.Edit(attack))
            {
                writer.String("_id", $"attack.{assetName.ToLowerInvariant()}")
                    .String("_displayName", displayName)
                    .Enum("_damageType", damageType)
                    .Float("_damage", damage)
                    .Float("_poiseDamage", poise)
                    .Enum("_stagger", stagger)
                    .Float("_knockbackSpeed", knockback)
                    .Enum("_impactReaction", reaction)
                    .Float("_startupSeconds", startup)
                    .Float("_activeSeconds", active)
                    .Float("_recoverySeconds", recovery)
                    .Enum("_shape", shape)
                    .Float("_range", range)
                    .Float("_arcDegrees", arc)
                    .Float("_staminaCost", staminaCost)
                    .Float("_lungeSpeed", lungeSpeed)
                    .Float("_hitStopSeconds", hitStop)
                    .Float("_cameraTrauma", trauma)
                    .Bool("_showTelegraph", telegraph)
                    .Bool("_unblockable", unblockable)
                    .Bool("_unparryable", unparryable)
                    .Bool("_leavesHazard", leavesHazard)
                    .Float("_hazardRadius", hazardRadius)
                    .Float("_hazardDamagePerTick", hazardDamagePerTick)
                    .Float("_hazardDurationSeconds", hazardDuration);

                // 仅为盒形写入，其 extents 代表完整命中框。其他形状根据范围和角度计算大小，
                // 额外显示误导性的盒子没有益处。
                if (shape == AttackShape.Box && boxHalfExtents.HasValue)
                {
                    writer.Vector3("_boxHalfExtents", boxHalfExtents.Value);
                }

                // 弧形只读取偏移中的高度，按设计以攻击者为中心，因此写入前向分量会误导但不影响行为。
                // 盒形则完全按偏移定位。
                if (offset.HasValue)
                {
                    writer.Vector3("_offset", offset.Value);
                }

                if (overlayColor.HasValue)
                {
                    writer.Color("_telegraphColor", overlayColor.Value);
                }
            }

            return attack;
        }

        private static CounterStrategy[] CreateCounterStrategies()
        {
            // 每种策略都应对设计说明中的一种习惯。阈值只是调参起点，并非已经验证的数值。
            CounterStrategy delayAttacks = CreateStrategy(
                "CounterPostDodgeAggression",
                BehaviorFeature.PostDodgeAggression, FeatureComparison.AtOrAbove, threshold: 0.6f,
                phase: 1, weight: 1.4f,
                tell: "它开始等待你露出破绽。",
                (BossTuningParameter.AttackDelay, 0.35f),
                (BossTuningParameter.FeintChance, 0.3f));

            CounterStrategy closeDistance = CreateStrategy(
                "CounterKiting",
                BehaviorFeature.PreferredDistance, FeatureComparison.AtOrAbove, threshold: 0.65f,
                phase: 1, weight: 1.2f,
                tell: "它不再给你喘息的机会。",
                (BossTuningParameter.GapCloserWeight, 0.8f),
                (BossTuningParameter.PreferredRange, 1.8f),
                (BossTuningParameter.Aggression, 0.75f));

            CounterStrategy parryHeavy = CreateStrategy(
                "CounterHeavySpam",
                BehaviorFeature.HeavyAttackRatio, FeatureComparison.AtOrAbove, threshold: 0.55f,
                phase: 2, weight: 1.5f,
                tell: "它开始预判你的重攻击。",
                (BossTuningParameter.ParryChance, 0.45f));

            CounterStrategy predictDodge = CreateStrategy(
                "CounterPredictableDodge",
                BehaviorFeature.DashDirectionBias, FeatureComparison.AtOrAbove, threshold: 0.55f,
                phase: 2, weight: 1.3f,
                tell: "它已经看穿了你的翻滚方向。",
                (BossTuningParameter.DodgePredictionStrength, 0.7f));

            CounterStrategy areaDenial = CreateStrategy(
                "CounterEvasive",
                BehaviorFeature.EvasionSuccess, FeatureComparison.AtOrAbove, threshold: 0.7f,
                phase: 3, weight: 1.6f,
                tell: "它改用大范围攻击封锁你的退路。",
                (BossTuningParameter.AreaDenialWeight, 0.85f),
                (BossTuningParameter.ComboExtensionChance, 0.5f));

            CounterStrategy punishPassivity = CreateStrategy(
                "CounterPassivity",
                BehaviorFeature.AttackFrequency, FeatureComparison.AtOrBelow, threshold: 0.25f,
                phase: 1, weight: 1f,
                tell: "它正在步步紧逼。",
                (BossTuningParameter.Aggression, 0.85f),
                (BossTuningParameter.ComboExtensionChance, 0.6f));

            // 经常治疗的玩家会暴露短暂无力应对的时刻。首领的回应不是新招，
            // 而是愿意用已有招式拉近距离并施压，让治疗需要争取而非免费获得。
            CounterStrategy punishHealing = CreateStrategy(
                "CounterHealing",
                BehaviorFeature.HealFrequency, FeatureComparison.AtOrAbove, threshold: 0.5f,
                phase: 1, weight: 1.1f,
                tell: "它会在你露出破绽时立刻突进。",
                (BossTuningParameter.Aggression, 0.8f),
                (BossTuningParameter.GapCloserWeight, 0.6f));

            // 危险玩家每场都在精确帧完成完美闪避。佯攻才是公平回应：展示并取消攻击预警，
            // 反过来利用其完美时机判断；为不存在的攻击消耗闪避后，就无法应对真正到来的攻击。
            CounterStrategy baitRhythmicDodge = CreateStrategy(
                "CounterRhythmicDodge",
                BehaviorFeature.DodgeTimingConsistency, FeatureComparison.AtOrAbove, threshold: 0.5f,
                phase: 2, weight: 1.35f,
                tell: "它开始用佯攻扰乱你熟悉的节奏。",
                (BossTuningParameter.FeintChance, 0.6f),
                (BossTuningParameter.AttackDelay, 0.2f));

            // 经常挥空的玩家在慌乱，每次空挥的后摇都是首领可切入的机会。回应是投入攻击：施压、近身、
            // 连续惩罚；这也提高首领自己的投入。玩家一旦停止乱挥使其攻击落空，首领反而失衡，
            // 这种逆转正是设计重点。
            CounterStrategy punishWhiffing = CreateStrategy(
                "CounterWhiffPunish",
                BehaviorFeature.WhiffRatio, FeatureComparison.AtOrAbove, threshold: 0.5f,
                phase: 1, weight: 1.2f,
                tell: "它开始抓住你空挥后的破绽。",
                (BossTuningParameter.Aggression, 0.8f),
                (BossTuningParameter.GapCloserWeight, 0.7f),
                (BossTuningParameter.ComboExtensionChance, 0.5f));

            // 用格挡回应一切的玩家暴露了首领可以公平利用的信息：格挡只对尊重防御的攻击有效。
            // 更常使用已有危险攻击会把习惯变成负担；危险攻击前摇是战斗中预警最充分的动作，
            // 因此仍然公平，只是要求玩家闪避而非剥夺防御。
            CounterStrategy punishTurtling = CreateStrategy(
                "CounterGuardReliance",
                BehaviorFeature.GuardReliance, FeatureComparison.AtOrAbove, threshold: 0.55f,
                phase: 1, weight: 1.3f,
                tell: "它不再顾忌你的防御。",
                (BossTuningParameter.UnblockableWeight, 0.9f));

            // 招架高手与龟缩玩家不同。应针对其时机精度，展示不真正发出的攻击，
            // 并混入防御无法应对的招式，惩罚精准时机本身而非防御依赖。
            CounterStrategy punishDeflectMastery = CreateStrategy(
                "CounterDeflectMastery",
                BehaviorFeature.DeflectSkill, FeatureComparison.AtOrAbove, threshold: 0.6f,
                phase: 2, weight: 1.45f,
                tell: "它不再让你轻易把握弹反的时机。",
                (BossTuningParameter.FeintChance, 0.55f),
                (BossTuningParameter.UnblockableWeight, 0.6f));

            return new[]
            {
                delayAttacks, closeDistance, parryHeavy, predictDodge, areaDenial, punishPassivity,
                punishHealing, baitRhythmicDodge, punishWhiffing, punishTurtling, punishDeflectMastery
            };
        }

        private static CounterStrategy CreateStrategy(
            string assetName,
            BehaviorFeature feature,
            FeatureComparison comparison,
            float threshold,
            int phase,
            float weight,
            string tell,
            params (BossTuningParameter Parameter, float Target)[] adjustments)
        {
            var strategy = AssetAuthoring.CreateOrLoad<CounterStrategy>(
                $"{StrategyFolder}/{assetName}.asset", out bool created);

            if (!ShouldWriteTuning(created))
            {
                return strategy;
            }

            using (AssetAuthoring.AssetWriter writer = AssetAuthoring.Edit(strategy))
            {
                writer.String("_id", $"strategy.{assetName.ToLowerInvariant()}")
                    .Enum("_triggerFeature", feature)
                    .Enum("_comparison", comparison)
                    .Float("_threshold", threshold)
                    .Int("_minimumPhase", phase)
                    .Float("_selectionWeight", weight)
                    .String("_tellMessage", tell);

                SerializedProperty array = writer.Array("_adjustments", adjustments.Length);
                for (int i = 0; i < adjustments.Length; i++)
                {
                    SerializedProperty element = array.GetArrayElementAtIndex(i);
                    element.FindPropertyRelative("_parameter").enumValueIndex = (int)adjustments[i].Parameter;
                    element.FindPropertyRelative("_targetValue").floatValue = adjustments[i].Target;
                }
            }

            return strategy;
        }

        /// <summary>
        /// 创建或修复玩家配置。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 每次重写引用，调参数值仅在创建时写入。这种区别是从实际故障中学到的。引用属于派生数据，
        /// 资产删除、移动或重建后必须自动修复；悬空引用会让玩家攻击解析为 null，按钮悄然无效。
        /// 手工编写的调参数值则必须保留。
        /// </para>
        /// <para>
        /// 旧实现完全跳过已有配置，导致重新生成攻击资产后双方永久无法攻击，且没有任何报错。
        /// </para>
        /// </remarks>
        private static void CreatePlayerConfig(PlayerAttacks attacks, WeaponDefinition[] weapons)
        {
            var config = AssetAuthoring.CreateOrLoad<PlayerConfig>(
                $"{ConfigFolder}/DefaultPlayerConfig.asset", out bool created);

            // 处决：由反击状态发动、针对破防的毁灭性回应。长有效窗口和深 hit-stop 表现为终结打击，
            // 遭遇控制器把致命一击变成电影式演出。它不可被招架，也是全游戏唯一如此的攻击。
            // 玩家通过打破首领防御赢得这次攻击；再让首领用刚刚失败的架势弹反，
            // 会拒绝玩家已付出代价的惩罚。只有处决拥有独立资产时才可安全标记：
            // 武器的 _riposteAttack 使用普通重攻击，标记它会让所有重攻击不可招架，
            // 破坏首领招架所要求的辨识。
            AttackDefinition execution = CreateAttack(
                "PlayerExecution", "Execution", DamageType.Heavy,
                damage: 72f, startup: 0.10f, active: 0.34f, recovery: 0.46f,
                range: 2.9f, arc: 120f, poise: 60f, knockback: 8f, hitStop: 0.18f, trauma: 0.6f,
                stagger: StaggerStrength.Break, unparryable: true, overlayColor: PlayerSwingColor);

            using (AssetAuthoring.AssetWriter writer = AssetAuthoring.Edit(config))
            {
                if (created)
                {
                    writer.String("_id", "player.default");
                }

                if (ShouldWriteTuning(created))
                {
                    // 格挡损失架势必须快于弹反带来的收益，否则龟缩会成为正确答案，弹反窗口也失去意义。
                    writer.Float("_blockPostureCost", 22f)
                        .Float("_deflectPostureDamage", 28f)

                        // 打击将玩家带走的距离。足够长，让重击可见地抛动角色；也足够短，
                        // 让控制在一步内恢复。
                        .Float("_impulseHalfLifeSeconds", 0.12f)
                        .Float("_bodyMass", 1f)

                        // 响应性。此前用较沉的 0.12 秒、0.16 秒和每秒 720 度尝试增加操作重量，
                        // 玩家评价却是“无法正常控制角色”。现在重量来自动画、
                        // hit-stop 和转向动量的速度下限，而不是输入与身体之间的延迟。
                        .Float("_accelerationSeconds", 0.07f)
                        .Float("_decelerationSeconds", 0.09f)
                        .Float("_turnSpeedDegreesPerSecond", 1080f)
                        .Float("_turnSpeedFloor", 0.35f)

                        // 闪避仍滚动 4.5 米，时长改为 0.32 秒：0.4 秒版本最后十分之一秒几乎不动且不能
                        // 退出。无敌保持首领调参所依据的绝对 0.135 秒。
                        .Float("_dashDurationSeconds", 0.32f)
                        .Float("_invulnerabilitySeconds", 0.135f)
                        .Float("_dashSteerFraction", 0.2f)
                        .Float("_dashCancelFraction", 0.7f)

                        // 被击倒：约 0.7 秒腾空、0.8 秒倒地、0.6秒起身，足以表现重击；无论首领如何追击，
                        // 总时长上限 2 .6 秒，之后有 2.5 秒击倒免疫，避免连续倒地。
                        .Float("_launchUpwardSpeed", 9f)
                        .Float("_maximumAirborneSeconds", 1.2f)
                        .Float("_knockdownSeconds", 0.8f)
                        .Float("_getUpSeconds", 0.6f)
                        .Float("_knockdownImmunitySeconds", 2.5f)
                        .Float("_knockbackReactionMultiplier", 1.6f)
                        .Float("_wallImpactSpeed", 7f)
                        .Float("_wallImpactStaggerSeconds", 0.3f);
                }

                writer.ReferenceArray("_weapons", weapons)
                    .ReferenceArray("_lightComboChain", attacks.LightChain)
                    .Reference("_heavyAttack", attacks.Heavy)
                    .Reference("_specialAttack", attacks.Special)
                    .Reference("_executionAttack", execution);
            }
        }

        /// <summary>
        /// 创建三种武器原型。
        /// </summary>
        /// <remarks>
        /// 当前共享底层攻击资产，差异在防御、操控和调参。这能控制资产数量并让选择有意义：巨剑不能招架，
        /// 能量刃格挡成本低但效果弱，长剑介于两者之间，弹反窗口最紧且奖励最高。
        /// </remarks>
        private static WeaponDefinition[] CreateWeapons(PlayerAttacks bladeAttacks)
        {
            // 现在每件武器使用自己的招式，而非共享招式。操控的速度、耐力、防御已有差异；
            // 独立攻击让选择改变战斗打法而不仅是防御方式，也让首领的武器偏好学习能读取真实差异。
            PlayerAttacks greatswordAttacks = CreateGreatswordAttacks();
            PlayerAttacks energyAttacks = CreateEnergyBladeAttacks();

            WeaponDefinition blade = CreateWeapon(
                "WeaponBlade", "长剑", new Color(0.55f, 0.85f, 1f, 0.6f),
                bladeAttacks, CreateBladeEmpoweredSpecial(), DefenceStyle.Deflect,
                deflectWindow: 0.2f, deflectPosture: 28f,
                moveMultiplier: 1f, staminaMultiplier: 1f,
                swingCueId: "swing.blade",
                bladeLength: 0.95f, bladeWidth: 0.1f, bladeGlows: false);

            // 完全不能招架。投入正确时赢得交锋，投入错误时惨败；这是另一种决策而不是更差的选择。
            WeaponDefinition greatsword = CreateWeapon(
                "WeaponGreatsword", "巨剑", new Color(1f, 0.75f, 0.35f, 0.6f),
                greatswordAttacks, CreateGreatswordEmpoweredSpecial(), DefenceStyle.HyperArmour,
                deflectWindow: 0.2f, deflectPosture: 0f,
                moveMultiplier: 0.82f, staminaMultiplier: 1.35f,
                swingCueId: "swing.greatsword",
                bladeLength: 1.45f, bladeWidth: 0.19f, bladeGlows: false);

            WeaponDefinition energyBlade = CreateWeapon(
                "WeaponEnergyBlade", "能量刃", new Color(0.7f, 1f, 0.8f, 0.6f),
                energyAttacks, CreateEnergyEmpoweredSpecial(), DefenceStyle.SustainedGuard,
                deflectWindow: 0.28f, deflectPosture: 16f,
                moveMultiplier: 1.12f, staminaMultiplier: 0.85f,
                swingCueId: "swing.energy",
                bladeLength: 0.8f, bladeWidth: 0.065f, bladeGlows: true);

            return new[] { blade, greatsword, energyBlade };
        }

        /// <summary>用紫色标记专注强化特殊攻击，与专注条一致。</summary>
        private static readonly Color EmpoweredSwingColor = new Color(1f, 0.55f, 1f, 0.7f);

        /// <summary>长剑强化特殊攻击：更宽、伤害更高的旋风，不额外消耗专注。</summary>
        /// <remarks>
        /// 强化特殊攻击不耗耐力，专注条就是代价；伤害和架势伤害明显提高，让正确消费满条真正扭转交锋，
        /// 而非只换颜色。紫色闪光与专注条一致，使回报在屏幕上可读。
        /// </remarks>
        private static AttackDefinition CreateBladeEmpoweredSpecial() => CreateAttack(
            "BladeSpecialEmpowered", "Tempest", DamageType.Special,
            damage: 60f, startup: 0.16f, active: 0.34f, recovery: 0.42f,
            range: 3.3f, arc: 360f, poise: 52f, knockback: 7f, hitStop: 0.12f, trauma: 0.5f,
            staminaCost: 0f, shape: AttackShape.Sphere, overlayColor: EmpoweredSwingColor);

        /// <summary>巨剑强化特殊攻击：毁灭性的清场灾变。</summary>
        private static AttackDefinition CreateGreatswordEmpoweredSpecial() => CreateAttack(
            "GreatswordSpecialEmpowered", "Cataclysm", DamageType.Special,
            damage: 78f, startup: 0.30f, active: 0.28f, recovery: 0.5f,
            range: 4.3f, arc: 360f, poise: 80f, knockback: 11f, hitStop: 0.16f, trauma: 0.6f,
            staminaCost: 0f, shape: AttackShape.Sphere, stagger: StaggerStrength.Interrupt,
            overlayColor: EmpoweredSwingColor);

        /// <summary>能量刃强化特殊攻击：更长、更快的刀刃风暴。</summary>
        private static AttackDefinition CreateEnergyEmpoweredSpecial() => CreateAttack(
            "EnergySpecialEmpowered", "Storm of Blades", DamageType.Special,
            damage: 48f, startup: 0.14f, active: 0.40f, recovery: 0.34f,
            range: 3.1f, arc: 360f, poise: 46f, knockback: 5f, hitStop: 0.09f, trauma: 0.42f,
            staminaCost: 0f, shape: AttackShape.Sphere, overlayColor: EmpoweredSwingColor);

        /// <summary>巨剑挥击使用琥珀色。</summary>
        private static readonly Color GreatswordSwingColor = new Color(1f, 0.7f, 0.35f, 0.6f);

        /// <summary>能量刃挥击使用青色。</summary>
        private static readonly Color EnergySwingColor = new Color(0.6f, 1f, 0.85f, 0.6f);

        /// <summary>
        /// 巨剑招式：缓慢沉重的两段连击，以速度换取范围、架势伤害和令人畏惧的投入挥击。
        /// </summary>
        private static PlayerAttacks CreateGreatswordAttacks()
        {
            AttackDefinition cleave = CreateAttack(
                "GreatswordLight1", "Cleave", DamageType.Light,
                damage: 20f, startup: 0.20f, active: 0.10f, recovery: 0.34f,
                range: 2.7f, arc: 140f, poise: 30f, knockback: 5f, hitStop: 0.08f, trauma: 0.24f,
                overlayColor: GreatswordSwingColor);

            AttackDefinition rend = CreateAttack(
                "GreatswordLight2", "Rend", DamageType.Light,
                damage: 30f, startup: 0.26f, active: 0.10f, recovery: 0.44f,
                range: 2.9f, arc: 150f, poise: 42f, knockback: 7f, hitStop: 0.10f, trauma: 0.32f,
                stagger: StaggerStrength.Interrupt, overlayColor: GreatswordSwingColor);

            AttackDefinition earthbreaker = CreateAttack(
                "GreatswordHeavy", "Earthbreaker", DamageType.Heavy,
                damage: 56f, startup: 0.44f, active: 0.12f, recovery: 0.62f,
                range: 3.0f, arc: 110f, poise: 60f, knockback: 9f, hitStop: 0.14f, trauma: 0.5f,
                staminaCost: 34f, lungeSpeed: 3f, stagger: StaggerStrength.Interrupt,
                overlayColor: GreatswordSwingColor);

            AttackDefinition seismic = CreateAttack(
                "GreatswordSpecial", "Seismic Slam", DamageType.Special,
                damage: 40f, startup: 0.30f, active: 0.26f, recovery: 0.5f,
                range: 3.8f, arc: 360f, poise: 45f, knockback: 8f, hitStop: 0.10f, trauma: 0.4f,
                staminaCost: 38f, shape: AttackShape.Sphere, overlayColor: GreatswordSwingColor);

            return new PlayerAttacks(new[] { cleave, rend }, earthbreaker, seismic);
        }

        /// <summary>
        /// 能量刃招式：快速四段连击，范围短、单次伤害低，奖励贴近首领，通常难以造成硬直。
        /// </summary>
        private static PlayerAttacks CreateEnergyBladeAttacks()
        {
            AttackDefinition slice = CreateAttack(
                "EnergyLight1", "Slice", DamageType.Light,
                damage: 8f, startup: 0.06f, active: 0.05f, recovery: 0.14f,
                range: 1.9f, arc: 90f, poise: 8f, knockback: 1.5f, hitStop: 0.03f, trauma: 0.08f,
                overlayColor: EnergySwingColor);

            AttackDefinition sliceBack = CreateAttack(
                "EnergyLight2", "Backslice", DamageType.Light,
                damage: 8f, startup: 0.05f, active: 0.05f, recovery: 0.14f,
                range: 1.9f, arc: 95f, poise: 9f, knockback: 1.8f, hitStop: 0.035f, trauma: 0.09f,
                overlayColor: EnergySwingColor);

            AttackDefinition cross = CreateAttack(
                "EnergyLight3", "Cross", DamageType.Light,
                damage: 10f, startup: 0.05f, active: 0.05f, recovery: 0.16f,
                range: 2.0f, arc: 100f, poise: 12f, knockback: 2.2f, hitStop: 0.04f, trauma: 0.11f,
                overlayColor: EnergySwingColor);

            AttackDefinition flourish = CreateAttack(
                "EnergyLight4", "Flourish", DamageType.Light,
                damage: 14f, startup: 0.08f, active: 0.06f, recovery: 0.26f,
                range: 2.1f, arc: 120f, poise: 18f, knockback: 3.5f, hitStop: 0.06f, trauma: 0.18f,
                overlayColor: EnergySwingColor);

            // 游戏唯一盒形命中框，也是唯一适合这种形状的攻击。
            // 3 .4 米处 40 度锥形足以打到明显站在侧边的人，不符合刺击外观。
            // 沿 +Z 的窄盒只命中动画所指位置，让侧步避刺符合预期。中心 z = 2. 0、半深 1.4 米，
            // 覆盖前方 0.6 至 3 .4 米，与弧形同样远，却没有防御内侧的死区。
            AttackDefinition thrust = CreateAttack(
                "EnergyHeavy", "Lunge Thrust", DamageType.Heavy,
                damage: 30f, startup: 0.22f, active: 0.08f, recovery: 0.4f,
                range: 3.4f, arc: 40f, poise: 30f, knockback: 5f, hitStop: 0.10f, trauma: 0.3f,
                staminaCost: 22f, lungeSpeed: 8f, stagger: StaggerStrength.Interrupt,
                shape: AttackShape.Box,
                offset: new Vector3(0f, 1.1f, 2f),
                boxHalfExtents: new Vector3(0.5f, 0.6f, 1.4f),
                overlayColor: EnergySwingColor);

            AttackDefinition dance = CreateAttack(
                "EnergySpecial", "Blade Dance", DamageType.Special,
                damage: 26f, startup: 0.15f, active: 0.30f, recovery: 0.36f,
                range: 2.8f, arc: 360f, poise: 28f, knockback: 4f, hitStop: 0.07f, trauma: 0.28f,
                staminaCost: 28f, shape: AttackShape.Sphere, overlayColor: EnergySwingColor);

            return new PlayerAttacks(new[] { slice, sliceBack, cross, flourish }, thrust, dance);
        }

        private static WeaponDefinition CreateWeapon(
            string assetName,
            string displayName,
            Color signature,
            PlayerAttacks attacks,
            AttackDefinition empoweredSpecial,
            DefenceStyle defence,
            float deflectWindow,
            float deflectPosture,
            float moveMultiplier,
            float staminaMultiplier,
            string swingCueId,
            float bladeLength,
            float bladeWidth,
            bool bladeGlows)
        {
            var weapon = AssetAuthoring.CreateOrLoad<WeaponDefinition>(
                $"{WeaponFolder}/{assetName}.asset", out bool created);

            // 根据武器标志颜色和比例生成，在下方与引用一起赋值而非作为调参：
            // 模型缺失是下次运行应自动修复的资产错误，不是可能被手动调整的值。
            GameObject model = WeaponModelBuilder.GetOrCreate(
                assetName, signature, bladeLength, bladeWidth, bladeGlows);

            using (AssetAuthoring.AssetWriter writer = AssetAuthoring.Edit(weapon))
            {
                // 始终修复引用；调参仅在创建或重新调参时写入。
                writer.ReferenceArray("_lightComboChain", attacks.LightChain)
                    .Reference("_heavyAttack", attacks.Heavy)
                    .Reference("_specialAttack", attacks.Special)
                    .Reference("_empoweredSpecialAttack", empoweredSpecial)
                    .Reference("_riposteAttack", attacks.Heavy)
                    .Reference("_modelPrefab", model);

                if (ShouldWriteTuning(created))
                {
                    writer.String("_id", $"weapon.{assetName.ToLowerInvariant()}")
                        .String("_displayName", displayName)
                        .Color("_signatureColor", signature)
                        .String("_swingCueId", swingCueId)
                        .Enum("_defence", defence)
                        .Float("_deflectWindowSeconds", deflectWindow)
                        .Float("_deflectPostureDamage", deflectPosture)
                        .Float("_moveSpeedMultiplier", moveMultiplier)
                        .Float("_staminaCostMultiplier", staminaMultiplier);
                }
            }

            return weapon;
        }

        private static void CreateBossConfig(
            ReactionProfile reactionProfile,
            BossAttacks attacks,
            CounterStrategy[] strategies)
        {
            var config = AssetAuthoring.CreateOrLoad<BossConfig>(
                $"{ConfigFolder}/DefaultBossConfig.asset", out bool created);

            using (AssetAuthoring.AssetWriter writer = AssetAuthoring.Edit(config))
            {
                // 始终修复：悬空攻击引用使首领目录为空，不断绕竞技场却从不攻击。
                writer.Reference("_reactionProfile", reactionProfile)
                    .Reference("_phaseShockwave", attacks.PhaseShockwave)
                    .ReferenceArray("_counterStrategies", strategies);

                if (created)
                {
                    writer.String("_id", "boss.default");
                }

                if (ShouldWriteTuning(created))
                {
                    // 显式写入而不依赖仅在首次创建资产时有效的C# 字段默认值。
                    // 900 生命面对约每秒 25 伤害，相当于纯输出 36秒；再加闪避、等待后摇和治疗，
                    // 即目标为三至五分钟的获胜战斗。此前六招且无第二形态时设为 650；十一招加狂暴后，
                    // 首领还未展尽思路就死了。初版 1000 则像伤害海绵。
                    writer.Float("_maxHealth", 900f)
                        .Float("_maxPoise", 100f)
                        .Float("_poiseRegenPerSecond", 12f)
                        .Float("_resolveThreshold", 100f)
                        .Float("_resolveBuildPerSecond", 3.2f)
                        .Float("_resolveBuildPerWhiff", 14f)

                        // 招架姿态：短窗口供玩家突破，长尾段供惩罚。窗口刻意短于长剑 0.2 秒弹反，
                        // 因为玩家主动选择挥击时机，首领只能猜测。
                        // 24 点姿态损失与玩家成功弹反对首领造成的损失接近，略低。
                        .Float("_baselineParryChance", 0.12f)
                        .Float("_parryWindowSeconds", 0.18f)
                        .Float("_parryTailSeconds", 0.45f)
                        .Float("_parryPostureDamage", 24f)
                        .Float("_parryCooldownSeconds", 1.2f)

                        // 首领很重，受推后的速度衰减比玩家更快。
                        .Float("_impulseHalfLifeSeconds", 0.08f)

                        // 完整架势吸收多数冲击，破碎架势承受全部位移。重量成为玩家能够逐步削弱的东西，
                        // 而非固定墙壁。
                        .Float("_knockbackResistanceAtFullPoise", 0.85f)
                        .Float("_knockbackResistanceWhenBroken", 0f)

                        // 质量为骑士四倍：顶住首领只轻推它，却大幅推回骑士；冲锋则把骑士推到前方。
                        .Float("_bodyMass", 4f)

                        // 存在感。以三分之一速度逼近、步行完成攻击前的最后距离，仅在真正远距离时跑动；
                        // 达到速度需 0.6 秒，转向平滑。玩家要求的是“缓慢沉重的行走”，
                        // 不是快速抽动的首领。
                        .Float("_accelerationSeconds", 0.6f)
                        .Float("_stalkSpeedFraction", 0.35f)
                        .Float("_approachRunDistance", 6f)
                        .Float("_approachWalkFraction", 0.45f)
                        .Float("_turnSmoothingSeconds", 0.25f)
                        .Float("_lungeBrakeDeceleration", 120f)

                        // 玩家侧步使冲锋撞墙，是巨兽自己的失误，应中断攻击并损失约三分之一架势。
                        .Float("_wallImpactSpeed", 9f)
                        .Float("_wallImpactPoiseDamage", 45f);
                }

                SerializedProperty phases = writer.Array("_phases", 4);

                WritePhase(
                    phases.GetArrayElementAtIndex(0), ShouldWriteTuning(created), "Measured", threshold: 1f,
                    moveMultiplier: 1f, cooldown: 1.15f, adaptationRate: 1f,
                    signature: attacks.Slam,
                    attacks.Sweep, attacks.Slam, attacks.DelayedOverhead);

                WritePhase(
                    phases.GetArrayElementAtIndex(1), ShouldWriteTuning(created), "Pressing", threshold: 0.66f,
                    moveMultiplier: 1.15f, cooldown: 0.85f, adaptationRate: 1.4f,
                    signature: attacks.Charge,
                    attacks.Sweep, attacks.Slam, attacks.Charge, attacks.Jab, attacks.Perilous,
                    attacks.DelayedOverhead, attacks.Spin, attacks.Kick);

                WritePhase(
                    phases.GetArrayElementAtIndex(2), ShouldWriteTuning(created), "Relentless", threshold: 0.33f,
                    moveMultiplier: 1.3f, cooldown: 0.6f, adaptationRate: 2f,
                    signature: attacks.Shockwave,
                    attacks.Sweep, attacks.Slam, attacks.Charge, attacks.Shockwave, attacks.Jab, attacks.Perilous,
                    attacks.DelayedOverhead, attacks.Spin, attacks.Kick, attacks.Grab, attacks.Leap);

                // Last Stand 是绝望阶段，沿用其他阶段的咆哮、冲击波、无敌后仰演出，但具有最完整招式、
                // 最短后摇和最快学习，让最终阶段同时最危险且最清晰。Frenzied 形态丢剑徒手，速度更快，
                // 几乎没有后摇。在四分之一生命而非七分之一时进入，
                // 让最后形态成为完整一段战斗而非收尾装饰。
                WritePhase(
                    phases.GetArrayElementAtIndex(3), ShouldWriteTuning(created), "Frenzied", threshold: 0.25f,
                    moveMultiplier: 1.55f, cooldown: 0.4f, adaptationRate: 2.5f,
                    signature: attacks.Shockwave,
                    attacks.Sweep, attacks.Slam, attacks.Charge, attacks.Shockwave, attacks.Jab, attacks.Perilous,
                    attacks.DelayedOverhead, attacks.Spin, attacks.Kick, attacks.Grab, attacks.Leap);
            }
        }

        private static void WritePhase(
            SerializedProperty phase,
            bool writeTuningValues,
            string name,
            float threshold,
            float moveMultiplier,
            float cooldown,
            float adaptationRate,
            AttackDefinition signature,
            params AttackDefinition[] attacks)
        {
            // 调参数值仅在创建时写入，保留设计者修改。下方攻击列表和标志招式则始终重写，
            // 因为属于派生数据；悬空攻击引用会让阶段失去攻击。
            if (writeTuningValues)
            {
                phase.FindPropertyRelative("_name").stringValue = name;
                phase.FindPropertyRelative("_healthThreshold").floatValue = threshold;
                phase.FindPropertyRelative("_moveSpeedMultiplier").floatValue = moveMultiplier;
                phase.FindPropertyRelative("_attackCooldownSeconds").floatValue = cooldown;
                phase.FindPropertyRelative("_adaptationRateMultiplier").floatValue = adaptationRate;
            }

            phase.FindPropertyRelative("_signatureAttack").objectReferenceValue = signature;

            SerializedProperty attackArray = phase.FindPropertyRelative("_attacks");
            attackArray.arraySize = attacks.Length;

            for (int i = 0; i < attacks.Length; i++)
            {
                attackArray.GetArrayElementAtIndex(i).objectReferenceValue = attacks[i];
            }
        }

        private static void CreateArenaConfig()
        {
            var config = AssetAuthoring.CreateOrLoad<ArenaConfig>(
                $"{ConfigFolder}/DefaultArenaConfig.asset", out bool created);

            using (AssetAuthoring.AssetWriter writer = AssetAuthoring.Edit(config))
            {
                if (created)
                {
                    writer.String("_id", "arena.default");
                }

                if (ShouldWriteTuning(created))
                {
                    // 显式写入而非依赖仅在首次创建资产时生效的 C # 默认值。此资产已存在，
                    // 代码默认值永远无法更新它，可能让 Inspector 标志未设置而行为像已设置。
                    writer.Bool("_clashEnabled", true);
                }
            }
        }

        private static void CreateEventChannels()
        {
            CreateChannel<VoidEventChannel>("OnPlayerDied", "Raised once when the player is defeated.");
            CreateChannel<VoidEventChannel>(
                "OnPerfectDodge",
                "Raised when a dodge negates an attack within the perfect-dodge window. Drives the " +
                "flourish that tells the player their timing was read correctly.");
            CreateChannel<VoidEventChannel>("OnBossDefeated", "Raised once when the boss is defeated.");
            CreateChannel<IntEventChannel>("OnBossPhaseChanged", "Carries the new zero-based phase index.");
            CreateChannel<FloatEventChannel>("OnBossHealthChanged", "Carries normalised boss health for the health bar.");
            CreateChannel<FloatEventChannel>("OnPlayerHealthChanged", "Carries normalised player health.");
            CreateChannel<FloatEventChannel>("OnPlayerStaminaChanged", "Carries normalised player stamina.");
            CreateChannel<FloatEventChannel>(
                "OnPlayerPostureChanged",
                "Carries normalised player posture. Draining it means the guard is about to break.");
            CreateChannel<FloatEventChannel>(
                "OnPlayerFocusChanged",
                "Carries normalised player focus. Filling it empowers the next special.");
            CreateChannel<FloatEventChannel>(
                "OnBossPostureChanged",
                "Carries normalised boss posture. Emptying it opens the riposte.");
            CreateChannel<StringEventChannel>(
                "OnWeaponDrawn",
                "Carries the name of a newly drawn weapon.");
            CreateChannel<StringEventChannel>(
                "OnWeaponSwingCue",
                "Carries the audio cue the drawn weapon swings with, so three weapons sharing one " +
                "moveset can still be told apart by ear. Separate from the display name because " +
                "matching on a name shown to the player would break the moment it were reworded.");
            CreateChannel<VoidEventChannel>(
                "OnDeflect",
                "Raised on a clean deflect. Drives the metallic ring, spark and freeze that make the " +
                "timing worth learning.");
            CreateChannel<StringEventChannel>(
                "OnAdaptationAdopted",
                "Carries the tell message for a newly adopted counter-strategy. Listened to by the " +
                "heads-up display so the player can perceive that the boss has changed.");
            CreateChannel<BoolEventChannel>("OnPauseStateChanged", "Carries whether the game is now paused.");
            CreateChannel<VoidEventChannel>(
                "OnBossOverbalanced",
                "Raised when a committed boss swing whiffs hard enough to overbalance it. Drives the " +
                "stumble, dust and stagger sound that show the opening the player's read just bought.");
            CreateChannel<VoidEventChannel>(
                "OnBossParried",
                "Raised when the boss meets a swing on the beat. The mirror of the player's deflect " +
                "channel, and routed the same way: the encounter director turns it into posture " +
                "damage on the player, so the player never holds a reference to the boss.");
        }

        private static void CreateChannel<TChannel>(string assetName, string description)
            where TChannel : EventChannelBase
        {
            var channel = AssetAuthoring.CreateOrLoad<TChannel>(
                $"{EventFolder}/{assetName}.asset", out bool created);

            if (created)
            {
                using (AssetAuthoring.AssetWriter writer = AssetAuthoring.Edit(channel))
                {
                    writer.String("_description", description);
                }
            }
        }
    }
}
