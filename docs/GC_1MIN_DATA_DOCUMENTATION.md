# GC Continuous 1-Minute Data Documentation

## File Information
- **Path**: `C:\Users\Magwe\Work\Trading_Apps\data_collector\data\GC\gc_continuous\gc_continuous_1min.parquet`
- **Total Rows**: 570,060
- **Total Columns**: 175
- **Date Range**: 2024-09-30 20:00:00 UTC to 2025-10-31 16:59:00 UTC
- **File Format**: Apache Parquet (1 row group)

---

## Column Categories Overview

| Category | Column Count | Description |
|----------|--------------|-------------|
| OHLCV & Timestamp | 6 | Core price/volume bars |
| BBO & Microprice | 12 | Best Bid/Offer metrics |
| Volume & Imbalance | 21 | Volume flow and imbalance features |
| Order Flow Dynamics | 37 | Order book flow metrics |
| MBO Action Metrics | 37 | Market-by-Order action tracking |
| Fake Order Detection | 11 | Spoofing/fake order indicators |
| Momentum & Acceleration | 8 | Price momentum features |
| Volatility & Range | 15 | Volatility and range metrics |
| Statistical | 6 | Skewness and kurtosis |
| VWAP | 6 | Volume-weighted average price |
| Open Interest Proxy | 7 | OI approximation metrics |
| Volume Rolling | 9 | Rolling volume statistics |

---

## Complete Column Reference

### 1. OHLCV & Timestamp (6 columns)

| Column | Data Type | Description |
|--------|-----------|-------------|
| `ts_event` | datetime64[ns, UTC] | Bar timestamp in UTC timezone |
| `open` | float32 | Opening price of the 1-minute bar |
| `high` | float32 | Highest price during the bar |
| `low` | float32 | Lowest price during the bar |
| `close` | float32 | Closing price of the 1-minute bar |
| `volume` | int32 | Total volume (contracts traded) |

---

### 2. BBO & Microprice (12 columns)

Best Bid/Offer metrics for market microstructure analysis.

| Column | Data Type | Description |
|--------|-----------|-------------|
| `best_bid` | float32 | Best bid price at bar close |
| `best_ask` | float32 | Best ask price at bar close |
| `bbo_spread` | float32 | Bid-ask spread (ask - bid) |
| `bbo_mid` | float32 | Mid price: (bid + ask) / 2 |
| `bbo_imb` | float32 | BBO imbalance: (close - mid) / spread |
| `bbo_imb_mean_10` | float32 | 10-bar rolling mean of BBO imbalance |
| `bbo_imb_mean_20` | float32 | 20-bar rolling mean of BBO imbalance |
| `bbo_imb_mean_50` | float32 | 50-bar rolling mean of BBO imbalance |
| `bbo_imb_std_10` | float32 | 10-bar rolling std of BBO imbalance |
| `bbo_imb_std_20` | float32 | 20-bar rolling std of BBO imbalance |
| `bbo_imb_std_50` | float32 | 50-bar rolling std of BBO imbalance |
| `microprice` | float32 | Volume-weighted BBO price |

**Microprice Formula**: `(bid * ask_vol + ask * bid_vol) / (bid_vol + ask_vol)`

---

### 3. Volume & Order Imbalance Metrics (21 columns)

| Column | Data Type | Description |
|--------|-----------|-------------|
| `buy_volume` | int32 | Volume from buy-side aggression |
| `sell_volume` | int16 | Volume from sell-side aggression |
| `signed_volume` | float32 | Net directional volume (buy - sell) |
| `cvd` | float32 | Cumulative Volume Delta (running sum of signed_volume) |
| `cvd_momentum_10` | float32 | 10-bar CVD change |
| `cvd_momentum_20` | float32 | 20-bar CVD change |
| `cvd_momentum_50` | float32 | 50-bar CVD change |
| `cvd_momentum_100` | float32 | 100-bar CVD change |
| `aggr_imb` | float32 | Aggressive imbalance: (buy - sell) / (buy + sell) |
| `aggr_imb_mean_10` | float32 | 10-bar rolling mean |
| `aggr_imb_mean_20` | float32 | 20-bar rolling mean |
| `aggr_imb_mean_50` | float32 | 50-bar rolling mean |
| `aggr_imb_mean_100` | float32 | 100-bar rolling mean |
| `aggr_imb_std_10` | float32 | 10-bar rolling std |
| `aggr_imb_std_20` | float32 | 20-bar rolling std |
| `aggr_imb_std_50` | float32 | 50-bar rolling std |
| `aggr_imb_std_100` | float32 | 100-bar rolling std |
| `body_ratio` | float32 | Candle body / total range ratio |
| `returns` | float32 | Simple returns: (close - prev_close) / prev_close |
| `log_returns` | float32 | Log returns: ln(close / prev_close) |
| `price_change` | float32 | Absolute price change: close - prev_close |

