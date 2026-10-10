using AdaptiveBossArena.Core.Services;
using UnityEngine;

namespace AdaptiveBossArena.Combat.Feel
{
    /// <summary>
    /// 实际每移动一个步幅播放一次脚步，让移动具有可听见的重量。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 由<em>移动距离</em>而不是速度或计时器驱动，让脚步节奏与角色实际运动一致，
    /// 并在角色停止瞬间停止——包括顿帧期间，此时 Transform 根本没有移动，因此无需查看任何时钟。
    /// </para>
    /// <para>
    /// 仅用于表现：读取自己的 Transform 并播放声音，不了解战斗。玩家和首领使用同一组件、
    /// 不同音效标识和步幅，让首领产生比玩家更重、更慢的脚步，无需特殊分支。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class FootstepEmitter : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Audio cue played on each footstep.")]
        private string _cueId = "step.player";

        [SerializeField]
        [Tooltip("Distance travelled between footsteps. Longer strides read as a larger character.")]
        private float _strideLength = 1.6f;

        [SerializeField]
        [Tooltip("Alpha-blended material the dust is drawn with. Assigned by the prefab generator.")]
        private Material _dustMaterial;

        [SerializeField]
        [Tooltip("Rough width of the dust a footfall lifts, in metres.")]
        private float _dustSize = 0.35f;

        /// <summary>灰尘起始高度，刚离开地面，位于靴子接触石头的位置。</summary>
        private const float DustHeight = 0.05f;

        [SerializeField]
        [Tooltip("Camera trauma each footfall adds when the camera is close. Zero for none: only the brute's " +
                 "steps are heavy enough to be felt.")]
        [Range(0f, 0.3f)]
        private float _tremor;

        /// <summary>重脚步可通过摄像机被感受到的距离。</summary>
        private const float TremorRange = 7f;

        private IAudioService _audio;
        private IScreenShake _shake;
        private ParticleSystem _dust;
        private Vector3 _lastPosition;
        private float _accumulated;

        /// <summary>
        /// 某一速度的脚步扬起多少微粒。
        /// </summary>
        /// <remarks>
        /// 行走几乎不扰动地面；奔跑则踢起砂砾。低于步行速度时完全不扬尘，
        /// 让慢慢逼近攻击范围的战斗者不会拖着灰尘。
        /// </remarks>
        /// <param name="speed01">水平速度占最高速度的比例。</param>
        /// <returns>脚步扬起的微粒数量。</returns>
        public static int PuffCount(float speed01) =>
            speed01 < 0.25f
                ? 0
                : Mathf.RoundToInt(Mathf.Lerp(2f, 7f, Mathf.InverseLerp(0.25f, 1f, Mathf.Clamp01(speed01))));

        /// <summary>指定音效标识、步幅和灰尘，由预制体生成器使用。</summary>
        /// <param name="cueId">要播放的脚步音效标识。</param>
        /// <param name="strideLength">两步之间的距离。</param>
        /// <param name="dustMaterial">脚步扬尘材质；为 null 则不产生灰尘。</param>
        /// <param name="dustSize">灰尘的大致宽度，单位为米。</param>
        /// <param name="tremor">靠近时每次脚步的摄像机震动强度；零表示没有。</param>
        public void Configure(
            string cueId, float strideLength, Material dustMaterial = null, float dustSize = 0.35f, float tremor = 0f)
        {
            _cueId = cueId;
            _strideLength = Mathf.Max(0.2f, strideLength);
            _dustMaterial = dustMaterial;
            _dustSize = dustSize;
            _tremor = tremor;
        }

        private void OnEnable()
        {
            _lastPosition = transform.position;

            // 预先准备，让首个脚步几乎在角色开始移动时就落下，而不是先无声移动完整一步。
            _accumulated = _strideLength * 0.6f;
        }

