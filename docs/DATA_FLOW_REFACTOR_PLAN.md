# Data Flow Refactoring Plan

## Current Issues

1. **Sequential Startup**: Cache loading, MTF building, and stream initialization happen sequentially
2. **Repeated Full Calculations**: Signal calculation rebuilds entire indicator DataFrame every time
3. **No Signal Caching**: Indicators are recalculated for ALL bars, not just new ones
4. **Wrong Cache Size**: `max_cache_size=300` with 30s bars = 2.5 hours (should be 480 for 4 hours)
5. **No Incremental MTF Updates**: MTF data is rebuilt from scratch instead of appending new bars
6. **5-Minute Warmup Not Implemented**: Bot doesn't wait for stable live data before trading

---

## Target Data Flow

```
Bot Start
    │
    ├──────────────────────────────────────┐
    │                                      │
    ▼                                      ▼
[Task A: Historical Data]          [Task B: Live Stream]
    │                                      │
    ├─ Check recent cache (< 1hr old)      ├─ Connect to stream source
    │  ├─ YES: Load last 4hrs cache        │  (Rithmic or NT8)
    │  └─ NO: Load from historical data    │
    │                                      ├─ Start aggregating bars
    ▼                                      │
    ├─ Build market_data_cache             │
    ├─ Build MTF dataframes                │
    ├─ Calculate initial indicators        ▼
    │                                      │
    └──────────────────────────────────────┘
                        │
                        ▼
                [Parallel Sync Point]
                   (after 5 mins)
                        │
                        ▼
    ┌───────────────────┴────────────────────┐
    │  Merge: Historical + Live Stream Bars  │
    │  - Append new live bars to cache       │
    │  - Update MTF with only new bars       │
    │  - Calculate indicators for new bars   │
    └────────────────────────────────────────┘
                        │
                        ▼
            [Main Trading Loop - Live Chart Style]
                        │
                        ▼
    On each new bar (stream callback):
        1. Append new bar to market_data_cache
        2. Maintain 4hr rolling window (trim old)
        3. Update MTF incrementally (aggregate new bar)
        4. Calculate indicators for new bar only
           (use lookback from cached signals)
        5. Store signal for current bar
        6. Emit signal for trading decisions
```

---

## Implementation Plan

### Phase 1: Update Cache Configuration

**File**: `intelligent_trader.py`

**Changes**:
1. Update `max_cache_size` from 300 to 480 (4 hours of 30s bars)
2. Add `FOUR_HOURS_BARS` constant for clarity
3. Add `warmup_minutes = 5` configuration
4. Add `_warmup_complete` flag

```python
# Class constants
FOUR_HOURS_SECONDS = 4 * 60 * 60  # 14,400 seconds
WARMUP_MINUTES = 5

# In __init__:
self.bar_interval_seconds = 30
self.max_cache_size = self.FOUR_HOURS_SECONDS // self.bar_interval_seconds  # 480 bars
self.warmup_minutes = self.WARMUP_MINUTES
self._warmup_complete = False
self._warmup_start_time = None
self._live_bars_count = 0
```

---

### Phase 2: Add Signal Caching Infrastructure

**New Data Structures**:
```python
# In __init__:
# Signal cache: stores calculated signals per bar timestamp
self._signal_cache: Dict[datetime, dict] = {}
self._signal_cache_max_size = 500  # Keep signals for ~4 hours of bars

# Last calculated indicator values (for incremental calculation)
self._last_indicators: Dict[str, Any] = {}

# MTF incremental update tracking
self._mtf_last_update_time: Dict[str, datetime] = {}
```

---

### Phase 3: Parallel Startup Implementation

**New Method**: `async def _parallel_startup(self)`

```python
async def _parallel_startup(self) -> bool:
    """
    Run parallel startup tasks:
    A) Load historical/cache data
    B) Initialize live stream and collect warmup bars
    
    Returns True when both tasks complete and bot is ready to trade.
    """
    logger.info("🚀 Starting parallel initialization...")
    
    # Create parallel tasks
    historical_task = asyncio.create_task(
        self._startup_load_historical(),
        name="historical-load"
    )
    stream_task = asyncio.create_task(
        self._startup_init_stream(),
        name="stream-init"
    )
    
    # Wait for both with timeout
    try:
        await asyncio.wait_for(
            asyncio.gather(historical_task, stream_task),
            timeout=120.0  # 2 minute timeout
        )
    except asyncio.TimeoutError:
        logger.error("❌ Startup timeout - continuing with available data")
    
    # Merge historical + live bars
    self._merge_startup_data()
    
    # Wait for warmup to complete
    await self._wait_for_warmup()
    
    # Calculate initial indicators for full dataset
    await self._calculate_initial_indicators()
    
    logger.info("✅ Parallel startup complete - ready to trade")
    return True
```

---

### Phase 4: Historical Data Loading Task

