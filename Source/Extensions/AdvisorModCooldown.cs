using RimMind.Contracts.Extension;

namespace RimMind.Advisor
{
    internal sealed class AdvisorModCooldown : IModCooldown
    {
        private readonly RimMindAdvisorSettings _settings;
        public AdvisorModCooldown(RimMindAdvisorSettings settings) { _settings = settings; }
        public string Id => "Advisor";
        public int CooldownTicks => _settings.requestCooldownTicks;
    }
}
