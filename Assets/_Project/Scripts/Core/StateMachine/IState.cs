namespace AdaptiveBossArena.Core.StateMachine
{
    /// <summary>
    /// 状态机可处于的一种独立行为。
    /// </summary>
    /// <remarks>
    /// 状态是普通 C# 对象而非组件，因此可完全在编辑模式测试中构建和推进状态机。上下文每次调用时传入，
    /// 而非构造时捕获，使状态不持有实例级可变连接信息，并可安全共享。
    /// </remarks>
    /// <typeparam name="TContext">状态操作的数据和服务。</typeparam>
    public interface IState<in TContext>
    {
        /// <summary>调试面板显示的可读名称。</summary>
        string Name { get; }

        /// <summary>状态机进入此状态时调用一次。</summary>
        /// <param name="context">状态机上下文。</param>
        void Enter(TContext context);

        /// <summary>此状态激活期间每帧调用一次。</summary>
        /// <param name="context">状态机上下文。</param>
        /// <param name="deltaTime">已经由时间服务缩放的经过时间。</param>
        void Tick(TContext context, float deltaTime);

        /// <summary>此状态激活期间，在物理步调用。</summary>
        /// <param name="context">状态机上下文。</param>
        /// <param name="fixedDeltaTime">物理步时长。</param>
        void FixedTick(TContext context, float fixedDeltaTime);

        /// <summary>状态机离开此状态时调用一次。</summary>
        /// <param name="context">状态机上下文。</param>
        void Exit(TContext context);
    }

    /// <summary>
    /// 使状态机从当前状态进入目标状态的条件。
    /// </summary>
    /// <remarks>
    /// 转移是一等对象，而非状态内部的分支。这使 Boss 行为图保持模块化：
    /// 为新场景新增反击响应只需注册转移，而非修改每个可能需要让出的状态。
    /// </remarks>
    /// <typeparam name="TContext">条件检查的数据和服务。</typeparam>
    public interface ITransition<TContext>
    {
        /// <summary>转移触发后进入的状态。</summary>
        IState<TContext> Target { get; }

        /// <summary>
        /// 评估顺序，较大值先检查。
        /// </summary>
        /// <remarks>
        /// 通过优先级，让死亡或硬直转移可靠地胜过同帧满足条件的普通攻击转移。
        /// </remarks>
        int Priority { get; }

        /// <summary>检查是否应触发此转移。</summary>
        /// <param name="context">状态机上下文。</param>
        /// <returns>应进入 <see cref="Target"/> 时返回 true。</returns>
        bool IsSatisfied(TContext context);
    }
}
