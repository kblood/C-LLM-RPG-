using CSharpRPGBackend.Core;
using CSharpRPGBackend.Utils;

namespace CSharpRPGBackend.Games;

/// <summary>
/// Sci-Fi Adventure - A futuristic space station mystery and escape.
/// Genre: Science Fiction with technology, aliens, and lasers.
/// </summary>
public static class SciFiAdventure
{
    public static Game Create()
    {
        var gameBuilder = new GameBuilder("scifi_adventure")
            .WithTitle("Escape from Station Zeta")
            .WithSubtitle("A Science Fiction Thriller")
            .WithStyle(GameStyle.SciFi)
            .WithDescription("A desperate escape from a hostile space station overrun by alien creatures.")
            .WithStory(@"
                You awoke in a cold metal chamber with no memory of how you got here.

                The station around you hums with an ominous sound. Red emergency lights flicker.
                Through the viewport, you see nothing but darkness and distant stars.

                Then you hear it - a sound that chills your blood. Screams echo through the corridors.

                Station Zeta is under attack. Alien lifeforms have overrun the lower sections.
                If you don't escape soon, you'll become their next meal...")
            .WithObjective("Escape from Station Zeta and reach the escape pods before the alien queen arrives")
            .WithEstimatedPlayTime(90)
            .WithAuthor("Game Designer")
            .WithPlayerDefaults(80, 1, "A human survivor with technical aptitude and survival instinct")
            .WithCombat(true)
            .WithInventory(true)
            .WithMagic(false)
            .WithTechnology(true)
            .WithNPCRecruitment(true)
            .WithPermadeath(true)  // Sci-fi can be deadly!
            .WithFreeRoam(false)   // Linear progression to escape
            .WithoutEconomy();     // No trading on a survival escape!

        // ========== ROOMS ==========

        var crewQuarters = new RoomBuilder("crew_quarters")
            .WithName("Crew Quarters - Your Cell")
            .WithDescription("A small, austere metal chamber. The bunk is cold. The walls are bare. " +
                "A small porthole shows the darkness of space. A heavy door hisses softly.")
            .AddExit("Out Into Corridor", "main_corridor_a")
            .WithMetadata("danger_level", 0)
            .WithMetadata("lighting", "red_emergency")
            .Build();

        var mainCorridorA = new RoomBuilder("main_corridor_a")
            .WithName("Main Corridor - Section A")
            .WithDescription("A long corridor lined with metal panels and flickering lights. " +
                "Warning klaxons sound in the distance. You see blood on the floor. Something terrible has happened.")
            .AddExit("Back To Quarters", "crew_quarters")
            .AddExit("To Medical Bay", "medical_bay")
            .AddExit("Continue Forward", "main_corridor_b")
            .AddNPCs("ai_voice")
            .WithMetadata("danger_level", 1)
            .WithMetadata("lighting", "red_emergency")
            .Build();

        var medicalBay = new RoomBuilder("medical_bay")
            .WithName("Medical Bay")
            .WithDescription("A sterile medical facility. Equipment beeps quietly. In the corner, you find " +
                "a crewmember named Dr. Chen, wounded but conscious. Medical supplies line the walls.")
            .AddExit("Back To Corridor", "main_corridor_a")
            .AddNPCs("dr_chen")
            .WithMetadata("danger_level", 0)
            .WithMetadata("lighting", "white_lights")
            .Build();

        var mainCorridorB = new RoomBuilder("main_corridor_b")
            .WithName("Main Corridor - Section B")
            .WithDescription("The corridor ahead is darker. You hear strange chittering sounds. " +
                "Organic matter covers parts of the walls. The aliens have been here. You must proceed carefully.")
            .AddExit("Back To Section A", "main_corridor_a")
            .AddExit("Forward To Armory", "armory")
            .AddExit("Down To Engineering", "main_corridor_c")
            .WithMetadata("danger_level", 2)
            .WithMetadata("lighting", "minimal")
            .WithMetadata("hostile_creatures", new[] { "alien_drone" })
            .Build();

        var armory = new RoomBuilder("armory")
            .WithName("Security Armory")
            .WithDescription("A locked armory containing weapons and ammunition. The security chief stands guard, " +
                "weapon in hand. She looks terrified but determined.")
            .AddExit("Back To Corridor", "main_corridor_b")
            .AddNPCs("security_chief")
            .WithMetadata("danger_level", 1)
            .WithMetadata("lighting", "normal")
            .Build();

        var mainCorridorC = new RoomBuilder("main_corridor_c")
            .WithName("Main Corridor - Lower Level")
            .WithDescription("You descend into the lower sections of the station. It's colder here. " +
                "Strange growths cover the walls. This is the alien's domain.")
            .AddExit("Up To Section B", "main_corridor_b")
            .AddExit("To Engineering Core", "engineering_core")
            .AddExit("To Observation Deck", "observation_deck")
            .WithMetadata("danger_level", 3)
            .WithMetadata("lighting", "pitch_black")
            .WithMetadata("hostile_creatures", new[] { "alien_drone", "alien_warrior" })
            .Build();

        var engineeringCore = new RoomBuilder("engineering_core")
            .WithName("Engineering Core")
            .WithDescription("The heart of the station pulses with energy. Massive reactors hum ominously. " +
                "An engineer works frantically at the main console, trying to stabilize systems.")
            .AddExit("Back To Lower Corridor", "main_corridor_c")
            .AddExit("To Escape Pod Bay", "escape_pod_bay")
            .AddNPCs("chief_engineer")
            .WithMetadata("danger_level", 2)
            .WithMetadata("lighting", "blue_reactor")
            .Build();

        var observationDeck = new RoomBuilder("observation_deck")
            .WithName("Observation Deck")
            .WithDescription("A wide viewport shows the cosmos. But something catches your eye - " +
                "a massive organic structure approaching the station. The alien queen's hive ship.")
            .AddExit("Back To Corridor", "main_corridor_c")
            .AddNPCs("station_commander")
            .WithMetadata("danger_level", 2)
            .WithMetadata("lighting", "starlight")
            .Build();

        var escapePodBay = new RoomBuilder("escape_pod_bay")
            .WithName("Escape Pod Bay")
            .WithDescription("A large hangar with three sleek escape pods. " +
                "The door control panel flashes red - LOCKED. Alien growths cover parts of the walls. " +
                "This is where survival awaits - if you can reach it.")
            .AddExit("Back To Engineering", "engineering_core")
            .WithMetadata("danger_level", 4)
            .WithMetadata("lighting", "red_emergency")
            .WithMetadata("objective_location", true)
            .Build();

        gameBuilder
            .AddRoom(crewQuarters)
            .AddRoom(mainCorridorA)
            .AddRoom(medicalBay)
            .AddRoom(mainCorridorB)
            .AddRoom(armory)
            .AddRoom(mainCorridorC)
            .AddRoom(engineeringCore)
            .AddRoom(observationDeck)
            .AddRoom(escapePodBay);

        // ========== NPCs ==========

        var aiVoice = new NpcBuilder("ai_voice", "ARIA (Station AI)")
            .WithHealth(999, 999)  // AI doesn't have health
            .WithLevel(5)
            .WithStats(5, 20, 8)
            .WithPersonalityPrompt(@"You are ARIA, the Artificial Intelligence controlling Station Zeta. " +
                @"You are logical, clinical, and trying to help the survivors. You provide information and warnings. " +
                @"Speak in a calm, robotic manner. You are bound by protocol but will help humans survive.")
            .Build();
        aiVoice.CurrentRoomId = "main_corridor_a";
        aiVoice.Role = NPCRole.Neutral;
        aiVoice.CanMove = false;

        var drChen = new NpcBuilder("dr_chen", "Dr. Sarah Chen")
            .WithHealth(40, 60)
            .WithLevel(1)
            .WithAlignment(CharacterAlignment.Good)
            .WithStats(8, 11, 1)
            .WithPersonalityPrompt(@"You are Dr. Sarah Chen, the station's physician. " +
                @"You're injured but still professional and helpful. You know medical procedures and can treat wounds. " +
                @"You're brave and willing to help other survivors escape.")
            .Build();
        drChen.CurrentRoomId = "medical_bay";
        drChen.HomeRoomId = "medical_bay";
        drChen.Role = NPCRole.Healer;
        drChen.CanJoinParty = true;
        drChen.Description = "A human woman with dark hair, wearing a bloodstained lab coat";

        var securityChief = new NpcBuilder("security_chief", "Commander Sarah Martinez")
            .WithHealth(90, 100)
            .WithLevel(3)
            .WithAlignment(CharacterAlignment.Good)
            .WithStats(15, 14, 5)
            .WithPersonalityPrompt(@"You are Commander Sarah Martinez, head of station security. " +
                @"You're tough, experienced, and trained for combat. You won't abandon civilians. " +
                @"You're strategic and calm under pressure. You might recruit allies for a better escape chance.")
            .Build();
        securityChief.CurrentRoomId = "armory";
        securityChief.HomeRoomId = "armory";
        securityChief.Role = NPCRole.Guard;
        securityChief.CanJoinParty = true;
        securityChief.Description = "A tall, athletic woman with sharp eyes and military bearing";

        var chiefEngineer = new NpcBuilder("chief_engineer", "Thomas Kowalski")
            .WithHealth(70, 80)
            .WithLevel(2)
            .WithAlignment(CharacterAlignment.Good)
            .WithStats(10, 10, 2)
            .WithPersonalityPrompt(@"You are Thomas Kowalski, chief engineer of Station Zeta. " +
                @"You're working desperately to keep the station from self-destructing. " +
                @"You understand the station's systems. You're stressed but determined. " +
                @"You might have technical solutions to problems.")
            .Build();
        chiefEngineer.CurrentRoomId = "engineering_core";
        chiefEngineer.HomeRoomId = "engineering_core";
        chiefEngineer.Role = NPCRole.Scholar;
        chiefEngineer.CanJoinParty = true;
        chiefEngineer.Description = "A middle-aged man with graying hair and mechanical expertise written in his eyes";

        var stationCommander = new NpcBuilder("station_commander", "Captain James Harrison")
            .WithHealth(100, 100)
            .WithLevel(4)
            .WithAlignment(CharacterAlignment.Good)
            .WithStats(13, 12, 3)
            .WithPersonalityPrompt(@"You are Captain James Harrison, commander of Station Zeta. " +
                @"You've watched your crew die. You're grim but focused on saving those who remain. " +
                @"You're authoritative and will take charge of the escape. " +
                @"You're the leader humanity needs.")
            .Build();
        stationCommander.CurrentRoomId = "observation_deck";
        stationCommander.HomeRoomId = "observation_deck";
        stationCommander.Role = NPCRole.Questgiver;
        stationCommander.CanJoinParty = true;
        stationCommander.Description = "An older man with captain's insignia and command presence";

        gameBuilder.AddNPCs(aiVoice, drChen, securityChief, chiefEngineer, stationCommander);

        // ========== ITEMS ==========

        // Weapons
        var laserPistol = new ItemBuilder("laser_pistol")
            .WithName("Laser Pistol")
            .WithDescription("A compact energy weapon emitting coherent light beams")
            .AsWeapon(damage: 10, criticalChance: 15)
            .WithWeight(2)
            .WithValue(80)
            .WithTheme("sci-fi")
            .Build();

        var plasmaRifle = new ItemBuilder("plasma_rifle")
            .WithName("Plasma Rifle")
            .WithDescription("A heavy plasma weapon designed to incinerate targets. Military grade.")
            .AsWeapon(damage: 18, criticalChance: 20)
            .WithWeight(6)
            .WithValue(150)
            .WithRarity(ItemRarity.Rare)
            .WithTheme("sci-fi")
            .Build();

        var monomolecularBlade = new ItemBuilder("mono_blade")
            .WithName("Monomolecular Blade")
            .WithDescription("An impossibly sharp blade capable of cutting through most materials")
            .AsWeapon(damage: 16, criticalChance: 25)
            .WithWeight(3)
            .WithValue(120)
            .WithTheme("sci-fi")
            .Build();

        // Armor
        var combatSuit = new ItemBuilder("combat_suit")
            .WithName("Combat Suit")
            .WithDescription("Reinforced tactical armor with integrated sensors")
            .AsArmor(armorBonus: 6, slot: "chest")
            .WithWeight(8)
            .WithValue(100)
            .WithTheme("sci-fi")
            .Build();

        var quantumArmor = new ItemBuilder("quantum_armor")
            .WithName("Quantum Deflection Suit")
            .WithDescription("Advanced quantum-field armor that bends incoming damage around the wearer")
            .AsArmor(armorBonus: 12, slot: "chest")
            .WithWeight(5)
            .WithValue(300)
            .WithRarity(ItemRarity.Epic)
            .WithTheme("sci-fi")
            .Build();

        // Keys
        var accessCard = new ItemBuilder("access_card")
            .WithName("Master Access Card")
            .WithDescription("A keycard that grants access to most station sections")
            .AsKey("armory_door", KeyType.Technological)
            .WithWeight(0)
            .WithValue(50)
            .WithTheme("sci-fi")
            .Build();

        var securityKeycode = new ItemBuilder("security_code")
            .WithName("Security Override Code")
            .WithDescription("A biometric security code needed to override the escape pod doors")
            .AsKey("escape_pod_lock", KeyType.Technological)
            .WithWeight(0)
            .WithValue(200)
            .WithRarity(ItemRarity.Rare)
            .WithTheme("sci-fi")
            .Build();

        // Teleportation
        var jumpGate = new ItemBuilder("jump_gate_device")
            .WithName("Emergency Jump Gate")
            .WithDescription("A portable device that opens a jump gate to a nearby safe location")
            .AsTeleportation("crew_quarters", "The device crackles with energy. You feel a strange pulling sensation as you're teleported away.")
            .WithWeight(3)
            .WithValue(250)
            .WithRarity(ItemRarity.Epic)
            .WithTheme("sci-fi")
            .Build();

        var warpBomb = new ItemBuilder("warp_bomb")
            .WithName("Temporal Warp Charge")
            .WithDescription("An experimental device that opens a temporary warp to another location")
            .AsTeleportation("escape_pod_bay", "Reality warps around you. You step through the distortion and arrive elsewhere.")
            .WithWeight(2)
            .WithValue(300)
            .WithRarity(ItemRarity.Rare)
            .WithTheme("sci-fi")
            .Build();

        // Consumables
        var stimPack = new ItemBuilder("stim_pack")
            .WithName("Combat Stimulant")
            .WithDescription("A hypodermic stim pack that increases alertness and healing")
            .AsConsumable(uses: 1)
            .WithConsumableEffect("heal", 60)
            .WithConsumableEffect("strength", 10)
            .WithWeight(1)
            .WithValue(35)
            .WithTheme("sci-fi")
            .Build();

        var nanobots = new ItemBuilder("nanobot_repair")
            .WithName("Nanobot Repair Kit")
            .WithDescription("Self-replicating nanobots that repair biological and mechanical damage")
            .AsConsumable(uses: 1)
            .WithConsumableEffect("heal", 80)
            .WithWeight(0)
            .WithValue(60)
            .WithTheme("sci-fi")
            .Build();

        // Quest Items
        var stationLog = new ItemBuilder("station_log")
            .WithName("Station Log - Final Entry")
            .WithDescription("The captain's final log entry before the alien invasion")
            .AsQuestItem()
            .WithWeight(0)
            .WithValue(100)
            .WithTheme("sci-fi")
            .Build();

        var alienDNA = new ItemBuilder("alien_dna_sample")
            .WithName("Alien DNA Sample")
            .WithDescription("A biological sample from the alien creatures. Could be valuable to the right people.")
            .AsQuestItem()
            .WithWeight(1)
            .WithValue(500)
            .WithRarity(ItemRarity.Rare)
            .WithTheme("sci-fi")
            .Build();

        gameBuilder.AddItems(
            laserPistol, plasmaRifle, monomolecularBlade, combatSuit, quantumArmor,
            accessCard, securityKeycode, jumpGate, warpBomb,
            stimPack, nanobots, stationLog, alienDNA);

        // ========== NPC LOOT ==========
        // Add items to defeated NPCs for looting
        drChen.CarriedItems[stimPack.Id] = new InventoryItem { Item = stimPack, Quantity = 2 };
        drChen.CarriedItems[nanobots.Id] = new InventoryItem { Item = nanobots, Quantity = 1 };

        securityChief.CarriedItems[laserPistol.Id] = new InventoryItem { Item = laserPistol, Quantity = 1 };
        securityChief.CarriedItems[combatSuit.Id] = new InventoryItem { Item = combatSuit, Quantity = 1 };

        chiefEngineer.CarriedItems[accessCard.Id] = new InventoryItem { Item = accessCard, Quantity = 1 };

        stationCommander.CarriedItems[securityKeycode.Id] = new InventoryItem { Item = securityKeycode, Quantity = 1 };

        // ========== QUESTS ==========

        var escapeQuest = new Quest
        {
            Id = "station_escape",
            Title = "Escape Station Zeta",
            Description = "Survive the alien outbreak and reach the escape pods before the hive ship arrives",
            GiverNpcId = "station_commander",
            RewardExperience = 400,
            RewardGold = 0,  // No money in space!
            Objectives = new() { "Survive lower decks", "Reach escape pod bay", "Launch escape pod" }
        };

        gameBuilder.AddQuest(escapeQuest);

        // Victory condition: Reach the escape pod bay and escape
        gameBuilder.AddWinCondition(new WinCondition
        {
            Id = "reach_escape_pods",
            Type = "room",
            TargetId = "escape_pod_bay",
            Description = "Reach the escape pod bay",
            VictoryMessage = "You burst into the escape pod bay! With trembling hands, you override the security locks " +
                           "and dive into the nearest pod. As the hatch seals shut, you slam the launch button. " +
                           "The pod shoots into space just as the alien hive ship appears on the horizon. " +
                           "You've survived Station Zeta. Against all odds, you're going home."
        });

        return gameBuilder
            .WithStartingRoom("crew_quarters")
            .Build();
    }
}
