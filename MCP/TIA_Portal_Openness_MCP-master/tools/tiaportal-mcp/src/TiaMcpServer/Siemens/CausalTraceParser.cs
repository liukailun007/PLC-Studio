using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace TiaMcpServer.Siemens
{
    // Pure SimaticML logic parser for the causal tracer. Deliberately has NO
    // TIA Openness dependency so it can be unit-tested standalone against exported
    // block XML. Two network shapes are handled, matched by element LocalName
    // (namespace/version agnostic):
    //   - StructuredText: token stream  (LHS := RHS;)
    //   - FlgNet (LAD/FBD): Parts (Access operands + Contact/Coil instructions) + Wires
    public static class CausalTraceParser
    {
        // Test seam: run the parser on a single exported block XML file.
        public static JsonObject AnalyzeXmlFileForTest(string xmlPath, string tag)
        {
            var writeSites = new JsonArray();
            var readSites = new JsonArray();
            var conditions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var doc = XDocument.Load(xmlPath);
            string blockName = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Name")?.Value ?? Path.GetFileNameWithoutExtension(xmlPath);
            AnalyzeBlock(doc, blockName, blockName, NormalizeOperand(tag), tag, writeSites, readSites, conditions);
            return new JsonObject
            {
                ["block"] = blockName,
                ["tag"] = tag,
                ["writeSites"] = writeSites,
                ["readSites"] = readSites,
                ["gatingConditions"] = new JsonArray(conditions.OrderBy(x => x).Select(x => JsonValue.Create(x)).ToArray())
            };
        }

        public static void AnalyzeBlock(XDocument doc, string blockName, string blockPath, string normTag, string displayTag,
            JsonArray writeSites, JsonArray readSites, HashSet<string> allConditions)
        {
            var compileUnits = doc.Descendants().Where(e => e.Name.LocalName == "SW.Blocks.CompileUnit").ToList();
            int netIndex = 0;
            foreach (var cu in compileUnits)
            {
                netIndex++;
                var src = cu.Descendants().FirstOrDefault(e => e.Name.LocalName == "NetworkSource");
                if (src == null) continue;
                string title = cu.Descendants().FirstOrDefault(e => e.Name.LocalName == "Title")?
                    .Descendants().FirstOrDefault(e => e.Name.LocalName == "Text")?.Value ?? "";

                var st = src.Elements().FirstOrDefault(e => e.Name.LocalName == "StructuredText");
                var flg = src.Elements().FirstOrDefault(e => e.Name.LocalName == "FlgNet");

                if (st != null)
                    AnalyzeStructuredText(st, blockName, blockPath, netIndex, title, normTag, displayTag, writeSites, readSites, allConditions);
                else if (flg != null)
                    AnalyzeFlgNet(flg, blockName, blockPath, netIndex, title, normTag, displayTag, writeSites, readSites, allConditions);
            }
        }

        // ── StructuredText ───────────────────────────────────────────────────────
        private static void AnalyzeStructuredText(XElement st, string blockName, string blockPath, int netIndex, string title,
            string normTag, string displayTag, JsonArray writeSites, JsonArray readSites, HashSet<string> allConditions)
        {
            var tokens = new List<(string kind, string text, bool isLiteral)>();
            foreach (var child in st.Elements())
            {
                switch (child.Name.LocalName)
                {
                    case "Access":
                        bool lit;
                        string op = ResolveOperand(child, out lit) ?? "";
                        tokens.Add(("OPERAND", op, lit));
                        break;
                    case "Token":
                        tokens.Add(("OP", child.Attribute("Text")?.Value ?? "", false));
                        break;
                }
            }

            var allOperandsInNet = tokens.Where(t => t.kind == "OPERAND" && !t.isLiteral).Select(t => t.text).Where(s => s.Length > 0).ToList();
            bool netReferencesTag = allOperandsInNet.Any(o => OperandMatches(o, normTag));

            var statement = new List<(string kind, string text, bool isLiteral)>();
            foreach (var t in tokens)
            {
                if (t.kind == "OP" && t.text == ";")
                {
                    HandleStatement(statement, blockName, blockPath, netIndex, title, normTag, allOperandsInNet, writeSites, allConditions);
                    statement.Clear();
                }
                else statement.Add(t);
            }
            if (statement.Count > 0)
                HandleStatement(statement, blockName, blockPath, netIndex, title, normTag, allOperandsInNet, writeSites, allConditions);

            if (netReferencesTag && !TagIsWrittenInList(writeSites, blockName, netIndex))
                AddReadSite(readSites, blockName, blockPath, netIndex, title, "StructuredText");
        }

        private static void HandleStatement(List<(string kind, string text, bool isLiteral)> stmt, string blockName, string blockPath,
            int netIndex, string title, string normTag, List<string> allOperandsInNet, JsonArray writeSites, HashSet<string> allConditions)
        {
            int assignIdx = stmt.FindIndex(t => t.kind == "OP" && t.text == ":=");
            if (assignIdx <= 0) return;
            var lhs = stmt.Take(assignIdx).LastOrDefault(t => t.kind == "OPERAND");
            if (lhs.kind != "OPERAND" || lhs.isLiteral) return;
            if (!OperandMatches(lhs.text, normTag)) return;

            var rhs = stmt.Skip(assignIdx + 1).Where(t => t.kind == "OPERAND" && !t.isLiteral).Select(t => t.text).Distinct().ToList();
            string stmtText = ReconstructStatement(stmt);

            var conds = new JsonArray();
            foreach (var c in allOperandsInNet.Distinct())
            {
                if (OperandMatches(c, normTag)) continue;
                conds.Add(c);
                allConditions.Add(c);
            }

            writeSites.Add(new JsonObject
            {
                ["block"] = blockName,
                ["blockPath"] = blockPath,
                ["network"] = netIndex,
                ["title"] = title,
                ["language"] = "StructuredText",
                ["writeKind"] = ":=",
                ["target"] = lhs.text,
                ["statement"] = stmtText,
                ["directRhsOperands"] = new JsonArray(rhs.Select(x => JsonValue.Create(x)).ToArray()),
                ["networkConditions"] = conds
            });
        }

        private static string ReconstructStatement(List<(string kind, string text, bool isLiteral)> stmt)
        {
            var sb = new StringBuilder();
            foreach (var t in stmt)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(t.text);
            }
            sb.Append(';');
            return sb.ToString();
        }

        // ── FlgNet (LAD/FBD) ──────────────────────────────────────────────────────
        private static void AnalyzeFlgNet(XElement flg, string blockName, string blockPath, int netIndex, string title,
            string normTag, string displayTag, JsonArray writeSites, JsonArray readSites, HashSet<string> allConditions)
        {
            var parts = flg.Elements().FirstOrDefault(e => e.Name.LocalName == "Parts");
            var wires = flg.Elements().FirstOrDefault(e => e.Name.LocalName == "Wires");
            if (parts == null) return;

            var accessByUid = new Dictionary<string, string>();
            var allOperands = new List<string>();
            foreach (var acc in parts.Elements().Where(e => e.Name.LocalName == "Access"))
            {
                string uid = acc.Attribute("UId")?.Value ?? "";
                bool lit;
                string op = ResolveOperand(acc, out lit) ?? "";
                if (uid.Length > 0) accessByUid[uid] = op;
                if (!lit && op.Length > 0) allOperands.Add(op);
            }

            var coilTypes = new HashSet<string> { "Coil", "SCoil", "RCoil" };
            var coils = parts.Elements()
                .Where(e => e.Name.LocalName == "Part" && coilTypes.Contains(e.Attribute("Name")?.Value ?? ""))
                .ToList();

            bool wroteTagHere = false;
            foreach (var coil in coils)
            {
                string coilUid = coil.Attribute("UId")?.Value ?? "";
                string coilType = coil.Attribute("Name")?.Value ?? "Coil";
                string? operandUid = FindCoilOperandUid(wires, coilUid);
                if (operandUid == null || !accessByUid.TryGetValue(operandUid, out var coilOperand)) continue;
                if (!OperandMatches(coilOperand, normTag)) continue;

                wroteTagHere = true;
                string writeKind = coilType == "SCoil" ? "S (set)" : coilType == "RCoil" ? "R (reset)" : "= (assign)";

                var conds = new JsonArray();
                foreach (var op in allOperands.Distinct())
                {
                    if (OperandMatches(op, normTag)) continue;
                    conds.Add(op);
                    allConditions.Add(op);
                }

                writeSites.Add(new JsonObject
                {
                    ["block"] = blockName,
                    ["blockPath"] = blockPath,
                    ["network"] = netIndex,
                    ["title"] = title,
                    ["language"] = "LAD",
                    ["writeKind"] = writeKind,
                    ["target"] = coilOperand,
                    ["networkConditions"] = conds
                });
            }

            if (!wroteTagHere && allOperands.Any(o => OperandMatches(o, normTag)))
                AddReadSite(readSites, blockName, blockPath, netIndex, title, "LAD");
        }

        private static string? FindCoilOperandUid(XElement? wires, string coilUid)
        {
            if (wires == null || coilUid.Length == 0) return null;
            foreach (var wire in wires.Elements().Where(e => e.Name.LocalName == "Wire"))
            {
                bool touchesCoilOperand = wire.Elements().Any(c =>
                    c.Name.LocalName == "NameCon" &&
                    (c.Attribute("UId")?.Value == coilUid) &&
                    (c.Attribute("Name")?.Value == "operand"));
                if (!touchesCoilOperand) continue;
                var ident = wire.Elements().FirstOrDefault(c => c.Name.LocalName == "IdentCon");
                var uid = ident?.Attribute("UId")?.Value;
                if (!string.IsNullOrEmpty(uid)) return uid;
            }
            return null;
        }

        // ── Operand resolution & matching ─────────────────────────────────────────
        private static string? ResolveOperand(XElement access, out bool isLiteral)
        {
            isLiteral = false;
            string scope = access.Attribute("Scope")?.Value ?? "";
            if (scope == "LiteralConstant" || scope == "TypedConstant")
            {
                isLiteral = true;
                return access.Descendants().FirstOrDefault(e => e.Name.LocalName == "ConstantValue")?.Value ?? "";
            }

            var symbol = access.Elements().FirstOrDefault(e => e.Name.LocalName == "Symbol");
            if (symbol == null) return null;

            var sb = new StringBuilder();
            foreach (var comp in symbol.Elements().Where(e => e.Name.LocalName == "Component"))
            {
                if (sb.Length > 0) sb.Append('.');
                sb.Append(comp.Attribute("Name")?.Value ?? "");
                var idx = comp.Elements().FirstOrDefault(e => e.Name.LocalName == "Access");
                if (idx != null)
                {
                    var iv = idx.Descendants().FirstOrDefault(e => e.Name.LocalName == "ConstantValue")?.Value;
                    if (iv != null) sb.Append('[').Append(iv).Append(']');
                }
            }
            string result = sb.ToString();
            if (scope == "LocalVariable") result = "#" + result;
            return result;
        }

        public static string NormalizeOperand(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
                if (ch != '"' && ch != '#' && !char.IsWhiteSpace(ch)) sb.Append(char.ToLowerInvariant(ch));
            return sb.ToString();
        }

        private static bool OperandMatches(string operand, string normTag)
        {
            string a = NormalizeOperand(operand);
            if (a.Length == 0 || normTag.Length == 0) return false;
            if (a == normTag) return true;
            string aBase = StripIndex(a);
            string tBase = StripIndex(normTag);
            if (aBase == tBase) return true;
            if (aBase.EndsWith("." + tBase, StringComparison.Ordinal)) return true;
            if (tBase.EndsWith("." + aBase, StringComparison.Ordinal)) return true;
            return false;
        }

        private static string StripIndex(string s)
        {
            int i = s.IndexOf('[');
            return i >= 0 ? s.Substring(0, i) : s;
        }

        private static void AddReadSite(JsonArray readSites, string blockName, string blockPath, int netIndex, string title, string lang)
        {
            readSites.Add(new JsonObject
            {
                ["block"] = blockName,
                ["blockPath"] = blockPath,
                ["network"] = netIndex,
                ["title"] = title,
                ["language"] = lang
            });
        }

        private static bool TagIsWrittenInList(JsonArray writeSites, string blockName, int netIndex)
        {
            foreach (var w in writeSites.OfType<JsonObject>())
                if (w["block"]?.ToString() == blockName && (w["network"]?.GetValue<int>() ?? -1) == netIndex)
                    return true;
            return false;
        }
    }
}
