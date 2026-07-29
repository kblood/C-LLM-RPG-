# C# RPG Backend - Architecture Diagrams

## 1. High-Level System Architecture

```mermaid
graph TB
    subgraph Entry["Program.cs - Entry Point"]
        CLI[CLI Args & Env Vars]
        MainMenu[Main Menu]
        Settings[LLM Settings Menu]
    end

    subgraph Modes["Execution Modes"]
        Interactive[Interactive Mode<br/>Player types commands]
        Replay[Replay Mode<br/>AI plays the game]
        Editor[WinForms Game Editor<br/>Visual game creation]
    end

    subgraph GameDefs["Game Definitions"]
        Fantasy[FantasyQuest<br/>8 rooms, 8+ NPCs<br/>Tiered economy]
        SciFi[SciFiAdventure<br/>9 rooms, 5 NPCs<br/>No economy]
        Custom[Custom Games<br/>Loaded from games/ dir]
    end

    subgraph Builders["Builder Utilities"]
        GB[GameBuilder]
        RB[RoomBuilder]
        NB[NpcBuilder]
        IB[ItemBuilder]
    end

    subgraph Core["Core Runtime"]
        GS[GameState<br/>Deterministic state container]
        GM[GameMaster<br/>Orchestration service<br/>3,774 lines]
        CS[CombatService<br/>Deterministic combat math]
    end

    subgraph LLM["LLM Integration"]
        IClient[ILlmClient Interface]
        Ollama[OllamaClient<br/>localhost:11434]
        LlamaCpp[LlamaCppClient<br/>localhost:8080]
        NpcBrain[NpcBrain<br/>Per-NPC personality AI]
        LlmSet[LlmSettings<br/>Persisted config]
    end

    subgraph Models["Data Models"]
        Character[Character]
        Room[Room & Exit]
        Item[Item & Inventory]
        Quest[Quest & Requirements]
        Currency[Wallet & Economy]
        Crafting[CraftingRecipe]
        Authority[GameMasterAuthority]
        Equipment[EquipmentSlots]
        Resources[RoomResources]
    end

    CLI --> MainMenu
    MainMenu --> Interactive
    MainMenu --> Replay
    MainMenu --> Settings
    Settings --> LlmSet
    LlmSet --> IClient

    Interactive --> GM
    Replay --> GM

    GameDefs --> GS
    Builders --> GameDefs
    Editor --> Custom

    GM --> GS
    GM --> CS
    GM --> NpcBrain
    GM --> IClient

    IClient --> Ollama
    IClient --> LlamaCpp

    GS --> Models
```

---

## 2. Two-Step LLM Game Loop (Core Architecture)

```mermaid
flowchart TB
    Input["Player Input<br/>(text command)"]

    subgraph Step1["Step 1: LLM Decision"]
        Context["Build Game Context<br/>- Current room & exits<br/>- NPCs (alive/dead status)<br/>- Player inventory & health<br/>- Combat state<br/>- Recent commands"]
        Decide["DecideActionsAsync()<br/>LLM returns JSON array"]
        Fallback["TryParseFallback()<br/>Hardcoded command parsing"]
        Actions["List&lt;ActionPlan&gt;<br/>[{action, target, details}]"]
    end

    subgraph Step2["Step 2: Deterministic Execution"]
        Router["ApplyActionAsync()<br/>Switch on action type"]

        subgraph Handlers["40+ Action Handlers"]
            Move[HandleMove]
            Talk[HandleTalkAsync]
            Attack[HandleAttack]
            Auto[HandleAuto]
            Take[HandleTake]
            Drop[HandleDrop]
            Use[HandleUse]
            Equip[HandleEquip]
            Buy[HandleBuy]
            Sell[HandleSell]
            Gather[HandleGatherAsync]
            Craft[HandleCraftAsync]
            Look[HandleLook]
            Status[HandleStatus]
            Inv[HandleInventory]
            Quests[HandleQuests]
            Flee[HandleFlee]
            More[... and more]
        end

        Results["List&lt;ActionResult&gt;<br/>{Success, Message}"]
    end

    subgraph Step3["Step 3: LLM Narration"]
        Narrate["NarrateWithResultsAsync()<br/>LLM creates engaging narrative<br/>grounded in actual outcomes"]
        DirectOut["Direct Output<br/>(info commands skip narration)"]
    end

    GameState["GameState Updated"]
    Response["Final Response<br/>Narration + Game State Footer"]

    Input --> Context
    Context --> Decide
    Decide -->|Valid JSON| Actions
    Decide -->|Invalid JSON| Fallback
    Fallback --> Actions
    Actions --> Router

    Router --> Move & Talk & Attack & Auto & Take & Drop & Use & Equip & Buy & Sell & Gather & Craft & Look & Status & Inv & Quests & Flee & More

    Handlers --> Results
    Handlers --> GameState

    Results -->|Combat/Narrative actions| Narrate
    Results -->|Info actions: look, status, inventory| DirectOut
    Narrate --> Response
    DirectOut --> Response
```

