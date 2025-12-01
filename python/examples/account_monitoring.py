"""
Account Monitoring Example

Demonstrates real-time account balance and P&L tracking
"""

from nt8 import NT8Client
import time
from datetime import datetime


class AccountMonitor:
    """Real-time account monitoring"""

    def __init__(self, account_name: str = "Sim101"):
        self.client = NT8Client(account_name=account_name)
        self.account_name = account_name

        # Setup callbacks
        self.client.on_account_update = self.on_account_update
        self.client.account_manager.on_balance_change = self.on_balance_change
        self.client.account_manager.on_pnl_change = self.on_pnl_change

        # Statistics
        self.update_count = 0
        self.start_balance = 0.0
        self.start_time = datetime.now()

    def run(self):
        """Run the monitor"""
        print("=" * 70)
        print(f"Account Monitoring: {self.account_name}")
        print("=" * 70)

        # Connect
        if not self.client.connect():
            print("Failed to connect to NinjaTrader")
            return

        # Request initial account update
        print("\nRequesting account information...")
        self.client.request_account_update()

        time.sleep(2)

        # Display initial account info
        account_info = self.client.get_account_info()
        self.start_balance = account_info.total_cash_balance

        print("\nInitial Account Status:")
        print(account_info)
        print("-" * 70)

        print("\nMonitoring account updates... Press Ctrl+C to stop\n")

        try:
            while True:
                time.sleep(10)
                self.print_summary()

        except KeyboardInterrupt:
            print("\n\nStopping monitor...")
        finally:
            self.print_final_summary()
            self.client.disconnect()
            print("Disconnected")

    def on_account_update(self, update):
        """Handle account updates"""
        self.update_count += 1

        print(f"\n[Update #{self.update_count}] {update.timestamp.strftime('%H:%M:%S')}")
        print(f"  Type: {update.update_type}")

        if update.cash_value is not None:
            print(f"  Cash Value: ${update.cash_value:,.2f}")

        if update.buying_power is not None:
            print(f"  Buying Power: ${update.buying_power:,.2f}")

        if update.realized_pnl is not None:
            print(f"  Realized P&L: ${update.realized_pnl:+,.2f}")

        if update.unrealized_pnl is not None:
            print(f"  Unrealized P&L: ${update.unrealized_pnl:+,.2f}")

        if update.net_liquidation is not None:
            print(f"  Net Liquidation: ${update.net_liquidation:,.2f}")

    def on_balance_change(self, old_balance, new_balance):
        """Handle balance changes"""
        change = new_balance - old_balance
        pct_change = (change / old_balance * 100) if old_balance > 0 else 0

        print(f"\n💰 BALANCE CHANGE:")
        print(f"  Previous: ${old_balance:,.2f}")
        print(f"  Current: ${new_balance:,.2f}")
        print(f"  Change: ${change:+,.2f} ({pct_change:+.2f}%)")

    def on_pnl_change(self, total_pnl):
        """Handle P&L changes"""
        account = self.client.get_account_info()

        print(f"\n📊 P&L UPDATE: ${total_pnl:+,.2f}")
        print(f"  Daily P&L: ${account.daily_total_pnl:+,.2f}")
        print(f"  Realized: ${account.realized_pnl:+,.2f}")
        print(f"  Unrealized: ${account.unrealized_pnl:+,.2f}")

        # Show warning if approaching daily loss limit
        max_daily_loss = 500.0  # Example limit
        if account.daily_total_pnl < 0:
            loss_pct = abs(account.daily_total_pnl / max_daily_loss) * 100
            if loss_pct >= 80:
                print(f"  ⚠️  WARNING: Using {loss_pct:.1f}% of daily loss limit!")

    def print_summary(self):
        """Print periodic summary"""
        account = self.client.get_account_info()

        print(f"\n{'=' * 70}")
        print(f"ACCOUNT SUMMARY - {datetime.now().strftime('%H:%M:%S')}")
        print(f"{'=' * 70}")

        # Balance
        balance_change = account.total_cash_balance - self.start_balance
        balance_change_pct = (balance_change / self.start_balance * 100) if self.start_balance > 0 else 0

        print(f"\nBalance:")
        print(f"  Starting: ${self.start_balance:,.2f}")
        print(f"  Current: ${account.total_cash_balance:,.2f}")
        print(f"  Change: ${balance_change:+,.2f} ({balance_change_pct:+.2f}%)")
        print(f"  Buying Power: ${account.buying_power:,.2f}")

        # P&L
        print(f"\nP&L:")
        print(f"  Daily: ${account.daily_total_pnl:+,.2f}")
        print(f"    Realized: ${account.daily_realized_pnl:+,.2f}")
        print(f"    Unrealized: ${account.daily_unrealized_pnl:+,.2f}")
        print(f"  Total: ${account.total_pnl:+,.2f}")

        # Trading activity
        print(f"\nTrading Activity:")
        print(f"  Trades Today: {account.total_trades_today}")
        if account.total_trades_today > 0:
            print(f"  Winners: {account.winning_trades_today}")
            print(f"  Losers: {account.losing_trades_today}")
            print(f"  Win Rate: {account.win_rate:.1f}%")

        # Health check
        is_healthy, reason = self.client.is_account_healthy(
            min_balance=10000.0,
            max_daily_loss=500.0
        )
        health_icon = "✓" if is_healthy else "✗"
        print(f"\nAccount Health: {health_icon} {reason}")

        print(f"\nUpdates Received: {self.update_count}")
        print("=" * 70)

    def print_final_summary(self):
        """Print final summary"""
        account = self.client.get_account_info()
        elapsed = (datetime.now() - self.start_time).total_seconds()

        print(f"\n{'=' * 70}")
        print("FINAL SUMMARY")
        print(f"{'=' * 70}")
        print(f"Monitoring Duration: {elapsed:.0f} seconds ({elapsed/60:.1f} minutes)")
        print(f"Total Updates: {self.update_count}")
        print(f"Starting Balance: ${self.start_balance:,.2f}")
        print(f"Ending Balance: ${account.total_cash_balance:,.2f}")

        balance_change = account.total_cash_balance - self.start_balance
        print(f"Change: ${balance_change:+,.2f}")

        print(f"\nFinal P&L:")
        print(f"  Daily: ${account.daily_total_pnl:+,.2f}")
        print(f"  Total: ${account.total_pnl:+,.2f}")

        print("=" * 70)


