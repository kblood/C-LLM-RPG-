using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSharpRPGBackend.LLM;

/// <summary>
/// Client for Google's Gemini REST API.
/// API keys are read from GEMINI_API_KEY first, with an optional in-memory
/// fallback supplied by the caller. Keys are sent in the x-goog-api-key header.
/// </summary>
public sealed class GeminiClient : ILlmClient
{
    public const string DefaultBaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    public const string DefaultModelName = "gemini-3.6-flash";
    public const string ApiKeyEnvironmentVariable = "GEMINI_API_KEY";

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _defaultModel;
    private readonly string? _apiKey;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string BackendName => "Gemini";
    public string DefaultModel => _defaultModel;
    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

    public GeminiClient(
        string baseUrl = DefaultBaseUrl,
        string defaultModel = DefaultModelName,
        string? apiKey = null,
        HttpClient? httpClient = null)
    {
        _baseUrl = NormalizeBaseUrl(baseUrl);
        _defaultModel = NormalizeModel(defaultModel);
        _apiKey = ResolveApiKey(apiKey);
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

    public async Task<string> ChatAsync(List<ChatMessage> messages, string? model = null)
    {
        EnsureApiKey();
        var requestBody = BuildRequest(messages);
        var endpoint = BuildModelEndpoint(model, "generateContent");

        using var request = CreateRequest(HttpMethod.Post, endpoint);
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody, JsonOptions),
            Encoding.UTF8,
            "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request);
        }
        catch (Exception ex) when (ex is not GeminiException)
        {
            throw new GeminiException($"Could not reach the Gemini API: {ex.Message}", innerException: ex);
        }

        using (response)
        {
            var responseText = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw CreateApiException(response.StatusCode, response.ReasonPhrase, responseText);

            GeminiGenerateResponse? completion;
            try
            {
                completion = JsonSerializer.Deserialize<GeminiGenerateResponse>(responseText, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new GeminiException("Gemini returned an invalid JSON response.", ex, response.StatusCode);
            }

            if (completion?.Error is not null)
                throw CreateApiException(response.StatusCode, response.ReasonPhrase, completion.Error);

            return ExtractCompletionText(completion);
        }
    }

    public IAsyncEnumerable<string> ChatStreamAsync(List<ChatMessage> messages, string? model = null)
        => ChatStreamAsync(messages, model, CancellationToken.None);

    /// <summary>
    /// Streams Gemini Server-Sent Events. This overload also allows callers that
    /// do not use <see cref="ILlmClient"/> directly to cancel the HTTP stream.
    /// </summary>
    public async IAsyncEnumerable<string> ChatStreamAsync(
        List<ChatMessage> messages,
        string? model,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EnsureApiKey();
        var requestBody = BuildRequest(messages);
        var endpoint = $"{BuildModelEndpoint(model, "streamGenerateContent")}?alt=sse";

        using var request = CreateRequest(HttpMethod.Post, endpoint);
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody, JsonOptions),
            Encoding.UTF8,
            "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not GeminiException and not OperationCanceledException)
        {
            throw new GeminiException($"Could not open a Gemini response stream: {ex.Message}", innerException: ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw CreateApiException(response.StatusCode, response.ReasonPhrase, errorBody);
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal))
                    continue;

                var data = line["data:".Length..].TrimStart();
                if (string.IsNullOrWhiteSpace(data))
                    continue;
                if (data.Equals("[DONE]", StringComparison.OrdinalIgnoreCase))
                    yield break;

                GeminiGenerateResponse? chunk;
                try
                {
                    chunk = JsonSerializer.Deserialize<GeminiGenerateResponse>(data, JsonOptions);
                }
                catch (JsonException ex)
                {
                    throw new GeminiException("Gemini returned an invalid streaming event.", ex, response.StatusCode);
                }

                if (chunk?.Error is not null)
                    throw CreateApiException(response.StatusCode, response.ReasonPhrase, chunk.Error);

