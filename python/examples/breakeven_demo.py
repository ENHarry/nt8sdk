"""
Standalone demonstration of auto-breakeven functionality
"""

from python.nt8.advanced_strategy import BreakevenConfig, BreakevenManager


def demonstrate_breakeven_long():
    """Demonstrate breakeven logic for a long position"""
    
    print("=" * 80)
    print("AUTO-BREAKEVEN DEMONSTRATION - LONG POSITION")
    print("=" * 80)
    
    # Configure 3-step breakeven as per requirements
    config = BreakevenConfig(
        num_steps=3,
        profit_targets=[7.0, 10.0, 15.0],
        breakeven_offsets=[0.0, 2.0, 4.0],
        trailing_ticks=2,
        tick_size=0.25,
        enabled=True
    )
    
    print(f"\n{config}\n")
    
    manager = BreakevenManager(config)
    
    # Initialize long position
    entry_price = 110.0
    initial_stop = 103.0
    
    print(f"{'=' * 80}")
    print("SCENARIO: Long Entry")
    print(f"{'=' * 80}")
    
    manager.initialize_position(
        entry_price=entry_price,
        stop_loss=initial_stop,
        is_long=True
    )
    
    # Simulate price movements
    price_scenarios = [
        (110.0, "Entry"),
        (112.0, "Moving up +2"),
        (115.0, "Moving up +5"),
        (117.0, "🎯 Step 1 Target Reached (+7)"),
        (118.0, "Continue up +8"),
        (120.0, "🎯 Step 2 Target Reached (+10)"),
        (122.0, "Continue up +12"),
        (125.0, "🎯 Step 3 Target Reached (+15)"),
        (127.0, "Peak at +17"),
        (126.5, "Slight pullback"),
        (126.0, "More pullback"),
        (125.5, "Trailing stop working..."),
    ]
    
    print(f"\n{'=' * 80}")
    print("PRICE MOVEMENT SIMULATION")
    print(f"{'=' * 80}\n")
    
    for price, description in price_scenarios:
        print(f"\n📊 Price: {price:.2f} - {description}")
        print("-" * 80)
        
        new_stop = manager.update(price)
        
        if new_stop:
            print(f"   ⚠️  STOP LOSS UPDATED TO: {new_stop:.2f}")
        else:
            print(f"   Current Stop: {manager.current_stop_loss:.2f}")
        
        profit = price - entry_price
        print(f"   Unrealized P&L: {profit:.2f} points")
        print(f"   Current Step: {manager.current_step}/{config.num_steps}")
        
        if manager.highest_price_long:
            print(f"   Highest Price: {manager.highest_price_long:.2f}")
    
    print(f"\n{'=' * 80}")
    print("FINAL STATISTICS")
    print(f"{'=' * 80}")
    print(f"Total Stop Adjustments: {manager.stop_adjustments}")
    print(f"Steps Activated: {manager.current_step}/{config.num_steps}")
    print(f"Final Stop Loss: {manager.current_stop_loss:.2f}")
    print(f"Original Stop Loss: {initial_stop:.2f}")
    if manager.current_stop_loss is not None:
        print(f"Improvement: {manager.current_stop_loss - initial_stop:.2f} points")
    else:
        print("Improvement: N/A (stop loss not set)")


def demonstrate_breakeven_short():
    """Demonstrate breakeven logic for a short position"""
    
    print("\n\n")
    print("=" * 80)
    print("AUTO-BREAKEVEN DEMONSTRATION - SHORT POSITION")
    print("=" * 80)
    
    # Configure 2-step breakeven for shorts
    config = BreakevenConfig(
        num_steps=2,
        profit_targets=[7.0, 10.0],
        breakeven_offsets=[0.0, 2.0],
        trailing_ticks=2,
        tick_size=0.25,
        enabled=True
    )
    
    print(f"\n{config}\n")
    
    manager = BreakevenManager(config)
    
    # Initialize short position
    entry_price = 110.0
    initial_stop = 117.0  # Stop above entry for shorts
    
    print(f"{'=' * 80}")
    print("SCENARIO: Short Entry")
    print(f"{'=' * 80}")
    
    manager.initialize_position(
        entry_price=entry_price,
        stop_loss=initial_stop,
        is_long=False
    )
    
    # Simulate price movements
    price_scenarios = [
        (110.0, "Entry"),
        (108.0, "Moving down -2"),
        (105.0, "Moving down -5"),
        (103.0, "🎯 Step 1 Target Reached (-7)"),
        (102.0, "Continue down -8"),
        (100.0, "🎯 Step 2 Target Reached (-10)"),
        (98.0, "Continue down -12"),
        (97.0, "Bottom at -13"),
        (97.5, "Slight bounce"),
        (98.0, "More bounce"),
        (98.5, "Trailing stop working..."),
    ]
    
    print(f"\n{'=' * 80}")
    print("PRICE MOVEMENT SIMULATION")
    print(f"{'=' * 80}\n")
    
    for price, description in price_scenarios:
        print(f"\n📊 Price: {price:.2f} - {description}")
        print("-" * 80)
        
        new_stop = manager.update(price)
        
        if new_stop:
            print(f"   ⚠️  STOP LOSS UPDATED TO: {new_stop:.2f}")
        else:
            print(f"   Current Stop: {manager.current_stop_loss:.2f}")
        
        profit = entry_price - price  # Inverse for shorts
        print(f"   Unrealized P&L: {profit:.2f} points")
        print(f"   Current Step: {manager.current_step}/{config.num_steps}")
        
        if manager.lowest_price_short:
            print(f"   Lowest Price: {manager.lowest_price_short:.2f}")
    
    print(f"\n{'=' * 80}")
    print("FINAL STATISTICS")
    print(f"{'=' * 80}")
    print(f"Total Stop Adjustments: {manager.stop_adjustments}")
    print(f"Steps Activated: {manager.current_step}/{config.num_steps}")
    print(f"Final Stop Loss: {manager.current_stop_loss:.2f}")
    print(f"Original Stop Loss: {initial_stop:.2f}")
    if manager.current_stop_loss is not None:
        print(f"Improvement: {initial_stop - manager.current_stop_loss:.2f} points")
    else:
        print("Improvement: N/A (stop loss not set)")


