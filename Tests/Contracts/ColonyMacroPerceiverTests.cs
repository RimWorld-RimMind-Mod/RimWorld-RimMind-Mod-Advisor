using System;
using System.Collections.Generic;
using RimMind.Advisor.Advisor;
using RimMind.Advisor.Perception;
using RimMind.Advisor.Settings;
using RimMind.Application.Common.Models.UI;
using RimMind.Presentation.Api;
using Verse;
using Xunit;

namespace RimMind.Advisor.Tests.Contracts
{
    public class ColonyMacroPerceiverTests
    {
        [Fact]
        public void Evaluate_FoodDaysRemaining_LessThanThree_FlagsFoodCritical()
        {
            var snapshot = new ColonyMacroSnapshot
            {
                ColonistCount = 3,
                TotalFoodNutrition = 10f, // 10 / (3 * 1.6) = 2.08 days
                WoodCount = 100,
                SteelCount = 100,
                OutdoorTemperature = 20f,
                FreeColonistBedCount = 3
            };

            var state = ColonyMacroPerceiver.Evaluate(snapshot);

            Assert.True(state.IsFoodCritical);
            Assert.True(state.FoodDaysRemaining < 3.0f);
            Assert.Contains(state.CriticalBottlenecks, b => b.Contains("[Food Crisis]"));
        }

        [Fact]
        public void Evaluate_FoodDaysRemaining_Abundant_DoesNotFlagCritical()
        {
            var snapshot = new ColonyMacroSnapshot
            {
                ColonistCount = 3,
                TotalFoodNutrition = 120f, // 120 / (3 * 1.6) = 25 days
                WoodCount = 100,
                SteelCount = 100,
                OutdoorTemperature = 20f,
                FreeColonistBedCount = 3
            };

            var state = ColonyMacroPerceiver.Evaluate(snapshot);

            Assert.False(state.IsFoodCritical);
            Assert.True(state.FoodDaysRemaining >= 3.0f);
            Assert.DoesNotContain(state.CriticalBottlenecks, b => b.Contains("[Food Crisis]"));
        }

        [Fact]
        public void Evaluate_ExtremeCold_FlagsExtremeCold()
        {
            var snapshot = new ColonyMacroSnapshot
            {
                ColonistCount = 2,
                TotalFoodNutrition = 50f,
                WoodCount = 100,
                SteelCount = 100,
                OutdoorTemperature = -15f,
                ColonistMinComfyTemp = 10f,
                FreeColonistBedCount = 2
            };

            var state = ColonyMacroPerceiver.Evaluate(snapshot);

            Assert.True(state.IsExtremeCold);
            Assert.Contains(state.CriticalBottlenecks, b => b.Contains("[Extreme Cold]"));
        }

        [Fact]
        public void Evaluate_ExtremeHeat_FlagsExtremeHeat()
        {
            var snapshot = new ColonyMacroSnapshot
            {
                ColonistCount = 2,
                TotalFoodNutrition = 50f,
                WoodCount = 100,
                SteelCount = 100,
                OutdoorTemperature = 42f,
                ColonistMaxComfyTemp = 32f,
                FreeColonistBedCount = 2
            };

            var state = ColonyMacroPerceiver.Evaluate(snapshot);

            Assert.True(state.IsExtremeHeat);
            Assert.Contains(state.CriticalBottlenecks, b => b.Contains("[Extreme Heat]"));
        }

        [Fact]
        public void Evaluate_UnburiedCorpses_FlagsBottleneck()
        {
            var snapshot = new ColonyMacroSnapshot
            {
                ColonistCount = 3,
                TotalFoodNutrition = 50f,
                WoodCount = 100,
                SteelCount = 100,
                OutdoorTemperature = 21f,
                UnburiedCorpsesCount = 3,
                FreeColonistBedCount = 3
            };

            var state = ColonyMacroPerceiver.Evaluate(snapshot);

            Assert.True(state.HasUnburiedCorpses);
            Assert.Contains(state.CriticalBottlenecks, b => b.Contains("[Unburied Corpses]"));
        }

        [Fact]
        public void Evaluate_ShelterShortage_FlagsHomelessColonists()
        {
            var snapshot = new ColonyMacroSnapshot
            {
                ColonistCount = 4,
                TotalFoodNutrition = 50f,
                WoodCount = 100,
                SteelCount = 100,
                OutdoorTemperature = 21f,
                FreeColonistBedCount = 2
            };

            var state = ColonyMacroPerceiver.Evaluate(snapshot);

            Assert.True(state.HasHomelessColonists);
            Assert.Contains(state.CriticalBottlenecks, b => b.Contains("[Shelter Shortage]"));
        }

        [Fact]
        public void Evaluate_HostileThreat_FlagsActiveThreat()
        {
            var snapshot = new ColonyMacroSnapshot
            {
                ColonistCount = 3,
                TotalFoodNutrition = 50f,
                WoodCount = 100,
                SteelCount = 100,
                OutdoorTemperature = 21f,
                HostileThreatCount = 2,
                FreeColonistBedCount = 3
            };

            var state = ColonyMacroPerceiver.Evaluate(snapshot);

            Assert.True(state.HasActiveThreat);
            Assert.Contains(state.CriticalBottlenecks, b => b.Contains("[Hostile Threat]"));
        }

