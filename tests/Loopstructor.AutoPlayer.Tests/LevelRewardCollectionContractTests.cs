using Loopstructor.AutoPlayer.Plugin;
using MetroTD.RewardSystem;
using Mono.Cecil;
using Newtonsoft.Json.Linq;

namespace Loopstructor.AutoPlayer.Tests;

public sealed class LevelRewardCollectionContractTests
{
    private const string FallbackType = "Loopstructor.AutoPlayer.Plugin.RewardUiRuntimeFallback";

    [Fact]
    public void QueryReward_MapsLevelCollectionCardsToStableOptions()
    {
        using LevelRewardPanelFixture fixture = new();
        fixture.DisplayVehicle("settlement-a", 0, "霜金");
        fixture.DisplayDisposable("settlement-b", 1, "追魂");
        fixture.DisplayMoney(120, fallbackName: "金币 120");
        fixture.Panel.IsShowingLevelRewards = true;
        fixture.Panel.Open = true;

        Assert.True(RewardUiRuntimeFallback.TryQueryState(out JObject first));
        Assert.True(ResultSuccess(first), first.ToString());
        JObject firstState = StateOf(first);
        JArray firstOptions = (JArray)firstState["options"]!;

        Assert.True(firstState["levelCollection"]!.Value<bool>());
        Assert.True(firstState["panelOpen"]!.Value<bool>());
        Assert.Equal(3, firstOptions.Count);
        Assert.Equal(new[] { 0, 1, 2 }, firstOptions.Select(option => option["index"]!.Value<int>()).ToArray());
        Assert.Equal(
            new[] { "vehicle", "disposable", "money" },
            firstOptions.Select(option => option["rewardKind"]!.Value<string>()).ToArray());
        Assert.Equal(120, firstOptions[2]["money"]!.Value<int>());
        Assert.Equal("金币 120", firstOptions[2]["rewardName"]!.Value<string>());
        foreach (JObject option in firstOptions.Cast<JObject>())
        {
            Assert.NotEqual(0, option["instanceId"]!.Value<int>());
            Assert.True(option["buttonActive"]!.Value<bool>());
            Assert.True(option["canAcquire"]!.Value<bool>());
            Assert.Equal("MetroTD.RewardSystem.RewardCollectionItemUI", option["nativeType"]!.Value<string>());
        }

        int[] instanceIds = firstOptions.Select(option => option["instanceId"]!.Value<int>()).ToArray();
        Assert.Equal(3, instanceIds.Distinct().Count());
        string phaseToken = firstState["phaseToken"]!.Value<string>()!;
        Assert.NotEmpty(phaseToken);

        Assert.True(RewardUiRuntimeFallback.TryQueryState(out JObject second));
        JObject secondState = StateOf(second);
        Assert.Equal(phaseToken, secondState["phaseToken"]!.Value<string>());
        Assert.Equal(
            instanceIds,
            ((JArray)secondState["options"]!).Select(option => option["instanceId"]!.Value<int>()).ToArray());

        fixture.ReplaceCardsWithNewInstances();
        Assert.True(RewardUiRuntimeFallback.TryQueryState(out JObject third));
        Assert.NotEqual(phaseToken, StateOf(third)["phaseToken"]!.Value<string>());
    }

    [Fact]
    public void Selection_InvokesClaimOnlyOnTheMatchingCard()
    {
        using LevelRewardPanelFixture fixture = new();
        fixture.DisplayVehicle("settlement-a", 0, "霜金");
        fixture.DisplayDisposable("settlement-b", 1, "追魂");
        fixture.DisplayMoney(120, fallbackName: "金币 120");
        fixture.Panel.IsShowingLevelRewards = true;
        fixture.Panel.Open = true;

        Assert.True(RewardUiRuntimeFallback.TryQueryState(out JObject query));
        JObject state = StateOf(query);
        JObject target = (JObject)((JArray)state["options"]!)[1];
        JObject arguments = new()
        {
            ["phaseToken"] = state["phaseToken"]!.Value<string>(),
            ["itemInstanceId"] = target["instanceId"]!.Value<int>(),
            ["index"] = target["index"]!.Value<int>()
        };

        Assert.True(RewardUiRuntimeFallback.TryChooseOption(arguments, out JObject result));
        Assert.True(ResultSuccess(result));
        Assert.Equal(0, fixture.Cards[0].ClaimCount);
        Assert.Equal(1, fixture.Cards[1].ClaimCount);
        Assert.Equal(0, fixture.Cards[2].ClaimCount);
        JObject resultState = StateOf(result);
        Assert.Equal(1, resultState["selectedIndex"]!.Value<int>());
        Assert.True(resultState["invocationStarted"]!.Value<bool>());
    }

