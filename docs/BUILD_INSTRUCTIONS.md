# Building the NT8 Python Adapter

## Quick Start (Easiest Method)

### Option 1: Using the Build Script (Recommended)

1. **Navigate to the csharp folder:**
   ```cmd
   cd C:\Users\Magwe\Work\Trading_Apps\packages\nt8-python-sdk\csharp
   ```

2. **Run the build script:**
   ```cmd
   Build.bat
   ```

3. **The script will:**
   - Check for NinjaTrader 8 installation
   - Find Visual Studio/MSBuild automatically
   - Build the project in Release mode
   - Copy the DLL to NinjaTrader's AddOns folder
   - Show you the results

4. **Restart NinjaTrader 8**

5. **Verify installation:**
   - Open Tools → Output Window in NT8
   - Look for: `"Python adapter waiting for connection on pipe: NT8PythonSDK"`

---

## Option 2: Using Visual Studio (Full Control)

### Prerequisites
- Visual Studio 2019 or 2022 (Community, Professional, or Enterprise)
- .NET Framework 4.8 SDK
- NinjaTrader 8 installed

### Steps

1. **Open the project in Visual Studio:**
   - Double-click: `NT8PythonAdapter\NT8PythonAdapter.csproj`
   - OR: Open Visual Studio → File → Open → Project/Solution → Select the .csproj file

2. **Verify NinjaTrader References:**
   - In Solution Explorer, expand "References"
   - Check that these show **without warning icons**:
     - NinjaTrader.Cbi
     - NinjaTrader.Core
     - NinjaTrader.Custom
     - NinjaTrader.Data
     - NinjaTrader.Gui

3. **If references are missing or broken:**
   - Right-click "References" → Add Reference
   - Click "Browse" button
   - Navigate to: `C:\Program Files\NinjaTrader 8\bin\`
   - Add the missing DLLs

4. **Select Release configuration:**
   - At the top of Visual Studio, change dropdown from "Debug" to **"Release"**
   - Platform should be "Any CPU"

5. **Build the project:**
   - Menu: Build → Build Solution
   - OR: Press `Ctrl+Shift+B`
   - OR: Right-click project → Build

6. **Check build output:**
   - Look at the Output window (View → Output)
   - Should see: `Build succeeded`
   - Location: `NT8PythonAdapter\bin\Release\NT8PythonAdapter.dll`

7. **Copy DLL to NinjaTrader:**
   The post-build event should automatically copy, but verify:
   ```
   Source: NT8PythonAdapter\bin\Release\NT8PythonAdapter.dll
   Target: %USERPROFILE%\Documents\NinjaTrader 8\bin\Custom\AddOns\NT8PythonAdapter.dll
   ```

   Manual copy if needed:
   ```cmd
   copy "NT8PythonAdapter\bin\Release\NT8PythonAdapter.dll" "%USERPROFILE%\Documents\NinjaTrader 8\bin\Custom\AddOns\"
   ```

8. **Restart NinjaTrader 8**

---

## Option 3: Using MSBuild Command Line

### Find MSBuild
```cmd
REM VS 2022
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"

REM VS 2019
"C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
```

### Build Command
```cmd
cd NT8PythonAdapter

"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" ^
  NT8PythonAdapter.csproj ^
  /p:Configuration=Release ^
  /p:Platform=AnyCPU ^
  /t:Clean,Build ^
  /v:minimal
```

### Copy DLL
```cmd
copy "bin\Release\NT8PythonAdapter.dll" "%USERPROFILE%\Documents\NinjaTrader 8\bin\Custom\AddOns\"
```

---

## Verification

### 1. Check DLL was created
```cmd
dir "%USERPROFILE%\Documents\NinjaTrader 8\bin\Custom\AddOns\NT8PythonAdapter.dll"
```

Expected size: ~40-60 KB

### 2. Restart NinjaTrader 8
**Important:** You MUST restart NT8 for the AddOn to load!

### 3. Check NT8 Output Window
1. In NinjaTrader, go to: **Tools → Output Window**
2. Look for these messages:
   ```
   Python adapter waiting for connection on pipe: NT8PythonSDK
   ```

If you see this, the adapter is running! ✅

### 4. Test with Python
```python
from nt8 import NT8Client

client = NT8Client()
if client.connect():
    print("✅ Connected to NT8 Adapter!")
    client.disconnect()
else:
    print("❌ Connection failed")
