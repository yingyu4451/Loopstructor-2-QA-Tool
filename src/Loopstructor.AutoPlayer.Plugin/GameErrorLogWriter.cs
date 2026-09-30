using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Loopstructor.AutoPlayer.Plugin;

/// <summary>
/// game-errors.log 的写入器。刻意不依赖 UnityEngine，便于直接测试：
/// 去重、限流、格式化与轮转都在这里完成，Unity 侧的日志回调只做转发。
/// </summary>
internal sealed class GameErrorLogWriter
{
    private const int MaximumTrackedErrors = 512;
    private const int MaximumStackLines = 12;
    private static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(5);
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _path;
    private readonly long _maximumBytes;
    private readonly object _gate = new();
    private readonly Dictionary<string, RepeatState> _repeats = new(StringComparer.Ordinal);
    private long _length;

    internal GameErrorLogWriter(string path, long maximumBytes)
    {
        _path = Path.GetFullPath(path);
        _maximumBytes = maximumBytes > 0 ? maximumBytes : 1;
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        _length = File.Exists(_path) ? new FileInfo(_path).Length : 0;
    }

    /// <summary>
    /// 追加一条报错。<paramref name="category"/> 为 Error/Exception/Assert 之一。
    /// 相同 category + message 在去重窗口内重复出现时只累加计数，
    /// 下一次落盘时在标题行注明合并了多少次。
    /// </summary>
    internal void Write(DateTime utc, string category, string message, string? stackTrace)
    {
        lock (_gate)
        {
            string key = category + "\u0000" + message;
            int merged = 0;
            bool tracked = _repeats.TryGetValue(key, out RepeatState? state);
            if (tracked && state != null && utc - state.LastUtc < RepeatWindow)
            {
                state.Merged++;
                return;
            }

            if (tracked && state != null) merged = state.Merged;
            if (_repeats.Count >= MaximumTrackedErrors) _repeats.Clear();
            _repeats[key] = new RepeatState { LastUtc = utc };
            Append(Format(utc, category, message, stackTrace, merged));
        }
    }

    private static string Format(DateTime utc, string category, string message, string? stackTrace, int merged)
    {
        StringBuilder builder = new();
        builder.Append('[')
            .Append(utc.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append("Z] [")
            .Append(category)
            .Append("] ")
            .Append(string.IsNullOrWhiteSpace(message) ? "(无消息)" : message.Trim());
        if (merged > 0)
        {
            builder.Append("（合并 ").Append(merged.ToString(CultureInfo.InvariantCulture)).Append(" 次）");
        }

        builder.Append('\n');
        int emitted = 0;
        foreach (string line in (stackTrace ?? string.Empty).Split('\n'))
        {
            string trimmed = line.TrimEnd('\r').Trim();
            if (trimmed.Length == 0) continue;
            builder.Append("    ").Append(trimmed).Append('\n');
            if (++emitted >= MaximumStackLines) break;
        }

        return builder.ToString();
    }

    private void Append(string text)
    {
        byte[] bytes = Utf8NoBom.GetBytes(text);
        if (_length + bytes.Length > _maximumBytes) Rotate();
        using FileStream stream = new(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        stream.Write(bytes, 0, bytes.Length);
        _length += bytes.Length;
    }

    private void Rotate()
    {
        try
        {
            // netstandard2.1 没有 File.Move(source, destination, overwrite) 重载。
            string backup = _path + ".1";
            if (File.Exists(backup)) File.Delete(backup);
            if (File.Exists(_path)) File.Move(_path, backup);
        }
        catch (IOException)
        {
            // 轮转失败时继续追加，日志宁可变大也不要丢。
        }

        _length = 0;
    }

    private sealed class RepeatState
    {
        internal DateTime LastUtc;
        internal int Merged;
    }
}
