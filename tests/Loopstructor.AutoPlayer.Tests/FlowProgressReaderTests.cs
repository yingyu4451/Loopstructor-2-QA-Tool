using Loopstructor.AutoPlayer.Core;

namespace Loopstructor.AutoPlayer.Tests;

/// <summary>
/// 流程测试页只读当前流程档来核对“教程死亡 / 副官解锁 / 设施升级 / 无尽与排行榜”。
/// 这里锁定投影字段，并保证损坏的解锁记录不会被误报成已解锁。
/// </summary>
public sealed class FlowProgressReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "flow-progress-" + Guid.NewGuid().ToString("N"));

    public FlowProgressReaderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void TryRead_ProjectsTheStableFlowProgressFields()
    {
        const string json = """
        {
          "version": 2,
          "revision": 41,
          "profileId": "profile-demo",
          "context": {
            "segmentId": "demo_free",
            "phase": 2,
            "tutorialDeathCount": 1,
            "hasBattleSave": true
          },
          "profile": {
            "characterLevels": { "Character_Seraphina": 2, "character_2": 1 },
            "modeUnlocks": {
              "version": 1,
              "unlocked": true,
              "sourceRunId": "run-1",
              "characterId": "Character_Vera",
              "endlessIntroduced": true,
              "leaderboardIntroduced": false
            },
            "whiteboxSession": {
              "sourceFluid": 46,
              "lastCompletedLayers": 23,
              "unlockedVehicles": [3, 257, 0],
              "unlockedCharacters": ["Character_Seraphina", "Character_Vera", "Character_Greta"],
              "upgradeLevels": { "character_1_node_choices": 2, "character_1_vehicle_candidates": 1 }
            }
          }
        }
        """;

        Assert.True(FlowProgressReader.TryRead(json, out FlowProgressSnapshot snapshot, out string error), error);

        Assert.Equal(41, snapshot.Revision);
        Assert.Equal("profile-demo", snapshot.ProfileId);
        Assert.Equal("demo_free", snapshot.SegmentId);
        Assert.Equal(2, snapshot.Phase);
        Assert.Equal(1, snapshot.TutorialDeathCount);
        Assert.True(snapshot.HasBattleSave);
        Assert.Equal(46, snapshot.SourceFluid);
        Assert.Equal(23, snapshot.LastCompletedLayers);
        Assert.Equal(3, snapshot.UnlockedVehicleCount);
        Assert.Equal(3, snapshot.UnlockedCharacterCount);
        Assert.Contains("Character_Vera", snapshot.UnlockedCharacters);
        Assert.Equal(2, snapshot.CharacterLevels["Character_Seraphina"]);
        Assert.Equal(2, snapshot.UpgradeLevels["character_1_node_choices"]);
        Assert.Equal(2, snapshot.UpgradeCount);
        Assert.Equal(3, snapshot.UpgradeLevelSum);
        Assert.True(snapshot.ModeUnlockRecordPresent);
        Assert.True(snapshot.ModeUnlockRecordValid);
        Assert.True(snapshot.EndlessAndLeaderboardUnlocked);
        Assert.True(snapshot.EndlessIntroduced);
        Assert.False(snapshot.LeaderboardIntroduced);
        Assert.Equal("Character_Vera", snapshot.ModeUnlockCharacterId);
    }

    [Fact]
    public void TryRead_TreatsMissingProgressAsNotYetUnlocked()
    {
        const string json = """
        {
          "context": { "segmentId": "preview_first" },
          "profile": { "whiteboxSession": {} }
        }
        """;

        Assert.True(FlowProgressReader.TryRead(json, out FlowProgressSnapshot snapshot, out string error), error);

        Assert.Equal("preview_first", snapshot.SegmentId);
        Assert.Empty(snapshot.UnlockedCharacters);
        Assert.Empty(snapshot.UpgradeLevels);
        Assert.False(snapshot.HasBattleSave);
        Assert.False(snapshot.ModeUnlockRecordPresent);
        Assert.False(snapshot.EndlessAndLeaderboardUnlocked);
    }

    [Fact]
    public void TryRead_DoesNotTreatAMalformedUnlockRecordAsUnlocked()
    {
        const string json = """
        {
          "context": { "segmentId": "demo_free" },
          "profile": {
            "modeUnlocks": { "version": 1, "unlocked": true },
            "whiteboxSession": {}
          }
        }
        """;

        Assert.True(FlowProgressReader.TryRead(json, out FlowProgressSnapshot snapshot, out string error), error);

        Assert.True(snapshot.ModeUnlockRecordPresent);
        Assert.False(snapshot.ModeUnlockRecordValid);
        Assert.False(snapshot.EndlessAndLeaderboardUnlocked);
        Assert.False(snapshot.EndlessIntroduced);
    }

    [Fact]
    public void TryRead_RejectsInvalidJson()
    {
        Assert.False(FlowProgressReader.TryRead("{ not json", out _, out string error));
        Assert.Contains("不是有效 JSON", error);
    }

    [Fact]
    public void ReadFile_ReturnsTheParsedProfileAndWriteTime()
    {
        string path = Path.Combine(_root, FlowProgressFileReader.ProfileFileName);
        File.WriteAllText(path, """
        {
          "context": { "segmentId": "demo_free", "tutorialDeathCount": 2 },
          "profile": { "whiteboxSession": { "unlockedCharacters": ["Character_Seraphina"] } }
        }
        """);

        FlowProgressFileResult result = FlowProgressFileReader.TryRead(path);

        Assert.True(result.Available, result.Reason);
        Assert.Equal(Path.GetFullPath(path), result.ProfilePath);
        Assert.NotNull(result.LastWriteUtc);
        Assert.NotNull(result.Progress);
        Assert.Equal("demo_free", result.Progress!.SegmentId);
        Assert.Equal(2, result.Progress.TutorialDeathCount);
    }

    [Fact]
    public void ReadFile_ExplainsWhenTheProfileDoesNotExistYet()
    {
        FlowProgressFileResult result = FlowProgressFileReader.TryRead(Path.Combine(_root, FlowProgressFileReader.ProfileFileName));

        Assert.False(result.Available);
        Assert.Contains("尚未生成", result.Reason);
    }
}