```

---

## Troubleshooting

### Build Errors

#### Error: "The type or namespace name 'NinjaTrader' could not be found"
**Solution:** NinjaTrader DLL references are missing or incorrect.
1. Check that NT8 is installed at `C:\Program Files\NinjaTrader 8\`
2. Update paths in `NT8PythonAdapter.csproj` lines 50-69 if NT8 is in a different location
3. In Visual Studio: Right-click References → Add Reference → Browse to NT8 DLLs

#### Error: "Could not load file or assembly 'NinjaTrader.Cbi'"
**Solution:**
1. Set `<Private>False</Private>` for all NT8 references in .csproj
2. Rebuild the project

#### Error: "The target framework version v4.8 is not installed"
**Solution:**
1. Download .NET Framework 4.8 Developer Pack
2. Install from: https://dotnet.microsoft.com/download/dotnet-framework/net48

#### Error: "MSBuild is not recognized"
**Solution:**
1. Install Visual Studio 2019 or 2022
2. OR install Visual Studio Build Tools
3. Make sure to include ".NET desktop build tools" workload

### Runtime Errors

#### Adapter doesn't appear in NT8 Output Window
**Solutions:**
1. Verify DLL is in correct location:
   ```
   %USERPROFILE%\Documents\NinjaTrader 8\bin\Custom\AddOns\NT8PythonAdapter.dll
   ```

2. Check NT8 version compatibility (should be NT8, not NT7)

3. Enable AddOns in NT8:
   - Tools → Options → NinjaScript
   - Check "Enable AddOns"
   - Restart NT8

4. Check for errors in NT8 Log:
   - Tools → Log
   - Look for AddOn loading errors

#### "Python client disconnected" immediately
**Solutions:**
1. Check Windows Firewall isn't blocking Named Pipes
2. Verify Python client is using correct pipe name: "NT8PythonSDK"
3. Make sure only ONE instance of NT8 is running

#### Orders not executing
**Solutions:**
1. Check account is connected in NT8
2. Verify account name in adapter (default: "Sim101")
3. Check NT8 Output Window for error messages
4. Ensure instrument is correctly formatted: "ES 03-25" (with space)

---

## Development Build (Debug Mode)

For development and debugging:

```cmd
MSBuild.exe NT8PythonAdapter.csproj /p:Configuration=Debug
```

Debug DLL location: `bin\Debug\NT8PythonAdapter.dll`

**Debugging:**
1. Build in Debug mode
2. In Visual Studio: Debug → Attach to Process
3. Select `NinjaTrader.exe`
4. Set breakpoints in your code
5. Trigger the code from Python

---

## Files Included in Build

The build compiles these source files:

| File | Purpose |
|------|---------|
| NT8PythonAdapter_Enhanced.cs | Main adapter |
| BinaryProtocolHelper.cs | Binary protocol encoding/decoding |
| OrderManager.cs | Order execution and tracking |
| MarketDataManager.cs | Market data streaming |
| AccountDataManager.cs | Account balance tracking |
| MessageQueue.cs | Thread-safe message queue |

**Total:** ~1,950 lines of C# code

---

## Post-Build Verification Checklist

- [ ] DLL created successfully (check bin\Release\)
- [ ] DLL copied to NT8 AddOns folder
- [ ] NinjaTrader 8 restarted
- [ ] "Python adapter waiting..." message in Output Window
- [ ] Python client can connect
- [ ] No errors in NT8 Log

---

## Updating the Adapter

When you make changes to the code:

1. **Make your changes** in Visual Studio
2. **Build** (Ctrl+Shift+B)
3. **Close NinjaTrader** (important!)
4. **Copy new DLL** to AddOns folder (or let post-build do it)
5. **Restart NinjaTrader**
6. **Test changes**

**Note:** You MUST close NT8 before replacing the DLL, as it locks the file when loaded.

---

## Build Configurations

### Release (Production)
- Optimized code
- No debug symbols
- Smaller DLL
- **Use this for trading**

### Debug (Development)
- Full debug symbols
- No optimizations
- Can attach debugger
- **Use this for development**

---

## Advanced: Custom Build Locations

To change where the DLL is copied, edit the `PostBuildEvent` in `NT8PythonAdapter.csproj`:

```xml
<PostBuildEvent>
  copy "$(TargetPath)" "C:\Your\Custom\Path\$(TargetFileName)" /Y
</PostBuildEvent>
```

---

## Getting Help

If you encounter issues:

1. **Check build output** in Visual Studio for specific errors
2. **Check NT8 Log** (Tools → Log) for runtime errors
3. **Check NT8 Output Window** for adapter messages
4. **Run Build.bat** with verbose output:
   ```cmd
   MSBuild.exe ... /v:detailed
   ```

---

## Success Indicators

You know the build was successful when:

✅ Build completes with 0 errors
✅ DLL file exists in bin\Release\
✅ DLL copied to NT8 AddOns folder
✅ NT8 shows "Python adapter waiting..." message
✅ Python client connects successfully
✅ Can place test orders

---

**Ready to build? Run `Build.bat` now!** 🚀
