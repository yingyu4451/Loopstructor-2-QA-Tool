using System;
using HarmonyLib;
using Loopstructor.AutoPlayer.Core;
using Loopstructor.AutoPlayer.Plugin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Loopstructor.AutoPlayer.EditorBridge.Runtime;

/// <summary>在 Unity Editor Play Mode 中托管无 BepInEx 依赖的 QA 运行控制。</summary>
public static class UnityEditorCheatBridge
{
    private static EditorCheatRuntimeHost? m_host;

    /// <summary>获取 Editor 运行控制是否已就绪。</summary>
    public static bool IsRunning => m_host != null && m_host.Runtime.IsAvailable;

    /// <summary>创建 Play Mode 运行宿主。</summary>
    public static bool TryStart(string artifactRoot, out string message)
    {
        if (m_host != null)
        {
            message = IsRunning ? "Unity Editor Play Mode 运行控制已就绪。" : "Unity Editor Play Mode 运行控制不可用。";
            return IsRunning;
        }

        GameObject hostObject = new("Loopstructor.QA.EditorRuntime")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        UnityEngine.Object.DontDestroyOnLoad(hostObject);
        EditorCheatRuntimeHost host = hostObject.AddComponent<EditorCheatRuntimeHost>();
        if (!host.Initialize(artifactRoot, out message))
        {
            UnityEngine.Object.Destroy(hostObject);
            return false;
        }

        m_host = host;
        return true;
    }

    /// <summary>在 Unity 主线程执行一条白名单 QA 命令。</summary>
    public static EditorCheatResponse Execute(string command, JObject? arguments) =>
        m_host == null
            ? EditorCheatResponse.Fail("Unity Editor 尚未进入可控制的 Play Mode。", "EDITOR_RUNTIME_NOT_READY")
            : m_host.Runtime.Execute(command, arguments ?? new JObject());

    /// <summary>停止运行控制并清除全部瞬态效果。</summary>
    public static void Stop()
    {
        EditorCheatRuntimeHost? host = m_host;
        m_host = null;
        if (host == null) return;
        host.Runtime.Dispose();
        UnityEngine.Object.Destroy(host.gameObject);
    }

    internal static void NotifyDestroyed(EditorCheatRuntimeHost host)
    {
        if (ReferenceEquals(m_host, host)) m_host = null;
    }
}

/// <summary>Editor Bridge 返回给 Host 的统一命令结果。</summary>
public sealed class EditorCheatResponse
{
    /// <summary>获取命令是否成功。</summary>
    public bool Success { get; set; }
    /// <summary>获取命令是否修改了游戏状态。</summary>
    public bool Mutated { get; set; }
    /// <summary>获取面向用户的结果说明。</summary>
    public string Message { get; set; } = string.Empty;
    /// <summary>获取稳定错误码。</summary>
    public string ErrorCode { get; set; } = string.Empty;
    /// <summary>获取命令返回的数据。</summary>
    public JObject Data { get; set; } = new();

    internal static EditorCheatResponse From(CheatExecutionResult result) => new()
    {
        Success = result.Success,
        Mutated = result.Mutated,
        Message = result.Message,
        ErrorCode = result.ErrorCode,
        Data = result.Data
    };

    internal static EditorCheatResponse Fail(string message, string errorCode = "CHEAT_COMMAND_FAILED") => new()
    {
        Message = message,
        ErrorCode = errorCode,
        Data = new JObject { ["errorCode"] = errorCode }
    };
}

internal sealed class EditorCheatRuntimeHost : MonoBehaviour
{
    public EditorCheatRuntime Runtime { get; } = new();

    public bool Initialize(string artifactRoot, out string message) => Runtime.Initialize(artifactRoot, out message);

    private void Update() => Runtime.Tick();

    private void OnGUI() => Runtime.Draw();

    private void OnDestroy()
    {
        Runtime.Dispose();
        UnityEditorCheatBridge.NotifyDestroyed(this);
    }
}

internal sealed class EditorCheatRuntime : IDisposable
{
    private const string HarmonyId = "com.loopstructor.qa-editor-bridge.map-skip";

    private readonly CheatRuntimeBridge m_bridge = new();
    private readonly GameHotkeyReader m_hotkeys = new();
    private Harmony? m_harmony;
    private bool m_mapSkipInstalled;
    private bool m_hotkeysEnabled;
    private string m_hotkeyResolutionError = string.Empty;
    private bool m_enabled;
    private bool m_disposed;

    public bool IsAvailable => !m_disposed && m_bridge.IsAvailable;

