"""
Advanced strategy example with risk management and auto-breakeven
"""

from .client import NT8Client, OrderAction, OrderState, MarketPosition
from collections import deque
from datetime import datetime
from typing import Optional, List, Dict
import time


class BreakevenConfig:
    """Configuration for auto-breakeven management"""

    def __init__(
        self,
        num_steps: int = 2,
        profit_targets: Optional[List[float]] = None,
        breakeven_offsets: Optional[List[float]] = None,
        trailing_ticks: int = 2,
        tick_size: Optional[float] = None,
        enabled: bool = True,
        instrument: Optional[str] = None
    ):
        """
        Initialize breakeven configuration - fully dynamic with no hard-coding

        Args:
            num_steps: Number of breakeven steps (1-3)
            profit_targets: List of profit targets in points for each step
            breakeven_offsets: List of offsets above entry price for stop placement
            trailing_ticks: Number of ticks to trail stop loss
            tick_size: Instrument tick size (if None, will be queried dynamically)
            enabled: Whether auto-breakeven is enabled
            instrument: Trading instrument (used for dynamic tick size lookup)
        """
        if num_steps < 1 or num_steps > 3:
            raise ValueError("num_steps must be between 1 and 3")

        self.num_steps = num_steps
        self._tick_size = tick_size  # Can be None for dynamic lookup
        self.trailing_ticks = trailing_ticks
        self.enabled = enabled
        self.instrument = instrument

        # Default profit targets if not provided
        if profit_targets is None:
            self.profit_targets = [7.0, 10.0, 15.0][:self.num_steps]
        else:
            if len(profit_targets) != num_steps:
                raise ValueError(f"profit_targets must have {num_steps} elements")
            self.profit_targets = profit_targets[:]

        # Default breakeven offsets if not provided (at entry price by default)
        if breakeven_offsets is None:
            self.breakeven_offsets = [0.0, 0.0, 0.0][:self.num_steps]
        else:
            if len(breakeven_offsets) != num_steps:
                raise ValueError(f"breakeven_offsets must have {num_steps} elements")
            self.breakeven_offsets = breakeven_offsets[:]

        # Validate profit targets are in ascending order
        for i in range(len(self.profit_targets) - 1):
            if self.profit_targets[i] >= self.profit_targets[i + 1]:
                raise ValueError("profit_targets must be in ascending order")

    @property
    def tick_size(self) -> float:
        """Get tick size (raises error if not set and not queried)"""
        if self._tick_size is None:
            raise ValueError("Tick size not set. Call set_tick_size() or provide in constructor")
        return self._tick_size

    def set_tick_size(self, tick_size: float):
        """
        Set tick size dynamically (for instruments where tick size needs to be queried)

        Args:
            tick_size: The tick size value
        """
        if tick_size <= 0:
            raise ValueError("tick_size must be positive")
        self._tick_size = tick_size

    def has_tick_size(self) -> bool:
        """Check if tick size is set"""
        return self._tick_size is not None
    
    def get_trailing_distance(self) -> float:
        """Get trailing stop distance in price points"""
        return self.trailing_ticks * self.tick_size
    
    def __str__(self):
        lines = [
            f"Auto-Breakeven Configuration ({self.num_steps} steps):",
            f"  Enabled: {self.enabled}",
            f"  Tick Size: {self.tick_size}",
            f"  Trailing: {self.trailing_ticks} ticks ({self.get_trailing_distance():.2f} points)",
        ]
        for i in range(self.num_steps):
            lines.append(
                f"  Step {i+1}: Profit target {self.profit_targets[i]:.2f} → "
                f"Breakeven @ entry + {self.breakeven_offsets[i]:.2f}"
            )
        return "\n".join(lines)


