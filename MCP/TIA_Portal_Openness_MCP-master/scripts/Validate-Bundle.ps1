#Requires -Version 5.1
<#
.SYNOPSIS
    Offline validation: required files, JSON parse, blueprint bundle list, tool roster count.
.DESCRIPTION
    Run from any directory. Default bundle root = parent of this script's folder (the delivery package root).
    Does not start TiaMcpServer or TIA Portal.
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Validate-Bundle.ps1
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Validate-Bundle.ps1 -BundleRoot "D:\kits\TIA_MCP_交付包"
#>
param(
    [Parameter(Mandatory = $false)]
    [string]$BundleRoot = "",
    [switch]$Strict
)

$ErrorActionPreference = "Stop"

function Resolve-BundleRoot {
    if ($BundleRoot -and (Test-Path -LiteralPath $BundleRoot)) {
        return (Resolve-Path -LiteralPath $BundleRoot).Path
    }
    return (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
}

$root = Resolve-BundleRoot
$failures = New-Object System.Collections.Generic.List[string]

function Fail([string]$msg) {
    [void]$failures.Add($msg)
    Write-Host "[FAIL] $msg" -ForegroundColor Red
}

function Ok([string]$msg) {
    Write-Host "[ OK ] $msg" -ForegroundColor Green
}

Write-Host "Bundle root: $root"

$exe = Join-Path $root "tools\tiaportal-mcp\src\TiaMcpServer\bin\Release\net48\TiaMcpServer.exe"
if (-not (Test-Path -LiteralPath $exe)) { Fail "Missing server exe: $exe" } else { Ok "TiaMcpServer.exe present" }

$readme = Join-Path $root "README.md"
if (-not (Test-Path -LiteralPath $readme)) { Fail "Missing README.md" } else { Ok "README.md present" }

$skill = Join-Path $root "tools\tiaportal-mcp\skill\SKILL.md"
if (-not (Test-Path -LiteralPath $skill)) { Fail "Missing SKILL.md" } else { Ok "SKILL.md present" }

$blueprintPath = Join-Path $root "templates\project-blueprints\full_plc_hmi_project.json"
if (-not (Test-Path -LiteralPath $blueprintPath)) {
    Fail "Missing blueprint JSON"
}
else {
    try {
        $blueprint = Get-Content -LiteralPath $blueprintPath -Raw -Encoding UTF8 | ConvertFrom-Json
        Ok "Blueprint JSON parses"
        if ($blueprint.requiredBundleFiles) {
            foreach ($rel in $blueprint.requiredBundleFiles) {
                $p = Join-Path $root ($rel -replace "/", [IO.Path]::DirectorySeparatorChar)
                if (-not (Test-Path -LiteralPath $p)) {
                    Fail "Blueprint requiredBundleFiles missing: $rel"
                }
            }
            Ok ("Blueprint requiredBundleFiles all exist ({0} paths)" -f $blueprint.requiredBundleFiles.Count)
        }
    }
    catch {
        Fail ("Blueprint JSON invalid: " + $_.Exception.Message)
    }
}

$manifestPath = Join-Path $root "manifest\package-manifest.json"
if (Test-Path -LiteralPath $manifestPath) {
    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
        Ok "package-manifest.json parses"
        $expectedTools = $manifest.capabilities.mcpToolCount
        $toolsPath = Join-Path $root "manifest\tools-list.json"
        if (Test-Path -LiteralPath $toolsPath) {
            $toolsDoc = Get-Content -LiteralPath $toolsPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $n = @($toolsDoc.tools).Count
            if ($expectedTools -and ($n -ne $expectedTools)) {
                $msg = "tools-list tool count ($n) != manifest mcpToolCount ($expectedTools)"
                if ($Strict) { Fail $msg } else { Write-Host "[WARN] $msg" -ForegroundColor Yellow }
            }
            else {
                Ok ("tools-list count matches manifest ({0})" -f $n)
            }
        }
    }
    catch {
        Fail ("manifest JSON invalid: " + $_.Exception.Message)
    }
}
else {
    Fail "Missing manifest\package-manifest.json"
}

$plcJsonDir = Join-Path $root "templates\plc\plcbuild-json"
if (Test-Path -LiteralPath $plcJsonDir) {
    Get-ChildItem -LiteralPath $plcJsonDir -Filter "*.json" -File | ForEach-Object {
        try {
            $null = Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        }
        catch {
            Fail ("plcbuild-json invalid: $($_.Name) — " + $_.Exception.Message)
        }
    }
    Ok ("All plcbuild-json files parse ({0} files)" -f @((Get-ChildItem -LiteralPath $plcJsonDir -Filter "*.json" -File)).Count)
}

$hmiDir = Join-Path $root "templates\hmi"
if (Test-Path -LiteralPath $hmiDir) {
    Get-ChildItem -LiteralPath $hmiDir -Filter "*.json" -File | ForEach-Object {
        try {
            $null = Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        }
        catch {
            Fail ("HMI template JSON invalid: $($_.Name) — " + $_.Exception.Message)
        }
    }
    Ok ("All templates/hmi JSON files parse ({0} files)" -f @((Get-ChildItem -LiteralPath $hmiDir -Filter "*.json" -File)).Count)
}

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "Validation FAILED ($($failures.Count) issue(s))." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Validation PASSED." -ForegroundColor Green
exit 0
