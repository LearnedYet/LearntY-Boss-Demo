using System.Collections.Generic;
using AdaptiveBossArena.Combat.Feel;

namespace AdaptiveBossArena.Editor.Art
{
    /// <summary>
    /// 战斗角色使用哪个模型，以及每项行为播放哪个片段。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 每个身体一张表。Animator Controller、骨架及其缩放都据此构建，因此替换角色美术只需改一张表，
    /// 不必修改构建器。
    /// </para>
    /// <para>
    /// 片段通过查看所有姿势的接触图表选择，而不是按文件名猜测；每个条目旁都说明理由。
    /// Humanoid 片段可以跨骨架重定向，所以骑士能使用 CC0库的翻滚：Mixamo 剑盾包没有此动作。
    /// </para>
    /// </remarks>
    public sealed class CharacterClipTable
    {
        /// <summary>由攻击自身时间轴定位片段而不是按时钟播放的状态。</summary>
        public static readonly IReadOnlyCollection<string> AttackStateNames = new[]
        {
            "Light1", "Light2", "Light3", "Heavy", "Special", "Overhead", "Hook", "Dash", "Cast",

            // 巨兽第二套招式：踢击、抓取、跃击，以及狂暴时的徒手连击。按身体动作而不是使用它的招式命名，
            // 让一个片段可以服务多个招式。
            "Kick", "Spin", "Grab", "Leap", "Claw", "Rip", "Roar",

            // 处决动作以前借用掠过目标的斩击。招架姿态不在此处：它由桥接组件作为状态交叉淡入，
            // 不由攻击时间轴定位。
            "Riposte"
        };

        private const string Mixamo = LicensedArtPostprocessor.MixamoFolder;
        private const string QuaterniusLibrary1 = "Assets/_Project/Art/ThirdParty/Quaternius/UniversalAnimationLibrary/UAL1_Standard.fbx";
        private const string QuaterniusLibrary2 = "Assets/_Project/Art/ThirdParty/Quaternius/UniversalAnimationLibrary2/UAL2_Standard.fbx";

        /// <summary>创建一张表。</summary>
        /// <param name="name">生成控制器的名称。</param>
        /// <param name="rigModel">战斗角色模型的资产路径。</param>
        /// <param name="rigScale">使模型达到目标身高的统一缩放。</param>
        /// <param name="clipSources">查找片段的资产路径和文件夹；首个匹配项优先。</param>
        /// <param name="locomotion">零到一的速度阈值，以及各阈值使用的片段。</param>
        /// <param name="states">其余每个状态及其片段。</param>
        /// <param name="directional">
        /// 方向混合使用的前进、后退和两侧移动片段，按实测位移定位。为空时保留以下配置的单速度混合：
        /// <paramref name="locomotion"/>.
        /// </param>
        public CharacterClipTable(
            string name,
            string rigModel,
            float rigScale,
            string[] clipSources,
            (float Speed, string Clip)[] locomotion,
            IReadOnlyDictionary<string, string> states,
            string[] directional = null)
        {
            Name = name;
            RigModel = rigModel;
            RigScale = rigScale;
            ClipSources = clipSources;
            Locomotion = locomotion;
            States = states;
            Directional = directional ?? new string[0];
        }

        /// <summary>骑士：Mixamo 的 Paladin，配合剑盾动作集。</summary>
        public static CharacterClipTable Knight => new CharacterClipTable(
            "KnightController",
            LicensedArtPostprocessor.KnightCharacter,
            1f,
            new[] { Mixamo + "Animations/Knight", Mixamo + "Animations/Shared", QuaterniusLibrary1 },
            new[]
            {
                // idle (4)：稳定持盾姿态。idle (2) 和 (3) 是持续八秒的小动作，会偏离站姿。
                (0f, "sword and shield idle (4)"),
                (0.3f, "sword and shield walk"),
                // 使用 run 而非 run (2)：后者是低身冲刺，看起来像逃跑。
                (1f, "sword and shield run")
            },
            new Dictionary<string, string>
            {
                // 使用完整投入的翻滚，而非 CC0 库的替代动作：剑盾集没有自带翻滚，
                // 而闪避是玩家最常做的动作。
                [CharacterAnimatorParameters.RollState] = "Sprinting Forward Roll",
                [CharacterAnimatorParameters.GuardState] = "sword and shield block idle",
                [CharacterAnimatorParameters.StaggerState] = "sword and shield impact (2)",
                [CharacterAnimatorParameters.DeathState] = "sword and shield death",
                // impact：盾后肩部短促回缩，而非踉跄。
                [CharacterAnimatorParameters.HitState] = "sword and shield impact",
                // 身体向后被抛离地面；起身动作从仰卧开始，与之匹配。
                [CharacterAnimatorParameters.AirborneState] = "Flying Back Death",
                [CharacterAnimatorParameters.KnockedDownState] = "Getting Up",

                // 三段连击：前向斩、回斩、最后低位横扫。
                ["Light1"] = "sword and shield slash",
                ["Light2"] = "sword and shield slash (3)",
                ["Light3"] = "sword and shield slash (5)",
                // 动作集中幅度最大、身体随之转动的投入挥击。
                ["Heavy"] = "sword and shield slash (4)",
                ["Special"] = "sword and shield attack (2)",
                ["Overhead"] = "sword and shield attack",
                ["Hook"] = "sword and shield kick",
                ["Dash"] = "sword and shield attack (3)",
                ["Cast"] = "sword and shield casting (2)",

                // 将盾牌向前打出，而非使用招架以前借用的持续持盾姿势。也不用Mixamo 拳击：
                // 其前摇让身体偏离前方 77 度，不符合玩家可以操控方向的姿态朝向规则。
                ["Parry"] = "sword and shield block",
                // 处决：双手驱动的刺击，而非又一次掠过目标的斩击。
                ["Riposte"] = "Stabbing"
            },
            // walk (2) 向后移动；strafe 和 strafe (4) 向右，(2) 和 (3) 向左；这是测量结果而非猜测。
            new[]
            {
                "sword and shield walk", "sword and shield run", "sword and shield walk (2)",
                "sword and shield strafe", "sword and shield strafe (2)", "sword and shield strafe (3)", "sword and shield strafe (4)"
            });

