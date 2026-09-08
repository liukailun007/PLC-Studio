using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>
    /// V2 计划严格完成度审计器。
    /// 用途：把 docs/TIA_MCP_常见操作全覆盖方案_V2_二次优化计划.md 中的关键验收项拆成可复查证据，
    /// 防止离线套件通过后误把“仍需真实 TIA/在线目标验证”的项目标成 100%。
    /// </summary>
    public static class V2PlanCompletionAuditor
    {
        public static JsonObject Run(string workspaceRoot, string reportDirectory)
        {
            if (string.IsNullOrWhiteSpace(workspaceRoot))
                throw new ArgumentException("Workspace root is required.", nameof(workspaceRoot));
            if (string.IsNullOrWhiteSpace(reportDirectory))
                throw new ArgumentException("Report directory is required.", nameof(reportDirectory));

            workspaceRoot = Path.GetFullPath(workspaceRoot);
            reportDirectory = Path.GetFullPath(reportDirectory);
            Directory.CreateDirectory(reportDirectory);

            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var repoRoot = Path.Combine(workspaceRoot, "tools", "tiaportal-mcp");
            var mcpServerPath = Path.Combine(repoRoot, "src", "TiaMcpServer", "ModelContextProtocol", "McpServer.cs");
            var portalPath = Path.Combine(repoRoot, "src", "TiaMcpServer", "Siemens", "Portal.cs");
            var testDir = Path.Combine(repoRoot, "tests", "TiaMcpServer.Test");
            var docsDir = Path.Combine(repoRoot, "docs");
            var planPath = Path.Combine(workspaceRoot, "docs", "TIA_MCP_常见操作全覆盖方案_V2_二次优化计划.md");
            var releaseReportDir = Path.Combine(workspaceRoot, "reports", "offline_release_suite");
            var realValidationText = LoadLatestRealValidationEvidenceText(Path.Combine(workspaceRoot, "reports"));

            var evidence = new EvidenceIndex(
                ReadText(mcpServerPath),
                ReadText(portalPath),
                Directory.Exists(testDir) ? string.Join("\n", Directory.EnumerateFiles(testDir, "*.cs").Select(ReadText)) : "",
                Directory.Exists(docsDir) ? string.Join("\n", Directory.EnumerateFiles(docsDir, "*.md", SearchOption.AllDirectories).Select(ReadText)) : "",
                ReadText(planPath),
                LoadLatestReleaseSuiteJson(releaseReportDir),
                realValidationText);

            var items = new List<JsonObject>
            {
                Item("plc-builder-tools-exposed", "PLC builder 工具已暴露为 MCP 工具", true,
                    HasAll(evidence.McpServer, "BuildPlcUdtXml", "BuildPlcTagTableXml", "BuildPlcGlobalDbXml", "BuildStructuredTextXml", "BuildFlgNetCallXml", "ComposePlcFcBlockXml"),
                    "McpServer.cs 中应能找到 UDT/TagTable/GlobalDB/ST/FlgNet/FC builder 工具。"),
                Item("plc-build-and-import-dryrun", "PlcBuildAndImport 默认 dryRun 且有回归测试", true,
                    evidence.McpServer.Contains("PlcBuildAndImport") && evidence.McpServer.Contains("Default dryRun=true") && evidence.Tests.Contains("Test_PlcBuildAndImport_DryRunWritesAndClassifiesXml"),
                    "一步式工具默认不得连接或写入 TIA，必须有 dryRun 测试。"),
                Item("plc-fb-builder", "PLC FB 至少可由结构化输入 build+import", true,
                    HasAny(evidence.McpServer, "ComposePlcFbBlockXml", "BuildPlcFbXml", "kind: udt|tagtable|globaldb|fc|fb"),
                    "V2 验收写明 UDT/TagTable/DB/FC/FB 至少可 build+import；当前没有 FB 时不能标 100%。"),
                Item("plc-golden-fixtures", "PLC builder golden fixtures 离线回归通过", true,
                    LatestSuiteItemOk(evidence.ReleaseSuite, "plc-builder-offline-suite") && evidence.Tests.Contains("Test_PlcBuilderMcpTools"),
                    "离线套件需覆盖 TMP_EXPORT/_verify 金样本，并且测试存在。"),
                Item("partial-split-risk-reduction", "Portal/McpServer/Program 轻拆分或风险收敛已落地", false,
                    Directory.EnumerateFiles(Path.Combine(repoRoot, "src", "TiaMcpServer"), "*.cs", SearchOption.AllDirectories).Any(p => Path.GetFileName(p).StartsWith("Portal.", StringComparison.OrdinalIgnoreCase)) ||
                    evidence.McpServer.Contains("PlcBuilderToolJson") ||
                    evidence.McpServer.Contains("HmiActionScriptRecipeBuilder"),
                    "V2 建议是降风险项，不作为 100% 功能硬门，但需要记录已做的边界抽离。"),
                Item("unified-template-layout-offline", "Unified 模板布局与美观 QA 可离线验证", true,
                    LatestSuiteItemOk(evidence.ReleaseSuite, "unified-hmi-template-layout") && evidence.McpServer.Contains("BuildUnifiedHmiTemplateApplyDesignJson"),
                    "模板布局 QA 和 ApplyDesignJson 生成入口必须存在并通过离线套件。"),
                Item("unified-theme-layout-online-tool", "Unified Theme/Layout 高层在线工具已具备真实应用入口", true,
                    HasAny(evidence.McpServer, "ApplyUnifiedHmiTheme", "ApplyUnifiedHmiLayout", "HmiUnified.ApplyTheme", "HmiUnified.ApplyLayout"),
                    "V2 5.1 明确要求 Theme/Layout 高阶入口；只有通用 ApplyUnifiedHmiScreenDesignJson 不等于完成。"),
                Item("unified-button-action", "Unified ButtonAction 配方化并受安全测试保护", true,
                    evidence.McpServer.Contains("EnsureUnifiedHmiButtonAction") && evidence.Tests.Contains("TestHmiUnifiedActionRecipes") && LatestSuiteItemOk(evidence.ReleaseSuite, "hmi-action-script-recipe"),
                    "按钮动作配方、安全阻断和离线探针必须同时存在。"),
                Item("unified-action-syntaxcheck-real", "Unified 按钮动作 SyntaxCheck 0 error 真实证据", true,
                    HasAny(evidence.RealValidationText, "SyntaxCheck 0 error", "syntaxErrorCount=0", "\"syntaxErrorCount\": 0"),
                    "V2 硬门要求脚本 SyntaxCheck 0 error；纯离线配方生成不能替代真实 WinCC SyntaxCheck。"),
                Item("global-library-template-reuse", "GlobalLibrary 模板学习 + MCP 原生重建商用替代路线", true,
                    HasAll(evidence.McpServer, "PlanGlobalLibraryTemplateReuse", "template-learn-and-native-rebuild", "directMasterCopyImportRequired") &&
                    evidence.Tests.Contains("Test_PlanGlobalLibraryTemplateReuse_IsOfflineNativeRebuildFallback") &&
                    HasAny(evidence.RealValidationText, "MasterCopy found", "ScreenItems exposed only", "Extension candidate methods: none", "masterCopyImportReadbackOk\": false"),
                    "真实 TIA V21 证据显示直接 MasterCopy 导入未公开可验证；V2 改为商用可复用路线：全局库学习 + Theme/Layout/Action 原生重建 + 读回验证。"),
                Item("online-readonly-safety", "在线监视只读安全红线已验证", true,
                    LatestSuiteItemOk(evidence.ReleaseSuite, "online-monitoring-safety") && evidence.Tests.Contains("TestOnlineMonitoringSafety") && evidence.McpServer.Contains("PlanOnlineReadOnlyMonitoring"),
                    "不得暴露 Force/在线写/监控表修改；预检工具和安全测试必须通过。"),
                Item("online-readonly-data-provider-plan", "在线当前值改为 OPC UA/S7 只读 DataProvider 商用路线", true,
                    HasAll(evidence.McpServer, "PlanOnlineReadOnlyDataProvider", "planned-read-only-provider", "usesTiaOpennessForCurrentValues") &&
                    evidence.Tests.Contains("Test_PlanOnlineReadOnlyDataProvider_UsesExternalReadOnlyProvider") &&
                    HasAny(evidence.RealValidationText, "No explicit current/monitor value property was readable", "entryCountRead\": 10", "currentValueReadOk\": false"),
                    "真实 TIA V21 证据显示 Openness 只暴露监控表定义，不暴露 UI 当前值；V2 改为可商用只读 DataProvider：OPC UA 首选，S7 read-only 备选。"),
                Item("hardware-network-primitives", "硬件网络原语工具族具备读回证据", true,
                    HasAll(evidence.McpServer, "PlanHardwareNetworkConfiguration", "EnsureSubnet", "AttachDeviceNodeToSubnet", "SetCpuCommonSettings") &&
                    evidence.Tests.Contains("TestHardwareNetworkPrimitives") &&
                    evidence.Docs.Contains("hardware-network"),
                    "V2 7/10 要求子网/连接/IP 配置可由原语工具组合完成且读回；需要工具、测试和 docs/tools/hardware-network.md 同时存在。"),
                Item("docs-minimal-contracts", "复制复用文档已覆盖关键工具", true,
                    HasAll(evidence.Docs, "plc-builders", "hmi-unified-actions", "hmi-unified-theme-layout", "online-monitoring-safety", "hardware-network"),
                    "模型第一次拿到包时，应能从工具文档知道入口、参数、安全边界。")
            };

            var hardItems = items.Where(x => x["hardGate"]?.GetValue<bool>() == true).ToList();
            var passedHard = hardItems.Count(x => x["status"]?.ToString() == "pass");
            var strictPercent = hardItems.Count == 0 ? 100 : (int)Math.Round(passedHard * 100.0 / hardItems.Count, MidpointRounding.AwayFromZero);
            var blocked = new JsonArray(items.Where(x => x["hardGate"]?.GetValue<bool>() == true && x["status"]?.ToString() != "pass").Select(x => x.DeepClone()).ToArray());

            var root = new JsonObject
            {
                ["format"] = "tia-mcp-v2-plan-completion-audit-v1",
                ["timestamp"] = DateTime.Now.ToString("O"),
                ["workspaceRoot"] = workspaceRoot,
                ["planPath"] = planPath,
                ["latestReleaseSuiteJson"] = evidence.LatestReleaseSuiteJsonPath,
                ["strictCompletionPercent"] = strictPercent,
                ["verifiedHardGateCount"] = passedHard,
                ["hardGateCount"] = hardItems.Count,
                ["blockedHardGateCount"] = blocked.Count,
                ["canClaimV2Complete"] = blocked.Count == 0,
                ["items"] = new JsonArray(items.Select(x => x.DeepClone()).ToArray()),
                ["blockedItems"] = blocked,
                ["nextActions"] = BuildNextActions(blocked),
                ["ok"] = true
            };

            var jsonPath = Path.Combine(reportDirectory, "v2_plan_completion_audit_" + stamp + ".json");
            var mdPath = Path.Combine(reportDirectory, "v2_plan_completion_audit_" + stamp + ".md");
            root["jsonPath"] = jsonPath;
            root["markdownPath"] = mdPath;
            File.WriteAllText(jsonPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), Encoding.UTF8);
            File.WriteAllText(mdPath, BuildMarkdown(root), Encoding.UTF8);
            return root;
        }

        public static string BuildMarkdown(JsonObject root)
        {
            var md = new StringBuilder();
            md.AppendLine("# TIA MCP V2 Plan Completion Audit");
            md.AppendLine();
            md.AppendLine("- Generated: " + root["timestamp"]);
            md.AppendLine("- Strict completion: " + root["strictCompletionPercent"] + "%");
            md.AppendLine("- Can claim V2 complete: " + root["canClaimV2Complete"]);
            md.AppendLine("- Verified hard gates: " + root["verifiedHardGateCount"] + "/" + root["hardGateCount"]);
            md.AppendLine();
            md.AppendLine("## Items");
            foreach (var node in root["items"] as JsonArray ?? new JsonArray())
            {
                if (node is not JsonObject item) continue;
                md.AppendLine("- " + item["id"] + ": " + item["status"] + " - " + item["title"]);
                md.AppendLine("  - evidence: " + item["evidence"]);
            }
            md.AppendLine();
            md.AppendLine("## Blocked Hard Gates");
            var blocked = root["blockedItems"] as JsonArray ?? new JsonArray();
            if (blocked.Count == 0)
            {
                md.AppendLine("- None.");
            }
            foreach (var node in blocked)
            {
                if (node is not JsonObject item) continue;
                md.AppendLine("- " + item["id"] + ": " + item["evidence"]);
            }
            md.AppendLine();
            md.AppendLine("## Next Actions");
            foreach (var action in root["nextActions"] as JsonArray ?? new JsonArray())
            {
                md.AppendLine("- " + action);
            }
            return md.ToString();
        }

        private static JsonObject Item(string id, string title, bool hardGate, bool passed, string evidence)
        {
            return new JsonObject
            {
                ["id"] = id,
                ["title"] = title,
                ["hardGate"] = hardGate,
                ["status"] = passed ? "pass" : "blocked",
                ["evidence"] = evidence
            };
        }

        private static JsonArray BuildNextActions(JsonArray blocked)
        {
            var result = new JsonArray();
            foreach (var node in blocked)
            {
                if (node is not JsonObject item) continue;
                var id = item["id"]?.ToString() ?? "";
                result.Add(id switch
                {
                    "plc-fb-builder" => "补齐 FB builder/BuildAndImport 支持，并用 XML 解析、dryRun 分类和临时 TIA 导入编译验证。",
                    "unified-theme-layout-online-tool" => "新增 Theme/Layout 高阶工具，应用后用 DescribeHmiScreenItem/导出读回验证。",
                    "unified-action-syntaxcheck-real" => "在临时 TIA V21 Unified HMI 项目执行按钮事件脚本 SyntaxCheck，保存 0 error 证据。",
                    "global-library-template-reuse" => "保留 MasterCopy 直导为可选实验路径；主线改为全局库模板学习、Unified 原生重建、读回验证和 SyntaxCheck。",
                    "online-readonly-data-provider-plan" => "实现 OPC UA/S7 read-only Provider 执行层；TIA Openness 继续负责变量发现和监控表定义读取，不负责当前值。",
                    "hardware-network-primitives" => "补齐 EnsureSubnet/AttachDeviceNodeToSubnet/SetCpuCommonSettings 或等价原语，并返回读回证据。",
                    _ => "补齐 " + id + " 的实现和验证证据。"
                });
            }
            if (result.Count == 0)
            {
                result.Add("V2 严格硬门全部通过；可以重新跑 release suite 并在授权后同步交付包。");
            }
            return result;
        }

        private static bool HasAll(string text, params string[] tokens) => tokens.All(x => text.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);
        private static bool HasAny(string text, params string[] tokens) => tokens.Any(x => text.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);

        private static bool LatestSuiteItemOk(JsonObject? suite, string id)
        {
            if (suite == null) return false;
            return (suite["items"] as JsonArray ?? new JsonArray())
                .OfType<JsonObject>()
                .Any(x => string.Equals(x["id"]?.ToString(), id, StringComparison.OrdinalIgnoreCase) && x["ok"]?.GetValue<bool>() == true);
        }

        private static string ReadText(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : ""; }
            catch { return ""; }
        }

        private static JsonObject? LoadLatestReleaseSuiteJson(string reportDirectory)
        {
            try
            {
                if (!Directory.Exists(reportDirectory)) return null;
                var latest = Directory.EnumerateFiles(reportDirectory, "offline_release_validation_suite_*.json")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                return latest == null ? null : JsonNode.Parse(File.ReadAllText(latest)) as JsonObject;
            }
            catch
            {
                return null;
            }
        }

        private static string LoadLatestRealValidationEvidenceText(string reportsRoot)
        {
            try
            {
                if (!Directory.Exists(reportsRoot)) return "";

                var directories = new[]
                {
                    "unified_hmi_action_syntaxcheck",
                    "global_library_mastercopy_import",
                    "online_current_value_read",
                    "monitoring_readonly"
                };

                var builder = new StringBuilder();
                foreach (var directory in directories)
                {
                    var path = Path.Combine(reportsRoot, directory);
                    if (!Directory.Exists(path)) continue;

                    foreach (var pattern in new[] { "*.json", "*.md" })
                    {
                        var latest = Directory.EnumerateFiles(path, pattern)
                            .OrderByDescending(File.GetLastWriteTimeUtc)
                            .FirstOrDefault();
                        if (latest == null) continue;

                        builder.AppendLine("### " + directory + " :: " + Path.GetFileName(latest));
                        builder.AppendLine(ReadText(latest));
                    }
                }

                return builder.ToString();
            }
            catch
            {
                return "";
            }
        }

        private sealed class EvidenceIndex
        {
            public EvidenceIndex(string mcpServer, string portal, string tests, string docs, string plan, JsonObject? releaseSuite, string realValidationText)
            {
                McpServer = mcpServer ?? "";
                Portal = portal ?? "";
                Tests = tests ?? "";
                Docs = docs ?? "";
                Plan = plan ?? "";
                ReleaseSuite = releaseSuite;
                ReleaseSuiteText = releaseSuite?.ToJsonString() ?? "";
                RealValidationText = realValidationText ?? "";
                LatestReleaseSuiteJsonPath = releaseSuite?["jsonPath"]?.ToString() ?? "";
            }

            public string McpServer { get; }
            public string Portal { get; }
            public string Tests { get; }
            public string Docs { get; }
            public string Plan { get; }
            public JsonObject? ReleaseSuite { get; }
            public string ReleaseSuiteText { get; }
            public string RealValidationText { get; }
            public string LatestReleaseSuiteJsonPath { get; }
        }
    }
}
