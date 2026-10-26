using RimMind.Advisor.Comps;
using RimMind.Advisor.Concurrency;
using RimMind.Advisor.Settings;
using RimMind.Presentation.Api;
using RimWorld;
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
            ApprovalManager.ClearPendingRequests();
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            Instance = this;
            AdvisorConcurrencyTracker.Reset();
            ApprovalManager.ClearPendingRequests();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            Instance = this;
            AdvisorConcurrencyTracker.Reset();
            ApprovalManager.ClearPendingRequests();
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
                bool hasCrisisAlert = HasColonyCrisisAlert(map);
                bool hasDistressedAnimals = HasDistressedColonyAnimals(map);

                foreach (var pawn in RimMindPawnLookup.GetEligibleColonists(map))
                {
                    if (capacity.Remaining == 0) return;

                    var comp = pawn.GetComp<CompAIAdvisor>();
                    if (comp == null || !comp.IsEligible() || comp.HasPendingRequest || !comp.IsEnabled) continue;
                    if (RimMindAPI.IsAgentActive(pawn.ThingID)) continue;

                    bool shouldTrigger = comp.ShouldIdleTrigger() || comp.ShouldMoodTrigger();
                    if (!shouldTrigger && comp.IsIdle() && hasDistressedAnimals)
                    {
                        shouldTrigger = true;
                    }
                    if (!shouldTrigger && hasCrisisAlert)
                    {
                        shouldTrigger = true;
                    }
                    if (!shouldTrigger) continue;

                    float scale = RimMindAPI.Settings.ActivityFrequencyScale;
                    int ticksGame = Find.TickManager.TicksGame;
                    int dynamicCooldown = AdvisorCooldownCalculator.CalculateCooldownForPawn(pawn, settings);
                    float cdMult = scale > 0.01f ? (1.0f / scale) : 1.0f;
                    dynamicCooldown = UnityEngine.Mathf.RoundToInt(dynamicCooldown * UnityEngine.Mathf.Clamp(cdMult, 0.35f, 3.5f));
                    int advisorCooldownLeft = dynamicCooldown - (ticksGame - comp.LastRequestTick);
                    if (advisorCooldownLeft > 0 || !comp.CanRequestAdvice()) continue;

                    float effectiveChance = UnityEngine.Mathf.Clamp(settings.macroCheckChance * scale, 0.05f, 0.95f);
                    if (Rand.Value > effectiveChance) continue;

                    if (!capacity.TryReserve()) return;
                    comp.RequestAdvice(settings);
                }
            }
        }

        private static bool HasDistressedColonyAnimals(Map map)
        {
            var animals = RimMindPawnLookup.GetActiveSpawnedColonyAnimals(map);
            if (animals == null) return false;
            foreach (var animal in animals)
            {
                if (animal.Downed) return true;
                if (animal.health?.HasHediffsNeedingTend() == true) return true;
                if (animal.needs?.food?.CurCategory >= HungerCategory.UrgentlyHungry) return true;
            }
            return false;
        }

        private static readonly System.Reflection.FieldInfo? _activeAlertsField =
            HarmonyLib.AccessTools.Field(typeof(AlertsReadout), "activeAlerts");

        private static bool HasColonyCrisisAlert(Map map)
        {
            try
            {
                if (Find.UIRoot is UIRoot_Play playUi && playUi.alerts != null)
                {
                    if (_activeAlertsField?.GetValue(playUi.alerts) is System.Collections.Generic.List<Alert> activeAlerts)
                    {
                        for (int i = 0; i < activeAlerts.Count; i++)
                        {
                            var alert = activeAlerts[i];
                            if (alert == null || alert is Alert_ColonistsIdle) continue;
                            if (alert.Priority == AlertPriority.Critical || alert.Priority == AlertPriority.High)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch (System.Exception)
            {
                // Silently ignore during uninitialized states
            }
            return false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _lastTick, "lastTick");
        }
    }
}