---

### 4. Order Flow Dynamics (37 columns)

Metrics capturing order book flow and dynamics.

| Column | Data Type | Description |
|--------|-----------|-------------|
| `net_order_flow` | int32 | Net orders: add_count - delete_count |
| `net_order_flow_10` | float32 | 10-bar rolling sum |
| `net_order_flow_20` | float32 | 20-bar rolling sum |
| `net_order_flow_50` | float32 | 50-bar rolling sum |
| `net_order_flow_100` | float32 | 100-bar rolling sum |
| `net_volume_flow` | int32 | Net volume: add_volume - delete_volume |
| `net_volume_flow_10` | float32 | 10-bar rolling sum |
| `net_volume_flow_20` | float32 | 20-bar rolling sum |
| `net_volume_flow_50` | float32 | 50-bar rolling sum |
| `order_flow_10` | float32 | 10-bar rolling signed volume |
| `order_flow_20` | float32 | 20-bar rolling signed volume |
| `order_flow_50` | float32 | 50-bar rolling signed volume |
| `order_flow_100` | float32 | 100-bar rolling signed volume |
| `order_price_change` | float32 | Mean order price change (from order modifications) |
| `order_price_change_mean_10` | float32 | 10-bar rolling mean |
| `order_price_change_mean_20` | float32 | 20-bar rolling mean |
| `order_price_change_mean_50` | float32 | 50-bar rolling mean |
| `order_price_change_std_10` | float32 | 10-bar rolling std |
| `order_price_change_std_20` | float32 | 20-bar rolling std |
| `order_price_change_std_50` | float32 | 50-bar rolling std |
| `order_price_volatility` | float32 | Absolute order price change |
| `order_size_change` | int8 | Net order size change |
| `order_size_change_sum_10` | float32 | 10-bar rolling sum |
| `order_size_change_sum_20` | float32 | 20-bar rolling sum |
| `order_size_change_sum_50` | float32 | 50-bar rolling sum |
| `order_size_pressure` | int8 | Order size pressure indicator |
| `order_size_pressure_10` | float32 | 10-bar rolling mean |
| `order_size_pressure_20` | float32 | 20-bar rolling mean |
| `order_size_pressure_50` | float32 | 50-bar rolling mean |
| `order_stability` | float32 | Modify count / add count ratio |
| `order_stability_mean_20` | float32 | 20-bar rolling mean |
| `order_stability_mean_50` | float32 | 50-bar rolling mean |
| `order_stability_mean_100` | float32 | 100-bar rolling mean |
| `order_turnover` | float32 | (add + delete) / total actions ratio |
| `order_turnover_mean_20` | float32 | 20-bar rolling mean |
| `order_turnover_mean_50` | float32 | 50-bar rolling mean |
| `order_turnover_mean_100` | float32 | 100-bar rolling mean |

---

### 5. MBO Action Metrics (37 columns)

Market-by-Order (MBO) action tracking from Level 3 data.

#### Action Counts
| Column | Data Type | Description |
|--------|-----------|-------------|
| `add_count` | int32 | Number of order additions |
| `add_count_sum_10` | float32 | 10-bar rolling sum |
| `add_count_sum_20` | float32 | 20-bar rolling sum |
| `add_count_sum_50` | float32 | 50-bar rolling sum |
| `add_count_sum_100` | float32 | 100-bar rolling sum |
| `modify_count` | int32 | Number of order modifications |
| `modify_count_sum_10` | float32 | 10-bar rolling sum |
| `modify_count_sum_20` | float32 | 20-bar rolling sum |
| `modify_count_sum_50` | float32 | 50-bar rolling sum |
| `modify_count_sum_100` | float32 | 100-bar rolling sum |
| `delete_count` | int32 | Number of order deletions/cancellations |
| `delete_count_sum_10` | float32 | 10-bar rolling sum |
| `delete_count_sum_20` | float32 | 20-bar rolling sum |
| `delete_count_sum_50` | float32 | 50-bar rolling sum |
| `delete_count_sum_100` | float32 | 100-bar rolling sum |
| `fill_count` | int32 | Number of order fills |
| `trade_count` | int32 | Number of trades |

