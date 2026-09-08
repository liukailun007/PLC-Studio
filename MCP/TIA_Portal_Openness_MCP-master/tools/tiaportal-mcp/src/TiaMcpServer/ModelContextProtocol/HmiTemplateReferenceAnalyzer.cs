using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TiaMcpServer.ModelContextProtocol
{
    public static class HmiTemplateReferenceAnalyzer
    {
        public static JsonObject Analyze(string templateDirectory, string referenceProjectPath, string referenceGlobalLibraryPath)
        {
            var root = new JsonObject
            {
                ["timestamp"] = DateTime.Now.ToString("O"),
                ["templateDirectory"] = templateDirectory,
                ["referenceProjectPath"] = referenceProjectPath,
                ["referenceGlobalLibraryPath"] = referenceGlobalLibraryPath,
                ["safetyPolicy"] = new JsonObject
                {
                    ["mode"] = "Offline template/reference analysis only.",
                    ["tia"] = "TIA Portal is not connected or opened by this analyzer.",
                    ["write"] = "No project, global library, HMI screen, watch table, or delivery package content is modified.",
                    ["binding"] = "A HMI tag binding is only considered ready when the PLC-side tag/member can be verified later in TIA."
                }
            };

            var templates = AnalyzeTemplates(templateDirectory);
            var referenceHints = AnalyzeReferenceHints(referenceProjectPath, referenceGlobalLibraryPath);
            root["templates"] = templates;
            root["referenceHints"] = referenceHints;
            root["readiness"] = BuildReadiness(templates, referenceHints);
            root["recommendations"] = BuildRecommendations(templates, referenceHints);
            root["ok"] = Directory.Exists(templateDirectory) && templates.Count > 0;
            return root;
        }

        public static string BuildMarkdown(JsonObject root, string jsonPath)
        {
            var md = new StringBuilder();
            md.AppendLine("# HMI Template Reference Analysis");
            md.AppendLine();
            md.AppendLine("Generated: " + root["timestamp"]);
            md.AppendLine("JSON: " + jsonPath);
            md.AppendLine();
            md.AppendLine("## Safety");
            md.AppendLine("- Offline analysis only; no TIA connection, no project write, no global-library import.");
            md.AppendLine("- HMI binding remains unverified until the PLC-side tag or DB member is read back from TIA.");
            md.AppendLine("- Delivery package is not modified by this report.");
            md.AppendLine();

            md.AppendLine("## Template Summary");
            if (root["templates"] is JsonArray templates)
            {
                foreach (var node in templates)
                {
                    var t = node as JsonObject;
                    md.AppendLine("- " + t?["templateName"] + ": tags=" + t?["requiredTagCount"] + ", items=" + t?["itemCount"] + ", dynamizations=" + t?["dynamizationCount"] + ", actions=" + t?["actionCount"]);
                }
            }
            md.AppendLine();

            md.AppendLine("## Binding And Event Readiness");
            if (root["readiness"] is JsonArray readiness)
            {
                foreach (var node in readiness)
                {
                    var item = node as JsonObject;
                    md.AppendLine("- " + item?["templateName"] + ": " + item?["status"] + " - " + item?["detail"]);
                }
            }
            md.AppendLine();

            md.AppendLine("## HMI Action Recipes");
            if (root["templates"] is JsonArray actionTemplates)
            {
                foreach (var node in actionTemplates)
                {
                    if (node is not JsonObject t) continue;
                    var summary = t["actionRecipeSummary"] as JsonObject;
                    md.AppendLine("- " + t["templateName"] + ": actions=" + summary?["actionCount"] + ", effective=" + summary?["effectiveActionCount"] + ", ready=" + summary?["readyForGeneration"] + ", needsConfirm=" + summary?["requiresOperatorConfirm"] + ", highRisk=" + summary?["highRiskWrites"] + ", duplicates=" + ((summary?["duplicateActions"] as JsonArray)?.Count ?? 0) + ", missingTargets=" + ((summary?["missingTargets"] as JsonArray)?.Count ?? 0));
                    if (summary?["effectiveRecipes"] is JsonArray recipes)
                    {
                        foreach (var recipeNode in recipes.Take(8))
                        {
                            if (recipeNode is not JsonObject recipe) continue;
                            md.AppendLine("  - " + recipe["item"] + "." + recipe["event"] + ": " + recipe["recipeKind"] + ", safety=" + recipe["safetyLevel"] + ", status=" + recipe["status"]);
                        }
                    }
                }
            }
            md.AppendLine();

            md.AppendLine("## Reference Signals");
            var hints = root["referenceHints"] as JsonObject;
            md.AppendLine("- HMI runtime exists: " + hints?["hmiRuntimeExists"]);
            md.AppendLine("- Screen RDF files: " + hints?["screenRdfCount"]);
            md.AppendLine("- Faceplate RDF files: " + hints?["faceplateRdfCount"]);
            md.AppendLine("- Global library exists: " + hints?["globalLibraryExists"]);
            md.AppendLine("- Global library `.al*` files: " + hints?["globalLibraryFileCount"]);
            md.AppendLine();

            md.AppendLine("## Recommendations");
            if (root["recommendations"] is JsonArray recs)
            {
                foreach (var rec in recs)
                {
                    md.AppendLine("- " + rec);
                }
            }

            return md.ToString();
        }

        private static JsonArray AnalyzeTemplates(string templateDirectory)
        {
            var result = new JsonArray();
            if (!Directory.Exists(templateDirectory)) return result;

            foreach (var file in Directory.EnumerateFiles(templateDirectory, "*.json", SearchOption.TopDirectoryOnly).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var json = JsonNode.Parse(File.ReadAllText(file, Encoding.UTF8)) as JsonObject;
                    if (json == null) continue;

                    var requiredTags = json["RequiredTags"] as JsonArray ?? new JsonArray();
                    var items = json["Items"] as JsonArray ?? new JsonArray();
                    var dyn = new JsonArray();
                    var actions = new JsonArray();

                    foreach (var itemNode in items)
                    {
                        if (itemNode is not JsonObject item) continue;
                        CollectDynamizations(item, item["Name"]?.ToString() ?? "", dyn);
                        if (item["Actions"] is JsonArray actionArray)
                        {
                            foreach (var actionNode in actionArray)
                            {
                                if (actionNode is JsonObject action)
                                {
                                    var itemName = item["Name"]?.ToString() ?? "";
                                    var eventName = action["Event"]?.ToString() ?? "";
                                    var script = action["Script"]?.ToString() ?? "";
                                    var actionJson = new JsonObject
                                    {
                                        ["item"] = itemName,
                                        ["event"] = eventName,
                                        ["script"] = script,
                                        ["referencedTags"] = new JsonArray(ExtractRuntimeTags(script).Select(x => JsonValue.Create(x)).ToArray())
                                    };
                                    actionJson["recipe"] = BuildHmiActionRecipe(actionJson, action);
                                    actions.Add(actionJson);
                                }
                            }
                        }
                    }

                    if (json["Events"] is JsonArray eventArray)
                    {
                        foreach (var eventNode in eventArray)
                        {
                            if (eventNode is not JsonObject action) continue;
                            var actionJson = new JsonObject
                            {
                                ["item"] = action["Item"]?.ToString() ?? "",
                                ["event"] = action["Event"]?.ToString() ?? "",
                                ["actionKind"] = action["ActionKind"]?.ToString() ?? "",
                                ["targetTag"] = action["TargetTag"]?.ToString() ?? "",
                                ["targetScreen"] = action["TargetScreen"]?.ToString() ?? "",
                                ["targetPopup"] = action["TargetPopup"]?.ToString() ?? "",
                                ["script"] = action["Script"]?.ToString() ?? "",
                                ["referencedTags"] = new JsonArray(ExtractActionTags(action).Select(x => JsonValue.Create(x)).ToArray())
                            };
                            actionJson["recipe"] = BuildHmiActionRecipe(actionJson, action);
                            actions.Add(actionJson);
                        }
                    }

                    var tagNames = requiredTags
                        .OfType<JsonObject>()
                        .Select(x => x["Name"]?.ToString() ?? "")
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var templateObjectNames = BuildTemplateObjectNameSet(json, items);
                    var actionTags = actions
                        .OfType<JsonObject>()
                        .SelectMany(x => (x["referencedTags"] as JsonArray ?? new JsonArray()).Select(y => y?.ToString() ?? ""))
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var missingActionTags = actionTags.Where(x => !tagNames.Contains(x)).ToArray();
                    var actionRecipeSummary = BuildActionRecipeSummary(actions, tagNames, templateObjectNames);

                    result.Add(new JsonObject
                    {
                        ["file"] = file,
                        ["templateName"] = json["TemplateName"]?.ToString() ?? Path.GetFileNameWithoutExtension(file),
                        ["format"] = json["Format"]?.ToString() ?? "",
                        ["purpose"] = json["Purpose"]?.ToString() ?? "",
                        ["screenName"] = json["Screen"]?["Name"]?.ToString() ?? "",
                        ["requiredTagCount"] = requiredTags.Count,
                        ["itemCount"] = items.Count,
                        ["dynamizationCount"] = dyn.Count,
                        ["actionCount"] = actions.Count,
                        ["requiredTags"] = CloneArray(requiredTags),
                        ["dynamizations"] = dyn,
                        ["actions"] = actions,
                        ["actionRecipeSummary"] = actionRecipeSummary,
                        ["missingRequiredTagsForActions"] = new JsonArray(missingActionTags.Select(x => JsonValue.Create(x)).ToArray()),
                        ["bindingPolicy"] = "RequiredTags.Name must exist as HMI tag, RequiredTags.PlcTag must exist as PLC tag or DB member before template application is treated as verified."
                    });
                }
                catch (Exception ex)
                {
                    result.Add(new JsonObject
                    {
                        ["file"] = file,
                        ["error"] = ex.Message,
                        ["ok"] = false
                    });
                }
            }

            return result;
        }

        private static JsonObject AnalyzeReferenceHints(string referenceProjectPath, string referenceGlobalLibraryPath)
        {
            var runtimeRoot = Directory.Exists(referenceProjectPath)
                ? Directory.EnumerateDirectories(referenceProjectPath, "currentConfiguration", SearchOption.AllDirectories).FirstOrDefault()
                : null;
            var screenDir = runtimeRoot == null ? "" : Path.Combine(runtimeRoot, "screens");
            var faceplateDir = runtimeRoot == null ? "" : Path.Combine(runtimeRoot, "faceplates");
            var libraryRoot = string.IsNullOrWhiteSpace(referenceGlobalLibraryPath)
                ? ""
                : Directory.Exists(referenceGlobalLibraryPath)
                ? referenceGlobalLibraryPath
                : Path.GetDirectoryName(referenceGlobalLibraryPath) ?? referenceGlobalLibraryPath;

            var sampleStrings = new JsonArray();
            if (!string.IsNullOrWhiteSpace(runtimeRoot) && Directory.Exists(runtimeRoot))
            {
                foreach (var file in Directory.EnumerateFiles(runtimeRoot, "*.rdf", SearchOption.AllDirectories).Take(80))
                {
                    try
                    {
                        var text = Encoding.UTF8.GetString(File.ReadAllBytes(file));
                        foreach (var pattern in new[] { "Faceplate", "Screen", "Button", "IO", "Tag", "Alarm", "Trend", "Recipe" })
                        {
                            var count = CountOccurrences(text, pattern);
                            if (count > 0 && sampleStrings.Count < 80)
                            {
                                sampleStrings.Add(new JsonObject
                                {
                                    ["file"] = file,
                                    ["pattern"] = pattern,
                                    ["count"] = count
                                });
                            }
                        }
                    }
                    catch
                    {
                        // RDF内容可能是二进制或局部编码，离线学习只记录可读线索。
                    }
                }
            }

            return new JsonObject
            {
                ["hmiRuntimeExists"] = runtimeRoot != null,
                ["runtimeRoot"] = runtimeRoot ?? "",
                ["screenRdfCount"] = Directory.Exists(screenDir) ? Directory.EnumerateFiles(screenDir, "*.rdf").Count() : 0,
                ["faceplateRdfCount"] = Directory.Exists(faceplateDir) ? Directory.EnumerateFiles(faceplateDir, "*.rdf").Count() : 0,
                ["globalLibraryExists"] = !string.IsNullOrWhiteSpace(libraryRoot) && (Directory.Exists(libraryRoot) || File.Exists(referenceGlobalLibraryPath)),
                ["globalLibraryFileCount"] = Directory.Exists(libraryRoot) ? Directory.EnumerateFiles(libraryRoot, "*.al*", SearchOption.TopDirectoryOnly).Count() : 0,
                ["sampleRuntimeStringHints"] = sampleStrings
            };
        }

        private static JsonArray BuildReadiness(JsonArray templates, JsonObject referenceHints)
        {
            var list = new JsonArray();
            foreach (var node in templates)
            {
                if (node is not JsonObject t) continue;
                var missingActionTags = t["missingRequiredTagsForActions"] as JsonArray ?? new JsonArray();
                var requiredTagCount = t["requiredTagCount"]?.GetValue<int>() ?? 0;
                var actionCount = t["actionCount"]?.GetValue<int>() ?? 0;
                var dynCount = t["dynamizationCount"]?.GetValue<int>() ?? 0;
                var status = missingActionTags.Count == 0 && requiredTagCount > 0 ? "ready-for-tia-validation" : "needs-template-fix";
                var detail = missingActionTags.Count == 0
                    ? $"Template has {requiredTagCount} required tags, {dynCount} dynamic bindings, and {actionCount} event actions. Next validation must verify PLC-side tags in TIA before applying."
                    : "Action scripts reference tags not listed in RequiredTags: " + string.Join(", ", missingActionTags.Select(x => x?.ToString()));
                list.Add(new JsonObject
                {
                    ["templateName"] = t["templateName"]?.ToString() ?? "",
                    ["status"] = status,
                    ["detail"] = detail
                });
            }

            if (referenceHints["hmiRuntimeExists"]?.GetValue<bool>() != true)
            {
                list.Add(new JsonObject
                {
                    ["templateName"] = "reference-project",
                    ["status"] = "blocked",
                    ["detail"] = "Reference HMI runtime currentConfiguration was not found."
                });
            }

            return list;
        }

        private static JsonArray BuildRecommendations(JsonArray templates, JsonObject referenceHints)
        {
            var list = new JsonArray
            {
                "HMI模板应用前必须先从TIA读回PLC变量表/DB成员，逐项匹配RequiredTags.PlcTag；未匹配则拒绝绑定。",
                "按钮、输入框、状态文本应分开验证：按钮检查事件脚本引用的HMI变量，IOField检查ProcessValue绑定，状态文本检查Visible/颜色等动态化。",
                "参考项目runtime RDF只用于学习画面规模、faceplate数量和控件关键词，不直接复制为模板，避免不可维护和版本耦合。",
                "全局库对象级复用下一步走ProbeGlobalLibrary只读读回，再在临时项目验证导入MasterCopy/Type，不直接对真实项目导入。"
            };

            if ((referenceHints["screenRdfCount"]?.GetValue<int>() ?? 0) > 0)
            {
                list.Add("参考项目包含大量screen RDF，建议把常见页面拆成总览、设备单元、PID面板、报警/趋势、维护诊断五类模板。");
            }

            if ((referenceHints["faceplateRdfCount"]?.GetValue<int>() ?? 0) > 0)
            {
                list.Add("参考项目包含faceplate RDF，HMI模板体系应优先补充faceplate级别的参数接口和实例化规则。");
            }

            if (templates.Count > 0)
            {
                list.Add("当前JSON模板已有RequiredTags/Dynamizations/Actions骨架，可以作为商业版模板契约；下一步应增加版本号、控件能力矩阵、PLC同步预检报告。");
            }

            return list;
        }

        private static JsonObject BuildHmiActionRecipe(JsonObject normalizedAction, JsonObject sourceAction)
        {
            var actionKind = sourceAction["ActionKind"]?.ToString() ?? normalizedAction["actionKind"]?.ToString() ?? "";
            var eventName = normalizedAction["event"]?.ToString() ?? "";
            var script = normalizedAction["script"]?.ToString() ?? "";
            var referencedTags = (normalizedAction["referencedTags"] as JsonArray ?? new JsonArray())
                .Select(x => x?.ToString() ?? "")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (string.IsNullOrWhiteSpace(actionKind))
            {
                actionKind = InferActionKindFromScript(script);
            }

            var recipeKind = NormalizeActionKind(actionKind);
            var targetScreen = sourceAction["TargetScreen"]?.ToString() ?? "";
            var targetPopup = sourceAction["TargetPopup"]?.ToString() ?? "";
            var explicitNavigationScript = !string.IsNullOrWhiteSpace(script);
            if ((recipeKind == "open-popup" || recipeKind == "goto-screen") && !explicitNavigationScript)
            {
                recipeKind = "project-binding-placeholder";
            }
            var writesPlcOrHmiTag = recipeKind is "set-bit" or "reset-bit" or "toggle-bit" or "set-value" or "confirm-write";
            var requiresConfirm = recipeKind == "confirm-write" || sourceAction["RequiresConfirm"]?.GetValue<bool?>() == true;
            var highRisk = recipeKind == "confirm-write" || recipeKind == "set-value";
            var status = "ready-for-generation";
            var warnings = new JsonArray();

            if (string.IsNullOrWhiteSpace(eventName))
            {
                status = "needs-template-fix";
                warnings.Add("Event is empty.");
            }

            if (writesPlcOrHmiTag && referencedTags.Length == 0)
            {
                status = "needs-template-fix";
                warnings.Add("Write action has no TargetTag/TargetTags/script tag reference.");
            }

            if (highRisk && !requiresConfirm)
            {
                status = "needs-confirmation-policy";
                warnings.Add("High-risk value write must require operator confirmation and range validation.");
            }

            return new JsonObject
            {
                ["recipeKind"] = recipeKind,
                ["actionKind"] = actionKind,
                ["event"] = eventName,
                ["targetTags"] = new JsonArray(referencedTags.Select(x => JsonValue.Create(x)).ToArray()),
                ["targetScreen"] = targetScreen,
                ["targetPopup"] = targetPopup,
                ["writesTag"] = writesPlcOrHmiTag,
                ["requiresOperatorConfirm"] = requiresConfirm,
                ["safetyLevel"] = recipeKind == "project-binding-placeholder" ? "structure-placeholder" : highRisk ? "high" : writesPlcOrHmiTag ? "command" : "navigation",
                ["status"] = status,
                ["applyAsScript"] = recipeKind != "project-binding-placeholder",
                ["verificationRequired"] = new JsonArray(BuildActionVerificationSteps(recipeKind, referencedTags).Select(x => JsonValue.Create(x)).ToArray()),
                ["warnings"] = warnings
            };
        }

        private static JsonObject BuildActionRecipeSummary(JsonArray actions, HashSet<string> requiredTagNames, HashSet<string> templateObjectNames)
        {
            var recipes = new JsonArray();
            var ready = 0;
            var confirm = 0;
            var highRisk = 0;
            var missingRequiredTags = new JsonArray();
            var duplicateActions = new JsonArray();
            var missingTargets = new JsonArray();
            var seenActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var effectiveRecipes = new JsonArray();

            foreach (var actionNode in actions.OfType<JsonObject>())
            {
                var recipe = actionNode["recipe"] as JsonObject ?? new JsonObject();
                var row = recipe.DeepClone().AsObject();
                row["item"] = actionNode["item"]?.ToString() ?? "";
                row["event"] = actionNode["event"]?.ToString() ?? "";
                var actionKey = string.Join("|",
                    row["item"]?.ToString() ?? "",
                    row["event"]?.ToString() ?? "",
                    row["recipeKind"]?.ToString() ?? "",
                    string.Join(",", (row["targetTags"] as JsonArray ?? new JsonArray()).Select(x => x?.ToString() ?? "")),
                    row["targetScreen"]?.ToString() ?? "",
                    row["targetPopup"]?.ToString() ?? "");
                if (!seenActions.Add(actionKey))
                {
                    duplicateActions.Add(new JsonObject
                    {
                        ["item"] = row["item"]?.ToString() ?? "",
                        ["event"] = row["event"]?.ToString() ?? "",
                        ["recipeKind"] = row["recipeKind"]?.ToString() ?? ""
                    });
                }
                else
                {
                    effectiveRecipes.Add(row.DeepClone());
                }

                if (string.Equals(recipe["status"]?.ToString(), "ready-for-generation", StringComparison.OrdinalIgnoreCase))
                    ready++;
                if (recipe["requiresOperatorConfirm"]?.GetValue<bool>() == true)
                    confirm++;
                if (string.Equals(recipe["safetyLevel"]?.ToString(), "high", StringComparison.OrdinalIgnoreCase))
                    highRisk++;

                foreach (var tagNode in recipe["targetTags"] as JsonArray ?? new JsonArray())
                {
                    var tag = tagNode?.ToString() ?? "";
                    if (!string.IsNullOrWhiteSpace(tag) && !requiredTagNames.Contains(tag))
                    {
                        missingRequiredTags.Add(new JsonObject
                        {
                            ["item"] = row["item"]?.ToString() ?? "",
                            ["event"] = row["event"]?.ToString() ?? "",
                            ["tag"] = tag
                        });
                    }
                }

                var targetPopup = row["targetPopup"]?.ToString() ?? "";
                var targetScreen = row["targetScreen"]?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(targetPopup) && !templateObjectNames.Contains(targetPopup))
                {
                    missingTargets.Add(new JsonObject
                    {
                        ["item"] = row["item"]?.ToString() ?? "",
                        ["event"] = row["event"]?.ToString() ?? "",
                        ["targetType"] = "popup",
                        ["target"] = targetPopup
                    });
                }
                if (!string.IsNullOrWhiteSpace(targetScreen) && !templateObjectNames.Contains(targetScreen))
                {
                    missingTargets.Add(new JsonObject
                    {
                        ["item"] = row["item"]?.ToString() ?? "",
                        ["event"] = row["event"]?.ToString() ?? "",
                        ["targetType"] = "screen",
                        ["target"] = targetScreen
                    });
                }

                recipes.Add(row);
            }

            return new JsonObject
            {
                ["actionCount"] = actions.Count,
                ["readyForGeneration"] = ready,
                ["effectiveActionCount"] = effectiveRecipes.Count,
                ["requiresOperatorConfirm"] = confirm,
                ["highRiskWrites"] = highRisk,
                ["missingRequiredTags"] = missingRequiredTags,
                ["duplicateActions"] = duplicateActions,
                ["missingTargets"] = missingTargets,
                ["effectiveRecipes"] = effectiveRecipes,
                ["recipes"] = recipes
            };
        }

        private static HashSet<string> BuildTemplateObjectNameSet(JsonObject template, JsonArray items)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string value)
            {
                if (!string.IsNullOrWhiteSpace(value)) names.Add(value);
            }

            Add(template["Screen"]?["Name"]?.ToString() ?? "");
            foreach (var item in items.OfType<JsonObject>())
            {
                Add(item["Name"]?.ToString() ?? "");
            }

            foreach (var component in template["Components"] as JsonArray ?? new JsonArray())
            {
                if (component is JsonObject obj)
                {
                    Add(obj["Name"]?.ToString() ?? "");
                }
            }

            return names;
        }

        private static string InferActionKindFromScript(string script)
        {
            if (string.IsNullOrWhiteSpace(script)) return "";
            if (Regex.IsMatch(script, @"SetBitInTag", RegexOptions.IgnoreCase)) return "SetBitInTag";
            if (Regex.IsMatch(script, @"ResetBitInTag", RegexOptions.IgnoreCase)) return "ResetBitInTag";
            if (Regex.IsMatch(script, @"ToggleBitInTag", RegexOptions.IgnoreCase)) return "ToggleBitInTag";
            if (Regex.IsMatch(script, @"SetTagValue|Write", RegexOptions.IgnoreCase)) return "SetValue";
            if (Regex.IsMatch(script, @"OpenPopup", RegexOptions.IgnoreCase)) return "OpenPopup";
            if (Regex.IsMatch(script, @"ChangeScreen|SetScreen", RegexOptions.IgnoreCase)) return "GotoScreen";
            return "Script";
        }

        private static string NormalizeActionKind(string actionKind)
        {
            if (string.IsNullOrWhiteSpace(actionKind)) return "unknown";
            var value = actionKind.Trim();
            if (value.Equals("SetBitInTag", StringComparison.OrdinalIgnoreCase)) return "set-bit";
            if (value.Equals("ResetBitInTag", StringComparison.OrdinalIgnoreCase)) return "reset-bit";
            if (value.Equals("ToggleBitInTag", StringComparison.OrdinalIgnoreCase)) return "toggle-bit";
            if (value.Equals("SetValue", StringComparison.OrdinalIgnoreCase) || value.Equals("SetTagValue", StringComparison.OrdinalIgnoreCase)) return "set-value";
            if (value.Equals("ConfirmWrite", StringComparison.OrdinalIgnoreCase)) return "confirm-write";
            if (value.Equals("OpenPopup", StringComparison.OrdinalIgnoreCase)) return "open-popup";
            if (value.Equals("GotoScreen", StringComparison.OrdinalIgnoreCase) || value.Equals("ChangeScreen", StringComparison.OrdinalIgnoreCase) || value.Equals("NavigateScreen", StringComparison.OrdinalIgnoreCase)) return "goto-screen";
            return "script";
        }

        private static IEnumerable<string> BuildActionVerificationSteps(string recipeKind, string[] targetTags)
        {
            yield return "Read back HMI item and event name after template application.";
            if (targetTags.Length > 0)
            {
                yield return "Verify every referenced HMI tag exists before binding event script.";
                yield return "Verify every mapped PLC tag or DB member exists before template application.";
            }

            if (recipeKind is "set-bit" or "reset-bit" or "toggle-bit")
            {
                yield return "Run script syntax check and read back ScriptCode.";
            }
            else if (recipeKind is "set-value" or "confirm-write")
            {
                yield return "Require range validation, operator confirmation, script syntax check, and readback.";
            }
            else if (recipeKind is "open-popup" or "goto-screen")
            {
                yield return "Verify target popup/screen exists or is created in the same template transaction.";
            }
            else if (recipeKind == "project-binding-placeholder")
            {
                yield return "Treat this as a structural placeholder; do not generate ScriptCode until the target project provides a verified navigation or popup API.";
            }
        }

        private static void CollectDynamizations(JsonObject node, string itemName, JsonArray output)
        {
            foreach (var kv in node)
            {
                if (kv.Value is JsonObject child)
                {
                    if (kv.Key.Equals("Dynamizations", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var dyn in child)
                        {
                            output.Add(new JsonObject
                            {
                                ["item"] = itemName,
                                ["property"] = dyn.Key,
                                ["tag"] = dyn.Value?["Tag"]?.ToString() ?? "",
                                ["readOnly"] = dyn.Value?["ReadOnly"]?.ToString() ?? ""
                            });
                        }
                    }
                    else
                    {
                        CollectDynamizations(child, itemName, output);
                    }
                }
                else if (kv.Value is JsonArray array)
                {
                    foreach (var item in array.OfType<JsonObject>())
                    {
                        CollectDynamizations(item, itemName, output);
                    }
                }
            }
        }

        private static IEnumerable<string> ExtractRuntimeTags(string script)
        {
            var matches = Regex.Matches(script, @"Tags\.SysFct\.\w+\(\s*""([^""]+)""", RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    yield return match.Groups[1].Value;
                }
            }
        }

        private static IEnumerable<string> ExtractActionTags(JsonObject action)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in ExtractRuntimeTags(action["Script"]?.ToString() ?? ""))
            {
                if (seen.Add(tag)) yield return tag;
            }

            var targetTag = action["TargetTag"]?.ToString() ?? "";
            if (!string.IsNullOrWhiteSpace(targetTag) && seen.Add(targetTag))
            {
                yield return targetTag;
            }

            if (action["TargetTags"] is JsonArray targetTags)
            {
                foreach (var node in targetTags)
                {
                    var tag = node?.ToString() ?? "";
                    if (!string.IsNullOrWhiteSpace(tag) && seen.Add(tag))
                    {
                        yield return tag;
                    }
                }
            }
        }

        private static JsonArray CloneArray(JsonArray array)
        {
            return JsonNode.Parse(array.ToJsonString(new JsonSerializerOptions { WriteIndented = false })) as JsonArray ?? new JsonArray();
        }

        private static int CountOccurrences(string text, string pattern)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(pattern, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                count++;
                index += pattern.Length;
            }
            return count;
        }
    }
}
