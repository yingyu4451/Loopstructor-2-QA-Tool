using System;
using Loopstructor.AutoPlayer.Core;
using UnityEngine;

namespace Loopstructor.AutoPlayer.Plugin;

/// <summary>
/// 把游戏进程的报错落盘，供 QA 工具读取。
///
/// 打包时未勾选 Development Build 的游戏不会在游戏内显示报错，游戏自带的调试日志系统
/// 也可能整体关闭；但 UnityEngine 在任何构建下都会触发
/// <see cref="Application.logMessageReceivedThreaded"/>，因此这里能拿到 Error、Exception 和
/// Assert 的完整失败现场。编辑器不使用本插件，不需要对应实现。
/// </summary>
internal sealed class GameErrorLog : IDisposable
{
    private const long MaximumBytes = 8L * 1024 * 1024;

    private readonly GameErrorLogWriter _writer;
    private bool _attached;

    internal GameErrorLog(string artifactRoot)
        : this(System.IO.Path.Combine(artifactRoot, Protocol.GameErrorLogFileName), MaximumBytes)
    {
    }

    internal GameErrorLog(string path, long maximumBytes)
    {
        _writer = new GameErrorLogWriter(path, maximumBytes);
    }

    /// <summary>开始捕获。可重复调用。</summary>
    internal void Attach()
    {
        if (_attached) return;
        Application.logMessageReceivedThreaded += OnLogMessage;
        _attached = true;
    }

    public void Dispose()
    {
        if (!_attached) return;
        _attached = false;
        Application.logMessageReceivedThreaded -= OnLogMessage;
    }

    private void OnLogMessage(string condition, string stackTrace, LogType type)
    {
        string? category = Category(type);
        if (category == null) return;

        try
        {
            // 该回调可能来自任意线程，写入器内部串行化；这里只负责筛选与转发。
            _writer.Write(DateTime.UtcNow, category, condition, stackTrace);
        }
        catch (Exception)
        {
            // 记录失败现场本身绝不能影响游戏继续运行。
        }
    }

    private static string? Category(LogType type)
    {
        switch (type)
        {
            case LogType.Error: return "Error";
            case LogType.Exception: return "Exception";
            case LogType.Assert: return "Assert";
            default: return null;
        }
    }
}
