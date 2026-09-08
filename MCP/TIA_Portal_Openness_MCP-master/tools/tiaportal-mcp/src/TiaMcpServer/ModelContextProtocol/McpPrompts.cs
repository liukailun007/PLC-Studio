using ModelContextProtocol.Server;
using System.ComponentModel;

namespace TiaMcpServer.ModelContextProtocol
{
    [McpServerPromptType]
    public static class McpPrompts
    {
        // ──────────────────────────────────────────────────────────────────────
        // Basic connection / navigation
        // ──────────────────────────────────────────────────────────────────────

        [McpServerPrompt(Name = "Connect"), Description("Connect to TIA Portal")]
        public static string Connect()
        {
            return @"Connect to TIA Portal.

Steps:
1. EnsureOpennessUserGroup — confirm the Windows user is in 'Siemens TIA Openness' group.
2. Connect — attach to a running TIA Portal process or launch a new one.
3. GetState — confirm IsConnected=true.

Use the Connect tool to initiate the connection.";
        }

        [McpServerPrompt(Name = "OpenProject"), Description("Open a TIA Portal project")]
        public static string OpenProject(string projectPath)
        {
            return $@"Open a TIA Portal project.

Common parameter values:
- projectPath: full path to project file (.ap21) or multi-user session (.als21).

Steps:
1. Connect (if not already connected).
2. OpenProject with projectPath.
3. GetProjectTree — discover device/software paths for further operations.

Use the OpenProject tool with:
- projectPath: {projectPath}";
        }

        [McpServerPrompt(Name = "CloseProject"), Description("Close the currently open TIA Portal project")]
        public static string CloseProject()
        {
            return @"Close the currently open TIA Portal project.

Steps:
1. SaveProject — save any pending changes first.
2. CloseProject — close the project.

Use the CloseProject tool to close the current project.";
        }

        [McpServerPrompt(Name = "Disconnect"), Description("Disconnect from TIA Portal")]
        public static string Disconnect()
        {
            return @"Disconnect from TIA Portal.

Steps:
1. SaveProject — save any unsaved changes.
2. CloseProject (optional) — cleanly close the project.
3. Disconnect — release the Openness handle.

Use the Disconnect tool to remove the connection.";
        }

        [McpServerPrompt(Name = "GetProjectTree"), Description("Get the full project structure tree")]
        public static string GetProjectTree()
        {
            return @"Retrieve the complete project structure as an ASCII tree.

The tree shows:
- All devices (PLCs, HMI panels, drives)
- Device items (CPU, communication modules)
- PLC software paths (e.g. 'PLC_1')
- HMI software paths (e.g. 'HMI_RT_1')

Steps:
1. Connect + OpenProject (if not already done).
2. GetProjectTree — read the tree.
3. Note the exact softwarePath values for PLC and HMI operations.

IMPORTANT: always call GetProjectTree first when working with an unfamiliar project.";
        }

        // ──────────────────────────────────────────────────────────────────────
        // Hardware / network
        // ──────────────────────────────────────────────────────────────────────

        [McpServerPrompt(Name = "CreateProjectWithDevices"), Description("Create a new project, add a PLC and HMI, connect them via PROFINET")]
        public static string CreateProjectWithDevices(
            string projectDirectory,
            string projectName,
            string plcFamily,
            string plcDeviceName,
            string hmiKeyword,
            string hmiDeviceName)
        {
            return $@"Create a new TIA Portal project with PLC + HMI connected via PROFINET.

Steps:
1. Connect.
2. CreateProject — directory='{projectDirectory}', name='{projectName}'.
3. AddDeviceWithFallback — family='{plcFamily}', deviceName='{plcDeviceName}'.
4. AddHardwareCatalogDeviceWithProbe — keyword='{hmiKeyword}', deviceName='{hmiDeviceName}'.
5. GetProjectTree — note the exact device-item paths.
6. ConnectDeviceNodesToProfinetSubnet — firstRootPath='{plcDeviceName}', secondRootPath=derived from tree.
7. SaveProject.

Use these tools in order with the parameters above.";
        }

        [McpServerPrompt(Name = "AddProfinetDevice"), Description("Add a Siemens hardware device and connect it to an existing PROFINET subnet")]
        public static string AddProfinetDevice(string keyword, string deviceName, string existingPlcRoot)
        {
            return $@"Add a Siemens hardware device and connect it to PROFINET.

Steps:
1. SearchHardwareCatalog — keyword='{keyword}' to confirm exact MLFB.
2. AddHardwareCatalogDeviceWithProbe — keyword='{keyword}', deviceName='{deviceName}'.
3. GetProjectTree — discover device-item paths under '{deviceName}'.
4. ConnectDeviceNodesToProfinetSubnet — firstRootPath='{existingPlcRoot}', secondRootPath=path from tree.
5. SaveProject.

Use these tools in order.";
        }

