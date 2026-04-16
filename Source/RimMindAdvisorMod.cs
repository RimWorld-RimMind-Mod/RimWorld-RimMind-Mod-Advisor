using System.Linq;
using HarmonyLib;
using RimMind.Actions;
using RimMind.Advisor.Data;
using RimMind.Advisor.Settings;
using RimMind.Core;
using RimMind.Core.Prompt;
using RimMind.Core.UI;
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

            RimMindAPI.RegisterSettingsTab("advisor", () => "RimMind.Advisor.Settings.Tab".Translate(), DrawSettingsContent);
            RimMindAPI.RegisterModCooldown("Advisor", () => Settings.requestCooldownTicks);

            RimMindAPI.RegisterPawnContextProvider("advisor_history", pawn =>
            {
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
            }, PromptSection.PriorityAuxiliary);

            Log.Message("[RimMind-Advisor] Initialized.");
        }

        public override string SettingsCategory() => "RimMind - Advisor";

        public override void DoSettingsWindowContents(Rect rect)
        {
            DrawSettingsContent(rect);
        }

        internal static void DrawSettingsContent(Rect inRect)
        {
            Rect contentArea = SettingsUIHelper.SplitContentArea(inRect);
            Rect bottomBar  = SettingsUIHelper.SplitBottomBar(inRect);

            float contentH = EstimateHeight();
            Rect viewRect = new Rect(0f, 0f, contentArea.width - 16f, contentH);
            Widgets.BeginScrollView(contentArea, ref _scrollPos, viewRect);

            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            listing.CheckboxLabeled("RimMind.Advisor.Settings.EnableAdvisor".Translate(), ref Settings.enableAdvisor,
                "RimMind.Advisor.Settings.EnableAdvisor.Desc".Translate());

            SettingsUIHelper.DrawSectionHeader(listing, "RimMind.Advisor.Settings.TriggerSources".Translate());
            listing.CheckboxLabeled("RimMind.Advisor.Settings.EnableIdleTrigger".Translate(), ref Settings.enableIdleTrigger,
                "RimMind.Advisor.Settings.EnableIdleTrigger.Desc".Translate());
            if (Settings.enableIdleTrigger)
            {
                string scanTicks = $"{Settings.pawnScanIntervalTicks}";
                string scanSecs = $"{Settings.pawnScanIntervalTicks / 60f:F1}";
                listing.Label("  " + "RimMind.Advisor.Settings.PawnScanInterval".Translate(scanTicks, scanSecs));
                Settings.pawnScanIntervalTicks = (int)listing.Slider(Settings.pawnScanIntervalTicks, 600f, 6000f);
                Settings.pawnScanIntervalTicks = (Settings.pawnScanIntervalTicks / 100) * 100;
            }
            listing.CheckboxLabeled("RimMind.Advisor.Settings.EnableMoodTrigger".Translate(), ref Settings.enableMoodTrigger,
                "RimMind.Advisor.Settings.EnableMoodTrigger.Desc".Translate());
            if (Settings.enableMoodTrigger)
            {
                string moodPct = $"{Settings.moodThreshold * 100:F0}";
                listing.Label("  " + "RimMind.Advisor.Settings.MoodThreshold".Translate(moodPct));
                Settings.moodThreshold = listing.Slider(Settings.moodThreshold, 0.25f, 0.6f);
            }

            SettingsUIHelper.DrawCustomPromptSection(listing,
                "RimMind.Advisor.Settings.CustomPrompt".Translate(),
                ref Settings.advisorCustomPrompt);

            SettingsUIHelper.DrawSectionHeader(listing, "RimMind.Advisor.Settings.Section.Display".Translate());
            listing.CheckboxLabeled("RimMind.Advisor.Settings.ShowThoughtBubble".Translate(), ref Settings.showThoughtBubble,
                "RimMind.Advisor.Settings.ShowThoughtBubble.Desc".Translate());

            SettingsUIHelper.DrawSectionHeader(listing, "RimMind.Advisor.Settings.Section.Request".Translate());
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

            SettingsUIHelper.DrawSectionHeader(listing, "RimMind.Advisor.Settings.Section.Approval".Translate());
            listing.CheckboxLabeled("RimMind.Advisor.Settings.EnableRequestSystem".Translate(), ref Settings.enableRequestSystem,
                "RimMind.Advisor.Settings.EnableRequestSystem.Desc".Translate());
            listing.CheckboxLabeled("RimMind.Advisor.Settings.EnableRiskApproval".Translate(), ref Settings.enableRiskApproval,
                "RimMind.Advisor.Settings.EnableRiskApproval.Desc".Translate());
            if (Settings.enableRiskApproval)
            {
                string[] riskLabels = new[] { "Low", "Medium", "High", "Critical" };
                string currentLabel = Settings.autoBlockRiskLevel.ToString();
                listing.Label("RimMind.Advisor.Settings.AutoBlockRiskLevel".Translate(currentLabel));
                int riskVal = (int)listing.Slider((float)Settings.autoBlockRiskLevel, 0f, 3f);
                Settings.autoBlockRiskLevel = (RiskLevel)riskVal;
            }

            listing.End();
            Widgets.EndScrollView();

            SettingsUIHelper.DrawBottomBar(bottomBar, () =>
            {
                Settings.enableAdvisor = true;
                Settings.showThoughtBubble = true;
                Settings.enableIdleTrigger = true;
                Settings.enableMoodTrigger = true;
                Settings.requestCooldownTicks = 36000;
                Settings.maxConcurrentRequests = 3;
                Settings.pawnScanIntervalTicks = 3600;
                Settings.moodThreshold = 0.4f;
                Settings.advisorCustomPrompt = "";
                Settings.requestExpireTicks = 30000;
                Settings.enableRequestSystem = true;
                Settings.enableRiskApproval = true;
                Settings.autoBlockRiskLevel = RiskLevel.High;
            });

            Settings.Write();
        }

        private static float EstimateHeight()
        {
            float h = 30f;
            h += 24f;
            h += 24f + 24f;
            if (Settings.enableIdleTrigger)
                h += 24f + 32f;
            h += 24f;
            if (Settings.enableMoodTrigger)
                h += 24f + 32f;
            h += 24f + 80f;
            h += 24f + 24f;
            h += 24f + 24f + 32f + 24f + 32f + 24f + 32f;
            h += 24f + 24f + 24f;
            if (Settings.enableRiskApproval)
                h += 24f + 32f;
            return h + 40f;
        }
    }
}
