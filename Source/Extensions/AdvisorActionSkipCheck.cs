using RimMind.Application.Common.Interfaces.Extension;

namespace RimMind.Advisor
{
    internal sealed class AdvisorActionSkipCheck : ISkipCheck
    {
        public string Id => "advisor.action";
        public SkipCheckKind Kind => SkipCheckKind.Action;
        public bool ShouldSkip(in SkipCheckArgs args) => !RimMindAdvisorMod.Settings.enableAdvisor;
    }
}
