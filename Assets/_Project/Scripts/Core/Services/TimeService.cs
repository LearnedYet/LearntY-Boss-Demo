using UnityEngine;

namespace AdaptiveBossArena.Core.Services
{
    /// <summary>
    /// 默认 <see cref="ITimeService"/> 实现，由外部每帧驱动。
    /// </summary>
    /// <remarks>
    /// 采用普通类而非 <c>MonoBehaviour</c>，可在编辑模式测试中输入合成时间差来验证逻辑。
    /// Game 程序集中的薄组件持有实例，在更新循环调用 <see cref="Tick"/>。
    /// </remarks>
    public sealed class TimeService : ITimeService
    {
        /// <summary>
        /// 命中停顿使用的时间倍率。保持略高于零而非等于零，让动画和物理继续接收时间步，
        /// 避免冻结结束时出现明显重新稳定的过程。
        /// </summary>
        private const float HitStopTimeScale = 0.0001f;

        private const float NormalTimeScale = 1f;

        /// <summary>
        /// 慢动作可将基础物理步长缩小到的最小比例。
        /// </summary>
        /// <remarks>
        /// 随慢动作缩小物理步长，可使画面中的物理运动与正常速度一样平滑，这正是缩放它的理由。
        /// 若没有下限，深度慢动作会向求解器请求过小时间步，关节和接触不再正常求解，
        /// 表现为致命一击的慢动作中布娃娃突然炸开。
        /// </remarks>
        private const float MinimumPhysicsStepFraction = 0.25f;

        private readonly float _baseFixedDeltaTime;

        private float _hitStopRemaining;
        private float _slowMotionRemaining;
        private float _slowMotionScale = NormalTimeScale;
        private bool _isPaused;

        /// <summary>创建时间服务。</summary>
        /// <param name="baseFixedDeltaTime">
        /// 项目未缩放的物理步长。只捕获一次，反复重应用缩放时不会累积漂移。
        /// </param>
        public TimeService(float baseFixedDeltaTime = 1f / 60f) =>
            _baseFixedDeltaTime = baseFixedDeltaTime;

        /// <inheritdoc />
        public float DeltaTime { get; private set; }

        /// <inheritdoc />
        public float UnscaledDeltaTime { get; private set; }

        /// <inheritdoc />
        /// <remarks>
        /// <para>
        /// 只随慢动作缩放，绝不随命中停顿或暂停缩放。以前所有情况都跟随时间倍率，
        /// 导致每次停顿时物理步长约为1.7e-6 秒，暂停时恰为零。此前没有使用Rigidbody，因此问题未显现；
        /// 改造新增的布娃娃和碎片则会在每次冻结时收到数千个极小时间步。
        /// </para>
        /// <para>
        /// 冻结无需缩小步长。时间倍率接近零时，引擎几乎不积累模拟时间，
        /// 因此保持普通步长即可完全不执行物理步，这才符合冻结表现。
        /// </para>
        /// </remarks>
        public float FixedDeltaTime => _baseFixedDeltaTime * PhysicsStepScale();

        /// <inheritdoc />
        public float CombatTime { get; private set; }

        /// <inheritdoc />
        public float TimeScale { get; private set; } = NormalTimeScale;

        /// <inheritdoc />
        public bool IsPaused => _isPaused;

        /// <summary>
        /// 推进全部时间效果，并将最终倍率重新发布到引擎。
        /// </summary>
        /// <param name="unscaledDeltaTime">此帧经过的真实时间。</param>
        public void Tick(float unscaledDeltaTime)
        {
            UnscaledDeltaTime = unscaledDeltaTime;

            ExpireTimeEffects(unscaledDeltaTime);
            TimeScale = ResolveTimeScale();

            DeltaTime = unscaledDeltaTime * TimeScale;
            CombatTime += DeltaTime;

            ApplyToEngine();
        }

        /// <inheritdoc />
        public void RequestHitStop(float seconds)
        {
            // 取两者较长值而非直接覆盖，使冻结期间命中的重击能延长停顿，而不被先前轻击的较短停顿截断。
            _hitStopRemaining = Mathf.Max(_hitStopRemaining, Mathf.Max(0f, seconds));
        }

        /// <inheritdoc />
        public void RequestSlowMotion(float scale, float durationSeconds)
        {
            _slowMotionScale = Mathf.Clamp(scale, 0.01f, NormalTimeScale);
            _slowMotionRemaining = Mathf.Max(_slowMotionRemaining, Mathf.Max(0f, durationSeconds));
        }

        /// <inheritdoc />
        public void ClearTimeEffects()
        {
            _hitStopRemaining = 0f;
            _slowMotionRemaining = 0f;
            _slowMotionScale = NormalTimeScale;

            // 立即重新发布，而非等待下一次更新，因为这里也用于销毁清理。若服务在命中停顿期间销毁，
            // 否则引擎时间倍率会停在接近零，将冻结状态带入下一场景或编辑器。
            TimeScale = ResolveTimeScale();
            ApplyToEngine();
        }

        /// <inheritdoc />
        public void SetPaused(bool paused)
        {
            _isPaused = paused;
            TimeScale = ResolveTimeScale();
            ApplyToEngine();
        }

        /// <inheritdoc />
        public void ResetCombatClock() => CombatTime = 0f;

        /// <summary>按真实时间倒计时生效中的时间效果，避免效果自我减速。</summary>
        private void ExpireTimeEffects(float unscaledDeltaTime)
        {
            if (_isPaused || unscaledDeltaTime <= 0f)
            {
                return;
            }

            if (_hitStopRemaining > 0f)
            {
                _hitStopRemaining = Mathf.Max(0f, _hitStopRemaining - unscaledDeltaTime);
            }

            if (_slowMotionRemaining > 0f)
            {
                _slowMotionRemaining = Mathf.Max(0f, _slowMotionRemaining - unscaledDeltaTime);
                if (_slowMotionRemaining <= 0f)
                {
                    _slowMotionScale = NormalTimeScale;
                }
            }
        }

        /// <summary>从全部生效效果解析实际倍率，优先处理权威最高的效果。</summary>
        private float ResolveTimeScale()
        {
            if (_isPaused)
            {
                return 0f;
            }

            if (_hitStopRemaining > 0f)
            {
                return HitStopTimeScale;
            }

            return _slowMotionRemaining > 0f ? _slowMotionScale : NormalTimeScale;
        }

        /// <summary>慢动作缩小物理步长的比例；命中停顿和暂停不缩小它。</summary>
        private float PhysicsStepScale()
        {
            if (_isPaused || _hitStopRemaining > 0f || _slowMotionRemaining <= 0f)
            {
                return NormalTimeScale;
            }

            return Mathf.Max(MinimumPhysicsStepFraction, _slowMotionScale);
        }

        /// <summary>将解析后的倍率发布到引擎全局时间状态。</summary>
        private void ApplyToEngine()
        {
            Time.timeScale = TimeScale;

            // 参见 FixedDeltaTime：慢动作缩小步长以保持画面中物理运动平滑；命中停顿和暂停保持原步长，
            // 避免给求解器传入退化步长。
            Time.fixedDeltaTime = FixedDeltaTime;
        }
    }
}
