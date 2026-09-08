using System;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>
    /// 商用就绪门禁构建器。
    /// 用于把离线验收套件、诊断报告、运行手册和发布清单汇总成一份可交付、可审计的缺口报告。
    /// </summary>
    public static class ReleaseReadinessGateBuilder
    {
        public static JsonObject Build(JsonObject suiteRoot, JsonObject diagnostics, JsonObject runbook, JsonObject manifest)
        {
            if (suiteRoot == null) throw new ArgumentNullException(nameof(suiteRoot));
            if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));
            if (runbook == null) throw new ArgumentNullException(nameof(runbook));
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));

            var gates = new JsonArray
            {
                Gate("offline-release-suite", "离线发布套件必须通过",
                    suiteRoot["ok"]?.GetValue<bool>() == true && diagnostics["suiteOk"]?.GetValue<bool>() == true,
                    "先保证不连接 TIA、不修改工程的离线套件 OK=true，作为外发包最小自检入口。"),
                Gate("no-failed-items", "诊断报告不能存在失败项",
                    (diagnostics["failedItems"] as JsonArray ?? new JsonArray()).Count == 0,
                    "任何 failedItems 都必须先修复或明确降级说明，禁止带失败项宣传为完整商用品。"),
                Gate("online-monitoring-read-only", "在线能力只允许只读监视",
                    HasObservation(diagnostics, "online-safety", "pass"),
                    "在线监视只读是安全红线；不得通过监控表在线修改对象。"),
                Gate("force-capability-disabled", "禁止 Force/强制表能力",
                    SafetyRedlinesContain(diagnostics, "Force") && SafetyRedlinesContain(diagnostics, "强制"),
                    "发布物中不能暴露、生成或执行强制表相关能力。"),
                Gate("hmi-plc-real-symbol-sync", "HMI 绑定必须同步到真实 PLC 符号",
                    !HasBlockingObservation(diagnostics, "hmi-plc-sync"),
                    "所有 HMI tag、控件动态化和事件目标必须来自真实 PLC tag 或 DB 成员，不能凭空绑定 M 点。"),
                Gate("hmi-action-api-readback", "HMI 导航/弹窗/事件 API 必须临时工程发现并读回",
                    !HasBlockingObservation(diagnostics, "hmi-action-api-discovery"),
                    "WinCC Unified 事件 API 不能只靠猜测脚本；需要临时项目发现、应用、读回和 SyntaxCheck 证据。"),
                Gate("temporary-tia-project-proof", "写入型能力需要临时 TIA 工程证据",
                    HasTemporaryProjectProof(suiteRoot, diagnostics, runbook),
                    "外发前至少要保留临时 TIA V21 工程导入、读回、SyntaxCheck 或编译诊断证据。"),
                Gate("delivery-package-sync-controlled", "交付包同步必须显式授权",
                    SafetyRedlinesContain(diagnostics, "交付包"),
                    "代码侧可以继续优化，但同步到交付文件夹必须有明确许可并重新跑验收。")
            };

            var gaps = new JsonArray(gates
                .OfType<JsonObject>()
                .Where(x => x["passed"]?.GetValue<bool>() != true)
                .Select(x => new JsonObject
                {
                    ["id"] = x["id"]?.ToString() ?? "",
                    ["title"] = x["title"]?.ToString() ?? "",
                    ["requiredEvidence"] = x["requiredEvidence"]?.ToString() ?? "",
                    ["nextAction"] = BuildNextAction(x["id"]?.ToString() ?? "")
                })
                .ToArray());

            return new JsonObject
            {
                ["format"] = "tia-mcp-release-readiness-gate-v1",
                ["timestamp"] = DateTime.Now.ToString("O"),
                ["offlineOnly"] = true,
                ["releaseReady"] = gaps.Count == 0 && manifest["releaseReady"]?.GetValue<bool>() == true,
                ["gateCount"] = gates.Count,
                ["passedGateCount"] = gates.OfType<JsonObject>().Count(x => x["passed"]?.GetValue<bool>() == true),
                ["failedGateCount"] = gaps.Count,
                ["gates"] = gates,
                ["gaps"] = gaps,
                ["safetyRedlines"] = diagnostics["safetyRedlines"]?.DeepClone() ?? new JsonArray(),
                ["ok"] = true
            };
        }

        public static string BuildMarkdown(JsonObject gateReport, string jsonPath)
        {
            var md = new StringBuilder();
            md.AppendLine("# TIA MCP Release Readiness Gate");
            md.AppendLine();
            md.AppendLine("Generated: " + gateReport["timestamp"]);
            md.AppendLine("JSON: " + jsonPath);
            md.AppendLine();
            md.AppendLine("## Summary");
            md.AppendLine("- Release ready: " + gateReport["releaseReady"]);
            md.AppendLine("- Gates: " + gateReport["gateCount"]);
            md.AppendLine("- Passed: " + gateReport["passedGateCount"]);
            md.AppendLine("- Failed: " + gateReport["failedGateCount"]);
            md.AppendLine();
            md.AppendLine("## Gates");
            foreach (var node in gateReport["gates"] as JsonArray ?? new JsonArray())
            {
                if (node is not JsonObject gate) continue;
                md.AppendLine("- " + gate["id"] + ": " + (gate["passed"]?.GetValue<bool>() == true ? "PASS" : "BLOCKED") + " - " + gate["title"]);
                md.AppendLine("  - evidence: " + gate["requiredEvidence"]);
            }
            md.AppendLine();
            md.AppendLine("## Gaps");
            var gaps = gateReport["gaps"] as JsonArray ?? new JsonArray();
            if (gaps.Count == 0)
                md.AppendLine("- None.");
            foreach (var node in gaps)
            {
                if (node is not JsonObject gap) continue;
                md.AppendLine("- " + gap["id"] + ": " + gap["nextAction"]);
            }
            md.AppendLine();
            md.AppendLine("## Safety Redlines");
            foreach (var redline in gateReport["safetyRedlines"] as JsonArray ?? new JsonArray())
            {
                md.AppendLine("- " + redline);
            }
            return md.ToString();
        }

        private static JsonObject Gate(string id, string title, bool passed, string requiredEvidence)
        {
            return new JsonObject
            {
                ["id"] = id,
                ["title"] = title,
                ["passed"] = passed,
                ["requiredEvidence"] = requiredEvidence
            };
        }

        private static bool HasObservation(JsonObject diagnostics, string id, string status)
        {
            return (diagnostics["observations"]?["items"] as JsonArray ?? new JsonArray())
                .OfType<JsonObject>()
                .Any(x => x["id"]?.ToString() == id && x["status"]?.ToString() == status);
        }

        private static bool HasBlockingObservation(JsonObject diagnostics, string id)
        {
            return (diagnostics["observations"]?["items"] as JsonArray ?? new JsonArray())
                .OfType<JsonObject>()
                .Any(x => x["id"]?.ToString() == id && x["blocking"]?.GetValue<bool>() == true);
        }

        private static bool SafetyRedlinesContain(JsonObject diagnostics, string text)
        {
            return (diagnostics["safetyRedlines"] as JsonArray ?? new JsonArray())
                .Any(x => (x?.ToString() ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool HasTemporaryProjectProof(JsonObject suiteRoot, JsonObject diagnostics, JsonObject runbook)
        {
            var reportIndex = diagnostics["reportIndex"] as JsonArray ?? new JsonArray();
            var classicPreflightOk = reportIndex.OfType<JsonObject>().Any(x =>
                x["id"]?.ToString() == "classic-hmi-temporary-import-preflight" &&
                x["ok"]?.GetValue<bool>() == true);
            var suiteJson = suiteRoot.ToJsonString();
            var runbookJson = runbook.ToJsonString();
            return classicPreflightOk &&
                   (suiteJson.IndexOf("temporary", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    suiteJson.IndexOf("临时", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    runbookJson.IndexOf("temporary", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    runbookJson.IndexOf("临时", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string BuildNextAction(string id)
        {
            return id switch
            {
                "offline-release-suite" => "重新运行 offline release suite，确保 OK=true 后再进入交付判断。",
                "no-failed-items" => "逐项修复 diagnostics.failedItems，必要时补充失败降级说明和测试。",
                "online-monitoring-read-only" => "重新运行在线监视安全自测，确认没有在线写值、监控表修改和 Force 能力。",
                "force-capability-disabled" => "补齐 Force/强制表禁止策略，并在自测中验证不会暴露相关工具。",
                "hmi-plc-real-symbol-sync" => "为 HMI 模板提供真实 PLC tag/DB 成员映射，重新跑 PLC-HMI 同步预检。",
                "hmi-action-api-readback" => "用临时 TIA V21 工程发现 WinCC Unified 事件 API，应用后读回并保留 SyntaxCheck 证据。",
                "temporary-tia-project-proof" => "补跑临时 TIA 工程导入、读回、SyntaxCheck/编译诊断，保存报告路径。",
                "delivery-package-sync-controlled" => "等待明确授权后再同步交付包，并同步后重新跑 release suite。",
                _ => "补齐该门禁要求的证据后重新生成 readiness gate。"
            };
        }
    }
}
