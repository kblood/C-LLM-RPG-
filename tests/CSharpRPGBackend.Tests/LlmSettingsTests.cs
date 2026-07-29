using System.Text.Json;
using CSharpRPGBackend.LLM;

namespace CSharpRPGBackend.Tests;

public class LlmSettingsTests
{
    [Fact]
    public void DefaultsToOllamaAndCreatesEachSupportedBackend()
    {
        var settings = new LlmSettings();

        Assert.Equal("ollama", settings.Backend);
        Assert.True(settings.IsOllama);
        Assert.IsType<OllamaClient>(settings.CreateClient());

        settings.Backend = "llama.cpp";
        Assert.True(settings.IsLlamaCpp);
        Assert.IsType<LlamaCppClient>(settings.CreateClient());

        settings.Backend = "gemini";
        settings.GeminiModel = "gemini-test-model";
        var gemini = Assert.IsType<GeminiClient>(settings.CreateClient());
        Assert.Equal("gemini-test-model", gemini.DefaultModel);

        settings.Backend = "openrouter";
        settings.OpenRouterModel = "vendor/test-model";
        var openRouter = Assert.IsType<OpenRouterClient>(settings.CreateClient());
        Assert.Equal("vendor/test-model", openRouter.DefaultModel);

        settings.Backend = "unrecognized-backend";
        Assert.True(settings.IsOllama);
        Assert.IsType<OllamaClient>(settings.CreateClient());
    }

    [Fact]
    public void ApiKeysAreNeverSerializedOrDeserialized()
    {
        const string geminiSecret = "gemini-super-secret";
        const string openRouterSecret = "openrouter-super-secret";
        var settings = new LlmSettings
        {
            Backend = "openrouter",
            GeminiApiKey = geminiSecret,
            OpenRouterApiKey = openRouterSecret
        };

        var json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<LlmSettings>(json);

        Assert.DoesNotContain(geminiSecret, json, StringComparison.Ordinal);
        Assert.DoesNotContain(openRouterSecret, json, StringComparison.Ordinal);
        Assert.DoesNotContain("GeminiApiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OpenRouterApiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(restored);
        Assert.Null(restored.GeminiApiKey);
        Assert.Null(restored.OpenRouterApiKey);
    }

    [Fact]
    public void CreateRuntimeCopy_PreservesConfigurationWithoutSharingMutations()
    {
        var shared = new LlmSettings
        {
            Backend = "gemini",
            Model = "local-model",
            OllamaUrl = "http://localhost:12000",
            LlamaCppUrl = "http://localhost:12001",
            GeminiUrl = "https://gemini.example.test",
            GeminiModel = "gemini-session-model",
            GeminiApiKey = "runtime-gemini-key",
            OpenRouterUrl = "https://openrouter.example.test",
            OpenRouterModel = "vendor/model",
            OpenRouterApiKey = "runtime-openrouter-key",
            OpenRouterAppUrl = "https://rpg.example.test",
            OpenRouterAppName = "RPG Test",
            ContextSize = 16384
        };

        var session = shared.CreateRuntimeCopy();

        Assert.NotSame(shared, session);
        Assert.Equal(shared.Backend, session.Backend);
        Assert.Equal(shared.ActiveUrl, session.ActiveUrl);
        Assert.Equal(shared.ActiveModel, session.ActiveModel);
        Assert.Equal(shared.GeminiApiKey, session.GeminiApiKey);
        Assert.Equal(shared.OpenRouterApiKey, session.OpenRouterApiKey);
        Assert.Equal(shared.ContextSize, session.ContextSize);

        session.Backend = "openrouter";
        session.OpenRouterModel = "different/model";
        session.ContextSize = 4096;

        Assert.Equal("gemini", shared.Backend);
        Assert.Equal("vendor/model", shared.OpenRouterModel);
        Assert.Equal(16384, shared.ContextSize);
    }
}
