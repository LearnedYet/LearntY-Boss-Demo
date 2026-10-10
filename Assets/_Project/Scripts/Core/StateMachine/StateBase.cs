namespace AdaptiveBossArena.Core.StateMachine
{
    /// <summary>
    /// 便捷基类，使各状态回调都可选择性重写。
    /// </summary>
    /// <remarks>
    /// 多数状态只关心五个成员中的两三个。继承此类可让具体状态仅保留其特有逻辑，
    /// 并提供攻击时序和预警时长依赖的状态计时。
    /// </remarks>
    /// <typeparam name="TContext">状态操作的数据和服务。</typeparam>
    public abstract class StateBase<TContext> : IState<TContext>
    {
        /// <inheritdoc />
        public virtual string Name => GetType().Name;

        /// <summary>进入此状态后经过的缩放时间秒数。</summary>
        public float TimeInState { get; private set; }

        /// <inheritdoc />
        public void Enter(TContext context)
        {
            TimeInState = 0f;
            OnEnter(context);
        }

        /// <inheritdoc />
        public void Tick(TContext context, float deltaTime)
        {
            TimeInState += deltaTime;
            OnTick(context, deltaTime);
        }

        /// <inheritdoc />
        public virtual void FixedTick(TContext context, float fixedDeltaTime)
        {
        }

        /// <inheritdoc />
        public void Exit(TContext context) => OnExit(context);

        /// <summary>重写以执行进入逻辑。</summary>
        /// <param name="context">状态机上下文。</param>
        protected virtual void OnEnter(TContext context)
        {
        }

        /// <summary>重写以执行每帧逻辑。</summary>
        /// <param name="context">状态机上下文。</param>
        /// <param name="deltaTime">经过的缩放时间。</param>
        protected virtual void OnTick(TContext context, float deltaTime)
        {
        }

        /// <summary>重写以执行退出逻辑。</summary>
        /// <param name="context">状态机上下文。</param>
        protected virtual void OnExit(TContext context)
        {
        }
    }
}
