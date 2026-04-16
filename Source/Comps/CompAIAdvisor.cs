using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using RimMind.Actions;
using RimMind.Advisor.Advisor;
using RimMind.Advisor.Concurrency;
using RimMind.Advisor.Data;
using RimMind.Advisor.Settings;
using RimMind.Core;
using RimMind.Core.Client;
using RimMind.Core.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimMind.Advisor.Comps
{
    public class CompAIAdvisor : ThingComp
    {
        public bool IsEnabled = false;

        private bool    _hasPendingRequest;
        private int     _lastRequestTick = -9999;
        private string? _pendingReason;

        public bool HasPendingRequest => _hasPendingRequest;
        public int  AdvisorCooldownTicksLeft =>
            System.Math.Max(0, Settings.requestCooldownTicks - (Find.TickManager.TicksGame - _lastRequestTick));

        private Pawn Pawn => (Pawn)parent;
        private RimMindAdvisorSettings Settings => RimMindAdvisorMod.Settings;
        private bool DebugLogging => RimMind.Core.RimMindCoreMod.Settings.debugLogging;

        public override void CompTick()
        {
            if (Pawn.Map == null) return;

            var s = Settings;
            if (!Pawn.IsHashIntervalTick(s.pawnScanIntervalTicks)) return;

            if (!s.enableAdvisor)
            {
                return;
            }
            if (!RimMindAPI.IsConfigured())
            {
                DebugSkip("API not configured");
                return;
            }
            if (_hasPendingRequest)
            {
                DebugSkip("Pending request exists");
                return;
            }
            if (!IsEnabled)
            {
                DebugSkip("Advisor toggle off for this colonist");
                return;
            }
            if (!IsEligible())
            {
                DebugSkip("Colonist ineligible (dead/slave/drafted/no mood)");
                return;
            }

            bool idleTriggered = s.enableIdleTrigger && IsIdle();
            bool moodTriggered = s.enableMoodTrigger && IsMoodBelowThreshold();

            if (!idleTriggered && !moodTriggered)
            {
                DebugSkip($"No trigger met (idle={idleTriggered}, mood={moodTriggered})");
                return;
            }

            int ticksGame = Find.TickManager.TicksGame;
            int advisorCooldownLeft = s.requestCooldownTicks - (ticksGame - _lastRequestTick);
            if (advisorCooldownLeft > 0)
            {
                if (DebugLogging)
                    Log.Message($"[RimMind-Advisor][Advisor Cooldown] {Pawn.Name.ToStringShort} cooling down, {advisorCooldownLeft} ticks remaining (~{advisorCooldownLeft / 2500f:F2} game hours). requestCooldownTicks={s.requestCooldownTicks}");
                return;
            }

            if (AdvisorConcurrencyTracker.ActiveCount >= s.maxConcurrentRequests)
            {
                if (DebugLogging)
                    Log.Message($"[RimMind-Advisor][Concurrency Limit] Current concurrent {AdvisorConcurrencyTracker.ActiveCount}/{s.maxConcurrentRequests}, {Pawn.Name.ToStringShort} skipped.");
                return;
            }

            RequestAIAdvice();
        }

        private void DebugSkip(string reason)
        {
            if (DebugLogging)
                Log.Message($"[RimMind-Advisor][Skip] {Pawn.Name.ToStringShort}: {reason}");
        }

        private bool IsEligible() =>
            Pawn.IsFreeNonSlaveColonist &&
            !Pawn.Dead &&
            !(Pawn.drafter?.Drafted ?? false) &&
            Pawn.needs?.mood != null;

        private bool IsIdle()
        {
            var job = Pawn.jobs?.curJob;
            if (job == null) return true;
            if (job.playerForced) return false;

            var def = job.def;
            return def == JobDefOf.Wait
                || def == JobDefOf.Wait_Wander
                || def == JobDefOf.GotoWander
                || def == JobDefOf.Wait_MaintainPosture;
        }

        private bool IsMoodBelowThreshold()
        {
            var mood = Pawn.needs?.mood;
            if (mood == null) return false;
            return mood.CurLevelPercentage < Settings.moodThreshold;
        }

        private void RequestAIAdvice()
        {
            _hasPendingRequest = true;
            AdvisorConcurrencyTracker.Increment();

            var request = new AIRequest
            {
                SystemPrompt = AdvisorPromptBuilder.BuildSystemPrompt(Pawn),
                UserPrompt   = AdvisorPromptBuilder.BuildUserPrompt(Pawn),
                MaxTokens    = 400,
                Temperature  = 0.7f,
                RequestId    = $"Advisor_{Pawn.ThingID}",
                ModId        = "Advisor",
                ExpireAtTicks = Find.TickManager.TicksGame + Settings.requestExpireTicks,
                UseJsonMode  = true,
            };

            RimMindAPI.RequestAsync(request, OnAdviceReceived);
        }

        public void ForceRequestAdvice()
        {
            if (_hasPendingRequest)
            {
                Log.Warning($"[RimMind-Advisor] ForceRequest: {Pawn.Name.ToStringShort} already has a pending request, skipping.");
                return;
            }

            IsEnabled        = true;
            _lastRequestTick = -9999;

            RimMind.Core.Internal.AIRequestQueue.Instance?.ClearCooldown("Advisor");
            Log.Message($"[RimMind-Advisor] ForceRequest: Core-layer cooldown cleared (Advisor), sending request...");

            RequestAIAdvice();
        }

        private void OnAdviceReceived(AIResponse response)
        {
            _hasPendingRequest = false;
            AdvisorConcurrencyTracker.Decrement();

            if (!response.Success)
            {
                Log.Warning($"[RimMind-Advisor] Request failed for {Pawn.Name.ToStringShort}: {response.Error}");
                return;
            }

            AdviceBatch? batch;
            try
            {
                batch = JsonConvert.DeserializeObject<AdviceBatch>(response.Content);
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[RimMind-Advisor] JSON parse failed for {Pawn.Name.ToStringShort}: {ex.Message}\nContent: {response.Content}");
                return;
            }

            if (batch?.advices == null || batch.advices.Count == 0)
            {
                Log.Warning($"[RimMind-Advisor] No valid advice for {Pawn.Name.ToStringShort}");
                return;
            }

            var supported = new HashSet<string>(RimMindActionsAPI.GetSupportedIntents());
            var intents   = new List<BatchActionIntent>();

            foreach (var advice in batch.advices)
            {
                if (advice.action.NullOrEmpty() || !supported.Contains(advice.action)) continue;
                if (!RimMindActionsAPI.IsAllowed(advice.action)) continue;

                var riskLevel = RimMindActionsAPI.GetRiskLevel(advice.action);
                bool systemBlocked = Settings.enableRiskApproval
                    && riskLevel.HasValue
                    && riskLevel.Value >= Settings.autoBlockRiskLevel;

                bool needsApproval = systemBlocked
                    || advice.request_type == "request"
                    || advice.request_type == "high_risk";

                Pawn actor = Pawn;
                if (!advice.pawn.NullOrEmpty())
                {
                    actor = Pawn.Map?.mapPawns.FreeColonists
                        .FirstOrDefault(p => p.Name.ToStringShort == advice.pawn)
                        ?? Pawn;
                }

                Pawn? targetPawn = null;
                if (!advice.target.NullOrEmpty())
                    targetPawn = Pawn.Map?.mapPawns.AllPawns
                        .FirstOrDefault(p => p.Name.ToStringShort == advice.target);

                if (needsApproval && Settings.enableRequestSystem)
                {
                    string title = advice.request_type == "request"
                        ? "RimMind.Advisor.Request.ColonistRequest".Translate(actor.Name.ToStringShort)
                        : "RimMind.Advisor.Request.RiskAction".Translate(advice.action);

                    var capturedActor = actor;
                    var capturedTarget = targetPawn;
                    var capturedAdvice = advice;

                    string approveLabel = "RimMind.Advisor.Request.Approve".Translate();
                    string rejectLabel  = "RimMind.Advisor.Request.Reject".Translate();
                    string ignoreLabel  = "RimMind.Advisor.Request.Ignore".Translate();

                    var entry = new RequestEntry
                    {
                        source = "advisor",
                        pawn = capturedActor,
                        title = title,
                        description = capturedAdvice.reason,
                        systemBlocked = systemBlocked,
                        options = systemBlocked
                            ? new[] { approveLabel, rejectLabel }
                            : new[] { approveLabel, rejectLabel, ignoreLabel },
                        callback = choice =>
                        {
                            string result;
                            if (choice == approveLabel)
                            {
                                var singleIntent = new List<BatchActionIntent>
                                {
                                    new BatchActionIntent
                                    {
                                        IntentId = capturedAdvice.action,
                                        Actor = capturedActor,
                                        Target = capturedTarget,
                                        Param = capturedAdvice.param,
                                        Reason = capturedAdvice.reason,
                                    }
                                };
                                int executed = RimMindActionsAPI.ExecuteBatch(singleIntent);
                                result = "approved";

                                if (executed > 0 && capturedActor.Map != null)
                                {
                                    string moteText = !capturedAdvice.reason.NullOrEmpty()
                                        ? $"[RimMind] {capturedAdvice.reason}"
                                        : $"[RimMind] {capturedAdvice.action}";
                                    MoteMaker.ThrowText(
                                        capturedActor.DrawPos,
                                        capturedActor.Map,
                                        moteText,
                                        new Color(0.4f, 1f, 0.6f),
                                        5f);
                                }
                            }
                            else if (choice == rejectLabel)
                            {
                                result = systemBlocked ? "system_blocked" : "rejected";
                            }
                            else
                            {
                                result = "ignored";
                            }

                            var historyStore = AdvisorHistoryStore.Instance;
                            if (historyStore != null)
                            {
                                historyStore.AddRecord(capturedActor, new AdvisorRequestRecord
                                {
                                    action = capturedAdvice.action,
                                    reason = capturedAdvice.reason ?? "",
                                    result = result,
                                    tick = Find.TickManager.TicksGame,
                                });
                            }
                        }
                    };
                    RimMindAPI.RegisterPendingRequest(entry);
                }
                else
                {
                    intents.Add(new BatchActionIntent
                    {
                        IntentId = advice.action,
                        Actor = actor,
                        Target = targetPawn,
                        Param = advice.param,
                        Reason = advice.reason,
                    });
                }
            }

            if (intents.Count == 0) return;

            int count = RimMindActionsAPI.ExecuteBatch(intents);
            Log.Message($"[RimMind-Advisor] Executed {count}/{intents.Count} actions for {Pawn.Name.ToStringShort}");

            if (Settings.showThoughtBubble && Pawn.Map != null)
            {
                var reasons = new System.Collections.Generic.List<string>();
                foreach (var intent in intents)
                    if (!intent.Reason.NullOrEmpty()) reasons.Add(intent.Reason!);

                _pendingReason = reasons.Count > 0 ? reasons[0] : null;

                if (reasons.Count > 0)
                {
                    string moteText;
                    if (reasons.Count == 1)
                        moteText = $"[RimMind] {reasons[0]}";
                    else if (reasons.Count == 2)
                        moteText = $"[RimMind] {reasons[0]} / {reasons[1]}";
                    else
                        moteText = $"[RimMind] {reasons[0]} / {reasons[1]}" + "RimMind.Advisor.UI.MoteMore".Translate(reasons.Count - 2);

                    MoteMaker.ThrowText(
                        Pawn.DrawPos,
                        Pawn.Map,
                        moteText,
                        new Color(0.6f, 0.9f, 1f),
                        5f);
                }
            }

            _lastRequestTick = Find.TickManager.TicksGame;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            string label    = IsEnabled ? "RimMind.Advisor.UI.Gizmo.Enabled".Translate() : "RimMind.Advisor.UI.Gizmo.Disabled".Translate();
            string subLabel = "";

            if (IsEnabled)
            {
                int cooldownLeft = Settings.requestCooldownTicks - (Find.TickManager.TicksGame - _lastRequestTick);
                if (cooldownLeft > 0)
                    subLabel = "RimMind.Advisor.UI.Gizmo.Cooldown".Translate($"{cooldownLeft / 2500f:F1}");
                else if (_hasPendingRequest)
                    subLabel = "RimMind.Advisor.UI.Gizmo.Waiting".Translate();
            }

            yield return new Command_Action
            {
                defaultLabel = label,
                defaultDesc  = subLabel.NullOrEmpty()
                    ? "RimMind.Advisor.UI.Gizmo.Desc".Translate()
                    : subLabel,
                icon   = ContentFinder<Texture2D>.Get("UI/AdvisorIcon", reportFailure: false),
                action = () => IsEnabled = !IsEnabled,
            };

            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "RimMind.Advisor.UI.Gizmo.ForceRequest".Translate(),
                    defaultDesc  = "RimMind.Advisor.UI.Gizmo.ForceRequestDesc".Translate(),
                    icon   = ContentFinder<Texture2D>.Get("UI/AdvisorIcon", reportFailure: false),
                    action = ForceRequestAdvice,
                };
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref IsEnabled, "aiAdvisorEnabled", false);
        }
    }
}