        /// <summary>首领：Mixamo 的 Warrok，驼背巨兽，配合巨剑动作集。</summary>
        public static CharacterClipTable Brute => new CharacterClipTable(
            "BruteController",
            LicensedArtPostprocessor.BossCharacter,
            1.05f,
            new[] { Mixamo + "Animations/Boss", Mixamo + "Animations/Shared", QuaterniusLibrary1 },
            new[]
            {
                (0f, "great sword idle"),
                (0.3f, "great sword walk"),
                // 使用 run (2) 而非 run：“great sword run” 实际向后迈步，曾让前进的巨兽播放后退步伐。
                (1f, "great sword run (2)")
            },
            new Dictionary<string, string>
            {
                [CharacterAnimatorParameters.RollState] = "Roll",
                [CharacterAnimatorParameters.GuardState] = "great sword blocking (2)",
                // impact (3)：最长的回缩，向后踉跄一步，表现架势崩溃而非轻微受击。
                [CharacterAnimatorParameters.StaggerState] = "great sword impact (3)",
                [CharacterAnimatorParameters.DeathState] = "two handed sword death",
                [CharacterAnimatorParameters.HitState] = "great sword impact",
                [CharacterAnimatorParameters.AirborneState] = "great sword impact (2)",
                [CharacterAnimatorParameters.KnockedDownState] = "great sword impact (2)",

                ["Light1"] = "great sword slash (5)",
                // Wide Sweep：slash (4) 让刀刃在腰高横过身体。
                ["Light2"] = "great sword slash (4)",
                ["Light3"] = "great sword high spin attack",
                // Perilous Overhead：slash (3) 先高举刀刃再劈下。
                ["Heavy"] = "great sword slash (3)",
                ["Special"] = "great sword attack",
                // Ground Slam：跃起后身体连同刀刃一起砸下。
                ["Overhead"] = "great sword jump attack",
                // Jab：动作集中最快的斩击。
                ["Hook"] = "great sword slash",
                // Charge：向前滑步后斩击。
                ["Dash"] = "great sword slide attack",
                // 冲击波：蓄势施法后向外释放。
                ["Cast"] = "spell cast",

                // 破防踢击：保持握剑时向前蹬出靴子。
                ["Kick"] = "great sword kick",
                // 旋转连击：刀刃绕身体完整转一圈。
                ["Spin"] = "great sword high spin attack",
                // 抓取：双臂向前伸出，抓住骑士。
                ["Grab"] = "Zombie Attack",
                // 跃击：身体腾空，落到骑士身上。
                ["Leap"] = "Mutant Jump Attack",
                // 放下剑后，狂暴状态的徒手横挥。
                ["Claw"] = "Mutant Swiping",
                // 将立柱从地面拔出，以肩部重量带动投掷。
                ["Rip"] = "Mutant Punch",
                // 咆哮：仰头、挺胸，也用于阶段转换。
                ["Roar"] = "Mutant Roaring"
            },
            // walk (2) 向后移动；strafe (2) 和 (4) 向右，strafe 和 (3) 向左。
            new[]
            {
                "great sword walk", "great sword run (2)", "great sword walk (2)",
                "great sword strafe", "great sword strafe (2)", "great sword strafe (3)", "great sword strafe (4)"
            });

