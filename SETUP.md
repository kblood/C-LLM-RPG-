# Setup Guide - C# RPG Backend with Ollama

## Prerequisites

- Windows/Mac/Linux
- .NET 8.0 or later ([download](https://dotnet.microsoft.com/download/dotnet/8.0))
- Ollama ([download](https://ollama.ai))

## Step 1: Install Ollama

1. Download from https://ollama.ai
2. Install and run `ollama serve` (keeps running in background on http://localhost:11434)

## Step 2: Download an LLM Model

In a new terminal (while Ollama is running):

```bash
ollama pull mistral
```

Other options:
- `ollama pull qwen` - Good for dialogue
- `ollama pull llama2` - Great for roleplay
- `ollama pull neural-chat` - Fast and conversational
- `ollama pull orca-mini` - Lightweight (~3GB)

To see available models: https://ollama.ai/library

## Step 3: Build & Run

```bash
cd C:\Devstuff\git\CSharpRPGBackend
dotnet build
dotnet run
```

If you see "✓ Connected to Ollama", you're good to go!

## Step 4: Play the Game

```
> Town Square
What do you do? help

Narrating your action...
[Help text appears]

What do you do? look
What do you do? talk merchant
What do you say? Can you help me?
```

## Troubleshooting

### Port Already in Use
If 11434 is taken, start Ollama on a different port:
```bash
ollama serve --addr 127.0.0.1:8888
```

Then update `Program.cs`:
```csharp
const string ollamaUrl = "http://localhost:8888";
```

### Model Download Takes Forever
Use a smaller model:
```bash
ollama pull orca-mini
```

Then update `Program.cs`:
```csharp
const string ollamaModel = "orca-mini";
```

### Out of Memory
- Use a smaller model
- Reduce conversation history in `NpcBrain.cs` (change `TakeLast(10)` to `TakeLast(5)`)
- Close other applications

### Can't Connect to Ollama
1. Verify Ollama is running: `ollama serve`
2. Test connection: `curl http://localhost:11434/api/tags`
3. Check firewall settings

## Performance Tips

- **Fastest Response**: Use `orca-mini` or `neural-chat`
- **Best Quality**: Use `mistral` or `qwen`
- **Streaming**: Use `ChatStreamAsync` in `NpcBrain.cs` for long responses
- **Context**: Reduce history size if responses are slow

## Next Steps

1. Customize NPCs in `GameState.cs`
2. Add new rooms and locations
3. Extend the combat system
4. Create quests and objectives
5. Build a Godot/Unity integration layer

Ready to follow the next instructions for more customization!
