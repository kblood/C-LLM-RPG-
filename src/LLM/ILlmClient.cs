namespace CSharpRPGBackend.LLM;

/// <summary>
/// Abstraction over any LLM backend (Ollama, llama.cpp server, OpenAI, etc.).
/// Allows the game to swap backends without changing game logic.
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// Send a chat request and receive the full response as a string.
    /// </summary>
    Task<string> ChatAsync(List<ChatMessage> messages, string? model = null);

    /// <summary>
    /// Stream a chat response chunk by chunk.
    /// Useful for long narrative responses.
    /// </summary>
    IAsyncEnumerable<string> ChatStreamAsync(List<ChatMessage> messages, string? model = null);

    /// <summary>
    /// Check whether the backend is running and reachable.
    /// </summary>
    Task<bool> IsHealthyAsync();

    /// <summary>
    /// Human-readable name of this backend (e.g. "Ollama", "llama.cpp").
    /// </summary>
    string BackendName { get; }

    /// <summary>
    /// The model that will be used when no explicit model is passed to ChatAsync.
    /// </summary>
    string DefaultModel { get; }

    /// <summary>
    /// Return the list of model names available on this backend.
    /// Returns an empty list when the backend is unreachable or does not support listing.
    /// </summary>
    Task<List<string>> ListModelsAsync();
}
