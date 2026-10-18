using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Loopstructor.AutoPlayer.Plugin;

/// <summary>
/// Reads and operates the registered reward UI without whole-scene scans or full MCP snapshots.
/// </summary>
internal static class RewardUiRuntimeFallback
{
    private const string RewardPanelTypeName = "MetroTD.RewardSystem.RewardUIPanel";
    private const string RewardItemTypeName = "MetroTD.RewardSystem.GeneralRewardItemUI";
    private const string RewardMutexTypeName = "MetroTD.RewardSystem.GeneralRewardItemMutexController";
    private const string RewardSpawnerTypeName = "MetroTD.RewardSystem.RewardObjectSpawner";
    private const string RewardObjectTypeName = "RewardObjectBase";
    private const string RewardTypeName = "MetroTD.RewardSystem.Reward";
    private const string RazorRewardTypeName = "MetroTD.RewardSystem.RazorReward";
    private const string PotionRewardTypeName = "MetroTD.RewardSystem.PotionReward";
    private const string SuperModuleRewardTypeName = "MetroTD.RewardSystem.SuperModuleReward";
    private const string DisposableManagerTypeName = "MetroTD.DisposableSystem.DisposableManager";
    private const string SuperModuleManagerTypeName = "MetroTD.SuperModuleSystem.SuperModuleManager";
    private const string MapConfigControllerTypeName = "MetroTD.RoomSystem.LoopstructorMapCfgController";
    private const string LevelRewardCollectionItemTypeName = "MetroTD.RewardSystem.RewardCollectionItemUI";
    private const string LevelRewardCollectionIconTypeName = "MetroTD.RewardSystem.LevelRewardCollectionIcon";
    private const string ResultSource = "pluginReflection:RewardUI:light";

    private static ReflectionContract? _contract;
    private static LevelReflectionContract? _levelContract;

    internal static bool TryQueryState(out JObject result)
    {
        if (TryQueryLevelState(out result))
        {
            return true;
        }

        result = null!;
        if (!TryGetContract(out ReflectionContract contract))
        {
            return false;
        }

        try
        {
            RewardSnapshot snapshot = BuildSnapshot(contract);
            result = Success("\u5df2\u8bfb\u53d6\u8f7b\u91cf\u5956\u52b1\u754c\u9762\u72b6\u6001\u3002", snapshot.State);
            return true;
        }
        catch (Exception exception)
        {
            result = Error(
                "\u8bfb\u53d6\u5956\u52b1\u754c\u9762\u5931\u8d25\uff1a" + Unwrap(exception).Message,
                new JObject { ["source"] = ResultSource });
            return true;
        }
    }

    internal static bool TryChooseOption(JObject? arguments, out JObject result)
    {
        if (TryChooseLevelOption(arguments, out result))
        {
            return true;
        }

        result = null!;
        if (!TryGetContract(out ReflectionContract contract))
        {
            return false;
        }

        RewardSnapshot before;
        try
        {
            before = BuildSnapshot(contract);
        }
        catch (Exception exception)
        {
            result = Error(
                "\u9009\u62e9\u5956\u52b1\u524d\u65e0\u6cd5\u8bfb\u53d6\u754c\u9762\u72b6\u6001\uff1a" + Unwrap(exception).Message,
                new JObject { ["source"] = ResultSource });
            return true;
        }

        if (!TryReadSelection(arguments, out string phaseToken, out int itemInstanceId, out int index))
        {
            result = SelectionError(
                "\u5956\u52b1\u9009\u62e9\u53c2\u6570\u4e0d\u5b8c\u6574\uff1b\u5fc5\u987b\u540c\u65f6\u63d0\u4f9b phaseToken\u3001itemInstanceId \u548c index\u3002",
                before.State);
            return true;
        }

        if (!before.PanelOpen)
        {
            result = SelectionError("\u5956\u52b1\u9762\u677f\u5df2\u7ecf\u5173\u95ed\u3002", before.State);
            return true;
        }

        if (!string.Equals(before.PhaseToken, phaseToken, StringComparison.Ordinal))
        {
            result = SelectionError("\u5956\u52b1\u9636\u6bb5\u5df2\u53d8\u5316\uff0c\u62d2\u7edd\u4f7f\u7528\u8fc7\u671f\u9009\u9879\u3002", before.State);
            return true;
        }

        RewardOptionSnapshot? target = before.Options.FirstOrDefault(option =>
            option.Index == index && option.InstanceId == itemInstanceId);
        if (target == null)
        {
            result = SelectionError("\u5956\u52b1\u9009\u9879\u5b9e\u4f8b\u4e0e\u7d22\u5f15\u4e0d\u5339\u914d\u3002", before.State);
            return true;
        }

        if (!before.MutexAvailable)
        {
            result = SelectionError("\u65e0\u6cd5\u9a8c\u8bc1\u5956\u52b1\u4e92\u65a5\u9501\uff0c\u4e3a\u907f\u514d\u91cd\u590d\u9886\u53d6\uff0c\u672c\u6b21\u672a\u6267\u884c\u70b9\u51fb\u3002", before.State);
            return true;
        }

        if (before.Busy)
        {
            result = SelectionError("\u5956\u52b1\u4e92\u65a5\u9501\u5fd9\uff0c\u672c\u6b21\u672a\u6267\u884c\u70b9\u51fb\u3002", before.State);
            return true;
        }


        if (!target.ButtonActive)
        {
            result = SelectionError("\u6307\u5b9a\u5956\u52b1\u9009\u9879\u5f53\u524d\u4e0d\u53ef\u70b9\u51fb\u3002", before.State);
            return true;
        }

        if (!target.CanAcquire)
        {
            string reason = string.IsNullOrWhiteSpace(target.UnavailableReason)
                ? "游戏当前不允许领取该奖励。"
                : target.UnavailableReason;
            result = SelectionError("指定奖励当前不可领取：" + reason, before.State);
            return true;
        }

        bool invocationStarted = false;
        try
        {
            invocationStarted = true;
            contract.ClickEvent.Invoke(target.Item, null);

            RewardSnapshot after = BuildSnapshot(contract);
            after.State["invocationStarted"] = true;
            after.State["selectedPhaseToken"] = phaseToken;
            after.State["selectedItemInstanceId"] = itemInstanceId;
            after.State["selectedIndex"] = index;
            result = Success("\u5df2\u53d1\u9001\u4e00\u6b21\u5956\u52b1\u9009\u62e9\uff0c\u6b63\u5728\u7b49\u5f85\u754c\u9762\u6536\u655b\u3002", after.State);
            return true;
        }
        catch (Exception exception)
        {
            JObject failureState = before.State;
            failureState["source"] = ResultSource;
            if (invocationStarted)
            {
                failureState["outcomeUnknown"] = true;
                failureState["needsReconciliation"] = true;
                failureState["uncertaintyOrigin"] = "rewardClickException";
                failureState["invocationStarted"] = true;
            }

            result = Error(
                "\u5956\u52b1\u9009\u62e9\u8c03\u7528\u5931\u8d25\uff1a" + Unwrap(exception).Message,
                failureState);
            return true;
        }
    }

