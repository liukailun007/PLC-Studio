using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>
    /// Classic/Basic HMI 离线总验收套件。
    /// 用一条命令覆盖：PLC 符号提取、HMI 文件包生成、HMI tag 引用校验、PLC-HMI 符号同步正负例。
    /// </summary>
    public static class ClassicHmiOfflineValidationSuite
    {
        public static JsonObject Run(string reportDirectory)
        {
            Directory.CreateDirectory(reportDirectory);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var suiteDir = Path.Combine(reportDirectory, "suite_" + stamp);
            var plcDir = Path.Combine(suiteDir, "plc_xml");
            var hmiDir = Path.Combine(suiteDir, "classic_hmi_package");
            Directory.CreateDirectory(plcDir);
            Directory.CreateDirectory(hmiDir);

            File.WriteAllText(Path.Combine(plcDir, "MotorTags.xml"), BuildPlcTagTableXml(), Encoding.UTF8);
            File.WriteAllText(Path.Combine(plcDir, "DB1_MotorData.xml"), BuildMotorDbXml(includeSpeedSet: true), Encoding.UTF8);
            var plcManifest = PlcSymbolManifestBuilder.BuildFromXmlPath(plcDir);

            var packageWrite = ClassicHmiMinimalPackageBuilder.WriteFiles(BuildClassicHmiPackageJson(), hmiDir);
            var packageValidation = ClassicHmiMinimalPackageBuilder.ValidateFiles(hmiDir);
            var syncGood = ClassicHmiMinimalPackageBuilder.ValidateFilesWithPlcSymbols(hmiDir, plcManifest["symbolNames"]?.ToJsonString() ?? "[]");

            var badPlcDir = Path.Combine(suiteDir, "plc_xml_bad_missing_speed");
            Directory.CreateDirectory(badPlcDir);
            File.WriteAllText(Path.Combine(badPlcDir, "MotorTags.xml"), BuildPlcTagTableXml(), Encoding.UTF8);
            File.WriteAllText(Path.Combine(badPlcDir, "DB1_MotorData.xml"), BuildMotorDbXml(includeSpeedSet: false), Encoding.UTF8);
            var badPlcManifest = PlcSymbolManifestBuilder.BuildFromXmlPath(badPlcDir);
            var syncBad = ClassicHmiMinimalPackageBuilder.ValidateFilesWithPlcSymbols(hmiDir, badPlcManifest["symbolNames"]?.ToJsonString() ?? "[]");

            var badHmiDir = Path.Combine(suiteDir, "classic_hmi_bad_missing_hmi_tag");
            Directory.CreateDirectory(badHmiDir);
            CopyPackageFile(packageWrite["screenXmlPath"]?.ToString(), Path.Combine(badHmiDir, "Bad_Screen.xml"));
            var tagXml = File.Exists(packageWrite["tagTableXmlPath"]?.ToString() ?? "")
                ? File.ReadAllText(packageWrite["tagTableXmlPath"]?.ToString() ?? "", Encoding.UTF8)
                : "";
            tagXml = tagXml.Replace("<Name>Speed_Set</Name>", "<Name>Speed_Set_Deleted</Name>");
            File.WriteAllText(Path.Combine(badHmiDir, "Bad_TagTable.xml"), tagXml, Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(badHmiDir, "Bad_manifest.json"),
                @"{""format"":""bad-case"",""tagTableXmlPath"":""Bad_TagTable.xml"",""screenXmlPath"":""Bad_Screen.xml""}",
                Encoding.UTF8);
            var packageBad = ClassicHmiMinimalPackageBuilder.ValidateFiles(badHmiDir);

            var items = new JsonArray(
                SuiteItem("plc-symbol-manifest", "PLC 符号清单提取", plcManifest),
                SuiteItem("classic-hmi-package-write", "Classic HMI 文件包生成", packageWrite),
                SuiteItem("classic-hmi-package-validate", "Classic HMI tag 引用校验", packageValidation),
                SuiteItem("classic-hmi-plc-sync-good", "PLC-HMI 同步正例", syncGood),
                NegativeSuiteItem("classic-hmi-plc-sync-bad", "PLC-HMI 同步负例：缺少 DB1_MotorData.SpeedSet", syncBad, "missingPlcSymbolCount"),
                NegativeSuiteItem("classic-hmi-tag-bad", "HMI tag 引用负例：缺少 Speed_Set", packageBad, "missingTagCount"));

            var ok = items.OfType<JsonObject>().All(x => x["ok"]?.GetValue<bool>() == true);
            var root = new JsonObject
            {
                ["timestamp"] = DateTime.Now.ToString("O"),
                ["mode"] = "classic-hmi-offline-validation-suite",
                ["offlineOnly"] = true,
                ["ok"] = ok,
                ["suiteDirectory"] = suiteDir,
                ["safetyPolicy"] = new JsonObject
                {
                    ["tia"] = "离线总验收：不连接 TIA Portal，不打开工程，不导入 PLC/HMI 对象。",
                    ["write"] = "只写 reports 目录下的套件文件和报告，不修改工程、reference 或交付包。",
                    ["binding"] = "所有 HMI 控件/事件引用必须能对上 HMI tag；所有 HMI ControllerTag 必须能对上 PLC XML 导出的符号清单。"
                },
                ["items"] = items,
                ["plcManifest"] = plcManifest,
                ["packageWrite"] = packageWrite,
                ["packageValidation"] = packageValidation,
                ["syncGood"] = syncGood,
                ["syncBad"] = syncBad,
                ["packageBad"] = packageBad
            };

            var jsonPath = Path.Combine(reportDirectory, "classic_hmi_offline_validation_suite_" + stamp + ".json");
            var mdPath = Path.Combine(reportDirectory, "classic_hmi_offline_validation_suite_" + stamp + ".md");
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

        private static JsonObject SuiteItem(string id, string title, JsonObject result)
        {
            return new JsonObject
            {
                ["id"] = id,
                ["title"] = title,
                ["ok"] = result["ok"]?.GetValue<bool>() == true,
                ["expected"] = "pass",
                ["summary"] = BuildItemSummary(result)
            };
        }

        private static JsonObject NegativeSuiteItem(string id, string title, JsonObject result, string countField)
        {
            var blocked = result["ok"]?.GetValue<bool>() != true &&
                          Convert.ToInt32(result[countField]?.ToString() ?? "0") > 0;
            return new JsonObject
            {
                ["id"] = id,
                ["title"] = title,
                ["ok"] = blocked,
                ["expected"] = "blocked",
                ["summary"] = BuildItemSummary(result)
            };
        }

        private static string BuildItemSummary(JsonObject result)
        {
            if (result["symbolCount"] != null) return "symbolCount=" + result["symbolCount"];
            if (result["fileCount"] != null) return "fileCount=" + result["fileCount"];
            if (result["missingTagCount"] != null) return "missingTagCount=" + result["missingTagCount"];
            if (result["missingPlcSymbolCount"] != null) return "missingPlcSymbolCount=" + result["missingPlcSymbolCount"];
            return "ok=" + result["ok"];
        }

        private static void CopyPackageFile(string? source, string target)
        {
            if (!string.IsNullOrWhiteSpace(source) && File.Exists(source))
                File.Copy(source, target, true);
            else
                File.WriteAllText(target, "<Document />", Encoding.UTF8);
        }

        private static string BuildClassicHmiPackageJson()
        {
            return @"{
  ""Name"": ""Classic_Motor_Suite"",
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
<Document>
  <Engineering version=""V21"" />
  <SW.Tags.PlcTagTable ID=""0"">
    <AttributeList><Name>MotorTags</Name></AttributeList>
    <ObjectList>
      <SW.Tags.PlcTag ID=""1"" CompositionName=""Tags""><AttributeList><DataTypeName>Bool</DataTypeName><LogicalAddress>%M0.0</LogicalAddress><Name>Motor_Start</Name></AttributeList></SW.Tags.PlcTag>
      <SW.Tags.PlcTag ID=""2"" CompositionName=""Tags""><AttributeList><DataTypeName>Bool</DataTypeName><LogicalAddress>%M0.2</LogicalAddress><Name>Motor_Run</Name></AttributeList></SW.Tags.PlcTag>
    </ObjectList>
  </SW.Tags.PlcTagTable>
</Document>";
        }

        private static string BuildMotorDbXml(bool includeSpeedSet)
        {
            var speedSet = includeSpeedSet ? @"            <Member Name=""SpeedSet"" Datatype=""Int"" />" : "";
            return @"<?xml version=""1.0"" encoding=""utf-8""?>
<Document>
  <Engineering version=""V21"" />
  <SW.Blocks.GlobalDB ID=""0"">
    <AttributeList>
      <Interface>
        <Sections xmlns=""http://www.siemens.com/automation/Openness/SW/Interface/v5"">
          <Section Name=""Static"">
            <Member Name=""Motor"" Datatype=""&quot;UDT_Motor&quot;"">
              <Member Name=""Start"" Datatype=""Bool"" />
              <Member Name=""Run"" Datatype=""Bool"" />
            </Member>
" + speedSet + @"
          </Section>
        </Sections>
      </Interface>
      <Name>DB1_MotorData</Name>
      <Number>1</Number>
      <ProgrammingLanguage>DB</ProgrammingLanguage>
    </AttributeList>
  </SW.Blocks.GlobalDB>
</Document>";
        }

        private static string BuildMarkdown(JsonObject root, string jsonPath)
        {
            var md = new StringBuilder();
            md.AppendLine("# Classic HMI Offline Validation Suite");
            md.AppendLine();
            md.AppendLine("Generated: " + root["timestamp"]);
            md.AppendLine("JSON: " + jsonPath);
            md.AppendLine();
            md.AppendLine("## Safety");
            md.AppendLine("- 离线总验收，不连接 TIA Portal，不打开工程，不导入 PLC/HMI 对象。");
            md.AppendLine("- 只写 reports 目录下的套件文件和报告，不修改工程、reference 或交付包。");
            md.AppendLine();
            md.AppendLine("## Summary");
            md.AppendLine("- OK: " + root["ok"]);
            md.AppendLine("- Suite directory: " + root["suiteDirectory"]);
            md.AppendLine();
            md.AppendLine("## Items");
            foreach (var item in root["items"] as JsonArray ?? new JsonArray())
            {
                if (item is not JsonObject obj) continue;
                md.AppendLine("- " + obj["title"] + ": " + (obj["ok"]?.GetValue<bool>() == true ? "PASS" : "FAIL") + " (" + obj["summary"] + ")");
            }
            md.AppendLine();
            md.AppendLine("## Next Validation");
            md.AppendLine("- 离线套件通过后，下一步仍需导入临时 Classic/Basic HMI 工程，读回 HMI tags/items/bindings/events 并编译诊断。");
            return md.ToString();
        }
    }
}
