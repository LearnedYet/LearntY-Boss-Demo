using UnityEngine;

namespace AdaptiveBossArena.Core.Constants
{
    /// <summary>
    /// 项目定义的物理层名称。
    /// </summary>
    /// <remarks>
    /// 项目配置器按此列表创建层并配置碰撞矩阵，因此这里的名称是唯一正式定义，
    /// 而非检查器设置的另一个副本。
    /// </remarks>
    public static class LayerNames
    {
        /// <summary>玩家身体碰撞体。</summary>
        public const string Player = "Player";

        /// <summary>代表玩家造成伤害的区域。</summary>
        public const string PlayerHitbox = "PlayerHitbox";

        /// <summary>玩家身上接收伤害的区域。</summary>
        public const string PlayerHurtbox = "PlayerHurtbox";

        /// <summary>Boss 身体碰撞体。</summary>
        public const string Boss = "Boss";

        /// <summary>代表 Boss 造成伤害的区域。</summary>
        public const string BossHitbox = "BossHitbox";

        /// <summary>Boss 身上接收伤害的区域。</summary>
        public const string BossHurtbox = "BossHurtbox";

        /// <summary>竞技场地面和墙壁。</summary>
        public const string Arena = "Arena";

        /// <summary>移动中的伤害区域。</summary>
        public const string Projectile = "Projectile";

        /// <summary>
        /// 死亡战士的物理身体。与世界和碎片碰撞，不与战士或伤害区域碰撞，因此尸体既不能阻挡活人，
        /// 也不能受攻击。
        /// </summary>
        public const string Ragdoll = "Ragdoll";

        /// <summary>破碎石块。与世界、尸体和其他碎片碰撞，不与战士或伤害区域碰撞。</summary>
        public const string Debris = "Debris";

        /// <summary>全部自定义层，按配置器分配顺序排列。</summary>
        public static readonly string[] All =
        {
            Player,
            PlayerHitbox,
            PlayerHurtbox,
            Boss,
            BossHitbox,
            BossHurtbox,
            Arena,
            Projectile,
            Ragdoll,
            Debris
        };
    }

    /// <summary>
    /// 已解析的层索引和掩码，首次使用后缓存。
    /// </summary>
    /// <remarks>
    /// <see cref="LayerMask.NameToLayer"/>会对项目层表进行字符串比较。
    /// 每帧多次重叠检测的攻击区域反复调用它很浪费，因此只解析一次并复用结果。
    /// </remarks>
    public static class Layers
    {
        private static bool _initialised;

        private static int _player;
        private static int _playerHitbox;
        private static int _playerHurtbox;
        private static int _boss;
        private static int _bossHitbox;
        private static int _bossHurtbox;
        private static int _arena;
        private static int _projectile;
        private static int _ragdoll;
        private static int _debris;

        /// <summary>玩家身体层索引。</summary>
        public static int Player => Resolved(ref _player);

        /// <summary>玩家攻击区域层索引。</summary>
        public static int PlayerHitbox => Resolved(ref _playerHitbox);

        /// <summary>玩家受击区域层索引。</summary>
        public static int PlayerHurtbox => Resolved(ref _playerHurtbox);

        /// <summary>Boss 身体层索引。</summary>
        public static int Boss => Resolved(ref _boss);

        /// <summary>Boss 攻击区域层索引。</summary>
        public static int BossHitbox => Resolved(ref _bossHitbox);

        /// <summary>Boss 受击区域层索引。</summary>
        public static int BossHurtbox => Resolved(ref _bossHurtbox);

        /// <summary>竞技场几何层索引。</summary>
        public static int Arena => Resolved(ref _arena);

        /// <summary>投射物层索引。</summary>
        public static int Projectile => Resolved(ref _projectile);

        /// <summary>死亡战士物理身体的层索引。</summary>
        public static int Ragdoll => Resolved(ref _ragdoll);

        /// <summary>破碎石块的层索引。</summary>
        public static int Debris => Resolved(ref _debris);

        /// <summary>匹配玩家攻击可伤害的全部对象的掩码。</summary>
        public static int PlayerAttackMask => 1 << BossHurtbox;

        /// <summary>匹配 Boss 攻击可伤害的全部对象的掩码。</summary>
        public static int BossAttackMask => 1 << PlayerHurtbox;

        /// <summary>匹配阻挡移动和视线的实体几何的掩码。</summary>
        public static int ArenaMask => 1 << Arena;

        /// <summary>强制重建缓存，在配置器修改层表后使用。</summary>
        public static void InvalidateCache() => _initialised = false;

        /// <summary>返回缓存索引，首次访问时解析整组层。</summary>
        private static int Resolved(ref int field)
        {
            if (!_initialised)
            {
                ResolveAll();
            }

            return field;
        }

        /// <summary>一次解析全部层索引，并报告缺失项。</summary>
        private static void ResolveAll()
        {
            _initialised = true;

            _player = Lookup(LayerNames.Player);
            _playerHitbox = Lookup(LayerNames.PlayerHitbox);
            _playerHurtbox = Lookup(LayerNames.PlayerHurtbox);
            _boss = Lookup(LayerNames.Boss);
            _bossHitbox = Lookup(LayerNames.BossHitbox);
            _bossHurtbox = Lookup(LayerNames.BossHurtbox);
            _arena = Lookup(LayerNames.Arena);
            _projectile = Lookup(LayerNames.Projectile);
            _ragdoll = Lookup(LayerNames.Ragdoll);
            _debris = Lookup(LayerNames.Debris);
        }

        /// <summary>解析单个层，项目未配置时发出显著警告。</summary>
        private static int Lookup(string layerName)
        {
            int index = LayerMask.NameToLayer(layerName);

            if (index < 0)
            {
                Debug.LogError(
                    $"Layer '{layerName}' does not exist. " +
                    "Run 'Adaptive Boss Arena/Setup/Configure Project' to create the project's layers.");
                return 0;
            }

            return index;
        }
    }
}
