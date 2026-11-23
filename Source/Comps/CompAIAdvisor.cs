using System.Collections.Generic;
using RimMind.Advisor.Advisor;
using RimMind.Advisor.Settings;
using RimMind.Presentation.Api;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimMind.Advisor.Comps
{
    public class CompAIAdvisor : ThingComp
    {
        public bool IsEnabled = true;

        private int _lastRequestTick = -9999;
        private AdvisorCycleCoordinator? _cycle;

        private Pawn? PawnOrNull => parent as Pawn;
        private RimMindAdvisorSettings Settings => RimMindAdvisorMod.Settings;
        private AdvisorCycleCoordinator Cycle
        {
            get
            {
                if (_cycle == null)
                {
                    _cycle = new AdvisorCycleCoordinator(PawnOrNull!, () => Settings)
                    {
                        LastRequestTick = _lastRequestTick
                    };
                }
                return _cycle;
            }
        }

        public bool HasPendingRequest => _cycle != null && _cycle.HasPendingRequest;
        public int LastRequestTick => _cycle != null ? (_lastRequestTick = _cycle.LastRequestTick) : _lastRequestTick;
        public AdvisorTaskDriver? TaskDriver => _cycle?.TaskDriver;
        public int AdvisorCooldownTicksLeft => PawnOrNull == null
            ? 0
            : (_cycle != null
                ? _cycle.CooldownTicksLeft
                : AdvisorCooldownCalculator.CooldownTicksLeft(
                    AdvisorCooldownCalculator.CalculateCooldownForPawn(PawnOrNull, Settings),
                    Find.TickManager?.TicksGame ?? 0,
                    _lastRequestTick));

        public bool CanRequestAdvice() =>
            PawnOrNull != null && IsEnabled && !HasPendingRequest && IsEligible() && AdvisorCooldownTicksLeft <= 0;

        public bool IsEligible() =>
            PawnOrNull is Pawn pawn &&
            RimMindPawnLookup.IsActiveSpawnedColonist(pawn) &&
            !pawn.Downed &&
            !pawn.InMentalState &&
            !(pawn.drafter?.Drafted ?? false) &&
            pawn.needs?.mood != null;

        public bool IsIdle()
        {
            if (PawnOrNull is not Pawn pawn) return false;
            var job = pawn.jobs?.curJob;
            if (job == null) return true;
            if (job.playerForced) return false;

            var def = job.def;
            return def == JobDefOf.Wait
                || def == JobDefOf.Wait_Wander
                || def == JobDefOf.GotoWander
                || def == JobDefOf.Wait_MaintainPosture;
        }

        public bool IsMoodBelowThreshold()
        {
            if (PawnOrNull is not Pawn pawn) return false;
            var mood = pawn.needs?.mood;
            return mood != null && mood.CurLevelPercentage < Settings.moodThreshold;
        }

        public bool ShouldIdleTrigger() => Settings.enableIdleTrigger && IsIdle();

        public bool ShouldMoodTrigger() => Settings.enableMoodTrigger && IsMoodBelowThreshold();

        public void RequestAdvice(RimMindAdvisorSettings settings)
        {
            if (PawnOrNull == null) return;
            Cycle.RequestAdvice(settings);
        }

        public void ForceRequestAdvice()
        {
            if (PawnOrNull == null) return;
            IsEnabled = true;
            Cycle.ForceRequestAdvice();
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (PawnOrNull == null || !RimMindPawnLookup.IsActiveSpawnedColonist(PawnOrNull)) yield break;

            var label = IsEnabled
                ? RimMindAPI.Text.TrOr("RimMind.Advisor.UI.Gizmo.Enabled", "AI Advisor: On")
                : RimMindAPI.Text.TrOr("RimMind.Advisor.UI.Gizmo.Disabled", "AI Advisor: Off");
            var subLabel = string.Empty;

            if (IsEnabled)
            {
                if (AdvisorCooldownTicksLeft > 0)
                {
                    subLabel = RimMindAPI.Text.TrOr(
                        "RimMind.Advisor.UI.Gizmo.Cooldown",
                        "Cooling down ({0} game hours)",
                        $"{AdvisorCooldownTicksLeft / 2500f:F1}");
                }
                else if (HasPendingRequest)
                {
                    subLabel = RimMindAPI.Text.TrOr("RimMind.Advisor.UI.Gizmo.Waiting", "Waiting for AI response...");
                }
            }

            yield return new Command_Action
            {
                defaultLabel = label,
                defaultDesc = subLabel.NullOrEmpty()
                    ? RimMindAPI.Text.TrOr(
                        "RimMind.Advisor.UI.Gizmo.Desc",
                        "When enabled, AI will choose the next action for this colonist when idle.")
                    : subLabel,
                icon = ContentFinder<Texture2D>.Get("UI/AdvisorIcon", reportFailure: false),
                action = () => IsEnabled = !IsEnabled,
            };

            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = RimMindAPI.Text.TrOr("RimMind.Advisor.UI.Gizmo.ForceRequest", "Request AI Advice Now"),
                    defaultDesc = RimMindAPI.Text.TrOr(
                        "RimMind.Advisor.UI.Gizmo.ForceRequestDesc",
                        "Bypass cooldown and request immediately (dev mode only)"),
                    icon = ContentFinder<Texture2D>.Get("UI/AdvisorIcon", reportFailure: false),
                    action = ForceRequestAdvice,
                };
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            _cycle?.CancelActiveCycle();
            if (PawnOrNull != null)
            {
                ApprovalManager.CancelPendingForPawn(PawnOrNull);
            }
            base.PostDeSpawn(map, mode);
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            _cycle?.CancelActiveCycle();
            if (PawnOrNull != null)
            {
                ApprovalManager.CancelPendingForPawn(PawnOrNull);
            }
            base.PostDestroy(mode, previousMap);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref IsEnabled, "aiAdvisorEnabled", true);
            if (_cycle != null)
            {
                _lastRequestTick = _cycle.LastRequestTick;
            }
            Scribe_Values.Look(ref _lastRequestTick, "aiAdvisorLastRequestTick", -9999);
            if (Scribe.mode == LoadSaveMode.LoadingVars && parent is Pawn pawn)
            {
                if (_lastRequestTick <= 0 && Find.TickManager != null)
                {
                    int stagger = (pawn.thingIDNumber & 0x7FFF) % 1500;
                    _lastRequestTick = Find.TickManager.TicksGame - (Settings.requestCooldownTicks - stagger);
                }
                if (_cycle != null)
                {
                    _cycle.LastRequestTick = _lastRequestTick;
                }
            }
        }
    }
}
