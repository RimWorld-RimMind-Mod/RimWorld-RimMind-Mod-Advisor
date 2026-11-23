using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimMind.Domain.Common;
using RimMind.Presentation.Api;
using RimWorld;
using Verse;

namespace RimMind.Advisor.Perception
{
    public class ColonyMacroSnapshot
    {
        public int ColonistCount { get; set; }
        public int DownedColonistCount { get; set; }
        public float TotalFoodNutrition { get; set; }
        public int WoodCount { get; set; }
        public int SteelCount { get; set; }
        public int MedicineCount { get; set; }
        public float OutdoorTemperature { get; set; }
        public float ColonistMinComfyTemp { get; set; } = -999f;
        public float ColonistMaxComfyTemp { get; set; } = 999f;
        public bool HasHypothermiaColonist { get; set; }
        public bool HasHeatstrokeColonist { get; set; }
        public int UnburiedCorpsesCount { get; set; }
        public int FreeColonistBedCount { get; set; }
        public int HostileThreatCount { get; set; }
        public int GrowingZoneCellsCount { get; set; } = -1;
        public List<string> ActiveColonistRoster { get; set; } = new List<string>();
    }

    public class ColonyMacroState
    {
        public int ColonistCount { get; set; }
        public int DownedColonistCount { get; set; }
        public float TotalFoodNutrition { get; set; }
        public float FoodDaysRemaining { get; set; }
        public int WoodCount { get; set; }
        public int SteelCount { get; set; }
        public int MedicineCount { get; set; }
        public float OutdoorTemperature { get; set; }
        public List<string> ActiveColonistRoster { get; set; } = new List<string>();

        public bool IsFoodCritical { get; set; }
        public bool IsExtremeCold { get; set; }
        public bool IsExtremeHeat { get; set; }
        public bool HasUnburiedCorpses { get; set; }
        public bool HasHomelessColonists { get; set; }
        public bool HasActiveThreat { get; set; }
        public bool IsResourceDepleted { get; set; }

        public List<string> CriticalBottlenecks { get; } = new List<string>();
    }

