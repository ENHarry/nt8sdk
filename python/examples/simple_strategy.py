from python.nt8.client import NT8Client, OrderAction
from collections import deque
import time


class SimpleStrategy:
    def __init__(self, instrument: str, fast_period: int = 10, slow_period: int = 20):
        self.instrument = instrument
        self.fast_period = fast_period
        self.slow_period = slow_period
        
        self.prices = deque(maxlen=slow_period)
        self.client = NT8Client()
        
    def run(self):
        """Run the strategy"""
        # Connect to NT8
        if not self.client.connect():
            print("Failed to connect to NinjaTrader")
            return
        
        # Subscribe to market data
        self.client.subscribe_market_data(self.instrument)
        
        # Get the buffer and subscribe to tick updates
        buffer = self.client.market_data.get_buffer(self.instrument)
        buffer.subscribe(self.on_tick)
        
        print(f"Strategy running for {self.instrument}")
        print(f"Fast MA: {self.fast_period} | Slow MA: {self.slow_period}")
        
        try:
            while True:
                time.sleep(1)
        except KeyboardInterrupt:
            print("\\nShutting down strategy...")
            self.client.disconnect()
    
    def on_tick(self, tick):
        """Process incoming tick data"""
        self.prices.append(tick.price)
        
        # Need enough data for slow MA
        if len(self.prices) < self.slow_period:
            return
        
        # Calculate moving averages
        fast_ma = sum(list(self.prices)[-self.fast_period:]) / self.fast_period
        slow_ma = sum(self.prices) / self.slow_period
        
        # Get current position
        position = self.client.get_position(self.instrument)
        
        # Generate signals
        if fast_ma > slow_ma and position.is_flat:
            print(f"BUY signal at {tick.price:.2f} (Fast MA: {fast_ma:.2f} > Slow MA: {slow_ma:.2f})")
            self.client.place_market_order(
                instrument=self.instrument,
                action=OrderAction.BUY,
                quantity=1,
                signal_name="MA_Cross"
            )
        
        elif fast_ma < slow_ma and position.is_long:
            print(f"SELL signal at {tick.price:.2f} (Fast MA: {fast_ma:.2f} < Slow MA: {slow_ma:.2f})")
            self.client.place_market_order(
                instrument=self.instrument,
                action=OrderAction.SELL,
                quantity=1,
                signal_name="MA_Cross"
            )


if __name__ == "__main__":
    strategy = SimpleStrategy(instrument="ES 03-25", fast_period=10, slow_period=20)
    strategy.run()