        // ──────────────────────────────────────────────────────────────────────
        // PLC blocks — export / import
        // ──────────────────────────────────────────────────────────────────────

        [McpServerPrompt(Name = "GetSoftwareTree"), Description("Get the structure/tree of a specific PLC software showing blocks and types")]
        public static string GetSoftwareTree(string softwarePath)
        {
            return $@"Retrieve the complete PLC software structure.

The tree shows:
- OB / FC / FB / GlobalDB / InstanceDB blocks with group hierarchy
- UDT types
- External sources
- Full qualified paths needed for ExportBlock (e.g. 'Program blocks/FBs/FB_Motor')

Steps:
1. Connect + OpenProject.
2. GetSoftwareTree — softwarePath='{softwarePath}'.
3. Note the exact block paths for export/import operations.

Use the GetSoftwareTree tool with:
- softwarePath: {softwarePath}";
        }

        [McpServerPrompt(Name = "ExportBlocks"), Description("Export blocks from PLC software")]
        public static string ExportBlocks(string softwarePath, string exportPath, string regexName, bool preservePath)
        {
            return $@"Export blocks from PLC software.

Common parameter values:
- softwarePath: e.g. 'PLC_1'
- exportPath: '${{workspacefolder}}/export/Program blocks'
- regexName: empty string for all blocks, or 'FB_.*' for function blocks only
- preservePath: false=flat export, true=maintain folder structure

Steps:
1. CompileAndDiagnosePlc — ensure blocks are consistent before export.
2. ExportBlocks — export with the parameters below.

Use the ExportBlocks tool with:
- softwarePath: {softwarePath}
- exportPath: {exportPath}
- regexName: {regexName}
- preservePath: {preservePath.ToString().ToLower()}";
        }

        [McpServerPrompt(Name = "ExportTypes"), Description("Export types from PLC software")]
        public static string ExportTypes(string softwarePath, string exportPath, string regexName, bool preservePath)
        {
            return $@"Export user-defined types (UDTs) from PLC software.

Steps:
1. ExportTypes — export with the parameters below.

Use the ExportTypes tool with:
- softwarePath: {softwarePath}
- exportPath: {exportPath}
- regexName: {regexName}
- preservePath: {preservePath.ToString().ToLower()}";
        }

        [McpServerPrompt(Name = "ExportBlocksAsDocuments"), Description("Export blocks as documents (.s7dcl/.s7res format, V20+)")]
        public static string ExportBlocksAsDocuments(string softwarePath, string exportPath, string regexName, bool preservePath)
        {
            return $@"Export blocks as SIMATIC SD documents (.s7dcl/.s7res). Requires TIA Portal V20+.

Note: importing LAD blocks requires the .s7res to contain en-US tags for all items.

Use the ExportBlocksAsDocuments tool with:
- softwarePath: {softwarePath}
- exportPath: {exportPath}
- regexName: {regexName}
- preservePath: {preservePath.ToString().ToLower()}";
        }

        // ──────────────────────────────────────────────────────────────────────
        // Convenience export shorthands
        // ──────────────────────────────────────────────────────────────────────

        [McpServerPrompt(Name = "ExportAllBlocksFlattened"), Description("Export all blocks from PLC software (flat)")]
        public static string ExportAllBlocksFlattened(string softwarePath, string exportPath)
            => ExportBlocks(softwarePath, exportPath, "", false);

        [McpServerPrompt(Name = "ExportAllBlocksStructured"), Description("Export all blocks from PLC software (preserving folder structure)")]
        public static string ExportAllBlocksStructured(string softwarePath, string exportPath)
            => ExportBlocks(softwarePath, exportPath, "", true);

        [McpServerPrompt(Name = "ExportAllTypesFlattened"), Description("Export all UDT types from PLC software (flat)")]
        public static string ExportAllTypesFlattened(string softwarePath, string exportPath)
            => ExportTypes(softwarePath, exportPath, "", false);

        [McpServerPrompt(Name = "ExportAllTypesStructured"), Description("Export all UDT types from PLC software (preserving folder structure)")]
        public static string ExportAllTypesStructured(string softwarePath, string exportPath)
            => ExportTypes(softwarePath, exportPath, "", true);

