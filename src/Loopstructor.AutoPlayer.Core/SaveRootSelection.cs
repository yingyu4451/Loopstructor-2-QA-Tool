namespace Loopstructor.AutoPlayer.Core;

/// <summary>
/// 选择「当前真正在写的存档根目录」。1.457 起对局存档从 SaveManager 的 Gui2_Test
/// 迁到 PlayerFlow/slot_*/profile.json，但部分流程阶段 RunModeRuntime.Active 不是常真，
/// 只靠这个开关会在基地/过场时回退到旧目录，把已经停写的 Gui2_Test 当成存档根。
/// 这里改为比较两边「对局存档」的最后写入时间：谁新用谁；旧目录只有 User/Setting/
/// saveVersion 在动，不算对局存档，不会被误判为新。
/// </summary>
public static class SaveRootSelection
{
    /// <summary>在两个候选里挑较新的一方；任一目录不存在时退回另一方；都不存在返回 false。</summary>
    public static bool TryPick(
        string? flowRoot,
        DateTime flowWriteUtc,
        string? legacyRoot,
        DateTime legacyWriteUtc,
        out string saveRoot)
    {
        saveRoot = string.Empty;
        bool hasFlow = !string.IsNullOrWhiteSpace(flowRoot) && Directory.Exists(flowRoot);
        bool hasLegacy = !string.IsNullOrWhiteSpace(legacyRoot) && Directory.Exists(legacyRoot);
        if (!hasFlow && !hasLegacy) return false;
        if (hasFlow && !hasLegacy)
        {
            saveRoot = Path.GetFullPath(flowRoot!);
            return true;
        }

        if (!hasFlow)
        {
            saveRoot = Path.GetFullPath(legacyRoot!);
            return true;
        }

        saveRoot = Path.GetFullPath(flowWriteUtc >= legacyWriteUtc ? flowRoot! : legacyRoot!);
        return true;
    }

    /// <summary>
    /// 旧 SaveManager 目录里对局存档（Game_* 子目录）的最后写入时间。
    /// User_*/Setting_*/saveVersion 每次启动都会被重写，不能当作对局进度。
    /// </summary>
    public static DateTime GetLegacyRunSaveWriteUtc(string? legacyRoot)
    {
        if (string.IsNullOrWhiteSpace(legacyRoot) || !Directory.Exists(legacyRoot)) return DateTime.MinValue;
        try
        {
            DateTime newest = DateTime.MinValue;
            foreach (string directory in Directory.EnumerateDirectories(legacyRoot, "Game_*"))
            {
                foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                {
                    DateTime written = File.GetLastWriteTimeUtc(file);
                    if (written > newest) newest = written;
                }
            }

            return newest;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return DateTime.MinValue;
        }
    }
}
