using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using RimMind.Advisor.Data;
using RimMind.Advisor.Settings;
using RimMind.Advisor.Advisor;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using RimMind.Application.Api;
using RimMind.Presentation.Settings;
using RimMind.Presentation.UI;
using UnityEngine;
using Verse;

namespace RimMind.Advisor
{
    public class RimMindAdvisorMod : Mod
    {
        public static RimMindAdvisorSettings Settings = null!;
        private static Vector2 _scrollPos = Vector2.zero;

        public RimMindAdvisorMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RimMindAdvisorSettings>();
            new Harmony("mcocdaa.RimMindAdvisor").PatchAll();

            RimMindAPI.Extensions<ISettingsTab>().Register(new AdvisorSettingsTab(this));
            RimMindAPI.Extensions<IToggleBehavior>().Register(new AdvisorToggleBehavior(Settings));
            RimMindAPI.Extensions<IModCooldown>().Register(new AdvisorModCooldown(Settings));
            RimMindAPI.Extensions<ISkipCheck>().Register(new AdvisorActionSkipCheck());

            RimMindAPI.Context.ContextKeys.Register(new ContextProviderDef(
                "advisor_history", ContextLayer.L4_History, 0.8f,
                async (ctx, ct) =>
                {
                    if (ctx.PawnId <= 0) return null;
                    var pawn = Find.WorldPawns.AllPawnsAlive.FirstOrDefault(p => p.thingIDNumber == ctx.PawnId)
                        ?? Find.CurrentMap?.mapPawns?.FreeColonists.FirstOrDefault(p => p.thingIDNumber == ctx.PawnId);
                    if (pawn == null) return null;
                    var historyStore = AdvisorHistoryStore.Instance;
                    if (historyStore == null) return null;
                    var records = historyStore.GetRecords(pawn);
                    if (records.Count == 0) return null;
                    var recent = records.Skip(System.Math.Max(0, records.Count - 5)).ToList();
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("RimMind.Advisor.Prompt.RecentHistory".Translate());
                    foreach (var r in recent)
                    {
                        string resultLabel = r.result switch
                        {
                            "approved" => "RimMind.Advisor.Prompt.ResultApproved".Translate(),
                            "rejected" => "RimMind.Advisor.Prompt.ResultRejected".Translate(),
                            "system_blocked" => "RimMind.Advisor.Prompt.ResultBlocked".Translate(),
                            _ => "RimMind.Advisor.Prompt.ResultIgnored".Translate()
                        };
                        sb.AppendLine($"- {r.action}: {r.reason} → {resultLabel}");
                    }
                    return sb.ToString().TrimEnd();
                }, "RimMind.Advisor", stalenessTicks: 3000, invalidationTriggers: new[] { "AdvisorEvent" }));

            RimMindAPI.Context.ContextKeys.Register(new ContextProviderDef(
                "actions_list", ContextLayer.L3_State, 0.85f,
                async (ctx, ct) =>
                {
                    if (ctx.Scenario != RimMindAPI.Context.ScenarioDecision) return null;
                    if (ctx.PawnId <= 0) return null;
                    var pawn = Find.WorldPawns.AllPawnsAlive.FirstOrDefault(p => p.thingIDNumber == ctx.PawnId)
                        ?? Find.CurrentMap?.mapPawns?.FreeColonists.FirstOrDefault(p => p.thingIDNumber == ctx.PawnId);
                    var text = BuildToolListText();
                    return string.IsNullOrEmpty(text) ? null : text;
                }, "RimMind.Advisor", stalenessTicks: 750, invalidationTriggers: new[] { "AdvisorEvent" }));

            RimMindAPI.Context.ContextKeys.Register(new ContextProviderDef(
                "advisor_task", ContextLayer.L0_Static, 0.95f,
                async (ctx, ct) =>
                {
                    if (ctx.Scenario != RimMindAPI.Context.ScenarioDecision) return null;
                    var instruction = string.Join("\n\n", new[] { "Role", "Goal", "Process", "Constraint", "Output", "FieldRules", "OutputRules", "RiskControl", "DiversityHint", "RequestRules", "Example" }
                        .Select(k => (string)$"RimMind.Advisor.Prompt.TaskInstruction.{k}".Translate())
                        .Where(t => !string.IsNullOrEmpty(t)));
                    return instruction;
                }, "RimMind.Advisor", stalenessTicks: 0, invalidationTriggers: new[] { "AdvisorEvent" }));

            Log.Message("[RimMind-Advisor] Initialized.");
        }

        public override string SettingsCategory() => "RimMind - Advisor";

        public override void DoSettingsWindowContents(Rect rect)
        {
            DrawSettingsContent(rect);
        }

