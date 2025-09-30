namespace ShadowrunAi.Core.Options;

public class StorageOptions
{
    public const string SectionName = "Storage";

    public string ConnectionString { get; set; } = string.Empty;

    public string ContainerName { get; set; } = "session-files";

    public string? DefaultRulebookBlobName { get; set; } = "defaults/SR5E.pdf";
}