    public bool Initialize(string artifactRoot, out string message)
    {
        m_bridge.Initialize(artifactRoot);
        if (!m_bridge.IsAvailable)
        {
            message = "当前游戏缺少 Editor 运行控制需要的成员：" + string.Join("、", m_bridge.MissingMembers);
            return false;
        }

        m_enabled = true;
        TryInstallMapSkip();
        RegisterEvents();
        message = "Unity Editor Play Mode 运行控制已启动。";
        return true;
    }

    public EditorCheatResponse Execute(string command, JObject arguments)
    {
        if (!CheatCommands.All.Contains(command, StringComparer.Ordinal))
            return EditorCheatResponse.Fail("未知的作弊命令：" + command, "UNKNOWN_CHEAT_COMMAND");

        try
        {
            if (string.Equals(command, CheatCommands.SetEnabled, StringComparison.Ordinal))
            {
                m_enabled = arguments.Value<bool?>("enabled") != false;
                if (!m_enabled) ResetEditorEffects();
                return EditorCheatResponse.From(CheatExecutionResult.Ok(
                    m_enabled ? "Editor 运行控制已启用。" : "Editor 运行控制已停用。",
                    BuildState()));
            }
            if (string.Equals(command, CheatCommands.QueryCatalog, StringComparison.Ordinal))
                return EditorCheatResponse.From(CheatExecutionResult.Ok("已读取 QA 项目目录。", m_bridge.QueryCatalog()));
            if (string.Equals(command, CheatCommands.QueryState, StringComparison.Ordinal))
                return EditorCheatResponse.From(CheatExecutionResult.Ok("已读取 QA 运行状态。", BuildState()));
            if (!m_enabled) return EditorCheatResponse.Fail("Editor 运行控制已停用。", "CHEAT_MODE_DISABLED");

            CheatExecutionResult result = command switch
            {
                CheatCommands.GrantVehicle => m_bridge.GrantVehicle(arguments),
                CheatCommands.RemoveVehicle => m_bridge.RemoveVehicle(arguments),
                CheatCommands.ClearFieldVehicles => m_bridge.ClearFieldVehicles(),
                CheatCommands.ClearBackpackVehicles => m_bridge.ClearBackpackVehicles(),
                CheatCommands.GrantDisposable => m_bridge.GrantDisposable(arguments),
                CheatCommands.ClearConsumables => m_bridge.ClearConsumables(),
                CheatCommands.GrantCatapultPoint => m_bridge.GrantCatapultPoint(arguments),
                CheatCommands.RemoveCatapultPoint => m_bridge.RemoveCatapultPoint(arguments),
                CheatCommands.ClearBackpackCatapultPoints => m_bridge.ClearBackpackCatapultPoints(),
                CheatCommands.RemoveFieldCatapultPoint => m_bridge.RemoveFieldCatapultPoint(arguments),
                CheatCommands.ClearFieldCatapultPoints => m_bridge.ClearFieldCatapultPoints(),
                CheatCommands.SetFieldCatapultDeleteMode => m_bridge.SetFieldCatapultDeleteMode(arguments.Value<bool?>("enabled") == true),
                CheatCommands.SetBaseGodMode => m_bridge.SetBaseGodMode(arguments),
                CheatCommands.EndWave => m_bridge.EndWave(),
                CheatCommands.ClearEnemies => m_bridge.ClearEnemies(),
                CheatCommands.SkipRewardPopup => m_bridge.SkipRewardPopup(),
                CheatCommands.QueryVehicles => CheatExecutionResult.Ok("已读取当前战车。", m_bridge.QueryVehicles()),
                CheatCommands.ModifyVehicle => m_bridge.ModifyVehicle(arguments),
                CheatCommands.SetVehicleEnchantment => m_bridge.SetVehicleEnchantment(arguments),
                CheatCommands.QueryEnemies => CheatExecutionResult.Ok("已读取当前敌人。", m_bridge.QueryEnemies()),
                CheatCommands.ModifyEnemy => m_bridge.ModifyEnemy(arguments),
                CheatCommands.SetEnemyIdOverlay => SetEnemyIdOverlay(arguments),
                CheatCommands.SetEnemyBuffOverlay => SetEnemyBuffOverlay(arguments),
                CheatCommands.GrantRelic => m_bridge.GrantRelic(arguments),
                CheatCommands.GrantAllRelics => m_bridge.StartGrantAllRelics(),
                CheatCommands.RemoveRelic => m_bridge.RemoveRelic(arguments),
                CheatCommands.RemoveAllRelics => m_bridge.StartRemoveAllRelics(),
                CheatCommands.SetSpawnPointCapture => m_bridge.SetSpawnPointCapture(arguments),
                CheatCommands.RemoveSpawnPoint => m_bridge.RemoveSpawnPoint(arguments),
                CheatCommands.ClearSpawnPoints => m_bridge.ClearSpawnPoints(),
                CheatCommands.SpawnEnemy => m_bridge.SpawnEnemy(arguments),
                CheatCommands.SetMapSkipEnabled => SetMapSkipEnabled(arguments),
                CheatCommands.SetHotkeysEnabled => SetHotkeysEnabled(arguments),
                CheatCommands.SetCatalogPoolMode => m_bridge.SetCatalogPoolMode(arguments.Value<string?>("mode")),
                _ => CheatExecutionResult.Fail("未知的作弊命令：" + command, "UNKNOWN_CHEAT_COMMAND")
            };
            return EditorCheatResponse.From(result);
        }
        catch (Exception exception)
        {
            Exception error = CheatRuntimeBridge.Unwrap(exception);
            return EditorCheatResponse.Fail("执行 Editor QA 命令时发生异常：" + error.Message);
        }
    }

