using Loopstructor.AutoPlayer.Core;

namespace Loopstructor.AutoPlayer.Tests;

/// <summary>
/// 回归覆盖：1.457 流程存档只有 3 个大段（章节），工具却拿地图分页块 +1 得到
/// 「第04章」（35 层 ÷ mapStageStep 10 = 4 块）。章节编号必须来自流程大段顺序。
/// </summary>
public sealed class FlowSegmentChapterResolverTests
{
    private static readonly string[] DemoFlow = { "preview_first", "preview_second", "demo_free" };

    [Theory]
    [InlineData("preview_first", 1)]
    [InlineData("preview_second", 2)]
    [InlineData("demo_free", 3)]
    public void ResolveChapter_UsesTheSegmentOrder(string segmentId, int expected)
    {
        Assert.Equal(expected, FlowSegmentChapterResolver.ResolveChapter(DemoFlow, segmentId));
    }

    [Fact]
    public void ResolveChapter_StaysWithinTheConfiguredSegmentCount()
    {
        int chapter = FlowSegmentChapterResolver.ResolveChapter(DemoFlow, "demo_free");

        Assert.True(chapter <= DemoFlow.Length, "章节号不得超过流程配置的大段数量。");
    }

    [Fact]
    public void ResolveChapter_ReturnsZeroWhenTheSegmentCannotBeLocated()
    {
        Assert.Equal(0, FlowSegmentChapterResolver.ResolveChapter(DemoFlow, "unknown_segment"));
        Assert.Equal(0, FlowSegmentChapterResolver.ResolveChapter(DemoFlow, null));
        Assert.Equal(0, FlowSegmentChapterResolver.ResolveChapter(DemoFlow, string.Empty));
        Assert.Equal(0, FlowSegmentChapterResolver.ResolveChapter(Array.Empty<string>(), "demo_free"));
        Assert.Equal(0, FlowSegmentChapterResolver.ResolveChapter(null, "demo_free"));
    }
}
