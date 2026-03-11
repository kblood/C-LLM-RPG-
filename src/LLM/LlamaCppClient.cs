using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSharpRPGBackend.LLM;

/// <summary>
/// HTTP client for llama.cpp's built-in HTTP server (llama-server / server example).
/// The server exposes an OpenAI-compatible /v1/chat/completions endpoint so the
/// same Chat API used for Ollama works here too.
///
/// Quick start:
///   llama-server --model /path/to/model.gguf --port 8080
///
/// To use a model already downloaded by Ollama, call
///   LlamaCppClient.FindOllamaModelPath("granite4:3b")
/// to get the GGUF blob path and pass it to llama-server's --model flag.
/// </summary>
public class LlamaCppClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _defaultModel;

    public string BackendName => "llama.cpp";
    public string DefaultModel => _defaultModel;

    /// <param name="baseUrl">Base URL of the llama-server instance, e.g. http://localhost:8080</param>
    /// <param name="defaultModel">
    ///   Model label sent in every request.  llama-server ignores this field (it uses
    ///   whichever model was loaded at startup), but the value is kept for display purposes.
    /// </param>
    public LlamaCppClient(string baseUrl = "http://localhost:8080", string defaultModel = "llama.cpp")
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _defaultModel = defaultModel;
        _httpClient = new HttpClient
        {
            // llama-server can be slow with large prompts – give it a generous timeout
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

    // -------------------------------------------------------------------------
    // ILlmClient implementation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Send a chat request to llama-server and return the full response text.
    /// Uses the OpenAI-compatible POST /v1/chat/completions endpoint.
    /// </summary>
    public async Task<string> ChatAsync(List<ChatMessage> messages, string? model = null)
    {
        var request = BuildRequest(messages, model, stream: false);

        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(request, _jsonOptions),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync($"{_baseUrl}/v1/chat/completions", content);
            response.EnsureSuccessStatusCode();

            var responseText = await response.Content.ReadAsStringAsync();
            var completion = JsonSerializer.Deserialize<OpenAiChatResponse>(responseText, _jsonOptions);

            return completion?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
        }
        catch (Exception ex) when (ex is not LlamaCppException)
        {
            throw new LlamaCppException($"Error communicating with llama.cpp server: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Stream a chat response from llama-server using server-sent events (SSE).
    /// </summary>
    public async IAsyncEnumerable<string> ChatStreamAsync(
        List<ChatMessage> messages,
        string? model = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = BuildRequest(messages, model, stream: true);

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/chat/completions")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(request, _jsonOptions),
                Encoding.UTF8,
                "application/json")
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (ex is not LlamaCppException)
        {
            throw new LlamaCppException($"Error streaming from llama.cpp server: {ex.Message}", ex);
        }

        using (response)
        using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
        using (var reader = new StreamReader(stream))
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // SSE lines start with "data: "
                if (!line.StartsWith("data: ", StringComparison.Ordinal))
                    continue;

                var data = line["data: ".Length..];

                // "[DONE]" signals the end of the stream
                if (data.Equals("[DONE]", StringComparison.OrdinalIgnoreCase))
                    yield break;

                OpenAiChatResponse? chunk;
                try
                {
                    chunk = JsonSerializer.Deserialize<OpenAiChatResponse>(data, _jsonOptions);
                }
                catch
                {
                    continue; // Skip malformed lines
                }

                var delta = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
                if (delta != null)
                    yield return delta;
            }
        }
    }

    // IAsyncEnumerable without CancellationToken (required by ILlmClient)
    public IAsyncEnumerable<string> ChatStreamAsync(List<ChatMessage> messages, string? model = null)
        => ChatStreamAsync(messages, model, CancellationToken.None);

    /// <summary>
    /// Check whether the llama-server is reachable by calling GET /health.
    /// </summary>
    public async Task<bool> IsHealthyAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Return the models available via the OpenAI-compatible GET /v1/models endpoint.
    /// Falls back to the locally discovered Ollama model list when the server is offline.
    /// </summary>
    public async Task<List<string>> ListModelsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/v1/models");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var doc  = JsonDocument.Parse(json);
            var result = new List<string>();
            if (doc.RootElement.TryGetProperty("data", out var data))
                foreach (var m in data.EnumerateArray())
                    if (m.TryGetProperty("id", out var id))
                        result.Add(id.GetString() ?? string.Empty);
            return result;
        }
        catch
        {
            // Server offline – return locally installed Ollama models as suggestions
            return ListOllamaModels().ToList();
        }
    }

    // -------------------------------------------------------------------------
    // Server lifecycle helpers
    // -------------------------------------------------------------------------

    private static Process? _serverProcess;

    /// <summary>
    /// Find the llama-server executable in PATH or common install locations.
    /// </summary>
    public static string? FindLlamaServerExe()
    {
        // 1. Check PATH
        var ext      = OperatingSystem.IsWindows() ? ".exe" : "";
        var fullName = "llama-server" + ext;
        var pathVar  = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(dir.Trim(), fullName);
            if (File.Exists(candidate)) return candidate;
        }

        // 2. Common manual / winget install locations (Windows)
        var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var home     = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // winget installs to %LOCALAPPDATA%\Microsoft\WinGet\Packages\ggml.llamacpp_*\
        var wingetBase = Path.Combine(localApp, "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(wingetBase))
        {
            foreach (var dir in Directory.EnumerateDirectories(wingetBase, "ggml.llamacpp_*"))
            {
                var candidate = Path.Combine(dir, fullName);
                if (File.Exists(candidate)) return candidate;
            }
        }

        var roots = new[]
        {
            Path.Combine(localApp, "Programs", "llama.cpp"),
            Path.Combine(localApp, "llama.cpp"),
            @"C:\llama.cpp",
            @"C:\llama-server",
            Path.Combine(home, "llama.cpp"),
        };
        foreach (var root in roots)
        foreach (var sub in new[] { "", "build\\bin", "bin" })
        {
            var candidate = Path.Combine(root, sub, fullName);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    /// <summary>
    /// Start llama-server with the given model/port/context, killing any existing
    /// tracked or port-occupying instance first.
    /// Returns (true, null) on success or (false, errorMessage) on failure.
    /// </summary>
    public static (bool started, string? error) LaunchServer(string modelTag, int port, int ctxSize)
    {
        var blobPath = FindOllamaModelPath(modelTag);
        if (blobPath == null)
            return (false, $"Model '{modelTag}' not found in Ollama store. Run: ollama pull {modelTag}");

        var exe = FindLlamaServerExe();
        if (exe == null)
            return (false, "llama-server not found. Install via: winget install ggml.llamacpp");

        // Kill any existing server (tracked process or anything occupying the port)
        StopServer(port);
        // Brief pause so the OS releases the port before we bind again
        System.Threading.Thread.Sleep(500);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = exe,
                Arguments              = $"--model \"{blobPath}\" --port {port} --ctx-size {ctxSize}",
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };
            _serverProcess = Process.Start(psi);
            if (_serverProcess == null)
                return (false, "Process.Start returned null.");

            // Give it 500 ms to fail fast (bad model path, CUDA error, etc.)
            _serverProcess.WaitForExit(500);
            if (_serverProcess.HasExited)
            {
                var stderr = _serverProcess.StandardError.ReadToEnd().Trim();
                var stdout = _serverProcess.StandardOutput.ReadToEnd().Trim();
                var detail = !string.IsNullOrEmpty(stderr) ? stderr
                           : !string.IsNullOrEmpty(stdout) ? stdout
                           : $"exit code {_serverProcess.ExitCode}";
                return (false, $"Process exited immediately: {detail}");
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Could not start llama-server: {ex.Message}");
        }
    }

    /// <summary>
    /// Kill the tracked llama-server process. Optionally also kills anything else
    /// listening on <paramref name="port"/>.
    /// </summary>
    public static void StopServer(int port = 0)
    {
        if (_serverProcess != null)
        {
            try { if (!_serverProcess.HasExited) _serverProcess.Kill(entireProcessTree: true); }
            catch { /* ignore */ }
            _serverProcess = null;
        }

        if (port > 0)
            KillProcessOnPort(port);
    }

    /// <summary>Kill any process currently listening on the given TCP port (Windows: netstat).</summary>
    private static void KillProcessOnPort(int port)
    {
        try
        {
            var psi = new ProcessStartInfo("netstat", "-ano")
            {
                UseShellExecute       = false,
                RedirectStandardOutput = true,
                CreateNoWindow        = true
            };
            using var p = Process.Start(psi);
            if (p == null) return;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);

            foreach (var line in output.Split('\n'))
            {
                if (!line.Contains($":{port} ") && !line.Contains($":{port}\t")) continue;
                if (!line.Contains("LISTENING", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && int.TryParse(parts[^1], out var pid))
                {
                    try { Process.GetProcessById(pid).Kill(entireProcessTree: true); } catch { }
                }
            }
        }
        catch { /* netstat unavailable or access denied – silently skip */ }
    }

    // -------------------------------------------------------------------------
    // Ollama model discovery helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Attempts to locate the GGUF blob file for a model that was already downloaded
    /// via <c>ollama pull &lt;modelTag&gt;</c>.
    ///
    /// Ollama stores models in:
    ///   Windows : %LOCALAPPDATA%\Ollama\models\
    ///   Linux   : ~/.ollama/models/
    ///   macOS   : ~/.ollama/models/
    ///
    /// The manifest JSON at  manifests/registry.ollama.ai/library/{name}/{tag}
    /// contains a "layers" array.  The layer whose mediaType is
    /// "application/vnd.ollama.image.model" is the GGUF blob.
    /// Its "digest" field ("sha256:xxxx…") maps to  blobs/sha256-xxxx…
    ///
    /// Returns null when the model cannot be found.
    /// </summary>
    public static string? FindOllamaModelPath(string modelTag)
    {
        var modelsRoot = GetOllamaModelsRoot();
        if (modelsRoot == null)
            return null;

        // Normalise tag: "granite4:3b" → name="granite4", tag="3b"
        //                "mistral"     → name="mistral",  tag="latest"
        var colonIdx = modelTag.IndexOf(':');
        var name = colonIdx >= 0 ? modelTag[..colonIdx] : modelTag;
        var tag  = colonIdx >= 0 ? modelTag[(colonIdx + 1)..] : "latest";

        var manifestPath = Path.Combine(
            modelsRoot, "manifests", "registry.ollama.ai", "library", name, tag);

        if (!File.Exists(manifestPath))
        {
            // Try without "library/" (custom registries)
            manifestPath = Path.Combine(modelsRoot, "manifests", name, tag);
            if (!File.Exists(manifestPath))
                return null;
        }

        try
        {
            var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var layers = manifest.RootElement.GetProperty("layers");

            foreach (var layer in layers.EnumerateArray())
            {
                if (!layer.TryGetProperty("mediaType", out var mt))
                    continue;

                var mediaType = mt.GetString() ?? string.Empty;
                if (!mediaType.Contains("model", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!layer.TryGetProperty("digest", out var digestEl))
                    continue;

                var digest = digestEl.GetString();
                if (string.IsNullOrEmpty(digest))
                    continue;

                // "sha256:abc123..." → "sha256-abc123..."
                var blobFileName = digest.Replace(':', '-');
                var blobPath = Path.Combine(modelsRoot, "blobs", blobFileName);

                if (File.Exists(blobPath))
                    return blobPath;
            }
        }
        catch
        {
            // Manifest unreadable / unexpected format
        }

        return null;
    }

    /// <summary>
    /// Returns the list of model tags that Ollama has downloaded locally.
    /// </summary>
    public static IReadOnlyList<string> ListOllamaModels()
    {
        var modelsRoot = GetOllamaModelsRoot();
        if (modelsRoot == null)
            return Array.Empty<string>();

        var libraryDir = Path.Combine(modelsRoot, "manifests", "registry.ollama.ai", "library");
        if (!Directory.Exists(libraryDir))
            return Array.Empty<string>();

        var result = new List<string>();
        foreach (var modelDir in Directory.EnumerateDirectories(libraryDir))
        {
            var modelName = Path.GetFileName(modelDir);
            foreach (var tagFile in Directory.EnumerateFiles(modelDir))
            {
                var tagName = Path.GetFileName(tagFile);
                result.Add($"{modelName}:{tagName}");
            }
        }

        return result;
    }

    /// <summary>
    /// Prints a usage hint to the console showing how to launch llama-server
    /// with a model that Ollama has already downloaded.
    /// </summary>
    public static void PrintLaunchHint(string modelTag, int ctxSize = 8192)
    {
        var blobPath = FindOllamaModelPath(modelTag);
        if (blobPath == null)
        {
            Console.WriteLine($"  [llama.cpp] Model '{modelTag}' not found in Ollama store.");
            Console.WriteLine($"  Run:  ollama pull {modelTag}");
        }
        else
        {
            Console.WriteLine($"  [llama.cpp] Found model blob:");
            Console.WriteLine($"  {blobPath}");
            Console.WriteLine();
            Console.WriteLine($"  Launch llama-server with:");
            Console.WriteLine($"  llama-server --model \"{blobPath}\" --port 8080 --ctx-size {ctxSize}");
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private OpenAiChatRequest BuildRequest(List<ChatMessage> messages, string? model, bool stream)
        => new()
        {
            Model = model ?? _defaultModel,
            Messages = messages,
            Stream = stream,
            Temperature = 0.7f
        };

    private static string? GetOllamaModelsRoot()
    {
        // Windows: %LOCALAPPDATA%\Ollama\models
        if (OperatingSystem.IsWindows())
        {
            var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var winPath = Path.Combine(localApp, "Ollama", "models");
            if (Directory.Exists(winPath))
                return winPath;
        }

        // Linux / macOS: ~/.ollama/models
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var unixPath = Path.Combine(home, ".ollama", "models");
        if (Directory.Exists(unixPath))
            return unixPath;

        return null;
    }
}

// -------------------------------------------------------------------------
// OpenAI-compatible request / response models
// -------------------------------------------------------------------------

internal class OpenAiChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; set; } = new();

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    [JsonPropertyName("temperature")]
    public float Temperature { get; set; } = 0.7f;
}

internal class OpenAiChatResponse
{
    [JsonPropertyName("choices")]
    public List<OpenAiChoice>? Choices { get; set; }
}

internal class OpenAiChoice
{
    /// <summary>Used in non-streaming responses.</summary>
    [JsonPropertyName("message")]
    public OpenAiMessage? Message { get; set; }

    /// <summary>Used in streaming (SSE) responses.</summary>
    [JsonPropertyName("delta")]
    public OpenAiMessage? Delta { get; set; }
}

internal class OpenAiMessage
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

public class LlamaCppException : Exception
{
    public LlamaCppException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}