    public void Tick()
    {
        if (!IsAvailable || !m_enabled) return;
        SpawnPointCaptureInputPatch.Dispatch();
        m_bridge.TickGrantAllRelics();
        m_bridge.TickRemoveAllRelics();
        m_bridge.TickEnemyOverlays();
        if (m_hotkeysEnabled) PollHotkeys();
        if (MapSkipPatch.Enabled) MapSkipPatch.Tick();
    }

    public void Draw()
    {
        if (IsAvailable && m_enabled) m_bridge.DrawEnemyOverlays();
    }

    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;
        UnregisterEvents();
        m_bridge.CancelGrantAllRelics("Editor Play Mode 已结束。");
        m_bridge.CancelRemoveAllRelics("Editor Play Mode 已结束。");
        ResetEditorEffects();
        SpawnPointCaptureInputPatch.Detach();
        if (m_harmony != null)
        {
            MapSkipPatch.Uninstall(m_harmony);
            m_harmony = null;
        }

        m_mapSkipInstalled = false;
        m_hotkeysEnabled = false;
        m_bridge.ResetCatalogPoolMode();
    }

    private void ResetEditorEffects()
    {
        m_bridge.ResetTransientFeatures();
        MapSkipPatch.Reset();
        m_hotkeysEnabled = false;
        m_hotkeyResolutionError = string.Empty;
        m_bridge.ResetCatalogPoolMode();
    }

    private CheatExecutionResult SetHotkeysEnabled(JObject arguments)
    {
        bool enabled = arguments.Value<bool?>("enabled") != false;
        m_hotkeysEnabled = enabled;
        m_hotkeyResolutionError = string.Empty;
        if (!enabled)
        {
            return CheatExecutionResult.Changed(
                "游戏内快捷键已关闭。",
                new JObject { ["hotkeysEnabled"] = false, ["hotkeysAvailable"] = m_hotkeys.IsReady });
        }

        bool resolved = m_hotkeys.TryResolve(out string reason);
        if (!resolved)
        {
            m_hotkeyResolutionError = reason;
            Debug.LogWarning("[QA EditorBridge] 游戏内快捷键已开启，但游戏输入系统尚未就绪：" + reason);
        }

        string message = resolved
            ? "游戏内快捷键已开启：F 清除场上敌人，F1 结束当前波次。"
            : "游戏内快捷键已开启，但游戏输入系统尚未就绪，进入战斗后会自动重试：" + reason;
        return CheatExecutionResult.Changed(
            message,
            new JObject { ["hotkeysEnabled"] = true, ["hotkeysAvailable"] = resolved });
    }

    private CheatExecutionResult SetMapSkipEnabled(JObject arguments)
    {
        bool enabled = arguments.Value<bool?>("enabled") == true;
        if (enabled && !m_mapSkipInstalled) EnsureMapSkipInstalled();
        if (!MapSkipPatch.SetEnabled(enabled))
        {
            return CheatExecutionResult.Fail(
                "地图自由跳转当前不可用：当前游戏版本缺少地图节点或按钮入口。",
                "EDITOR_MAP_SKIP_UNAVAILABLE");
        }

        return CheatExecutionResult.Changed(
            enabled
                ? "地图自由跳转已开启，可以选择当前进度之后的任意节点。"
                : "地图自由跳转已关闭，地图可达性已恢复。",
            new JObject { ["mapSkipEnabled"] = MapSkipPatch.Enabled });
    }

    private void EnsureMapSkipInstalled()
    {
        if (!m_mapSkipInstalled) TryInstallMapSkip();
    }

    private void TryInstallMapSkip()
    {
        try
        {
            m_harmony ??= new Harmony(HarmonyId);
            m_mapSkipInstalled = MapSkipPatch.Install(m_harmony, line => Debug.Log("[QA EditorBridge] " + line));
        }
        catch (Exception exception)
        {
            m_mapSkipInstalled = false;
            Debug.LogWarning(
                "[QA EditorBridge] 地图自由跳转补丁安装失败：" + CheatRuntimeBridge.Unwrap(exception).Message);
        }
    }

    private void PollHotkeys()
    {
        if (!m_hotkeys.TryResolve(out string reason))
        {
            if (!string.Equals(m_hotkeyResolutionError, reason, StringComparison.Ordinal))
            {
                m_hotkeyResolutionError = reason;
                Debug.LogWarning("[QA EditorBridge] 游戏内快捷键暂不可用：" + reason);
            }

            return;
        }

        if (!string.IsNullOrEmpty(m_hotkeyResolutionError))
        {
            m_hotkeyResolutionError = string.Empty;
            Debug.Log("[QA EditorBridge] 游戏内快捷键已就绪：F 清除场上敌人，F1 结束当前波次。");
        }

        if (m_hotkeys.WasPressedThisFrame(GameHotkey.SkipWave)) TriggerHotkey(CheatCommands.EndWave, "F1");
        if (m_hotkeys.WasPressedThisFrame(GameHotkey.ClearEnemies)) TriggerHotkey(CheatCommands.ClearEnemies, "F");
    }

    private void TriggerHotkey(string command, string keyName)
    {
        try
        {
            CheatExecutionResult result = string.Equals(command, CheatCommands.EndWave, StringComparison.Ordinal)
                ? m_bridge.EndWave()
                : m_bridge.ClearEnemies();
            if (result.Success) Debug.Log("[QA EditorBridge] 游戏内快捷键 " + keyName + "：" + result.Message);
            else Debug.Log("[QA EditorBridge] 游戏内快捷键 " + keyName + " 未生效：" + result.Message);
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "[QA EditorBridge] 游戏内快捷键 " + keyName + " 执行失败：" +
                CheatRuntimeBridge.Unwrap(exception).Message);
        }
    }

    private JObject BuildState()
    {
        JObject state = new()
        {
            ["available"] = IsAvailable,
            ["enabled"] = m_enabled,
            ["enemyIdsVisible"] = m_bridge.EnemyIdsVisible,
            ["enemyBuffsVisible"] = m_bridge.EnemyBuffsVisible,
            ["baseGodMode"] = m_bridge.BaseGodModeRequested,
            ["mapSkipEnabled"] = MapSkipPatch.Enabled,
            ["hotkeysEnabled"] = m_hotkeysEnabled,
            ["hotkeysAvailable"] = m_hotkeys.IsReady,
            ["catalogPoolMode"] = m_bridge.CatalogPoolMode,
            ["spawnPointCapture"] = m_bridge.SpawnPointCaptureData(),
            ["fieldCatapultDeleteMode"] = m_bridge.FieldCatapultDeleteMode,
            ["protocolVersion"] = Protocol.CheatCurrentVersion,
            ["availabilityReason"] = IsAvailable ? string.Empty : "Editor 运行控制不可用。",
            ["capabilities"] = JArray.FromObject(CheatCommands.All)
        };
        if (IsAvailable)
        {
            state.Merge(m_bridge.QueryOwnedState(), new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
        }
        return state;
    }

    private CheatExecutionResult SetEnemyIdOverlay(JObject arguments)
    {
        bool visible = arguments.Value<bool?>("visible") == true;
        m_bridge.EnemyIdsVisible = visible;
        m_bridge.InvalidateEnemyOverlayCache();
        return CheatExecutionResult.Changed(
            visible ? "已在游戏画面中显示敌人 ID。" : "已关闭游戏画面中的敌人 ID。",
            new JObject { ["visible"] = visible, ["enemyIdsVisible"] = visible });
    }

    private CheatExecutionResult SetEnemyBuffOverlay(JObject arguments)
    {
        bool visible = arguments.Value<bool?>("visible") == true;
        m_bridge.EnemyBuffsVisible = visible;
        m_bridge.InvalidateEnemyOverlayCache();
        return CheatExecutionResult.Changed(
            visible ? "已在游戏画面中显示怪物 Buff。" : "已关闭游戏画面中的怪物 Buff。",
            new JObject { ["visible"] = visible, ["enemyBuffsVisible"] = visible });
    }

    private void RegisterEvents() => SceneManager.activeSceneChanged += OnActiveSceneChanged;

    private void UnregisterEvents() => SceneManager.activeSceneChanged -= OnActiveSceneChanged;

    private void OnActiveSceneChanged(Scene previous, Scene current)
    {
        m_bridge.CancelGrantAllRelics("游戏场景已切换。");
        m_bridge.CancelRemoveAllRelics("游戏场景已切换。");
        m_bridge.ResetTransientFeatures();
        MapSkipPatch.Reset();
    }
}
