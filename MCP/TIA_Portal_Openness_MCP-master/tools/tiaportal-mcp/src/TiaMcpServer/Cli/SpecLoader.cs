using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using YamlDotNet.Serialization;

namespace TiaMcpServer.Cli
{
    /// <summary>
    /// Loads a project spec from a .json (pass-through) or .yaml/.yml file and returns a JSON
    /// string suitable for <c>McpServer.ScaffoldProject</c> / <c>McpServer.PatchProject</c>.
    /// JSON is the canonical, zero-ambiguity form (AIs should emit it); YAML is a human convenience.
    /// </summary>
    public static class SpecLoader
    {
        public static string LoadAsJson(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"spec file not found: {path}");

            var text = File.ReadAllText(path); // ReadAllText strips a UTF-8 BOM if present
            text = ResolveBundleToken(text);
            var ext = Path.GetExtension(path).ToLowerInvariant();

            if (ext == ".json")
                return text; // pass-through, no YAML round-trip

            if (ext == ".yaml" || ext == ".yml")
                return YamlToJson(text);

            // Unknown extension: sniff. Leading { or [ means JSON, otherwise treat as YAML.
            var trimmed = text.TrimStart();
            if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
                return text;
            return YamlToJson(text);
        }

        // The shipped spec templates reference bundled .scl/.s7dcl files via a "__BUNDLE__" token
        // so they work without the user hand-editing absolute paths. Resolve it to the package
        // root (found by walking up from the exe to a dir that has both templates/ and tools/).
        // Forward slashes are used so the result stays valid inside JSON string values (no \-escaping
        // needed) and Windows file APIs accept them. If the root can't be found, the token is left
        // as-is and the user must substitute it manually.
        private static string ResolveBundleToken(string text)
        {
            if (!text.Contains("__BUNDLE__")) return text;
            var root = FindBundleRoot();
            if (root == null) return text;
            return text.Replace("__BUNDLE__\\\\", root + "/")  // JSON "__BUNDLE__\\templates" -> root + "/templates"
                       .Replace("__BUNDLE__/", root + "/")      // YAML / forward-slash form
                       .Replace("__BUNDLE__", root);
        }

        private static string? FindBundleRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 12 && dir != null; i++, dir = dir.Parent)
                if (Directory.Exists(Path.Combine(dir.FullName, "templates")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "tools")))
                    return dir.FullName.Replace('\\', '/');
            return null;
        }

        public static string YamlToJson(string yaml)
        {
            var graph = new DeserializerBuilder().Build().Deserialize<object?>(yaml);
            return ToNode(graph)?.ToJsonString() ?? "{}";
        }

        // YamlDotNet maps mappings to Dictionary<object,object>, sequences to List<object>,
        // and all scalars to string. We rebuild a JsonNode and infer scalar types so that
        // the JSON we hand to ScaffoldProject re-parses numbers/bools correctly.
        private static JsonNode? ToNode(object? o)
        {
            switch (o)
            {
                case null:
                    return null;
                case IDictionary<object, object> map:
                {
                    var obj = new JsonObject();
                    foreach (var kv in map)
                        obj[Convert.ToString(kv.Key, CultureInfo.InvariantCulture) ?? ""] = ToNode(kv.Value);
                    return obj;
                }
                case string s:
                    return InferScalar(s);
                case IEnumerable<object> list:
                {
                    var arr = new JsonArray();
                    foreach (var item in list)
                        arr.Add(ToNode(item));
                    return arr;
                }
                default:
                    return JsonValue.Create(o);
            }
        }

        private static JsonNode? InferScalar(string s)
        {
            if (s.Length == 0) return JsonValue.Create("");
            switch (s)
            {
                case "null": case "Null": case "NULL": case "~": return null;
                case "true": case "True": case "TRUE": return JsonValue.Create(true);
                case "false": case "False": case "FALSE": return JsonValue.Create(false);
            }
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                return JsonValue.Create(l);
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return JsonValue.Create(d);
            return JsonValue.Create(s);
        }
    }
}
