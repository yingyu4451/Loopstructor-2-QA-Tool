using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Loopstructor.AutoPlayer.Plugin;

internal static class PlatformWriteIsolationPatch
{
    private const int RequiredPatchCount = 4;

    public static bool Applied { get; private set; }
    public static IReadOnlyList<string> PatchedEntries { get; private set; } = Array.Empty<string>();

    public static void Install(Harmony harmony, Action<string> log)
    {
        List<string> patched = new();
        TryPatch(
            harmony,
            "ActFramework_ByHZR.Achievements.Unit.SteamAchievementController",
            "UnlockAchievement",
            new[] { typeof(string) },
            patched);
        TryPatch(
            harmony,
            "ActFramework_ByHZR.MainLoop.Version.PlatformAchievementBridge",
            "ReportIGPAchievement",
            new[] { typeof(string) },
            patched);
        TryPatch(
            harmony,
            "MetroTD.UISystem.SettlementDataTotalManager",
            "TryAutoSendResultOnce",
            new[] { typeof(string) },
            patched);
        TryPatchSteamRestart(harmony, patched);
        PatchedEntries = patched;
        Applied = patched.Count == RequiredPatchCount;
        log(Applied
            ? "已阻止向外部平台上传成就和结算数据：" + string.Join(", ", patched)
            : $"外部写入隔离不完整（{patched.Count}/{RequiredPatchCount}）：" + string.Join(", ", patched));
    }

    private static void TryPatch(
        Harmony harmony,
        string typeName,
        string methodName,
        Type?[] parameterTypes,
        ICollection<string> patched)
    {
        Type? type = AccessTools.TypeByName(typeName);
        MethodInfo? original = ResolvePatchTarget(type, methodName, parameterTypes);
        MethodInfo? prefix = AccessTools.Method(typeof(PlatformWriteIsolationPatch), nameof(Skip));
        if (original == null || prefix == null)
        {
            return;
        }

        harmony.Patch(original, prefix: new HarmonyMethod(prefix));
        patched.Add(typeName + "." + methodName);
    }

    private static void TryPatchSteamRestart(Harmony harmony, ICollection<string> patched)
    {
        const string typeName = "Steamworks.SteamAPI";
        const string methodName = "RestartAppIfNecessary";
        Type? type = AccessTools.TypeByName(typeName);
        MethodInfo? original = ResolvePatchTarget(
            type,
            methodName,
            AccessTools.TypeByName("Steamworks.AppId_t"));
        MethodInfo? prefix = AccessTools.Method(typeof(PlatformWriteIsolationPatch), nameof(SkipSteamRestart));
        if (original == null || prefix == null) return;
        harmony.Patch(original, prefix: new HarmonyMethod(prefix));
        patched.Add(typeName + "." + methodName);
    }

    /// <summary>
    /// 按参数类型精确绑定补丁目标。游戏一旦给同名方法增加重载，
    /// AccessTools.Method(name) 会在重载之间抛 AmbiguousMatchException 并中断补丁安装；
    /// 这里改用项目既有的 ReflectionContractBinder 精确参数绑定，无法解析参数类型时拒绝猜测。
    /// </summary>
    private static MethodInfo? ResolvePatchTarget(Type? type, string methodName, params Type?[] parameterTypes)
    {
        if (type == null || parameterTypes.Length == 0)
        {
            return null;
        }

        foreach (Type? parameterType in parameterTypes)
        {
            if (parameterType == null)
            {
                return null;
            }
        }

        return ReflectionContractBinder.ResolveMethod(type, methodName, parameterTypes)
               ?? ResolveStaticMethod(type, methodName, parameterTypes);
    }

    private static MethodInfo? ResolveStaticMethod(Type type, string methodName, Type?[] parameterTypes)
    {
        MethodInfo? match = null;
        foreach (MethodInfo candidate in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal) ||
                !ParametersMatch(candidate.GetParameters(), parameterTypes))
            {
                continue;
            }

            if (match != null)
            {
                return null;
            }

            match = candidate;
        }

        return match;
    }

    private static bool ParametersMatch(ParameterInfo[] parameters, Type?[] expected)
    {
        if (parameters.Length != expected.Length)
        {
            return false;
        }

        for (int index = 0; index < parameters.Length; index++)
        {
            if (parameters[index].ParameterType != expected[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool Skip() => false;

    private static bool SkipSteamRestart(ref bool __result)
    {
        __result = false;
        return false;
    }
}
