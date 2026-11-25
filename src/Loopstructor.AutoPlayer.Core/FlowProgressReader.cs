using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Loopstructor.AutoPlayer.Core;

/// <summary>
/// 流程档（PlayerFlow/slot_*/profile.json）里可供 QA 只读核对的进度快照。
/// 这里只做投影，不判断流程是否“正确”；判断留给流程测试清单与人工确认。
/// </summary>
public sealed class FlowProgressSnapshot
{
    public long Revision { get; set; }
    public string ProfileId { get; set; } = string.Empty;
    public string SegmentId { get; set; } = string.Empty;
    public int Phase { get; set; }
    public int TutorialDeathCount { get; set; }
    public bool HasBattleSave { get; set; }
    public int SourceFluid { get; set; }
    public int LastCompletedLayers { get; set; }
    public int UnlockedVehicleCount { get; set; }
    public IReadOnlyList<string> UnlockedCharacters { get; set; } = Array.Empty<string>();
    public int UnlockedCharacterCount => UnlockedCharacters.Count;
    public IReadOnlyDictionary<string, int> CharacterLevels { get; set; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, int> UpgradeLevels { get; set; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public bool ModeUnlockRecordPresent { get; set; }
    public bool ModeUnlockRecordValid { get; set; }
    public bool EndlessAndLeaderboardUnlocked { get; set; }
    public bool EndlessIntroduced { get; set; }
    public bool LeaderboardIntroduced { get; set; }
    public string ModeUnlockCharacterId { get; set; } = string.Empty;

    public int UpgradeCount => UpgradeLevels.Count;
    public int UpgradeLevelSum => UpgradeLevels.Values.Sum();
}

/// <summary>
/// 解析流程档 JSON。只读取稳定字段；缺字段按“尚未产生/尚未解锁”处理，
/// 但格式损坏时明确失败，避免把损坏档误报成已解锁。
/// </summary>
public static class FlowProgressReader
{
    public static bool TryRead(string? json, out FlowProgressSnapshot snapshot, out string error)
    {
        snapshot = new FlowProgressSnapshot();
        error = string.Empty;
        string content = json ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            error = "流程存档内容为空。";
            return false;
        }

        JObject root;
        try
        {
            root = JObject.Parse(content);
        }
        catch (Exception exception) when (exception is Newtonsoft.Json.JsonException or ArgumentException)
        {
            error = "流程存档不是有效 JSON：" + exception.Message;
            return false;
        }

        JObject? context = root["context"] as JObject;
        JObject? profile = root["profile"] as JObject;
        JObject? session = profile?["whiteboxSession"] as JObject;
        JObject? modeUnlocks = profile?["modeUnlocks"] as JObject;

        bool modeUnlockValid = modeUnlocks != null
                               && ReadInt(modeUnlocks, "version") == 1
                               && ReadBool(modeUnlocks, "unlocked")
                               && !string.IsNullOrWhiteSpace(ReadString(modeUnlocks, "sourceRunId"))
                               && !string.IsNullOrWhiteSpace(ReadString(modeUnlocks, "characterId"));

        snapshot = new FlowProgressSnapshot
        {
            Revision = ReadLong(root, "revision"),
            ProfileId = ReadString(root, "profileId"),
            SegmentId = ReadString(context, "segmentId"),
            Phase = ReadInt(context, "phase"),
            TutorialDeathCount = ReadInt(context, "tutorialDeathCount"),
            HasBattleSave = ReadBool(context, "hasBattleSave"),
            SourceFluid = ReadInt(session, "sourceFluid"),
            LastCompletedLayers = ReadInt(session, "lastCompletedLayers"),
            UnlockedVehicleCount = ReadArrayCount(session?["unlockedVehicles"]),
            UnlockedCharacters = ReadStringArray(session?["unlockedCharacters"]),
            CharacterLevels = ReadIntMap(profile?["characterLevels"]),
            UpgradeLevels = ReadIntMap(session?["upgradeLevels"]),
            ModeUnlockRecordPresent = modeUnlocks != null,
            ModeUnlockRecordValid = modeUnlockValid,
            EndlessAndLeaderboardUnlocked = modeUnlockValid && ReadBool(modeUnlocks, "unlocked"),
            EndlessIntroduced = modeUnlockValid && ReadBool(modeUnlocks, "endlessIntroduced"),
            LeaderboardIntroduced = modeUnlockValid && ReadBool(modeUnlocks, "leaderboardIntroduced"),
            ModeUnlockCharacterId = ReadString(modeUnlocks, "characterId")
        };
        return true;
    }

    private static string ReadString(JObject? source, string name)
    {
        JToken? token = source?[name];
        return token?.Type == JTokenType.String ? (token.Value<string>() ?? string.Empty).Trim() : string.Empty;
    }

    private static int ReadInt(JObject? source, string name)
    {
        JToken? token = source?[name];
        if (token == null) return 0;
        try
        {
            return token.Type == JTokenType.Integer ? token.Value<int>() : 0;
        }
        catch (Exception) when (token.Type == JTokenType.Integer)
        {
            return 0;
        }
    }

    private static long ReadLong(JObject? source, string name)
    {
        JToken? token = source?[name];
        if (token == null) return 0;
        try
        {
            return token.Type == JTokenType.Integer ? token.Value<long>() : 0;
        }
        catch (Exception) when (token.Type == JTokenType.Integer)
        {
            return 0;
        }
    }

    private static bool ReadBool(JObject? source, string name)
    {
        JToken? token = source?[name];
        return token is { Type: JTokenType.Boolean } && token.Value<bool>();
    }

    private static int ReadArrayCount(JToken? token) => token is JArray array ? array.Count : 0;

    private static IReadOnlyList<string> ReadStringArray(JToken? token)
    {
        if (token is not JArray array) return Array.Empty<string>();
        return array
            .Where(item => item.Type == JTokenType.String)
            .Select(item => (item.Value<string>() ?? string.Empty).Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, int> ReadIntMap(JToken? token)
    {
        Dictionary<string, int> result = new(StringComparer.OrdinalIgnoreCase);
        if (token is not JObject value) return result;
        foreach (JProperty property in value.Properties())
        {
            if (property.Value.Type != JTokenType.Integer) continue;
            try
            {
                result[property.Name] = property.Value.Value<int>();
            }
            catch (Exception)
            {
                // 单个字段超出 Int32 或类型异常时忽略该项，不影响其它进度。
            }
        }

        return result;
    }
}

/// <summary>流程档文件读取结果；文件不存在或正被写入时不抛异常，交给界面提示。</summary>
public sealed class FlowProgressFileResult
{
    public bool Available { get; set; }
    public string ProfilePath { get; set; } = string.Empty;
    public DateTime? LastWriteUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public FlowProgressSnapshot? Progress { get; set; }
}

/// <summary>读取流程档文件。游戏写入时允许共享读写，避免把正常写入误报成损坏。</summary>
public static class FlowProgressFileReader
{
    public const string ProfileFileName = "profile.json";

    public static FlowProgressFileResult TryRead(string? profilePath)
    {
        if (string.IsNullOrWhiteSpace(profilePath))
            return Failed(string.Empty, "没有可用的流程存档路径。");

        string path = Path.GetFullPath(profilePath);
        if (!File.Exists(path))
            return Failed(path, "流程存档尚未生成；进入基地或开始一局后再刷新。");

        string json;
        DateTime lastWriteUtc;
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            json = reader.ReadToEnd();
            lastWriteUtc = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failed(path, "读取流程存档失败：" + exception.Message);
        }

        if (!FlowProgressReader.TryRead(json, out FlowProgressSnapshot snapshot, out string error))
            return Failed(path, error);

        return new FlowProgressFileResult
        {
            Available = true,
            ProfilePath = path,
            LastWriteUtc = lastWriteUtc,
            Progress = snapshot
        };
    }

    private static FlowProgressFileResult Failed(string path, string reason) => new()
    {
        Available = false,
        ProfilePath = path,
        Reason = reason
    };
}

