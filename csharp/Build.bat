@echo off
REM ============================================================================
REM NT8 Python Adapter Build Script
REM ============================================================================
echo.
echo ========================================================================
echo  NinjaTrader 8 Python Adapter - Build Script
echo ========================================================================
echo.

echo [1/5] Using NinjaTrader references under C:\Program Files\NinjaTrader 8\bin
echo.

REM Try to find MSBuild
set MSBUILD_PATH=

REM VS 2022
if exist "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" (
    set MSBUILD_PATH=C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe
    echo [2/5] Found Visual Studio 2022 Community
    goto :found_msbuild
)

if exist "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe" (
    set MSBUILD_PATH=C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe
    echo [2/5] Found Visual Studio 2022 Professional
    goto :found_msbuild
)

if exist "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe" (
    set MSBUILD_PATH=C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe
    echo [2/5] Found Visual Studio 2022 Enterprise
    goto :found_msbuild
)

REM VS 2019
if exist "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe" (
    set MSBUILD_PATH=C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe
    echo [2/5] Found Visual Studio 2019 Community
    goto :found_msbuild
)

if exist "C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe" (
    set MSBUILD_PATH=C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe
    echo [2/5] Found Visual Studio 2019 Professional
    goto :found_msbuild
)

REM If not found, show error
echo.
echo ERROR: MSBuild not found!
echo.
echo Please install one of the following:
echo   - Visual Studio 2022 (Community/Professional/Enterprise)
echo   - Visual Studio 2019 (Community/Professional/Enterprise)
echo   - Visual Studio Build Tools
echo.
echo Download from: https://visualstudio.microsoft.com/downloads/
echo.
exit /b 1

:found_msbuild
echo.

REM Navigate to project directory
cd /d "%~dp0NT8PythonAdapter"

echo [3/5] Building NT8PythonAdapter (Release configuration)...
echo.

REM Build the project
"%MSBUILD_PATH%" NT8PythonAdapter.csproj /p:Configuration=Release /p:Platform=AnyCPU /t:Clean,Build /v:minimal

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ========================================================================
    echo  BUILD FAILED!
    echo ========================================================================
    echo.
    echo Please check the error messages above.
    echo Common issues:
    echo   1. Missing NinjaTrader DLL references - check paths in .csproj
    echo   2. .NET Framework 4.8 not installed
    echo   3. Code compilation errors
    echo.
    exit /b 1
)

echo.
echo [4/6] Build successful!
echo.

REM Check if DLL was created
if not exist "bin\Release\NT8PythonAdapter.dll" (
    echo ERROR: NT8PythonAdapter.dll was not created!
    exit /b 1
)

REM Copy to NinjaTrader AddOns folder (post-build should have done this)
echo [5/6] Copying DLL to NinjaTrader Custom folder...
if not exist "%USERPROFILE%\Documents\NinjaTrader 8\bin\Custom" (
    mkdir "%USERPROFILE%\Documents\NinjaTrader 8\bin\Custom"
)

copy /Y "bin\Release\NT8PythonAdapter.dll" "%USERPROFILE%\Documents\NinjaTrader 8\bin\Custom\NT8PythonAdapter.dll"

echo.
echo [6/6] Creating NinjaScript Export Package...
REM Update the strategy in the export folder
copy /Y "Strategies\NTPythonIndicatorExporter.cs" "NinjaScriptExport\Strategies\NTPythonIndicatorExporter.cs"

REM Create ZIP archive for NinjaTrader import
cd NinjaScriptExport
if exist "..\NT8PythonAdapter_NinjaScriptExport.zip" del "..\NT8PythonAdapter_NinjaScriptExport.zip"
powershell -Command "Compress-Archive -Path 'ExportDefinition.xml','AddOns','Strategies' -DestinationPath '..\NT8PythonAdapter_NinjaScriptExport.zip' -Force"
cd ..

echo.
echo ========================================================================
echo  BUILD COMPLETE!
echo ========================================================================
echo.
echo Output Files:
echo   DLL:    %USERPROFILE%\Documents\NinjaTrader 8\bin\Custom\NT8PythonAdapter.dll
echo   Export: %~dp0NT8PythonAdapter\NT8PythonAdapter_NinjaScriptExport.zip
echo.
echo ========================================================================
echo  INSTALLATION INSTRUCTIONS
echo ========================================================================
echo.
echo The Strategy requires import via NinjaTrader's NinjaScript Editor:
echo.
echo   1. Open NinjaTrader 8
echo   2. Go to: Tools -^> Import -^> NinjaScript Add-On...
echo   3. Browse to: %~dp0NT8PythonAdapter\NT8PythonAdapter_NinjaScriptExport.zip
echo   4. Click Open, then Import
echo   5. NinjaTrader will compile the Strategy automatically
echo   6. RESTART NinjaTrader 8
echo.
echo To use the Strategy:
echo   1. Open a Chart
echo   2. Right-click -^> Strategies -^> NTPythonIndicatorExporter
echo   3. Configure OutputDirectory if needed
echo   4. Click OK to enable
echo.
echo ========================================================================
echo.