    internal static bool TryCollectRewardObject(JObject? arguments, out JObject result)
    {
        result = null!;
        if (!TryGetContract(out ReflectionContract contract))
        {
            return false;
        }

        if (!TryReadRewardObjectIdentity(arguments, out int instanceId))
        {
            result = Error(
                "\u6536\u53d6\u5956\u52b1\u7269\u53c2\u6570\u65e0\u6548\uff1b\u5fc5\u987b\u63d0\u4f9b\u975e\u96f6 instanceId\u3002",
                new JObject
                {
                    ["source"] = ResultSource,
                    ["targetInstanceId"] = instanceId,
                    ["invocationStarted"] = false
                });
            return true;
        }

        RewardObjectCollectionSnapshot before;
        try
        {
            before = BuildRewardObjectCollectionSnapshot(contract, instanceId);
        }
        catch (Exception exception)
        {
            result = Error(
                "\u6536\u53d6\u5956\u52b1\u7269\u524d\u65e0\u6cd5\u8bfb\u53d6\u5df2\u6ce8\u518c\u5bf9\u8c61\uff1a" + Unwrap(exception).Message,
                new JObject
                {
                    ["source"] = ResultSource,
                    ["targetInstanceId"] = instanceId,
                    ["invocationStarted"] = false
                });
            return true;
        }

        if (!before.SpawnerAvailable)
        {
            result = Error("\u5956\u52b1\u7269\u751f\u6210\u5668\u5f53\u524d\u4e0d\u53ef\u7528\uff0c\u672c\u6b21\u672a\u6267\u884c\u70b9\u51fb\u3002", before.State);
            return true;
        }

        if (before.MatchCount == 0 || before.Target == null)
        {
            result = Error("\u672a\u5728\u5df2\u6ce8\u518c\u7684\u6d3b\u52a8\u5956\u52b1\u7269\u4e2d\u627e\u5230\u6307\u5b9a instanceId\uff0c\u672c\u6b21\u672a\u6267\u884c\u70b9\u51fb\u3002", before.State);
            return true;
        }

        if (before.MatchCount != 1)
        {
            result = Error("\u6307\u5b9a instanceId \u5339\u914d\u5230\u591a\u4e2a\u5df2\u6ce8\u518c\u5956\u52b1\u7269\uff0c\u4e3a\u907f\u514d\u8bef\u6536\u53d6\uff0c\u672c\u6b21\u672a\u6267\u884c\u70b9\u51fb\u3002", before.State);
            return true;
        }

        if (contract.RewardObjectButton == null ||
            contract.RewardButtonPointEnter == null ||
            contract.RewardButtonLeftPointDown == null ||
            contract.RewardButtonLeftPointUp == null)
        {
            result = Error("\u65e0\u6cd5\u89e3\u6790\u5956\u52b1\u7269 Btn \u73a9\u5bb6\u70b9\u51fb\u94fe\uff0c\u672c\u6b21\u672a\u6267\u884c\u6536\u53d6\u3002", before.State);
            return true;
        }

        object? button;
        try
        {
            button = contract.RewardObjectButton.GetValue(before.Target, null);
        }
        catch (Exception exception)
        {
            result = Error(
                "\u8bfb\u53d6\u5956\u52b1\u7269 Btn \u5931\u8d25\uff0c\u672c\u6b21\u672a\u6267\u884c\u6536\u53d6\uff1a" + Unwrap(exception).Message,
                before.State);
            return true;
        }

        if (button == null || button is UnityEngine.Object unityButton && unityButton == null)
        {
            result = Error("\u6307\u5b9a\u5956\u52b1\u7269\u6ca1\u6709\u53ef\u7528 Btn\uff0c\u672c\u6b21\u672a\u6267\u884c\u6536\u53d6\u3002", before.State);
            return true;
        }

        bool invocationStarted = false;
        try
        {
            invocationStarted = true;
            contract.RewardButtonPointEnter.Invoke(button, null);
            contract.RewardButtonLeftPointDown.Invoke(button, null);
            contract.RewardButtonLeftPointUp.Invoke(button, null);

            result = Success(
                "\u5df2\u53d1\u9001\u5956\u52b1\u7269\u73a9\u5bb6\u70b9\u51fb\u94fe\uff0c\u6b63\u5728\u7b49\u5f85\u6536\u53d6\u7ed3\u7b97\u3002",
                new JObject
                {
                    ["source"] = ResultSource,
                    ["invocationStarted"] = true,
                    ["targetInstanceId"] = instanceId,
                    ["pending"] = true,
                    ["needsPolling"] = true
                });
            return true;
        }
        catch (Exception exception)
        {
            JObject failureState = before.State;
            failureState["source"] = ResultSource;
            failureState["targetInstanceId"] = instanceId;
            failureState["outcomeUnknown"] = true;
            failureState["needsReconciliation"] = true;
            failureState["uncertaintyOrigin"] = "rewardObjectClickException";
            failureState["invocationStarted"] = invocationStarted;
            failureState["pending"] = true;
            failureState["needsPolling"] = true;

            result = Error(
                "\u5956\u52b1\u7269\u70b9\u51fb\u94fe\u8c03\u7528\u5931\u8d25\uff0c\u7ed3\u679c\u9700\u8981\u91cd\u65b0\u5bf9\u8d26\uff1a" + Unwrap(exception).Message,
                failureState);
            return true;
        }
    }

    private static RewardObjectCollectionSnapshot BuildRewardObjectCollectionSnapshot(
        ReflectionContract contract,
        int instanceId)
    {
        JObject state = new()
        {
            ["source"] = ResultSource,
            ["targetInstanceId"] = instanceId,
            ["invocationStarted"] = false,
            ["spawnerAvailable"] = false,
            ["activeRewardObjectCount"] = 0,
            ["matchingRewardObjectCount"] = 0
        };

        if (!TryGetRegisteredObject(contract.SpawnerInstance, out object? spawner) ||
            spawner == null ||
            contract.SpawnerRewardObjects.GetValue(spawner) is not IEnumerable rewardObjects)
        {
            return new RewardObjectCollectionSnapshot(state, false, 0, null);
        }

        int activeCount = 0;
        int matchCount = 0;
        Component? target = null;
        foreach (object? candidate in rewardObjects)
        {
            if (candidate is not GameObject gameObject || gameObject == null ||
                !gameObject.scene.IsValid() || !gameObject.activeInHierarchy)
            {
                continue;
            }

            Component? rewardObject = gameObject.GetComponent(contract.RewardObjectType);
            if (rewardObject == null)
            {
                continue;
            }

            activeCount++;
            if (rewardObject.GetInstanceID() != instanceId)
            {
                continue;
            }

            matchCount++;
            target = rewardObject;
        }

        state["spawnerAvailable"] = true;
        state["activeRewardObjectCount"] = activeCount;
        state["matchingRewardObjectCount"] = matchCount;
        return new RewardObjectCollectionSnapshot(state, true, matchCount, target);
    }

    private static RewardSnapshot BuildSnapshot(ReflectionContract contract)
    {
        Component? panel = TryGetRegisteredComponent(contract.PanelInstance);
        bool panelOpen = IsOpenPanel(contract, panel);
        bool panelActive = panelOpen && contract.PanelIsActive.GetValue(panel, null) is bool active && active;

        bool mutexAvailable = TryGetRegisteredObject(contract.MutexInstance, out object? mutex);
        bool busy = mutexAvailable && mutex != null &&
                    contract.MutexBusy.GetValue(mutex, null) is bool mutexBusy && mutexBusy;

        bool mapRewardVehicleGetFetter = panelOpen && ReadMapRewardVehicleGetFetter(contract);
        List<RewardOptionSnapshot> options = panelOpen && panel != null
            ? GetRewardOptions(contract, panel, mapRewardVehicleGetFetter)
            : new List<RewardOptionSnapshot>();
        string phaseToken = panelOpen && panel != null
            ? BuildPhaseToken(panel.GetInstanceID(), options)
            : string.Empty;

        JArray optionStates = new(options.Select(option => option.State));
        JArray rewardObjects = BuildRewardObjects(
            contract,
            out int pooledRewardObjectCount,
            out bool spawnerAvailable);
        bool optionsPending = panelOpen && (options.Count == 0 || !mutexAvailable);
        bool rewardObjectsPending = !panelOpen && !spawnerAvailable;
        JObject state = new()
        {
            ["panelOpen"] = panelOpen,
            ["panelActive"] = panelActive,
            ["panelAvailable"] = panel != null,
            ["panelInstanceId"] = panel == null ? 0 : panel.GetInstanceID(),
            ["phaseToken"] = phaseToken,
            ["busy"] = busy,
            ["mutexAvailable"] = mutexAvailable,
            ["spawnerAvailable"] = spawnerAvailable,
            ["pending"] = optionsPending || rewardObjectsPending,
            ["needsPolling"] = optionsPending || rewardObjectsPending,
            ["activeRewardObjectCount"] = rewardObjects.Count,
            ["pooledRewardObjectCount"] = pooledRewardObjectCount,
            ["rewardObjects"] = rewardObjects,
            ["options"] = optionStates,
            ["source"] = ResultSource
        };

        return new RewardSnapshot(
            state,
            panelOpen,
            phaseToken,
            mutexAvailable,
            busy,
            options);
    }

    private static List<RewardOptionSnapshot> GetRewardOptions(
        ReflectionContract contract,
        Component panel,
        bool mapRewardVehicleGetFetter)
    {
        List<RewardOptionSnapshot> options = new();
        if (contract.PanelRewardContent.GetValue(panel) is not Transform content)
        {
            return options;
        }

        for (int childIndex = 0; childIndex < content.childCount; childIndex++)
        {
            Transform child = content.GetChild(childIndex);
            if (child == null || !child.gameObject.activeInHierarchy)
            {
                continue;
            }

            Component? item = child.GetComponent(contract.RewardItemType);
            if (item == null || !item.gameObject.activeInHierarchy)
            {
                continue;
            }

            options.Add(BuildRewardOption(contract, item, options.Count, mapRewardVehicleGetFetter));
        }

        return options;
    }

