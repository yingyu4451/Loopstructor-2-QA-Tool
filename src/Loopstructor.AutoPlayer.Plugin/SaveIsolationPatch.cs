using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace Loopstructor.AutoPlayer.Plugin;

internal static class SaveIsolationPatch
{
    private static readonly object Sync = new();
    private static string s_profileRoot = string.Empty;
    private static MethodInfo? s_saveManagerGetter;
    private static MethodInfo? s_saveManagerGetPath;
    private static bool s_saveManagerResolutionFailed;

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

        Installed = true;
        log("已为当前激活进程安装存档隔离挂钩：" + s_profileRoot);
        return true;
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

    public static bool TryResolveRuntimeSaveFolder(out string saveRoot)
    {
        saveRoot = string.Empty;
        // Player mode never installs this patch, so it cannot redirect saves either. Returning
        // before any reflection keeps the once-per-second call free (a full type scan here used
        // to cost ~75-108 ms of main-thread time and produced the 1% low frame collapse).
        if (!Installed) return false;
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
