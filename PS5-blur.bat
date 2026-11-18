@echo off
chcp 65001 >nul
cd /d "%~dp0"
if not exist "PS5Stream.exe" (
    echo Building PS5Stream.exe...
    "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /optimize /win32manifest:ps5stream.manifest /out:PS5Stream.exe PS5Stream.cs || (pause & exit /b 1)
)
start "" "PS5Stream.exe" blur
