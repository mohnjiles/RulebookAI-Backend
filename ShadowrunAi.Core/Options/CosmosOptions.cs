namespace ShadowrunAi.Core.Options;

public class CosmosOptions
{
    public const string SectionName = "Cosmos";

    public string AccountEndpoint { get; set; } = string.Empty;

    public string AccountKey { get; set; } = string.Empty;

    public string DatabaseId { get; set; } = "ShadowrunAi";

    public string ContainerId { get; set; } = "Sessions";

    public string PartitionKeyPath { get; set; } = "/id";
}

