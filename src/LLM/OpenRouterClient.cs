using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSharpRPGBackend.LLM;

/// <summary>
/// Client for OpenRouter's OpenAI-compatible chat-completions API.
/// API keys are read from OPENROUTER_API_KEY first, with an optional in-memory
/// fallback supplied by the caller. Keys are never placed in request URLs.
/// </summary>
public sealed class OpenRouterClient : ILlmClient
{
    public const string DefaultBaseUrl = "https://openrouter.ai/api/v1";
    public const string DefaultModelName = "openrouter/auto";
    public const string ApiKeyEnvironmentVariable = "OPENROUTER_API_KEY";

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _defaultModel;
    private readonly string? _apiKey;
    private readonly string? _appUrl;
    private readonly string? _appName;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string BackendName => "OpenRouter";
    public string DefaultModel => _defaultModel;
    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

    public OpenRouterClient(
        string baseUrl = DefaultBaseUrl,
        string defaultModel = DefaultModelName,
        string? apiKey = null,
        string? appUrl = null,
        string? appName = "CSharpRPGBackend",
        HttpClient? httpClient = null)
    {
        _baseUrl = NormalizeBaseUrl(baseUrl);
        _defaultModel = string.IsNullOrWhiteSpace(defaultModel) ? DefaultModelName : defaultModel.Trim();
        _apiKey = ResolveApiKey(apiKey);
        _appUrl = string.IsNullOrWhiteSpace(appUrl) ? null : appUrl.Trim();
        _appName = string.IsNullOrWhiteSpace(appName) ? null : appName.Trim();
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

    public async Task<string> ChatAsync(List<ChatMessage> messages, string? model = null)
    {
        EnsureApiKey();
        var requestBody = BuildRequest(messages, model, stream: false);

        using var request = CreateRequest(HttpMethod.Post, $"{_baseUrl}/chat/completions");
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody, JsonOptions),
            Encoding.UTF8,
            "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request);
        }
        catch (Exception ex) when (ex is not OpenRouterException)
        {
            throw new OpenRouterException($"Could not reach the OpenRouter API: {ex.Message}", innerException: ex);
        }

        using (response)
        {
            var responseText = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw CreateApiException(response.StatusCode, response.ReasonPhrase, responseText);

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(responseText);
            }
            catch (JsonException ex)
            {
                throw new OpenRouterException("OpenRouter returned an invalid JSON response.", ex, response.StatusCode);
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.TryGetProperty("error", out var error))
                    throw CreateApiException(response.StatusCode, response.ReasonPhrase, error);

