namespace Loopstructor.AutoPlayer.Core;

/// <summary>
/// 新版流程存档（PlayerFlow）的路径约定：{persistentDataPath}/PlayerFlow/slot_{slot}/profile.json。
/// 游戏 1.457 起把白模对局存档从 SaveManager 的 Gui2_Test 目录迁到 PlayerFlow，插件若继续按
/// SaveManager.GetSaveFolderPath() 备份/回档，只会读写到停在旧版本的空壳目录。
/// </summary>
public static class FlowSaveSlotResolver
{
    public const string FlowDirectoryName = "PlayerFlow";
    public const string SlotPrefix = "slot_";
    public const string ProfileFileName = "profile.json";

    /// <summary>流程档根目录：persistentDataPath/PlayerFlow。</summary>
    public static string ResolvePlayerFlowRoot(string persistentDataPath) =>
        Path.Combine(persistentDataPath, FlowDirectoryName);

    /// <summary>隔离模式下把流程档映射到隔离根内的同一相对结构。</summary>
    public static string ResolveIsolatedProfilePath(string isolatedRoot, string slot) =>
        Path.Combine(isolatedRoot, FlowDirectoryName, SlotPrefix + slot, ProfileFileName);

    /// <summary>流程档标识只允许字母、数字、下划线和短横线，与游戏侧校验保持一致。</summary>
    public static bool IsValidSlot(string? slot)
    {
        if (slot == null || slot.Length == 0 || slot.Length > 64) return false;
        foreach (char c in slot)
        {
            bool allowed = (c >= 'a' && c <= 'z')
                           || (c >= 'A' && c <= 'Z')
                           || (c >= '0' && c <= '9')
                           || c == '_'
                           || c == '-';
            if (!allowed) return false;
        }

        return true;
    }

    /// <summary>选择 PlayerFlow 下 profile.json 最新写入的流程档目录。</summary>
    public static bool TrySelectNewestSlot(string? playerFlowRoot, out string saveRoot)
    {
        saveRoot = string.Empty;
        if (string.IsNullOrWhiteSpace(playerFlowRoot) || !Directory.Exists(playerFlowRoot)) return false;

        string? newest = null;
        DateTime newestWriteUtc = DateTime.MinValue;
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(playerFlowRoot, SlotPrefix + "*"))
            {
                string profile = Path.Combine(directory, ProfileFileName);
                if (!File.Exists(profile)) continue;
                DateTime writtenUtc = File.GetLastWriteTimeUtc(profile);
                if (newest == null || writtenUtc > newestWriteUtc)
                {
                    newest = directory;
                    newestWriteUtc = writtenUtc;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }

        if (newest == null) return false;
        saveRoot = Path.GetFullPath(newest);
        return true;
    }
}
