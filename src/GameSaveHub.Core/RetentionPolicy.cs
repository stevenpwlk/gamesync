using System.Globalization;

namespace GameSaveHub.Core;

public sealed record VersionRetentionCandidate(Guid Id, DateTimeOffset CreatedAtUtc, bool IsCurrent, bool IsProtected);

public static class RetentionPolicy
{
    public static IReadOnlySet<Guid> SelectVersionsToKeep(
        IEnumerable<VersionRetentionCandidate> versions,
        int latestCount = 20,
        int dailyCount = 30,
        int weeklyCount = 26)
    {
        if (latestCount < 0 || dailyCount < 0 || weeklyCount < 0) throw new ArgumentOutOfRangeException(nameof(latestCount));
        var ordered = versions.OrderByDescending(x => x.CreatedAtUtc).ToArray();
        var keep = ordered.Where(x => x.IsCurrent || x.IsProtected).Select(x => x.Id).ToHashSet();
        keep.UnionWith(ordered.Take(latestCount).Select(x => x.Id));
        keep.UnionWith(ordered
            .GroupBy(x => x.CreatedAtUtc.UtcDateTime.Date)
            .OrderByDescending(x => x.Key)
            .Take(dailyCount)
            .Select(x => x.First().Id));
        keep.UnionWith(ordered
            .GroupBy(x => (ISOWeek.GetYear(x.CreatedAtUtc.UtcDateTime), ISOWeek.GetWeekOfYear(x.CreatedAtUtc.UtcDateTime)))
            .OrderByDescending(x => x.Key.Item1)
            .ThenByDescending(x => x.Key.Item2)
            .Take(weeklyCount)
            .Select(x => x.First().Id));
        return keep;
    }
}
