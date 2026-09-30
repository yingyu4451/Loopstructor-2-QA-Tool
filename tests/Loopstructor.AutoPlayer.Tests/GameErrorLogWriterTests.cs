using Loopstructor.AutoPlayer.Plugin;

namespace Loopstructor.AutoPlayer.Tests;

/// <summary>
/// 非 Development Build 的游戏包不会在游戏内显示报错，但 UnityEngine 仍会触发
/// Application.logMessageReceivedThreaded。插件把 Error/Exception/Assert 汇聚成
/// game-errors.log 供 QA 工具读取，这里固化该文件的写入行为。
/// </summary>
public sealed class GameErrorLogWriterTests
{
    private static readonly DateTime Start = new(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Write_AppendsTimestampedBlockWithStackTrace()
    {
        using TemporaryDirectory temporary = new();
        string path = Path.Combine(temporary.Root, "game-errors.log");
        GameErrorLogWriter writer = new(path, maximumBytes: 1024 * 1024);

        writer.Write(Start, "Exception", "NullReferenceException: boom", "at A.B ()\n  at C.D ()");

        string text = File.ReadAllText(path);
        Assert.Contains("2026-09-30 10:00:00.000Z", text, StringComparison.Ordinal);
        Assert.Contains("[Exception] NullReferenceException: boom", text, StringComparison.Ordinal);
        Assert.Contains("at A.B ()", text, StringComparison.Ordinal);
        Assert.Contains("at C.D ()", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_CollapsesRepeatsInsideTheWindowAndReportsHowManyWereMerged()
    {
        using TemporaryDirectory temporary = new();
        string path = Path.Combine(temporary.Root, "game-errors.log");
        GameErrorLogWriter writer = new(path, maximumBytes: 1024 * 1024);

        // 同一错误每帧抛出时会把日志刷爆：窗口内只累加计数，下一次落盘再报告合并了多少次。
        for (int second = 0; second < 4; second++)
        {
            writer.Write(Start.AddSeconds(second), "Error", "同样的错误", "at X ()");
        }
        writer.Write(Start.AddSeconds(10), "Error", "同样的错误", "at X ()");

        string[] headers = File.ReadAllLines(path).Where(line => line.StartsWith('[')).ToArray();
        Assert.Equal(2, headers.Length);
        Assert.Contains("合并 3 次", headers[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Write_KeepsDistinctErrorsInSeparateBlocks()
    {
        using TemporaryDirectory temporary = new();
        string path = Path.Combine(temporary.Root, "game-errors.log");
        GameErrorLogWriter writer = new(path, maximumBytes: 1024 * 1024);

        writer.Write(Start, "Error", "缺少战车描述信息", "at E.F ()");
        writer.Write(Start.AddSeconds(1), "Assert", "资源缺失", "at G.H ()");

        string[] headers = File.ReadAllLines(path).Where(line => line.StartsWith('[')).ToArray();
        Assert.Equal(2, headers.Length);
        Assert.Contains("[Error]", headers[0], StringComparison.Ordinal);
        Assert.Contains("[Assert]", headers[1], StringComparison.Ordinal);
        Assert.DoesNotContain("合并", headers[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Write_RotatesTheFileInsteadOfGrowingWithoutBound()
    {
        using TemporaryDirectory temporary = new();
        string path = Path.Combine(temporary.Root, "game-errors.log");
        GameErrorLogWriter writer = new(path, maximumBytes: 400);

        for (int index = 0; index < 40; index++)
        {
            writer.Write(Start.AddSeconds(index * 10d), "Error", "错误 " + index, "at Y ()");
        }

        Assert.True(File.Exists(path), "current game-errors.log should exist");
        Assert.True(File.Exists(path + ".1"), "rotated game-errors.log.1 should exist");
        Assert.Contains("错误 39", File.ReadAllText(path), StringComparison.Ordinal);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), "Loopstructor.AutoPlayer.GameErrorLogTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
