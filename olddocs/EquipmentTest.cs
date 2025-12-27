using CSharpRPGBackend.Core;
using CSharpRPGBackend.Services;

namespace CSharpRPGBackend;

/// <summary>
/// Test program to demonstrate the equipment system working.
/// Shows that equipped weapons increase damage and equipped armor reduces damage taken.
/// </summary>
public class EquipmentTest
{
    public static void RunTest()
    {
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine("EQUIPMENT SYSTEM TEST - Part 1 Verification");
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine();

        var combatService = new CombatService();

        // Test 1: Weapon Damage Bonus
        Console.WriteLine("TEST 1: Weapon Damage Bonus");
        Console.WriteLine("-".PadRight(80, '-'));
        TestWeaponDamage(combatService);
        Console.WriteLine();

        // Test 2: Armor Damage Reduction
        Console.WriteLine("TEST 2: Armor Damage Reduction");
        Console.WriteLine("-".PadRight(80, '-'));
        TestArmorReduction(combatService);
        Console.WriteLine();

        // Test 3: Multiple Armor Pieces
        Console.WriteLine("TEST 3: Multiple Armor Pieces Stacking");
        Console.WriteLine("-".PadRight(80, '-'));
        TestMultipleArmor(combatService);
        Console.WriteLine();

        // Test 4: Both Weapon and Armor
        Console.WriteLine("TEST 4: Full Equipment (Weapon + Armor)");
        Console.WriteLine("-".PadRight(80, '-'));
        TestFullEquipment(combatService);
        Console.WriteLine();

        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine("ALL TESTS COMPLETE");
        Console.WriteLine("=".PadRight(80, '='));
    }

    private static void TestWeaponDamage(CombatService combatService)
    {
        // Create attacker with base stats
        var attacker = new Character
        {
            Id = "test_attacker",
            Name = "Test Warrior",
            Strength = 10, // Base damage: 5 + (10-10)/2 = 5
            Agility = 1, // Minimum agility = guaranteed hit
            Health = 100,
            MaxHealth = 100
        };

        // Create defender
        var defender = new Character
        {
            Id = "test_dummy",
            Name = "Training Dummy",
            Armor = 0, // No armor for pure damage test
            Agility = 1, // Minimum agility = won't dodge
            Health = 100,
            MaxHealth = 100
        };

        // Test without weapon
        Console.WriteLine("Scenario A: No weapon equipped");
        Console.WriteLine($"  Attacker Strength: {attacker.Strength}");
        Console.WriteLine($"  Expected base damage: 5 + (10-10)/2 = 5");

        var resultNoWeapon = combatService.ResolveAttack(attacker, defender);
        Console.WriteLine($"  Actual damage dealt: {resultNoWeapon.DamageDealt}");
        Console.WriteLine($"  ✅ Result: {(resultNoWeapon.DamageDealt == 5 ? "PASS" : "FAIL")}");
        Console.WriteLine();

        // Create and equip weapon
        var ironSword = new Item
        {
            Id = "iron_sword",
            Name = "Iron Sword",
            DamageBonus = 5,
            IsEquippable = true,
            EquipmentSlot = "main_hand"
        };

        // Add to carried items and equip
        attacker.CarriedItems["iron_sword"] = new InventoryItem { Item = ironSword, Quantity = 1 };
        attacker.EquipmentSlots["main_hand"] = "iron_sword";

        Console.WriteLine("Scenario B: Iron Sword equipped (+5 damage)");
        Console.WriteLine($"  Base damage: 5");
        Console.WriteLine($"  Weapon bonus: +{ironSword.DamageBonus}");
        Console.WriteLine($"  Expected total: 5 + 5 = 10");

        var resultWithWeapon = combatService.ResolveAttack(attacker, defender);
        Console.WriteLine($"  Actual damage dealt: {resultWithWeapon.DamageDealt}");
        Console.WriteLine($"  ✅ Result: {(resultWithWeapon.DamageDealt == 10 ? "PASS" : "FAIL")}");
    }

