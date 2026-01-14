@echo off
cd /d "%~dp0"
echo Building NTPythonIndicatorExporter Strategy...

set MSBUILD_PATH=C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe

"%MSBUILD_PATH%" NTPythonIndicatorExporter.csproj /p:Configuration=Release /p:Platform=x64 /t:Clean,Build /v:minimal

if %ERRORLEVEL% NEQ 0 (
    echo Build FAILED!
    exit /b 1
)

echo.
echo Build successful!
echo DLL copied to: %USERPROFILE%\Documents\NinjaTrader 8\bin\Custom\
