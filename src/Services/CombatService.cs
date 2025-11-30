using CSharpRPGBackend.Core;

namespace CSharpRPGBackend.Services;

/// <summary>
/// Handles all combat calculations and damage resolution.
/// Uses character stats (Strength, Agility, Armor) and equipment bonuses.
/// </summary>
public class CombatService
{
    private readonly Random _random = new();

    /// <summary>
    /// Represents the result of a combat action.
    /// </summary>
    public class CombatResult
    {
        public bool WasHit { get; set; }
        public bool WasCritical { get; set; }
        public int DamageDealt { get; set; }
        public int DamageAfterArmor { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Resolves an attack from attacker to defender.
    /// Accounts for dodge chance, critical hits, and armor.
    /// </summary>
    public CombatResult ResolveAttack(Character attacker, Character defender)
    {
        var result = new CombatResult();

        // Check if attack hits or is dodged
        if (IsAttackDodged(attacker, defender))
        {
            result.WasHit = false;
            result.DamageDealt = 0;
            result.DamageAfterArmor = 0;
            result.Message = $"{defender.Name} dodges the attack!";
            return result;
        }

        // Calculate base damage from attacker
        int baseDamage = attacker.GetTotalDamage(null); // No weapon bonus for now (simplified)

        // Check for critical hit
        bool isCritical = RollForCritical(attacker);
        if (isCritical)
        {
            baseDamage = (int)(baseDamage * 1.5); // 50% damage increase
            result.WasCritical = true;
        }

        // Apply armor reduction - use defender's armor stat directly
        int defenderArmorRating = defender.Armor;
        int armorReduction = CalculateArmorReduction(defenderArmorRating);
        int finalDamage = Math.Max(1, baseDamage - armorReduction); // Minimum 1 damage gets through

        result.WasHit = true;
        result.DamageDealt = baseDamage;
        result.DamageAfterArmor = finalDamage;

        // Generate message
        string critText = isCritical ? " CRITICAL HIT!" : "";
        string armorText = armorReduction > 0 ? $" ({baseDamage} - {armorReduction} armor = {finalDamage})" : "";
        result.Message = $"{attacker.Name} attacks {defender.Name} for {finalDamage} damage{critText}{armorText}";

        return result;
    }

    /// <summary>
    /// Check if an attack is dodged based on agility and randomness.
    /// </summary>
    private bool IsAttackDodged(Character attacker, Character defender)
    {
        // Attacker's accuracy is reduced by their agility difference
        // Defender's dodge chance increases with agility
        int attackerAccuracy = 70 + (attacker.Agility - 10) * 2; // Base 70%, +2% per agility point
        int defenderDodge = 10 + (defender.Agility - 10) * 3; // Base 10%, +3% per agility point

        int hitChance = Math.Max(20, Math.Min(95, attackerAccuracy - defenderDodge)); // Clamp 20-95%
        int roll = _random.Next(100);

        return roll >= hitChance;
    }

    /// <summary>
    /// Check if an attack is a critical hit based on agility.
    /// </summary>
    private bool RollForCritical(Character attacker)
    {
        // Base 5% critical chance, +1% per agility point above 10
        int critChance = 5 + (attacker.Agility - 10);
        critChance = Math.Max(1, Math.Min(50, critChance)); // Clamp 1-50%

        int roll = _random.Next(100);
        return roll < critChance;
    }

    /// <summary>
    /// Calculate how much damage is reduced by armor.
    /// Armor reduces damage but never more than a percentage.
    /// </summary>
    private int CalculateArmorReduction(int armorRating)
    {
        // Each armor point reduces damage by 0.5, max 50% reduction
        int reduction = armorRating / 2;
        return Math.Min(reduction, 15); // Cap at 15 damage reduction
    }

    /// <summary>
    /// Get the equipped weapon for a character, if any.
    /// </summary>
    private Item? GetEquippedWeapon(Character character)
    {
        var mainHandSlot = character.EquipmentSlots.ContainsKey("main_hand") ? character.EquipmentSlots["main_hand"] : null;
        if (mainHandSlot != null)
        {
            return new Item { Id = mainHandSlot, DamageBonus = 5 }; // Simplified - would normally fetch from inventory
        }

        return null;
    }

    /// <summary>
    /// Get all equipped armor items for a character.
    /// </summary>
    private Dictionary<string, Item> GetEquippedArmorItems(Character character)
    {
        var armorItems = new Dictionary<string, Item>();
        var armorSlots = new[] { "head", "chest", "hands", "legs", "feet" };

        foreach (var slot in armorSlots)
        {
            if (character.EquipmentSlots.ContainsKey(slot) && character.EquipmentSlots[slot] != null)
            {
                var itemId = character.EquipmentSlots[slot];
                // In a real implementation, would fetch item details from a repository
                // For now, we use the Character's Armor stat directly
            }
        }

        return armorItems;
    }

    /// <summary>
    /// Apply damage to a character and check if they're defeated.
    /// </summary>
    public bool ApplyDamage(Character character, int damage)
    {
        character.TakeDamage(damage);
        return character.IsAlive;
    }

    /// <summary>
    /// Get combat stats as a formatted string for display.
    /// </summary>
    public string GetCombatStats(Character character, Item? weapon = null)
    {
        int totalDamage = character.GetTotalDamage(weapon);
        int agilityMod = character.GetAgilityModifier();
        int critChance = 5 + agilityMod;
        critChance = Math.Max(1, Math.Min(50, critChance));

        return $"Damage: {totalDamage} | Armor: {character.Armor} | Agility: {character.Agility} (Critical: {critChance}%)";
    }

    /// <summary>
    /// Represents the result of a flee attempt.
    /// </summary>
    public class FleeResult
    {
        public bool Succeeded { get; set; }
        public int FleeChance { get; set; }
        public int Roll { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Attempt to flee from combat based on agility comparison.
    /// Player's agility vs NPC's agility determines success chance.
    /// </summary>
    public FleeResult AttemptFlee(Character player, Character enemy)
    {
        var result = new FleeResult();

        // Base flee chance: 50%
        // +5% for each point of player agility above enemy agility
        // -10% for each point of enemy agility above player agility
        int agilityDifference = player.Agility - enemy.Agility;
        int fleeChance = 50 + (agilityDifference * 5);

        // Clamp between 15% and 95%
        fleeChance = Math.Max(15, Math.Min(95, fleeChance));
        result.FleeChance = fleeChance;

        // Roll the dice
        int roll = _random.Next(100);
        result.Roll = roll;
        result.Succeeded = roll < fleeChance;

        if (result.Succeeded)
        {
            result.Message = $"You successfully escape from {enemy.Name}! (Roll {roll} vs {fleeChance}% chance)";
        }
        else
        {
            result.Message = $"You try to flee but {enemy.Name} cuts off your escape! (Roll {roll} vs {fleeChance}% chance)";
        }

        return result;
    }
}
