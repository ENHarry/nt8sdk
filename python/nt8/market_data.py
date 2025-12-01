from dataclasses import dataclass
from datetime import datetime
from typing import List, Callable, Dict
from collections import deque


@dataclass
class TickData:
    """Single tick of market data"""
    instrument: str
    timestamp: datetime
    price: float
    volume: int
    bid: float
    ask: float
    
    @property
    def spread(self) -> float:
        """Bid-ask spread"""
        return self.ask - self.bid
    
    @property
    def mid_price(self) -> float:
        """Mid-point between bid and ask"""
        return (self.bid + self.ask) / 2.0


@dataclass
class MarketDepthLevel:
    """Single level in market depth"""
    price: float
    volume: int
    position: int  # 0 = best bid/ask


class MarketDataBuffer:
    """Efficient circular buffer for market data"""
    
    def __init__(self, maxlen: int = 10000):
        self.ticks: deque = deque(maxlen=maxlen)
        self.subscribers: List[Callable] = []
    
    def add_tick(self, tick: TickData):
        """Add tick and notify subscribers"""
        self.ticks.append(tick)
        for callback in self.subscribers:
            try:
                callback(tick)
            except Exception as e:
                print(f"Error in tick callback: {e}")
    
    def subscribe(self, callback: Callable):
        """Subscribe to tick updates"""
        self.subscribers.append(callback)
    
    def get_latest(self, count: int = 1) -> List[TickData]:
        """Get last N ticks"""
        return list(self.ticks)[-count:] if count > 0 else list(self.ticks)
    
    def get_latest_price(self) -> float:
        """Get most recent price"""
        return self.ticks[-1].price if self.ticks else 0.0


class MarketDataManager:
    """Manages market data for multiple instruments"""
    
    def __init__(self):
        self.buffers: Dict[str, MarketDataBuffer] = {}
    
    def get_buffer(self, instrument: str) -> MarketDataBuffer:
        """Get or create buffer for instrument"""
        if instrument not in self.buffers:
            self.buffers[instrument] = MarketDataBuffer()
        return self.buffers[instrument]
    
    def add_tick(self, instrument: str, tick: TickData):
        """Add tick to appropriate buffer"""
        self.get_buffer(instrument).add_tick(tick)
    
    def subscribe(self, instrument: str, callback: Callable):
        """Subscribe to tick updates for instrument"""
        self.get_buffer(instrument).subscribe(callback)