**New Method**: `async def _startup_load_historical(self)`

```python
async def _startup_load_historical(self) -> bool:
    """Task A: Load historical data (cache or parquet)."""
    try:
        # Check for recent cache (less than 1 hour old)
        cache_file = self._find_recent_cache(max_age_hours=1)
        
        if cache_file:
            logger.info(f"📂 Loading recent cache: {cache_file}")
            loaded = self._load_parquet_to_cache(cache_file, max_bars=self.max_cache_size)
            if loaded >= self.max_cache_size * 0.5:  # At least 50% of expected bars
                logger.info(f"✅ Loaded {loaded} bars from cache")
                self._historical_loaded = True
                return True
        
        # Fallback: load from historical data folder
        historical_file = self._find_historical_data()
        if historical_file:
            logger.info(f"📂 Loading historical data: {historical_file}")
            loaded = self._load_parquet_to_cache(historical_file, max_bars=self.max_cache_size)
            if loaded > 0:
                logger.info(f"✅ Loaded {loaded} historical bars")
                self._historical_loaded = True
                return True
        
        logger.warning("⚠️ No historical data available")
        return False
        
    except Exception as e:
        logger.error(f"❌ Historical data load failed: {e}")
        return False
```

---

### Phase 5: Live Stream Initialization Task

**New Method**: `async def _startup_init_stream(self)`

```python
async def _startup_init_stream(self) -> bool:
    """Task B: Initialize live stream and start collecting warmup bars."""
    try:
        self._warmup_start_time = datetime.now(self.ET_TZ)
        self._live_bars_buffer: List[dict] = []  # Temporary buffer for warmup bars
        
        if self.use_rithmic_data and self.rithmic_client:
            # Initialize Rithmic stream
            connected = await self.rithmic_client.connect()
            if connected:
                await self.rithmic_client.start_polling()
                self.rithmic_data_active = True
                logger.info("✅ Rithmic stream initialized")
                return True
        
        # Fallback to NT8 stream
        self._initialize_market_data_subscription()
        logger.info("✅ NT8 stream initialized")
        return True
        
    except Exception as e:
        logger.error(f"❌ Stream initialization failed: {e}")
        return False
```

---

### Phase 6: Incremental Signal Calculation

**New Method**: `def _calculate_signal_for_bar(self, bar: dict, lookback_bars: int = 50) -> dict`

```python
def _calculate_signal_for_bar(self, bar: dict, lookback_bars: int = 50) -> dict:
    """
    Calculate indicators and signal for a single new bar.
    Uses cached signals/indicators for lookback instead of full recalculation.
    
    This is the "live chart" approach where we only calculate for the new bar.
    
    Args:
        bar: The new bar to calculate signals for
        lookback_bars: Number of previous bars to use for calculation
        
    Returns:
        Signal dict with all indicator values
    """
    bar_timestamp = bar['timestamp']
    
    # Check if we already have this signal cached
    if bar_timestamp in self._signal_cache:
        return self._signal_cache[bar_timestamp]
    
    # Get lookback data from cache (not full rebuild)
    cache_len = len(self.market_data_cache)
    start_idx = max(0, cache_len - lookback_bars)
    lookback_data = self.market_data_cache[start_idx:]
    
    # Build minimal DataFrame for calculation
    df = pd.DataFrame(lookback_data)
    
    # Calculate indicators only on the tail (new bar)
    # TA-Lib functions efficiently handle this
    signal = self._calculate_indicators_for_df(df)
    
    # Cache the result
    self._signal_cache[bar_timestamp] = signal
    
    # Trim old signals from cache
    if len(self._signal_cache) > self._signal_cache_max_size:
        oldest_keys = sorted(self._signal_cache.keys())[:-self._signal_cache_max_size]
        for key in oldest_keys:
            del self._signal_cache[key]
    
    return signal
```

---

### Phase 7: Incremental MTF Update

**New Method**: `def _update_mtf_with_bar(self, bar: dict)`

```python
def _update_mtf_with_bar(self, bar: dict) -> None:
    """
    Incrementally update MTF caches with a new bar.
    Does NOT rebuild from scratch - just aggregates the new bar.
    
    Args:
        bar: New bar to aggregate into MTF timeframes
    """
    bar_time = bar['timestamp']
    
    for tf_key in self.mtf_timeframes:
        cache = self.mtf_data_cache.get(tf_key, [])
        
        # Determine timeframe boundary
        tf_boundary = self._get_tf_boundary(bar_time, tf_key)
        
        # Check if we need a new MTF bar or update existing
        if cache and cache[-1]['timestamp'] == tf_boundary:
            # Update existing bar (aggregate)
            cache[-1]['high'] = max(cache[-1]['high'], bar['high'])
            cache[-1]['low'] = min(cache[-1]['low'], bar['low'])
            cache[-1]['close'] = bar['close']
            cache[-1]['volume'] += bar.get('volume', 1)
            cache[-1]['tick_count'] += bar.get('tick_count', 1)
        else:
            # Create new MTF bar
            new_tf_bar = {
                'timestamp': tf_boundary,
                'open': bar['open'],
                'high': bar['high'],
                'low': bar['low'],
                'close': bar['close'],
                'volume': bar.get('volume', 1),
                'tick_count': bar.get('tick_count', 1)
            }
            cache.append(new_tf_bar)
            
            # Trim to max size
            max_bars = self.mtf_max_bars.get(tf_key, 100)
            if len(cache) > max_bars:
                cache.pop(0)
        
        self.mtf_data_cache[tf_key] = cache
```

