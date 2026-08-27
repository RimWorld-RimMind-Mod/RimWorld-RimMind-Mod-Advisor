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
        public bool IsEnabled = false;

        private AdvisorCycleCoordinator? _cycle;

        private Pawn Pawn => (Pawn)parent;
        private RimMindAdvisorSettings Settings => RimMindAdvisorMod.Settings;
        private AdvisorCycleCoordinator Cycle =>
            _cycle ??= new AdvisorCycleCoordinator(Pawn, () => Settings);

        public bool HasPendingRequest => Cycle.HasPendingRequest;
        public int LastRequestTick => Cycle.LastRequestTick;
        public AdvisorTaskDriver? TaskDriver => Cycle.TaskDriver;
        public int AdvisorCooldownTicksLeft => Cycle.CooldownTicksLeft;

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

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref IsEnabled, "aiAdvisorEnabled", false);
        }
    }
}