    private static void TestArmorReduction(CombatService combatService)
    {
        // Create attacker with fixed damage
        var attacker = new Character
        {
            Id = "test_attacker",
            Name = "Test Bandit",
            Strength = 14, // Base damage: 5 + (14-10)/2 = 7
            Agility = 1, // Minimum agility = guaranteed hit
            Health = 100,
            MaxHealth = 100
        };

        // Create defender without armor
        var defender = new Character
        {
            Id = "test_defender",
            Name = "Test Knight",
            Armor = 0, // Will add armor via equipment
            Agility = 1, // Low agility = won't dodge
            Health = 100,
            MaxHealth = 100
        };

        // Test without armor
        Console.WriteLine("Scenario A: No armor equipped");
        Console.WriteLine($"  Incoming damage: 7");
        Console.WriteLine($"  Defender armor: 0");
        Console.WriteLine($"  Expected damage taken: 7");

        var resultNoArmor = combatService.ResolveAttack(attacker, defender);
        Console.WriteLine($"  Actual damage taken: {resultNoArmor.DamageAfterArmor}");
        Console.WriteLine($"  ✅ Result: {(resultNoArmor.DamageAfterArmor == 7 ? "PASS" : "FAIL")}");
        Console.WriteLine();

        // Create and equip chest armor
        var ironChestplate = new Item
        {
            Id = "iron_chestplate",
            Name = "Iron Chestplate",
            ArmorBonus = 4, // +4 armor = 2 damage reduction (armor/2)
            IsEquippable = true,
            EquipmentSlot = "chest"
        };

        // Add to carried items and equip
        defender.CarriedItems["iron_chestplate"] = new InventoryItem { Item = ironChestplate, Quantity = 1 };
        defender.EquipmentSlots["chest"] = "iron_chestplate";

        Console.WriteLine("Scenario B: Iron Chestplate equipped (+4 armor)");
        Console.WriteLine($"  Incoming damage: 7");
        Console.WriteLine($"  Base armor: 0");
        Console.WriteLine($"  Equipment armor: +{ironChestplate.ArmorBonus}");
        Console.WriteLine($"  Total armor: 4");
        Console.WriteLine($"  Armor reduction: 4 / 2 = 2");
        Console.WriteLine($"  Expected damage taken: 7 - 2 = 5");

        var resultWithArmor = combatService.ResolveAttack(attacker, defender);
        Console.WriteLine($"  Actual damage taken: {resultWithArmor.DamageAfterArmor}");
        Console.WriteLine($"  ✅ Result: {(resultWithArmor.DamageAfterArmor == 5 ? "PASS" : "FAIL")}");
    }

    private static void TestMultipleArmor(CombatService combatService)
    {
        // Create attacker
        var attacker = new Character
        {
            Id = "test_attacker",
            Name = "Test Attacker",
            Strength = 16, // Base damage: 5 + (16-10)/2 = 8
            Agility = 1, // Minimum agility = guaranteed hit
            Health = 100,
            MaxHealth = 100
        };

        // Create defender with base armor
        var defender = new Character
        {
            Id = "test_defender",
            Name = "Test Tank",
            Armor = 2, // Base armor stat
            Agility = 1,
            Health = 100,
            MaxHealth = 100
        };

        // Create multiple armor pieces
        var helmet = new Item
        {
            Id = "iron_helmet",
            Name = "Iron Helmet",
            ArmorBonus = 2,
            IsEquippable = true,
            EquipmentSlot = "head"
        };

        var chestplate = new Item
        {
            Id = "iron_chestplate",
            Name = "Iron Chestplate",
            ArmorBonus = 4,
            IsEquippable = true,
            EquipmentSlot = "chest"
        };

        var gloves = new Item
        {
            Id = "iron_gloves",
            Name = "Iron Gloves",
            ArmorBonus = 1,
            IsEquippable = true,
            EquipmentSlot = "hands"
        };

        // Equip all armor pieces
        defender.CarriedItems["iron_helmet"] = new InventoryItem { Item = helmet, Quantity = 1 };
        defender.CarriedItems["iron_chestplate"] = new InventoryItem { Item = chestplate, Quantity = 1 };
        defender.CarriedItems["iron_gloves"] = new InventoryItem { Item = gloves, Quantity = 1 };

        defender.EquipmentSlots["head"] = "iron_helmet";
        defender.EquipmentSlots["chest"] = "iron_chestplate";
        defender.EquipmentSlots["hands"] = "iron_gloves";

        Console.WriteLine("Scenario: Multiple armor pieces equipped");
        Console.WriteLine($"  Incoming damage: 8");
        Console.WriteLine($"  Base armor: {defender.Armor}");
        Console.WriteLine($"  Helmet: +{helmet.ArmorBonus}");
        Console.WriteLine($"  Chestplate: +{chestplate.ArmorBonus}");
        Console.WriteLine($"  Gloves: +{gloves.ArmorBonus}");
        Console.WriteLine($"  Total armor: 2 + 2 + 4 + 1 = 9");
        Console.WriteLine($"  Armor reduction: 9 / 2 = 4 (capped at min 1 damage)");
        Console.WriteLine($"  Expected damage taken: 8 - 4 = 4");

        var result = combatService.ResolveAttack(attacker, defender);
        Console.WriteLine($"  Actual damage taken: {result.DamageAfterArmor}");
        Console.WriteLine($"  ✅ Result: {(result.DamageAfterArmor == 4 ? "PASS" : "FAIL")}");
    }

