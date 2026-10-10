using System.ComponentModel;

// ReSharper disable once CheckNamespace - 此类型必须位于该命名空间才能生效。
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// 编译器所需标记，支持仅init的访问器和位置record。
    /// </summary>
    /// <remarks>
    /// Unity的C#9引用程序集缺少该类型，因此<c>init</c>或<c>record</c>会出现预定义
    /// System.Runtime.CompilerServices.IsExternalInit未定义或未导入的编译错误。自行声明是兼容方案，
    /// 让项目使用惯常不可变数据类型，不产生运行时代码或成本。
    /// </remarks>
    /// <remarks>
    /// 在依赖图最底层程序集公开声明且只声明一次。internal只能支持Utilities内部的<c>init</c>；
    /// 其他程序集再声明一次会导致类型歧义。
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class IsExternalInit
    {
    }
}
