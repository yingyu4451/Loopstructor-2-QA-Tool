using Loopstructor.AutoPlayer.Core;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Loopstructor.AutoPlayer.Tests;

/// <summary>
/// 回归覆盖：状态里的「章-关」必须与游戏底部关卡面板显示的编号一致。面板用
/// MultiFunBtn.GetLayerProgress(layer) 算阶段/段内序号（教学局与流程局规则不同），
/// 0.6.102 曾把流程大段序号当章节，结果游戏里显示 2-2、备份名却是 3-14。
/// </summary>
public sealed class HudStageProgressContractTests
{
    private const string ControllerType = "Loopstructor.AutoPlayer.Plugin.AutoPlayController";
    private const string BridgeType = "Loopstructor.AutoPlayer.Plugin.RuntimeBridge";

    [Fact]
    public void ObserveMapProgress_UsesTheGamePanelProgressForChapterAndLevel()
    {
        using AssemblyDefinition plugin = Read("Loopstructor.AutoPlayer.Plugin.dll");
        MethodDefinition observe = RequireMethod(RequireType(plugin, ControllerType), "ObserveMapProgress");

        Assert.Contains(Calls(observe), IsCall(BridgeType, "TryGetStageProgress"));
        Assert.DoesNotContain(Calls(observe), IsCall(BridgeType, "TryGetMapProgress"));
    }

    [Fact]
    public void StageProgress_ReadsThePanelLabelFirstAndFallsBackToTheLayerConversion()
    {
        using AssemblyDefinition plugin = Read("Loopstructor.AutoPlayer.Plugin.dll");
        MethodDefinition progress = RequireMethod(RequireType(plugin, BridgeType), "TryGetStageProgress");

        Assert.Contains(Calls(progress), IsCall(BridgeType, "TryReadHudStageLabel"));
        Assert.Contains(Calls(progress), IsCall(BridgeType, "TryGetMapProgress"));
        Assert.Contains(Calls(progress), IsCall(BridgeType, "TryGetDisplayProgress"));
    }

    [Fact]
    public void HudLabel_ReadsTheRoomTextFromTheMultiFunBtnPanel()
    {
        using AssemblyDefinition plugin = Read("Loopstructor.AutoPlayer.Plugin.dll");
        TypeDefinition bridge = RequireType(plugin, BridgeType);
        MethodDefinition read = RequireMethod(bridge, "TryReadHudStageLabel");
        string[] literals = LoadedStrings(read).ToArray();

        Assert.Contains("MetroTD.UISystem.MultiFunBtn", literals);
        Assert.Contains("m_roomText", literals);
        Assert.Contains("text", literals);

        MethodDefinition typeInitializer = RequireMethod(bridge, ".cctor");
        Assert.Contains(
            LoadedStrings(typeInitializer),
            value => value.Contains(@"(\d+)\s*-\s*(\d+)", StringComparison.Ordinal));
    }

    [Fact]
    public void DisplayProgress_ReusesTheMultiFunBtnLayerConversion()
    {
        using AssemblyDefinition plugin = Read("Loopstructor.AutoPlayer.Plugin.dll");
        MethodDefinition display = RequireMethod(RequireType(plugin, BridgeType), "TryGetDisplayProgress");
        string[] literals = LoadedStrings(display).ToArray();

        Assert.Contains("MetroTD.UISystem.MultiFunBtn", literals);
        Assert.Contains("GetLayerProgress", literals);
        Assert.Contains("MetroTD.RoomSystem.RoomMapSpawner", literals);
        Assert.Contains("StageStep", literals);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    public void Chapter_IsTheDisplayedStage_EvenWhenAFlowSegmentIsKnown(int stage, int chapter)
    {
        AutoPlayerStatus status = new() { CurrentMapStage = stage, CurrentMapLayer = 2, CurrentFlowChapter = 3 };

        Assert.Equal(chapter, status.CurrentChapter);
    }

    [Fact]
    public void Chapter_IsZeroWithoutMapProgress()
    {
        AutoPlayerStatus status = new() { CurrentMapStage = -1, CurrentMapLayer = -1, CurrentFlowChapter = 3 };

        Assert.Equal(0, status.CurrentChapter);
    }

    private static AssemblyDefinition Read(string assemblyName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, assemblyName);
        Assert.True(File.Exists(path), "Assembly was not copied to the test output: " + path);
        return AssemblyDefinition.ReadAssembly(path);
    }

    private static TypeDefinition RequireType(AssemblyDefinition assembly, string fullName)
    {
        TypeDefinition? type = assembly.MainModule.Types.FirstOrDefault(candidate => candidate.FullName == fullName);
        Assert.NotNull(type);
        return type!;
    }

    private static MethodDefinition RequireMethod(TypeDefinition type, string name)
    {
        MethodDefinition? method = type.Methods.FirstOrDefault(candidate => candidate.Name == name);
        Assert.NotNull(method);
        return method!;
    }

    private static IEnumerable<MethodReference> Calls(MethodDefinition method) =>
        method.Body.Instructions
            .Where(instruction => instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
            .Select(instruction => (MethodReference)instruction.Operand);

    private static Predicate<MethodReference> IsCall(string declaringType, string name) =>
        call => call.DeclaringType.FullName == declaringType && call.Name == name;

    private static IEnumerable<string> LoadedStrings(MethodDefinition method) =>
        method.Body.Instructions
            .Where(instruction => instruction.OpCode == OpCodes.Ldstr)
            .Select(instruction => (string)instruction.Operand);
}
