using System;
using System.Collections.Generic;

namespace AdaptiveBossArena.Core.StateMachine
{
    /// <summary>
    /// 带每状态转移表和全局转移表的有限状态机。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 转移由状态机持有而非放在状态内部，因此可不修改状态便重连行为。这对 Boss很重要：
    /// 开始招架重击的反制策略在运行时注册转移，现有攻击状态无需感知变化。
    /// </para>
    /// <para>
    /// 全局转移优先于每状态转移，因此死亡或架势击破等始终应获胜的条件无需在每个状态重复。
    /// </para>
    /// <para>
    /// 构建完成后更新不产生分配。进入状态时缓存转移列表，而非每帧查字典。
    /// </para>
    /// </remarks>
    /// <typeparam name="TContext">状态机及状态操作的数据和服务。</typeparam>
    public sealed class StateMachine<TContext>
    {
        private static readonly List<ITransition<TContext>> EmptyTransitions =
            new List<ITransition<TContext>>();

        private readonly TContext _context;

        private readonly Dictionary<IState<TContext>, List<ITransition<TContext>>> _transitions =
            new Dictionary<IState<TContext>, List<ITransition<TContext>>>();

        private readonly List<ITransition<TContext>> _globalTransitions =
            new List<ITransition<TContext>>();

        private List<ITransition<TContext>> _currentTransitions = EmptyTransitions;

        /// <summary>创建状态机并进入初始状态。</summary>
        /// <param name="context">传递给各状态回调的上下文。</param>
        /// <param name="initialState">初始状态。</param>
        /// <exception cref="ArgumentNullException">未提供初始状态时抛出。</exception>
        public StateMachine(TContext context, IState<TContext> initialState)
        {
            _context = context;
            CurrentState = initialState ?? throw new ArgumentNullException(nameof(initialState));
            CurrentState.Enter(_context);
        }

        /// <summary>状态机当前所处的状态。</summary>
        public IState<TContext> CurrentState { get; private set; }

        /// <summary>当前状态已经过的缩放时间秒数。</summary>
        public float TimeInCurrentState { get; private set; }

        /// <summary>
        /// 转移完成后触发，携带离开的状态和进入的状态。
        /// </summary>
        /// <remarks>用于调试面板和与阶段变化关联的音频提示。</remarks>
        public event Action<IState<TContext>, IState<TContext>> StateChanged;

        /// <summary>注册仅在指定状态激活时适用的转移。</summary>
        /// <param name="from">转移离开的状态。</param>
        /// <param name="transition">要注册的转移。</param>
        /// <exception cref="ArgumentNullException">任一参数缺失时抛出。</exception>
        public void AddTransition(IState<TContext> from, ITransition<TContext> transition)
        {
            if (from == null)
            {
                throw new ArgumentNullException(nameof(from));
            }

            if (transition == null)
            {
                throw new ArgumentNullException(nameof(transition));
            }

            if (!_transitions.TryGetValue(from, out List<ITransition<TContext>> list))
            {
                list = new List<ITransition<TContext>>();
                _transitions[from] = list;
            }

            InsertByPriority(list, transition);

            // 当前状态的转移列表已有缓存，因此战斗中新增转移时必须刷新缓存才能生效；
            // 适应正会执行这种操作。
            if (ReferenceEquals(from, CurrentState))
            {
                _currentTransitions = list;
            }
        }

        /// <summary>通过委托条件注册转移。</summary>
        /// <param name="from">转移离开的状态。</param>
        /// <param name="to">要进入的状态。</param>
        /// <param name="condition">决定是否触发的谓词。</param>
        /// <param name="priority">评估顺序，值越大越先检查。</param>
        public void AddTransition(
            IState<TContext> from,
            IState<TContext> to,
            Func<TContext, bool> condition,
            int priority = 0) =>
            AddTransition(from, new FuncTransition<TContext>(to, condition, priority));

        /// <summary>注册不受当前状态限制的转移。</summary>
        /// <param name="transition">要注册的转移。</param>
        /// <exception cref="ArgumentNullException">未提供转移时抛出。</exception>
        public void AddGlobalTransition(ITransition<TContext> transition)
        {
            if (transition == null)
            {
                throw new ArgumentNullException(nameof(transition));
            }

            InsertByPriority(_globalTransitions, transition);
        }

        /// <summary>通过委托条件注册全局转移。</summary>
        /// <param name="to">要进入的状态。</param>
        /// <param name="condition">决定是否触发的谓词。</param>
        /// <param name="priority">评估顺序，值越大越先检查。</param>
        public void AddGlobalTransition(IState<TContext> to, Func<TContext, bool> condition, int priority = 0) =>
            AddGlobalTransition(new FuncTransition<TContext>(to, condition, priority));

        /// <summary>评估转移，然后更新转移后的状态。</summary>
        /// <param name="deltaTime">经过的缩放时间。</param>
        public void Tick(float deltaTime)
        {
            IState<TContext> next = ResolveTransition();
            if (next != null)
            {
                ChangeState(next);
            }

            TimeInCurrentState += deltaTime;
            CurrentState.Tick(_context, deltaTime);
        }

        /// <summary>在物理步更新当前状态，此处不评估转移。</summary>
        /// <param name="fixedDeltaTime">物理步时长。</param>
        public void FixedTick(float fixedDeltaTime) => CurrentState.FixedTick(_context, fixedDeltaTime);

        /// <summary>
        /// 立即进入状态，绕过转移条件。
        /// </summary>
        /// <remarks>
        /// 仅供死亡或脚本阶段变化等权威事件使用。常规行为必须通过转移，
        /// 否则状态图不再描述 Boss 能执行的行为。
        /// </remarks>
        /// <param name="state">要进入的状态。</param>
        /// <exception cref="ArgumentNullException">未提供状态时抛出。</exception>
        public void ForceState(IState<TContext> state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            ChangeState(state);
        }

        /// <summary>找出满足条件且优先级最高的转移，没有则返回 null。</summary>
        private IState<TContext> ResolveTransition()
        {
            foreach (ITransition<TContext> transition in _globalTransitions)
            {
                if (!ReferenceEquals(transition.Target, CurrentState) && transition.IsSatisfied(_context))
                {
                    return transition.Target;
                }
            }

            foreach (ITransition<TContext> transition in _currentTransitions)
            {
                if (transition.IsSatisfied(_context))
                {
                    return transition.Target;
                }
            }

            return null;
        }

        /// <summary>执行退出、切换和进入，然后通知监听者。</summary>
        private void ChangeState(IState<TContext> next)
        {
            IState<TContext> previous = CurrentState;

            previous.Exit(_context);
            CurrentState = next;
            TimeInCurrentState = 0f;

            _currentTransitions = _transitions.TryGetValue(next, out List<ITransition<TContext>> list)
                ? list
                : EmptyTransitions;

            CurrentState.Enter(_context);
            StateChanged?.Invoke(previous, next);
        }

        /// <summary>插入转移，保持列表按优先级降序排列。</summary>
        private static void InsertByPriority(
            List<ITransition<TContext>> list,
            ITransition<TContext> transition)
        {
            int index = 0;
            while (index < list.Count && list[index].Priority >= transition.Priority)
            {
                index++;
            }

            list.Insert(index, transition);
        }
    }
}
