using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>
    /// PLC Builder 离线总验证套件。
    /// 每个 Builder 增量都应该接入这里，避免“功能写了但没跑过”。
    /// </summary>
    public static class PlcBuilderOfflineValidationSuite
    {
        public static JsonObject Run(string fixtureDirectory, string reportDirectory)
        {
            Directory.CreateDirectory(reportDirectory);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var suiteDir = Path.Combine(reportDirectory, "suite_" + stamp);
            Directory.CreateDirectory(suiteDir);

            var fixtureReportDir = Path.Combine(suiteDir, "fixture_readiness");
            var probeReportDir = Path.Combine(suiteDir, "builder_probes");

            var fixture = PlcBuilderFixtureReadinessAnalyzer.Analyze(fixtureDirectory);
            PlcBuilderFixtureReadinessAnalyzer.WriteReports(fixture, fixtureReportDir);

            var tagTable = PlcTagTableXmlBuilder.RunProbe(fixtureDirectory, probeReportDir);
            var udt = PlcUdtXmlBuilder.RunProbe(fixtureDirectory, probeReportDir);
            var structuredText = StructuredTextXmlBuilder.RunProbe(fixtureDirectory, probeReportDir);
            var fcBlock = PlcFcBlockXmlComposer.RunProbe(fixtureDirectory, probeReportDir);
            var globalDb = PlcGlobalDbXmlBuilder.RunProbe(fixtureDirectory, probeReportDir);
            var flgNetCall = FlgNetCallXmlBuilder.RunProbe(ResolveWorkspaceRoot(fixtureDirectory), probeReportDir);

            var items = new JsonArray(
                SuiteItem("fixture-readiness", "PLC Builder 金样本就绪检查", fixture),
                SuiteItem("tag-table-builder", "PLC 变量表 Builder", tagTable),
                SuiteItem("udt-builder", "PLC UDT Builder", udt),
                SuiteItem("structured-text-builder", "SCL StructuredText Builder", structuredText),
                SuiteItem("fc-block-composer", "PLC FC Block Composer", fcBlock),
                SuiteItem("global-db-builder", "PLC Global DB Builder", globalDb),
                SuiteItem("flgnet-call-builder", "LAD FlgNet Call Builder", flgNetCall));

            var root = new JsonObject
            {
                ["timestamp"] = DateTime.Now.ToString("O"),
                ["mode"] = "offline-plc-builder-validation-suite",
                ["fixtureDirectory"] = fixtureDirectory,
                ["suiteDirectory"] = suiteDir,
                ["safetyPolicy"] = new JsonObject
                {
                    ["tia"] = "离线总验证：不连接 TIA Portal，不打开项目，不导入 PLC 对象。",
                    ["write"] = "只写 reports 目录下的 suite 报告和生成样本，不修改 TMP_EXPORT、参考项目或交付包。"
                },
                ["items"] = items,
                ["ok"] = items.OfType<JsonObject>().All(x => x["ok"]?.GetValue<bool>() == true)
            };

            var jsonPath = Path.Combine(reportDirectory, "plc_builder_offline_suite_" + stamp + ".json");
            var mdPath = Path.Combine(reportDirectory, "plc_builder_offline_suite_" + stamp + ".md");
            File.WriteAllText(jsonPath, root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }), Encoding.UTF8);
            File.WriteAllText(mdPath, BuildMarkdown(root, jsonPath), Encoding.UTF8);
            root["jsonPath"] = jsonPath;
            root["markdownPath"] = mdPath;
            return root;
        }

        private static JsonObject SuiteItem(string id, string title, JsonObject result)
        {
            return new JsonObject
            {
                ["id"] = id,
                ["title"] = title,
                ["ok"] = result["ok"]?.GetValue<bool>() == true,
                ["markdownPath"] = result["markdownPath"]?.ToString() ?? "",
                ["jsonPath"] = result["jsonPath"]?.ToString() ?? "",
                ["generatedPath"] = result["generatedPath"]?.ToString() ?? "",
                ["semanticEqual"] = result["semanticEqual"]?.GetValue<bool?>()
            };
        }

        private static string ResolveWorkspaceRoot(string fixtureDirectory)
        {
            var current = new DirectoryInfo(Path.GetFullPath(fixtureDirectory));
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "TMP_EXPORT")) &&
                    Directory.Exists(Path.Combine(current.FullName, "tools")))
                    return current.FullName;

                current = current.Parent;
            }

            return Path.GetFullPath(Path.Combine(fixtureDirectory, "..", ".."));
        }

        private static string BuildMarkdown(JsonObject root, string jsonPath)
        {
            var md = new StringBuilder();
            md.AppendLine("# PLC Builder Offline Validation Suite");
            md.AppendLine();
            md.AppendLine("Generated: " + root["timestamp"]);
            md.AppendLine("JSON: " + jsonPath);
            md.AppendLine();
            md.AppendLine("## Safety");
            md.AppendLine("- 离线总验证，不连接 TIA Portal，不打开项目，不导入 PLC 对象。");
            md.AppendLine("- 只写 reports 目录下的 suite 报告和生成样本，不修改 TMP_EXPORT、reference 或交付包。");
            md.AppendLine();
            md.AppendLine("## Summary");
            md.AppendLine("- OK: " + root["ok"]);
            md.AppendLine("- Fixture directory: " + root["fixtureDirectory"]);
            md.AppendLine("- Suite directory: " + root["suiteDirectory"]);
            md.AppendLine();
            md.AppendLine("## Items");
            if (root["items"] is JsonArray items)
            {
                foreach (var item in items.OfType<JsonObject>())
                {
                    md.AppendLine("- " + item["title"] + ": " + (item["ok"]?.GetValue<bool>() == true ? "PASS" : "FAIL"));
                    if (!string.IsNullOrWhiteSpace(item["markdownPath"]?.ToString()))
                    {
                        md.AppendLine("  - report: " + item["markdownPath"]);
                    }
                    if (!string.IsNullOrWhiteSpace(item["generatedPath"]?.ToString()))
                    {
                        md.AppendLine("  - generated: " + item["generatedPath"]);
                    }
                    if (item["semanticEqual"] != null)
                    {
                        md.AppendLine("  - semanticEqual: " + item["semanticEqual"]);
                    }
                }
            }

            return md.ToString();
        }
    }
}
