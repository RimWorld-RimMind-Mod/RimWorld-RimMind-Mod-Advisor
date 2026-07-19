using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace RimMind.Advisor.Tests
{
    public class AdvisorToolCallArchitectureTests
    {
        private static readonly string RepoRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        private static string ReadAdvisorSource(string relativePath)
        {
            var path = Path.Combine(RepoRoot, "RimMind-Advisor", "Source", relativePath);
            Assert.True(File.Exists(path), $"Missing source file: {path}");
            return File.ReadAllText(path);
        }

        private static string ReadAdvisorFile(string relativePath)
        {
            var path = Path.Combine(RepoRoot, "RimMind-Advisor", relativePath);
            Assert.True(File.Exists(path), $"Missing Advisor file: {path}");
            return File.ReadAllText(path);
        }

        [Fact]
        public void Advisor_Defaults_LegacyJsonFallback_Off()
        {
            var source = ReadAdvisorSource(Path.Combine("Settings", "RimMindAdvisorSettings.cs"));
            Assert.Contains("enableLegacyJsonFallback = false", source);
        }

        [Fact]
        public void Advisor_Uses_ManualToolDispatch()
        {
            var source = ReadAdvisorSource(Path.Combine("Advisor", "AdvisorTaskDriver.cs"));
            Assert.Contains("WithToolDispatchMode(ToolCallDispatchMode.Manual)", source);
        }

        [Fact]
        public void Advisor_Feedback_Does_Not_Require_ResponseSchema()
        {
            var source = ReadAdvisorSource(Path.Combine("Advisor", "AdvisorTaskDriver.cs"));
            Assert.DoesNotContain("_lastSchema != null", source);
            Assert.Contains("_lastMessages != null", source);
            Assert.Contains("_lastTools != null", source);
        }

        [Fact]
        public void Advisor_TextFallback_Is_Gated_By_Setting()
        {
            var source = ReadAdvisorSource(Path.Combine("Advisor", "AdvisorTaskDriver.cs"));
            Assert.Contains("_settings.enableLegacyJsonFallback", source);
            Assert.Contains("TryParseContentAsToolCalls", source);
        }

        [Fact]
        public void Advisor_RiskResolver_Uses_Core_Mechanism_Metadata()
        {
            var source = ReadAdvisorSource(Path.Combine("Advisor", "AdvisorToolRiskResolver.cs"));
            Assert.Contains("var mechanismRegistry = RimMindAPI.Mechanisms", source);
            Assert.Contains("mechanismRegistry.FindById", source);
            Assert.Contains("GetRiskForOperation", source);
            Assert.Contains("MechanismRisk.Dangerous", source);
        }

        [Fact]
        public void Advisor_RiskResolver_Uses_Dictionary_For_Operation_Suffix_Mapping()
        {
            var source = ReadAdvisorSource(Path.Combine("Advisor", "AdvisorToolRiskResolver.cs"));
            Assert.Contains("OperationSuffixMap", source);
            Assert.Contains("Dictionary<string, MechanismOperationType>", source);
            Assert.Contains("StringComparer.OrdinalIgnoreCase", source);
            Assert.Contains("TryGetValue", source);
            Assert.DoesNotContain("switch (suffix", source);
        }

        [Fact]
        public void Advisor_ToolCallExecutor_Uses_Core_ToolRegistry()
        {
            var source = ReadAdvisorSource(Path.Combine("Advisor", "AdvisorToolCallExecutor.cs"));
            Assert.Contains("RimMindAPI.Tools.FindById", source);
            Assert.Contains("ToolCallArgs", source);
            Assert.Contains("ExecuteAsync", source);
        }

        [Fact]
        public void Advisor_Component_Does_Not_Use_Deprecated_ActionsApi()
        {
            var source = ReadAdvisorSource(Path.Combine("Comps", "CompAIAdvisor.cs"));
            Assert.DoesNotContain(Forbidden("RimMind", "ActionsAPI"), source);
            Assert.DoesNotContain(Forbidden("Batch", "ActionIntent"), source);
            Assert.Contains("ExecuteToolCallsSafely", source);
            Assert.Contains("ToolResult.Fail", source);
        }

        [Fact]
        public void Advisor_Component_Has_No_Duplicate_BroadcastDecisionExecuted()
        {
            // Task 8: the private duplicate BroadcastDecisionExecuted must be removed;
            // only the public AdvisorTaskDriver.BroadcastDecisionExecuted should be called via the captured driver.
            var source = ReadAdvisorSource(Path.Combine("Comps", "CompAIAdvisor.cs"));
            Assert.DoesNotContain("private void BroadcastDecisionExecuted", source);
            Assert.Contains("taskDriver.BroadcastDecisionExecuted(toolCall.Name, reason);", source);
            Assert.Contains("taskDriver.BroadcastDecisionExecuted(call.Name,", source);
            Assert.DoesNotContain("_taskDriver?.BroadcastDecisionExecuted", source);
        }

        [Fact]
        public void Advisor_Component_Reports_When_MainThread_Tool_Execution_Is_Asynchronous()
        {
            var source = ReadAdvisorSource(Path.Combine("Comps", "CompAIAdvisor.cs"));

            Assert.Contains("executionTask.IsCompleted", source);
            Assert.Contains("[ToolCall][MainThreadWait]", source);
            Assert.DoesNotContain("Task.Run(() => _toolExecutor.ExecuteAsync", source);
        }

        [Fact]
        public void Advisor_Component_Centralizes_Approval_Deferral()
        {
            // Task 9: approval deferral check must be centralized in ShouldDeferForApproval,
            // replacing the inlined systemBlocked / isRequest logic at the call site.
            var source = ReadAdvisorSource(Path.Combine("Comps", "CompAIAdvisor.cs"));
            Assert.Contains("private bool ShouldDeferForApproval(RiskLevel riskLevel, string? arguments)", source);
            Assert.Contains("if (ShouldDeferForApproval(riskLevel, tc.Arguments))", source);
            // The inlined call-site patterns must be gone. The centralized method uses the
            // `arguments` parameter, so the `tc.Arguments` variant and the inlined if-check
            // must no longer appear (the method returns systemBlocked || isRequest instead).
            Assert.DoesNotContain("IsToolCallRequest(tc.Arguments)", source);
            Assert.DoesNotContain("if (systemBlocked || isRequest)", source);
        }

        [Fact]
        public void Advisor_Component_AllExecutionPaths_UseOneCentralizedFeedback_Loop()
        {
            var source = ReadAdvisorSource(Path.Combine("Comps", "CompAIAdvisor.cs"));

            int feedbackChecks = Regex.Matches(source, @"ShouldRequestFeedback\(\)").Count;
            int feedbackRequests = Regex.Matches(source, @"RequestToolFeedback\(").Count;
            Assert.Equal(1, feedbackChecks);
            Assert.Equal(1, feedbackRequests);
            Assert.Contains("QueueFeedbackBatch(requestCycle, approvedCalls, results);", source);
            Assert.Contains("QueueFeedbackBatch(requestCycle, calls, results);", source);
        }

        [Fact]
        public void Advisor_Component_Approval_Callbacks_Keep_Their_Originating_Driver()
        {
            var source = ReadAdvisorSource(Path.Combine("Comps", "CompAIAdvisor.cs"));

            Assert.Contains("SubmitToolCallForApproval(tc, riskLevel, taskDriver, requestCycle)", source);
            Assert.Contains("AdvisorTaskDriver taskDriver,", source);
            Assert.Contains("AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult> requestCycle)", source);
            Assert.Contains("taskDriver.BroadcastDecisionExecuted(toolCall.Name, reason);", source);
            Assert.DoesNotContain("_taskDriver?.BroadcastDecisionExecuted(toolCall.Name, reason);", source);
        }

        [Fact]
        public void Advisor_Component_Tracks_Approval_Lifecycle_Before_Completing_Request()
        {
            var source = ReadAdvisorSource(Path.Combine("Comps", "CompAIAdvisor.cs"));

            Assert.Contains("AdvisorRequestCycleState<ClientStructuredToolCall, ToolResult>? _requestCycle", source);
            Assert.Contains("requestCycle.TrackPendingApproval(", source);
            Assert.Contains("requestCycle.TryFinishApproval(approvalEntry)", source);
            Assert.Contains("completedCycle?.CancelPendingApprovals();", source);
            Assert.Contains("TryAdvanceRequestCycle(taskDriver, requestCycle);", source);
            Assert.DoesNotContain("_pendingApprovalCount", source);
        }

        [Fact]
        public void Advisor_Source_And_Project_Do_Not_Use_Actions_Dependency()
        {
            var sourceRoot = Path.Combine(RepoRoot, "RimMind-Advisor", "Source");
            var forbiddenNames = new[]
            {
                Forbidden("RimMind", ".Actions"),
                Forbidden("RimMind", "ActionsAPI"),
                Forbidden("Batch", "ActionIntent"),
                Forbidden("RimMind", "Actions")
            };

            foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
            {
                var source = StripComments(File.ReadAllText(file));
                foreach (var forbiddenName in forbiddenNames)
                {
                    Assert.DoesNotContain(forbiddenName, source);
                }
            }

            var project = ReadAdvisorSource("RimMindAdvisor.csproj");
            foreach (var forbiddenName in forbiddenNames)
            {
                Assert.DoesNotContain(forbiddenName, project);
            }
        }

        [Fact]
        public void Advisor_Candidate_Context_Uses_Core_ToolRegistry()
        {
            var modSource = ReadAdvisorSource("RimMindAdvisorMod.cs");
            var candidateSource = ReadAdvisorSource(Path.Combine("Advisor", "JobCandidateBuilder.cs"));

            Assert.Contains("RimMindAPI.Tools.GetAllDefinitions", modSource);
            Assert.Contains("RimMindAPI.Tools.GetAllDefinitions", candidateSource);
        }

        [Fact]
        public void Advisor_NormalPromptKeys_Do_Not_Advertise_LegacyJsonFallback()
        {
            var languageFiles = new[]
            {
                Path.Combine("Languages", "English", "Keyed", "RimMind_Advisor.xml"),
                Path.Combine("Languages", "ChineseSimplified", "Keyed", "RimMind_Advisor.xml")
            };

            var forbiddenPhrases = new[]
            {
                "fallback to JSON",
                "JSON fallback",
                "fallback JSON",
                "legacy JSON fallback",
                "text JSON advice",
                "回退到 JSON",
                "JSON回退",
                "JSON 回退",
                "旧版 JSON 回退",
                "文本 JSON 建议"
            };

            foreach (var languageFile in languageFiles)
            {
                var xml = ReadAdvisorFile(languageFile);
                var normalPromptValues = ExtractNormalPromptInstructionValues(xml);

                foreach (var value in normalPromptValues)
                {
                    foreach (var phrase in forbiddenPhrases)
                    {
                        Assert.DoesNotContain(phrase, value, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
        }

        private static IEnumerable<string> ExtractNormalPromptInstructionValues(string xml)
        {
            var matches = Regex.Matches(
                xml,
                @"<RimMind\.Advisor\.Prompt\.TaskInstruction\.[^>]+>(.*?)</RimMind\.Advisor\.Prompt\.TaskInstruction\.[^>]+>",
                RegexOptions.Singleline);

            foreach (Match match in matches)
            {
                yield return match.Groups[1].Value;
            }
        }

        private static string Forbidden(string left, string right) => left + right;

        private static string StripComments(string source)
        {
            source = Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline);
            return Regex.Replace(source, @"//.*?$", "", RegexOptions.Multiline);
        }
    }
}
