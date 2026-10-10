using UnityEngine;

namespace AdaptiveBossArena.Combat.Feel
{
    /// <summary>
    /// 攻击前摇和挥击期间，在地面绘制命中体积。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 没有它，战斗不可见。命中盒是没有渲染器的查询体积，因此按攻击只会产生伤害数字，
    /// 除此之外什么都没有；玩家确实无法判断是否已经攻击、能触及多远，或者重攻击是否与轻攻击不同。
    /// 这不是润色问题，而是可玩游戏与无法读取的游戏之间的区别。
    /// </para>
    /// <para>
    /// 两个阶段刻意采用不同绘制方式。前摇用暗淡轮廓准确显示打击将落在何处，
    /// 作为首领预警和玩家瞄准辅助。生效窗口短暂明亮闪烁，确认挥击已经发生。
    /// 由于两者都根据攻击自身数值生成，视觉不会偏离命中盒。
    /// </para>
    /// <para>
    /// 按未缩放时间淡出，让闪光在自身触发的顿帧中仍可读。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class AttackVisualizer : MonoBehaviour
    {
        /// <summary>高于地面的高度，足以避免与地面深度冲突。</summary>
        private const float GroundOffset = 0.03f;

        /// <summary>危险（无法格挡）预警的脉冲频率，单位为每秒周期数。</summary>
        private const float PerilousPulseHz = 4f;

        /// <summary>危险预警毫无疑义的红色，表示“不要格挡”。</summary>
        private static readonly Color PerilousColor = new Color(1f, 0.14f, 0.11f);

        [SerializeField]
        [Tooltip("Transparent unlit material the overlay is drawn with. Assigned by the prefab generator.")]
        private Material _overlayMaterial;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Opacity of the wind-up telegraph. Low enough not to dominate, high enough to read.")]
        private float _telegraphAlpha = 0.3f;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Opacity at the moment the blow lands.")]
        private float _strikeAlpha = 0.55f;

        [SerializeField]
        [Range(0.05f, 0.6f)]
        [Tooltip("How long the strike flash takes to fade out.")]
        private float _strikeFadeSeconds = 0.22f;

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private MaterialPropertyBlock _propertyBlock;
        private Mesh _currentMesh;

        private Color _baseColor = Color.white;
        private float _alpha;
        private float _strikeFadeRemaining;
        private bool _isTelegraphing;
        private bool _perilous;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int FallbackColorId = Shader.PropertyToID("_Color");

        private void Awake()
        {
            _meshFilter = gameObject.AddComponent<MeshFilter>();
            _meshRenderer = gameObject.AddComponent<MeshRenderer>();
            _propertyBlock = new MaterialPropertyBlock();

            _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
            _meshRenderer.enabled = false;

            // 从序列化字段赋值，因为渲染器直到现在才存在，因此预制体生成器在构建时没有对象可写入材质。
            if (_overlayMaterial != null)
            {
                _meshRenderer.sharedMaterial = _overlayMaterial;
            }

            transform.localPosition = new Vector3(0f, GroundOffset, 0f);
        }

        private void LateUpdate()
        {
            // 危险前摇闪烁红色，让“闪避，不要格挡”的判断早于挥击到达。仅在预警期间使用，
            // 避免与下方打击闪光冲突。
            if (_isTelegraphing && _perilous && _strikeFadeRemaining <= 0f)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * PerilousPulseHz * Mathf.PI * 2f);
                _alpha = Mathf.Lerp(_telegraphAlpha, Mathf.Min(1f, _telegraphAlpha * 2.2f), pulse);
                ApplyColor();
            }

            if (_strikeFadeRemaining > 0f)
            {
                _strikeFadeRemaining -= Time.unscaledDeltaTime;

                float fraction = Mathf.Clamp01(_strikeFadeRemaining / _strikeFadeSeconds);
                _alpha = Mathf.Lerp(_isTelegraphing ? _telegraphAlpha : 0f, _strikeAlpha, fraction);

                ApplyColor();

                if (_strikeFadeRemaining <= 0f && !_isTelegraphing)
                {
                    _meshRenderer.enabled = false;
                }
            }
        }

        /// <summary>指定叠加材质，由预制体生成器使用。</summary>
        /// <param name="material">透明无光照材质，所有可视化器共用。</param>
        public void SetMaterial(Material material) => _overlayMaterial = material;

        /// <summary>显示攻击的前摇预警。</summary>
        /// <param name="attack">提供形状和颜色的攻击。</param>
        public void ShowTelegraph(AttackDefinition attack)
        {
            if (attack == null || !BuildMeshFor(attack))
            {
                return;
            }

            _perilous = attack.Unblockable;
            _baseColor = _perilous ? PerilousColor : attack.TelegraphColor;
            _isTelegraphing = true;
            _alpha = _telegraphAlpha;

            _meshRenderer.enabled = true;
            ApplyColor();
        }

        /// <summary>攻击开始生效的瞬间闪亮显示体积。</summary>
        /// <param name="attack">提供形状和颜色的攻击。</param>
        public void ShowStrike(AttackDefinition attack)
        {
            if (attack == null || !BuildMeshFor(attack))
            {
                return;
            }

            _baseColor = attack.Unblockable ? PerilousColor : attack.TelegraphColor;
            _isTelegraphing = false;
            _perilous = false;
            _alpha = _strikeAlpha;
            _strikeFadeRemaining = _strikeFadeSeconds;

            _meshRenderer.enabled = true;
            ApplyColor();
        }

        /// <summary>隐藏叠加效果，允许打击闪光完成淡出。</summary>
        public void Hide()
        {
            _isTelegraphing = false;

            if (_strikeFadeRemaining <= 0f)
            {
                _meshRenderer.enabled = false;
            }
        }

        /// <summary>攻击变化时重新构建网格，否则复用。</summary>
        private bool BuildMeshFor(AttackDefinition attack)
        {
            Mesh mesh = AttackShapeMesh.Build(attack);

            if (mesh == null)
            {
                return false;
            }

            // 上一个网格在运行时创建，不属于资源数据库，因此必须显式销毁，否则每次挥击都会泄漏一个。
            if (_currentMesh != null)
            {
                Destroy(_currentMesh);
            }

            _currentMesh = mesh;
            _meshFilter.sharedMesh = mesh;

            return true;
        }

        /// <summary>通过属性块推送当前颜色与不透明度。</summary>
        private void ApplyColor()
        {
            var color = new Color(_baseColor.r, _baseColor.g, _baseColor.b, _alpha);

            _meshRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorId, color);
            _propertyBlock.SetColor(FallbackColorId, color);
            _meshRenderer.SetPropertyBlock(_propertyBlock);
        }

        private void OnDestroy()
        {
            if (_currentMesh != null)
            {
                Destroy(_currentMesh);
            }
        }
    }
}
