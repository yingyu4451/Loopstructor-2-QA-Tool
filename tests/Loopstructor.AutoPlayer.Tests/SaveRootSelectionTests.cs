using Loopstructor.AutoPlayer.Core;

namespace Loopstructor.AutoPlayer.Tests;

/// <summary>
/// 回归覆盖：0.6.101 只在 RunModeRuntime.Active 为真时用 PlayerFlow，基地/过场阶段会回退到已经
/// 停写的 Gui2_Test，于是备份/回档一直拿到旧存档（用户看到备份停在 15:02、回档无效）。
/// 现在按「对局存档最后写入时间」选根：PlayerFlow 的 profile.json 对比旧目录 Game_*。
/// </summary>
public sealed class SaveRootSelectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "save-root-selection", Guid.NewGuid().ToString("N"));

    public SaveRootSelectionTests() => Directory.CreateDirectory(_root);

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
    public void TryPick_UsesTheOnlyExistingRoot()
    {
        string flow = Directory.CreateDirectory(Path.Combine(_root, "PlayerFlow", "slot_demo-v1")).FullName;
        string legacy = Directory.CreateDirectory(Path.Combine(_root, "Gui2_Test")).FullName;

        Assert.True(SaveRootSelection.TryPick(flow, DateTime.MinValue, null, DateTime.MinValue, out string onlyFlow));
        Assert.Equal(Path.GetFullPath(flow), onlyFlow);

        Assert.True(SaveRootSelection.TryPick(null, DateTime.MinValue, legacy, DateTime.MinValue, out string onlyLegacy));
        Assert.Equal(Path.GetFullPath(legacy), onlyLegacy);
    }

    [Fact]
    public void TryPick_PrefersTheNewerRunSave()
    {
        string flow = Directory.CreateDirectory(Path.Combine(_root, "flow")).FullName;
        string legacy = Directory.CreateDirectory(Path.Combine(_root, "legacy")).FullName;
        DateTime older = new(2026, 11, 17, 8, 6, 11, DateTimeKind.Utc);
        DateTime newer = new(2026, 11, 20, 9, 7, 20, DateTimeKind.Utc);

        Assert.True(SaveRootSelection.TryPick(flow, newer, legacy, older, out string flowPicked));
        Assert.Equal(Path.GetFullPath(flow), flowPicked);

        Assert.True(SaveRootSelection.TryPick(flow, older, legacy, newer, out string legacyPicked));
        Assert.Equal(Path.GetFullPath(legacy), legacyPicked);
    }

    [Fact]
    public void TryPick_ReturnsFalseWhenNeitherRootExists()
    {
        Assert.False(SaveRootSelection.TryPick(Path.Combine(_root, "missing-flow"), DateTime.MinValue,
            Path.Combine(_root, "missing-legacy"), DateTime.MinValue, out _));
    }

    [Fact]
    public void GetLegacyRunSaveWriteUtc_IgnoresUserSettingAndSaveVersionFiles()
    {
        string legacy = Path.Combine(_root, "Gui2_Test");
        string game = Directory.CreateDirectory(Path.Combine(legacy, "Game_0")).FullName;
        string gameFile = Path.Combine(game, "Game");
        File.WriteAllText(gameFile, "{}");
        DateTime runWritten = new(2026, 11, 17, 8, 6, 11, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(gameFile, runWritten);

        string user = Directory.CreateDirectory(Path.Combine(legacy, "User_0")).FullName;
        File.WriteAllText(Path.Combine(user, "User"), "{}");
        File.WriteAllText(Path.Combine(legacy, "saveVersion"), "{}");
        DateTime now = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(Path.Combine(user, "User"), now);
        File.SetLastWriteTimeUtc(Path.Combine(legacy, "saveVersion"), now);

        Assert.Equal(runWritten, SaveRootSelection.GetLegacyRunSaveWriteUtc(legacy));
    }

    [Fact]
    public void GetLegacyRunSaveWriteUtc_ReturnsMinValueWhenNoGameDirectoryExists()
    {
        string legacy = Directory.CreateDirectory(Path.Combine(_root, "empty-legacy")).FullName;
        File.WriteAllText(Path.Combine(legacy, "saveVersion"), "{}");

        Assert.Equal(DateTime.MinValue, SaveRootSelection.GetLegacyRunSaveWriteUtc(legacy));
        Assert.Equal(DateTime.MinValue, SaveRootSelection.GetLegacyRunSaveWriteUtc(null));
    }
}