---

## 3. AI Replay System (How AI Plays the Game)

```mermaid
flowchart TB
    Start["GameReplay.PlayGameAsync(maxTurns=30)"]

    subgraph Init["Initialization"]
        CreateState["Create fresh GameState"]
        CreateGM["Create GameMaster"]
        InitTracking["Initialize tracking:<br/>- visitedRooms: HashSet<br/>- talkedToNpcs: HashSet<br/>- recentActions: List (last 8)<br/>- turnsSinceExplored<br/>- turnsSinceCombat"]
        InitLog["Start markdown log"]
    end

    subgraph TurnLoop["Turn Loop (up to maxTurns)"]
        LogState["Log current state:<br/>Room, Health, Exits, NPCs, Inventory"]
        GenAction["GeneratePlayerActionAsync()"]

        subgraph AIDecision["AI Decision Engine"]
            BuildCtx["Build Strategic Context:<br/>- Health percentage<br/>- Combat status<br/>- Room exits (marked visited/unvisited)<br/>- NPCs classified as enemy/ally<br/>- Items on floor<br/>- Quest objectives"]

            subgraph Priorities["Strategic Priority Hints"]
                P1["1. IN COMBAT → use 'auto'"]
                P2["2. Hostile enemy present → attack"]
                P3["3. Health < 25% → heal or flee"]
                P4["4. Items on floor → pick up"]
                P5["5. Active quests → work toward goals"]
                P6["6. Unvisited exits → EXPLORE"]
                P7["7. Fresh NPCs → talk once"]
                P8["8. Stuck too long → move somewhere"]
            end

            LLMCall["LLM Call:<br/>'You are an expert RPG player<br/>making smart decisions'<br/>Returns single action command"]
        end

        subgraph FallbackLogic["Fallback (if LLM fails)"]
            FB1["In combat? → 'auto'"]
            FB2["Enemies alive? → 'attack {name}'"]
            FB3["Unvisited exits? → 'go {exit}'"]
            FB4["Any exits? → 'go {random exit}'"]
            FB5["Default → 'look around'"]
        end

        Execute["GameMaster.ProcessPlayerActionAsync(action)"]
        LogResult["Log narration to markdown"]
        UpdateTrack["Update tracking:<br/>- Mark room visited<br/>- Mark NPC talked to<br/>- Add to recent actions<br/>- Increment turn counters"]

        subgraph EndChecks["End Condition Checks"]
            WinCheck["Win condition met?<br/>(reached goal room)"]
            DeathCheck["Player dead?<br/>(Health <= 0)"]
            TurnCheck["Max turns reached?"]
        end
    end

    SaveLog["Save REPLAY_{game}.md"]

    Start --> Init
    Init --> TurnLoop
    LogState --> GenAction
    GenAction --> BuildCtx
    BuildCtx --> Priorities
    Priorities --> LLMCall
    LLMCall -->|Success| Execute
    LLMCall -->|Failure| FallbackLogic
    FallbackLogic --> Execute
    Execute --> LogResult
    LogResult --> UpdateTrack
    UpdateTrack --> EndChecks
    WinCheck -->|No| LogState
    WinCheck -->|Yes: Victory!| SaveLog
    DeathCheck -->|Yes: Game Over| SaveLog
    TurnCheck -->|Yes: Timeout| SaveLog
```

---

## 4. Data Model Relationships

