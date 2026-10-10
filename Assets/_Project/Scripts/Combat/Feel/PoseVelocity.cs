using UnityEngine;

namespace AdaptiveBossArena.Combat.Feel
{
    /// <summary>
    /// 根据两个动画姿势求出骨骼移动速度。
    /// </summary>
    /// <remarks>
    /// 若不明确指定，物理系统会让布娃娃身体从静止开始，因此挥击中死亡的战斗者会突然停止并垂直下落，
    /// 仿佛动画被拔掉。将最后几个动画帧中骨骼的速度交给各身体，就能让挥击、硬直或突进延续到倒下过程。
    /// </remarks>
    public static class PoseVelocity
    {
        /// <summary>继承速度的上限，防止异常帧的跳变将四肢甩飞。</summary>
        public const float MaximumLinearSpeed = 8f;

        /// <summary>继承旋转速度的上限，单位为弧度每秒，原因相同。</summary>
        public const float MaximumAngularSpeed = 20f;

        /// <summary>两个位置之间的线速度。</summary>
        /// <param name="previous">前一个姿势中的位置。</param>
        /// <param name="current">后一个姿势中的位置。</param>
        /// <param name="seconds">两个姿势之间的时间；为零（例如冻结帧）时不产生速度。</param>
        /// <returns>速度，上限为 <see cref="MaximumLinearSpeed"/>。</returns>
        public static Vector3 Linear(Vector3 previous, Vector3 current, float seconds)
        {
            if (seconds <= Mathf.Epsilon)
            {
                return Vector3.zero;
            }

            return Vector3.ClampMagnitude((current - previous) / seconds, MaximumLinearSpeed);
        }

        /// <summary>两个旋转之间绕世界轴的角速度，单位为弧度每秒。</summary>
        /// <param name="previous">前一个姿势中的旋转。</param>
        /// <param name="current">后一个姿势中的旋转。</param>
        /// <param name="seconds">两个姿势之间的时间，为零时不产生速度。</param>
        /// <returns>角速度，上限为 <see cref="MaximumAngularSpeed"/>。</returns>
        public static Vector3 Angular(Quaternion previous, Quaternion current, float seconds)
        {
            if (seconds <= Mathf.Epsilon)
            {
                return Vector3.zero;
            }

            Quaternion delta = current * Quaternion.Inverse(previous);
            delta.ToAngleAxis(out float degrees, out Vector3 axis);

            // ToAngleAxis 将小幅反向旋转报告为几乎整圈的正向旋转；应取较短路径。
            if (degrees > 180f)
            {
                degrees -= 360f;
            }

            if (float.IsNaN(axis.x) || float.IsInfinity(axis.x) || Mathf.Approximately(degrees, 0f))
            {
                return Vector3.zero;
            }

            return Vector3.ClampMagnitude(axis.normalized * (degrees * Mathf.Deg2Rad / seconds), MaximumAngularSpeed);
        }
    }
}
