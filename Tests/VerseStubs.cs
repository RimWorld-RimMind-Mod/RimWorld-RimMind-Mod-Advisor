using System;
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
        public static string Translate(this string key) => key;
        public static string Translate(this string key, params object[] args) => key;
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

    // IExposable 接口桩，供 AdvisorRequestRecord 实现
    public interface IExposable
    {
        void ExposeData();
    }

    // LookMode 枚举桩，供 Scribe_Collections 使用
    public enum LookMode
    {
        Undef,
        Value,
        Deep,
        Reference
    }

    // Scribe_Values 桩，供 AdvisorRequestRecord.ExposeData 使用
    public static class Scribe_Values
    {
        public static void Look<T>(ref T value, string label, T defaultValue = default!) { }
    }

    // Scribe_Collections 桩，供 AdvisorHistoryStore.ExposeData 使用
    public static class Scribe_Collections
    {
        public static void Look<T>(ref List<T>? list, string label, LookMode lookMode = LookMode.Undef) { }
        public static void Look<TKey, TValue>(ref Dictionary<TKey, TValue>? dict, string label,
            LookMode keyLookMode = LookMode.Undef, LookMode valueLookMode = LookMode.Undef) where TKey : notnull { }
    }

    // Find 桩，供 ApprovalManager 获取当前游戏 Tick
    public static class Find
    {
        public static TickManager TickManager { get; set; } = new TickManager();
    }

    public class TickManager
    {
        public int TicksGame { get; set; } = 0;
    }
}

namespace RimWorld.Planet
{
    // World 桩，供 WorldComponent 构造函数使用
    public class World { }

    // WorldComponent 桩，供 AdvisorHistoryStore 继承
    public class WorldComponent
    {
        public WorldComponent(World world) { }
        public virtual void ExposeData() { }
    }
}

namespace RimMind.Application.Common.Models.Client
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

namespace RimMind.Application.Common.Models.UI
{
    // RequestEntry 桩，供 ApprovalManager.SubmitForApproval 使用
    public class RequestEntry
    {
        public string title = "";
        public string description = "";
        public string[] options = Array.Empty<string>();
        public string[]? optionTooltips;
        public Action<string>? callback;
        public object? pawn;
        public string source = "";
        public bool systemBlocked;
        public int expireTicks;
        public int tick;

        public int ExpireAtTicks
        {
            get => expireTicks;
            set => expireTicks = value;
        }
    }
}

namespace RimMind.Application.Common.Interfaces.UI
{
    // 占位接口，ApprovalManager 源码引用此命名空间
    public interface IRequestService { }
}

namespace RimMind.Application.Common.Interfaces.Context
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

namespace RimMind.Presentation
{
    public static class RimMindAPI
    {
        public static void RequestStructuredAsync(object request, string? schema,
            System.Action<object> onComplete, object? tools = null) { }

        // RegisterPendingRequest 桩，存储提交的审批请求以供测试验证
        public static List<RimMind.Application.Common.Models.UI.RequestEntry> PendingRequests { get; } = new();

        public static void RegisterPendingRequest(RimMind.Application.Common.Models.UI.RequestEntry entry)
        {
            PendingRequests.Add(entry);
        }

        public static void ClearPendingRequests() => PendingRequests.Clear();
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
        // 审批相关设置，供 ApprovalManager 测试使用
        public bool enableRiskApproval = true;
        public RimMind.Domain.Enums.RiskLevel autoBlockRiskLevel = RimMind.Domain.Enums.RiskLevel.High;
        public bool enableRequestSystem = true;
    }
}
