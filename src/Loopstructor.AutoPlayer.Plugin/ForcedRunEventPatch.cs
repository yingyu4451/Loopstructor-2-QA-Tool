using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Loopstructor.AutoPlayer.Plugin;

/// <summary>
/// 「直接触发」在任意节点沙盒化开启新版事件时，屏蔽事件收尾对当前波次 / 奖励 / 地图节点的影响。
/// 只在 <see cref="ActiveFlow"/> 存活期间生效；补丁安装失败时直接触发会明确失败，不影响其它命令。
/// </summary>
internal static class ForcedRunEventPatch
{
    private const string HarmonyId = "com.ponegames.loopstructor2.forced-runevent";
    private const string RunModeRuntimeTypeName = "MetroTD.GameModeSystem.Whitebox.RunModeRuntime";
    private const string FlowTypeName = "MetroTD.GameModeSystem.Whitebox.RunEventFlowController";

    private static readonly object Sync = new();
    private static Harmony? s_harmony;
    private static bool s_installed;
    private static string s_installError = string.Empty;

    /// <summary>当前由工具直接开启、需要屏蔽收尾副作用的事件流程。</summary>
    internal static UnityEngine.Object? ActiveFlow { get; set; }

    private static bool Suppressed => ActiveFlow is UnityEngine.Object flow && flow != null;

    /// <summary>安装一次收尾屏蔽补丁。失败会缓存原因，供命令返回明确错误。</summary>
    internal static bool TryInstall(out string error)
    {
        lock (Sync)
        {
            if (s_installed)
            {
                error = string.Empty;
                return true;
            }

            if (!string.IsNullOrEmpty(s_installError))
            {
                error = s_installError;
                return false;
            }

            try
            {
                Type? waveProgress = FindType("MetroTD.RoomSystem.WaveProgressController");
                Type? waveDuration = FindType("MetroTD.RoomSystem.WaveDurationController");
                Type? flow = FindType(FlowTypeName);
                Type? session = FindType("MetroTD.GameModeSystem.Whitebox.RunWhiteboxSession");
                Type? runtime = FindType(RunModeRuntimeTypeName);
                if (waveProgress == null || waveDuration == null || flow == null || session == null || runtime == null)
                {
                    s_installError = "当前游戏版本缺少事件沙盒需要的收尾入口。";
                    error = s_installError;
                    return false;
                }

                Harmony harmony = new(HarmonyId);
                int patched = 0;
                patched += Patch(harmony, FindMethod(waveProgress, "RestoreRunEventState", 1), nameof(Skip));
                patched += Patch(harmony, FindMethod(waveProgress, "CompleteRunEventWithRewards", 3), nameof(Skip));
                patched += Patch(harmony, FindMethod(waveDuration, "CompleteRunEventLayer", 1), nameof(Skip));
                patched += Patch(harmony, FindMethod(waveDuration, "CompleteRunEventLayer", 2), nameof(Skip));
                patched += Patch(harmony, FindMethod(flow, "PrepareBattle", 3), nameof(FinishInsteadOfBattle));
                patched += Patch(harmony, FindMethod(session, "RecordEventGroupEntry", 2), nameof(Skip));
                patched += Patch(harmony, FindMethod(session, "RecordFlowRescueChoice", 4), nameof(Skip));
                patched += Patch(harmony, FindMethod(runtime, "HandleEventFailureReturn", 0), nameof(FinishFailureInsteadOfReturn));
                if (patched < 7)
                {
                    s_installError = "当前游戏版本的事件收尾入口不完整，无法安全地直接触发。";
                    error = s_installError;
                    return false;
                }

                s_harmony = harmony;
                s_installed = true;
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                s_installError = "安装事件沙盒补丁失败：" + exception.Message;
                error = s_installError;
                return false;
            }
        }
    }

    private static int Patch(Harmony harmony, MethodInfo? target, string prefixName)
    {
        if (target == null) return 0;
        MethodInfo prefix = typeof(ForcedRunEventPatch).GetMethod(prefixName, BindingFlags.NonPublic | BindingFlags.Static)
                            ?? throw new MissingMethodException(typeof(ForcedRunEventPatch).FullName, prefixName);
        harmony.Patch(target, prefix: new HarmonyMethod(prefix));
        return 1;
    }

    private static MethodInfo? FindMethod(Type type, string name, int parameterCount) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == name && method.GetParameters().Length == parameterCount);

    /// <summary>收尾副作用：强制事件存活期间跳过波次、奖励与节点恢复。</summary>
    private static bool Skip() => !Suppressed;

    /// <summary>事件选项进入内部战斗：沙盒里没有真实节点，直接结束流程而不是开战。</summary>
    private static bool FinishInsteadOfBattle(object __instance)
    {
        if (!Suppressed) return true;
        EndForcedFlow(__instance);
        return false;
    }

    /// <summary>沙盒事件异常时不要按 Player 规则结算/回基地：收掉流程，保持当前对局。</summary>
    private static bool FinishFailureInsteadOfReturn()
    {
        if (!Suppressed) return true;
        if (ActiveFlow != null) EndForcedFlow(ActiveFlow);
        return false;
    }

    internal static void EndForcedFlow(object flow)
    {
        ActiveFlow = null;
        try
        {
            UnfreezeMap(flow);
            Type type = flow.GetType();
            PropertyInfo? state = type.GetProperty("State", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo? setter = state?.GetSetMethod(true);
            if (setter != null && state!.PropertyType.IsEnum)
            {
                setter.Invoke(flow, new[] { Enum.Parse(state.PropertyType, "Finished") });
            }

            Type? runtimeType = FindType(RunModeRuntimeTypeName);
            object? runtime = runtimeType?
                .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?
                .GetValue(null);
            MethodInfo? release = runtime?.GetType().GetMethod(
                "ReleaseRunEvent",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { type },
                null);
            if (release != null)
            {
                release.Invoke(runtime, new[] { flow });
                return;
            }

            if (flow is Component component)
            {
                UnityEngine.Object.Destroy(component.gameObject);
            }
        }
        catch
        {
            // 结束失败时保留原流程，避免误删其它对象。
        }
    }

    /// <summary>异常收尾不会走 ReleaseUIFreeze；这里补一次地图解冻，避免事件结束后地图仍然被冻结。</summary>
    private static void UnfreezeMap(object flow)
    {
        try
        {
            Type type = flow.GetType();
            FieldInfo? field = type.GetField("m_uiLock", BindingFlags.NonPublic | BindingFlags.Instance);
            object? uiLock = field?.GetValue(flow);
            if (uiLock == null) return;
            uiLock.GetType()
                .GetMethod("UnFreezeLayer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string) }, null)?
                .Invoke(uiLock, new object?[] { "RoomMap" });
            field!.SetValue(flow, null);
        }
        catch
        {
            // 解冻失败不阻断收尾。
        }
    }

    private static Type? FindType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? exact = assembly.GetType(fullName, false);
            if (exact != null) return exact;
        }

        return null;
    }
}
