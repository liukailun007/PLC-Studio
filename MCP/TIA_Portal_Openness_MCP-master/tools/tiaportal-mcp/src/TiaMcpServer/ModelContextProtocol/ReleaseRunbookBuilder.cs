using System;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>
    /// 发布运行手册构建器。
    /// 面向第一次拿到项目/交付包的人，说明先跑什么、怎么看报告、哪些安全红线不能碰。
    /// </summary>
    public static class ReleaseRunbookBuilder
    {
        public static JsonObject Build(JsonObject suiteRoot, JsonObject diagnostics)
        {
            if (suiteRoot == null) throw new ArgumentNullException(nameof(suiteRoot));
            if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));

            var workspaceRoot = suiteRoot["workspaceRoot"]?.ToString() ?? "";
            var mainReport = suiteRoot["markdownPath"]?.ToString() ?? "";
            var reportDirectory = "";
            if (!string.IsNullOrWhiteSpace(mainReport))
            {
                reportDirectory = System.IO.Path.GetDirectoryName(mainReport) ?? "";
            }
            if (string.IsNullOrWhiteSpace(reportDirectory))
            {
                reportDirectory = suiteRoot["suiteDirectory"]?.ToString() ?? "";
            }
            var nextActions = diagnostics["recommendedNextActions"] as JsonArray ?? new JsonArray();
            var observations = diagnostics["observations"]?["items"] as JsonArray ?? new JsonArray();

            return new JsonObject
            {
                ["format"] = "tia-mcp-release-runbook-v1",
                ["timestamp"] = DateTime.Now.ToString("O"),
                ["workspaceRoot"] = workspaceRoot,
                ["suiteOk"] = suiteRoot["ok"]?.GetValue<bool>() == true,
                ["mainReport"] = mainReport,
                ["diagnosticReport"] = suiteRoot["diagnosticMarkdownPath"]?.ToString() ?? diagnostics["markdownPath"]?.ToString() ?? "",
                ["reportDirectory"] = reportDirectory,
                ["quickStartCommands"] = new JsonArray
                {
                    "dotnet build \"" + workspaceRoot + "\\tools\\tiaportal-mcp\\TiaMcpServer.sln\" -c Release",
                    "dotnet run --project \"" + workspaceRoot + "\\tools\\tiaportal-mcp\\src\\TiaMcpServer\\TiaMcpServer.csproj\" -c Release -- --tia-major-version 21 --run-offline-release-suite --offline-release-suite-report-directory \"" + workspaceRoot + "\\reports\\offline_release_suite\"",
                    "dotnet run --project \"" + workspaceRoot + "\\tools\\tiaportal-mcp\\src\\TiaMcpServer\\TiaMcpServer.csproj\" -c Release -- --tia-major-version 21 --run-online-monitoring-safety-self-test"
                },
                ["handoffChecklist"] = new JsonArray
                {
                    "确认 D:\\app\\TIA21\\Portal V21\\PublicAPI\\V21\\net48 可用。",
                    "确认当前 Windows 用户属于 Siemens TIA Openness 组。",
                    "先跑 offline release suite，再看 diagnostics 和 runbook。",
                    "真实工程写入前必须先用临时 TIA 工程验证导入、读回、SyntaxCheck/编译诊断。",
                    "交付包只有在明确允许时才同步更新。"
                },
                ["safetyRedlines"] = diagnostics["safetyRedlines"]?.DeepClone() ?? new JsonArray(),
                ["currentKnownBlocks"] = new JsonArray(observations
                    .OfType<JsonObject>()
                    .Where(x => x["blocking"]?.GetValue<bool>() == true)
                    .Select(x => new JsonObject
                    {
                        ["id"] = x["id"]?.ToString() ?? "",
                        ["status"] = x["status"]?.ToString() ?? "",
                        ["detail"] = x["detail"]?.ToString() ?? ""
                    })
                    .ToArray()),
                ["nextActions"] = nextActions.DeepClone(),
                ["ok"] = true
            };
        }

        public static string BuildMarkdown(JsonObject runbook, string jsonPath)
        {
            var md = new StringBuilder();
            md.AppendLine("# TIA MCP Release Runbook");
            md.AppendLine();
            md.AppendLine("Generated: " + runbook["timestamp"]);
            md.AppendLine("JSON: " + jsonPath);
            md.AppendLine();
            md.AppendLine("## Purpose");
            md.AppendLine("This runbook is for a first-time agent or engineer receiving the package. It explains the verified entry points, safety redlines, and current known blocks without needing prior chat context.");
            md.AppendLine();
            md.AppendLine("## Start Here");
            md.AppendLine("- Workspace: " + runbook["workspaceRoot"]);
            md.AppendLine("- Main report: " + runbook["mainReport"]);
            md.AppendLine("- Diagnostic report: " + runbook["diagnosticReport"]);
            md.AppendLine("- Suite OK: " + runbook["suiteOk"]);
            md.AppendLine();
            md.AppendLine("## Quick Commands");
            foreach (var command in runbook["quickStartCommands"] as JsonArray ?? new JsonArray())
            {
                md.AppendLine("```powershell");
                md.AppendLine(command?.ToString() ?? "");
                md.AppendLine("```");
            }
            md.AppendLine();
            md.AppendLine("## Handoff Checklist");
            foreach (var item in runbook["handoffChecklist"] as JsonArray ?? new JsonArray())
            {
                md.AppendLine("- " + item);
            }
            md.AppendLine();
            md.AppendLine("## Safety Redlines");
            foreach (var item in runbook["safetyRedlines"] as JsonArray ?? new JsonArray())
            {
                md.AppendLine("- " + item);
            }
            md.AppendLine();
            md.AppendLine("## Current Known Blocks");
            var blocks = runbook["currentKnownBlocks"] as JsonArray ?? new JsonArray();
            if (blocks.Count == 0)
            {
                md.AppendLine("- No blocking signals in the latest diagnostic report.");
            }
            foreach (var node in blocks)
            {
                if (node is not JsonObject block) continue;
                md.AppendLine("- " + block["id"] + ": " + block["status"] + " - " + block["detail"]);
            }
            md.AppendLine();
            md.AppendLine("## Next Actions");
            foreach (var item in runbook["nextActions"] as JsonArray ?? new JsonArray())
            {
                md.AppendLine("- " + item);
            }
            return md.ToString();
        }
    }
}