    [Fact]
    public void Selection_RejectsAStalePhaseTokenWithoutClaimingAnything()
    {
        using LevelRewardPanelFixture fixture = new();
        fixture.DisplayVehicle("settlement-a", 0, "霜金");
        fixture.Panel.IsShowingLevelRewards = true;
        fixture.Panel.Open = true;

        Assert.True(RewardUiRuntimeFallback.TryQueryState(out JObject query));
        JObject option = (JObject)((JArray)StateOf(query)["options"]!)[0];
        JObject arguments = new()
        {
            ["phaseToken"] = "stale-phase-token",
            ["itemInstanceId"] = option["instanceId"]!.Value<int>(),
            ["index"] = option["index"]!.Value<int>()
        };

        Assert.True(RewardUiRuntimeFallback.TryChooseOption(arguments, out JObject result));
        Assert.False(ResultSuccess(result));
        Assert.Contains("奖励阶段已变化", result["message"]!.Value<string>());
        Assert.Equal(0, fixture.Cards[0].ClaimCount);
    }

    [Fact]
    public void Selection_RejectsAMismatchedInstanceIdWithoutClaimingAnything()
    {
        using LevelRewardPanelFixture fixture = new();
        fixture.DisplayVehicle("settlement-a", 0, "霜金");
        fixture.DisplayDisposable("settlement-b", 1, "追魂");
        fixture.Panel.IsShowingLevelRewards = true;
        fixture.Panel.Open = true;

        Assert.True(RewardUiRuntimeFallback.TryQueryState(out JObject query));
        JObject state = StateOf(query);
        JArray options = (JArray)state["options"]!;
        JObject arguments = new()
        {
            ["phaseToken"] = state["phaseToken"]!.Value<string>(),
            ["itemInstanceId"] = options[0]["instanceId"]!.Value<int>(),
            ["index"] = 1
        };

        Assert.True(RewardUiRuntimeFallback.TryChooseOption(arguments, out JObject result));
        Assert.False(ResultSuccess(result));
        Assert.Contains("实例与索引不匹配", result["message"]!.Value<string>());
        Assert.Equal(0, fixture.Cards[0].ClaimCount);
        Assert.Equal(0, fixture.Cards[1].ClaimCount);
    }

    [Fact]
    public void Selection_RejectsAGrantedCardWithoutClaimingAnything()
    {
        using LevelRewardPanelFixture fixture = new();
        LevelRewardCollectionIcon icon = fixture.DisplayVehicle("settlement-a", 0, "霜金");
        icon.granted = true;
        fixture.Panel.IsShowingLevelRewards = true;
        fixture.Panel.Open = true;

        Assert.True(RewardUiRuntimeFallback.TryQueryState(out JObject query));
        JObject state = StateOf(query);
        JObject option = (JObject)((JArray)state["options"]!)[0];
        Assert.False(option["buttonActive"]!.Value<bool>());
        JObject arguments = new()
        {
            ["phaseToken"] = state["phaseToken"]!.Value<string>(),
            ["itemInstanceId"] = option["instanceId"]!.Value<int>(),
            ["index"] = 0
        };

        Assert.True(RewardUiRuntimeFallback.TryChooseOption(arguments, out JObject result));
        Assert.False(ResultSuccess(result));
        Assert.Contains("不可点击", result["message"]!.Value<string>());
        Assert.Equal(0, fixture.Cards[0].ClaimCount);
    }

    [Fact]
    public void Query_DoesNotTreatAnOpenLegacyPanelAsTheLevelCollectionUi()
    {
        using LevelRewardPanelFixture fixture = new();
        fixture.DisplayMoney(50, fallbackName: "金币 50");
        fixture.Panel.IsShowingLevelRewards = false;
        fixture.Panel.m_creator.Components.Clear();
        fixture.Cards.Clear();
        fixture.Panel.Open = true;

        // 旧奖励队列契约所需的游戏类型在测试程序集中不存在，因此回退路径只能如实报告不可用。
        Assert.False(RewardUiRuntimeFallback.TryQueryState(out _));
    }

    [Fact]
    public void Query_ReportsAClosedLevelPanelWhenTheGameHasNotCreatedTheInstanceYet()
    {
        RewardUIPanel.Instance = null;

        Assert.True(RewardUiRuntimeFallback.TryQueryState(out JObject result));
        Assert.True(ResultSuccess(result));
        JObject state = StateOf(result);
        Assert.True(state["levelCollection"]!.Value<bool>());
        Assert.False(state["panelOpen"]!.Value<bool>());
        Assert.Empty((JArray)state["options"]!);
    }

