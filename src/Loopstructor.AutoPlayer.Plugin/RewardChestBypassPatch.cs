using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Loopstructor.AutoPlayer.Plugin;

/// <summary>
/// 临时解除“有未领取奖励箱就不能选关”的 UI 门禁。
/// 不领取、不清空奖励，也不改奖励账本；只在短时间窗口内把选关入口重新打开。
/// </summary>
internal static class RewardChestBypassPatch
{
    public const float ActivationSeconds = 30f;
    private const string HarmonyId = "com.loopstructor.qa-tool.reward-chest-bypass";
    private static readonly object Sync = new();
    private static Harmony? _harmony;
    private static bool _installed;
    private static float _activeUntil;
    private static PropertyInfo? _roomPanelInstance;
    private static MethodInfo? _roomPanelSetBlocked;
    private static PropertyInfo? _multiFunInstance;
    private static MethodInfo? _multiFunSetBlocked;

    public static bool IsActive => Time.realtimeSinceStartup < _activeUntil;

    public static bool TryActivate(out string message)
    {
        message = string.Empty;
        if (!EnsureInstalled(out string installError))
        {
            message = installError;
            return false;
        }

        _activeUntil = Time.realtimeSinceStartup + ActivationSeconds;
        if (ApplyOpen(out _))
        {
            message = $"已临时解除奖励箱选关锁 {ActivationSeconds:0} 秒；奖励不会自动领取。";
            return true;
        }

        message = $"已临时解除奖励箱选关锁 {ActivationSeconds:0} 秒；当前选关入口尚未创建，进入选关界面后自动生效。";
        return true;
    }

    public static void Reset()
    {
        _activeUntil = 0f;
    }

    internal static bool TryInstall(out string error) => EnsureInstalled(out error);

    private static bool EnsureInstalled(out string error)
    {
        lock (Sync)
        {
            if (_installed && _harmony != null)
            {
                error = string.Empty;
                return true;
            }

            try
            {
                if (!ResolveGateAccessors(out error)) return false;

                MethodInfo? prefix = typeof(RewardChestBypassPatch).GetMethod(
                    nameof(ForceOpen),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (prefix == null || _roomPanelSetBlocked == null || _multiFunSetBlocked == null)
                {
                    error = "奖励箱选关补丁初始化失败。";
                    return false;
                }

                HarmonyMethod harmonyPrefix = new(prefix);
                _harmony = new Harmony(HarmonyId);
                _harmony.Patch(_roomPanelSetBlocked, prefix: harmonyPrefix);
                _harmony.Patch(_multiFunSetBlocked, prefix: harmonyPrefix);
                _installed = true;
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = "安装奖励箱选关补丁失败：" + exception.Message;
                return false;
            }
        }
    }

    /// <summary>门禁刷新会把 blocked 写回 true；前缀在写入前临时改为 false（__0 即第一个参数）。</summary>
    private static void ForceOpen(ref bool __0)
    {
        if (IsActive) __0 = false;
    }

    private static bool ApplyOpen(out string error)
    {
        error = string.Empty;
        if (!ResolveGateAccessors(out error)) return false;

        try
        {
            bool applied = false;
            object? roomPanel = _roomPanelInstance?.GetValue(null, null);
            if (roomPanel != null && _roomPanelSetBlocked != null)
            {
                _roomPanelSetBlocked.Invoke(roomPanel, new object[] { false });
                applied = true;
            }

            object? multiFun = _multiFunInstance?.GetValue(null, null);
            if (multiFun != null && _multiFunSetBlocked != null)
            {
                _multiFunSetBlocked.Invoke(multiFun, new object[] { false });
                applied = true;
            }

            if (!applied)
            {
                error = "当前不在选关界面。";
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            error = "解除奖励箱选关锁失败：" + exception.Message;
            return false;
        }
    }

    private static bool ResolveGateAccessors(out string error)
    {
        error = string.Empty;
        if (_roomPanelInstance != null && _roomPanelSetBlocked != null &&
            _multiFunInstance != null && _multiFunSetBlocked != null)
            return true;

        try
        {
            Type? roomPanelType = AccessTools.TypeByName("MetroTD.UISystem.RoomMapChoosePanel");
            Type? multiFunType = AccessTools.TypeByName("MetroTD.UISystem.MultiFunBtn");
            const BindingFlags staticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            const BindingFlags instanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            _roomPanelInstance = roomPanelType?.GetProperty("Instance", staticFlags);
            _roomPanelSetBlocked = roomPanelType?.GetMethod(
                "SetRewardChoiceBlocked", instanceFlags, null, new[] { typeof(bool) }, null);
            _multiFunInstance = multiFunType?.GetProperty("Instance", staticFlags);
            _multiFunSetBlocked = multiFunType?.GetMethod(
                "SetRewardChoiceBlocked", instanceFlags, null, new[] { typeof(bool) }, null);
        }
        catch (Exception exception)
        {
            error = "解析奖励箱选关接口失败：" + exception.Message;
            return false;
        }

        if (_roomPanelInstance == null || _roomPanelSetBlocked == null ||
            _multiFunInstance == null || _multiFunSetBlocked == null)
        {
            error = "当前游戏版本缺少奖励箱选关接口，无法临时开放选关。";
            return false;
        }

        return true;
    }
}
