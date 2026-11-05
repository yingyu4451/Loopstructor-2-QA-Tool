using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Loopstructor.AutoPlayer.Tests;

/// <summary>
/// 回归覆盖：安装 QA 工具后游戏 1% low 帧骤降。
/// 玩家模式（ResidentPlayer）不安装存档隔离补丁，但 AutoPlayController.Tick 仍需要解析真实存档
/// 目录供自动备份使用；历史缺陷是该方法每次都重新执行 AccessTools.TypeByName，在真实玩家构建里
/// 单次实测 75–108ms（全类型扫描，0 帧渲染），形成每秒一次的主线程尖峰，10 秒窗口最慢 1% 帧
/// 被拉低到约 16fps。0.6.95 用「未安装就返回」修掉尖峰，却让玩家模式永远拿不到存档目录、
/// 自动备份从 0.6.95 起停止；0.6.97 改为 SaveManager 契约只解析一次并缓存（失败也缓存）。
/// 约定：路径解析在玩家模式也必须生效；隔离探测（ProbeRuntimeSaveFolder）仍只在 QA 模式运行。
/// </summary>
public sealed class SaveIsolationPatchContractTests
{
    private const string PatchType = "Loopstructor.AutoPlayer.Plugin.SaveIsolationPatch";
    private const string ControllerType = "Loopstructor.AutoPlayer.Plugin.AutoPlayController";
    private const string ActivationType = "Loopstructor.AutoPlayer.Plugin.ActivationContext";
    private const string AccessToolsType = "HarmonyLib.AccessTools";

    [Fact]
    public void TryResolveRuntimeSaveFolder_ResolvesThePathEvenWhenTheIsolationPatchIsNotInstalled()
    {
        using AssemblyDefinition plugin = Read("Loopstructor.AutoPlayer.Plugin.dll");
        MethodDefinition resolve = RequireMethod(RequireType(plugin, PatchType), "TryResolveRuntimeSaveFolder");
        MethodReference[] calls = Calls(resolve).ToArray();

        Assert.DoesNotContain(calls, call => call.DeclaringType.FullName == AccessToolsType);
        Assert.DoesNotContain(calls, IsCall(PatchType, "get_Installed"));
        Assert.Contains(calls, IsCall(PatchType, "TryEnsureSaveManagerHandles"));
    }

    [Fact]
    public void TryResolveRuntimeSaveFolder_DoesNotRescanAssemblies()
    {
        using AssemblyDefinition plugin = Read("Loopstructor.AutoPlayer.Plugin.dll");
        MethodDefinition resolve = RequireMethod(RequireType(plugin, PatchType), "TryResolveRuntimeSaveFolder");
        MethodReference[] calls = Calls(resolve).ToArray();

        Assert.DoesNotContain(calls, call => call.DeclaringType.FullName == AccessToolsType);
        Assert.Contains(calls, IsCall(PatchType, "TryEnsureSaveManagerHandles"));
    }

    [Fact]
    public void SaveManagerContract_IsResolvedOnceAndCachedForLaterCalls()
    {
        using AssemblyDefinition plugin = Read("Loopstructor.AutoPlayer.Plugin.dll");
        TypeDefinition patch = RequireType(plugin, PatchType);
        MethodDefinition ensure = RequireMethod(patch, "TryEnsureSaveManagerHandles");

        Assert.Contains("ActFramework_ByHZR.Save.SaveManager", LoadedStrings(ensure));
        Assert.Contains(Calls(ensure), IsCall(AccessToolsType, "TypeByName"));
        Assert.Contains(Calls(ensure), IsCall(AccessToolsType, "PropertyGetter"));
        Assert.Contains(Calls(ensure), IsCall(AccessToolsType, "Method"));

        Assert.Contains(patch.Fields, field => field.Name == "s_saveManagerGetter");
        Assert.Contains(patch.Fields, field => field.Name == "s_saveManagerGetPath");
        Assert.Contains(patch.Fields, field => field.Name == "s_saveManagerResolutionFailed");

        MethodDefinition probe = RequireMethod(patch, "ProbeRuntimeSaveFolder");
        Assert.Contains(Calls(probe), IsCall(PatchType, "TryEnsureSaveManagerHandles"));
        Assert.DoesNotContain(Calls(probe), call => call.DeclaringType.FullName == AccessToolsType);
    }

