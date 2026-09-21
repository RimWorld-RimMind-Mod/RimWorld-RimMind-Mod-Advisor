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

#if !RIMMIND_ADVISOR_TESTS
        /// <summary>
        /// Evaluates pawn and map state to determine the dynamic cooldown.
        /// </summary>
        public static int CalculateCooldownForPawn(Pawn pawn, RimMindAdvisorSettings settings)
        {
            if (settings == null)
                return 30000;

            if (pawn == null)
                return settings.requestCooldownTicks;

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

            return CalculateDynamicCooldown(
                settings.requestCooldownTicks,
                isMapHighDanger,
                isPawnInCrisis,
                isBreakRisk);
        }
#endif
    }
}
