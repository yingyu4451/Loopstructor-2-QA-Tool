using Loopstructor.AutoPlayer.Manager.Services;

namespace Loopstructor.AutoPlayer.Tests;

public sealed class LogTailReaderTests
{
    [Fact]
    public void Reset_StartAtEnd_SkipsPreviousProcessHistory()
    {
        using TemporaryDirectory temporary = new();
        string path = Path.Combine(temporary.Root, "Player.log");
        File.WriteAllLines(path, new[] { "old warning", "old error" });
        LogTailReader reader = new();

        reader.Reset(path, startAtEnd: true);
        File.AppendAllLines(path, new[] { "current info" });

        Assert.Equal(new[] { "current info" }, reader.ReadAvailable());
    }

    [Fact]
    public void Reset_FromBeginning_ReadsNewManagerLaunchLog()
    {
        using TemporaryDirectory temporary = new();
        string path = Path.Combine(temporary.Root, "Player.log");
        File.WriteAllLines(path, new[] { "current startup", "current ready" });
        LogTailReader reader = new();

        reader.Reset(path);

        Assert.Equal(new[] { "current startup", "current ready" }, reader.ReadAvailable());
    }

    [Fact]
    public void ReadAvailable_RewindsWhenCurrentProcessTruncatesLog()
    {
        using TemporaryDirectory temporary = new();
        string path = Path.Combine(temporary.Root, "Player.log");
        File.WriteAllText(path, new string('x', 128) + Environment.NewLine);
        LogTailReader reader = new();
        reader.Reset(path);
        Assert.Single(reader.ReadAvailable());

        File.WriteAllText(path, "new process" + Environment.NewLine);

        Assert.Equal(new[] { "new process" }, reader.ReadAvailable());
    }

    [Fact]
    public void ReadAvailable_LimitsLinesWithoutLosingTheRest()
    {
        using TemporaryDirectory temporary = new();
        string path = Path.Combine(temporary.Root, "Player.log");
        File.WriteAllLines(path, Enumerable.Range(1, 10).Select(index => $"line {index}").ToArray());
        LogTailReader reader = new();
        reader.Reset(path);

        Assert.Equal(new[] { "line 1", "line 2", "line 3" }, reader.ReadAvailable(maximumLines: 3));
        Assert.Equal(new[] { "line 4", "line 5", "line 6" }, reader.ReadAvailable(maximumLines: 3));
        Assert.Equal(new[] { "line 7", "line 8", "line 9" }, reader.ReadAvailable(maximumLines: 3));
        Assert.Equal(new[] { "line 10" }, reader.ReadAvailable(maximumLines: 3));
    }

    [Fact]
    public void ReadAvailable_RetriesTrailingPartialLineUntilItIsComplete()
    {
        using TemporaryDirectory temporary = new();
        string path = Path.Combine(temporary.Root, "Player.log");
        File.WriteAllText(path, "complete" + Environment.NewLine + "incomp");
        LogTailReader reader = new();
        reader.Reset(path);

        Assert.Equal(new[] { "complete" }, reader.ReadAvailable());
        Assert.Empty(reader.ReadAvailable());

        File.AppendAllText(path, "lete" + Environment.NewLine);

        Assert.Equal(new[] { "incomplete" }, reader.ReadAvailable());
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), "Loopstructor.AutoPlayer.LogTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
