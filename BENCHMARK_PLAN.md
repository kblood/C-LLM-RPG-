# LLM Benchmarking Plan

## Goals

1. **Speed** — measure tokens per second (input and output separately) for each backend/model combination
2. **Latency** — measure time to first token (TTFT) and total wall-clock time
3. **Quality** — automatically score how well each model handles the specific tasks this game requires (JSON action parsing, NPC dialogue, narrative narration)
4. **Comparison** — run the same test suite against Ollama and llama.cpp and produce a side-by-side markdown report

---

## Metrics to Collect

| Metric | Source | Notes |
|---|---|---|
| Prompt tokens | Backend response | Ollama: `prompt_eval_count`; llama.cpp: `usage.prompt_tokens` |
| Output tokens | Backend response | Ollama: `eval_count`; llama.cpp: `usage.completion_tokens` |
| Prompt TPS | Backend response | Ollama: `prompt_eval_count / prompt_eval_duration`; llama.cpp: `timings.prompt_n / timings.prompt_ms` |
| Output TPS | Backend response | Ollama: `eval_count / eval_duration`; llama.cpp: `timings.predicted_n / timings.predicted_ms` |
| Time to first token | Stopwatch (streaming) | Time from request sent to first chunk received |
| Total latency | Stopwatch | Wall-clock time for full response |
| Quality score | Scorer functions | 0–100, per test case (see below) |

---

## Architecture

### New files

```
src/LLM/
  LlmMetrics.cs          # TokenStats, BenchmarkResult data structures
  BenchmarkTest.cs       # BenchmarkTest, BenchmarkSuite, scorer functions

src/Services/
  BenchmarkRunner.cs     # Runs a suite against one or more clients, collects results
```

### Changes to existing files

```
src/LLM/ILlmClient.cs
  + ChatWithMetricsAsync(messages) → (content: string, metrics: TokenStats)

src/LLM/OllamaClient.cs
  + Implement ChatWithMetricsAsync using prompt_eval_count / eval_count fields
  + Extend OllamaChatResponse with timing fields

src/LLM/LlamaCppClient.cs
  + Implement ChatWithMetricsAsync using usage + timings fields
  + Extend OpenAiChatResponse with usage / timings fields

Program.cs
  + Menu option 5: "Run Benchmark"
  + dotnet run benchmark  shortcut (bypasses menu)
```

---

## Data Structures

```csharp
// Raw timing + token data returned by a single chat call
public class TokenStats
{
    public int    PromptTokens      { get; set; }
    public int    OutputTokens      { get; set; }
    public double PromptTps         { get; set; }  // tokens/sec during prompt eval
    public double OutputTps         { get; set; }  // tokens/sec during generation
    public double TimeToFirstTokenMs { get; set; } // wall-clock ms to first chunk
    public double TotalLatencyMs    { get; set; }  // wall-clock ms for full response
}

// Result of one test case against one client
public class BenchmarkResult
{
    public string     BackendName  { get; set; }
    public string     ModelName    { get; set; }
    public string     TestId       { get; set; }
    public string     TestCategory { get; set; }
    public TokenStats Metrics      { get; set; }
    public int        QualityScore { get; set; }  // 0-100
    public string     ScoreReason  { get; set; }  // human-readable explanation
    public string     RawOutput    { get; set; }
}
```

---

## Test Suite

Tests are grouped into categories that map directly to how this game uses the LLM.

### Category 1 — JSON Action Parsing (critical path)

The GameMaster's most important task is converting natural language to a valid JSON action array.
A failure here breaks the game entirely; quality is binary (valid JSON with correct structure = pass).

| Test ID | Prompt | Expected | Scorer |
|---|---|---|---|
| `json_move_simple` | "go north" | `[{"action":"move","target":"north"}]` | Valid JSON + action=move |
| `json_multi_action` | "pick up the sword and go east" | Two actions: take + move | Valid JSON array with 2 entries |
| `json_attack` | "attack the goblin" | `[{"action":"attack","target":"goblin"}]` | Valid JSON + action=attack |
| `json_talk` | "talk to the merchant" | `[{"action":"talk","target":"merchant"}]` | Valid JSON + action=talk |
| `json_ambiguous` | "look around" | `[{"action":"look","target":""}]` or similar | Valid JSON + action=look/examine |

Scoring: 100 = valid JSON with correct action key, 50 = valid JSON but wrong structure, 0 = invalid JSON

### Category 2 — NPC Dialogue Quality

Tests NPC Brain responses for coherence, staying in character, and appropriate length.

| Test ID | Prompt | Criteria | Scorer |
|---|---|---|---|
| `npc_greeting` | "Hello there" | Friendly, in-character, 1-3 sentences | Length + keyword check |
| `npc_trade` | "Do you have any weapons for sale?" | Mentions trading/items, not off-topic | Relevance check |
| `npc_hostile` | "I will destroy you!" (hostile NPC personality) | Aggressive tone, in character | Tone keyword check |
| `npc_lore` | "Tell me about this dungeon" | Provides world-building detail | Length + coherence |

