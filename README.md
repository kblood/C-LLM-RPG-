# C# Living-World RPG Backend

A .NET 8 RPG prototype with authoritative game rules and optional LLM narration. It runs as either a console game or a Blazor web app. Ollama is the default LLM backend; llama.cpp, Google Gemini, and OpenRouter are also supported.

The LLM interprets free-form commands and narrates outcomes. Movement, combat, inventory, quests, crafting, progression, saves, and world changes remain authoritative C# state, so a model cannot invent rewards or bypass rules.

## What is implemented

- Isolated game sessions with explicit starting loadouts
- Rooms, locked exits, floor items, equipment, combat, merchants, gathering, and crafting
- Turn-based world simulation with NPC patrols and renewable or permanently depleted resources
- Structured quest objectives with one-time rewards and cumulative character leveling
- Multi-stage world projects that can change rooms, routes, resources, and NPC locations
- A complete Ravensholm restoration arc in Fantasy Quest
- Versioned JSON saves, console autosave, and web Continue/Save controls
- Provider-neutral LLM interface with Ollama, llama.cpp, Gemini, and OpenRouter clients
- Console and Blazor front ends

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Optionally, one supported LLM service for free-form interpretation, NPC dialogue, and narration. Direct commands continue to work when no provider is available.

## Quick start with Ollama

```powershell
ollama serve
ollama pull granite4:3b
dotnet run
```

Choose a game from the menu. Settings can be changed without editing source code.

To run the web UI:

```powershell
dotnet run --project RPGWeb
```

The web server binds to `http://127.0.0.1:5100` by default. A server operator can deliberately expose a different trusted address through configuration key `ListenUrl` or `RPGWEB_LISTEN_URL`, for example `$env:RPGWEB_LISTEN_URL = "http://0.0.0.0:5100"`. Provider endpoints are server-managed and cannot be edited in the browser. Backend and model changes made in the web settings page apply only to that browser session and do not rewrite the server's settings file. Web saves are isolated per browser by a random HttpOnly cookie and stored beneath `saves/web/<browser-slot>/`; clearing that cookie starts a new save slot.

## LLM backends

Configuration is stored in `llm-settings.json`. API keys are deliberately excluded from that file and from command-line arguments; cloud credentials come from environment variables.

| Backend | `--backend` value | Default model | Credential |
|---|---|---|---|
| Ollama | `ollama` | `granite4:3b` | None |
| llama.cpp | `llamacpp` | `granite4:3b` | None |
| Google Gemini | `gemini` | `gemini-3.6-flash` | `GEMINI_API_KEY` |
| OpenRouter | `openrouter` | `openrouter/auto` | `OPENROUTER_API_KEY` |

### Console settings

Open **Settings** from the main menu, or supply non-secret overrides:

```powershell
dotnet run -- --backend=ollama --model=granite4:3b
dotnet run -- --backend=llamacpp --llamacpp-url=http://localhost:8080
dotnet run -- --backend=gemini --model=gemini-3.6-flash
dotnet run -- --backend=openrouter --model=openrouter/auto
```

The supported URL overrides are `--ollama-url`, `--llamacpp-url`, `--gemini-url`, and `--openrouter-url`. `LLM_BACKEND` is also accepted as an environment override.

### Gemini

```powershell
$env:GEMINI_API_KEY = "your-key"
dotnet run -- --backend=gemini --model=gemini-3.6-flash
```

The client uses Gemini's `generateContent` and streaming endpoints and sends the key in the `x-goog-api-key` header. See the [official Gemini API reference](https://ai.google.dev/api/generate-content).

### OpenRouter

```powershell
$env:OPENROUTER_API_KEY = "your-key"
dotnet run -- --backend=openrouter --model=openrouter/auto
```

