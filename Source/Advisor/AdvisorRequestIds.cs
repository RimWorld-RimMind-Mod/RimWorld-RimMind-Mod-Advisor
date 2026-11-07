namespace RimMind.Advisor.Advisor
{
    /// <summary>
    /// Advisor-owned identity used by Core's request queue.
    /// The same id must be passed to <c>LlmRequestEnvelopeBuilder.WithModId</c>, to
    /// <c>RimMindAPI.ClearModCooldown</c> / <c>GetModCooldownTicksLeft</c>, and exposed by
    /// <c>AdvisorModCooldown</c>: Core stores per-mod cooldowns under the envelope ModId,
    /// so any other spelling silently targets a cooldown key that never exists.
    /// </summary>
    public static class AdvisorRequestIds
    {
        /// <summary>Envelope ModId and Core queue cooldown key for every Advisor request.</summary>
        public const string RequestModId = "RimMind.Advisor";
    }
}
