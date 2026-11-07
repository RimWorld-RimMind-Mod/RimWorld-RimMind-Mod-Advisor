using RimMind.Advisor.Advisor;
using RimMind.Advisor.Settings;
using RimMind.Application.Common.Interfaces.Extension;

namespace RimMind.Advisor
{
    internal sealed class AdvisorModCooldown : IModCooldown
    {
        private readonly RimMindAdvisorSettings _settings;
        public AdvisorModCooldown(RimMindAdvisorSettings settings) { _settings = settings; }
        // Must equal the envelope ModId: Core resolves and stores queue cooldowns by that key.
        public string Id => AdvisorRequestIds.RequestModId;
        public string OwnerModId => AdvisorRequestIds.RequestModId;
        // Same activity scaling as the per-pawn cooldown (AdvisorCooldownCalculator), so the
        // Core queue gate does not silently re-impose the unscaled interval.
        public int CooldownTicks => AdvisorCooldownCalculator.ApplyActivityFrequencyScale(
            _settings.requestCooldownTicks,
            RimMind.Presentation.Api.RimMindAPI.Settings.ActivityFrequencyScale);
    }
}