        private void Update()
        {
            _audio ??= ServiceRegistry.Current != null &&
                       ServiceRegistry.Current.TryGet(out IAudioService audio)
                ? audio
                : null;

            Vector3 position = transform.position;
            Vector3 travel = position - _lastPosition;
            travel.y = 0f;
            _lastPosition = position;

            float moved = travel.magnitude;

            // 大于一个步幅的跳变是传送（重试重生），不是脚步——跳过它，避免重置在出生点触发脚步声。
            if (moved <= 0f || moved > _strideLength)
            {
                return;
            }

            _accumulated += moved;

            if (_accumulated >= _strideLength)
            {
                _accumulated -= _strideLength;
                _audio?.PlayCue(_cueId, position);
                Tremble(position);

                // 速度以每秒四步作为基准，约等于奔跑：足以区分行走和冲刺，
                // 而不需要组件知道所属角色的任何信息。
                float seconds = Time.deltaTime;
                float speed01 = seconds > Mathf.Epsilon
                    ? Mathf.Clamp01(moved / seconds / (_strideLength * 4f))
                    : 0f;

                LiftDust(position, travel, speed01);
            }
        }

        /// <summary>
        /// 附近的重脚步会轻微震动摄像机。
        /// </summary>
        /// <remarks>
        /// 巨兽的重量应当可感受，也可见：近处一步震动画面，随距离减弱到零，让缓慢接近带有物理威胁。
        /// 以位于玩家附近的摄像机作为判断依据，因此完全无需了解玩家。
        /// </remarks>
        private void Tremble(Vector3 position)
        {
            if (_tremor <= 0f)
            {
                return;
            }

            _shake ??= ServiceRegistry.Current != null && ServiceRegistry.Current.TryGet(out IScreenShake shake)
                ? shake
                : null;

            Camera view = Camera.main;

            if (_shake == null || view == null)
            {
                return;
            }

            float distance = Vector3.Distance(view.transform.position, position);
            float closeness = 1f - Mathf.Clamp01(distance / TremorRange);

            if (closeness > 0f)
            {
                _shake.AddTrauma(_tremor * closeness);
            }
        }

        /// <summary>在脚落地的位置扬尘，沿移动反方向飘散。</summary>
        private void LiftDust(Vector3 position, Vector3 travel, float speed01)
        {
            int count = PuffCount(speed01);

            if (count == 0 || _dustMaterial == null)
            {
                return;
            }

            if (_dust == null)
            {
                BuildDust();
            }

            Vector3 back = travel.sqrMagnitude > 1e-6f ? -travel.normalized : Vector3.zero;

            var parameters = new ParticleSystem.EmitParams
            {
                position = new Vector3(position.x, DustHeight, position.z),
                applyShapeToPosition = false
            };

            for (int i = 0; i < count; i++)
            {
                float spread = (i / (float)count - 0.5f) * 2f;

                parameters.velocity = back * (0.5f + speed01) +
                                      new Vector3(spread * 0.4f, 0.25f + speed01 * 0.3f, spread * 0.4f);

                _dust.Emit(parameters, 1);
            }
        }

        /// <summary>
        /// 构建每次脚步共用的唯一粒子系统。
        /// </summary>
        /// <remarks>
        /// 和命中爆发一样，向始终播放的系统手动发射：每步重启系统成本更高，还会丢弃上一步仍悬浮的灰尘。
        /// </remarks>
        private void BuildDust()
        {
            var child = new GameObject("FootDust");
            child.transform.SetParent(transform, worldPositionStays: false);

            _dust = child.AddComponent<ParticleSystem>();
            _dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = _dust.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = 48;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 0.9f;
            main.startSize = _dustSize;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.38f, 0.35f, 0.3f, 0.24f));

            // 略有浮力：踢起的砂砾先悬浮再落下。
            main.gravityModifier = -0.02f;

            ParticleSystem.EmissionModule emission = _dust.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = _dust.shape;
            shape.enabled = false;

            ParticleSystem.SizeOverLifetimeModule size = _dust.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));

            ParticleSystem.ColorOverLifetimeModule fade = _dust.colorOverLifetime;
            fade.enabled = true;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(gradient);

            ParticleSystem.LimitVelocityOverLifetimeModule drag = _dust.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 3.5f;

            var renderer = child.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _dustMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _dust.Play();
        }
    }
}
