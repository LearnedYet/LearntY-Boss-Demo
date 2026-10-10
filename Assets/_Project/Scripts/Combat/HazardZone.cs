using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Core.Constants;
using AdaptiveBossArena.Core.Services;
using UnityEngine;

namespace AdaptiveBossArena.Combat
{
    /// <summary>
    /// 持续存在的危险地面：先预警，再逐步伤害站在其中的角色，最后消退。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 危险区域只伤害<em>玩家</em>：归属首领阵营并查询玩家受击盒层。首领不受自己火焰伤害符合类型惯例，
    /// 更重要的是，竞技场新增的空间压力完全位于防火墙的玩家一侧。AI 完全不知道危险区域存在。
    /// </para>
    /// <para>
    /// 伤害设置 <see cref="DamageInfo.IgnoresInvulnerability"/>，因此闪避不能穿过危险区域；
    /// 应对灼烧地面要<em>离开</em>，而非把握无敌帧。它不造成姿态伤害，也不引发硬直，
    /// 短暂停留损失生命与站位，而非控制权：产生压力，不形成无限硬直。
    /// 按 <see cref="ITimeService.DeltaTime"/>更新，使危险区域随世界在顿帧中冻结。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class HazardZone : MonoBehaviour
    {
        /// <summary>区域造成伤害前的预警时间，足够看清并离开。</summary>
        private const float WarningSeconds = 0.55f;

        /// <summary>生效窗口后的无害消散时间。</summary>
        private const float FadeSeconds = 0.5f;

        /// <summary>伤害跳数之间的秒数，使区域逐步削血而非瞬间撕碎玩家。</summary>
        private const float DamageIntervalSeconds = 0.45f;

        /// <summary>每跳轻微向外推力，引导玩家离开区域边缘。</summary>
        private const float HazardKnockback = 2.5f;

        /// <summary>圆盘高于地面的高度，避免深度冲突。</summary>
        private const float DiscLift = 0.02f;


        /// <summary>生效期间圆盘的最高不透明度。</summary>
        private const float MaxAlpha = 0.95f;

        /// <summary>大于一，使痕迹裂缝中的余烬产生泛光；颜色由纹理提供。</summary>
        private static readonly Color HazardColor = new Color(1.25f, 1.1f, 1.05f);

        /// <summary>痕迹宽度相对于伤害直径的比例。</summary>
        private const float ScarReach = 2.3f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>区域爆发音效标识；采用字符串，因为 Combat 位于音频服务下层。</summary>
        private const string HazardCueId = "hazard.erupt";

        /// <summary>每次烧伤骑士时播放的音效，与音频服务的灼烧提示一致。</summary>
        private const string BurnCueId = "hazard.burn";

        /// <summary>伤害跳烧到目标后痕迹闪亮的时长。</summary>
        private const float FlareSeconds = 0.22f;

        /// <summary>闪亮峰值时痕迹增加的亮度。</summary>
        private const float FlareBoost = 1.6f;

        /// <summary>闪亮剩余秒数，按战斗时间倒计时。</summary>
        private float _flare;

        private readonly Collider[] _overlap = new Collider[8];

        private Transform _disc;
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _properties;
        private ITimeService _time;
        private IAudioService _audio;

        private HazardTicker _ticker;
        private Vector3 _center;
        private float _radius;
        private float _damagePerTick;

        /// <summary>一次构建圆盘视觉，与区域内其他对象共享材质。</summary>
        /// <param name="material">所有区域染色并复用的透明材质。</param>
        public void Construct(Material material)
        {
            // 携带焦痕纹理、平铺在地面的四边形。过去是纯色薄圆柱，看起来像摆在地面的标记，
            // 而非被砸击破坏的地面。
            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Quad);
            disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            disc.name = "Disc";

            // 原始体的胶囊碰撞体会拦截攻击和移动；危险区域通过重叠查询自行处理触发，
            // 所以视觉对象不携带碰撞体。
            Destroy(disc.GetComponent<Collider>());

            disc.transform.SetParent(transform, worldPositionStays: false);

            _renderer = disc.GetComponent<MeshRenderer>();
            _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            // 逐区域设置颜色而不创建逐区域材质实例，使多个危险区域共享透明材质，同时独立消退。
            _properties = new MaterialPropertyBlock();

            _disc = disc.transform;

            gameObject.SetActive(false);
        }