        /// <summary>
        /// 巨兽放下剑后的形态：同一身体，徒手战斗。
        /// </summary>
        /// <remarks>
        /// 同一骨架上的第二个控制器，而非第二个角色。状态名称与以下控制器一致：<see cref="Brute"/>，
        /// 因此每次攻击保留原有的片段绑定，狂暴形态无需独立招式；同一“Heavy”状态在这里播放重拳，
        /// 在原形态播放高举刀刃。
        /// </remarks>
        public static CharacterClipTable BruteFrenzy => new CharacterClipTable(
            "BruteFrenzyController",
            LicensedArtPostprocessor.BossCharacter,
            1.05f,
            new[] { Mixamo + "Animations/Boss", Mixamo + "Animations/Shared", QuaterniusLibrary1 },
            new[]
            {
                // 狂暴时从不真正静止：最慢也会驼背屈身。
                (0f, "Mutant Flexing Muscles"),
                (0.3f, "Mutant Walking"),
                (1f, "Mutant Run")
            },
            new Dictionary<string, string>
            {
                [CharacterAnimatorParameters.RollState] = "Roll",
                // 已没有可以格挡的装备，因此举盾姿态改为敞开胸口。
                [CharacterAnimatorParameters.GuardState] = "Standing Taunt Chest Thump",
                [CharacterAnimatorParameters.StaggerState] = "great sword impact (3)",
                [CharacterAnimatorParameters.DeathState] = "Mutant Dying",
                [CharacterAnimatorParameters.HitState] = "great sword impact",
                [CharacterAnimatorParameters.AirborneState] = "great sword impact (2)",
                [CharacterAnimatorParameters.KnockedDownState] = "Kneeling Idle",

                // 所有挥击改为徒手动作：连击用横挥，重攻击用拳击，移动攻击用跃击，演出动作用咆哮。
                ["Light1"] = "Mutant Swiping",
                ["Light2"] = "Mutant Swiping",
                ["Light3"] = "Mutant Punch",
                ["Heavy"] = "Mutant Punch",
                ["Special"] = "Mutant Roaring",
                ["Overhead"] = "Mutant Jump Attack",
                ["Hook"] = "Mutant Swiping",
                ["Dash"] = "Mutant Jump Attack",
                ["Cast"] = "Mutant Roaring",
                ["Kick"] = "Mutant Punch",
                ["Spin"] = "Mutant Swiping",
                ["Grab"] = "Zombie Attack",
                ["Leap"] = "Mutant Jump Attack",
                ["Claw"] = "Mutant Swiping",
                ["Rip"] = "Mutant Punch",
                ["Roar"] = "Mutant Roaring"
            });

        /// <summary>生成控制器的名称。</summary>
        public string Name { get; }

        /// <summary>战斗角色模型的资产路径。</summary>
        public string RigModel { get; }

        /// <summary>使模型达到目标身高的统一缩放。</summary>
        public float RigScale { get; }

        /// <summary>查找片段的资产路径和文件夹；首个匹配项优先。</summary>
        public string[] ClipSources { get; }

        /// <summary>按 Speed 参数混合的速度阈值及其片段。</summary>
        public (float Speed, string Clip)[] Locomotion { get; }

        /// <summary>其余每个状态及其片段。</summary>
        public IReadOnlyDictionary<string, string> States { get; }

        /// <summary>方向混合使用的移动片段；为空时使用单速度混合。</summary>
        public string[] Directional { get; }

        /// <summary>许可美术缺失时双方使用的 CC0 人偶。</summary>
        /// <param name="name">生成控制器的名称。</param>
        /// <param name="rigScale">此战斗角色的缩放。</param>
        /// <returns>该表。</returns>
        public static CharacterClipTable Fallback(string name, float rigScale) => new CharacterClipTable(
            name,
            QuaterniusLibrary1,
            rigScale,
            new[] { QuaterniusLibrary1, QuaterniusLibrary2 },
            new[] { (0f, "Sword_Idle"), (0.3f, "Walk_Loop"), (0.7f, "Jog_Fwd_Loop"), (1f, "Sprint_Loop") },
            new Dictionary<string, string>
            {
                [CharacterAnimatorParameters.RollState] = "Roll",
                [CharacterAnimatorParameters.GuardState] = "Sword_Block",
                [CharacterAnimatorParameters.StaggerState] = "Idle_Shield_Break",
                [CharacterAnimatorParameters.DeathState] = "Death01",
                [CharacterAnimatorParameters.HitState] = "Hit_Chest",
                [CharacterAnimatorParameters.AirborneState] = "Hit_Knockback",
                [CharacterAnimatorParameters.KnockedDownState] = "LayToIdle",
                ["Light1"] = "Sword_Regular_A",
                ["Light2"] = "Sword_Regular_B",
                ["Light3"] = "Sword_Regular_C",
                ["Heavy"] = "Sword_Attack",
                ["Special"] = "Sword_Regular_Combo",
                ["Overhead"] = "OverhandThrow",
                ["Hook"] = "Melee_Hook",
                ["Dash"] = "Sword_Dash_RM",
                ["Cast"] = "OverhandThrow"
            });
    }
}
