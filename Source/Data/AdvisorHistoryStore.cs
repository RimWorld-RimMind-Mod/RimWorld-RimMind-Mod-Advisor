using System;
using System.Collections.Generic;
using RimWorld.Planet;
using Verse;
using RimMind.Presentation.Api;

namespace RimMind.Advisor.Data
{
    public sealed class PawnAdvisorHistory : IExposable
    {
        public int pawnId;
        public List<AdvisorRequestRecord> records = new List<AdvisorRequestRecord>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnId, "pawnId", 0);
            List<AdvisorRequestRecord>? serialized = records;
            Scribe_Collections.Look(ref serialized, "records", LookMode.Deep);
            records = serialized ?? new List<AdvisorRequestRecord>();
        }
    }

    public class AdvisorHistoryStore : WorldComponent
    {
        private static readonly List<AdvisorRequestRecord> EmptyRecords = new List<AdvisorRequestRecord>();
        private static readonly WorldScopedComponentCache<AdvisorHistoryStore> Cache =
            new WorldScopedComponentCache<AdvisorHistoryStore>();
#if RIMMIND_ADVISOR_TESTS
        private static AdvisorHistoryStore? _testFallbackInstance;
#endif

        private Dictionary<int, List<AdvisorRequestRecord>> _records = new Dictionary<int, List<AdvisorRequestRecord>>();
        private List<PawnAdvisorHistory>? _scribePawnHistories;
        private List<AdvisorRequestRecord> _globalLog = new List<AdvisorRequestRecord>();

        public static AdvisorHistoryStore? Instance
        {
            get
            {
                var cached = Cache.Get();
#if RIMMIND_ADVISOR_TESTS
                return cached ?? _testFallbackInstance;
#else
                return cached;
#endif
            }
        }

        public static void ResetCache()
        {
            Cache.Reset();
#if RIMMIND_ADVISOR_TESTS
            _testFallbackInstance = null;
#endif
        }

        public int TrackedPawnCount => _records.Count;

        public AdvisorHistoryStore(World world) : base(world)
        {
            Cache.Reset();
#if RIMMIND_ADVISOR_TESTS
            _testFallbackInstance = this;
#endif
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
            AdvisorHistoryChangedEvent.Publish(pawn.thingIDNumber);
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

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                _scribePawnHistories = new List<PawnAdvisorHistory>(_records.Count);
                foreach (var kvp in _records)
                {
                    if (kvp.Value != null && kvp.Value.Count > 0)
                    {
                        _scribePawnHistories.Add(new PawnAdvisorHistory
                        {
                            pawnId = kvp.Key,
                            records = kvp.Value
                        });
                    }
                }
            }

            Scribe_Collections.Look(ref _scribePawnHistories, "pawnHistories", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                _records = new Dictionary<int, List<AdvisorRequestRecord>>();
                if (_scribePawnHistories != null)
                {
                    for (int i = 0; i < _scribePawnHistories.Count; i++)
                    {
                        var entry = _scribePawnHistories[i];
                        if (entry != null && entry.records != null && entry.records.Count > 0)
                        {
                            _records[entry.pawnId] = entry.records;
                        }
                    }
                }
            }

            if (Scribe.mode == LoadSaveMode.Saving || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                _scribePawnHistories = null;
            }

            List<AdvisorRequestRecord>? globalLog = _globalLog;
            Scribe_Collections.Look(ref globalLog, "globalLog", LookMode.Deep);
            _globalLog = globalLog ?? new List<AdvisorRequestRecord>();
        }
    }

#if RIMMIND_ADVISOR_TESTS
    internal sealed class AdvisorHistoryChangedEvent : RimMind.Domain.Events.AgentBusEvent
    {
        public const string EventName = "AdvisorEvent";

        public AdvisorHistoryChangedEvent(int pawnId)
        {
            PawnId = pawnId;
            NpcId = pawnId > 0 ? "NPC-" + pawnId : string.Empty;
        }

        public static void Register()
        {
            RimMind.Application.Features.AgentBus.AgentBusImpl.RegisterKnownEventType(EventName, typeof(AdvisorHistoryChangedEvent));
        }

        public static void Publish(int pawnId)
        {
        }
    }
#endif
}