        [McpServerPrompt(Name = "ExportAllBlocksAsDocumentsFlattened"), Description("Export all blocks as documents (flat)")]
        public static string ExportAllBlocksAsDocumentsFlattened(string softwarePath, string exportPath)
            => ExportBlocksAsDocuments(softwarePath, exportPath, "", false);

        [McpServerPrompt(Name = "ExportAllBlocksAsDocumentsStructured"), Description("Export all blocks as documents (preserving folder structure)")]
        public static string ExportAllBlocksAsDocumentsStructured(string softwarePath, string exportPath)
            => ExportBlocksAsDocuments(softwarePath, exportPath, "", true);

        // ──────────────────────────────────────────────────────────────────────
        // Import from documents (V20+)
        // ──────────────────────────────────────────────────────────────────────

        [McpServerPrompt(Name = "ImportFromDocuments"), Description("Import a single block from SIMATIC SD documents (.s7dcl/.s7res, V20+)")]
        public static string ImportFromDocuments(string softwarePath, string groupPath, string importPath, string fileNameWithoutExtension, string importOption)
        {
            return $@"Import one program block from SIMATIC SD documents (requires TIA Portal V20+).

Note: importing LAD blocks requires the .s7res to contain en-US tags.

Use the ImportFromDocuments tool with:
- softwarePath: {softwarePath}
- groupPath: {groupPath}
- importPath: {importPath}
- fileNameWithoutExtension: {fileNameWithoutExtension}
- importOption: {importOption}";
        }

        [McpServerPrompt(Name = "ImportBlocksFromDocuments"), Description("Import blocks from SIMATIC SD documents (.s7dcl/.s7res, V20+)")]
        public static string ImportBlocksFromDocuments(string softwarePath, string groupPath, string importPath, string regexName, string importOption)
        {
            return $@"Import multiple program blocks from SIMATIC SD documents (requires TIA Portal V20+).

Note: importing LAD blocks requires the .s7res to contain en-US tags.

Use the ImportBlocksFromDocuments tool with:
- softwarePath: {softwarePath}
- groupPath: {groupPath}
- importPath: {importPath}
- regexName: {regexName}
- importOption: {importOption}";
        }

        // ──────────────────────────────────────────────────────────────────────
        // PLC block creation from natural language
        // ──────────────────────────────────────────────────────────────────────

        [McpServerPrompt(Name = "CreatePlcFunctionBlock"), Description("Create a new PLC FB from a natural-language description")]
        public static string CreatePlcFunctionBlock(string softwarePath, string fbName, int fbNumber, string description)
        {
            return $@"Create a new PLC Function Block (FB) and import it into the project.

Goal: {description}

Steps:
1. GetSoftwareTree — softwarePath='{softwarePath}' — find an appropriate group path.
2. ComposePlcFbBlockXml — fbBlockJson with name='{fbName}', number={fbNumber}, inputs/outputs/statics matching the description.
   - dryRun=true first to validate XML.
3. PlcBuildAndImport — kind='fb', softwarePath='{softwarePath}', dryRun=false, compileAfter=true.
4. CompileAndDiagnosePlc — verify 0 errors.
5. GetBlockInfo — softwarePath='{softwarePath}', blockPath='Program blocks/{fbName}' — confirm import.

If compile errors occur: export the failed block, inspect the XML, fix the interface, re-import.";
        }

        [McpServerPrompt(Name = "CreatePlcFunctionBlockWithLogic"), Description("Create a PLC FB with SCL logic from natural language")]
        public static string CreatePlcFunctionBlockWithLogic(string softwarePath, string fbName, int fbNumber)
        {
            return $@"Create a PLC Function Block with SCL structured-text logic.

Steps:
1. GetSoftwareTree — softwarePath='{softwarePath}'.
2. ComposePlcFbBlockXml — provide fbBlockJson:
   {{
     ""blockName"": ""{fbName}"",
     ""blockNumber"": {fbNumber},
     ""inputs"":  [{{ ""name"": ""..."", ""datatype"": ""..."" }}],
     ""outputs"": [{{ ""name"": ""..."", ""datatype"": ""..."" }}],
     ""statics"": [{{ ""name"": ""..."", ""datatype"": ""..."" }}],
     ""structuredText"": {{ ""operations"": [...] }}
   }}
3. PlcBuildAndImport — kind='fb', softwarePath='{softwarePath}', dryRun=true then dryRun=false.
4. CompileAndDiagnosePlc.
5. GetBlockInfo — readback confirmation.";
        }

