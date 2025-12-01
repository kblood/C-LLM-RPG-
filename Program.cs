using CSharpRPGBackend.Core;
using CSharpRPGBackend.LLM;
using CSharpRPGBackend.Services;
using CSharpRPGBackend.Games;

const string ollamaUrl = "http://localhost:11434";
const string ollamaModel = "granite4:3b"; // Using Granite 4 3B - lightweight and fast
var gamesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "games");

// Check for special modes
if (args.Length > 0)
{
    if (args[0] == "replay")
    {
        await RunReplayMode();
        return;
    }
    else if (args[0] == "test-equipment" || args[0] == "test")
    {
        RunEquipmentTest();
        return;
    }
}

// Interactive game mode
await RunInteractiveMode();

async Task RunReplayMode()
{
    Console.WriteLine("=== C# RPG Backend - Automated Game Replayer ===\n");

    // Initialize Ollama client
    var ollamaClient = new OllamaClient(ollamaUrl, ollamaModel);

    // Check if Ollama is running
    Console.WriteLine("Checking Ollama connection...");
    if (!await ollamaClient.IsHealthyAsync())
    {
        Console.WriteLine("ERROR: Cannot connect to Ollama at " + ollamaUrl);
        Console.WriteLine("Make sure Ollama is running: ollama serve");
        return;
    }

    Console.WriteLine("✓ Connected to Ollama");
    Console.WriteLine($"Using model: {ollamaModel}\n");

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
            var gameMaster = new GameMaster(gameState, ollamaClient, null, game);

            // Create replay player
            var replay = new GameReplay(gameState, gameMaster, ollamaClient, game);

            // Play the game
            Console.WriteLine($"Starting automated gameplay...\n");
            var logContent = await replay.PlayGameAsync(maxTurns: 30);

            // Save to file
            var fileName = $"REPLAY_{gameName.Replace(" ", "_")}.md";
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), fileName);
            await replay.SaveLogAsync(filePath);

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
    Console.WriteLine("=== C# RPG Backend - Game Selector ===\n");
    Console.WriteLine("Available Games:");

    var allGames = new List<(string label, Game game, string? gameDir)>();
    allGames.AddRange(staticGames);
    allGames.AddRange(dynamicGames);

    for (int i = 0; i < allGames.Count; i++)
    {
        var type = staticGames.Contains(allGames[i]) ? "[STATIC]" : "[JSON]";
        Console.WriteLine($"{i + 1}. {allGames[i].label} {type}");
    }

    Console.WriteLine($"\nSelect a game (1-{allGames.Count}): ");

    var gameChoice = Console.ReadLine();
    if (!int.TryParse(gameChoice, out var choiceIndex) || choiceIndex < 1 || choiceIndex > allGames.Count)
    {
        Console.WriteLine("Invalid selection. Loading default game...");
        choiceIndex = 1;
    }

    var (selectedLabel, game, selectedGameDir) = allGames[choiceIndex - 1];

    Console.WriteLine($"\nLoading: {game.Title}");
    if (!string.IsNullOrWhiteSpace(game.StoryIntroduction))
        Console.WriteLine($"\n{game.StoryIntroduction}");

    Console.WriteLine("\nPress any key to begin...");
    try
    {
        Console.ReadKey();
    }
    catch
    {
        // In non-interactive mode (piped input), skip ReadKey
    }

    // Initialize Ollama client (silently, don't clear screen)
    var ollamaClient = new OllamaClient(ollamaUrl, ollamaModel);

    // Check if Ollama is running
    if (!await ollamaClient.IsHealthyAsync())
    {
        Console.WriteLine("ERROR: Cannot connect to Ollama at " + ollamaUrl);
        Console.WriteLine("Make sure Ollama is running: ollama serve");
        return;
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
    // For JSON games, use startingItems from the game definition
    // For static games, add all weapons and armor
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
            // Fallback to default behavior
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

    // Initialize the Game Master
    var gameMaster = new GameMaster(gameState, ollamaClient, null, game);

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
            // Check for win condition (reached a winning room)
            if (game.WinConditionRoomIds != null && game.WinConditionRoomIds.Contains(gameState.CurrentRoomId))
            {
                Console.WriteLine("\n" + new string('=', 60));
                Console.WriteLine("🎉 VICTORY! You have achieved the objective!");
                Console.WriteLine("=" + new string('=', 59));
                break;
            }

            // Get player input
            Console.Write("> What do you do? ");
            var playerInput = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(playerInput))
                continue;

            // Handle quit command (special case - not sent to LLM)
            if (playerInput.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
                playerInput.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("\nThanks for playing!");
                break;
            }

            // Special case: explicit help command
            if (playerInput.Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(gameMaster.GetAvailableActions());
                continue;
            }

            // All other commands go through the LLM-powered Game Master
            // This includes: movement, talking, examining items, using items, etc.
            // The LLM will parse natural language and execute the appropriate action
            Console.WriteLine("\n⏳ Processing your action...");
            var actionNarration = await gameMaster.ProcessPlayerActionAsync(playerInput);
            Console.WriteLine($"\n{actionNarration}");

            // Log the action
            sessionLog.WriteLine($"[{DateTime.Now:HH:mm:ss}] Player: {playerInput}");
            sessionLog.WriteLine($"[{DateTime.Now:HH:mm:ss}] Game: {actionNarration}");
            sessionLog.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Error: {ex.Message}");
        }
    }

    // Handle end of game
    if (!gameState.Player.IsAlive)
    {
        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine("GAME OVER - You have been defeated!");
        Console.WriteLine("=" + new string('=', 59));
    }

    // Close the session log
    sessionLog.Dispose();
    Console.WriteLine($"\n📝 Session log saved to: {sessionLogPath}");
}

void RunEquipmentTest()
{
    Console.WriteLine("\n🎮 Running Equipment System Test Mode\n");
    CSharpRPGBackend.EquipmentTest.RunTest();
    Console.WriteLine("\n✅ Test Complete!");
}
