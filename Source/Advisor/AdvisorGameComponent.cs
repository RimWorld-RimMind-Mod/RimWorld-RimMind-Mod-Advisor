using RimMind.Advisor.Comps;
using RimMind.Advisor.Concurrency;
using RimMind.Advisor.Settings;
using RimMind.Core;
using RimMind.Core.Comps;
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
            var activeCount = AdvisorConcurrencyTracker.ActiveCount;
            int maxConcurrent = settings.maxConcurrentRequests;

            foreach (var map in Find.Maps)
            {
                foreach (var pawn in map.mapPawns.FreeColonists)
                {
                    if (activeCount >= maxConcurrent) return;

                    var comp = pawn.GetComp<CompAIAdvisor>();
                    if (comp == null || !comp.IsEligible() || comp.HasPendingRequest || !comp.IsEnabled) continue;
                    if (CompPawnAgent.IsAgentActive(pawn)) continue;

                    bool shouldTrigger = comp.ShouldIdleTrigger() || comp.ShouldMoodTrigger();
                    if (!shouldTrigger) continue;

                    int ticksGame = Find.TickManager.TicksGame;
                    int advisorCooldownLeft = settings.requestCooldownTicks - (ticksGame - comp.LastRequestTick);
                    if (advisorCooldownLeft > 0) continue;

                    activeCount++;
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
