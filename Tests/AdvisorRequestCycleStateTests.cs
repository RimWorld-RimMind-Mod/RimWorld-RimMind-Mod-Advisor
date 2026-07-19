using System;
using System.Collections.Generic;
using RimMind.Advisor.Advisor;
using RimMind.Application.Common.Models.UI;
using Xunit;

namespace RimMind.Advisor.Tests
{
    public class AdvisorRequestCycleStateTests
    {
        [Fact]
        public void FeedbackWaitsForEveryApprovalAndStartsOnlyOnce()
        {
            var cycle = new AdvisorRequestCycleState<string, string>();
            cycle.AddPendingApproval();
            cycle.AddPendingApproval();

            cycle.QueueFeedback("approved-a", "result-a");
            cycle.FinishApproval();

            Assert.False(cycle.TryStartFeedback(out _, out _));
            Assert.False(cycle.CanComplete);

            cycle.FinishApproval();

            Assert.True(cycle.TryStartFeedback(out var calls, out var results));
            Assert.Equal(new[] { "approved-a" }, calls);
            Assert.Equal(new[] { "result-a" }, results);
            Assert.False(cycle.TryStartFeedback(out _, out _));
            Assert.False(cycle.CanComplete);

            cycle.FinishFeedback();
            Assert.True(cycle.CanComplete);
        }

        [Fact]
        public void MixedDirectAndApprovedResultsAreReturnedAsOneFeedbackBatch()
        {
            var cycle = new AdvisorRequestCycleState<string, string>();
            cycle.AddPendingApproval();
            cycle.QueueFeedback(
                new List<string> { "direct" },
                new List<string> { "direct-result" });
            cycle.QueueFeedback("approved", "approved-result");
            cycle.FinishApproval();

            Assert.True(cycle.TryStartFeedback(out var calls, out var results));
            Assert.Equal(new[] { "direct", "approved" }, calls);
            Assert.Equal(new[] { "direct-result", "approved-result" }, results);
        }

        [Fact]
        public void RejectedOnlyBatchCanCompleteAfterLastApproval()
        {
            var cycle = new AdvisorRequestCycleState<string, string>();
            cycle.AddPendingApproval();
            cycle.AddPendingApproval();

            cycle.FinishApproval();
            Assert.False(cycle.CanComplete);

            cycle.FinishApproval();
            Assert.True(cycle.CanComplete);
        }

        [Fact]
        public void SynchronousApprovalCompletionCannotAdvanceAnOpenResponseBatch()
        {
            var cycle = new AdvisorRequestCycleState<string, string>();
            cycle.BeginResponseBatch();
            cycle.AddPendingApproval();
            cycle.QueueFeedback("direct", "result");
            cycle.FinishApproval();

            Assert.False(cycle.TryStartFeedback(out _, out _));
            Assert.False(cycle.CanComplete);

            cycle.EndResponseBatch();
            Assert.True(cycle.TryStartFeedback(out _, out _));
        }

        [Fact]
        public void CancelPendingApprovals_DismissesEachTrackedEntryExactlyOnce()
        {
            var cycle = new AdvisorRequestCycleState<string, string>();
            var first = new RequestEntry();
            var second = new RequestEntry();
            var firstCancelCount = 0;
            var secondCancelCount = 0;
            first.completionCallback = _ => cycle.TryFinishApproval(first);
            second.completionCallback = _ => cycle.TryFinishApproval(second);

            cycle.TrackPendingApproval(first, () =>
            {
                firstCancelCount++;
                first.TryComplete(null, RequestCompletionReason.Dismissed);
            });
            cycle.TrackPendingApproval(second, () =>
            {
                secondCancelCount++;
                second.TryComplete(null, RequestCompletionReason.Dismissed);
            });

            cycle.CancelPendingApprovals();
            cycle.CancelPendingApprovals();

            Assert.Equal(1, firstCancelCount);
            Assert.Equal(1, secondCancelCount);
            Assert.Equal(0, cycle.PendingApprovals);
            Assert.True(cycle.CanComplete);
        }

        [Fact]
        public void CompletedApproval_IsRemovedFromCancellationTracking()
        {
            var cycle = new AdvisorRequestCycleState<string, string>();
            var entry = new RequestEntry();
            var cancelCount = 0;
            entry.callback = _ => cycle.TryFinishApproval(entry);
            cycle.TrackPendingApproval(entry, () => cancelCount++);

            Assert.True(entry.TryComplete("approved", RequestCompletionReason.Selected));
            cycle.CancelPendingApprovals();

            Assert.Equal(0, cancelCount);
            Assert.Equal(0, cycle.PendingApprovals);
        }

        [Fact]
        public void CancelPendingApprovals_FirstCancellationThrows_StillCancelsAndUntracksEveryEntry()
        {
            var cycle = new AdvisorRequestCycleState<string, string>();
            var first = new RequestEntry();
            var second = new RequestEntry();
            var secondCancelCount = 0;
            cycle.TrackPendingApproval(first, () => throw new InvalidOperationException("first cancellation failed"));
            cycle.TrackPendingApproval(second, () => secondCancelCount++);

            var errors = cycle.CancelPendingApprovals();

            Assert.Single(errors);
            Assert.IsType<InvalidOperationException>(errors[0]);
            Assert.Equal(1, secondCancelCount);
            Assert.Equal(0, cycle.PendingApprovals);
            Assert.True(cycle.CanComplete);
        }
    }
}
