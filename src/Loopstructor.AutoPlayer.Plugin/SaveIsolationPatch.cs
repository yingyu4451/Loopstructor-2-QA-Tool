using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Loopstructor.AutoPlayer.Core;
using UnityEngine;

namespace Loopstructor.AutoPlayer.Plugin;

internal static class SaveIsolationPatch
{
    private const string FlowModeTypeName = "MetroTD.GameModeSystem.Whitebox.RunModeRuntime";
    private const string FlowFactoryTypeName = "MetroTD.FlowSystem.FlowSessionFactory";

    private static readonly object Sync = new();
    private static string s_profileRoot = string.Empty;
    private static MethodInfo? s_saveManagerGetter;
    private static MethodInfo? s_saveManagerGetPath;
    private static bool s_saveManagerResolutionFailed;
    private static MethodInfo? s_flowModeGetter;
    private static bool s_flowModeResolutionFailed;

    public static bool Installed { get; private set; }
    public static bool Applied { get; private set; }
    public static bool Verified { get; private set; }
    public static bool VerificationFailed { get; private set; }
    public static string VerificationError { get; private set; } = string.Empty;
    public static string IsolatedRoot => s_profileRoot;

    public static bool Install(Harmony harmony, string profileRoot, Action<string> log)
    {
        s_profileRoot = Path.GetFullPath(profileRoot);
        Directory.CreateDirectory(s_profileRoot);

        Type? type = AccessTools.TypeByName("ActFramework_ByHZR.Save.SavePathUtility");
        MethodInfo? wrapper = type == null
            ? null
            : AccessTools.Method(type, "GetCompanyAppDataPath", new[] { typeof(bool), typeof(string) });
        MethodInfo? handler = type == null
            ? null
            : AccessTools.Method(type, "GetCompanyAppDataPathHandle", new[] { typeof(bool), typeof(string) });
        MethodInfo? prefix = AccessTools.Method(typeof(SaveIsolationPatch), nameof(Prefix));
        if (wrapper == null || handler == null || prefix == null)
        {
            VerificationFailed = true;
            VerificationError = "存档路径挂钩契约不完整。";
            log(VerificationError);
            return false;
        }

        HashSet<MethodBase> targets = new() { wrapper, handler };
        foreach (MethodBase target in targets)
        {
            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
        }

        InstallFlowIsolation(harmony, log);

        Installed = true;
        log("已为当前激活进程安装存档隔离挂钩：" + s_profileRoot);
        return true;
    }

    /// <summary>
    /// 新版流程存档（PlayerFlow）直接拼 Application.persistentDataPath，不经过 SavePathUtility；
    /// QA 模式必须把这条路径也收进隔离根，否则隔离运行会写进真实存档目录。
    /// 该方法在旧版本里不存在，缺失时只记录日志，不影响 SaveManager 隔离。
    /// </summary>
    private static void InstallFlowIsolation(Harmony harmony, Action<string> log)
    {
        Type? factoryType = AccessTools.TypeByName(FlowFactoryTypeName);
        MethodInfo? resolvePath = factoryType == null
            ? null
            : AccessTools.Method(factoryType, "ResolvePath", new[] { typeof(string), typeof(string) });
        MethodInfo? flowPrefix = AccessTools.Method(typeof(SaveIsolationPatch), nameof(FlowPathPrefix));
        if (resolvePath == null || flowPrefix == null)
        {
            log("当前游戏版本没有可隔离的流程存档入口；流程存档将沿用真实 persistentDataPath。");
            return;
        }

        harmony.Patch(resolvePath, prefix: new HarmonyMethod(flowPrefix));
        log("已为当前激活进程安装流程存档隔离挂钩：" + Path.Combine(s_profileRoot, FlowSaveSlotResolver.FlowDirectoryName));
    }

    private static bool FlowPathPrefix(ref string __result, string slot)
    {
        // 非法标识交回游戏原方法，保持它自己的报错语义。
        if (!FlowSaveSlotResolver.IsValidSlot(slot)) return true;
        __result = FlowSaveSlotResolver.ResolveIsolatedProfilePath(s_profileRoot, slot).Replace('\\', '/');
        return false;
    }

    /// <summary>
    /// Resolves the SaveManager reflection contract exactly once. AccessTools.TypeByName
    /// enumerates every loaded assembly and every type until it finds a match; the player
    /// build measured 75-108 ms per call, so a per-second path probe must never repeat it.
    /// Failures are cached as well: a missing contract must not keep rescanning assemblies.
    /// </summary>
    private static bool TryEnsureSaveManagerHandles()
    {
        if (s_saveManagerGetter != null && s_saveManagerGetPath != null) return true;
        if (s_saveManagerResolutionFailed) return false;

        try
        {
            Type? type = AccessTools.TypeByName("ActFramework_ByHZR.Save.SaveManager");
            MethodInfo? getter = type == null ? null : AccessTools.PropertyGetter(type, "Instance");
            MethodInfo? getPath = type == null ? null : AccessTools.Method(type, "GetSaveFolderPath", Type.EmptyTypes);
            if (getter == null || getPath == null)
            {
                s_saveManagerResolutionFailed = true;
                return false;
            }

            s_saveManagerGetter = getter;
            s_saveManagerGetPath = getPath;
            return true;
        }
        catch (Exception)
        {
            s_saveManagerResolutionFailed = true;
            return false;
        }
    }

