using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>
    /// 离线检查 PLC XML Builder 所需的金样本是否齐全、可解析、结构可用。
    /// 这里只读 TMP_EXPORT/_verify，不连接 TIA，不导入项目。
    /// </summary>
    public static class PlcBuilderFixtureReadinessAnalyzer
    {
        private static readonly FixtureRule[] RequiredFixtures =
        {
            new FixtureRule("udt", "UDT_Fault.xml", Array.Empty<string>(), "UDT/PLC 数据类型金样本", new[] { "SW.Types.PlcStruct" }),
            new FixtureRule("tag-table", "TagTable_StartStop.xml", Array.Empty<string>(), "PLC 变量表金样本", new[] { "SW.Tags.PlcTagTable", "SW.Tags.PlcTag" }),
            new FixtureRule("scl-fc", "FC_StartStop.xml", Array.Empty<string>(), "SCL FC 金样本", new[] { "SW.Blocks.FC", "StructuredText" }),
            new FixtureRule(
                "lad-flgnet",
                Path.Combine("..", "Source", "5T车", "Blocks", "01_手动控制", "FC控制", "05-故障保护.xml"),
                new[] { Path.Combine("Limit_Protect_roundtrip.xml", "Limit_Protect.xml") },
                "LAD/FlgNet 金样本",
                new[] { "SW.Blocks.FC", "FlgNet" }),
            new FixtureRule("global-db", Path.Combine("Sim_Data_roundtrip.xml", "Sim_Data.xml"), Array.Empty<string>(), "全局 DB 金样本", new[] { "SW.Blocks.GlobalDB" })
        };

        public static JsonObject Analyze(string fixtureDirectory)
        {
            var root = new JsonObject
            {
                ["timestamp"] = DateTime.Now.ToString("O"),
                ["mode"] = "offline-plc-builder-fixture-readiness",
                ["fixtureDirectory"] = fixtureDirectory,
                ["exists"] = Directory.Exists(fixtureDirectory),
                ["safetyPolicy"] = new JsonObject
                {
                    ["tia"] = "离线只读检查：不连接 TIA Portal，不打开项目，不导入 PLC 对象。",
                    ["write"] = "只写 reports 目录下的检查报告，不修改 TMP_EXPORT、参考项目或交付包。",
                    ["purpose"] = "作为 PLC XML Builder 的第一道回归门，先确认金样本齐全且结构可识别。"
                }
            };

            var results = RequiredFixtures.Select(rule => AnalyzeFixture(fixtureDirectory, rule)).ToList();
            root["fixtures"] = new JsonArray(results.Select(x => x.Json).ToArray());
            root["summary"] = new JsonObject
            {
                ["required"] = RequiredFixtures.Length,
                ["pass"] = results.Count(x => x.Ok),
                ["fail"] = results.Count(x => !x.Ok)
            };
            root["ok"] = Directory.Exists(fixtureDirectory) && results.All(x => x.Ok);
            root["nextBuilderScope"] = BuildNextBuilderScope(results);
            return root;
        }

        public static void WriteReports(JsonObject root, string reportDirectory)
        {
            Directory.CreateDirectory(reportDirectory);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var jsonPath = Path.Combine(reportDirectory, "plc_builder_fixture_readiness_" + stamp + ".json");
            var mdPath = Path.Combine(reportDirectory, "plc_builder_fixture_readiness_" + stamp + ".md");

            File.WriteAllText(jsonPath, root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }), Encoding.UTF8);
            File.WriteAllText(mdPath, BuildMarkdown(root, jsonPath), Encoding.UTF8);

            root["jsonPath"] = jsonPath;
            root["markdownPath"] = mdPath;
        }

        public static string BuildMarkdown(JsonObject root, string jsonPath)
        {
            var md = new StringBuilder();
            md.AppendLine("# PLC Builder Fixture Readiness");
            md.AppendLine();
            md.AppendLine("Generated: " + root["timestamp"]);
            md.AppendLine("JSON: " + jsonPath);
            md.AppendLine();
            md.AppendLine("## Safety");
            md.AppendLine("- 离线只读检查，不连接 TIA Portal，不打开项目，不导入 PLC 对象。");
            md.AppendLine("- 只生成 reports 下的检查报告，不修改 TMP_EXPORT、reference 或交付包。");
            md.AppendLine();

            var summary = root["summary"] as JsonObject;
            md.AppendLine("## Summary");
            md.AppendLine("- Fixture directory: " + root["fixtureDirectory"]);
            md.AppendLine("- OK: " + root["ok"]);
            md.AppendLine("- Required: " + summary?["required"] + ", pass: " + summary?["pass"] + ", fail: " + summary?["fail"]);
            md.AppendLine();

            md.AppendLine("## Fixtures");
            if (root["fixtures"] is JsonArray fixtures)
            {
                foreach (var node in fixtures.OfType<JsonObject>())
                {
                    md.AppendLine("- " + node["id"] + " / " + node["title"] + ": " + node["status"]);
                    md.AppendLine("  - path: " + node["path"]);
                    md.AppendLine("  - required markers: " + string.Join(", ", (node["requiredMarkers"] as JsonArray ?? new JsonArray()).Select(x => x?.ToString())));
                    md.AppendLine("  - detected markers: " + string.Join(", ", (node["detectedMarkers"] as JsonArray ?? new JsonArray()).Select(x => x?.ToString())));
                    if (!string.IsNullOrWhiteSpace(node["error"]?.ToString()))
                    {
                        md.AppendLine("  - error: " + node["error"]);
                    }
                }
            }
            md.AppendLine();

            md.AppendLine("## Next Builder Scope");
            if (root["nextBuilderScope"] is JsonArray steps)
            {
                foreach (var step in steps)
                {
                    md.AppendLine("- " + step);
                }
            }

            return md.ToString();
        }

        private static FixtureResult AnalyzeFixture(string fixtureDirectory, FixtureRule rule)
        {
            var candidates = new[] { rule.RelativePath }.Concat(rule.AlternativeRelativePaths).ToArray();
            var path = ResolveFirstExistingFixturePath(fixtureDirectory, candidates);
            var attemptedPaths = candidates.Select(x => Path.GetFullPath(Path.Combine(fixtureDirectory, x))).ToArray();
            var json = new JsonObject
            {
                ["id"] = rule.Id,
                ["title"] = rule.Title,
                ["relativePath"] = rule.RelativePath,
                ["path"] = path,
                ["attemptedPaths"] = new JsonArray(attemptedPaths.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
                ["requiredMarkers"] = new JsonArray(rule.RequiredMarkers.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray())
            };

            if (!File.Exists(path))
            {
                json["status"] = Directory.Exists(path) ? "FAIL: expected XML file but found directory" : "FAIL: missing";
                json["error"] = Directory.Exists(path)
                    ? "路径是目录，需要指向目录内的真实 XML 文件。"
                    : "金样本文件不存在。";
                json["detectedMarkers"] = new JsonArray();
                return new FixtureResult(false, json);
            }

            try
            {
                var text = File.ReadAllText(path, Encoding.UTF8);
                var doc = XDocument.Parse(text, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
                var localNames = (doc.Root == null
                        ? Enumerable.Empty<XElement>()
                        : doc.Root.DescendantsAndSelf())
                    .Select(x => x.Name.LocalName)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                var detected = rule.RequiredMarkers.Where(marker => HasMarker(text, localNames, marker)).ToArray();

                json["status"] = detected.Length == rule.RequiredMarkers.Length ? "PASS" : "FAIL: marker missing";
                json["length"] = new FileInfo(path).Length;
                json["sha256"] = Sha256(path);
                json["rootElement"] = doc.Root?.Name.LocalName ?? "";
                json["detectedMarkers"] = new JsonArray(detected.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
                json["elementCount"] = doc.Descendants().Count();
                json["networkSourceCount"] = doc.Descendants().Count(x => x.Name.LocalName == "NetworkSource");
                json["compileUnitCount"] = doc.Descendants().Count(x => x.Name.LocalName == "SW.Blocks.CompileUnit");

                if (detected.Length != rule.RequiredMarkers.Length)
                {
                    var missing = rule.RequiredMarkers.Except(detected, StringComparer.Ordinal).ToArray();
                    json["error"] = "缺少结构标记: " + string.Join(", ", missing);
                }

                return new FixtureResult(detected.Length == rule.RequiredMarkers.Length, json);
            }
            catch (Exception ex)
            {
                json["status"] = "FAIL: XML parse error";
                json["error"] = ex.Message;
                json["detectedMarkers"] = new JsonArray();
                return new FixtureResult(false, json);
            }
        }

        private static JsonArray BuildNextBuilderScope(IReadOnlyCollection<FixtureResult> results)
        {
            if (results.Any(x => !x.Ok))
            {
                return new JsonArray
                {
                    "先补齐或修正失败金样本，避免后续 Builder 在错误样本上开发。",
                    "修正后重新运行 --generate-plc-builder-fixture-readiness，全部 PASS 后再进入 XML Builder 实现。"
                };
            }

            return new JsonArray
            {
                "第一步实现 BuildUdtXml 或 BuildPlcTagTableXml，并使用本报告中的 UDT/变量表样本做 XML 解析回归。",
                "第二步实现 StructuredTextBuilder，只覆盖赋值、IF、调用、变量访问的最小集合。",
                "第三步再处理 FlgNetBuilder，必须用 LAD/FlgNet 金样本验证 Wire、NameCon、IdentCon 引用闭合。"
            };
        }

        private static bool HasMarker(string xmlText, IReadOnlyCollection<string> localNames, string marker)
        {
            if (marker.StartsWith("SW.", StringComparison.Ordinal))
            {
                return xmlText.IndexOf("<" + marker, StringComparison.Ordinal) >= 0 ||
                       xmlText.IndexOf("</" + marker, StringComparison.Ordinal) >= 0;
            }

            return localNames.Contains(marker);
        }

        private static string ResolveFirstExistingFixturePath(string fixtureDirectory, IEnumerable<string> relativePaths)
        {
            foreach (var relativePath in relativePaths)
            {
                var path = Path.GetFullPath(Path.Combine(fixtureDirectory, relativePath));
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return Path.GetFullPath(Path.Combine(fixtureDirectory, relativePaths.First()));
        }

        private static string Sha256(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private sealed class FixtureRule
        {
            public FixtureRule(string id, string relativePath, string[] alternativeRelativePaths, string title, string[] requiredMarkers)
            {
                Id = id;
                RelativePath = relativePath;
                AlternativeRelativePaths = alternativeRelativePaths;
                Title = title;
                RequiredMarkers = requiredMarkers;
            }

            public string Id { get; }
            public string RelativePath { get; }
            public string[] AlternativeRelativePaths { get; }
            public string Title { get; }
            public string[] RequiredMarkers { get; }
        }

        private sealed class FixtureResult
        {
            public FixtureResult(bool ok, JsonObject json)
            {
                Ok = ok;
                Json = json;
            }

            public bool Ok { get; }
            public JsonObject Json { get; }
        }
    }
}
