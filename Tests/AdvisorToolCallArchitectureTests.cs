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
            Assert.Contains("RimMindAPI.Mechanisms.FindById", source);
            Assert.Contains("GetRiskForOperation", source);
            Assert.Contains("MechanismRisk.Dangerous", source);
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
    }
}
