nt8-python-sdk/
├── README.md ⭐ (Updated with test instructions)
├── LICENSE
├── .gitignore ✨ (New)
│
├── python/
│   ├── nt8sdk/
│   │   ├── __init__.py
│   │   ├── client.py ✅ (Fixed enum handling)
│   │   ├── types.py ✅ (Fixed MarketDataType)
│   │   ├── protocol.py ✅ (Fixed return types)
│   │   ├── orders.py
│   │   └── market_data.py
│   │
│   ├── examples/
│   │   ├── simple_strategy.py ✅ (Fixed callbacks)
│   │   ├── market_data_stream.py ✅ (Improved)
│   │   └── advanced_strategy.py ✨ (New!)
│   │
│   ├── tests/ ✨ (New!)
│   │   ├── __init__.py
│   │   ├── test_connection.py
│   │   └── test_orders.py
│   │
│   ├── setup.py
│   └── requirements.txt
│
├── csharp/
│   ├── README.md ✨ (New!)
│   └── NT8PythonAdapter/
│       ├── NT8PythonAdapter.cs ✅ (Major fixes)
│       └── NT8PythonAdapter.csproj
│
└── docs/
    ├── installation.md
    ├── quickstart.md
    ├── api_reference.md ✨ (New!)
    └── performance_tuning.md ✨ (New!)