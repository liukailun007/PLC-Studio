using System;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>
    /// 发布清单构建器。
    /// 面向交付/商用前的机器可读索引，记录验证过的能力、报告路径、安全红线和仍需阻断的事项。
    /// </summary>
    public static class ReleaseManifestBuilder
    {
        public static JsonObject Build(JsonObject suiteRoot, JsonObject diagnostics, JsonObject runbook)
        {
            if (suiteRoot == null) throw new ArgumentNullException(nameof(suiteRoot));
            if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));
            if (runbook == null) throw new ArgumentNullException(nameof(runbook));

            var reportIndex = diagnostics["reportIndex"] as JsonArray ?? new JsonArray();
            var knownBlocks = runbook["currentKnownBlocks"] as JsonArray ?? new JsonArray();
            var suiteOk = suiteRoot["ok"]?.GetValue<bool>() == true;
            var noFailedItems = (diagnostics["failedItems"] as JsonArray ?? new JsonArray()).Count == 0;
            var hasKnownBlocks = knownBlocks.Count > 0;

            var manifest = new JsonObject
            {
                ["format"] = "tia-mcp-release-manifest-v1",
                ["timestamp"] = DateTime.Now.ToString("O"),
                ["suiteOk"] = suiteOk,
                ["releaseReady"] = suiteOk && noFailedItems && !hasKnownBlocks,
                ["releaseReadinessReason"] = suiteOk && noFailedItems && !hasKnownBlocks
                    ? "All release gates are green and no known blocking signals remain."
                    : "Offline gates are usable, but known blocking signals remain; do not claim final release readiness until they are closed and temporary TIA project validation passes.",
                ["workspaceRoot"] = suiteRoot["workspaceRoot"]?.ToString() ?? "",
                ["reports"] = new JsonObject
                {
                    ["main"] = suiteRoot["markdownPath"]?.ToString() ?? "",
                    ["mainJson"] = suiteRoot["jsonPath"]?.ToString() ?? "",
                    ["diagnostics"] = diagnostics["markdownPath"]?.ToString() ?? suiteRoot["diagnosticMarkdownPath"]?.ToString() ?? "",
                    ["diagnosticsJson"] = diagnostics["jsonPath"]?.ToString() ?? suiteRoot["diagnosticJsonPath"]?.ToString() ?? "",
                    ["runbook"] = runbook["markdownPath"]?.ToString() ?? suiteRoot["runbookMarkdownPath"]?.ToString() ?? "",
                    ["runbookJson"] = runbook["jsonPath"]?.ToString() ?? suiteRoot["runbookJsonPath"]?.ToString() ?? ""
                },
                ["verifiedCapabilities"] = new JsonArray(reportIndex
                    .OfType<JsonObject>()
                    .Where(x => x["ok"]?.GetValue<bool>() == true)
                    .Select(x => new JsonObject
                    {
                        ["id"] = x["id"]?.ToString() ?? "",
                        ["title"] = x["title"]?.ToString() ?? "",
                        ["summary"] = x["summary"]?.ToString() ?? "",
                        ["report"] = x["markdownPath"]?.ToString() ?? ""
                    })
                    .ToArray()),
                ["knownBlocks"] = knownBlocks.DeepClone(),
                ["safetyRedlines"] = diagnostics["safetyRedlines"]?.DeepClone() ?? new JsonArray(),
                ["requiredBeforeDeliveryPackageSync"] = new JsonArray
                {
                    "Run offline release suite and keep OK=true.",
                    "Review diagnostics and runbook.",
                    "Close or explicitly document all known blocks.",
                    "Run temporary TIA V21 project import/readback/SyntaxCheck/compile diagnostics for write-capable flows.",
                    "Only then synchronize changed content into the delivery package when explicitly permitted."
                },
                ["quickStartCommands"] = runbook["quickStartCommands"]?.DeepClone() ?? new JsonArray(),
                ["ok"] = true
            };

            manifest["releaseReadinessGate"] = ReleaseReadinessGateBuilder.Build(suiteRoot, diagnostics, runbook, manifest);
            return manifest;
        }

        public static string BuildMarkdown(JsonObject manifest, string jsonPath)
        {
            var md = new StringBuilder();
            md.AppendLine("# TIA MCP Release Manifest");
            md.AppendLine();
            md.AppendLine("Generated: " + manifest["timestamp"]);
            md.AppendLine("JSON: " + jsonPath);
            md.AppendLine();
            md.AppendLine("## Readiness");
            md.AppendLine("- Suite OK: " + manifest["suiteOk"]);
            md.AppendLine("- Release ready: " + manifest["releaseReady"]);
            md.AppendLine("- Reason: " + manifest["releaseReadinessReason"]);
            if (manifest["releaseReadinessGate"] is JsonObject gate)
            {
                md.AppendLine("- Readiness gate failed: " + gate["failedGateCount"]);
            }
            md.AppendLine();
            md.AppendLine("## Reports");
            var reports = manifest["reports"] as JsonObject ?? new JsonObject();
            md.AppendLine("- Main: " + reports["main"]);
            md.AppendLine("- Diagnostics: " + reports["diagnostics"]);
            md.AppendLine("- Runbook: " + reports["runbook"]);
            md.AppendLine();
            md.AppendLine("## Verified Capabilities");
            foreach (var node in manifest["verifiedCapabilities"] as JsonArray ?? new JsonArray())
            {
                if (node is not JsonObject item) continue;
                md.AppendLine("- " + item["id"] + ": " + item["summary"]);
            }
            md.AppendLine();
            md.AppendLine("## Known Blocks");
            var blocks = manifest["knownBlocks"] as JsonArray ?? new JsonArray();
            if (blocks.Count == 0)
                md.AppendLine("- None.");
            foreach (var node in blocks)
            {
                if (node is not JsonObject block) continue;
                md.AppendLine("- " + block["id"] + ": " + block["status"] + " - " + block["detail"]);
            }
            md.AppendLine();
            md.AppendLine("## Release Readiness Gaps");
            var gaps = manifest["releaseReadinessGate"]?["gaps"] as JsonArray ?? new JsonArray();
            if (gaps.Count == 0)
                md.AppendLine("- None.");
            foreach (var node in gaps)
            {
                if (node is not JsonObject gap) continue;
                md.AppendLine("- " + gap["id"] + ": " + gap["nextAction"]);
            }
            md.AppendLine();
            md.AppendLine("## Before Delivery Package Sync");
            foreach (var item in manifest["requiredBeforeDeliveryPackageSync"] as JsonArray ?? new JsonArray())
            {
                md.AppendLine("- " + item);
            }
            return md.ToString();
        }
    }
}
