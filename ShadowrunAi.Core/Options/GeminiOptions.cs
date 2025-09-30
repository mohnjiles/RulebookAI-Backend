namespace ShadowrunAi.Core.Options;

public class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "gemini-2.5-flash-lite";

    public string? TitleModel { get; set; }

    public string BaseUri { get; set; } = "https://generativelanguage.googleapis.com";

    public bool UseCaching { get; set; } = true;

    public int RetryCount { get; set; } = 3;

    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(60);

    public string? SystemInstruction { get; set; }

    public string? DefaultSystemInstructionPath { get; set; } = "systemInstruction.txt";

    public int HistoryTurnLimit { get; set; } = 10;

    public int FileProcessingTimeoutSeconds { get; set; } = 60;

    public int FileProcessingPollSeconds { get; set; } = 2;
}

