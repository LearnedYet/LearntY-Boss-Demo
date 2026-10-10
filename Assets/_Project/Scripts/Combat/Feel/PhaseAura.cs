using UnityEngine;

namespace AdaptiveBossArena.Combat.Feel
{
    /// <summary>
    /// 随战斗升级而增强的彩色辉光。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 基础几何体上的阶段变化原本不可见——无论首领沉稳还是绝望，胶囊体看起来都相同。
    /// 这里让首领从内部发光，开始时冷暗，每个阶段更炽热、更愤怒，让玩家在角色身上看见升级，
    /// 而不是只从受伤更重推断。
    /// </para>
    /// <para>
    /// 刻意采用真实灯光而不是材质染色：它不与任何材质颜色叠加（因此不会与受击闪光争夺基础色），
    /// 会照亮首领周围地面，并自然利用后处理体积的泛光。所有变化按未缩放时间缓动，
    /// 确保阶段转换脉冲即使发生于顿帧中仍可辨认。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PhaseAura : MonoBehaviour
    {
        /// <summary>辉光所在的局部高度，大致为首领重心。</summary>
        private const float LightHeight = 1.5f;

        /// <summary>辉光范围，单位为世界单位。</summary>
        private const float LightRange = 7f;

        /// <summary>稳定辉光向新阶段强度缓动的速度。</summary>
        private const float EaseHalfLife = 0.25f;

        /// <summary>阶段转换脉冲回落的速度。</summary>
        private const float PulseDecayPerSecond = 3.5f;

        /// <summary>阶段转换脉冲在稳定辉光之上添加的额外强度。</summary>
        private const float PulseIntensity = 7f;

        /// <summary>战术爆发蓄满时增加的额外强度，让首领蓄势清晰可见。</summary>
        private const float ChargeIntensity = 6f;

        /// <summary>招架窗口开启时的额外强度。</summary>
        private const float ParryIntensity = 5f;

        /// <summary>
        /// 招架窗口开启时，光环立即切换到的颜色。
        /// </summary>
        /// <remarks>
        /// 采用冷色，与首领其他所有炽热效果区分。区别必须在余光中仅看到六分之一秒时仍可辨认，
        /// 因此不能采用任何看起来只是普通辉光加亮的颜色。
        /// </remarks>
        private static readonly Color ParryColor = new Color(0.55f, 0.85f, 1.6f);

        private Light _light;
        private Color _targetColor = Color.black;
        private float _targetIntensity;
        private float _steadyIntensity;
        private float _pulse;
        private float _charge;
        private bool _parryWindowOpen;

        [SerializeField]
        [Tooltip("Additive material the embers rising off the boss are drawn with. Assigned by the prefab generator.")]
        private Material _emberMaterial;

        private ParticleSystem _embers;

        /// <summary>首领各阶段每秒散发的余烬数，从平静时没有余烬到最终阶段连续散发。</summary>
        private static readonly float[] EmbersPerSecondByPhase = { 0f, 4f, 10f, 20f };

        /// <summary>指定余烬材质，由预制体生成器使用。</summary>
        /// <param name="material">加法混合火花材质。</param>
        public void SetEmberMaterial(Material material) => _emberMaterial = material;

        private void Awake()
        {
            var lightObject = new GameObject("Aura");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = new Vector3(0f, LightHeight, 0f);

            _light = lightObject.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.range = LightRange;
            _light.shadows = LightShadows.None;
            _light.intensity = 0f;
            _light.enabled = false;

            BuildEmbers(lightObject.transform);
            SetPhase(0);
        }

        /// <summary>
        /// 构建随首领愤怒增强而从身上升起的余烬。
        /// </summary>
        /// <remarks>
        /// 单独灯光会照亮首领周围地面，却不在首领自身呈现任何效果。
        /// 从核心向上飘的余烬让每次升级在身体上可见，最终阶段连续喷出的余烬就是玩家要求的“光环”。
        /// 浏览器中减半，因为较弱图形硬件需要为每个粒子付出成本。
        /// </remarks>
        private void BuildEmbers(Transform parent)
        {
            if (_emberMaterial == null)
            {
                return;
            }

            var embersObject = new GameObject("Embers");
            embersObject.transform.SetParent(parent, false);

            _embers = embersObject.AddComponent<ParticleSystem>();
            _embers.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = _embers.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = 64;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.055f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.45f, 0.15f), new Color(1f, 0.25f, 0.08f));
            main.gravityModifier = -0.12f;

            ParticleSystem.EmissionModule emission = _embers.emission;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = _embers.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.45f;

            ParticleSystem.NoiseModule noise = _embers.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.8f;

