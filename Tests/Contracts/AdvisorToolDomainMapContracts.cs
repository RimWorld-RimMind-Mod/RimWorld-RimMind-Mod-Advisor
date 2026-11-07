using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RimMind.Advisor.Advisor;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Api;
using Verse;
using Xunit;

namespace RimMind.Advisor.Tests.Contracts
{
    [Collection("Advisor runtime")]
    public sealed class AdvisorToolDomainMapContracts
    {
        private static readonly Regex ToolIdPattern =
            new Regex("override string Id\\s*=>\\s*\"(?<id>[^\"]+)\"", RegexOptions.Compiled);
        private static readonly Regex DefaultDomainPattern =
            new Regex("DefaultAutonomyDomain\\s*=>\\s*AutonomyDomain\\.(?<domain>[A-Za-z0-9_]+)", RegexOptions.Compiled);

        [Fact]
        public void AdvisorToolRiskResolver_Contains_No_Hardcoded_Actions_ToolIds_Or_DomainMap()
        {
            var resolverPath = Path.Combine(
                FindRepoRoot(),
                "RimMind-Advisor",
                "Source",
                "Advisor",
                "AdvisorToolRiskResolver.cs");
            Assert.True(File.Exists(resolverPath), $"Missing {resolverPath}");
            var source = File.ReadAllText(resolverPath);

            Assert.DoesNotContain("ToolAutonomyDomainMap", source, StringComparison.Ordinal);
            Assert.DoesNotContain("actions_", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("actions.", source, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ClassifyPolicy", source, StringComparison.Ordinal);
        }

        [Fact]
        public void Every_mutating_Actions_composite_tool_id_resolves_an_autonomy_domain_via_manifest()
        {
            var actionsDir = Path.Combine(FindRepoRoot(), "RimMind-Actions", "Source", "Actions");
            Assert.True(Directory.Exists(actionsDir), $"Actions source not found at {actionsDir}");

            RimMindAPI.Tools.Definitions.Clear();
            RimMindAPI.Tools.Handlers.Clear();
            try
            {
                var files = Directory.GetFiles(actionsDir, "*CompositeTool.cs");
                var mutatingIds = new System.Collections.Generic.List<string>();

                foreach (var file in files)
                {
                    var src = File.ReadAllText(file);
                    var idMatch = ToolIdPattern.Match(src);
                    if (!idMatch.Success) continue;

                    var id = idMatch.Groups["id"].Value;
                    if (id.IndexOf("query", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                    var domainMatch = DefaultDomainPattern.Match(src);
                    Assert.True(domainMatch.Success, $"Composite tool '{id}' must declare DefaultAutonomyDomain in its manifest");
                    var domain = (AutonomyDomain)Enum.Parse(typeof(AutonomyDomain), domainMatch.Groups["domain"].Value);

                    RimMindAPI.Tools.Definitions.Add(new ToolDefinition
                    {
                        Id = id,
                        Manifest = new ToolManifest
                        {
                            OwnerModId = "RimMindActions",
                            DefaultDomain = domain,
                            AutonomyDomainId = domain.ToString()
                        }
                    });
                    mutatingIds.Add(id);
                }

                Assert.NotEmpty(mutatingIds);
                var unmapped = mutatingIds
                    .Where(id => !AdvisorToolRiskResolver.TryGetAutonomyDomain(id, out _))
                    .ToList();
                Assert.True(unmapped.Count == 0, "Unmapped Actions tool ids: " + string.Join(", ", unmapped));
            }
            finally
            {
                RimMindAPI.Tools.Definitions.Clear();
                RimMindAPI.Tools.Handlers.Clear();
            }
        }

        [Theory]
        [InlineData("actions.emergency_flee")]
        [InlineData("actions_emergency_flee")]
        [InlineData("emergency_flee")]
        [InlineData(" actions.triage_patient ")]
        public void Dotted_underscored_and_alias_ids_resolve_identically_from_manifest(string toolName)
        {
            RimMindAPI.Tools.Definitions.Clear();
            try
            {
                RimMindAPI.Tools.Definitions.Add(new ToolDefinition
                {
                    Id = "actions.emergency_flee",
                    Manifest = new ToolManifest
                    {
                        DefaultDomain = AutonomyDomain.EmergencySurvival,
                        Aliases = new[] { "emergency_flee", "actions_emergency_flee" }
                    }
                });
                RimMindAPI.Tools.Definitions.Add(new ToolDefinition
                {
                    Id = "actions.triage_patient",
                    Manifest = new ToolManifest
                    {
                        DefaultDomain = AutonomyDomain.EmergencySurvival,
                        Aliases = new[] { "triage_patient", "actions_triage_patient" }
                    }
                });

                Assert.True(AdvisorToolRiskResolver.TryGetAutonomyDomain(toolName, out var domain));
                Assert.Equal(AutonomyDomain.EmergencySurvival, domain);
            }
            finally
            {
                RimMindAPI.Tools.Definitions.Clear();
            }
        }

        [Fact]
        public void Dynamic_IToolPolicyClassifier_escalates_survival_bed_and_meal_bill_fixing_B4()
        {
            var pawn = new Pawn { thingIDNumber = 7 };
            RimMindAPI.Autonomy.Policy = AgentAutonomyPolicy.CreateDefault();
            RimMindAPI.Tools.Handlers.Clear();
            RimMindAPI.Tools.Definitions.Clear();
            try
            {
                RimMindAPI.Tools.Handlers.Add(new DynamicBlueprintClassifierHandler());

                // Static manifest lookup (no args) resolves to BlueprintPlacement
                Assert.Equal(
                    AutonomyDomain.BlueprintPlacement,
                    AdvisorToolRiskResolver.ResolveAutonomyDomain("actions_place_blueprint"));
                Assert.Equal(
                    RiskLevel.Medium,
                    AdvisorToolRiskResolver.Resolve("actions_place_blueprint"));

                // Survival Bed args escalate to EmergencySurvival (FullAutonomous by default -> no approval required!)
                var bedArgs = new ToolCallArgs
                {
                    ToolName = "actions_place_blueprint",
                    ArgumentsJson = "{\"building_def\":\"Bed\"}",
                    PawnId = pawn.thingIDNumber
                };
                Assert.Equal(
                    AutonomyDomain.EmergencySurvival,
                    AdvisorToolRiskResolver.ResolveAutonomyDomain("actions_place_blueprint", bedArgs));
                Assert.Equal(
                    RiskLevel.Low,
                    AdvisorToolRiskResolver.Resolve("actions_place_blueprint", bedArgs));
                Assert.False(
                    AdvisorToolRiskResolver.RequiresAutonomyApproval(pawn, "actions_place_blueprint", bedArgs));

                // Luxury statue args stay in BlueprintPlacement (RequireApproval by default -> requires approval!)
                var statueArgs = new ToolCallArgs
                {
                    ToolName = "actions_place_blueprint",
                    ArgumentsJson = "{\"building_def\":\"SculptureSmall\"}",
                    PawnId = pawn.thingIDNumber
                };
                Assert.Equal(
                    AutonomyDomain.BlueprintPlacement,
                    AdvisorToolRiskResolver.ResolveAutonomyDomain("actions_place_blueprint", statueArgs));
                Assert.Equal(
                    RiskLevel.Medium,
                    AdvisorToolRiskResolver.Resolve("actions_place_blueprint", statueArgs));
                Assert.True(
                    AdvisorToolRiskResolver.RequiresAutonomyApproval(pawn, "actions_place_blueprint", statueArgs));
            }
            finally
            {
                RimMindAPI.Tools.Handlers.Clear();
                RimMindAPI.Tools.Definitions.Clear();
                RimMindAPI.Autonomy.Policy = AgentAutonomyPolicy.CreateDefault();
            }
        }

        private sealed class DynamicBlueprintClassifierHandler : IToolHandler, IToolPolicyClassifier
        {
            public string Id => "actions_place_blueprint";
            public string OwnerModId => "RimMindActions";

            public ToolDefinition Definition { get; } = new ToolDefinition
            {
                Id = "actions_place_blueprint",
                Manifest = new ToolManifest
                {
                    OwnerModId = "RimMindActions",
                    DefaultDomain = AutonomyDomain.BlueprintPlacement,
                    RiskLevel = RiskLevel.Medium,
                    Aliases = new[] { "place_blueprint", "actions.place_blueprint" }
                }
            };

            public ToolPolicyDecision Classify(ToolCallArgs args)
            {
                string? buildingDef = null;
                if (!string.IsNullOrWhiteSpace(args?.ArgumentsJson))
                {
                    try
                    {
                        buildingDef = JObject.Parse(args!.ArgumentsJson)["building_def"]?.ToString();
                    }
                    catch
                    {
                    }
                }

                bool isSurvival = string.Equals(buildingDef, "Bed", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(buildingDef, "Campfire", StringComparison.OrdinalIgnoreCase);
                var domain = isSurvival ? AutonomyDomain.EmergencySurvival : AutonomyDomain.BlueprintPlacement;
                return new ToolPolicyDecision
                {
                    Domain = domain,
                    DomainId = domain.ToString(),
                    Risk = isSurvival ? RiskLevel.Low : RiskLevel.Medium,
                    RequiresApproval = false
                };
            }

            public Task<Result<ToolResult, RimMindError>> ExecuteAsync(ToolCallArgs args, CancellationToken ct)
                => Task.FromResult(Result<ToolResult, RimMindError>.Ok(ToolResult.Ok("ok", args.ToolCallId, Id)));
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
