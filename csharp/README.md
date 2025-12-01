# NT8 Python Adapter - C# Component

This is the C# NinjaScript AddOn that bridges Python with NinjaTrader 8.

## Building

1. **Prerequisites**:
   - Visual Studio 2019 or later
   - .NET Framework 4.8 SDK
   - NinjaTrader 8 installed

2. **Add NinjaTrader References**:
   - Open project in Visual Studio
   - Right-click References → Add Reference → Browse
   - Navigate to `C:\\Program Files\\NinjaTrader 8\\bin\\`
   - Add these DLLs:
     - NinjaTrader.Core.dll
     - NinjaTrader.Cbi.dll  
     - NinjaTrader.Data.dll
     - NinjaTrader.Gui.dll

3. **Build**:
   - Set configuration to **Release**
   - Build → Build Solution (Ctrl+Shift+B)

4. **Deploy**:
   - Copy `bin/Release/NT8PythonAdapter.dll` to:
     `%USERPROFILE%\\Documents\\NinjaTrader 8\\bin\\Custom\\AddOns\\`

5. **Restart NinjaTrader** to load the AddOn

## Debugging

For debugging with Visual Studio:
1. Tools → Options → Debugging
2. Uncheck "Enable Just My Code"
3. Debug → Attach to Process → NinjaTrader.exe

## Troubleshooting

**"Could not load file or assembly"**
- Verify all NinjaTrader DLL references point to correct installation
- Ensure targeting .NET Framework 4.8 (not .NET Core)

**AddOn not appearing in NT8**
- Check DLL is in correct AddOns folder
- Verify no compilation errors
- Check NT8 Log tab for errors

**Pipe connection fails**
- Ensure AddOn is started (appears in Tools → AddOns menu)
- Check Windows Firewall isn't blocking named pipes
- Verify pipe name matches Python client