```mermaid
classDiagram
    class Game {
        +string Id
        +string Title
        +string Subtitle
        +string Description
        +string StartingRoomId
        +GameStyle Style
        +EconomyConfig Economy
        +GameMasterAuthority Authority
        +CraftingConfig Crafting
        +EquipmentSlotConfiguration Equipment
        +Dictionary~string,Room~ Rooms
        +Dictionary~string,Character~ NPCs
        +Dictionary~string,Item~ Items
        +List~Quest~ Quests
        +List~string~ WinConditions
        +bool FreeRoam
    }

    class GameState {
        +string CurrentRoomId
        +Character Player
        +Dictionary~string,Room~ Rooms
        +Dictionary~string,Character~ NPCs
        +Inventory PlayerInventory
        +List~Quest~ ActiveQuests
        +List~string~ Companions
        +bool InCombatMode
        +string CurrentCombatNpcId
        +MoveToRoomByExit(exitName) bool
        +GetCurrentRoom() Room
        +GetNPCInRoom(npcId) Character
        +AddCompanion(npcId)
        +MoveCompanionsToCurrentRoom()
    }

    class Room {
        +string Id
        +string Name
        +string Description
        +Dictionary~string,Exit~ Exits
        +List~string~ NPCIds
        +List~Item~ Items
        +RoomResources Resources
        +GetAvailableExits() List~Exit~
        +FindExit(displayName) Exit
    }

    class Exit {
        +string Id
        +string DisplayName
        +string DestinationRoomId
        +string Description
        +bool IsAvailable
    }

    class Character {
        +string Id
        +string Name
        +int Health / MaxHealth
        +int Strength
        +int Agility
        +int Armor
        +NPCRole Role
        +string PersonalityPrompt
        +Dictionary~string,string~ EquipmentSlots
        +Dictionary~string,InventoryItem~ CarriedItems
        +Wallet Wallet
        +string CurrentRoomId
        +bool CanMove
        +bool CanCraft
        +string CraftingSpecialty
        +bool IsAlive
        +GetTotalDamage(weapon) int
        +GetTotalArmor(equipped) int
        +TakeDamage(amount)
        +EquipItem(item, slot) bool
    }

    class Item {
        +string Id
        +string Name
        +ItemType Type
        +int DamageBonus
        +int ArmorBonus
        +bool IsEquippable
        +string EquipmentSlot
        +bool IsConsumable
        +bool IsTeleportation
        +ItemPricing Pricing
        +ItemRarity Rarity
        +GetBuyPrice() long
        +GetSellPrice() long
    }

    class Inventory {
        +Dictionary~string,InventoryItem~ Items
        +int MaxWeight
        +AddItem(item, qty) bool
        +RemoveItem(itemId, qty) bool
        +GetItem(itemId) InventoryItem
    }

    class Quest {
        +string Id
        +string Title
        +QuestType Type
        +QuestStatus Status
        +string GiverNpcId
        +List~QuestRequirement~ Requirements
        +QuestRewards Rewards
        +bool IsRepeatable
    }

    class Wallet {
        +long TotalBaseUnits
        +Add(amount)
        +Remove(amount) bool
        +CanAfford(amount) bool
        +Format(economyConfig) string
    }

    class CraftingRecipe {
        +string Id
        +string OutputItemId
        +int OutputQuantity
        +List~RecipeIngredient~ Ingredients
        +string CraftingSpecialty
        +long CraftingCost
    }

    class GameMasterAuthority {
        +bool CanCreateItems
        +bool CanCreateQuests
        +bool CanDecideResources
        +bool CanCreateRecipes
        +bool CanCreateNPCs
        +int NarrationCreativity
        +Strict()$ GameMasterAuthority
        +Balanced()$ GameMasterAuthority
        +Dynamic()$ GameMasterAuthority
        +OpenWorld()$ GameMasterAuthority
    }

    Game "1" *-- "many" Room
    Game "1" *-- "many" Character
    Game "1" *-- "many" Item
    Game "1" *-- "many" Quest
    Game "1" *-- "1" EconomyConfig
    Game "1" *-- "1" GameMasterAuthority
    Game "1" *-- "1" CraftingConfig

    GameState "1" *-- "many" Room
    GameState "1" *-- "1" Character : Player
    GameState "1" *-- "many" Character : NPCs
    GameState "1" *-- "1" Inventory
    GameState "1" *-- "many" Quest

    Room "1" *-- "many" Exit
    Room "1" o-- "many" Character : NPCIds
    Room "1" o-- "many" Item : floor items

    Character "1" *-- "1" Wallet
    Character "1" o-- "many" Item : CarriedItems
    Character "1" o-- "many" CraftingRecipe : KnownRecipes

    Quest "1" *-- "many" QuestRequirement
    Quest "1" *-- "1" QuestRewards

    Inventory "1" *-- "many" InventoryItem
```

---

## 5. LLM Integration Architecture