    public static class ColonyMacroPerceiver
    {
        public static ColonyMacroState Evaluate(ColonyMacroSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            var state = new ColonyMacroState
            {
                ColonistCount = snapshot.ColonistCount,
                DownedColonistCount = snapshot.DownedColonistCount,
                TotalFoodNutrition = snapshot.TotalFoodNutrition,
                WoodCount = snapshot.WoodCount,
                SteelCount = snapshot.SteelCount,
                MedicineCount = snapshot.MedicineCount,
                OutdoorTemperature = snapshot.OutdoorTemperature,
                ActiveColonistRoster = snapshot.ActiveColonistRoster != null
                    ? new List<string>(snapshot.ActiveColonistRoster)
                    : new List<string>()
            };

            float dailyConsumption = Math.Max(1, snapshot.ColonistCount) * 1.6f;
            state.FoodDaysRemaining = snapshot.ColonistCount > 0
                ? (float)Math.Round(snapshot.TotalFoodNutrition / dailyConsumption, 1)
                : 99f;

            state.IsFoodCritical = snapshot.ColonistCount > 0 &&
                (state.FoodDaysRemaining < 3.0f || snapshot.TotalFoodNutrition < 5.0f);

            float coldThreshold = snapshot.ColonistMinComfyTemp > -900f
                ? snapshot.ColonistMinComfyTemp - 20f
                : -15f;
            state.IsExtremeCold = snapshot.OutdoorTemperature < coldThreshold ||
                snapshot.HasHypothermiaColonist;

            float heatThreshold = snapshot.ColonistMaxComfyTemp < 900f
                ? snapshot.ColonistMaxComfyTemp + 15f
                : 45f;
            state.IsExtremeHeat = snapshot.OutdoorTemperature > heatThreshold ||
                snapshot.HasHeatstrokeColonist;

            state.HasUnburiedCorpses = snapshot.UnburiedCorpsesCount > 0;
            state.HasHomelessColonists = snapshot.ColonistCount > snapshot.FreeColonistBedCount;
            state.HasActiveThreat = snapshot.HostileThreatCount > 0;
            state.IsResourceDepleted = (snapshot.WoodCount < 80 || snapshot.SteelCount < 50);

            if (state.IsFoodCritical)
            {
                state.CriticalBottlenecks.Add($"[Food Crisis] Colony only has {state.FoodDaysRemaining:F1} days of food remaining! (Nutrition: {snapshot.TotalFoodNutrition:F1})");
            }
            if (snapshot.GrowingZoneCellsCount >= 0 && snapshot.GrowingZoneCellsCount < 15 && snapshot.ColonistCount > 0)
            {
                state.CriticalBottlenecks.Add("[No Agriculture] Colony has insufficient Growing Zones (<15 cells)! Crops take several days to mature; establish a fertile growing zone promptly before starting rations run out.");
            }
            if (state.IsExtremeCold)
            {
                state.CriticalBottlenecks.Add($"[Extreme Cold] Outdoor temp is {snapshot.OutdoorTemperature:F1}°C! Risk of severe hypothermia.");
            }
            if (state.IsExtremeHeat)
            {
                state.CriticalBottlenecks.Add($"[Extreme Heat] Outdoor temp is {snapshot.OutdoorTemperature:F1}°C! Risk of heatstroke.");
            }
            if (state.HasUnburiedCorpses)
            {
                state.CriticalBottlenecks.Add($"[Unburied Corpses] {snapshot.UnburiedCorpsesCount} unburied corpses are rotting in the colony area!");
            }
            if (state.HasHomelessColonists)
            {
                state.CriticalBottlenecks.Add($"[Shelter Shortage] {snapshot.ColonistCount - snapshot.FreeColonistBedCount} colonists do not have a private bed/room.");
            }
            if (state.HasActiveThreat)
            {
                state.CriticalBottlenecks.Add($"[Hostile Threat] {snapshot.HostileThreatCount} hostile enemies detected on the map!");
            }
            if (state.IsResourceDepleted)
            {
                state.CriticalBottlenecks.Add($"[Resource Depleted] Wood ({snapshot.WoodCount}) or Steel ({snapshot.SteelCount}) is below safe construction reserves (Wood>=80, Steel>=50).");
            }

            return state;
        }

        public static string FormatPromptAugmentation(ColonyMacroState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            var sb = new StringBuilder();
            sb.AppendLine("=== COLONY MACRO STATUS & SURVIVAL PRIORITIES ===");
            sb.AppendLine($"- Colonists: {state.ColonistCount} active (Downed: {state.DownedColonistCount})");
            if (state.ActiveColonistRoster != null && state.ActiveColonistRoster.Count > 0)
            {
                sb.AppendLine($"- Active Colonist Roster: {string.Join(", ", state.ActiveColonistRoster)}");
            }
            sb.AppendLine($"- Food Supply: {state.TotalFoodNutrition:F1} nutrition (~{state.FoodDaysRemaining:F1} days remaining)");
            sb.AppendLine($"- Outdoor Temperature: {state.OutdoorTemperature:F1}°C");
            sb.AppendLine($"- Resources: Wood {state.WoodCount}, Steel {state.SteelCount}, Medicine {state.MedicineCount}");

            if (state.CriticalBottlenecks.Count > 0)
            {
                sb.AppendLine("CRITICAL SURVIVAL BOTTLENECKS (PRIORITIZE THESE ACTIONS!):");
                foreach (var bottleneck in state.CriticalBottlenecks)
                {
                    sb.AppendLine($"* {bottleneck}");
                }
                sb.AppendLine("As the Colony Strategic Advisor, emit multi-tool strategic recommendations addressing these exact bottlenecks!");
            }
            else
            {
                sb.AppendLine("Colony status is currently stable. Focus on long-term development, expansion, crafting, and defense fortification.");
            }

            return sb.ToString().TrimEnd();
        }

