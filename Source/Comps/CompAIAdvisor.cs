using System.Collections.Generic;
using RimMind.Advisor.Advisor;
using RimMind.Advisor.Settings;
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

        private Pawn Pawn => (Pawn)parent;
        private RimMindAdvisorSettings Settings => RimMindAdvisorMod.Settings;
        private AdvisorCycleCoordinator Cycle
        {
            get
            {
                if (_cycle == null)
                {
                    _cycle = new AdvisorCycleCoordinator(Pawn, () => Settings)
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
        public int AdvisorCooldownTicksLeft => _cycle != null
            ? _cycle.CooldownTicksLeft
            : System.Math.Max(0, AdvisorCooldownCalculator.CalculateCooldownForPawn(Pawn, Settings) - ((Find.TickManager?.TicksGame ?? 0) - _lastRequestTick));

        public bool CanRequestAdvice() =>
            IsEnabled && !HasPendingRequest && IsEligible() && AdvisorCooldownTicksLeft <= 0;

        public bool IsEligible() =>
            Pawn.IsFreeNonSlaveColonist &&
            !Pawn.Dead &&
            !(Pawn.drafter?.Drafted ?? false) &&
            Pawn.needs?.mood != null;

        public bool IsIdle()
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

        public bool IsMoodBelowThreshold()
        {
            var mood = Pawn.needs?.mood;
            return mood != null && mood.CurLevelPercentage < Settings.moodThreshold;
        }

        public bool ShouldIdleTrigger() => Settings.enableIdleTrigger && IsIdle();

        public bool ShouldMoodTrigger() => Settings.enableMoodTrigger && IsMoodBelowThreshold();

        public void RequestAdvice(RimMindAdvisorSettings settings) => Cycle.RequestAdvice(settings);

        public void ForceRequestAdvice()
        {
            IsEnabled = true;
            Cycle.ForceRequestAdvice();
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            var label = IsEnabled
                ? "RimMind.Advisor.UI.Gizmo.Enabled".Translate()
                : "RimMind.Advisor.UI.Gizmo.Disabled".Translate();
            var subLabel = string.Empty;

            if (IsEnabled)
            {
                if (AdvisorCooldownTicksLeft > 0)
                {
                    subLabel = "RimMind.Advisor.UI.Gizmo.Cooldown".Translate(
                        $"{AdvisorCooldownTicksLeft / 2500f:F1}");
                }
                else if (HasPendingRequest)
                {
                    subLabel = "RimMind.Advisor.UI.Gizmo.Waiting".Translate();
                }
            }

            yield return new Command_Action
            {
                defaultLabel = label,
                defaultDesc = subLabel.NullOrEmpty()
                    ? "RimMind.Advisor.UI.Gizmo.Desc".Translate()
                    : subLabel,
                icon = ContentFinder<Texture2D>.Get("UI/AdvisorIcon", reportFailure: false),
                action = () => IsEnabled = !IsEnabled,
            };

            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "RimMind.Advisor.UI.Gizmo.ForceRequest".Translate(),
                    defaultDesc = "RimMind.Advisor.UI.Gizmo.ForceRequestDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/AdvisorIcon", reportFailure: false),
                    action = ForceRequestAdvice,
                };
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            ApprovalManager.CancelPendingForPawn(Pawn);
            base.PostDeSpawn(map, mode);
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            ApprovalManager.CancelPendingForPawn(Pawn);
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
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (_lastRequestTick <= 0 && Find.TickManager != null)
                {
                    int stagger = (Pawn.thingIDNumber & 0x7FFF) % 1500;
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
