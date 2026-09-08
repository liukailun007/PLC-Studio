@echo off
REM ============================================================
REM  TIA ProjectContext one-click refresh (double-click this file)
REM  1) Change PROJECT to your .ap21 absolute path if different.
REM  2) Double click -> scans (read-only) -> writes
REM     repo\out\project-context\maps.json
REM  No TIA write-back. Requires TIA V21 + Siemens TIA Openness group.
REM ============================================================
setlocal

set "PROJECT=C:\Users\Administrator\Desktop\项目3\项目3.ap21"
set "LOGIC=--with-logic"

cd /d "%~dp0..\.."

echo [refresh] project: %PROJECT%
echo [refresh] scanning (read-only, ~20-60s)...
node scripts\tia-project-scanner\scan.mjs --project "%PROJECT%" %LOGIC%
set RC=%ERRORLEVEL%
echo.
if %RC%==0 (
    echo [OK] maps refreshed: %CD%\out\project-context\maps.json
    echo      query e.g.:  node scripts\tia-project-scanner\query.mjs index
) else (
    echo [FAIL] scan did not finish. Check: TIA V21 installed/Openness group/path.
)
echo.
pause
endlocal
