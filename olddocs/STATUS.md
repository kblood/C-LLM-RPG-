# CSharpRPGBackend - Improvement Status Tracker

**Last Updated**: 2025-12-01
**Review Date**: 2025-12-01
**Overall Completion**: 1/52 (2%)

---

## 📊 Progress Overview

| Priority | Total | Completed | In Progress | Not Started |
|----------|-------|-----------|-------------|-------------|
| 🔴 Critical | 5 | 1 | 0 | 4 |
| 🟡 High Priority | 5 | 0 | 0 | 5 |
| 🟢 Medium Priority | 5 | 0 | 0 | 5 |
| 🔵 Low Priority | 10 | 0 | 0 | 10 |
| 💡 Future Enhancements | 20 | 0 | 0 | 20 |

---

## 🔴 Critical Issues (Fix Immediately)

### Combat & Game Logic

- [x] **#1: Equipment System Non-Functional** (✅ Fully Complete: 2025-12-01)
  - **File**: `src/Services/CombatService.cs:44` + `src/Services/GameMaster.cs:1867-2134` + `src/Models/EquipmentSlot.cs` + `src/Models/Game.cs:46-59`
  - **Issue**: Combat always passes `null` weapon, ignoring equipped items + no way for players to equip items + hard-coded equipment slots
  - **Impact**: Players get no benefit from equipping weapons and can't equip items + all games forced to use same slot system
  - **Fix**: Pass actual equipped weapon to `GetTotalDamage()` + add equip/unequip commands + implement dynamic slot configuration
  - **Effort**: 8 hours total (Part 1: 2h, Part 2: 4h, Dynamic Slots: 2h)
  - **Status**: ✅ **COMPLETE** - Full equipment system functional with dynamic per-game slot configuration
  - **Related**: #9 (Armor Scaling Bug)
  - **Changes Made (Part 1 - Combat Integration)**:
    - Updated `ResolveAttack()` to call `GetEquippedWeapon()` and pass to `GetTotalDamage()`
    - Rewrote `GetEquippedWeapon()` to fetch from `CarriedItems` instead of creating fake items
    - Added `GetTotalArmor()` method to include equipped armor bonuses
    - Updated armor calculation to use `GetTotalArmor(defender)`
    - Fixed companion assistance to use equipped weapons
  - **Changes Made (Part 2 - Player Commands)**:
    - Added `HandleEquip()` method with slot detection and auto-unequip
    - Added `HandleUnequip()` method with item name and slot matching
    - Added `HandleEquipped()` method to display current equipment and stats
    - Added `DetermineSlotFromType()` helper to infer slots from item type/name
    - Updated `ApplyActionAsync` switch to route equip/unequip/equipped actions
    - Updated LLM decision prompt with equipment action examples
    - Added fallback parser support for equip/wear/wield/unequip/remove commands
  - **Changes Made (Part 3 - Dynamic Equipment Slots)**:
    - Created `src/Models/EquipmentSlot.cs` with `EquipmentSlotDefinition` and `EquipmentSlotConfiguration` classes
    - Added factory methods: `CreateDefault()` (7 slots), `CreateMinimal()` (1 slot), `CreateSciFi()` (6 slots)
    - Implemented smart `DetermineSlotForItem()` with priority system (explicit → type+keyword → type → fallback)
    - Added `EquipmentSlots` property to `Game` class with default fallback via `GetEquipmentSlots()`
    - Updated `GameMaster` to use `_equipmentSlots` from game configuration
    - Modified `HandleEquipped()` to display dynamic slots with proper names
    - Full backward compatibility - games without EquipmentSlots use default configuration
  - **Documentation**:
    - `EQUIPMENT_FIX_SUMMARY.md` - Part 1 complete guide
    - `EQUIPMENT_TEST_RESULTS.md` - Test verification with results
    - `EQUIPMENT_PART2_COMPLETE.md` - Part 2 implementation guide
    - `DYNAMIC_EQUIPMENT_SLOTS.md` - Dynamic slot system documentation