#### Action Volumes
| Column | Data Type | Description |
|--------|-----------|-------------|
| `add_volume` | int32 | Volume added to order book |
| `add_volume_sum_10` | float32 | 10-bar rolling sum |
| `add_volume_sum_20` | float32 | 20-bar rolling sum |
| `add_volume_sum_50` | float32 | 50-bar rolling sum |
| `modify_volume` | int16 | Volume modified |
| `delete_volume` | int16 | Volume deleted/cancelled |
| `delete_volume_sum_10` | float32 | 10-bar rolling sum |
| `delete_volume_sum_20` | float32 | 20-bar rolling sum |
| `delete_volume_sum_50` | float32 | 50-bar rolling sum |
| `fill_volume` | int16 | Volume filled |
| `trade_volume` | int16 | Volume traded |

#### Action Ratios
| Column | Data Type | Description |
|--------|-----------|-------------|
| `add_ratio` | float32 | Add count / total actions |
| `modify_ratio` | float32 | Modify count / total actions |
| `delete_ratio` | float32 | Delete count / total actions |
| `fill_ratio` | float32 | Fill count / total actions |
| `trade_ratio` | float32 | Trade count / total actions |
| `cancel_ratio` | float32 | Delete count / add count |
| `cancel_ratio_mean_20` | float32 | 20-bar rolling mean |
| `cancel_ratio_mean_50` | float32 | 50-bar rolling mean |
| `cancel_ratio_mean_100` | float32 | 100-bar rolling mean |

---

### 6. Fake Order Detection (11 columns)

Spoofing and fake order detection metrics.

| Column | Data Type | Description |
|--------|-----------|-------------|
| `fake_order_count` | int8 | Count of detected fake orders |
| `fake_order_count_sum_10` | float32 | 10-bar rolling sum |
| `fake_order_count_sum_20` | float32 | 20-bar rolling sum |
| `fake_order_count_sum_50` | float32 | 50-bar rolling sum |
| `fake_order_count_sum_100` | float32 | 100-bar rolling sum |
| `fake_order_ratio` | float32 | Fake orders / volume |
| `fake_order_ratio_mean_10` | float32 | 10-bar rolling mean |
| `fake_order_ratio_mean_20` | float32 | 20-bar rolling mean |
| `fake_order_ratio_mean_50` | float32 | 50-bar rolling mean |
| `fake_order_ratio_mean_100` | float32 | 100-bar rolling mean |
| `cumulative_fake_orders` | float32 | Running total of fake orders |

**Detection Method**: Orders outside median ± 10 * MAD (Median Absolute Deviation) per instrument.

---

### 7. Momentum & Acceleration (8 columns)

| Column | Data Type | Description |
|--------|-----------|-------------|
| `momentum_5` | float32 | 5-bar price change |
| `momentum_10` | float32 | 10-bar price change |
| `momentum_20` | float32 | 20-bar price change |
| `momentum_50` | float32 | 50-bar price change |
| `price_acceleration_5` | float32 | 5-bar momentum change (2nd derivative) |
| `price_acceleration_10` | float32 | 10-bar momentum change |
| `price_acceleration_20` | float32 | 20-bar momentum change |
| `price_acceleration_50` | float32 | 50-bar momentum change |

---

### 8. Volatility & Range (15 columns)

| Column | Data Type | Description |
|--------|-----------|-------------|
| `trading_range` | float32 | High - Low |
| `trading_range_mean_10` | float32 | 10-bar rolling mean |
| `trading_range_mean_20` | float32 | 20-bar rolling mean |
| `trading_range_mean_50` | float32 | 50-bar rolling mean |
| `trading_range_mean_100` | float32 | 100-bar rolling mean |
| `range_position` | float32 | (close - low) / range (0-1 normalized) |
| `range_expansion_10` | float32 | Current range / 10-bar mean range |
| `range_expansion_20` | float32 | Current range / 20-bar mean range |
| `range_expansion_50` | float32 | Current range / 50-bar mean range |
| `range_expansion_100` | float32 | Current range / 100-bar mean range |
| `true_range` | float32 | max(H-L, |H-prev_C|, |L-prev_C|) |
| `volatility_5` | float32 | 5-bar rolling std of returns |
| `volatility_10` | float32 | 10-bar rolling std of returns |
| `volatility_20` | float32 | 20-bar rolling std of returns |
| `volatility_50` | float32 | 50-bar rolling std of returns |

---

### 9. Statistical Features (6 columns)

Higher-order statistical moments of returns.

