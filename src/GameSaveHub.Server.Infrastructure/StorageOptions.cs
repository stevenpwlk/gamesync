namespace GameSaveHub.Server.Infrastructure;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";
    public string Root { get; set; } = "data";
    public long MaxArtifactBytes { get; set; } = 64L * 1024 * 1024;
    public int MaxChunkBytes { get; set; } = 4 * 1024 * 1024;
}
