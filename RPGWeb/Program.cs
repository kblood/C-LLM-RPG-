using RPGWeb.Components;
using RPGWeb.Services;
using CSharpRPGBackend.LLM;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<BrowserSaveSlot>();

// Load LLM settings — persisted file first, then env/config overrides
var settings = LlmSettings.Load();
var backend = builder.Configuration["LlmBackend"]
    ?? Environment.GetEnvironmentVariable("LLM_BACKEND");
var ollamaUrl = builder.Configuration["OllamaUrl"]
    ?? Environment.GetEnvironmentVariable("OLLAMA_URL");
var llamaCppUrl = builder.Configuration["LlamaCppUrl"]
    ?? Environment.GetEnvironmentVariable("LLAMACPP_URL");
var geminiUrl = builder.Configuration["GeminiUrl"]
    ?? Environment.GetEnvironmentVariable("GEMINI_URL");
var openRouterUrl = builder.Configuration["OpenRouterUrl"]
    ?? Environment.GetEnvironmentVariable("OPENROUTER_URL");
var model = builder.Configuration["Model"]
    ?? Environment.GetEnvironmentVariable("LLM_MODEL");
if (backend != null) settings.Backend = backend;
if (ollamaUrl != null) settings.OllamaUrl = ollamaUrl;
if (llamaCppUrl != null) settings.LlamaCppUrl = llamaCppUrl;
if (geminiUrl != null) settings.GeminiUrl = geminiUrl;
if (openRouterUrl != null) settings.OpenRouterUrl = openRouterUrl;
if (model != null)
{
    if (settings.IsGemini) settings.GeminiModel = model;
    else if (settings.IsOpenRouter) settings.OpenRouterModel = model;
    else settings.Model = model;
}

builder.Services.AddSingleton(settings);

// Game session is scoped per Blazor circuit (one game per browser tab)
builder.Services.AddScoped<GameSessionService>();

// Stay local unless the server operator explicitly chooses another interface.
var port = builder.Configuration["Port"] ?? "5100";
var listenUrl = builder.Configuration["ListenUrl"]
    ?? Environment.GetEnvironmentVariable("RPGWEB_LISTEN_URL")
    ?? $"http://127.0.0.1:{port}";
builder.WebHost.UseUrls(listenUrl);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseMiddleware<BrowserSaveSlotCookieMiddleware>();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Print access info on startup
var llmClient = settings.CreateClient();
Console.WriteLine($"RPG Web Server listening on {listenUrl}");
Console.WriteLine($"LLM Backend: {llmClient.BackendName} ({settings.ActiveUrl}) model: {settings.ActiveModel}");
Console.WriteLine();

app.Run();
