using System;
using RimMind.Advisor.Settings;
using RimWorld;
using Verse;

namespace RimMind.Advisor.Advisor
{
    /// <summary>
    /// Calculates dynamic, adaptive cooldowns for Advisor requests based on colony danger and colonist crisis levels.
    /// </summary>
    public static class AdvisorCooldownCalculator
    {
        public const int MinCrisisCooldownTicks = 2500; // 1 in-game hour minimum safety guard

        /// <summary>
        /// Pure calculation of dynamic cooldown based on danger state.
        /// </summary>
        public static int CalculateDynamicCooldown(
            int baseCooldownTicks,
            bool isMapHighDanger,
            bool isPawnInCrisis,
            bool isPawnInExtremeBreakRisk,
            int minCooldownTicks = MinCrisisCooldownTicks)
        {
            int safeMin = Math.Max(600, minCooldownTicks);
            if (baseCooldownTicks <= safeMin)
                return safeMin;

            float multiplier = 1.0f;
            if (isPawnInCrisis)
            {
                multiplier = 0.10f; // ~1.2h for life-threatening crisis
            }
            else if (isPawnInExtremeBreakRisk)
            {
                multiplier = 0.15f; // ~1.8h for extreme mental danger
            }
            else if (isMapHighDanger)
            {
                multiplier = 0.20f; // ~2.4h for colony raid/high danger
            }

            int dynamicCooldown = (int)(baseCooldownTicks * multiplier);
            return Math.Max(safeMin, dynamicCooldown);
        }

        public const float MinActivityCooldownMultiplier = 0.35f;
        public const float MaxActivityCooldownMultiplier = 3.5f;

        /// <summary>
        /// Scales a cooldown by Core's ActivityFrequencyScale (higher scale = shorter cooldown),
        /// clamped to [0.35x, 3.5x]. Non-positive or tiny scales leave the cooldown unchanged.
        /// </summary>
        public static int ApplyActivityFrequencyScale(int cooldownTicks, float activityFrequencyScale)
            => RimMind.Application.Common.Helpers.ActivityCooldownScaler.Scale(cooldownTicks, activityFrequencyScale);

        /// <summary>
        /// The single effective Advisor cooldown: danger-adapted base cooldown, scaled by the
        /// activity frequency, never below the crisis safety floor. Every readiness check (scan,
        /// <c>CanRequestAdvice</c>, gizmo/debug display) must derive from this value.
        /// </summary>
        public static int CalculateEffectiveCooldown(
            int baseCooldownTicks,
            bool isMapHighDanger,
            bool isPawnInCrisis,
            bool isPawnInExtremeBreakRisk,
            float activityFrequencyScale,
            int minCooldownTicks = MinCrisisCooldownTicks)
        {
            int dynamicCooldown = CalculateDynamicCooldown(
                baseCooldownTicks,
                isMapHighDanger,
                isPawnInCrisis,
                isPawnInExtremeBreakRisk,
                minCooldownTicks);
            int scaled = ApplyActivityFrequencyScale(dynamicCooldown, activityFrequencyScale);
            return Math.Max(Math.Max(600, minCooldownTicks), scaled);
        }

        /// <summary>Ticks left until the next request is allowed (0 when ready).</summary>
        public static int CooldownTicksLeft(int effectiveCooldownTicks, int nowTicks, int lastRequestTick)
            => Math.Max(0, effectiveCooldownTicks - (nowTicks - lastRequestTick));

#if !RIMMIND_ADVISOR_TESTS
        /// <summary>
        /// Evaluates pawn and map state to determine the effective cooldown
        /// (including Core's ActivityFrequencyScale).
        /// </summary>
        public static int CalculateCooldownForPawn(Pawn pawn, RimMindAdvisorSettings settings)
        {
            if (settings == null)
                return 30000;

            float activityScale = RimMind.Presentation.Api.RimMindAPI.Settings.ActivityFrequencyScale;
            if (pawn == null)
                return CalculateEffectiveCooldown(settings.requestCooldownTicks, false, false, false, activityScale);

            bool isMapHighDanger = false;
            try
            {
                if (pawn.Map?.dangerWatcher != null)
                {
                    isMapHighDanger = pawn.Map.dangerWatcher.DangerRating == StoryDanger.High;
                }
            }
            catch
            {
                // Defensive fallback for test stubs or edge cases
            }

            bool isPawnInCrisis = false;
            bool isBreakRisk = false;

            try
            {
                if (pawn.health?.hediffSet != null)
                {
                    isPawnInCrisis = pawn.Downed || pawn.health.hediffSet.BleedRateTotal > 0.1f;
                }
                else if (pawn.Downed)
                {
                    isPawnInCrisis = true;
                }

                if (pawn.InMentalState)
                {
                    isPawnInCrisis = true;
                }

                if (pawn.mindState?.mentalBreaker != null && pawn.needs?.mood != null)
                {
                    isBreakRisk = pawn.needs.mood.CurLevel < pawn.mindState.mentalBreaker.BreakThresholdExtreme;
                }
            }
            catch
            {
                // Defensive fallback
            }

            return CalculateEffectiveCooldown(
                settings.requestCooldownTicks,
                isMapHighDanger,
                isPawnInCrisis,
                isBreakRisk,
                activityScale);
        }
#endif
    }
}
