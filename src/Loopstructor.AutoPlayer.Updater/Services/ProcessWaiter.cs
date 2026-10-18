using System.Diagnostics;

namespace Loopstructor.AutoPlayer.Updater.Services;

public sealed class ProcessWaiter
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Waits until every live process in <paramref name="processIds"/> exits.
    /// When <paramref name="unresponsiveProcessId"/> stays alive past
    /// <paramref name="forcedTerminationGrace"/>, it is terminated so a stalled
    /// hand-off cannot block the update forever (for example an Electron Manager
    /// that failed to quit after the updater window appeared).
    /// </summary>
    public async Task WaitForExitAsync(
        IEnumerable<int> processIds,
        TimeSpan timeout,
        Action<string>? progress = null,
        CancellationToken cancellationToken = default,
        int? unresponsiveProcessId = null,
        TimeSpan? forcedTerminationGrace = null)
    {
        List<int> alive = processIds
            .Where(processId => processId > 0 && processId != Environment.ProcessId)
            .Distinct()
            .ToList();
        if (alive.Count == 0) return;

        Stopwatch stopwatch = Stopwatch.StartNew();
        bool forcedTerminationAttempted = false;
        TimeSpan? lastProgressAt = null;

        while (true)
        {
            alive = alive.Where(IsAlive).ToList();
            if (alive.Count == 0) return;

            if (!forcedTerminationAttempted
                && unresponsiveProcessId is int forcedProcessId
                && forcedTerminationGrace is TimeSpan grace
                && stopwatch.Elapsed >= grace
                && alive.Contains(forcedProcessId))
            {
                forcedTerminationAttempted = true;
                progress?.Invoke($"进程 {forcedProcessId} 在 {grace.TotalSeconds:0} 秒内没有退出，正在强制结束以便继续更新...");
                if (TryTerminate(forcedProcessId))
                {
                    alive.Remove(forcedProcessId);
                    if (alive.Count == 0) return;
                }
            }

            if (stopwatch.Elapsed >= timeout)
            {
                string stillRunning = string.Join("、", alive.Select(processId => $"PID {processId}"));
                throw new TimeoutException(
                    $"等待进程退出超时（{timeout.TotalSeconds:0} 秒）：{stillRunning} 仍在运行。" +
                    "请关闭 Loopstructor 2 QA Tool 或游戏后重试。");
            }

            if (lastProgressAt is null || stopwatch.Elapsed - lastProgressAt.Value >= ProgressInterval)
            {
                lastProgressAt = stopwatch.Elapsed;
                string ids = string.Join("、", alive.Select(processId => processId.ToString()));
                progress?.Invoke(
                    $"正在等待进程 {ids} 退出（已等待 {stopwatch.Elapsed.TotalSeconds:0} 秒，最多 {timeout.TotalSeconds:0} 秒）...");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private static bool IsAlive(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryTerminate(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            if (process.HasExited) return true;
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
            return process.HasExited;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return false;
        }
    }
}
