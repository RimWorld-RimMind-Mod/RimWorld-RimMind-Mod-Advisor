using System;
using System.Collections.Generic;
using RimMind.Application.Common.Models.UI;

namespace RimMind.Advisor.Advisor
{
    internal sealed class AdvisorRequestCycleState<TCall, TResult>
    {
        private readonly List<TCall> _queuedCalls = new List<TCall>();
        private readonly List<TResult> _queuedResults = new List<TResult>();
        private readonly Dictionary<RequestEntry, Action> _pendingApprovalCancellations =
            new Dictionary<RequestEntry, Action>();

        public int PendingApprovals { get; private set; }
        public bool FeedbackInFlight { get; private set; }
        public bool ResponseBatchOpen { get; private set; }
        public bool HasQueuedFeedback => _queuedCalls.Count > 0;
        public bool CanComplete =>
            !ResponseBatchOpen && PendingApprovals == 0 && !FeedbackInFlight && !HasQueuedFeedback;

        public void BeginResponseBatch()
        {
            if (ResponseBatchOpen)
                throw new InvalidOperationException("An Advisor response batch is already open.");

            ResponseBatchOpen = true;
        }

        public void EndResponseBatch()
        {
            if (!ResponseBatchOpen)
                throw new InvalidOperationException("No Advisor response batch is open.");

            ResponseBatchOpen = false;
        }

        public void AddPendingApproval()
        {
            checked
            {
                PendingApprovals++;
            }
        }

        public void FinishApproval()
        {
            if (PendingApprovals <= 0)
                throw new InvalidOperationException("No pending Advisor approval to finish.");

            PendingApprovals--;
        }

        public void TrackPendingApproval(RequestEntry entry, Action cancel)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (cancel == null) throw new ArgumentNullException(nameof(cancel));
            if (_pendingApprovalCancellations.ContainsKey(entry))
                throw new InvalidOperationException("The Advisor approval is already tracked.");

            _pendingApprovalCancellations.Add(entry, cancel);
            AddPendingApproval();
        }

        public bool TryFinishApproval(RequestEntry entry)
        {
            if (!_pendingApprovalCancellations.Remove(entry))
                return false;

            FinishApproval();
            return true;
        }

        public IReadOnlyList<Exception> CancelPendingApprovals()
        {
            var errors = new List<Exception>();
            var pending = new List<KeyValuePair<RequestEntry, Action>>(_pendingApprovalCancellations);
            foreach (var approval in pending)
            {
                if (!_pendingApprovalCancellations.ContainsKey(approval.Key))
                    continue;

                try
                {
                    approval.Value();
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
                finally
                {
                    TryFinishApproval(approval.Key);
                }
            }

            return errors;
        }

        public void QueueFeedback(TCall call, TResult result)
        {
            _queuedCalls.Add(call);
            _queuedResults.Add(result);
        }

        public void QueueFeedback(IReadOnlyList<TCall> calls, IReadOnlyList<TResult> results)
        {
            if (calls.Count != results.Count)
                throw new ArgumentException("Advisor feedback calls and results must have matching counts.");

            for (var i = 0; i < calls.Count; i++)
                QueueFeedback(calls[i], results[i]);
        }

        public bool TryStartFeedback(out List<TCall> calls, out List<TResult> results)
        {
            if (ResponseBatchOpen || PendingApprovals > 0 || FeedbackInFlight || !HasQueuedFeedback)
            {
                calls = new List<TCall>();
                results = new List<TResult>();
                return false;
            }

            calls = new List<TCall>(_queuedCalls);
            results = new List<TResult>(_queuedResults);
            _queuedCalls.Clear();
            _queuedResults.Clear();
            FeedbackInFlight = true;
            return true;
        }

        public void FinishFeedback()
        {
            if (!FeedbackInFlight)
                throw new InvalidOperationException("No Advisor feedback request is in flight.");

            FeedbackInFlight = false;
        }

        public void DiscardQueuedFeedback()
        {
            _queuedCalls.Clear();
            _queuedResults.Clear();
        }
    }
}
