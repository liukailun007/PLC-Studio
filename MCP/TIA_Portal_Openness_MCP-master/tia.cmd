@echo off
chcp 65001 >nul
rem `tia` 命令入口（V21）。把本交付包根目录加入 PATH 后，可直接：tia gen spec.yaml
rem V20 用户请改用同目录的 tia-v20.cmd。所有参数原样透传给引擎 exe。
"%~dp0tools\tiaportal-mcp\src\TiaMcpServer\bin\Release\net48\TiaMcpServer.exe" %*
exit /b %ERRORLEVEL%
