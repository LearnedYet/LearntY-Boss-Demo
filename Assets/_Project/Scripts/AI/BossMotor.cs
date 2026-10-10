using System;
using AdaptiveBossArena.Combat.Movement;
using AdaptiveBossArena.Core.Constants;
using UnityEngine;

namespace AdaptiveBossArena.AI
{
    /// <summary>
    /// 将 Boss 移动意图转为运动。
    /// </summary>
    /// <remarks>
    /// 与玩家 Motor 几乎相同，却刻意保留独立类型。Boss 转向慢、加速沉重，这些是战斗特性而非偶然差异：
    /// 慢转向让绕行成为可行战术，也是闪避预测适应要应对的压力。共享 Motor 会让双方操控依赖相同调参值，
    /// 悄悄耦合本应相互平衡的两套参数。
    /// </remarks>
    public sealed class BossMotor
    {
        /// <summary>低于此速度时视为静止。</summary>
        private const float StationarySpeedThreshold = 0.05f;

        private readonly CharacterController _controller;
        private readonly Transform _transform;
        private readonly BossConfig _config;

        private Vector3 _planarVelocity;
        private float _speedMultiplier = 1f;

        /// <summary>外加运动与自主转向分开保存，参见玩家 Motor。</summary>
        private readonly MotionIntegrator _motion =
            new MotionIntegrator(GameplayConstants.Gravity, GameplayConstants.GroundedSpeed);

        /// <summary>创建绑定 CharacterController 的 Motor。</summary>
        /// <param name="controller">执行移动和碰撞的控制器。</param>
        /// <param name="config">Boss 移动参数。</param>
        /// <exception cref="ArgumentNullException">控制器或配置缺失时抛出。</exception>
        public BossMotor(CharacterController controller, BossConfig config)
        {
            _controller = controller != null
                ? controller
                : throw new ArgumentNullException(nameof(controller));

            _config = config != null ? config : throw new ArgumentNullException(nameof(config));
            _transform = controller.transform;
        }

        /// <summary>当前水平速度。</summary>
        public Vector3 PlanarVelocity => _planarVelocity;

        /// <summary>击退等外力推动，叠加在角色自主移动之上并持续生效。</summary>
        public Vector3 ImpulseVelocity => _motion.Impulse;

        /// <summary>平滑转向的角速度，跨帧保留。</summary>
        private float _yawVelocity;

        /// <summary>当前水平速率。</summary>
        public float Speed => _planarVelocity.magnitude;

        /// <summary>Boss 实际静止时为 true。</summary>
        public bool IsStationary => Speed < StationarySpeedThreshold;

        /// <summary>Boss 世界坐标。</summary>
        public Vector3 Position => _transform.position;

        /// <summary>Boss 面向的单位向量。</summary>
        public Vector3 Facing => _transform.forward;

        /// <summary>应用当前阶段倍率后的最高速度。</summary>
        public float CurrentTopSpeed => _config.MoveSpeed * _speedMultiplier;

        /// <summary>应用当前生效阶段的移动倍率。</summary>
        /// <param name="multiplier">基础移动速度倍率。</param>
        public void SetSpeedMultiplier(float multiplier) => _speedMultiplier = Mathf.Max(0f, multiplier);

        /// <summary>向目标速度加速：方向向量长度表示最高速度的比例。</summary>
        /// <remarks>
        /// 以前丢弃长度并将所有方向归一化到全速，导致巨兽处处奔跑，包括本应展示重量感的缓慢绕行。
        /// 一表示全速，更长向量也限制为全速。
        /// </remarks>
        /// <param name="direction">要移动的世界方向，长度表示最高速度的比例。</param>
        /// <param name="deltaTime">经过的缩放时间。</param>
        public void MoveInDirection(Vector3 direction, float deltaTime)
        {
            direction.y = 0f;

            Vector3 target = direction.sqrMagnitude > Mathf.Epsilon
                ? Vector3.ClampMagnitude(direction, 1f) * CurrentTopSpeed
                : Vector3.zero;

            float rate = _config.AccelerationSeconds <= 0f
                ? float.MaxValue
                : CurrentTopSpeed / _config.AccelerationSeconds;

            _planarVelocity = Vector3.MoveTowards(_planarVelocity, target, rate * deltaTime);
        }

