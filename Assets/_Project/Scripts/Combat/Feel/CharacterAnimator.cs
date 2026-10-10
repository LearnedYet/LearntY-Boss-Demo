using AdaptiveBossArena.Core.Perception;
using AdaptiveBossArena.Utilities;
using UnityEngine;

namespace AdaptiveBossArena.Combat.Feel
{
    /// <summary>
    /// 用程序化、代码驱动的动画赋予基础几何体角色生命感。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 位于角色的<em>视觉</em>根上——同一个子对象也带有<see cref="HitFlash"/> 和武器轨迹——并让该
    /// Transform 的局部位置、旋转和缩放，向角色当前动作以及进行中攻击阶段对应的姿势缓动。
    /// 碰撞体和受击盒在上一级角色根上，因此这里不会移动任何命中盒：突进仅影响外观，
    /// 战斗始终在没有动画的根上进行，与测试所描述的相同。
    /// </para>
    /// <para>
    /// 它只是信息消费者。所属控制器每帧通过<see cref="SetMotionState"/> 推送状态，
    /// 通过 <see cref="Recoil"/> 报告命中；动画器从不反向访问、从不读取输入，
    /// 也从不向 AI 提供任何信息——这对首领尤其关键。仅用于表现。
    /// </para>
    /// <para>
    /// 姿势按 <see cref="Time.unscaledDeltaTime"/> 缓动，让突进在自身触发的顿帧中保持可读，
    /// 而不是随世界冻结在半空。
    /// </para>
    /// <para>
    /// 命中是刻意设置的例外。反作用在接触帧以完整强度施加，其包络按战斗时间倒数，因此在整个冻结中保持，
    /// 并在世界恢复时回弹。过去也按真实时间倒数，这导致重击冻结结束时大部分反作用已经淡去——冻结与推力
    /// 先后发生，而不是同时发生，两者都缺少命中感。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CharacterAnimator : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Animation magnitudes. Assigned by the prefab generator; without it the character " +
                 "simply stands still.")]
        private CharacterAnimationConfig _config;

        private Vector3 _restPosition;
        private Quaternion _restRotation;
        private Vector3 _restScale;

        private ObservableActionState _state = ObservableActionState.Idle;
        private AttackPhase _attackPhase = AttackPhase.Inactive;
        private float _planarSpeed01;

        private float _bobTime;

        private Vector3 _recoilLocalDir;
        private float _recoilRemaining;

        /// <summary>打击命中时设置，让下一姿势跳过缓动并立即呈现。</summary>
        private bool _snapToImpact;

        /// <summary>命中包络倒数使用的战斗时钟；为 null 则回退到缩放的引擎时间。</summary>
        private Core.Services.ITimeService _time;

        private float _flourishRemaining;

        /// <summary>
        /// 此动画器旁的可选骨骼桥接组件。当它驱动真实骨架时，此程序化层只保留叠加命中效果，
        /// 确保两者不争夺姿势。
        /// </summary>
        private CharacterAnimationBridge _bridge;

        private void Awake()
        {
            _restPosition = transform.localPosition;
            _restRotation = transform.localRotation;
            _restScale = transform.localScale;

            // 缓存而不是每帧查找：美术资源通过重新生成预制体加入，
            // 因此桥接组件以及它发现的骨架在角色整个生命周期中固定。
            _bridge = GetComponent<CharacterAnimationBridge>();
        }

        /// <summary>骨骼而非此程序化层拥有基础姿势时为 true。</summary>
        private bool SkeletonDrivesPose => _bridge != null && _bridge.HasSkeleton;

        /// <summary>指定调参资源，由预制体生成器使用。</summary>
        /// <param name="config">动画幅度。</param>
        public void SetConfig(CharacterAnimationConfig config) => _config = config;

        /// <summary>
        /// 指定让命中效果在顿帧期间保持的战斗时钟。
        /// </summary>
        /// <remarks>
        /// 由所属控制器注入而不是查找，因为组合根之外的任何对象都不能访问服务注册表。
        /// </remarks>
        /// <param name="time">战斗时间服务。</param>
        public void SetTime(Core.Services.ITimeService time) => _time = time;

        /// <summary>
        /// 推送角色本帧可见的状态。
        /// </summary>
        /// <remarks>
        /// 所属控制器每次推进调用一次。动画器保留最后接收的值，因此控制器暂停时姿势自然冻结。
        /// </remarks>
        /// <param name="state">观察者能够看见的动作。</param>
        /// <param name="attackPhase">进行中攻击的阶段，驱动前摇和突进。</param>
        /// <param name="planarSpeed01">水平速度占最高速度的比例，用于奔跑倾斜。</param>
        /// <param name="attack">进行中的攻击；也可为 null。骨架用它选择和定位片段。</param>
        /// <param name="attackElapsedSeconds">自该攻击开始以来的时间。</param>
        /// <param name="reactionProgress01">从地面起身的进度，用于骨架起身片段。</param>
        public void SetMotionState(
            ObservableActionState state,
            AttackPhase attackPhase,
            float planarSpeed01,
            AttackDefinition attack = null,
            float attackElapsedSeconds = 0f,
            float reactionProgress01 = 0f)
        {
            _state = state;
            _attackPhase = attackPhase;
            _planarSpeed01 = Mathf.Clamp01(planarSpeed01);

            // 转发，让两个表现层都使用控制器的一次推送。没有骨架时不执行操作。
            _bridge?.SetMotionState(state, attackPhase, planarSpeed01, attack, attackElapsedSeconds, reactionProgress01);
        }

        /// <summary>此角色使用的动画调参。</summary>
        public CharacterAnimationConfig Config => _config;

        /// <summary>沿来袭打击方向推开身体。</summary>
        /// <param name="worldHitDirection">打击从攻击者指向目标的方向。</param>
        public void Recoil(Vector3 worldHitDirection)
        {
            if (_config == null || transform.parent == null)
            {
                return;
            }

            // 转换到根的局部空间，让反作用与身体朝向位于同一坐标系；随后压平，避免命中将角色抛离地面。
            Vector3 local = transform.parent.InverseTransformDirection(worldHitDirection);
            local.y = 0f;

            _recoilLocalDir = MathUtil.SafeNormalize(local);
            _recoilRemaining = _config.RecoilDurationSeconds;
            _snapToImpact = true;

            // 叠加推力在骨架模型上也可见，因此即使骨骼拥有姿势也保留它；
            // 骨架自身的 Hit 片段与它一起播放。
            _bridge?.Recoil();
        }

        /// <summary>阶段转换或其他表现动作时，让身体向上仰起。</summary>
        /// <param name="seconds">
        /// 骨架维持咆哮的时长；零表示使用配置的表现动作时长。基础几何体身体始终使用配置时长，
        /// 因为该时长与其压缩和拉伸相配。
        /// </param>
        public void Flourish(float seconds = 0f)
        {
            if (_config != null)
            {
                _flourishRemaining = _config.FlourishDurationSeconds;
            }

            // 骨架有真实咆哮可播放，取代程序化仰起。
            _bridge?.Flourish(seconds > 0f ? seconds : _config != null ? _config.FlourishDurationSeconds : 0f);
        }

        /// <summary>将姿势立即恢复到静止并清除冲量，用于重试。</summary>
        public void ResetPose()
        {
            _recoilRemaining = 0f;
            _flourishRemaining = 0f;
            _snapToImpact = false;
            _state = ObservableActionState.Idle;
            _attackPhase = AttackPhase.Inactive;

            transform.localPosition = _restPosition;
            transform.localRotation = _restRotation;
            transform.localScale = _restScale;

            _bridge?.ResetState();
        }

        private void LateUpdate()
        {
            if (_config == null)
            {
                return;
            }

            float deltaTime = Time.unscaledDeltaTime;
            _bobTime += deltaTime;

            AdvanceImpulses(_time != null ? _time.DeltaTime : Time.deltaTime);

            // 骨架身体严格保留在根所在位置。在动作捕捉片段之上，
            // 沿打击方向推动整个视觉对象并加上倾斜和摇摆，会让身体在地面滑行，
            // 偏离战斗判定使用的命中盒，并在片段因顿帧冻结时仍按真实时间缓动。
            // 改由骨架自身的 Hits 层表现命中。
            if (SkeletonDrivesPose)
            {
                HoldAtRest();
                return;
            }

            ComposeTarget(out Vector3 targetPosition, out Quaternion targetRotation, out Vector3 targetScale);

            if (_snapToImpact)
            {
                // 接触帧立即显示完整打击。如果缓动进入，就会将冻结时间用来向反作用移动，
                // 而不是保持反作用。
                _snapToImpact = false;
                transform.localPosition = targetPosition;
                transform.localScale = targetScale;
            }

            transform.localPosition = MathUtil.Damp(
                transform.localPosition, targetPosition, _config.PositionHalfLife, deltaTime);
            transform.localScale = MathUtil.Damp(
                transform.localScale, targetScale, _config.ScaleHalfLife, deltaTime);
            transform.localRotation = Quaternion.Slerp(
                transform.localRotation, targetRotation, DampFactor(_config.RotationHalfLife, deltaTime));
        }

        /// <summary>让视觉根严格保持在角色根上，使骨架成为唯一移动的部分。</summary>
        private void HoldAtRest()
        {
            if (transform.localPosition != _restPosition)
            {
                transform.localPosition = _restPosition;
            }

            if (transform.localRotation != _restRotation)
            {
                transform.localRotation = _restRotation;
            }

            if (transform.localScale != _restScale)
            {
                transform.localScale = _restScale;
            }
        }

        /// <summary>按战斗时间衰减短暂的反作用和表现动作包络。</summary>
        private void AdvanceImpulses(float deltaTime)
        {
            _recoilRemaining = Mathf.Max(0f, _recoilRemaining - deltaTime);
            _flourishRemaining = Mathf.Max(0f, _flourishRemaining - deltaTime);
        }

        /// <summary>根据基础状态和活动冲量构建完整目标姿势。</summary>
        private void ComposeTarget(out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            BasePose(out Vector3 offset, out float leanDegrees, out Vector3 scaleFactor);

            // 反作用和表现动作叠加在角色当前动作上，因此挥击中受击会扰动挥击，而不是替换它。
            if (_recoilRemaining > 0f)
            {
                float envelope = _recoilRemaining / _config.RecoilDurationSeconds;
                offset += _recoilLocalDir * (_config.RecoilDistance * envelope);
                scaleFactor = Vector3.Scale(scaleFactor, Squash(_config.RecoilSquash * envelope));
            }

            if (_flourishRemaining > 0f)
            {
                float envelope = _flourishRemaining / _config.FlourishDurationSeconds;
                leanDegrees -= _config.FlourishRearDegrees * envelope;
                scaleFactor = Vector3.Scale(scaleFactor, Stretch(_config.FlourishStretch * envelope));
            }

            position = _restPosition + offset;
            rotation = _restRotation * Quaternion.Euler(leanDegrees, 0f, IdleSway());

            // 压缩和拉伸用于基础几何体身体。非均匀缩放蒙皮网格会扭曲四肢，而不是表现重量，
            // 因此骨架只保留叠加推力和倾斜。
            scale = SkeletonDrivesPose ? _restScale : Vector3.Scale(_restScale, scaleFactor);
        }

        /// <summary>为当前动作选择基础姿势。</summary>
        /// <remarks>
        /// 返回局部空间偏移、以度表示的俯仰倾斜（正值让身体向前，即朝向方向倾斜），
        /// 以及压缩和拉伸的乘法缩放系数。
        /// </remarks>
        private void BasePose(out Vector3 offset, out float leanDegrees, out Vector3 scaleFactor)
        {
            offset = Vector3.zero;
            leanDegrees = 0f;
            scaleFactor = Vector3.one;

            // 骨架拥有身体时，基础姿势保持中性，只保留 ComposeTarget 中叠加的反作用和表现动作——增加命
            // 中重量，而不是竞争姿势。
            if (SkeletonDrivesPose)
            {
                return;
            }

            switch (_state)
            {
                case ObservableActionState.LightAttacking:
                case ObservableActionState.HeavyAttacking:
                case ObservableActionState.UsingAbility:
                    AttackPose(out offset, out leanDegrees, out scaleFactor);
                    break;

                case ObservableActionState.Dashing:
                    leanDegrees = _config.DashLeanDegrees;
                    scaleFactor = Stretch(_config.DashStretch);
                    break;

                case ObservableActionState.Guarding:
                case ObservableActionState.Parrying:
                    offset = new Vector3(0f, -_config.GuardCrouch, 0f);
                    leanDegrees = _config.GuardLeanDegrees;
                    break;

                case ObservableActionState.Healing:
                    offset = new Vector3(0f, -_config.HealCrouch, 0f);
                    break;

                case ObservableActionState.Staggered:
                    // 反作用冲量负责主要视觉表现；基础姿势仅轻微垮下，
                    // 避免长硬直尚未结束时突然恢复直立。
                    leanDegrees = -_config.AnticipationLeanDegrees * 0.5f;
                    break;

                case ObservableActionState.Dead:
                    offset = new Vector3(0f, -_config.DeathSink, 0f);
                    leanDegrees = _config.DeathFallDegrees;
                    break;

                case ObservableActionState.Moving:
                    leanDegrees = _config.MoveLeanDegrees * _planarSpeed01;
                    offset = new Vector3(0f, Bob(1f + _planarSpeed01), 0f);
                    break;

                default:
                    offset = new Vector3(0f, Bob(1f), 0f);
                    break;
            }
        }

        /// <summary>由攻击阶段决定的挥击前摇、突进和跟随动作。</summary>
        private void AttackPose(out Vector3 offset, out float leanDegrees, out Vector3 scaleFactor)
        {
            float weight = _state == ObservableActionState.LightAttacking ? 1f : _config.HeavyMultiplier;

            switch (_attackPhase)
            {
                case AttackPhase.Startup:
                    // 向后、向下蓄势并后倾——让打击可读的预备动作。
                    offset = new Vector3(0f, -_config.AnticipationCrouch * 0.4f, -_config.AnticipationCrouch) * weight;
                    leanDegrees = -_config.AnticipationLeanDegrees * weight;
                    scaleFactor = Squash(_config.AnticipationSquash * weight);
                    break;

                case AttackPhase.Active:
                    // 投入：向打击突进，并沿其方向伸展。
                    offset = new Vector3(0f, 0f, _config.StrikeLunge * weight);
                    leanDegrees = _config.StrikeLeanDegrees * weight;
                    scaleFactor = Stretch(_config.StrikeStretch * weight);
                    break;

                default:
                    // 后摇和短暂未激活帧逐渐回到中性；缓动提供跟随动作，无需单独姿势。
                    offset = Vector3.zero;
                    leanDegrees = 0f;
                    scaleFactor = Vector3.one;
                    break;
            }
        }

        /// <summary>按状态活跃程度缩放的垂直呼吸偏移。</summary>
        private float Bob(float energy) =>
            Mathf.Sin(_bobTime * _config.IdleBobFrequency * energy * Mathf.PI * 2f)
            * _config.IdleBobAmplitude * energy;

        /// <summary>缓慢左右呼吸摇摆，单位为度。</summary>
        private float IdleSway() =>
            _state == ObservableActionState.Idle
                ? Mathf.Sin(_bobTime * _config.IdleBobFrequency * Mathf.PI) * _config.IdleSwayDegrees
                : 0f;

        /// <summary>更宽、更矮，表现蓄势或受击收缩。</summary>
        private static Vector3 Squash(float amount) =>
            new Vector3(1f + amount, 1f - amount, 1f + amount);

        /// <summary>更高、更窄，表现投入的伸展。</summary>
        private static Vector3 Stretch(float amount) =>
            new Vector3(1f - amount * 0.5f, 1f + amount, 1f - amount * 0.5f);

        /// <summary>按半衰期计算、不依赖帧率的插值系数。</summary>
        private static float DampFactor(float halfLifeSeconds, float deltaTime) =>
            1f - Mathf.Exp(-deltaTime / Mathf.Max(0.0001f, halfLifeSeconds));
    }
}
