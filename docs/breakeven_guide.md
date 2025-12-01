# Auto-Breakeven Guide

## Overview

The Auto-Breakeven feature automatically manages your stop loss to protect profits as your trade moves in your favor. It uses a multi-step approach to progressively move your stop loss, eventually activating a trailing stop that never goes below your secured profit level.

## How It Works

### Basic Concept

1. **Entry**: You enter a position with an initial stop loss
2. **Profit Targets**: As price moves in your favor and reaches predefined profit levels
3. **Breakeven Steps**: Stop loss automatically moves to lock in profits
4. **Trailing Stop**: Once activated, stop trails price while respecting the breakeven floor

### Step-by-Step Example

Let's say you go LONG at **110.00** with these settings:

```python
config = BreakevenConfig(
    num_steps=3,
    profit_targets=[7.0, 10.0, 15.0],
    breakeven_offsets=[0.0, 2.0, 4.0],
    trailing_ticks=2,
    tick_size=0.25
)
```

**Initial State:**
- Entry: 110.00
- Initial Stop: 103.00 (7 point risk)

**Step 1 Activation (Price reaches 117.00):**
- Profit: +7.0 points ✓ Target reached!
- Stop moves to: 110.00 (entry + 0.0 offset)
- Result: Risk eliminated, now at breakeven

**Step 2 Activation (Price reaches 120.00):**
- Profit: +10.0 points ✓ Target reached!
- Stop moves to: 112.00 (entry + 2.0 offset)
- Result: 2 points of profit locked in

**Step 3 Activation (Price reaches 125.00):**
- Profit: +15.0 points ✓ Target reached!
- Stop moves to: 114.00 (entry + 4.0 offset)
- Result: 4 points of profit locked in

**Trailing Stop Active (Price continues to 127.00):**
- Price: 127.00
- Trailing Stop: 126.50 (2 ticks * 0.25 = 0.50 below high)
- Current Stop: 126.50
- Floor: 114.00 (Step 3 level)

**Price Pulls Back (Price drops to 125.00):**
- Price: 125.00
- Trailing calculation: 124.50
- BUT Floor is 114.00, so stop stays at 126.50
- Trailing stop respects the floor!

## Configuration Parameters

### BreakevenConfig

```python
BreakevenConfig(
    num_steps: int = 2,              # Number of breakeven steps (1-3)
    profit_targets: List[float],     # Profit levels to activate each step
    breakeven_offsets: List[float],  # Where to place stop at each step
    trailing_ticks: int = 2,         # Ticks to trail stop by
    tick_size: float = 0.25,         # Instrument tick size
    enabled: bool = True             # Enable/disable feature
)
```

#### num_steps
Number of breakeven steps (1-3). More steps = more gradual profit protection.

#### profit_targets
List of profit levels (in points) that trigger each breakeven step.
- Must have exactly `num_steps` elements
- Should be in ascending order
- Example: `[7.0, 10.0, 15.0]` for 3 steps

#### breakeven_offsets
List of offsets above entry price where stop is placed at each step.
- Must have exactly `num_steps` elements
- Value 0.0 = true breakeven (stop at entry)
- Positive values = locked profit above entry
- Example: `[0.0, 2.0, 4.0]` progressively locks more profit

#### trailing_ticks
Number of ticks to trail the stop loss by.
- Smaller values = tighter trailing (more likely to exit early)
- Larger values = looser trailing (give trade more room)
- Example: `2` ticks on ES = 0.50 points

#### tick_size
The minimum price increment for the instrument.
- ES, NQ: 0.25
- YM: 1.0
- 6E: 0.0001

## Preset Configurations

### Conservative (1-Step)
Single breakeven level with wider trailing stop.

```python
config = BreakevenConfig(
    num_steps=1,
    profit_targets=[5.0],
    breakeven_offsets=[0.0],
    trailing_ticks=3,
    tick_size=0.25
)
```

**Use when:** You want simple protection without frequent adjustments.

### Moderate (2-Step) - Recommended
Two-level breakeven with moderate trailing.

```python
config = BreakevenConfig(
    num_steps=2,
    profit_targets=[7.0, 10.0],
    breakeven_offsets=[0.0, 2.0],
    trailing_ticks=2,
    tick_size=0.25
)
```

**Use when:** Balancing protection and profit potential.

### Aggressive (3-Step)
Three-level breakeven for maximum profit protection.

```python
config = BreakevenConfig(
    num_steps=3,
    profit_targets=[7.0, 10.0, 15.0],
    breakeven_offsets=[0.0, 2.0, 4.0],
    trailing_ticks=2,
    tick_size=0.25
)
```

**Use when:** You want to lock in profits progressively in trending markets.

## Usage in Strategy

### Basic Usage

```python
from advanced_strategy import AdvancedStrategy, BreakevenConfig

# Create configuration
config = BreakevenConfig(
    num_steps=2,
    profit_targets=[7.0, 10.0],
    breakeven_offsets=[0.0, 2.0],
    trailing_ticks=2,
    tick_size=0.25
)

# Create strategy with breakeven
strategy = AdvancedStrategy(
    instrument="ES 03-25",
    max_position=3,
    breakeven_config=config
)

# Run strategy
strategy.run()
```

### Interactive Configuration

```python
from advanced_strategy import create_strategy_with_config

# This will prompt you for configuration
strategy = create_strategy_with_config()
strategy.run()
```

### Disable Auto-Breakeven

