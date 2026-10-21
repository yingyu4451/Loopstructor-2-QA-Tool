using System.Text;

namespace Loopstructor.AutoPlayer.Manager.Services;

public sealed class LogTailReader
{
    private const byte LineFeed = (byte)'\n';
    private const byte CarriageReturn = (byte)'\r';
    private string _path = string.Empty;
    private long _position;

    public void Reset(string path, bool startAtEnd = false)
    {
        _path = Path.GetFullPath(path);
        _position = startAtEnd && File.Exists(_path)
            ? new FileInfo(_path).Length
            : 0;
    }

    public IReadOnlyList<string> ReadAvailable(int maximumLines = 250)
    {
        if (string.IsNullOrWhiteSpace(_path) || !File.Exists(_path))
        {
            return Array.Empty<string>();
        }

        using FileStream stream = new(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length < _position)
        {
            _position = 0;
        }

        long startPosition = _position;
        stream.Position = startPosition;
        int limit = Math.Max(1, maximumLines);
        List<string> lines = new(limit);
        List<byte> current = new(256);
        byte[] buffer = new byte[64 * 1024];
        long consumed = 0;
        long completePosition = startPosition;
        int read;
        while (lines.Count < limit && (read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int offset = 0; offset < read && lines.Count < limit; offset++)
            {
                consumed++;
                if (buffer[offset] != LineFeed)
                {
                    current.Add(buffer[offset]);
                    continue;
                }

                int count = current.Count;
                if (count > 0 && current[count - 1] == CarriageReturn) count--;
                string line = Encoding.UTF8.GetString(current.ToArray(), 0, count);
                if (lines.Count == 0 && startPosition == 0) line = line.TrimStart('\uFEFF');
                lines.Add(line);
                current.Clear();
                completePosition = startPosition + consumed;
            }
        }

        // Only advance past whole lines. A trailing partial line is re-read on the next call, once the writer
        // has completed it, so lines are never split or dropped between polls.
        _position = lines.Count > 0 ? completePosition : startPosition;

        return lines;
    }
}