    private static RewardOptionSnapshot BuildRewardOption(
        ReflectionContract contract,
        Component item,
        int index,
        bool mapRewardVehicleGetFetter)
    {
        object? reward = contract.ItemReward.GetValue(item);
        int money = contract.ItemMoney.GetValue(item) is int amount ? amount : 0;
        object? button = contract.ItemButton.GetValue(item, null);
        bool buttonActive = button != null &&
                            contract.ButtonActive.GetValue(button, null) is bool active && active;
        bool isVehicle = reward != null && contract.RazorRewardType.IsInstanceOfType(reward);
        bool isDisposable = reward != null && contract.PotionRewardType.IsInstanceOfType(reward);
        bool isSuperModule = reward != null && contract.SuperModuleRewardType.IsInstanceOfType(reward);
        DetermineAvailability(
            contract,
            reward,
            isDisposable,
            isSuperModule,
            out bool canAcquire,
            out string unavailableReason);
        string rewardKind = isVehicle
            ? "vehicle"
            : isDisposable
                ? "disposable"
                : isSuperModule
                    ? "superModule"
                    : money > 0
                        ? "money"
                        : "unknown";

        object? fetter = isVehicle ? contract.RazorInitFetter.GetValue(reward) : null;
        bool carryInitFetter = isVehicle && contract.RazorCarryInitFetter.GetValue(reward) is bool carry && carry;
        mapRewardVehicleGetFetter = isVehicle && mapRewardVehicleGetFetter;
        JObject? fetterState = BuildFetterState(contract, fetter);
        bool willCarryInitFetter = carryInitFetter && mapRewardVehicleGetFetter &&
                                   fetterState?["isActual"]?.Value<bool>() == true;
        JArray effectiveFetters = new();
        if (willCarryInitFetter && fetterState != null)
        {
            effectiveFetters.Add(fetterState.DeepClone());
        }

        JObject state = new()
        {
            ["index"] = index,
            ["instanceId"] = item.GetInstanceID(),
            ["type"] = item.GetType().Name,
            ["buttonActive"] = buttonActive,
            ["canAcquire"] = canAcquire,
            ["unavailableReason"] = NullableString(string.IsNullOrWhiteSpace(unavailableReason) ? null : unavailableReason),
            ["rewardKind"] = rewardKind,
            ["rewardType"] = NullableString(reward?.GetType().Name),
            ["rewardName"] = NullableString(reward is UnityEngine.Object rewardObject ? rewardObject.name : null),
            ["rewardRare"] = NullableString(reward == null ? null : contract.RewardRare.GetValue(reward)?.ToString()),
            ["vehicleType"] = NullableString(isVehicle ? contract.RazorVehicleType.GetValue(reward)?.ToString() : null),
            ["initFetter"] = NullableString(fetterState?["fetterEnum"]?.Value<string>()),
            ["templateInitFetter"] = (JToken?)fetterState ?? JValue.CreateNull(),
            ["carryInitFetter"] = isVehicle ? carryInitFetter : JValue.CreateNull(),
            ["mapRewardVehicleGetFetter"] = isVehicle ? mapRewardVehicleGetFetter : JValue.CreateNull(),
            ["willCarryInitFetter"] = isVehicle ? willCarryInitFetter : JValue.CreateNull(),
            ["effectiveInitFetter"] = NullableString(willCarryInitFetter ? fetterState?["fetterEnum"]?.Value<string>() : null),
            ["effectiveFetters"] = effectiveFetters,
            ["disposableEnum"] = NullableString(isDisposable ? contract.PotionDisposable.GetValue(reward)?.ToString() : null),
            ["assignDisposableEnum"] = NullableString(isDisposable ? contract.PotionAssignedDisposable.GetValue(reward)?.ToString() : null),
            ["superModuleEnum"] = NullableString(isSuperModule ? contract.SuperModule.GetValue(reward)?.ToString() : null),
            ["allowRepeatedAcquire"] = isSuperModule && contract.SuperModuleAllowRepeated.GetValue(reward) is bool repeated && repeated,
            ["money"] = money,
            ["source"] = ResultSource
        };

        return new RewardOptionSnapshot(
            item,
            index,
            item.GetInstanceID(),
            buttonActive,
            canAcquire,
            unavailableReason,
            state);
    }

    private static void DetermineAvailability(
        ReflectionContract contract,
        object? reward,
        bool isDisposable,
        bool isSuperModule,
        out bool canAcquire,
        out string unavailableReason)
    {
        canAcquire = true;
        unavailableReason = string.Empty;
        if (reward == null)
        {
            return;
        }

        try
        {
            if (isDisposable)
            {
                if (!TryGetRegisteredObject(contract.DisposableManagerInstance, out object? manager) ||
                    manager == null)
                {
                    canAcquire = false;
                    unavailableReason = "道具管理器尚未初始化。";
                    return;
                }

                object? disposableEnum = contract.PotionDisposable.GetValue(reward);
                if (disposableEnum == null)
                {
                    canAcquire = false;
                    unavailableReason = "道具奖励缺少枚举配置。";
                    return;
                }

                object?[] templateArguments = { disposableEnum, null };
                bool templateFound = contract.DisposableTryGetTemplate.Invoke(manager, templateArguments) is bool found && found;
                object? template = templateArguments[1];
                if (!templateFound || template == null)
                {
                    canAcquire = false;
                    unavailableReason = "游戏没有提供该道具的运行时模板。";
                    return;
                }

                canAcquire = contract.DisposableCanAdd.Invoke(manager, new[] { reward, template }) is bool allowed && allowed;
                if (!canAcquire)
                {
                    unavailableReason = "支援栏位已满，且该道具不能堆叠或立即使用。";
                }

                return;
            }

            if (isSuperModule &&
                contract.SuperModuleAllowRepeated.GetValue(reward) is bool allowRepeated &&
                !allowRepeated)
            {
                if (!TryGetRegisteredObject(contract.SuperModuleManagerInstance, out object? manager) || manager == null)
                {
                    canAcquire = false;
                    unavailableReason = "遗物管理器尚未初始化。";
                    return;
                }

                object? target = contract.SuperModule.GetValue(reward);
                if (contract.SuperModuleGetCurrentAll.Invoke(manager, null) is IEnumerable current)
                {
                    foreach (object? held in current)
                    {
                        if (Equals(held, target))
                        {
                            canAcquire = false;
                            unavailableReason = "该遗物已经持有，且不允许重复获取。";
                            return;
                        }
                    }
                }
            }
        }
        catch (Exception exception)
        {
            canAcquire = false;
            unavailableReason = "无法安全验证奖励可领取性：" + Unwrap(exception).Message;
        }
    }

