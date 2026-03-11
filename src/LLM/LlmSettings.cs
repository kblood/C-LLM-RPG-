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
    public string Backend { get; set; } = "ollama";          // "ollama" | "llamacpp"

    [JsonPropertyName("model")]
    public string Model { get; set; } = "granite4:3b";

    [JsonPropertyName("ollamaUrl")]
    public string OllamaUrl { get; set; } = "http://localhost:11434";

    [JsonPropertyName("llamaCppUrl")]
    public string LlamaCppUrl { get; set; } = "http://localhost:8080";

    /// <summary>Context window size passed to both Ollama (num_ctx) and llama-server (--ctx-size).</summary>
    [JsonPropertyName("contextSize")]
    public int ContextSize { get; set; } = 8192;

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
    public ILlmClient CreateClient() => Backend.ToLowerInvariant() switch
    {
        "llamacpp" or "llama.cpp" or "llama_cpp"
            => new LlamaCppClient(LlamaCppUrl, Model),
        _   => new OllamaClient(OllamaUrl, Model, ContextSize)
    };

    public bool IsLlamaCpp => Backend.Equals("llamacpp", StringComparison.OrdinalIgnoreCase)
                           || Backend.Equals("llama.cpp",  StringComparison.OrdinalIgnoreCase)
                           || Backend.Equals("llama_cpp",  StringComparison.OrdinalIgnoreCase);

    /// <summary>Port number parsed from LlamaCppUrl (e.g. "http://localhost:8080" → 8080).</summary>
    public int LlamaCppPort
    {
        get
        {
            var m = System.Text.RegularExpressions.Regex.Match(LlamaCppUrl, @":(\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value) : 8080;
        }
    }

    /// <summary>One-line summary for the main menu header.</summary>
    public string Summary => IsLlamaCpp
        ? $"llama.cpp  [{LlamaCppUrl}]  model: {Model}  ctx:{ContextSize}"
        : $"Ollama     [{OllamaUrl}]  model: {Model}  ctx:{ContextSize}";
}
