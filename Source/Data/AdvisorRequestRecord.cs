using Verse;

namespace RimMind.Advisor.Data
{
    public class AdvisorRequestRecord : IExposable
    {
        public string action = string.Empty;
        public string reason = string.Empty;
        public string result = string.Empty;
        public int tick;

        public void ExposeData()
        {
#pragma warning disable CS8601
            Scribe_Values.Look(ref action, "action", string.Empty);
            Scribe_Values.Look(ref reason, "reason", string.Empty);
            Scribe_Values.Look(ref result, "result", string.Empty);
#pragma warning restore CS8601
            Scribe_Values.Look(ref tick, "tick");
        }
    }
}
