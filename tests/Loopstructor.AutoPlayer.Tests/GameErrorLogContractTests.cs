using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Loopstructor.AutoPlayer.Tests;

/// <summary>
/// 回归覆盖：打包时未勾选 Development Build 的游戏不会在游戏内显示报错，
/// 游戏自带的调试日志系统也可能整体关闭。插件改为订阅 UnityEngine 的线程日志回调，
/// 把 Error/Exception/Assert 落盘成 game-errors.log，由 Host 并入运行日志。
/// 编辑器不加载本插件，因此不需要对应实现。
/// </summary>
public sealed class GameErrorLogContractTests
{
    private const string SinkType = "Loopstructor.AutoPlayer.Plugin.GameErrorLog";
    private const string SessionType = "Loopstructor.AutoPlayer.Plugin.AutoPlayerRuntimeSession";
    private const string HostEngineType = "Loopstructor.AutoPlayer.Host.DesktopHostEngine";

    [Fact]
    public void GameErrorLog_SubscribesToTheThreadedUnityCallbackAndKeepsOnlyFailures()
    {
        using AssemblyDefinition plugin = Read("Loopstructor.AutoPlayer.Plugin.dll");
        TypeDefinition sink = RequireType(plugin, SinkType);
        MethodDefinition attach = RequireMethod(sink, "Attach");
        MethodDefinition dispose = RequireMethod(sink, "Dispose");
        MethodDefinition handler = RequireMethod(sink, "OnLogMessage");

        // 任何构建下都会触发这个回调，这是唯一不依赖游戏自身开关的报错入口。
        Assert.Contains(Calls(attach), call => call.Name == "add_logMessageReceivedThreaded");
        Assert.Contains(Calls(dispose), call => call.Name == "remove_logMessageReceivedThreaded");

        Assert.Contains(Calls(handler), call => call.Name == "Category");
        // 回调可能来自任意线程，写入必须走串行化的写入器。
        Assert.Contains(Calls(handler), call => call.Name == "Write");

        // LogType.Error/Exception/Assert 是编译期常量，IL 里表现为 switch 常量而不是属性调用，
        // 因此改用类别方法里的字面量来确认只有这三类失败会被记录。
        MethodDefinition category = RequireMethod(sink, "Category");
        string[] categories = LoadedStrings(category).ToArray();
        foreach (string failure in new[] { "Error", "Exception", "Assert" })
        {
            Assert.Contains(failure, categories);
        }
        Assert.Equal(3, categories.Length);
    }

    [Fact]
    public void RuntimeSession_AttachesAndReleasesTheGameErrorLogWithItsLifecycle()
    {
        using AssemblyDefinition plugin = Read("Loopstructor.AutoPlayer.Plugin.dll");
        TypeDefinition session = RequireType(plugin, SessionType);
        MethodDefinition attach = RequireMethod(session, "AttachLifecycleEvents");
        MethodDefinition detach = RequireMethod(session, "DetachLifecycleEvents");

        Assert.Contains(Calls(attach), call => call.DeclaringType.FullName == SinkType);
        Assert.Contains(Calls(attach), call => call.Name == "Attach");
        Assert.Contains(Calls(detach), call => call.Name == "Dispose");
    }

    [Fact]
    public void Host_MergesTheGameErrorLogIntoTheRuntimeLog()
    {
        using AssemblyDefinition host = Read("Loopstructor.AutoPlayer.Host.dll");
        TypeDefinition engine = RequireType(host, HostEngineType);
        MethodDefinition adopt = RequireMethod(engine, "AdoptSession");
        MethodDefinition poll = RequireMethod(engine, "PollOnceAsync");
        MethodDefinition path = RequireMethod(engine, "GameErrorLogPath");

        // 路径与插件共用 Core 里的常量名，编译后同为字面量。
        Assert.Contains("game-errors.log", LoadedStrings(path));
        Assert.Contains(Calls(adopt), call => call.Name == "GameErrorLogPath");
        Assert.Contains(Calls(adopt), call => call.Name == "Reset");
        // PollOnceAsync 是 async：真正的 IL 在编译器生成的状态机类型里。
        Assert.Contains(
            StateMachineStrings(poll),
            value => value.Contains("游戏包报错", StringComparison.Ordinal));
    }

    private static AssemblyDefinition Read(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        Assert.True(File.Exists(path), "Assembly was not copied to the test output: " + path);
        return AssemblyDefinition.ReadAssembly(path);
    }

    private static TypeDefinition RequireType(AssemblyDefinition assembly, string fullName) =>
        assembly.MainModule.Types.Single(type => type.FullName == fullName);

    private static MethodDefinition RequireMethod(TypeDefinition type, string name) =>
        type.Methods.Single(method => method.Name == name);

    private static IEnumerable<string> LoadedStrings(MethodDefinition method) =>
        method.Body.Instructions
            .Where(instruction => instruction.OpCode.Code == Code.Ldstr)
            .Select(instruction => instruction.Operand)
            .OfType<string>();

    /// <summary>async 方法的实际 IL 在 <c>&lt;方法名&gt;d__NN</c> 状态机类型里。</summary>
    private static IEnumerable<string> StateMachineStrings(MethodDefinition method)
    {
        string prefix = "<" + method.Name + ">";
        foreach (TypeDefinition nested in method.DeclaringType.NestedTypes)
        {
            if (!nested.Name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            foreach (MethodDefinition candidate in nested.Methods.Where(item => item.HasBody))
            {
                foreach (string value in LoadedStrings(candidate)) yield return value;
            }
        }
    }

    private static IEnumerable<MethodReference> Calls(MethodDefinition method) =>
        method.Body.Instructions
            .Where(instruction => instruction.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj
                or Code.Ldftn or Code.Ldvirtftn)
            .Select(instruction => instruction.Operand)
            .OfType<MethodReference>();
}