`openrouter/auto` lets OpenRouter select a suitable available model. You can instead enter any model ID exposed by the provider. See the [OpenRouter quickstart](https://openrouter.ai/docs/quickstart) and [Auto Router guide](https://openrouter.ai/docs/guides/routing/routers/auto-router).

For the web app, the same environment variables must be set before launching the server. Secrets are not shown or accepted by the settings page.

## Playing the game

Free-form requests are accepted, but direct commands are useful when playing without a healthy LLM connection.

| Command | Example |
|---|---|
| Inspect the area | `look` |
| Travel | `move forest` |
| Talk | `talk merchant` |
| Fight | `attack goblin` |
| Manage items | `inventory`, `take sword`, `drop potion` |
| Use equipment | `equip sword`, `unequip sword`, `equipped` |
| Trade | `shop`, `buy potion`, `sell sword` |
| Gather and craft | `gather iron ore`, `recipes`, `craft iron sword` |
| Track quests | `quests`, `accept <quest>`, `turn in <quest>` |
| Grow the world | `projects`, `contribute 3 iron ore to forgeworks` |
| Character info | `status` |
| Persist immediately | `save` |

Looking at status, inventory, the room, quests, projects, recipes, or a shop does not advance time. Actions such as moving, fighting, gathering, crafting, and talking advance exactly one turn. Console progress autosaves after every processed action and when quitting under `saves/<game-id>.json`.

## Living-world example: Ravensholm

Fantasy Quest includes a two-stage project rather than a purely static map:

1. Defeat the Goblin King to reclaim the mine. The cave becomes safer and its iron resource improves.
2. Contribute three iron ore to restore the Forgeworks. A new district opens, descriptions change, and the blacksmith relocates there.

The reusable project model supports requirements based on events, quests, items, kills, locations, levels, and contributions. Effects can alter room descriptions and metadata, enable exits, add resources or floor items, and move NPCs. Definitions live in the game content while `WorldProjectService` applies them to runtime state.

## Project layout

```text
src/
  Core/       GameState and isolated runtime-state factory
  Games/      Built-in game definitions
  LLM/        Provider-neutral interface and provider clients
  Models/     Items, characters, quests, saves, and living-world models
  Services/   Game master, world simulation, progression, projects, and saves
RPGWeb/       Blazor front end
RPGGameEditor/ Desktop content editor
tests/        Core xUnit and Playwright browser regression suites
games/        JSON-authored games and schemas
```

## Build and test

```powershell
dotnet build CSharpRPGBackend.sln
dotnet test tests/CSharpRPGBackend.Tests/CSharpRPGBackend.Tests.csproj
```

The core regression suite covers runtime-state isolation, loadouts, leveling, world ticks, quest reward idempotence, the Ravensholm transformation, save round-trips, all LLM factories, credential serialization safety, and GameMaster turn/item/lock behavior.

The browser suite launches the real Blazor Server app and verifies autosave, refresh/continue behavior, and save isolation between independent browsers. Install its Chromium build once before running it locally:

```powershell
dotnet build tests/RPGWeb.E2ETests/RPGWeb.E2ETests.csproj
pwsh tests/RPGWeb.E2ETests/bin/Debug/net8.0/playwright.ps1 install chromium
dotnet test tests/RPGWeb.E2ETests/RPGWeb.E2ETests.csproj --no-build
```

Set `RPGWEB_DATA_DIRECTORY` to redirect runtime saves from the repository root. The browser suite uses a unique temporary directory automatically. GitHub Actions runs the build, core tests, and browser test for every push and pull request.

## Adding content

Games can be authored in C# under `src/Games` or loaded from JSON under `games`. JSON `game.json` files may include a `worldProjects` array using the same project, stage, condition, and effect fields as the C# models. Use catalog item IDs for starting loadouts, quest requirements, resources, recipes, and project contributions. `GameStateFactory` clones authored definitions into each new runtime so one session cannot mutate another.

For a growing world, add `WorldProject` definitions to a game and compose stages from `WorldProjectRequirement` and `WorldProjectEffect`. Keep gameplay consequences in services and use the LLM only to phrase the resulting events.

## License

MIT
