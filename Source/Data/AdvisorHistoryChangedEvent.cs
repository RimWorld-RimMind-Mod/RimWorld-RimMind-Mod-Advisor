using System;
using RimMind.Domain.Events;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Api;

namespace RimMind.Advisor.Data
{
    /// <summary>
    /// Published on the Core bus whenever a pawn's advisor request history changes.
    /// The <c>advisor_history</c> context key names <see cref="EventName"/> as its invalidation
    /// trigger, so the next prompt sees the newest approval/rejection instead of a cached copy.
    /// </summary>
    public sealed class AdvisorHistoryChangedEvent : AgentBusEvent
    {
        /// <summary>Bus event name used by context-key invalidation triggers.</summary>
        public const string EventName = "AdvisorEvent";

        public AdvisorHistoryChangedEvent(int pawnId)
        {
            PawnId = pawnId;
            NpcId = pawnId > 0 ? "NPC-" + pawnId : string.Empty;
        }

        /// <summary>Registers <see cref="EventName"/> with the Core bus. Call before registering context keys.</summary>
        internal static void Register()
        {
            try { RimMindAPI.Bus.RegisterEventType<AdvisorHistoryChangedEvent>(EventName); }
            catch (Exception ex) { RimMindErrors.Warn($"[RimMind-Advisor] Event type registration failed: {ex.Message}"); }
        }

        /// <summary>Publishes from any thread; never throws.</summary>
        internal static void Publish(int pawnId)
        {
            try { RimMindAPI.Bus.PublishOnMainThread(new AdvisorHistoryChangedEvent(pawnId)); }
            catch (Exception ex) { RimMindErrors.Warn($"[RimMind-Advisor] History change notification failed: {ex.Message}"); }
        }
    }
}
