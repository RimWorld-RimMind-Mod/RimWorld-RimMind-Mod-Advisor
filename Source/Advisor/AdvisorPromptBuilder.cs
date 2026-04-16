using System.Collections.Generic;
using System.Linq;
using RimMind.Advisor.Data;
using RimMind.Advisor.Settings;
using RimMind.Core;
using RimMind.Core.Prompt;
using Verse;

namespace RimMind.Advisor.Advisor
{
    public static class AdvisorPromptBuilder
    {
        public static string BuildSystemPrompt(Pawn pawn)
        {
            string name = pawn.Name.ToStringShort;

            var builder = StructuredPromptBuilder.FromKeyPrefix("RimMind.Advisor.Prompt.System")
                .Role("RimMind.Advisor.Prompt.System.Role".Translate(name))
                .Constraint("RimMind.Advisor.Prompt.FieldRules".Translate(name))
                .ConstraintFromKey("RimMind.Advisor.Prompt.OutputRules")
                .ConstraintFromKey("RimMind.Advisor.Prompt.RiskControl")
                .WithCustom(RimMindAdvisorMod.Settings?.advisorCustomPrompt);

            return builder.Build();
        }

        public static string BuildUserPrompt(Pawn pawn)
        {
            string candidateList = JobCandidateBuilder.Build(pawn);
            var sections = new List<PromptSection>();

            sections.Add(new PromptSection("candidates",
                "RimMind.Advisor.Prompt.CandidateHeader".Translate() + "\n" + candidateList,
                PromptSection.PriorityCurrentInput));

            var pawnSections = RimMindAPI.BuildFullPawnSections(pawn);
            sections.AddRange(pawnSections);

            var historyStore = AdvisorHistoryStore.Instance;
            if (historyStore != null)
            {
                var records = historyStore.GetRecords(pawn);
                if (records.Count > 0)
                {
                    var recent = records.TakeLast(5).ToList();
                    string historyText = string.Join("\n", recent.Select(r =>
                        $"[{r.action}] {r.reason} → {r.result}"));
                    string compressed = ContextComposer.CompressHistory(historyText, 5,
                        "RimMind.Advisor.Prompt.HistoryCompressed".Translate(records.Count - 5));
                    sections.Add(new PromptSection("advisor_history",
                        "RimMind.Advisor.Prompt.HistoryHeader".Translate() + "\n" + compressed,
                        PromptSection.PriorityMemory));
                }
            }

            var budget = new PromptBudget(5000, 600);
            return budget.ComposeToString(sections);
        }
    }
}
