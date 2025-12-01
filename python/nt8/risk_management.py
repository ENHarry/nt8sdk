"""
Advanced Risk Management for NT8 Trading

Provides position sizing, risk controls, and exposure management
"""

from dataclasses import dataclass, field
from datetime import datetime, time as datetime_time
from typing import Optional, Dict, List, Callable
from enum import Enum
import math


class RiskLevel(str, Enum):
    """Risk level classification"""
    LOW = "LOW"
    MEDIUM = "MEDIUM"
    HIGH = "HIGH"
    CRITICAL = "CRITICAL"


@dataclass
class RiskLimits:
    """Risk management limits configuration"""

    # Position limits
    max_contracts_per_trade: int = 3
    max_total_contracts: int = 10
    max_instruments: int = 5

    # Dollar risk limits
    max_risk_per_trade: float = 100.0  # Max $ risk per trade
    max_daily_loss: float = 500.0  # Max daily loss
    max_total_loss: float = 2000.0  # Max total loss before shutdown

    # P&L targets
    daily_profit_target: Optional[float] = None  # Stop trading after reaching
    max_daily_profit: Optional[float] = None  # Cap daily profit

    # Position sizing
    risk_per_trade_pct: float = 1.0  # % of account to risk per trade
    max_position_size_pct: float = 10.0  # Max % of account in one position

    # Trading time restrictions
    trading_start_time: Optional[datetime_time] = None
    trading_end_time: Optional[datetime_time] = None

    # Consecutive loss limits
    max_consecutive_losses: int = 3
    cool_down_after_losses: int = 300  # seconds

    def __post_init__(self):
        """Validate limits"""
        if self.max_risk_per_trade <= 0:
            raise ValueError("max_risk_per_trade must be positive")
        if self.max_daily_loss <= 0:
            raise ValueError("max_daily_loss must be positive")
        if not (0 < self.risk_per_trade_pct <= 100):
            raise ValueError("risk_per_trade_pct must be between 0 and 100")


@dataclass
class TradeRiskMetrics:
    """Risk metrics for a single trade"""
    instrument: str
    entry_price: float
    stop_loss: float
    quantity: int
    tick_size: float
    tick_value: float

    @property
    def risk_per_contract(self) -> float:
        """Calculate risk per contract in dollars"""
        points_at_risk = abs(self.entry_price - self.stop_loss)
        ticks_at_risk = points_at_risk / self.tick_size
        return ticks_at_risk * self.tick_value

    @property
    def total_risk(self) -> float:
        """Total dollar risk for the trade"""
        return self.risk_per_contract * self.quantity

    @property
    def risk_reward_ratio(self) -> Optional[float]:
        """Calculate risk/reward if target provided"""
        # This would need target price, returning None for now
        return None


class PositionSizer:
    """Calculate optimal position sizes based on risk"""

    def __init__(self, account_balance: float, risk_limits: RiskLimits):
        self.account_balance = account_balance
        self.risk_limits = risk_limits

    def calculate_position_size(
        self,
        entry_price: float,
        stop_loss: float,
        tick_size: float,
        tick_value: float,
        max_contracts: Optional[int] = None
    ) -> int:
        """
        Calculate position size based on risk parameters

        Args:
            entry_price: Entry price for the trade
            stop_loss: Stop loss price
            tick_size: Instrument tick size (e.g., 0.25 for ES)
            tick_value: Dollar value per tick (e.g., 12.50 for ES)
            max_contracts: Optional maximum contracts override

        Returns:
            Number of contracts to trade
        """
        # Calculate risk per contract
        points_at_risk = abs(entry_price - stop_loss)
        if points_at_risk == 0:
            return 0

        ticks_at_risk = points_at_risk / tick_size
        risk_per_contract = ticks_at_risk * tick_value

        # Calculate based on dollar risk limit
        max_by_dollar_risk = int(self.risk_limits.max_risk_per_trade / risk_per_contract)

        # Calculate based on account percentage
        account_risk_dollars = self.account_balance * (self.risk_limits.risk_per_trade_pct / 100.0)
        max_by_account_pct = int(account_risk_dollars / risk_per_contract)

        # Apply position size limits
        position_size = min(
            max_by_dollar_risk,
            max_by_account_pct,
            self.risk_limits.max_contracts_per_trade
        )

        # Apply max contracts override if provided
        if max_contracts is not None:
            position_size = min(position_size, max_contracts)

        return max(0, position_size)

    def calculate_stop_loss(
        self,
        entry_price: float,
        is_long: bool,
        risk_amount: float,
        tick_size: float,
        tick_value: float
    ) -> float:
        """
        Calculate stop loss price based on risk amount

        Args:
            entry_price: Entry price
            is_long: True for long position, False for short
            risk_amount: Dollar amount to risk
            tick_size: Instrument tick size
            tick_value: Dollar value per tick

        Returns:
            Stop loss price
        """
        ticks_to_risk = risk_amount / tick_value
        points_to_risk = ticks_to_risk * tick_size

        if is_long:
            return entry_price - points_to_risk
        else:
            return entry_price + points_to_risk

    def update_account_balance(self, new_balance: float):
        """Update account balance for position sizing calculations"""
        self.account_balance = new_balance


