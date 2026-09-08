using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>
    /// SCL StructuredText/v4 XML 片段构造器。
    /// 第一版只覆盖 IF/ELSE、赋值、局部变量、常量和换行缩进，先服务最小可验证用例。
    /// </summary>
    public sealed class StructuredTextXmlBuilder
    {
        private int _uid;
        private readonly StringBuilder _xml = new StringBuilder();

        public StructuredTextXmlBuilder(int firstUid = 21)
        {
            _uid = firstUid;
        }

        public StructuredTextXmlBuilder Token(string text)
        {
            _xml.Append("<Token Text=\"").Append(Escape(text)).Append("\" UId=\"").Append(Next()).AppendLine("\" />");
            return this;
        }

        public StructuredTextXmlBuilder Blank(int count = 1)
        {
            if (count <= 1)
                _xml.Append("<Blank UId=\"").Append(Next()).AppendLine("\" />");
            else
                _xml.Append("<Blank Num=\"").Append(count).Append("\" UId=\"").Append(Next()).AppendLine("\" />");
            return this;
        }

        public StructuredTextXmlBuilder NewLine()
        {
            _xml.Append("<NewLine UId=\"").Append(Next()).AppendLine("\" />");
            return this;
        }

        public StructuredTextXmlBuilder LocalVariable(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("SCL 局部变量名不能为空。", nameof(name));
            return LocalVariable(new[] { name });
        }

        // 局部变量多段路径：用于多实例 FB 输出 (#trig.Q) / 局部 STRUCT 成员等。
        // V21 schema 要求 Symbol 中各 Component 之间用 <Token Text="."/> 分隔。
        public StructuredTextXmlBuilder LocalVariable(params string[] components)
        {
            if (components == null || components.Length == 0)
                throw new ArgumentException("SCL 局部变量名不能为空。", nameof(components));
            foreach (var c in components)
            {
                if (string.IsNullOrWhiteSpace(c))
                    throw new ArgumentException("SCL 局部变量路径段不能为空。", nameof(components));
                EnsureLocalSymbolSegment(c);
            }

            _xml.Append("<Access Scope=\"LocalVariable\" UId=\"").Append(Next()).AppendLine("\">");
            _xml.Append("<Symbol UId=\"").Append(Next()).AppendLine("\">");
            for (var i = 0; i < components.Length; i++)
            {
                if (i > 0)
                    _xml.Append("<Token Text=\".\" UId=\"").Append(Next()).AppendLine("\" />");
                _xml.Append("<Component Name=\"").Append(Escape(components[i])).Append("\" UId=\"").Append(Next()).AppendLine("\" />");
            }
            _xml.AppendLine("</Symbol>");
            _xml.AppendLine("</Access>");
            return this;
        }

        // 全局变量访问：支持单段（"I_Start"）或多段路径（"DB_Motor.Speed" → ["DB_Motor","Speed"]）。
        // V21 schema 要求 Symbol 中各 Component 之间用 <Token Text="."/> 分隔；单段全局通常带
        // <BooleanAttribute Name="HasQuotes">true</BooleanAttribute> 标记是 quoted 写法。
        public StructuredTextXmlBuilder GlobalVariable(params string[] components)
        {
            if (components == null || components.Length == 0)
                throw new ArgumentException("SCL 全局变量名不能为空。", nameof(components));
            foreach (var c in components)
                if (string.IsNullOrWhiteSpace(c))
                    throw new ArgumentException("SCL 全局变量路径段不能为空。", nameof(components));

            _xml.Append("<Access Scope=\"GlobalVariable\" UId=\"").Append(Next()).AppendLine("\">");
            _xml.Append("<Symbol UId=\"").Append(Next()).AppendLine("\">");
            for (var i = 0; i < components.Length; i++)
            {
                if (i > 0)
                    _xml.Append("<Token Text=\".\" UId=\"").Append(Next()).AppendLine("\" />");
                if (i == 0 && components.Length == 1)
                {
                    // 单段全局：含 HasQuotes 标记
                    _xml.Append("<Component Name=\"").Append(Escape(components[i])).Append("\" UId=\"").Append(Next()).AppendLine("\">");
                    _xml.Append("<BooleanAttribute Name=\"HasQuotes\" UId=\"").Append(Next()).AppendLine("\">true</BooleanAttribute>");
                    _xml.AppendLine("</Component>");
                }
                else
                {
                    _xml.Append("<Component Name=\"").Append(Escape(components[i])).Append("\" UId=\"").Append(Next()).AppendLine("\" />");
                }
            }
            _xml.AppendLine("</Symbol>");
            _xml.AppendLine("</Access>");
            return this;
        }

        // 智能 Symbol：根据 TIA 约定自动决定 scope。支持的输入形式：
        //   "I_Start"           → 全局变量, 单段
        //   "DB.member"         → 全局变量, 多段（整体被引号包裹）
        //   "DB".member         → 全局变量, 多段（仅 DB 名带引号；TIA SCL 文本常见）
        //   "DB.m1".m2          → 全局变量, 多段（部分带引号）
        //   var / #var          → 局部变量, 单段
        //   #trig.Q / var.m     → 局部变量, 多段
        // 规则：只要原输入含有 "，就视为全局；点号一律按段拆。
        public StructuredTextXmlBuilder Symbol(string nameSpec)
        {
            if (string.IsNullOrWhiteSpace(nameSpec))
                throw new ArgumentException("SCL 变量名不能为空。", nameof(nameSpec));
            var trimmed = nameSpec.Trim();
            if (trimmed.StartsWith("#")) trimmed = trimmed.Substring(1);
            var hasQuote = trimmed.Contains('"');
            // 去除所有引号，再按 . 分段
            var stripped = trimmed.Replace("\"", "");
            var parts = stripped.Split('.');
            if (hasQuote) return GlobalVariable(parts);
            if (parts.Length > 1) return LocalVariable(parts);
            return LocalVariable(stripped);
        }

        // 护栏：局部变量段必须是合法 SCL 标识符。condition / source / line 的 {sym} 全部经此校验，
        // 把「整段表达式被当成单个变量名」的静默错误从 TIA 编译期提前到离线 dryRun 阶段。
        // 全局（带引号）符号名允许空格/特殊字符，故只校验局部段，不在 GlobalVariable 调用。
        private static void EnsureLocalSymbolSegment(string segment)
        {
            var s = segment.Trim();
            bool valid = s.Length > 0 && (char.IsLetter(s[0]) || s[0] == '_');
            for (var i = 1; valid && i < s.Length; i++)
                if (!char.IsLetterOrDigit(s[i]) && s[i] != '_')
                    valid = false;

            if (!valid)
                throw new ArgumentException(
                    "SCL 局部符号非法：\"" + segment + "\"。它含运算符/空格/括号，看起来是表达式而非单个变量名。" +
                    "condition / source / {sym} 只接受单个变量名；复杂表达式请用 op:\"line\" + items[]，" +
                    "CASE/FOR/WHILE 或函数调用（ABS/LIMIT/TON 等）请走外部 SCL（ImportPlcExternalSource + GenerateBlocksFromExternalSource）。",
                    nameof(segment));

            if (string.Equals(s, "TRUE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "FALSE", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    "SCL 布尔字面量 \"" + segment + "\" 不能作为符号。赋值请用 literalValue/value，行内请用 {lit:\"" + s.ToUpperInvariant() + "\"}。",
                    nameof(segment));
        }

        public StructuredTextXmlBuilder LiteralConstant(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("SCL 常量不能为空。", nameof(value));

            _xml.Append("<Access Scope=\"LiteralConstant\" UId=\"").Append(Next()).AppendLine("\">");
            _xml.Append("<Constant UId=\"").Append(Next()).AppendLine("\">");
            _xml.Append("<ConstantValue UId=\"").Append(Next()).Append("\">").Append(Escape(value)).AppendLine("</ConstantValue>");
            _xml.AppendLine("</Constant>");
            _xml.AppendLine("</Access>");
            return this;
        }

        public StructuredTextXmlBuilder Assignment(string target, string literalValue, int indent = 0)
        {
            if (indent > 0) Blank(indent);
            // target 通过 Symbol 智能识别 scope（"x" 全局 / x 局部）
            Symbol(target).Blank().Token(":=").Blank().LiteralConstant(literalValue).Token(";").NewLine();
            return this;
        }

        // 符号 -> 符号 赋值，例如 "Q_RunLamp4" := "Q_RunLamp3"。target 与 source 各自走 Symbol 识别。
        public StructuredTextXmlBuilder AssignFromSymbol(string target, string source, int indent = 0)
        {
            if (indent > 0) Blank(indent);
            Symbol(target).Blank().Token(":=").Blank().Symbol(source).Token(";").NewLine();
            return this;
        }

        public StructuredTextXmlBuilder IfHeader(string conditionVariable, int indent = 0)
        {
            if (indent > 0) Blank(indent);
            Token("IF").Blank().Symbol(conditionVariable).Blank().Token("THEN").NewLine();
            return this;
        }

        // 新增：ELSIF — 多段优先级常用
        public StructuredTextXmlBuilder ElsIfHeader(string conditionVariable, int indent = 0)
        {
            if (indent > 0) Blank(indent);
            Token("ELSIF").Blank().Symbol(conditionVariable).Blank().Token("THEN").NewLine();
            return this;
        }

        public StructuredTextXmlBuilder ElseLine(int indent = 0)
        {
            if (indent > 0) Blank(indent);
            Token("ELSE").NewLine();
            return this;
        }

        public StructuredTextXmlBuilder EndIf(int indent = 0)
        {
            if (indent > 0) Blank(indent);
            Token("END_IF").Token(";").NewLine();
            return this;
        }

        public string BuildInnerXml()
        {
            return _xml.ToString();
        }

        public string BuildStructuredTextXml()
        {
            return "<StructuredText xmlns=\"http://www.siemens.com/automation/Openness/SW/NetworkSource/StructuredText/v4\">" +
                   Environment.NewLine +
                   BuildInnerXml() +
                   "</StructuredText>";
        }

        public static JsonObject RunProbe(string fixtureDirectory, string reportDirectory)
        {
            Directory.CreateDirectory(reportDirectory);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var goldenPath = Path.Combine(fixtureDirectory, "FC_StartStop.xml");
            var generatedPath = Path.Combine(reportDirectory, "StructuredText_StartStop.generated_" + stamp + ".xml");
            var jsonPath = Path.Combine(reportDirectory, "structured_text_builder_probe_" + stamp + ".json");
            var mdPath = Path.Combine(reportDirectory, "structured_text_builder_probe_" + stamp + ".md");

            var generatedXml = new StructuredTextXmlBuilder()
                .IfHeader("EStop")
                .Assignment("Run", "FALSE", indent: 2)
                .ElseLine()
                .IfHeader("Stop", indent: 2)
                .Assignment("Run", "FALSE", indent: 4)
                .EndIf(indent: 2)
                .IfHeader("Start", indent: 2)
                .Assignment("Run", "TRUE", indent: 4)
                .EndIf(indent: 2)
                .Token("END_IF").Token(";")
                .BuildStructuredTextXml();

            File.WriteAllText(generatedPath, generatedXml, Encoding.UTF8);
            var golden = AnalyzeStructuredText(goldenPath);
            var generated = AnalyzeStructuredText(generatedPath);
            var semanticEqual = CompareSemantics(golden, generated);

            var root = new JsonObject
            {
                ["timestamp"] = DateTime.Now.ToString("O"),
                ["mode"] = "offline-structured-text-builder-probe",
                ["goldenPath"] = goldenPath,
                ["generatedPath"] = generatedPath,
                ["safetyPolicy"] = new JsonObject
                {
                    ["tia"] = "离线生成和解析 StructuredText XML，不连接 TIA Portal，不导入 PLC 块。",
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

        public static JsonObject AnalyzeStructuredText(string path)
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
                var structuredText = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "StructuredText");
                var tokens = doc.Descendants().Where(x => x.Name.LocalName == "Token")
                    .Select(x => x.Attribute("Text")?.Value ?? "")
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToArray();
                var variables = doc.Descendants().Where(x => x.Name.LocalName == "Component")
                    .Select(x => x.Attribute("Name")?.Value ?? "")
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToArray();
                var constants = doc.Descendants().Where(x => x.Name.LocalName == "ConstantValue")
                    .Select(x => x.Value)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToArray();

                root["ok"] = structuredText != null && tokens.Length > 0;
                root["tokens"] = new JsonArray(tokens.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
                root["variables"] = new JsonArray(variables.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
                root["constants"] = new JsonArray(constants.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
                root["assignmentCount"] = tokens.Count(x => x == ":=");
                root["ifCount"] = tokens.Count(x => x == "IF");
                root["endIfCount"] = tokens.Count(x => x == "END_IF");
                return root;
            }
            catch (Exception ex)
            {
                root["ok"] = false;
                root["error"] = ex.Message;
                return root;
            }
        }

        private static bool CompareSemantics(JsonObject golden, JsonObject generated)
        {
            return NormalizeArray(golden, "tokens").SequenceEqual(NormalizeArray(generated, "tokens"), StringComparer.Ordinal) &&
                   NormalizeArray(golden, "variables").SequenceEqual(NormalizeArray(generated, "variables"), StringComparer.Ordinal) &&
                   NormalizeArray(golden, "constants").SequenceEqual(NormalizeArray(generated, "constants"), StringComparer.Ordinal) &&
                   golden["assignmentCount"]?.ToString() == generated["assignmentCount"]?.ToString() &&
                   golden["ifCount"]?.ToString() == generated["ifCount"]?.ToString() &&
                   golden["endIfCount"]?.ToString() == generated["endIfCount"]?.ToString();
        }

        private static string[] NormalizeArray(JsonObject root, string name)
        {
            return (root[name] as JsonArray ?? new JsonArray())
                .Select(x => x?.ToString() ?? "")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
        }

        private static string BuildProbeMarkdown(JsonObject root, string jsonPath)
        {
            var md = new StringBuilder();
            md.AppendLine("# StructuredText Builder Probe");
            md.AppendLine();
            md.AppendLine("Generated: " + root["timestamp"]);
            md.AppendLine("JSON: " + jsonPath);
            md.AppendLine();
            md.AppendLine("## Safety");
            md.AppendLine("- 离线生成和解析 StructuredText XML，不连接 TIA Portal，不导入 PLC 块。");
            md.AppendLine("- 只写 reports 目录下的生成样本和探针报告，不修改 TMP_EXPORT 或交付包。");
            md.AppendLine();
            md.AppendLine("## Summary");
            md.AppendLine("- OK: " + root["ok"]);
            md.AppendLine("- Semantic equal to golden: " + root["semanticEqual"]);
            md.AppendLine("- Golden: " + root["goldenPath"]);
            md.AppendLine("- Generated: " + root["generatedPath"]);
            md.AppendLine();
            if (root["generated"] is JsonObject generated)
            {
                md.AppendLine("## Generated Semantics");
                md.AppendLine("- IF count: " + generated["ifCount"]);
                md.AppendLine("- END_IF count: " + generated["endIfCount"]);
                md.AppendLine("- Assignment count: " + generated["assignmentCount"]);
                md.AppendLine("- Variables: " + string.Join(", ", NormalizeArray(generated, "variables")));
                md.AppendLine("- Constants: " + string.Join(", ", NormalizeArray(generated, "constants")));
            }

            return md.ToString();
        }

        private int Next()
        {
            return _uid++;
        }

        private static string Escape(string value)
        {
            return SecurityElement.Escape(value) ?? "";
        }
    }
}
