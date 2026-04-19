using System.Collections.Generic;
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
                .Goal("RimMind.Advisor.Prompt.System.Goal".Translate(name))
                .Constraint("RimMind.Advisor.Prompt.FieldRules".Translate(name))
                .ConstraintFromKey("RimMind.Advisor.Prompt.OutputRules")
                .ConstraintFromKey("RimMind.Advisor.Prompt.RiskControl");

            if (RimMindAdvisorMod.Settings?.enableRequestSystem == true)
                builder.ConstraintFromKey("RimMind.Advisor.Prompt.DiversityHint");

            builder.WithCustom(RimMindAdvisorMod.Settings?.advisorCustomPrompt);

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

            var budget = new PromptBudget(5000, 600);
            return budget.ComposeToString(sections);
        }
    }
}
