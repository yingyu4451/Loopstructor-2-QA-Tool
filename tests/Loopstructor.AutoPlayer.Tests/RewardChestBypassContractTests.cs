using Loopstructor.AutoPlayer.Core;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Loopstructor.AutoPlayer.Tests;

/// <summary>
/// 战斗页的“不领奖励，开放选关”只临时解除奖励箱对选关的门禁：
/// 不领取、不清空奖励箱，也不改变奖励账本。这里锁定命令、补丁目标和 Editor 分发。
/// </summary>
public sealed class RewardChestBypassContractTests
{
    private const string BridgeType = "Loopstructor.AutoPlayer.Plugin.CheatRuntimeBridge";
    private const string ControllerType = "Loopstructor.AutoPlayer.Plugin.CheatController";
    private const string PatchType = "Loopstructor.AutoPlayer.Plugin.RewardChestBypassPatch";
    private const string Command = "cheat.skipRewardChests";

    [Fact]
    public void Command_IsRegisteredAsAnAuditedMutation()
    {
        Assert.Contains(Command, CheatCommands.All);
        Assert.True(CheatCommands.IsMutationCommand(Command));
        Assert.False(CheatCommands.IsAutoPlayObservationCommand(Command));
    }

    [Fact]
    public void Controller_DispatchesTheBypassMutation()
    {
        using AssemblyDefinition assembly = ReadPlugin();
        MethodDefinition executeCore = RequireMethod(RequireType(assembly, ControllerType), "ExecuteCore");

        Assert.Contains("cheat.skiprewardchests", LoadedStrings(executeCore));
        Assert.Contains(Calls(executeCore), IsCall(BridgeType, "SkipRewardChests"));
    }

    [Fact]
    public void Bridge_InstallsTheGatePatchBeforeTheGateCanBeInlined()
    {
        using AssemblyDefinition assembly = ReadPlugin();
        MethodDefinition initialize = RequireMethod(RequireType(assembly, BridgeType), "Initialize");

        Assert.Contains(
            Calls(initialize),
            call => call.DeclaringType.FullName == PatchType && call.Name == "TryInstall");
    }

    [Fact]
    public void Bridge_ActivatesTheTemporaryGateBypass()
    {
        using AssemblyDefinition assembly = ReadPlugin();
        MethodDefinition skip = RequireMethod(RequireType(assembly, BridgeType), "SkipRewardChests");

        Assert.Contains(
            Calls(skip),
            call => call.DeclaringType.FullName == PatchType && call.Name == "TryActivate");
        Assert.Contains("REWARD_CHEST_BYPASS_UNAVAILABLE", LoadedStrings(skip));
    }

    [Fact]
    public void Patch_TargetsTheRewardChoiceGateAndDoesNotClaimRewards()
    {
        using AssemblyDefinition assembly = ReadPlugin();
        TypeDefinition patch = RequireType(assembly, PatchType);
        string[] strings = patch.Methods
            .SelectMany(LoadedStrings)
            .ToArray();

        Assert.Contains("MetroTD.UISystem.RoomMapChoosePanel", strings);
        Assert.Contains("MetroTD.UISystem.MultiFunBtn", strings);
        Assert.Contains("SetRewardChoiceBlocked", strings);
        Assert.Contains(patch.Methods, method => method.Name == "ForceOpen");

        string[] allStrings = patch.Methods.SelectMany(LoadedStrings).ToArray();
        Assert.DoesNotContain("ClaimAllLevelRewards", allStrings);
        Assert.DoesNotContain("SkipLevelDisposableReward", allStrings);
        Assert.DoesNotContain("ClearRewardChestProjection", allStrings);
    }

    [Fact]
    public void EditorBridge_DispatchesTheBypassMutation()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            root, "src", "Loopstructor.AutoPlayer.EditorBridge.Runtime", "UnityEditorCheatBridge.cs"));
        Assert.Contains("CheatCommands.SkipRewardChests => m_bridge.SkipRewardChests()", source);
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
            .Where(instruction => instruction.OpCode.Code is Code.Call or Code.Callvirt)
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
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