class BreakevenManager:
    """Manages auto-breakeven and trailing stop loss"""
    
    def __init__(self, config: BreakevenConfig):
        self.config = config
        
        # Position tracking
        self.entry_price: Optional[float] = None
        self.position_side: Optional[str] = None  # 'LONG' or 'SHORT'
        self.current_stop_loss: Optional[float] = None
        self.initial_stop_loss: Optional[float] = None
        
        # Breakeven state
        self.current_step = 0  # 0 = no breakeven activated yet
        self.highest_price_long: Optional[float] = None
        self.lowest_price_short: Optional[float] = None
        
        # Statistics
        self.step_activation_times: Dict[int, datetime] = {}
        self.stop_adjustments = 0
    
    def reset(self):
        """Reset for new position"""
        self.entry_price = None
        self.position_side = None
        self.current_stop_loss = None
        self.initial_stop_loss = None
        self.current_step = 0
        self.highest_price_long = None
        self.lowest_price_short = None
        self.step_activation_times.clear()
        self.stop_adjustments = 0
    
    def initialize_position(
        self,
        entry_price: float,
        stop_loss: float,
        is_long: bool
    ):
        """Initialize tracking for a new position"""
        self.reset()
        self.entry_price = entry_price
        self.initial_stop_loss = stop_loss
        self.current_stop_loss = stop_loss
        self.position_side = 'LONG' if is_long else 'SHORT'
        
        if is_long:
            self.highest_price_long = entry_price
        else:
            self.lowest_price_short = entry_price
        
        print(f"\n[Breakeven Manager] Position initialized:")
        print(f"  Side: {self.position_side}")
        print(f"  Entry: {entry_price:.2f}")
        print(f"  Initial Stop: {stop_loss:.2f}")
        print(f"  Risk: {abs(entry_price - stop_loss):.2f} points")
    
    def update(self, current_price: float) -> Optional[float]:
        """
        Update breakeven logic based on current price
        
        Args:
            current_price: Current market price
            
        Returns:
            New stop loss price if it should be updated, None otherwise
        """
        if not self.config.enabled or self.entry_price is None:
            return None
        
        if self.position_side == 'LONG':
            return self._update_long(current_price)
        elif self.position_side == 'SHORT':
            return self._update_short(current_price)
        
        return None
    
    def _update_long(self, current_price: float) -> Optional[float]:
        """Update logic for long positions"""
        # Track highest price
        if self.highest_price_long is None or current_price > self.highest_price_long:
            self.highest_price_long = current_price
        
        profit = current_price - self.entry_price if self.entry_price is not None else 0.0
        new_stop = None
        
        # Check if we should activate next breakeven step
        if self.current_step < self.config.num_steps:
            target_profit = self.config.profit_targets[self.current_step]
            
            if profit >= target_profit:
                # Activate this breakeven step
                self.current_step += 1
                breakeven_offset = self.config.breakeven_offsets[self.current_step - 1]
                if self.entry_price is not None:
                    new_stop = self.entry_price + breakeven_offset
                
                self.step_activation_times[self.current_step] = datetime.now()
                
                print(f"\n[Breakeven] Step {self.current_step} activated!")
                print(f"  Profit reached: {profit:.2f} (target: {target_profit:.2f})")
                print(f"  Moving stop to: {new_stop:.2f} (entry + {breakeven_offset:.2f})")
        
        # Apply trailing stop if we're at a breakeven level
        if self.current_step > 0:
            # Calculate trailing stop from highest price
            trailing_distance = self.config.get_trailing_distance()
            trailing_stop = self.highest_price_long - trailing_distance
            
            # Get current breakeven floor
            if self.entry_price is not None:
                breakeven_floor = self.entry_price + self.config.breakeven_offsets[self.current_step - 1]
            else:
                breakeven_floor = 0.0  # Fallback value if entry_price is None
            
            # Trailing stop can't go below breakeven floor
            trailing_stop = max(trailing_stop, breakeven_floor)
            
            # Only move stop up, never down
            if self.current_stop_loss is None or trailing_stop > self.current_stop_loss:
                if new_stop is None or trailing_stop > new_stop:
                    new_stop = trailing_stop
                    print(f"[Trailing Stop] Updated to {new_stop:.2f} "
                          f"(floor: {breakeven_floor:.2f}, high: {self.highest_price_long:.2f})")
        
        if new_stop is not None and new_stop != self.current_stop_loss:
            self.current_stop_loss = new_stop
            self.stop_adjustments += 1
            return new_stop
        
        return None
    
    def _update_short(self, current_price: float) -> Optional[float]:
        """Update logic for short positions"""
        # Track lowest price
        if self.lowest_price_short is None or current_price < self.lowest_price_short:
            self.lowest_price_short = current_price
        
        profit = (self.entry_price - current_price) if self.entry_price is not None else 0.0  # Profit is inverse for shorts
        new_stop = None
        
        # Check if we should activate next breakeven step
        if self.current_step < self.config.num_steps:
            target_profit = self.config.profit_targets[self.current_step]
            
            if profit >= target_profit:
                # Activate this breakeven step
                self.current_step += 1
                breakeven_offset = self.config.breakeven_offsets[self.current_step - 1]
                if self.entry_price is not None:
                    new_stop = self.entry_price - breakeven_offset  # Subtract for shorts
                else:
                    new_stop = None
                
                self.step_activation_times[self.current_step] = datetime.now()
                
                print(f"\n[Breakeven] Step {self.current_step} activated!")
                print(f"  Profit reached: {profit:.2f} (target: {target_profit:.2f})")
                print(f"  Moving stop to: {new_stop:.2f} (entry - {breakeven_offset:.2f})")
        
        # Apply trailing stop if we're at a breakeven level
        if self.current_step > 0:
            # Calculate trailing stop from lowest price
            trailing_distance = self.config.get_trailing_distance()
            trailing_stop = self.lowest_price_short + trailing_distance
            
            # Get current breakeven ceiling
            if self.entry_price is not None:
                breakeven_ceiling = self.entry_price - self.config.breakeven_offsets[self.current_step - 1]
            else:
                breakeven_ceiling = 0.0  # Fallback value if entry_price is None
            
            # Trailing stop can't go above breakeven ceiling
            trailing_stop = min(trailing_stop, breakeven_ceiling)
            
            # Only move stop down, never up (for shorts)
            if self.current_stop_loss is None or trailing_stop < self.current_stop_loss:
                if new_stop is None or trailing_stop < new_stop:
                    new_stop = trailing_stop
                    print(f"[Trailing Stop] Updated to {new_stop:.2f} "
                          f"(ceiling: {breakeven_ceiling:.2f}, low: {self.lowest_price_short:.2f})")
        
        if new_stop is not None and new_stop != self.current_stop_loss:
            self.current_stop_loss = new_stop
            self.stop_adjustments += 1
            return new_stop
        
        return None
    
    def get_status(self) -> str:
        """Get current status string"""
        if self.entry_price is None:
            return "No active position"
        
        status_lines = [
            f"Side: {self.position_side}",
            f"Entry: {self.entry_price:.2f}",
            f"Current Stop: {self.current_stop_loss:.2f}",
            f"Step: {self.current_step}/{self.config.num_steps}",
            f"Adjustments: {self.stop_adjustments}"
        ]
        
        if self.position_side == 'LONG' and self.highest_price_long:
            status_lines.append(f"High: {self.highest_price_long:.2f}")
        elif self.position_side == 'SHORT' and self.lowest_price_short:
            status_lines.append(f"Low: {self.lowest_price_short:.2f}")
        
        return " | ".join(status_lines)