                foreach (var text in ExtractTextParts(chunk))
                    yield return text;
            }
        }
    }

    public async Task<bool> IsHealthyAsync()
    {
        if (!HasApiKey)
            return false;

        try
        {
            using var request = CreateRequest(HttpMethod.Get, $"{_baseUrl}/models?pageSize=1");
            using var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<string>> ListModelsAsync()
    {
        if (!HasApiKey)
            return new List<string>();

        try
        {
            var models = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? pageToken = null;

            // The API is paginated. The cap prevents a faulty endpoint from
            // keeping the menu/model picker in an endless page-token loop.
            for (var page = 0; page < 20; page++)
            {
                var endpoint = $"{_baseUrl}/models?pageSize=1000";
                if (!string.IsNullOrWhiteSpace(pageToken))
                    endpoint += $"&pageToken={Uri.EscapeDataString(pageToken)}";

                using var request = CreateRequest(HttpMethod.Get, endpoint);
                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    return new List<string>();

                var responseText = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<GeminiModelListResponse>(responseText, JsonOptions);

                foreach (var item in result?.Models ?? Enumerable.Empty<GeminiModelInfo>())
                {
                    if (string.IsNullOrWhiteSpace(item.Name))
                        continue;
                    if (item.SupportedGenerationMethods is { Count: > 0 } methods &&
                        !methods.Contains("generateContent", StringComparer.OrdinalIgnoreCase))
                        continue;

                    models.Add(NormalizeModel(item.Name));
                }

                pageToken = result?.NextPageToken;
                if (string.IsNullOrWhiteSpace(pageToken))
                    break;
            }

            return models.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch
        {
            // Model discovery is optional according to ILlmClient.
            return new List<string>();
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string endpoint)
    {
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.TryAddWithoutValidation("x-goog-api-key", _apiKey);
        return request;
    }

    private string BuildModelEndpoint(string? model, string operation)
    {
        var modelName = NormalizeModel(string.IsNullOrWhiteSpace(model) ? _defaultModel : model);
        return $"{_baseUrl}/models/{Uri.EscapeDataString(modelName)}:{operation}";
    }

    private static GeminiGenerateRequest BuildRequest(List<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0)
            throw new ArgumentException("At least one chat message is required.", nameof(messages));

        var systemParts = new List<GeminiPart>();
        var contents = new List<GeminiContent>();

        foreach (var message in messages)
        {
            if (message is null)
                continue;

            var role = message.Role?.Trim().ToLowerInvariant();
            if (role == "system")
            {
                if (!string.IsNullOrEmpty(message.Content))
                    systemParts.Add(new GeminiPart { Text = message.Content });
                continue;
            }

            contents.Add(new GeminiContent
            {
                Role = role is "assistant" or "model" ? "model" : "user",
                Parts = new List<GeminiPart>
                {
                    new() { Text = message.Content ?? string.Empty }
                }
            });
        }

        if (contents.Count == 0)
            throw new ArgumentException("At least one non-system chat message is required by Gemini.", nameof(messages));

        return new GeminiGenerateRequest
        {
            Contents = contents,
            SystemInstruction = systemParts.Count == 0
                ? null
                : new GeminiContent { Parts = systemParts }
        };
    }

    private static string ExtractCompletionText(GeminiGenerateResponse? response)
    {
        var parts = ExtractTextParts(response).ToList();
        if (parts.Count > 0)
            return string.Concat(parts);

        var blockReason = response?.PromptFeedback?.BlockReason;
        var finishReason = response?.Candidates?.FirstOrDefault()?.FinishReason;
        var reason = !string.IsNullOrWhiteSpace(blockReason)
            ? $" The prompt was blocked ({blockReason})."
            : !string.IsNullOrWhiteSpace(finishReason)
                ? $" The candidate ended with {finishReason}."
                : string.Empty;

        throw new GeminiException($"Gemini returned no text content.{reason}");
    }

    private static IEnumerable<string> ExtractTextParts(GeminiGenerateResponse? response)
    {
        var parts = response?.Candidates?.FirstOrDefault()?.Content?.Parts;
        if (parts is null)
            yield break;

        foreach (var part in parts)
            if (!string.IsNullOrEmpty(part.Text))
                yield return part.Text;
    }

    private GeminiException CreateApiException(HttpStatusCode statusCode, string? reasonPhrase, string responseBody)
    {
        GeminiError? error = null;
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("error", out var errorElement))
                error = errorElement.Deserialize<GeminiError>(JsonOptions);
        }
        catch (JsonException)
        {
            // Fall back to a small, sanitized response excerpt below.
        }

        if (error is not null)
            return CreateApiException(statusCode, reasonPhrase, error);

        var detail = Sanitize(responseBody);
        var message = $"Gemini API request failed ({(int)statusCode} {reasonPhrase}).";
        if (!string.IsNullOrWhiteSpace(detail))
            message += $" {detail}";
        return new GeminiException(message, statusCode: statusCode);
    }

    private GeminiException CreateApiException(HttpStatusCode statusCode, string? reasonPhrase, GeminiError error)
    {
        var detail = Sanitize(error.Message);
        var message = $"Gemini API request failed ({(int)statusCode} {reasonPhrase}).";
        if (!string.IsNullOrWhiteSpace(detail))
            message += $" {detail}";
        return new GeminiException(message, statusCode: statusCode, providerErrorCode: error.Status);
    }

    private string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var safe = value;
        if (!string.IsNullOrEmpty(_apiKey))
            safe = safe.Replace(_apiKey, "[redacted]", StringComparison.Ordinal);
        safe = safe.ReplaceLineEndings(" ").Trim();
        return safe.Length <= 1000 ? safe : safe[..1000] + "…";
    }

    private void EnsureApiKey()
    {
        if (!HasApiKey)
        {
            throw new GeminiException(
                $"Gemini API key is not configured. Set {ApiKeyEnvironmentVariable} or provide an in-memory key.",
                providerErrorCode: "missing_api_key");
        }

        if (_apiKey!.Contains('\r') || _apiKey.Contains('\n'))
        {
            throw new GeminiException(
                "Gemini API key contains invalid newline characters.",
                providerErrorCode: "invalid_api_key");
        }
    }

    private static string? ResolveApiKey(string? runtimeApiKey)
    {
        var environmentApiKey = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
        return !string.IsNullOrWhiteSpace(environmentApiKey)
            ? environmentApiKey.Trim()
            : string.IsNullOrWhiteSpace(runtimeApiKey) ? null : runtimeApiKey.Trim();
    }

    private static string NormalizeBaseUrl(string? baseUrl)
    {
        var value = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new ArgumentException("Gemini base URL must be an absolute HTTP or HTTPS URL.", nameof(baseUrl));
        if (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
            throw new ArgumentException("Gemini base URL must use HTTPS unless it targets the local machine.", nameof(baseUrl));
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Gemini base URL cannot contain a query string or fragment.", nameof(baseUrl));

        return value;
    }

    private static string NormalizeModel(string? model)
    {
        var value = string.IsNullOrWhiteSpace(model) ? DefaultModelName : model.Trim();
        return value.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            ? value["models/".Length..]
            : value;
    }
}

