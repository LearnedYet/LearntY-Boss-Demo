using System;
using System.Collections.Generic;

namespace AdaptiveBossArena.Core.Services
{
    /// <summary>
    /// 按接口作为键的最小容器，由组合根在场景启动时填充。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 刻意不使用完整依赖注入框架。项目只有约十余个长期服务，
    /// 自动解析构造函数和管理生命周期的容器会引入不必要的复杂性。
    /// </para>
    /// <para>
    /// 依赖可直接传递时优先使用构造函数或检查器注入。只有 Unity对象模型阻碍直接传递时才使用注册表，
    /// 常见情况是Unity 实例化的组件无法接收构造函数参数。
    /// </para>
    /// </remarks>
    public sealed class ServiceRegistry
    {
        private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

        /// <summary>
        /// 运行场景正在使用的注册表。
        /// </summary>
        /// <remarks>
        /// 项目唯一的全局访问点，并刻意保持唯一。访问它的对象应仅因为由 Unity实例化，
        /// 且没有其他途径传入依赖。
        /// </remarks>
        public static ServiceRegistry Current { get; private set; } = new ServiceRegistry();

        /// <summary>设置当前注册表，替换此前的注册表。</summary>
        /// <param name="registry">要安装的注册表。</param>
        /// <exception cref="ArgumentNullException">未提供注册表时抛出。</exception>
        public static void SetCurrent(ServiceRegistry registry) =>
            Current = registry ?? throw new ArgumentNullException(nameof(registry));

        /// <summary>当前注册的服务数。</summary>
        public int Count => _services.Count;

        /// <summary>按服务接口注册实现。</summary>
        /// <typeparam name="TService">使用者查询服务时采用的接口。</typeparam>
        /// <param name="implementation">要提供的实例。</param>
        /// <exception cref="ArgumentNullException">未提供实现时抛出。</exception>
        /// <exception cref="InvalidOperationException">服务已经注册时抛出。</exception>
        public void Register<TService>(TService implementation) where TService : class
        {
            if (implementation == null)
            {
                throw new ArgumentNullException(nameof(implementation));
            }

            Type key = typeof(TService);
            if (_services.ContainsKey(key))
            {
                // 静默替换会掩盖场景中重复管理器的问题，产生只有场景重载后才出现的错误。
                throw new InvalidOperationException(
                    $"Service '{key.Name}' is already registered. Unregister it first if replacement is intended.");
            }

            _services.Add(key, implementation);
        }

        /// <summary>注册服务，替换已有注册。</summary>
        /// <typeparam name="TService">使用者查询服务时采用的接口。</typeparam>
        /// <param name="implementation">要提供的实例。</param>
        public void RegisterOrReplace<TService>(TService implementation) where TService : class
        {
            if (implementation == null)
            {
                throw new ArgumentNullException(nameof(implementation));
            }

            _services[typeof(TService)] = implementation;
        }

        /// <summary>解析已注册的服务。</summary>
        /// <typeparam name="TService">要解析的接口。</typeparam>
        /// <returns>已注册的实现。</returns>
        /// <exception cref="InvalidOperationException">服务从未注册时抛出。</exception>
        public TService Get<TService>() where TService : class
        {
            if (_services.TryGetValue(typeof(TService), out object service))
            {
                return (TService)service;
            }

            throw new InvalidOperationException(
                $"Service '{typeof(TService).Name}' was not registered. " +
                "Check that the composition root runs before the consumer's initialisation.");
        }

        /// <summary>尝试解析服务，服务不存在时不抛出异常。</summary>
        /// <typeparam name="TService">要解析的接口。</typeparam>
        /// <param name="service">接收实现，或为 null。</param>
        /// <returns>服务已注册时为 true。</returns>
        public bool TryGet<TService>(out TService service) where TService : class
        {
            if (_services.TryGetValue(typeof(TService), out object found))
            {
                service = (TService)found;
                return true;
            }

            service = null;
            return false;
        }

        /// <summary>移除注册。</summary>
        /// <typeparam name="TService">要注销的接口。</typeparam>
        /// <returns>成功移除注册时为 true。</returns>
        public bool Unregister<TService>() where TService : class => _services.Remove(typeof(TService));

        /// <summary>移除全部注册。</summary>
        public void Clear() => _services.Clear();
    }
}