        public static bool IsCriticalCorpse(bool isSpawned, bool isFogged, bool isHumanlike, bool isInHomeArea)
        {
            return isSpawned && !isFogged && (isHumanlike || isInHomeArea);
        }

#if !RIMMIND_ADVISOR_TESTS
        public static ColonyMacroSnapshot CaptureSnapshot(Map map)
        {
            var snapshot = new ColonyMacroSnapshot();
            if (map == null) return snapshot;

            try
            {
                var colonists = RimMindPawnLookup.GetSpawnedColonistsSafe(map)
                    .Where(p => RimMindPawnLookup.IsActiveSpawnedColonist(p))
                    .ToList();
                snapshot.ColonistCount = colonists.Count;
                snapshot.DownedColonistCount = colonists.Count(p => p.Downed);
                snapshot.ActiveColonistRoster = colonists
                    .Select(c => $"{c.Name?.ToStringShort ?? c.LabelShort} (pawn_id: {c.thingIDNumber})")
                    .ToList();

                snapshot.TotalFoodNutrition = MapResourceCounter.GetAvailableFoodNutrition(map);

                snapshot.WoodCount = MapResourceCounter.GetAvailableCount(map, ThingDefOf.WoodLog);
                snapshot.SteelCount = MapResourceCounter.GetAvailableCount(map, ThingDefOf.Steel);
                snapshot.MedicineCount = MapResourceCounter.GetAvailableCount(map, ThingDefOf.MedicineIndustrial)
                                       + MapResourceCounter.GetAvailableCount(map, ThingDefOf.MedicineHerbal);

                snapshot.OutdoorTemperature = map.mapTemperature?.OutdoorTemp ?? 21f;

                if (colonists.Count > 0)
                {
                    snapshot.ColonistMinComfyTemp = colonists.Min(p => p.GetStatValue(StatDefOf.ComfyTemperatureMin));
                    snapshot.ColonistMaxComfyTemp = colonists.Max(p => p.GetStatValue(StatDefOf.ComfyTemperatureMax));
                    snapshot.HasHypothermiaColonist = colonists.Any(p => p.health?.hediffSet?.HasHediff(HediffDefOf.Hypothermia) == true);
                    snapshot.HasHeatstrokeColonist = colonists.Any(p => p.health?.hediffSet?.HasHediff(HediffDefOf.Heatstroke) == true);
                }

                var corpses = map.listerThings?.ThingsInGroup(ThingRequestGroup.Corpse);
                if (corpses != null)
                {
                    var homeArea = map.areaManager?.Home;
                    snapshot.UnburiedCorpsesCount = corpses.OfType<Corpse>().Count(c =>
                        c != null && IsCriticalCorpse(
                            c.Spawned,
                            c.Spawned && c.Position.Fogged(map),
                            c.InnerPawn?.RaceProps?.Humanlike == true,
                            c.Spawned && homeArea != null && homeArea[c.Position]));
                }

                if (map.listerBuildings?.allBuildingsColonist != null)
                {
                    snapshot.FreeColonistBedCount = map.listerBuildings.allBuildingsColonist
                        .OfType<Building_Bed>()
                        .Count(b => !b.ForPrisoners && !b.Medical && b.def?.building?.bed_humanlike == true);
                }

                var allSpawned = map.mapPawns?.AllPawnsSpawned;
                if (allSpawned != null)
                {
                    int hostileCount = 0;
                    for (int i = 0; i < allSpawned.Count; i++)
                    {
                        var p = allSpawned[i];
                        if (p != null && !p.Dead && !p.Downed && !p.Position.Fogged(map) && p.HostileTo(Faction.OfPlayer))
                            hostileCount++;
                    }
                    snapshot.HostileThreatCount = hostileCount;
                }

                snapshot.GrowingZoneCellsCount = map.zoneManager?.AllZones
                    ?.OfType<Zone_Growing>()
                    .Sum(z => z.cells?.Count ?? 0) ?? 0;
            }
            catch (Exception ex)
            {
                Log.Warning($"[ColonyMacroPerceiver] Failed to capture full macro snapshot: {ex.Message}");
            }

            return snapshot;
        }
#endif
    }
}
