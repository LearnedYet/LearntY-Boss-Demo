using UnityEngine;
using UnityEngine.Rendering;

namespace AdaptiveBossArena.Combat
{
    /// <summary>
    /// 固定容量、轮流发放的可复用地面危险区域池。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="Feel.ImpactBurstPool"/> 对应：每次攻击实例化区域会在战斗中分配内存，
    /// 因此所有区域启动时构建一次再回收复用。少量容量足以覆盖最后一搏阶段多个痕迹同时重叠的情况。
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class HazardField : MonoBehaviour, IHazardField
    {
        private const string ShaderName = "Universal Render Pipeline/Unlit";
        private const string FallbackShaderName = "Sprites/Default";

        /// <summary>
        /// 每次地面冲击留下痕迹时，携带中心与半径触发。
        /// </summary>
        /// <remarks>
        /// 表现层监听，在砸击落点破坏石材。首领侧任何系统都不能监听，因为该事件在冲击当帧触发，
        /// 没有感知延迟。
        /// </remarks>
        public event System.Action<Vector3, float> Spawned;

        [SerializeField]
        [Range(2, 16)]
        [Tooltip("How many hazards may be active at once. Beyond this the oldest is recycled.")]
        private int _capacity = 8;

        [SerializeField]
        [Tooltip("Material the hazard discs are drawn with. Assigned by the scene generator, so its shader ships.")]
        private Material _discMaterial;

        private HazardZone[] _zones;
        private int _next;

        /// <summary>绘制危险圆盘的材质。</summary>
        public Material DiscMaterial => _discMaterial;

        /// <summary>分配圆盘材质，供场景生成器使用。</summary>
        /// <param name="material">生成的危险区域材质。</param>
        public void SetMaterial(Material material) => _discMaterial = material;

        private void Awake()
        {
            // 参见 ImpactBurstPool.Construct：运行时按名称查找的着色器可能被构建剥离，
            // 因此优先使用生成资源，运行时创建只作为明确报告的回退。
            Material material = _discMaterial;

            if (material == null)
            {
                Debug.LogWarning(
                    "[Adaptive Boss Arena] Hazard discs have no material and built one at runtime. Its " +
                    "shader may be stripped from a build; re-run the scene setup.");

                material = CreateDiscMaterial();
            }

            _zones = new HazardZone[_capacity];

            for (int i = 0; i < _capacity; i++)
            {
                var zoneObject = new GameObject($"HazardZone_{i}");
                zoneObject.transform.SetParent(transform, worldPositionStays: false);

                _zones[i] = zoneObject.AddComponent<HazardZone>();
                _zones[i].Construct(material);
            }
        }

        /// <inheritdoc />
        public void Spawn(Vector3 center, float radius, float damagePerTick, float durationSeconds)
        {
            // 在对象池检查前宣告事件：没有危险圆盘的场景中，地面冲击仍应破坏石材。
            Spawned?.Invoke(center, radius);

            if (_zones == null || _zones.Length == 0)
            {
                return;
            }

            _zones[_next].Activate(center, radius, damagePerTick, durationSeconds);
            _next = (_next + 1) % _zones.Length;
        }

        /// <summary>构建所有圆盘共享的透明材质。</summary>
        private static Material CreateDiscMaterial()
        {
            Shader shader = Shader.Find(ShaderName) ?? Shader.Find(FallbackShaderName);

            var material = new Material(shader)
            {
                name = "HazardDisc (generated)",
                hideFlags = HideFlags.HideAndDontSave
            };

            // 采用透明混合而非叠加，使痕迹像让地面变暗的污渍，而非光源。防御性设置属性；
            // 缺少属性的着色器会忽略它。
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.renderQueue = (int)RenderQueue.Transparent;

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            return material;
        }
    }
}
