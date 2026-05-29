using System;
using System.IO;
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
        public void Advisor_TextFallback_Is_Gated_By_Setting()
        {
            var source = ReadAdvisorSource(Path.Combine("Advisor", "AdvisorTaskDriver.cs"));
            Assert.Contains("_settings.enableLegacyJsonFallback", source);
            Assert.Contains("TryParseContentAsToolCalls", source);
        }
    }
}