Scoring: 100 = on topic + appropriate length + in character, partial credit for partial matches

### Category 3 — Narrative Narration

Tests the narration step (NarrateWithResultsAsync) for quality, not contradicting the result, and being engaging.

| Test ID | Action Result | Criteria | Scorer |
|---|---|---|---|
| `narrate_move_success` | Player moved north successfully | Mentions direction, describes new area | Keyword presence |
| `narrate_attack_hit` | Player dealt 15 damage to goblin | Mentions damage/hit, not a miss | Does not contradict result |
| `narrate_attack_miss` | Player missed | Describes a miss | Does not claim hit occurred |
| `narrate_item_pickup` | Player picked up iron sword | Mentions the item name | Item name present in output |

Scoring: 100 = accurate + engaging, 50 = accurate but dry/short, 0 = contradicts the result

### Category 4 — Raw Speed (no quality scoring)

Simple fixed-length prompts to get clean TPS numbers without quality noise.

| Test ID | Prompt | Purpose |
|---|---|---|
| `speed_short` | 10-token prompt | Measure TTFT and output TPS for small requests |
| `speed_medium` | 200-token prompt | Typical game context size |
| `speed_long` | 800-token prompt | Full game state context (worst case) |

Each speed test is run 3 times and the results are averaged.

---

## Scorer Functions

```
JsonActionScorer(output, expectedAction):
  1. Try parse output as JSON array
  2. If fail → 0
  3. Check first element has "action" key → +50
  4. Check action value matches expected → +30
  5. Check "target" key present → +20

DialogueQualityScorer(output, minWords, maxWords, requiredKeywords):
  1. Word count in [minWords, maxWords] → +40
  2. Each required keyword present → +20 each (capped at +40)
  3. No refusal phrases ("I cannot", "As an AI") → +20

NarrationAccuracyScorer(output, result):
  1. Does not contradict result.Success flag → +50
  2. Key facts from result present in output → +30
  3. Length >= 30 words → +20
```

---

## Output Report

The benchmark runner produces a `BENCHMARK_<timestamp>.md` file, e.g.:

```markdown
# Benchmark Report — 2026-02-21 14:30

## Configuration
- Backends tested: Ollama (http://localhost:11434), llama.cpp (http://localhost:8080)
- Models: granite4:3b (Ollama), granite4:3b (llama.cpp)
- Runs per test: 3 (averaged)

## Speed Summary

| Backend    | Model       | Prompt TPS | Output TPS | TTFT (ms) | Total Latency (ms) |
|------------|-------------|------------|------------|-----------|--------------------|
| Ollama     | granite4:3b | 1 240      | 47.3       | 180       | 2 100              |
| llama.cpp  | granite4:3b | 890        | 52.1       | 95        | 1 850              |

## Quality Summary

| Category          | Ollama granite4:3b | llama.cpp granite4:3b |
|-------------------|--------------------|----------------------|
| JSON Parsing      | 92 / 100           | 88 / 100             |
| NPC Dialogue      | 74 / 100           | 71 / 100             |
| Narration         | 68 / 100           | 65 / 100             |
| **Overall**       | **78 / 100**       | **75 / 100**         |

## Detailed Results

### JSON Action Parsing
...per-test breakdown...

### NPC Dialogue
...per-test breakdown...
```

The report is also printed as a table to the console during the run.

---

## Menu Integration

```
╔══════════════════════════════════════════╗
║      C# RPG Backend  – Main Menu         ║
╠══════════════════════════════════════════╣
║  1. Play a game                          ║
║  2. Run replay (automated)               ║
║  3. LLM Settings                         ║
║  4. Run Benchmark                        ║
║  5. Quit                                  ║
╚══════════════════════════════════════════╝
```

The benchmark menu will ask:
- Which backends to include (current only / all configured / enter URL manually)
- Which model(s) to test (fetched live from each backend)
- Which test categories to run (all / speed only / quality only / specific category)
- Number of runs to average (default: 3)

`dotnet run benchmark` skips the menu and runs all categories against the currently configured backend/model.

---

## Implementation Order

1. **`LlmMetrics.cs`** — data structures only, no logic
2. **`ILlmClient` update** — add `ChatWithMetricsAsync` signature
3. **`OllamaClient` update** — extend response model + implement `ChatWithMetricsAsync`
4. **`LlamaCppClient` update** — extend response model + implement `ChatWithMetricsAsync`
5. **`BenchmarkTest.cs`** — test cases and scorer functions
6. **`BenchmarkRunner.cs`** — orchestrates runs, collects results, renders report
7. **`Program.cs`** — wire up menu option and `dotnet run benchmark` shortcut
