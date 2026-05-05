using System.Collections.Generic;

namespace Verse
{
    public static class Log
    {
        public static void Warning(string msg) { }
        public static void Message(string msg) { }
        public static void Error(string msg) { }
    }

    public static class Extensions
    {
        public static bool NullOrEmpty(this string? s) => string.IsNullOrEmpty(s);
    }

    public class Pawn
    {
        public int thingIDNumber;
        public string LabelShort = "TestPawn";
        public string NameToStringShort = "TestPawn";
        public string ThingID = "TestPawn_0";
        public bool Dead;
        public bool Destroyed() => Dead;
    }
}

namespace RimMind.Actions
{
    public static class RimMindActionsAPI
    {
        public static IReadOnlyList<string> GetSupportedIntents()
        {
            return new List<string> { "assign_job", "forbid_area", "social_relax", "add_thought" };
        }

        public static List<RimMind.Core.Client.StructuredTool> GetStructuredTools()
        {
            return new List<RimMind.Core.Client.StructuredTool>();
        }
    }
}

namespace RimMind.Core.Client
{
    public class StructuredTool
    {
        public string Name = "";
        public string Description = "";
        public string? ParametersSchema;
    }

    public class AIRequest { }
    public class AIResponse { }
    public class ChatMessage
    {
        public string Role = "";
        public string Content = "";
        public string? ReasoningContent;
        public string? ToolCallId;
        public List<ChatToolCall>? ToolCalls;
    }
    public class ChatToolCall
    {
        public string Id = "";
        public string Name = "";
        public string Arguments = "";
    }
    public enum AIRequestPriority { Normal }
}

namespace RimMind.Core.Context
{
    public class ContextRequest
    {
        public string NpcId = "";
        public string Scenario = "";
        public float Budget;
        public int MaxTokens;
        public float Temperature;
    }
}

namespace RimMind.Core
{
    public static class RimMindAPI
    {
        public static void RequestStructuredAsync(object request, string? schema,
            System.Action<object> onComplete, object? tools = null) { }
    }

    public static class RimMindCoreMod
    {
        public static object? Settings;
    }
}

namespace RimMind.Advisor.Settings
{
    public class RimMindAdvisorSettings
    {
        public string advisorCustomPrompt = "";
        public int requestExpireTicks = 600;
    }
}

namespace RimMind.Advisor.Data
{
    public class AdvisorHistoryStore
    {
        public static AdvisorHistoryStore? Instance;
        public List<AdvisorRequestRecord>? GlobalLog;
    }

    public class AdvisorRequestRecord
    {
        public string action = "";
        public string? reason;
        public string result = "";
        public int tick;
    }
}
