using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Loopstructor.AutoPlayer.Core;

namespace Loopstructor.AutoPlayer.Plugin;

/// <summary>
/// 从白模流程会话读取当前大段（segment）在流程配置里的序号。
/// 反射链：RunModeRuntime.Instance → Session → GetFlowDocument().context.segmentId
/// 以及 Session.m_entry.flowConfig.definition.segments[].id。
/// 全部成员解析一次后缓存；任何一步缺失都返回 false，由调用方回退旧的地图阶段编号。
/// </summary>
internal static class FlowChapterReader
{
    private const string RuntimeTypeName = "MetroTD.GameModeSystem.Whitebox.RunModeRuntime";
    private const BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static bool s_resolutionFailed;
    private static MethodInfo? s_instanceGetter;
    private static PropertyInfo? s_sessionProperty;
    private static MethodInfo? s_getDocument;
    private static FieldInfo? s_entryField;
    private static FieldInfo? s_flowConfigField;
    private static FieldInfo? s_definitionField;
    private static FieldInfo? s_segmentsField;
    private static FieldInfo? s_documentContextField;
    private static FieldInfo? s_segmentIdField;
    private static FieldInfo? s_idField;
    private static object? s_segmentIdsOwner;
    private static IReadOnlyList<string>? s_segmentIds;

    public static bool TryGetCurrentChapter(out int chapter)
    {
        chapter = 0;
        if (!TryEnsureHandles()) return false;
        try
        {
            object? runtime = s_instanceGetter!.Invoke(null, null);
            if (runtime == null) return false;
            object? session = s_sessionProperty!.GetValue(runtime, null);
            if (session == null) return false;
            object? document = s_getDocument!.Invoke(session, null);
            object? context = document == null ? null : s_documentContextField!.GetValue(document);
            string? segmentId = context == null ? null : s_segmentIdField!.GetValue(context) as string;
            if (string.IsNullOrEmpty(segmentId)) return false;

            int resolved = FlowSegmentChapterResolver.ResolveChapter(ReadSegmentIds(session), segmentId);
            if (resolved <= 0) return false;
            chapter = resolved;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static IReadOnlyList<string>? ReadSegmentIds(object session)    {
        object? entry = s_entryField!.GetValue(session);
        object? flowConfig = entry == null ? null : s_flowConfigField!.GetValue(entry);
        object? definition = flowConfig == null ? null : s_definitionField!.GetValue(flowConfig);
        object? segments = definition == null ? null : s_segmentsField!.GetValue(definition);
        if (segments is not IEnumerable list) return null;
        if (ReferenceEquals(segments, s_segmentIdsOwner) && s_segmentIds != null) return s_segmentIds;

        List<string> ids = new();
        foreach (object? segment in list)
        {
            if (segment == null) continue;
            if (s_idField!.GetValue(segment) is string id && !string.IsNullOrEmpty(id)) ids.Add(id);
        }

        s_segmentIdsOwner = segments;
        s_segmentIds = ids;
        return ids;
    }

    /// <summary>
    /// Instance 是基类 IOdinMonoBehaviourSingleton&lt;T&gt; 上的静态属性（运行时类型自己不声明），
    /// 必须带 FlattenHierarchy 才能取到；取不到时再退回 AccessTools。
    /// </summary>
    private static MethodInfo? ResolveSingletonGetter(Type runtimeType)
    {
        PropertyInfo? property = runtimeType.GetProperty(
            "Instance",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        MethodInfo? getter = property?.GetGetMethod(true);
        return getter ?? AccessTools.PropertyGetter(runtimeType, "Instance");
    }

    private static bool TryEnsureHandles()
    {
        if (s_resolutionFailed) return false;
        if (s_instanceGetter != null && s_sessionProperty != null && s_getDocument != null && s_entryField != null
            && s_flowConfigField != null && s_definitionField != null && s_segmentsField != null
            && s_documentContextField != null && s_segmentIdField != null && s_idField != null)
        {
            return true;
        }

        try
        {
            Type? runtimeType = AccessTools.TypeByName(RuntimeTypeName);
            if (runtimeType == null)
            {
                s_resolutionFailed = true;
                return false;
            }

            s_instanceGetter = ResolveSingletonGetter(runtimeType);
            s_sessionProperty = runtimeType.GetProperty("Session", InstanceFlags);
            if (s_instanceGetter == null || s_sessionProperty == null)
            {
                s_resolutionFailed = true;
                return false;
            }

            Type sessionType = s_sessionProperty.PropertyType;
            s_getDocument = sessionType.GetMethod("GetFlowDocument", InstanceFlags, null, Type.EmptyTypes, null);
            s_entryField = sessionType.GetField("m_entry", InstanceFlags);
            if (s_getDocument == null || s_entryField == null)
            {
                s_resolutionFailed = true;
                return false;
            }

            s_documentContextField = s_getDocument.ReturnType.GetField("context", InstanceFlags);
            if (s_documentContextField == null)
            {
                s_resolutionFailed = true;
                return false;
            }

            s_segmentIdField = s_documentContextField.FieldType.GetField("segmentId", InstanceFlags);
            s_flowConfigField = s_entryField.FieldType.GetField("flowConfig", InstanceFlags);
            if (s_segmentIdField == null || s_flowConfigField == null)
            {
                s_resolutionFailed = true;
                return false;
            }

            s_definitionField = s_flowConfigField.FieldType.GetField("definition", InstanceFlags);
            if (s_definitionField == null)
            {
                s_resolutionFailed = true;
                return false;
            }

            s_segmentsField = s_definitionField.FieldType.GetField("segments", InstanceFlags);
            if (s_segmentsField == null)
            {
                s_resolutionFailed = true;
                return false;
            }

            Type[] arguments = s_segmentsField.FieldType.GetGenericArguments();
            if (arguments.Length != 1)
            {
                s_resolutionFailed = true;
                return false;
            }

            s_idField = arguments[0].GetField("id", InstanceFlags);
            if (s_idField == null)
            {
                s_resolutionFailed = true;
                return false;
            }

            return true;
        }
        catch (Exception)
        {
            s_resolutionFailed = true;
            return false;
        }
    }
}
