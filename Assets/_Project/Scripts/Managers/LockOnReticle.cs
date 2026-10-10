using UnityEngine;
using UnityEngine.UI;

namespace AdaptiveBossArena.Game
{
    /// <summary>
    /// 相机锁定巨兽时，用小圆环标记它。
    /// </summary>
    /// <remarks>
    /// 锁定会改变相机与骑士行为：视角追踪巨兽，骑士面向它并侧移，因此必须一眼看出是否锁定，
    /// 而非从相机移动方式推测。这里只负责表现，读取相机状态，绝不修改它。
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class LockOnReticle : MonoBehaviour
    {
        /// <summary>圆环位于巨兽胸口高度，那里本来就是视线关注点。</summary>
        private const float ChestHeight = 1.9f;

        private const int RingTextureSize = 64;

        [SerializeField]
        [Tooltip("The camera whose lock-on this shows.")]
        private ArenaCameraRig _rig;

        [SerializeField]
        [Tooltip("What the camera locks on to.")]
        private Transform _target;

        [SerializeField]
        [Tooltip("The ring drawn over the target.")]
        private RectTransform _marker;

        private Canvas _canvas;
        private RectTransform _canvasRect;

        /// <summary>分配相机、目标和圆环，供场景生成器使用。</summary>
        /// <param name="rig">相机装置。</param>
        /// <param name="target">巨兽。</param>
        /// <param name="marker">HUD 画布下圆环的矩形变换。</param>
        public void Bind(ArenaCameraRig rig, Transform target, RectTransform marker)
        {
            _rig = rig;
            _target = target;
            _marker = marker;
        }

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
            _canvasRect = _canvas != null ? _canvas.transform as RectTransform : null;

            // 与这里其他资源一样，圆环由代码绘制，而非导入。
            if (_marker != null && _marker.TryGetComponent(out Image image))
            {
                image.sprite = RingSprite();
                image.raycastTarget = false;
            }
        }

        private void LateUpdate()
        {
            if (_marker == null)
            {
                return;
            }

            Camera view = Camera.main;
            bool show = _rig != null && _target != null && _rig.IsLockedOn && view != null && _canvasRect != null;

            Vector3 screen = show ? view.WorldToScreenPoint(_target.position + Vector3.up * ChestHeight) : Vector3.zero;

            // 相机后方目标投影会在屏幕上镜像反转；此时隐藏圆环，避免指向错误方向。
            show &= screen.z > 0f;

            if (_marker.gameObject.activeSelf != show)
            {
                _marker.gameObject.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            Camera canvasCamera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, canvasCamera, out Vector2 local))
            {
                _marker.anchoredPosition = local;
            }
        }

        /// <summary>边缘柔和的细亮圆环。</summary>
        private static Sprite RingSprite()
        {
            var texture = new Texture2D(RingTextureSize, RingTextureSize, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                name = "LockOnRing"
            };

            float centre = (RingTextureSize - 1) * 0.5f;
            float outer = centre;
            float inner = centre * 0.72f;

            for (int y = 0; y < RingTextureSize; y++)
            {
                for (int x = 0; x < RingTextureSize; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), new Vector2(centre, centre));
                    float alpha = Mathf.Clamp01(Mathf.Min(outer - distance, distance - inner));
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();

            return Sprite.Create(
                texture, new Rect(0f, 0f, RingTextureSize, RingTextureSize), new Vector2(0.5f, 0.5f));
        }
    }
}
