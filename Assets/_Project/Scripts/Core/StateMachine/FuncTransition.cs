using System;

namespace AdaptiveBossArena.Core.StateMachine
{
    /// <summary>
    /// 由委托提供条件的状态转移。
    /// </summary>
    /// <remarks>
    /// 适用于多数条件只需对上下文计算一个表达式、无需独立命名类型的情况。
    /// 携带调参数据或需要编辑为资源的条件应直接实现 <see cref="ITransition{TContext}"/>。
    /// </remarks>
    /// <typeparam name="TContext">条件检查的数据和服务。</typeparam>
    public sealed class FuncTransition<TContext> : ITransition<TContext>
    {
        private readonly Func<TContext, bool> _condition;

        /// <summary>创建由委托驱动的转移。</summary>
        /// <param name="target">要进入的状态。</param>
        /// <param name="condition">决定是否触发的谓词。</param>
        /// <param name="priority">评估顺序，值越大越先检查。</param>
        /// <exception cref="ArgumentNullException">缺少目标或条件时抛出。</exception>
        public FuncTransition(IState<TContext> target, Func<TContext, bool> condition, int priority = 0)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            _condition = condition ?? throw new ArgumentNullException(nameof(condition));
            Priority = priority;
        }

        /// <inheritdoc />
        public IState<TContext> Target { get; }

        /// <inheritdoc />
        public int Priority { get; }

        /// <inheritdoc />
        public bool IsSatisfied(TContext context) => _condition(context);
    }
}
