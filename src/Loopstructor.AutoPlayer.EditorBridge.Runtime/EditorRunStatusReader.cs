using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Loopstructor.AutoPlayer.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Loopstructor.AutoPlayer.EditorBridge.Runtime;

/// <summary>
/// Editor Play Mode 下的运行状态：当前存档根与「章-关」进度。
/// 自动备份在 Host 侧依赖这三项；Player 由 BepInEx 插件上报，Editor 没有插件，
/// 因此这里按与插件相同的规则（PlayerFlow 新旧对比 + 游戏底部面板文字）计算后随 /api/status 上报。
/// </summary>
internal static class EditorRunStatusReader
{
    private const string SaveManagerTypeName = "ActFramework_ByHZR.Save.SaveManager";
    private const string HudPanelTypeName = "MetroTD.UISystem.MultiFunBtn";

    private static readonly Regex StageLabelPattern = new(@"^\s*(\d+)\s*-\s*(\d+)\s*$", RegexOptions.CultureInvariant);
    private static MethodInfo? s_saveManagerGetter;
    private static MethodInfo? s_saveManagerGetPath;
    private static Type? s_hudPanelType;
    private static bool s_hudPanelResolutionFailed;
    private static bool s_saveManagerResolutionFailed;

    public static JObject Empty() => new()
    {
        ["runStatusReady"] = false,
        ["activeSaveRoot"] = string.Empty,
        ["currentChapter"] = 0,
        ["currentMapLayer"] = -1
    };

    public static JObject Build()
    {
        JObject status = Empty();
        try
        {
            bool hasSaveRoot = TryResolveSaveRoot(out string saveRoot);
            bool hasProgress = TryReadStageProgress(out int chapter, out int level);
            status["activeSaveRoot"] = saveRoot;
            status["currentChapter"] = chapter;
            status["currentMapLayer"] = level;
            status["runStatusReady"] = hasSaveRoot && hasProgress;
        }
        catch (Exception)
        {
            // 状态读取失败时保持未就绪，Host 的备份服务会按门槛跳过。
        }

        return status;
    }

    private static bool TryResolveSaveRoot(out string saveRoot)
    {
        saveRoot = string.Empty;
        string persistent = Application.persistentDataPath;
        if (string.IsNullOrWhiteSpace(persistent)) return false;

        string flowRootPath = FlowSaveSlotResolver.ResolvePlayerFlowRoot(persistent);
        bool hasFlow = FlowSaveSlotResolver.TrySelectNewestSlot(flowRootPath, out string flowRoot, out DateTime flowWrite);
        string? legacyRoot = TryResolveSaveManagerFolder();
        DateTime legacyWrite = legacyRoot == null ? DateTime.MinValue : SaveRootSelection.GetLegacyRunSaveWriteUtc(legacyRoot);
        return SaveRootSelection.TryPick(hasFlow ? flowRoot : null, flowWrite, legacyRoot, legacyWrite, out saveRoot);
    }

    private static string? TryResolveSaveManagerFolder()
    {
        if (!TryEnsureSaveManagerHandles()) return null;
        try
        {
            object? instance = s_saveManagerGetter!.Invoke(null, null);
            if (instance == null) return null;
            string? path = s_saveManagerGetPath!.Invoke(instance, null) as string;
            if (string.IsNullOrWhiteSpace(path)) return null;
            string fullPath = Path.GetFullPath(path);
            return Directory.Exists(fullPath) ? fullPath : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool TryEnsureSaveManagerHandles()
    {
        if (s_saveManagerGetter != null && s_saveManagerGetPath != null) return true;
        if (s_saveManagerResolutionFailed) return false;

        Type? type = FindType(SaveManagerTypeName);
        MethodInfo? getter = type?
            .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?
            .GetGetMethod();
        MethodInfo? getPath = type?.GetMethod(
            "GetSaveFolderPath",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            Type.EmptyTypes,
            null);
        if (getter == null || getPath == null)
        {
            s_saveManagerResolutionFailed = true;
            return false;
        }

        s_saveManagerGetter = getter;
        s_saveManagerGetPath = getPath;
        return true;
    }

    private static bool TryReadStageProgress(out int chapter, out int level)
    {
        chapter = 0;
        level = -1;
        try
        {
            object? panel = ResolveHudPanel();
            if (panel == null) return false;
            object? label = panel.GetType()
                .GetField("m_roomText", BindingFlags.NonPublic | BindingFlags.Instance)?
                .GetValue(panel);
            string? text = label?
                .GetType()
                .GetProperty("text", BindingFlags.Public | BindingFlags.Instance)?
                .GetValue(label) as string;
            Match match = StageLabelPattern.Match(text ?? string.Empty);
            if (!match.Success) return false;

            int parsedChapter = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            int parsedLevel = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (parsedChapter <= 0 || parsedLevel <= 0) return false;

            chapter = parsedChapter;
            level = parsedLevel;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static object? ResolveHudPanel()
    {
        if (s_hudPanelResolutionFailed) return null;
        s_hudPanelType ??= FindType(HudPanelTypeName);
        if (s_hudPanelType == null)
        {
            s_hudPanelResolutionFailed = true;
            return null;
        }

        try
        {
            return s_hudPanelType
                .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?
                .GetValue(null);
        }
        catch (Exception)
        {
            s_hudPanelResolutionFailed = true;
            return null;
        }
    }

    private static Type? FindType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? exact = assembly.GetType(fullName, false);
            if (exact != null) return exact;
        }

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type? match = assembly.GetTypes().FirstOrDefault(type => type.FullName == fullName);
                if (match != null) return match;
            }
            catch (ReflectionTypeLoadException exception)
            {
                Type? match = exception.Types.FirstOrDefault(type => type?.FullName == fullName);
                if (match != null) return match;
            }
        }

        return null;
    }
}
