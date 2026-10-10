using AdaptiveBossArena.Core.Services;
using UnityEngine;

namespace AdaptiveBossArena.Combat.Feel
{
    /// <summary>
    /// 在打击落点留下地面血迹，一段时间后淡出。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 打击喷出鲜血，地面却保持干净，使长战斗不留任何痕迹。停留一段时间再淡出的血迹，
    /// 让地面记录交锋发生的位置。
    /// </para>
    /// <para>
    /// 固定数量的平面四边形形成环形池，优先复用最旧项，确保长战斗不会分配内存，
    /// 且不会累积超过 <see cref="Capacity"/> 个血迹。每个沿产生它的打击拉伸。按游戏时间淡出，
    /// 因此暂停或顿帧会保持它。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BloodDecalPool : MonoBehaviour
    {
        /// <summary>地面同时存在的最多血迹数量。</summary>
        public const int Capacity = 24;

        /// <summary>浏览器地面同时存在的最多血迹数量，每个都会给较弱 GPU 带来过度绘制成本。</summary>
        public const int WebCapacity = 12;

        /// <summary>血迹开始淡出前维持完整强度的时间，单位为秒。</summary>
        public const float HoldSeconds = 16f;

        /// <summary>血迹完全淡出的时间，单位为秒。</summary>
        public const float FadeSeconds = 6f;

        /// <summary>高于地面的高度：位于旧污迹之上、地面痕迹和预警之下。</summary>
        private const float Lift = 0.011f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private MeshRenderer[] _renderers = new MeshRenderer[0];
        private float[] _placedAt = new float[0];
        private MaterialPropertyBlock _block;
        private IRandomProvider _random;
        private float _clock;
        private int _next;

        /// <summary>当前地面上的血迹数量。</summary>
        public int VisibleCount
        {
            get
            {
                int count = 0;

                foreach (MeshRenderer renderer in _renderers)
                {
                    count += renderer != null && renderer.gameObject.activeSelf ? 1 : 0;
                }

                return count;
            }
        }

        /// <summary>血迹在某个存活时间的不透明度。</summary>
        /// <param name="ageSeconds">放置以来的秒数。</param>
        /// <returns>新血迹为一，在淡出过程中缓动到零。</returns>
        public static float AlphaAt(float ageSeconds)
        {
            if (ageSeconds <= HoldSeconds)
            {
                return 1f;
            }

            float t = Mathf.Clamp01((ageSeconds - HoldSeconds) / FadeSeconds);
            return 1f - t * t;
        }

        /// <summary>构建池。</summary>
        /// <param name="material">透明度混合的血迹材质。</param>
        /// <param name="random">各血迹旋转和大小的来源，避免血迹使用全局生成器。</param>
        public void Construct(Material material, IRandomProvider random)
        {
            _random = random;
            _block = new MaterialPropertyBlock();
            int capacity = EffectBudget.IsReduced ? WebCapacity : Capacity;
            _renderers = new MeshRenderer[capacity];
            _placedAt = new float[capacity];

            for (int i = 0; i < capacity; i++)
            {
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = $"Blood_{i:D2}";

                // 仅用于视觉：任何对象都不应与污迹碰撞。
                Destroy(quad.GetComponent<Collider>());

                quad.transform.SetParent(transform, worldPositionStays: false);

                var renderer = quad.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                _renderers[i] = renderer;
                quad.SetActive(false);
            }
        }

        /// <summary>在打击下方地面留下血迹。</summary>
        /// <param name="position">打击落点，只使用其地面位置。</param>
        /// <param name="direction">打击方向，血迹沿此方向溅出。</param>
        /// <param name="size">大致尺寸，单位为米。</param>
        public void Place(Vector3 position, Vector3 direction, float size)
        {
            if (_renderers.Length == 0 || _random == null)
            {
                return;
            }

            MeshRenderer renderer = _renderers[_next];
            _placedAt[_next] = _clock;
            _next = (_next + 1) % _renderers.Length;

            var along = new Vector3(direction.x, 0f, direction.z);
            along = along.sqrMagnitude > 1e-6f ? along.normalized : Quaternion.Euler(0f, _random.NextFloat(0f, 360f), 0f) * Vector3.forward;

            // 四边形沿 -z 朝向；放平后，纹理上方沿打击方向，因此液滴随打击飞行。
            Quaternion flat = Quaternion.LookRotation(Vector3.down, along);
            float scale = size * _random.NextFloat(0.75f, 1.25f);

            Transform quad = renderer.transform;
            quad.SetPositionAndRotation(new Vector3(position.x, Lift, position.z) + along * (scale * 0.25f), flat);
            quad.localScale = new Vector3(scale * _random.NextFloat(0.7f, 1f), scale * 1.4f, 1f);

            renderer.gameObject.SetActive(true);
            SetAlpha(renderer, 1f);
        }

        private void Update()
        {
            _clock += Time.deltaTime;

            for (int i = 0; i < _renderers.Length; i++)
            {
                MeshRenderer renderer = _renderers[i];

                if (renderer == null || !renderer.gameObject.activeSelf)
                {
                    continue;
                }

                float age = _clock - _placedAt[i];

                // 血迹仍新鲜时无需写入：放置时已设为完整强度。
                if (age <= HoldSeconds)
                {
                    continue;
                }

                float alpha = AlphaAt(age);

                if (alpha <= 0f)
                {
                    renderer.gameObject.SetActive(false);
                    continue;
                }

                SetAlpha(renderer, alpha);
            }
        }

        private void SetAlpha(MeshRenderer renderer, float alpha)
        {
            renderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, new Color(1f, 1f, 1f, alpha));
            renderer.SetPropertyBlock(_block);
        }
    }
}
