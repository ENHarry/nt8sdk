"""
Example: Stream and display market data
"""

from python.nt8.client import NT8Client
import time


def print_tick(tick):
    """Callback to print tick data"""
    print(f"{tick.instrument:12} | "
          f"Time: {tick.timestamp.strftime('%H:%M:%S.%f')[:-3]} | "
          f"Price: {tick.price:8.2f} | "
          f"Bid: {tick.bid:8.2f} | "
          f"Ask: {tick.ask:8.2f} | "
          f"Volume: {tick.volume:6}")


def main():
    client = NT8Client()
    
    # Connect
    print("Connecting to NinjaTrader 8...")
    if not client.connect():
        print("Failed to connect. Make sure NinjaTrader is running and adapter is loaded.")
        return
    
    print("Connected successfully!")
    
    # Subscribe to multiple instruments
    instruments = ["ES 03-25", "NQ 03-25"]
    for instrument in instruments:
        print(f"Subscribing to {instrument}...")
        client.subscribe_market_data(instrument)
        buffer = client.market_data.get_buffer(instrument)
        buffer.subscribe(print_tick)
    
    print("\\nStreaming market data... Press Ctrl+C to stop")
    print("-" * 80)
    
    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        print("\\n\\nShutting down...")
        client.disconnect()
        print("Disconnected.")


if __name__ == "__main__":
    main()