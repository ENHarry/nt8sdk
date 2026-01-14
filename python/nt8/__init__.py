import os

from .client_filebased import NT8Client as NT8FileClient

try:  # pragma: no cover - optional dependency
    from .client_managed import NT8ManagedClient
except Exception:  # ImportError, pythonnet missing, etc.
    NT8ManagedClient = None  # type: ignore

try:  # pragma: no cover - optional dependency
    from .client_hybrid import NT8HybridClient
except Exception:  # ImportError, pythonnet missing, etc.
    NT8HybridClient = None  # type: ignore


def _select_client_impl() -> type:
    impl = os.getenv("NT8_CLIENT_IMPL", "file").lower()
    if impl == "managed" and NT8ManagedClient is not None:
        return NT8ManagedClient
    return NT8FileClient


NT8Client = _select_client_impl()

from .types import (
    OrderAction, OrderType, OrderState, MarketDataType,
    MarketPosition, TimeInForce
)
from .orders import Order, OrderUpdate, Position
from .market_data import TickData, MarketDepthLevel
from .account import AccountInfo, AccountUpdate, AccountManager, AccountConnectionStatus
from .risk_management import (
    RiskManager, RiskLimits, PositionSizer, TradeRiskMetrics,
    RiskLevel, calculate_risk_reward_ratio, calculate_position_value,
    points_to_dollars
)
from .advanced_strategy import BreakevenConfig, BreakevenManager, AdvancedStrategy
from .indicator_client import NT8IndicatorClient

__version__ = "1.1.0"
__all__ = [
    # Core client
    'NT8Client',
    'NT8ManagedClient',
    'NT8HybridClient',
    'NT8FileClient',
    'NT8IndicatorClient',

    # Types and enums
    'OrderAction', 'OrderType', 'OrderState', 'MarketDataType',
    'MarketPosition', 'TimeInForce',

    # Orders and positions
    'Order', 'OrderUpdate', 'Position',

    # Market data
    'TickData', 'MarketDepthLevel',

    # Account management
    'AccountInfo', 'AccountUpdate', 'AccountManager', 'AccountConnectionStatus',

    # Risk management
    'RiskManager', 'RiskLimits', 'PositionSizer', 'TradeRiskMetrics',
    'RiskLevel', 'calculate_risk_reward_ratio', 'calculate_position_value',
    'points_to_dollars',

    # Advanced strategies
    'BreakevenConfig', 'BreakevenManager', 'AdvancedStrategy'
]