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
    public static class HmiComponentCatalogAnalyzer
    {
        private static readonly (string Id, string Title, string[] Needles)[] ComponentRules =
        {
            ("layout", "布局框架", new[] { "Screenlayout", "Header", "Navigation", "SubNavigation", "ThirdNavigation" }),
            ("dashboard", "仪表盘与总览", new[] { "Dashboard", "Overview", "Tile", "Value Overview", "ListView" }),
            ("popup", "弹窗与选项面板", new[] { "Popup", "OptionPanel", "Option Panel", "Alerts", "Parameter Settings" }),
            ("notification", "通知与报警", new[] { "Notification", "Alarm", "Warning", "Error", "Alerts" }),
            ("command", "命令与功能面板", new[] { "Function Panel", "Functions", "Command", "Button" }),
            ("wizard", "向导步骤", new[] { "Wizard", "Progress", "Steps" }),
            ("value-input", "数值输入与步进", new[] { "ValueStepper", "IO", "Parameter", "Setting" }),
            ("chart", "图表与趋势", new[] { "PieChart", "Trend", "Chart" }),
            ("status-graphic", "状态图形", new[] { "Graphics", "NoError", "Error", "Ok", "NotOk", "Status" }),
            ("machine-module", "设备模块", new[] { "Machine_Modules", "Module", "Motor", "Drive" })
        };

        private static readonly (string Id, string Title, string[] Needles, string SafePolicy)[] EventRules =
        {
            ("navigate", "画面导航", new[] { "Navigation", "Navigate", "Screen" }, "只切换画面，不写 PLC。"),
            ("open-popup", "打开弹窗", new[] { "Popup", "OptionPanel", "Open" }, "只打开弹窗或参数面板，参数写入必须走确认按钮。"),
            ("close-popup", "关闭弹窗", new[] { "Close", "Cancel", "Back" }, "关闭弹窗不写 PLC。"),
            ("set-bit", "瞬时命令置位", new[] { "SetBitInTag", "Cmd_", "Start", "Stop", "Reset" }, "只允许绑定到已验证的 PLC 命令变量，不能凭空生成 M 点。"),
            ("set-value", "参数写入", new[] { "ValueStepper", "Parameter", "SetValue", "Write" }, "在线时应有权限、范围、确认和异常反馈。"),
            ("wizard-step", "向导上一步/下一步", new[] { "Wizard", "Next", "Previous", "Step" }, "向导事件只改变 HMI 内部流程状态，最终写入集中确认。"),
            ("acknowledge", "报警确认", new[] { "Alarm", "Acknowledge", "Notification_OK" }, "报警确认应使用 WinCC 报警服务，不直接改 PLC 报警位。")
        };

        public static JsonObject Analyze(string globalLibraryProbeJsonPath, string templateDirectory)
        {
            var root = new JsonObject
            {
                ["timestamp"] = DateTime.Now.ToString("O"),
                ["globalLibraryProbeJsonPath"] = globalLibraryProbeJsonPath,
                ["templateDirectory"] = templateDirectory,
                ["safetyPolicy"] = new JsonObject
                {
                    ["mode"] = "离线分析：只读取全局库探测报告和本地 HMI 模板。",
                    ["tia"] = "不连接 TIA Portal，不打开项目，不导入全局库对象。",
                    ["write"] = "不修改参考项目、全局库、监控表、强制表或交付包。",
                    ["binding"] = "HMI 控件绑定必须能在 PLC 变量、DB 成员或 HMI 内部变量中被验证，禁止凭空绑定 M 点。"
                }
            };

            var libraryPaths = LoadLibraryPaths(globalLibraryProbeJsonPath);
            var templateFacts = LoadTemplateFacts(templateDirectory);
            var componentCatalog = BuildComponentCatalog(libraryPaths, templateFacts);
            var eventCatalog = BuildEventCatalog(libraryPaths, templateFacts);

            root["libraryPathCount"] = libraryPaths.Count;
            root["templateCount"] = templateFacts.Count;
            root["componentCatalog"] = componentCatalog;
            root["eventCatalog"] = eventCatalog;
            root["templateCoverage"] = BuildTemplateCoverage(componentCatalog, eventCatalog, templateFacts);
            root["recommendedScreenBlueprint"] = BuildRecommendedBlueprint();
            root["nextImplementationSteps"] = BuildNextSteps(componentCatalog, eventCatalog);
            root["ok"] = File.Exists(globalLibraryProbeJsonPath) && Directory.Exists(templateDirectory) && libraryPaths.Count > 0 && templateFacts.Count > 0;

            return root;
        }

        public static string BuildMarkdown(JsonObject root, string jsonPath)
        {
            var md = new StringBuilder();
            md.AppendLine("# HMI Component Catalog");
            md.AppendLine();
            md.AppendLine("Generated: " + root["timestamp"]);
            md.AppendLine("JSON: " + jsonPath);
            md.AppendLine();
            md.AppendLine("## Safety");
            md.AppendLine("- 离线分析，不连接 TIA，不写参考项目，不同步交付包。");
            md.AppendLine("- 所有 HMI 控件绑定最终都必须和 PLC 变量/DB 成员或明确的 HMI 内部变量同步验证。");
            md.AppendLine("- 在线监视只读；强制表和在线修改仍是禁区。");
            md.AppendLine();

            md.AppendLine("## Component Catalog");
            if (root["componentCatalog"] is JsonArray components)
            {
                foreach (var node in components.OfType<JsonObject>())
                {
                    md.AppendLine("- " + node["title"] + ": libraryHits=" + node["libraryHitCount"] + ", templateHits=" + node["templateHitCount"] + ", status=" + node["status"]);
                    var samples = node["sampleLibraryPaths"] as JsonArray ?? new JsonArray();
                    foreach (var sample in samples.Take(3))
                    {
                        md.AppendLine("  - " + sample);
                    }
                }
            }
            md.AppendLine();

            md.AppendLine("## Event Catalog");
            if (root["eventCatalog"] is JsonArray events)
            {
                foreach (var node in events.OfType<JsonObject>())
                {
                    md.AppendLine("- " + node["title"] + ": libraryHints=" + node["libraryHintCount"] + ", templateActions=" + node["templateActionCount"]);
                    md.AppendLine("  - safety: " + node["safePolicy"]);
                }
            }
            md.AppendLine();

            md.AppendLine("## Template Coverage");
            if (root["templateCoverage"] is JsonArray coverage)
            {
                foreach (var node in coverage.OfType<JsonObject>())
                {
                    md.AppendLine("- " + node["templateName"] + ": " + node["summary"]);
                }
            }
            md.AppendLine();

            md.AppendLine("## Recommended Screen Blueprint");
            if (root["recommendedScreenBlueprint"] is JsonArray blueprint)
            {
                foreach (var node in blueprint.OfType<JsonObject>())
                {
                    md.AppendLine("- " + node["area"] + ": " + node["components"] + " - " + node["purpose"]);
                }
            }
            md.AppendLine();

            md.AppendLine("## Next Implementation Steps");
            if (root["nextImplementationSteps"] is JsonArray steps)
            {
                foreach (var step in steps)
                {
                    md.AppendLine("- " + step);
                }
            }

            return md.ToString();
        }

        public static void WriteReports(JsonObject root, string reportDir)
        {
            Directory.CreateDirectory(reportDir);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var jsonPath = Path.Combine(reportDir, "hmi_component_catalog_" + stamp + ".json");
            var mdPath = Path.Combine(reportDir, "hmi_component_catalog_" + stamp + ".md");
            File.WriteAllText(jsonPath, root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }), Encoding.UTF8);
            File.WriteAllText(mdPath, BuildMarkdown(root, jsonPath), Encoding.UTF8);
        }

        private static List<string> LoadLibraryPaths(string path)
        {
            var paths = new List<string>();
            if (!File.Exists(path)) return paths;

            var json = JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) as JsonObject;
            var probe = json?["probe"] as JsonObject;
            foreach (var key in new[] { "masterCopies", "libraryTypes" })
            {
                if (probe?[key] is not JsonArray arr) continue;
                paths.AddRange(arr.Select(x => x?.ToString() ?? "").Where(x => !string.IsNullOrWhiteSpace(x)));
            }

            return paths.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<TemplateFact> LoadTemplateFacts(string templateDirectory)
        {
            var facts = new List<TemplateFact>();
            if (!Directory.Exists(templateDirectory)) return facts;

            foreach (var file in Directory.EnumerateFiles(templateDirectory, "*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var json = JsonNode.Parse(File.ReadAllText(file, Encoding.UTF8)) as JsonObject;
                    if (json == null) continue;

                    var fact = new TemplateFact
                    {
                        File = file,
                        TemplateName = json["TemplateName"]?.ToString() ?? Path.GetFileNameWithoutExtension(file),
                        Purpose = json["Purpose"]?.ToString() ?? ""
                    };

                    if (json["Components"] is JsonArray components)
                    {
                        fact.Components.AddRange(components.OfType<JsonObject>().Select(x => x["Kind"]?.ToString() ?? x["Name"]?.ToString() ?? "").Where(x => !string.IsNullOrWhiteSpace(x)));
                    }

                    if (json["Items"] is JsonArray items)
                    {
                        foreach (var itemNode in items.OfType<JsonObject>())
                        {
                            fact.ItemTypes.Add(itemNode["Type"]?.ToString() ?? "");
                            fact.ItemNames.Add(itemNode["Name"]?.ToString() ?? "");
                            if (itemNode["Actions"] is JsonArray actions)
                            {
                                foreach (var action in actions.OfType<JsonObject>())
                                {
                                    fact.Events.Add(action["Event"]?.ToString() ?? "");
                                    fact.Scripts.Add(action["Script"]?.ToString() ?? "");
                                }
                            }
                        }
                    }

                    if (json["Events"] is JsonArray events)
                    {
                        fact.Events.AddRange(events.OfType<JsonObject>().Select(x => x["Event"]?.ToString() ?? x["Kind"]?.ToString() ?? "").Where(x => !string.IsNullOrWhiteSpace(x)));
                    }

                    facts.Add(fact);
                }
                catch (Exception ex)
                {
                    facts.Add(new TemplateFact
                    {
                        File = file,
                        TemplateName = Path.GetFileNameWithoutExtension(file),
                        Purpose = "Parse error: " + ex.Message
                    });
                }
            }

            return facts;
        }

        private static JsonArray BuildComponentCatalog(List<string> libraryPaths, List<TemplateFact> templateFacts)
        {
            var result = new JsonArray();
            foreach (var rule in ComponentRules)
            {
                var libraryHits = libraryPaths.Where(path => ContainsAny(path, rule.Needles)).ToList();
                var templateHits = templateFacts.Where(t => t.ContainsAny(rule.Needles)).Select(t => t.TemplateName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                result.Add(new JsonObject
                {
                    ["id"] = rule.Id,
                    ["title"] = rule.Title,
                    ["libraryHitCount"] = libraryHits.Count,
                    ["templateHitCount"] = templateHits.Count,
                    ["status"] = templateHits.Count > 0 ? "covered-by-current-templates" : libraryHits.Count > 0 ? "candidate-from-global-library" : "not-found",
                    ["sampleLibraryPaths"] = new JsonArray(libraryHits.Take(8).Select(x => JsonValue.Create(x)).ToArray()),
                    ["coveredTemplates"] = new JsonArray(templateHits.Select(x => JsonValue.Create(x)).ToArray())
                });
            }

            return result;
        }

        private static JsonArray BuildEventCatalog(List<string> libraryPaths, List<TemplateFact> templateFacts)
        {
            var result = new JsonArray();
            foreach (var rule in EventRules)
            {
                var libraryHits = libraryPaths.Where(path => ContainsAny(path, rule.Needles)).ToList();
                var templateActionCount = templateFacts.Sum(t => t.CountEventHints(rule.Needles));
                result.Add(new JsonObject
                {
                    ["id"] = rule.Id,
                    ["title"] = rule.Title,
                    ["libraryHintCount"] = libraryHits.Count,
                    ["templateActionCount"] = templateActionCount,
                    ["safePolicy"] = rule.SafePolicy,
                    ["sampleLibraryPaths"] = new JsonArray(libraryHits.Take(6).Select(x => JsonValue.Create(x)).ToArray())
                });
            }

            return result;
        }

        private static JsonArray BuildTemplateCoverage(JsonArray componentCatalog, JsonArray eventCatalog, List<TemplateFact> templateFacts)
        {
            var result = new JsonArray();
            var componentNames = ComponentRules.Select(x => x.Title).ToArray();
            var eventNames = EventRules.Select(x => x.Title).ToArray();
            foreach (var fact in templateFacts)
            {
                var coveredComponents = ComponentRules.Where(rule => fact.ContainsAny(rule.Needles)).Select(rule => rule.Title).ToList();
                var coveredEvents = EventRules.Where(rule => fact.CountEventHints(rule.Needles) > 0).Select(rule => rule.Title).ToList();
                var missingComponents = componentNames.Except(coveredComponents).Take(5).ToList();
                var missingEvents = eventNames.Except(coveredEvents).Take(4).ToList();
                result.Add(new JsonObject
                {
                    ["templateName"] = fact.TemplateName,
                    ["file"] = fact.File,
                    ["coveredComponents"] = new JsonArray(coveredComponents.Select(x => JsonValue.Create(x)).ToArray()),
                    ["coveredEvents"] = new JsonArray(coveredEvents.Select(x => JsonValue.Create(x)).ToArray()),
                    ["recommendedComponentGaps"] = new JsonArray(missingComponents.Select(x => JsonValue.Create(x)).ToArray()),
                    ["recommendedEventGaps"] = new JsonArray(missingEvents.Select(x => JsonValue.Create(x)).ToArray()),
                    ["summary"] = $"components={coveredComponents.Count}, events={coveredEvents.Count}, recommendedGaps={missingComponents.Count + missingEvents.Count}"
                });
            }

            return result;
        }

        private static JsonArray BuildRecommendedBlueprint()
        {
            return new JsonArray
            {
                new JsonObject { ["area"] = "顶部栏", ["components"] = "Header + 状态胶囊 + 当前用户/时间", ["purpose"] = "建立统一品牌感和运行状态入口。" },
                new JsonObject { ["area"] = "主导航", ["components"] = "MainNavigation + SubNavigation", ["purpose"] = "按总览、设备、报警、参数、诊断组织画面。" },
                new JsonObject { ["area"] = "总览区", ["components"] = "Dashboard Tiles + Value Overview + Status Graphics", ["purpose"] = "快速看到运行、自动、故障、产量、速度等关键状态。" },
                new JsonObject { ["area"] = "设备卡片", ["components"] = "Machine Module + Function Panel", ["purpose"] = "每台设备暴露状态、命令和进入详情的事件。" },
                new JsonObject { ["area"] = "参数区", ["components"] = "ValueStepper + OptionPanel + Confirm/Cancel", ["purpose"] = "参数输入要有范围、确认、取消和错误提示。" },
                new JsonObject { ["area"] = "报警区", ["components"] = "Notification + Alarm/Alert Popup", ["purpose"] = "报警摘要、确认入口和详细弹窗分层展示。" },
                new JsonObject { ["area"] = "向导区", ["components"] = "Wizard + Progress Indicator", ["purpose"] = "用于换型、点动、调试流程，事件按步骤推进。" }
            };
        }

        private static JsonArray BuildNextSteps(JsonArray componentCatalog, JsonArray eventCatalog)
        {
            return new JsonArray
            {
                "把现有模板扩展为 Header、Navigation、Dashboard、CommandPanel、AlarmBanner、PopupLauncher、ValueStepper 等组件契约。",
                "每个按钮事件都声明 Event、ActionKind、TargetTag 或 TargetScreen，并由预检查验证 RequiredTags 中存在对应变量。",
                "参数写入类控件统一增加 Min/Max/Unit/ConfirmRequired/ErrorTag 字段，避免 HMI 直接裸写 PLC。",
                "模板应用前增加 PLC/HMI 同步校验：未验证 PLC 变量或 DB 成员时只生成报告，不落地到项目。",
                "参考全局库路径只作为布局和组件命名依据，真正项目写入必须用 TIA 读回、编译和 HMI 导出验证。"
            };
        }

        private static bool ContainsAny(string value, IEnumerable<string> needles)
        {
            return needles.Any(needle => value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private sealed class TemplateFact
        {
            public string File { get; set; } = "";
            public string TemplateName { get; set; } = "";
            public string Purpose { get; set; } = "";
            public List<string> Components { get; } = new List<string>();
            public List<string> ItemTypes { get; } = new List<string>();
            public List<string> ItemNames { get; } = new List<string>();
            public List<string> Events { get; } = new List<string>();
            public List<string> Scripts { get; } = new List<string>();

            public bool ContainsAny(IEnumerable<string> needles)
            {
                var joined = string.Join(" ", new[] { TemplateName, Purpose }.Concat(Components).Concat(ItemTypes).Concat(ItemNames).Concat(Events).Concat(Scripts));
                return HmiComponentCatalogAnalyzer.ContainsAny(joined, needles);
            }

            public int CountEventHints(IEnumerable<string> needles)
            {
                var values = Events.Concat(Scripts).ToList();
                return values.Count(value => HmiComponentCatalogAnalyzer.ContainsAny(value, needles));
            }
        }
    }
}
