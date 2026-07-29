using CSharpRPGBackend.Core;

namespace CSharpRPGBackend.Tests;

public class CharacterProgressionTests
{
    [Fact]
    public void GainExperience_LevelsAtCumulativeThresholdsAndImprovesStats()
    {
        var character = new Character
        {
            Level = 1,
            Experience = 0,
            Health = 40,
            MaxHealth = 50,
            Strength = 10,
            Agility = 12
        };

        character.GainExperience(99);

        Assert.Equal(1, character.Level);
        Assert.Equal(99, character.Experience);

        character.GainExperience(1);

        Assert.Equal(2, character.Level);
        Assert.Equal(60, character.MaxHealth);
        Assert.Equal(50, character.Health);
        Assert.Equal(11, character.Strength);
        Assert.Equal(13, character.Agility);

        character.GainExperience(200);

        Assert.Equal(3, character.Level);
        Assert.Equal(300, character.Experience);
        Assert.Equal(70, character.MaxHealth);
        Assert.Equal(60, character.Health);
        Assert.Equal(12, character.Strength);
        Assert.Equal(14, character.Agility);

        character.GainExperience(-50);
        Assert.Equal(300, character.Experience);
    }
}