        [McpServerPrompt(Name = "CreatePlcGlobalDb"), Description("Create a new PLC GlobalDB from a natural-language description")]
        public static string CreatePlcGlobalDb(string softwarePath, string dbName, int dbNumber)
        {
            return $@"Create a new PLC Global Data Block (GlobalDB).

Steps:
1. BuildPlcGlobalDbXml or PlcBuildAndImport — kind='globaldb':
   {{
     ""dbName"": ""{dbName}"",
     ""dbNumber"": {dbNumber},
     ""staticMembers"": [{{ ""name"": ""..."", ""datatype"": ""..."", ""startValue"": ""..."" }}]
   }}
2. PlcBuildAndImport — kind='globaldb', softwarePath='{softwarePath}', dryRun=false.
3. CompileAndDiagnosePlc.
4. GetBlockInfo — readback.";
        }

        [McpServerPrompt(Name = "CreatePlcTagTable"), Description("Create a new PLC tag table with I/O tags")]
        public static string CreatePlcTagTable(string softwarePath, string tableName)
        {
            return $@"Create a new PLC tag table with I/O address assignments.

Steps:
1. BuildPlcTagTableXml or PlcBuildAndImport — kind='tagtable':
   {{
     ""tableName"": ""{tableName}"",
     ""tags"": [
       {{ ""name"": ""..."", ""dataTypeName"": ""Bool"", ""logicalAddress"": ""%I0.0"" }},
       {{ ""name"": ""..."", ""dataTypeName"": ""Bool"", ""logicalAddress"": ""%Q0.0"" }}
     ]
   }}
2. PlcBuildAndImport — kind='tagtable', softwarePath='{softwarePath}', dryRun=false.
3. CompileAndDiagnosePlc.";
        }

        // ──────────────────────────────────────────────────────────────────────
        // Compile and diagnostics
        // ──────────────────────────────────────────────────────────────────────

        [McpServerPrompt(Name = "CompileAndDiagnose"), Description("Compile PLC software and diagnose errors/warnings")]
        public static string CompileAndDiagnose(string softwarePath)
        {
            return $@"Compile PLC software and review structured diagnostics.

Steps:
1. CompileAndDiagnosePlc — softwarePath='{softwarePath}'.
2. Review output:
   - errors=0 → success, continue.
   - errors>0 → read each error message; the block name and line number are included.
     - Export the failing block with ExportBlock to a working directory.
     - Inspect the XML, correct the interface/logic.
     - ImportBlock the corrected file.
     - Repeat CompileAndDiagnosePlc.

Use the CompileAndDiagnosePlc tool with:
- softwarePath: {softwarePath}";
        }

        // ──────────────────────────────────────────────────────────────────────
        // HMI — Unified (WinCC Unified)
        // ──────────────────────────────────────────────────────────────────────

        [McpServerPrompt(Name = "CreateUnifiedHmiPage"), Description("Create a complete WinCC Unified HMI screen with tags, controls, and PLC bindings")]
        public static string CreateUnifiedHmiPage(
            string hmiSoftwarePath,
            string screenName,
            string plcName,
            string connectionName)
        {
            return $@"Create a complete WinCC Unified HMI screen with PLC-bound tags and controls.

Steps:
1. EnsureUnifiedHmiConnection — hmiSoftwarePath='{hmiSoftwarePath}', connectionName='{connectionName}', plcName='{plcName}'.
2. EnsureUnifiedHmiScreen — hmiSoftwarePath='{hmiSoftwarePath}', screenName='{screenName}'.
3. EnsureUnifiedHmiTagTable — hmiSoftwarePath='{hmiSoftwarePath}', tagTableName='默认变量表'.
4. EnsureUnifiedHmiTag (repeat for each tag) — bind each tag to a PLC DB variable.
5. ApplyUnifiedHmiScreenDesignJson — apply complete layout JSON (controls with positions, text, properties).
6. EnsureUnifiedHmiDynamization (optional) — bind controls to HMI tags for live values.
7. SaveProject.

Typical designJson structure:
{{
  ""items"": [
    {{ ""name"": ""BTN_Start"", ""type"": ""Button"", ""left"": 50,  ""top"": 50, ""width"": 120, ""height"": 40, ""text"": ""Start"" }},
    {{ ""name"": ""BTN_Stop"",  ""type"": ""Button"", ""left"": 200, ""top"": 50, ""width"": 120, ""height"": 40, ""text"": ""Stop""  }},
    {{ ""name"": ""LMP_Run"",   ""type"": ""Rectangle"", ""left"": 350, ""top"": 50, ""width"": 60, ""height"": 40 }}
  ]
}}";
        }

