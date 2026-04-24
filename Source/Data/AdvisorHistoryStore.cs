using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace RimMind.Advisor.Data
{
    public class AdvisorHistoryStore : WorldComponent
    {
        private Dictionary<int, List<AdvisorRequestRecord>> _records = new Dictionary<int, List<AdvisorRequestRecord>>();
        private List<AdvisorRequestRecord> _globalLog = new List<AdvisorRequestRecord>();

        private static AdvisorHistoryStore? _instance;
        public static AdvisorHistoryStore? Instance => _instance;

        public AdvisorHistoryStore(World world) : base(world)
        {
            if (_instance != null && _instance != this)
                Log.Warning($"[RimMind-Advisor] AdvisorHistoryStore: replacing stale instance");
            _instance = this;
        }

        ~AdvisorHistoryStore()
        {
            if (_instance == this)
                _instance = null;
        }

        public List<AdvisorRequestRecord> GetRecords(Pawn pawn)
        {
            if (!_records.TryGetValue(pawn.thingIDNumber, out var list))
            {
                list = new List<AdvisorRequestRecord>();
                _records[pawn.thingIDNumber] = list;
            }
            return list;
        }

        public void AddRecord(Pawn pawn, AdvisorRequestRecord record)
        {
            var list = GetRecords(pawn);
            list.Add(record);
            if (list.Count > 50)
                list.RemoveRange(0, list.Count - 50);
            _globalLog.Add(record);
            if (_globalLog.Count > 200)
                _globalLog.RemoveRange(0, _globalLog.Count - 200);
        }

        public IReadOnlyList<AdvisorRequestRecord> GlobalLog => _globalLog;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref _records, "advisorRecords", LookMode.Value, LookMode.Deep);
            _records ??= new Dictionary<int, List<AdvisorRequestRecord>>();
            Scribe_Collections.Look(ref _globalLog, "globalLog", LookMode.Deep);
            _globalLog ??= new List<AdvisorRequestRecord>();
        }
    }
}
