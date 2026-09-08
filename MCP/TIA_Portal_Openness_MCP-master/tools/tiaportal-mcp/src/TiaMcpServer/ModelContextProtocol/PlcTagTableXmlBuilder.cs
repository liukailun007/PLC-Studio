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
    /// PLC 变量表 XML 构造器。
    /// 目标是把“手拼 XML”收敛为可复用、可验证的结构化生成能力。
    /// </summary>
    public static class PlcTagTableXmlBuilder
    {
        public static XDocument BuildDocument(string tableName, IEnumerable<PlcTagDefinition> tags)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("变量表名称不能为空。", nameof(tableName));

            var normalizedTags = tags?.ToList() ?? throw new ArgumentNullException(nameof(tags));
            if (normalizedTags.Count == 0)
                throw new ArgumentException("变量表至少需要 1 个变量。", nameof(tags));

            ValidateTags(normalizedTags);

            return new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("Document",
                    new XElement("Engineering", new XAttribute("version", "V21")),
                    new XElement("DocumentInfo",
                        new XElement("Created", "2000-01-01T00:00:00.0000000Z"),
                        new XElement("ExportSetting", "None"),
                        new XElement("InstalledProducts")),
                    new XElement("SW.Tags.PlcTagTable",
                        new XAttribute("ID", "0"),
                        new XElement("AttributeList",
                            new XElement("Name", tableName)),
                        new XElement("ObjectList",
                            normalizedTags.Select((tag, index) =>
                                new XElement("SW.Tags.PlcTag",
                                    new XAttribute("ID", (index + 1).ToString()),
                                    new XAttribute("CompositionName", "Tags"),
                                    new XElement("AttributeList",
                                        new XElement("DataTypeName", tag.DataTypeName),
                                        new XElement("LogicalAddress", tag.LogicalAddress),
                                        new XElement("Name", tag.Name))))))));
        }

        public static string BuildXml(string tableName, IEnumerable<PlcTagDefinition> tags)
        {
            using var writer = new Utf8StringWriter();
            BuildDocument(tableName, tags).Save(writer, SaveOptions.None);
            return writer.ToString();
        }

        public static JsonObject RunProbe(string fixtureDirectory, string reportDirectory)
        {
            Directory.CreateDirectory(reportDirectory);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var generatedPath = Path.Combine(reportDirectory, "TagTable_StartStop.generated_" + stamp + ".xml");
            var jsonPath = Path.Combine(reportDirectory, "plc_tag_table_builder_probe_" + stamp + ".json");
            var mdPath = Path.Combine(reportDirectory, "plc_tag_table_builder_probe_" + stamp + ".md");
            var goldenPath = Path.Combine(fixtureDirectory, "TagTable_StartStop.xml");

            var tags = new[]
            {
                new PlcTagDefinition("StartPB", "Bool", "%I0.0"),
                new PlcTagDefinition("StopPB", "Bool", "%I0.1"),
                new PlcTagDefinition("EStop", "Bool", "%I0.2"),
                new PlcTagDefinition("RunOut", "Bool", "%Q0.0")
            };
            File.WriteAllText(generatedPath, BuildXml("StartStop", tags), Encoding.UTF8);

            var generated = AnalyzeTagTable(generatedPath);
            var golden = AnalyzeTagTable(goldenPath);
            var semanticEqual = CompareTagTables(golden, generated);
            var root = new JsonObject
            {
                ["timestamp"] = DateTime.Now.ToString("O"),
                ["mode"] = "offline-plc-tag-table-builder-probe",
                ["goldenPath"] = goldenPath,
                ["generatedPath"] = generatedPath,
                ["safetyPolicy"] = new JsonObject
                {
                    ["tia"] = "离线生成和解析 XML，不连接 TIA Portal，不导入 PLC 变量表。",
                    ["write"] = "只写 reports 目录下的生成样本和探针报告，不修改 TMP_EXPORT 或交付包。"
                },
                ["golden"] = golden,
                ["generated"] = generated,
                ["semanticEqual"] = semanticEqual,
                ["ok"] = golden["ok"]?.GetValue<bool>() == true &&
                       generated["ok"]?.GetValue<bool>() == true &&
                       semanticEqual
            };

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

        public static JsonObject AnalyzeTagTable(string path)
        {
            var root = new JsonObject
            {
                ["path"] = path,
                ["exists"] = File.Exists(path)
            };

            if (!File.Exists(path))
            {
                root["ok"] = false;
                root["error"] = "文件不存在。";
                return root;
            }

            try
            {
                var doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
                var table = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "SW.Tags.PlcTagTable");
                var tableName = table?
                    .Element("AttributeList")?
                    .Element("Name")?
                    .Value ?? "";
                var tagNodes = doc.Descendants().Where(x => x.Name.LocalName == "SW.Tags.PlcTag").ToList();
                var tags = tagNodes.Select(ReadTag).ToArray();

                root["ok"] = table != null && !string.IsNullOrWhiteSpace(tableName) && tags.All(x => x["ok"]?.GetValue<bool>() == true);
                root["tableName"] = tableName;
                root["tagCount"] = tags.Length;
                root["tags"] = new JsonArray(tags);
                return root;
            }
            catch (Exception ex)
            {
                root["ok"] = false;
                root["error"] = ex.Message;
                return root;
            }
        }

        private static JsonObject ReadTag(XElement tagNode)
        {
            var attrs = tagNode.Element("AttributeList");
            var name = attrs?.Element("Name")?.Value ?? "";
            var dataType = attrs?.Element("DataTypeName")?.Value ?? "";
            var address = attrs?.Element("LogicalAddress")?.Value ?? "";
            return new JsonObject
            {
                ["ok"] = !string.IsNullOrWhiteSpace(name) &&
                       !string.IsNullOrWhiteSpace(dataType) &&
                       !string.IsNullOrWhiteSpace(address),
                ["name"] = name,
                ["dataTypeName"] = dataType,
                ["logicalAddress"] = address
            };
        }

        private static bool CompareTagTables(JsonObject golden, JsonObject generated)
        {
            if (golden["tableName"]?.ToString() != generated["tableName"]?.ToString())
                return false;

            return NormalizeTags(golden).SequenceEqual(NormalizeTags(generated), StringComparer.Ordinal);
        }

        private static IEnumerable<string> NormalizeTags(JsonObject table)
        {
            return (table["tags"] as JsonArray ?? new JsonArray())
                .OfType<JsonObject>()
                .Select(x => x["name"] + "|" + x["dataTypeName"] + "|" + x["logicalAddress"])
                .OrderBy(x => x, StringComparer.Ordinal);
        }

        private static string BuildProbeMarkdown(JsonObject root, string jsonPath)
        {
            var md = new StringBuilder();
            md.AppendLine("# PLC Tag Table Builder Probe");
            md.AppendLine();
            md.AppendLine("Generated: " + root["timestamp"]);
            md.AppendLine("JSON: " + jsonPath);
            md.AppendLine();
            md.AppendLine("## Safety");
            md.AppendLine("- 离线生成和解析 XML，不连接 TIA Portal，不导入 PLC 变量表。");
            md.AppendLine("- 只写 reports 目录下的生成样本和探针报告，不修改 TMP_EXPORT 或交付包。");
            md.AppendLine();
            md.AppendLine("## Summary");
            md.AppendLine("- OK: " + root["ok"]);
            md.AppendLine("- Semantic equal to golden: " + root["semanticEqual"]);
            md.AppendLine("- Golden: " + root["goldenPath"]);
            md.AppendLine("- Generated: " + root["generatedPath"]);
            md.AppendLine();
            md.AppendLine("## Generated Tags");
            if (root["generated"] is JsonObject generated && generated["tags"] is JsonArray tags)
            {
                foreach (var tag in tags.OfType<JsonObject>())
                {
                    md.AppendLine("- " + tag["name"] + ": " + tag["dataTypeName"] + " @ " + tag["logicalAddress"]);
                }
            }

            return md.ToString();
        }

        private static void ValidateTags(IReadOnlyCollection<PlcTagDefinition> tags)
        {
            var duplicates = tags.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Where(x => x.Count() > 1)
                .Select(x => x.Key)
                .ToArray();
            if (duplicates.Length > 0)
                throw new ArgumentException("PLC 变量名重复: " + string.Join(", ", duplicates));

            foreach (var tag in tags)
            {
                if (string.IsNullOrWhiteSpace(tag.Name))
                    throw new ArgumentException("PLC 变量名不能为空。");
                if (string.IsNullOrWhiteSpace(tag.DataTypeName))
                    throw new ArgumentException("PLC 变量数据类型不能为空: " + tag.Name);
                if (string.IsNullOrWhiteSpace(tag.LogicalAddress))
                    throw new ArgumentException("PLC 变量地址不能为空: " + tag.Name);
                if (!tag.LogicalAddress.StartsWith("%", StringComparison.Ordinal))
                    throw new ArgumentException("PLC 变量地址必须使用 TIA 绝对地址格式，例如 %I0.0、%Q0.0: " + tag.Name);
            }
        }

        private sealed class Utf8StringWriter : StringWriter
        {
            public override Encoding Encoding => Encoding.UTF8;
        }
    }

    public sealed class PlcTagDefinition
    {
        public PlcTagDefinition(string name, string dataTypeName, string logicalAddress)
        {
            Name = name;
            DataTypeName = dataTypeName;
            LogicalAddress = logicalAddress;
        }

        public string Name { get; }
        public string DataTypeName { get; }
        public string LogicalAddress { get; }
    }
}
