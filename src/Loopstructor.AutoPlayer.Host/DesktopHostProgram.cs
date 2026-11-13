using System.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Loopstructor.AutoPlayer.Host;

internal static class DesktopHostProgram
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        Converters = { new StringEnumConverter(new CamelCaseNamingStrategy()) }
    };

    public static async Task<int> RunAsync(IReadOnlyList<string> args)
    {
        int parentProcessId = ParseParentProcessId(args);
        using CancellationTokenSource lifetime = new();
        using DesktopHostWriter writer = new(Console.Out, JsonSettings);
        await using DesktopHostEngine engine = new(writer.WriteEventAsync, lifetime.Token);
        try
        {
            await engine.InitializeAsync();
            await writer.WriteEventAsync("ready", new
            {
                protocolVersion = DesktopHostProtocol.CurrentVersion,
                processId = Environment.ProcessId
            });

            Task parentMonitor = MonitorParentAsync(parentProcessId, lifetime);
            while (!lifetime.IsCancellationRequested)
            {
                string? line = await Console.In.ReadLineAsync(lifetime.Token);
                if (line == null) break;

                DesktopHostRequest? request;
                try
                {
                    request = JsonConvert.DeserializeObject<DesktopHostRequest>(line, JsonSettings);
                    if (request == null || string.IsNullOrWhiteSpace(request.Id) || string.IsNullOrWhiteSpace(request.Method))
                    {
                        throw new JsonException("请求缺少 id 或 method。");
                    }
                }
                catch (Exception exception) when (exception is JsonException or ArgumentException)
                {
                    await writer.WriteResponseAsync(new DesktopHostResponse
                    {
                        Success = false,
                        Error = "Host 请求格式无效：" + exception.Message
                    });
                    continue;
                }

                try
                {
                    JToken? result = await engine.ExecuteAsync(request.Method, request.Params);
                    await writer.WriteResponseAsync(new DesktopHostResponse
                    {
                        Id = request.Id,
                        Success = true,
                        Result = result
                    });
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    await writer.WriteResponseAsync(new DesktopHostResponse
                    {
                        Id = request.Id,
                        Success = false,
                        Error = $"{request.Method} 执行失败（{exception.GetType().Name}）：{exception.Message}"
                    });
                }
            }

            lifetime.Cancel();
            await parentMonitor;
            return 0;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            lifetime.Cancel();
        }
    }

    private static int ParseParentProcessId(IReadOnlyList<string> args)
    {
        for (int index = 0; index + 1 < args.Count; index++)
        {
            if (string.Equals(args[index], "--parent-pid", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[index + 1], out int processId)
                && processId > 0)
            {
                return processId;
            }
        }

        return 0;
    }

    private static async Task MonitorParentAsync(int processId, CancellationTokenSource lifetime)
    {
        if (processId <= 0) return;
        try
        {
            using Process process = Process.GetProcessById(processId);
            Task waitForExit = process.WaitForExitAsync(lifetime.Token);
            // Belt and braces: also poll the parent every second. WaitForExitAsync can miss the exit
            // when the parent handle is inherited through a launcher, and a stale Host would then
            // keep its named pipe, log tailers and game polling alive forever.
            while (!lifetime.IsCancellationRequested)
            {
                Task delay = Task.Delay(TimeSpan.FromSeconds(1), lifetime.Token);
                Task finished = await Task.WhenAny(waitForExit, delay).ConfigureAwait(false);
                if (finished == waitForExit || process.HasExited)
                {
                    lifetime.Cancel();
                    return;
                }

                try
                {
                    using Process probe = Process.GetProcessById(processId);
                    if (probe.HasExited)
                    {
                        lifetime.Cancel();
                        return;
                    }
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
                {
                    lifetime.Cancel();
                    return;
                }
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            lifetime.Cancel();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
    }

    private sealed class DesktopHostWriter : IDisposable
    {
        private readonly TextWriter _writer;
        private readonly JsonSerializerSettings _settings;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public DesktopHostWriter(TextWriter writer, JsonSerializerSettings settings)
        {
            _writer = writer;
            _settings = settings;
        }

        public Task WriteEventAsync(string name, object? payload) => WriteAsync(new DesktopHostEvent
        {
            Event = name,
            Payload = payload == null ? null : JToken.FromObject(payload, JsonSerializer.Create(_settings))
        });

        public Task WriteResponseAsync(DesktopHostResponse response) => WriteAsync(response);

        private async Task WriteAsync(object value)
        {
            string line = JsonConvert.SerializeObject(value, Formatting.None, _settings);
            await _gate.WaitAsync();
            try
            {
                await _writer.WriteLineAsync(line);
                await _writer.FlushAsync();
            }
            finally
            {
                _gate.Release();
            }
        }

        public void Dispose() => _gate.Dispose();
    }
}
