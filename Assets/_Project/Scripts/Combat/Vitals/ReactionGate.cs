using AdaptiveBossArena.Core.Combat;

namespace AdaptiveBossArena.Combat.Vitals
{
    /// <summary>从被击退或击倒的角度看，角色身体当前所处的状态。</summary>
    public enum Footing
    {
        /// <summary>站立，处于控制中，最严重也只是硬直。</summary>
        Standing = 0,

        /// <summary>被抛起，尚未落地。</summary>
        Airborne = 1,

        /// <summary>倒在地面。</summary>
        Down = 2,

        /// <summary>正在从地面起身。</summary>
        GettingUp = 3
    }

    /// <summary>
    /// 决定角色实际承受哪些击倒和击飞，防止它们被连续衔接。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 击倒和击飞会夺走控制权，而任何夺走控制权的机制都可能反复施加，让角色永远无法取回控制权。
    /// 项目曾经已经发布过一次此类缺陷：每次命中都造成硬直，硬直还会自行延长，
    /// 因此足够快的首领能无限将玩家困在原地。这里是同一种缺陷，后果更严重，因此通过结构排除，
    /// 而不是靠调参规避。
    /// </para>
    /// <para>
    /// 每条规则都关闭一种被压制的方式：每次腾空最多击飞一次；倒地或起身的身体不会受伤；
    /// 站起后的一段时间内，击倒和击飞退化为普通击退。无论命中次数多少、速度多快，
    /// 它们共同将失去控制的最长时间限制到<see cref="WorstCaseSecondsWithoutControl"/>。采用纯逻辑，
    /// 让每条规则都能测试。
    /// </para>
    /// </remarks>
    public sealed class ReactionGate
    {
        private readonly float _immunitySeconds;
        private float _immuneUntil = float.NegativeInfinity;

        /// <summary>创建判定门。</summary>
        /// <param name="immunitySecondsAfterGettingUp">
        /// 站起后多长时间内，击倒和击飞降级为击退。
        /// </param>
        public ReactionGate(float immunitySecondsAfterGettingUp)
        {
            _immunitySeconds = immunitySecondsAfterGettingUp < 0f ? 0f : immunitySecondsAfterGettingUp;
        }

        /// <summary>身体当前所处的状态。</summary>
        public Footing Footing { get; private set; } = Footing.Standing;

        /// <summary>角色是否失去站立能力且尚未恢复控制。</summary>
        public bool IsReacting => Footing != Footing.Standing;

        /// <summary>命中能否对角色造成伤害。</summary>
        /// <remarks>
        /// 倒地和起身时为 false。否则倒在地面的身体会成为游戏中最容易命中的目标，
        /// 击倒也会变成最强的起手，而不是喘息机会。
        /// </remarks>
        public bool CanBeHurt => Footing == Footing.Standing || Footing == Footing.Airborne;

        /// <summary>当前能否开始较轻的打断，例如硬直。</summary>
        /// <remarks>仅限站立时：空中或倒地时进入硬直会截断受击反应。</remarks>
        public bool AdmitsStagger => Footing == Footing.Standing;

        /// <summary>决定请求的受击反应会变成什么，并让身体进入该状态。</summary>
        /// <param name="requested">命中请求的反应。</param>
        /// <param name="now">当前战斗时间，单位为秒。</param>
        /// <returns>角色承受的反应；可能弱于请求，但绝不会更强。</returns>
        public ImpactReaction Admit(ImpactReaction requested, float now)
        {
            if (!CanBeHurt)
            {
                return ImpactReaction.None;
            }

            if (requested != ImpactReaction.Knockdown && requested != ImpactReaction.Launch)
            {
                return requested;
            }

            // 空中时，落地本就会使其倒地。再次击飞会产生浮空连段，而击倒只会截断下落；两者都改为推力。
            if (Footing == Footing.Airborne || now < _immuneUntil)
            {
                return ImpactReaction.Knockback;
            }

            Footing = requested == ImpactReaction.Launch ? Footing.Airborne : Footing.Down;

            return requested;
        }

        /// <summary>被抛起的身体到达地面。</summary>
        public void Land()
        {
            if (Footing == Footing.Airborne)
            {
                Footing = Footing.Down;
            }
        }

        /// <summary>倒地的身体开始起身。</summary>
        public void BeginGettingUp()
        {
            if (Footing == Footing.Down)
            {
                Footing = Footing.GettingUp;
            }
        }

        /// <summary>角色重新站立，并短暂免疫再次击倒。</summary>
        /// <param name="now">当前战斗时间，单位为秒。</param>
        public void FinishGettingUp(float now)
        {
            if (Footing != Footing.GettingUp)
            {
                return;
            }

            Footing = Footing.Standing;
            _immuneUntil = now + _immunitySeconds;
        }

        /// <summary>恢复到没有免疫的站立状态，用于重试。</summary>
        public void Reset()
        {
            Footing = Footing.Standing;
            _immuneUntil = float.NegativeInfinity;
        }

        /// <summary>单次击倒或击飞能阻止角色行动的最长时间。</summary>
        /// <remarks>
        /// 每项都是一个阶段的硬性上限，而判定门会拒绝任何增加阶段的命中：空中不能再次击飞，
        /// 倒地不能受伤，起身后不能再次击倒。因此总和是上限，而不是估计。
        /// </remarks>
        /// <param name="maximumAirborneSeconds">腾空状态等待落地的最长时间。</param>
        /// <param name="downSeconds">倒地时间。</param>
        /// <param name="getUpSeconds">起身时间。</param>
        /// <returns>无法控制的最大秒数。</returns>
        public static float WorstCaseSecondsWithoutControl(
            float maximumAirborneSeconds, float downSeconds, float getUpSeconds) =>
            maximumAirborneSeconds + downSeconds + getUpSeconds;
    }
}