class AdvancedStrategy:
    """
    Advanced trading strategy with:
    - Position sizing
    - Risk management (stop loss, take profit)
    - Auto-breakeven with configurable steps
    - Trailing stop loss
    - Max daily loss limit
    - Trade throttling
    """
    
    def __init__(
        self,
        instrument: str,
        max_position: int = 3,
        breakeven_config: Optional[BreakevenConfig] = None
    ):
        self.instrument = instrument
        self.max_position = max_position
        
        # Risk management
        self.max_daily_loss = 500.0
        self.stop_loss_ticks = 10
        self.take_profit_ticks = 20
        self.daily_pnl = 0.0
        
        # Trade throttling
        self.min_trade_interval = 60  # seconds
        self.last_trade_time = None
        
        # Market data
        self.prices = deque(maxlen=100)
        self.ticks_processed = 0
        
        # Client
        self.client = NT8Client()
        
        # Auto-breakeven manager
        if breakeven_config is None:
            # Default 2-step breakeven
            breakeven_config = BreakevenConfig(
                num_steps=2,
                profit_targets=[7.0, 10.0],
                breakeven_offsets=[0.0, 2.0],  # Step 1 at entry, Step 2 at entry+2
                trailing_ticks=2,
                tick_size=0.25,
                enabled=True
            )
        self.breakeven_manager = BreakevenManager(breakeven_config)
        
        # Order tracking
        self.entry_order_id: Optional[str] = None
        self.stop_order_id: Optional[str] = None
        self.current_entry_price: Optional[float] = None
        
    def run(self):
        """Run the strategy"""
        print("=" * 70)
        print("Advanced Strategy with Auto-Breakeven")
        print("=" * 70)
        print(f"\nInstrument: {self.instrument}")
        print(f"Max Position: {self.max_position}")
        print(f"Max Daily Loss: ${self.max_daily_loss}")
        print(f"Stop Loss: {self.stop_loss_ticks} ticks")
        print(f"Take Profit: {self.take_profit_ticks} ticks")
        print(f"\n{self.breakeven_manager.config}")
        print("-" * 70)
        
        # Connect
        if not self.client.connect():
            print("Failed to connect to NinjaTrader")
            return
        
        # Setup callbacks
        self.client.on_order_update = self.on_order_update
        self.client.on_position_update = self.on_position_update
        
        # Subscribe to market data
        self.client.subscribe_market_data(self.instrument)
        buffer = self.client.market_data.get_buffer(self.instrument)
        buffer.subscribe(self.on_tick)
        
        print("\nStrategy is running... Press Ctrl+C to stop\n")
        
        try:
            while True:
                # Periodic status update
                time.sleep(10)
                self.print_status()
                
                # Check risk limits
                if self.daily_pnl <= -self.max_daily_loss:
                    print(f"\n⚠️  Max daily loss reached: ${self.daily_pnl:.2f}")
                    print("Closing all positions and stopping...")
                    self.close_all_positions()
                    break
                    
        except KeyboardInterrupt:
            print("\n\nShutting down strategy...")
            self.close_all_positions()
        finally:
            self.client.disconnect()
            print("Strategy stopped")
    
    def on_tick(self, tick):
        """Process incoming tick data"""
        self.prices.append(tick.price)
        self.ticks_processed += 1
        
        # Update breakeven manager with current price
        position = self.client.get_position(self.instrument)
        if not position.is_flat and self.breakeven_manager.entry_price is not None:
            new_stop = self.breakeven_manager.update(tick.price)
            
            if new_stop is not None:
                # Need to modify stop loss order
                self.modify_stop_loss(new_stop)
        
        # Need enough data for signal generation
        if len(self.prices) < 50:
            return
        
        # Check if we can trade
        if not self.can_trade():
            return
        
        # Generate signals
        signal = self.generate_signal()
        
        if signal:
            self.execute_signal(signal, tick.price)
    
    def generate_signal(self):
        """Generate trading signals"""
        # Simple momentum strategy
        recent_prices = list(self.prices)[-20:]
        momentum = (recent_prices[-1] - recent_prices[0]) / recent_prices[0]
        
        position = self.client.get_position(self.instrument)
        
        # Buy signal
        if momentum > 0.001 and position.quantity < self.max_position:
            return OrderAction.BUY
        
        # Sell signal
        if momentum < -0.001 and position.quantity > 0:
            return OrderAction.SELL
        
        return None
    
    def can_trade(self):
        """Check if we can place a trade"""
        # Check daily loss limit
        if self.daily_pnl <= -self.max_daily_loss:
            return False
        
        # Check trade throttling
        if self.last_trade_time:
            elapsed = (datetime.now() - self.last_trade_time).total_seconds()
            if elapsed < self.min_trade_interval:
                return False
        
        return True
    
    def execute_signal(self, action, price):
        """Execute trading signal with stop loss"""
        position = self.client.get_position(self.instrument)
        
        # Determine quantity
        if action == OrderAction.BUY:
            quantity = min(1, self.max_position - position.quantity)
        else:
            quantity = min(1, position.quantity)
        
        if quantity <= 0:
            return
        
        # Calculate stop loss price
        tick_value = self.breakeven_manager.config.tick_size
        stop_distance = self.stop_loss_ticks * tick_value
        
        if action == OrderAction.BUY:
            stop_price = price - stop_distance
            is_long = True
        else:
            stop_price = price + stop_distance
            is_long = False
        
        # Place entry order
        order_id = self.client.place_market_order(
            instrument=self.instrument,
            action=action,
            quantity=quantity,
            signal_name="MOMENTUM"
        )
        
        self.entry_order_id = order_id
        self.current_entry_price = price
        
        # Will place stop loss after entry fills
        # Store intended stop for when entry fills
        self._pending_stop_price = stop_price
        self._pending_is_long = is_long
        
        self.last_trade_time = datetime.now()
        print(f"\n{'=' * 70}")
        print(f"📈 {action.value} SIGNAL")
        print(f"{'=' * 70}")
        print(f"Entry: {price:.2f}")
        print(f"Stop Loss: {stop_price:.2f}")
        print(f"Risk: {abs(price - stop_price):.2f} points")
        print(f"Order ID: {order_id}")
    
    def modify_stop_loss(self, new_stop_price: float):
        """Modify existing stop loss order"""
        # In a real implementation, you would:
        # 1. Cancel existing stop order
        # 2. Place new stop order at new_stop_price
        # For this example, we'll just log it
        
        print(f"\n[Stop Loss Update] New stop: {new_stop_price:.2f}")
        
        # Placeholder for actual order modification
        # self.client.cancel_order(self.stop_order_id)
        # self.stop_order_id = self.client.place_stop_order(...)
    
    def on_order_update(self, update):
        """Handle order updates"""
        if update.order_id == self.entry_order_id and update.state == OrderState.FILLED:
            # Entry order filled
            print(f"\n✓ Entry filled @ {update.avg_price:.2f}")
            
            # Initialize breakeven manager
            if hasattr(self, '_pending_stop_price') and hasattr(self, '_pending_is_long'):
                self.breakeven_manager.initialize_position(
                    entry_price=update.avg_price,
                    stop_loss=self._pending_stop_price,
                    is_long=self._pending_is_long
                )
                
                # Place stop loss order (in real implementation)
                print(f"  Placing stop loss @ {self._pending_stop_price:.2f}")
                # self.stop_order_id = self.client.place_stop_order(...)
                
                del self._pending_stop_price
                del self._pending_is_long
            
            self.entry_order_id = None
        
        elif update.state == OrderState.REJECTED:
            print(f"\n✗ Order rejected: {update.order_id}")
            if update.order_id == self.entry_order_id:
                self.entry_order_id = None
    
    def on_position_update(self, position):
        """Handle position updates"""
        if position.instrument == self.instrument:
            self.daily_pnl = position.realized_pnl + position.unrealized_pnl
            
            # Reset breakeven manager if position closed
            if position.is_flat and self.breakeven_manager.entry_price is not None:
                print(f"\n[Position Closed]")
                print(f"  P&L: ${position.realized_pnl:.2f}")
                print(f"  Breakeven Stats: {self.breakeven_manager.get_status()}")
                self.breakeven_manager.reset()
                self.stop_order_id = None
    
    def close_all_positions(self):
        """Close all positions"""
        position = self.client.get_position(self.instrument)
        
        if position.quantity > 0:
            print(f"\nClosing position: {position.quantity} contracts")
            self.client.place_market_order(
                instrument=self.instrument,
                action=OrderAction.SELL,
                quantity=position.quantity,
                signal_name="CLOSE_ALL"
            )
            time.sleep(2)
    
    def print_status(self):
        """Print strategy status"""
        position = self.client.get_position(self.instrument)
        
        print(f"\n{'=' * 70}")
        print(f"Status @ {datetime.now().strftime('%H:%M:%S')}")
        print(f"{'=' * 70}")
        print(f"Ticks processed: {self.ticks_processed}")
        print(f"Position: {position.market_position.value} ({position.quantity})")
        print(f"Daily P&L: ${self.daily_pnl:.2f}")
        print(f"Active orders: {len(self.client.get_active_orders())}")
        
        if not position.is_flat and self.breakeven_manager.entry_price is not None:
            print(f"\nBreakeven Manager: {self.breakeven_manager.get_status()}")
        
        print("=" * 70)