        [Fact]
        public void Evaluate_ResourceDepletion_FlagsDepletion()
        {
            var snapshot = new ColonyMacroSnapshot
            {
                ColonistCount = 3,
                TotalFoodNutrition = 50f,
                WoodCount = 35, // < 80
                SteelCount = 20, // < 50
                OutdoorTemperature = 21f,
                FreeColonistBedCount = 3
            };

            var state = ColonyMacroPerceiver.Evaluate(snapshot);

            Assert.True(state.IsResourceDepleted);
            Assert.Contains(state.CriticalBottlenecks, b => b.Contains("[Resource Depleted]"));
        }

        [Fact]
        public void FormatPromptAugmentation_FormatsCorrectSummaryAndBottlenecks()
        {
            var snapshot = new ColonyMacroSnapshot
            {
                ColonistCount = 3,
                TotalFoodNutrition = 8f,
                WoodCount = 40,
                SteelCount = 30,
                OutdoorTemperature = -8f,
                FreeColonistBedCount = 2
            };

            var state = ColonyMacroPerceiver.Evaluate(snapshot);
            var text = ColonyMacroPerceiver.FormatPromptAugmentation(state);

            Assert.Contains("=== COLONY MACRO STATUS & SURVIVAL PRIORITIES ===", text);
            Assert.Contains("CRITICAL SURVIVAL BOTTLENECKS", text);
            Assert.Contains("[Food Crisis]", text);
            Assert.Contains("[Extreme Cold]", text);
            Assert.Contains("[Shelter Shortage]", text);
            Assert.Contains("[Resource Depleted]", text);
        }

        [Fact]
        public void ApprovalManager_SubmitCabinetProposal_ExecutesAllCallbacksOnApproveAll()
        {
            var settings = new RimMindAdvisorSettings();
            var manager = new ApprovalManager(settings);
            var pawn = new Pawn { thingIDNumber = 101, LabelShort = "Alice" };

            int execCount1 = 0;
            int execCount2 = 0;
            int execCount3 = 0;
            bool allApprovedInvoked = false;

            var proposal = new CabinetProposal
            {
                Title = "Emergency Survival Cabinet Plan",
                Summary = "Food is critical and severe cold has struck.",
                Items = new List<AdviceItem>
                {
                    new AdviceItem { Action = "actions_designate_resources", Reason = "Harvest wild berries" },
                    new AdviceItem { Action = "actions_add_worktable_bill", Reason = "Cook 15 simple meals" },
                    new AdviceItem { Action = "actions_manage_equipment", Reason = "Wear parka" }
                },
                ExecutionCallbacks = new List<Action>
                {
                    () => execCount1++,
                    () => execCount2++,
                    () => execCount3++
                }
            };

            var entry = manager.SubmitCabinetProposal(
                proposal,
                pawn,
                onAllApproved: () => allApprovedInvoked = true,
                onRejected: () => { });

            Assert.NotNull(entry);
            Assert.Equal("advisor_cabinet", entry.source);
            Assert.Equal("Emergency Survival Cabinet Plan", entry.title);
            Assert.Contains("actions_designate_resources", entry.description);
            Assert.Contains("actions_add_worktable_bill", entry.description);
            Assert.Contains("actions_manage_equipment", entry.description);

            // Simulate selecting "Approve All" (options[0])
            string approveOption = entry.options[0];
            entry.callback?.Invoke(approveOption);

            Assert.Equal(1, execCount1);
            Assert.Equal(1, execCount2);
            Assert.Equal(1, execCount3);
            Assert.True(allApprovedInvoked);
        }

        [Fact]
        public void ApprovalManager_SubmitCabinetProposal_InvokesOnRejectedOnDismiss()
        {
            var settings = new RimMindAdvisorSettings();
            var manager = new ApprovalManager(settings);
            var pawn = new Pawn { thingIDNumber = 102, LabelShort = "Bob" };

            int execCount = 0;
            bool rejectedInvoked = false;

            var proposal = new CabinetProposal
            {
                Title = "Economic Expansion Plan",
                Summary = "Expand rooms and mining.",
                Items = new List<AdviceItem>
                {
                    new AdviceItem { Action = "actions_plan_room_project", Reason = "Build warehouse" }
                },
                ExecutionCallbacks = new List<Action>
                {
                    () => execCount++
                }
            };

            var entry = manager.SubmitCabinetProposal(
                proposal,
                pawn,
                onAllApproved: () => { },
                onRejected: () => rejectedInvoked = true);

            // Simulate selecting "Dismiss" (options[1])
            string dismissOption = entry.options[1];
            entry.callback?.Invoke(dismissOption);

            Assert.Equal(0, execCount);
            Assert.True(rejectedInvoked);
        }
    }
}