---

### Phase 8: Refactored Signal Loop (Live Chart Style)

**Refactored**: `async def _signal_loop(self)`

```python
async def _signal_loop(self) -> None:
    """Signal loop - calculates signals only for new bars (live chart style)."""
    logger.info("📡 Signal loop started")
    
    last_processed_timestamp = None
    
    try:
        while self.is_trading and not self._shutdown_event.is_set():
            if not self.is_trading_hours():
                wait_seconds = self.get_time_until_market_opens() or 60
                await asyncio.sleep(min(wait_seconds, 300))
                continue
            
            try:
                # Check for new bars in cache
                if self.market_data_cache:
                    latest_bar = self.market_data_cache[-1]
                    current_timestamp = latest_bar['timestamp']
                    
                    # Only calculate if we have a new bar
                    if current_timestamp != last_processed_timestamp:
                        # Update MTF incrementally
                        self._update_mtf_with_bar(latest_bar)
                        
                        # Calculate signal for this bar only
                        signal = self._calculate_signal_for_bar(latest_bar)
                        
                        # Store and emit
                        self._latest_signal = signal
                        self.signal_cache.append((datetime.now(), signal))
                        
                        last_processed_timestamp = current_timestamp
                        
                        logger.debug(f"📊 Signal calculated for {current_timestamp}: {signal.get('signal', 'HOLD')}")
                
            except Exception as exc:
                logger.warning(f"⚠️ Signal loop hiccup: {exc}")
            
            await asyncio.sleep(self.signal_loop_interval)
            
    except asyncio.CancelledError:
        logger.info("📡 Signal loop cancelled")
        raise
    finally:
        logger.info("📡 Signal loop exited")
```

---

### Phase 9: Update Bar Callback (Incremental Processing)

**Refactored**: `def _on_rithmic_bar(self, bar)` and `_aggregate_quote_to_cache`

When a new bar completes:
1. Append to `market_data_cache`
2. Trim to 4-hour window
3. Call `_update_mtf_with_bar()` (incremental)
4. Signal calculation happens in `_signal_loop`

```python
def _on_bar_complete(self, bar: dict) -> None:
    """Called when a bar completes (from any source).
    
    Handles:
    1. Adding to cache with 4hr trim
    2. Incremental MTF update
    3. Periodic cache save
    """
    # Add to cache with size limit
    self._append_bar_to_cache(bar)
    
    # Incremental MTF update (not full rebuild)
    self._update_mtf_with_bar(bar)
    
    # Track for warmup
    if not self._warmup_complete:
        self._live_bars_count += 1
        if self._check_warmup_complete():
            self._warmup_complete = True
            logger.info(f"✅ Warmup complete: {self._live_bars_count} live bars collected")
    
    logger.debug(f"📊 Bar complete @ {bar['timestamp']}: Cache={len(self.market_data_cache)}/{self.max_cache_size}")
```

---

## Summary of Changes

| File | Current | After Refactor |
|------|---------|----------------|
| Cache Size | 300 bars (2.5 hrs) | 480 bars (4 hrs) |
| Startup | Sequential | Parallel (historical + stream) |
| MTF Updates | Full rebuild | Incremental aggregation |
| Signal Calc | Full DataFrame | Per-bar with lookback cache |
| Warmup | None | 5 minutes required |
| Signal Cache | None | Rolling 4-hour cache |

---

## Implementation Order

1. **Phase 1**: Update cache size configuration
2. **Phase 2**: Add signal caching infrastructure  
3. **Phase 3**: Implement parallel startup
4. **Phase 4**: Historical data loading task
5. **Phase 5**: Live stream initialization task
6. **Phase 6**: Incremental signal calculation
7. **Phase 7**: Incremental MTF update
8. **Phase 8**: Refactor signal loop
9. **Phase 9**: Update bar callbacks

---

## Key Benefits

1. **Faster Startup**: Parallel loading reduces startup time
2. **Lower CPU Usage**: Only calculate for new bars, not entire history
3. **Consistent Data**: 4-hour rolling window matches trading needs
4. **Proper Warmup**: 5-minute stream collection ensures stable data
5. **Live Chart Parity**: Same calculation pattern as trading platforms
