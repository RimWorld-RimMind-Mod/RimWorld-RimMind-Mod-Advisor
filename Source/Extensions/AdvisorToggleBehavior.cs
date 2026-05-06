using RimMind.Contracts.Extension;
using RimMind.Advisor.Settings;

namespace RimMind.Advisor
{
    internal sealed class AdvisorToggleBehavior : IToggleBehavior
    {
        private readonly RimMindAdvisorSettings _settings;
        public AdvisorToggleBehavior(RimMindAdvisorSettings settings) { _settings = settings; }
        public string Id => "advisor.toggle";
        public bool IsActive => _settings.enableAdvisor;
        public void Toggle() => _settings.enableAdvisor = !_settings.enableAdvisor;
    }
}