def demonstrate_custom_configuration():
    """Show how to create custom configurations"""
    
    print("\n\n")
    print("=" * 80)
    print("CUSTOM CONFIGURATION EXAMPLES")
    print("=" * 80)
    
    examples = [
        {
            "name": "Conservative (1-step)",
            "config": BreakevenConfig(
                num_steps=1,
                profit_targets=[5.0],
                breakeven_offsets=[0.0],
                trailing_ticks=3,
                tick_size=0.25
            )
        },
        {
            "name": "Moderate (2-step)",
            "config": BreakevenConfig(
                num_steps=2,
                profit_targets=[7.0, 10.0],
                breakeven_offsets=[0.0, 2.0],
                trailing_ticks=2,
                tick_size=0.25
            )
        },
        {
            "name": "Aggressive (3-step)",
            "config": BreakevenConfig(
                num_steps=3,
                profit_targets=[7.0, 10.0, 15.0],
                breakeven_offsets=[0.0, 2.0, 4.0],
                trailing_ticks=2,
                tick_size=0.25
            )
        },
        {
            "name": "Tight Trailing (wide stops)",
            "config": BreakevenConfig(
                num_steps=2,
                profit_targets=[10.0, 20.0],
                breakeven_offsets=[3.0, 5.0],
                trailing_ticks=1,  # Very tight trailing
                tick_size=0.25
            )
        },
    ]
    
    for example in examples:
        print(f"\n{'-' * 80}")
        print(f"Configuration: {example['name']}")
        print(f"{'-' * 80}")
        print(example['config'])


if __name__ == "__main__":
    # Run all demonstrations
    demonstrate_breakeven_long()
    demonstrate_breakeven_short()
    demonstrate_custom_configuration()
    
    print("\n\n")
    print("=" * 80)
    print("KEY CONCEPTS")
    print("=" * 80)
    print("""
1. PROFIT TARGETS: Define when each breakeven step activates
   - Step 1: Activates when profit reaches first target
   - Step 2: Activates when profit reaches second target
   - Step 3: Activates when profit reaches third target

2. BREAKEVEN OFFSETS: Define where stop loss moves to
   - Offset 0.0: Stop moves to entry price (true breakeven)
   - Offset 2.0: Stop moves to entry + 2 points (locked profit)
   - Offset 4.0: Stop moves to entry + 4 points (more locked profit)

3. TRAILING STOP: After breakeven activates, stop trails price
   - Trails by specified number of ticks
   - NEVER goes below current breakeven floor
   - Only moves in profitable direction

4. LONG POSITIONS:
   - Stops move UP as price rises
   - Floor prevents stop from moving down

5. SHORT POSITIONS:
   - Stops move DOWN as price falls
   - Ceiling prevents stop from moving up

EXAMPLE SCENARIO (3-step, Entry at 110):
- Initial Stop: 103 (7 point risk)
- Price reaches 117 (+7): Stop moves to 110 (breakeven)
- Price reaches 120 (+10): Stop moves to 112 (2 points locked)
- Price reaches 125 (+15): Stop moves to 114 (4 points locked)
- Price peaks at 127: Stop trails to 126.50 (2 ticks)
- Stop never goes below 114 (Step 3 floor)
""")
    
    print("=" * 80)
    print("TO USE IN YOUR STRATEGY:")
    print("=" * 80)
    print("""
from advanced_strategy import AdvancedStrategy, BreakevenConfig

# Create your configuration
config = BreakevenConfig(
    num_steps=3,
    profit_targets=[7.0, 10.0, 15.0],
    breakeven_offsets=[0.0, 2.0, 4.0],
    trailing_ticks=2,
    tick_size=0.25,
    enabled=True
)

# Create strategy with config
strategy = AdvancedStrategy(
    instrument="ES 03-25",
    max_position=3,
    breakeven_config=config
)

# Run it!
strategy.run()
""")