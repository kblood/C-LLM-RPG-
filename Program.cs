using CSharpRPGBackend.Core;
using CSharpRPGBackend.LLM;
using CSharpRPGBackend.Services;
using CSharpRPGBackend.Games;

var gamesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "games");

// ── Load persisted settings (llm-settings.json) ───────────────────────────────
var settings = LlmSettings.Load();

// CLI overrides (--backend=  --model=  --ollama-url=  --llamacpp-url=)
ApplyCliOverrides(args, settings);

// Build the LLM client from current settings
ILlmClient llmClient = settings.CreateClient();

static void ApplyCliOverrides(string[] args, LlmSettings s)
{
    foreach (var arg in args)
    {
        if (arg.StartsWith("--backend=",     StringComparison.OrdinalIgnoreCase))
            s.Backend = arg.Split('=', 2)[1];
        else if (arg.StartsWith("--model=",  StringComparison.OrdinalIgnoreCase))
            s.Model   = arg.Split('=', 2)[1];
        else if (arg.StartsWith("--ollama-url=", StringComparison.OrdinalIgnoreCase))
            s.OllamaUrl = arg.Split('=', 2)[1];
        else if (arg.StartsWith("--llamacpp-url=", StringComparison.OrdinalIgnoreCase))
            s.LlamaCppUrl = arg.Split('=', 2)[1];
    }

    // Also honour the legacy environment variable
    var envBackend = Environment.GetEnvironmentVariable("LLM_BACKEND");
    if (!string.IsNullOrWhiteSpace(envBackend))
        s.Backend = envBackend;
}

// Check for special modes (bypass menu)
if (args.Length > 0 && !args[0].StartsWith("--"))
{
    if (args[0] == "replay")
    {
        await RunReplayMode();
        return;
    }
    else if (args[0] == "agent")
    {
        await RunReplayMode(useAgent: true);
        return;
    }
    else if (args[0] == "test-equipment" || args[0] == "test")
    {
        RunEquipmentTest();
        return;
    }
}

// ── Main menu ─────────────────────────────────────────────────────────────────
await RunMainMenu();

async Task RunMainMenu()
{
    while (true)
    {
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════╗");
        Console.WriteLine("║      C# RPG Backend  – Main Menu         ║");
        Console.WriteLine("╠══════════════════════════════════════════╣");
        Console.WriteLine($"║  LLM: {settings.Summary,-36}║");
        Console.WriteLine("╠══════════════════════════════════════════╣");
        Console.WriteLine("║  1. Play a game                          ║");
        Console.WriteLine("║  2. Run replay (automated)               ║");
        Console.WriteLine("║  3. LLM Settings                         ║");
        Console.WriteLine("║  4. Quit                                  ║");
        Console.WriteLine("╚══════════════════════════════════════════╝");
        Console.Write("Choice: ");

        var choice = Console.ReadLine()?.Trim();
        switch (choice)
        {
            case "1":
                await RunInteractiveMode();
                break;
            case "2":
                Console.Write("Use [A]gent (smart) or [S]imple replay? [A/s]: ");
                var replayChoice = Console.ReadLine()?.Trim().ToLower();
                await RunReplayMode(useAgent: replayChoice != "s");
                break;
            case "3":
                await RunSettingsMenu();
                // Rebuild client in case settings changed
                llmClient = settings.CreateClient();
                break;
            case "4":
            case "q":
            case "quit":
            case "exit":
                Console.WriteLine("Goodbye!");
                return;
            default:
                Console.WriteLine("Invalid choice, try again.");
                break;
        }
    }
}

