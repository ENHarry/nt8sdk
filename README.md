# NinjaTrader 8 Python SDK

A robust Python SDK for NinjaTrader 8 using file-based communication with ATI protocol support.

## Overview

This SDK provides a complete solution for interfacing Python applications with NinjaTrader 8, enabling automated trading, market data streaming, and account management through a file-based communication system.

## Architecture

The SDK consists of two main components:

1. **C# AddOn** (`NT8PythonAdapter.dll`) - Runs inside NinjaTrader 8
2. **Python Client** (`nt8` package) - Provides Python API for communication

### Communication Method

- **File-based**: Uses dedicated directories for command/response exchange
- **ATI Protocol**: Compatible with NinjaTrader's Automated Trading Interface format
- **Private Channels**: Uses `incoming/python` and `outgoing/python` subdirectories to avoid conflicts

## Installation

### 1. Deploy the C# AddOn

```powershell
# Build the C# adapter
cd csharp
.\Build.bat

# Copy to NinjaTrader (note: Custom directory, NOT AddOns subdirectory)
copy NT8PythonAdapter\bin\Release\NT8PythonAdapter.dll "C:\Users\%USERNAME%\Documents\NinjaTrader 8\bin\Custom\"
```

### 2. Install Python Package

```bash
cd python
pip install -e .
```

### 3. Restart NinjaTrader

After copying the DLL, restart NinjaTrader 8. The adapter will create these directories automatically:
- `Documents\NinjaTrader 8\incoming\python\`
- `Documents\NinjaTrader 8\outgoing\python\`

## Quick Start

```python
from nt8 import NT8Client

# Initialize client
client = NT8Client()

# Test connection
response = client.ping()
print(f"Connection: {response}")  # Should print "PONG"

# Get adapter status
status = client.get_status()
print(f"Status: {status}")

# Get account information (requires connected broker)
account_info = client.get_account_info()
print(f"Account: {account_info}")
```

## Features

### Core Communication
- ✅ PING/PONG connectivity testing
- ✅ Status monitoring
- ✅ Account information retrieval
- ✅ ATI protocol compliance (13-field format)

### Trading Operations
- 📋 Order placement and management
- 📋 Position monitoring
- 📋 Account balance tracking
- 📋 Risk management tools

### Market Data
- 📋 Real-time tick data
- 📋 Market depth (Level II)
- 📋 Historical data access

### Advanced Features
- 📋 Breakeven management
- 📋 Advanced strategy components
- 📋 Risk management systems

## Testing

Run the connection test to verify everything is working:

```bash
python python\test_connection_ati.py
```

Expected output:
```
NT8 Python Adapter - File-Based Connection Test
===============================================

[Step 1] Checking directories...
✓ Directories exist

[Step 2] Testing PING command...
✓ Response received: PONG
✓ PING test PASSED!

[Step 3] Testing STATUS command...
✓ Response: OK|Running XX:XX:XX|Commands: 1|Account: None
✓ STATUS test PASSED!

[Step 4] Testing ACCOUNT_INFO command...
✓ Response: ERROR|No account connected (expected if no broker connected)

Test Summary:
- File-based communication is working
- NT8PythonAdapter is responding to commands
- Ready for trading operations
```

## Project Structure

```
nt8-python-sdk/
├── csharp/                          # C# AddOn source
│   ├── NT8PythonAdapter/
│   │   ├── NT8PythonAdapter_FileBased.cs  # Main adapter implementation
│   │   └── NT8PythonAdapter.csproj   # Project file
│   └── Build.bat                     # Build script
├── python/                          # Python SDK
│   ├── nt8/
│   │   ├── client_filebased.py      # Main client implementation
│   │   ├── types.py                 # Type definitions
│   │   ├── orders.py                # Order management
│   │   ├── market_data.py           # Market data handling
│   │   ├── account.py               # Account management
│   │   ├── risk_management.py       # Risk management tools
│   │   └── advanced_strategy.py     # Strategy components
│   ├── examples/                    # Example scripts
│   ├── tests/                       # Test files
│   └── setup.py                     # Package setup
└── docs/                           # Documentation
```

## API Reference

### NT8Client

The main client class for communicating with NinjaTrader 8.

```python
client = NT8Client()
```

#### Core Methods

- `ping()` - Test connection (returns "PONG")
- `get_status()` - Get adapter runtime status  
- `get_account_info()` - Get connected account information

#### Configuration

The client automatically detects NinjaTrader directories. Communication files use the pattern:
- Outgoing: `oif_{timestamp}_{uuid}.txt`
- Response timeout: 5 seconds default

## Troubleshooting

### Common Issues

1. **"Directories not created"**
   - Ensure DLL is in `Documents\NinjaTrader 8\bin\Custom\` (NOT in AddOns subdirectory)
   - Restart NinjaTrader after copying DLL

2. **"No response to commands"**
   - Check NinjaTrader log for adapter startup messages
   - Verify directories exist in `Documents\NinjaTrader 8\incoming\python\`

3. **"Account errors"**
   - Connect a broker account in NinjaTrader first
   - Ensure account is connected and active

### Build Issues

If compilation fails:
- Verify Visual Studio 2022 Community is installed
- Check that NinjaTrader 8 is installed (for assembly references)
- Run build as Administrator if needed

## Version History

### v1.1.0 (Current)
- ✅ File-based communication working
- ✅ ATI protocol compliance
- ✅ Basic connectivity (PING/STATUS/ACCOUNT_INFO)
- ✅ Private communication channels

### v1.0.0 (Legacy)
- Named pipe implementation (deprecated)

## License

See LICENSE file for details.

## Support

For issues and questions:
1. Check the troubleshooting section above
2. Review the test output for specific error messages
3. Check NinjaTrader logs for adapter-related messages