        internal static void DrawSettingsContent(Rect inRect)
        {
            Rect contentArea = SettingsUIDrawer.SplitContentArea(inRect);
            Rect bottomBar = SettingsUIDrawer.SplitBottomBar(inRect);

            float contentH = EstimateHeight();
            Rect viewRect = new Rect(0f, 0f, contentArea.width - 16f, contentH);
            Widgets.BeginScrollView(contentArea, ref _scrollPos, viewRect);

            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            listing.CheckboxLabeled("RimMind.Advisor.Settings.EnableAdvisor".Translate(), ref Settings.enableAdvisor,
                "RimMind.Advisor.Settings.EnableAdvisor.Desc".Translate());
            listing.CheckboxLabeled(
                "RimMind.Advisor.Settings.EnableLegacyJsonFallback".Translate(),
                ref Settings.enableLegacyJsonFallback,
                "RimMind.Advisor.Settings.EnableLegacyJsonFallback.Desc".Translate());

            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Advisor.Settings.TriggerSources".Translate());
            listing.CheckboxLabeled("RimMind.Advisor.Settings.EnableIdleTrigger".Translate(), ref Settings.enableIdleTrigger,
                "RimMind.Advisor.Settings.EnableIdleTrigger.Desc".Translate());
            if (Settings.enableIdleTrigger)
            {
                string scanTicks = $"{Settings.pawnScanIntervalTicks}";
                string scanSecs = $"{Settings.pawnScanIntervalTicks / 60f:F1}";
                listing.Label("  " + "RimMind.Advisor.Settings.PawnScanInterval".Translate(scanTicks, scanSecs));
                GUI.color = Color.gray;
                listing.Label("    " + "RimMind.Advisor.Settings.PawnScanInterval.Desc".Translate());
                GUI.color = Color.white;
                Settings.pawnScanIntervalTicks = (int)listing.Slider(Settings.pawnScanIntervalTicks, 600f, 6000f);
                Settings.pawnScanIntervalTicks = (Settings.pawnScanIntervalTicks / 100) * 100;
            }
            listing.CheckboxLabeled("RimMind.Advisor.Settings.EnableMoodTrigger".Translate(), ref Settings.enableMoodTrigger,
                "RimMind.Advisor.Settings.EnableMoodTrigger.Desc".Translate());
            if (Settings.enableMoodTrigger)
            {
                string moodPct = $"{Settings.moodThreshold * 100:F0}";
                listing.Label("  " + "RimMind.Advisor.Settings.MoodThreshold".Translate(moodPct));
                GUI.color = Color.gray;
                listing.Label("    " + "RimMind.Advisor.Settings.MoodThreshold.Desc".Translate());
                GUI.color = Color.white;
                Settings.moodThreshold = listing.Slider(Settings.moodThreshold, 0.25f, 0.6f);
            }
            if (!Settings.enableIdleTrigger && !Settings.enableMoodTrigger)
            {
                GUI.color = Color.yellow;
                listing.Label("RimMind.Advisor.Settings.NoTriggerWarning".Translate());
                GUI.color = Color.white;
            }

            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Advisor.Settings.Section.Display".Translate());
            listing.CheckboxLabeled("RimMind.Advisor.Settings.ShowThoughtBubble".Translate(), ref Settings.showThoughtBubble,
                "RimMind.Advisor.Settings.ShowThoughtBubble.Desc".Translate());
            listing.Label("RimMind.Advisor.Settings.CustomPrompt".Translate());
            Settings.advisorCustomPrompt = listing.TextEntry(Settings.advisorCustomPrompt, 5);

            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Advisor.Settings.Section.Request".Translate());
            string cooldownHours = $"{Settings.requestCooldownTicks / 2500f:F1}";
            string cooldownTicks = $"{Settings.requestCooldownTicks}";
            listing.Label("RimMind.Advisor.Settings.RequestCooldown".Translate(cooldownHours, cooldownTicks));
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Advisor.Settings.RequestCooldown.Desc".Translate());
            GUI.color = Color.white;
            Settings.requestCooldownTicks = (int)listing.Slider(Settings.requestCooldownTicks, 3600f, 72000f);
            Settings.requestCooldownTicks = (Settings.requestCooldownTicks / 600) * 600;

            listing.Label("RimMind.Advisor.Settings.MaxConcurrent".Translate($"{Settings.maxConcurrentRequests}"));
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Advisor.Settings.MaxConcurrent.Desc".Translate());
            GUI.color = Color.white;
            Settings.maxConcurrentRequests = (int)listing.Slider(Settings.maxConcurrentRequests, 1f, 5f);

            listing.Label("RimMind.Advisor.Settings.RequestExpire".Translate($"{Settings.requestExpireTicks / 60000f:F2}"));
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Advisor.Settings.RequestExpire.Desc".Translate());
            GUI.color = Color.white;
            Settings.requestExpireTicks = (int)listing.Slider(Settings.requestExpireTicks, 3600f, 120000f);
            Settings.requestExpireTicks = (Settings.requestExpireTicks / 1500) * 1500;

            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Advisor.Settings.Section.Approval".Translate());
            listing.CheckboxLabeled("RimMind.Advisor.Settings.EnableRequestSystem".Translate(), ref Settings.enableRequestSystem,
                "RimMind.Advisor.Settings.EnableRequestSystem.Desc".Translate());
            listing.CheckboxLabeled("RimMind.Advisor.Settings.EnableRiskApproval".Translate(), ref Settings.enableRiskApproval,
                "RimMind.Advisor.Settings.EnableRiskApproval.Desc".Translate());
            if (!Settings.enableRequestSystem && Settings.enableRiskApproval)
            {
                GUI.color = Color.yellow;
                listing.Label("RimMind.Advisor.Settings.RiskWithoutApprovalWarning".Translate());
                GUI.color = Color.white;
            }
            if (Settings.enableRiskApproval)
            {
                string[] riskLabels = new[] { "Low", "Medium", "High", "Critical" };
                string currentLabel = Settings.autoBlockRiskLevel.ToString();
                listing.Label("RimMind.Advisor.Settings.AutoBlockRiskLevel".Translate(currentLabel));
                GUI.color = Color.gray;
                listing.Label("  " + "RimMind.Advisor.Settings.AutoBlockRiskLevel.Desc".Translate());
                GUI.color = Color.white;
                int riskVal = (int)listing.Slider((float)Settings.autoBlockRiskLevel, 0f, 3f);
                Settings.autoBlockRiskLevel = (RiskLevel)riskVal;
            }

            listing.End();
            Widgets.EndScrollView();

            SettingsUIDrawer.DrawBottomBar(bottomBar, () =>
            {
                Settings.enableAdvisor = true;
                Settings.enableLegacyJsonFallback = false;
                Settings.showThoughtBubble = true;
                Settings.enableIdleTrigger = true;
                Settings.enableMoodTrigger = true;
                Settings.requestCooldownTicks = 30000;
                Settings.maxConcurrentRequests = 3;
                Settings.pawnScanIntervalTicks = 3600;
                Settings.moodThreshold = 0.3f;
                Settings.requestExpireTicks = 30000;
                Settings.enableRequestSystem = true;
                Settings.enableRiskApproval = true;
                Settings.autoBlockRiskLevel = RiskLevel.High;
                Settings.advisorCustomPrompt = string.Empty;
            });

            Settings.Write();
        }

