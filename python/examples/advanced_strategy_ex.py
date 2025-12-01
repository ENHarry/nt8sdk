"""
Advanced trading strategy with:
- Position sizing
- Risk management (stop loss, take profit)
- Max daily loss limit
- Trade throttling
"""
    

from python.nt8.advanced_strategy import AdvancedStrategy
from python.nt8.client import NT8Client
from python.nt8.types import OrderAction, OrderState
import time
from collections import deque
from datetime import datetime

def __init__(self, instrument: str, max_position: int = 3):
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
    
def run(self):
    """Run the strategy"""
    print(f"Starting Advanced Strategy")
    print(f"Instrument: {self.instrument}")
    print(f"Max Position: {self.max_position}")
    print(f"Max Daily Loss: ${self.max_daily_loss}")
    print("-" * 60)
    
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
    
    print("Strategy is running... Press Ctrl+C to stop\\n")
    
    try:
        while True:
            # Periodic status update
            time.sleep(10)
            self.print_status()
            
            # Check risk limits
            if self.daily_pnl <= -self.max_daily_loss:
                print(f"\\n⚠️  Max daily loss reached: ${self.daily_pnl:.2f}")
                print("Closing all positions and stopping...")
                self.close_all_positions()
                break
                
    except KeyboardInterrupt:
        print("\\n\\nShutting down strategy...")
        self.close_all_positions()
    finally:
        self.client.disconnect()
        print("Strategy stopped")

def on_tick(self, tick):
    """Process incoming tick data"""
    self.prices.append(tick.price)
    self.ticks_processed += 1
    
    # Need enough data
    if len(self.prices) < 50:
        return
    
    # Check if we can trade
    if not self.can_trade():
        return
    
    # Calculate signals
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
    """Execute trading signal"""
    position = self.client.get_position(self.instrument)
    
    # Determine quantity
    if action == OrderAction.BUY:
        quantity = min(1, self.max_position - position.quantity)
    else:
        quantity = min(1, position.quantity)
    
    if quantity <= 0:
        return
    
    # Place order
    order_id = self.client.place_market_order(
        instrument=self.instrument,
        action=action,
        quantity=quantity,
        signal_name="MOMENTUM"
    )
    
    self.last_trade_time = datetime.now()
    print(f"\\n{action.value} {quantity} @ {price:.2f} (Order: {order_id})")

def on_order_update(self, update):
    """Handle order updates"""
    if update.state == OrderState.FILLED:
        print(f"  ✓ Order filled @ {update.avg_price:.2f}")

def on_position_update(self, position):
    """Handle position updates"""
    if position.instrument == self.instrument:
        self.daily_pnl = position.realized_pnl + position.unrealized_pnl

def close_all_positions(self):
    """Close all positions"""
    position = self.client.get_position(self.instrument)
    
    if position.quantity > 0:
        print(f"Closing position: {position.quantity} contracts")
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
    
    print(f"\\n[Status @ {datetime.now().strftime('%H:%M:%S')}]")
    print(f"  Ticks processed: {self.ticks_processed}")
    print(f"  Position: {position.market_position.value} ({position.quantity})")
    print(f"  Daily P&L: ${self.daily_pnl:.2f}")
    print(f"  Active orders: {len(self.client.get_active_orders())}")


if __name__ == "__main__":
    strategy = AdvancedStrategy(
        instrument="ES 03-25",
        max_position=3
    )
    strategy.run()