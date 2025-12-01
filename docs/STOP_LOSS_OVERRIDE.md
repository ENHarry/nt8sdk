# Stop-Loss Override Feature

## Overview
A hard stop-loss mechanism designed for prop account evaluations that exits positions when losses exceed a configured percentage threshold, regardless of reversal signals or trend indicators.

## Configuration

Located in `bot/intelligent_trader.py` (lines ~400-402):

```python
# Stop-loss override for prop account risk management
self.enable_stop_loss_override = True   # Enable/disable the feature
self.stop_loss_override_pct = 15.0      # Stop-loss percentage threshold
```

## How It Works

### Trigger Condition
The stop-loss override triggers when:
```python
if profit_pct < -self.stop_loss_override_pct:
    # Exit position immediately
```

### Execution Priority
The check runs **BEFORE** all other exit conditions:
1. ✅ **Stop-loss override** ← HIGHEST PRIORITY
2. Advanced TP rules
3. Quick profit checks
4. Time-based exits
5. Reversal detection
6. ADX-based exits

### Example Scenarios

**Scenario 1: -16% Loss with Strong Uptrend**
- Entry: LONG @ $25,220.25
- Current: $25,212.00
- P&L: -$160 (-16%)
- ADX: 64.1 (STRONG uptrend)
- +DI: 87.6 >> -DI: 6.7

**WITHOUT Stop-Loss Override:**
```
✗ No exit - trend still strong
✗ No reversal signals (3 consecutive needed)
✗ No DI crossover (2+ timeframes needed)
→ Position continues, potential for larger loss
```

**WITH Stop-Loss Override (15% threshold):**
```
✓ Exit triggered at -16% loss
✓ Position closed regardless of trend
→ Loss limited to -$160
```

## Configuration Options

### Enabled (Default - Recommended for Prop Accounts)
```python
self.enable_stop_loss_override = True
self.stop_loss_override_pct = 15.0
```
- **Use Case**: Prop account evaluations, strict risk management
- **Behavior**: Exit when loss > 15%
- **Pros**: Protects account, limits drawdown, meets eval rules
- **Cons**: May exit profitable trends during retracements

### Disabled (Trend-Following Mode)
```python
self.enable_stop_loss_override = False
```
- **Use Case**: Personal accounts, trend-following strategies
- **Behavior**: Only exits on reversal signals
- **Pros**: Stays in strong trends, higher profit potential
- **Cons**: Tolerates larger drawdowns (-20%+ possible)

### Adjustable Threshold Examples

**Aggressive (10%)**
```python
self.stop_loss_override_pct = 10.0
```
- Tighter risk control
- More frequent exits
- Lower maximum loss per trade

**Conservative (20%)**
```python
self.stop_loss_override_pct = 20.0
```
- More room for retracements
- Fewer false exits
- Higher maximum loss per trade

**Moderate (15%) - Default**
```python
self.stop_loss_override_pct = 15.0
```
- Balanced approach
- Typical for prop account evals
- Reasonable drawdown tolerance

## Implementation Details

### Location
File: `bot/intelligent_trader.py`
- Configuration: Lines ~400-402
- Execution: Lines ~5918-5923 (in `monitor_position_conditions()`)

### Log Messages
```
🛑 STOP-LOSS OVERRIDE TRIGGERED: -16.0% loss exceeds 15.0% threshold
💸 Closing position to protect account (P&L: $-160.00)
```

### Exit Reason Tracking
```python
exit_reason = "Stop-loss override: -16.0% loss exceeds 15.0% threshold"
```
This appears in:
- Trade metrics
- Daily summaries
- Performance reports

## Best Practices

### For Prop Account Evaluations
```python
self.enable_stop_loss_override = True
self.stop_loss_override_pct = 15.0  # Adjust based on eval rules
```
- Enable the feature
- Set threshold based on account rules
- Typical prop firm limits: 10-20% daily loss

### For Live Trading
```python
self.enable_stop_loss_override = True
self.stop_loss_override_pct = 10.0  # Tighter for real money
```
- Recommend keeping enabled
- Use tighter threshold (10-12%)
- Better safe than sorry

### For Backtesting
```python
self.enable_stop_loss_override = False  # Test pure strategy
```
- Disable to test strategy performance
- Evaluate reversal detection effectiveness
- Assess trend-following capability

## Comparison with Reversal Detection

| Feature | Stop-Loss Override | Reversal Detection |
|---------|-------------------|-------------------|
| **Trigger** | Loss % threshold | Signal reversal |
| **Speed** | Immediate | Requires confirmation |
| **Sensitivity** | Fixed threshold | Market-dependent |
| **False Exits** | Low (% based) | Moderate (signal noise) |
| **Trend Following** | Poor (exits trends) | Good (follows indicators) |
| **Risk Control** | Excellent (hard limit) | Moderate (can lag) |
| **Prop Account** | ✅ Ideal | ⚠️ May be too slow |
| **Personal Account** | ✅ Safe | ✅ Profitable |

## Testing

### Test Scenario 1: Small Loss
```
Entry: $25,000.00
Current: $24,950.00
Loss: -$50.00 (-5%)
Threshold: 15%
Expected: No exit (below threshold)
```

### Test Scenario 2: Threshold Breach
```
Entry: $25,000.00
Current: $24,600.00
Loss: -$400.00 (-16%)
Threshold: 15%
Expected: Exit triggered
```

### Test Scenario 3: Feature Disabled
```
Entry: $25,000.00
Current: $24,600.00
Loss: -$400.00 (-16%)
enable_stop_loss_override: False
Expected: No exit (feature disabled)
```

## Summary

The stop-loss override provides a **safety net** for prop account evaluations by enforcing a hard loss limit. It complements the reversal detection system by providing guaranteed risk protection when trends move against you.

**Key Advantages:**
- ✅ Hard risk limit (predictable max loss)
- ✅ Fast execution (no confirmation needed)
- ✅ Simple configuration (one boolean, one threshold)
- ✅ Ideal for prop account rules
- ✅ Works independently of signals/indicators

**When to Use:**
- Prop firm evaluations ✅
- Risk-averse trading ✅
- Volatile markets ✅
- Learning/testing ✅
- Backtesting trend-following ❌ (disable to test pure strategy)
