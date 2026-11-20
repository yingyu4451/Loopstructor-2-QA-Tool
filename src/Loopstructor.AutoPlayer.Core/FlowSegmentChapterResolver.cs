namespace Loopstructor.AutoPlayer.Core;

/// <summary>
/// 新版流程存档按「大段（segment）」组织：1.457 的 Demo 流程依次是
/// preview_first / preview_second / demo_free，玩家的章节进度就是大段顺序。
/// 游戏的本地化与 UI 里没有「第几章」这个数字文本，工具此前拿地图分页块
/// （RoomMapSpawner.CurrentStage，35 层 ÷ mapStageStep 10 = 4 块）当章节，
/// 于是在只有 3 章的游戏里出现了「第04章」。这里统一按大段顺序编号。
/// </summary>
public static class FlowSegmentChapterResolver
{
    /// <summary>返回当前大段在流程配置中的序号（1 起）；无法定位时返回 0。</summary>
    public static int ResolveChapter(IReadOnlyList<string>? segmentIds, string? currentSegmentId)
    {
        if (segmentIds == null || segmentIds.Count == 0) return 0;
        if (string.IsNullOrWhiteSpace(currentSegmentId)) return 0;
        for (int index = 0; index < segmentIds.Count; index++)
        {
            if (string.Equals(segmentIds[index], currentSegmentId, StringComparison.Ordinal)) return index + 1;
        }

        return 0;
    }
}
