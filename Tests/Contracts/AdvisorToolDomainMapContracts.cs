using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RimMind.Advisor.Advisor;
using Xunit;

namespace RimMind.Advisor.Tests.Contracts
{
    public sealed class AdvisorToolDomainMapContracts
    {
        private static readonly Regex ToolIdPattern =
            new Regex("override string Id\\s*=>\\s*\"(?<id>[^\"]+)\"", RegexOptions.Compiled);

        [Fact]
        public void Every_mutating_Actions_composite_tool_id_resolves_an_autonomy_domain()
        {
            // Guards against the dotted-vs-underscored ID drift that silently disabled autonomy checks
            // for actions.emergency_flee / stabilize_rest / eat_and_recreation / triage_patient.
            var actionsDir = Path.Combine(FindRepoRoot(), "RimMind-Actions", "Source", "Actions");
            Assert.True(Directory.Exists(actionsDir), $"Actions source not found at {actionsDir}");

            var ids = Directory.GetFiles(actionsDir, "*CompositeTool.cs")
                .Select(File.ReadAllText)
                .Select(src => ToolIdPattern.Match(src))
                .Where(m => m.Success)
                .Select(m => m.Groups["id"].Value)
                .Where(id => id.IndexOf("query", StringComparison.OrdinalIgnoreCase) < 0) // read-only tools need no domain
                .ToList();

            Assert.NotEmpty(ids);
            var unmapped = ids.Where(id => !AdvisorToolRiskResolver.TryGetAutonomyDomain(id, out _)).ToList();
            Assert.True(unmapped.Count == 0, "Unmapped Actions tool ids: " + string.Join(", ", unmapped));
        }

        [Theory]
        [InlineData("actions.emergency_flee")]
        [InlineData("actions_emergency_flee")]
        [InlineData(" actions.triage_patient ")]
        public void Dotted_and_underscored_ids_resolve_identically(string toolName)
        {
            Assert.True(AdvisorToolRiskResolver.TryGetAutonomyDomain(toolName, out var domain));
            Assert.Equal(RimMind.Domain.Agent.Policies.AutonomyDomain.EmergencySurvival, domain);
        }

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "RimMind-Actions", "Source")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new DirectoryNotFoundException("RimMind repo root not found");
        }
    }
}
