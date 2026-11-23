using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Extension;

namespace RimMind.Advisor
{
    internal sealed class AdvisorActionSkipCheck : ISkipCheck
    {
        public string Id => "advisor.action";
        public string OwnerModId => RimMindOwnerConsts.AdvisorModId;
        public SkipCheckKind Kind => SkipCheckKind.Action;
        public bool ShouldSkip(in SkipCheckArgs args) => !RimMindAdvisorMod.Settings.enableAdvisor;
    }
}
