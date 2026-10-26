using System;
using System.Collections.Generic;
using RimWorld.Planet;
using Verse;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Api;

namespace RimMind.Advisor.Data
{
    public class AdvisorHistoryStore : WorldComponent
    {
        private static readonly List<AdvisorRequestRecord> EmptyRecords = new List<AdvisorRequestRecord>();
        private Dictionary<int, List<AdvisorRequestRecord>> _records = new Dictionary<int, List<AdvisorRequestRecord>>();
        private List<AdvisorRequestRecord> _globalLog = new List<AdvisorRequestRecord>();

        private static AdvisorHistoryStore? _instance;
        public static AdvisorHistoryStore? Instance => _instance;

        public int TrackedPawnCount => _records.Count;

        public AdvisorHistoryStore(World world) : base(world)
        {
            if (_instance != null && _instance != this)
                RimMindErrors.Warn($"[RimMind-Advisor] AdvisorHistoryStore: replacing stale instance");
            _instance = this;
        }

        public List<AdvisorRequestRecord> GetRecords(Pawn? pawn)
        {
            if (pawn == null || !_records.TryGetValue(pawn.thingIDNumber, out var list))
                return EmptyRecords;
            return list;
        }

        public IReadOnlyList<AdvisorRequestRecord> GetRecentRecords(Pawn? pawn, int maxCount)
        {
            var records = GetRecords(pawn);
            if (records.Count == 0 || maxCount <= 0)
                return EmptyRecords;
            if (records.Count <= maxCount)
                return records;

            int startIndex = Math.Max(0, records.Count - maxCount);
            var result = new List<AdvisorRequestRecord>(records.Count - startIndex);
            for (int i = startIndex; i < records.Count; i++)
            {
                result.Add(records[i]);
            }
            return result;
        }

        public void AddRecord(Pawn? pawn, AdvisorRequestRecord record)
        {
            if (pawn == null || record == null) return;
            if (!_records.TryGetValue(pawn.thingIDNumber, out var list))
            {
                list = new List<AdvisorRequestRecord>();
                _records[pawn.thingIDNumber] = list;
            }
            list.Add(record);
            if (list.Count > 50)
                list.RemoveRange(0, list.Count - 50);
            _globalLog.Add(record);
            if (_globalLog.Count > 200)
                _globalLog.RemoveRange(0, _globalLog.Count - 200);
        }

        public void PruneInactivePawns()
        {
            if (_records.Count == 0) return;
            bool hasActiveMap = (Find.Maps != null && Find.Maps.Count > 0) || Find.CurrentMap != null;
            if (!hasActiveMap) return;

            var toRemove = new List<int>();
            foreach (var kvp in _records)
            {
                if (kvp.Value == null || kvp.Value.Count == 0)
                {
                    toRemove.Add(kvp.Key);
                    continue;
                }

                var pawn = RimMindPawnLookup.FindPawnByNumber(kvp.Key);
                if (pawn == null || !RimMindPawnLookup.IsActiveSpawnedColonist(pawn))
                {
                    toRemove.Add(kvp.Key);
                }
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                _records.Remove(toRemove[i]);
            }
        }

        public IReadOnlyList<AdvisorRequestRecord> GlobalLog => _globalLog;

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving || Scribe.mode == LoadSaveMode.Inactive)
            {
                PruneInactivePawns();
            }
            Dictionary<int, List<AdvisorRequestRecord>>? records = _records;
            Scribe_Collections.Look(ref records, "advisorRecords", LookMode.Value, LookMode.Deep);
            _records = records ?? new Dictionary<int, List<AdvisorRequestRecord>>();

            List<AdvisorRequestRecord>? globalLog = _globalLog;
            Scribe_Collections.Look(ref globalLog, "globalLog", LookMode.Deep);
            _globalLog = globalLog ?? new List<AdvisorRequestRecord>();
        }
    }
}