```python
config = BreakevenConfig(enabled=False)
strategy = AdvancedStrategy(
    instrument="ES 03-25",
    max_position=3,
    breakeven_config=config
)
```

## Testing the Feature

Run the demonstration script to see breakeven logic in action:

```bash
python examples/breakeven_demo.py
```

This will show you:
- Long position example with 3 steps
- Short position example with 2 steps
- Various configuration presets
- Step-by-step stop loss adjustments

## Important Notes

### Long Positions
- Stop loss moves **UP** as price rises
- Stop **NEVER** moves down (only up)
- Breakeven offset is **added** to entry price

### Short Positions
- Stop loss moves **DOWN** as price falls
- Stop **NEVER** moves up (only down)
- Breakeven offset is **subtracted** from entry price

### Trailing Stop Behavior
- Only activates after first breakeven step
- Trails by specified number of ticks
- **Always respects current breakeven floor/ceiling**
- Updates on every tick when active

### Order Management
The example implementation logs stop loss updates but doesn't actually modify orders. In production, you would:

```python
def modify_stop_loss(self, new_stop_price: float):
    # Cancel existing stop
    if self.stop_order_id:
        self.client.cancel_order(self.stop_order_id)
    
    # Place new stop
    position = self.client.get_position(self.instrument)
    action = OrderAction.SELL if position.is_long else OrderAction.BUY
    
    self.stop_order_id = self.client.place_stop_order(
        instrument=self.instrument,
        action=action,
        quantity=position.quantity,
        stop_price=new_stop_price,
        signal_name="AUTO_BE_STOP"
    )
```

## Best Practices

### Setting Profit Targets
- Base on Average True Range (ATR)
- Use support/resistance levels
- Consider instrument volatility
- Test in simulation first

### Setting Breakeven Offsets
- Start with 0.0 for first step (true breakeven)
- Increase gradually (e.g., 0, 2, 4 or 0, 3, 6)
- Consider your profit target spacing
- Avoid offsets too close to profit targets

### Setting Trailing Distance
- More volatile instruments need wider trailing
- Consider your trading timeframe
- 2-3 ticks typical for scalping
- 4-6 ticks for swing trading

### Number of Steps
- **1 step**: Simple, good for beginners
- **2 steps**: Balanced, recommended for most
- **3 steps**: Advanced, best for trending markets

## Troubleshooting

### Stop Not Moving
- Check if profit target reached
- Verify breakeven is enabled
- Confirm position is initialized
- Check logs for errors

### Stop Moving Too Frequently
- Increase trailing_ticks
- Widen profit target spacing
- Consider market volatility

### Stop Too Tight
- Increase trailing_ticks
- Reduce number of steps
- Increase breakeven_offsets

### Stop Too Loose
- Decrease trailing_ticks
- Add more steps
- Tighten profit targets

## Performance Impact

The breakeven manager is highly optimized:
- O(1) complexity for updates
- No allocations during updates
- Processes in microseconds
- Minimal memory footprint

Safe to use even for high-frequency strategies.

## Advanced Customization

### Custom Logic

You can extend the `BreakevenManager` class:

```python
class CustomBreakevenManager(BreakevenManager):
    def _update_long(self, current_price: float) -> Optional[float]:
        # Call parent logic
        new_stop = super()._update_long(current_price)
        
        # Add custom logic
        if self.should_tighten_stop(current_price):
            new_stop = self.calculate_tighter_stop(current_price)
        
        return new_stop
```

### Dynamic Configuration

Adjust configuration based on market conditions:

```python
def get_dynamic_config(volatility: float) -> BreakevenConfig:
    if volatility > 2.0:
        # High volatility: wider settings
        return BreakevenConfig(
            num_steps=2,
            profit_targets=[10.0, 15.0],
            breakeven_offsets=[0.0, 3.0],
            trailing_ticks=4
        )
    else:
        # Low volatility: tighter settings
        return BreakevenConfig(
            num_steps=3,
            profit_targets=[5.0, 8.0, 12.0],
            breakeven_offsets=[0.0, 2.0, 4.0],
            trailing_ticks=2
        )
```

## Examples by Instrument

### ES (E-mini S&P 500)
```python
BreakevenConfig(
    num_steps=2,
    profit_targets=[7.0, 10.0],
    breakeven_offsets=[0.0, 2.0],
    trailing_ticks=2,
    tick_size=0.25
)
```

### NQ (E-mini Nasdaq)
```python
BreakevenConfig(
    num_steps=2,
    profit_targets=[15.0, 25.0],  # More volatile
    breakeven_offsets=[0.0, 5.0],
    trailing_ticks=3,
    tick_size=0.25
)
```

### YM (E-mini Dow)
```python
BreakevenConfig(
    num_steps=2,
    profit_targets=[40.0, 60.0],
    breakeven_offsets=[0.0, 10.0],
    trailing_ticks=2,
    tick_size=1.0
)
```

### CL (Crude Oil)
```python
BreakevenConfig(
    num_steps=3,
    profit_targets=[0.30, 0.50, 0.80],
    breakeven_offsets=[0.0, 0.10, 0.20],
    trailing_ticks=2,
    tick_size=0.01
)
```

## Conclusion

Auto-breakeven is a powerful risk management tool that:
- ✅ Protects profits automatically
- ✅ Eliminates emotional decision-making
- ✅ Adapts to market movement
- ✅ Fully configurable and dynamic
- ✅ Works for any instrument or timeframe

Start with moderate settings and adjust based on your results!