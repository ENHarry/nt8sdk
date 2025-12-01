"""
Comprehensive Risk-Managed Trading Strategy Example

Demonstrates:
- Account monitoring
- Risk management with position sizing
- Daily loss limits
- Consecutive loss protection
- Trading time restrictions
"""

from nt8 import (
    NT8Client, OrderAction, RiskManager, RiskLimits,
    PositionSizer, BreakevenConfig, BreakevenManager
)
from datetime import time as datetime_time
from collections import deque
import time


class RiskManagedStrategy:
    """
    Trading strategy with comprehensive risk management
    """

    def __init__(
        self,
        instrument: str = "ES 03-25",
        account_balance: float = 50000.0,
        tick_size: float = 0.25,
        tick_value: float = 12.50
    ):
        self.instrument = instrument
        self.tick_size = tick_size
        self.tick_value = tick_value

        # NT8 Client
        self.client = NT8Client(account_name="Sim101")

        # Configure risk limits
        self.risk_limits = RiskLimits(
            max_contracts_per_trade=2,
            max_total_contracts=6,
            max_instruments=3,
            max_risk_per_trade=200.0,  # $200 max risk per trade
            max_daily_loss=500.0,  # $500 max daily loss
            risk_per_trade_pct=2.0,  # Risk 2% of account per trade
            max_position_size_pct=15.0,
            daily_profit_target=1000.0,  # Stop after $1000 profit
            trading_start_time=datetime_time(9, 30),  # 9:30 AM
            trading_end_time=datetime_time(16, 0),  # 4:00 PM
            max_consecutive_losses=3,
            cool_down_after_losses=300  # 5 minutes
        )

        # Risk manager
        self.risk_manager = RiskManager(self.risk_limits, account_balance)

        # Position sizer
        self.position_sizer = PositionSizer(account_balance, self.risk_limits)

        # Breakeven configuration (dynamic tick size)
        self.breakeven_config = BreakevenConfig(
            num_steps=3,
            profit_targets=[7.0, 10.0, 15.0],
            breakeven_offsets=[0.0, 2.0, 4.0],
            trailing_ticks=2,
            tick_size=tick_size,
            enabled=True,
            instrument=instrument
        )
        self.breakeven_manager = BreakevenManager(self.breakeven_config)

        # Market data
        self.prices = deque(maxlen=100)

        # Callbacks
        self.setup_callbacks()

    def setup_callbacks(self):
        """Setup risk management callbacks"""
        # Account callbacks
        self.client.account_manager.on_balance_change = self.on_balance_change
        self.client.account_manager.on_pnl_change = self.on_pnl_change

        # Risk manager callbacks
        self.risk_manager.on_risk_violation = self.on_risk_violation
        self.risk_manager.on_limit_reached = self.on_limit_reached

        # Client callbacks
        self.client.on_order_update = self.on_order_update
        self.client.on_position_update = self.on_position_update
        self.client.on_account_update = self.on_account_update

    def run(self):
        """Run the strategy"""
        print("=" * 70)
        print("Risk-Managed Trading Strategy")
        print("=" * 70)
        print(f"\nInstrument: {self.instrument}")
        print(f"Account Balance: ${self.position_sizer.account_balance:,.2f}")
        print(f"\nRisk Limits:")
        print(f"  Max Risk Per Trade: ${self.risk_limits.max_risk_per_trade:.2f}")
        print(f"  Max Daily Loss: ${self.risk_limits.max_daily_loss:.2f}")
        print(f"  Risk % Per Trade: {self.risk_limits.risk_per_trade_pct}%")
        print(f"  Daily Profit Target: ${self.risk_limits.daily_profit_target:.2f}")
        print(f"  Trading Hours: {self.risk_limits.trading_start_time} - {self.risk_limits.trading_end_time}")
        print(f"  Max Consecutive Losses: {self.risk_limits.max_consecutive_losses}")
        print("-" * 70)

        # Connect
        if not self.client.connect():
            print("Failed to connect to NinjaTrader")
            return

        # Request account update
        self.client.request_account_update()

        # Subscribe to market data
        self.client.subscribe_market_data(self.instrument)
        buffer = self.client.market_data.get_buffer(self.instrument)
        buffer.subscribe(self.on_tick)

        print("\nStrategy is running... Press Ctrl+C to stop\n")

        try:
            while True:
                time.sleep(15)
                self.print_status()

                # Check if trading should be disabled
                if not self.risk_manager.trading_enabled:
                    print(f"\n\nTrading disabled: {self.risk_manager.shutdown_reason}")
                    print("Closing all positions and exiting...")
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

        # Update breakeven manager
        position = self.client.get_position(self.instrument)
        if not position.is_flat and self.breakeven_manager.entry_price is not None:
            new_stop = self.breakeven_manager.update(tick.price)
            if new_stop is not None:
                # Modify stop loss
                print(f"[Auto-Breakeven] Adjusting stop to {new_stop:.2f}")
                # self.client.modify_order(stop_order_id, stop_price=new_stop)

        # Need enough data for signals
        if len(self.prices) < 50:
            return

        # Generate signal
        signal = self.generate_signal()
        if signal:
            self.execute_signal(signal, tick.price)

    def generate_signal(self):
        """Generate trading signals (simple momentum)"""
        recent_prices = list(self.prices)[-20:]
        momentum = (recent_prices[-1] - recent_prices[0]) / recent_prices[0]

        position = self.client.get_position(self.instrument)

        # Buy signal
        if momentum > 0.002 and position.quantity < self.risk_limits.max_total_contracts:
            return OrderAction.BUY

        # Sell signal
        if momentum < -0.002 and position.quantity > 0:
            return OrderAction.SELL

        return None

    def execute_signal(self, action, current_price):
        """Execute trade with risk management"""
        # Check if trade is allowed
        can_trade, reason = self.risk_manager.can_trade(self.instrument, 1)
        if not can_trade:
            print(f"\n[Trade Blocked] {reason}")
            return

        # Calculate stop loss based on ATR or fixed ticks (using fixed for demo)
        stop_loss_ticks = 8
        stop_distance = stop_loss_ticks * self.tick_size

        if action == OrderAction.BUY:
            stop_price = current_price - stop_distance
            is_long = True
        else:
            stop_price = current_price + stop_distance
            is_long = False

        # Calculate optimal position size
        quantity = self.position_sizer.calculate_position_size(
            entry_price=current_price,
            stop_loss=stop_price,
            tick_size=self.tick_size,
            tick_value=self.tick_value,
            max_contracts=self.risk_limits.max_contracts_per_trade
        )

        if quantity == 0:
            print("[Position Sizing] Calculated size is 0 - risk too high")
            return

        # Validate trade risk
        valid_risk, risk_reason = self.risk_manager.validate_trade_risk(
            entry_price=current_price,
            stop_loss=stop_price,
            quantity=quantity,
            tick_size=self.tick_size,
            tick_value=self.tick_value
        )

        if not valid_risk:
            print(f"[Risk Check Failed] {risk_reason}")
            return

        # Calculate risk metrics
        points_risk = abs(current_price - stop_price)
        ticks_risk = points_risk / self.tick_size
        dollar_risk = quantity * ticks_risk * self.tick_value

        print(f"\n{'=' * 70}")
        print(f"📊 {action.value} SIGNAL - RISK VALIDATED")
        print(f"{'=' * 70}")
        print(f"Entry Price: {current_price:.2f}")
        print(f"Stop Loss: {stop_price:.2f}")
        print(f"Quantity: {quantity} contracts")
        print(f"Risk per Contract: ${dollar_risk/quantity:.2f}")
        print(f"Total Risk: ${dollar_risk:.2f}")
        print(f"Risk as % of Account: {(dollar_risk/self.position_sizer.account_balance)*100:.2f}%")

        # Place bracket order (entry + stop + target)
        target_price = current_price + (3 * stop_distance) if is_long else current_price - (3 * stop_distance)

        orders = self.client.place_bracket_order(
            instrument=self.instrument,
            action=action,
            quantity=quantity,
            entry_price=None,  # Market order
            stop_loss=stop_price,
            take_profit=target_price,
            signal_name="RISK_MANAGED"
        )

        print(f"Orders Placed:")
        print(f"  Entry: {orders['entry_id']}")
        print(f"  Stop: {orders['stop_id']}")
        print(f"  Target: {orders['target_id']}")

        # Register trade with risk manager
        self.risk_manager.register_trade(self.instrument, quantity, is_long)

    def on_order_update(self, update):
        """Handle order updates"""
        print(f"[Order Update] {update.order_id}: {update.state.value}")

        if update.state.value == "FILLED":
            print(f"  Filled @ {update.avg_price:.2f}, Qty: {update.filled}")

    def on_position_update(self, position):
        """Handle position updates"""
        if position.instrument == self.instrument:
            self.risk_manager.update_daily_pnl(position.realized_pnl + position.unrealized_pnl)

            # If position closed, update risk manager
            if position.is_flat and self.breakeven_manager.entry_price is not None:
                pnl = position.realized_pnl
                print(f"\n[Position Closed] P&L: ${pnl:+,.2f}")

                # This would normally get quantity from the closed position
                self.risk_manager.close_position(self.instrument, 1, pnl)
                self.breakeven_manager.reset()

    def on_account_update(self, update):
        """Handle account updates"""
        print(f"[Account Update] {update.account_name}: {update.update_type}")
        if update.buying_power is not None:
            print(f"  Buying Power: ${update.buying_power:,.2f}")

        # Update position sizer with new balance
        if update.cash_value is not None:
            self.position_sizer.update_account_balance(update.cash_value)

    def on_balance_change(self, old_balance, new_balance):
        """Handle balance changes"""
        change = new_balance - old_balance
        print(f"[Balance Change] ${old_balance:,.2f} → ${new_balance:,.2f} ({change:+,.2f})")

    def on_pnl_change(self, total_pnl):
        """Handle P&L changes"""
        risk_level = self.risk_manager.get_risk_level()
        print(f"[P&L Update] ${total_pnl:+,.2f} | Risk Level: {risk_level.value}")

    def on_risk_violation(self, message, level):
        """Handle risk violations"""
        print(f"\n⚠️  RISK ALERT [{level.value}]: {message}\n")

    def on_limit_reached(self, reason):
        """Handle limit reached"""
        print(f"\n🛑 LIMIT REACHED: {reason}\n")
        print("Trading will be disabled!")

    def close_all_positions(self):
        """Close all positions"""
        position = self.client.get_position(self.instrument)

        if position.quantity > 0:
            print(f"\nClosing {position.quantity} contracts...")
            self.client.cancel_all_orders(self.instrument)
            time.sleep(0.5)

            self.client.place_market_order(
                instrument=self.instrument,
                action=OrderAction.SELL,
                quantity=position.quantity,
                signal_name="CLOSE_ALL"
            )
            time.sleep(1)

    def print_status(self):
        """Print strategy status"""
        position = self.client.get_position(self.instrument)
        account = self.client.get_account_info()
        risk_metrics = self.risk_manager.get_risk_metrics()

        print(f"\n{'=' * 70}")
        print("STRATEGY STATUS")
        print(f"{'=' * 70}")

        print("\nAccount:")
        print(f"  Balance: ${account.total_cash_balance:,.2f}")
        print(f"  Buying Power: ${account.buying_power:,.2f}")
        print(f"  Daily P&L: ${account.daily_total_pnl:+,.2f}")
        print(f"  Total P&L: ${account.total_pnl:+,.2f}")

        print("\nPosition:")
        print(f"  Side: {position.market_position.value}")
        print(f"  Quantity: {position.quantity}")
        print(f"  Unrealized P&L: ${position.unrealized_pnl:+,.2f}")

        print("\nRisk Metrics:")
        print(f"  Trading Enabled: {risk_metrics['trading_enabled']}")
        print(f"  Risk Level: {risk_metrics['risk_level']}")
        print(f"  Daily Trades: {risk_metrics['daily_trades']}")
        print(f"  Consecutive Losses: {risk_metrics['consecutive_losses']}")
        print(f"  Active Instruments: {risk_metrics['active_instruments']}")
        print(f"  Total Contracts: {risk_metrics['total_contracts']}")
        print(f"  Daily Loss Used: {risk_metrics['daily_loss_used_pct']:.1f}%")

        if not position.is_flat and self.breakeven_manager.entry_price is not None:
            print(f"\nBreakeven: {self.breakeven_manager.get_status()}")

        print("=" * 70)


if __name__ == "__main__":
    strategy = RiskManagedStrategy(
        instrument="ES 03-25",
        account_balance=50000.0,
        tick_size=0.25,
        tick_value=12.50
    )
    strategy.run()
