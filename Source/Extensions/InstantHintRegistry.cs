using System.Collections.Generic;

namespace RimMind.Advisor
{
    /// <summary>
    /// Registry of known instant action intent IDs.
    /// Replaces the hardcoded HashSet in JobCandidateBuilder.
    /// </summary>
    public static class InstantHintRegistry
    {
        private static readonly HashSet<string> KnownActions = new HashSet<string>
        {
            "force_rest",
            "social_relax",
            "eat_food",
            "tend_pawn",
            "rescue_pawn",
            "inspire_work",
            "inspire_shoot",
            "inspire_trade",
            "move_to",
        };

        public static bool IsKnownAction(string intentId) => KnownActions.Contains(intentId);
        public static IReadOnlyCollection<string> GetKnownActions() => KnownActions;
    }
}
