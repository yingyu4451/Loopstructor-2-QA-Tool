using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Loopstructor.AutoPlayer.Tests;

/// <summary>
/// 回归覆盖：安装 QA 工具后游戏 1% low 帧骤降。
/// 玩家模式（ResidentPlayer）不安装存档隔离补丁，但 AutoPlayController.Tick 仍每秒调用
/// SaveIsolationPatch.TryResolveRuntimeSaveFolder；该方法每次都重新执行 AccessTools.TypeByName，
/// 在真实玩家构建里单次实测 75–108ms（全类型扫描，0 帧渲染），形成每秒一次的主线程尖峰，
/// 10 秒窗口最慢 1% 帧被拉低到约 16fps。
/// 约定：未安装补丁直接返回；SaveManager 契约只解析一次并缓存（失败也缓存）；Tick 中玩家模式跳过存档探测。
/// </summary>
public sealed class SaveIsolationPatchContractTests
{
    private const string PatchType = "Loopstructor.AutoPlayer.Plugin.SaveIsolationPatch";
    private const string ControllerType = "Loopstructor.AutoPlayer.Plugin.AutoPlayController";
    private const string ActivationType = "Loopstructor.AutoPlayer.Plugin.ActivationContext";
    private const string AccessToolsType = "HarmonyLib.AccessTools";

    [Fact]
    public void TryResolveRuntimeSaveFolder_ReturnsImmediatelyWhenThePatchIsNotInstalled()
    {
        using AssemblyDefinition plugin = Read("Loopstructor.AutoPlayer.Plugin.dll");
        MethodDefinition resolve = RequireMethod(RequireType(plugin, PatchType), "TryResolveRuntimeSaveFolder");
        IReadOnlyList<Instruction> body = Body(resolve);

        int installedCheck = FindCall(body, PatchType, "get_Installed");
        Assert.True(installedCheck >= 0, "未安装补丁时必须先检查 Installed，玩家模式应零反射返回。");

        int firstReflectionInvoke = FindCall(body, "System.Reflection.MethodBase", "Invoke");
        Assert.True(
            firstReflectionInvoke < 0 || installedCheck < firstReflectionInvoke,
            "Installed 守卫必须位于任何反射 Invoke 之前。");
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
    public void AutoPlayController_Tick_SkipsTheSaveProbeInPlayerMode()
    {
        using AssemblyDefinition plugin = Read("Loopstructor.AutoPlayer.Plugin.dll");
        MethodDefinition tick = RequireMethod(RequireType(plugin, ControllerType), "Tick");
        IReadOnlyList<Instruction> body = Body(tick);

        int playerModeCheck = FindCall(body, ActivationType, "get_IsPlayerMode");
        Assert.True(playerModeCheck >= 0, "Tick 必须显式判断玩家模式。");

        int resolveCall = FindCall(body, PatchType, "TryResolveRuntimeSaveFolder");
        Assert.True(resolveCall >= 0, "非玩家模式仍需要解析隔离存档路径。");
        Assert.True(playerModeCheck < resolveCall, "存档探测必须放在玩家模式判定之后。");

        int probeCall = FindCall(body, PatchType, "ProbeRuntimeSaveFolder");
        Assert.True(probeCall < 0 || playerModeCheck < probeCall, "存档目录探测必须放在玩家模式判定之后。");
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

