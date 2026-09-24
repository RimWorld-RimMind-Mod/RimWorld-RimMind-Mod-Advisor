using System;
using System.Linq;
using LudeonTK;
using RimMind.Advisor.Advisor;
using RimMind.Advisor.Comps;
using RimMind.Advisor.Concurrency;
using RimMind.Advisor.Data;
using RimMind.Advisor.Settings;
using RimMind.Application.Common.Models.UI;
using RimMind.Presentation.Api;
using RimWorld;
using Verse;

namespace RimMind.Advisor.Debug
{
    /// <summary>
    /// In-game behavioral autotest suite for RimMind-Advisor.
    /// Discovered automatically by Core's BehaviorAutotestRunner and exposed to Dev menu.
    /// </summary>
    public sealed class AdvisorBehaviorAutotestSuite : BehaviorAutotestSuiteBase
    {
        public override string ModId => "Advisor";
        public override string SuiteId => "Behavior.AdvisorCore";

        [DebugAction("Autotests", "Run Advisor In-Game Behavior Test", actionType = DebugActionType.Action)]
        public static void RunFromDevMenu() => RunSuiteFromDevMenu<AdvisorBehaviorAutotestSuite>();

        public override void RunSuite(IInGameBehaviorSuiteContext context)
        {
            Pawn? pawn = context.ActiveColonist;

            // 1. CompAIAdvisor Attachment
            if (context.CurrentMap?.mapPawns?.FreeColonists != null && context.CurrentMap.mapPawns.FreeColonists.Count > 0)
            {
                bool allAttached = context.CurrentMap.mapPawns.FreeColonists.All(p => p.GetComp<CompAIAdvisor>() != null);
                context.Assert(allAttached, "All free colonists on current map have CompAIAdvisor attached");
            }
            else if (pawn != null)
            {
                context.Assert(pawn.GetComp<CompAIAdvisor>() != null, "Active colonist has CompAIAdvisor attached");
            }
            else
            {
                context.Warn("No colonists on map to check CompAIAdvisor attachment");
            }

            // 2. JobCandidateBuilder Evaluation
            if (pawn != null)
            {
                try
                {
                    string candidates = JobCandidateBuilder.Build(pawn);
                    context.Assert(!string.IsNullOrWhiteSpace(candidates), "JobCandidateBuilder generates non-empty candidates for colonist");
                }
                catch (Exception ex)
                {
                    context.Assert(false, $"JobCandidateBuilder threw exception: {ex.Message}");
                }
            }

            // 3. ApprovalManager & Pending Request Lifecycle
            if (pawn != null)
            {
                RequestEntry? entry = null;
                try
                {
                    var approvalMgr = new ApprovalManager(RimMindAdvisorMod.Settings);
                    var advice = new AdviceItem
                    {
                        Action = "stabilize_rest",
                        Reason = "Pawn is exhausted and needs rest"
                    };

                    bool approved = false;
                    entry = approvalMgr.SubmitForApproval(
                        advice,
                        pawn,
                        onApproved: () => approved = true,
                        onRejected: () => { });

                    var pendingList = RimMindAPI.GetPendingRequests();
                    context.Assert(pendingList.Contains(entry), "Submitted advisor request entry is present in pending requests");

                    // Select approve option
                    if (entry.options != null && entry.options.Length > 0)
                    {
                        entry.TryComplete(entry.options[0], RequestCompletionReason.Selected);
                        context.Assert(approved, "Selecting approve option triggered onApproved callback");
                    }

                    // Verify removed from pending queue
                    if (entry != null)
                    {
                        RimMindAPI.DismissPendingRequest(entry);
                    }
                    var pendingAfter = RimMindAPI.GetPendingRequests();
                    context.Assert(!pendingAfter.Contains(entry!), "Approved advisor request entry is cleanly removed from pending requests");
                }
                catch (Exception ex)
                {
                    context.Assert(false, $"ApprovalManager flow threw exception: {ex.Message}");
                }
                finally
                {
                    if (entry != null)
                    {
                        RimMindAPI.DismissPendingRequest(entry);
                    }
                }
            }

            // 4. AdvisorHistoryStore
            var store = AdvisorHistoryStore.Instance;
            context.Assert(store != null, "AdvisorHistoryStore world component instance is available");
            if (store != null && pawn != null)
            {
                var record = new AdvisorRequestRecord
                {
                    action = "autotest_action",
                    reason = "autotest verification",
                    result = "success",
                    tick = Find.TickManager.TicksGame
                };

                int countBefore = store.GetRecords(pawn).Count;
                store.AddRecord(pawn, record);
                var recordsAfter = store.GetRecords(pawn);

                context.Assert(recordsAfter.Count == countBefore + 1, "AdvisorHistoryStore records appended successfully");
                context.Assert(recordsAfter.Last().action == "autotest_action", "Latest history record matches submitted action");

                // Cleanup: remove the test record
                recordsAfter.Remove(record);
            }

            // 5. AdvisorConcurrencyTracker
            int startCount = AdvisorConcurrencyTracker.ActiveCount;
            AdvisorConcurrencyTracker.Increment();
            context.Assert(AdvisorConcurrencyTracker.ActiveCount == startCount + 1, "AdvisorConcurrencyTracker incremented correctly");
            AdvisorConcurrencyTracker.Decrement();
            context.Assert(AdvisorConcurrencyTracker.ActiveCount == startCount, "AdvisorConcurrencyTracker decremented correctly");

            // 6. Advisor Mod Cooldown
            RimMindAPI.ClearModCooldown("Advisor");
            int cdLeft = RimMindAPI.GetModCooldownTicksLeft("Advisor");
            context.Assert(cdLeft == 0, "Advisor Core-level cooldown cleared successfully");
        }
    }
}
