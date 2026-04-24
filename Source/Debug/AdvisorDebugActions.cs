using System.Linq;
using System.Text;
using LudeonTK;
using RimMind.Advisor.Comps;
using RimMind.Advisor.Concurrency;
using RimMind.Advisor.Advisor;
using RimMind.Core;
using RimMind.Core.Context;
using RimMind.Core.Internal;
using Verse;

namespace RimMind.Advisor.Debug
{
    [StaticConstructorOnStartup]
    public static class AdvisorDebugActions
    {
        [DebugAction("RimMind Advisor", "Show Advisor State (selected)",
            actionType = DebugActionType.Action)]
        private static void ShowAdvisorStateSelected()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (pawn == null)
            {
                Log.Warning("[RimMind-Advisor] Please select a colonist on the map first before opening the Dev menu.");
                return;
            }

            var comp = pawn.GetComp<CompAIAdvisor>();
            if (comp == null)
            {
                Log.Warning($"[RimMind-Advisor] {pawn.Name.ToStringShort} has no CompAIAdvisor (non-humanlike?).");
                return;
            }

            var s = RimMindAdvisorMod.Settings;
            var coreS = RimMindCoreMod.Settings;
            int now = Find.TickManager.TicksGame;
            string requestId = $"Advisor_{pawn.ThingID}";

            int advisorLeft = comp.AdvisorCooldownTicksLeft;
            string advisorCooldown = advisorLeft > 0
                ? $"Cooling down ({advisorLeft} ticks ~ {advisorLeft / 2500f:F2} game hours)"
                : "Ready";

            int coreLeft = AIRequestQueue.Instance?.GetCooldownTicksLeft("Advisor") ?? 0;
            string coreCooldown = coreLeft > 0
                ? $"Cooling down ({coreLeft} ticks)"
                : "Ready";

            var sb = new StringBuilder();
            sb.AppendLine($"[RimMind-Advisor] === {pawn.Name.ToStringShort} Advisor State ===");
            sb.AppendLine($"  Advisor toggle: {(comp.IsEnabled ? "On" : "Off")}");
            sb.AppendLine($"  API configured: {RimMindAPI.IsConfigured()}");
            sb.AppendLine($"  Has pending request: {comp.HasPendingRequest}");
            sb.AppendLine($"  [Advisor Cooldown] {advisorCooldown}  (requestCooldownTicks={s.requestCooldownTicks})");
            sb.AppendLine($"  [Core Cooldown]    {coreCooldown}  (mod cooldown for Advisor)");
            sb.AppendLine($"  Concurrency: {AdvisorConcurrencyTracker.ActiveCount}/{s.maxConcurrentRequests}");
            sb.AppendLine($"  debugLogging: {coreS.debugLogging}");
            Log.Message(sb.ToString());
        }

        [DebugAction("RimMind Advisor", "Force Request Advice (selected)",
            actionType = DebugActionType.Action)]
        private static void ForceRequestForSelected()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (pawn == null)
            {
                Log.Warning("[RimMind-Advisor] Please select a colonist on the map first before opening the Dev menu.");
                return;
            }

            var comp = pawn.GetComp<CompAIAdvisor>();
            if (comp == null)
            {
                Log.Warning($"[RimMind-Advisor] {pawn.Name.ToStringShort} has no CompAIAdvisor (non-humanlike?).");
                return;
            }

            if (!RimMindAPI.IsConfigured())
            {
                Log.Warning("[RimMind-Advisor] API not configured. Please enter API Key in Mod settings.");
                return;
            }

            Log.Message($"[RimMind-Advisor] Sending advisor request for {pawn.Name.ToStringShort}...");
            comp.ForceRequestAdvice();
        }

        [DebugAction("RimMind Advisor", "Show Job Candidates (selected)",
            actionType = DebugActionType.Action)]
        private static void ShowJobCandidates()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (pawn == null)
            {
                Log.Warning("[RimMind-Advisor] Please select a colonist first.");
                return;
            }

            string candidates = JobCandidateBuilder.Build(pawn);
            Log.Message($"[RimMind-Advisor] Job candidates for {pawn.Name.ToStringShort}:\n{candidates}");
        }

        [DebugAction("RimMind Advisor", "Show Full Prompt (selected)",
            actionType = DebugActionType.Action)]
        private static void ShowFullPrompt()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (pawn == null)
            {
                Log.Warning("[RimMind-Advisor] Please select a colonist first.");
                return;
            }

            var npcId = $"NPC-{pawn.ThingID}";
            var request = new ContextRequest
            {
                NpcId = npcId,
                Scenario = ScenarioIds.Decision,
                Budget = 0.5f,
                MaxTokens = 400,
                Temperature = 0.7f,
            };
            var engine = new ContextEngine(HistoryManager.Instance);
            var snapshot = engine.BuildSnapshot(request);
            var sysMsgs = snapshot.Messages.Where(m => m.Role == "system").Select(m => m.Content);
            var userMsgs = snapshot.Messages.Where(m => m.Role == "user").Select(m => m.Content);
            Log.Message($"[RimMind-Advisor] === System Prompt ===\n{string.Join("\n---\n", sysMsgs)}\n\n=== User Prompt ===\n{string.Join("\n", userMsgs)}");
        }

        [DebugAction("RimMind Advisor", "List All Advisor States",
            actionType = DebugActionType.Action)]
        private static void ListAllAdvisorStates()
        {
            var map = Find.CurrentMap;
            if (map == null)
            {
                Log.Warning("[RimMind-Advisor] No map available.");
                return;
            }

            var coreS = RimMindCoreMod.Settings;
            var advS = RimMindAdvisorMod.Settings;
            var sb = new StringBuilder("=== All Advisor States ===\n");
            foreach (var pawn in map.mapPawns.FreeColonists)
            {
                var comp = pawn.GetComp<CompAIAdvisor>();
                if (comp == null) continue;

                int advisorLeft = comp.AdvisorCooldownTicksLeft;
                int coreLeft = AIRequestQueue.Instance?.GetCooldownTicksLeft("Advisor") ?? 0;
                string aState = advisorLeft > 0 ? $"AdvisorCD{advisorLeft}t" : "AdvisorReady";
                string cState = coreLeft > 0 ? $"CoreCD{coreLeft}t" : "CoreReady";

                sb.AppendLine($"  {pawn.Name.ToStringShort}: toggle={comp.IsEnabled}  pending={comp.HasPendingRequest}  [{aState}]  [{cState}]");
            }
            sb.AppendLine($"Concurrency: {AdvisorConcurrencyTracker.ActiveCount}/{advS.maxConcurrentRequests}  " +
                          $"API={RimMindAPI.IsConfigured()}  debugLogging={coreS.debugLogging}");
            Log.Message(sb.ToString());
        }

        [DebugAction("RimMind Advisor", "Clear ALL Cooldowns",
            actionType = DebugActionType.Action)]
        private static void ClearAllCooldowns()
        {
            AIRequestQueue.Instance?.ClearAllCooldowns();
            Log.Message("[RimMind-Advisor] All Core-layer cooldowns cleared. To clear Advisor-layer cooldowns, use Force Request Advice on each colonist.");
        }

        [DebugAction("RimMind Advisor", "Reset Concurrency Count",
            actionType = DebugActionType.Action)]
        private static void ResetConcurrency()
        {
            while (AdvisorConcurrencyTracker.ActiveCount > 0)
                AdvisorConcurrencyTracker.Decrement();
            Log.Message("[RimMind-Advisor] Concurrency count reset to 0.");
        }
    }
}
