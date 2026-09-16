using System.Collections.Generic;
using RimMind.Domain.Enums;
using RimMind.Domain.Llm;
using Verse;

namespace RimMind.Advisor
{
    /// <summary>
    /// Advisor prompt construction shared helpers.
    /// </summary>
    public static class AdvisorPromptHelper
    {
        /// <summary>
        /// Translate RiskLevel to localized tag string.
        /// Shared by JobCandidateBuilder and RimMindAdvisorMod.
        /// </summary>
        public static string RiskTag(RiskLevel risk) => risk switch
        {
            RiskLevel.Low => "RimMind.Advisor.Prompt.Risk.Low".Translate(),
            RiskLevel.Medium => "RimMind.Advisor.Prompt.Risk.Medium".Translate(),
            RiskLevel.High => "RimMind.Advisor.Prompt.Risk.High".Translate(),
            RiskLevel.Critical => "RimMind.Advisor.Prompt.Risk.Critical".Translate(),
            _ => "",
        };

        /// <summary>
        /// Find the index of the last "system" role message.
        /// Shared by AdvisorTaskDriver.BuildAndSendRequest (was inlined 3 times).
        /// </summary>
        public static int FindLastSystemIndex(IList<ChatMessage> messages)
        {
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (messages[i].Role == "system") return i;
            }
            return -1;
        }
    }
}
