using RimMind.Advisor.Comps;
using RimMind.Advisor.Concurrency;
using RimMind.Advisor.Settings;
using RimMind.Presentation.Api;
using Verse;

namespace RimMind.Advisor.Advisor
{
    public class AdvisorGameComponent : GameComponent
    {
        public static AdvisorGameComponent? Instance { get; private set; }

        private int _lastTick;

        public AdvisorGameComponent() : base() { }
        public AdvisorGameComponent(Game game)
        {
            Instance = this;
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            Instance = this;
            AdvisorConcurrencyTracker.Reset();
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            var settings = RimMindAdvisorMod.Settings;
            if (!settings.enableAdvisor) return;
            if (!RimMindAPI.IsConfigured()) return;

            int now = Find.TickManager.TicksGame;
            int interval = settings.pawnScanIntervalTicks;
            if (now < _lastTick + interval) return;
            _lastTick = now;

            EvaluateAllPawns(settings);
        }

        private void EvaluateAllPawns(RimMindAdvisorSettings settings)
        {
            var capacity = new AdvisorRequestCapacity(
                AdvisorConcurrencyTracker.ActiveCount,
                settings.maxConcurrentRequests);

            foreach (var map in Find.Maps)
            {
                foreach (var pawn in map.mapPawns.FreeColonists)
                {
                    if (capacity.Remaining == 0) return;

                    var comp = pawn.GetComp<CompAIAdvisor>();
                    if (comp == null || !comp.IsEligible() || comp.HasPendingRequest || !comp.IsEnabled) continue;
                    if (RimMindAPI.IsAgentActive(pawn.ThingID)) continue;

                    bool shouldTrigger = comp.ShouldIdleTrigger() || comp.ShouldMoodTrigger();
                    if (!shouldTrigger) continue;

                    if (Rand.Value > settings.macroCheckChance) continue;
                    float scale = RimMindAPI.Settings.ActivityFrequencyScale;
                    float effectiveChance = UnityEngine.Mathf.Clamp(settings.macroCheckChance * scale, 0.05f, 0.95f);
                    if (Rand.Value > effectiveChance) continue;

                    int ticksGame = Find.TickManager.TicksGame;
                    int dynamicCooldown = AdvisorCooldownCalculator.CalculateCooldownForPawn(pawn, settings);
                    float cdMult = scale > 0.01f ? (1.0f / scale) : 1.0f;
                    dynamicCooldown = UnityEngine.Mathf.RoundToInt(dynamicCooldown * UnityEngine.Mathf.Clamp(cdMult, 0.35f, 3.5f));
                    int advisorCooldownLeft = dynamicCooldown - (ticksGame - comp.LastRequestTick);
                    if (advisorCooldownLeft > 0) continue;

                    if (!capacity.TryReserve()) return;
                    comp.RequestAdvice(settings);
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _lastTick, "lastTick");
        }
    }
}