- [ ] **#2: Infinite Recursion Risk in HandleExamine**
  - **File**: `src/Services/GameMaster.cs:1580`
  - **Issue**: Recursive call without validation can cause stack overflow
  - **Impact**: App crashes on malformed examine commands
  - **Fix**: Add recursion depth counter and validate extracted names
  - **Effort**: 30 minutes
  - **Status**: Not Started

### LLM Integration

- [ ] **#4: LLM Reliability Problems**
  - **File**: `src/LLM/OllamaClient.cs:20`
  - **Issues**:
    - No timeout configuration (can hang indefinitely)
    - No retry logic (single failures kill actions)
    - Creates new HttpClient per instance (socket exhaustion)
  - **Impact**: ~10-15% of player commands fail
  - **Fix**:
    - Add 30-second timeout
    - Implement exponential backoff retry (3 attempts using Polly)
    - Use HttpClientFactory pattern
  - **Effort**: 3-4 hours
  - **Status**: Not Started
  - **Dependencies**: Install Polly NuGet package

### Game Editor

- [ ] **#3: Data Loss Risk in Game Editor**
  - **File**: `RPGGameEditor/GameEditorForm.cs:232-301`
  - **Issues**:
    - No dirty state tracking (users can lose work)
    - No backup before overwriting files
    - Deleted items leave orphaned JSON files
  - **Impact**: Users can lose hours of work
  - **Fix**:
    - Implement change tracking with modified flag
    - Create .bak files before saving
    - Delete corresponding JSON files on item deletion
  - **Effort**: 4-6 hours
  - **Status**: Not Started

- [ ] **#5: ID Collision in Game Editor**
  - **File**: `RPGGameEditor/GameEditorForm.cs:388-433`
  - **Issue**: No validation prevents duplicate IDs
  - **Impact**: JSON serialization conflicts, game loading failures
  - **Fix**: Check uniqueness: `if (_rooms.Any(r => r.Id == newId)) { error }`
  - **Effort**: 1 hour
  - **Status**: Not Started
  - **Related**: #24 (Add Validation Layer)

---

## 🟡 High Priority Improvements

### Architecture

