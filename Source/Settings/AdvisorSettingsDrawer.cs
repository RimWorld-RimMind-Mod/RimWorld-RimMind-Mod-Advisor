using RimMind.Domain.Enums;
using RimMind.Presentation.UI;
using UnityEngine;
using Verse;

namespace RimMind.Advisor.Settings
{
    internal static class AdvisorSettingsDrawer
    {
        private static Vector2 _scrollPos = Vector2.zero;

        internal static void Draw(Rect inRect)
        {
            Rect contentArea = SettingsUIDrawer.SplitContentArea(inRect);
            Rect bottomBar = SettingsUIDrawer.SplitBottomBar(inRect);

            float contentH = EstimateHeight();
            Rect viewRect = new Rect(0f, 0f, contentArea.width - 16f, contentH);
            Widgets.BeginScrollView(contentArea, ref _scrollPos, viewRect);

            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            listing.CheckboxLabeled("RimMind.Advisor.Settings.EnableAdvisor".Translate(), ref RimMindAdvisorMod.Settings.enableAdvisor,
                "RimMind.Advisor.Settings.EnableAdvisor.Desc".Translate());
            listing.CheckboxLabeled(
                "RimMind.Advisor.Settings.EnableLegacyJsonFallback".Translate(),
                ref RimMindAdvisorMod.Settings.enableLegacyJsonFallback,
                "RimMind.Advisor.Settings.EnableLegacyJsonFallback.Desc".Translate());

            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Advisor.Settings.TriggerSources".Translate());
            listing.CheckboxLabeled("RimMind.Advisor.Settings.EnableIdleTrigger".Translate(), ref RimMindAdvisorMod.Settings.enableIdleTrigger,
                "RimMind.Advisor.Settings.EnableIdleTrigger.Desc".Translate());
            if (RimMindAdvisorMod.Settings.enableIdleTrigger)
            {
                string scanTicks = $"{RimMindAdvisorMod.Settings.pawnScanIntervalTicks}";
                string scanSecs = $"{RimMindAdvisorMod.Settings.pawnScanIntervalTicks / 60f:F1}";
                listing.Label("  " + "RimMind.Advisor.Settings.PawnScanInterval".Translate(scanTicks, scanSecs));
                GUI.color = Color.gray;
                listing.Label("    " + "RimMind.Advisor.Settings.PawnScanInterval.Desc".Translate());
                GUI.color = Color.white;
                RimMindAdvisorMod.Settings.pawnScanIntervalTicks = (int)listing.Slider(RimMindAdvisorMod.Settings.pawnScanIntervalTicks, 600f, 6000f);
                RimMindAdvisorMod.Settings.pawnScanIntervalTicks = (RimMindAdvisorMod.Settings.pawnScanIntervalTicks / 100) * 100;
            }
            listing.CheckboxLabeled("RimMind.Advisor.Settings.EnableMoodTrigger".Translate(), ref RimMindAdvisorMod.Settings.enableMoodTrigger,
                "RimMind.Advisor.Settings.EnableMoodTrigger.Desc".Translate());
            if (RimMindAdvisorMod.Settings.enableMoodTrigger)
            {
                string moodPct = $"{RimMindAdvisorMod.Settings.moodThreshold * 100:F0}";
                listing.Label("  " + "RimMind.Advisor.Settings.MoodThreshold".Translate(moodPct));
                GUI.color = Color.gray;
                listing.Label("    " + "RimMind.Advisor.Settings.MoodThreshold.Desc".Translate());
                GUI.color = Color.white;
                RimMindAdvisorMod.Settings.moodThreshold = listing.Slider(RimMindAdvisorMod.Settings.moodThreshold, 0.25f, 0.6f);
            }
            if (!RimMindAdvisorMod.Settings.enableIdleTrigger && !RimMindAdvisorMod.Settings.enableMoodTrigger)
            {
                GUI.color = Color.yellow;
                listing.Label("RimMind.Advisor.Settings.NoTriggerWarning".Translate());
                GUI.color = Color.white;
            }

            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Advisor.Settings.Section.Display".Translate());
            listing.CheckboxLabeled("RimMind.Advisor.Settings.ShowThoughtBubble".Translate(), ref RimMindAdvisorMod.Settings.showThoughtBubble,
                "RimMind.Advisor.Settings.ShowThoughtBubble.Desc".Translate());
            listing.Label("RimMind.Advisor.Settings.CustomPrompt".Translate());
            RimMindAdvisorMod.Settings.advisorCustomPrompt = listing.TextEntry(RimMindAdvisorMod.Settings.advisorCustomPrompt, 5);

            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Advisor.Settings.Section.Request".Translate());
            string cooldownHours = $"{RimMindAdvisorMod.Settings.requestCooldownTicks / 2500f:F1}";
            string cooldownTicks = $"{RimMindAdvisorMod.Settings.requestCooldownTicks}";
            listing.Label("RimMind.Advisor.Settings.RequestCooldown".Translate(cooldownHours, cooldownTicks));
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Advisor.Settings.RequestCooldown.Desc".Translate());
            GUI.color = Color.white;
            RimMindAdvisorMod.Settings.requestCooldownTicks = (int)listing.Slider(RimMindAdvisorMod.Settings.requestCooldownTicks, 3600f, 72000f);
            RimMindAdvisorMod.Settings.requestCooldownTicks = (RimMindAdvisorMod.Settings.requestCooldownTicks / 600) * 600;

            listing.Label("RimMind.Advisor.Settings.MaxConcurrent".Translate($"{RimMindAdvisorMod.Settings.maxConcurrentRequests}"));
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Advisor.Settings.MaxConcurrent.Desc".Translate());
            GUI.color = Color.white;
            RimMindAdvisorMod.Settings.maxConcurrentRequests = (int)listing.Slider(RimMindAdvisorMod.Settings.maxConcurrentRequests, 1f, 5f);

            listing.Label("RimMind.Advisor.Settings.RequestExpire".Translate($"{RimMindAdvisorMod.Settings.requestExpireTicks / 60000f:F2}"));
            GUI.color = Color.gray;
            listing.Label("  " + "RimMind.Advisor.Settings.RequestExpire.Desc".Translate());
            GUI.color = Color.white;
            RimMindAdvisorMod.Settings.requestExpireTicks = (int)listing.Slider(RimMindAdvisorMod.Settings.requestExpireTicks, 3600f, 120000f);
            RimMindAdvisorMod.Settings.requestExpireTicks = (RimMindAdvisorMod.Settings.requestExpireTicks / 1500) * 1500;

            SettingsUIDrawer.DrawSectionHeader(listing, "RimMind.Advisor.Settings.Section.Approval".Translate());
            listing.CheckboxLabeled("RimMind.Advisor.Settings.EnableRequestSystem".Translate(), ref RimMindAdvisorMod.Settings.enableRequestSystem,
                "RimMind.Advisor.Settings.EnableRequestSystem.Desc".Translate());
            listing.CheckboxLabeled("RimMind.Advisor.Settings.EnableRiskApproval".Translate(), ref RimMindAdvisorMod.Settings.enableRiskApproval,
                "RimMind.Advisor.Settings.EnableRiskApproval.Desc".Translate());
            if (!RimMindAdvisorMod.Settings.enableRequestSystem && RimMindAdvisorMod.Settings.enableRiskApproval)
            {
                GUI.color = Color.yellow;
                listing.Label("RimMind.Advisor.Settings.RiskWithoutApprovalWarning".Translate());
                GUI.color = Color.white;
            }
            if (RimMindAdvisorMod.Settings.enableRiskApproval)
            {
                string[] riskLabels = new[] { "Low", "Medium", "High", "Critical" };
                string currentLabel = RimMindAdvisorMod.Settings.autoBlockRiskLevel.ToString();
                listing.Label("RimMind.Advisor.Settings.AutoBlockRiskLevel".Translate(currentLabel));
                GUI.color = Color.gray;
                listing.Label("  " + "RimMind.Advisor.Settings.AutoBlockRiskLevel.Desc".Translate());
                GUI.color = Color.white;
                int riskVal = (int)listing.Slider((float)RimMindAdvisorMod.Settings.autoBlockRiskLevel, 0f, 3f);
                RimMindAdvisorMod.Settings.autoBlockRiskLevel = (RiskLevel)riskVal;
            }

            listing.End();
            Widgets.EndScrollView();

            SettingsUIDrawer.DrawBottomBar(bottomBar, () =>
            {
                RimMindAdvisorMod.Settings.enableAdvisor = true;
                RimMindAdvisorMod.Settings.enableLegacyJsonFallback = false;
                RimMindAdvisorMod.Settings.showThoughtBubble = true;
                RimMindAdvisorMod.Settings.enableIdleTrigger = true;
                RimMindAdvisorMod.Settings.enableMoodTrigger = true;
                RimMindAdvisorMod.Settings.requestCooldownTicks = 30000;
                RimMindAdvisorMod.Settings.maxConcurrentRequests = 3;
                RimMindAdvisorMod.Settings.pawnScanIntervalTicks = 3600;
                RimMindAdvisorMod.Settings.moodThreshold = 0.3f;
                RimMindAdvisorMod.Settings.requestExpireTicks = 30000;
                RimMindAdvisorMod.Settings.enableRequestSystem = true;
                RimMindAdvisorMod.Settings.enableRiskApproval = true;
                RimMindAdvisorMod.Settings.autoBlockRiskLevel = RiskLevel.High;
                RimMindAdvisorMod.Settings.advisorCustomPrompt = string.Empty;
            });

            RimMindAdvisorMod.Settings.Write();
        }

        private static float EstimateHeight()
        {
            float h = 30f;
            h += 24f;
            h += 24f;
            h += 24f + 24f;
            if (RimMindAdvisorMod.Settings.enableIdleTrigger)
                h += 24f + 32f;
            h += 24f;
            if (!RimMindAdvisorMod.Settings.enableIdleTrigger && !RimMindAdvisorMod.Settings.enableMoodTrigger)
                h += 24f;
            if (RimMindAdvisorMod.Settings.enableMoodTrigger)
                h += 24f + 32f;
            h += 24f + 80f;
            h += 24f + 24f;
            h += 24f + 24f;
            h += 24f + 24f + 32f + 24f + 32f + 24f + 32f;
            h += 24f + 24f + 24f;
            if (!RimMindAdvisorMod.Settings.enableRequestSystem && RimMindAdvisorMod.Settings.enableRiskApproval)
                h += 24f;
            if (RimMindAdvisorMod.Settings.enableRiskApproval)
                h += 24f + 32f;
            return h + 40f;
        }
    }
}
