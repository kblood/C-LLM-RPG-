using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSharpRPGBackend.LLM;

/// <summary>
/// Persisted LLM backend settings saved to llm-settings.json next to the executable.
/// </summary>
public class LlmSettings
{
    // ── Persisted fields ──────────────────────────────────────────────────────

    [JsonPropertyName("backend")]
    public string Backend { get; set; } = "ollama"; // Ollama remains the default backend.

    [JsonPropertyName("model")]
    public string Model { get; set; } = "granite4:3b";

    [JsonPropertyName("ollamaUrl")]
    public string OllamaUrl { get; set; } = "http://localhost:11434";

    [JsonPropertyName("llamaCppUrl")]
    public string LlamaCppUrl { get; set; } = "http://localhost:8080";

    [JsonPropertyName("geminiUrl")]
    public string GeminiUrl { get; set; } = GeminiClient.DefaultBaseUrl;

    [JsonPropertyName("geminiModel")]
    public string GeminiModel { get; set; } = GeminiClient.DefaultModelName;

    [JsonPropertyName("openRouterUrl")]
    public string OpenRouterUrl { get; set; } = OpenRouterClient.DefaultBaseUrl;

    [JsonPropertyName("openRouterModel")]
    public string OpenRouterModel { get; set; } = OpenRouterClient.DefaultModelName;

    /// <summary>Optional OpenRouter attribution URL sent as HTTP-Referer.</summary>
    [JsonPropertyName("openRouterAppUrl")]
    public string? OpenRouterAppUrl { get; set; }

    /// <summary>Optional OpenRouter attribution title.</summary>
    [JsonPropertyName("openRouterAppName")]
    public string? OpenRouterAppName { get; set; } = "CSharpRPGBackend";

    /// <summary>Context window size passed to both Ollama (num_ctx) and llama-server (--ctx-size).</summary>
    [JsonPropertyName("contextSize")]
    public int ContextSize { get; set; } = 8192;

    // Runtime-only credential fallbacks. Clients prefer their environment
    // variables, and these properties are explicitly excluded from both save
    // and load so llm-settings.json can never become a credential store.
    [JsonIgnore]
    public string? GeminiApiKey { get; set; }

    [JsonIgnore]
    public string? OpenRouterApiKey { get; set; }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static string SettingsPath =>
        Path.Combine(AppContext.BaseDirectory, "llm-settings.json");

    /// <summary>Load from disk, or return defaults if the file does not exist.</summary>
    public static LlmSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<LlmSettings>(json, _jsonOptions) ?? new LlmSettings();
            }
        }
        catch
        {
            // Ignore corrupt file – start with defaults
        }
        return new LlmSettings();
    }

    /// <summary>Persist to disk.</summary>
    public void Save()
    {
        try
        {
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, _jsonOptions));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Settings] Warning: could not save settings – {ex.Message}");
        }
    }

    /// <summary>Build the right ILlmClient from the current settings.</summary>
    public ILlmClient CreateClient() => NormalizedBackend switch
    {
        "llamacpp" or "llama.cpp" or "llama_cpp"
            => new LlamaCppClient(LlamaCppUrl, Model),
        "gemini" or "google" or "google-gemini"
            => new GeminiClient(GeminiUrl, GeminiModel, GeminiApiKey),
        "openrouter" or "open-router" or "open_router"
            => new OpenRouterClient(
                OpenRouterUrl,
                OpenRouterModel,
                OpenRouterApiKey,
                OpenRouterAppUrl,
                OpenRouterAppName),
        _ => new OllamaClient(OllamaUrl, Model, ContextSize)
    };

    /// <summary>
    /// Creates an independent in-memory copy, including runtime-only credentials.
    /// This is useful for per-session overrides that must not mutate or persist the
    /// server's shared configuration.
    /// </summary>
    public LlmSettings CreateRuntimeCopy() => new()
    {
        Backend = Backend,
        Model = Model,
        OllamaUrl = OllamaUrl,
        LlamaCppUrl = LlamaCppUrl,
        GeminiUrl = GeminiUrl,
        GeminiModel = GeminiModel,
        OpenRouterUrl = OpenRouterUrl,
        OpenRouterModel = OpenRouterModel,
        OpenRouterAppUrl = OpenRouterAppUrl,
        OpenRouterAppName = OpenRouterAppName,
        ContextSize = ContextSize,
        GeminiApiKey = GeminiApiKey,
        OpenRouterApiKey = OpenRouterApiKey
    };

    public bool IsLlamaCpp => NormalizedBackend is "llamacpp" or "llama.cpp" or "llama_cpp";
    public bool IsGemini => NormalizedBackend is "gemini" or "google" or "google-gemini";
    public bool IsOpenRouter => NormalizedBackend is "openrouter" or "open-router" or "open_router";
    public bool IsOllama => !IsLlamaCpp && !IsGemini && !IsOpenRouter;

    /// <summary>The endpoint used by the selected backend.</summary>
    [JsonIgnore]
    public string ActiveUrl => IsLlamaCpp
        ? LlamaCppUrl
        : IsGemini
            ? GeminiUrl
            : IsOpenRouter
                ? OpenRouterUrl
                : OllamaUrl;

    /// <summary>The model used by the selected backend.</summary>
    [JsonIgnore]
    public string ActiveModel => IsGemini
        ? GeminiModel
        : IsOpenRouter
            ? OpenRouterModel
            : Model;

    /// <summary>Port number parsed from LlamaCppUrl (e.g. "http://localhost:8080" → 8080).</summary>
    public int LlamaCppPort
    {
        get
        {
            var m = System.Text.RegularExpressions.Regex.Match(LlamaCppUrl ?? string.Empty, @":(\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value) : 8080;
        }
    }

    /// <summary>One-line summary for the main menu header.</summary>
    public string Summary => IsLlamaCpp
        ? $"llama.cpp  [{LlamaCppUrl}]  model: {Model}  ctx:{ContextSize}"
        : IsGemini
            ? $"Gemini     [{GeminiUrl}]  model: {GeminiModel}"
            : IsOpenRouter
                ? $"OpenRouter [{OpenRouterUrl}]  model: {OpenRouterModel}"
                : $"Ollama     [{OllamaUrl}]  model: {Model}  ctx:{ContextSize}";

    private string NormalizedBackend => (Backend ?? string.Empty).Trim().ToLowerInvariant();
}