- [ ] **#6: GameMaster God Object Refactoring**
  - **File**: `src/Services/GameMaster.cs` (2,157 lines)
  - **Issue**: Too many responsibilities, hard to test/modify
  - **Fix**: Break into separate services:
    - `ActionExecutor` - Handle[Action] methods
    - `DialogueService` - NPC conversations
    - `InventoryService` - Take/drop/use items
    - `NarrationService` - LLM narrative generation
    - `WinConditionChecker` - Victory logic
  - **Effort**: 2-3 days
  - **Status**: Not Started
  - **Blockers**: Should add tests first (#8)

- [ ] **#7: Character Class Overloaded**
  - **File**: `src/Models/Character.cs` (66+ properties)
  - **Issue**: Serves both Player and NPC roles, causing confusion
  - **Fix**: Use inheritance or composition:
    - Option 1: `Player : Character`, `Npc : Character`
    - Option 2: `Character + NpcBehavior component`
  - **Effort**: 1-2 days
  - **Status**: Not Started
  - **Impact**: Breaking change - requires migration

- [ ] **#11: Add Dependency Injection**
  - **Files**: `Program.cs`, all services
  - **Issue**: Manual object construction everywhere, hard to test
  - **Fix**: Use .NET's built-in DI container
  - **Effort**: 1 day
  - **Status**: Not Started
  - **Benefits**: Enables easier testing and loose coupling

### Testing & Quality

- [ ] **#8: No Unit Tests**
  - **Issue**: Zero test coverage makes refactoring risky
  - **Impact**: Can't verify combat math, LLM parsing, game logic
  - **Fix**:
    - Add xUnit or NUnit project
    - Test CombatService calculations
    - Mock OllamaClient for testing
    - Test builder patterns
  - **Effort**: 3-5 days (ongoing)
  - **Status**: Not Started
  - **Target Coverage**: 60%+

### Game Logic Bugs

- [ ] **#9: Armor Scaling Bug**
  - **File**: `src/Services/CombatService.cs:106-108`
  - **Issue**: Flat 15-point cap doesn't scale, can reduce damage to 0
  - **Fix**: Use percentage: `damage * (1 - Math.Min(0.5, armor * 0.01))`
  - **Effort**: 30 minutes
  - **Status**: Not Started
  - **Related**: #1 (Equipment System)

- [ ] **#10: JSON Parsing Fragility**
  - **File**: `src/Services/GameMaster.cs:342-379`
  - **Issue**: Simple `IndexOf('[')` matches JSON in examples/markdown
  - **Impact**: 5-10% parsing failure rate
  - **Fix**: Strip markdown first: `Regex.Replace(response, @"```json\s*|\s*```", "")`
  - **Effort**: 1 hour
  - **Status**: Not Started

---

## 🟢 Medium Priority Enhancements

### Logging & Observability

- [ ] **#12: Implement Proper Logging**
  - **Files**: All files with `Console.WriteLine`
  - **Issue**: Debug output in production, no log levels
  - **Fix**: Replace with `ILogger<T>`, add structured logging
  - **Effort**: 2-3 hours
  - **Status**: Not Started
  - **Dependencies**: Microsoft.Extensions.Logging

### LLM Improvements

- [ ] **#13: LLM Provider Abstraction**
  - **File**: `src/LLM/OllamaClient.cs`
  - **Issue**: Tightly coupled to Ollama
  - **Fix**: Create `ILLMProvider` interface
  - **Effort**: 3-4 hours
  - **Status**: Not Started
  - **Benefits**: Can switch to OpenAI, Claude, others

- [ ] **#29: Response Caching**
  - **Issue**: Identical commands re-execute LLM calls
  - **Fix**: Cache successful responses with 30-second TTL
  - **Effort**: 2-3 hours
  - **Status**: Not Started
  - **Impact**: 30-50% reduction in API calls

### Validation

- [ ] **#14: Game State Validation Layer**
  - **Issue**: No validation prevents invalid states
  - **Fix**: Create `GameStateValidator` to check invariants
  - **Effort**: 4-6 hours
  - **Status**: Not Started
  - **Examples**: NPCs in valid rooms, positive health, etc.

- [ ] **#15: Replace Magic Strings with Enums**
  - **File**: `src/Services/GameMaster.cs:906-945`
  - **Issue**: String-based action names cause runtime errors on typos
  - **Fix**: Create `enum GameAction { Move, Talk, Attack, ... }`
  - **Effort**: 2 hours
  - **Status**: Not Started

---

## 🔵 Low Priority Improvements

### Code Quality

- [ ] **#16: Extract Combat Configuration**
  - **File**: `src/Services/CombatService.cs`
  - **Issue**: Hard-coded formulas (base damage 5, flee chance 50%)
  - **Fix**: Create `CombatConfig` class
  - **Effort**: 2 hours
  - **Status**: Not Started

- [ ] **#17: Primitive Obsession**
  - **Files**: `src/Models/*.cs`
  - **Issue**: Using `string` for RoomId, `int` for Health
  - **Fix**: Create value objects: `record RoomId(string Value)`
  - **Effort**: 1 day
  - **Status**: Not Started
  - **Impact**: Breaking change

- [ ] **#18: Large Switch Statements**
  - **File**: `src/Services/GameMaster.cs:906-945`
  - **Issue**: 15+ case switch in `ApplyActionAsync`
  - **Fix**: Use Command Pattern with action registry
  - **Effort**: 3-4 hours
  - **Status**: Not Started

- [ ] **#19: Inconsistent Error Handling**
  - **Files**: Multiple files
  - **Issue**: Mix of exceptions, nulls, bools for errors
  - **Fix**: Standardize on Result<T> pattern
  - **Effort**: 1-2 days
  - **Status**: Not Started

- [ ] **#20: Mixed Concerns in GameState**
  - **File**: `src/Core/GameState.cs`
  - **Issue**: Contains both state and logic methods
  - **Fix**: Move logic to GameStateService, keep state as DTO
  - **Effort**: 3-4 hours
  - **Status**: Not Started

### Bugs & Edge Cases

- [ ] **#21: Room Exit Validation Missing**
  - **File**: `src/Core/GameState.cs:216`
  - **Issue**: `GetCurrentRoom()` can throw if CurrentRoomId invalid
  - **Fix**: Add defensive checks, validate on state change
  - **Effort**: 1 hour
  - **Status**: Not Started

- [ ] **#22: InitializeDefaultGame Duplicate NPCs**
  - **File**: `src/Core/GameState.cs:43, 55`
  - **Issue**: "bartender" appears in two rooms simultaneously
  - **Fix**: Remove duplicate or create two separate NPCs
  - **Effort**: 5 minutes
  - **Status**: Not Started

- [ ] **#23: Combat Mode Race Condition**
  - **File**: `src/Services/GameMaster.cs:1476`
  - **Issue**: Multiple queued actions execute after combat ends
  - **Fix**: Validate combat state before each action
  - **Effort**: 1 hour
  - **Status**: Not Started

- [ ] **#24: Swallowed Exceptions in Program.cs**
  - **File**: `Program.cs:104`
  - **Issue**: Empty catch blocks ignore errors silently
  - **Fix**: Log exceptions properly
  - **Effort**: 30 minutes
  - **Status**: Not Started
  - **Related**: #12 (Proper Logging)

- [ ] **#25: NpcBrain Memory Leak**
  - **File**: `src/Services/GameMaster.cs:624`
  - **Issue**: NpcBrain instances never disposed in replay mode
  - **Fix**: Implement IDisposable, cleanup in GameMaster.Dispose()
  - **Effort**: 1 hour
  - **Status**: Not Started

---

## 💡 Future Enhancements (Low Priority)

### Persistence

- [ ] **#26: No Persistence Layer**
  - **Issue**: All state in-memory, can't save mid-session
  - **Fix**: Add `IGameRepository` for save/load
  - **Effort**: 2-3 days
  - **Status**: Not Started

### Performance

- [ ] **#27: Excessive LLM Calls**
  - **File**: `src/Services/GameMaster.cs`
  - **Issue**: 4 LLM calls per talk action (should be 2-3)
  - **Fix**: Optimize `ConvertPlayerCommandToNpcMessageAsync` logic
  - **Effort**: 2-3 hours
  - **Status**: Not Started

- [ ] **#28: String Concatenation in Loops**
  - **File**: `src/Services/GameMaster.cs:228`
  - **Issue**: Multiple LINQ chains with string allocations
  - **Fix**: Use StringBuilder for large rooms
  - **Effort**: 1 hour
  - **Status**: Not Started

### Game Editor Features

- [ ] **#30: Quest Editor Not Implemented**
  - **File**: `RPGGameEditor/GameEditorForm.cs:477`
  - **Issue**: Quests load/save but can't create/edit
  - **Fix**: Implement QuestEditorDialog
  - **Effort**: 1 day
  - **Status**: Not Started

- [ ] **#31: Win Conditions Not Editable**
  - **File**: `RPGGameEditor/GameEditorForm.cs`
  - **Issue**: WinConditionEditorDialog exists but never called
  - **Fix**: Add UI to configure win conditions
  - **Effort**: 2-3 hours
  - **Status**: Not Started

- [ ] **#32: No Room Items Editor**
  - **Issue**: Items can't be placed in rooms via editor
  - **Fix**: Add items tab to RoomEditorDialog
  - **Effort**: 3-4 hours
  - **Status**: Not Started

- [ ] **#33: No Undo/Redo in Editor**
  - **Issue**: Can't recover from mistakes
  - **Fix**: Implement Command Pattern for undo stack
  - **Effort**: 2-3 days
  - **Status**: Not Started

- [ ] **#34: No Search/Filter in TreeView**
  - **Issue**: Hard to find items in large games
  - **Fix**: Add search textbox with filtering
  - **Effort**: 2-3 hours
  - **Status**: Not Started

- [ ] **#35: No Drag-and-Drop Support**
  - **Issue**: All interactions require dialogs
  - **Fix**: Implement drag-and-drop for items/NPCs
  - **Effort**: 1 day
  - **Status**: Not Started

- [ ] **#36: No Bulk Operations**
  - **Issue**: Can't duplicate or batch delete
  - **Fix**: Add multi-select and bulk actions
  - **Effort**: 4-6 hours
  - **Status**: Not Started

- [ ] **#37: No Auto-Save or Recovery**
  - **Issue**: Work lost on crash
  - **Fix**: Implement periodic auto-save
  - **Effort**: 3-4 hours
  - **Status**: Not Started

- [ ] **#38: Missing Progress Indicators**
  - **Issue**: No feedback during load/save
  - **Fix**: Add progress bars for long operations
  - **Effort**: 2 hours
  - **Status**: Not Started

- [ ] **#39: No Tooltips or Help Text**
  - **Issue**: Fields lack explanation
  - **Fix**: Add tooltips to all form fields
  - **Effort**: 2-3 hours
  - **Status**: Not Started

### Advanced Features

- [ ] **#40: NPC Personality Editor**
  - **Issue**: Rich personality system not exposed in UI
  - **Fix**: Add personality/dialogue editor dialog
  - **Effort**: 2-3 days
  - **Status**: Not Started

- [ ] **#41: Room Ambiance Editor**
  - **Issue**: Ambiance and hazards not editable
  - **Fix**: Add ambiance tab to RoomEditorDialog
  - **Effort**: 1 day
  - **Status**: Not Started

- [ ] **#42: Item Effects Editor**
  - **Issue**: Only damage/armor editable, not effects
  - **Fix**: Add effects configuration UI
  - **Effort**: 1-2 days
  - **Status**: Not Started

- [ ] **#43: Game Validation Tools**
  - **Issue**: No way to check for errors before playing
  - **Fix**: Add "Validate Game" button with report
  - **Effort**: 1 day
  - **Status**: Not Started

- [ ] **#44: Export/Import Features**
  - **Issue**: Can't share games as packages
  - **Fix**: Add ZIP export/import
  - **Effort**: 4-6 hours
  - **Status**: Not Started

- [ ] **#45: Game Testing Integration**
  - **Issue**: Must switch to terminal to play
  - **Fix**: Add "Play Game" button launching engine
  - **Effort**: 2-3 hours
  - **Status**: Not Started

---

## 🔬 Technical Debt

### Architecture

- [ ] **#46: No Thread Safety**
  - **Issue**: Dictionary-based storage not thread-safe
  - **Fix**: Use ConcurrentDictionary or implement locking
  - **Effort**: 1 day
  - **Status**: Not Started
  - **Note**: Only needed if adding multiplayer

- [ ] **#47: Static Game Configuration**
  - **Issue**: Game rules hard-coded, can't vary by game
  - **Fix**: Make rules configurable in Game class
  - **Effort**: 2-3 days
  - **Status**: Not Started

- [ ] **#48: No Schema Validation**
  - **Issue**: JSON loaded without validation
  - **Fix**: Add JSON schema validation on load
  - **Effort**: 1 day
  - **Status**: Not Started

### Security

- [ ] **#49: No Input Sanitization**
  - **Issue**: Player input sent directly to LLM
  - **Fix**: Sanitize user input before LLM calls
  - **Effort**: 2-3 hours
  - **Status**: Not Started

- [ ] **#50: No Rate Limiting**
  - **Issue**: LLM calls not throttled
  - **Fix**: Add rate limiting to prevent API abuse
  - **Effort**: 2-3 hours
  - **Status**: Not Started

- [ ] **#51: File System Security**
  - **Issue**: Game editor can write anywhere
  - **Fix**: Sandbox game file locations
  - **Effort**: 1 day
  - **Status**: Not Started

- [ ] **#52: No Request Cancellation**
  - **Issue**: Can't cancel long-running LLM requests
  - **Fix**: Add CancellationToken support throughout
  - **Effort**: 1 day
  - **Status**: Not Started

---

## 📋 Implementation Roadmap

### Week 1: Critical Stabilization
1. Equipment system fix (#1)
2. LLM timeout and retry (#4)
3. HandleExamine recursion (#2)
4. Game editor dirty tracking (#3)
5. ID collision validation (#5)

**Goal**: Fix user-facing bugs and prevent data loss

### Month 1: Reliability & Testing
6. Add unit tests (#8)
7. Fix JSON parsing (#10)
8. Implement proper logging (#12)
9. Fix armor scaling (#9)
10. Add game state validator (#14)

**Goal**: Improve reliability and enable confident refactoring

### Quarter 1: Architecture & Features
11. Refactor GameMaster (#6)
12. Add dependency injection (#11)
13. Abstract LLM provider (#13)
14. Split Character class (#7)
15. Complete game editor features (#30, #31, #32)

**Goal**: Clean architecture and feature completeness

### Quarter 2+: Polish & Advanced Features
16. Persistence layer (#26)
17. Response caching (#29)
18. Advanced editor features (#33-#45)
19. Security hardening (#49-#52)
20. Performance optimization (#27, #28)

**Goal**: Production-ready, polished experience

---

## 📊 Metrics

### Code Quality Targets
- [ ] Test coverage: 0% → 60%+
- [ ] Largest class: 2,157 lines → <500 lines
- [ ] Character properties: 66 → <30 per class
- [ ] Average method length: ? → <30 lines

### Reliability Targets
- [ ] LLM success rate: 85-90% → >98%
- [ ] User-facing errors: 1 in 7 → <1 in 50
- [ ] JSON parse failures: 5-10% → <2%
- [ ] Data loss incidents: Multiple per day → Zero

### Performance Targets
- [ ] LLM calls per action: 2-4 → 1-2
- [ ] Average response time: ? → <2 seconds
- [ ] Memory usage: ? → Stable over time
- [ ] API call reduction: 0% → 30-50% (with caching)

---

## 🏷️ Labels & Categories

**By Component:**
- Combat System: #1, #9, #16
- LLM Integration: #4, #10, #13, #27, #29
- Game Editor: #3, #5, #30-#45
- Architecture: #6, #7, #11, #17, #18, #46, #47
- Testing: #8
- Validation: #14, #15, #24, #43, #48
- Logging: #12, #24
- Security: #49-#52
- Bugs: #2, #21-#23, #25

**By Effort:**
- < 1 hour: #2, #5, #9, #21, #22, #24, #28
- 1-4 hours: #1, #10, #12, #15, #16, #20, #23, #25, #31, #34, #37-#39, #44, #45, #49, #50
- 1 day: #7, #11, #17, #30, #32, #35, #41, #43, #46, #48, #51, #52
- 2-5 days: #6, #8, #26, #33, #40, #42, #47
- Ongoing: #8 (testing)

---

## 📝 Notes

- Update this file as issues are completed
- Link to specific PRs/commits in each checkbox
- Add "Completed" date when checking items off
- Move completed items to CHANGELOG.md periodically
- Review and reprioritize monthly

**Convention**:
- ✅ Completed items: `- [x] **#N: Title** (Completed: YYYY-MM-DD)`
- 🚧 In progress: `- [ ] **#N: Title** (In Progress - assignee)`
- ⏸️ Blocked: `- [ ] **#N: Title** (Blocked by #X)`

---

**Generated**: 2025-12-01 by comprehensive codebase review
