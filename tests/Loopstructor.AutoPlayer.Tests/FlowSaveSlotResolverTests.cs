using Loopstructor.AutoPlayer.Core;

namespace Loopstructor.AutoPlayer.Tests;

/// <summary>
/// 回归覆盖：游戏 1.457 把白模对局存档从 SaveManager 的 Gui2_Test 迁移到
/// {persistentDataPath}/PlayerFlow/slot_{slot}/profile.json 后，自动备份与回档全部失效。
/// 这里锁定新的路径约定与「取最新写入流程档」的选择规则。
/// </summary>
public sealed class FlowSaveSlotResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "flow-slot-" + Guid.NewGuid().ToString("N"));

    public FlowSaveSlotResolverTests() => Directory.CreateDirectory(_root);

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
    public void ResolvePlayerFlowRoot_AppendsThePlayerFlowDirectory()
    {
        string persistent = Path.Combine(_root, "LocalLow", "PoneGames", "Loopstructor 2_ Skyspine");

        Assert.Equal(
            Path.Combine(persistent, "PlayerFlow"),
            FlowSaveSlotResolver.ResolvePlayerFlowRoot(persistent));
    }

    [Fact]
    public void ResolveIsolatedProfilePath_KeepsTheGameLayoutUnderTheIsolationRoot()
    {
        string isolated = Path.Combine(_root, "profile");

        Assert.Equal(
            Path.Combine(isolated, "PlayerFlow", "slot_demo-v1", "profile.json"),
            FlowSaveSlotResolver.ResolveIsolatedProfilePath(isolated, "demo-v1"));
    }

    [Theory]
    [InlineData("demo-v1", true)]
    [InlineData("main_2", true)]
    [InlineData("", false)]
    [InlineData("..\\escape", false)]
    [InlineData("slot with space", false)]
    [InlineData("../../../etc", false)]
    public void IsValidSlot_OnlyAllowsSimpleIdentifiers(string slot, bool expected)
    {
        Assert.Equal(expected, FlowSaveSlotResolver.IsValidSlot(slot));
    }

    [Fact]
    public void TrySelectNewestSlot_ChoosesTheMostRecentlyWrittenProfile()
    {
        string playerFlowRoot = Path.Combine(_root, "PlayerFlow");
        string older = CreateSlot(playerFlowRoot, "demo-v1", new DateTime(2026, 11, 20, 13, 30, 0, DateTimeKind.Utc));
        string newer = CreateSlot(playerFlowRoot, "main-v1", new DateTime(2026, 11, 20, 13, 40, 0, DateTimeKind.Utc));
        CreateSlotWithoutProfile(playerFlowRoot, "empty-v1");

        Assert.True(FlowSaveSlotResolver.TrySelectNewestSlot(playerFlowRoot, out string selected));
        Assert.Equal(Path.GetFullPath(newer), selected);
        Assert.NotEqual(Path.GetFullPath(older), selected);
    }

    [Fact]
    public void TrySelectNewestSlot_ReturnsFalseWhenNoProfileExists()
    {
        string playerFlowRoot = Path.Combine(_root, "PlayerFlow");
        CreateSlotWithoutProfile(playerFlowRoot, "demo-v1");

        Assert.False(FlowSaveSlotResolver.TrySelectNewestSlot(playerFlowRoot, out string selected));
        Assert.Equal(string.Empty, selected);
    }

    [Fact]
    public void TrySelectNewestSlot_ReturnsFalseForMissingRoot()
    {
        Assert.False(FlowSaveSlotResolver.TrySelectNewestSlot(Path.Combine(_root, "missing"), out _));
        Assert.False(FlowSaveSlotResolver.TrySelectNewestSlot(null, out _));
    }

    private static string CreateSlot(string playerFlowRoot, string slot, DateTime writtenUtc)
    {
        string directory = Path.Combine(playerFlowRoot, "slot_" + slot);
        Directory.CreateDirectory(directory);
        string profile = Path.Combine(directory, "profile.json");
        File.WriteAllText(profile, "{\"version\":1}");
        File.SetLastWriteTimeUtc(profile, writtenUtc);
        return directory;
    }

    private static void CreateSlotWithoutProfile(string playerFlowRoot, string slot)
    {
        string directory = Path.Combine(playerFlowRoot, "slot_" + slot);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "profile.json.bak"), "{}");
    }
}
