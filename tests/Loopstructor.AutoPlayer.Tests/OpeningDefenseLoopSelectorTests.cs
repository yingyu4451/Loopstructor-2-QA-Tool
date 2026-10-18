using Loopstructor.AutoPlayer.Core;

namespace Loopstructor.AutoPlayer.Tests;

public sealed class OpeningDefenseLoopSelectorTests
{
    [Fact]
    public void BalancedThreeStationRing_IsSelectedWithoutWarning()
    {
        OpeningDefenseLoopSelection selection = OpeningDefenseLoopSelector.Select(new[]
        {
            Candidate(1, 10d, 0d, isAttribute: true),
            Candidate(2, -5d, 9d, isAttribute: false),
            Candidate(3, -5d, -9d, isAttribute: false)
        });

        Assert.True(selection.Selected);
        Assert.False(selection.Degraded);
        Assert.Equal(string.Empty, selection.Warning);
        Assert.Equal(3, selection.Plan!.OrderedPointInstanceIds.Count);
        Assert.True(RailLayoutStrategyPlanner.IsBalancedDefenseRing(selection.Plan.Score));
    }

    [Fact]
    public void OutlierStation_IsLeftUnconnectedSoTheBalancedRingSurvives()
    {
        OpeningDefenseLoopSelection selection = OpeningDefenseLoopSelector.Select(new[]
        {
            Candidate(1, 10d, 0d, isAttribute: true),
            Candidate(2, -5d, 9d, isAttribute: false),
            Candidate(3, -5d, -9d, isAttribute: false),
            Candidate(4, 70d, 0d, isAttribute: false)
        });

        Assert.True(selection.Selected);
        Assert.False(selection.Degraded);
        Assert.DoesNotContain(4, selection.Plan!.OrderedPointInstanceIds);
        Assert.Equal(3, selection.Plan.OrderedPointInstanceIds.Count);
        Assert.True(RailLayoutStrategyPlanner.IsBalancedDefenseRing(selection.Plan.Score));
        Assert.Contains("未纳入的站点", selection.Warning);
    }

    [Fact]
    public void DegenerateButEncirclingLayout_IsAcceptedWithAnHonestWarning()
    {
        OpeningDefenseLoopSelection selection = OpeningDefenseLoopSelector.Select(new[]
        {
            Candidate(1, 10d, 0d, isAttribute: true),
            Candidate(2, -9d, 3d, isAttribute: false),
            Candidate(3, 8d, -6d, isAttribute: false)
        });

        Assert.True(selection.Selected);
        Assert.True(selection.Degraded);
        Assert.True(selection.Plan!.Score.EncirclesBase);
        Assert.False(RailLayoutStrategyPlanner.IsBalancedDefenseRing(selection.Plan.Score));
        Assert.Contains("非均衡闭环", selection.Warning);
    }

    [Fact]
    public void MissingAttributeStation_ReportsTheGameFacingError()
    {
        OpeningDefenseLoopSelection selection = OpeningDefenseLoopSelector.Select(new[]
        {
            Candidate(1, 10d, 0d, isAttribute: false),
            Candidate(2, -5d, 9d, isAttribute: false),
            Candidate(3, -5d, -9d, isAttribute: false)
        });

        Assert.False(selection.Selected);
        Assert.Equal(OpeningDefenseLoopSelector.NoLoopError, selection.Error);
    }

    [Fact]
    public void LayoutThatCannotEncircleTheBase_IsAcceptedWithAnHonestWarning()
    {
        OpeningDefenseLoopSelection selection = OpeningDefenseLoopSelector.Select(new[]
        {
            Candidate(1, 10d, 0d, isAttribute: true),
            Candidate(2, 12d, 2d, isAttribute: false),
            Candidate(3, 14d, 0d, isAttribute: false)
        });

        Assert.True(selection.Selected);
        Assert.True(selection.Degraded);
        Assert.False(selection.Plan!.Score.EncirclesBase);
        Assert.Contains("包围基地", selection.Warning);
    }

    [Fact]
    public void DegenerateLayoutThatCannotFormAnyLoop_IsRejected()
    {
        OpeningDefenseLoopSelection selection = OpeningDefenseLoopSelector.Select(new[]
        {
            Candidate(1, 10d, 0d, isAttribute: true),
            Candidate(2, 11d, 0d, isAttribute: false),
            Candidate(3, 12d, 0d, isAttribute: false)
        });

        Assert.False(selection.Selected);
        Assert.Equal(OpeningDefenseLoopSelector.NoLoopError, selection.Error);
    }

    [Fact]
    public void DuplicateInstanceIds_AreDeduplicatedBeforePlanning()
    {
        OpeningDefenseLoopSelection selection = OpeningDefenseLoopSelector.Select(new[]
        {
            Candidate(1, 10d, 0d, isAttribute: true),
            Candidate(1, 10d, 0d, isAttribute: true),
            Candidate(2, -5d, 9d, isAttribute: false),
            Candidate(3, -5d, -9d, isAttribute: false)
        });

        Assert.True(selection.Selected);
        Assert.Equal(3, selection.Plan!.OrderedPointInstanceIds.Count);
    }

    private static RailLoopPointCandidate Candidate(
        int instanceId,
        double x,
        double y,
        bool isAttribute) =>
        new()
        {
            InstanceId = instanceId,
            IsAttribute = isAttribute,
            MustInclude = true,
            Grid = new RailLayoutPoint(x, y)
        };
}
