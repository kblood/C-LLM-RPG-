using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSharpRPGBackend.LLM;

/// <summary>
/// HTTP client for communicating with a local Ollama instance.
/// Supports both standard chat completions and streaming responses.
/// </summary>
public class OllamaClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _defaultModel;
    private readonly int _contextSize;

    public string BackendName => "Ollama";
    public string DefaultModel => _defaultModel;

    public OllamaClient(string baseUrl = "http://localhost:11434", string defaultModel = "mistral", int contextSize = 8192)
    {
        _baseUrl = baseUrl;
        _defaultModel = defaultModel;
        _contextSize = contextSize;
        _httpClient = new HttpClient();
    }

    /// <summary>
    /// Send a chat request to Ollama and get a response.
    /// </summary>
    public async Task<string> ChatAsync(List<ChatMessage> messages, string? model = null)
    {
        model ??= _defaultModel;

        var request = new OllamaChatRequest
        {
            Model = model,
            Messages = messages,
            Stream = false,
            Options = new OllamaOptions { NumCtx = _contextSize }
        };

        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(request),
                System.Text.Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/chat", content);
            response.EnsureSuccessStatusCode();

            var responseText = await response.Content.ReadAsStringAsync();
            var chatResponse = JsonSerializer.Deserialize<OllamaChatResponse>(responseText);

            return chatResponse?.Message?.Content ?? string.Empty;
        }
        catch (Exception ex)
        {
            throw new OllamaException($"Error communicating with Ollama: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Stream a chat response from Ollama (useful for long responses).
    /// </summary>
    public async IAsyncEnumerable<string> ChatStreamAsync(
        List<ChatMessage> messages,
        string? model = null)
    {
        model ??= _defaultModel;

        var request = new OllamaChatRequest
        {
            Model = model,
            Messages = messages,
            Stream = true,
            Options = new OllamaOptions { NumCtx = _contextSize }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request),
            System.Text.Encoding.UTF8,
            "application/json"
        );

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/chat")
        {
            Content = content
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            throw new OllamaException($"Error streaming from Ollama: {ex.Message}", ex);
        }

        using (response)
        using (var stream = await response.Content.ReadAsStreamAsync())
        using (var reader = new StreamReader(stream))
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var chatResponse = JsonSerializer.Deserialize<OllamaChatResponse>(line);
                if (chatResponse?.Message?.Content != null)
                {
                    yield return chatResponse.Message.Content;
                }
            }
        }
    }

    /// <summary>
    /// Check if Ollama is running and accessible.
    /// </summary>
    public async Task<bool> IsHealthyAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/tags");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Return the list of models that Ollama currently has pulled.
    /// </summary>
    public async Task<List<string>> ListModelsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/tags");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var doc  = JsonDocument.Parse(json);
            var result = new List<string>();
            if (doc.RootElement.TryGetProperty("models", out var models))
                foreach (var m in models.EnumerateArray())
                    if (m.TryGetProperty("name", out var n))
                        result.Add(n.GetString() ?? string.Empty);
            return result;
        }
        catch
        {
            return new List<string>();
        }
    }
}

public class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

public class OllamaOptions
{
    [JsonPropertyName("num_ctx")]
    public int NumCtx { get; set; } = 8192;
}

public class OllamaChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; set; } = new();

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    [JsonPropertyName("options")]
    public OllamaOptions? Options { get; set; }
}

public class OllamaChatResponse
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("message")]
    public ChatMessage? Message { get; set; }

    [JsonPropertyName("done")]
    public bool Done { get; set; }
}

public class OllamaException : LlmClientException
{
    public OllamaException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