        /// <summary>按固定速率减速，用于停止超过普通移动速度的突进。</summary>
        /// <remarks>
        /// 普通减速按行走速度缩放，因此以三倍行走速度突进时，Boss 会在自己的攻击期间滑行数米才停下。
        /// </remarks>
        /// <param name="deceleration">每秒损失的速度。</param>
        /// <param name="deltaTime">经过的缩放时间。</param>
        public void Brake(float deceleration, float deltaTime) =>
            _planarVelocity = Vector3.MoveTowards(
                _planarVelocity, Vector3.zero, Mathf.Max(0f, deceleration) * deltaTime);

        /// <summary>使 Boss 停下。</summary>
        /// <param name="deltaTime">经过的缩放时间。</param>
        public void Decelerate(float deltaTime) => MoveInDirection(Vector3.zero, deltaTime);

        /// <summary>直接覆盖 Boss 自主速度，用于突进攻击。</summary>
        /// <param name="velocity">要采用的水平速度。</param>
        public void SetPlanarVelocity(Vector3 velocity) =>
            _planarVelocity = new Vector3(velocity.x, 0f, velocity.z);

        /// <summary>立即停止 Boss 自主移动，外加推动仍会继续。</summary>
        public void Halt() => _planarVelocity = Vector3.zero;

        /// <summary>施加转向输入无法覆盖的击退等外加推动。</summary>
        /// <param name="velocity">要增加的水平速度。</param>
        public void AddImpulse(Vector3 velocity) => _motion.AddImpulse(velocity);

        /// <summary>积分速度并解析碰撞。</summary>
        /// <param name="deltaTime">经过的缩放时间。</param>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            Vector3 velocity = _motion.Step(
                _planarVelocity, _config.ImpulseHalfLifeSeconds, _controller.isGrounded, deltaTime);

            Vector3 horizontal = _planarVelocity + _motion.Impulse;
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
        /// 上次移动撞墙时身体全部水平运动的速度，否则为零。
        /// </summary>
        /// <remarks>
        /// 仅报告，不执行反应。猛烈撞墙导致硬直或架势损失属于战斗决策，Motor 只负责移动身体。
        /// </remarks>
        public float WallImpactSpeed { get; private set; }

        /// <summary>上次移动后身体是否站在某个表面上。</summary>
        public bool IsGrounded => _controller.isGrounded;

        /// <summary>向上或向下速度，单位米每秒。</summary>
        public float VerticalVelocity => _motion.VerticalVelocity;

        /// <summary>
        /// 按配置转向速率旋向指定方向。
        /// </summary>
        /// <remarks>
        /// 刻意设置较低上限，让持续移动的玩家能绕到 Boss 背后；闪避预测反制正是用于缓解该压力。
        /// </remarks>
        /// <param name="direction">要面向的世界方向。</param>
        /// <param name="deltaTime">经过的缩放时间。</param>
        public void FaceDirection(Vector3 direction, float deltaTime)
        {
            direction.y = 0f;

            if (direction.sqrMagnitude < Mathf.Epsilon)
            {
                return;
            }

            // 采用平滑加速而非恒速转向：巨兽逐渐开始和结束转身，这构成其重量感的重要部分。
            // 最高速率不变，因此正面应对和绕行难度与以前相同。
            float target = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            float yaw = Mathf.SmoothDampAngle(
                _transform.eulerAngles.y, target, ref _yawVelocity,
                _config.TurnSmoothingSeconds, _config.TurnSpeedDegreesPerSecond, deltaTime);

            _transform.rotation = Quaternion.Euler(0f, yaw, 0f);
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

        /// <summary>传送 Boss，在战斗重启时使用。</summary>
        /// <param name="position">目标位置。</param>
        public void Teleport(Vector3 position)
        {
            _controller.enabled = false;
            _transform.position = position;
            _controller.enabled = true;

            _planarVelocity = Vector3.zero;
            _motion.Reset();
        }
    }
}
