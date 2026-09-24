using System.Collections.Generic;
using RimMind.Domain.Llm;

namespace RimMind.Advisor.Advisor
{
    /// <summary>
    /// Pure state holder for iterative tool feedback. Clearing and depth checks are
    /// behaviorally testable without constructing a Pawn or sending an AI request.
    /// </summary>
    internal sealed class AdvisorFeedbackSession
    {
        public List<ChatMessage>? Messages { get; private set; }
        public List<StructuredTool>? Tools { get; private set; }
        public string? Schema { get; private set; }
        public int ToolCallDepth { get; private set; }
        public string? ReasoningContent { get; private set; }

        public bool HasPendingState => Messages != null;

        public void Begin(List<StructuredTool>? tools, string? schema)
        {
            Messages = null;
            Tools = tools;
            Schema = schema;
            ToolCallDepth = 0;
            ReasoningContent = null;
        }

        public void CaptureMessages(List<ChatMessage> messages)
            => Messages = messages;

        public void SetReasoningContent(string? content)
            => ReasoningContent = content;

        public bool CanRequestFeedback(int maximumDepth)
            => ToolCallDepth < maximumDepth
               && Messages != null
               && Tools != null
               && Tools.Count > 0;

        public void BeginFeedback(List<ChatMessage> messages)
        {
            ToolCallDepth++;
            Messages = messages;
        }

        public void Clear()
        {
            Messages = null;
            Tools = null;
            Schema = null;
            ToolCallDepth = 0;
            ReasoningContent = null;
        }
    }
}
