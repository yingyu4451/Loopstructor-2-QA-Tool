using Mono.Cecil;
using Mono.Cecil.Cil;
using Newtonsoft.Json.Linq;

namespace Loopstructor.AutoPlayer.Tests;

public sealed class SkipRewardPopupRuntimeContractTests
{
    private const string BridgeType = "Loopstructor.AutoPlayer.Plugin.CheatRuntimeBridge";
    private const string ControllerType = "Loopstructor.AutoPlayer.Plugin.CheatController";

    [Fact]
    public void Controller_DispatchesAuditedRewardSkipMutation()
    {
        using AssemblyDefinition assembly = ReadPlugin();
        MethodDefinition executeCore = RequireMethod(RequireType(assembly, ControllerType), "ExecuteCore");

        Assert.Contains("cheat.skiprewardpopup", LoadedStrings(executeCore));
        Assert.Contains(Calls(executeCore), IsCall(BridgeType, "SkipRewardPopup"));
    }

    [Fact]
    public void RuntimeSkip_DelegatesToVerifiedNativeCommand_AfterGameRemovedTheLegacyQueue()
    {
        using AssemblyDefinition assembly = ReadPlugin();
        TypeDefinition bridge = RequireType(assembly, BridgeType);
        MethodDefinition skip = RequireMethod(bridge, "SkipRewardPopup");
        string[] strings = LoadedStrings(skip).ToArray();

        Assert.Contains("skipReward", strings);
        Assert.Contains("REWARD_SKIP_UNAVAILABLE", strings);
        Assert.Contains("REWARD_SKIP_REJECTED", strings);
        Assert.Contains("REWARD_SKIP_FAILED", strings);
        Assert.NotNull(bridge.Properties.SingleOrDefault(property => property.Name == "NativeCommandInvoker"));

        string[] removedMembers =
        {
            "MetroTD.RewardSystem.RewardUIPanel",
            "SkipHandle",
            "UseCurrent",
            "m_currentQueueItem",
            "m_currentRewardQueneItems",
            "queueAdvanced",
            "remainingSelectionCount"
        };
        foreach (string removedMember in removedMembers)
        {
            Assert.DoesNotContain(removedMember, strings);
        }

        Assert.DoesNotContain(Calls(skip), IsCall(BridgeType, "FindMethodByParameterCount"));
        Assert.DoesNotContain(Calls(skip), IsCall(BridgeType, "DispatchRewardJump"));
        Assert.DoesNotContain(Calls(skip), IsCall(BridgeType, "RequestGameSave"));
    }

    [Fact]
    public void RuntimeSkip_MapsNativeResultAndFailsClosedWithoutTheNativeChannel()
    {
        Type bridgeType = LoadBridgeType();
        object bridge = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(bridgeType);
        SetField(bridgeType, bridge, "<IsAvailable>k__BackingField", true);
        System.Reflection.PropertyInfo invoker = bridgeType.GetProperty(
            "NativeCommandInvoker",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        // 没有注入原生通道时必须给出可解释的失败，而不是静默成功。
        object unavailable = InvokeSkip(bridgeType, bridge);
        Assert.False((bool)ResultProperty(unavailable, "Success")!);
        Assert.Equal("REWARD_SKIP_UNAVAILABLE", (string)ResultProperty(unavailable, "ErrorCode")!);

        // 原生通道受理后，作弊结果必须携带实际派发的命令。
        invoker.SetValue(bridge, (Func<string, JObject?, JObject>)NativeSuccess);
        object success = InvokeSkip(bridgeType, bridge);
        Assert.True((bool)ResultProperty(success, "Success")!);
        Assert.Equal("skipReward", CapturedCommand);
        JObject successData = Assert.IsType<JObject>(ResultProperty(success, "Data"));
        Assert.Equal("skipReward", successData.Value<string>("nativeCommand"));

        // 游戏原生命令如实返回"流程已移除"时，作弊结果必须如实失败并透出这条消息。
        invoker.SetValue(bridge, (Func<string, JObject?, JObject>)NativeRemoved);
        object rejected = InvokeSkip(bridgeType, bridge);
        Assert.False((bool)ResultProperty(rejected, "Success")!);
        Assert.Equal("REWARD_SKIP_REJECTED", (string)ResultProperty(rejected, "ErrorCode")!);
        Assert.Equal("奖励跳过流程已移除。", (string)ResultProperty(rejected, "Message")!);

        // 原生派发器抛异常时不能把异常泄漏给管道调用方。
        invoker.SetValue(bridge, (Func<string, JObject?, JObject>)NativeThrowing);
        object failed = InvokeSkip(bridgeType, bridge);
        Assert.False((bool)ResultProperty(failed, "Success")!);
        Assert.Equal("REWARD_SKIP_FAILED", (string)ResultProperty(failed, "ErrorCode")!);
    }

    private static string CapturedCommand { get; set; } = string.Empty;

    private static JObject NativeSuccess(string command, JObject? arguments)
    {
        CapturedCommand = command;
        return new JObject { ["success"] = true, ["message"] = "原生跳过已受理。" };
    }

    private static JObject NativeRemoved(string command, JObject? arguments) => new()
    {
        ["success"] = false,
        ["message"] = "奖励跳过流程已移除。"
    };

    private static JObject NativeThrowing(string command, JObject? arguments) =>
        throw new InvalidOperationException("native host crashed");

    private static object InvokeSkip(Type bridgeType, object bridge) =>
        bridgeType.GetMethod("SkipRewardPopup")!.Invoke(bridge, null)!;

    private static object? ResultProperty(object result, string name) =>
        result.GetType().GetProperty(name)!.GetValue(result);

    private static AssemblyDefinition ReadPlugin()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Loopstructor.AutoPlayer.Plugin.dll");
        Assert.True(File.Exists(path), "Plugin assembly was not copied to the test output: " + path);
        return AssemblyDefinition.ReadAssembly(path);
    }

    private static Type LoadBridgeType()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Loopstructor.AutoPlayer.Plugin.dll");
        return System.Reflection.Assembly.LoadFrom(path).GetType(BridgeType, throwOnError: true)!;
    }

    private static void SetField(Type type, object target, string name, object value) =>
        type.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(target, value);

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
}
