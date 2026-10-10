using AdaptiveBossArena.Core.Services;
using UnityEngine;

namespace AdaptiveBossArena.Combat.Feel
{
    /// <summary>命中的性质，决定其外观。</summary>
    /// <remarks>
    /// 刻意保持种类较少。交锋中每种效果必须一眼可辨，超过五六种后便不再表现为不同事物，而开始显得杂乱。
    /// </remarks>
    public enum ImpactFlavour
    {
        /// <summary>轻击命中身体，短促血液喷溅，锁甲上溅出少量火花。</summary>
        Light = 0,

        /// <summary>重击命中身体，更多鲜血飞得更远，并产生闪光。</summary>
        Heavy = 1,

        /// <summary>成功弹反，明亮钢铁火花以紧密锥形迸出，没有鲜血。</summary>
        Deflect = 2,

        /// <summary>延迟格挡，暗淡且分散，让它明显逊于弹反。</summary>
        Block = 3,

        /// <summary>防御被打破，游戏中最大的爆发效果，因为这是最重要的时刻。</summary>
        PostureBreak = 4,

        /// <summary>身体或刀刃撞上石头，产生砂砾与灰尘，没有鲜血。</summary>
        Stone = 5
    }

    /// <summary>
    /// 一个可复用的命中效果：钢铁火花、血液喷溅和扬尘，并带有闪光。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与项目其他资源一样，完全由代码构建。在检查器中创作的粒子系统需要以 YAML 提交，
    /// 而生成器方案正是为避免这一点而存在。
    /// </para>
    /// <para>
    /// 使用三个系统而不是一个，因为它们需要不同混合方式。火花是光，会添加到画面上；鲜血和灰尘是物质，
    /// 会覆盖画面。单个加法混合系统将每次命中绘制为发光橙色条纹组成的扇面，这让命中身体看起来像玩具。
    /// </para>
    /// <para>
    /// 手动发射，而不是按速率或爆发配置驱动：系统始终播放，但没有自动发射内容，
    /// <see cref="Play"/> 直接调用 <c>Emit</c>。每次命中重启粒子系统明显更昂贵，
    /// 且会丢弃前次命中仍存活的粒子，这在连击中恰好是错误行为。
    /// </para>
    /// <para>
    /// 使用未缩放时间，让爆发效果在伴随的顿帧中继续运动。
    /// 随世界冻结的火花会消除顿帧本来要提供的强调作用。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ImpactBurst : MonoBehaviour
    {
        /// <summary>灯光闪亮的持续时间，比粒子短，以形成强调。</summary>
        private const float FlashSeconds = 0.08f;

        private ParticleSystem _sparks;
        private ParticleSystem _blood;
        private ParticleSystem _dust;
        private Light _flash;
        private IRandomProvider _random;
        private float _flashRemaining;
        private float _flashIntensity;

        /// <summary>此爆发效果没有任何部分仍可见时为 true。</summary>
        public bool IsIdle =>
            _flashRemaining <= 0f && _sparks.particleCount == 0 && _blood.particleCount == 0 && _dust.particleCount == 0;

        /// <summary>构建粒子系统和灯光，由所属池调用。</summary>
        /// <param name="sparkMaterial">绘制火花的加法混合材质。</param>
        /// <param name="matterMaterial">绘制鲜血和灰尘的透明度混合材质；为 null 则使用火花材质。</param>
        /// <param name="random">散射来源，避免爆发效果使用全局随机生成器。</param>
        public void Construct(Material sparkMaterial, Material matterMaterial, IRandomProvider random)
        {
            _random = random;
            matterMaterial = matterMaterial != null ? matterMaterial : sparkMaterial;

            // 细、热、快速且会在地面弹跳：火花是钢铁碎屑，不是余烬。
            _sparks = CreateSystem("Sparks", sparkMaterial, maxParticles: 48, gravity: 1.4f, lifetime: 0.35f);
            var sparkRenderer = _sparks.GetComponent<ParticleSystemRenderer>();
            sparkRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            sparkRenderer.velocityScale = 0.012f;
            sparkRenderer.lengthScale = 1f;

            ParticleSystem.CollisionModule collision = _sparks.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Low;
            collision.bounce = 0.35f;
            collision.dampen = 0.45f;
            collision.lifetimeLoss = 0.25f;

            // 沉重液滴快速画弧下落；沿飞行方向略微拉伸可表现液体感。
            _blood = CreateSystem("Blood", matterMaterial, maxParticles: 64, gravity: 2.6f, lifetime: 0.55f);
            var bloodRenderer = _blood.GetComponent<ParticleSystemRenderer>();
            bloodRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            bloodRenderer.velocityScale = 0.02f;
            bloodRenderer.lengthScale = 1.2f;

            ParticleSystem.LimitVelocityOverLifetimeModule bloodDrag = _blood.limitVelocityOverLifetime;
            bloodDrag.enabled = true;
            bloodDrag.drag = 1.5f;

            // 缓慢、扩散、淡薄：灰尘在打击后悬浮，而不是随打击飞行。
            _dust = CreateSystem("Dust", matterMaterial, maxParticles: 24, gravity: -0.05f, lifetime: 1.4f);
            _dust.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.Billboard;

            ParticleSystem.SizeOverLifetimeModule dustSize = _dust.sizeOverLifetime;
            dustSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1f));

