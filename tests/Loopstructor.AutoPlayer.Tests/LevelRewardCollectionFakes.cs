// 真实游戏 1.419 等级奖励领取界面的测试替身：只声明插件轻量反射契约使用的成员。
// 字段名/属性名必须与 Assets/Scripts/Systems/RewardSystem/UI 下的真实类型保持一致。
namespace MetroTD.RewardSystem
{
    public enum RewardItemType
    {
        Vehicle,
        Disposable,
        SuperModule,
        Money
    }

    public enum VehicleType
    {
        None,
        Assault
    }

    public enum DisposableEnum
    {
        None,
        Grenade
    }

    public enum SuperModuleEnum
    {
        None,
        Booster
    }

    public enum FetterEnum
    {
        None,
        Attack
    }

    public sealed class FetterModuleData
    {
        public FetterEnum fetterEnum;
        public int level;
        public int count;
    }

    public sealed class LevelRewardSettlementItem
    {
        public VehicleType vehicleType;
        public DisposableEnum disposableEnum;
        public SuperModuleEnum superModuleEnum;
        public FetterModuleData? fetter;
        public bool allowRepeatedAcquire;
    }

    public sealed class LevelRewardCollectionIcon
    {
        public string settlementId = string.Empty;
        public int itemIndex = -1;
        public RewardItemType itemType;
        public bool layerMoney;
        public bool granted;
        public bool canClaim = true;
        public string fallbackName = string.Empty;
        public string quantity = string.Empty;
        public string sourceLabel = string.Empty;
        public LevelRewardSettlementItem? item;
    }

    public sealed class RewardCollectionItemUI
    {
        public LevelRewardCollectionIcon? Data { get; set; }

        public int ClaimCount { get; private set; }

        public void Claim() => ClaimCount++;
    }

    public sealed class RewardCreator
    {
        public List<RewardCollectionItemUI> Components { get; } = new();
    }

    public sealed class RewardUIPanel
    {
        public static RewardUIPanel? Instance { get; set; }

        public bool Open { get; set; }

        public bool IsActive => Open;

        public bool IsShowingLevelRewards { get; set; }

        public RewardCreator m_creator = new();

        public bool m_claimingLevelRewards;

        public bool m_refreshRequested;

        public List<LevelRewardCollectionIcon> Displayed { get; } = new();

        public IReadOnlyList<LevelRewardCollectionIcon> DisplayedRewards => Displayed;

        public int ClaimSingleDisplayedRewardCount { get; private set; }

        public bool IsOpen() => Open;

        public void ClaimSingleDisplayedReward(LevelRewardCollectionIcon data) =>
            ClaimSingleDisplayedRewardCount++;
    }
}
