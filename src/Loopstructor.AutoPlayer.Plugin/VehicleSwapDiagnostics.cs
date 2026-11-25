using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace Loopstructor.AutoPlayer.Plugin;

/// <summary>
/// 只读诊断：灰市升级在组件替换内部用 <c>catch {}</c> 吞掉异常，导致「灰市升级失败」无法定位。
/// 这里给组件卸载 / 重装与替换入口挂只记录、不改变行为的补丁，异常与车辆羁绊数据写入
/// <c>%LOCALAPPDATA%\LoopstructorAutoPlayer\logs\vehicle-swap-diagnostic.log</c>。
/// </summary>
internal static class VehicleSwapDiagnostics
{
    private const string HarmonyId = "com.ponegames.loopstructor2.vehicle-swap-diagnostic";
    private const long MaxLogBytes = 1024 * 1024;

    private static readonly object Sync = new();
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LoopstructorAutoPlayer",
        "logs");

    private static bool s_installed;
    private static string s_error = string.Empty;

    internal static string LogPath => Path.Combine(DirectoryPath, "vehicle-swap-diagnostic.log");

    internal static void EnsureInstalled()
    {
        if (s_installed) return;
        lock (Sync)
        {
            if (s_installed) return;
            try
            {
                Type? component = FindType("MetroTD.VehicleSystem.BasicVehicleComponent");
                Type? controller = FindType("MetroTD.VehicleSystem.VehicleController");
                Type? manager = FindType("MetroTD.VehicleSystem.VehicleManager");
                if (component == null || controller == null || manager == null)
                {
                    s_error = "缺少战车组件类型";
                    return;
                }

                MethodInfo? onInit = component.GetMethod("OnInit", BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo? remove = controller.GetMethod("RemoveBasicRazorComponent", BindingFlags.Public | BindingFlags.Instance);
                MethodInfo? swap = manager
                    .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(method => method.Name == "TryReplaceVehicleComponentWithSnapshot" && method.GetParameters().Length == 5);
                if (onInit == null || remove == null || swap == null)
                {
                    s_error = "缺少组件卸载/重装入口";
                    return;
                }

                Harmony harmony = new(HarmonyId);
                harmony.Patch(onInit, finalizer: new HarmonyMethod(typeof(VehicleSwapDiagnostics), nameof(LogComponentInitException)));
                harmony.Patch(remove, finalizer: new HarmonyMethod(typeof(VehicleSwapDiagnostics), nameof(LogComponentRemoveException)));
                harmony.Patch(
                    swap,
                    prefix: new HarmonyMethod(typeof(VehicleSwapDiagnostics), nameof(LogSwapEnter)),
                    postfix: new HarmonyMethod(typeof(VehicleSwapDiagnostics), nameof(LogSwapExit)));
                s_installed = true;
            }
            catch (Exception exception)
            {
                s_error = exception.Message;
            }
        }
    }

    internal static string? LastError => string.IsNullOrEmpty(s_error) ? null : s_error;

    private static Exception? LogComponentInitException(Exception? __exception, object __instance)
    {
        if (__exception != null) Write("BasicVehicleComponent.OnInit", __instance, __exception);
        return __exception;
    }

    private static Exception? LogComponentRemoveException(Exception? __exception, object __instance)
    {
        if (__exception != null) Write("VehicleController.RemoveBasicRazorComponent", __instance, __exception);
        return __exception;
    }

    private static void LogSwapEnter(object? vehicle, IEnumerable? originFetterModuleDatas, IEnumerable? finalFetterModuleDatas)
    {
        try
        {
            Write($"尝试替换战车组件 vehicle={DescribeVehicle(vehicle)} 原羁绊=[{DescribeFetters(originFetterModuleDatas)}] 目标羁绊=[{DescribeFetters(finalFetterModuleDatas)}]");
        }
        catch
        {
            // 日志失败不影响升级流程。
        }
    }

    private static void LogSwapExit(bool __result, object? vehicle)
    {
        if (__result) return;
        try
        {
            Write($"替换战车组件失败 vehicle={DescribeVehicle(vehicle)} 自定义羁绊=[{DescribeFetters(ReadManagerModules())}]");
        }
        catch
        {
            // 日志失败不影响升级流程。
        }
    }

    private static IEnumerable? ReadManagerModules()
    {
        try
        {
            Type? managerType = FindType("MetroTD.VehicleSystem.VehicleManager");
            if (managerType == null) return null;
            object? manager = managerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null)
                              ?? managerType.GetField("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null);
            return manager == null ? null : GetMember(manager, "CustomModuleData") as IEnumerable;
        }
        catch
        {
            return null;
        }
    }

    private static string DescribeVehicle(object? instance)
    {
        if (instance == null) return "<null>";
        try
        {
            Type type = instance.GetType();
            object? owner = type
                .GetField("vehicleController", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?
                .GetValue(instance)
                ?? (type.Name == "VehicleController" ? instance : null);
            object? vehicleType = owner == null ? null : GetMember(owner, "vehicleType");
            object? level = owner == null ? null : GetMember(owner, "level");
            object? id = owner == null ? null : GetMember(owner, "ID");
            return $"{vehicleType ?? type.Name} L{level ?? "?"} ID={id ?? "?"}";
        }
        catch
        {
            return instance.GetType().Name;
        }
    }

    private static string DescribeFetters(IEnumerable? modules)
    {
        if (modules == null) return string.Empty;
        try
        {
            string[] parts = modules
                .Cast<object?>()
                .Where(module => module != null)
                .Select(module =>
                    $"{GetMember(module!, "fetterEnum") ?? "?"}x{GetMember(module!, "count") ?? 1}(L{GetMember(module!, "level") ?? 1})")
                .ToArray();
            return string.Join(", ", parts);
        }
        catch
        {
            return "<读取失败>";
        }
    }

    private static object? GetMember(object target, string name)
    {
        Type type = target.GetType();
        PropertyInfo? property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property != null) return property.GetValue(target);
        for (Type? cursor = type; cursor != null; cursor = cursor.BaseType)
        {
            FieldInfo? field = cursor.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (field != null) return field.GetValue(target);
        }

        return null;
    }

    private static void Write(string message, object? instance = null, Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            FileInfo info = new(LogPath);
            if (info.Exists && info.Length > MaxLogBytes) File.WriteAllText(LogPath, string.Empty, new UTF8Encoding(false));
            StringBuilder builder = new();
            builder.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] ").AppendLine(message);
            if (instance != null) builder.AppendLine("vehicle=" + DescribeVehicle(instance));
            if (exception != null) builder.AppendLine(exception.ToString());
            builder.AppendLine();
            File.AppendAllText(LogPath, builder.ToString(), new UTF8Encoding(false));
        }
        catch
        {
            // 诊断日志写失败时静默，不能影响游戏流程。
        }
    }

    private static Type? FindType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? exact = assembly.GetType(fullName, false);
            if (exact != null) return exact;
        }

        return null;
    }
}
