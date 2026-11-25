using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Loopstructor.AutoPlayer.Tests;

/// <summary>
/// 新版白盒事件触发器：目录查询与强制开组都通过反射调用游戏运行时，
/// 这里固化命令注册、双端分发与关键门禁，防止后续改版悄悄丢掉某条路径。
/// </summary>
public sealed class RunEventTriggerContractTests
{
    private const string BridgeType = "Loopstructor.AutoPlayer.Plugin.CheatRuntimeBridge";
    private const string ControllerType = "Loopstructor.AutoPlayer.Plugin.CheatController";

    [Fact]
    public void Controller_DispatchesRunEventQueryAndTrigger()
    {
        using AssemblyDefinition assembly = ReadPlugin();
        MethodDefinition executeCore = RequireMethod(RequireType(assembly, ControllerType), "ExecuteCore");

        Assert.Contains("cheat.queryrunevents", LoadedStrings(executeCore));
        Assert.Contains("cheat.triggerrunevent", LoadedStrings(executeCore));
        Assert.Contains(Calls(executeCore), IsCall(BridgeType, "QueryRunEvents"));
        Assert.Contains(Calls(executeCore), IsCall(BridgeType, "TriggerRunEvent"));
    }

    [Fact]
    public void RuntimeQuery_BuildsCatalogFromRunConfigAndLoadedLibraries()
    {
        using AssemblyDefinition assembly = ReadPlugin();
        TypeDefinition bridge = RequireType(assembly, BridgeType);
        MethodDefinition query = RequireMethod(bridge, "QueryRunEvents");
        MethodDefinition collect = RequireMethod(bridge, "CollectRunEventLibraries");
        string[] queryStrings = LoadedStrings(query).ToArray();
        string[] collectStrings = LoadedStrings(collect).ToArray();

        Assert.Contains("inRun", queryStrings);
        Assert.Contains("runEventActive", queryStrings);
        Assert.Contains("panelOpen", queryStrings);
        Assert.Contains("libraries", queryStrings);
        Assert.Contains("MetroTD.GameModeSystem.Whitebox.RunModeRuntime", queryStrings);
        Assert.Contains("MetroTD.GameModeSystem.SO_RunEventLibraryCfg", queryStrings);

        Assert.Contains("normalStage", collectStrings);
        Assert.Contains("tutorialStage", collectStrings);
        Assert.Contains("loopStage", collectStrings);
        Assert.Contains(Calls(collect), call => call.Name == "FindObjectsOfTypeAll");
    }

    [Fact]
    public void RuntimeTrigger_ValidatesNodeAndGroupBeforeStartingFlow()
    {
        using AssemblyDefinition assembly = ReadPlugin();
        TypeDefinition bridge = RequireType(assembly, BridgeType);
        MethodDefinition trigger = RequireMethod(bridge, "TriggerRunEvent");
        string[] strings = LoadedStrings(trigger).ToArray();

        Assert.Contains("RUN_NOT_ACTIVE", strings);
        Assert.Contains("RUN_EVENT_ACTIVE", strings);
        Assert.Contains("EVENT_NODE_NOT_SELECTED", strings);
        Assert.Contains("direct", strings);
        Assert.Contains(Calls(trigger), call => call.Name == "StartForcedRunEvent");
        Assert.Contains("EVENT_NODE_NOT_READY", strings);
        Assert.Contains("WAVE_IN_PROGRESS", strings);
        Assert.Contains("EVENT_GROUP_LIMIT_REACHED", strings);
        Assert.Contains("EVENT_LIBRARY_NOT_FOUND", strings);
        Assert.Contains("EVENT_GROUP_NOT_FOUND", strings);
        Assert.Contains("EVENT_PANEL_BUSY", strings);
        Assert.Contains("EVENT_PANEL_NOT_READY", strings);
        Assert.Contains("EVENT_CAMERA_MISSING", strings);
        Assert.Contains("EVENT_INLINE_CHOICE_PENDING", strings);
        Assert.Contains("runEventSnapshot", strings);
        Assert.Contains("libraryId", strings);
        Assert.Contains("groupId", strings);
        Assert.Contains("Begin", strings);
        Assert.Contains("ValidateGraph", strings);
        Assert.Contains("CanEnterEventGroup", strings);
        Assert.Contains("EventFlow", strings);
        Assert.Contains(Calls(trigger), call => call.Name == "AddComponent");
    }

    [Fact]
    public void Runtime_ResolvesRunEventTypesLazilySoOlderBuildsKeepOtherCheats()
    {
        using AssemblyDefinition assembly = ReadPlugin();
        TypeDefinition bridge = RequireType(assembly, BridgeType);
        MethodDefinition initialize = RequireMethod(bridge, "Initialize");
        MethodDefinition contract = RequireMethod(bridge, "ValidateRuntimeContract");

        Assert.DoesNotContain("RunEventFlowController", LoadedStrings(initialize));
        Assert.DoesNotContain("RunEventFlowController", LoadedStrings(contract));
        Assert.DoesNotContain("SO_RunEventLibraryCfg", LoadedStrings(contract));
    }

