using UnityEngine;

namespace AdaptiveBossArena.Combat.Feel
{
    /// <summary>
    /// 一组固定、可复用的物理石块，确保破坏物体不会产生分配或不断增长。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 按平台档位限制：WebGL 构建保留的活动身体远少于桌面版本。所有碎片都在使用时取回最旧的，
    /// 让充满砸击的长战斗保留最新、最明显的碎片，同时始终不超预算。
    /// </para>
    /// <para>
    /// 碎片休眠或超过稳定时间后会变为静态身体。地面充满清醒刚体时，
    /// 求解器每帧都为无人关注的碎片付出成本。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class DebrisPool : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Material the pieces are drawn with. Assigned by the scene generator, so its shader ships.")]
        private Material _material;

        [SerializeField]
        [Min(1)]
        [Tooltip("Most pieces live at once in the WebGL build.")]
        private int _webCapacity = 40;

        [SerializeField]
        [Min(1)]
        [Tooltip("Most pieces live at once in a desktop build.")]
        private int _desktopCapacity = 150;

        [SerializeField]
        [Min(0.5f)]
        [Tooltip("Seconds before a WebGL piece is frozen where it lies.")]
        private float _webSettleSeconds = 6f;

        [SerializeField]
        [Min(0.5f)]
        [Tooltip("Seconds before a desktop piece is frozen where it lies.")]
        private float _desktopSettleSeconds = 20f;

        private Rigidbody[] _pieces;
        private float[] _settleAt;
        private int _next;
        private float _settleSeconds;

        /// <summary>此平台允许同时活动的碎片数量。</summary>
        public int Capacity => _pieces?.Length ?? 0;

        /// <summary>当前世界中的碎片数量。</summary>
        public int ActiveCount
        {
            get
            {
                int count = 0;

                if (_pieces != null)
                {
                    foreach (Rigidbody piece in _pieces)
                    {
                        count += piece.gameObject.activeSelf ? 1 : 0;
                    }
                }

                return count;
            }
        }

        /// <summary>指定材质与预算，由场景生成器使用。</summary>
        /// <param name="material">碎片材质。</param>
        /// <param name="webCapacity">WebGL 活动碎片上限。</param>
        /// <param name="desktopCapacity">桌面活动碎片上限。</param>
        public void Bind(Material material, int webCapacity, int desktopCapacity)
        {
            _material = material;
            _webCapacity = webCapacity;
            _desktopCapacity = desktopCapacity;
        }

        private void Awake()
        {
            bool web = Application.platform == RuntimePlatform.WebGLPlayer;
            int capacity = DestructionRules.Capacity(web, _webCapacity, _desktopCapacity);

            _settleSeconds = web ? _webSettleSeconds : _desktopSettleSeconds;
            _pieces = new Rigidbody[capacity];
            _settleAt = new float[capacity];

            Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            int layer = Core.Constants.Layers.Debris;

            for (int i = 0; i < capacity; i++)
            {
                var piece = new GameObject($"Debris_{i:D3}") { layer = layer };
                piece.transform.SetParent(transform, false);
                piece.AddComponent<MeshFilter>().sharedMesh = cube;
                piece.AddComponent<MeshRenderer>().sharedMaterial = _material;
                piece.AddComponent<BoxCollider>();

                Rigidbody body = piece.AddComponent<Rigidbody>();
                body.mass = 20f;
                body.interpolation = RigidbodyInterpolation.Interpolate;

                piece.SetActive(false);
                _pieces[i] = body;
            }
        }

        /// <summary>将一个碎片抛入世界；全部占用时取回最旧的。</summary>
        /// <param name="position">碎片起始位置。</param>
        /// <param name="rotation">初始朝向。</param>
        /// <param name="size">尺寸，单位为米。</param>
        /// <param name="velocity">初始速度。</param>
        /// <param name="spin">初始角速度，单位为弧度每秒。</param>
        public void Launch(Vector3 position, Quaternion rotation, Vector3 size, Vector3 velocity, Vector3 spin)
        {
            if (_pieces == null || _pieces.Length == 0)
            {
                return;
            }

            Rigidbody body = _pieces[_next];
            _settleAt[_next] = Time.time + _settleSeconds;
            _next = (_next + 1) % _pieces.Length;

            body.gameObject.SetActive(true);
            body.isKinematic = false;
            body.transform.SetPositionAndRotation(position, rotation);
            body.transform.localScale = size;
            body.position = position;
            body.rotation = rotation;
            body.linearVelocity = velocity;
            body.angularVelocity = spin;
        }

        /// <summary>移除所有碎片，用于重试。</summary>
        public void Clear()
        {
            if (_pieces == null)
            {
                return;
            }

            foreach (Rigidbody piece in _pieces)
            {
                if (!piece.isKinematic)
                {
                    piece.linearVelocity = Vector3.zero;
                    piece.angularVelocity = Vector3.zero;
                }

                piece.gameObject.SetActive(false);
            }

            _next = 0;
        }

        private void FixedUpdate()
        {
            if (_pieces == null)
            {
                return;
            }

            for (int i = 0; i < _pieces.Length; i++)
            {
                Rigidbody piece = _pieces[i];

                if (piece.isKinematic || !piece.gameObject.activeSelf)
                {
                    continue;
                }

                if (piece.IsSleeping() || Time.time >= _settleAt[i])
                {
                    piece.isKinematic = true;
                }
            }
        }
    }
}
