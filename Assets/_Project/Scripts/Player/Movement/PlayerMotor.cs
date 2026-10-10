using System;
using AdaptiveBossArena.Combat.Movement;
using AdaptiveBossArena.Core.Constants;
using UnityEngine;

namespace AdaptiveBossArena.Player.Movement
{
    /// <summary>
    /// 将移动意图转为角色运动。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 由当前拥有角色控制权的状态驱动的普通类，而非自行决策的组件。
    /// 闪避和玩家控制的移动只区别在由谁设置速度，因此共享积分和碰撞路径，避免两套处理不一致。
    /// </para>
    /// <para>
    /// 加速采用 <see cref="Vector3.MoveTowards"/>，速率由配置的到达最高速所需时长计算。
    /// 因此配置值有准确含义：0.06 秒加速确实意味着 60 毫秒达到全速；平滑趋近只能近似做到，
    /// 令设计者调整的数值不符合字面意义。
    /// </para>
    /// <para>
    /// 竞技场由真实碰撞体围合，因此边界由角色控制器自身碰撞响应处理。这里不限制位置，
    /// 也刻意不依赖竞技场配置。
    /// </para>
    /// </remarks>
    public sealed class PlayerMotor
    {
        /// <summary>低于此速度时视为静止。</summary>
        private const float StationarySpeedThreshold = 0.05f;

        private readonly CharacterController _controller;
        private readonly Transform _transform;
        private readonly PlayerConfig _config;
        private readonly Transform _cameraTransform;

        private Vector3 _planarVelocity;

        /// <summary>
        /// 将击退和重力等施加给角色的运动与自主运动分开保存，因此转向输入无法覆盖推动。
        /// </summary>
        private readonly MotionIntegrator _motion =
            new MotionIntegrator(GameplayConstants.Gravity, GameplayConstants.GroundedSpeed);

        /// <summary>创建绑定 CharacterController 的 Motor。</summary>
        /// <param name="controller">执行移动和碰撞的控制器。</param>
        /// <param name="config">移动调参值。</param>
        /// <param name="cameraTransform">
        /// 用于解释移动输入的相机。可为 null，此时输入按世界坐标解释。
        /// </param>
        /// <exception cref="ArgumentNullException">控制器或配置缺失时抛出。</exception>
        public PlayerMotor(CharacterController controller, PlayerConfig config, Transform cameraTransform)
        {
            _controller = controller != null
                ? controller
                : throw new ArgumentNullException(nameof(controller));

            _config = config != null ? config : throw new ArgumentNullException(nameof(config));
            _transform = controller.transform;
            _cameraTransform = cameraTransform;
        }

        /// <summary>当前水平速度，单位世界单位每秒。</summary>
        public Vector3 PlanarVelocity => _planarVelocity;

        /// <summary>击退等外力推动，叠加在角色自主移动之上并持续生效。</summary>
        public Vector3 ImpulseVelocity => _motion.Impulse;

        /// <summary>当前水平速率，单位世界单位每秒。</summary>
        public float Speed => _planarVelocity.magnitude;

        /// <summary>当前速率占配置最高速度的比例。</summary>
        public float NormalizedSpeed =>
            _config.MoveSpeed <= 0f ? 0f : Mathf.Clamp01(Speed / _config.MoveSpeed);

        /// <summary>角色实际静止时为 true。</summary>
        public bool IsStationary => Speed < StationarySpeedThreshold;

        /// <summary>角色世界坐标。</summary>
        public Vector3 Position => _transform.position;

        /// <summary>角色面向的单位向量。</summary>
        public Vector3 Facing => _transform.forward;

        /// <summary>
        /// 向移动输入请求的速度加速。
        /// </summary>
        /// <param name="moveInput">移动轴，长度最多为一。</param>
        /// <param name="deltaTime">经过的缩放时间。</param>
        public void ApplyMoveInput(Vector2 moveInput, float deltaTime)
        {
            Vector3 desiredDirection = ToWorldDirection(moveInput);

            // 带速度转向时先损失一部分速度，因此奔跑反向会在转身中减速，而非免费旋转整个速度向量。
            float momentumFactor = MomentumRules.TargetSpeedFactor(
                _planarVelocity, desiredDirection, _config.MoveSpeed, _config.TurnSpeedFloor);

            Vector3 targetVelocity = desiredDirection * (_config.MoveSpeed * momentumFactor);

            bool isAccelerating = desiredDirection.sqrMagnitude > 0f;
            float durationSeconds = isAccelerating
                ? _config.AccelerationSeconds
                : _config.DecelerationSeconds;

            _planarVelocity = Vector3.MoveTowards(
                _planarVelocity, targetVelocity, RateFor(durationSeconds) * deltaTime);
        }

        /// <summary>按配置减速度使角色停下。</summary>
        /// <param name="deltaTime">经过的缩放时间。</param>
        public void Decelerate(float deltaTime) => ApplyMoveInput(Vector2.zero, deltaTime);

        /// <summary>
        /// 直接覆盖水平速度，绕过加速。
        /// </summary>
        /// <remarks>供闪避和击退使用，两者均为外加运动，而非自主转向。</remarks>
        /// <param name="velocity">要采用的水平速度，丢弃垂直分量。</param>
        public void SetPlanarVelocity(Vector3 velocity) =>
            _planarVelocity = new Vector3(velocity.x, 0f, velocity.z);

        /// <summary>立即停止角色自主移动，外加推动仍会继续。</summary>
        public void Halt() => _planarVelocity = Vector3.zero;

