"""
Account information and balance tracking for NT8
"""

from dataclasses import dataclass, field
from datetime import datetime
from typing import Optional, Dict, Callable
from enum import Enum


class AccountConnectionStatus(str, Enum):
    """Account connection status"""
    CONNECTED = "CONNECTED"
    DISCONNECTED = "DISCONNECTED"
    CONNECTING = "CONNECTING"
    UNKNOWN = "UNKNOWN"


@dataclass
class AccountInfo:
    """Account information snapshot"""
    account_name: str
    account_type: str = "Sim101"  # Sim101, Live, etc.
    connection_status: AccountConnectionStatus = AccountConnectionStatus.UNKNOWN

    # Balance information
    cash_value: float = 0.0
    buying_power: float = 0.0
    net_liquidation: float = 0.0
    total_cash_balance: float = 0.0

    # P&L tracking
    realized_pnl: float = 0.0
    unrealized_pnl: float = 0.0
    total_pnl: float = 0.0

    # Daily metrics
    daily_realized_pnl: float = 0.0
    daily_unrealized_pnl: float = 0.0
    daily_total_pnl: float = 0.0

    # Trading metrics
    total_trades_today: int = 0
    winning_trades_today: int = 0
    losing_trades_today: int = 0

    # Margin and risk
    initial_margin: float = 0.0
    maintenance_margin: float = 0.0
    excess_margin: float = 0.0
    margin_used: float = 0.0

    # Metadata
    last_update: Optional[datetime] = None
    currency: str = "USD"

    @property
    def total_balance(self) -> float:
        """Total account balance including unrealized P&L"""
        return self.total_cash_balance + self.unrealized_pnl

    @property
    def is_connected(self) -> bool:
        """Check if account is connected"""
        return self.connection_status == AccountConnectionStatus.CONNECTED

    @property
    def win_rate(self) -> float:
        """Calculate win rate for today"""
        total = self.winning_trades_today + self.losing_trades_today
        if total == 0:
            return 0.0
        return (self.winning_trades_today / total) * 100.0

    @property
    def available_buying_power(self) -> float:
        """Available buying power"""
        return max(0.0, self.buying_power - self.margin_used)

    def __str__(self) -> str:
        """String representation of account info"""
        lines = [
            f"Account: {self.account_name} ({self.account_type})",
            f"Status: {self.connection_status.value}",
            f"Balance: ${self.total_cash_balance:,.2f}",
            f"Net Liquidation: ${self.net_liquidation:,.2f}",
            f"Buying Power: ${self.buying_power:,.2f}",
            f"Available: ${self.available_buying_power:,.2f}",
            f"",
            f"Today's P&L: ${self.daily_total_pnl:+,.2f}",
            f"  Realized: ${self.daily_realized_pnl:+,.2f}",
            f"  Unrealized: ${self.daily_unrealized_pnl:+,.2f}",
            f"",
            f"Total P&L: ${self.total_pnl:+,.2f}",
            f"Trades Today: {self.total_trades_today} (Win Rate: {self.win_rate:.1f}%)",
        ]
        return "\n".join(lines)


@dataclass
class AccountUpdate:
    """Account update event"""
    account_name: str
    timestamp: datetime

    # Updated fields
    cash_value: Optional[float] = None
    buying_power: Optional[float] = None
    realized_pnl: Optional[float] = None
    unrealized_pnl: Optional[float] = None
    net_liquidation: Optional[float] = None

    update_type: str = "BALANCE"  # BALANCE, PNL, MARGIN, etc.

    def __str__(self) -> str:
        return f"AccountUpdate({self.account_name}, {self.update_type}, {self.timestamp})"


