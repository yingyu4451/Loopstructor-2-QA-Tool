using System.Reflection;
using Loopstructor.AutoPlayer.Plugin;

namespace Loopstructor.AutoPlayer.Tests;

/// <summary>
/// 回归覆盖：游戏 1.409 为 MetroTD.CatapultSystem.EnergyCatapultTrainCacheService 增加了
/// TryDeployVehicle(LinePoint, VehicleController, bool, Line) 重载。旧实现用
/// Type.GetMethod(名称, 标志) 做单名称查询，在两个重载之间抛 AmbiguousMatchException，
/// 把整个 AutoPlayer 运行时打挂，QA 工具因此永远停在“等待插件连接”。
/// 这里固化正确的绑定行为与失败降级行为。
/// </summary>
public sealed class ReflectionContractBinderTests
{
    [Fact]
    public void ResolveMethod_BindsTheExactOverloadThatSingleNameLookupCannotDisambiguate()
    {
        Type service = typeof(OverloadedDeploymentService);

        // 故障形状：同名重载共存时，单名称反射查询无法消歧 —— 这正是线上崩溃的原因。
        Assert.Throws<AmbiguousMatchException>(() =>
            service.GetMethod("TryDeployVehicle", BindingFlags.Public | BindingFlags.Instance));

        // 插件只提交两个实参，因此必须绑定两参数重载。
        MethodInfo? bound = ReflectionContractBinder.ResolveMethod(
            service,
            "TryDeployVehicle",
            typeof(string),
            typeof(int));

        Assert.NotNull(bound);
        Assert.Equal(2, bound!.GetParameters().Length);
    }

    [Fact]
    public void ResolveMethod_ReturnsNullWhenNoOverloadMatchesTheRequestedParameterTypes()
    {
        MethodInfo? bound = ReflectionContractBinder.ResolveMethod(
            typeof(OverloadedDeploymentService),
            "TryDeployVehicle",
            typeof(int),
            typeof(int));

        Assert.Null(bound);
    }

    [Fact]
    public void ResolveMethod_ReturnsNullWhenAGameSideParameterTypeCouldNotBeResolved()
    {
        MethodInfo? bound = ReflectionContractBinder.ResolveMethod(
            typeof(OverloadedDeploymentService),
            "TryDeployVehicle",
            null,
            typeof(int));

        Assert.Null(bound);
    }

    [Fact]
    public void TryProbeCapability_ReportsTheReflectionFailureInsteadOfAbortingRuntimeStartup()
    {
        bool available = ReflectionContractBinder.TryProbeCapability(
            () => throw new AmbiguousMatchException("Ambiguous match found."),
            "EnergyCatapultTrainCacheService independent-vehicle contract",
            out string missing);

        Assert.False(available);
        Assert.Contains(
            "EnergyCatapultTrainCacheService independent-vehicle contract",
            missing,
            StringComparison.Ordinal);
        Assert.Contains("AmbiguousMatchException", missing, StringComparison.Ordinal);
    }

    [Fact]
    public void TryProbeCapability_KeepsTheCapabilityNameWhenTheContractIsSimplyAbsent()
    {
        bool available = ReflectionContractBinder.TryProbeCapability(
            () => false,
            "RebuildUI_DirectUpgradePanel decoration-factory contract",
            out string missing);

        Assert.False(available);
        Assert.Equal("RebuildUI_DirectUpgradePanel decoration-factory contract", missing);
    }

    [Fact]
    public void TryProbeCapability_RecordsNothingWhenTheCapabilityIsPresent()
    {
        bool available = ReflectionContractBinder.TryProbeCapability(
            () => true,
            "EnergyCatapultTrainCacheService independent-vehicle contract",
            out string missing);

        Assert.True(available);
        Assert.Equal(string.Empty, missing);
    }

    private sealed class OverloadedDeploymentService
    {
        // 与游戏 1.409 相同的形状：两参数入口 + 带可选参数的扩展入口。
        public string TryDeployVehicle(string point, int vehicle) => "两点位投放";

        public string TryDeployVehicle(string point, int vehicle, bool forward, object? launchLine = null) =>
            "带方向的投放";
    }
}
