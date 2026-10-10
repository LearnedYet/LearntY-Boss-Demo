namespace AdaptiveBossArena.Core.Services
{
    /// <summary>
    /// 接收相机震动请求。
    /// </summary>
    /// <remarks>
    /// 通过抽象让战斗代码无需持有相机引用就能请求打击反馈，也让玩家在设置中降低或关闭震动时，
    /// 只需在一处处理，而非每个调用处分别判断。
    /// </remarks>
    public interface IScreenShake
    {
        /// <summary>
        /// 增加震动强度，强度会自行衰减。
        /// </summary>
        /// <remarks>
        /// 强度采用累积而非重启，因此同时发生的多次命中会形成较大震动，而非多个互相干扰的小震动。
        /// </remarks>
        /// <param name="amount">要增加的震动强度，轻击约 0.1，重击约 0.5。</param>
        void AddTrauma(float amount);

        /// <summary>
        /// 短暂将相机推向动作中心，是定向推镜而非震动。
        /// </summary>
        /// <remarks>
        /// 震动使画面抖动，推镜则使整个视野冲入再弹回。仅用于最有分量的时刻，如重击命中或精准弹反，
        /// 使额外强调表现为打击而非噪声。与震动使用相同无障碍强度倍率。
        /// </remarks>
        /// <param name="amount">推镜强度，与震动强度采用近似相同尺度。</param>
        void Punch(float amount);

        /// <summary>
        /// 短暂改变相机视野角，然后弹回。
        /// </summary>
        /// <remarks>
        /// 正值在闪避等速度爆发时扩大视野；负值在完美闪避后的慢动作等专注时刻收窄视野。
        /// 同样受震动无障碍倍率控制，因此降低相机运动也会减弱它。
        /// </remarks>
        /// <param name="degrees">带符号的视野角变化，正值扩大，负值收窄。</param>
        void PunchFov(float degrees);

        /// <summary>
        /// 按无障碍设置缩放全部震动和推镜。
        /// </summary>
        /// <remarks>
        /// 零完全关闭相机运动，对部分玩家这是需要而非偏好。保留在接口上，使设置菜单可经服务注册表访问，
        /// 而无需引用具体相机。
        /// </remarks>
        /// <param name="intensity">零到一的倍率，零表示关闭。</param>
        void SetUserIntensity(float intensity);

        /// <summary>立即停止震动。</summary>
        void ClearTrauma();
    }
}
