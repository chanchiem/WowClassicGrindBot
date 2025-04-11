@echo off
set autoStart=true
REM #start "C:\Program Files (x86)\Google\Chrome\Application\chrome.exe" "http://localhost:5000"
cd /D "%~dp0"

set profilePath=..\class\Mage_SOD_Farm.json

if /I "%autoStart%"=="true" (
    echo AutoStart enabled. Starting bot with profile: %profilePath%
    dotnet run --configuration Release --autostart --profile "%profilePath%"
) else (
    echo AutoStart not enabled or variable not set. Starting normally.
    dotnet run --configuration Release
)

pause
