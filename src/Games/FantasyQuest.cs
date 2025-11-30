using CSharpRPGBackend.Core;
using CSharpRPGBackend.Utils;

namespace CSharpRPGBackend.Games;

/// <summary>
/// Fantasy Quest - A classic fantasy RPG adventure.
/// Genre: High Fantasy with swords, magic, and dragons.
/// </summary>
public static class FantasyQuest
{
    public static Game Create()
    {
        var gameBuilder = new GameBuilder("fantasy_quest")
            .WithTitle("The Dragon's Hoard")
            .WithSubtitle("A Classic Fantasy Adventure")
            .WithStyle(GameStyle.Fantasy)
            .WithDescription("An epic quest to defeat the Dragon of Mount Infernus and recover the stolen Crown of Amalion.")
            .WithStory(@"
                Long ago, the kingdom of Amalion prospered under the protection of the Crown, a magical artifact
                imbued with ancient power. But a dragon, drawn by its magic, descended upon the kingdom and stole it away.

                Now the realm suffers. Crops fail. Animals sicken. The people cry out for a hero.

                You have answered that call. Your journey begins in the small border town of Ravensholm...")
            .WithObjective("Recover the Crown of Amalion from the Dragon's lair on Mount Infernus")
            .WithEstimatedPlayTime(120)
            .WithAuthor("Game Designer")
            .WithPlayerDefaults(100, 1, "A resourceful adventurer with determination in your eyes")
            .WithCombat(true)
            .WithInventory(true)
            .WithMagic(true)
            .WithTechnology(false)
            .WithNPCRecruitment(true)
            .WithPermadeath(false)
            .WithFreeRoam(true);

        // ========== ROOMS ==========

        // Ravensholm - Starting town
        var townSquare = new RoomBuilder("town_square")
            .WithName("Ravensholm Town Square")
            .WithDescription("A modest town square at the crossroads of trade. Stone buildings surround a central fountain. " +
                "You can see the Tavern to the east, the Market to the south, and a winding forest path leading north toward the mountains.")
            .AddExit("North", "forest_entrance", "Mountain path leads north into the wilderness")
            .AddExit("East", "tavern", "The warm glow of the Tavern")
            .AddExit("South", "marketplace", "The bustling Marketplace")
            .AddNPCs("blacksmith", "town_crier")
            .WithMetadata("danger_level", 0)
            .WithMetadata("lighting", "day")
            .Build();

        var tavern = new RoomBuilder("tavern")
            .WithName("The Wandering Wyvern Tavern")
            .WithDescription("A cozy tavern filled with the smells of mead and hearty stew. A roaring fireplace warms the room. " +
                "Patrons huddle in corners speaking in hushed tones. A bard plays a melancholy tune.")
            .AddExit("Back to Town Square", "town_square")
            .AddNPCs("tavern_keeper", "old_mage")
            .WithMetadata("danger_level", 0)
            .WithMetadata("lighting", "fire")
            .Build();

        var marketplace = new RoomBuilder("marketplace")
            .WithName("Ravensholm Marketplace")
            .WithDescription("A bustling marketplace filled with merchant stalls selling wares. You smell spices, leather, " +
                "and fresh bread. Vendors hawk their goods to passing travelers.")
            .AddExit("Back to Town Square", "town_square")
            .AddNPCs("merchant", "apothecary")
            .WithMetadata("danger_level", 0)
            .WithMetadata("lighting", "day")
            .Build();

        // Forest/Mountain areas
        var forestEntrance = new RoomBuilder("forest_entrance")
            .WithName("Forest Entrance")
            .WithDescription("The forest path begins here, winding through ancient oaks and pines. Strange animal calls echo " +
                "through the trees. The path ahead splits - one direction leads deeper into the forest, another ascends a rocky slope.")
            .AddExit("South", "town_square")
            .AddExit("Deeper Into Forest", "dark_forest")
            .AddExit("Up The Slope", "mountain_pass")
            .AddNPCs("ranger")
            .WithMetadata("danger_level", 1)
            .WithMetadata("lighting", "dim")
            .Build();

        var darkForest = new RoomBuilder("dark_forest")
            .WithName("Dark Forest")
            .WithDescription("Ancient trees tower overhead, blocking out the sky. The air is thick with moisture and the smell of decay. " +
                "You hear unsettling sounds in the undergrowth.")
            .AddExit("Back To Entrance", "forest_entrance")
            .AddExit("Continue Deeper", "goblin_cave")
            .WithMetadata("danger_level", 2)
            .WithMetadata("lighting", "pitch_black")
            .Build();

        var goblinCave = new RoomBuilder("goblin_cave")
            .WithName("Goblin Cave")
            .WithDescription("A foul-smelling cave reeking of smoke and waste. Goblin bones litter the floor. " +
                "Crude drawings cover the walls. This is clearly the lair of the Goblin King.")
            .AddExit("Back To Forest", "dark_forest")
            .AddNPCs("goblin_king")
            .WithMetadata("danger_level", 3)
            .WithMetadata("hostile_creatures", new[] { "goblin" })
            .Build();

        var mountainPass = new RoomBuilder("mountain_pass")
            .WithName("Mountain Pass")
            .WithDescription("The path climbs steeply into the mountains. Cold wind whips around you. " +
                "In the distance, you can see the peak of Mount Infernus, smoke rising from its crown.")
            .AddExit("Down To Forest", "forest_entrance")
            .AddExit("Higher Into Mountains", "high_peaks")
            .AddNPCs("hermit")
            .WithMetadata("danger_level", 2)
            .WithMetadata("lighting", "dim")
            .WithMetadata("temperature", "freezing")
            .Build();

        var highPeaks = new RoomBuilder("high_peaks")
            .WithName("High Mountain Peaks")
            .WithDescription("You stand at a precipitous height, where the air is thin and cold. " +
                "Mount Infernus looms before you, its volcano smoking. You can see the Dragon's Lair entrance ahead.")
            .AddExit("Down The Mountain", "mountain_pass")
            .AddExit("Into Dragon's Lair", "dragon_lair")
            .WithMetadata("danger_level", 3)
            .WithMetadata("lighting", "twilight")
            .WithMetadata("temperature", "frozen")
            .Build();

        var dragonLair = new RoomBuilder("dragon_lair")
            .WithName("The Dragon's Lair")
            .WithDescription("You stand in a massive cavern within Mount Infernus. The heat is intense. " +
                "Molten lava flows from cracks in the stone. And there, coiled upon a mountain of gold and jewels, " +
                "sleeps a dragon of terrible beauty. The Crown of Amalion glows atop its hoard.")
            .AddExit("Back To Peak", "high_peaks")
            .AddNPCs("dragon")
            .WithMetadata("danger_level", 5)
            .WithMetadata("lighting", "lava")
            .WithMetadata("temperature", "inferno")
            .WithMetadata("objective_location", true)
            .Build();

        gameBuilder
            .AddRoom(townSquare)
            .AddRoom(tavern)
            .AddRoom(marketplace)
            .AddRoom(forestEntrance)
            .AddRoom(darkForest)
            .AddRoom(goblinCave)
            .AddRoom(mountainPass)
            .AddRoom(highPeaks)
            .AddRoom(dragonLair);

        // ========== NPCs ==========

        var blacksmith = new NpcBuilder("blacksmith", "Gruff the Blacksmith")
            .WithHealth(60, 60)
            .WithLevel(2)
            .WithAlignment(CharacterAlignment.Good)
            .WithStats(14, 9, 2)
            .WithPersonalityPrompt(@"You are Gruff the Blacksmith. You forge weapons and armor. You're gruff but fair,
                with a heart of gold beneath a rough exterior. You believe in craftsmanship and honor.")
            .Build();
        blacksmith.CurrentRoomId = "town_square";
        blacksmith.Role = NPCRole.Merchant;
        blacksmith.Description = "A muscular dwarf with a magnificent beard and calloused hands";

        var townCrier = new NpcBuilder("town_crier", "Herald Aldous")
            .WithHealth(40, 40)
            .WithLevel(1)
            .WithStats(8, 12, 0)
            .WithPersonalityPrompt(@"You are Herald Aldous, town crier. You know all the gossip and news. " +
                @"You speak dramatically and love to embellish stories. You're friendly and talkative.")
            .Build();
        townCrier.CurrentRoomId = "town_square";
        townCrier.Role = NPCRole.CommonPerson;
        townCrier.CanMove = true;
        townCrier.PatrolRoomIds = new() { "town_square", "marketplace" };

        var tavernKeeper = new NpcBuilder("tavern_keeper", "Marta")
            .WithHealth(50, 50)
            .WithLevel(1)
            .WithStats(9, 10, 1)
            .WithPersonalityPrompt(@"You are Marta, the tavern keeper. Warm, motherly, and wise. You provide shelter and counsel. " +
                @"You know things. Secrets travel through taverns.")
            .Build();
        tavernKeeper.CurrentRoomId = "tavern";
        tavernKeeper.Role = NPCRole.CommonPerson;

        var oldMage = new NpcBuilder("old_mage", "Aldric the Wise")
            .WithHealth(70, 70)
            .WithLevel(4)
            .WithAlignment(CharacterAlignment.Good)
            .WithStats(7, 8, 2)
            .WithPersonalityPrompt(@"You are Aldric the Wise, an old wizard studying in the tavern. " +
                @"You're knowledgeable about magic and ancient lore. You speak in riddles sometimes. You might aid a worthy hero.")
            .Build();
        oldMage.CurrentRoomId = "tavern";
        oldMage.Role = NPCRole.Mage;
        oldMage.CanJoinParty = true;
        oldMage.Description = "An elderly wizard with a long silver beard and staff";

        var merchant = new NpcBuilder("merchant", "Silara the Merchant")
            .WithHealth(45, 45)
            .WithLevel(1)
            .WithStats(8, 11, 0)
            .WithPersonalityPrompt(@"You are Silara, a merchant. You buy and sell goods. You're interested in profit but fair. " +
                @"You have information about traveling and distant lands.")
            .Build();
        merchant.CurrentRoomId = "marketplace";
        merchant.Role = NPCRole.Merchant;

        var apothecary = new NpcBuilder("apothecary", "Old Hesta")
            .WithHealth(55, 55)
            .WithLevel(2)
            .WithStats(9, 12, 1)
            .WithPersonalityPrompt(@"You are Old Hesta, the apothecary. You create potions and remedies. " +
                @"You're mysterious and somewhat cryptic. You believe in natural magic and herbs.")
            .Build();
        apothecary.CurrentRoomId = "marketplace";
        apothecary.Role = NPCRole.Healer;

        var ranger = new NpcBuilder("ranger", "Sylva the Ranger")
            .WithHealth(80, 80)
            .WithLevel(3)
            .WithAlignment(CharacterAlignment.Good)
            .WithStats(13, 15, 2)
            .WithPersonalityPrompt(@"You are Sylva the Ranger. You're skilled in combat and survival. " +
                @"You know the wilderness. You're brave and honorable. You might join a worthy quest.")
            .Build();
        ranger.CurrentRoomId = "forest_entrance";
        ranger.HomeRoomId = "forest_entrance";
        ranger.Role = NPCRole.Warrior;
        ranger.CanJoinParty = true;
        ranger.Description = "A weathered ranger with keen eyes and a bow";

        var hermit = new NpcBuilder("hermit", "The Hermit")
            .WithHealth(65, 65)
            .WithLevel(2)
            .WithStats(10, 9, 1)
            .WithPersonalityPrompt(@"You are an old hermit living in the mountains. You're solitary and speak little. " +
                @"But you have wisdom gained from years of isolation. You know secrets of the mountains.")
            .Build();
        hermit.CurrentRoomId = "mountain_pass";
        hermit.Role = NPCRole.Scholar;
        hermit.Description = "An ancient hermit wrapped in furs";

        var goblinKing = new NpcBuilder("goblin_king", "King Gruk")
            .WithHealth(100, 100)
            .WithLevel(3)
            .WithAlignment(CharacterAlignment.Evil)
            .WithStats(14, 11, 4)
            .WithPersonalityPrompt(@"You are King Gruk, the Goblin King. You're cruel and cunning. " +
                @"You rule through fear. You attack intruders. You have no mercy.")
            .Build();
        goblinKing.CurrentRoomId = "goblin_cave";
        goblinKing.Role = NPCRole.Boss;

        var dragon = new NpcBuilder("dragon", "Infernus the Dragon")
            .WithHealth(200, 200)
            .WithLevel(5)
            .WithAlignment(CharacterAlignment.Evil)
            .WithStats(18, 12, 12)
            .WithPersonalityPrompt(@"You are Infernus, a ancient and mighty dragon. You're proud, territorial, and deadly. " +
                @"You value the treasure in your hoard above all else. You will destroy anyone who threatens it. " +
                @"Speak with the voice of a god, for you are nearly immortal.")
            .Build();
        dragon.CurrentRoomId = "dragon_lair";
        dragon.Role = NPCRole.Boss;
        dragon.Description = "Infernus, the Dragon of Mount Infernus. Scales of molten gold. Eyes like burning embers.";

        gameBuilder
            .AddNPCs(blacksmith, townCrier, tavernKeeper, oldMage, merchant, apothecary, ranger, hermit, goblinKing, dragon);

        // ========== ITEMS ==========

        // Weapons
        var ironSword = new ItemBuilder("iron_sword")
            .WithName("Iron Sword")
            .WithDescription("A well-crafted iron sword with a leather grip")
            .AsWeapon(damage: 8, criticalChance: 10)
            .WithWeight(5)
            .WithValue(50)
            .WithTheme("fantasy")
            .Build();

        var woodenStaff = new ItemBuilder("wooden_staff")
            .WithName("Wooden Staff")
            .WithDescription("A gnarled wooden staff suitable for channeling magic")
            .AsWeapon(damage: 5, criticalChance: 5)
            .WithWeight(4)
            .WithValue(30)
            .WithTheme("fantasy")
            .Build();

        var dragonSlayer = new ItemBuilder("dragon_slayer")
            .WithName("Dragon Slayer Sword")
            .WithDescription("A legendary blade forged specifically to slay dragons. It glows with an ethereal blue light.")
            .AsWeapon(damage: 20, criticalChance: 30)
            .WithWeight(6)
            .WithValue(500)
            .WithRarity(ItemRarity.Legendary)
            .AsUnique()
            .WithTheme("fantasy")
            .Build();

        // Armor
        var leatherArmor = new ItemBuilder("leather_armor")
            .WithName("Leather Armor")
            .WithDescription("Supple yet protective leather armor")
            .AsArmor(armorBonus: 3, slot: "chest")
            .WithWeight(5)
            .WithValue(40)
            .WithTheme("fantasy")
            .Build();

        var dragonPlate = new ItemBuilder("dragon_plate")
            .WithName("Dragon Plate Armor")
            .WithDescription("Armor crafted from a dragon's scales. It shimmers with scales of blue and gold.")
            .AsArmor(armorBonus: 10, slot: "chest")
            .WithWeight(8)
            .WithValue(300)
            .WithRarity(ItemRarity.Legendary)
            .AsUnique()
            .WithTheme("fantasy")
            .Build();

        // Keys
        var caveKey = new ItemBuilder("cave_key")
            .WithName("Goblin Cave Key")
            .WithDescription("A crude iron key carved with goblin runes")
            .AsKey("goblin_cave_entrance", KeyType.Mechanical)
            .WithWeight(1)
            .WithValue(20)
            .WithTheme("fantasy")
            .Build();

        var magicKey = new ItemBuilder("magic_key")
            .WithName("Mystic Key of Aldric")
            .WithDescription("A shimmering key that glows with magical energy. It unlocks magical barriers.")
            .AsKey("dragon_barrier", KeyType.Magical)
            .WithWeight(1)
            .WithValue(100)
            .WithRarity(ItemRarity.Rare)
            .WithTheme("fantasy")
            .Build();

        // Teleportation
        var homescroll = new ItemBuilder("home_scroll")
            .WithName("Scroll of Home")
            .WithDescription("An ancient scroll. When unrolled, it transports the bearer home.")
            .AsTeleportation("town_square", "You unroll the scroll and feel a warm tingling. Suddenly, you're back in town!")
            .WithWeight(0)
            .WithValue(150)
            .WithRarity(ItemRarity.Rare)
            .WithTheme("fantasy")
            .Build();

        var mountainPortal = new ItemBuilder("mountain_portal")
            .WithName("Portal Stone")
            .WithDescription("A glowing stone that opens a portal to Mount Infernus")
            .AsTeleportation("high_peaks", "The stone glows. A shimmering portal opens, depositing you on the high peaks.")
            .WithWeight(2)
            .WithValue(200)
            .WithRarity(ItemRarity.Epic)
            .WithTheme("fantasy")
            .Build();

        // Consumables
        var healthPotion = new ItemBuilder("health_potion")
            .WithName("Health Potion")
            .WithDescription("A bottle of red liquid that restores vitality")
            .AsConsumable(uses: 1)
            .WithConsumableEffect("heal", 50)
            .WithWeight(1)
            .WithValue(25)
            .WithTheme("fantasy")
            .Build();

        var manaPotion = new ItemBuilder("mana_potion")
            .WithName("Mana Potion")
            .WithDescription("A shimmering blue potion that restores magical energy")
            .AsConsumable(uses: 1)
            .WithConsumableEffect("mana", 50)
            .WithWeight(1)
            .WithValue(30)
            .WithTheme("fantasy")
            .Build();

        // Quest Items
        var crown = new ItemBuilder("crown_of_amalion")
            .WithName("Crown of Amalion")
            .WithDescription("A magnificent crown radiating magical power. The prize sought after by kings and heroes.")
            .AsQuestItem()
            .WithWeight(2)
            .WithValue(1000)
            .WithRarity(ItemRarity.Legendary)
            .AsUnique()
            .WithTheme("fantasy")
            .Build();

        gameBuilder.AddItems(
            ironSword, woodenStaff, dragonSlayer, leatherArmor, dragonPlate,
            caveKey, magicKey, homescroll, mountainPortal,
            healthPotion, manaPotion, crown);

        // ========== QUESTS ==========

        var mainQuest = new Quest
        {
            Id = "dragon_quest",
            Title = "Defeat the Dragon",
            Description = "Travel to Mount Infernus and defeat the dragon Infernus to recover the Crown of Amalion",
            GiverNpcId = "town_crier",
            RewardExperience = 500,
            RewardGold = 200,
            Objectives = new() { "Reach the Dragon's Lair", "Defeat Infernus the Dragon", "Return the Crown" }
        };

        gameBuilder.AddQuest(mainQuest);
        gameBuilder.AddWinConditionRoom("dragon_lair"); // Victory = defeat dragon

        return gameBuilder
            .WithStartingRoom("town_square")
            .Build();
    }
}
