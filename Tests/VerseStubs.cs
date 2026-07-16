using System;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Mechanisms;

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
        // Name 桩，供 AdvisorApprovalGateAdapter.FindTargetPawn 按 Name.ToStringFull 匹配
        public Name? Name;
    }

    // Name 桩，供 AdvisorApprovalGateAdapter.FindTargetPawn 使用 pawn.Name?.ToStringFull
    public class Name
    {
        public string ToStringFull = "";
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
        public static List<Map> Maps { get; } = new List<Map>();
    }

    public class TickManager
    {
        public int TicksGame { get; set; } = 0;
    }

    // Map 桩，供 AdvisorApprovalGateAdapter 查找 Pawn
    public class Map
    {
        public MapPawns mapPawns = new MapPawns();
    }

    // MapPawns 桩，供 AdvisorApprovalGateAdapter 遍历 AllPawns / FreeColonists
    public class MapPawns
    {
        public List<Pawn> AllPawns = new List<Pawn>();
        // FreeColonists 桩，供 AdvisorApprovalGateAdapter.RequestApproval 回退到第一个殖民者
        public List<Pawn> FreeColonists = new List<Pawn>();
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

namespace RimMind.Presentation.Api
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

        // Mechanisms 桩，供 AdvisorToolRiskResolver 编译使用（测试时返回 null -> Resolve 返回 Low）
        public static IGameMechanismRegistry? Mechanisms => null;
    }
}

namespace RimMind.Presentation
{
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