    public static void ProbeRuntimeSaveFolder()
    {
        lock (Sync)
        {
            if (Verified || VerificationFailed || !Installed) return;

            try
            {
                if (!TryEnsureSaveManagerHandles())
                {
                    VerificationFailed = true;
                    VerificationError = "SaveManager 验证契约不可用。";
                    return;
                }

                object? instance = s_saveManagerGetter!.Invoke(null, null);
                if (instance == null) return;
                string? actualPath = s_saveManagerGetPath!.Invoke(instance, null) as string;
                if (string.IsNullOrWhiteSpace(actualPath)) return;

                string fullPath = Path.GetFullPath(actualPath);
                if (!IsInside(fullPath, s_profileRoot))
                {
                    VerificationFailed = true;
                    VerificationError = "SaveManager 解析到隔离存档之外的路径：" + fullPath;
                    return;
                }

                // The runtime path is the safety boundary. Mark the hook as applied
                // after the probe confirms the effective SaveManager destination.
                Applied = true;
                Verified = true;
                VerificationError = string.Empty;
            }
            catch (TargetInvocationException)
            {
                // SaveManager is not ready during the first scene's initialization.
            }
            catch (NullReferenceException)
            {
                // Framework globals are not ready yet.
            }
            catch (Exception exception)
            {
                VerificationFailed = true;
                VerificationError = "无法验证隔离存档目录：" + exception.Message;
            }
        }
    }

    /// <summary>流程模式（白模 RunModeRuntime）是否激活；旧版本没有该类型时恒为 false。</summary>
    private static bool IsFlowModeActive()
    {
        if (s_flowModeGetter == null)
        {
            if (s_flowModeResolutionFailed) return false;
            try
            {
                Type? type = AccessTools.TypeByName(FlowModeTypeName);
                MethodInfo? getter = type == null ? null : AccessTools.PropertyGetter(type, "Active");
                if (getter == null)
                {
                    s_flowModeResolutionFailed = true;
                    return false;
                }

                s_flowModeGetter = getter;
            }
            catch (Exception)
            {
                s_flowModeResolutionFailed = true;
                return false;
            }
        }

        try
        {
            return s_flowModeGetter!.Invoke(null, null) is true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>按 PlayerFlow 约定解析当前流程档目录；QA 隔离模式下限定在隔离根内。</summary>
    private static bool TryResolveFlowSaveFolder(out string saveRoot)
    {
        saveRoot = string.Empty;
        string playerFlowRoot;
        if (!string.IsNullOrEmpty(s_profileRoot))
        {
            playerFlowRoot = Path.Combine(s_profileRoot, FlowSaveSlotResolver.FlowDirectoryName);
        }
        else
        {
            string persistentDataPath;
            try
            {
                persistentDataPath = Application.persistentDataPath;
            }
            catch (Exception)
            {
                return false;
            }

            if (string.IsNullOrEmpty(persistentDataPath)) return false;
            playerFlowRoot = FlowSaveSlotResolver.ResolvePlayerFlowRoot(persistentDataPath);
        }

        return FlowSaveSlotResolver.TrySelectNewestSlot(playerFlowRoot, out saveRoot);
    }

    public static bool TryResolveRuntimeSaveFolder(out string saveRoot)
    {
        saveRoot = string.Empty;
        // 1.457 起白模对局存档迁到 PlayerFlow；流程模式激活时必须优先按新约定解析，
        // 否则自动备份与回档仍指向已经停写的 SaveManager 目录。
        if (IsFlowModeActive() && TryResolveFlowSaveFolder(out saveRoot)) return true;

        // Path resolution also runs in player mode: the auto-backup service needs the real save
        // folder even though this patch is not installed there. SaveManager handles are cached
        // once (see TryEnsureSaveManagerHandles), so this per-second call no longer rescans
        // assemblies; the old full type scan cost ~75-108 ms and caused the 1% low collapse.
        if (!TryEnsureSaveManagerHandles()) return false;

        try
        {
            object? instance = s_saveManagerGetter!.Invoke(null, null);
            if (instance == null) return false;
            string? path = s_saveManagerGetPath!.Invoke(instance, null) as string;
            if (string.IsNullOrWhiteSpace(path)) return false;

            string fullPath = Path.GetFullPath(path);
            if (!Path.IsPathRooted(fullPath) || !Directory.Exists(fullPath)) return false;
            saveRoot = fullPath;
            return true;
        }
        catch (TargetInvocationException)
        {
            return false;
        }
        catch (NullReferenceException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (Exception)
        {
            // SaveManager may be between scene lifetimes. A passive path probe must never
            // make the plugin noisy or interrupt the game loop.
            return false;
        }
    }

    private static bool Prefix(ref string __result)
    {
        __result = s_profileRoot.Replace('\\', '/');
        Applied = true;
        return false;
    }

    private static bool IsInside(string candidate, string root)
    {
        string normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string normalizedCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}
