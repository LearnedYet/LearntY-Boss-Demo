using AdaptiveBossArena.Core.Combat;
using UnityEngine;

namespace AdaptiveBossArena.Combat
{
    /// <summary>
    /// 交战角色上接收命中并转发给拥有者的体积。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 接收体积与角色分离，使单个角色可以拥有多个体积，各有伤害倍率。因此首领能拥有值得瞄准的弱点，
    /// 而伤害管线无需了解身体结构。
    /// </para>
    /// <para>
    /// 初始化时沿层级向上查找一次拥有者。每次命中再查找会在战斗最忙时重复搜索；
    /// 缓存还让配置错误的预制体在启动而非首次接触时报告问题。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class Hurtbox : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Damage scaling for hits landing here. Above one marks a weak point; below one " +
                 "marks armour.")]
        [Range(0.1f, 4f)]
        private float _damageMultiplier = 1f;

        private IDamageable _owner;

        /// <summary>此体积所属的交战角色。</summary>
        public IDamageable Owner => _owner;

        /// <summary>命中此处时应用的伤害缩放。</summary>
        public float DamageMultiplier => _damageMultiplier;

        /// <summary>此体积已接入角色时为 true。</summary>
        public bool IsValid => _owner != null;

        private void Awake()
        {
            _owner = GetComponentInParent<IDamageable>();

            if (_owner == null)
            {
                Debug.LogError(
                    $"[Adaptive Boss Arena] Hurtbox on '{name}' found no IDamageable in its parents. " +
                    "Hits landing here will be discarded.",
                    this);
            }
        }

        /// <summary>
        /// 将来袭攻击按此体积倍率缩放，再提交给拥有者。
        /// </summary>
        /// <param name="damage">来袭攻击。</param>
        /// <returns>
        /// 拥有者对命中的决定。是否命中由拥有者而非此体积决定；无敌帧和完美闪避在那里处理。
        /// </returns>
        public DamageResult Receive(in DamageInfo damage)
        {
            if (_owner == null)
            {
                return DamageResult.NoDamage(DamageOutcome.Ignored);
            }

            if (Mathf.Approximately(_damageMultiplier, 1f))
            {
                return _owner.TakeDamage(damage);
            }

            DamageInfo scaled = damage.WithAmount(damage.Amount * _damageMultiplier);
            return _owner.TakeDamage(scaled);
        }
    }
}
