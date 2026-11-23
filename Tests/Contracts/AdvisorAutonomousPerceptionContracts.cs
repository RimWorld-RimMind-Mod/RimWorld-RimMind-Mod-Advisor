using System.Linq;
using RimMind.Advisor.Comps;
using RimMind.Presentation.Api;
using RimMind.Testing;
using Verse;
using Xunit;

namespace RimMind.Advisor.Tests.Contracts
{
    [Collection("Advisor runtime")]
    public sealed class AdvisorAutonomousPerceptionContracts
    {
        [Fact]
        public void Pawn_lookup_filters_active_colonists_and_colony_animals_safely()
        {
            ContractCaseRunner.Run(
                ("CompAIAdvisor test stub is enabled by default", () =>
                {
                    var comp = new CompAIAdvisor();
                    Assert.True(comp.IsEnabled);
                }),
                ("GetSpawnedColonistsSafe returns active colonists and excludes dead", () =>
                {
                    var map = new Map();
                    var liveColonist = new Pawn { thingIDNumber = 10, Dead = false, Map = map, LabelShort = "Alice" };
                    var deadColonist = new Pawn { thingIDNumber = 11, Dead = true, Map = map, LabelShort = "Bob" };
                    map.mapPawns.FreeColonists.Add(liveColonist);
                    map.mapPawns.FreeColonists.Add(deadColonist);

                    var colonists = RimMindPawnLookup.GetSpawnedColonistsSafe(map);
                    Assert.Single(colonists);
                    Assert.Same(liveColonist, colonists[0]);
                }),
                ("GetActiveSpawnedColonyAnimals yields active pets and excludes dead", () =>
                {
                    var map = new Map();
                    var livePet = new Pawn { thingIDNumber = 50, Dead = false, LabelShort = "Husky" };
                    var deadPet = new Pawn { thingIDNumber = 51, Dead = true, LabelShort = "Cat" };
                    map.mapPawns.SpawnedColonyAnimals.Add(livePet);
                    map.mapPawns.SpawnedColonyAnimals.Add(deadPet);

                    var animals = RimMindPawnLookup.GetActiveSpawnedColonyAnimals(map).ToList();
                    Assert.Single(animals);
                    Assert.Same(livePet, animals[0]);
                }));
        }
    }
}
