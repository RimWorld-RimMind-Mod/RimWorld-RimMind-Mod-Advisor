using RimMind.Domain.Enums;
using RimMind.Presentation.Api;

namespace RimMind.Advisor
{
    /// <summary>
    /// Advisor prompt construction shared helpers.
    /// </summary>
    public static class AdvisorPromptHelper
    {
        /// <summary>
        /// Translate RiskLevel to localized tag string with safe English fallback.
        /// </summary>
        public static string RiskTag(RiskLevel risk) => risk switch
        {
            RiskLevel.Low => RimMindAPI.Text.TrOr("RimMind.Advisor.Prompt.Risk.Low", "[Low]"),
            RiskLevel.Medium => RimMindAPI.Text.TrOr("RimMind.Advisor.Prompt.Risk.Medium", "[Medium]"),
            RiskLevel.High => RimMindAPI.Text.TrOr("RimMind.Advisor.Prompt.Risk.High", "[High]"),
            RiskLevel.Critical => RimMindAPI.Text.TrOr("RimMind.Advisor.Prompt.Risk.Critical", "[Danger]"),
            _ => "",
        };
    }
}