```mermaid
flowchart TB
    subgraph Config["Configuration Layer"]
        LlmSettings["LlmSettings<br/>llm-settings.json<br/>- Backend: ollama | llamacpp<br/>- Model: granite4:3b<br/>- URLs & context size"]
        LlmSettings -->|CreateClient()| Factory["Factory Method"]
    end

    subgraph Interface["ILlmClient Interface"]
        ChatAsync["ChatAsync(messages, model)<br/>→ string"]
        ChatStreamAsync["ChatStreamAsync(messages, model)<br/>→ IAsyncEnumerable&lt;string&gt;"]
        IsHealthy["IsHealthyAsync() → bool"]
        ListModels["ListModelsAsync() → List&lt;string&gt;"]
    end

    subgraph OllamaImpl["OllamaClient"]
        OllamaHTTP["POST /api/chat<br/>localhost:11434"]
        OllamaModels["GET /api/tags"]
        OllamaReq["OllamaChatRequest<br/>{Model, Messages, Stream,<br/>Options: {NumCtx: 8192}}"]
    end

    subgraph LlamaCppImpl["LlamaCppClient"]
        LlamaHTTP["POST /v1/chat/completions<br/>OpenAI-compatible API<br/>localhost:8080"]
        LlamaHealth["GET /health"]
        LlamaModels["GET /v1/models"]
        subgraph ServerMgmt["Server Lifecycle"]
            FindExe["FindLlamaServerExe()<br/>Search PATH & common dirs"]
            FindModel["FindOllamaModelPath()<br/>Read Ollama manifests<br/>→ GGUF blob path"]
            Launch["LaunchServer(model, port, ctx)<br/>Spawn llama-server process"]
            Stop["StopServer()<br/>Kill tracked process"]
        end
    end

    subgraph Callers["LLM Call Points in Codebase"]
        GM_Decide["GameMaster.DecideActionsAsync()<br/>→ JSON action array"]
        GM_Narrate["GameMaster.NarrateWithResultsAsync()<br/>→ Narrative text"]
        NPC_Talk["NpcBrain.RespondToPlayerAsync()<br/>→ NPC dialogue"]
        NPC_Intent["NpcBrain.GetIntentAsync()<br/>→ NPC intent JSON"]
        GM_Follow["GetNpcFollowDecisionAsync()<br/>→ Follow decision"]
        GM_Give["GetNpcGiveDecisionAsync()<br/>→ Accept/reject items"]
        GM_Gather["TryDynamicGatherAsync()<br/>→ Resource availability"]
        Replay_AI["GameReplay.GeneratePlayerActionAsync()<br/>→ Next player action"]
    end

    Factory -->|"ollama"| OllamaImpl
    Factory -->|"llamacpp"| LlamaCppImpl

    OllamaImpl -.->|implements| Interface
    LlamaCppImpl -.->|implements| Interface

    Callers -->|via ILlmClient| Interface

    subgraph MessageFormat["Chat Message Format"]
        Msg["ChatMessage<br/>Role: system | user | assistant<br/>Content: string"]
        Conv["Conversation = List&lt;ChatMessage&gt;<br/>[system prompt, ...history, user msg]"]
    end
```

---

## 6. Combat System

```mermaid
flowchart TB
    subgraph Initiation["Combat Initiation"]
        PlayerAttack["Player: 'attack goblin'"]
        HandleAttack["HandleAttack(npcName)<br/>- Find NPC in room<br/>- Set InCombatMode = true<br/>- Set CurrentCombatNpcId"]
    end

    subgraph Resolution["CombatService.ResolveAttack()"]
        HitCheck["Hit Check<br/>Base: 70%<br/>+ attacker agility × 2%<br/>- defender agility × 3%<br/>Clamped: 20-95%"]

        HitCheck -->|Miss| DodgeResult["Dodge!<br/>No damage dealt"]
        HitCheck -->|Hit| DamageCalc

        DamageCalc["Damage Calculation<br/>Base: 5 + (Strength-10)/2<br/>+ Weapon DamageBonus<br/>Minimum: 1"]

        CritCheck["Critical Check<br/>Base: 5%<br/>+ 1% per agility above 10<br/>Clamped: 1-50%"]

        DamageCalc --> CritCheck
        CritCheck -->|Critical!| CritDmg["Damage × 1.5"]
        CritCheck -->|Normal| ArmorCalc

        CritDmg --> ArmorCalc
        ArmorCalc["Armor Reduction<br/>Base Armor stat<br/>+ equipped armor bonuses<br/>Reduction: armor / 2<br/>Max reduction: 15"]

        ArmorCalc --> FinalDmg["Final Damage<br/>max(1, damage - armorReduction)"]
    end

    subgraph AutoCombat["HandleAuto() - Auto-Attack Mode"]
        AutoLoop["Loop rounds until:<br/>- Player health ≤ 5% MaxHealth<br/>- OR enemy defeated"]
        RoundN["Each round:<br/>1. Player attacks enemy<br/>2. If enemy alive: enemy attacks player<br/>3. Track damage totals"]
        Summary["Summary output:<br/>- Rounds fought<br/>- Total damage dealt<br/>- Total damage taken<br/>- Final health states"]
    end

    subgraph Flee["AttemptFlee()"]
        FleeCalc["Flee Chance<br/>Base: 50%<br/>+ 5% per agility difference<br/>(player - enemy)<br/>Clamped: 15-95%"]
        FleeCalc -->|Success| ExitCombat["InCombatMode = false<br/>CurrentCombatNpcId = null"]
        FleeCalc -->|Fail| StillFighting["Remain in combat<br/>Enemy gets free attack"]
    end

    subgraph Death["NPC Death"]
        NPCDead["NPC Health ≤ 0"]
        NPCDead --> MarkDead["CanMove = false<br/>Display: ☠️ skull"]
        MarkDead --> LootCheck{"Has items<br/>or currency?"}
        LootCheck -->|Yes| LootIcon["Display: ☠️💰<br/>Can examine & loot"]
        LootCheck -->|No| JustDead["Display: ☠️<br/>Body remains in room"]
    end

    PlayerAttack --> HandleAttack
    HandleAttack --> Resolution
    Resolution --> AutoCombat
```

