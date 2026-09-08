using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>
    /// WinCC Unified HMI 动作脚本配方生成器。
    /// 这里只做离线脚本生成和 lint，不连接 TIA，不写 HMI 项目。
    /// </summary>
    public static class HmiActionScriptRecipeBuilder
    {
        public static JsonObject Build(string recipeKind, string eventName, IEnumerable<string> targetTags, string targetScreen = "", string targetPopup = "")
        {
            var kind = NormalizeRecipeKind(recipeKind);
            var tags = targetTags?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();

            var errors = new JsonArray();
            var warnings = new JsonArray();
            var script = "";
            var requiresApiDiscovery = false;
            var requiresSafetyPolicy = false;
            var applyBlockedReason = "";

            if (string.IsNullOrWhiteSpace(eventName))
                errors.Add("Event name is required.");

            switch (kind)
            {
                case "set-bit":
                    script = BuildSingleTagScript("SetBitInTag", tags, errors);
                    break;
                case "reset-bit":
                    script = BuildSingleTagScript("ResetBitInTag", tags, errors);
                    break;
                case "toggle-bit":
                    script = BuildSingleTagScript("ToggleBitInTag", tags, errors);
                    break;
                case "set-value":
                    script = BuildSetValueScript(tags, errors);
                    requiresSafetyPolicy = true;
                    applyBlockedReason = "SetValue is intentionally blocked until range validation, operator confirmation, permission checks, TIA SyntaxCheck, and ScriptCode readback are implemented for the target project.";
                    warnings.Add("SetValue is high risk: use an explicit ConfirmWrite or project-specific validated write recipe before applying.");
                    break;
                case "confirm-write":
                    script = BuildConfirmWriteScript(tags, errors);
                    requiresSafetyPolicy = true;
                    applyBlockedReason = "ConfirmWrite is intentionally blocked until range validation, operator confirmation, permission checks, TIA SyntaxCheck, and ScriptCode readback are implemented for the target project.";
                    warnings.Add("ConfirmWrite is high risk: range validation, operator confirmation, and TIA SyntaxCheck/readback are required.");
                    break;
                case "open-popup":
                    script = BuildOpenPopupScript(targetPopup, errors);
                    requiresApiDiscovery = !string.IsNullOrWhiteSpace(script);
                    applyBlockedReason = requiresApiDiscovery ? "Popup API is not verified from local TIA export/reference yet." : "";
                    if (requiresApiDiscovery)
                        warnings.Add(applyBlockedReason);
                    break;
                case "goto-screen":
                    script = BuildGotoScreenScript(targetScreen, errors);
                    requiresApiDiscovery = !string.IsNullOrWhiteSpace(script);
                    applyBlockedReason = requiresApiDiscovery ? "Screen navigation API is not verified from local TIA export/reference yet." : "";
                    if (requiresApiDiscovery)
                        warnings.Add(applyBlockedReason);
                    break;
                case "script":
                    warnings.Add("Generic script recipe cannot be generated deterministically from action metadata.");
                    break;
                case "project-binding-placeholder":
                    warnings.Add("Navigation/popup placeholder is structural only; no ScriptCode is generated or applied.");
                    break;
                default:
                    errors.Add("Unsupported recipe kind: " + recipeKind);
                    break;
            }

            var syntax = AnalyzeGeneratedScript(script);
            foreach (var warning in syntax.Warnings)
                warnings.Add(warning);
            foreach (var error in syntax.Errors)
                errors.Add(error);

            return new JsonObject
            {
                ["recipeKind"] = kind,
                ["event"] = eventName,
                ["targetTags"] = new JsonArray(tags.Select(x => JsonValue.Create(x)).ToArray()),
                ["targetScreen"] = targetScreen ?? "",
                ["targetPopup"] = targetPopup ?? "",
                ["script"] = script,
                ["safetyLevel"] = kind == "confirm-write" || kind == "set-value" ? "high" : tags.Length > 0 ? "command" : "navigation",
                ["requiresApiDiscovery"] = requiresApiDiscovery,
                ["requiresSafetyPolicy"] = requiresSafetyPolicy,
                ["applyBlockedReason"] = applyBlockedReason,
                ["applyBlocked"] = !string.IsNullOrWhiteSpace(applyBlockedReason),
                ["requiresSyntaxCheckInTia"] = !string.IsNullOrWhiteSpace(script),
                ["requiresReadback"] = kind != "project-binding-placeholder",
                ["preApplySafetyGates"] = new JsonArray(BuildPreApplySafetyGates(kind, tags).Select(x => JsonValue.Create(x)).ToArray()),
                ["ok"] = errors.Count == 0,
                ["warnings"] = warnings,
                ["errors"] = errors,
                ["discoveryRequired"] = new JsonArray(BuildDiscoverySteps(kind).Select(x => JsonValue.Create(x)).ToArray()),
                ["verificationRequired"] = new JsonArray(BuildVerificationSteps(kind, tags).Select(x => JsonValue.Create(x)).ToArray())
            };
        }

        public static JsonObject RunProbe(string templateDirectory, string reportDirectory)
        {
            Directory.CreateDirectory(reportDirectory);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var jsonPath = Path.Combine(reportDirectory, "hmi_action_script_recipe_probe_" + stamp + ".json");
            var mdPath = Path.Combine(reportDirectory, "hmi_action_script_recipe_probe_" + stamp + ".md");

            var analysis = HmiTemplateReferenceAnalyzer.Analyze(templateDirectory, "", "");
            var generated = new JsonArray();
            foreach (var template in analysis["templates"] as JsonArray ?? new JsonArray())
            {
                if (template is not JsonObject templateObj) continue;
                var templateName = templateObj["templateName"]?.ToString() ?? "";
                foreach (var recipe in templateObj["actionRecipeSummary"]?["effectiveRecipes"] as JsonArray ?? new JsonArray())
                {
                    if (recipe is not JsonObject row) continue;
                    var targetTags = (row["targetTags"] as JsonArray ?? new JsonArray()).Select(x => x?.ToString() ?? "");
                    var built = Build(
                        row["recipeKind"]?.ToString() ?? "",
                        row["event"]?.ToString() ?? "",
                        targetTags,
                        row["targetScreen"]?.ToString() ?? "",
                        row["targetPopup"]?.ToString() ?? "");
                    built["templateName"] = templateName;
                    built["item"] = row["item"]?.ToString() ?? "";
                    generated.Add(built);
                }
            }

            var root = new JsonObject
            {
                ["timestamp"] = DateTime.Now.ToString("O"),
                ["mode"] = "offline-hmi-action-script-recipe-probe",
                ["templateDirectory"] = templateDirectory,
                ["safetyPolicy"] = new JsonObject
                {
                    ["tia"] = "离线生成 HMI 动作脚本，不连接 TIA Portal，不执行 SyntaxCheck。",
                    ["write"] = "只写 reports 目录下的脚本配方探针报告，不修改 HMI 模板、参考项目或交付包。",
                    ["apply"] = "真正应用到 HMI 时仍必须调用 TIA SyntaxCheck，并读回 ScriptCode。"
                },
                ["templateCount"] = (analysis["templates"] as JsonArray)?.Count ?? 0,
                ["generatedActionCount"] = generated.Count,
                ["apiDiscoveryRequiredCount"] = generated
                    .OfType<JsonObject>()
                    .Count(x => x["requiresApiDiscovery"]?.GetValue<bool>() == true),
                ["applyBlockedCount"] = generated
                    .OfType<JsonObject>()
                    .Count(x => x["applyBlocked"]?.GetValue<bool>() == true),
                ["safeDeterministicApplyCandidateCount"] = generated
                    .OfType<JsonObject>()
                    .Count(IsSafeDeterministicApplyCandidate),
                ["safetySelfTest"] = RunSafetySelfTest(),
                ["generated"] = generated,
                ["ok"] = generated.OfType<JsonObject>().All(x => x["ok"]?.GetValue<bool>() == true || string.Equals(x["recipeKind"]?.ToString(), "script", StringComparison.OrdinalIgnoreCase))
            };
            root["ok"] = root["ok"]?.GetValue<bool>() == true && root["safetySelfTest"]?["ok"]?.GetValue<bool>() == true;

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

        private static string BuildSingleTagScript(string functionName, string[] tags, JsonArray errors)
        {
            if (tags.Length != 1)
            {
                errors.Add(functionName + " requires exactly one target tag.");
                return "";
            }

            return "HMIRuntime.Tags.SysFct." + functionName + "(\"" + EscapeJs(tags[0]) + "\", 0);";
        }

        private static string BuildConfirmWriteScript(string[] tags, JsonArray errors)
        {
            if (tags.Length == 0)
            {
                errors.Add("ConfirmWrite requires at least one target tag.");
                return "";
            }

            var sb = new StringBuilder();
            sb.AppendLine("// 高风险写入：实际应用前必须增加范围校验和操作员确认。");
            foreach (var tag in tags)
            {
                sb.AppendLine("// TODO: validate and write " + tag);
            }
            return sb.ToString().TrimEnd();
        }

        private static string BuildSetValueScript(string[] tags, JsonArray errors)
        {
            if (tags.Length != 1)
            {
                errors.Add("SetValue requires exactly one target tag.");
                return "";
            }

            var sb = new StringBuilder();
            sb.AppendLine("// 高风险写入：实际应用前必须增加范围校验、权限校验和操作员确认。");
            sb.AppendLine("// TODO: validate value and write " + tags[0]);
            return sb.ToString().TrimEnd();
        }

        private static string BuildOpenPopupScript(string targetPopup, JsonArray errors)
        {
            if (string.IsNullOrWhiteSpace(targetPopup))
            {
                errors.Add("OpenPopup requires TargetPopup.");
                return "";
            }

            return "// 打开弹窗：" + targetPopup + Environment.NewLine +
                   "// TODO: bind to the verified WinCC Unified popup-open API for this project version.";
        }

        private static string BuildGotoScreenScript(string targetScreen, JsonArray errors)
        {
            if (string.IsNullOrWhiteSpace(targetScreen))
            {
                errors.Add("GotoScreen requires TargetScreen.");
                return "";
            }

            return "// 切换画面：" + targetScreen + Environment.NewLine +
                   "// TODO: bind to the verified WinCC Unified screen-navigation API for this project version.";
        }

        private static (string[] Warnings, string[] Errors) AnalyzeGeneratedScript(string script)
        {
            var warnings = new List<string>();
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(script))
                return (warnings.ToArray(), errors.ToArray());

            if (Regex.IsMatch(script, @"Force", RegexOptions.IgnoreCase))
                errors.Add("Generated script must not contain force-related text.");
            if (Regex.IsMatch(script, @"WatchTable|ForceTable", RegexOptions.IgnoreCase))
                errors.Add("Generated script must not reference watch/force table operations.");
            if (!Balanced(script, '(', ')'))
                errors.Add("Generated script has unbalanced parentheses.");
            if (!Balanced(script, '"', '"'))
                errors.Add("Generated script has unbalanced double quotes.");
            if (script.Contains("TODO", StringComparison.OrdinalIgnoreCase))
                warnings.Add("Generated script contains TODO placeholder and must not be applied without project-specific implementation.");

            return (warnings.ToArray(), errors.ToArray());
        }

        private static IEnumerable<string> BuildVerificationSteps(string kind, string[] tags)
        {
            yield return "Verify HMI item and event exist before applying ScriptCode.";
            if (tags.Length > 0)
            {
                yield return "Verify referenced HMI tag exists.";
                yield return "Verify mapped PLC tag or DB member exists.";
            }
            yield return "Apply ScriptCode only through SetUnifiedHmiButtonEventScriptCode.";
            yield return "Run TIA SyntaxCheck and read back ScriptCode.";
            if (kind == "confirm-write")
                yield return "Require range validation and operator confirmation before write.";
            if (kind == "set-value")
                yield return "Require explicit value source, range validation, operator confirmation, and readback before write.";
        }

        private static IEnumerable<string> BuildPreApplySafetyGates(string kind, string[] tags)
        {
            yield return "Verify HMI item and event path by readback.";
            foreach (var tag in tags)
            {
                yield return "Verify HMI tag exists: " + tag;
                yield return "Verify mapped PLC tag or DB member exists: " + tag;
            }

            if (kind == "confirm-write" || kind == "set-value")
            {
                yield return "Define min/max/type validation for every target tag.";
                yield return "Require explicit operator confirmation UI before write.";
                yield return "Require permission/role check before write.";
                yield return "Read current value before write.";
                yield return "Write only through the verified WinCC Unified V21 API.";
                yield return "Run TIA SyntaxCheck and read back ScriptCode.";
                yield return "Read back the final tag value after write in a temporary project first.";
            }
        }

        private static IEnumerable<string> BuildDiscoverySteps(string kind)
        {
            if (!string.Equals(kind, "open-popup", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(kind, "goto-screen", StringComparison.OrdinalIgnoreCase))
                yield break;

            yield return "Use a temporary TIA project or the local reference project to create/inspect one verified event.";
            yield return "Read back the exact ScriptCode and API shape generated by WinCC Unified V21.";
            yield return "Apply the discovered script through SetUnifiedHmiButtonEventScriptCode in a disposable screen.";
            yield return "Run SyntaxCheck and read back ScriptCode before marking this recipe deterministic.";
        }

        private static string NormalizeRecipeKind(string recipeKind)
        {
            return (recipeKind ?? "").Trim().ToLowerInvariant();
        }

        private static string EscapeJs(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static bool Balanced(string text, char open, char close)
        {
            if (open == close)
                return text.Count(x => x == open) % 2 == 0;

            var depth = 0;
            foreach (var ch in text)
            {
                if (ch == open) depth++;
                if (ch == close) depth--;
                if (depth < 0) return false;
            }
            return depth == 0;
        }

        private static bool IsSafeDeterministicApplyCandidate(JsonObject recipe)
        {
            var kind = recipe["recipeKind"]?.ToString() ?? "";
            var allowed = new[] { "set-bit", "reset-bit", "toggle-bit" };
            return allowed.Contains(kind, StringComparer.OrdinalIgnoreCase)
                   && recipe["ok"]?.GetValue<bool>() == true
                   && recipe["applyBlocked"]?.GetValue<bool>() != true
                   && recipe["requiresApiDiscovery"]?.GetValue<bool>() != true
                   && recipe["requiresSafetyPolicy"]?.GetValue<bool>() != true
                   && !string.IsNullOrWhiteSpace(recipe["script"]?.ToString() ?? "");
        }

        public static JsonObject RunSafetySelfTest()
        {
            var cases = new JsonArray();
            void AddCase(string id, JsonObject recipe, bool expectedOk, bool expectedApplyBlocked, bool expectedSafeApply)
            {
                var actualOk = recipe["ok"]?.GetValue<bool>() == true;
                var actualBlocked = recipe["applyBlocked"]?.GetValue<bool>() == true;
                var actualSafe = IsSafeDeterministicApplyCandidate(recipe);
                cases.Add(new JsonObject
                {
                    ["id"] = id,
                    ["recipeKind"] = recipe["recipeKind"]?.ToString() ?? "",
                    ["expectedOk"] = expectedOk,
                    ["actualOk"] = actualOk,
                    ["expectedApplyBlocked"] = expectedApplyBlocked,
                    ["actualApplyBlocked"] = actualBlocked,
                    ["expectedSafeApplyCandidate"] = expectedSafeApply,
                    ["actualSafeApplyCandidate"] = actualSafe,
                    ["pass"] = actualOk == expectedOk && actualBlocked == expectedApplyBlocked && actualSafe == expectedSafeApply,
                    ["applyBlockedReason"] = recipe["applyBlockedReason"]?.ToString() ?? "",
                    ["errors"] = recipe["errors"]?.DeepClone() ?? new JsonArray(),
                    ["warnings"] = recipe["warnings"]?.DeepClone() ?? new JsonArray()
                });
            }

            AddCase("set-bit-safe", Build("set-bit", "Tapped", new[] { "Cmd_Start" }), true, false, true);
            AddCase("reset-bit-safe", Build("reset-bit", "Released", new[] { "Cmd_Start" }), true, false, true);
            AddCase("toggle-bit-safe", Build("toggle-bit", "Tapped", new[] { "Cmd_Auto" }), true, false, true);
            AddCase("set-bit-missing-tag", Build("set-bit", "Tapped", Array.Empty<string>()), false, false, false);
            AddCase("confirm-write-blocked", Build("confirm-write", "Tapped", new[] { "Set_Speed" }), true, true, false);
            AddCase("set-value-blocked", Build("set-value", "Tapped", new[] { "Set_Speed" }), true, true, false);
            AddCase("goto-screen-api-discovery-blocked", Build("goto-screen", "Tapped", Array.Empty<string>(), "Alarm_Overview"), true, true, false);
            AddCase("open-popup-api-discovery-blocked", Build("open-popup", "Tapped", Array.Empty<string>(), "", "Popup_Parameter"), true, true, false);

            return new JsonObject
            {
                ["format"] = "hmi-action-script-recipe-safety-self-test-v1",
                ["timestamp"] = DateTime.Now.ToString("O"),
                ["ok"] = cases.OfType<JsonObject>().All(x => x["pass"]?.GetValue<bool>() == true),
                ["caseCount"] = cases.Count,
                ["cases"] = cases
            };
        }

        private static string BuildMarkdown(JsonObject root, string jsonPath)
        {
            var md = new StringBuilder();
            md.AppendLine("# HMI Action Script Recipe Probe");
            md.AppendLine();
            md.AppendLine("Generated: " + root["timestamp"]);
            md.AppendLine("JSON: " + jsonPath);
            md.AppendLine();
            md.AppendLine("## Safety");
            md.AppendLine("- Offline script generation only; no TIA connection and no HMI project write.");
            md.AppendLine("- Generated scripts still require TIA SyntaxCheck and ScriptCode readback before use.");
            md.AppendLine("- Delivery package is not modified.");
            md.AppendLine();
            md.AppendLine("## Summary");
            md.AppendLine("- OK: " + root["ok"]);
            md.AppendLine("- Template count: " + root["templateCount"]);
            md.AppendLine("- Generated actions: " + root["generatedActionCount"]);
            md.AppendLine("- API discovery blocked actions: " + root["apiDiscoveryRequiredCount"]);
            md.AppendLine("- Apply blocked actions: " + root["applyBlockedCount"]);
            md.AppendLine("- Safe deterministic apply candidates: " + root["safeDeterministicApplyCandidateCount"]);
            md.AppendLine("- Safety self-test: " + root["safetySelfTest"]?["ok"]);
            md.AppendLine();
            md.AppendLine("## Generated Recipes");
            foreach (var node in root["generated"] as JsonArray ?? new JsonArray())
            {
                if (node is not JsonObject item) continue;
                md.AppendLine("- " + item["templateName"] + "." + item["item"] + "." + item["event"] + ": " + item["recipeKind"] + ", ok=" + item["ok"] + ", safety=" + item["safetyLevel"] + ", requiresApiDiscovery=" + item["requiresApiDiscovery"]);
            }

            return md.ToString();
        }
    }
}
