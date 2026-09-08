using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>
    /// Classic/Basic HMI 变量表 XML 离线构建器。
    /// 变量可只声明 HMI 类型，也可通过 Connection + ControllerTag 绑定到 PLC 符号。
    /// </summary>
    public static class ClassicHmiTagTableXmlBuilder
    {
        public static JsonObject BuildFromJson(string tableJson)
        {
            var root = JsonNode.Parse(tableJson) as JsonObject
                ?? throw new ArgumentException("Classic HMI tag table JSON root must be an object.", nameof(tableJson));
            var doc = BuildDocument(root);
            var xml = ToXml(doc);
            var analysis = AnalyzeXml(xml);
            return new JsonObject
            {
                ["format"] = "tia-classic-hmi-tag-table-xml-offline-v1",
                ["timestamp"] = DateTime.Now.ToString("O"),
                ["offlineOnly"] = true,
                ["safetyPolicy"] = new JsonObject
                {
                    ["tia"] = "Offline XML generation only; TIA Portal is not connected.",
                    ["write"] = "No project, reference project, or delivery package content is modified.",
                    ["binding"] = "ControllerTag is a requested PLC symbolic binding; it is not verified until read back from TIA.",
                    ["apply"] = "Import only into a temporary Classic/Basic HMI project first, then read back tags, connections, controller tags, and compile/diagnose."
                },
                ["ok"] = analysis["ok"]?.GetValue<bool>() == true,
                ["analysis"] = analysis,
                ["xml"] = xml
            };
        }

        public static XDocument BuildDocument(JsonObject root)
        {
            var tableName = GetString(root, "Name", "name", GetString(root, "TableName", "tableName", "Classic_HMI_Tags"));
            if (string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("Classic HMI tag table name is required.");
            var tags = (root["Tags"] as JsonArray ?? root["tags"] as JsonArray ?? new JsonArray())
                .OfType<JsonObject>()
                .ToArray();
            ValidateTags(tags);

            return new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("Document",
                    new XElement("Engineering", new XAttribute("version", "V21")),
                    new XElement("DocumentInfo",
                        new XElement("Created", "2000-01-01T00:00:00.0000000Z"),
                        new XElement("ExportSetting", "None"),
                        new XElement("InstalledProducts")),
                    new XElement("Hmi.Tag.TagTable",
                        new XAttribute("ID", "0"),
                        new XElement("AttributeList",
                            new XElement("Name", tableName)),
                        new XElement("ObjectList",
                            tags.Select((tag, index) => BuildTag(index + 1, tag))))));
        }

        public static JsonObject AnalyzeXml(string xml)
        {
            var errors = new JsonArray();
            var warnings = new JsonArray();
            var result = new JsonObject
            {
                ["ok"] = false,
                ["tableName"] = "",
                ["tagCount"] = 0,
                ["symbolicBindingCount"] = 0,
                ["errors"] = errors,
                ["warnings"] = warnings
            };

            try
            {
                var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
                var table = doc.Descendants("Hmi.Tag.TagTable").FirstOrDefault();
                if (table == null)
                {
                    errors.Add("missing-tag-table: Hmi.Tag.TagTable element is required.");
                    return result;
                }

                result["tableName"] = table.Element("AttributeList")?.Element("Name")?.Value ?? "";
                var tags = doc.Descendants("Hmi.Tag.Tag").ToArray();
                result["tagCount"] = tags.Length;
                if (tags.Length == 0) warnings.Add("empty-tag-table: no HMI tags found.");

                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var symbolic = 0;
                foreach (var tag in tags)
                {
                    var name = tag.Element("AttributeList")?.Element("Name")?.Value ?? "";
                    if (string.IsNullOrWhiteSpace(name)) errors.Add("unnamed-tag: Hmi.Tag.Tag has no Name.");
                    else if (!names.Add(name)) errors.Add("duplicate-tag-name: " + name);

                    var controllerTag = tag.Element("LinkList")?.Element("ControllerTag")?.Element("Name")?.Value ?? "";
                    var connection = tag.Element("LinkList")?.Element("Connection")?.Element("Name")?.Value ?? "";
                    if (!string.IsNullOrWhiteSpace(controllerTag) || !string.IsNullOrWhiteSpace(connection))
                    {
                        symbolic++;
                        if (string.IsNullOrWhiteSpace(controllerTag)) errors.Add("symbolic-binding-missing-controller-tag: " + name);
                        if (string.IsNullOrWhiteSpace(connection)) errors.Add("symbolic-binding-missing-connection: " + name);
                    }
                }

                result["symbolicBindingCount"] = symbolic;
                result["ok"] = errors.Count == 0;
                return result;
            }
            catch (Exception ex)
            {
                errors.Add("xml-parse-error: " + ex.Message);
                return result;
            }
        }

        private static XElement BuildTag(int id, JsonObject tag)
        {
            var name = GetString(tag, "Name", "name", "");
            var dataType = GetString(tag, "DataType", "dataType", "Bool");
            var length = GetString(tag, "Length", "length", DefaultLength(dataType));
            var connection = GetString(tag, "Connection", "connection", "");
            var controllerTag = GetString(tag, "ControllerTag", "controllerTag", GetString(tag, "PlcTag", "plcTag", ""));
            var symbolic = !string.IsNullOrWhiteSpace(connection) || !string.IsNullOrWhiteSpace(controllerTag);

            var attributes = new List<XElement>();
            if (symbolic)
            {
                attributes.Add(new XElement("AcquisitionTriggerMode", "Visible"));
                attributes.Add(new XElement("AddressAccessMode", "Symbolic"));
            }
            attributes.Add(new XElement("Length", length));
            if (symbolic) attributes.Add(new XElement("LogicalAddress", ""));
            attributes.Add(new XElement("Name", SecurityElement.Escape(name)));

            var links = new List<XElement>();
            if (symbolic)
            {
                links.Add(OpenLink("AcquisitionCycle", "1 s"));
                links.Add(OpenLink("Connection", connection));
                links.Add(OpenLink("ControllerTag", controllerTag));
            }
            links.Add(OpenLink("DataType", dataType));
            links.Add(OpenLink("HmiDataType", dataType));

            return new XElement("Hmi.Tag.Tag",
                new XAttribute("ID", id.ToString()),
                new XAttribute("CompositionName", "Tags"),
                new XElement("AttributeList", attributes),
                new XElement("LinkList", links));
        }

        private static XElement OpenLink(string elementName, string name)
        {
            return new XElement(elementName,
                new XAttribute("TargetID", "@OpenLink"),
                new XElement("Name", SecurityElement.Escape(name ?? "")));
        }

        private static void ValidateTags(JsonObject[] tags)
        {
            if (tags.Length == 0)
                throw new ArgumentException("Classic HMI tag table requires at least one tag.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in tags)
            {
                var name = GetString(tag, "Name", "name", "");
                if (string.IsNullOrWhiteSpace(name))
                    throw new ArgumentException("Classic HMI tag Name is required.");
                if (!names.Add(name))
                    throw new ArgumentException("Duplicate Classic HMI tag name: " + name);

                var connection = GetString(tag, "Connection", "connection", "");
                var controllerTag = GetString(tag, "ControllerTag", "controllerTag", GetString(tag, "PlcTag", "plcTag", ""));
                if (!string.IsNullOrWhiteSpace(connection) ^ !string.IsNullOrWhiteSpace(controllerTag))
                    throw new ArgumentException("Classic HMI symbolic tag requires both Connection and ControllerTag/PlcTag: " + name);
            }
        }

        private static string DefaultLength(string dataType)
        {
            if (string.IsNullOrWhiteSpace(dataType)) return "2";
            // V21 Classic HMI 要求 Length 与 PLC 数据类型字节数一致；不一致 import 直接拒绝。
            switch (dataType.Trim().ToUpperInvariant())
            {
                case "BOOL":  return "1";
                case "BYTE": case "USINT": case "SINT": case "CHAR": return "1";
                case "WORD": case "INT": case "UINT": return "2";
                case "DWORD": case "DINT": case "UDINT": case "REAL": case "TIME": case "TIME_OF_DAY": case "DATE_AND_TIME": case "TOD": return "4";
                case "LWORD": case "LINT": case "ULINT": case "LREAL": case "LTIME": case "DTL": return "8";
                default: return "2"; // 未识别类型给最常见值，调用方可手工传 Length 覆盖
            }
        }

        private static string GetString(JsonObject obj, string pascal, string camel, string fallback)
        {
            return obj[pascal]?.ToString() ?? obj[camel]?.ToString() ?? fallback;
        }

        private static string ToXml(XDocument document)
        {
            using var writer = new Utf8StringWriter();
            document.Save(writer, SaveOptions.None);
            return writer.ToString();
        }

        private sealed class Utf8StringWriter : StringWriter
        {
            public override Encoding Encoding => Encoding.UTF8;
        }
    }
}