        /// <summary>在指定位置点亮区域并开始生命周期。</summary>
        /// <param name="center">位于地面高度的世界空间中心。</param>
        /// <param name="radius">危险区域半径。</param>
        /// <param name="damagePerTick">每跳对区域内目标造成的伤害。</param>
        /// <param name="durationSeconds">区域保持危险的时长。</param>
        public void Activate(Vector3 center, float radius, float damagePerTick, float durationSeconds)
        {
            _center = center;
            _radius = Mathf.Max(0.1f, radius);
            _damagePerTick = Mathf.Max(0f, damagePerTick);
            _ticker = new HazardTicker(WarningSeconds, Mathf.Max(0.1f, durationSeconds), FadeSeconds, DamageIntervalSeconds);

            transform.position = new Vector3(center.x, GameplayConstants.GroundPlaneHeight + DiscLift, center.z);

            // Unity 圆柱直径为一个单位、高为两个单位，因此水平缩放使用完整直径，
            // 竖直缩放使用厚度的一半。
            if (_disc != null)
            {
                // 痕迹略超出伤害半径，使裂纹和淡化边缘位于危险区域之外。
                _disc.localScale = new Vector3(_radius * ScarReach, _radius * ScarReach, 1f);
            }

            gameObject.SetActive(true);
            ApplyVisual(0f);

            // 爆发瞬间播放低沉轰鸣，让身后形成的危险区域既可见也可闻。
            _audio ??= ServiceRegistry.Current != null &&
                       ServiceRegistry.Current.TryGet(out IAudioService audio)
                ? audio
                : null;
            _audio?.PlayCue(HazardCueId, center);
        }

        private void Update()
        {
            if (_ticker == null)
            {
                return;
            }

            _time ??= ServiceRegistry.Current != null &&
                      ServiceRegistry.Current.TryGet(out ITimeService time)
                ? time
                : null;

            float deltaTime = _time?.DeltaTime ?? Time.deltaTime;

            // 每次烧伤目标都会让痕迹闪亮，并在目标位置嘶响。过去地面悄悄扣除生命，像凭空受伤；
            // 现在每跳都能看见、听见是地面造成的伤害。
            if (_ticker.Advance(deltaTime) && DamageOccupants(out Vector3 burned))
            {
                _flare = FlareSeconds;
                _audio?.PlayCue(BurnCueId, burned);
            }

            _flare = Mathf.Max(0f, _flare - deltaTime);
            ApplyVisual(_ticker.VisualPresence);

            if (_ticker.IsExpired)
            {
                _ticker = null;
                gameObject.SetActive(false);
            }
        }

        /// <summary>对区域内每个玩家受击盒应用一跳伤害。</summary>
        /// <param name="burned">最后被烧伤者站立的位置。</param>
        /// <returns>有人被烧伤时为 true。</returns>
        private bool DamageOccupants(out Vector3 burned)
        {
            burned = _center;
            bool any = false;

            int count = Physics.OverlapSphereNonAlloc(
                _center, _radius, _overlap, Layers.BossAttackMask, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                Hurtbox hurtbox = _overlap[i].GetComponentInParent<Hurtbox>();

                if (hurtbox == null)
                {
                    continue;
                }

                Vector3 outward = hurtbox.transform.position - _center;
                outward.y = 0f;
                outward = outward.sqrMagnitude > Mathf.Epsilon ? outward.normalized : Vector3.forward;

                hurtbox.Receive(new DamageInfo
                {
                    Amount = _damagePerTick,

                    // 地面火焰不是轻击。正确标注类型，避免后续根据来袭类型分支的音频、
                    // 受击反应和学习系统，把危险区域伤害跳误当成首领发动的攻击。
                    Type = DamageType.Hazard,
                    SourceTeam = CombatantTeam.Boss,
                    SourceInstanceId = GetInstanceID(),
                    HitPoint = hurtbox.transform.position,
                    HitDirection = outward,
                    KnockbackSpeed = HazardKnockback,
                    PoiseDamage = 0f,
                    Stagger = StaggerStrength.None,
                    HitStopSeconds = 0f,
                    IgnoresInvulnerability = true
                });

                any = true;
                burned = hurtbox.transform.position;
            }

            return any;
        }

        /// <summary>让圆盘不透明度随区域存在程度淡变。</summary>
        private void ApplyVisual(float presence)
        {
            if (_renderer == null)
            {
                return;
            }

            float flare = _flare / FlareSeconds;
            Color color = HazardColor * (1f + FlareBoost * flare);
            color.a = Mathf.Clamp01(presence * MaxAlpha + flare);

            _properties.SetColor(BaseColorId, color);
            _renderer.SetPropertyBlock(_properties);
        }
    }
}
