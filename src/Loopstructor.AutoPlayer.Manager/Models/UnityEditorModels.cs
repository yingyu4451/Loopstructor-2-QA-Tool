using Loopstructor.AutoPlayer.Core;

namespace Loopstructor.AutoPlayer.Manager.Models;

public sealed class UnityProjectInspection
{
    public string Path { get; init; } = string.Empty;
    public bool Valid { get; init; }
    public string UnityVersion { get; init; } = string.Empty;
    public bool BridgeInstalled { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class EditorBridgeOperationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public UnityProjectInspection? Inspection { get; init; }

    public static EditorBridgeOperationResult Fail(string message) => new() { Message = message };
}

public sealed class EditorBridgeInstance
{
    public string InstanceId { get; init; } = string.Empty;
    public string Kind { get; init; } = "editor";
    public int ProcessId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string ProjectPath { get; init; } = string.Empty;
    public string UnityVersion { get; init; } = string.Empty;
    public string GameVersion { get; init; } = string.Empty;
    public string SceneName { get; init; } = string.Empty;
    public string Mode { get; init; } = "editor-edit";
    public bool RuntimeReady { get; init; }
    public DateTime LastSeenAt { get; init; }
}

public sealed class EditorBridgeConnectionResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string InstanceId { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public string Mode { get; init; } = "editor-edit";
    public bool RuntimeReady { get; init; }
    public string SceneName { get; init; } = string.Empty;
    public string AssemblySha256 { get; init; } = string.Empty;
    public bool RunStatusReady { get; init; }
    public string ActiveSaveRoot { get; init; } = string.Empty;
    public int CurrentChapter { get; init; }
    public int CurrentMapLayer { get; init; } = -1;

    /// <summary>
    /// 把 Editor Play Mode 上报的存档根与「章-关」写进运行状态。
    /// 自动备份服务只认这三项；Editor 没有插件，只能由 /api/status 每次轮询带回。
    /// </summary>
    public void ApplyRunStatus(AutoPlayerStatus status)
    {
        if (RunStatusReady)
        {
            status.ActiveSaveRoot = ActiveSaveRoot;
            status.CurrentMapStage = CurrentChapter > 0 ? CurrentChapter - 1 : -1;
            status.CurrentMapLayer = CurrentMapLayer;
            return;
        }

        status.ActiveSaveRoot = string.Empty;
        status.CurrentMapStage = -1;
        status.CurrentMapLayer = -1;
    }
}

internal sealed class TrustedEditorBridgeInstance
{
    public string InstanceId { get; init; } = string.Empty;
    public string Kind { get; init; } = "editor";
    public int ProcessId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string ProjectPath { get; init; } = string.Empty;
    public string UnityExecutablePath { get; init; } = string.Empty;
    public string UnityVersion { get; init; } = string.Empty;
    public string GameVersion { get; init; } = string.Empty;
    public string SceneName { get; init; } = string.Empty;
    public string Mode { get; init; } = "editor-edit";
    public bool RuntimeReady { get; init; }
    public int Port { get; init; }
    public string ProtocolVersion { get; init; } = string.Empty;
    public string Token { get; init; } = string.Empty;
    public string AssemblySha256 { get; init; } = string.Empty;
    public string ArtifactRoot { get; init; } = string.Empty;
    public DateTime LastSeenAt { get; init; }

    public EditorBridgeInstance ToPublic() => new()
    {
        InstanceId = InstanceId,
        Kind = Kind,
        ProcessId = ProcessId,
        DisplayName = DisplayName,
        ProjectPath = ProjectPath,
        UnityVersion = UnityVersion,
        GameVersion = GameVersion,
        SceneName = SceneName,
        Mode = Mode,
        RuntimeReady = RuntimeReady,
        LastSeenAt = LastSeenAt
    };
}
