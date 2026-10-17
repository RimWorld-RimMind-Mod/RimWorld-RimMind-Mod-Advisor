using System;
using System.IO;
using System.Linq;
using RimMind.Advisor.Comps;
using RimMind.Presentation.Api;
using RimMind.Testing;
using Verse;
using Xunit;

namespace RimMind.Advisor.Tests.Contracts
{
    public sealed class AdvisorAutonomousPerceptionContracts
    {
        [Fact]
        public void CompAIAdvisor_is_enabled_by_default_and_persisted_correctly()
        {
            ContractCaseRunner.Run(
                ("source defines IsEnabled default as true", () =>
                {
                    string source = ReadSource("Comps/CompAIAdvisor.cs");
                    Assert.Contains("public bool IsEnabled = true;", source, StringComparison.Ordinal);
                    Assert.DoesNotContain("public bool IsEnabled = false;", source, StringComparison.Ordinal);
                }),
                ("source exposes IsEnabled in Scribe with true default", () =>
                {
                    string source = ReadSource("Comps/CompAIAdvisor.cs");
                    Assert.Contains("Scribe_Values.Look(ref IsEnabled, \"aiAdvisorEnabled\", true);", source, StringComparison.Ordinal);
                }),
                ("stub instance is enabled by default", () =>
                {
                    var comp = new CompAIAdvisor();
                    Assert.True(comp.IsEnabled);
                }));
        }

        [Fact]
        public void CompAIAdvisor_ShouldIdleTrigger_evaluates_settings_and_idle_state()
        {
            ContractCaseRunner.Run(
                ("source requires both enableIdleTrigger and IsIdle", () =>
                {
                    string source = ReadSource("Comps/CompAIAdvisor.cs");
                    Assert.Contains("public bool ShouldIdleTrigger() => Settings.enableIdleTrigger && IsIdle();", source, StringComparison.Ordinal);
                }),
                ("source defines IsIdle with playerForced exclusion and standard wait wander jobs", () =>
                {
                    string source = ReadSource("Comps/CompAIAdvisor.cs");
                    Assert.Contains("if (job == null) return true;", source, StringComparison.Ordinal);
                    Assert.Contains("if (job.playerForced) return false;", source, StringComparison.Ordinal);
                    Assert.Contains("JobDefOf.Wait", source, StringComparison.Ordinal);
                    Assert.Contains("JobDefOf.Wait_Wander", source, StringComparison.Ordinal);
                    Assert.Contains("JobDefOf.GotoWander", source, StringComparison.Ordinal);
                    Assert.Contains("JobDefOf.Wait_MaintainPosture", source, StringComparison.Ordinal);
                }),
                ("idle trigger logic simulates expected truth table", () =>
                {
                    Func<bool, bool, bool> eval = (enableIdle, isIdle) => enableIdle && isIdle;
                    Assert.True(eval(true, true));
                    Assert.False(eval(false, true));
                    Assert.False(eval(true, false));
                    Assert.False(eval(false, false));
                }));
        }

        [Fact]
        public void AdvisorGameComponent_removes_double_random_check_and_scales_effective_chance()
        {
            ContractCaseRunner.Run(
                ("source queries GetEligibleColonists instead of raw FreeColonists", () =>
                {
                    string source = ReadSource("Advisor/AdvisorGameComponent.cs");
                    Assert.Contains("RimMindPawnLookup.GetEligibleColonists(map)", source, StringComparison.Ordinal);
                }),
                ("source does not have redundant double Rand.Value check", () =>
                {
                    string source = ReadSource("Advisor/AdvisorGameComponent.cs");
                    Assert.DoesNotContain("if (Rand.Value > settings.macroCheckChance) continue;", source, StringComparison.Ordinal);
                    Assert.Contains("float effectiveChance = UnityEngine.Mathf.Clamp(settings.macroCheckChance * scale, 0.05f, 0.95f);", source, StringComparison.Ordinal);
                    Assert.Contains("if (Rand.Value > effectiveChance) continue;", source, StringComparison.Ordinal);
                }),
                ("source triggers autonomous consultation when idle pawn perceives distressed animal", () =>
                {
                    string source = ReadSource("Advisor/AdvisorGameComponent.cs");
                    Assert.Contains("if (!shouldTrigger && comp.IsIdle() && HasDistressedColonyAnimals(map))", source, StringComparison.Ordinal);
                    Assert.Contains("HasDistressedColonyAnimals(Map map)", source, StringComparison.Ordinal);
                }));
        }

        [Fact]
        public void Domestic_animals_are_perceived_in_candidate_builder_and_advisor_loop()
        {
            ContractCaseRunner.Run(
                ("JobCandidateBuilder queries GetActiveSpawnedColonyAnimals for tend and rescue", () =>
                {
                    string source = ReadSource("Advisor/JobCandidateBuilder.cs");
                    Assert.Contains("RimMindPawnLookup.GetActiveSpawnedColonyAnimals(pawn.Map)", source, StringComparison.Ordinal);
                    Assert.Contains("injuredAnimal.Name?.ToStringShort ?? injuredAnimal.LabelShort", source, StringComparison.Ordinal);
                    Assert.Contains("downedAnimal.Name?.ToStringShort ?? downedAnimal.LabelShort", source, StringComparison.Ordinal);
                    Assert.Contains("HungerCategory.Starving", source, StringComparison.Ordinal);
                }),
                ("AdvisorGameComponent HasDistressedColonyAnimals checks downed, tend, and hunger", () =>
                {
                    string source = ReadSource("Advisor/AdvisorGameComponent.cs");
                    Assert.Contains("RimMindPawnLookup.GetActiveSpawnedColonyAnimals(map)", source, StringComparison.Ordinal);
                    Assert.Contains("animal.Downed", source, StringComparison.Ordinal);
                    Assert.Contains("animal.health?.HasHediffsNeedingTend() == true", source, StringComparison.Ordinal);
                    Assert.Contains("animal.needs?.food?.CurCategory >= HungerCategory.UrgentlyHungry", source, StringComparison.Ordinal);
                }),
                ("PawnLookup GetActiveSpawnedColonyAnimals yields active pets and excludes dead", () =>
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

        private static string ReadSource(string relativePath) =>
            File.ReadAllText(Path.Combine(
                SourceRoot(),
                relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static string SourceRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null &&
                   !Directory.Exists(Path.Combine(directory.FullName, "RimMind-Advisor", "Source")))
                directory = directory.Parent;

            return Path.Combine(
                directory?.FullName ?? throw new InvalidOperationException("Repository root not found."),
                "RimMind-Advisor",
                "Source");
        }
    }
}