    private static JObject? BuildFetterState(ReflectionContract contract, object? fetter)
    {
        if (fetter == null)
        {
            return null;
        }

        string fetterEnum = contract.FetterEnum.GetValue(fetter)?.ToString() ?? string.Empty;
        int level = contract.FetterLevel.GetValue(fetter) is int readLevel ? readLevel : 0;
        int count = contract.FetterCount.GetValue(fetter) is int readCount ? readCount : 0;
        return new JObject
        {
            ["fetterEnum"] = fetterEnum,
            ["level"] = level,
            ["count"] = count,
            ["isActual"] = count > 0 && !string.Equals(fetterEnum, "None", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static JArray BuildRewardObjects(
        ReflectionContract contract,
        out int pooledRewardObjectCount,
        out bool spawnerAvailable)
    {
        JArray activeObjects = new();
        int observedObjects = 0;
        if (!TryGetRegisteredObject(contract.SpawnerInstance, out object? spawner) ||
            spawner == null ||
            contract.SpawnerRewardObjects.GetValue(spawner) is not IEnumerable rewardObjects)
        {
            pooledRewardObjectCount = 0;
            spawnerAvailable = false;
            return activeObjects;
        }

        spawnerAvailable = true;

        foreach (object? candidate in rewardObjects)
        {
            if (candidate is not GameObject gameObject || gameObject == null)
            {
                continue;
            }

            observedObjects++;
            Component? rewardObject = gameObject.GetComponent(contract.RewardObjectType);
            if (rewardObject == null || !gameObject.scene.IsValid() || !gameObject.activeInHierarchy)
            {
                continue;
            }

            bool appearanceReady = IsAppearanceReady(gameObject, out string appearanceState, out float normalizedTime);

            activeObjects.Add(new JObject
            {
                ["index"] = activeObjects.Count,
                ["instanceId"] = rewardObject.GetInstanceID(),
                ["name"] = rewardObject.name,
                ["type"] = rewardObject.GetType().Name,
                ["active"] = true,
                ["appearanceReady"] = appearanceReady,
                ["appearanceState"] = appearanceState,
                ["appearanceNormalizedTime"] = normalizedTime,
                ["source"] = ResultSource
            });
        }

        pooledRewardObjectCount = Math.Max(0, observedObjects - activeObjects.Count);
        return activeObjects;
    }

    private static bool IsAppearanceReady(
        GameObject gameObject,
        out string state,
        out float normalizedTime)
    {
        state = "noAnimator";
        normalizedTime = 1f;
        Animator? animator = gameObject.GetComponent<Animator>() ??
                             gameObject.GetComponentInChildren<Animator>(true);
        if (animator == null || !animator.isActiveAndEnabled || animator.layerCount == 0)
        {
            return true;
        }

        if (animator.IsInTransition(0))
        {
            state = "transition";
            normalizedTime = 0f;
            return false;
        }

        AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
        normalizedTime = current.normalizedTime;
        if (current.IsName("fall"))
        {
            state = "fall";
            return normalizedTime >= 1f;
        }

        if (current.IsName("open"))
        {
            state = "open";
            return normalizedTime >= 1f;
        }

        if (current.IsName("idle") || current.IsName("Idle"))
        {
            state = "idle";
            return true;
        }

        state = current.loop ? "loop" : current.length <= 0f ? "empty" : "nonLoop";
        return current.loop || current.length <= 0f || normalizedTime >= 1f;
    }

    private static bool ReadMapRewardVehicleGetFetter(ReflectionContract contract)
    {
        try
        {
            if (!TryGetRegisteredObject(contract.MapConfigInstance, out object? controller) || controller == null)
            {
                return false;
            }

            object? config = contract.MapConfigCurrent.GetValue(controller, null);
            return config != null && contract.MapRewardVehicleGetFetter.GetValue(config) is bool enabled && enabled;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildPhaseToken(int panelInstanceId, IEnumerable<RewardOptionSnapshot> options) => string.Join(
        ":",
        panelInstanceId,
        string.Join(",", options.Select(option => option.InstanceId)));

    private static bool TryQueryLevelState(out JObject result)
    {
        result = null!;
        if (!TryGetLevelContract(out LevelReflectionContract contract))
        {
            return false;
        }

        // 新版游戏的面板在首次打开前可能尚未创建：此时旧奖励队列契约也已不存在，
        // 如实返回“面板关闭”状态，避免让调用方误以为轻量奖励契约不可用。
        if (!TryGetLevelPanel(contract, out object? panel) || panel == null)
        {
            result = Success("已读取轻量奖励界面状态。", BuildLevelClosedState());
            return true;
        }

        if (!IsLevelCollectionActive(contract, panel))
        {
            return false;
        }

        try
        {
            RewardSnapshot snapshot = BuildLevelSnapshot(contract, panel);
            result = Success("已读取轻量奖励界面状态。", snapshot.State);
            return true;
        }
        catch (Exception exception)
        {
            result = Error(
                "读取奖励界面失败：" + Unwrap(exception).Message,
                new JObject { ["source"] = ResultSource });
            return true;
        }
    }

    private static JObject BuildLevelClosedState() => new()
    {
        ["panelOpen"] = false,
        ["panelActive"] = false,
        ["panelAvailable"] = false,
        ["panelInstanceId"] = 0,
        ["phaseToken"] = string.Empty,
        ["busy"] = false,
        ["mutexAvailable"] = false,
        ["spawnerAvailable"] = false,
        ["pending"] = false,
        ["needsPolling"] = false,
        ["activeRewardObjectCount"] = 0,
        ["pooledRewardObjectCount"] = 0,
        ["rewardObjects"] = new JArray(),
        ["options"] = new JArray(),
        ["source"] = ResultSource,
        ["refresh"] = false,
        ["finished"] = false,
        ["levelCollection"] = true
    };

    private static bool TryChooseLevelOption(JObject? arguments, out JObject result)
    {
        result = null!;
        if (!TryGetLevelContract(out LevelReflectionContract contract) ||
            !TryGetLevelPanel(contract, out object? panel) || panel == null ||
            !IsLevelCollectionActive(contract, panel))
        {
            return false;
        }

        RewardSnapshot before;
        try
        {
            before = BuildLevelSnapshot(contract, panel);
        }
        catch (Exception exception)
        {
            result = Error(
                "选择奖励前无法读取界面状态：" + Unwrap(exception).Message,
                new JObject { ["source"] = ResultSource });
            return true;
        }

        if (!TryReadSelection(arguments, out string phaseToken, out int itemInstanceId, out int index))
        {
            result = SelectionError(
                "奖励选择参数不完整；必须同时提供 phaseToken、itemInstanceId 和 index。",
                before.State);
            return true;
        }

        if (!before.PanelOpen)
        {
            result = SelectionError("奖励面板已经关闭。", before.State);
            return true;
        }

        if (!string.Equals(before.PhaseToken, phaseToken, StringComparison.Ordinal))
        {
            result = SelectionError("奖励阶段已变化，拒绝使用过期选项。", before.State);
            return true;
        }

        RewardOptionSnapshot? target = before.Options.FirstOrDefault(option =>
            option.Index == index && option.InstanceId == itemInstanceId);
        if (target == null)
        {
            result = SelectionError("奖励选项实例与索引不匹配。", before.State);
            return true;
        }

        if (target.LevelIcon == null || target.LevelCard == null)
        {
            result = SelectionError("奖励卡片尚未绑定，本次未执行领取。", before.State);
            return true;
        }

        if (!target.ButtonActive)
        {
            result = SelectionError("指定奖励选项当前不可点击。", before.State);
            return true;
        }

        if (!target.CanAcquire)
        {
            string reason = string.IsNullOrWhiteSpace(target.UnavailableReason)
                ? "游戏当前不允许领取该奖励。"
                : target.UnavailableReason;
            result = SelectionError("指定奖励当前不可领取：" + reason, before.State);
            return true;
        }

        bool invocationStarted = false;
        try
        {
            invocationStarted = true;
            contract.CardClaim.Invoke(target.LevelCard, null);

            RewardSnapshot after = BuildLevelSnapshot(contract, panel);
            after.State["invocationStarted"] = true;
            after.State["selectedPhaseToken"] = phaseToken;
            after.State["selectedItemInstanceId"] = itemInstanceId;
            after.State["selectedIndex"] = index;
            result = Success("已发送一次奖励领取，正在等待奖励界面刷新。", after.State);
            return true;
        }
        catch (Exception exception)
        {
            JObject failureState = before.State;
            failureState["source"] = ResultSource;
            if (invocationStarted)
            {
                failureState["outcomeUnknown"] = true;
                failureState["needsReconciliation"] = true;
                failureState["uncertaintyOrigin"] = "rewardClaimException";
                failureState["invocationStarted"] = true;
            }

            result = Error(
                "奖励领取调用失败：" + Unwrap(exception).Message,
                failureState);
            return true;
        }
    }

    private static bool TryGetLevelPanel(LevelReflectionContract contract, out object? panel)
    {
        try
        {
            panel = contract.PanelInstance.GetValue(null, null);
        }
        catch
        {
            panel = null;
        }

        return panel != null;
    }

    private static bool IsLevelCollectionActive(LevelReflectionContract contract, object panel)
    {
        try
        {
            if (contract.IsShowingLevelRewards != null &&
                contract.IsShowingLevelRewards.GetValue(panel, null) is bool showing && showing)
            {
                return true;
            }

            return ReadLevelCards(contract, panel).Any(card => contract.CardData.GetValue(card, null) != null);
        }
        catch
        {
            return false;
        }
    }

    private static RewardSnapshot BuildLevelSnapshot(LevelReflectionContract contract, object panel)
    {
        bool panelOpen = IsOpenLevelPanel(contract, panel);
        bool panelActive = panelOpen && contract.PanelIsActive.GetValue(panel, null) is bool active && active;
        bool claiming = panelOpen &&
                        contract.ClaimingLevelRewards?.GetValue(panel) is bool claimInProgress &&
                        claimInProgress;
        bool refreshRequested = panelOpen &&
                                contract.RefreshRequested?.GetValue(panel) is bool refresh &&
                                refresh;
        List<RewardOptionSnapshot> options = panelOpen
            ? GetLevelRewardOptions(contract, panel)
            : new List<RewardOptionSnapshot>();
        string phaseToken = panelOpen ? BuildLevelPhaseToken(panel, options) : string.Empty;
        bool optionsPending = panelOpen && options.Count == 0;
        JObject state = new()
        {
            ["panelOpen"] = panelOpen,
            ["panelActive"] = panelActive,
            ["panelAvailable"] = true,
            ["panelInstanceId"] = EnsureNonZeroIdentity(GetObjectIdentity(panel)),
            ["phaseToken"] = phaseToken,
            ["busy"] = claiming,
            ["mutexAvailable"] = true,
            ["spawnerAvailable"] = false,
            ["pending"] = optionsPending,
            ["needsPolling"] = optionsPending,
            ["activeRewardObjectCount"] = 0,
            ["pooledRewardObjectCount"] = 0,
            ["rewardObjects"] = new JArray(),
            ["options"] = new JArray(options.Select(option => option.State)),
            ["source"] = ResultSource,
            ["refresh"] = refreshRequested,
            ["finished"] = false,
            ["levelCollection"] = true
        };

        return new RewardSnapshot(state, panelOpen, phaseToken, true, claiming, options);
    }

    private static bool IsOpenLevelPanel(LevelReflectionContract contract, object panel)
    {
        if (!IsActiveSceneObject(panel))
        {
            return false;
        }

        return contract.PanelIsOpen.Invoke(panel, null) is bool open && open;
    }

    private static List<RewardOptionSnapshot> GetLevelRewardOptions(
        LevelReflectionContract contract,
        object panel)
    {
        List<RewardOptionSnapshot> options = new();
        object? displayed = contract.DisplayedRewards.GetValue(panel, null);
        if (displayed is not IEnumerable icons)
        {
            return options;
        }

        List<object> cards = ReadLevelCards(contract, panel);
        int index = 0;
        foreach (object? icon in icons)
        {
            if (icon == null)
            {
                continue;
            }

            object? card = FindLevelCard(contract, cards, icon);
            options.Add(BuildLevelRewardOption(contract, icon, card, index));
            index++;
        }

        return options;
    }

    private static List<object> ReadLevelCards(LevelReflectionContract contract, object panel)
    {
        List<object> cards = new();
        if (contract.PanelCreator == null || contract.CreatorComponents == null)
        {
            return cards;
        }

        object? creator = contract.PanelCreator.GetValue(panel);
        if (creator == null || contract.CreatorComponents.GetValue(creator, null) is not IEnumerable components)
        {
            return cards;
        }

        foreach (object? card in components)
        {
            if (card == null)
            {
                continue;
            }

            if (!IsActiveSceneObject(card))
            {
                continue;
            }

            cards.Add(card);
        }

        return cards;
    }

    private static object? FindLevelCard(
        LevelReflectionContract contract,
        List<object> cards,
        object icon)
    {
        foreach (object card in cards)
        {
            if (ReferenceEquals(contract.CardData.GetValue(card, null), icon))
            {
                return card;
            }
        }

        object? settlementId = contract.IconSettlementId.GetValue(icon);
        object? itemIndex = contract.IconItemIndex.GetValue(icon);
        object? layerMoney = contract.IconLayerMoney.GetValue(icon);
        foreach (object card in cards)
        {
            object? data = contract.CardData.GetValue(card, null);
            if (data == null)
            {
                continue;
            }

            if (!Equals(contract.IconSettlementId.GetValue(data), settlementId) ||
                !Equals(contract.IconItemIndex.GetValue(data), itemIndex) ||
                !Equals(contract.IconLayerMoney.GetValue(data), layerMoney))
            {
                continue;
            }

            return card;
        }

        return null;
    }

    private static RewardOptionSnapshot BuildLevelRewardOption(
        LevelReflectionContract contract,
        object icon,
        object? card,
        int index)
    {
        bool granted = contract.IconGranted.GetValue(icon) is bool grantedValue && grantedValue;
        bool canClaim = contract.IconCanClaim.GetValue(icon) is bool canClaimValue && canClaimValue;
        bool buttonActive = card != null && canClaim && !granted;
        bool canAcquire = buttonActive;
        string unavailableReason = card == null
            ? "奖励卡片尚未绑定。"
            : granted
                ? "该奖励已经领取。"
                : canClaim
                    ? string.Empty
                    : "游戏当前不允许领取该奖励。";
        string rewardType = contract.IconItemType.GetValue(icon)?.ToString() ?? "Unknown";
        string rewardKind = rewardType switch
        {
            "Vehicle" => "vehicle",
            "Disposable" => "disposable",
            "SuperModule" => "superModule",
            "Money" => "money",
            _ => "unknown"
        };
        object? settlementItem = ReadLevelSettlementField(contract.IconItem, icon);
        int money = rewardKind == "money" &&
                    contract.IconQuantity?.GetValue(icon) is string quantityText &&
                    int.TryParse(quantityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMoney)
            ? parsedMoney
            : 0;
        object? fetter = rewardKind == "vehicle"
            ? ReadLevelSettlementField(contract.SettlementFetter, settlementItem)
            : null;
        JObject? fetterState = BuildLevelFetterState(contract, fetter);
        bool willCarryInitFetter = rewardKind == "vehicle" &&
                                   fetterState?["isActual"]?.Value<bool>() == true;
        JArray effectiveFetters = new();
        if (willCarryInitFetter && fetterState != null)
        {
            effectiveFetters.Add(fetterState.DeepClone());
        }

        int instanceId = card == null
            ? BuildLevelSyntheticInstanceId(contract, icon)
            : EnsureNonZeroIdentity(GetObjectIdentity(card));
        string? fallbackName = contract.IconFallbackName.GetValue(icon) as string;
        string? sourceLabel = contract.IconSourceLabel?.GetValue(icon) as string;
        JObject state = new()
        {
            ["index"] = index,
            ["instanceId"] = instanceId,
            ["type"] = card == null ? LevelRewardCollectionIconTypeName : card.GetType().Name,
            ["buttonActive"] = buttonActive,
            ["canAcquire"] = canAcquire,
            ["unavailableReason"] = NullableString(unavailableReason),
            ["rewardKind"] = rewardKind,
            ["rewardType"] = NullableString(rewardType),
            ["rewardName"] = NullableString(fallbackName),
            ["rewardRare"] = JValue.CreateNull(),
            ["vehicleType"] = NullableString(rewardKind == "vehicle"
                ? ReadLevelSettlementField(contract.SettlementVehicleType, settlementItem)?.ToString()
                : null),
            ["initFetter"] = NullableString(fetterState?["fetterEnum"]?.Value<string>()),
            ["templateInitFetter"] = (JToken?)fetterState ?? JValue.CreateNull(),
            ["carryInitFetter"] = rewardKind == "vehicle" ? (JToken)true : JValue.CreateNull(),
            ["mapRewardVehicleGetFetter"] = rewardKind == "vehicle" ? (JToken)true : JValue.CreateNull(),
            ["willCarryInitFetter"] = rewardKind == "vehicle" ? (JToken)willCarryInitFetter : JValue.CreateNull(),
            ["effectiveInitFetter"] = NullableString(willCarryInitFetter
                ? fetterState?["fetterEnum"]?.Value<string>()
                : null),
            ["effectiveFetters"] = effectiveFetters,
            ["disposableEnum"] = NullableString(rewardKind == "disposable"
                ? ReadLevelSettlementField(contract.SettlementDisposable, settlementItem)?.ToString()
                : null),
            ["assignDisposableEnum"] = JValue.CreateNull(),
            ["superModuleEnum"] = NullableString(rewardKind == "superModule"
                ? ReadLevelSettlementField(contract.SettlementSuperModule, settlementItem)?.ToString()
                : null),
            ["allowRepeatedAcquire"] = rewardKind == "superModule" &&
                                       ReadLevelSettlementField(contract.SettlementAllowRepeated, settlementItem) is bool repeated &&
                                       repeated,
            ["money"] = money,
            ["granted"] = granted,
            ["nativeType"] = LevelRewardCollectionItemTypeName,
            ["path"] = JValue.CreateNull(),
            ["sourceLabel"] = NullableString(sourceLabel),
            ["source"] = ResultSource
        };

        return new RewardOptionSnapshot(
            card ?? icon,
            index,
            instanceId,
            buttonActive,
            canAcquire,
            unavailableReason,
            state,
            icon,
            card);
    }

    private static object? ReadLevelSettlementField(FieldInfo? field, object? settlementItem)
    {
        if (field == null || settlementItem == null)
        {
            return null;
        }

        try
        {
            return field.GetValue(settlementItem);
        }
        catch
        {
            return null;
        }
    }

    private static JObject? BuildLevelFetterState(LevelReflectionContract contract, object? fetter)
    {
        if (fetter == null || contract.FetterEnum == null || contract.FetterLevel == null ||
            contract.FetterCount == null)
        {
            return null;
        }

        string fetterEnum = contract.FetterEnum.GetValue(fetter)?.ToString() ?? string.Empty;
        int level = contract.FetterLevel.GetValue(fetter) is int readLevel ? readLevel : 0;
        int count = contract.FetterCount.GetValue(fetter) is int readCount ? readCount : 0;
        return new JObject
        {
            ["fetterEnum"] = fetterEnum,
            ["level"] = level,
            ["count"] = count,
            ["isActual"] = count > 0 && !string.Equals(fetterEnum, "None", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static int BuildLevelSyntheticInstanceId(LevelReflectionContract contract, object icon)
    {
        string settlementId = contract.IconSettlementId.GetValue(icon) as string ?? string.Empty;
        int itemIndex = contract.IconItemIndex.GetValue(icon) is int readIndex ? readIndex : -1;
        bool layerMoney = contract.IconLayerMoney.GetValue(icon) is bool readLayerMoney && readLayerMoney;
        unchecked
        {
            int hash = (int)2166136261;
            foreach (char character in settlementId)
            {
                hash = (hash ^ character) * 16777619;
            }

            hash = (hash ^ itemIndex) * 16777619;
            hash = (hash ^ (layerMoney ? 1 : 0)) * 16777619;
            return hash == 0 ? 1 : hash;
        }
    }

    private static int GetObjectIdentity(object? target)
    {
        if (target == null)
        {
            return 0;
        }

        // 用反射读取 Unity 对象的实例 ID：轻量奖励契约需要在没有 UnityEngine 程序集的测试宿主中也能运行。
        MethodInfo? getInstanceId = target.GetType().GetMethod(
            "GetInstanceID",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            Type.EmptyTypes,
            null);
        if (getInstanceId != null && getInstanceId.ReturnType == typeof(int))
        {
            try
            {
                if (getInstanceId.Invoke(target, null) is int instanceId)
                {
                    return instanceId;
                }
            }
            catch
            {
                return 0;
            }
        }

        return RuntimeHelpers.GetHashCode(target);
    }

    private static bool IsActiveSceneObject(object? target)
    {
        if (target == null)
        {
            return false;
        }

        // 真实游戏对象（Component）通过反射检查激活状态；测试替身没有 gameObject 成员时视为可用。
        PropertyInfo? gameObjectProperty = FindInstanceProperty(target.GetType(), "gameObject");
        if (gameObjectProperty == null || gameObjectProperty.PropertyType.FullName != "UnityEngine.GameObject")
        {
            return true;
        }

        object? gameObject = gameObjectProperty.GetValue(target, null);
        if (gameObject == null)
        {
            return false;
        }

        PropertyInfo? activeInHierarchy = FindInstanceProperty(gameObject.GetType(), "activeInHierarchy");
        if (activeInHierarchy?.GetValue(gameObject, null) is bool active && !active)
        {
            return false;
        }

        PropertyInfo? sceneProperty = FindInstanceProperty(gameObject.GetType(), "scene");
        object? scene = sceneProperty?.GetValue(gameObject, null);
        if (scene != null &&
            FindInstanceMethod(scene.GetType(), "IsValid") is MethodInfo isValid &&
            isValid.Invoke(scene, null) is bool valid &&
            !valid)
        {
            return false;
        }

        return true;
    }

    private static int EnsureNonZeroIdentity(int identity) => identity == 0 ? 1 : identity;

    private static string BuildLevelPhaseToken(object panel, IEnumerable<RewardOptionSnapshot> options) => string.Join(
        ":",
        EnsureNonZeroIdentity(GetObjectIdentity(panel)),
        string.Join(",", options.Select(option => option.InstanceId)));

    private static bool IsOpenPanel(ReflectionContract contract, Component? panel)
    {
        if (panel == null || panel.gameObject == null || !panel.gameObject.scene.IsValid() ||
            !panel.gameObject.activeInHierarchy)
        {
            return false;
        }

        return contract.PanelIsOpen.Invoke(panel, null) is bool open && open;
    }

    private static Component? TryGetRegisteredComponent(PropertyInfo instance)
    {
        try
        {
            return instance.GetValue(null, null) as Component;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetRegisteredObject(PropertyInfo instance, out object? value)
    {
        try
        {
            value = instance.GetValue(null, null);
            return value != null;
        }
        catch
        {
            value = null;
            return false;
        }
    }

    private static bool TryGetRegisteredObject(FieldInfo instance, out object? value)
    {
        try
        {
            value = instance.GetValue(null);
            return value != null;
        }
        catch
        {
            value = null;
            return false;
        }
    }

    private static bool TryReadSelection(
        JObject? arguments,
        out string phaseToken,
        out int itemInstanceId,
        out int index)
    {
        phaseToken = arguments?["phaseToken"]?.Type == JTokenType.String
            ? arguments["phaseToken"]!.Value<string>() ?? string.Empty
            : string.Empty;
        itemInstanceId = arguments?["itemInstanceId"]?.Type == JTokenType.Integer
            ? arguments["itemInstanceId"]!.Value<int>()
            : 0;
        index = arguments?["index"]?.Type == JTokenType.Integer
            ? arguments["index"]!.Value<int>()
            : -1;
        return !string.IsNullOrWhiteSpace(phaseToken) && itemInstanceId != 0 && index >= 0;
    }

    private static bool TryReadRewardObjectIdentity(JObject? arguments, out int instanceId)
    {
        instanceId = 0;
        JToken? identity = arguments?["instanceId"];
        if (identity?.Type != JTokenType.Integer)
        {
            return false;
        }

        try
        {
            instanceId = identity.Value<int>();
            return instanceId != 0;
        }
        catch
        {
            instanceId = 0;
            return false;
        }
    }

    private static bool TryGetContract(out ReflectionContract contract)
    {
        if (_contract != null)
        {
            contract = _contract;
            return true;
        }

        Type? panelType = FindType(RewardPanelTypeName);
        Type? itemType = FindType(RewardItemTypeName);
        Type? mutexType = FindType(RewardMutexTypeName);
        Type? spawnerType = FindType(RewardSpawnerTypeName);
        Type? rewardObjectType = FindType(RewardObjectTypeName);
        Type? rewardType = FindType(RewardTypeName);
        Type? razorRewardType = FindType(RazorRewardTypeName);
        Type? potionRewardType = FindType(PotionRewardTypeName);
        Type? superModuleRewardType = FindType(SuperModuleRewardTypeName);
        Type? disposableManagerType = FindType(DisposableManagerTypeName);
        Type? superModuleManagerType = FindType(SuperModuleManagerTypeName);
        Type? mapConfigControllerType = FindType(MapConfigControllerTypeName);

        PropertyInfo? panelInstance = FindStaticProperty(panelType, "Instance");
        MethodInfo? panelIsOpen = FindInstanceMethod(panelType, "IsOpen");
        PropertyInfo? panelIsActive = FindInstanceProperty(panelType, "IsActive");
        FieldInfo? panelRewardContent = FindInstanceField(panelType, "m_rewardContent");

        FieldInfo? itemReward = FindInstanceField(itemType, "m_reward");
        FieldInfo? itemMoney = FindInstanceField(itemType, "m_money");
        PropertyInfo? itemButton = FindInstanceProperty(itemType, "Btn");
        PropertyInfo? buttonActive = FindInstanceProperty(itemButton?.PropertyType, "BtnActive");
        MethodInfo? clickEvent = FindInstanceMethod(itemType, "ClickEvent");

        FieldInfo? mutexInstance = FindStaticField(mutexType, "m_instance");
        PropertyInfo? mutexBusy = FindInstanceProperty(mutexType, "isInUsing");
        PropertyInfo? spawnerInstance = FindStaticProperty(spawnerType, "Instance");
        FieldInfo? spawnerRewardObjects = FindInstanceField(spawnerType, "m_rewardObjects");
        PropertyInfo? rewardObjectButton = FindInstanceProperty(rewardObjectType, "Btn");
        Type? rewardButtonType = rewardObjectButton?.PropertyType;
        MethodInfo? rewardButtonPointEnter = FindInstanceMethod(rewardButtonType, "PointEnter");
        MethodInfo? rewardButtonLeftPointDown = FindInstanceMethod(rewardButtonType, "LeftPointDown");
        MethodInfo? rewardButtonLeftPointUp = FindInstanceMethod(rewardButtonType, "LeftPointUp");

        FieldInfo? rewardRare = FindInstanceField(rewardType, "rewardRare");
        FieldInfo? razorVehicleType = FindInstanceField(razorRewardType, "vehicleType");
        FieldInfo? razorInitFetter = FindInstanceField(razorRewardType, "initFetterModuleData");
        FieldInfo? razorCarryInitFetter = FindInstanceField(razorRewardType, "carryInitFetter");
        FieldInfo? fetterEnum = FindInstanceField(razorInitFetter?.FieldType, "fetterEnum");
        FieldInfo? fetterLevel = FindInstanceField(razorInitFetter?.FieldType, "level");
        FieldInfo? fetterCount = FindInstanceField(razorInitFetter?.FieldType, "count");
        FieldInfo? potionDisposable = FindInstanceField(potionRewardType, "disposableEnum");
        FieldInfo? potionAssignedDisposable = FindInstanceField(potionRewardType, "assignDisposableEnum");
        FieldInfo? superModule = FindInstanceField(superModuleRewardType, "superModuleEnum");
        FieldInfo? superModuleAllowRepeated = FindInstanceField(superModuleRewardType, "allowRepeatedAcquire");
        PropertyInfo? disposableManagerInstance = FindStaticProperty(disposableManagerType, "Instance");
        MethodInfo? disposableTryGetTemplate = FindInstanceMethod(disposableManagerType, "TryGetDisposableTemplate", 2);
        MethodInfo? disposableCanAdd = FindInstanceMethod(disposableManagerType, "CanAdd", 2);
        PropertyInfo? superModuleManagerInstance = FindStaticProperty(superModuleManagerType, "Instance");
        MethodInfo? superModuleGetCurrentAll = FindInstanceMethod(superModuleManagerType, "GetCurrentAll");

        PropertyInfo? mapConfigInstance = FindStaticProperty(mapConfigControllerType, "Instance");
        PropertyInfo? mapConfigCurrent = FindInstanceProperty(mapConfigControllerType, "CurrentCfg");
        FieldInfo? mapRewardVehicleGetFetter = FindInstanceField(mapConfigCurrent?.PropertyType, "rewardVehicleGetFetter");

        if (panelType == null || itemType == null || mutexType == null || spawnerType == null ||
            rewardObjectType == null || rewardType == null || razorRewardType == null ||
            potionRewardType == null || superModuleRewardType == null ||
            panelInstance == null || panelIsOpen == null || panelIsActive == null ||
            panelRewardContent == null ||
            itemReward == null || itemMoney == null || itemButton == null || buttonActive == null ||
            clickEvent == null || mutexInstance == null || mutexBusy == null ||
            spawnerInstance == null || spawnerRewardObjects == null || rewardRare == null ||
            razorVehicleType == null || razorInitFetter == null || razorCarryInitFetter == null ||
            fetterEnum == null || fetterLevel == null || fetterCount == null ||
            potionDisposable == null || potionAssignedDisposable == null || superModule == null ||
            superModuleAllowRepeated == null || disposableManagerInstance == null ||
            disposableTryGetTemplate == null || disposableCanAdd == null ||
            superModuleManagerInstance == null || superModuleGetCurrentAll == null ||
            mapConfigInstance == null || mapConfigCurrent == null ||
            mapRewardVehicleGetFetter == null)
        {
            contract = null!;
            return false;
        }

        _contract = new ReflectionContract(
            panelInstance, panelIsOpen, panelIsActive, panelRewardContent,
            itemType, itemReward, itemMoney, itemButton, buttonActive, clickEvent,
            mutexInstance, mutexBusy, spawnerInstance, spawnerRewardObjects, rewardObjectType,
            rewardObjectButton, rewardButtonPointEnter, rewardButtonLeftPointDown, rewardButtonLeftPointUp,
            rewardRare, razorRewardType, razorVehicleType, razorInitFetter, razorCarryInitFetter,
            fetterEnum, fetterLevel, fetterCount, potionRewardType, potionDisposable,
            potionAssignedDisposable, superModuleRewardType, superModule, superModuleAllowRepeated,
            disposableManagerInstance, disposableTryGetTemplate, disposableCanAdd,
            superModuleManagerInstance, superModuleGetCurrentAll,
            mapConfigInstance, mapConfigCurrent, mapRewardVehicleGetFetter);
        contract = _contract;
        return true;
    }

    private static bool TryGetLevelContract(out LevelReflectionContract contract)
    {
        if (_levelContract != null)
        {
            contract = _levelContract;
            return true;
        }

        Type? panelType = FindType(RewardPanelTypeName);
        Type? iconType = FindType(LevelRewardCollectionIconTypeName);
        Type? cardType = FindType(LevelRewardCollectionItemTypeName);

        PropertyInfo? panelInstance = FindStaticProperty(panelType, "Instance");
        MethodInfo? panelIsOpen = FindInstanceMethod(panelType, "IsOpen");
        PropertyInfo? panelIsActive = FindInstanceProperty(panelType, "IsActive");
        PropertyInfo? displayedRewards = FindInstanceProperty(panelType, "DisplayedRewards");
        PropertyInfo? isShowingLevelRewards = FindInstanceProperty(panelType, "IsShowingLevelRewards");
        FieldInfo? panelCreator = FindInstanceField(panelType, "m_creator");
        PropertyInfo? creatorComponents = FindInstanceProperty(panelCreator?.FieldType, "Components");
        FieldInfo? claimingLevelRewards = FindInstanceField(panelType, "m_claimingLevelRewards");
        FieldInfo? refreshRequested = FindInstanceField(panelType, "m_refreshRequested");

        FieldInfo? iconSettlementId = FindInstanceField(iconType, "settlementId");
        FieldInfo? iconItemIndex = FindInstanceField(iconType, "itemIndex");
        FieldInfo? iconItemType = FindInstanceField(iconType, "itemType");
        FieldInfo? iconLayerMoney = FindInstanceField(iconType, "layerMoney");
        FieldInfo? iconGranted = FindInstanceField(iconType, "granted");
        FieldInfo? iconCanClaim = FindInstanceField(iconType, "canClaim");
        FieldInfo? iconFallbackName = FindInstanceField(iconType, "fallbackName");
        FieldInfo? iconQuantity = FindInstanceField(iconType, "quantity");
        FieldInfo? iconSourceLabel = FindInstanceField(iconType, "sourceLabel");
        FieldInfo? iconItem = FindInstanceField(iconType, "item");
        PropertyInfo? cardData = FindInstanceProperty(cardType, "Data");
        MethodInfo? cardClaim = FindInstanceMethod(cardType, "Claim");

        Type? settlementItemType = iconItem?.FieldType;
        FieldInfo? settlementVehicleType = FindInstanceField(settlementItemType, "vehicleType");
        FieldInfo? settlementDisposable = FindInstanceField(settlementItemType, "disposableEnum");
        FieldInfo? settlementSuperModule = FindInstanceField(settlementItemType, "superModuleEnum");
        FieldInfo? settlementFetter = FindInstanceField(settlementItemType, "fetter");
        FieldInfo? settlementAllowRepeated = FindInstanceField(settlementItemType, "allowRepeatedAcquire");
        FieldInfo? fetterEnum = FindInstanceField(settlementFetter?.FieldType, "fetterEnum");
        FieldInfo? fetterLevel = FindInstanceField(settlementFetter?.FieldType, "level");
        FieldInfo? fetterCount = FindInstanceField(settlementFetter?.FieldType, "count");

        if (panelType == null || iconType == null || cardType == null ||
            panelInstance == null || panelIsOpen == null || panelIsActive == null ||
            displayedRewards == null ||
            iconSettlementId == null || iconItemIndex == null || iconItemType == null ||
            iconLayerMoney == null || iconGranted == null || iconCanClaim == null ||
            iconFallbackName == null ||
            cardData == null || cardClaim == null)
        {
            contract = null!;
            return false;
        }

        _levelContract = new LevelReflectionContract
        {
            PanelInstance = panelInstance,
            PanelIsOpen = panelIsOpen,
            PanelIsActive = panelIsActive,
            DisplayedRewards = displayedRewards,
            IsShowingLevelRewards = isShowingLevelRewards,
            PanelCreator = panelCreator,
            CreatorComponents = creatorComponents,
            ClaimingLevelRewards = claimingLevelRewards,
            RefreshRequested = refreshRequested,
            IconSettlementId = iconSettlementId,
            IconItemIndex = iconItemIndex,
            IconItemType = iconItemType,
            IconLayerMoney = iconLayerMoney,
            IconGranted = iconGranted,
            IconCanClaim = iconCanClaim,
            IconFallbackName = iconFallbackName,
            IconQuantity = iconQuantity,
            IconSourceLabel = iconSourceLabel,
            IconItem = iconItem,
            CardData = cardData,
            CardClaim = cardClaim,
            SettlementVehicleType = settlementVehicleType,
            SettlementDisposable = settlementDisposable,
            SettlementSuperModule = settlementSuperModule,
            SettlementFetter = settlementFetter,
            SettlementAllowRepeated = settlementAllowRepeated,
            FetterEnum = fetterEnum,
            FetterLevel = fetterLevel,
            FetterCount = fetterCount
        };
        contract = _levelContract;
        return true;
    }

    private static Type? FindType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType(fullName, false);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }

    private static PropertyInfo? FindStaticProperty(Type? type, string name)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        for (Type? current = type; current != null; current = current.BaseType)
        {
            PropertyInfo? property = current.GetProperty(name, Flags);
            if (property != null)
            {
                return property;
            }
        }

        return null;
    }

    private static FieldInfo? FindStaticField(Type? type, string name)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        for (Type? current = type; current != null; current = current.BaseType)
        {
            FieldInfo? field = current.GetField(name, Flags);
            if (field != null)
            {
                return field;
            }
        }

        return null;
    }

    private static PropertyInfo? FindInstanceProperty(Type? type, string name)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        for (Type? current = type; current != null; current = current.BaseType)
        {
            PropertyInfo? property = current.GetProperty(name, Flags);
            if (property != null)
            {
                return property;
            }
        }

        return null;
    }

    private static FieldInfo? FindInstanceField(Type? type, string name)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        for (Type? current = type; current != null; current = current.BaseType)
        {
            FieldInfo? field = current.GetField(name, Flags);
            if (field != null)
            {
                return field;
            }
        }

        return null;
    }

    private static MethodInfo? FindInstanceMethod(Type? type, string name)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        for (Type? current = type; current != null; current = current.BaseType)
        {
            MethodInfo? method = current.GetMethod(name, Flags, null, Type.EmptyTypes, null);
            if (method != null)
            {
                return method;
            }
        }

        return null;
    }

    private static MethodInfo? FindInstanceMethod(Type? type, string name, int parameterCount)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        for (Type? current = type; current != null; current = current.BaseType)
        {
            MethodInfo? method = current.GetMethods(Flags)
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, name, StringComparison.Ordinal) &&
                    candidate.GetParameters().Length == parameterCount);
            if (method != null)
            {
                return method;
            }
        }

        return null;
    }

    private static JToken NullableString(string? value) => value == null
        ? JValue.CreateNull()
        : new JValue(value);

    private static JObject Success(string message, JObject state) => new()
    {
        ["success"] = true,
        ["message"] = message,
        ["suggestion"] = JValue.CreateNull(),
        ["data"] = new JObject { ["state"] = state }
    };

    private static JObject SelectionError(string message, JObject state) => Error(message, state);

    private static JObject Error(string message, JObject state) => new()
    {
        ["success"] = false,
        ["message"] = message,
        ["suggestion"] = "\u8bf7\u91cd\u65b0\u67e5\u8be2\u5f53\u524d\u5956\u52b1\u72b6\u6001\uff0c\u4e0d\u8981\u91cd\u653e\u65e7\u9009\u9879\u3002",
        ["data"] = new JObject { ["state"] = state }
    };

    private static Exception Unwrap(Exception exception) =>
        exception is TargetInvocationException target && target.InnerException != null
            ? target.InnerException
            : exception;

    private sealed class RewardSnapshot
    {
        public RewardSnapshot(
            JObject state,
            bool panelOpen,
            string phaseToken,
            bool mutexAvailable,
            bool busy,
            List<RewardOptionSnapshot> options)
        {
            State = state;
            PanelOpen = panelOpen;
            PhaseToken = phaseToken;
            MutexAvailable = mutexAvailable;
            Busy = busy;
            Options = options;
        }

        public JObject State { get; }
        public bool PanelOpen { get; }
        public string PhaseToken { get; }
        public bool MutexAvailable { get; }
        public bool Busy { get; }
        public List<RewardOptionSnapshot> Options { get; }
    }

    private sealed class RewardOptionSnapshot
    {
        public RewardOptionSnapshot(
            object item,
            int index,
            int instanceId,
            bool buttonActive,
            bool canAcquire,
            string unavailableReason,
            JObject state,
            object? levelIcon = null,
            object? levelCard = null)
        {
            Item = item;
            Index = index;
            InstanceId = instanceId;
            ButtonActive = buttonActive;
            CanAcquire = canAcquire;
            UnavailableReason = unavailableReason;
            State = state;
            LevelIcon = levelIcon;
            LevelCard = levelCard;
        }

        public object Item { get; }
        public int Index { get; }
        public int InstanceId { get; }
        public bool ButtonActive { get; }
        public bool CanAcquire { get; }
        public string UnavailableReason { get; }
        public JObject State { get; }
        public object? LevelIcon { get; }
        public object? LevelCard { get; }
    }

    private sealed class RewardObjectCollectionSnapshot
    {
        public RewardObjectCollectionSnapshot(
            JObject state,
            bool spawnerAvailable,
            int matchCount,
            Component? target)
        {
            State = state;
            SpawnerAvailable = spawnerAvailable;
            MatchCount = matchCount;
            Target = target;
        }

        public JObject State { get; }
        public bool SpawnerAvailable { get; }
        public int MatchCount { get; }
        public Component? Target { get; }
    }

    private sealed class ReflectionContract
    {
        public ReflectionContract(
            PropertyInfo panelInstance,
            MethodInfo panelIsOpen,
            PropertyInfo panelIsActive,
            FieldInfo panelRewardContent,
            Type rewardItemType,
            FieldInfo itemReward,
            FieldInfo itemMoney,
            PropertyInfo itemButton,
            PropertyInfo buttonActive,
            MethodInfo clickEvent,
            FieldInfo mutexInstance,
            PropertyInfo mutexBusy,
            PropertyInfo spawnerInstance,
            FieldInfo spawnerRewardObjects,
            Type rewardObjectType,
            PropertyInfo? rewardObjectButton,
            MethodInfo? rewardButtonPointEnter,
            MethodInfo? rewardButtonLeftPointDown,
            MethodInfo? rewardButtonLeftPointUp,
            FieldInfo rewardRare,
            Type razorRewardType,
            FieldInfo razorVehicleType,
            FieldInfo razorInitFetter,
            FieldInfo razorCarryInitFetter,
            FieldInfo fetterEnum,
            FieldInfo fetterLevel,
            FieldInfo fetterCount,
            Type potionRewardType,
            FieldInfo potionDisposable,
            FieldInfo potionAssignedDisposable,
            Type superModuleRewardType,
            FieldInfo superModule,
            FieldInfo superModuleAllowRepeated,
            PropertyInfo disposableManagerInstance,
            MethodInfo disposableTryGetTemplate,
            MethodInfo disposableCanAdd,
            PropertyInfo superModuleManagerInstance,
            MethodInfo superModuleGetCurrentAll,
            PropertyInfo mapConfigInstance,
            PropertyInfo mapConfigCurrent,
            FieldInfo mapRewardVehicleGetFetter)
        {
            PanelInstance = panelInstance;
            PanelIsOpen = panelIsOpen;
            PanelIsActive = panelIsActive;
            PanelRewardContent = panelRewardContent;
            RewardItemType = rewardItemType;
            ItemReward = itemReward;
            ItemMoney = itemMoney;
            ItemButton = itemButton;
            ButtonActive = buttonActive;
            ClickEvent = clickEvent;
            MutexInstance = mutexInstance;
            MutexBusy = mutexBusy;
            SpawnerInstance = spawnerInstance;
            SpawnerRewardObjects = spawnerRewardObjects;
            RewardObjectType = rewardObjectType;
            RewardObjectButton = rewardObjectButton;
            RewardButtonPointEnter = rewardButtonPointEnter;
            RewardButtonLeftPointDown = rewardButtonLeftPointDown;
            RewardButtonLeftPointUp = rewardButtonLeftPointUp;
            RewardRare = rewardRare;
            RazorRewardType = razorRewardType;
            RazorVehicleType = razorVehicleType;
            RazorInitFetter = razorInitFetter;
            RazorCarryInitFetter = razorCarryInitFetter;
            FetterEnum = fetterEnum;
            FetterLevel = fetterLevel;
            FetterCount = fetterCount;
            PotionRewardType = potionRewardType;
            PotionDisposable = potionDisposable;
            PotionAssignedDisposable = potionAssignedDisposable;
            SuperModuleRewardType = superModuleRewardType;
            SuperModule = superModule;
            SuperModuleAllowRepeated = superModuleAllowRepeated;
            DisposableManagerInstance = disposableManagerInstance;
            DisposableTryGetTemplate = disposableTryGetTemplate;
            DisposableCanAdd = disposableCanAdd;
            SuperModuleManagerInstance = superModuleManagerInstance;
            SuperModuleGetCurrentAll = superModuleGetCurrentAll;
            MapConfigInstance = mapConfigInstance;
            MapConfigCurrent = mapConfigCurrent;
            MapRewardVehicleGetFetter = mapRewardVehicleGetFetter;
        }

        public PropertyInfo PanelInstance { get; }
        public MethodInfo PanelIsOpen { get; }
        public PropertyInfo PanelIsActive { get; }
        public FieldInfo PanelRewardContent { get; }
        public Type RewardItemType { get; }
        public FieldInfo ItemReward { get; }
        public FieldInfo ItemMoney { get; }
        public PropertyInfo ItemButton { get; }
        public PropertyInfo ButtonActive { get; }
        public MethodInfo ClickEvent { get; }
        public FieldInfo MutexInstance { get; }
        public PropertyInfo MutexBusy { get; }
        public PropertyInfo SpawnerInstance { get; }
        public FieldInfo SpawnerRewardObjects { get; }
        public Type RewardObjectType { get; }
        public PropertyInfo? RewardObjectButton { get; }
        public MethodInfo? RewardButtonPointEnter { get; }
        public MethodInfo? RewardButtonLeftPointDown { get; }
        public MethodInfo? RewardButtonLeftPointUp { get; }
        public FieldInfo RewardRare { get; }
        public Type RazorRewardType { get; }
        public FieldInfo RazorVehicleType { get; }
        public FieldInfo RazorInitFetter { get; }
        public FieldInfo RazorCarryInitFetter { get; }
        public FieldInfo FetterEnum { get; }
        public FieldInfo FetterLevel { get; }
        public FieldInfo FetterCount { get; }
        public Type PotionRewardType { get; }
        public FieldInfo PotionDisposable { get; }
        public FieldInfo PotionAssignedDisposable { get; }
        public Type SuperModuleRewardType { get; }
        public FieldInfo SuperModule { get; }
        public FieldInfo SuperModuleAllowRepeated { get; }
        public PropertyInfo DisposableManagerInstance { get; }
        public MethodInfo DisposableTryGetTemplate { get; }
        public MethodInfo DisposableCanAdd { get; }
        public PropertyInfo SuperModuleManagerInstance { get; }
        public MethodInfo SuperModuleGetCurrentAll { get; }
        public PropertyInfo MapConfigInstance { get; }
        public PropertyInfo MapConfigCurrent { get; }
        public FieldInfo MapRewardVehicleGetFetter { get; }
    }

    private sealed class LevelReflectionContract
    {
        public PropertyInfo PanelInstance = null!;
        public MethodInfo PanelIsOpen = null!;
        public PropertyInfo PanelIsActive = null!;
        public PropertyInfo DisplayedRewards = null!;
        public PropertyInfo? IsShowingLevelRewards;
        public FieldInfo? PanelCreator;
        public PropertyInfo? CreatorComponents;
        public FieldInfo? ClaimingLevelRewards;
        public FieldInfo? RefreshRequested;
        public FieldInfo IconSettlementId = null!;
        public FieldInfo IconItemIndex = null!;
        public FieldInfo IconItemType = null!;
        public FieldInfo IconLayerMoney = null!;
        public FieldInfo IconGranted = null!;
        public FieldInfo IconCanClaim = null!;
        public FieldInfo IconFallbackName = null!;
        public FieldInfo? IconQuantity;
        public FieldInfo? IconSourceLabel;
        public FieldInfo? IconItem;
        public PropertyInfo CardData = null!;
        public MethodInfo CardClaim = null!;
        public FieldInfo? SettlementVehicleType;
        public FieldInfo? SettlementDisposable;
        public FieldInfo? SettlementSuperModule;
        public FieldInfo? SettlementFetter;
        public FieldInfo? SettlementAllowRepeated;
        public FieldInfo? FetterEnum;
        public FieldInfo? FetterLevel;
        public FieldInfo? FetterCount;
    }
}
