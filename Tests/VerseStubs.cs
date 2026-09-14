using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Mechanisms;

namespace Verse
{
    public static class Log
    {
        public static void Warning(string msg) { }
        public static List<string> Messages { get; } = new();
        public static void Message(string msg) => Messages.Add(msg);
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
        public Name Name = new Name();
        public T? GetComp<T>() where T : class => null;
    }

    public class Name
    {
        public string ToStringFull = "";
        public string ToStringShort = "TestPawn";
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
        public static WorldPawns WorldPawns { get; } = new WorldPawns();
        public static Map? CurrentMap { get; set; }
        public static Selector Selector { get; } = new Selector();
    }

    public sealed class Selector { public object? SingleSelectedThing { get; set; } }
    public sealed class Game { }
    public static class Current { public static Game? Game { get; set; } }
    public sealed class StaticConstructorOnStartupAttribute : Attribute { }

    public static class LongEventHandler
    {
        private static readonly ConcurrentQueue<Action> Pending = new();
        public static TaskCompletionSource<bool> Queued { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public static void ExecuteWhenFinished(Action action)
        {
            Pending.Enqueue(action);
            Queued.TrySetResult(true);
        }
        public static void Drain()
        {
            while (Pending.TryDequeue(out Action? action)) action();
        }
        public static void Reset()
        {
            Pending.Clear();
            Queued = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public class WorldPawns
    {
        public List<Pawn> AllPawnsAlive { get; } = new List<Pawn>();
    }

    public class TickManager
    {
        public int TicksGame { get; set; } = 0;
    }

    public class Map
    {
        public MapPawns mapPawns = new MapPawns();
    }

    public class MapPawns
    {
        public List<Pawn> AllPawns = new List<Pawn>();
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
        public static class Settings
        {
            public static RimMind.Application.Common.Interfaces.Context.IContextBuilder? ContextEngine { get; set; }
            public static RimMind.Application.Common.Interfaces.Context.IContextBuilder? GetContextEngine() => ContextEngine;
            public static bool DebugLogging => false;
        }

        public static bool IsConfigured() => true;
        public static int GetModCooldownTicksLeft(string modId) => 0;
        public static void ClearModCooldown(string modId) { }
        public static IReadOnlyList<RimMind.Application.Common.Models.UI.RequestEntry> GetPendingRequests() => PendingRequests;
        public static class Context
        {
            public static string ScenarioDecision => RimMind.Application.Common.Models.Context.ScenarioIds.Decision;
            public static TestContextRegistry ContextKeys { get; } = new TestContextRegistry();
        }

        public sealed class TestContextRegistry
        {
            public List<RimMind.Application.Common.Interfaces.Context.ContextProviderDef> Definitions { get; } = new();
            public void Register(RimMind.Application.Common.Interfaces.Context.ContextProviderDef definition)
                => Definitions.Add(definition);
        }

        public static class Providers
        {
            public static void RegisterPawnProvider(string id, string owner, Func<Verse.Pawn, string> provider,
                int priority, bool overrideExisting) { }
        }

        public static class Tools
        {
            public static List<RimMind.Application.Common.Models.Tools.ToolDefinition> Definitions { get; } = new();
            public static IReadOnlyList<RimMind.Application.Common.Models.Tools.ToolDefinition> GetAllDefinitions() => Definitions;
            public static object? FindById(string id) => Definitions.Find(definition => definition.Id == id);
        }

        public static class Request
        {
            public static List<RimMind.Domain.Llm.LlmRequestEnvelope> Sent { get; } = new();
            public static void Send(RimMind.Domain.Llm.LlmRequestEnvelope envelope,
                Action<RimMind.Domain.ValueObjects.Result<RimMind.Domain.Llm.LlmResponse, RimMind.Domain.ValueObjects.RimMindError>> callback)
                => Sent.Add(envelope);
            public static void Send(RimMind.Domain.Llm.LlmRequestEnvelope envelope,
                Action<RimMind.Domain.ValueObjects.Result<RimMind.Domain.Llm.LlmResponse, RimMind.Domain.ValueObjects.RimMindError>, RimMind.Application.Common.Models.Pipeline.LlmRequestContext> callback)
                => Sent.Add(envelope);
        }

        public static bool ShouldSkipAction(string id) => false;
        public static void PublishPerception(int pawnId, string kind, string summary, float intensity) { }

        public static void RequestStructuredAsync(object request, string? schema,
            System.Action<object> onComplete, object? tools = null) { }

        // RegisterPendingRequest 桩，存储提交的审批请求以供测试验证
        public static List<RimMind.Application.Common.Models.UI.RequestEntry> PendingRequests { get; } = new();
        public static Action<RimMind.Application.Common.Models.UI.RequestEntry>? RegisterPendingRequestBehavior { get; set; }

        public static void RegisterPendingRequest(RimMind.Application.Common.Models.UI.RequestEntry entry)
        {
            PendingRequests.Add(entry);
            RegisterPendingRequestBehavior?.Invoke(entry);
        }

        public static bool DismissPendingRequest(RimMind.Application.Common.Models.UI.RequestEntry entry)
        {
            if (!PendingRequests.Remove(entry))
                return false;

            return entry.TryComplete(
                null,
                RimMind.Application.Common.Models.UI.RequestCompletionReason.Dismissed);
        }

        public static void ClearPendingRequests()
        {
            PendingRequests.Clear();
            RegisterPendingRequestBehavior = null;
        }

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
        public int requestCooldownTicks = 600;
        public int maxConcurrentRequests = 3;
        // 审批相关设置，供 ApprovalManager 测试使用
        public bool enableRiskApproval = true;
        public bool enableLegacyJsonFallback;
        public RimMind.Domain.Enums.RiskLevel autoBlockRiskLevel = RimMind.Domain.Enums.RiskLevel.High;
        public bool enableRequestSystem = true;
    }
}

namespace LudeonTK
{
    public enum DebugActionType { Action }
    public sealed class DebugActionAttribute : Attribute
    {
        public DebugActionAttribute(string category, string name) { Name = name; }
        public DebugActionType actionType;
        public string Name { get; }
    }
}

namespace RimMind.Advisor
{
    public static class RimMindAdvisorMod
    {
        public static Settings.RimMindAdvisorSettings Settings { get; } = new();
    }
    public static class JobCandidateBuilder
    {
        public static string Build(Verse.Pawn pawn) => string.Empty;
    }
}

namespace RimMind.Advisor.Comps
{
    public sealed class CompAIAdvisor
    {
        public int AdvisorCooldownTicksLeft => 0;
        public bool IsEnabled => true;
        public bool HasPendingRequest => false;
        public void ForceRequestAdvice() { }
    }
}
