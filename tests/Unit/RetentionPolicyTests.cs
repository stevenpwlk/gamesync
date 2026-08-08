using GameSaveHub.Core;

namespace GameSaveHub.UnitTests;

public sealed class RetentionPolicyTests
{
    [Fact]
    public void KeepsLatestDailyWeeklyCurrentAndProtected()
    {
        var start = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
        var versions = Enumerable.Range(0, 80)
            .Select(index => new VersionRetentionCandidate(
                Guid.NewGuid(),
                start.AddDays(-index),
                IsCurrent: index == 79,
                IsProtected: index == 78))
            .ToArray();

        var keep = RetentionPolicy.SelectVersionsToKeep(versions, latestCount: 20, dailyCount: 30, weeklyCount: 4);

        Assert.All(versions.Take(30), version => Assert.Contains(version.Id, keep));
        Assert.Contains(versions[79].Id, keep);
        Assert.Contains(versions[78].Id, keep);
        Assert.DoesNotContain(versions[60].Id, keep);
    }

    [Fact]
    public void RejectsNegativeCounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RetentionPolicy.SelectVersionsToKeep([], -1, 0, 0));
    }
}
