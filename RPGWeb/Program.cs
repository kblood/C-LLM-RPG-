using RPGWeb.Components;
using RPGWeb.Services;
using CSharpRPGBackend.LLM;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Load LLM settings — use environment variable or defaults
var settings = new LlmSettings();
var ollamaUrl = builder.Configuration["OllamaUrl"]
    ?? Environment.GetEnvironmentVariable("OLLAMA_URL")
    ?? "http://localhost:11434";
var model = builder.Configuration["Model"]
    ?? Environment.GetEnvironmentVariable("LLM_MODEL")
    ?? "granite4:3b";
settings.OllamaUrl = ollamaUrl;
settings.Model = model;

// LLM client is singleton (stateless HTTP, shared across all sessions)
builder.Services.AddSingleton<ILlmClient>(settings.CreateClient());
builder.Services.AddSingleton(settings);

// Game session is scoped per Blazor circuit (one game per browser tab)
builder.Services.AddScoped<GameSessionService>();

// Bind to all interfaces so other machines on the network can connect
var port = builder.Configuration["Port"] ?? "5100";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Print access info on startup
var llmClient = app.Services.GetRequiredService<ILlmClient>();
Console.WriteLine($"RPG Web Server starting on port {port}");
Console.WriteLine($"LLM Backend: {llmClient.BackendName} ({ollamaUrl}) model: {model}");
Console.WriteLine($"Local:   http://localhost:{port}");
Console.WriteLine($"Network: http://<your-ip>:{port}");
Console.WriteLine();

app.Run();
