using System.Diagnostics;
using Loopstructor.AutoPlayer.Updater.Services;

namespace Loopstructor.AutoPlayer.Tests;

public sealed class ProcessWaiterTests
{
    [Fact]
    public async Task WaitForExitAsync_ReturnsWhenProcessIsAlreadyExited()
    {
        using Process blocker = StartBlocker();
        int processId = blocker.Id;
        blocker.Kill(entireProcessTree: true);
        Assert.True(blocker.WaitForExit(5000));

        ProcessWaiter waiter = new();
        await waiter.WaitForExitAsync(
            new[] { processId, 0, -1, Environment.ProcessId },
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task WaitForExitAsync_ReportsProgressAndTimesOutWithProcessIds()
    {
        using Process blocker = StartBlocker();
        ProcessWaiter waiter = new();
        List<string> messages = new();

        TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await waiter.WaitForExitAsync(
                new[] { blocker.Id },
                TimeSpan.FromMilliseconds(400),
                message =>
                {
                    lock (messages) messages.Add(message);
                }));

        Assert.Contains($"PID {blocker.Id}", exception.Message);
        Assert.Contains("仍在运行", exception.Message);
        Assert.Contains("请关闭", exception.Message);
        Assert.NotEmpty(messages);
        Assert.Contains(messages, message => message.Contains(blocker.Id.ToString()));
    }

    [Fact]
    public async Task WaitForExitAsync_ForceTerminatesUnresponsiveProcessAfterGrace()
    {
        using Process blocker = StartBlocker();
        ProcessWaiter waiter = new();
        List<string> messages = new();

        await waiter.WaitForExitAsync(
            new[] { blocker.Id },
            TimeSpan.FromSeconds(15),
            message =>
            {
                lock (messages) messages.Add(message);
            },
            unresponsiveProcessId: blocker.Id,
            forcedTerminationGrace: TimeSpan.FromMilliseconds(500));

        Assert.True(blocker.WaitForExit(5000));
        Assert.Contains(messages, message => message.Contains("强制结束"));
    }

    private static Process StartBlocker() => Process.Start(new ProcessStartInfo("ping.exe")
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        ArgumentList =
        {
            "-n", "60", "-w", "1000", "127.0.0.1"
        }
    }) ?? throw new InvalidOperationException("无法启动进程等待测试的占位进程。");
}
