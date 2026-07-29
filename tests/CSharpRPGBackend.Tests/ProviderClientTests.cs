using System.Net;
using System.Text;
using System.Text.Json;
using CSharpRPGBackend.LLM;

namespace CSharpRPGBackend.Tests;

public class ProviderClientTests
{
    [Fact]
    public async Task GeminiUsesGenerateContentWithHeaderCredentialAndRoleMapping()
    {
        var handler = new RecordingHandler(
            """{"candidates":[{"content":{"parts":[{"text":"Gemini reply"}]}}]}""");
        using var httpClient = new HttpClient(handler);
        var client = new GeminiClient(
            "https://generativelanguage.googleapis.com/v1beta",
            "models/test-model",
            "test-secret",
            httpClient);

        var response = await client.ChatAsync(new List<ChatMessage>
        {
            new() { Role = "system", Content = "Narrate faithfully." },
            new() { Role = "user", Content = "Hello" },
            new() { Role = "assistant", Content = "Greetings" }
        });

        Assert.Equal("Gemini reply", response);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/models/test-model:generateContent",
            handler.RequestUri?.ToString());
        Assert.True(handler.Headers.ContainsKey("x-goog-api-key"));
        Assert.DoesNotContain("key=", handler.RequestUri?.Query ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        using var payload = JsonDocument.Parse(handler.Body!);
        Assert.Equal("Narrate faithfully.", payload.RootElement
            .GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal("user", payload.RootElement.GetProperty("contents")[0].GetProperty("role").GetString());
        Assert.Equal("model", payload.RootElement.GetProperty("contents")[1].GetProperty("role").GetString());
    }

    [Fact]
    public async Task OpenRouterUsesChatCompletionsBearerAuthAndAttributionHeaders()
    {
        var handler = new RecordingHandler(
            """{"choices":[{"message":{"content":"Router reply"}}]}""");
        using var httpClient = new HttpClient(handler);
        var client = new OpenRouterClient(
            "https://openrouter.ai/api/v1",
            "vendor/test-model",
            "test-secret",
            "https://example.test/rpg",
            "RPG Tests",
            httpClient);

        var response = await client.ChatAsync(new List<ChatMessage>
        {
            new() { Role = "user", Content = "Hello" }
        });

        Assert.Equal("Router reply", response);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", handler.RequestUri?.ToString());
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.False(string.IsNullOrWhiteSpace(handler.AuthorizationParameter));
        Assert.Equal("https://example.test/rpg", Assert.Single(handler.Headers["HTTP-Referer"]));
        Assert.Equal("RPG Tests", Assert.Single(handler.Headers["X-Title"]));

        using var payload = JsonDocument.Parse(handler.Body!);
        Assert.Equal("vendor/test-model", payload.RootElement.GetProperty("model").GetString());
        Assert.False(payload.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal("Hello", payload.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public RecordingHandler(string responseBody)
        {
            _responseBody = responseBody;
        }

        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? Body { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public Dictionary<string, string[]> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            Body = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            foreach (var header in request.Headers)
                Headers[header.Key] = header.Value.ToArray();

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
