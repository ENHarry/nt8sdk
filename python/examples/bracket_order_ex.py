"""
Bracket Order Example

Demonstrates how to place bracket orders (entry + stop loss + take profit)
"""

from nt8 import NT8Client, OrderAction
import time


def place_simple_bracket():
    """Place a simple bracket order"""

    # Create client
    client = NT8Client()

    print("Connecting to NinjaTrader...")
    if not client.connect():
        print("Failed to connect")
        return

    instrument = "ES 03-25"

    # Define entry and exit prices
    entry_price = 4500.00  # Limit entry
    stop_loss = 4492.00  # 8 points risk
    take_profit = 4524.00  # 24 points reward (3:1 R:R)

    print(f"\n{'=' * 60}")
    print("Placing Bracket Order")
    print(f"{'=' * 60}")
    print(f"Instrument: {instrument}")
    print(f"Entry: {entry_price}")
    print(f"Stop Loss: {stop_loss}")
    print(f"Take Profit: {take_profit}")
    print(f"Risk: {entry_price - stop_loss:.2f} points")
    print(f"Reward: {take_profit - entry_price:.2f} points")
    print(f"R:R Ratio: {(take_profit - entry_price)/(entry_price - stop_loss):.1f}:1")

    # Place bracket order
    orders = client.place_bracket_order(
        instrument=instrument,
        action=OrderAction.BUY,
        quantity=1,
        entry_price=entry_price,  # Limit order entry
        stop_loss=stop_loss,
        take_profit=take_profit,
        signal_name="BRACKET_DEMO"
    )

    print(f"\nOrders placed successfully!")
    print(f"  Entry Order ID: {orders['entry_id']}")
    print(f"  Stop Loss ID: {orders['stop_id']}")
    print(f"  Take Profit ID: {orders['target_id']}")

    # Monitor for 30 seconds
    print("\nMonitoring orders for 30 seconds...")
    time.sleep(30)

    # Check position
    position = client.get_position(instrument)
    print(f"\nPosition Status:")
    print(f"  Side: {position.market_position.value}")
    print(f"  Quantity: {position.quantity}")
    print(f"  Unrealized P&L: ${position.unrealized_pnl:+,.2f}")

    # Get active orders
    active_orders = client.get_active_orders(instrument)
    print(f"\nActive Orders: {len(active_orders)}")
    for order in active_orders:
        print(f"  {order.order_id}: {order.order_type.value} {order.action.value} {order.quantity}")

    client.disconnect()
    print("\nDisconnected")


def place_market_bracket():
    """Place bracket order with market entry"""

    client = NT8Client()

    if not client.connect():
        return

    instrument = "ES 03-25"

    # Get current price
    latest_tick = client.get_latest_tick(instrument)
    if latest_tick is None:
        print("No market data available")
        client.disconnect()
        return

    current_price = latest_tick.price
    tick_size = 0.25
    stop_ticks = 8
    target_ticks = 24

    # Calculate stop and target based on current price
    stop_loss = current_price - (stop_ticks * tick_size)
    take_profit = current_price + (target_ticks * tick_size)

    print(f"\n{'=' * 60}")
    print("Market Entry Bracket Order")
    print(f"{'=' * 60}")
    print(f"Current Price: {current_price:.2f}")
    print(f"Stop Loss: {stop_loss:.2f} (-{stop_ticks} ticks)")
    print(f"Take Profit: {take_profit:.2f} (+{target_ticks} ticks)")

    # Place market entry bracket
    orders = client.place_bracket_order(
        instrument=instrument,
        action=OrderAction.BUY,
        quantity=1,
        entry_price=None,  # Market order
        stop_loss=stop_loss,
        take_profit=take_profit,
        signal_name="MKT_BRACKET"
    )

    print(f"\nOrders placed!")
    print(f"  Entry (Market): {orders['entry_id']}")
    print(f"  Stop Loss: {orders['stop_id']}")
    print(f"  Take Profit: {orders['target_id']}")

    # Wait for fill
    print("\nWaiting for entry fill...")
    time.sleep(5)

    position = client.get_position(instrument)
    if not position.is_flat:
        print(f"\n✓ Position opened:")
        print(f"  Quantity: {position.quantity}")
        print(f"  Avg Price: {position.avg_price:.2f}")
        print(f"  Unrealized P&L: ${position.unrealized_pnl:+,.2f}")
    else:
        print("\nNo position - entry may not have filled yet")

    client.disconnect()


def advanced_bracket_with_partial_exits():
    """Advanced bracket with scaled exits"""

    client = NT8Client()

    if not client.connect():
        return

    instrument = "ES 03-25"
    quantity = 3  # 3 contracts

    # Entry parameters
    entry_price = None  # Market
    current_price = 4500.00  # Simulated
    stop_loss = current_price - 8.0

    print(f"\n{'=' * 60}")
    print("Scaled Exit Bracket Strategy")
    print(f"{'=' * 60}")
    print(f"Entry: Market")
    print(f"Quantity: {quantity} contracts")
    print(f"Stop Loss: {stop_loss:.2f}")

    # Place entry
    entry_id = client.place_market_order(
        instrument=instrument,
        action=OrderAction.BUY,
        quantity=quantity,
        signal_name="SCALED_EXIT"
    )

    print(f"\nEntry Order: {entry_id}")

    # Wait for fill
    time.sleep(2)

    # Place stop for full position
    stop_id = client.place_stop_order(
        instrument=instrument,
        action=OrderAction.SELL,
        quantity=quantity,
        stop_price=stop_loss,
        signal_name="FULL_STOP"
    )

    # Place scaled profit targets
    target1_price = current_price + 4.0  # +4 points for 1 contract
    target2_price = current_price + 8.0  # +8 points for 1 contract
    target3_price = current_price + 16.0  # +16 points for 1 contract

    target1_id = client.place_limit_order(
        instrument=instrument,
        action=OrderAction.SELL,
        quantity=1,
        limit_price=target1_price,
        signal_name="TARGET_1"
    )

    target2_id = client.place_limit_order(
        instrument=instrument,
        action=OrderAction.SELL,
        quantity=1,
        limit_price=target2_price,
        signal_name="TARGET_2"
    )

    target3_id = client.place_limit_order(
        instrument=instrument,
        action=OrderAction.SELL,
        quantity=1,
        limit_price=target3_price,
        signal_name="TARGET_3"
    )

    print(f"\nStop Loss: {stop_id} @ {stop_loss:.2f}")
    print(f"Target 1: {target1_id} @ {target1_price:.2f} (1 contract)")
    print(f"Target 2: {target2_id} @ {target2_price:.2f} (1 contract)")
    print(f"Target 3: {target3_id} @ {target3_price:.2f} (1 contract)")

    print("\nScaled exit strategy active!")
    print("As each target is hit, stop loss should be adjusted (manually or via breakeven)")

    time.sleep(5)
    client.disconnect()


if __name__ == "__main__":
    print("Bracket Order Examples")
    print("=" * 60)
    print("\n1. Simple Limit Entry Bracket")
    print("2. Market Entry Bracket")
    print("3. Advanced Scaled Exits")

    choice = input("\nSelect example (1-3, default=1): ").strip() or "1"

    if choice == "1":
        place_simple_bracket()
    elif choice == "2":
        place_market_bracket()
    elif choice == "3":
        advanced_bracket_with_partial_exits()
    else:
        print("Invalid choice")