class AccountManager:
    """Manages account information and updates"""

    def __init__(self, account_name: str = "Sim101"):
        self.account_name = account_name
        self.account_info = AccountInfo(account_name=account_name)

        # Callbacks
        self.on_account_update: Optional[Callable[[AccountUpdate], None]] = None
        self.on_balance_change: Optional[Callable[[float, float], None]] = None
        self.on_pnl_change: Optional[Callable[[float], None]] = None

        # History tracking
        self.update_history: list[AccountUpdate] = []
        self.max_history_size = 1000

        # Daily reset tracking
        self._last_reset_date: Optional[datetime] = None

    def update_account(self, update: AccountUpdate):
        """Process account update"""
        # Store in history
        self.update_history.append(update)
        if len(self.update_history) > self.max_history_size:
            self.update_history.pop(0)

        # Update account info
        if update.cash_value is not None:
            old_balance = self.account_info.total_cash_balance
            self.account_info.cash_value = update.cash_value
            self.account_info.total_cash_balance = update.cash_value

            if self.on_balance_change:
                self.on_balance_change(old_balance, update.cash_value)

        if update.buying_power is not None:
            self.account_info.buying_power = update.buying_power

        if update.realized_pnl is not None:
            self.account_info.realized_pnl = update.realized_pnl
            self.account_info.total_pnl = self.account_info.realized_pnl + self.account_info.unrealized_pnl

        if update.unrealized_pnl is not None:
            self.account_info.unrealized_pnl = update.unrealized_pnl
            self.account_info.total_pnl = self.account_info.realized_pnl + self.account_info.unrealized_pnl

            if self.on_pnl_change:
                self.on_pnl_change(self.account_info.total_pnl)

        if update.net_liquidation is not None:
            self.account_info.net_liquidation = update.net_liquidation

        self.account_info.last_update = update.timestamp

        # Trigger callback
        if self.on_account_update:
            self.on_account_update(update)

        # Check if we need to reset daily metrics
        self._check_daily_reset(update.timestamp)

    def update_daily_pnl(self, realized: float, unrealized: float):
        """Update daily P&L metrics"""
        self.account_info.daily_realized_pnl = realized
        self.account_info.daily_unrealized_pnl = unrealized
        self.account_info.daily_total_pnl = realized + unrealized

    def update_daily_trades(self, total: int, wins: int, losses: int):
        """Update daily trade statistics"""
        self.account_info.total_trades_today = total
        self.account_info.winning_trades_today = wins
        self.account_info.losing_trades_today = losses

    def update_margin(self, initial: float, maintenance: float, used: float):
        """Update margin information"""
        self.account_info.initial_margin = initial
        self.account_info.maintenance_margin = maintenance
        self.account_info.margin_used = used
        self.account_info.excess_margin = self.account_info.buying_power - used

    def update_connection_status(self, status: AccountConnectionStatus):
        """Update connection status"""
        self.account_info.connection_status = status

    def _check_daily_reset(self, current_time: datetime):
        """Check if we need to reset daily metrics (new trading day)"""
        if self._last_reset_date is None:
            self._last_reset_date = current_time.date()
            return

        if current_time.date() > self._last_reset_date:
            # New trading day - reset daily metrics
            self.reset_daily_metrics()
            self._last_reset_date = current_time.date()

    def reset_daily_metrics(self):
        """Reset daily tracking metrics"""
        self.account_info.daily_realized_pnl = 0.0
        self.account_info.daily_unrealized_pnl = 0.0
        self.account_info.daily_total_pnl = 0.0
        self.account_info.total_trades_today = 0
        self.account_info.winning_trades_today = 0
        self.account_info.losing_trades_today = 0

    def get_account_info(self) -> AccountInfo:
        """Get current account information"""
        return self.account_info

    def get_balance(self) -> float:
        """Get current account balance"""
        return self.account_info.total_cash_balance

    def get_buying_power(self) -> float:
        """Get current buying power"""
        return self.account_info.buying_power

    def get_daily_pnl(self) -> float:
        """Get today's total P&L"""
        return self.account_info.daily_total_pnl

    def get_total_pnl(self) -> float:
        """Get total P&L"""
        return self.account_info.total_pnl

    def is_account_healthy(self, min_balance: float = 0.0, max_daily_loss: Optional[float] = None) -> tuple[bool, str]:
        """
        Check if account is in good health

        Args:
            min_balance: Minimum required balance
            max_daily_loss: Maximum allowed daily loss (positive number)

        Returns:
            Tuple of (is_healthy, reason)
        """
        if not self.account_info.is_connected:
            return False, "Account not connected"

        if self.account_info.total_cash_balance < min_balance:
            return False, f"Balance ${self.account_info.total_cash_balance:,.2f} below minimum ${min_balance:,.2f}"

        if max_daily_loss is not None and self.account_info.daily_total_pnl < -max_daily_loss:
            return False, f"Daily loss ${abs(self.account_info.daily_total_pnl):,.2f} exceeds limit ${max_daily_loss:,.2f}"

        if self.account_info.buying_power <= 0:
            return False, "No buying power available"

        return True, "Account healthy"

    def get_recent_updates(self, count: int = 10) -> list[AccountUpdate]:
        """Get recent account updates"""
        return self.update_history[-count:] if count > 0 else self.update_history[:]

    def __str__(self) -> str:
        return str(self.account_info)