def simple_account_check():
    """Simple one-time account check"""

    print("Simple Account Check")
    print("=" * 70)

    client = NT8Client(account_name="Sim101")

    if not client.connect():
        print("Failed to connect")
        return

    # Request account update
    client.request_account_update()
    time.sleep(2)

    # Get account info
    account = client.get_account_info()

    print(f"\nAccount: {account.account_name}")
    print(f"Type: {account.account_type}")
    print(f"Status: {account.connection_status.value}")
    print(f"\nBalance: ${account.total_cash_balance:,.2f}")
    print(f"Buying Power: ${account.buying_power:,.2f}")
    print(f"Net Liquidation: ${account.net_liquidation:,.2f}")
    print(f"\nDaily P&L: ${account.daily_total_pnl:+,.2f}")
    print(f"Total P&L: ${account.total_pnl:+,.2f}")

    # Check health
    is_healthy, reason = client.is_account_healthy(min_balance=5000.0)
    print(f"\nAccount Health: {reason}")

    client.disconnect()
    print("\nDone")


if __name__ == "__main__":
    print("\nAccount Monitoring Examples")
    print("=" * 70)
    print("1. Real-time Account Monitoring")
    print("2. Simple Account Check")

    choice = input("\nSelect example (1-2, default=1): ").strip() or "1"

    if choice == "1":
        monitor = AccountMonitor(account_name="Sim101")
        monitor.run()
    elif choice == "2":
        simple_account_check()
    else:
        print("Invalid choice")