            ParticleSystem.LimitVelocityOverLifetimeModule dustDrag = _dust.limitVelocityOverLifetime;
            dustDrag.enabled = true;
            dustDrag.drag = 4f;

            ParticleSystem.RotationOverLifetimeModule dustSpin = _dust.rotationOverLifetime;
            dustSpin.enabled = true;
            dustSpin.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

            // 浏览器中不使用闪光灯：每次命中的逐像素灯光正是集成显卡无法承受的成本，
            // 而火花的泛光仍可表现闪亮。
            if (!EffectBudget.IsReduced)
            {
                _flash = gameObject.AddComponent<Light>();
                _flash.type = LightType.Point;
                _flash.range = 5f;
                _flash.shadows = LightShadows.None;
                _flash.enabled = false;
            }
        }

        /// <summary>在一点发射爆发效果，沿指定方向抛出。</summary>
        /// <param name="position">命中落点。</param>
        /// <param name="direction">爆发效果抛出的方向。</param>
        /// <param name="flavour">命中效果的种类。</param>
        public void Play(Vector3 position, Vector3 direction, ImpactFlavour flavour)
        {
            transform.position = position;

            BurstProfile profile = ProfileFor(flavour);
            Vector3 axis = direction.sqrMagnitude > Mathf.Epsilon ? direction.normalized : Vector3.up;

            Emit(_sparks, position, axis, profile.Sparks, SparkColour, speedJitter: 0.5f);
            Emit(_blood, position, axis, profile.Blood, BloodColour, speedJitter: 0.45f);
            Emit(_dust, position, axis, profile.Dust, DustColour, speedJitter: 0.6f);

            if (_flash == null)
            {
                return;
            }

            _flashIntensity = profile.FlashIntensity;
            _flashRemaining = FlashSeconds;
            _flash.color = profile.FlashColour;
            _flash.enabled = _flashIntensity > 0f;
        }

        private void Update()
        {
            if (_flashRemaining <= 0f)
            {
                return;
            }

            // 与粒子相同，使用未缩放时间：闪光用来标记命中的帧，而该帧通常处于冻结状态。
            _flashRemaining -= Time.unscaledDeltaTime;

            if (_flashRemaining <= 0f)
            {
                _flash.enabled = false;
                return;
            }

            _flash.intensity = _flashIntensity * (_flashRemaining / FlashSeconds);
        }

        private static readonly Color32 SparkColour = new Color32(255, 214, 160, 255);
        private static readonly Color32 BloodColour = new Color32(58, 4, 5, 255);
        private static readonly Color32 DustColour = new Color32(92, 84, 74, 70);

        private ParticleSystem CreateSystem(string systemName, Material material, int maxParticles, float gravity, float lifetime)
        {
            var child = new GameObject(systemName);
            child.transform.SetParent(transform, worldPositionStays: false);

            ParticleSystem system = child.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.gravityModifier = gravity;
            main.startLifetime = lifetime;

            // 发射和形状模块都禁用：爆发效果的一切都在播放瞬间由发射参数决定，而不是由配置决定。
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;

            // 粒子临终时缩小并淡出，而不是突然消失，这正是火花与彩纸碎屑的主要区别。
            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(FadeOutGradient());

            var renderer = child.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            system.Play();

            return system;
        }

        private void Emit(ParticleSystem system, Vector3 position, Vector3 axis, Spray spray, Color32 colour, float speedJitter)
        {
            var parameters = new ParticleSystem.EmitParams { position = position, applyShapeToPosition = false };

            for (int i = 0; i < spray.Count; i++)
            {
                // 围绕命中方向散射，而不是在球面发射。沿打击方向继续飞行的喷溅，
                // 让命中看起来来自某个方向。
                Vector3 scatter = new Vector3(
                    _random.NextFloat(-1f, 1f), _random.NextFloat(-1f, 1f), _random.NextFloat(-1f, 1f)) * spray.Spread;

                parameters.velocity = (axis + scatter).normalized * spray.Speed *
                                      _random.NextFloat(1f - speedJitter, 1f + speedJitter);
                parameters.startSize = spray.Size * _random.NextFloat(0.6f, 1.4f);
                parameters.startLifetime = system.main.startLifetime.constant * _random.NextFloat(0.6f, 1.3f);
                parameters.rotation = _random.NextFloat(0f, 360f);
                parameters.startColor = colour;

                system.Emit(parameters, 1);
            }
        }

        /// <summary>一次爆发抛出多少某种物质，以及抛出方式。</summary>
        private readonly struct Spray
        {
            public Spray(int count, float speed, float size, float spread)
            {
                Count = count;
                Speed = speed;
                Size = size;
                Spread = spread;
            }

            public int Count { get; }
            public float Speed { get; }
            public float Size { get; }
            public float Spread { get; }
        }

        /// <summary>区分各类命中效果的全部信息。</summary>
        private readonly struct BurstProfile
        {
            public BurstProfile(Spray sparks, Spray blood, Spray dust, float flashIntensity, Color flashColour)
            {
                Sparks = sparks;
                Blood = blood;
                Dust = dust;
                FlashIntensity = flashIntensity;
                FlashColour = flashColour;
            }

            public Spray Sparks { get; }
            public Spray Blood { get; }
            public Spray Dust { get; }
            public float FlashIntensity { get; }
            public Color FlashColour { get; }
        }

        private static readonly Spray None = new Spray(0, 0f, 0f, 0f);

        /// <summary>
        /// 各类效果的外观。
        /// </summary>
        /// <remarks>
        /// 调参目标是不读任何数值也能辨认结果：鲜血代表打击命中身体，只有火花代表钢铁相撞；
        /// 弹反的火花比格挡更多、更亮、更集中。玩家只用余光就能学会识别结果。
        /// </remarks>
        private static BurstProfile ProfileFor(ImpactFlavour flavour)
        {
            var warmFlash = new Color(1f, 0.72f, 0.45f);
            var steelFlash = new Color(0.85f, 0.9f, 1f);

            switch (flavour)
            {
                case ImpactFlavour.Heavy:
                    return new BurstProfile(
                        sparks: new Spray(6, 7f, 0.018f, 0.6f),
                        blood: new Spray(30, 4.5f, 0.09f, 0.55f),
                        dust: new Spray(3, 1.2f, 0.7f, 1f),
                        flashIntensity: 3f, flashColour: warmFlash);

                case ImpactFlavour.Deflect:
                    return new BurstProfile(
                        sparks: new Spray(36, 11f, 0.016f, 0.3f),
                        blood: None,
                        dust: None,
                        flashIntensity: 12f, flashColour: steelFlash);

                case ImpactFlavour.Block:
                    return new BurstProfile(
                        sparks: new Spray(10, 5f, 0.015f, 0.8f),
                        blood: None,
                        dust: new Spray(2, 0.8f, 0.5f, 1f),
                        flashIntensity: 2.5f, flashColour: steelFlash);

                case ImpactFlavour.PostureBreak:
                    return new BurstProfile(
                        sparks: new Spray(30, 12f, 0.02f, 0.9f),
                        blood: new Spray(24, 5f, 0.1f, 0.9f),
                        dust: new Spray(8, 2f, 1.1f, 1f),
                        flashIntensity: 14f, flashColour: warmFlash);

                case ImpactFlavour.Stone:
                    return new BurstProfile(
                        sparks: new Spray(8, 6f, 0.015f, 0.7f),
                        blood: None,
                        dust: new Spray(10, 2.2f, 1f, 1f),
                        flashIntensity: 0f, flashColour: warmFlash);

                default:
                    return new BurstProfile(
                        sparks: new Spray(3, 6f, 0.015f, 0.5f),
                        blood: new Spray(14, 3.5f, 0.07f, 0.45f),
                        dust: None,
                        flashIntensity: 1.2f, flashColour: warmFlash);
            }
        }

        /// <summary>短暂维持完全不透明后逐渐下降的透明度曲线。</summary>
        private static Gradient FadeOutGradient()
        {
            var gradient = new Gradient();

            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 0.45f),
                    new GradientAlphaKey(0f, 1f)
                });

            return gradient;
        }
    }
}
