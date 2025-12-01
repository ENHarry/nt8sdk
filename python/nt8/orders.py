from dataclasses import dataclass, field
from datetime import datetime
from typing import Optional, Dict
from .types import OrderAction, OrderType, OrderState, MarketPosition


@dataclass
class Order:
    """Represents a trading order"""
    order_id: str
    instrument: str
    action: OrderAction
    order_type: OrderType
    quantity: int
    limit_price: float = 0.0
    stop_price: float = 0.0
    state: OrderState = OrderState.INITIALIZED
    filled: int = 0
    remaining: int = 0
    avg_fill_price: float = 0.0
    submitted_time: Optional[datetime] = None
    filled_time: Optional[datetime] = None
    signal_name: str = ""
    
    @property
    def is_active(self) -> bool:
        """Check if order is still active"""
        return self.state in (OrderState.SUBMITTED, OrderState.ACCEPTED, OrderState.WORKING)
    
    @property
    def is_filled(self) -> bool:
        """Check if order is completely filled"""
        return self.state == OrderState.FILLED
    
    @property
    def is_partially_filled(self) -> bool:
        """Check if order is partially filled"""
        return self.state == OrderState.PART_FILLED


@dataclass
class OrderUpdate:
    """Order state update event"""
    order_id: str
    state: OrderState
    filled: int
    remaining: int
    avg_price: float
    timestamp: datetime
    error_message: str = ""


@dataclass
class Position:
    """Current position for an instrument"""
    instrument: str
    market_position: MarketPosition = MarketPosition.FLAT
    quantity: int = 0
    avg_price: float = 0.0
    unrealized_pnl: float = 0.0
    realized_pnl: float = 0.0
    
    @property
    def is_flat(self) -> bool:
        return self.market_position == MarketPosition.FLAT
    
    @property
    def is_long(self) -> bool:
        return self.market_position == MarketPosition.LONG
    
    @property
    def is_short(self) -> bool:
        return self.market_position == MarketPosition.SHORT


class OrderTracker:
    """Tracks orders and positions"""
    
    def __init__(self):
        self.orders: Dict[str, Order] = {}
        self.positions: Dict[str, Position] = {}
        self.filled_orders: Dict[str, Order] = {}
    
    def add_order(self, order: Order):
        """Add new order to tracking"""
        self.orders[order.order_id] = order
    
    def update_order(self, update: OrderUpdate):
        """Update order state"""
        if update.order_id in self.orders:
            order = self.orders[update.order_id]
            order.state = update.state
            order.filled = update.filled
            order.remaining = update.remaining
            order.avg_fill_price = update.avg_price
            
            if order.is_filled:
                order.filled_time = update.timestamp
                self.filled_orders[order.order_id] = order
                del self.orders[order.order_id]
            elif update.state in (OrderState.CANCELLED, OrderState.REJECTED):
                del self.orders[update.order_id]
    
    def update_position(self, position: Position):
        """Update position information"""
        self.positions[position.instrument] = position
    
    def get_position(self, instrument: str) -> Position:
        """Get current position for instrument"""
        return self.positions.get(instrument, Position(instrument=instrument))
    
    def get_active_orders(self, instrument: Optional[str] = None) -> list:
        """Get active orders, optionally filtered by instrument"""
        orders = [o for o in self.orders.values() if o.is_active]
        if instrument:
            orders = [o for o in orders if o.instrument == instrument]
        return orders