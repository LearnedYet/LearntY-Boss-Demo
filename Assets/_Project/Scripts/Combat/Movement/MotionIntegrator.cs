using UnityEngine;

namespace AdaptiveBossArena.Combat.Movement
{
    /// <summary>
    /// 每帧将主动移动、外部冲量和重力合成为一个速度。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 过去两个移动器都只有一个可被任何对象覆盖的水平速度。主动移动、突进和击退都写同一字段，
    /// 最后写入者生效：命中帧施加的击退，会在下一帧被受击者的移动输入替换，而首领则从不响应击退。
    /// 无法推动任何人的打击会显得毫无重量。
    /// </para>
    /// <para>
    /// 这里将角色<em>被施加的运动</em>与角色自身<em>正在进行的运动</em>分开。
    /// 状态代码仍和以前一样设置主动移动速度。冲量只会累加，按自身半衰期衰减，
    /// 且不会被任何次数的主动移动写入覆盖——因此推力无论受击者是否按住方向都会继续。
    /// 垂直速度采用实际重力，落地时维持较小的向下速度，让控制器接地保持稳定。
    /// </para>
    /// <para>
    /// 采用纯 C#，没有引擎调用，放在玩家和首领移动器都已引用的 Combat程序集内共用。
    /// 两个移动器仍保持为不同类型，因为它们的操控需要相互协调调参；只有这里的计算共用。
    /// </para>
    /// </remarks>
    public sealed class MotionIntegrator
    {
        /// <summary>低于此速度的冲量视为耗尽并清除。</summary>
        private const float SpentImpulseSpeed = 0.01f;

        private readonly float _gravity;
        private readonly float _groundedSpeed;

        private Vector3 _impulse;
        private float _verticalVelocity;

        /// <summary>创建积分器。</summary>
        /// <param name="gravity">向下加速度，以正数表示，单位为米每平方秒。</param>
        /// <param name="groundedSpeed">
        /// 落地时保持的向下速度，以负数表示。让控制器贴住地面，而不累积会影响下次坠落初速的速度。
        /// </param>
        public MotionIntegrator(float gravity, float groundedSpeed)
        {
            _gravity = Mathf.Max(0f, gravity);
            _groundedSpeed = Mathf.Min(0f, groundedSpeed);
            _verticalVelocity = _groundedSpeed;
        }

        /// <summary>仍在持续的外加水平速度。</summary>
        public Vector3 Impulse => _impulse;

        /// <summary>当前垂直速度。</summary>
        public float VerticalVelocity => _verticalVelocity;

        /// <summary>
        /// 添加外加水平速度，例如击退。
        /// </summary>
        /// <remarks>
        /// 累加而不是赋值，因此短时间内连续两次打击比一次推得更远。丢弃垂直分量；击飞是单独的、
        /// 明确的操作。
        /// </remarks>
        /// <param name="velocity">要添加的速度。</param>
        public void AddImpulse(Vector3 velocity)
        {
            _impulse.x += velocity.x;
            _impulse.z += velocity.z;
        }

        /// <summary>将身体向上抛起，替换原有垂直速度。</summary>
        /// <remarks>
        /// 替换而不是累加：站在地面的身体带有较小的向下接地速度，累加会让同样的击飞每次都稍低一点。
        /// </remarks>
        /// <param name="upwardSpeed">初始向上速度，单位为米每秒。</param>
        public void Launch(float upwardSpeed)
        {
            _verticalVelocity = Mathf.Max(0f, upwardSpeed);
        }

        /// <summary>丢弃外加推力，例如墙壁挡住被它推动的身体时。</summary>
        public void StopImpulse()
        {
            _impulse = Vector3.zero;
        }

        /// <summary>清除所有外加速度，用于传送或重试。</summary>
        public void Reset()
        {
            _impulse = Vector3.zero;
            _verticalVelocity = _groundedSpeed;
        }

        /// <summary>
        /// 推进外加运动，并返回本帧移动所需的完整速度。
        /// </summary>
        /// <param name="steeredVelocity">角色自身的主动运动，忽略其垂直分量。</param>
        /// <param name="impulseHalfLifeSeconds">
        /// 冲量速度减半所需的时间。更重的身体会更快消减推力。
        /// </param>
        /// <param name="isGrounded">角色上次移动后是否位于地面。</param>
        /// <param name="deltaTime">经过缩放的时间增量。</param>
        /// <returns>合成速度。</returns>
        public Vector3 Step(Vector3 steeredVelocity, float impulseHalfLifeSeconds, bool isGrounded, float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return new Vector3(steeredVelocity.x + _impulse.x, 0f, steeredVelocity.z + _impulse.z);
            }

            // 在衰减前采样，让推力在命中当帧就能移动受击者。
            Vector3 imposed = _impulse;
            DecayImpulse(impulseHalfLifeSeconds, deltaTime);
            AdvanceVertical(isGrounded, deltaTime);

            return new Vector3(
                steeredVelocity.x + imposed.x,
                _verticalVelocity,
                steeredVelocity.z + imposed.z);
        }

        /// <summary>按半衰期指数衰减，让结果不依赖帧率。</summary>
        private void DecayImpulse(float halfLifeSeconds, float deltaTime)
        {
            if (halfLifeSeconds <= 0f)
            {
                _impulse = Vector3.zero;
                return;
            }

            _impulse *= Mathf.Pow(0.5f, deltaTime / halfLifeSeconds);

            if (_impulse.sqrMagnitude < SpentImpulseSpeed * SpentImpulseSpeed)
            {
                _impulse = Vector3.zero;
            }
        }

        private void AdvanceVertical(bool isGrounded, float deltaTime)
        {
            // 落地且不在上升时：保持较小的接地速度，而不累积重力。静止站立一分钟的角色，
            // 不应在离开地面的首帧就以终端速度跌下台阶。
            if (isGrounded && _verticalVelocity <= 0f)
            {
                _verticalVelocity = _groundedSpeed;
                return;
            }

            _verticalVelocity -= _gravity * deltaTime;
        }
    }
}
