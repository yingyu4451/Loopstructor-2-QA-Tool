using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Loopstructor.AutoPlayer.Tests;

/// <summary>
/// 回归覆盖：1.457 只有 3 个流程大段（章节），工具却把地图分页块（RoomMapSpawner.CurrentStage）
/// 当章节 +1，于是备份名出现「第04章」。章节号必须由流程大段顺序解析，写入状态后由
/// Manager 统一用于备份命名。
/// </summary>
public sealed class FlowChapterContractTests
{
    private const string ControllerType = "Loopstructor.AutoPlayer.Plugin.AutoPlayController";
    private const string ReaderType = "Loopstructor.AutoPlayer.Plugin.FlowChapterReader";
    private const string ResolverType = "Loopstructor.AutoPlayer.Core.FlowSegmentChapterResolver";

    [Fact]
    public void Tick_ResolvesTheFlowChapterAndPublishesItInTheSnapshot()
    {
        using AssemblyDefinition plugin = Read("Loopstructor.AutoPlayer.Plugin.dll");
        TypeDefinition controller = RequireType(plugin, ControllerType);

        MethodDefinition tick = RequireMethod(controller, "Tick");
        Assert.Contains(Calls(tick), IsCall(ReaderType, "TryGetCurrentChapter"));

        MethodDefinition snapshot = RequireMethod(controller, "Snapshot");
        Assert.Contains(
            Calls(snapshot),
            call => call.DeclaringType.FullName == "Loopstructor.AutoPlayer.Core.AutoPlayerStatus"
                    && call.Name == "set_CurrentFlowChapter");
        Assert.Contains(controller.Fields, field => field.Name == "_currentFlowChapter");
    }

    [Fact]
    public void Reader_MapsSegmentsThroughTheFlowSessionAndTheSegmentOrder()
    {
        using AssemblyDefinition plugin = Read("Loopstructor.AutoPlayer.Plugin.dll");
        TypeDefinition reader = RequireType(plugin, ReaderType);

        Assert.Contains(
            Calls(RequireMethod(reader, "TryGetCurrentChapter")),
            IsCall(ResolverType, "ResolveChapter"));

        string[] literals = reader.Methods
            .SelectMany(method => LoadedStrings(method))
            .ToArray();
        Assert.Contains("MetroTD.GameModeSystem.Whitebox.RunModeRuntime", literals);
        Assert.Contains("GetFlowDocument", literals);
        Assert.Contains("m_entry", literals);
        Assert.Contains("flowConfig", literals);
        Assert.Contains("definition", literals);
        Assert.Contains("segments", literals);
        Assert.Contains("segmentId", literals);
    }

    private static AssemblyDefinition Read(string assemblyName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, assemblyName);
        Assert.True(File.Exists(path), "Assembly was not copied to the test output: " + path);
        return AssemblyDefinition.ReadAssembly(path);
    }

    private static TypeDefinition RequireType(AssemblyDefinition assembly, string fullName) =>
        assembly.MainModule.Types.Single(type => type.FullName == fullName);

    private static MethodDefinition RequireMethod(TypeDefinition type, string name) =>
        type.Methods.Single(method => method.Name == name);

    private static IEnumerable<MethodReference> Calls(MethodDefinition method) =>
        method.HasBody
            ? method.Body.Instructions
                .Where(instruction => instruction.OpCode.Code
                    is Code.Call or Code.Callvirt or Code.Newobj or Code.Ldftn or Code.Ldvirtftn)
                .Select(instruction => instruction.Operand)
                .OfType<MethodReference>()
            : Enumerable.Empty<MethodReference>();

    private static IEnumerable<string> LoadedStrings(MethodDefinition method) =>
        method.HasBody
            ? method.Body.Instructions
                .Where(instruction => instruction.OpCode.Code == Code.Ldstr)
                .Select(instruction => instruction.Operand)
                .OfType<string>()
            : Enumerable.Empty<string>();

    private static Predicate<MethodReference> IsCall(string declaringType, string methodName) =>
        call => call.DeclaringType.FullName == declaringType && call.Name == methodName;
}