    [Fact]
    public void AutoPlayController_Tick_KeepsResolvingTheSaveRootInPlayerMode()
    {
        using AssemblyDefinition plugin = Read("Loopstructor.AutoPlayer.Plugin.dll");
        MethodDefinition tick = RequireMethod(RequireType(plugin, ControllerType), "Tick");
        Instruction[] instructions = Body(tick).ToArray();

        int playerModeCheck = FindCall(instructions, ActivationType, "get_IsPlayerMode");
        Assert.True(playerModeCheck >= 0, "Tick 必须显式判断玩家模式（隔离探测只在 QA 模式运行）。");

        int probeCall = FindCall(instructions, PatchType, "ProbeRuntimeSaveFolder");
        Assert.True(probeCall >= 0, "QA 模式仍需要隔离探测。");
        Assert.True(playerModeCheck < probeCall, "隔离探测必须放在玩家模式判定之后。");

        int resolveCall = FindCall(instructions, PatchType, "TryResolveRuntimeSaveFolder");
        Assert.True(resolveCall >= 0, "玩家模式也需要解析真实存档目录供自动备份使用。");
        Assert.True(probeCall < resolveCall, "存档路径解析必须在隔离探测之后无条件执行。");

        // 玩家模式判定产生的第一条分支必须跳到「路径解析或更早」的位置，
        // 说明被跳过的是隔离探测，而不是存档路径解析。
        int branchIndex = -1;
        for (int index = playerModeCheck + 1; index < instructions.Length; index++)
        {
            if (instructions[index].OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch)
            {
                branchIndex = index;
                break;
            }
        }

        Assert.True(branchIndex > 0, "玩家模式判定后应当存在跳过隔离探测的分支。");
        Assert.True(
            instructions[branchIndex].Operand is Instruction,
            "玩家模式分支必须携带跳转目标。");
        Instruction branchTarget = (Instruction)instructions[branchIndex].Operand!;
        int targetIndex = Array.IndexOf(instructions, branchTarget);
        Assert.True(
            targetIndex >= 0 && targetIndex <= resolveCall,
            "玩家模式分支不得跳过存档路径解析（自动备份依赖该值）。");

        int assignIndex = instructions
            .Select((instruction, index) => (instruction, index))
            .Where(pair => pair.instruction.OpCode.Code == Code.Stfld
                && pair.instruction.Operand is FieldReference field
                && field.Name == "_activeSaveRoot")
            .Select(pair => pair.index)
            .FirstOrDefault(-1);
        Assert.True(assignIndex > resolveCall, "解析成功后必须把真实存档目录写回 _activeSaveRoot。");
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

    private static IReadOnlyList<Instruction> Body(MethodDefinition method) =>
        method.HasBody ? method.Body.Instructions.ToArray() : Array.Empty<Instruction>();

    private static IEnumerable<string> LoadedStrings(MethodDefinition method) =>
        method.Body.Instructions
            .Where(instruction => instruction.OpCode.Code == Code.Ldstr)
            .Select(instruction => instruction.Operand)
            .OfType<string>();

    private static Predicate<MethodReference> IsCall(string declaringType, string methodName) =>
        call => call.DeclaringType.FullName == declaringType && call.Name == methodName;

    private static int FindCall(IReadOnlyList<Instruction> instructions, string declaringType, string methodName)
    {
        for (int index = 0; index < instructions.Count; index++)
        {
            if (instructions[index].Operand is MethodReference call
                && call.DeclaringType.FullName == declaringType
                && call.Name == methodName)
            {
                return index;
            }
        }

        return -1;
    }
}

