using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>
    /// 发布诊断报告构建器。
    /// 将离线总验收 JSON 汇总成便于交付、排障和商用验收的报告索引。
    /// </summary>
    public static class ReleaseDiagnosticReportBuilder
    {
        public static JsonObject Build(JsonObject suiteRoot)
        {
            if (suiteRoot == null) throw new ArgumentNullException(nameof(suiteRoot));

            var items = suiteRoot["items"] as JsonArray ?? new JsonArray();
            var reportIndex = BuildReportIndex(items);
            var failedItems = new JsonArray(reportIndex
                .OfType<JsonObject>()
                .Where(x => x["ok"]?.GetValue<bool>() != true)
                .Select(x => x.DeepClone())
                .ToArray());
            var observations = BuildObservations(suiteRoot);
            var collectedSignals = new JsonArray();
            CollectSignals(suiteRoot, "$", collectedSignals, 80);

            return new JsonObject
            {
                ["format"] = "tia-mcp-release-diagnostic-report-v1",
                ["timestamp"] = DateTime.Now.ToString("O"),
                ["offlineOnly"] = suiteRoot["offlineOnly"]?.GetValue<bool>() == true,
                ["suiteOk"] = suiteRoot["ok"]?.GetValue<bool>() == true,
                ["suiteDirectory"] = suiteRoot["suiteDirectory"]?.ToString() ?? "",
                ["summary"] = new JsonObject
                {
                    ["itemCount"] = reportIndex.Count,
                    ["passedItemCount"] = reportIndex.OfType<JsonObject>().Count(x => x["ok"]?.GetValue<bool>() == true),
                    ["failedItemCount"] = failedItems.Count,
                    ["blockingSignalCount"] = observations["blockingSignalCount"]?.GetValue<int>() ?? 0,
                    ["collectedSignalCount"] = collectedSignals.Count
                },
                ["safetyRedlines"] = new JsonArray
                {
                    "在线监视只能读当前状态，禁止通过监控表在线修改对象。",
                    "不暴露、不生成、不执行强制表/Force 相关能力。",
                    "HMI 绑定必须来自真实 PLC tag 或 DB 成员，禁止凭空绑定 M 点。",
                    "高风险写入必须经过范围校验、操作员确认、权限校验、SyntaxCheck 和读回。",
                    "交付包未获得明确许可前不自动修改。"
                },
                ["reportIndex"] = reportIndex,
                ["failedItems"] = failedItems,
                ["observations"] = observations,
                ["collectedSignals"] = collectedSignals,
                ["recommendedNextActions"] = BuildRecommendedNextActions(suiteRoot, failedItems, observations),
                ["ok"] = true
            };
        }

        public static string BuildMarkdown(JsonObject diagnostics, string jsonPath)
        {
            var md = new StringBuilder();
            md.AppendLine("# TIA MCP Release Diagnostics");
            md.AppendLine();
            md.AppendLine("Generated: " + diagnostics["timestamp"]);
            md.AppendLine("JSON: " + jsonPath);
            md.AppendLine();
            md.AppendLine("## Summary");
            var summary = diagnostics["summary"] as JsonObject ?? new JsonObject();
            md.AppendLine("- Suite OK: " + diagnostics["suiteOk"]);
            md.AppendLine("- Items: " + summary["itemCount"]);
            md.AppendLine("- Passed: " + summary["passedItemCount"]);
            md.AppendLine("- Failed: " + summary["failedItemCount"]);
            md.AppendLine("- Blocking signals: " + summary["blockingSignalCount"]);
            md.AppendLine("- Suite directory: " + diagnostics["suiteDirectory"]);
            md.AppendLine();

            md.AppendLine("## Safety Redlines");
            foreach (var redline in diagnostics["safetyRedlines"] as JsonArray ?? new JsonArray())
            {
                md.AppendLine("- " + redline);
            }
            md.AppendLine();

            md.AppendLine("## Report Index");
            foreach (var node in diagnostics["reportIndex"] as JsonArray ?? new JsonArray())
            {
                if (node is not JsonObject item) continue;
                md.AppendLine("- " + item["id"] + ": " + (item["ok"]?.GetValue<bool>() == true ? "PASS" : "FAIL") + " (" + item["summary"] + ")");
                if (!string.IsNullOrWhiteSpace(item["markdownPath"]?.ToString()))
                    md.AppendLine("  - report: " + item["markdownPath"]);
            }
            md.AppendLine();

            md.AppendLine("## Observations");
            foreach (var node in diagnostics["observations"]?["items"] as JsonArray ?? new JsonArray())
            {
                if (node is not JsonObject item) continue;
                md.AppendLine("- " + item["id"] + ": " + item["status"] + " - " + item["detail"]);
            }
            md.AppendLine();

            md.AppendLine("## Next Actions");
            foreach (var action in diagnostics["recommendedNextActions"] as JsonArray ?? new JsonArray())
            {
                md.AppendLine("- " + action);
            }
            return md.ToString();
        }

        private static JsonArray BuildReportIndex(JsonArray items)
        {
            return new JsonArray(items
                .OfType<JsonObject>()
                .Select(item => new JsonObject
                {
                    ["id"] = item["id"]?.ToString() ?? "",
                    ["title"] = item["title"]?.ToString() ?? "",
                    ["ok"] = item["ok"]?.GetValue<bool>() == true,
                    ["summary"] = item["summary"]?.ToString() ?? "",
                    ["markdownPath"] = item["markdownPath"]?.ToString() ?? "",
                    ["jsonPath"] = item["jsonPath"]?.ToString() ?? ""
                })
                .ToArray());
        }

        private static JsonObject BuildObservations(JsonObject root)
        {
            var items = new JsonArray();
            var blocking = 0;

            void Add(string id, string status, string detail, bool isBlocking)
            {
                if (isBlocking) blocking++;
                items.Add(new JsonObject
                {
                    ["id"] = id,
                    ["status"] = status,
                    ["detail"] = detail,
                    ["blocking"] = isBlocking
                });
            }

            var hmiAction = root["hmiAction"] as JsonObject ?? new JsonObject();
            var applyBlocked = ToInt(hmiAction["applyBlockedCount"]);
            var apiDiscovery = ToInt(hmiAction["apiDiscoveryRequiredCount"]);
            var safeCandidates = ToInt(hmiAction["safeDeterministicApplyCandidateCount"]);
            Add("hmi-action-apply-blocks", applyBlocked > 0 ? "blocked-by-design" : "clear", "applyBlockedCount=" + applyBlocked, false);
            Add("hmi-action-api-discovery", apiDiscovery > 0 ? "needs-api-discovery" : "clear", "apiDiscoveryRequiredCount=" + apiDiscovery, apiDiscovery > 0);
            Add("hmi-action-safe-candidates", safeCandidates > 0 ? "ready" : "none", "safeDeterministicApplyCandidateCount=" + safeCandidates, false);

            var hmiPlcSync = root["hmiTemplatePlcSyncPrecheck"] as JsonObject ?? new JsonObject();
            var blockedTemplates = ToInt(hmiPlcSync["blockedTemplateCount"]);
            var readyTemplates = ToInt(hmiPlcSync["readyTemplateCount"]);
            Add("hmi-plc-sync", blockedTemplates > 0 ? "blocked" : "ready", "readyTemplateCount=" + readyTemplates + ", blockedTemplateCount=" + blockedTemplates, blockedTemplates > 0);

            var onlineSafety = root["onlineSafety"] as JsonObject ?? new JsonObject();
            Add("online-safety", onlineSafety["ok"]?.GetValue<bool>() == true ? "pass" : "fail", "checkedTools=" + onlineSafety["checkedTools"], onlineSafety["ok"]?.GetValue<bool>() != true);

            return new JsonObject
            {
                ["blockingSignalCount"] = blocking,
                ["items"] = items
            };
        }

        private static JsonArray BuildRecommendedNextActions(JsonObject suiteRoot, JsonArray failedItems, JsonObject observations)
        {
            var result = new JsonArray();
            if (failedItems.Count > 0)
            {
                result.Add("先处理 failedItems 中的失败项；禁止把失败总套件作为可发布版本。");
            }
            if ((observations["items"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Any(x => x["id"]?.ToString() == "hmi-plc-sync" && x["blocking"]?.GetValue<bool>() == true))
            {
                result.Add("补齐 HMI 模板到真实 PLC tag/DB 成员的显式映射，再重新运行 PLC 同步预检。");
            }
            if ((observations["items"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Any(x => x["id"]?.ToString() == "hmi-action-api-discovery" && x["blocking"]?.GetValue<bool>() == true))
            {
                result.Add("用临时 TIA V21 工程发现并读回 WinCC Unified 导航/弹窗事件 API，再解除相关配方阻断。");
            }
            if (failedItems.Count == 0)
            {
                result.Add("离线发布套件已通过；下一阶段应执行临时 TIA 工程导入、读回、SyntaxCheck/编译诊断验证。");
            }
            result.Add("保持在线监视只读红线；任何写 PLC 功能必须另走双闸门和审计日志。");
            return result;
        }

        private static void CollectSignals(JsonNode? node, string path, JsonArray output, int limit)
        {
            if (node == null || output.Count >= limit) return;
            if (node is JsonObject obj)
            {
                foreach (var kv in obj)
                {
                    if (output.Count >= limit) return;
                    var childPath = path + "." + kv.Key;
                    if (IsSignalKey(kv.Key) && kv.Value is JsonArray arr && arr.Count > 0)
                    {
                        output.Add(new JsonObject
                        {
                            ["path"] = childPath,
                            ["kind"] = kv.Key,
                            ["count"] = arr.Count,
                            ["preview"] = PreviewArray(arr, 5)
                        });
                    }
                    else if (IsSignalKey(kv.Key) && kv.Value != null && !string.IsNullOrWhiteSpace(kv.Value.ToString()))
                    {
                        output.Add(new JsonObject
                        {
                            ["path"] = childPath,
                            ["kind"] = kv.Key,
                            ["count"] = 1,
                            ["preview"] = kv.Value.ToString()
                        });
                    }
                    CollectSignals(kv.Value, childPath, output, limit);
                }
            }
            else if (node is JsonArray arr)
            {
                for (var i = 0; i < arr.Count && output.Count < limit; i++)
                    CollectSignals(arr[i], path + "[" + i + "]", output, limit);
            }
        }

        private static bool IsSignalKey(string key)
        {
            var k = key ?? "";
            return k.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0
                   || k.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0
                   || k.IndexOf("missing", StringComparison.OrdinalIgnoreCase) >= 0
                   || k.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0
                   || k.IndexOf("blocked", StringComparison.OrdinalIgnoreCase) >= 0
                   || k.IndexOf("mismatch", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static JsonArray PreviewArray(JsonArray arr, int max)
        {
            return new JsonArray(arr.Take(max).Select(x => x?.DeepClone()).ToArray());
        }

        private static int ToInt(JsonNode? node)
        {
            if (node == null) return 0;
            if (int.TryParse(node.ToString(), out var value)) return value;
            return 0;
        }
    }
}
