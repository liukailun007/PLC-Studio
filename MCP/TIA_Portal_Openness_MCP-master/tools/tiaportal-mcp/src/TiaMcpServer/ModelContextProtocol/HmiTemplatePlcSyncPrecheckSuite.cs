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
    /// Unified HMI 模板与 PLC 符号同步预检套件。
    /// 只做离线读取和报告输出，用于阻断“凭空绑定变量”的风险。
    /// </summary>
    public static class HmiTemplatePlcSyncPrecheckSuite
    {
        public static JsonObject Run(string templateDirectory, string plcXmlPath, string reportDirectory, string mappingFilePath = "")
        {
            if (string.IsNullOrWhiteSpace(templateDirectory))
                throw new ArgumentException("Template directory is required.", nameof(templateDirectory));
            if (string.IsNullOrWhiteSpace(plcXmlPath))
                throw new ArgumentException("PLC XML path is required.", nameof(plcXmlPath));
            if (string.IsNullOrWhiteSpace(reportDirectory))
                throw new ArgumentException("Report directory is required.", nameof(reportDirectory));

            templateDirectory = Path.GetFullPath(templateDirectory);
            plcXmlPath = Path.GetFullPath(plcXmlPath);
            Directory.CreateDirectory(reportDirectory);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            var templateAnalysis = HmiTemplateReferenceAnalyzer.Analyze(templateDirectory, "", "");
            var manifest = PlcSymbolManifestBuilder.BuildFromXmlPath(plcXmlPath);
            var mapping = LoadMapping(mappingFilePath);
            var precheck = BuildPrecheck(templateAnalysis["templates"] as JsonArray ?? new JsonArray(), manifest, mapping);
            var selfTest = RunEmbeddedSelfTest(reportDirectory, stamp);

            var readyCount = precheck.OfType<JsonObject>().Count(x => x["status"]?.ToString() == "ready");
            var blockedCount = precheck.OfType<JsonObject>().Count(x => x["status"]?.ToString() == "blocked");
            var root = new JsonObject
            {
                ["format"] = "tia-hmi-template-plc-sync-precheck-suite-v1",
                ["timestamp"] = DateTime.Now.ToString("O"),
                ["offlineOnly"] = true,
                ["templateDirectory"] = templateDirectory,
                ["plcXmlPath"] = plcXmlPath,
                ["mappingFilePath"] = mappingFilePath ?? "",
                ["ok"] = Directory.Exists(templateDirectory)
                         && (manifest["symbolCount"]?.GetValue<int>() ?? 0) > 0
                         && selfTest["ok"]?.GetValue<bool>() == true,
                ["safetyPolicy"] = new JsonObject
                {
                    ["tia"] = "离线同步预检：不连接 TIA Portal，不打开工程，不导入 PLC/HMI 对象。",
                    ["write"] = "只写 reports 目录下的报告；不修改模板、reference、交付包或真实工程。",
                    ["binding"] = "只有完整 PLC 符号存在且数据类型兼容时，模板绑定才标记为 ready；缺失或不兼容必须阻断。"
                },
                ["templateCount"] = precheck.Count,
                ["readyTemplateCount"] = readyCount,
                ["blockedTemplateCount"] = blockedCount,
                ["plcSymbolCount"] = manifest["symbolCount"]?.GetValue<int>() ?? 0,
                ["mappingLoaded"] = mapping["loaded"]?.GetValue<bool>() == true,
                ["templates"] = precheck,
                ["plcManifest"] = manifest,
                ["mapping"] = mapping,
                ["selfTest"] = selfTest
            };

            var jsonPath = Path.Combine(reportDirectory, "hmi_template_plc_sync_precheck_" + stamp + ".json");
            var mdPath = Path.Combine(reportDirectory, "hmi_template_plc_sync_precheck_" + stamp + ".md");
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

        public static JsonArray BuildPrecheck(JsonArray templates, JsonObject plcManifest, JsonObject? mapping = null)
        {
            var symbolMap = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in plcManifest["symbols"] as JsonArray ?? new JsonArray())
            {
                if (node is not JsonObject symbol) continue;
                var name = NormalizeSymbol(symbol["symbol"]?.ToString() ?? "");
                if (!string.IsNullOrWhiteSpace(name) && !symbolMap.ContainsKey(name))
                    symbolMap[name] = symbol;
            }

            var mappingMap = BuildMappingMap(mapping);
            var result = new JsonArray();
            foreach (var templateNode in templates)
            {
                if (templateNode is not JsonObject template) continue;
                var templateName = template["templateName"]?.ToString() ?? "";
                var bindingChecks = new JsonArray();
                var missing = new JsonArray();
                var typeMismatches = new JsonArray();

                foreach (var tagNode in template["requiredTags"] as JsonArray ?? new JsonArray())
                {
                    if (tagNode is not JsonObject tag) continue;
                    var hmiTag = tag["Name"]?.ToString() ?? "";
                    var requestedPlcTag = NormalizeSymbol(tag["PlcTag"]?.ToString() ?? "");
                    var mappedPlcTag = ResolveMappedPlcTag(mappingMap, templateName, hmiTag);
                    var effectivePlcTag = string.IsNullOrWhiteSpace(mappedPlcTag) ? requestedPlcTag : mappedPlcTag;
                    var hmiType = NormalizeDataType(tag["DataType"]?.ToString() ?? "");
                    symbolMap.TryGetValue(effectivePlcTag, out var plcSymbol);
                    var plcType = NormalizeDataType(plcSymbol?["dataType"]?.ToString() ?? "");
                    var symbolFound = plcSymbol != null;
                    var typeCompatible = !symbolFound || string.IsNullOrWhiteSpace(plcType) || AreDataTypesCompatible(hmiType, plcType);

                    var check = new JsonObject
                    {
                        ["hmiTag"] = hmiTag,
                        ["requestedPlcTag"] = requestedPlcTag,
                        ["mappedPlcTag"] = mappedPlcTag,
                        ["effectivePlcTag"] = effectivePlcTag,
                        ["hmiDataType"] = hmiType,
                        ["plcDataType"] = plcType,
                        ["symbolFound"] = symbolFound,
                        ["dataTypeCompatible"] = typeCompatible,
                        ["mappingRequired"] = !string.IsNullOrWhiteSpace(mappedPlcTag),
                        ["status"] = symbolFound && typeCompatible ? "ready" : "blocked",
                        ["reason"] = !symbolFound
                            ? "PLC 完整符号不存在，禁止绑定。"
                            : !typeCompatible
                                ? "PLC 数据类型与 HMI 变量类型不兼容，禁止绑定。"
                                : "PLC 完整符号存在且类型兼容。"
                    };
                    bindingChecks.Add(check);
                    if (!symbolFound) missing.Add(check.DeepClone());
                    else if (!typeCompatible) typeMismatches.Add(check.DeepClone());
                }

                var actionMissing = template["missingRequiredTagsForActions"] as JsonArray ?? new JsonArray();
                var blocked = missing.Count > 0 || typeMismatches.Count > 0 || actionMissing.Count > 0;
                result.Add(new JsonObject
                {
                    ["templateName"] = templateName,
                    ["requiredTagCount"] = (template["requiredTags"] as JsonArray)?.Count ?? 0,
                    ["bindingChecks"] = bindingChecks,
                    ["missingPlcSymbols"] = missing,
                    ["dataTypeMismatches"] = typeMismatches,
                    ["missingRequiredTagsForActions"] = actionMissing.DeepClone(),
                    ["status"] = blocked ? "blocked" : "ready",
                    ["gate"] = blocked ? "do-not-apply" : "ready-for-temporary-project-validation",
                    ["note"] = blocked
                        ? "模板仍可用于设计分析，但不能执行 HMI 变量创建、动态化绑定或事件写入。"
                        : "离线符号预检通过，下一步仍需临时 TIA 工程导入/读回/诊断验证。"
                });
            }

            return result;
        }

        private static JsonObject RunEmbeddedSelfTest(string reportDirectory, string stamp)
        {
            var fixtureDir = Path.Combine(reportDirectory, "sync_precheck_fixture_" + stamp);
            Directory.CreateDirectory(fixtureDir);
            File.WriteAllText(Path.Combine(fixtureDir, "template.json"), @"{
  ""Format"": ""tia-unified-screen-v1"",
  ""TemplateName"": ""sync-fixture"",
  ""RequiredTags"": [
    { ""Name"": ""Motor_Run"", ""DataType"": ""Bool"", ""PlcTag"": ""DB1_MotorData.Motor.Run"" },
    { ""Name"": ""Speed_Set"", ""DataType"": ""Int"", ""PlcTag"": ""DB1_MotorData.SpeedSet"" }
  ],
  ""Items"": []
}", Encoding.UTF8);
            File.WriteAllText(Path.Combine(fixtureDir, "DB1_MotorData.xml"), @"<?xml version=""1.0"" encoding=""utf-8""?>
<Document>
  <SW.Blocks.GlobalDB ID=""0"">
    <AttributeList>
      <Interface>
        <Sections xmlns=""http://www.siemens.com/automation/Openness/SW/Interface/v5"">
          <Section Name=""Static"">
            <Member Name=""Motor"" Datatype=""&quot;UDT_Motor&quot;"">
              <Member Name=""Run"" Datatype=""Bool"" />
            </Member>
            <Member Name=""SpeedSet"" Datatype=""Int"" />
          </Section>
        </Sections>
      </Interface>
      <Name>DB1_MotorData</Name>
    </AttributeList>
  </SW.Blocks.GlobalDB>
</Document>", Encoding.UTF8);

            var templates = HmiTemplateReferenceAnalyzer.Analyze(fixtureDir, "", "")["templates"] as JsonArray ?? new JsonArray();
            var manifest = PlcSymbolManifestBuilder.BuildFromXmlPath(fixtureDir);
            var positive = BuildPrecheck(templates, manifest);

            var badManifest = PlcSymbolManifestBuilder.BuildFromXmlPath(Path.Combine(fixtureDir, "DB1_MotorData.xml"));
            var symbols = badManifest["symbols"] as JsonArray ?? new JsonArray();
            for (var i = symbols.Count - 1; i >= 0; i--)
            {
                if (symbols[i]?["symbol"]?.ToString() == "DB1_MotorData.SpeedSet")
                    symbols.RemoveAt(i);
            }
            badManifest["symbolCount"] = symbols.Count;
            badManifest["symbolNames"] = new JsonArray(symbols.OfType<JsonObject>().Select(x => JsonValue.Create(x["symbol"]?.ToString() ?? "")).ToArray());
            var negative = BuildPrecheck(templates, badManifest);

            var positiveReady = positive.OfType<JsonObject>().All(x => x["status"]?.ToString() == "ready");
            var negativeBlocked = negative.OfType<JsonObject>().Any(x => x["status"]?.ToString() == "blocked");
            return new JsonObject
            {
                ["ok"] = positiveReady && negativeBlocked,
                ["fixtureDirectory"] = fixtureDir,
                ["positiveReady"] = positiveReady,
                ["negativeBlocked"] = negativeBlocked,
                ["positive"] = positive,
                ["negative"] = negative
            };
        }

        private static JsonObject LoadMapping(string mappingFilePath)
        {
            var root = new JsonObject
            {
                ["path"] = mappingFilePath ?? "",
                ["loaded"] = false,
                ["entries"] = new JsonArray(),
                ["warnings"] = new JsonArray()
            };
            if (string.IsNullOrWhiteSpace(mappingFilePath)) return root;
            if (!File.Exists(mappingFilePath))
            {
                (root["warnings"] as JsonArray)?.Add("mapping-file-not-found");
                return root;
            }

            try
            {
                var json = JsonNode.Parse(File.ReadAllText(mappingFilePath, Encoding.UTF8)) as JsonObject
                    ?? throw new InvalidOperationException("Mapping root must be a JSON object.");
                var entries = root["entries"] as JsonArray ?? new JsonArray();
                foreach (var templateNode in json["Templates"] as JsonArray ?? new JsonArray())
                {
                    if (templateNode is not JsonObject template) continue;
                    var templateName = template["TemplateName"]?.ToString() ?? "";
                    foreach (var mappingNode in template["Mappings"] as JsonArray ?? new JsonArray())
                    {
                        if (mappingNode is not JsonObject mapping) continue;
                        entries.Add(new JsonObject
                        {
                            ["templateName"] = templateName,
                            ["hmiTag"] = mapping["HmiTag"]?.ToString() ?? "",
                            ["mappedPlcTag"] = NormalizeSymbol(mapping["MappedPlcTag"]?.ToString() ?? "")
                        });
                    }
                }
                root["loaded"] = true;
            }
            catch (Exception ex)
            {
                (root["warnings"] as JsonArray)?.Add("mapping-parse-error: " + ex.Message);
            }
            return root;
        }

        private static Dictionary<string, string> BuildMappingMap(JsonObject? mapping)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in mapping?["entries"] as JsonArray ?? new JsonArray())
            {
                if (node is not JsonObject entry) continue;
                var key = (entry["templateName"]?.ToString() ?? "") + "\u001f" + (entry["hmiTag"]?.ToString() ?? "");
                var mapped = NormalizeSymbol(entry["mappedPlcTag"]?.ToString() ?? "");
                if (!string.IsNullOrWhiteSpace(mapped))
                    result[key] = mapped;
            }
            return result;
        }

        private static string ResolveMappedPlcTag(Dictionary<string, string> map, string templateName, string hmiTag)
        {
            return map.TryGetValue(templateName + "\u001f" + hmiTag, out var mapped) ? mapped : "";
        }

        private static bool AreDataTypesCompatible(string hmiType, string plcType)
        {
            hmiType = NormalizeDataType(hmiType);
            plcType = NormalizeDataType(plcType);
            if (string.IsNullOrWhiteSpace(hmiType) || string.IsNullOrWhiteSpace(plcType)) return true;
            if (string.Equals(hmiType, plcType, StringComparison.OrdinalIgnoreCase)) return true;
            var ints = new HashSet<string>(new[] { "SInt", "USInt", "Byte", "Int", "UInt", "Word", "DInt", "UDInt", "DWord", "LInt", "ULInt", "LWord" }, StringComparer.OrdinalIgnoreCase);
            var reals = new HashSet<string>(new[] { "Real", "LReal" }, StringComparer.OrdinalIgnoreCase);
            return (ints.Contains(hmiType) && ints.Contains(plcType)) || (reals.Contains(hmiType) && reals.Contains(plcType));
        }

        private static string NormalizeDataType(string value)
        {
            return (value ?? "").Trim().Trim('"').Replace("&quot;", "").Replace("&QUOT;", "");
        }

        private static string NormalizeSymbol(string value)
        {
            return (value ?? "").Trim().Trim('"');
        }

        private static string BuildMarkdown(JsonObject root, string jsonPath)
        {
            var md = new StringBuilder();
            md.AppendLine("# HMI Template PLC Sync Precheck");
            md.AppendLine();
            md.AppendLine("Generated: " + root["timestamp"]);
            md.AppendLine("JSON: " + jsonPath);
            md.AppendLine();
            md.AppendLine("## Safety");
            md.AppendLine("- 离线预检，不连接 TIA Portal，不打开或修改工程。");
            md.AppendLine("- 只输出 reports 报告；不修改模板、reference 或交付包。");
            md.AppendLine("- PLC 完整符号不存在或类型不兼容时，HMI 绑定必须阻断。");
            md.AppendLine();
            md.AppendLine("## Summary");
            md.AppendLine("- OK: " + root["ok"]);
            md.AppendLine("- Templates: " + root["templateCount"]);
            md.AppendLine("- Ready: " + root["readyTemplateCount"]);
            md.AppendLine("- Blocked: " + root["blockedTemplateCount"]);
            md.AppendLine("- PLC symbols: " + root["plcSymbolCount"]);
            md.AppendLine("- Embedded self-test: " + root["selfTest"]?["ok"]);
            md.AppendLine();
            md.AppendLine("## Templates");
            foreach (var node in root["templates"] as JsonArray ?? new JsonArray())
            {
                if (node is not JsonObject template) continue;
                md.AppendLine("- " + template["templateName"] + ": " + template["status"] +
                              ", gate=" + template["gate"] +
                              ", requiredTags=" + template["requiredTagCount"] +
                              ", missing=" + ((template["missingPlcSymbols"] as JsonArray)?.Count ?? 0) +
                              ", typeMismatch=" + ((template["dataTypeMismatches"] as JsonArray)?.Count ?? 0));
            }
            return md.ToString();
        }
    }
}