        [McpServerPrompt(Name = "CreateStartStopHmi"), Description("Create a motor start/stop HMI screen with minimal tags and buttons")]
        public static string CreateStartStopHmi(string hmiSoftwarePath, string screenName)
        {
            return $@"Create a motor start/stop HMI screen using the built-in shortcut.

Steps:
1. GetHmiProgramInfo — softwarePath='{hmiSoftwarePath}' — confirm it is Unified HMI.
2. EnsureStartStopUnifiedHmi — hmiSoftwarePath='{hmiSoftwarePath}', screenName='{screenName}'.
   This creates tags: StartPB / StopPB / EStop / RunOut (all Bool) and matching UI items.
3. SaveProject.

For custom layouts, use CreateUnifiedHmiPage instead.";
        }

        [McpServerPrompt(Name = "ApplyHmiLayout"), Description("Apply a grid-based layout to an existing Unified HMI screen")]
        public static string ApplyHmiLayout(string hmiSoftwarePath, string screenName)
        {
            return $@"Apply a grid layout to a Unified HMI screen.

Steps:
1. BuildUnifiedHmiLayoutDesignJson — provide layoutJson:
   {{
     ""grid"": true, ""columns"": 4, ""cellWidth"": 150, ""cellHeight"": 60, ""gap"": 10,
     ""items"": [
       {{ ""name"": ""LBL_Title"", ""type"": ""Rectangle"", ""row"": 0, ""col"": 0, ""colSpan"": 4, ""text"": ""Motor Control"" }},
       {{ ""name"": ""BTN_Start"", ""type"": ""Button"",    ""row"": 1, ""col"": 0, ""text"": ""Start"" }},
       {{ ""name"": ""BTN_Stop"",  ""type"": ""Button"",    ""row"": 1, ""col"": 1, ""text"": ""Stop""  }}
     ]
   }}
2. ApplyUnifiedHmiLayout — hmiSoftwarePath='{hmiSoftwarePath}', screenName='{screenName}'.
3. SaveProject.";
        }

        // ──────────────────────────────────────────────────────────────────────
        // HMI — Classic / Basic
        // ──────────────────────────────────────────────────────────────────────

        [McpServerPrompt(Name = "CreateClassicHmiScreen"), Description("Create a Classic/Basic WinCC HMI screen from JSON definition")]
        public static string CreateClassicHmiScreen(string hmiSoftwarePath, string screenName)
        {
            return $@"Create a Classic/Basic WinCC HMI screen.

Steps:
1. BuildClassicHmiScreenXml — designJson with Screen and Items (Text/Button/IOField/Lamp/Rectangle).
2. WriteClassicHmiMinimalPackageFiles — write screen XML and tag table to disk.
3. ValidateClassicHmiMinimalPackageFiles — verify package is well-formed.
4. ImportHmiScreen — hmiSoftwarePath='{hmiSoftwarePath}', importPath=written XML file.
5. ImportHmiTagTable — import matching tag table.
6. SaveProject.";
        }

        // ──────────────────────────────────────────────────────────────────────
        // Online monitoring
        // ──────────────────────────────────────────────────────────────────────

        [McpServerPrompt(Name = "MonitorPlcValues"), Description("Read current PLC variable values from a watch table (read-only)")]
        public static string MonitorPlcValues(string softwarePath)
        {
            return $@"Read current PLC variable values online (read-only, no writes).

Steps:
1. PlanOnlineReadOnlyMonitoring — validate the monitoring request offline first.
2. ProbePlcMonitorOnlineCapabilities — softwarePath='{softwarePath}' — discover available monitoring APIs.
3. GetPlcWatchTables — softwarePath='{softwarePath}' — list available watch tables.
4. ReadPlcWatchTableCurrentValuesReadOnly — read current values.

IMPORTANT: this is read-only monitoring. Writing values to the PLC is not supported via MCP.";
        }

        // ──────────────────────────────────────────────────────────────────────
        // Validation / release
        // ──────────────────────────────────────────────────────────────────────

        [McpServerPrompt(Name = "RunPreRelease"), Description("Run offline validation suite before releasing a project")]
        public static string RunPreRelease(string workspaceRoot, string reportDirectory)
        {
            return $@"Run the offline pre-release validation suite.

Steps:
1. RunOfflineReleaseValidationSuite — workspaceRoot='{workspaceRoot}', reportDirectory='{reportDirectory}'.
2. BuildReleaseDiagnosticReport — pass the generated JSON path.
3. BuildReleaseRunbook — generate first-user instructions.
4. Review findings; fix any failures before shipping.

Use these tools in order.";
        }
    }
}
