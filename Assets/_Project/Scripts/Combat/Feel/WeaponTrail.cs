using UnityEngine;

namespace AdaptiveBossArena.Combat.Feel
{
    /// <summary>
    /// 挥击过程中由整段刀刃扫过形成的拖影。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是让攻击看起来实际经过一段路径的最低成本方式。没有它，挥击只是两帧之间的姿势变化；有了它，
    /// 挥击后仍可见弧线，让玩家从只看清一半的攻击中判断范围和方向。
    /// </para>
    /// <para>
    /// 它是刀刃根部与尖端之间的表面，而不是单点后方的带状轨迹。旧带状轨迹跟随身体旁边的固定点，
    /// 因此描绘的是战斗者周围的圆，而不是刀刃弧线；挥击结束后，它会像平面的彩色卡片一样悬在空中。
    /// </para>
    /// <para>
    /// 轨迹只在挥击期间生成，之后自行淡出。停止时清除它会恰好在眼睛追踪拖影的瞬间将它截断在空中。
    /// </para>
    /// <para>
    /// 按游戏时间衰老，因此顿帧会让拖影随冻结刀刃停留，而不是在冻结画面仍显示时消失。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class WeaponTrail : MonoBehaviour
    {
        /// <summary>同时保留的最多刀刃位置数量，在每秒六十帧时足以覆盖远长于存活期的时间。</summary>
        private const int Capacity = 24;

        /// <summary>两次记录位置间插入的点数，让快速挥击呈现曲线而不是折角。</summary>
        private const int Subdivisions = 3;

        [SerializeField]
        [Range(0.1f, 1.5f)]
        [Tooltip("How long the smear is emitted for after a swing begins. Covers wind-up through " +
                 "recovery without bleeding into the next attack.")]
        private float _emitSeconds = 0.45f;

        [SerializeField]
        [Range(0.05f, 0.5f)]
        [Tooltip("How long a stretch of the smear lasts before it has faded out.")]
        private float _lifetimeSeconds = 0.16f;

        [SerializeField]
        [Tooltip("Distance from this transform, along its forward, where the smear's inner edge runs.")]
        private float _baseDistance = 0.3f;

        [SerializeField]
        [Tooltip("Distance from this transform, along its forward, where the smear's outer edge runs.")]
        private float _tipDistance = 0.95f;

        [SerializeField]
        [Tooltip("Colour at the head of the smear. Kept dim; the material adds it as light.")]
        private Color _colour = new Color(0.5f, 0.48f, 0.45f, 1f);

        [SerializeField]
        [Tooltip("Additive material, bright toward the blade's edge. Assigned by the prefab generator.")]
        private Material _material;

        private readonly Vector3[] _bases = new Vector3[Capacity];
        private readonly Vector3[] _tips = new Vector3[Capacity];
        private readonly float[] _times = new float[Capacity];

        private Vector3[] _vertices;
        private Color[] _colours;
        private Vector2[] _uvs;
        private int[] _triangles;

        private Mesh _mesh;
        private GameObject _surface;
        private int _count;
        private int _head;
        private float _clock;
        private float _remaining;
        private bool _meshEmpty = true;

        /// <summary>刀刃正在留下拖影时为 true。</summary>
        public bool IsEmitting => _remaining > 0f;

        /// <summary>当前绘制拖影经过的刀刃位置数量。</summary>
        public int VisibleSamples => _count;

        /// <summary>配置拖影，由预制体生成器使用。</summary>
        /// <param name="baseDistance">内边缘沿此 Transform 前向的距离。</param>
        /// <param name="tipDistance">外边缘沿此 Transform 前向的距离。</param>
        /// <param name="colour">拖影头部的颜色。</param>
        /// <param name="material">加法混合拖影材质。</param>
        public void Configure(float baseDistance, float tipDistance, Color colour, Material material)
        {
            _baseDistance = baseDistance;
            _tipDistance = tipDistance;
            _colour = colour;
            _material = material;
        }

        /// <summary>开始生成；若挥击已经进行，则延长生成时间。</summary>
        public void Begin()
        {
            // 重新开始而不是累积：连击第二次命中应获得完整拖影，而不是越来越长的拖影。
            _remaining = _emitSeconds;
        }

        private void Awake()
        {
            int points = (Capacity - 1) * Subdivisions + 1;
            _vertices = new Vector3[points * 2];
            _colours = new Color[points * 2];
            _uvs = new Vector2[points * 2];
            _triangles = new int[(points - 1) * 6];

            _mesh = new Mesh { name = "BladeSmear" };
            _mesh.MarkDynamic();

            // 放在世界空间且位于战斗者层级之外：记录的位置已经是世界位置，若父对象移动或缩放，
            // 就会让拖影随身体一起运动。
            _surface = new GameObject(name + "Smear");
            _surface.AddComponent<MeshFilter>().sharedMesh = _mesh;

            var renderer = _surface.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void OnDestroy()
        {
            if (_surface != null)
            {
                Destroy(_surface);
            }

            if (_mesh != null)
            {
                Destroy(_mesh);
            }
        }

        private void LateUpdate()
        {
            // 使用游戏时间：顿帧期间随刀刃冻结。
            float delta = Time.deltaTime;
            _clock += delta;

            if (_remaining > 0f)
            {
                _remaining -= delta;
                Record();
            }

            Expire();
            Rebuild();
        }

        private void Record()
        {
            Vector3 origin = transform.position;
            Vector3 along = transform.forward;

            _head = (_head + 1) % Capacity;
            _bases[_head] = origin + along * _baseDistance;
            _tips[_head] = origin + along * _tipDistance;
            _times[_head] = _clock;
            _count = Mathf.Min(_count + 1, Capacity);
        }

        private void Expire()
        {
            while (_count > 0 && _clock - _times[Index(_count - 1)] > _lifetimeSeconds)
            {
                _count--;
            }
        }

        /// <summary>比最新采样旧指定步数的采样在环形缓冲区中的槽位。</summary>
        private int Index(int stepsBack) => (_head - stepsBack + Capacity) % Capacity;

        private void Rebuild()
        {
            if (_count < 2)
            {
                // 战斗绝大多数时间处于空闲；每帧清除已经为空的网格纯属浪费。
                if (!_meshEmpty)
                {
                    _mesh.Clear();
                    _meshEmpty = true;
                }

                return;
            }

            _mesh.Clear();
            _meshEmpty = false;

            int points = (_count - 1) * Subdivisions + 1;

            for (int p = 0; p < points; p++)
            {
                // 沿记录采样的位置，以最新采样为先，表示为带小数的采样索引。
                float along = p / (float)Subdivisions;
                int i = Mathf.Min((int)along, _count - 2);
                float t = along - i;

                Vector3 inner = Sample(_bases, i, t);
                Vector3 outer = Sample(_tips, i, t);
                float age = Mathf.Lerp(_clock - _times[Index(i)], _clock - _times[Index(i + 1)], t);
                float life = Mathf.Clamp01(1f - age / _lifetimeSeconds);

                Color colour = _colour;
                colour.a *= life * life;

                _vertices[p * 2] = inner;
                _vertices[p * 2 + 1] = outer;
                _colours[p * 2] = colour;
                _colours[p * 2 + 1] = colour;
                _uvs[p * 2] = new Vector2(1f - life, 0f);
                _uvs[p * 2 + 1] = new Vector2(1f - life, 1f);
            }

            int triangle = 0;

            for (int p = 0; p < points - 1; p++)
            {
                int a = p * 2, b = a + 1, c = a + 2, d = a + 3;
                _triangles[triangle++] = a; _triangles[triangle++] = b; _triangles[triangle++] = d;
                _triangles[triangle++] = a; _triangles[triangle++] = d; _triangles[triangle++] = c;
            }

            _mesh.SetVertices(_vertices, 0, points * 2);
            _mesh.SetColors(_colours, 0, points * 2);
            _mesh.SetUVs(0, _uvs, 0, points * 2);
            _mesh.SetTriangles(_triangles, 0, triangle, 0, calculateBounds: true);
        }

        /// <summary>使用相邻采样构造曲线，取得两个记录采样之间的 Catmull-Rom 点。</summary>
        private Vector3 Sample(Vector3[] positions, int i, float t)
        {
            Vector3 p1 = positions[Index(i)];
            Vector3 p2 = positions[Index(i + 1)];
            Vector3 p0 = i > 0 ? positions[Index(i - 1)] : p1 + (p1 - p2);
            Vector3 p3 = i + 2 < _count ? positions[Index(i + 2)] : p2 + (p2 - p1);

            float t2 = t * t, t3 = t2 * t;

            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                           (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }
    }
}