        /// <summary>
        /// 施加击退等推动，按自身半衰期持续衰减。
        /// </summary>
        /// <remarks>
        /// 与 <see cref="SetPlanarVelocity"/> 不同，角色之后的动作不会覆盖它。
        /// </remarks>
        /// <param name="velocity">要增加的水平速度。</param>
        public void AddImpulse(Vector3 velocity) => _motion.AddImpulse(velocity);

        /// <summary>积分当前速度并解析碰撞。</summary>
        /// <param name="deltaTime">经过的缩放时间。</param>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            Vector3 velocity = _motion.Step(
                _planarVelocity, _config.ImpulseHalfLifeSeconds, _controller.isGrounded, deltaTime);

            Vector3 horizontal = _motion.Impulse;
            horizontal.y = 0f;

            CollisionFlags flags = _controller.Move(velocity * deltaTime);

            // 撞墙速度在墙壁消除它之前读取，使碰撞按原始强度判断，而非停止后的零速度。
            WallImpactSpeed = (flags & CollisionFlags.Sides) != 0 ? horizontal.magnitude : 0f;

            if (WallImpactSpeed > 0f)
            {
                _motion.StopImpulse();
            }
        }

        /// <summary>
        /// 上次移动撞墙时外加推动的水平速度，否则为零。
        /// </summary>
        /// <remarks>
        /// 仅报告，不执行反应。猛烈撞墙导致硬直或架势损失属于战斗决策，Motor 只负责移动身体。
        /// </remarks>
        public float WallImpactSpeed { get; private set; }

        /// <summary>上次移动后身体是否站在某个表面上。</summary>
        public bool IsGrounded => _controller.isGrounded;

        /// <summary>向上或向下速度，单位米每秒。</summary>
        public float VerticalVelocity => _motion.VerticalVelocity;

        /// <summary>向上抛起身体。水平推动仍独立生效。</summary>
        /// <param name="upwardSpeed">初始向上速度，单位米每秒。</param>
        public void Launch(float upwardSpeed) => _motion.Launch(upwardSpeed);

        /// <summary>转向运动方向。</summary>
        /// <param name="deltaTime">经过的缩放时间。</param>
        public void FaceTravelDirection(float deltaTime)
        {
            if (IsStationary)
            {
                return;
            }

            FaceDirection(_planarVelocity, deltaTime);
        }

        /// <summary>按配置转向速率转向世界空间方向。</summary>
        /// <param name="direction">要面向的方向，忽略垂直分量。</param>
        /// <param name="deltaTime">经过的缩放时间。</param>
        public void FaceDirection(Vector3 direction, float deltaTime)
        {
            direction.y = 0f;

            if (direction.sqrMagnitude < Mathf.Epsilon)
            {
                return;
            }

            Quaternion target = Quaternion.LookRotation(direction, Vector3.up);
            _transform.rotation = Quaternion.RotateTowards(
                _transform.rotation, target, _config.TurnSpeedDegreesPerSecond * deltaTime);
        }

        /// <summary>立即面向指定方向，用于确定闪避方向。</summary>
        /// <param name="direction">要面向的方向。</param>
        public void SnapToDirection(Vector3 direction)
        {
            direction.y = 0f;

            if (direction.sqrMagnitude < Mathf.Epsilon)
            {
                return;
            }

            _transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }

        /// <summary>
        /// 根据外部施加的偏移移动身体，例如被另一身体推动。
        /// </summary>
        /// <remarks>
        /// 经过控制器而非直接设置位置，使推向墙的身体停在墙前，而非穿过墙壁。
        /// </remarks>
        /// <param name="offset">要应用的位移，忽略垂直分量。</param>
        public void Displace(Vector3 offset)
        {
            offset.y = 0f;

            if (offset.sqrMagnitude > 0f && _controller.enabled)
            {
                _controller.Move(offset);
            }
        }

        /// <summary>传送角色，移动期间禁用碰撞解析。</summary>
        /// <param name="position">目标位置。</param>
        public void Teleport(Vector3 position)
        {
            // 控制器内部缓存位置，因此移动期间必须禁用，否则下一步会立即跳回原位置。
            _controller.enabled = false;
            _transform.position = position;
            _controller.enabled = true;

            _planarVelocity = Vector3.zero;
            _motion.Reset();
        }

        /// <summary>
        /// 将移动轴转为相机水平面上的世界方向。
        /// </summary>
        /// <remarks>
        /// 生成的相机没有偏航，因此目前相当于恒等映射。仍按相机相对坐标编写，因为若不这样，
        /// 首次为构图旋转相机时会出现难以理解的静默错误。
        /// </remarks>
        /// <param name="moveInput">移动轴，长度最多为一。</param>
        /// <returns>对应的水平面世界空间方向。</returns>
        public Vector3 ToWorldDirection(Vector2 moveInput)
        {
            if (moveInput.sqrMagnitude < Mathf.Epsilon)
            {
                return Vector3.zero;
            }

            var input = new Vector3(moveInput.x, 0f, moveInput.y);

            if (_cameraTransform == null)
            {
                return input;
            }

            // 只使用相机偏航，否则俯仰会使移动方向倾入地面。
            float cameraYaw = _cameraTransform.eulerAngles.y;
            return Quaternion.Euler(0f, cameraYaw, 0f) * input;
        }

        /// <summary>将到达最高速度的时长转为速度变化速率。</summary>
        private float RateFor(float durationSeconds) =>
            durationSeconds <= 0f ? float.MaxValue : _config.MoveSpeed / durationSeconds;
    }
}