        private static float EstimateHeight()
        {
            float h = 30f;
            h += 24f;
            h += 24f;
            h += 24f + 24f;
            if (Settings.enableIdleTrigger)
                h += 24f + 32f;
            h += 24f;
            if (!Settings.enableIdleTrigger && !Settings.enableMoodTrigger)
                h += 24f;
            if (Settings.enableMoodTrigger)
                h += 24f + 32f;
            h += 24f + 80f;
            h += 24f + 24f;
            h += 24f + 24f;
            h += 24f + 24f + 32f + 24f + 32f + 24f + 32f;
            h += 24f + 24f + 24f;
            if (!Settings.enableRequestSystem && Settings.enableRiskApproval)
                h += 24f;
            if (Settings.enableRiskApproval)
                h += 24f + 32f;
            return h + 40f;
        }

        private static string BuildToolListText()
        {
            try
            {
                var defs = RimMindAPI.Tools.GetAllDefinitions();
                if (defs.Count == 0) return string.Empty;

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("RimMind.Advisor.Prompt.InstantSectionHeader".Translate());
                foreach (var def in defs)
                {
                    if (string.IsNullOrWhiteSpace(def.Id)) continue;
                    if (RimMindAPI.ShouldSkipAction(def.Id)) continue;

                    var riskTag = RiskTag(AdvisorToolRiskResolver.Resolve(def.Id));
                    var category = string.IsNullOrWhiteSpace(def.Category) ? "general" : def.Category;
                    var description = string.IsNullOrWhiteSpace(def.Description) ? "" : $" | {def.Description}";
                    sb.AppendLine($"- {def.Id} [{category}]{riskTag}{description}");
                }

                return sb.ToString().TrimEnd();
            }
            catch (System.Exception ex)
            {
                RimMindErrors.Warn($"[RimMind-Advisor] BuildToolListText failed: {ex.Message}");
                return string.Empty;
            }
        }

        private static string RiskTag(RiskLevel risk) => risk switch
        {
            RiskLevel.Low => "RimMind.Advisor.Prompt.Risk.Low".Translate(),
            RiskLevel.Medium => "RimMind.Advisor.Prompt.Risk.Medium".Translate(),
            RiskLevel.High => "RimMind.Advisor.Prompt.Risk.High".Translate(),
            RiskLevel.Critical => "RimMind.Advisor.Prompt.Risk.Critical".Translate(),
            _ => "",
        };
    }
}