async Task RunSettingsMenu()
{
    while (true)
    {
        Console.WriteLine();
        Console.WriteLine("┌─────────────────────────────────────────────┐");
        Console.WriteLine("│             LLM Settings                     │");
        Console.WriteLine("├─────────────────────────────────────────────┤");
        Console.WriteLine($"│  Backend  : {settings.Backend,-33}│");
        Console.WriteLine($"│  Model    : {settings.Model,-33}│");
        Console.WriteLine($"│  Ctx size : {settings.ContextSize,-33}│");
        Console.WriteLine($"│  Ollama   : {settings.OllamaUrl,-33}│");
        Console.WriteLine($"│  llama.cpp: {settings.LlamaCppUrl,-33}│");
        Console.WriteLine("├─────────────────────────────────────────────┤");
        Console.WriteLine("│  1. Change backend (Ollama / llama.cpp)      │");
        Console.WriteLine("│  2. Change model                             │");
        Console.WriteLine("│  3. Change context size                      │");
        Console.WriteLine("│  4. Change Ollama URL                        │");
        Console.WriteLine("│  5. Change llama.cpp URL                     │");
        Console.WriteLine("│  6. Test connection                          │");
        Console.WriteLine("│  7. Save & return                            │");
        Console.WriteLine("│  8. Discard & return                         │");
        Console.WriteLine("└─────────────────────────────────────────────┘");
        Console.Write("Choice: ");

        var choice = Console.ReadLine()?.Trim();
        switch (choice)
        {
            case "1":
                await ChangeBackend();
                break;
            case "2":
                await ChangeModel();
                break;
            case "3":
                ChangeContextSize();
                break;
            case "4":
                ChangeUrl("Ollama URL", v => settings.OllamaUrl = v, settings.OllamaUrl);
                break;
            case "5":
                ChangeUrl("llama.cpp URL", v => settings.LlamaCppUrl = v, settings.LlamaCppUrl);
                break;
            case "6":
                await TestConnection();
                break;
            case "7":
                settings.Save();
                Console.WriteLine("✓ Settings saved to llm-settings.json.");
                if (settings.IsLlamaCpp)
                {
                    Console.Write("  (Re)start llama-server with new settings? [y/N]: ");
                    if (Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true)
                        await StartLlamaServerAsync();
                }
                return;
            case "8":
                // Reload from disk (discard in-memory edits)
                var reloaded = LlmSettings.Load();
                settings.Backend    = reloaded.Backend;
                settings.Model      = reloaded.Model;
                settings.ContextSize = reloaded.ContextSize;
                settings.OllamaUrl  = reloaded.OllamaUrl;
                settings.LlamaCppUrl = reloaded.LlamaCppUrl;
                Console.WriteLine("Changes discarded.");
                return;
            default:
                Console.WriteLine("Invalid choice.");
                break;
        }
    }
}

async Task ChangeBackend()
{
    Console.WriteLine();
    Console.WriteLine("  Available backends:");
    Console.WriteLine("    1. Ollama    (default, http://localhost:11434)");
    Console.WriteLine("    2. llama.cpp (http://localhost:8080)");
    Console.Write("  Select backend [1/2]: ");
    var input = Console.ReadLine()?.Trim();

    string newBackend;
    if (input == "2")
    {
        newBackend   = "llamacpp";
    }
    else
    {
        newBackend   = "ollama";
    }

    settings.Backend = newBackend;

    // If switching backends, automatically suggest models
    Console.WriteLine($"  Backend set to: {newBackend}");
    await ChangeModel();  // Let user pick a model for the new backend
}

async Task ChangeModel()
{
    List<string> models;

    if (settings.IsLlamaCpp)
    {
        // For llama.cpp the server only knows the one model loaded at startup,
        // so always show the full Ollama local library instead – that's where
        // the user picks which model to pass to llama-server.
        models = LlamaCppClient.ListOllamaModels().ToList();
        if (models.Count > 0)
            Console.WriteLine("\n  Locally installed Ollama models (select one to use with llama-server):");
        else
            Console.WriteLine("\n  No Ollama models found locally. Run: ollama pull <model>");
    }
    else
    {
        var probe = settings.CreateClient();
        Console.WriteLine($"\n  Fetching available models from {settings.Backend}…");
        models = await probe.ListModelsAsync();
        if (models.Count > 0)
            Console.WriteLine("  Available models:");
        else
            Console.WriteLine("  (No models returned – is Ollama running?)");
    }

    if (models.Count > 0)
    {
        for (int i = 0; i < models.Count; i++)
            Console.WriteLine($"    {i + 1}. {models[i]}");

        Console.Write($"  Select model number, or type a name directly [{settings.Model}]: ");
        var input = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(input))
            return; // Keep current

        if (int.TryParse(input, out var idx) && idx >= 1 && idx <= models.Count)
            settings.Model = models[idx - 1];
        else
            settings.Model = input;
    }
    else
    {
        Console.Write($"  Type model name [{settings.Model}]: ");
        var input = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(input))
            settings.Model = input;
    }

    Console.WriteLine($"  Model set to: {settings.Model}");

    // For llama.cpp, always show the launch command for the chosen model
    if (settings.IsLlamaCpp)
    {
        Console.WriteLine();
        LlamaCppClient.PrintLaunchHint(settings.Model, settings.ContextSize);
    }
}