---

## 7. Economy & Trading System

```mermaid
flowchart TB
    subgraph EconConfig["Economy Configuration"]
        Disabled["WithoutEconomy()<br/>No trading, no currency"]
        Simple["WithSimpleEconomy()<br/>Single currency<br/>(Credits, Bottlecaps, etc.)"]
        Tiered["WithTieredEconomy()<br/>Platinum / Gold / Silver<br/>100 silver = 1 gold<br/>100 gold = 1 platinum"]
    end

    subgraph Wallet["Wallet System"]
        Store["TotalBaseUnits<br/>All currency as smallest unit"]
        Add["Add(amount)"]
        Remove["Remove(amount) → bool"]
        Afford["CanAfford(amount) → bool"]
        Format["Format() → display string<br/>Tiered: '2g 50s'<br/>Simple: '150 Credits'"]
    end

    subgraph Trading["Trading Flow"]
        Shop["'shop merchant'<br/>HandleShop()<br/>Lists items with prices"]
        Buy["'buy sword from merchant'<br/>HandleBuy()<br/>1. Find merchant NPC<br/>2. Find item in merchant stock<br/>3. Check player can afford<br/>4. Transfer currency & item"]
        Sell["'sell potion to merchant'<br/>HandleSell()<br/>1. Find merchant NPC<br/>2. Find item in player inventory<br/>3. Check merchant can afford<br/>4. Transfer item & currency"]
    end

    subgraph Pricing["Item Pricing"]
        BuyPrice["Buy Price<br/>BasePrice × BuyMultiplier<br/>(default: full price)"]
        SellPrice["Sell Price<br/>BasePrice × SellMultiplier<br/>(default: 50% of buy)"]
        Flags["CanBuy / CanSell flags<br/>(quest items: not buyable)"]
    end

    subgraph Loot["Currency Loot"]
        DefeatNPC["Defeat enemy NPC"]
        AutoLoot["Currency auto-looted<br/>from NPC Wallet"]
    end

    EconConfig --> Wallet
    Wallet --> Trading
    Pricing --> Trading
    DefeatNPC --> AutoLoot
    AutoLoot --> Wallet
```

---

## 8. Crafting & Gathering System

```mermaid
flowchart TB
    subgraph Gathering["Resource Gathering"]
        RoomRes["Room has RoomResources<br/>- Biome (cave, forest, etc.)<br/>- ResourceTags (ore, herbs)<br/>- GatherableResource list"]
        PlayerGather["Player: 'gather ore'"]
        CheckDefined{"Room has<br/>defined resource?"}
        CheckDefined -->|Yes| RollChance["Roll against FindChance<br/>Get MinQty-MaxQty items"]
        CheckDefined -->|No| CheckAuthority{"GM Authority:<br/>CanDecideResources?"}
        CheckAuthority -->|Yes| DynamicGather["TryDynamicGatherAsync()<br/>LLM decides if resource<br/>exists based on biome"]
        CheckAuthority -->|No| NoResource["'Nothing to gather here'"]
        ToolCheck["RequiredTool check<br/>(pickaxe for ore, etc.)"]
    end

    subgraph Crafting["Crafting System"]
        Recipe["CraftingRecipe<br/>- OutputItemId<br/>- Ingredients list<br/>- CraftingSpecialty<br/>- CraftingCost (currency)"]

        PlayerCraft["Player: 'craft sword'<br/>or 'craft sword with blacksmith'"]
        FindCrafter{"Find NPC crafter<br/>with matching specialty"}
        FindCrafter -->|Found| CheckMaterials{"Player has<br/>all ingredients?"}
        CheckMaterials -->|Yes| CheckCost{"Player can<br/>afford cost?"}
        CheckCost -->|Yes| CraftItem["Deduct materials & currency<br/>Add crafted item to inventory"]
        CheckMaterials -->|No| MissingMats["'Missing materials: ...'"]
        CheckCost -->|No| NoCash["'Not enough currency'"]
        FindCrafter -->|Not found| NoCrafter["'No crafter available'"]
    end

    subgraph Recipes["Recipe Viewing"]
        ViewRecipes["Player: 'recipes blacksmith'"]
        ListRecipes["Show NPC's available recipes<br/>with ingredients and costs"]
    end

    RollChance --> AddToInv["Add items to inventory"]
    DynamicGather --> AddToInv
    AddToInv -.->|Materials used in| Crafting
```

---

## 9. NPC Brain & Conversation System

