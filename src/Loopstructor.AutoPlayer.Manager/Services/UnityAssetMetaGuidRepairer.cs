using System.Text;
using System.Text.RegularExpressions;

namespace Loopstructor.AutoPlayer.Manager.Services;

/// <summary>
/// 修复 Unity 工程里 GUID 非法的 .meta 文件。
/// Unity 遇到这类 .meta 会在 AssetDatabase 刷新时打印两条 error 并忽略对应资源（"does not have a valid GUID"），
/// 由于 Asset 被忽略，替换为新的合法 GUID 不会破坏任何已有引用。
/// </summary>
public static class UnityAssetMetaGuidRepairer
{
    private static readonly string[] ScannedRootDirectories = { "Assets", "Packages" };
    private static readonly Regex GuidLineRegex = new(
        @"(?m)^([ \t]*guid[ \t]*:[ \t]*)([^\r\n]*?)([ \t]*)\r?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ValidGuidRegex = new(
        "^[0-9A-Fa-f]{32}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly byte[] Utf8Preamble = Encoding.UTF8.GetPreamble();

    public static UnityAssetMetaGuidRepairResult Repair(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
            return new UnityAssetMetaGuidRepairResult();

        EnumerationOptions enumeration = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        HashSet<string> knownGuids = new(StringComparer.OrdinalIgnoreCase);
        List<MetaCandidate> candidates = new();
        int failedCount = 0;

        foreach (string directoryName in ScannedRootDirectories)
        {
            string root = Path.Combine(projectRoot, directoryName);
            if (!Directory.Exists(root)) continue;
            foreach (string path in Directory.EnumerateFiles(root, "*.meta", enumeration))
            {
                if (!TryReadMeta(path, out string text, out bool hasPreamble))
                {
                    failedCount++;
                    continue;
                }

                Match match = GuidLineRegex.Match(text);
                if (match.Success && ValidGuidRegex.IsMatch(match.Groups[2].Value))
                {
                    knownGuids.Add(match.Groups[2].Value);
                    continue;
                }

                candidates.Add(new MetaCandidate(path, text, hasPreamble, match));
            }
        }

        int repairedCount = 0;
        foreach (MetaCandidate candidate in candidates)
        {
            string guid = CreateUniqueGuid(knownGuids);
            knownGuids.Add(guid);
            string updated = candidate.GuidLine.Success
                ? ReplaceGuidValue(candidate.Text, candidate.GuidLine, guid)
                : InsertGuidLine(candidate.Text, guid);
            if (!TryWriteMeta(candidate.Path, updated, candidate.HasPreamble))
            {
                failedCount++;
                continue;
            }

            repairedCount++;
        }

        return new UnityAssetMetaGuidRepairResult { RepairedCount = repairedCount, FailedCount = failedCount };
    }

    private static string CreateUniqueGuid(HashSet<string> knownGuids)
    {
        string guid;
        do
        {
            guid = Guid.NewGuid().ToString("N");
        }
        while (knownGuids.Contains(guid));

        return guid;
    }

    private static string ReplaceGuidValue(string text, Match match, string guid)
    {
        Group value = match.Groups[2];
        return string.Concat(text.AsSpan(0, value.Index), guid, text.AsSpan(value.Index + value.Length));
    }

    private static string InsertGuidLine(string text, string guid)
    {
        string line = "guid: " + guid;
        int newlineIndex = text.IndexOf('\n');
        if (newlineIndex < 0)
            return text.Length == 0 ? "fileFormatVersion: 2\n" + line + "\n" : text + "\n" + line;

        string newline = newlineIndex > 0 && text[newlineIndex - 1] == '\r' ? "\r\n" : "\n";
        return string.Concat(text.AsSpan(0, newlineIndex + 1), line, newline, text.AsSpan(newlineIndex + 1));
    }

    private static bool TryReadMeta(string path, out string text, out bool hasPreamble)
    {
        text = string.Empty;
        hasPreamble = false;
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
                return false;

            hasPreamble = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            int offset = hasPreamble ? 3 : 0;
            text = Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryWriteMeta(string path, string text, bool withPreamble)
    {
        string temporary = path + ".repair-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            byte[] content = Encoding.UTF8.GetBytes(text);
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (withPreamble) stream.Write(Utf8Preamble);
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporary);
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private readonly record struct MetaCandidate(string Path, string Text, bool HasPreamble, Match GuidLine);
}

public sealed class UnityAssetMetaGuidRepairResult
{
    public int RepairedCount { get; init; }
    public int FailedCount { get; init; }
}