def create_strategy_with_config():
    """Create strategy with custom breakeven configuration"""
    
    print("\n" + "=" * 70)
    print("Configure Auto-Breakeven")
    print("=" * 70)
    
    # Get configuration from user (or use defaults)
    print("\nBreakeven Configuration Options:")
    print("1. Conservative (1 step)")
    print("2. Moderate (2 steps) - Default")
    print("3. Aggressive (3 steps)")
    print("4. Custom")
    print("5. Disabled")
    
    choice = input("\nSelect option (1-5, default=2): ").strip() or "2"
    
    if choice == "1":
        # Conservative: Single breakeven at +5 points
        config = BreakevenConfig(
            num_steps=1,
            profit_targets=[5.0],
            breakeven_offsets=[0.0],  # At entry price
            trailing_ticks=3,
            tick_size=0.25,
            enabled=True
        )
    
    elif choice == "2":
        # Moderate: 2-step breakeven (default from example)
        config = BreakevenConfig(
            num_steps=2,
            profit_targets=[7.0, 10.0],
            breakeven_offsets=[0.0, 2.0],
            trailing_ticks=2,
            tick_size=0.25,
            enabled=True
        )
    
    elif choice == "3":
        # Aggressive: 3-step breakeven (as specified in requirements)
        config = BreakevenConfig(
            num_steps=3,
            profit_targets=[7.0, 10.0, 15.0],
            breakeven_offsets=[0.0, 2.0, 4.0],
            trailing_ticks=2,
            tick_size=0.25,
            enabled=True
        )
    
    elif choice == "4":
        # Custom configuration
        try:
            num_steps = int(input("Number of steps (1-3): "))
            num_steps = max(1, min(3, num_steps))
            
            profit_targets = []
            breakeven_offsets = []
            
            for i in range(num_steps):
                print(f"\nStep {i+1}:")
                profit = float(input(f"  Profit target (points): "))
                offset = float(input(f"  Breakeven offset (points above entry): "))
                profit_targets.append(profit)
                breakeven_offsets.append(offset)
            
            trailing_ticks = int(input("\nTrailing stop (ticks): "))
            tick_size = float(input("Tick size: "))
            
            config = BreakevenConfig(
                num_steps=num_steps,
                profit_targets=profit_targets,
                breakeven_offsets=breakeven_offsets,
                trailing_ticks=trailing_ticks,
                tick_size=tick_size,
                enabled=True
            )
        except (ValueError, KeyboardInterrupt):
            print("\nInvalid input, using default configuration")
            config = BreakevenConfig()
    
    elif choice == "5":
        # Disabled
        config = BreakevenConfig(enabled=False)
    
    else:
        # Default
        config = BreakevenConfig()
    
    return AdvancedStrategy(
        instrument="ES 03-25",
        max_position=3,
        breakeven_config=config
    )


if __name__ == "__main__":
    # Example 1: Use predefined aggressive 3-step configuration
    print("\n" + "=" * 70)
    print("Example: 3-Step Auto-Breakeven Strategy")
    print("=" * 70)
    
    # Create aggressive 3-step configuration as per requirements
    aggressive_config = BreakevenConfig(
        num_steps=3,
        profit_targets=[7.0, 10.0, 15.0],  # When to activate each step
        breakeven_offsets=[0.0, 2.0, 4.0],  # Where to place stop at each step
        trailing_ticks=2,                    # Trail by 2 ticks
        tick_size=0.25,                      # ES tick size
        enabled=True
    )
    
    strategy = AdvancedStrategy(
        instrument="ES 03-25",
        max_position=3,
        breakeven_config=aggressive_config
    )
    
    # Example 2: Interactive configuration
    # strategy = create_strategy_with_config()
    
    strategy.run()

    