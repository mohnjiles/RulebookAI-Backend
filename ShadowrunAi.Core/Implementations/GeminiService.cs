using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShadowrunAi.Core.Abstractions;
using ShadowrunAi.Core.Models;
using ShadowrunAi.Core.Options;

namespace ShadowrunAi.Core.Implementations;

public class GeminiService : IGenerativeAiService
{
    private readonly IGeminiApi _geminiApi;
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public GeminiService(IGeminiApi geminiApi, IOptions<GeminiOptions> options, ILogger<GeminiService> logger, IHttpClientFactory httpClientFactory)
    {
        _geminiApi = geminiApi;
        _logger = logger;
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ChatResponse> GenerateChatAsync(ChatSession session, string newMessage, CancellationToken cancellationToken = default)
    {
        var request = BuildRequest(session, newMessage);

        var response = await _geminiApi.GenerateContentAsync(_options.Model, _options.ApiKey, request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Gemini generate content failed: {StatusCode}", response.StatusCode);
            throw new InvalidOperationException($"Gemini API call failed with status code {response.StatusCode}");
        }

        var payload = response.Content;
        var text = ExtractText(payload);
        return new ChatResponse(text, payload?.CachedContent);
    }

    public async IAsyncEnumerable<string> GenerateChatStreamAsync(ChatSession session, string newMessage, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = BuildRequest(session, newMessage);

        var response = await _geminiApi.StreamGenerateContentAsync(_options.Model, _options.ApiKey, request, cancellationToken);

        if (!response.IsSuccessStatusCode || response.Content is null)
        {
            _logger.LogError("Gemini streaming content failed: {StatusCode}", response.StatusCode);
            yield break;
        }

        using var stream = response.Content;
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                line = line[5..].Trim();
            }

            if (string.Equals(line, "[DONE]", StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            foreach (var chunk in ExtractTextChunks(line))
            {
                if (!string.IsNullOrEmpty(chunk))
                {
                    yield return chunk;
                }
            }
        }
    }

    public async Task<string?> GenerateTitleAsync(ChatSession session, string prompt, CancellationToken cancellationToken = default)
    {
        var request = BuildTitleRequest(prompt);

        var model = string.IsNullOrWhiteSpace(_options.TitleModel) ? _options.Model : _options.TitleModel;

        var response = await _geminiApi.GenerateContentAsync(model, _options.ApiKey, request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Gemini title generation failed: {StatusCode}", response.StatusCode);
            return null;
        }

        return ExtractText(response.Content)?.Trim();
    }

    public async Task<UploadResult> UploadPdfAsync(Guid sessionId, Stream pdfStream, string fileName, string? systemInstruction, CancellationToken cancellationToken = default)
    {
        // Prefer resumable protocol to avoid multipart issues
        var length = TryGetLength(pdfStream) ?? throw new InvalidOperationException("PDF stream must support length for resumable upload");

        var startMetadata = new { file = new { display_name = fileName } };
        var startResponse = await _geminiApi.StartResumableUploadAsync(_options.ApiKey, length, "application/pdf", startMetadata, cancellationToken);
        if (!startResponse.IsSuccessStatusCode)
        {
            _logger.LogError("Gemini resumable start failed: {StatusCode}", startResponse.StatusCode);
            throw new InvalidOperationException("Gemini resumable start failed");
        }

        // Extract upload URL from response headers
        if (!startResponse.Headers.TryGetValues("X-Goog-Upload-URL", out var urls))
        {
            _logger.LogError("Gemini resumable start missing X-Goog-Upload-URL header");
            throw new InvalidOperationException("Gemini resumable start missing upload URL");
        }
        var uploadUrl = urls.FirstOrDefault();
        if (string.IsNullOrEmpty(uploadUrl))
        {
            _logger.LogError("Gemini resumable upload URL was empty");
            throw new InvalidOperationException("Gemini resumable upload URL was empty");
        }

        // Reset position if possible
        if (pdfStream.CanSeek)
        {
            pdfStream.Position = 0;
        }

        var streamContent = new StreamContent(pdfStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        streamContent.Headers.ContentLength = length;

        using var httpClient = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
        request.Headers.Add("X-Goog-Upload-Offset", "0");
        request.Headers.Add("X-Goog-Upload-Command", "upload, finalize");
        request.Content = streamContent;
        
        var uploadResponse = await httpClient.SendAsync(request, cancellationToken);
        if (!uploadResponse.IsSuccessStatusCode)
        {
            _logger.LogError("Gemini resumable upload failed: {StatusCode}", uploadResponse.StatusCode);
            var err = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Gemini resumable upload failed: {uploadResponse.StatusCode} {err}");
        }

        var uploadedFile = await DeserializeAsync<FileUploadResponse>(uploadResponse.Content, cancellationToken);
        await WaitForFileActiveAsync(uploadedFile.File!.Name!, cancellationToken);

        string? cacheName = null;
        var effectiveInstruction = string.IsNullOrWhiteSpace(systemInstruction) ? _options.SystemInstruction : systemInstruction;

        if (_options.UseCaching)
        {
            var cacheRequest = new CreateCacheRequest
            {
                Model = _options.Model,
                DisplayName = fileName,
                Contents = new List<GenerateContent>
                {
                    new()
                    {
                        Role = "user",
                        Parts = new List<GeneratePart>
                        {
                            new()
                            {
                                FileData = new FilePartData
                                {
                                    FileUri = uploadedFile.File!.Uri!,
                                    MimeType = uploadedFile.File!.MimeType ?? "application/pdf"
                                }
                            }
                        }
                    }
                },
                SystemInstruction = BuildSystemInstruction(effectiveInstruction)
            };

            var cacheResponse = await _geminiApi.CreateCacheAsync(_options.ApiKey, cacheRequest, cancellationToken);
            if (cacheResponse.IsSuccessStatusCode)
            {
                cacheName = cacheResponse.Content?.Name;
                if (!string.IsNullOrEmpty(cacheName))
                {
                    var ttl = (int)_options.CacheTtl.TotalSeconds;
                    await _geminiApi.UpdateCacheAsync(cacheName, _options.ApiKey, new UpdateCacheRequest
                    {
                        Config = new UpdateCacheConfig
                        {
                            Ttl = $"{ttl}s"
                        }
                    }, cancellationToken);
                }
            }
        }

        return new UploadResult(
            uploadedFile.File!.Name!,
            uploadedFile.File?.DisplayName ?? fileName,
            DateTimeOffset.UtcNow,
            cacheName,
            uploadedFile.File!.Uri,
            uploadedFile.File!.MimeType,
            effectiveInstruction,
            uploadedFile.File!.ExpirationTime);
    }

    public async Task<CacheResult?> GetCacheAsync(string cacheId, CancellationToken cancellationToken = default)
    {
        var response = await _geminiApi.GetCacheAsync(cacheId, _options.ApiKey, cancellationToken);
        if (!response.IsSuccessStatusCode || response.Content is null)
        {
            return null;
        }

        var content = response.Content;
        return new CacheResult(content.Name!, content.ExpireTime ?? DateTimeOffset.UtcNow.Add(_options.CacheTtl));
    }

    private GenerateContentRequest BuildRequest(ChatSession session, string newMessage)
    {
        var history = session.Turns
            .SelectMany(turn => new[] { turn.User, turn.Assistant })
            .Where(message => message is not null)
            .Select(message => new GenerateContent
        {
            Role = message!.Role,
            Parts = new List<GeneratePart>
            {
                new() { Text = message.Text }
            }
        }).ToList();

        var contents = history
            .TakeLast(_options.HistoryTurnLimit * 2)
            .ToList();

        contents.Add(new GenerateContent
        {
            Role = "user",
            Parts = BuildUserParts(session, newMessage)
        });

        var request = new GenerateContentRequest
        {
            Contents = contents,
            CachedContent = session.ProviderCacheId,
            SystemInstruction = BuildSystemInstruction(session.SystemInstruction ?? _options.SystemInstruction)
        };

        // Log cache usage for diagnostics
        if (!string.IsNullOrEmpty(session.ProviderCacheId))
        {
            _logger.LogInformation("Using cached content: {CacheId} for session {SessionId}", session.ProviderCacheId, session.Id);
        }
        else if (!string.IsNullOrEmpty(session.FileUri))
        {
            _logger.LogInformation("Using file URI: {FileUri} for session {SessionId}", session.FileUri, session.Id);
        }
        else
        {
            _logger.LogWarning("No cached content or file URI found for session {SessionId}", session.Id);
        }

        return request;
    }

    private GenerateContentRequest BuildTitleRequest(string prompt)
    {
        var contents = new List<GenerateContent>
        {
            new()
            {
                Role = "user",
                Parts = [ new GeneratePart { Text = prompt } ]
            }
        };

        return new GenerateContentRequest
        {
            Contents = contents,
            SystemInstruction = BuildSystemInstruction("You name runs like a veteran Shadowrun fixer. Return only the codename, three words or fewer.")
        };
    }

    private async Task<FileStatusResponse> WaitForFileActiveAsync(string fileName, CancellationToken cancellationToken)
    {
        var pollInterval = TimeSpan.FromSeconds(Math.Max(1, _options.FileProcessingPollSeconds));
        var maxAttempts = Math.Max(1, _options.FileProcessingTimeoutSeconds / Math.Max(1, _options.FileProcessingPollSeconds));

        var fileId = fileName.Split('/').LastOrDefault() ?? fileName;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var statusResponse = await _geminiApi.GetFileAsync(fileId, _options.ApiKey, cancellationToken);
            if (statusResponse is { IsSuccessStatusCode: true, Content: not null })
            {
                if (!string.Equals(statusResponse.Content.State, "PROCESSING", StringComparison.OrdinalIgnoreCase))
                {
                    return statusResponse.Content;
                }
            }

            await Task.Delay(pollInterval, cancellationToken);
        }

        throw new TimeoutException("File processing timed out");
    }

    private static MultipartContent BuildMultipartContent(Stream pdfStream, string fileName)
    {
        var boundary = "BOUNDARY";
        var multipartContent = new MultipartContent("related", boundary);

        // Match REST shape: metadata contains a 'file' resource description
        var metadata = new
        {
            file = new
            {
                displayName = fileName,
                mimeType = "application/pdf"
            }
        };

        var metadataContent = new StringContent(System.Text.Json.JsonSerializer.Serialize(metadata));
        metadataContent.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };
        multipartContent.Add(metadataContent);

        var streamContent = new StreamContent(pdfStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        streamContent.Headers.TryAddWithoutValidation("Content-Transfer-Encoding", "binary");
        // Do not set Content-Disposition for multipart/related; Gemini expects only Content-Type per part
        multipartContent.Add(streamContent);

        return multipartContent;
    }

    private static long? TryGetLength(Stream stream)
    {
        try
        {
            if (stream.CanSeek)
            {
                return stream.Length;
            }
        }
        catch
        {
            // ignore
        }
        return null;
    }

    private static async Task<T> DeserializeAsync<T>(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        return await System.Text.Json.JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Empty response body");
    }

    private static string ExtractText(GenerateContentResponse? payload)
    {
        if (payload?.Candidates is null)
        {
            return string.Empty;
        }

        return string.Join(
            string.Empty,
            payload.Candidates
                .SelectMany(candidate => candidate.Content?.Parts ?? Array.Empty<GeneratePart>())
                .Select(part => part.Text)
                .Where(text => !string.IsNullOrEmpty(text)));
    }

    private static List<GeneratePart> BuildUserParts(ChatSession session, string newMessage)
    {
        var parts = new List<GeneratePart>
        {
            new() { Text = newMessage }
        };

        // Include file reference when NOT using cache
        // With cache: file is already in the cached content
        // Without cache: must include file in EVERY request (API is stateless)
        if (string.IsNullOrEmpty(session.ProviderCacheId) && !string.IsNullOrEmpty(session.FileUri))
        {
            parts.Add(new GeneratePart
            {
                FileData = new FilePartData
                {
                    FileUri = session.FileUri!,
                    MimeType = session.MimeType ?? "application/pdf"
                }
            });
        }

        return parts;
    }

    private static IEnumerable<string> ExtractTextChunks(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            yield break;
        }

        if (!TryParseJson(line, out var json))
        {
            yield return line;
            yield break;
        }

        if (json.RootElement.TryGetProperty("candidates", out var candidates))
        {
            foreach (var candidate in candidates.EnumerateArray())
            {
                if (candidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
                {
                    foreach (var part in parts.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out var textElement))
                        {
                            var text = textElement.GetString();
                            if (!string.IsNullOrEmpty(text))
                            {
                                yield return text;
                            }
                        }
                    }
                }
            }
        }
        else if (json.RootElement.TryGetProperty("error", out var errorElement) && errorElement.TryGetProperty("message", out var messageElement))
        {
            throw new InvalidOperationException(messageElement.GetString() ?? "Gemini streaming error");
        }
    }

    private static GenerateSystemInstruction? BuildSystemInstruction(string? instruction)
    {
        if (string.IsNullOrWhiteSpace(instruction))
        {
            return null;
        }

        return new GenerateSystemInstruction
        {
            Parts =
            [
                new GeneratePart
                {
                    Text = instruction
                }
            ]
        };
    }

    private static bool TryParseJson(string line, out JsonDocument document)
    {
        try
        {
            document = JsonDocument.Parse(line);
            return true;
        }
        catch (JsonException)
        {
            document = null!;
            return false;
        }
    }
}