static void ChangeUrl(string label, Action<string> setter, string current)
{
    Console.Write($"  {label} [{current}]: ");
    var input = Console.ReadLine()?.Trim();
    if (!string.IsNullOrEmpty(input))
    {
        setter(input);
        Console.WriteLine($"  {label} updated.");
    }
}

void ChangeContextSize()
{
    Console.Write($"  Context size [{settings.ContextSize}]: ");
    var input = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(input)) return;
    if (int.TryParse(input, out var size) && size >= 512)
    {
        settings.ContextSize = size;
        Console.WriteLine($"  Context size set to {size}.");
        if (settings.IsLlamaCpp)
        {
            Console.WriteLine("  (Remember to restart llama-server with the new --ctx-size)");
            LlamaCppClient.PrintLaunchHint(settings.Model, settings.ContextSize);
        }
    }
    else
    {
        Console.WriteLine("  Invalid value – must be a number ≥ 512.");
    }
}

async Task TestConnection()
{
    var probe = settings.CreateClient();
    Console.Write($"  Testing connection to {probe.BackendName} ({(settings.IsLlamaCpp ? settings.LlamaCppUrl : settings.OllamaUrl)})… ");
    var ok = await probe.IsHealthyAsync();
    Console.WriteLine(ok ? "✓ OK" : "✗ FAILED – is the server running?");

    if (ok)
    {
        var models = await probe.ListModelsAsync();
        Console.WriteLine($"  {models.Count} model(s) available: {string.Join(", ", models.Take(5))}{(models.Count > 5 ? "…" : "")}");
    }
    else if (settings.IsLlamaCpp)
    {
        Console.Write("  Start llama-server now? [y/N]: ");
        if (Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true)
            await StartLlamaServerAsync();
        else
        {
            Console.WriteLine();
            LlamaCppClient.PrintLaunchHint(settings.Model, settings.ContextSize);
        }
    }
}

/// <summary>
/// Launch llama-server with current settings, then poll until healthy (up to 30 s).
/// Updates the global <c>llmClient</c> on success.
/// </summary>
async Task<bool> StartLlamaServerAsync()
{
    var port = settings.LlamaCppPort;
    var exe  = LlamaCppClient.FindLlamaServerExe();
    Console.WriteLine($"  exe        : {exe ?? "NOT FOUND"}");
    Console.WriteLine($"  model      : {settings.Model}");
    Console.WriteLine($"  ctx-size   : {settings.ContextSize}");
    Console.WriteLine($"  port       : {port}");
    var blob = LlamaCppClient.FindOllamaModelPath(settings.Model);
    Console.WriteLine($"  model blob : {blob ?? "NOT FOUND"}");
    Console.WriteLine();

    if (exe == null)
    {
        Console.WriteLine("  ✗ llama-server not found.");
        Console.WriteLine("    Install via: winget install ggml.llamacpp");
        return false;
    }
    if (blob == null)
    {
        Console.WriteLine($"  ✗ Model blob not found. Run: ollama pull {settings.Model}");
        return false;
    }

    var (started, error) = LlamaCppClient.LaunchServer(settings.Model, port, settings.ContextSize);
    if (!started)
    {
        Console.WriteLine($"  ✗ {error}");
        return false;
    }

    Console.Write("  Waiting for llama-server to become ready");
    var probe = settings.CreateClient();
    for (int i = 0; i < 60; i++)
    {
        await Task.Delay(500);
        if (i % 2 == 0) Console.Write(".");   // one dot per second
        if (await probe.IsHealthyAsync())
        {
            Console.WriteLine(" ✓ Ready!");
            llmClient = settings.CreateClient();
            return true;
        }
    }

    Console.WriteLine(" ✗ Timed out waiting for llama-server (30 s).");
    return false;
}