class RiskManager:
    """Comprehensive risk management system"""

    def __init__(self, risk_limits: RiskLimits, initial_balance: float):
        self.risk_limits = risk_limits
        self.position_sizer = PositionSizer(initial_balance, risk_limits)

        # Tracking
        self.daily_pnl = 0.0
        self.total_pnl = 0.0
        self.daily_trades = 0
        self.total_trades = 0

        # Loss tracking
        self.consecutive_losses = 0
        self.last_loss_time: Optional[datetime] = None

        # Position tracking
        self.active_positions: Dict[str, int] = {}  # instrument -> quantity
        self.total_contracts = 0

        # Callbacks
        self.on_risk_violation: Optional[Callable[[str, RiskLevel], None]] = None
        self.on_limit_reached: Optional[Callable[[str], None]] = None

        # State
        self.trading_enabled = True
        self.shutdown_reason: Optional[str] = None

    def can_trade(self, instrument: str, quantity: int) -> tuple[bool, str]:
        """
        Check if a trade is allowed based on risk limits

        Args:
            instrument: Trading instrument
            quantity: Proposed quantity

        Returns:
            Tuple of (allowed, reason)
        """
        if not self.trading_enabled:
            return False, f"Trading disabled: {self.shutdown_reason}"

        # Check time restrictions
        if not self._is_trading_time():
            return False, "Outside trading hours"

        # Check daily loss limit
        if self.daily_pnl <= -self.risk_limits.max_daily_loss:
            self._trigger_shutdown("Daily loss limit reached")
            return False, "Daily loss limit reached"

        # Check total loss limit
        if self.total_pnl <= -self.risk_limits.max_total_loss:
            self._trigger_shutdown("Total loss limit reached")
            return False, "Total loss limit reached"

        # Check daily profit target
        if (self.risk_limits.daily_profit_target is not None and
            self.daily_pnl >= self.risk_limits.daily_profit_target):
            return False, "Daily profit target reached"

        # Check max daily profit cap
        if (self.risk_limits.max_daily_profit is not None and
            self.daily_pnl >= self.risk_limits.max_daily_profit):
            return False, "Max daily profit reached"

        # Check consecutive losses cool-down
        if self.consecutive_losses >= self.risk_limits.max_consecutive_losses:
            if self.last_loss_time:
                time_since_loss = (datetime.now() - self.last_loss_time).total_seconds()
                if time_since_loss < self.risk_limits.cool_down_after_losses:
                    remaining = int(self.risk_limits.cool_down_after_losses - time_since_loss)
                    return False, f"Cool-down period: {remaining}s remaining"

        # Check total contracts
        if self.total_contracts + quantity > self.risk_limits.max_total_contracts:
            return False, f"Total contracts limit ({self.risk_limits.max_total_contracts}) would be exceeded"

        # Check per-trade contracts
        if quantity > self.risk_limits.max_contracts_per_trade:
            return False, f"Trade size exceeds max contracts per trade ({self.risk_limits.max_contracts_per_trade})"

        # Check instrument diversity
        if instrument not in self.active_positions:
            if len(self.active_positions) >= self.risk_limits.max_instruments:
                return False, f"Max instruments limit ({self.risk_limits.max_instruments}) reached"

        return True, "Trade allowed"

    def validate_trade_risk(
        self,
        entry_price: float,
        stop_loss: float,
        quantity: int,
        tick_size: float,
        tick_value: float
    ) -> tuple[bool, str]:
        """
        Validate if trade risk is acceptable

        Args:
            entry_price: Entry price
            stop_loss: Stop loss price
            quantity: Number of contracts
            tick_size: Instrument tick size
            tick_value: Dollar value per tick

        Returns:
            Tuple of (valid, reason)
        """
        # Calculate risk
        points_at_risk = abs(entry_price - stop_loss)
        ticks_at_risk = points_at_risk / tick_size
        risk_per_contract = ticks_at_risk * tick_value
        total_risk = risk_per_contract * quantity

        # Check against max risk per trade
        if total_risk > self.risk_limits.max_risk_per_trade:
            return False, f"Trade risk ${total_risk:.2f} exceeds limit ${self.risk_limits.max_risk_per_trade:.2f}"

        return True, "Risk acceptable"

    def register_trade(self, instrument: str, quantity: int, is_long: bool):
        """Register a new trade"""
        if instrument in self.active_positions:
            self.active_positions[instrument] += quantity
        else:
            self.active_positions[instrument] = quantity

        self.total_contracts += quantity
        self.daily_trades += 1
        self.total_trades += 1

    def close_position(self, instrument: str, quantity: int, pnl: float):
        """Register position closure"""
        if instrument in self.active_positions:
            self.active_positions[instrument] -= quantity
            if self.active_positions[instrument] <= 0:
                del self.active_positions[instrument]

        self.total_contracts = max(0, self.total_contracts - quantity)

        # Update P&L
        self.daily_pnl += pnl
        self.total_pnl += pnl

        # Track consecutive losses
        if pnl < 0:
            self.consecutive_losses += 1
            self.last_loss_time = datetime.now()

            # Check risk level
            if self.consecutive_losses >= self.risk_limits.max_consecutive_losses:
                self._trigger_risk_alert("Consecutive loss limit reached", RiskLevel.CRITICAL)
        else:
            self.consecutive_losses = 0

    def update_daily_pnl(self, pnl: float):
        """Update daily P&L"""
        self.daily_pnl = pnl

    def reset_daily_metrics(self):
        """Reset daily tracking metrics"""
        self.daily_pnl = 0.0
        self.daily_trades = 0
        self.consecutive_losses = 0
        self.last_loss_time = None

    def get_risk_level(self) -> RiskLevel:
        """Get current risk level"""
        # Check daily loss percentage
        daily_loss_pct = abs(self.daily_pnl / self.risk_limits.max_daily_loss) * 100

        if daily_loss_pct >= 90:
            return RiskLevel.CRITICAL
        elif daily_loss_pct >= 70:
            return RiskLevel.HIGH
        elif daily_loss_pct >= 50:
            return RiskLevel.MEDIUM
        else:
            return RiskLevel.LOW

    def get_risk_metrics(self) -> dict:
        """Get current risk metrics"""
        return {
            "trading_enabled": self.trading_enabled,
            "risk_level": self.get_risk_level().value,
            "daily_pnl": self.daily_pnl,
            "total_pnl": self.total_pnl,
            "daily_trades": self.daily_trades,
            "total_trades": self.total_trades,
            "consecutive_losses": self.consecutive_losses,
            "active_instruments": len(self.active_positions),
            "total_contracts": self.total_contracts,
            "daily_loss_used_pct": (abs(self.daily_pnl) / self.risk_limits.max_daily_loss) * 100
                                   if self.daily_pnl < 0 else 0,
        }

    def enable_trading(self):
        """Enable trading"""
        self.trading_enabled = True
        self.shutdown_reason = None

    def disable_trading(self, reason: str):
        """Disable trading"""
        self._trigger_shutdown(reason)

    def _is_trading_time(self) -> bool:
        """Check if current time is within trading hours"""
        if self.risk_limits.trading_start_time is None or self.risk_limits.trading_end_time is None:
            return True

        current_time = datetime.now().time()
        start = self.risk_limits.trading_start_time
        end = self.risk_limits.trading_end_time

        if start < end:
            return start <= current_time <= end
        else:
            # Handle overnight sessions
            return current_time >= start or current_time <= end

    def _trigger_shutdown(self, reason: str):
        """Trigger trading shutdown"""
        self.trading_enabled = False
        self.shutdown_reason = reason

        if self.on_limit_reached:
            self.on_limit_reached(reason)

    def _trigger_risk_alert(self, message: str, level: RiskLevel):
        """Trigger risk violation alert"""
        if self.on_risk_violation:
            self.on_risk_violation(message, level)

    def __str__(self) -> str:
        """String representation of risk status"""
        metrics = self.get_risk_metrics()
        return f"""
Risk Manager Status:
  Trading Enabled: {self.trading_enabled}
  Risk Level: {metrics['risk_level']}
  Daily P&L: ${self.daily_pnl:+,.2f}
  Total P&L: ${self.total_pnl:+,.2f}
  Daily Trades: {self.daily_trades}
  Consecutive Losses: {self.consecutive_losses}
  Active Instruments: {metrics['active_instruments']}/{self.risk_limits.max_instruments}
  Total Contracts: {self.total_contracts}/{self.risk_limits.max_total_contracts}
  Daily Loss Used: {metrics['daily_loss_used_pct']:.1f}%
"""


# Helper functions for common risk calculations

def calculate_risk_reward_ratio(entry: float, stop: float, target: float) -> float:
    """Calculate risk/reward ratio"""
    risk = abs(entry - stop)
    reward = abs(target - entry)
    return reward / risk if risk > 0 else 0.0


def calculate_position_value(quantity: int, price: float, tick_size: float, tick_value: float) -> float:
    """Calculate total position value in dollars"""
    ticks = price / tick_size
    return quantity * ticks * tick_value


def points_to_dollars(points: float, quantity: int, tick_size: float, tick_value: float) -> float:
    """Convert points to dollar value"""
    ticks = points / tick_size
    return quantity * ticks * tick_value