    private static void TestFullEquipment(CombatService combatService)
    {
        // Create fully equipped warrior
        var warrior = new Character
        {
            Id = "warrior",
            Name = "Equipped Warrior",
            Strength = 12, // Base damage: 5 + (12-10)/2 = 6
            Armor = 1, // Base armor
            Agility = 1, // Minimum agility = guaranteed hit
            Health = 100,
            MaxHealth = 100
        };

        // Create fully equipped knight
        var knight = new Character
        {
            Id = "knight",
            Name = "Equipped Knight",
            Strength = 10, // Base damage: 5
            Armor = 2, // Base armor
            Agility = 1, // Minimum agility = won't dodge
            Health = 100,
            MaxHealth = 100
        };

        // Equip warrior with weapon
        var battleAxe = new Item
        {
            Id = "battle_axe",
            Name = "Battle Axe",
            DamageBonus = 7,
            IsEquippable = true,
            EquipmentSlot = "main_hand"
        };
        warrior.CarriedItems["battle_axe"] = new InventoryItem { Item = battleAxe, Quantity = 1 };
        warrior.EquipmentSlots["main_hand"] = "battle_axe";

        // Equip knight with armor
        var steelArmor = new Item
        {
            Id = "steel_armor",
            Name = "Steel Armor",
            ArmorBonus = 6,
            IsEquippable = true,
            EquipmentSlot = "chest"
        };
        knight.CarriedItems["steel_armor"] = new InventoryItem { Item = steelArmor, Quantity = 1 };
        knight.EquipmentSlots["chest"] = "steel_armor";

        Console.WriteLine("Scenario: Warrior attacks Knight (both equipped)");
        Console.WriteLine();
        Console.WriteLine($"  {warrior.Name}:");
        Console.WriteLine($"    Strength: {warrior.Strength} (base damage: 6)");
        Console.WriteLine($"    Weapon: {battleAxe.Name} (+{battleAxe.DamageBonus} damage)");
        Console.WriteLine($"    Total damage: 6 + 7 = 13");
        Console.WriteLine();
        Console.WriteLine($"  {knight.Name}:");
        Console.WriteLine($"    Base armor: {knight.Armor}");
        Console.WriteLine($"    Equipment: {steelArmor.Name} (+{steelArmor.ArmorBonus} armor)");
        Console.WriteLine($"    Total armor: 2 + 6 = 8");
        Console.WriteLine($"    Armor reduction: 8 / 2 = 4");
        Console.WriteLine();
        Console.WriteLine($"  Expected outcome: 13 - 4 = 9 damage");

        var result = combatService.ResolveAttack(warrior, knight);
        Console.WriteLine($"  Actual damage: {result.DamageAfterArmor}");
        Console.WriteLine($"  ✅ Result: {(result.DamageAfterArmor == 9 ? "PASS" : "FAIL")}");
        Console.WriteLine();
        Console.WriteLine($"  Combat message: {result.Message}");
    }
}
