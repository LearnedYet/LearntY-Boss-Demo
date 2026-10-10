using AdaptiveBossArena.Core.Services;
using UnityEngine;

namespace AdaptiveBossArena.Combat.Feel
{
    /// <summary>
    /// 一组固定、可复用的命中爆发效果，按轮转方式分配。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 每次命中都实例化效果，会在连击期间每秒分配和销毁多个对象，
    /// 这正是游戏最需要流畅时发生卡顿的典型来源。每个爆发效果在启动时只构建一次，随后复用。
    /// </para>
    /// <para>
    /// 采用轮转分配而不是寻找首个空闲项。池大小明显大于同时重叠命中数量时，
    /// 最旧条目总是最安全的复用对象，并且避免在战斗中途执行的路径上扫描。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ImpactBurstPool : MonoBehaviour
    {
        /// <summary>
        /// 粒子使用的着色器。
        /// </summary>
        /// <remarks>
        /// 采用加法混合无光照，让火花呈现为光而不是彩色纸片，并从后处理体积获得泛光。
        /// </remarks>
        private const string ParticleShaderName = "Universal Render Pipeline/Particles/Unlit";

        /// <summary>渲染管线粒子着色器不可用的少见情况下使用的回退项。</summary>
        private const string FallbackShaderName = "Sprites/Default";

        private const uint ScatterSeed = 0x51A7u;

        [SerializeField]
        [Range(4, 32)]
        [Tooltip("How many impacts may be visible at once. Beyond this the oldest is recycled.")]
        private int _capacity = 12;

        private ImpactBurst[] _bursts;
        private int _next;

        /// <summary>
        /// 围绕绘制火花的材质构建池。
        /// </summary>
        /// <remarks>
        /// 材质由场景引用的资源传入，而不是在这里通过名称查找着色器后创建。
        /// 构建只包含被其中某个对象引用的着色器，因此按名称查找的着色器可能被构建剔除。
        /// 这里只保留创建材质的回退方式，且使用时会明确报告。
        /// </remarks>
        /// <param name="material">火花材质，通常为生成的资源。</param>
        /// <param name="matterMaterial">绘制鲜血与灰尘的透明度混合材质。</param>
        public void Construct(Material material, Material matterMaterial = null)
        {
            if (material == null)
            {
                Debug.LogWarning(
                    "[Adaptive Boss Arena] Impact bursts were given no material and built one at runtime. " +
                    "Its shader may be stripped from a build; re-run the scene setup.");

                material = CreateParticleMaterial();
            }

            int capacity = EffectBudget.IsReduced ? Mathf.Min(_capacity, EffectBudget.WebImpactBursts) : _capacity;
            _bursts = new ImpactBurst[capacity];

            // 使用独立的带种子生成器：散射只影响外观，因此不能使用决定首领决策的生成器，
            // 全局生成器也禁止使用。
            var random = new XorShiftRandomProvider(ScatterSeed);

            for (int i = 0; i < capacity; i++)
            {
                var burstObject = new GameObject($"ImpactBurst_{i}");
                burstObject.transform.SetParent(transform, worldPositionStays: false);

                _bursts[i] = burstObject.AddComponent<ImpactBurst>();
                _bursts[i].Construct(material, matterMaterial, random);
            }
        }

        /// <summary>在世界某点播放命中效果。</summary>
        /// <param name="position">命中落点。</param>
        /// <param name="direction">火花抛出的方向。</param>
        /// <param name="flavour">命中效果的种类。</param>
        public void Play(Vector3 position, Vector3 direction, ImpactFlavour flavour)
        {
            if (_bursts == null || _bursts.Length == 0)
            {
                return;
            }

            _bursts[_next].Play(position, direction, flavour);
            _next = (_next + 1) % _bursts.Length;
        }

        /// <summary>构建所有爆发效果共用的唯一材质。</summary>
        private static Material CreateParticleMaterial()
        {
            Shader shader = Shader.Find(ParticleShaderName) ?? Shader.Find(FallbackShaderName);

            var material = new Material(shader)
            {
                name = "ImpactSparks (generated)",
                hideFlags = HideFlags.HideAndDontSave
            };

            // 通过属性字符串命名，而不是使用关键字辅助方法，因为上方两个着色器具有的属性不同；
            // 设置不存在的属性不会生效，也不会报错，因此能够覆盖两者。
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 1f);
            material.SetFloat("_ZWrite", 0f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            material.EnableKeyword("_EMISSION");

            return material;
        }
    }
}