internal sealed class GeminiGenerateRequest
{
    [JsonPropertyName("contents")]
    public List<GeminiContent> Contents { get; set; } = new();

    [JsonPropertyName("systemInstruction")]
    public GeminiContent? SystemInstruction { get; set; }
}

internal sealed class GeminiContent
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("parts")]
    public List<GeminiPart> Parts { get; set; } = new();
}

internal sealed class GeminiPart
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

internal sealed class GeminiGenerateResponse
{
    [JsonPropertyName("candidates")]
    public List<GeminiCandidate>? Candidates { get; set; }

    [JsonPropertyName("promptFeedback")]
    public GeminiPromptFeedback? PromptFeedback { get; set; }

    [JsonPropertyName("error")]
    public GeminiError? Error { get; set; }
}

internal sealed class GeminiCandidate
{
    [JsonPropertyName("content")]
    public GeminiContent? Content { get; set; }

    [JsonPropertyName("finishReason")]
    public string? FinishReason { get; set; }
}

internal sealed class GeminiPromptFeedback
{
    [JsonPropertyName("blockReason")]
    public string? BlockReason { get; set; }
}

internal sealed class GeminiError
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

internal sealed class GeminiModelListResponse
{
    [JsonPropertyName("models")]
    public List<GeminiModelInfo>? Models { get; set; }

    [JsonPropertyName("nextPageToken")]
    public string? NextPageToken { get; set; }
}

internal sealed class GeminiModelInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("supportedGenerationMethods")]
    public List<string>? SupportedGenerationMethods { get; set; }
}

public sealed class GeminiException : LlmClientException
{
    public GeminiException(
        string message,
        Exception? innerException = null,
        HttpStatusCode? statusCode = null,
        string? providerErrorCode = null)
        : base(message, innerException, statusCode, providerErrorCode)
    {
    }
}
