using CSharpRPGBackend.Core;
using CSharpRPGBackend.Services;

namespace CSharpRPGBackend.Tests;

public class GameLoaderWorldProjectTests
{
    [Fact]
    public async Task LoadsWorldProjectsFromGameJsonUsingStringEnums()
    {
        var root = Path.Combine(Path.GetTempPath(), "CSharpRpgTests", Guid.NewGuid().ToString("N"));
        var rooms = Path.Combine(root, "rooms");
        Directory.CreateDirectory(rooms);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "game.json"), """
                {
                  "id": "growing-world",
                  "title": "Growing World",
                  "gameSettings": { "startingRoomId": "start" },
                  "worldProjects": [
                    {
                      "id": "repair_bridge",
                      "name": "Repair the Bridge",
                      "status": "available",
                      "stages": [
                        {
                          "id": "clear_road",
                          "name": "Clear the Road",
                          "requirements": [
                            {
                              "type": "event",
                              "eventType": "npcDefeated",
                              "targetId": "bandit",
                              "requiredAmount": 1
                            }
                          ],
                          "effects": [
                            {
                              "type": "setRoomDescription",
                              "targetId": "start",
                              "value": "The repaired bridge carries travelers again."
                            }
                          ]
                        }
                      ]
                    }
                  ]
                }
                """);
            await File.WriteAllTextAsync(Path.Combine(rooms, "start.json"), """
                {
                  "id": "start",
                  "name": "Start",
                  "description": "A broken bridge blocks the road.",
                  "exits": [],
                  "npcs": []
                }
                """);

            var game = await new GameLoader().LoadGameAsync(root);

            var project = Assert.Single(game.WorldProjects);
            Assert.Equal(WorldProjectStatus.Available, project.Status);
            var stage = Assert.Single(project.Stages);
            Assert.Equal(WorldEventType.NpcDefeated, Assert.Single(stage.Requirements).EventType);
            Assert.Equal(WorldProjectEffectType.SetRoomDescription, Assert.Single(stage.Effects).Type);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
