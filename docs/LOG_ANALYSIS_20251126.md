# Log Analysis - Trading Session 2025-11-26 (22:17 - 09:41)

## Session Summary
- **Duration**: ~11 hours overnight
- **Total Trades**: 334
- **Win Rate**: 70.7% (236 wins, 96 losses)
- **Total P&L**: +$9,822.53
- **Profit Factor**: 2.64
- **Sharpe Ratio**: 4.21
- **Max Drawdown**: $466.36
- **Starting Equity**: $114,304.75
- **Ending Equity**: $111,769.75 (discrepancy - see Issue #6)

---

## Major Issues Identified

### Issue #1: ADX Signal Returning 0 on 1m Timeframe
**Status**: 🟡 Partially Fixed (still occurs occasionally)

**Evidence**:
```
adx_long': '✗ 30s(1) ≠ 1m(0)'
adx_short': '✗ 1s(0) ≠ 5s(0)'
```

**Analysis**:
- At startup (22:17:27), ADX is returning 0 for all timeframes initially
- After ~3 seconds, 1s and 5s start working: `adx_short': '✓ 1s(1) ≡ 5s(1)`
- But 1m remains at 0 even after data is built: `adx_long': '✗ 30s(1) ≠ 1m(0)`
- The 1m cache only has 36 bars initially - may need more data for ADX

**Root Cause**:
- ADX requires sufficient data bars (typically 14-28 bars minimum with warm-up)
- 1m timeframe only has 36 bars at startup
- The ADX signal function may need adjustment for shorter datasets

---

### Issue #2: OB/OS Conflicts with Main Signal Direction
**Status**: 🟢 Working as Designed (but needs review)

**Evidence**:
```
Entry Signal Votes: BUY=2, SELL=1 | Final=1 (need 2/3) | ADX=19.6 | Supertrend=1, OBOS=-1, Trending=1
```

**Analysis**:
- OB/OS consistently voting SELL (-1) while other indicators vote BUY
- This is intentional - OB/OS is a mean-reversion indicator
- When price is overbought (high RSI), OB/OS says SELL even in uptrend
- This creates the "Reversal Risk: 33.3%" shown in logs

**Consideration**:
- May want to reduce OB/OS weight or exclude during strong trends (ADX > 25)
- Alternative: Only use OB/OS for exit signals, not entry

---

### Issue #3: Trending Long Timeframe Misalignment
**Status**: 🔴 Needs Investigation

**Evidence**:
```
trending_long': '✗ 30s(1) ≠ 1m(-1)'
```

**Analysis**:
- Trending indicator shows 30s = BUY but 1m = SELL
- This happens consistently throughout the session
- The 1m Trending signal is often opposite to shorter timeframes

**Possible Causes**:
1. Pivot detection finding different swing highs/lows on 1m vs 30s
2. Different momentum slope on longer timeframe
3. Need to validate the simplified trend detection for 1m bars

---

### Issue #4: Account Balance Discrepancy
**Status**: 🔴 Critical

**Evidence**:
- **Logged P&L**: +$9,822.53 (334 trades)
- **Starting Equity**: $114,304.75
- **Ending Equity**: $111,769.75
- **Expected Ending**: $114,304.75 + $9,822.53 = $124,127.28
- **Actual Difference**: $114,304.75 - $111,769.75 = **-$2,535.00 LOSS**

**Analysis**:
- Despite logging +$9,822.53 profit, account shows -$2,535 loss
- This is a **HUGE DISCREPANCY** of approximately $12,357.53
- P&L tracking may be using incorrect entry prices
- Commission may not be properly accounted

**Possible Causes**:
1. Slippage on LIMIT orders (placing at mid-price but filling at worse price)
2. Commission not subtracted from logged P&L
3. ATM strategy stop-losses not being tracked
4. Entry price logged != actual fill price

---

### Issue #5: Quick Profit Exit Strategy May Be Premature
**Status**: 🟡 Review Needed

**Evidence**:
```
22:18:10.648 | CLOSING POSITION - Reason: Quick profit exit (aggressive): $35.00 in range $30.00-$500.00
22:18:25.718 | CLOSING POSITION - Reason: Quick profit exit (aggressive): $45.00 in range $30.00-$500.00
```

**Analysis**:
- Taking profits at $30-$45 with target of 2.5 points ($12.50)
- This seems correct (taking > 2x the target)
- But if slippage is eating profits, may need higher threshold

---

### Issue #6: LIMIT Order Fill Rate
**Status**: 🟡 Affecting Performance

**Evidence**:
```
09:40:39.788 | ⚠️  Order 61075DB6B9D747569CFBDA921C8C4D7C did not fill within 30 seconds
09:41:13.580 | ⚠️  Order C4DBC4A6FDA34761856BE10F98AD7509 did not fill within 30 seconds
```

**Analysis**:
- LIMIT orders placed at pullback prices sometimes don't fill
- 30-second timeout then cancels
- This may be causing missed entries

---

## Recommendations

### Immediate Fixes Needed

1. **P&L Tracking Bug (Critical)**
   - Verify entry price matches actual fill price from NT8
   - Track commission per trade
   - Validate ATM strategy stop-loss closures

2. **ADX 1m Signal**
   - Increase minimum bar requirement for 1m ADX
   - Or use 30s ADX as fallback for 1m

3. **Trending 1m Alignment**
   - Review `trending_signal()` function for 1m data
   - Consider using longer swing period for 1m

### Performance Improvements

4. **LIMIT Order Timeout**
   - Consider reducing timeout from 30s to 15s
   - Or chase the market if signal still valid

5. **OB/OS Weighting**
   - Consider reducing OB/OS impact during strong trends (ADX > 25)

---

## Trade-by-Trade P&L Sample (First 5)

| Time | Side | Entry | Exit | Duration | P&L | Running |
|------|------|-------|------|----------|-----|---------|
| 22:17:27 | BUY | $25,316.50 | $25,318.25 | 00:43 | +$35.00 | +$30.32 |
| 22:18:11 | BUY | $25,317.75 | $25,320.00 | 00:14 | +$45.00 | +$68.14 |
| 22:18:45 | BUY | $25,321.25 | (stop?) | ~2 min | -$? | ~+$68 |

*Note: Commission is $2.18 per round trip*

---

## Log File Details

- **File**: `trending_trading_log_20251126_221722.log`
- **Size**: >50MB
- **Location**: `logs/bot/trending_bot/`
