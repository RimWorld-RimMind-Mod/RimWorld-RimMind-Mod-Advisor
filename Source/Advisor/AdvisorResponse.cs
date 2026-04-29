using RimMind.Actions;

namespace RimMind.Advisor.Advisor
{
    public class AdviceItem
    {
        public string Action = null!;
        public string? Target;
        public string? Param;
        public string? Reason;
        public RiskLevel RiskLevel;
    }
}