| Column | Data Type | Description |
|--------|-----------|-------------|
| `skewness_20` | float32 | 20-bar rolling skewness of returns |
| `skewness_50` | float32 | 50-bar rolling skewness |
| `skewness_100` | float32 | 100-bar rolling skewness |
| `kurtosis_20` | float32 | 20-bar rolling kurtosis of returns |
| `kurtosis_50` | float32 | 50-bar rolling kurtosis |
| `kurtosis_100` | float32 | 100-bar rolling kurtosis |

---

### 10. VWAP Features (6 columns)

Volume-Weighted Average Price metrics.

| Column | Data Type | Description |
|--------|-----------|-------------|
| `vwap_20` | float32 | 20-bar VWAP |
| `vwap_50` | float32 | 50-bar VWAP |
| `vwap_100` | float32 | 100-bar VWAP |
| `price_vwap_ratio_20` | float32 | close / vwap_20 |
| `price_vwap_ratio_50` | float32 | close / vwap_50 |
| `price_vwap_ratio_100` | float32 | close / vwap_100 |

---

### 11. Open Interest Proxy (7 columns)

Approximate open interest metrics (cumulative volume-based).

| Column | Data Type | Description |
|--------|-----------|-------------|
| `oi_proxy` | float32 | Cumulative volume as OI proxy |
| `oi_change_100` | float32 | 100-bar OI change |
| `oi_change_500` | float32 | 500-bar OI change |
| `oi_change_1000` | float32 | 1000-bar OI change |
| `oi_pct_change_100` | float32 | 100-bar OI percent change |
| `oi_pct_change_500` | float32 | 500-bar OI percent change |
| `oi_pct_change_1000` | float32 | 1000-bar OI percent change |

---

### 12. Volume Rolling Features (9 columns)

| Column | Data Type | Description |
|--------|-----------|-------------|
| `volume_sma_10` | float32 | 10-bar simple moving average |
| `volume_sma_20` | float32 | 20-bar SMA |
| `volume_sma_50` | float32 | 50-bar SMA |
| `volume_sma_100` | float32 | 100-bar SMA |
| `volume_ratio_10` | float32 | volume / volume_sma_10 |
| `volume_ratio_20` | float32 | volume / volume_sma_20 |
| `volume_ratio_50` | float32 | volume / volume_sma_50 |
| `volume_ratio_100` | float32 | volume / volume_sma_100 |
| `volume_turnover_ratio` | float32 | (add_volume + delete_volume) / volume |

---

## Sample Data

### First 3 Rows
```
                   ts_event     open     high      low    close  volume
0 2024-09-30 20:00:00+00:00  2709.12  2709.12  2708.72  2708.92     401
1 2024-09-30 20:01:00+00:00  2708.82  2709.23  2708.82  2709.02     191
2 2024-09-30 20:02:00+00:00  2709.02  2709.33  2708.82  2709.33     276
```

### Last 3 Rows
```
                     ts_event     open     high      low    close  volume
570057 2025-10-31 16:57:00+00:00  4013.50  4014.10  4013.10  4013.10     285
570058 2025-10-31 16:58:00+00:00  4013.20  4014.50  4012.90  4014.20    1054
570059 2025-10-31 16:59:00+00:00  4014.10  4014.40  4012.00  4013.40     982
```

---

## Data Quality Notes

1. **NaN Values**: Rolling window features have NaN for initial bars where window is not complete
2. **Order Price Change**: All `order_price_change*` columns are NaN (no modify price data in source)
3. **Delete/Cancel Metrics**: All zero (no cancellation data in source feed)
4. **Fake Order Detection**: All zero (no fake orders detected in this dataset)
5. **Memory Optimization**: Columns use optimized dtypes (float32, int32, int16, int8)

---

## Usage in FastTrader

This 175-column dataset is the standard training data format for FastTrader's ML models:

```python
import pandas as pd

# Load data
df = pd.read_parquet("path/to/gc_continuous_1min.parquet")

# Filter to trading hours if needed
df = df.set_index('ts_event')

# Use with FastTrader training
from fasttrader.core import FastTrader, FastTraderConfig

config = FastTraderConfig()
ft = FastTrader(config=config)
ft.train_models(df)
```

---

## Related Documentation

- [COMPLETE_175_COLUMN_IMPLEMENTATION.md](../COMPLETE_175_COLUMN_IMPLEMENTATION.md) - Column completeness verification
- [run_gc_features.py](../fasttrader/run_gc_features.py) - Feature engineering pipeline
- [LIVE_TRADER_GUIDE.md](../LIVE_TRADER_GUIDE.md) - Live trading with this data format
