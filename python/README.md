# nt8sdk

[![Python 3.8+](https://img.shields.io/badge/python-3.8+-blue.svg)](https://www.python.org/downloads/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

High-performance Python SDK for building automated trading strategies and tooling on top of NinjaTrader 8. This package provides thin client abstractions for market data, account access, and order routing that integrate tightly with the NT8 C# bridge.

## Features

- **File-based ATI Protocol**: Robust communication with NinjaTrader 8 using the Automated Trading Interface
- **Modern Pythonic API**: Clean, async-compatible client for order management, market data, and risk controls
- **Multiple Client Implementations**: Choose between file-based, managed (.NET), or hybrid clients
- **Risk Management**: Built-in position sizing, risk limits, and breakeven management
- **Advanced Strategy Support**: Reference templates for bracket orders, break-even handling, and risk-managed entries
- **No External Dependencies**: Core functionality requires no additional packages

## Installation

Install from PyPI:

```bash
pip install nt8sdk
```

For .NET managed client support (optional):

```bash
pip install nt8sdk[managed]
```

## Quick Start

```python
from nt8 import NT8Client, OrderAction, OrderType

# Create client (uses file-based by default)
client = NT8Client()

# Connect to NinjaTrader
client.connect()

# Get account info
accounts = client.get_accounts()
print(f"Connected accounts: {accounts}")

# Place a market order
response = client.place_order(
    action=OrderAction.BUY,
    symbol="ES 12-25",
    order_type=OrderType.MARKET,
    quantity=1
)
print(f"Order placed: {response.order_id}")

# Get current positions
positions = client.get_positions()
for pos in positions.positions:
    print(f"{pos.instrument}: {pos.quantity} @ {pos.avg_price}")
```

## Client Types

Set the client implementation via environment variable:

```bash
# File-based (default, no dependencies)
export NT8_CLIENT_IMPL=file

# Managed .NET client (requires pythonnet)
export NT8_CLIENT_IMPL=managed
```

Or import directly:

```python
from nt8 import NT8FileClient, NT8ManagedClient, NT8HybridClient
```

## Examples

See the `examples/` directory for complete working examples:

- Basic order placement
- Market data streaming
- Risk-managed strategies
- Breakeven management

## Requirements

- Python 3.8+
- Windows (NinjaTrader 8 is Windows-only)
- NinjaTrader 8 with ATI enabled

## License

Released under the MIT License. See `LICENSE` at the repository root for full text.
