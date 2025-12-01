from enum import Enum


class OrderAction(str, Enum):
    """Order action: buy or sell"""
    BUY = "BUY"
    SELL = "SELL"


class OrderType(str, Enum):
    """Order type"""
    MARKET = "MARKET"
    LIMIT = "LIMIT"
    STOP_MARKET = "STOP_MARKET"
    STOP_LIMIT = "STOP_LIMIT"


class OrderState(str, Enum):
    """Order state in lifecycle"""
    INITIALIZED = "INITIALIZED"
    SUBMITTED = "SUBMITTED"
    ACCEPTED = "ACCEPTED"
    WORKING = "WORKING"
    FILLED = "FILLED"
    PART_FILLED = "PART_FILLED"
    CANCELLED = "CANCELLED"
    REJECTED = "REJECTED"
    UNKNOWN = "UNKNOWN"


class MarketDataType(str, Enum):
    """Market data event types"""
    ASK = "ASK"
    BID = "BID"
    LAST = "LAST"


class MarketPosition(str, Enum):
    """Position direction"""
    FLAT = "FLAT"
    LONG = "LONG"
    SHORT = "SHORT"


class TimeInForce(str, Enum):
    """Order time in force"""
    DAY = "DAY"
    GTC = "GTC"
    IOC = "IOC"
    FOK = "FOK"