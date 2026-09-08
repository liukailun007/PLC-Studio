using System;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>
    /// Classic/Basic HMI 临时工程导入预检套件。
    /// 只做导入前检查和执行计划生成，不连接 TIA Portal，不创建项目，不导入文件。
    /// </summary>
    public static class ClassicHmiTemporaryImportPreflightSuite
    {
        public static JsonObject Run(string workspaceRoot, string reportDirectory)
        {
            workspaceRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(workspaceRoot) ? Directory.GetCurrentDirectory() : workspaceRoot);
            Directory.CreateDirectory(reportDirectory);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var suiteDir = Path.Combine(reportDirectory, "preflight_" + stamp);
            var packageDir = Path.Combine(suiteDir, "classic_hmi_package");
            var plcDir = Path.Combine(suiteDir, "plc_xml");
            Directory.CreateDirectory(packageDir);
            Directory.CreateDirectory(plcDir);

            File.WriteAllText(Path.Combine(plcDir, "MotorTags.xml"), BuildPlcTagTableXml(), Encoding.UTF8);
            File.WriteAllText(Path.Combine(plcDir, "DB1_MotorData.xml"), BuildMotorDbXml(), Encoding.UTF8);
            var plcManifest = PlcSymbolManifestBuilder.BuildFromXmlPath(plcDir);
            var package = ClassicHmiMinimalPackageBuilder.WriteFiles(BuildClassicHmiPackageJson(), packageDir);
            var fileValidation = ClassicHmiMinimalPackageBuilder.ValidateFiles(packageDir);
            var syncValidation = ClassicHmiMinimalPackageBuilder.ValidateFilesWithPlcSymbols(packageDir, plcManifest["symbolNames"]?.ToJsonString() ?? "[]");

            var env = BuildEnvironmentPreflight();
            var importPlan = BuildImportPlan(package);
            var gates = new JsonArray(
                Gate("tia-public-api", "TIA V21 PublicAPI net48 可用", env["publicApiExists"]?.GetValue<bool>() == true),
                Gate("tia-portal-exe", "TIA Portal V21 可执行文件可用", env["portalExeExists"]?.GetValue<bool>() == true),
                Gate("openness-group", "当前用户属于 Siemens TIA Openness 组", env["opennessGroupDetected"]?.GetValue<bool>() == true),
                Gate("hmi-package-files", "Classic HMI 文件包齐全且 XML 可解析", fileValidation["ok"]?.GetValue<bool>() == true),
                Gate("plc-symbols", "PLC XML 可提取符号清单", plcManifest["ok"]?.GetValue<bool>() == true),
                Gate("plc-hmi-sync", "HMI ControllerTag 均存在于 PLC 符号清单", syncValidation["ok"]?.GetValue<bool>() == true),
                Gate("import-order", "导入顺序已明确：变量表先于画面", importPlan["ok"]?.GetValue<bool>() == true));

            var ok = gates.OfType<JsonObject>().All(x => x["ok"]?.GetValue<bool>() == true);
            var root = new JsonObject
            {
                ["timestamp"] = DateTime.Now.ToString("O"),
                ["mode"] = "classic-hmi-temporary-import-preflight",
                ["offlineOnly"] = true,
                ["ok"] = ok,
                ["workspaceRoot"] = workspaceRoot,
                ["suiteDirectory"] = suiteDir,
                ["safetyPolicy"] = new JsonObject
                {
                    ["tia"] = "临时导入预检：不连接 TIA Portal，不创建项目，不导入 HMI/PLC 对象。",
                    ["write"] = "只写 reports 目录下的预检文件和报告，不修改工程、reference 或交付包。",
                    ["apply"] = "只有所有 gate 通过后，才允许进入临时工程导入/读回/编译诊断执行链；真实工程仍需二次确认。"
                },
                ["gates"] = gates,
                ["environment"] = env,
                ["plcManifest"] = plcManifest,
                ["package"] = package,
                ["fileValidation"] = fileValidation,
                ["syncValidation"] = syncValidation,
                ["importPlan"] = importPlan,
                ["blockedReasons"] = new JsonArray(gates.OfType<JsonObject>()
                    .Where(x => x["ok"]?.GetValue<bool>() != true)
                    .Select(x => JsonValue.Create(x["title"]?.ToString() ?? x["id"]?.ToString() ?? "unknown"))
                    .ToArray())
            };

            var jsonPath = Path.Combine(reportDirectory, "classic_hmi_temporary_import_preflight_" + stamp + ".json");
            var mdPath = Path.Combine(reportDirectory, "classic_hmi_temporary_import_preflight_" + stamp + ".md");
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

        private static JsonObject BuildEnvironmentPreflight()
        {
            var publicApi = @"D:\app\TIA21\Portal V21\PublicAPI\V21\net48";
            var portalExe = @"D:\app\TIA21\Portal V21\Bin\Siemens.Automation.Portal.exe";
            var currentUser = "";
            var groupNames = Array.Empty<string>();
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                currentUser = identity.Name;
                groupNames = identity.Groups?
                    .Select(g =>
                    {
                        try { return g.Translate(typeof(NTAccount)).Value; }
                        catch { return g.Value; }
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToArray() ?? Array.Empty<string>();
            }
            catch
            {
                groupNames = Array.Empty<string>();
            }

            var publicApiExists = Directory.Exists(publicApi);
            return new JsonObject
            {
                ["currentUser"] = currentUser,
                ["publicApiPath"] = publicApi,
                ["publicApiExists"] = publicApiExists,
                ["siemensEngineeringDllCount"] = publicApiExists ? Directory.GetFiles(publicApi, "Siemens.Engineering*.dll").Length : 0,
                ["portalExePath"] = portalExe,
                ["portalExeExists"] = File.Exists(portalExe),
                ["opennessGroupDetected"] = groupNames.Any(x => x.IndexOf("Siemens TIA Openness", StringComparison.OrdinalIgnoreCase) >= 0),
                ["tiaEngineerGroupDetected"] = groupNames.Any(x => x.IndexOf("Siemens TIA Engineer", StringComparison.OrdinalIgnoreCase) >= 0),
                ["matchedGroups"] = new JsonArray(groupNames
                    .Where(x => x.IndexOf("Siemens", StringComparison.OrdinalIgnoreCase) >= 0 || x.IndexOf("TIA", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(x => JsonValue.Create(x))
                    .ToArray())
            };
        }

        private static JsonObject BuildImportPlan(JsonObject package)
        {
            var tagPath = package["tagTableXmlPath"]?.ToString() ?? "";
            var screenPath = package["screenXmlPath"]?.ToString() ?? "";
            var steps = new JsonArray(
                Step(1, "Create temporary TIA V21 project", "CreateProject(projectDirectory, projectName)", "只在临时目录创建项目。"),
                Step(2, "Add temporary KTP700 Basic PN HMI", "AddHardwareCatalogDeviceWithProbe(...)", "只使用临时工程硬件。"),
                Step(3, "Import Classic HMI tag table first", "ImportHmiTagTable(\"HMI_RT_1\", \"\", tagTableXmlPath)", tagPath),
                Step(4, "Import Classic HMI screen second", "ImportHmiScreen(\"HMI_RT_1\", \"\", screenXmlPath)", screenPath),
                Step(5, "Read back HMI tags/screens/items", "GetHmiTagTables/GetHmiScreens/DescribeHmiTag/DescribeHmiScreenItem", "确认 tag、ControllerTag、动态绑定和按钮事件。"),
                Step(6, "Compile/diagnose HMI where public API supports it", "DescribeService/InvokeService guarded compile discovery", "未确认 API 前只允许发现，不允许保存真实工程。"),
                Step(7, "Save temporary project only after readback succeeds", "SaveProject()", "仅临时工程。"));

            return new JsonObject
            {
                ["ok"] = File.Exists(tagPath) && File.Exists(screenPath),
                ["tagTableXmlPath"] = tagPath,
                ["screenXmlPath"] = screenPath,
                ["steps"] = steps,
                ["executionBlockedByDefault"] = true,
                ["recommendedCli"] = "TiaMcpServer.exe --tia-major-version 21 --project-directory <temp> --project-name <probe> --probe-ktp700-basic-hmi-import",
                ["note"] = "当前预检只生成执行计划；真正执行应在临时工程中导入本预检输出的 tag table 与 screen XML，并读回验证。"
            };
        }

        private static JsonObject Gate(string id, string title, bool ok)
        {
            return new JsonObject { ["id"] = id, ["title"] = title, ["ok"] = ok };
        }

        private static JsonObject Step(int order, string title, string tool, string evidence)
        {
            return new JsonObject
            {
                ["order"] = order,
                ["title"] = title,
                ["tool"] = tool,
                ["evidence"] = evidence
            };
        }

        private static string BuildClassicHmiPackageJson()
        {
            return @"{
  ""Name"": ""Classic_Motor_TemporaryImportPreflight"",
  ""TagTable"": {
    ""Name"": ""Motor_HMI_Tags"",
    ""Tags"": [
      {""Name"":""Motor_Start"",""DataType"":""Bool"",""Length"":""1"",""Connection"":""HMI_Connection_1"",""PlcTag"":""DB1_MotorData.Motor.Start""},
      {""Name"":""Motor_Run"",""DataType"":""Bool"",""Length"":""1"",""Connection"":""HMI_Connection_1"",""PlcTag"":""DB1_MotorData.Motor.Run""},
      {""Name"":""Speed_Set"",""DataType"":""Int"",""Length"":""2"",""Connection"":""HMI_Connection_1"",""PlcTag"":""DB1_MotorData.SpeedSet""}
    ]
  },
  ""ScreenDesign"": {
    ""Screen"": {""Name"":""Motor_Main"",""Width"":640,""Height"":480},
    ""Items"": [
      {""Type"":""Text"",""Name"":""Title"",""Left"":20,""Top"":20,""Width"":260,""Height"":36,""Text"":{""zh-CN"":""电机控制""}},
      {""Type"":""Button"",""Name"":""Btn_Start"",""Left"":20,""Top"":82,""Width"":130,""Height"":46,""Text"":{""zh-CN"":""启动""},""Actions"":[
        {""Event"":""Press"",""ActionKind"":""SetBit"",""TargetTag"":""Motor_Start""},
        {""Event"":""Release"",""ActionKind"":""ResetBit"",""TargetTag"":""Motor_Start""}
      ]},
      {""Type"":""Lamp"",""Name"":""Lamp_Run"",""Left"":180,""Top"":86,""Width"":42,""Height"":42,""Tag"":""Motor_Run""},
      {""Type"":""IOField"",""Name"":""IO_Speed"",""Left"":20,""Top"":154,""Width"":140,""Height"":38,""ProcessValueTag"":""Speed_Set""}
    ]
  }
}";
        }

        private static string BuildPlcTagTableXml()
        {
            return @"<?xml version=""1.0"" encoding=""utf-8""?>
<Document><SW.Tags.PlcTagTable ID=""0""><AttributeList><Name>MotorTags</Name></AttributeList><ObjectList>
<SW.Tags.PlcTag ID=""1"" CompositionName=""Tags""><AttributeList><DataTypeName>Bool</DataTypeName><LogicalAddress>%M0.0</LogicalAddress><Name>Motor_Start</Name></AttributeList></SW.Tags.PlcTag>
<SW.Tags.PlcTag ID=""2"" CompositionName=""Tags""><AttributeList><DataTypeName>Bool</DataTypeName><LogicalAddress>%M0.2</LogicalAddress><Name>Motor_Run</Name></AttributeList></SW.Tags.PlcTag>
</ObjectList></SW.Tags.PlcTagTable></Document>";
        }

        private static string BuildMotorDbXml()
        {
            return @"<?xml version=""1.0"" encoding=""utf-8""?>
<Document><SW.Blocks.GlobalDB ID=""0""><AttributeList><Interface><Sections xmlns=""http://www.siemens.com/automation/Openness/SW/Interface/v5""><Section Name=""Static"">
<Member Name=""Motor"" Datatype=""&quot;UDT_Motor&quot;""><Member Name=""Start"" Datatype=""Bool"" /><Member Name=""Run"" Datatype=""Bool"" /></Member>
<Member Name=""SpeedSet"" Datatype=""Int"" />
</Section></Sections></Interface><Name>DB1_MotorData</Name><Number>1</Number><ProgrammingLanguage>DB</ProgrammingLanguage></AttributeList></SW.Blocks.GlobalDB></Document>";
        }

        private static string BuildMarkdown(JsonObject root, string jsonPath)
        {
            var md = new StringBuilder();
            md.AppendLine("# Classic HMI Temporary Import Preflight");
            md.AppendLine();
            md.AppendLine("Generated: " + root["timestamp"]);
            md.AppendLine("JSON: " + jsonPath);
            md.AppendLine();
            md.AppendLine("## Safety");
            md.AppendLine("- 预检不连接 TIA Portal，不创建项目，不导入 HMI/PLC 对象。");
            md.AppendLine("- 只写 reports 目录下的预检文件和报告，不修改工程、reference 或交付包。");
            md.AppendLine();
            md.AppendLine("## Summary");
            md.AppendLine("- OK: " + root["ok"]);
            md.AppendLine("- Suite directory: " + root["suiteDirectory"]);
            md.AppendLine();
            md.AppendLine("## Gates");
            foreach (var node in root["gates"] as JsonArray ?? new JsonArray())
            {
                if (node is not JsonObject gate) continue;
                md.AppendLine("- " + gate["title"] + ": " + (gate["ok"]?.GetValue<bool>() == true ? "PASS" : "FAIL"));
            }
            md.AppendLine();
            md.AppendLine("## Import Plan");
            foreach (var node in root["importPlan"]?["steps"] as JsonArray ?? new JsonArray())
            {
                if (node is not JsonObject step) continue;
                md.AppendLine("- " + step["order"] + ". " + step["title"] + " -> `" + step["tool"] + "`");
            }
            return md.ToString();
        }
    }
}
