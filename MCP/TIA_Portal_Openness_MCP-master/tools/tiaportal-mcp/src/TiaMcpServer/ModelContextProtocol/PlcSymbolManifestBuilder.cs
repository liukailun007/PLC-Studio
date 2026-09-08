using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>
    /// PLC 符号清单离线提取器。
    /// 用于从 PLC tag table / GlobalDB XML 导出物中提取可供 HMI 绑定校验的精确符号，避免手工猜变量。
    /// </summary>
    public static class PlcSymbolManifestBuilder
    {
        public static JsonObject BuildFromXmlPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("PLC XML path is required.", nameof(path));

            var resolved = Path.GetFullPath(path);
            var files = Directory.Exists(resolved)
                ? Directory.GetFiles(resolved, "*.xml", SearchOption.AllDirectories)
                : File.Exists(resolved)
                    ? new[] { resolved }
                    : Array.Empty<string>();

            var errors = new JsonArray();
            var warnings = new JsonArray();
            if (files.Length == 0)
                errors.Add("xml-file-not-found: " + path);

            var symbolMap = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
            var fileResults = new JsonArray();
            foreach (var file in files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var fileResult = AnalyzeFile(file);
                fileResults.Add(fileResult);
                if (fileResult["ok"]?.GetValue<bool>() != true)
                {
                    warnings.Add("xml-skipped: " + file);
                    continue;
                }

                foreach (var symbol in fileResult["symbols"] as JsonArray ?? new JsonArray())
                {
                    if (symbol is not JsonObject obj) continue;
                    var name = obj["symbol"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (!symbolMap.ContainsKey(name))
                        symbolMap[name] = obj;
                }
            }

            var symbols = symbolMap.Values
                .OrderBy(x => x["symbol"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .Select(x => x.DeepClone())
                .ToArray();

            if (symbols.Length == 0 && errors.Count == 0)
                warnings.Add("no-plc-symbols-found: no PLC tag table or GlobalDB symbols were extracted.");

            return new JsonObject
            {
                ["format"] = "tia-plc-symbol-manifest-offline-v1",
                ["timestamp"] = DateTime.Now.ToString("O"),
                ["offlineOnly"] = true,
                ["inputPath"] = path,
                ["resolvedPath"] = resolved,
                ["ok"] = errors.Count == 0 && symbols.Length > 0,
                ["safetyPolicy"] = new JsonObject
                {
                    ["tia"] = "离线 PLC 符号提取：不连接 TIA Portal，不打开工程，不导入 PLC/HMI 对象。",
                    ["write"] = "只读取调用方提供的 XML 文件或目录，不修改工程、reference 或交付包。",
                    ["binding"] = "提取结果用于 HMI 绑定预检；最终仍需临时工程导入/读回/编译诊断确认。"
                },
                ["fileCount"] = files.Length,
                ["symbolCount"] = symbols.Length,
                ["symbols"] = new JsonArray(symbols),
                ["symbolNames"] = new JsonArray(symbols.Select(x => JsonValue.Create(x?["symbol"]?.ToString() ?? "")).ToArray()),
                ["files"] = fileResults,
                ["errors"] = errors,
                ["warnings"] = warnings
            };
        }

        public static JsonObject RunProbe(string reportDirectory)
        {
            Directory.CreateDirectory(reportDirectory);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fixtureDir = Path.Combine(reportDirectory, "plc_symbol_fixture_" + stamp);
            Directory.CreateDirectory(fixtureDir);

            File.WriteAllText(Path.Combine(fixtureDir, "MotorTags.xml"), BuildProbeTagTableXml(), Encoding.UTF8);
            File.WriteAllText(Path.Combine(fixtureDir, "DB1_MotorData.xml"), BuildProbeDbXml(), Encoding.UTF8);

            var root = BuildFromXmlPath(fixtureDir);
            var expected = new[]
            {
                "Counter",
                "DB1_MotorData.Counter",
                "DB1_MotorData.ManualEnable",
                "DB1_MotorData.Motor",
                "DB1_MotorData.SpeedSet",
                "Motor_Run",
                "Motor_Start"
            };
            var actual = (root["symbolNames"] as JsonArray ?? new JsonArray())
                .Select(x => x?.ToString() ?? "")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missingExpected = expected.Where(x => !actual.Contains(x)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            root["mode"] = "plc-symbol-manifest-offline-probe";
            root["fixtureDirectory"] = fixtureDir;
            root["expectedSymbolCount"] = expected.Length;
            root["missingExpectedSymbols"] = new JsonArray(missingExpected.Select(x => JsonValue.Create(x)).ToArray());
            root["ok"] = root["ok"]?.GetValue<bool>() == true && missingExpected.Length == 0;

            var jsonPath = Path.Combine(reportDirectory, "plc_symbol_manifest_probe_" + stamp + ".json");
            var mdPath = Path.Combine(reportDirectory, "plc_symbol_manifest_probe_" + stamp + ".md");
            File.WriteAllText(jsonPath, root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }), Encoding.UTF8);
            File.WriteAllText(mdPath, BuildProbeMarkdown(root, jsonPath), Encoding.UTF8);
            root["jsonPath"] = jsonPath;
            root["markdownPath"] = mdPath;
            return root;
        }

        private static JsonObject AnalyzeFile(string file)
        {
            var errors = new JsonArray();
            var symbols = new JsonArray();
            var result = new JsonObject
            {
                ["path"] = file,
                ["ok"] = false,
                ["kind"] = "unknown",
                ["symbolCount"] = 0,
                ["symbols"] = symbols,
                ["errors"] = errors
            };

            try
            {
                var doc = XDocument.Load(file, LoadOptions.PreserveWhitespace);
                var tagTable = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "SW.Tags.PlcTagTable");
                var globalDb = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "SW.Blocks.GlobalDB");
                if (tagTable != null)
                {
                    result["kind"] = "plc-tag-table";
                    foreach (var tag in doc.Descendants().Where(x => x.Name.LocalName == "SW.Tags.PlcTag"))
                    {
                        var attrs = tag.Element("AttributeList");
                        var name = attrs?.Element("Name")?.Value ?? "";
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        symbols.Add(new JsonObject
                        {
                            ["symbol"] = CleanSymbol(name),
                            ["sourceKind"] = "PlcTag",
                            ["dataType"] = attrs?.Element("DataTypeName")?.Value ?? "",
                            ["logicalAddress"] = attrs?.Element("LogicalAddress")?.Value ?? "",
                            ["sourceFile"] = file
                        });
                    }
                }

                if (globalDb != null)
                {
                    result["kind"] = result["kind"]?.ToString() == "unknown" ? "global-db" : result["kind"] + "+global-db";
                    var dbName = globalDb.Element("AttributeList")?.Element("Name")?.Value ?? "";
                    foreach (var section in globalDb.Descendants().Where(x => x.Name.LocalName == "Section" && (x.Attribute("Name")?.Value ?? "") == "Static"))
                    {
                        foreach (var member in section.Elements().Where(x => x.Name.LocalName == "Member"))
                        {
                            AddDbMemberSymbols(symbols, file, dbName, member, "");
                        }
                    }
                }

                result["symbolCount"] = symbols.Count;
                result["ok"] = symbols.Count > 0;
                if (symbols.Count == 0)
                    errors.Add("no-symbols-in-file");
                return result;
            }
            catch (Exception ex)
            {
                errors.Add("xml-parse-error: " + ex.Message);
                return result;
            }
        }

        private static void AddDbMemberSymbols(JsonArray symbols, string file, string dbName, XElement member, string prefix)
        {
            var name = CleanSymbol(member.Attribute("Name")?.Value ?? "");
            if (string.IsNullOrWhiteSpace(dbName) || string.IsNullOrWhiteSpace(name)) return;
            var path = string.IsNullOrWhiteSpace(prefix) ? name : prefix + "." + name;
            symbols.Add(new JsonObject
            {
                ["symbol"] = dbName + "." + path,
                ["sourceKind"] = "GlobalDBMember",
                ["dbName"] = dbName,
                ["memberPath"] = path,
                ["dataType"] = member.Attribute("Datatype")?.Value ?? "",
                ["sourceFile"] = file
            });

            foreach (var child in member.Elements().Where(x => x.Name.LocalName == "Member"))
                AddDbMemberSymbols(symbols, file, dbName, child, path);
        }

        private static string CleanSymbol(string value)
        {
            value = (value ?? "").Trim();
            if (value.Length >= 2 && value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal))
                value = value.Substring(1, value.Length - 2);
            return value.Trim();
        }

        private static string BuildProbeTagTableXml()
        {
            return @"<?xml version=""1.0"" encoding=""utf-8""?>
<Document>
  <Engineering version=""V21"" />
  <SW.Tags.PlcTagTable ID=""0"">
    <AttributeList><Name>MotorTags</Name></AttributeList>
    <ObjectList>
      <SW.Tags.PlcTag ID=""1"" CompositionName=""Tags""><AttributeList><DataTypeName>Bool</DataTypeName><LogicalAddress>%M0.0</LogicalAddress><Name>Motor_Start</Name></AttributeList></SW.Tags.PlcTag>
      <SW.Tags.PlcTag ID=""2"" CompositionName=""Tags""><AttributeList><DataTypeName>Bool</DataTypeName><LogicalAddress>%M0.2</LogicalAddress><Name>Motor_Run</Name></AttributeList></SW.Tags.PlcTag>
      <SW.Tags.PlcTag ID=""3"" CompositionName=""Tags""><AttributeList><DataTypeName>Int</DataTypeName><LogicalAddress>%MW2</LogicalAddress><Name>Counter</Name></AttributeList></SW.Tags.PlcTag>
    </ObjectList>
  </SW.Tags.PlcTagTable>
</Document>";
        }

        private static string BuildProbeDbXml()
        {
            return @"<?xml version=""1.0"" encoding=""utf-8""?>
<Document>
  <Engineering version=""V21"" />
  <SW.Blocks.GlobalDB ID=""0"">
    <AttributeList>
      <Interface>
        <Sections xmlns=""http://www.siemens.com/automation/Openness/SW/Interface/v5"">
          <Section Name=""Static"">
            <Member Name=""Motor"" Datatype=""&quot;UDT_Motor&quot;"">
              <Member Name=""Start"" Datatype=""Bool"" />
              <Member Name=""Run"" Datatype=""Bool"" />
            </Member>
            <Member Name=""Counter"" Datatype=""Int"" />
            <Member Name=""ManualEnable"" Datatype=""Bool"" />
            <Member Name=""SpeedSet"" Datatype=""Int"" />
          </Section>
        </Sections>
      </Interface>
      <Name>DB1_MotorData</Name>
      <Number>1</Number>
      <ProgrammingLanguage>DB</ProgrammingLanguage>
    </AttributeList>
  </SW.Blocks.GlobalDB>
</Document>";
        }

        private static string BuildProbeMarkdown(JsonObject root, string jsonPath)
        {
            var md = new StringBuilder();
            md.AppendLine("# PLC Symbol Manifest Probe");
            md.AppendLine();
            md.AppendLine("Generated: " + root["timestamp"]);
            md.AppendLine("JSON: " + jsonPath);
            md.AppendLine();
            md.AppendLine("## Safety");
            md.AppendLine("- 离线 PLC 符号提取，不连接 TIA Portal，不打开工程，不导入对象。");
            md.AppendLine("- 只写 reports 目录下的探针文件，不修改工程、reference 或交付包。");
            md.AppendLine();
            md.AppendLine("## Summary");
            md.AppendLine("- OK: " + root["ok"]);
            md.AppendLine("- Symbol count: " + root["symbolCount"]);
            md.AppendLine("- Missing expected symbols: " + (root["missingExpectedSymbols"]?.ToJsonString() ?? "[]"));
            md.AppendLine();
            md.AppendLine("## Symbols");
            foreach (var item in root["symbolNames"] as JsonArray ?? new JsonArray())
                md.AppendLine("- " + item);
            return md.ToString();
        }
    }
}