            ParticleSystem.ColorOverLifetimeModule fade = _embers.colorOverLifetime;
            fade.enabled = true;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.4f, 0.2f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = embersObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _emberMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _embers.Play();
        }

        /// <summary>设置匹配阶段的稳定辉光，从零阶段的平静到最高阶段的狂怒。</summary>
        /// <param name="phaseIndex">从零开始的阶段索引。</param>
        public void SetPhase(int phaseIndex)
        {
            if (_embers != null)
            {
                float rate = EmbersPerSecondByPhase[Mathf.Clamp(phaseIndex, 0, EmbersPerSecondByPhase.Length - 1)];
                ParticleSystem.EmissionModule emission = _embers.emission;
                emission.rateOverTime = EffectBudget.IsReduced ? rate * 0.5f : rate;
            }

            switch (phaseIndex)
            {
                case 0:
                    _targetColor = Color.black;
                    _targetIntensity = 0f;
                    break;

                case 1:
                    _targetColor = new Color(1f, 0.5f, 0.2f);
                    _targetIntensity = 3f;
                    break;

                case 2:
                    _targetColor = new Color(1f, 0.2f, 0.15f);
                    _targetIntensity = 6f;
                    break;

                default:
                    // 最终抵抗及后续阶段：更炽热、曝光更强的深红色，
                    // 让绝望阶段比之前仅仅无情的阶段燃烧得明显更亮。
                    _targetColor = new Color(1f, 0.28f, 0.4f);
                    _targetIntensity = 8.5f;
                    break;
            }
        }

        /// <summary>短暂增强辉光，用于阶段转换或适应时刻。</summary>
        public void Pulse() => _pulse = PulseIntensity;

        /// <summary>
        /// 设置零到一的稳定蓄力辉光，让逐渐填满的战术爆发槽在首领身上可见。
        /// </summary>
        /// <remarks>
        /// 战术爆发的可读性。玩家看不到预兆的攻击与首领作弊无法区分，因此数值槽接近满值时，
        /// 首领会越来越亮，在爆发前清楚地表现蓄势。
        /// </remarks>
        /// <param name="normalized">战术爆发槽的填充程度，从零到一。</param>
        public void SetCharge(float normalized) => _charge = Mathf.Clamp01(normalized);

        /// <summary>
        /// 在首领身上显示或隐藏招架窗口。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 采用独立项而不是借用 <see cref="SetCharge"/>，因为后者由战术爆发槽控制；
        /// 借用它会让蓄力辉光错误表示数值槽的填充程度。
        /// </para>
        /// <para>
        /// 可读性义务是双向的。首领会得知玩家举起防御，但绝不知道玩家弹反窗口是否开启；
        /// 玩家的信息渠道是画面，因此首领的窗口必须在画面中可见，
        /// 否则会拒绝命中的姿态与随机拒绝命中的姿态无法区分。
        /// 只需显示窗口本身——可被惩罚的尾段看起来就是首领静止站立，实际也正是如此。
        /// </para>
        /// </remarks>
        /// <param name="isOpen">此刻到达的命中是否会被化解。</param>
        public void SetParryWindow(bool isOpen) => _parryWindowOpen = isOpen;

        /// <summary>将辉光清除并恢复到初始状态，用于重试。</summary>
        public void ResetAura()
        {
            _pulse = 0f;
            _charge = 0f;
            _parryWindowOpen = false;
            _steadyIntensity = 0f;
            SetPhase(0);

            if (_light != null)
            {
                _light.intensity = 0f;
                _light.color = _targetColor;
                _light.enabled = false;
            }
        }

        private void Update()
        {
            if (_light == null)
            {
                return;
            }

            float deltaTime = Time.unscaledDeltaTime;

            // 稳定辉光向阶段目标强度缓动；脉冲叠加在其上，独立回落。保持两者分离，
            // 确保阶段转换闪亮不会改变稳定辉光的目标。
            _pulse = Mathf.Max(0f, _pulse - PulseDecayPerSecond * deltaTime);
            _steadyIntensity = Mathf.Lerp(
                _steadyIntensity, _targetIntensity, DampFactor(EaseHalfLife, deltaTime));

            _light.intensity = _steadyIntensity + _pulse + _charge * ChargeIntensity +
                               (_parryWindowOpen ? ParryIntensity : 0f);

            // 立即切换，而不缓动。这里其他所有颜色变化都是缓慢升级，玩家有数秒察觉；
            // 这次变化必须在比缓动自身半衰期更短的窗口内可读，否则窗口关闭时，
            // 玩家看到的颜色才刚开始变化。
            _light.color = _parryWindowOpen
                ? ParryColor
                : Color.Lerp(_light.color, _targetColor, DampFactor(EaseHalfLife, deltaTime));

            // 完全暗下后关闭灯光，让初始阶段不为没有任何贡献的灯光付出成本。
            _light.enabled = _light.intensity > 0.01f;
        }

        /// <summary>按半衰期计算、不依赖帧率的插值系数。</summary>
        private static float DampFactor(float halfLifeSeconds, float deltaTime) =>
            1f - Mathf.Exp(-deltaTime / Mathf.Max(0.0001f, halfLifeSeconds));
    }
}