    [Fact]
    public void Runtime_DirectTrigger_UsesASandboxFlowWithCompletionSuppression()
    {
        using AssemblyDefinition assembly = ReadPlugin();
        TypeDefinition bridge = RequireType(assembly, BridgeType);
        MethodDefinition start = RequireMethod(bridge, "StartForcedRunEvent");
        string[] strings = LoadedStrings(start).ToArray();

        Assert.Contains("EVENT_DIRECT_PATCH_FAILED", strings);
        Assert.Contains("EVENT_DIRECT_BATTLE_UNSUPPORTED", strings);
        Assert.Contains("SwitchGroup", strings);
        // LevelNodeEnum 在游戏程序集里没有命名空间，必须从 LevelNodeData 字段类型解析，不能按名字硬找。
        Assert.Contains("levelNodeEnum", strings);
        Assert.DoesNotContain("MetroTD.RoomSystem.LevelNodeEnum", strings);
        Assert.Contains("m_uiLock", strings);
        Assert.DoesNotContain("MetroTD.Tutorial.DefaultUIInteraction", strings);
        Assert.Contains(Calls(start), call => call.DeclaringType.Name == "ForcedRunEventPatch");
        Assert.Contains(Calls(start), call => call.Name == "SetFieldDeep");

        TypeDefinition patch = RequireType(assembly, "Loopstructor.AutoPlayer.Plugin.ForcedRunEventPatch");
        string[] patchStrings = patch.Methods
            .Where(method => method.HasBody)
            .SelectMany(LoadedStrings)
            .ToArray();
        Assert.Contains("RestoreRunEventState", patchStrings);
        Assert.Contains("CompleteRunEventWithRewards", patchStrings);
        Assert.Contains("CompleteRunEventLayer", patchStrings);
        Assert.Contains("PrepareBattle", patchStrings);
    }

    [Fact]
    public void RuntimeQuery_ExposesWhetherDirectTriggeringIsAvailable()
    {
        using AssemblyDefinition assembly = ReadPlugin();
        TypeDefinition bridge = RequireType(assembly, BridgeType);
        MethodDefinition query = RequireMethod(bridge, "QueryRunEvents");

        Assert.Contains("direct", LoadedStrings(query));
        Assert.Contains(Calls(query), call => call.DeclaringType.Name == "ForcedRunEventPatch" && call.Name == "TryInstall");
    }

    [Fact]
    public void EditorBridge_ExposesTheSameRunEventCommands()
    {
        string editorBridge = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Loopstructor.AutoPlayer.EditorBridge.Runtime",
            "UnityEditorCheatBridge.cs"));

        Assert.Contains(
            "CheatCommands.TriggerRunEvent => m_bridge.TriggerRunEvent(arguments)",
            editorBridge,
            StringComparison.Ordinal);
        Assert.Contains(
            "CheatCommands.QueryRunEvents => m_bridge.QueryRunEvents()",
            editorBridge,
            StringComparison.Ordinal);
    }

    private static AssemblyDefinition ReadPlugin()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Loopstructor.AutoPlayer.Plugin.dll");
        Assert.True(File.Exists(path), "Plugin assembly was not copied to the test output: " + path);
        return AssemblyDefinition.ReadAssembly(path);
    }

    private static TypeDefinition RequireType(AssemblyDefinition assembly, string fullName) =>
        assembly.MainModule.Types.Single(type => type.FullName == fullName);

    private static MethodDefinition RequireMethod(TypeDefinition type, string name) =>
        type.Methods.Single(method => method.Name == name);

    private static IEnumerable<MethodReference> Calls(MethodDefinition method) =>
        method.Body.Instructions
            .Where(instruction => instruction.OpCode.Code
                is Code.Call or Code.Callvirt or Code.Newobj or Code.Ldftn or Code.Ldvirtftn)
            .Select(instruction => instruction.Operand)
            .OfType<MethodReference>();

    private static IEnumerable<string> LoadedStrings(MethodDefinition method) =>
        method.Body.Instructions
            .Where(instruction => instruction.OpCode.Code == Code.Ldstr)
            .Select(instruction => instruction.Operand)
            .OfType<string>();

    private static Predicate<MethodReference> IsCall(string declaringType, string methodName) =>
        call => call.DeclaringType.FullName == declaringType && call.Name == methodName;

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Loopstructor.AutoPlayer.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("未找到仓库根目录（Loopstructor.AutoPlayer.sln）。");
    }
}
