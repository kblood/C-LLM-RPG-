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

        // Calculate base damage from attacker (including equipped weapon)
        Item? equippedWeapon = GetEquippedWeapon(attacker);
        int baseDamage = attacker.GetTotalDamage(equippedWeapon);

        // Check for critical hit
        bool isCritical = RollForCritical(attacker);
        if (isCritical)
        {
            baseDamage = (int)(baseDamage * 1.5); // 50% damage increase
            result.WasCritical = true;
        }

        // Apply armor reduction - use defender's armor stat plus equipped armor
        int defenderArmorRating = GetTotalArmor(defender);
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
    /// Looks up the actual item from the character's carried items.
    /// </summary>
    private Item? GetEquippedWeapon(Character character)
    {
        // Check if main_hand slot has an item ID equipped
        if (!character.EquipmentSlots.TryGetValue("main_hand", out var itemId) || itemId == null)
            return null;

        // Look up the actual Item from the character's carried items
        if (character.CarriedItems.TryGetValue(itemId, out var inventoryItem))
            return inventoryItem.Item;

        return null;
    }

    /// <summary>
    /// Get all equipped armor items for a character.
    /// Looks up actual items from the character's carried items.
    /// </summary>
    private Dictionary<string, Item> GetEquippedArmorItems(Character character)
    {
        var armorItems = new Dictionary<string, Item>();
        var armorSlots = new[] { "head", "chest", "hands", "legs", "feet" };

        foreach (var slot in armorSlots)
        {
            if (character.EquipmentSlots.TryGetValue(slot, out var itemId) && itemId != null)
            {
                // Look up the actual Item from carried items
                if (character.CarriedItems.TryGetValue(itemId, out var inventoryItem))
                {
                    armorItems[slot] = inventoryItem.Item;
                }
            }
        }

        return armorItems;
    }

    /// <summary>
    /// Calculate total armor rating including base armor stat and equipped armor pieces.
    /// </summary>
    private int GetTotalArmor(Character character)
    {
        int totalArmor = character.Armor; // Base armor stat

        // Add armor bonuses from equipped items
        var equippedArmor = GetEquippedArmorItems(character);
        foreach (var item in equippedArmor.Values)
        {
            if (item.IsEquippable && item.ArmorBonus > 0)
                totalArmor += item.ArmorBonus;
        }

        return totalArmor;
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

    /// <summary>
    /// Represents companion assistance in combat.
    /// </summary>
    public class CompanionAssistance
    {
        public bool HasCompanions { get; set; }
        public int DamageBonus { get; set; }
        public List<string> CompanionMessages { get; set; } = new();
    }

    /// <summary>
    /// Calculate damage bonus and messages from companions assisting in combat.
    /// Each companion contributes based on their stats.
    /// </summary>
    public CompanionAssistance CalculateCompanionAssistance(List<Character> companions)
    {
        var assistance = new CompanionAssistance();

        if (companions == null || companions.Count == 0)
        {
            assistance.HasCompanions = false;
            return assistance;
        }

        assistance.HasCompanions = true;
        int totalBonus = 0;

        foreach (var companion in companions)
        {
            if (!companion.IsAlive)
                continue;

            // Each companion contributes 20% of their damage output as bonus
            Item? companionWeapon = GetEquippedWeapon(companion);
            int companionDamage = companion.GetTotalDamage(companionWeapon);
            int contribution = (int)(companionDamage * 0.2);
            totalBonus += contribution;

            // Add flavor message
            string message = companion.Strength >= 13 ? $"{companion.Name} strikes a powerful blow!"
                           : companion.Agility >= 13 ? $"{companion.Name} lands a quick strike!"
                           : $"{companion.Name} helps press the attack!";

            assistance.CompanionMessages.Add(message);
        }

        assistance.DamageBonus = totalBonus;
        return assistance;
    }
}