```mermaid
flowchart TB
    subgraph Init["NPC Brain Initialization"]
        GMInit["GameMaster.InitializeNpcBrains()"]
        ForEach["For each NPC in game:"]
        CustomPrompt{"Has custom<br/>PersonalityPrompt?"}
        CustomPrompt -->|Yes| UseCustom["Use NPC's PersonalityPrompt"]
        CustomPrompt -->|No| AutoGen["Auto-generate prompt:<br/>'You are {Name}, an NPC...<br/>Health: {hp}/{maxHp}<br/>Personality: Helpful and mysterious<br/>Keep responses brief (1-3 sentences)'"]
        UseCustom --> CreateBrain["new NpcBrain(llmClient, npc, prompt)"]
        AutoGen --> CreateBrain
    end

    subgraph Conversation["NPC Conversation Flow"]
        PlayerTalk["Player: 'talk to bartender'"]
        FindNPC["Find NPC in current room"]
        BuildMessages["Build message list:<br/>1. System prompt (personality)<br/>2. Last 10 conversation history<br/>3. Player's current message"]
        LLMCall["LLM ChatAsync()<br/>NPC responds in character"]
        SaveHistory["Save to NPC's ConversationHistory:<br/>- Player message<br/>- NPC response<br/>(persists across turns)"]
    end

    subgraph Decisions["NPC Decision Making"]
        FollowReq["Player: 'follow me'"]
        FollowDecision["GetNpcFollowDecisionAsync()<br/>LLM returns JSON:<br/>{willFollow, reason, emotion}"]

        GiveReq["Player: 'give sword to npc'"]
        GiveDecision["GetNpcGiveDecisionAsync()<br/>LLM returns JSON:<br/>{accepted, response, emotion}"]

        IntentReq["Game context trigger"]
        IntentDecision["GetIntentAsync()<br/>LLM returns JSON:<br/>{intent, emotion, willTrade}<br/>intent: friendly|hostile|trade|ignore"]
    end

    subgraph NPCTypes["NPC Role Classification"]
        Merchant["Merchant 🛒<br/>Buy/sell items"]
        Guard["Guard<br/>Patrols & defends"]
        Warrior["Warrior<br/>Combat-focused"]
        Boss["Boss<br/>Key encounters"]
        Questgiver["Questgiver<br/>Offers quests"]
        Healer["Healer<br/>Healing services"]
        Companion["Companion<br/>Follows player"]
    end

    GMInit --> ForEach
    ForEach --> CustomPrompt
    PlayerTalk --> FindNPC --> BuildMessages --> LLMCall --> SaveHistory
```

---

## 10. Program Startup & Mode Selection

```mermaid
flowchart TB
    Start["Program.cs Main()"]
    LoadSettings["LlmSettings.Load()<br/>from llm-settings.json"]
    CLIArgs["Parse CLI args:<br/>--backend, --model<br/>--ollama-url, --llamacpp-url<br/>LLM_BACKEND env var"]
    CreateClient["settings.CreateClient()<br/>→ ILlmClient"]

    CheckArgs{"CLI argument?"}
    CheckArgs -->|"replay"| ReplayMode
    CheckArgs -->|"test"| TestMode["RunEquipmentTest()"]
    CheckArgs -->|none| MainMenu

    subgraph MainMenu["Main Menu Loop"]
        ShowMenu["1. Play a game<br/>2. Run replay<br/>3. LLM Settings<br/>4. Quit"]
        Choice{"User choice"}
    end

    subgraph SettingsMenu["LLM Settings"]
        ShowConfig["Current: Backend, Model, URLs"]
        ChangeBackend["Change backend<br/>Ollama ↔ llama.cpp"]
        ChangeModel["Change model<br/>Browse available models"]
        TestConn["Test connection<br/>Health check + model list"]
        StartServer["Start llama-server<br/>(if llama.cpp)"]
        SaveSettings["Save to llm-settings.json"]
    end

    subgraph InteractiveMode["Interactive Game"]
        SelectGame["Select game:<br/>- FantasyQuest (built-in)<br/>- SciFiAdventure (built-in)<br/>- Custom (from games/ dir)"]
        InitGame["Create GameState<br/>Init economy & items<br/>Create GameMaster"]
        HealthCheck["Check LLM backend health"]
        GameLoop["Game Loop:<br/>Prompt → Process → Display<br/>Until quit/win/death"]
        SessionLog["Write SESSION_*.log"]
    end

    subgraph ReplayMode["Replay Mode"]
        CollectGames["Gather all games<br/>(built-in + custom)"]
        ForEachGame["For each game:"]
        InitReplay["Create GameState<br/>Create GameMaster<br/>Create GameReplay"]
        RunReplay["replay.PlayGameAsync(30 turns)"]
        SaveReplay["Save REPLAY_{game}.md"]
    end

    Start --> LoadSettings --> CLIArgs --> CreateClient --> CheckArgs
    Choice -->|1| InteractiveMode
    Choice -->|2| ReplayMode
    Choice -->|3| SettingsMenu
    SettingsMenu --> MainMenu

    SelectGame --> InitGame --> HealthCheck --> GameLoop --> SessionLog
    CollectGames --> ForEachGame --> InitReplay --> RunReplay --> SaveReplay
```