async Task RunReplayMode(bool useAgent = false)
{
    Console.WriteLine($"=== C# RPG Backend - {(useAgent ? "Agent" : "Simple")} Game Replayer ===\n");

    // Check if the LLM backend is running
    Console.WriteLine($"Checking {llmClient.BackendName} connection...");
    if (!await llmClient.IsHealthyAsync())
    {
        Console.WriteLine($"ERROR: Cannot connect to {llmClient.BackendName}.");
        if (settings.IsLlamaCpp)
        {
            Console.Write("  Start llama-server now? [y/N]: ");
            if (Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (!await StartLlamaServerAsync()) return;
            }
            else
            {
                LlamaCppClient.PrintLaunchHint(settings.Model, settings.ContextSize);
                return;
            }
        }
        else
        {
            Console.WriteLine("Make sure Ollama is running: ollama serve");
            return;
        }
    }

    Console.WriteLine($"✓ Connected to {llmClient.BackendName}");
    Console.WriteLine($"Using model: {llmClient.DefaultModel}\n");

    // Collect games for replay (static games + dynamic games)
    var gameLoader = new GameLoader();
    var gamesToReplay = new List<(string name, Game game)>();

    // Add static games
    gamesToReplay.Add(("Fantasy Quest", FantasyQuest.Create()));
    gamesToReplay.Add(("Sci-Fi Adventure", SciFiAdventure.Create()));

    // Add JSON-based games if available
    if (Directory.Exists(gamesDirectory))
    {
        try
        {
            var gameInfos = gameLoader.FindAvailableGames(gamesDirectory);
            foreach (var gameInfo in gameInfos)
            {
                try
                {
                    var loadedGame = await gameLoader.LoadGameAsync(gameInfo.GameDirectory);
                    gamesToReplay.Add((gameInfo.Title, loadedGame));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to load {gameInfo.Title}: {ex.Message}");
                }
            }
        }
        catch
        {
            // Silently continue without JSON games
        }
    }

    foreach (var (gameName, game) in gamesToReplay)
    {
        Console.WriteLine($"\n{'='*60}");
        Console.WriteLine($"Playing: {gameName}");
        Console.WriteLine($"{'='*60}\n");

        try
        {
            // Create game state
            var gameState = new GameState();
            gameState.Rooms = game.Rooms;
            gameState.NPCs = game.NPCs;
            gameState.CurrentRoomId = game.StartingRoomId;

            // Initialize player's starting currency if economy is enabled
            if (game.Economy?.Enabled == true && game.StartingCurrency > 0)
            {
                gameState.Player.Wallet.Add(game.StartingCurrency);
            }

            // Add starting items to inventory
            if (game.CustomSettings.ContainsKey("startingItems"))
            {
                try
                {
                    var startingItemsJson = game.CustomSettings["startingItems"];
                    var startingItems = System.Text.Json.JsonSerializer.Deserialize<List<StartingItemDefinition>>(startingItemsJson);
                    if (startingItems != null)
                    {
                        foreach (var startingItem in startingItems)
                        {
                            if (game.Items.TryGetValue(startingItem.ItemId, out var item))
                            {
                                gameState.PlayerInventory.AddItem(item, startingItem.Quantity);
                            }
                        }
                    }
                }
                catch
                {
                    // Fallback to default
                    foreach (var item in game.Items.Values)
                    {
                        if (item.Type == ItemType.Weapon || item.Type == ItemType.Armor)
                        {
                            gameState.PlayerInventory.AddItem(item, 1);
                        }
                    }
                }
            }
            else
            {
                // Default behavior for static games
                foreach (var item in game.Items.Values)
                {
                    if (item.Type == ItemType.Weapon || item.Type == ItemType.Armor)
                    {
                        gameState.PlayerInventory.AddItem(item, 1);
                    }
                }
            }

            // Initialize Game Master
            var gameMaster = new GameMaster(gameState, llmClient, null, game);

            // Play the game
            string logContent;
            string fileName;

            if (useAgent)
            {
                var agent = new AgentReplay(gameState, gameMaster, llmClient, game);
                Console.WriteLine($"Starting agent gameplay...\n");
                logContent = await agent.PlayGameAsync(maxTurns: 50);
                fileName = $"AGENT_{gameName.Replace(" ", "_")}.md";
                var filePath = Path.Combine(Directory.GetCurrentDirectory(), fileName);
                await agent.SaveLogAsync(filePath);
            }
            else
            {
                var replay = new GameReplay(gameState, gameMaster, llmClient, game);
                Console.WriteLine($"Starting automated gameplay...\n");
                logContent = await replay.PlayGameAsync(maxTurns: 30);
                fileName = $"REPLAY_{gameName.Replace(" ", "_")}.md";
                var filePath = Path.Combine(Directory.GetCurrentDirectory(), fileName);
                await replay.SaveLogAsync(filePath);
            }

            // Show summary
            var turnCount = logContent.Count(c => c == '#') - 1; // Count turns (excluding title)
            Console.WriteLine($"\n✓ Game completed in {turnCount} turns");
            Console.WriteLine($"✓ Replay saved to {fileName}\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error playing {gameName}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    Console.WriteLine("\n" + new string('=', 60));
    Console.WriteLine("All games completed! Check the REPLAY_*.md files for logs.");
    Console.WriteLine("=" + new string('=', 59));
}

async Task RunInteractiveMode()
{
    // Collect available games (both static and dynamic)
    var staticGames = new List<(string label, Game game, string? gameDir)>
    {
        ("Fantasy Quest - The Dragon's Hoard", FantasyQuest.Create(), null),
        ("Sci-Fi Adventure - Escape from Station Zeta", SciFiAdventure.Create(), null)
    };

    var gameLoader = new GameLoader();
    var dynamicGames = new List<(string label, Game game, string? gameDir)>();

    // Load dynamic games from the games/ directory
    if (Directory.Exists(gamesDirectory))
    {
        var gameInfos = gameLoader.FindAvailableGames(gamesDirectory);
        foreach (var gameInfo in gameInfos)
        {
            try
            {
                var loadedGame = await gameLoader.LoadGameAsync(gameInfo.GameDirectory);
                dynamicGames.Add((gameInfo.ToString(), loadedGame, gameInfo.GameDirectory));
            }
            catch
            {
                // Skip games that fail to load
            }
        }
    }

    // Display game selection menu
    Console.WriteLine("\n=== Select a Game ===\n");

    var allGames = new List<(string label, Game game, string? gameDir)>();
    allGames.AddRange(staticGames);
    allGames.AddRange(dynamicGames);

    for (int i = 0; i < allGames.Count; i++)
    {
        var type = staticGames.Contains(allGames[i]) ? "[STATIC]" : "[JSON]";
        Console.WriteLine($"  {i + 1}. {allGames[i].label} {type}");
    }
    Console.WriteLine($"  {allGames.Count + 1}. Back to main menu");

    Console.Write($"\nSelect a game (1-{allGames.Count}): ");

    var gameChoice = Console.ReadLine();
    if (!int.TryParse(gameChoice, out var choiceIndex) || choiceIndex < 1 || choiceIndex > allGames.Count + 1)
    {
        Console.WriteLine("Invalid selection. Loading default game...");
        choiceIndex = 1;
    }

    if (choiceIndex == allGames.Count + 1)
        return; // Back to main menu

    var (selectedLabel, game, selectedGameDir) = allGames[choiceIndex - 1];

    Console.WriteLine($"\nLoading: {game.Title}");
    if (!string.IsNullOrWhiteSpace(game.StoryIntroduction))
        Console.WriteLine($"\n{game.StoryIntroduction}");

    Console.WriteLine("\nPress any key to begin...");
    try { Console.ReadKey(); } catch { /* non-interactive */ }

    // Check if the LLM backend is running
    if (!await llmClient.IsHealthyAsync())
    {
        Console.WriteLine($"\nERROR: Cannot connect to {llmClient.BackendName}.");
        if (settings.IsLlamaCpp)
        {
            Console.Write("  Start llama-server now? [y/N]: ");
            if (Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (!await StartLlamaServerAsync()) { return; }
            }
            else
            {
                LlamaCppClient.PrintLaunchHint(settings.Model, settings.ContextSize);
                Console.WriteLine("You can change the backend in Settings (option 3 on the main menu).");
                return;
            }
        }
        else
        {
            Console.WriteLine("Make sure Ollama is running: ollama serve");
            Console.WriteLine("You can change the backend in Settings (option 3 on the main menu).");
            return;
        }
    }

    // Create a GameState from the selected game
    var gameState = new GameState();
    gameState.Rooms = game.Rooms;
    gameState.NPCs = game.NPCs;
    gameState.CurrentRoomId = game.StartingRoomId;

    // Initialize player's starting currency if economy is enabled
    if (game.Economy?.Enabled == true && game.StartingCurrency > 0)
    {
        gameState.Player.Wallet.Add(game.StartingCurrency);
    }

    // Add starting items to player inventory
    if (game.CustomSettings.ContainsKey("startingItems"))
    {
        try
        {
            var startingItemsJson = game.CustomSettings["startingItems"];
            var startingItems = System.Text.Json.JsonSerializer.Deserialize<List<StartingItemDefinition>>(startingItemsJson);
            if (startingItems != null)
            {
                foreach (var startingItem in startingItems)
                {
                    if (game.Items.TryGetValue(startingItem.ItemId, out var item))
                    {
                        gameState.PlayerInventory.AddItem(item, startingItem.Quantity);
                    }
                }
            }
        }
        catch
        {
            foreach (var item in game.Items.Values)
                if (item.Type == ItemType.Weapon || item.Type == ItemType.Armor)
                    gameState.PlayerInventory.AddItem(item, 1);
        }
    }
    else
    {
        foreach (var item in game.Items.Values)
            if (item.Type == ItemType.Weapon || item.Type == ItemType.Armor)
                gameState.PlayerInventory.AddItem(item, 1);
    }

    // Initialize the Game Master
    var gameMaster = new GameMaster(gameState, llmClient, null, game);

    // Set up session logging
    var sessionLogPath = Path.Combine(Directory.GetCurrentDirectory(), $"SESSION_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
    var sessionLog = new System.IO.StreamWriter(sessionLogPath, append: true) { AutoFlush = true };

    // Welcome message
    Console.WriteLine($"\n=== {game.Title} ===");
    if (!string.IsNullOrWhiteSpace(game.Subtitle))
        Console.WriteLine(game.Subtitle);
    Console.WriteLine("This is a console-based adventure with LLM-powered NPCs.");
    Console.WriteLine("Type 'help' for available actions.\n");

    // Show initial game state
    var initialRoom = gameState.GetCurrentRoom();
    var initialExits = initialRoom.GetAvailableExits();
    var initialNpcs = initialRoom.NPCIds
        .Where(id => gameState.NPCs.ContainsKey(id))
        .Select(id => gameState.NPCs[id].Name)
        .ToList();
    var initialInventory = gameState.PlayerInventory.Items.Count > 0
        ? string.Join(", ", gameState.PlayerInventory.Items.Values.Select(ii => ii.Item.Name))
        : "empty";

    Console.WriteLine("---\n");
    Console.WriteLine($"📍 **Location:** {initialRoom.Name}");
    Console.WriteLine($"❤️ **Health:** {gameState.Player.Health}/{gameState.Player.MaxHealth}");
    if (initialExits.Count > 0)
        Console.WriteLine($"🚪 **Exits:** {string.Join(", ", initialExits.Select(e => e.DisplayName))}");
    if (initialNpcs.Count > 0)
        Console.WriteLine($"👥 **NPCs Here:** {string.Join(", ", initialNpcs)}");
    Console.WriteLine($"🎒 **Inventory:** {initialInventory}\n");

    // Main game loop
    while (gameState.Player.IsAlive)
    {
        try
        {
            // Check for win condition
            if (game.WinConditionRoomIds != null && game.WinConditionRoomIds.Contains(gameState.CurrentRoomId))
            {
                Console.WriteLine("\n" + new string('=', 60));
                Console.WriteLine("🎉 VICTORY! You have achieved the objective!");
                Console.WriteLine("=" + new string('=', 59));
                break;
            }

            Console.Write("> What do you do? ");
            var playerInput = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(playerInput))
                continue;

            if (playerInput.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
                playerInput.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("\nThanks for playing!");
                break;
            }

            if (playerInput.Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(gameMaster.GetAvailableActions());
                continue;
            }

            Console.WriteLine("\n⏳ Processing your action...");
            var actionNarration = await gameMaster.ProcessPlayerActionAsync(playerInput);
            Console.WriteLine($"\n{actionNarration}");

            sessionLog.WriteLine($"[{DateTime.Now:HH:mm:ss}] Player: {playerInput}");
            sessionLog.WriteLine($"[{DateTime.Now:HH:mm:ss}] Game: {actionNarration}");
            sessionLog.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Error: {ex.Message}");
        }
    }

    if (!gameState.Player.IsAlive)
    {
        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine("GAME OVER - You have been defeated!");
        Console.WriteLine("=" + new string('=', 59));
    }

    sessionLog.Dispose();
    Console.WriteLine($"\n📝 Session log saved to: {sessionLogPath}");
}

void RunEquipmentTest()
{
    Console.WriteLine("\n🎮 Running Equipment System Test Mode\n");
    CSharpRPGBackend.EquipmentTest.RunTest();
    Console.WriteLine("\n✅ Test Complete!");
}