                if (TryGetFirstChoice(root, out var choice) &&
                    choice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var content))
                {
                    var text = ExtractContent(content);
                    if (!string.IsNullOrEmpty(text))
                        return text;
                }

                throw new OpenRouterException("OpenRouter returned no text content.", statusCode: response.StatusCode);
            }
        }
    }

    public IAsyncEnumerable<string> ChatStreamAsync(List<ChatMessage> messages, string? model = null)
        => ChatStreamAsync(messages, model, CancellationToken.None);

    /// <summary>
    /// Streams OpenRouter Server-Sent Events. This overload also allows callers
    /// that do not use <see cref="ILlmClient"/> directly to cancel the stream.
    /// </summary>
    public async IAsyncEnumerable<string> ChatStreamAsync(
        List<ChatMessage> messages,
        string? model,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EnsureApiKey();
        var requestBody = BuildRequest(messages, model, stream: true);

        using var request = CreateRequest(HttpMethod.Post, $"{_baseUrl}/chat/completions");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
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
        catch (Exception ex) when (ex is not OpenRouterException and not OperationCanceledException)
        {
            throw new OpenRouterException($"Could not open an OpenRouter response stream: {ex.Message}", innerException: ex);
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
                // OpenRouter may send SSE comments such as ": OPENROUTER PROCESSING".
                if (!line.StartsWith("data:", StringComparison.Ordinal))
                    continue;

                var data = line["data:".Length..].TrimStart();
                if (string.IsNullOrWhiteSpace(data))
                    continue;
                if (data.Equals("[DONE]", StringComparison.OrdinalIgnoreCase))
                    yield break;

                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(data);
                }
                catch (JsonException ex)
                {
                    throw new OpenRouterException("OpenRouter returned an invalid streaming event.", ex, response.StatusCode);
                }

                using (document)
                {
                    var root = document.RootElement;
                    if (root.TryGetProperty("error", out var error))
                        throw CreateApiException(response.StatusCode, response.ReasonPhrase, error);

                    if (TryGetFirstChoice(root, out var choice) &&
                        choice.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("content", out var content))
                    {
                        var text = ExtractContent(content);
                        if (!string.IsNullOrEmpty(text))
                            yield return text;
                    }
                }
            }
        }
    }

    public async Task<bool> IsHealthyAsync()
    {
        if (!HasApiKey)
            return false;

        try
        {
            using var request = CreateRequest(HttpMethod.Get, $"{_baseUrl}/models");
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
            using var request = CreateRequest(HttpMethod.Get, $"{_baseUrl}/models");
            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return new List<string>();

            var responseText = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseText);
            if (!document.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array)
                return new List<string>();

            return data.EnumerateArray()
                .Where(item => item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                .Select(item => item.GetProperty("id").GetString())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();
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
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (_appUrl is not null)
            request.Headers.TryAddWithoutValidation("HTTP-Referer", _appUrl);
        if (_appName is not null)
            request.Headers.TryAddWithoutValidation("X-Title", _appName);

        return request;
    }

    private OpenRouterChatRequest BuildRequest(List<ChatMessage> messages, string? model, bool stream)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0)
            throw new ArgumentException("At least one chat message is required.", nameof(messages));

        return new OpenRouterChatRequest
        {
            Model = string.IsNullOrWhiteSpace(model) ? _defaultModel : model.Trim(),
            Messages = messages,
            Stream = stream
        };
    }

    private static bool TryGetFirstChoice(JsonElement root, out JsonElement choice)
    {
        choice = default;
        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
            return false;

        choice = choices[0];
        return choice.ValueKind == JsonValueKind.Object;
    }

    private static string ExtractContent(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
            return content.GetString() ?? string.Empty;
        if (content.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var text = new StringBuilder();
        foreach (var part in content.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.String)
            {
                text.Append(part.GetString());
                continue;
            }

            if (part.ValueKind == JsonValueKind.Object &&
                part.TryGetProperty("text", out var partText) &&
                partText.ValueKind == JsonValueKind.String)
                text.Append(partText.GetString());
        }

        return text.ToString();
    }

    private OpenRouterException CreateApiException(
        HttpStatusCode statusCode,
        string? reasonPhrase,
        string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("error", out var error))
                return CreateApiException(statusCode, reasonPhrase, error);
        }
        catch (JsonException)
        {
            // Fall back to a small, sanitized response excerpt below.
        }

        var detail = Sanitize(responseBody);
        var message = $"OpenRouter API request failed ({(int)statusCode} {reasonPhrase}).";
        if (!string.IsNullOrWhiteSpace(detail))
            message += $" {detail}";
        return new OpenRouterException(message, statusCode: statusCode);
    }

    private OpenRouterException CreateApiException(
        HttpStatusCode statusCode,
        string? reasonPhrase,
        JsonElement error)
    {
        string? detail = null;
        string? errorCode = null;

        if (error.ValueKind == JsonValueKind.Object)
        {
            if (error.TryGetProperty("message", out var messageElement) &&
                messageElement.ValueKind == JsonValueKind.String)
                detail = messageElement.GetString();
            if (error.TryGetProperty("code", out var codeElement))
                errorCode = codeElement.ToString();
        }
        else if (error.ValueKind == JsonValueKind.String)
        {
            detail = error.GetString();
        }

        var message = $"OpenRouter API request failed ({(int)statusCode} {reasonPhrase}).";
        detail = Sanitize(detail);
        if (!string.IsNullOrWhiteSpace(detail))
            message += $" {detail}";
        return new OpenRouterException(message, statusCode: statusCode, providerErrorCode: errorCode);
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
            throw new OpenRouterException(
                $"OpenRouter API key is not configured. Set {ApiKeyEnvironmentVariable} or provide an in-memory key.",
                providerErrorCode: "missing_api_key");
        }

        if (_apiKey!.Contains('\r') || _apiKey.Contains('\n'))
        {
            throw new OpenRouterException(
                "OpenRouter API key contains invalid newline characters.",
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
            throw new ArgumentException("OpenRouter base URL must be an absolute HTTP or HTTPS URL.", nameof(baseUrl));
        if (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
            throw new ArgumentException("OpenRouter base URL must use HTTPS unless it targets the local machine.", nameof(baseUrl));
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("OpenRouter base URL cannot contain a query string or fragment.", nameof(baseUrl));

        return value;
    }
}

internal sealed class OpenRouterChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; set; } = new();

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }
}

public sealed class OpenRouterException : LlmClientException
{
    public OpenRouterException(
        string message,
        Exception? innerException = null,
        HttpStatusCode? statusCode = null,
        string? providerErrorCode = null)
        : base(message, innerException, statusCode, providerErrorCode)
    {
    }
}