---

## 11. Quest System

```mermaid
flowchart LR
    subgraph Types["Quest Types"]
        Story["Story<br/>Main storyline"]
        Side["SideQuest<br/>Optional content"]
        Job["Job<br/>Repeatable tasks"]
        Bounty["Bounty<br/>Kill targets"]
        Craft["CraftingOrder<br/>Make items"]
        Escort["Escort<br/>Protect NPCs"]
        Explore["Exploration<br/>Find locations"]
        Deliver["Delivery<br/>Transport items"]
    end

    subgraph Lifecycle["Quest Lifecycle"]
        Offered["Offered<br/>NPC presents quest"]
        Accepted["Accepted<br/>Player takes quest"]
        InProgress["InProgress<br/>Working on it"]
        Completed["Completed<br/>Requirements met"]
        TurnedIn["TurnedIn<br/>Rewards claimed"]
        Failed["Failed<br/>Quest failed"]
    end

    subgraph Requirements["Requirement Types"]
        ItemReq["item<br/>Collect X of item"]
        KillReq["kill<br/>Defeat X enemies"]
        LocationReq["location<br/>Visit a room"]
        TalkReq["talk<br/>Speak to NPC"]
        CraftReq["craft<br/>Create an item"]
        GatherReq["gather<br/>Find materials"]
    end

    subgraph Rewards["Quest Rewards"]
        XP["Experience points"]
        Currency["Currency"]
        Items["Items"]
        Reputation["Reputation changes"]
        Recipes["Recipes learned"]
    end

    Offered --> Accepted --> InProgress --> Completed --> TurnedIn
    InProgress --> Failed
    Requirements --> InProgress
    TurnedIn --> Rewards
```

---

## 12. Game Definition Builder Pattern

```mermaid
flowchart TB
    subgraph GameBuilder["GameBuilder (Fluent API)"]
        GB["new GameBuilder('game_id')"]
        GB --> Title[".WithTitle('Fantasy Quest')"]
        Title --> Style[".WithStyle(GameStyle.Fantasy)"]
        Style --> Story[".WithStory('Long ago...')"]
        Story --> Authority[".WithBalancedGameMaster()"]
        Authority --> Economy[".WithTieredEconomy(...)"]
        Economy --> Crafting[".WithNpcCrafting()"]
    end

    subgraph RoomBuilder["RoomBuilder"]
        RB["new RoomBuilder('tavern')"]
        RB --> RName[".WithName('The Rusty Tankard')"]
        RName --> RDesc[".WithDescription('A warm tavern...')"]
        RDesc --> Exit1[".AddExit('upstairs', 'tavern_upper')"]
        Exit1 --> Exit2[".AddExit('outside', 'town_square')"]
        Exit2 --> Biome[".WithBiome(Biomes.Urban)"]
        Biome --> Resource[".AddGatherableResource(...)"]
        Resource --> RBuild[".Build() → Room"]
    end

    subgraph NpcBuilder["NpcBuilder"]
        NB["new NpcBuilder('blacksmith', 'Thorin')"]
        NB --> Stats[".WithStats(str:14, agi:10, armor:3)"]
        Stats --> Health[".WithHealth(50, 50)"]
        Health --> Personality[".WithPersonalityPrompt('Gruff but kind...')"]
        Personality --> Crafter[".AsCrafter('blacksmith')"]
        Crafter --> Loot[".WithLoot(sword, 1)"]
        Loot --> Currency[".WithTieredCurrency(gold:10)"]
        Currency --> NBuild[".Build() → Character"]
    end

    subgraph ItemBuilder["ItemBuilder"]
        IB["new ItemBuilder('iron_sword')"]
        IB --> IName[".WithName('Iron Sword')"]
        IName --> IDesc[".WithDescription('A sturdy blade')"]
        IDesc --> Weapon[".AsWeapon(damageBonus: 5)"]
        Weapon --> Equip[".WithEquipmentSlot('main_hand')"]
        Equip --> Pricing[".WithPricing(base: 100)"]
        Pricing --> Rarity[".WithRarity(ItemRarity.Uncommon)"]
        Rarity --> IBuild[".Build() → Item"]
    end

    subgraph Assembly["Game Assembly"]
        AddRoom["gameBuilder.AddRoom(room)"]
        AddNPC["gameBuilder.AddNPC(npc)"]
        AddItem["gameBuilder.AddItem(item)"]
        AddQuest["gameBuilder.AddQuest(quest)"]
        AddRecipe["gameBuilder.AddRecipe(recipe)"]
        Build["gameBuilder.Build() → Game"]
    end

    RBuild --> AddRoom
    NBuild --> AddNPC
    IBuild --> AddItem
    GameBuilder --> Assembly
```

---

## 13. Complete Action Handler Map

