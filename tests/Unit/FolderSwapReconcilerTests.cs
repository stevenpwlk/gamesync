using GameSaveHub.Client.Orchestration;

namespace GameSaveHub.UnitTests;

public sealed class FolderSwapReconcilerTests
{
    [Fact]
    public void ClientOnlyMeansNoActionNeeded() =>
        Assert.Equal(FolderSwapReconciliationAction.NoActionNeeded, FolderSwapReconciler.Resolve(clientExists: true, clientOldExists: false));

    [Fact]
    public void BothPresentMeansCleanupOld() =>
        Assert.Equal(FolderSwapReconciliationAction.CleanupOldFolder, FolderSwapReconciler.Resolve(clientExists: true, clientOldExists: true));

    [Fact]
    public void OnlyOldPresentMeansRestoreFromOld() =>
        Assert.Equal(FolderSwapReconciliationAction.RestoreFromOld, FolderSwapReconciler.Resolve(clientExists: false, clientOldExists: true));

    [Fact]
    public void NeitherPresentRequiresManualReview() =>
        Assert.Equal(FolderSwapReconciliationAction.ManualReviewRequired, FolderSwapReconciler.Resolve(clientExists: false, clientOldExists: false));
}
