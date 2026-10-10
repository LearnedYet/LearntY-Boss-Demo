using AdaptiveBossArena.Core.Combat;
using AdaptiveBossArena.Core.Services;
using UnityEngine;

namespace AdaptiveBossArena.Combat.Feel
{
    /// <summary>
    /// 战斗参与者受击时，短暂给其渲染器染色。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是确认命中已生效的最低成本、可读方式。伤害数字和生命条都要求玩家将视线移开战斗；
    /// 模型自身的闪光能由余光读取，这也是快速交锋中唯一有效的反馈方式。
    /// </para>
    /// <para>
    /// 使用 <see cref="MaterialPropertyBlock"/>，而不是赋值给<c>Renderer.material</c>。
    /// 后者首次访问时会悄然实例化材质副本，每个战斗参与者泄漏一份，并破坏所有共用材质对象的批处理。
    /// </para>
    /// <para>
    /// 使用未缩放时间驱动，让闪光在伴随的顿帧中继续播放。
    /// </para>
    /// <para>
    /// 乘向血红色，而不是混合到白色。有纹理身体的基础色本来就是白色，因此混合到白色不会改变皮肤，
    /// 只会将纯色部件——盾牌、刀刃——变成苍白卡片。相乘会同样压暗所有部位。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class HitFlash : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int FallbackColorId = Shader.PropertyToID("_Color");

        [SerializeField]
        [Tooltip("Colour every part is multiplied by at peak flash.")]
        private Color _flashTint = new Color(1f, 0.4f, 0.34f);

        [SerializeField]
        [Range(0.02f, 0.4f)]
        [Tooltip("Flash duration. Short enough not to obscure the character during a combo.")]
        private float _durationSeconds = 0.09f;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("How far toward the flash colour the model travels at peak.")]
        private float _intensity = 0.85f;

        private Renderer[] _renderers;
        private Color[] _baseColors;
        private MaterialPropertyBlock _propertyBlock;
        private float _remaining;

        private void Awake()
        {
            // 只处理身体与手臂：若通过同一属性给轨迹或粒子系统染色，会失去它们自己的颜色和淡出效果。
            var bodies = new System.Collections.Generic.List<Renderer>();

            foreach (Renderer candidate in GetComponentsInChildren<Renderer>())
            {
                if (candidate is MeshRenderer || candidate is SkinnedMeshRenderer)
                {
                    bodies.Add(candidate);
                }
            }

            _renderers = bodies.ToArray();
            _propertyBlock = new MaterialPropertyBlock();
            _baseColors = new Color[_renderers.Length];

            for (int i = 0; i < _renderers.Length; i++)
            {
                _baseColors[i] = ReadBaseColor(_renderers[i]);
            }
        }

        private void LateUpdate()
        {
            if (_remaining <= 0f)
            {
                return;
            }

            _remaining -= Time.unscaledDeltaTime;

            // 从完整强度逐渐减为零，让闪光呈现命中感，而不是突然关闭的开关。
            float strength = Mathf.Clamp01(_remaining / _durationSeconds) * _intensity;
            ApplyTint(strength);
        }

        /// <summary>开始闪光，已经进行时也可以安全再次调用。</summary>
        /// <remarks>
        /// 在闪光开始的唯一入口遵循减少闪烁的无障碍设置，让下游无需了解它。
        /// </remarks>
        public void Play()
        {
            if (FeedbackSettings.ReducedFlashing)
            {
                return;
            }

            _remaining = _durationSeconds;
        }

        /// <summary>仅为实际造成伤害的命中开始闪光。</summary>
        /// <param name="result">命中结果。</param>
        public void PlayIfDamaged(in DamageResult result)
        {
            if (result.Connected)
            {
                Play();
            }
        }

        /// <summary>将所有渲染器混合到闪光颜色。</summary>
        private void ApplyTint(float strength)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer target = _renderers[i];

                if (target == null)
                {
                    continue;
                }

                target.GetPropertyBlock(_propertyBlock);

                Color tinted = _baseColors[i] * Color.Lerp(Color.white, _flashTint, strength);
                tinted.a = _baseColors[i].a;
                _propertyBlock.SetColor(BaseColorId, tinted);
                _propertyBlock.SetColor(FallbackColorId, tinted);

                target.SetPropertyBlock(_propertyBlock);
            }
        }

        /// <summary>读取渲染器未染色的颜色，兼容两种管线的属性名称。</summary>
        private static Color ReadBaseColor(Renderer target)
        {
            Material material = target != null ? target.sharedMaterial : null;

            if (material == null)
            {
                return Color.white;
            }

            if (material.HasProperty(BaseColorId))
            {
                return material.GetColor(BaseColorId);
            }

            return material.HasProperty(FallbackColorId) ? material.GetColor(FallbackColorId) : Color.white;
        }
    }
}
