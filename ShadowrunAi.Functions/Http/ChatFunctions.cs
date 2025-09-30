using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using AutoMapper;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using ShadowrunAi.Core.Abstractions;
using ShadowrunAi.Core.Models;
using ShadowrunAi.Functions.DTOs;
using System.Security.Cryptography;
using System.Text;
using ShadowrunAi.Core.Options;

namespace ShadowrunAi.Functions.Http;

public class ChatFunctions(
    IGenerativeAiService aiService,
    IDataService dataService,
    IStorageService storageService,
    IValidator<ChatRequestDto> chatValidator,
    IValidator<RerunRequestDto> rerunValidator,
    IValidator<DeleteMessageRequestDto> deleteMessageValidator,
    IMapper mapper,
    IOptions<GeminiOptions> geminiOptions,
    IOptions<ShadowrunAi.Core.Options.StorageOptions> storageOptions)
{
    private readonly GeminiOptions _geminiOptions = geminiOptions.Value;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Function("ChatStream")]
    [Authorize]
    public async Task<HttpResponseData> ChatStream(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "chat/stream")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var body = await JsonSerializer.DeserializeAsync<ChatRequestDto>(req.Body, JsonOptions, cancellationToken);
        if (body is null)
        {
            return await WriteErrorAsync(req, HttpStatusCode.BadRequest, "Invalid payload");
        }

        var validation = await chatValidator.ValidateAsync(body, cancellationToken);
        if (!validation.IsValid)
        {
            return await WriteErrorAsync(req, HttpStatusCode.BadRequest, validation.Errors.Select(e => e.ErrorMessage));
        }

        var session = await dataService.GetSessionAsync(body.SessionId, cancellationToken);
        if (session is null)
        {
            return await WriteErrorAsync(req, HttpStatusCode.NotFound, "Session not found");
        }

        await RehydrateSeedFileIfExpiredAsync(session, cancellationToken);

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/event-stream");

        var sb = new StringBuilder();
        await foreach (var chunk in aiService.GenerateChatStreamAsync(session, body.Message, cancellationToken))
        {
            if (!string.IsNullOrEmpty(chunk))
            {
                sb.Append(chunk);
            }
            await response.WriteStringAsync($"data: {JsonSerializer.Serialize(new { type = "chunk", text = chunk }, JsonOptions)}\n\n", cancellationToken);
        }

        await response.WriteStringAsync($"data: {JsonSerializer.Serialize(new { type = "done" }, JsonOptions)}\n\n", cancellationToken);

        var fullText = sb.ToString();
        session.Turns.Add(new ChatTurn
        {
            User = new ChatMessage { Role = "user", Text = body.Message },
            Assistant = new ChatMessage { Role = "model", Text = fullText },
            Timestamp = DateTimeOffset.UtcNow
        });

        if ((string.IsNullOrWhiteSpace(session.Title) || string.Equals(session.Title, "New chat", StringComparison.OrdinalIgnoreCase))
            && session.Turns.Count == 1)
        {
            var title = await GenerateCinematicTitleAsync(aiService, session, body.Message, cancellationToken);
            if (!string.IsNullOrWhiteSpace(title))
            {
                session.Title = title;
            }
        }

        session.UpdatedAt = DateTimeOffset.UtcNow;
        await dataService.UpsertSessionAsync(session, cancellationToken);

        return response;
    }

    [Function("ChatRerunStream")]
    [Authorize]
    public async Task<HttpResponseData> ChatRerunStream(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "chat/rerun/stream")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var body = await JsonSerializer.DeserializeAsync<RerunRequestDto>(req.Body, JsonOptions, cancellationToken);
        if (body is null)
        {
            return await WriteErrorAsync(req, HttpStatusCode.BadRequest, "Invalid payload");
        }

        var validation = await rerunValidator.ValidateAsync(body, cancellationToken);
        if (!validation.IsValid)
        {
            return await WriteErrorAsync(req, HttpStatusCode.BadRequest, validation.Errors.Select(e => e.ErrorMessage));
        }

        var session = await dataService.GetSessionAsync(body.SessionId, cancellationToken);
        if (session is null)
        {
            return await WriteErrorAsync(req, HttpStatusCode.NotFound, "Session not found");
        }

        await RehydrateSeedFileIfExpiredAsync(session, cancellationToken);

        var effectiveUserMessage = (body.TurnIndex >= 0 && body.TurnIndex < session.Turns.Count)
            ? session.Turns[body.TurnIndex].User?.Text
            : null;
        if (string.IsNullOrWhiteSpace(effectiveUserMessage))
        {
            effectiveUserMessage = body.UserMessage;
        }
        if (string.IsNullOrWhiteSpace(effectiveUserMessage))
        {
            return await WriteErrorAsync(req, HttpStatusCode.BadRequest, "User message required to re-run");
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/event-stream");

        var sb = new StringBuilder();
        await foreach (var chunk in aiService.GenerateChatStreamAsync(session, effectiveUserMessage!, cancellationToken))
        {
            if (!string.IsNullOrEmpty(chunk))
            {
                sb.Append(chunk);
                await response.WriteStringAsync($"data: {JsonSerializer.Serialize(new { type = "chunk", text = chunk }, JsonOptions)}\n\n", cancellationToken);
            }
        }

        await response.WriteStringAsync($"data: {JsonSerializer.Serialize(new { type = "done" }, JsonOptions)}\n\n", cancellationToken);

        var fullText = sb.ToString();
        if (body.TurnIndex >= 0 && body.TurnIndex < session.Turns.Count)
        {
            var turn = session.Turns[body.TurnIndex] ?? new ChatTurn();
            turn.User ??= new ChatMessage { Role = "user", Text = effectiveUserMessage };
            turn.Assistant = new ChatMessage { Role = "model", Text = fullText };
            session.Turns[body.TurnIndex] = turn;
        }
        else
        {
            session.Turns.Add(new ChatTurn
            {
                User = new ChatMessage { Role = "user", Text = effectiveUserMessage },
                Assistant = new ChatMessage { Role = "model", Text = fullText },
                Timestamp = DateTimeOffset.UtcNow
            });
        }

        session.UpdatedAt = DateTimeOffset.UtcNow;
        await dataService.UpsertSessionAsync(session, cancellationToken);

        return response;
    }

    [Function("StartSessionWithDefaultRulebook")]
    [Authorize]
    public async Task<HttpResponseData> StartSessionWithDefaultRulebook(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sessions/start-with-default")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var accountId = FunctionBase.GetAccountIdFromClaims(req);

        var defaultBlob = storageOptions.Value.DefaultRulebookBlobName;
        if (string.IsNullOrWhiteSpace(defaultBlob))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Default rulebook blob is not configured" }, cancellationToken);
            return bad;
        }

        // Ensure a shared seed session exists for the default rulebook (deterministic ID by blob name)
        var seedSessionId = ComputeDeterministicGuid(defaultBlob!);
        var seedSession = await dataService.GetSessionAsync(seedSessionId, cancellationToken);
        if (seedSession is null || string.IsNullOrEmpty(seedSession.ProviderFileId))
        {
            await using var seedStream = await storageService.GetByBlobNameAsync(defaultBlob!, cancellationToken);
            if (seedStream is null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Default rulebook not found in storage" }, cancellationToken);
                return notFound;
            }

            var seedFileName = Path.GetFileName(defaultBlob);
            var systemInstruction = string.IsNullOrWhiteSpace(_geminiOptions.DefaultSystemInstructionPath)
                ? _geminiOptions.SystemInstruction
                : await File.ReadAllTextAsync(_geminiOptions.DefaultSystemInstructionPath, cancellationToken);
            var seedUpload = await aiService.UploadPdfAsync(seedSessionId, seedStream, seedFileName, systemInstruction, cancellationToken);

            seedSession ??= new ChatSession { Id = seedSessionId };
            seedSession.FileName = seedFileName;
            seedSession.MimeType = "application/pdf";
            seedSession.StorageBlobName = defaultBlob;
            seedSession.ProviderFileId = seedUpload.ProviderId;
            seedSession.ProviderCacheId = seedUpload.CachedContentName ?? seedSession.ProviderCacheId;
            seedSession.FileUri = seedUpload.FileUri ?? seedSession.FileUri;
            seedSession.SystemInstruction = seedUpload.SystemInstruction ?? seedSession.SystemInstruction;
            seedSession.FileExpiresAt = seedUpload.ExpirationTime;
            seedSession.AccountId = null; // shared seed
            seedSession.UpdatedAt = DateTimeOffset.UtcNow;
            await dataService.UpsertSessionAsync(seedSession, cancellationToken);
        }

        // Create a new user session that references the shared file/cache
        var sessionId = Guid.NewGuid();
        var userSession = new ChatSession
        {
            Id = sessionId,
            FileName = seedSession?.FileName,
            MimeType = seedSession?.MimeType,
            StorageBlobName = seedSession?.StorageBlobName,
            ProviderFileId = seedSession?.ProviderFileId,
            ProviderCacheId = seedSession?.ProviderCacheId,
            FileUri = seedSession?.FileUri,
            FileExpiresAt = seedSession?.FileExpiresAt,
            SystemInstruction = seedSession?.SystemInstruction,
            AccountId = accountId, // scope to user
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await dataService.UpsertSessionAsync(userSession, cancellationToken);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { success = true, sessionId, providerId = userSession.ProviderFileId, cacheName = userSession.ProviderCacheId, fileUri = userSession.FileUri }, cancellationToken);
        return response;
    }

    private async Task RehydrateSeedFileIfExpiredAsync(ChatSession session, CancellationToken cancellationToken)
    {
        // Can't rehydrate without a blob name
        if (string.IsNullOrEmpty(session.StorageBlobName))
        {
            return;
        }

        // Only rehydrate if we have an expiration date and it has passed
        if (!session.FileExpiresAt.HasValue || session.FileExpiresAt.Value >= DateTimeOffset.UtcNow)
        {
            return; // Not expired or no expiration
        }

        // Re-upload from blob storage to get a fresh provider file ID
        await using var stream = await storageService.GetByBlobNameAsync(session.StorageBlobName, cancellationToken);
        if (stream is null)
        {
            // Or should we fail hard? For now, let the AI proceed without the file context.
            return;
        }

        var fileName = Path.GetFileName(session.FileName ?? session.StorageBlobName);
        var upload = await aiService.UploadPdfAsync(session.Id, stream, fileName, session.SystemInstruction, cancellationToken);

        session.ProviderFileId = upload.ProviderId;
        session.FileUri = upload.FileUri;
        session.FileExpiresAt = upload.ExpirationTime;
        session.ProviderCacheId = upload.CachedContentName ?? session.ProviderCacheId;
        session.UpdatedAt = DateTimeOffset.UtcNow;
        await dataService.UpsertSessionAsync(session, cancellationToken);
    }

    [Function("Chat")]
    [Authorize]
    public async Task<HttpResponseData> Chat(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "chat")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var accountId = FunctionBase.GetAccountIdFromClaims(req);
        var body = await JsonSerializer.DeserializeAsync<ChatRequestDto>(req.Body, cancellationToken: cancellationToken);
        if (body is null)
        {
            return await WriteErrorAsync(req, HttpStatusCode.BadRequest, "Invalid payload");
        }

        var validation = await chatValidator.ValidateAsync(body, cancellationToken);
        if (!validation.IsValid)
        {
            return await WriteErrorAsync(req, HttpStatusCode.BadRequest, validation.Errors.Select(e => e.ErrorMessage));
        }

        var session = await dataService.GetSessionAsync(body.SessionId, cancellationToken);
        if (session is null)
        {
            return await WriteErrorAsync(req, HttpStatusCode.NotFound, "Session not found");
        }
        if (!string.IsNullOrEmpty(session.AccountId) && !string.Equals(session.AccountId, accountId, StringComparison.Ordinal))
        {
            return await WriteErrorAsync(req, HttpStatusCode.Forbidden, "Forbidden");
        }

        await RehydrateSeedFileIfExpiredAsync(session, cancellationToken);

        var result = await aiService.GenerateChatAsync(session, body.Message, cancellationToken);

        session.Turns.Add(new ChatTurn
        {
            User = new ChatMessage { Role = "user", Text = body.Message },
            Assistant = new ChatMessage { Role = "model", Text = result.Text },
            Timestamp = DateTimeOffset.UtcNow
        });

        if ((string.IsNullOrWhiteSpace(session.Title) || string.Equals(session.Title, "New chat", StringComparison.OrdinalIgnoreCase))
            && session.Turns.Count == 1)
        {
            var title = await GenerateCinematicTitleAsync(aiService, session, body.Message, cancellationToken);
            if (!string.IsNullOrWhiteSpace(title))
            {
                session.Title = title;
            }
        }

        if (!string.IsNullOrEmpty(result.CacheId))
        {
            session.ProviderCacheId = result.CacheId;
        }

        session.UpdatedAt = DateTimeOffset.UtcNow;
        await dataService.UpsertSessionAsync(session, cancellationToken);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { response = result.Text, sessionId = session.Id, cacheId = result.CacheId }, cancellationToken);
        return response;
    }

    [Function("ListSessions")]
    [Authorize]
    public async Task<HttpResponseData> ListSessions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sessions")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var accountId = FunctionBase.GetAccountIdFromClaims(req) ?? string.Empty;
        Guid? currentId = null;
        var query = QueryHelpers.ParseQuery(req.Url.Query);
        if (query.TryGetValue("current", out var currentStr) && Guid.TryParse(currentStr.FirstOrDefault(), out var parsed))
        {
            currentId = parsed;
        }

        var sessions = await dataService.ListSessionsAsync(accountId, currentId, cancellationToken);
        var payload = mapper.Map<IEnumerable<SessionResponseDto>>(sessions);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { sessions = payload }, cancellationToken);
        return response;
    }

    [Function("DeleteSession")]
    [Authorize]
    public async Task<HttpResponseData> DeleteSession(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "sessions/{id:guid}")] HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        var accountId = FunctionBase.GetAccountIdFromClaims(req);
        var session = await dataService.GetSessionAsync(id, cancellationToken);
        if (session is null)
        {
            return await WriteErrorAsync(req, HttpStatusCode.NotFound, "Session not found");
        }
        if (!string.IsNullOrEmpty(session.AccountId) && !string.Equals(session.AccountId, accountId, StringComparison.Ordinal))
        {
            return await WriteErrorAsync(req, HttpStatusCode.Forbidden, "Forbidden");
        }
        await dataService.DeleteSessionAsync(id, cancellationToken);
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { success = true }, cancellationToken);
        return response;
    }

    [Function("DeleteMessage")]
    [Authorize]
    public async Task<HttpResponseData> DeleteMessage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "chat-message")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var accountId = FunctionBase.GetAccountIdFromClaims(req);
        var body = await JsonSerializer.DeserializeAsync<DeleteMessageRequestDto>(req.Body, JsonOptions, cancellationToken);
        if (body is null)
        {
            return await WriteErrorAsync(req, HttpStatusCode.BadRequest, "Invalid payload");
        }

        var validation = await deleteMessageValidator.ValidateAsync(body, cancellationToken);
        if (!validation.IsValid)
        {
            return await WriteErrorAsync(req, HttpStatusCode.BadRequest, validation.Errors.Select(e => e.ErrorMessage));
        }

        var session = await dataService.GetSessionAsync(body.SessionId, cancellationToken);
        if (session is null)
        {
            return await WriteErrorAsync(req, HttpStatusCode.NotFound, "Session not found");
        }
        if (!string.IsNullOrEmpty(session.AccountId) && !string.Equals(session.AccountId, accountId, StringComparison.Ordinal))
        {
            return await WriteErrorAsync(req, HttpStatusCode.Forbidden, "Forbidden");
        }

        if (body.TurnIndex < 0 || body.TurnIndex >= session.Turns.Count)
        {
            return await WriteErrorAsync(req, HttpStatusCode.BadRequest, "Invalid turn index");
        }

        var turn = session.Turns[body.TurnIndex];
        
        // Delete the specified message (user or ai)
        if (body.Role == "user")
        {
            turn.User = null;
        }
        else if (body.Role == "ai")
        {
            turn.Assistant = null;
        }

        // If the turn is now empty, remove it entirely
        if (turn.User is null && turn.Assistant is null)
        {
            session.Turns.RemoveAt(body.TurnIndex);
        }

        session.UpdatedAt = DateTimeOffset.UtcNow;
        await dataService.UpsertSessionAsync(session, cancellationToken);

        // Return updated history
        var response = req.CreateResponse(HttpStatusCode.OK);
        var historyDto = session.Turns.Select(t => new
        {
            user = t.User?.Text,
            ai = t.Assistant?.Text,
            timestamp = t.Timestamp
        }).ToList();
        await response.WriteAsJsonAsync(new { success = true, history = historyDto }, cancellationToken);
        return response;
    }

    [Function("SessionInfo")]
    [Authorize]
    public async Task<HttpResponseData> SessionInfo(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sessions/{id:guid}/info")] HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        var session = await dataService.GetSessionAsync(id, cancellationToken);
        if (session is null)
        {
            return await WriteErrorAsync(req, HttpStatusCode.NotFound, "Session not found");
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            sessionId = session.Id,
            fileName = session.FileName,
            providerFileId = session.ProviderFileId,
            providerCacheId = session.ProviderCacheId,
            fileUri = session.FileUri,
            fileExpiresAt = session.FileExpiresAt,
            cacheExpiresAt = session.CacheExpiresAt,
            accountId = session.AccountId,
            isSeedSession = string.IsNullOrEmpty(session.AccountId),
            hasCachedContent = !string.IsNullOrEmpty(session.ProviderCacheId),
            hasFileUri = !string.IsNullOrEmpty(session.FileUri),
            turnsCount = session.Turns.Count
        }, cancellationToken);
        return response;
    }

    [Function("ChatHistory")]
    [Authorize]
    public async Task<HttpResponseData> ChatHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "chat-history/{id:guid}")] HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        var accountId = FunctionBase.GetAccountIdFromClaims(req);
        var session = await dataService.GetSessionAsync(id, cancellationToken);
        if (session is null)
        {
            return await WriteErrorAsync(req, HttpStatusCode.NotFound, "Session not found");
        }
        if (!string.IsNullOrEmpty(session.AccountId) && !string.Equals(session.AccountId, accountId, StringComparison.Ordinal))
        {
            return await WriteErrorAsync(req, HttpStatusCode.Forbidden, "Forbidden");
        }

        var history = session.Turns.Select(t => new
        {
            user = t.User?.Text,
            ai = t.Assistant?.Text,
            timestamp = t.Timestamp
        });

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { history }, cancellationToken);
        return response;
    }

    [Function("UploadPdf")]
    public async Task<HttpResponseData> UploadPdf(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "upload-pdf")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var accountId = FunctionBase.GetAccountIdFromClaims(req);
        if (!TryGetBoundary(req, out var boundary))
        {
            return await WriteErrorAsync(req, HttpStatusCode.UnsupportedMediaType, "Missing or invalid multipart boundary");
        }

        var reader = new MultipartReader(boundary!, req.Body);
        var userSessionId = Guid.Empty;
        string? systemInstruction = null;
        string? fileName = null;
        string? contentType = null;
        MemoryStream? fileBuffer = null;

        for (var section = await reader.ReadNextSectionAsync(cancellationToken);
             section != null;
             section = await reader.ReadNextSectionAsync(cancellationToken))
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var contentDisposition))
            {
                continue;
            }

            if (contentDisposition.DispositionType.Equals("form-data") && string.IsNullOrEmpty(contentDisposition.FileName.Value))
            {
                using var streamReader = new StreamReader(section.Body);
                var value = await streamReader.ReadToEndAsync(cancellationToken);
                var name = contentDisposition.Name.Value?.Trim('"');
                if (string.Equals(name, "sessionId", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(value, out var parsed))
                {
                    userSessionId = parsed;
                }
                else if (string.Equals(name, "systemInstruction", StringComparison.OrdinalIgnoreCase))
                {
                    systemInstruction = value;
                }
            }
            else if (contentDisposition.DispositionType.Equals("form-data") && !string.IsNullOrEmpty(contentDisposition.FileName.Value))
            {
                fileName = contentDisposition.FileName.Value?.Trim('"');
                contentType = section.ContentType;
                fileBuffer = new MemoryStream();
                await section.Body.CopyToAsync(fileBuffer, cancellationToken);
                fileBuffer.Position = 0;
            }
        }

        if (fileBuffer is null || string.IsNullOrEmpty(fileName))
        {
            return await WriteErrorAsync(req, HttpStatusCode.BadRequest, "No PDF file uploaded");
        }
        if (!string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return await WriteErrorAsync(req, HttpStatusCode.BadRequest, "Only PDF files are allowed");
        }

        // --- Start of content-aware seeding ---
        var seedSessionId = ComputeDeterministicGuid(fileBuffer);
        var seedSession = await dataService.GetSessionAsync(seedSessionId, cancellationToken);

        // Rehydrate if expired
        var isExpired = seedSession is not null && (seedSession.FileExpiresAt is null || seedSession.FileExpiresAt < DateTimeOffset.UtcNow);
        if (isExpired)
        {
            await RehydrateSeedFileIfExpiredAsync(seedSession!, cancellationToken);
        }

        // Create seed if it doesn't exist
        if (seedSession is null)
        {
            var blobPrefix = $"content/{seedSessionId}";
            var savedBlobName = await storageService.SaveAsync(blobPrefix, fileBuffer, fileName, cancellationToken);
            fileBuffer.Position = 0;

            var upload = await aiService.UploadPdfAsync(seedSessionId, fileBuffer, fileName, systemInstruction, cancellationToken);

            seedSession = new ChatSession { Id = seedSessionId };
            seedSession.FileName = fileName;
            seedSession.MimeType = contentType;
            seedSession.StorageBlobName = savedBlobName;
            seedSession.ProviderFileId = upload.ProviderId;
            seedSession.ProviderCacheId = upload.CachedContentName;
            seedSession.FileUri = upload.FileUri;
            seedSession.FileExpiresAt = upload.ExpirationTime;
            seedSession.SystemInstruction = upload.SystemInstruction;
            seedSession.AccountId = null; // Shared
            seedSession.UpdatedAt = DateTimeOffset.UtcNow;
            await dataService.UpsertSessionAsync(seedSession, cancellationToken);
        }

        // Create/update the user's session, linking to the seed's file data
        if (userSessionId == Guid.Empty)
        {
            userSessionId = Guid.NewGuid();
        }

        var userSession = await dataService.GetSessionAsync(userSessionId, cancellationToken) ?? new ChatSession { Id = userSessionId };
        userSession.FileName = seedSession.FileName;
        userSession.MimeType = seedSession.MimeType;
        userSession.StorageBlobName = seedSession.StorageBlobName;
        userSession.ProviderFileId = seedSession.ProviderFileId;
        userSession.ProviderCacheId = seedSession.ProviderCacheId;
        userSession.FileUri = seedSession.FileUri;
        userSession.FileExpiresAt = seedSession.FileExpiresAt;
        userSession.SystemInstruction = systemInstruction ?? seedSession.SystemInstruction;
        userSession.AccountId = accountId;
        userSession.UpdatedAt = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(userSession.Title) || string.Equals(userSession.Title, "New chat", StringComparison.OrdinalIgnoreCase))
        {
            var title = await GenerateCinematicTitleAsync(aiService, userSession, fileName, cancellationToken);
            if (!string.IsNullOrWhiteSpace(title))
            {
                userSession.Title = title;
            }
        }
        await dataService.UpsertSessionAsync(userSession, cancellationToken);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            success = true,
            sessionId = userSession.Id,
            providerId = userSession.ProviderFileId,
            cacheName = userSession.ProviderCacheId,
            fileUri = userSession.FileUri
        }, cancellationToken);
        return response;
    }

    private static bool TryGetBoundary(HttpRequestData req, out string? boundary)
    {
        boundary = null;
        if (!req.Headers.TryGetValues("Content-Type", out var values))
        {
            return false;
        }
        var contentType = values.FirstOrDefault();
        if (string.IsNullOrEmpty(contentType))
        {
            return false;
        }
        if (!MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
        {
            return false;
        }
        boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        return !string.IsNullOrEmpty(boundary);
    }

    private static async Task<string?> GenerateCinematicTitleAsync(IGenerativeAiService aiService, ChatSession session, string seedText, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(seedText))
        {
            return null;
        }

        try
        {
            var prompt = $"Based on this runner's opening ask, ghost together a short mission codename. Keep it punchy, three words or less. Seed: {seedText}";
            var title = await aiService.GenerateTitleAsync(session, prompt, cancellationToken);
            title = title?.Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }
            // Strip surrounding quotes and limit length for UI safety
            title = title.Trim('"', '\'', '“', '”');
            return title.Length switch
            {
                > 60 => title[..60].Trim() + "…",
                _ => title
            };
        }
        catch
        {
            return null;
        }
    }

    private static Guid ComputeDeterministicGuid(string input)
    {
        using var md5 = MD5.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = md5.ComputeHash(bytes);
        return new Guid(hash);
    }

    private static Guid ComputeDeterministicGuid(Stream stream)
    {
        using var md5 = MD5.Create();
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }
        var hash = md5.ComputeHash(stream);
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }
        return new Guid(hash);
    }

    private static async Task<HttpResponseData> WriteErrorAsync(HttpRequestData req, HttpStatusCode statusCode, string message)
    {
        return await WriteErrorAsync(req, statusCode, new[] { message });
    }

    private static async Task<HttpResponseData> WriteErrorAsync(HttpRequestData req, HttpStatusCode statusCode, IEnumerable<string> messages)
    {
        var response = req.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(new { errors = messages });
        return response;
    }
}

