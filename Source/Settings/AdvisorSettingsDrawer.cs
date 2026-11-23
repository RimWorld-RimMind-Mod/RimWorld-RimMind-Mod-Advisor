using RimMind.Advisor.Comps;
using RimMind.Domain.Enums;
using RimMind.Presentation.Api;
using RimMind.Presentation.UI;
using UnityEngine;
using Verse;

namespace RimMind.Advisor.Settings
{
    internal static class AdvisorSettingsDrawer
    {
        private static Vector2 _scrollPos = Vector2.zero;
        private static float _measuredHeight = 760f;

        internal static void Draw(Rect inRect)
        {
            SettingsUIDrawer.DrawScrollableSettingsPage(
                inRect,
                ref _scrollPos,
                ref _measuredHeight,
                DrawContent,
                onReset: ResetToDefaults,
                onDirtySave: () => RimMindAdvisorMod.Settings.Write());
        }

        private static void DrawContent(Listing_Standard listing)
        {
            listing.CheckboxLabeled(
                RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.EnableAdvisor", "Enable AI Advisor System"),
                ref RimMindAdvisorMod.Settings.enableAdvisor,
                RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.EnableAdvisor.Desc", "When enabled, AI will choose the next action for colonists based on their state and personality."));
            listing.CheckboxLabeled(
                RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.EnableLegacyJsonFallback", "Enable legacy JSON fallback"),
                ref RimMindAdvisorMod.Settings.enableLegacyJsonFallback,
                RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.EnableLegacyJsonFallback.Desc", "Allow text JSON advice to be parsed into tool calls for old prompts. New prompts should use native ToolCall only."));

            SettingsUIDrawer.DrawSectionHeader(listing, RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.TriggerSources", "Trigger Sources"));
            listing.CheckboxLabeled(
                RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.EnableIdleTrigger", "Trigger on Colonist Idle"),
                ref RimMindAdvisorMod.Settings.enableIdleTrigger,
                RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.EnableIdleTrigger.Desc", "When a colonist has no work, AI will proactively choose their next action."));
            if (RimMindAdvisorMod.Settings.enableIdleTrigger)
            {
                string scanTicks = $"{RimMindAdvisorMod.Settings.pawnScanIntervalTicks}";
                string scanSecs = $"{RimMindAdvisorMod.Settings.pawnScanIntervalTicks / 60f:F1}";
                string scanTip = RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.PawnScanInterval.Desc", "Frequency of scanning for idle colonists. Lower values are more responsive but have higher performance cost.");
                listing.LabelWithTooltip(
                    "  " + RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.PawnScanInterval", "Pawn Scan Interval: {0} ticks (~{1}s)", scanTicks, scanSecs),
                    scanTip);
                float rawScan = listing.SliderWithTooltip(RimMindAdvisorMod.Settings.pawnScanIntervalTicks, 600f, 6000f, scanTip);
                RimMindAdvisorMod.Settings.pawnScanIntervalTicks = Mathf.RoundToInt(rawScan / 100f) * 100;
            }
            listing.CheckboxLabeled(
                RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.EnableMoodTrigger", "Trigger on Low Mood"),
                ref RimMindAdvisorMod.Settings.enableMoodTrigger,
                RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.EnableMoodTrigger.Desc", "When a colonist's mood is too low, AI will prioritize actions that improve their mood."));
            if (RimMindAdvisorMod.Settings.enableMoodTrigger)
            {
                string moodPct = $"{RimMindAdvisorMod.Settings.moodThreshold * 100:F0}";
                string moodTip = RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.MoodThreshold.Desc", "Triggers AI advisor when a colonist's mood falls below this percentage.");
                listing.LabelWithTooltip(
                    "  " + RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.MoodThreshold", "Mood Trigger Threshold: {0}%", moodPct),
                    moodTip);
                RimMindAdvisorMod.Settings.moodThreshold = listing.SliderWithTooltip(RimMindAdvisorMod.Settings.moodThreshold, 0.25f, 0.6f, moodTip);
            }
            if (!RimMindAdvisorMod.Settings.enableIdleTrigger && !RimMindAdvisorMod.Settings.enableMoodTrigger)
            {
                GUI.color = Color.yellow;
                listing.Label(RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.NoTriggerWarning", "Warning: No trigger source enabled. AI Advisor will not activate automatically."));
                GUI.color = Color.white;
            }

            SettingsUIDrawer.DrawSectionHeader(listing, RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.Section.Display", "Display"));
            listing.CheckboxLabeled(
                RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.ShowThoughtBubble", "Show AI Decision Bubble"),
                ref RimMindAdvisorMod.Settings.showThoughtBubble,
                RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.ShowThoughtBubble.Desc", "Display the AI's reasoning for choosing an action above the colonist."));
            listing.Label(RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.CustomPrompt", "Custom System Prompt (inserted after system messages)"));
            RimMindAdvisorMod.Settings.advisorCustomPrompt = listing.TextEntry(RimMindAdvisorMod.Settings.advisorCustomPrompt, 5);

            SettingsUIDrawer.DrawSectionHeader(listing, RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.Section.Request", "Request Settings"));
            string cooldownHours = $"{RimMindAdvisorMod.Settings.requestCooldownTicks / 2500f:F1}";
            string cooldownTicks = $"{RimMindAdvisorMod.Settings.requestCooldownTicks}";
            string cooldownTip = RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.RequestCooldown.Desc", "Minimum interval between AI requests for the same colonist.");
            listing.LabelWithTooltip(
                RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.RequestCooldown", "Request Cooldown: {0} game hours ({1} ticks)", cooldownHours, cooldownTicks),
                cooldownTip);
            float rawCooldown = listing.SliderWithTooltip(RimMindAdvisorMod.Settings.requestCooldownTicks, 3500f, 72000f, cooldownTip);
            RimMindAdvisorMod.Settings.requestCooldownTicks = Mathf.RoundToInt(rawCooldown / 500f) * 500;

            string maxConcTip = RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.MaxConcurrent.Desc", "Maximum number of simultaneous AI requests. Higher values improve responsiveness but increase API costs.");
            listing.LabelWithTooltip(
                RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.MaxConcurrent", "Max Concurrent Requests: {0}", $"{RimMindAdvisorMod.Settings.maxConcurrentRequests}"),
                maxConcTip);
            RimMindAdvisorMod.Settings.maxConcurrentRequests = Mathf.RoundToInt(
                listing.SliderWithTooltip(RimMindAdvisorMod.Settings.maxConcurrentRequests, 1f, 5f, maxConcTip));

            string expireTip = RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.RequestExpire.Desc", "Approval floating window expiry time. Requests not responded to within this time are automatically dismissed.");
            listing.LabelWithTooltip(
                RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.RequestExpire", "Approval request expiry: {0} game days", $"{RimMindAdvisorMod.Settings.requestExpireTicks / 60000f:F2}"),
                expireTip);
            float rawExpire = listing.SliderWithTooltip(RimMindAdvisorMod.Settings.requestExpireTicks, 3600f, 120000f, expireTip);
            RimMindAdvisorMod.Settings.requestExpireTicks = Mathf.RoundToInt(rawExpire / 1500f) * 1500;

            SettingsUIDrawer.DrawSectionHeader(listing, RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.Section.Approval", "Approval & Blocking"));
            listing.CheckboxLabeled(
                RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.EnableRequestSystem", "Enable request system (colonist requests require player approval)"),
                ref RimMindAdvisorMod.Settings.enableRequestSystem,
                RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.EnableRequestSystem.Desc", "When enabled, colonists can proactively submit action requests that require player approval before execution."));
            listing.CheckboxLabeled(
                RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.EnableRiskApproval", "Enable risk blocking (high-risk actions require player approval)"),
                ref RimMindAdvisorMod.Settings.enableRiskApproval,
                RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.EnableRiskApproval.Desc", "When enabled, actions at or above the risk threshold are automatically blocked and require player approval."));
            if (!RimMindAdvisorMod.Settings.enableRequestSystem && RimMindAdvisorMod.Settings.enableRiskApproval)
            {
                GUI.color = Color.yellow;
                listing.Label(RimMindAPI.Text.TrOr(
                    "RimMind.Advisor.Settings.RiskWithoutApprovalWarning",
                    "Warning: Approval system is off. Actions requiring approval will be blocked automatically without showing an approval card."));
                GUI.color = Color.white;
            }
            if (RimMindAdvisorMod.Settings.enableRiskApproval)
            {
                string currentLabel = AdvisorPromptHelper.RiskTag(RimMindAdvisorMod.Settings.autoBlockRiskLevel);
                string riskTip = RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.AutoBlockRiskLevel.Desc", "Actions at or above this risk level are automatically blocked and require player approval.");
                listing.LabelWithTooltip(
                    RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.AutoBlockRiskLevel", "Auto-block risk level threshold: {0}", currentLabel),
                    riskTip);
                int riskVal = Mathf.RoundToInt(listing.SliderWithTooltip((float)RimMindAdvisorMod.Settings.autoBlockRiskLevel, 0f, 3f, riskTip));
                RimMindAdvisorMod.Settings.autoBlockRiskLevel = (RiskLevel)Mathf.Clamp(riskVal, 0, 3);
            }

            if (Current.ProgramState == ProgramState.Playing && Find.CurrentMap != null)
            {
                var colonists = RimMindPawnLookup.GetSpawnedColonistsSafe(Find.CurrentMap);
                if (colonists.Count > 0)
                {
                    SettingsUIDrawer.DrawSectionHeader(listing, RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.Section.ColonistFilter", "Per-Colonist AI Advisor Control"));
                    string colonistTip = RimMindAPI.Text.TrOr("RimMind.Advisor.Settings.ColonistFilter.Tip", "Uncheck to ignore this colonist so AI Advisor will not automatically assign actions to them.");
                    for (int i = 0; i < colonists.Count; i++)
                    {
                        Pawn p = colonists[i];
                        if (!RimMindPawnLookup.IsActiveSpawnedColonist(p)) continue;
                        var comp = p.TryGetComp<CompAIAdvisor>();
                        if (comp == null) continue;
                        bool enabled = comp.IsEnabled;
                        listing.CheckboxLabeled("  " + p.LabelShortCap, ref enabled, colonistTip);
                        comp.IsEnabled = enabled;
                    }
                }
            }
        }

        private static void ResetToDefaults()
        {
            RimMindAdvisorMod.Settings.enableAdvisor = true;
            RimMindAdvisorMod.Settings.enableLegacyJsonFallback = false;
            RimMindAdvisorMod.Settings.showThoughtBubble = true;
            RimMindAdvisorMod.Settings.enableIdleTrigger = true;
            RimMindAdvisorMod.Settings.enableMoodTrigger = true;
            RimMindAdvisorMod.Settings.requestCooldownTicks = 5000;
            RimMindAdvisorMod.Settings.macroCheckChance = 0.35f;
            RimMindAdvisorMod.Settings.maxConcurrentRequests = 3;
            RimMindAdvisorMod.Settings.pawnScanIntervalTicks = 3600;
            RimMindAdvisorMod.Settings.moodThreshold = 0.3f;
            RimMindAdvisorMod.Settings.requestExpireTicks = 30000;
            RimMindAdvisorMod.Settings.enableRequestSystem = true;
            RimMindAdvisorMod.Settings.enableRiskApproval = true;
            RimMindAdvisorMod.Settings.autoBlockRiskLevel = RiskLevel.High;
            RimMindAdvisorMod.Settings.advisorCustomPrompt = string.Empty;
        }
    }
}
