using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSharpRPGBackend.LLM;

/// <summary>
/// HTTP client for communicating with a local Ollama instance.
/// Supports both standard chat completions and streaming responses.
/// </summary>
public class OllamaClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _defaultModel;

    public OllamaClient(string baseUrl = "http://localhost:11434", string defaultModel = "mistral")
    {
        _baseUrl = baseUrl;
        _defaultModel = defaultModel;
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
            Stream = false
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
            Stream = true
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
}

public class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

public class OllamaChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; set; } = new();

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }
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

public class OllamaException : Exception
{
    public OllamaException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
