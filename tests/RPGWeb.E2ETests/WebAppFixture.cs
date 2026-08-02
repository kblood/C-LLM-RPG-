using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace RPGWeb.E2ETests;

public sealed class WebAppFixture : IAsyncLifetime
{
#if DEBUG
    private const string BuildConfiguration = "Debug";
#else
    private const string BuildConfiguration = "Release";
#endif

    private readonly StringBuilder _serverLog = new();
    private readonly object _logLock = new();
    private Process? _serverProcess;
    private string? _temporaryDataDirectory;

    public string BaseUrl { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var suppliedBaseUrl = Environment.GetEnvironmentVariable("RPGWEB_E2E_BASE_URL");
        if (!string.IsNullOrWhiteSpace(suppliedBaseUrl))
        {
            BaseUrl = suppliedBaseUrl.TrimEnd('/');
            await WaitUntilReadyAsync();
            return;
        }

        var repositoryRoot = FindRepositoryRoot();
        var testDataRoot = Path.Combine(Path.GetTempPath(), "CSharpRPGBackend-E2E");
        _temporaryDataDirectory = Path.Combine(testDataRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temporaryDataDirectory);

        var port = GetAvailableTcpPort();
        BaseUrl = $"http://127.0.0.1:{port}";

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(Path.Combine(repositoryRoot, "RPGWeb", "RPGWeb.csproj"));
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add(BuildConfiguration);
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Testing";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["LLM_BACKEND"] = "ollama";
        startInfo.Environment["OLLAMA_URL"] = "http://127.0.0.1:1";
        startInfo.Environment["RPGWEB_DATA_DIRECTORY"] = _temporaryDataDirectory;
        startInfo.Environment["RPGWEB_LISTEN_URL"] = BaseUrl;

        _serverProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _serverProcess.OutputDataReceived += (_, args) => AppendLog(args.Data);
        _serverProcess.ErrorDataReceived += (_, args) => AppendLog(args.Data);
        if (!_serverProcess.Start())
            throw new InvalidOperationException("Could not start the RPG web server.");

        _serverProcess.BeginOutputReadLine();
        _serverProcess.BeginErrorReadLine();
        await WaitUntilReadyAsync();
    }

    public async Task DisposeAsync()
    {
        if (_serverProcess is { HasExited: false })
        {
            _serverProcess.Kill(entireProcessTree: true);
            try
            {
                await _serverProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException)
            {
                // The process tree has already received a kill signal.
            }
        }

        _serverProcess?.Dispose();

        if (_temporaryDataDirectory is not null)
        {
            var allowedRoot = Path.GetFullPath(
                Path.Combine(Path.GetTempPath(), "CSharpRPGBackend-E2E")) + Path.DirectorySeparatorChar;
            var resolvedTarget = Path.GetFullPath(_temporaryDataDirectory);
            if (resolvedTarget.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(resolvedTarget))
            {
                Directory.Delete(resolvedTarget, recursive: true);
            }
        }
    }

    public string GetServerLog()
    {
        lock (_logLock)
            return _serverLog.ToString();
    }

    private async Task WaitUntilReadyAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);

        while (DateTime.UtcNow < deadline)
        {
            if (_serverProcess is { HasExited: true })
            {
                throw new InvalidOperationException(
                    $"The RPG web server exited before becoming ready.{Environment.NewLine}{GetServerLog()}");
            }

            try
            {
                using var response = await client.GetAsync($"{BaseUrl}/healthz");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
                // The server is still starting.
            }
            catch (TaskCanceledException)
            {
                // The readiness request timed out; retry until the overall deadline.
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"The RPG web server did not become ready at {BaseUrl}.{Environment.NewLine}{GetServerLog()}");
    }

    private void AppendLog(string? message)
    {
        if (message is null)
            return;

        lock (_logLock)
            _serverLog.AppendLine(message);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CSharpRPGBackend.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static int GetAvailableTcpPort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WebAppCollection : ICollectionFixture<WebAppFixture>
{
    public const string Name = "RPG web application";
}