    [Fact]
    public void Fallback_KeepsTheLegacyRewardPathAndAddsTheLevelCollectionBinding()
    {
        using AssemblyDefinition assembly = ReadPlugin();
        TypeDefinition fallback = RequireType(assembly, FallbackType);
        string[] strings = fallback.Methods
            .Where(method => method.HasBody)
            .SelectMany(LoadedStrings)
            .ToArray();

        Assert.Contains("MetroTD.RewardSystem.RewardUIPanel", strings);
        Assert.Contains("MetroTD.RewardSystem.GeneralRewardItemUI", strings);
        Assert.Contains("m_rewardContent", strings);
        Assert.Contains("MetroTD.RewardSystem.RewardCollectionItemUI", strings);
        Assert.Contains("MetroTD.RewardSystem.LevelRewardCollectionIcon", strings);
        Assert.Contains("DisplayedRewards", strings);
        Assert.Contains("IsShowingLevelRewards", strings);
        Assert.Contains(fallback.Methods, method => method.Name == "TryQueryLevelState");
        Assert.Contains(fallback.Methods, method => method.Name == "TryChooseLevelOption");
    }

    private static JObject StateOf(JObject result) => (JObject)result["data"]!["state"]!;

    private static bool ResultSuccess(JObject result) => result["success"]!.Value<bool>();

    private static AssemblyDefinition ReadPlugin()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Loopstructor.AutoPlayer.Plugin.dll");
        Assert.True(File.Exists(path), "Plugin assembly was not copied to the test output: " + path);
        return AssemblyDefinition.ReadAssembly(path);
    }

    private static TypeDefinition RequireType(AssemblyDefinition assembly, string fullName) =>
        assembly.MainModule.Types.Single(type => type.FullName == fullName);

    private static IEnumerable<string> LoadedStrings(MethodDefinition method) =>
        method.Body.Instructions
            .Where(instruction => instruction.OpCode.Code == Mono.Cecil.Cil.Code.Ldstr)
            .Select(instruction => instruction.Operand)
            .OfType<string>();

    private sealed class LevelRewardPanelFixture : IDisposable
    {
        public LevelRewardPanelFixture()
        {
            RewardUIPanel.Instance = Panel;
        }

        public RewardUIPanel Panel { get; } = new();

        public List<RewardCollectionItemUI> Cards { get; } = new();

        public LevelRewardCollectionIcon DisplayVehicle(string settlementId, int itemIndex, string fallbackName)
        {
            LevelRewardCollectionIcon icon = Display(
                RewardItemType.Vehicle,
                settlementId,
                itemIndex,
                fallbackName);
            icon.item = new LevelRewardSettlementItem
            {
                vehicleType = VehicleType.Assault,
                fetter = new FetterModuleData
                {
                    fetterEnum = FetterEnum.Attack,
                    level = 2,
                    count = 3
                }
            };
            return icon;
        }

        public LevelRewardCollectionIcon DisplayDisposable(string settlementId, int itemIndex, string fallbackName)
        {
            LevelRewardCollectionIcon icon = Display(
                RewardItemType.Disposable,
                settlementId,
                itemIndex,
                fallbackName);
            icon.item = new LevelRewardSettlementItem
            {
                disposableEnum = DisposableEnum.Grenade
            };
            return icon;
        }

        public LevelRewardCollectionIcon DisplayMoney(int amount, string fallbackName)
        {
            LevelRewardCollectionIcon icon = Display(RewardItemType.Money, "settlement-money", -1, fallbackName);
            icon.layerMoney = true;
            icon.quantity = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return icon;
        }

        public void ReplaceCardsWithNewInstances()
        {
            List<LevelRewardCollectionIcon> icons = Panel.Displayed.ToList();
            Panel.m_creator.Components.Clear();
            Cards.Clear();
            foreach (LevelRewardCollectionIcon icon in icons)
            {
                AttachCard(icon);
            }
        }

        private LevelRewardCollectionIcon Display(
            RewardItemType itemType,
            string settlementId,
            int itemIndex,
            string fallbackName)
        {
            LevelRewardCollectionIcon icon = new()
            {
                settlementId = settlementId,
                itemIndex = itemIndex,
                itemType = itemType,
                fallbackName = fallbackName,
                canClaim = true
            };
            Panel.Displayed.Add(icon);
            AttachCard(icon);
            return icon;
        }

        private void AttachCard(LevelRewardCollectionIcon icon)
        {
            RewardCollectionItemUI card = new() { Data = icon };
            Cards.Add(card);
            Panel.m_creator.Components.Add(card);
        }

        public void Dispose()
        {
            RewardUIPanel.Instance = null;
            Panel.Displayed.Clear();
            Panel.m_creator.Components.Clear();
        }
    }
}