```mermaid
flowchart LR
    subgraph Movement["Movement"]
        move["move → HandleMove()<br/>Navigate between rooms"]
    end

    subgraph Combat["Combat"]
        attack["attack → HandleAttack()<br/>Start combat with NPC"]
        auto["auto → HandleAuto()<br/>Auto-attack until resolved"]
        flee["flee → HandleFlee()<br/>Attempt to escape combat"]
        stop["stop → HandleStopCombat()<br/>Exit combat mode"]
    end

    subgraph Items["Item Management"]
        take["take → HandleTake()<br/>Pick up item / loot NPC"]
        drop["drop → HandleDrop()<br/>Drop item from inventory"]
        use["use → HandleUse()<br/>Consume / teleport"]
        examine["examine → HandleExamine()<br/>Inspect item or NPC"]
        equip["equip → HandleEquip()<br/>Wear equipment"]
        unequip["unequip → HandleUnequip()<br/>Remove equipment"]
        equipped["equipped → HandleEquipped()<br/>Show gear"]
    end

    subgraph Social["NPC Interaction"]
        talk["talk → HandleTalkAsync()<br/>NpcBrain dialogue"]
        follow["follow → HandleFollowAsync()<br/>Recruit companion"]
        give["give → HandleGiveAsync()<br/>Give items to NPC"]
    end

    subgraph Economy["Economy"]
        shop["shop → HandleShop()<br/>View merchant stock"]
        buy["buy → HandleBuy()<br/>Purchase item"]
        sell["sell → HandleSell()<br/>Sell item"]
    end

    subgraph Crafting["Crafting & Gathering"]
        gather["gather → HandleGatherAsync()<br/>Collect resources"]
        search["search → HandleGatherAsync()<br/>Alias for gather"]
        craft["craft → HandleCraftAsync()<br/>Create items via NPC"]
        recipes["recipes → HandleRecipes()<br/>View available recipes"]
    end

    subgraph Info["Information"]
        look["look → HandleLook()<br/>Room description"]
        status["status → HandleStatus()<br/>Player stats & health"]
        inventory["inventory → HandleInventory()<br/>List carried items"]
        quests["quests → HandleQuests()<br/>Quest log"]
        help["help → HandleHelp()<br/>Command list"]
        display["display → HandleDisplay()<br/>Change display mode"]
    end
```

---

## 14. File Structure Overview

```mermaid
graph TB
    subgraph Root["CSharpRPGBackend/"]
        Program["Program.cs<br/>759 lines<br/>Entry point & menus"]
    end

    subgraph Core["src/Core/"]
        GameState["GameState.cs<br/>Central state management"]
    end

    subgraph Services["src/Services/"]
        GameMaster["GameMaster.cs<br/>3,774 lines<br/>Main orchestration"]
        CombatService["CombatService.cs<br/>300 lines<br/>Combat math"]
        GameReplay["GameReplay.cs<br/>342 lines<br/>AI auto-player"]
        GameLoader["GameLoader.cs<br/>Load game definitions"]
    end

    subgraph LLMDir["src/LLM/"]
        ILlmClient["ILlmClient.cs<br/>Interface"]
        OllamaClient["OllamaClient.cs<br/>Ollama HTTP"]
        LlamaCppClient["LlamaCppClient.cs<br/>llama.cpp HTTP + server mgmt"]
        NpcBrain["NpcBrain.cs<br/>NPC personality AI"]
        LlmSettings["LlmSettings.cs<br/>Config persistence"]
    end

    subgraph ModelsDir["src/Models/ (13 files)"]
        Character["Character.cs"]
        Room["Room.cs"]
        ItemModel["Item.cs"]
        Quest["Quest.cs"]
        Game["Game.cs"]
        ExitModel["Exit.cs"]
        Inventory["Inventory.cs"]
        Currency["Currency.cs"]
        CraftingModel["Crafting.cs"]
        GMAuth["GameMasterAuthority.cs"]
        EquipSlot["EquipmentSlot.cs"]
        Resources["Resources.cs"]
        JsonDef["JsonDefinitions.cs"]
    end

    subgraph Utils["src/Utils/"]
        GameBuilder["GameBuilder.cs"]
        RoomBuilder["RoomBuilder.cs<br/>(+ NpcBuilder)"]
        ItemBuilder["ItemBuilder.cs"]
    end

    subgraph Games["src/Games/"]
        Fantasy["FantasyQuest.cs"]
        SciFi["SciFiAdventure.cs"]
    end

    subgraph EditorDir["RPGGameEditor/"]
        EditorProgram["Program.cs"]
        EditorForm["GameEditorForm.cs<br/>WinForms UI"]
    end

    Program --> Services
    Program --> LLMDir
    Program --> Games
    Services --> Core
    Services --> ModelsDir
    Services --> LLMDir
    Games --> Utils
    Utils --> ModelsDir
    EditorDir -.->|"Edits game JSON files"| Games
```
