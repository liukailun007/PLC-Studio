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
    /// LAD FlgNet/v5 调用网络构造器。
    /// 第一版覆盖“变量/常量 Access + FC Call + Wire 参数连接”的常见网络。
    /// </summary>
    public static class FlgNetCallXmlBuilder
    {
        private static readonly XNamespace FlgNetNs = "http://www.siemens.com/automation/Openness/SW/NetworkSource/FlgNet/v5";

        public static XDocument BuildDocument(string callName, IEnumerable<FlgNetCallParameter> parameters)
        {
            var root = BuildFlgNet(callName, parameters);
            return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        }

        public static XElement BuildFlgNet(string callName, IEnumerable<FlgNetCallParameter> parameters)
        {
            if (string.IsNullOrWhiteSpace(callName))
                throw new ArgumentException("FlgNet 调用块名称不能为空。", nameof(callName));

            var parameterList = parameters?.ToArray() ?? Array.Empty<FlgNetCallParameter>();
            ValidateParameters(parameterList);

            var uid = 21;
            var accessParts = new List<XElement>();
            foreach (var parameter in parameterList)
            {
                parameter.AccessUid = uid++;
                accessParts.Add(parameter.SourceKind == FlgNetSourceKind.LiteralConstant
                    ? BuildLiteralAccess(parameter)
                    : BuildGlobalAccess(parameter));
            }

            var callUid = uid++;
            var call = new XElement(FlgNetNs + "Call",
                new XAttribute("UId", callUid),
                new XElement(FlgNetNs + "CallInfo",
                    new XAttribute("Name", callName),
                    new XAttribute("BlockType", "FC"),
                    parameterList.Select(x => new XElement(FlgNetNs + "Parameter",
                        new XAttribute("Name", x.ParameterName),
                        new XAttribute("Section", x.Section),
                        new XAttribute("Type", x.DataType)))));

            var wireUid = uid++;
            var wires = new List<XElement>
            {
                new XElement(FlgNetNs + "Wire",
                    new XAttribute("UId", wireUid++),
                    new XElement(FlgNetNs + "Powerrail"),
                    new XElement(FlgNetNs + "NameCon",
                        new XAttribute("UId", callUid),
                        new XAttribute("Name", "en")))
            };

            foreach (var parameter in parameterList)
            {
                wires.Add(parameter.Section == "Output"
                    ? new XElement(FlgNetNs + "Wire",
                        new XAttribute("UId", wireUid++),
                        new XElement(FlgNetNs + "NameCon",
                            new XAttribute("UId", callUid),
                            new XAttribute("Name", parameter.ParameterName)),
                        new XElement(FlgNetNs + "IdentCon",
                            new XAttribute("UId", parameter.AccessUid)))
                    : new XElement(FlgNetNs + "Wire",
                        new XAttribute("UId", wireUid++),
                        new XElement(FlgNetNs + "IdentCon",
                            new XAttribute("UId", parameter.AccessUid)),
                        new XElement(FlgNetNs + "NameCon",
                            new XAttribute("UId", callUid),
                            new XAttribute("Name", parameter.ParameterName))));
            }

            return new XElement(FlgNetNs + "FlgNet",
                new XElement(FlgNetNs + "Parts", accessParts.Concat(new[] { call })),
                new XElement(FlgNetNs + "Wires", wires));
        }

        public static string BuildXml(string callName, IEnumerable<FlgNetCallParameter> parameters)
        {
            using var writer = new Utf8StringWriter();
            BuildDocument(callName, parameters).Save(writer, SaveOptions.None);
            return writer.ToString();
        }

        public static JsonObject RunProbe(string workspaceRoot, string reportDirectory)
        {
            Directory.CreateDirectory(reportDirectory);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var goldenPath = Path.Combine(workspaceRoot, "TMP_EXPORT", "Source", "5T车", "Blocks", "01_手动控制", "FC控制", "05-故障保护.xml");
            var generatedPath = Path.Combine(reportDirectory, "FlgNet_LimitProtect.generated_" + stamp + ".xml");
            var jsonPath = Path.Combine(reportDirectory, "flgnet_call_builder_probe_" + stamp + ".json");
            var mdPath = Path.Combine(reportDirectory, "flgnet_call_builder_probe_" + stamp + ".md");

            var parameters = new[]
            {
                FlgNetCallParameter.Global("Current_Location", "Input", "Real", "21_DB_interface", "Manual", "Gantry_CurX"),
                FlgNetCallParameter.Global("Current_Speed", "Input", "Real", "21_DB_interface", "Manual", "Gantry_FdbackSpeed"),
                FlgNetCallParameter.Global("Max_Acc", "Input", "Real", "A0_DB_InitData", "Gantry", "ACC_Max"),
                FlgNetCallParameter.Global("SafeRangeMax", "Input", "Real", "A0_DB_InitData", "Gantry", "RunSafePos_Max"),
                FlgNetCallParameter.Global("SafeRangeMin", "Input", "Real", "A0_DB_InitData", "Gantry", "RunSafePos_Min"),
                FlgNetCallParameter.Constant("Location_Reserve", "Input", "Real", "500.0"),
                FlgNetCallParameter.Constant("Force_Bool", "Input", "Bool", "0"),
                FlgNetCallParameter.Constant("Pos_Limit", "Input", "Bool", "1"),
                FlgNetCallParameter.Constant("Neg_Limit", "Input", "Bool", "1"),
                FlgNetCallParameter.Global("Reset", "Input", "Bool", "复位"),
                FlgNetCallParameter.Global("Pos_Stop", "Output", "Bool", "大车后退停止限位"),
                FlgNetCallParameter.Global("Neg_Stop", "Output", "Bool", "大车前进停止限位")
            };

            File.WriteAllText(generatedPath, BuildXml("Limit_Protect", parameters), Encoding.UTF8);
            var golden = AnalyzeFirstFlgNet(goldenPath);
            var generated = AnalyzeFirstFlgNet(generatedPath);
            var semanticEqual = CompareSemantics(golden, generated);

            var root = new JsonObject
            {
                ["timestamp"] = DateTime.Now.ToString("O"),
                ["mode"] = "offline-flgnet-call-builder-probe",
                ["goldenPath"] = goldenPath,
                ["generatedPath"] = generatedPath,
                ["safetyPolicy"] = new JsonObject
                {
                    ["tia"] = "离线生成和解析 FlgNet XML，不连接 TIA Portal，不导入 PLC 块。",
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

        public static JsonObject AnalyzeFirstFlgNet(string path)
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
                var flgNet = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "FlgNet");
                if (flgNet == null && doc.Root?.Name.LocalName == "FlgNet")
                    flgNet = doc.Root;
                var call = flgNet?.Descendants().FirstOrDefault(x => x.Name.LocalName == "Call");
                var callInfo = call?.Elements().FirstOrDefault(x => x.Name.LocalName == "CallInfo");
                var accesses = flgNet?.Descendants().Where(x => x.Name.LocalName == "Access").Select(ReadAccess).ToArray() ?? Array.Empty<JsonObject>();
                var parameters = callInfo?.Elements().Where(x => x.Name.LocalName == "Parameter")
                    .Select(x => new JsonObject
                    {
                        ["name"] = x.Attribute("Name")?.Value ?? "",
                        ["section"] = x.Attribute("Section")?.Value ?? "",
                        ["type"] = x.Attribute("Type")?.Value ?? ""
                    })
                    .ToArray() ?? Array.Empty<JsonObject>();

                root["ok"] = flgNet != null && callInfo != null;
                root["callName"] = callInfo?.Attribute("Name")?.Value ?? "";
                root["blockType"] = callInfo?.Attribute("BlockType")?.Value ?? "";
                root["accesses"] = new JsonArray(accesses);
                root["parameters"] = new JsonArray(parameters);
                root["wireCount"] = flgNet?.Descendants().Count(x => x.Name.LocalName == "Wire") ?? 0;
                root["identConCount"] = flgNet?.Descendants().Count(x => x.Name.LocalName == "IdentCon") ?? 0;
                root["nameConCount"] = flgNet?.Descendants().Count(x => x.Name.LocalName == "NameCon") ?? 0;
                return root;
            }
            catch (Exception ex)
            {
                root["ok"] = false;
                root["error"] = ex.Message;
                return root;
            }
        }

        private static JsonObject ReadAccess(XElement access)
        {
            var scope = access.Attribute("Scope")?.Value ?? "";
            var components = access.Descendants()
                .Where(x => x.Name.LocalName == "Component")
                .Select(x => x.Attribute("Name")?.Value ?? "")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
            var constantType = access.Descendants().FirstOrDefault(x => x.Name.LocalName == "ConstantType")?.Value ?? "";
            var constantValue = access.Descendants().FirstOrDefault(x => x.Name.LocalName == "ConstantValue")?.Value ?? "";
            return new JsonObject
            {
                ["scope"] = scope,
                ["path"] = string.Join(".", components),
                ["constantType"] = constantType,
                ["constantValue"] = constantValue
            };
        }

        private static XElement BuildGlobalAccess(FlgNetCallParameter parameter)
        {
            return new XElement(FlgNetNs + "Access",
                new XAttribute("Scope", "GlobalVariable"),
                new XAttribute("UId", parameter.AccessUid),
                new XElement(FlgNetNs + "Symbol",
                    parameter.SymbolPath.Select(x => new XElement(FlgNetNs + "Component",
                        new XAttribute("Name", x)))));
        }

        private static XElement BuildLiteralAccess(FlgNetCallParameter parameter)
        {
            return new XElement(FlgNetNs + "Access",
                new XAttribute("Scope", "LiteralConstant"),
                new XAttribute("UId", parameter.AccessUid),
                new XElement(FlgNetNs + "Constant",
                    new XElement(FlgNetNs + "ConstantType", parameter.DataType),
                    new XElement(FlgNetNs + "ConstantValue", parameter.ConstantValue)));
        }

        private static bool CompareSemantics(JsonObject golden, JsonObject generated)
        {
            return golden["callName"]?.ToString() == generated["callName"]?.ToString() &&
                   golden["blockType"]?.ToString() == generated["blockType"]?.ToString() &&
                   golden["wireCount"]?.ToString() == generated["wireCount"]?.ToString() &&
                   golden["identConCount"]?.ToString() == generated["identConCount"]?.ToString() &&
                   golden["nameConCount"]?.ToString() == generated["nameConCount"]?.ToString() &&
                   NormalizeArray(golden, "parameters").SequenceEqual(NormalizeArray(generated, "parameters"), StringComparer.Ordinal) &&
                   NormalizeArray(golden, "accesses").SequenceEqual(NormalizeArray(generated, "accesses"), StringComparer.Ordinal);
        }

        private static string[] NormalizeArray(JsonObject root, string name)
        {
            return (root[name] as JsonArray ?? new JsonArray())
                .OfType<JsonObject>()
                .Select(x => string.Join("|", x.Select(kv => kv.Key + "=" + kv.Value)))
                .ToArray();
        }

        private static string BuildProbeMarkdown(JsonObject root, string jsonPath)
        {
            var md = new StringBuilder();
            md.AppendLine("# FlgNet Call Builder Probe");
            md.AppendLine();
            md.AppendLine("Generated: " + root["timestamp"]);
            md.AppendLine("JSON: " + jsonPath);
            md.AppendLine();
            md.AppendLine("## Safety");
            md.AppendLine("- 离线生成和解析 FlgNet XML，不连接 TIA Portal，不导入 PLC 块。");
            md.AppendLine("- 只写 reports 目录下的生成样本和探针报告，不修改 TMP_EXPORT 或交付包。");
            md.AppendLine();
            md.AppendLine("## Summary");
            md.AppendLine("- OK: " + root["ok"]);
            md.AppendLine("- Semantic equal to golden: " + root["semanticEqual"]);
            md.AppendLine("- Golden: " + root["goldenPath"]);
            md.AppendLine("- Generated: " + root["generatedPath"]);
            if (root["generated"] is JsonObject generated)
            {
                md.AppendLine("- Call: " + generated["callName"]);
                md.AppendLine("- Wires: " + generated["wireCount"]);
            }

            return md.ToString();
        }

        private static void ValidateParameters(IReadOnlyCollection<FlgNetCallParameter> parameters)
        {
            var duplicateNames = parameters.GroupBy(x => x.ParameterName, StringComparer.OrdinalIgnoreCase)
                .Where(x => x.Count() > 1)
                .Select(x => x.Key)
                .ToArray();
            if (duplicateNames.Length > 0)
                throw new ArgumentException("FlgNet 参数名重复: " + string.Join(", ", duplicateNames));

            foreach (var parameter in parameters)
            {
                if (string.IsNullOrWhiteSpace(parameter.ParameterName))
                    throw new ArgumentException("FlgNet 参数名不能为空。");
                if (string.IsNullOrWhiteSpace(parameter.Section))
                    throw new ArgumentException("FlgNet 参数 Section 不能为空: " + parameter.ParameterName);
                if (string.IsNullOrWhiteSpace(parameter.DataType))
                    throw new ArgumentException("FlgNet 参数 Type 不能为空: " + parameter.ParameterName);
                if (parameter.SourceKind == FlgNetSourceKind.GlobalVariable && parameter.SymbolPath.Length == 0)
                    throw new ArgumentException("FlgNet 全局变量参数必须提供符号路径: " + parameter.ParameterName);
                if (parameter.SourceKind == FlgNetSourceKind.LiteralConstant && string.IsNullOrWhiteSpace(parameter.ConstantValue))
                    throw new ArgumentException("FlgNet 常量参数必须提供常量值: " + parameter.ParameterName);
            }
        }

        private sealed class Utf8StringWriter : StringWriter
        {
            public override Encoding Encoding => Encoding.UTF8;
        }
    }

    public sealed class FlgNetCallParameter
    {
        private FlgNetCallParameter(string parameterName, string section, string dataType, FlgNetSourceKind sourceKind, string constantValue, string[] symbolPath)
        {
            ParameterName = parameterName;
            Section = section;
            DataType = dataType;
            SourceKind = sourceKind;
            ConstantValue = constantValue;
            SymbolPath = symbolPath;
        }

        public string ParameterName { get; }
        public string Section { get; }
        public string DataType { get; }
        public FlgNetSourceKind SourceKind { get; }
        public string ConstantValue { get; }
        public string[] SymbolPath { get; }
        public int AccessUid { get; set; }

        public static FlgNetCallParameter Global(string parameterName, string section, string dataType, params string[] symbolPath)
        {
            return new FlgNetCallParameter(parameterName, section, dataType, FlgNetSourceKind.GlobalVariable, "", symbolPath);
        }

        public static FlgNetCallParameter Constant(string parameterName, string section, string dataType, string value)
        {
            return new FlgNetCallParameter(parameterName, section, dataType, FlgNetSourceKind.LiteralConstant, value, Array.Empty<string>());
        }
    }

    public enum FlgNetSourceKind
    {
        GlobalVariable,
        LiteralConstant
    }
}
