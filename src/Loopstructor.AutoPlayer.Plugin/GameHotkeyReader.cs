using System;
using System.Reflection;

namespace Loopstructor.AutoPlayer.Plugin;

internal enum GameHotkey
{
    ClearEnemies,
    SkipWave
}

/// <summary>
/// Reads the game's own developer-mode cheat keys (F clears the field, F1 ends the wave)
/// through the Unity Input System. The plugin only compiles against Unity modules, never
/// against Unity.InputSystem, so the device graph is resolved reflectively. A miss is not
/// fatal: the reader retries on a short throttle because the input system initializes late.
/// </summary>
internal sealed class GameHotkeyReader
{
    private const string KeyboardTypeName = "UnityEngine.InputSystem.Keyboard";
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(2);

    private object? _keyboard;
    private PropertyInfo? _fKey;
    private PropertyInfo? _f1Key;
    private PropertyInfo? _wasPressedThisFrame;
    private DateTime _nextResolveUtc;
    private string _lastError = string.Empty;

    public bool IsReady => _keyboard != null && _fKey != null && _f1Key != null && _wasPressedThisFrame != null;

    public bool TryResolve(out string reason)
    {
        if (IsReady)
        {
            reason = string.Empty;
            return true;
        }

        if (DateTime.UtcNow < _nextResolveUtc)
        {
            reason = _lastError;
            return false;
        }

        _nextResolveUtc = DateTime.UtcNow + RetryInterval;
        try
        {
            Type? keyboardType = FindType(KeyboardTypeName);
            if (keyboardType == null)
            {
                return Fail(out reason, "游戏输入系统尚未加载，找不到 " + KeyboardTypeName + "。");
            }

            PropertyInfo? current = keyboardType.GetProperty("current", BindingFlags.Public | BindingFlags.Static);
            object? keyboard = current?.GetValue(null);
            if (keyboard == null)
            {
                return Fail(out reason, "游戏输入系统尚未初始化键盘设备。");
            }

            PropertyInfo? fKey = keyboardType.GetProperty("fKey", BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo? f1Key = keyboardType.GetProperty("f1Key", BindingFlags.Public | BindingFlags.Instance);
            if (fKey == null || f1Key == null)
            {
                return Fail(out reason, "当前游戏的输入系统没有暴露 F 或 F1 按键。");
            }

            PropertyInfo? wasPressed = keyboardType.GetProperty(
                                         "wasPressedThisFrame",
                                         BindingFlags.Public | BindingFlags.Instance)
                                     ?? fKey.PropertyType.GetProperty(
                                         "wasPressedThisFrame",
                                         BindingFlags.Public | BindingFlags.Instance);
            if (wasPressed == null)
            {
                return Fail(out reason, "当前游戏的输入系统无法读取按键按下状态。");
            }

            _keyboard = keyboard;
            _fKey = fKey;
            _f1Key = f1Key;
            _wasPressedThisFrame = wasPressed;
            _lastError = string.Empty;
            reason = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            return Fail(out reason, "读取游戏输入系统失败：" + exception.Message);
        }
    }

    public bool WasPressedThisFrame(GameHotkey hotkey)
    {
        if (!IsReady) return false;

        try
        {
            PropertyInfo keyProperty = hotkey == GameHotkey.SkipWave ? _f1Key! : _fKey!;
            object? control = keyProperty.GetValue(_keyboard);
            if (control == null) return false;

            object? value = _wasPressedThisFrame!.GetValue(control);
            return value is bool pressed && pressed;
        }
        catch
        {
            // A game update may invalidate the cached device graph; re-resolve on the next poll.
            _keyboard = null;
            _fKey = null;
            _f1Key = null;
            _wasPressedThisFrame = null;
            _nextResolveUtc = default;
            return false;
        }
    }

    private bool Fail(out string reason, string message)
    {
        _lastError = message;
        reason = message;
        return false;
    }

    private static Type? FindType(string fullName)
    {
        Type? direct = Type.GetType(fullName + ", Unity.InputSystem");
        if (direct != null) return direct;

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? candidate = assembly.GetType(fullName, throwOnError: false);
            if (candidate != null) return candidate;
        }

        return null;
    }
}
