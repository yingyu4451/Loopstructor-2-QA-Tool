using System.Net;
using System.Net.Http;
using System.Text;
using Loopstructor.AutoPlayer.Core;
using Loopstructor.AutoPlayer.Manager.Models;
using Loopstructor.AutoPlayer.Manager.Services;
using Newtonsoft.Json.Linq;

namespace Loopstructor.AutoPlayer.Tests;

/// <summary>
/// Editor Play Mode 没有 BepInEx 插件，运行状态只能由 /api/status 轮询带回。
/// 这里固化「Editor 上报 → 连接结果 → 备份状态」这条链，以及备份服务最终会落快照。
/// </summary>
public sealed class EditorAutoBackupTests
{
    [Fact]
    public void ApplyRunStatus_MapsEditorChapterLayerAndSaveRoot()
    {
        AutoPlayerStatus status = new();
        new EditorBridgeConnectionResult
        {
            RunStatusReady = true,
            ActiveSaveRoot = @"C:\saves\slot_demo-v1",
            CurrentChapter = 3,
            CurrentMapLayer = 2
        }.ApplyRunStatus(status);

        Assert.Equal(@"C:\saves\slot_demo-v1", status.ActiveSaveRoot);
        Assert.Equal(3, status.CurrentChapter);
        Assert.Equal(2, status.CurrentMapLayer);
    }

    [Fact]
    public void ApplyRunStatus_ClearsStaleValuesWhenPlayModeHasNoSaveYet()
    {
        AutoPlayerStatus status = new()
        {
            ActiveSaveRoot = @"C:\saves\old",
            CurrentMapStage = 4,
            CurrentMapLayer = 9
        };

        new EditorBridgeConnectionResult { RunStatusReady = false }.ApplyRunStatus(status);

        Assert.Equal(string.Empty, status.ActiveSaveRoot);
        Assert.Equal(0, status.CurrentChapter);
        Assert.Equal(-1, status.CurrentMapLayer);
    }

    [Fact]
    public async Task ConnectAsync_ReadsPlayModeSaveStatusFromTheStatusEndpoint()
    {
        JObject payload = new()
        {
            ["success"] = true,
            ["schemaVersion"] = EditorBridgeRegistry.CurrentSchemaVersion,
            ["protocolVersion"] = EditorBridgeRegistry.CurrentProtocolVersion,
            ["instanceId"] = "editor-1234",
            ["processId"] = 4321,
            ["projectPath"] = @"D:\Unity Project\Loopstructor2",
            ["unityVersion"] = "2022.3.62f3c1",
            ["gameVersion"] = "1.460",
            ["sceneName"] = "NewGameScene",
            ["mode"] = "editor-play",
            ["runtimeReady"] = true,
            ["assemblySha256"] = "abc123",
            ["runStatusReady"] = true,
            ["activeSaveRoot"] = @"C:\saves\slot_demo-v1",
            ["currentChapter"] = 3,
            ["currentMapLayer"] = 2
        };
        EditorBridgeClient client = new(new HttpClient(new StubHttpMessageHandler(payload.ToString())));
        TrustedEditorBridgeInstance instance = new()
        {
            InstanceId = "editor-1234",
            ProcessId = 4321,
            ProjectPath = @"D:\Unity Project\Loopstructor2",
            AssemblySha256 = "abc123",
            Port = 45678,
            Token = "token"
        };

        EditorBridgeConnectionResult result = await client.ConnectAsync(instance);

        Assert.True(result.Success);
        Assert.True(result.RunStatusReady);
        Assert.Equal(@"C:\saves\slot_demo-v1", result.ActiveSaveRoot);
        Assert.Equal(3, result.CurrentChapter);
        Assert.Equal(2, result.CurrentMapLayer);
    }

    [Fact]
    public async Task EditorShapedStatus_StillProducesAutoBackups()
    {
        string root = Path.Combine(Path.GetTempPath(), "loopstructor-editor-backup-tests", Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "game-save");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "profile.json"), "{}");
        try
        {
            AutoPlayerStatus status = new() { ActivationMode = AutoPlayerActivationMode.ResidentPlayer };
            new EditorBridgeConnectionResult
            {
                RunStatusReady = true,
                ActiveSaveRoot = source,
                CurrentChapter = 1,
                CurrentMapLayer = 1
            }.ApplyRunStatus(status);
            SaveBackupService service = new(Path.Combine(root, "backups"), TimeSpan.Zero);
            ManagerSettings settings = new() { AutomaticSaveBackupEnabled = true, MaximumSaveBackups = 5 };
            string? gameRoot = Path.GetDirectoryName(source);

            await service.ObserveAsync(status, settings, gameRoot, CancellationToken.None);
            await service.ObserveAsync(status, settings, gameRoot, CancellationToken.None);
            await service.ObserveAsync(status, settings, gameRoot, CancellationToken.None);

            Assert.Equal(1, service.Snapshot(true, 5).BackupCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class StubHttpMessageHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }
}
