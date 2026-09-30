using System;
using System.Reflection;

namespace Loopstructor.AutoPlayer.Plugin;

/// <summary>
/// 绑定游戏侧运行时契约的窄接口。游戏每次更新都可能新增重载或改名，
/// 因此这里的每个查询都必须“要么绑定到唯一确定的成员，要么安静地报告缺失”，
/// 绝不允许把反射异常抛进 AutoPlayer 运行时的启动路径。
/// </summary>
internal static class ReflectionContractBinder
{
    /// <summary>
    /// 按名称与参数类型精确绑定一个公开实例方法。
    /// 不使用 <c>Type.GetMethod(名称, 标志)</c>：只要游戏为同名方法新增一个重载，
    /// 该重载就会抛 <see cref="AmbiguousMatchException"/>。任何参数类型缺失时返回 null，
    /// 由调用方按“契约缺失”处理。
    /// </summary>
    internal static MethodInfo? ResolveMethod(Type? declaringType, string name, params Type?[] parameterTypes)
    {
        if (declaringType == null || parameterTypes == null) return null;

        Type[] required = new Type[parameterTypes.Length];
        for (int index = 0; index < parameterTypes.Length; index++)
        {
            if (parameterTypes[index] == null) return null;
            required[index] = parameterTypes[index]!;
        }

        foreach (MethodInfo candidate in declaringType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!string.Equals(candidate.Name, name, StringComparison.Ordinal)) continue;
            ParameterInfo[] parameters = candidate.GetParameters();
            if (parameters.Length != required.Length) continue;

            bool matched = true;
            for (int index = 0; index < required.Length; index++)
            {
                if (parameters[index].ParameterType != required[index])
                {
                    matched = false;
                    break;
                }
            }

            if (matched) return candidate;
        }

        return null;
    }

    /// <summary>
    /// 探测一项可选能力。探测本身抛出的反射异常会被降级为“能力不可用”，
    /// 并作为缺失说明返回给调用方；这样单个能力探测失败不会再终止整个运行时。
    /// </summary>
    internal static bool TryProbeCapability(Func<bool> probe, string capability, out string missing)
    {
        if (probe == null)
        {
            missing = capability;
            return false;
        }

        try
        {
            if (probe())
            {
                missing = string.Empty;
                return true;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            missing = capability + "（探测异常：" + exception.GetType().Name + "：" + exception.Message + "）";
            return false;
        }

        missing = capability;
        return false;
    }
}
