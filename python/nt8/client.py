import win32pipe
import win32file
import pywintypes
import time
import threading
from datetime import datetime
from typing import Optional, Callable
import uuid

from .types import OrderAction, OrderType, OrderState, TimeInForce, MarketPosition
from .orders import Order, OrderUpdate, OrderTracker, Position
from .market_data import TickData, MarketDataManager
from .protocol import BinaryProtocol
from .account import AccountManager, AccountUpdate, AccountInfo, AccountConnectionStatus


class NT8Client:
    """High-performance NT8 Python client using Named Pipes"""

    def __init__(self, pipe_name: str = "NT8PythonSDK", reconnect: bool = True, account_name: str = "Sim101"):
        self.pipe_name = f"\\\\\\\\.\\\\pipe\\\\{pipe_name}"
        self.pipe_handle = None
        self.connected = False
        self.reconnect = reconnect

        # Core components
        self.order_tracker = OrderTracker()
        self.market_data = MarketDataManager()
        self.protocol = BinaryProtocol()
        self.account_manager = AccountManager(account_name=account_name)

        # Background threads
        self._read_thread: Optional[threading.Thread] = None
        self._running = False

        # Callbacks
        self.on_order_update: Optional[Callable] = None
        self.on_position_update: Optional[Callable] = None
        self.on_account_update: Optional[Callable] = None
        self.on_error: Optional[Callable] = None
    
    def connect(self, timeout_seconds: int = 30) -> bool:
        """Connect to NT8 adapter via Named Pipe"""
        start_time = time.time()
        
        while time.time() - start_time < timeout_seconds:
            try:
                self.pipe_handle = win32file.CreateFile(
                    self.pipe_name,
                    win32file.GENERIC_READ | win32file.GENERIC_WRITE,
                    0,
                    None,
                    win32file.OPEN_EXISTING,
                    0,
                    None
                )
                
                # Set pipe to message mode
                win32pipe.SetNamedPipeHandleState(
                    self.pipe_handle,
                    win32pipe.PIPE_READMODE_MESSAGE,
                    None,
                    None
                )
                
                self.connected = True
                self._running = True
                
                # Start background reader thread
                self._read_thread = threading.Thread(target=self._read_loop, daemon=True)
                self._read_thread.start()
                
                print(f"Connected to NT8 adapter on {self.pipe_name}")
                return True
                
            except pywintypes.error as e:
                if e.winerror == 2:  # ERROR_FILE_NOT_FOUND
                    time.sleep(0.1)
                    continue
                else:
                    raise
        
        print(f"Failed to connect to NT8 adapter after {timeout_seconds}s")
        return False
    
    def disconnect(self):
        """Disconnect from NT8 adapter"""
        self._running = False
        if self._read_thread:
            self._read_thread.join(timeout=2.0)
        
        if self.pipe_handle:
            win32file.CloseHandle(self.pipe_handle)
            self.pipe_handle = None
        
        self.connected = False
        print("Disconnected from NT8 adapter")
    
    def _read_loop(self):
        """Background thread for reading messages from NT8"""
        while self._running and self.connected:
            try:
                # Read message from pipe
                result, data = win32file.ReadFile(self.pipe_handle, 64*1024)
                
                if data:
                    self._process_message(data)
                    
            except pywintypes.error as e:
                if e.winerror == 109:  # ERROR_BROKEN_PIPE
                    self.connected = False
                    if self.reconnect:
                        print("Connection lost, attempting reconnect...")
                        time.sleep(1)
                        self.connect()
                    break
                else:
                    if self.on_error:
                        self.on_error(e)
            except Exception as e:
                if self.on_error:
                    self.on_error(e)
    
    def _process_message(self, data: bytes):
        """Process incoming message from NT8"""
        msg_type = data[0]

        if msg_type == BinaryProtocol.MSG_TICK:
            tick_data = self.protocol.decode_tick_data(data)
            tick = TickData(
                instrument=tick_data['instrument'],
                timestamp=datetime.fromtimestamp(tick_data['timestamp']),
                price=tick_data['price'],
                volume=tick_data['volume'],
                bid=tick_data['bid'],
                ask=tick_data['ask']
            )
            self.market_data.add_tick(tick.instrument, tick)

        elif msg_type == BinaryProtocol.MSG_ORDER_UPDATE:
            update_data = self.protocol.decode_order_update(data)
            update = OrderUpdate(
                order_id=update_data['order_id'],
                state=OrderState(update_data['state']),
                filled=update_data['filled'],
                remaining=update_data['remaining'],
                avg_price=update_data['avg_price'],
                timestamp=datetime.fromtimestamp(update_data['timestamp'])
            )
            self.order_tracker.update_order(update)

            if self.on_order_update:
                self.on_order_update(update)

        elif msg_type == BinaryProtocol.MSG_POSITION_UPDATE:
            pos_data = self.protocol.decode_position_update(data)
            position = Position(
                instrument=pos_data['instrument'],
                market_position=pos_data['position'],
                quantity=pos_data['quantity'],
                avg_price=pos_data['avg_price'],
                unrealized_pnl=pos_data['unrealized_pnl']
            )
            self.order_tracker.update_position(position)

            if self.on_position_update:
                self.on_position_update(position)

        elif msg_type == BinaryProtocol.MSG_ACCOUNT_UPDATE:
            account_data = self.protocol.decode_account_update(data)
            update = AccountUpdate(
                account_name=account_data['account_name'],
                timestamp=datetime.fromtimestamp(account_data['timestamp']),
                cash_value=account_data.get('cash_value'),
                buying_power=account_data.get('buying_power'),
                realized_pnl=account_data.get('realized_pnl'),
                unrealized_pnl=account_data.get('unrealized_pnl'),
                net_liquidation=account_data.get('net_liquidation'),
                update_type=account_data.get('update_type', 'BALANCE')
            )
            self.account_manager.update_account(update)

            if self.on_account_update:
                self.on_account_update(update)
    
    def _send_command(self, command: bytes):
        """Send command to NT8 adapter"""
        if not self.connected:
            raise RuntimeError("Not connected to NT8 adapter")
        
        win32file.WriteFile(self.pipe_handle, command)
    
    def subscribe_market_data(self, instrument: str):
        """Subscribe to market data for instrument"""
        command = f"SUBSCRIBE|{instrument}".encode('utf-8')
        self._send_command(command)
    
    def unsubscribe_market_data(self, instrument: str):
        """Unsubscribe from market data"""
        command = f"UNSUBSCRIBE|{instrument}".encode('utf-8')
        self._send_command(command)
    
    def place_market_order(self, instrument: str, action: OrderAction, 
                          quantity: int, signal_name: str = "") -> str:
        """Place market order"""
        order_id = str(uuid.uuid4())[:8]
        
        # Convert enum to string if needed
        action_str = action.value if isinstance(action, OrderAction) else action
        
        order = Order(
            order_id=order_id,
            instrument=instrument,
            action=action if isinstance(action, OrderAction) else OrderAction(action),
            order_type=OrderType.MARKET,
            quantity=quantity,
            signal_name=signal_name,
            submitted_time=datetime.now()
        )
        
        self.order_tracker.add_order(order)
        
        command = self.protocol.encode_order_command(
            action=action_str,
            instrument=instrument,
            quantity=quantity,
            order_type="MARKET",
            signal_name=signal_name
        )
        self._send_command(command)
        
        return order_id
    
    def place_limit_order(self, instrument: str, action: OrderAction,
                         quantity: int, limit_price: float, 
                         signal_name: str = "") -> str:
        """Place limit order"""
        order_id = str(uuid.uuid4())[:8]
        
        # Convert enum to string if needed
        action_str = action.value if isinstance(action, OrderAction) else action
        
        order = Order(
            order_id=order_id,
            instrument=instrument,
            action=action if isinstance(action, OrderAction) else OrderAction(action),
            order_type=OrderType.LIMIT,
            quantity=quantity,
            limit_price=limit_price,
            signal_name=signal_name,
            submitted_time=datetime.now()
        )
        
        self.order_tracker.add_order(order)
        
        command = self.protocol.encode_order_command(
            action=action_str,
            instrument=instrument,
            quantity=quantity,
            order_type="LIMIT",
            limit_price=limit_price,
            signal_name=signal_name
        )
        self._send_command(command)
        
        return order_id
    
    def get_position(self, instrument: str) -> Position:
        """Get current position for instrument"""
        return self.order_tracker.get_position(instrument)
    
    def get_active_orders(self, instrument: str = None) -> list:
        """Get active orders"""
        return self.order_tracker.get_active_orders(instrument)
    
    def get_latest_tick(self, instrument: str) -> Optional[TickData]:
        """Get most recent tick for instrument"""
        buffer = self.market_data.get_buffer(instrument)
        ticks = buffer.get_latest(1)
        return ticks[0] if ticks else None

    # ========================================================================
    # Enhanced Order Management
    # ========================================================================

    def place_stop_order(self, instrument: str, action: OrderAction,
                        quantity: int, stop_price: float,
                        signal_name: str = "") -> str:
        """
        Place stop market order

        Args:
            instrument: Trading instrument (e.g., "ES 03-25")
            action: BUY or SELL
            quantity: Number of contracts
            stop_price: Stop trigger price
            signal_name: Optional signal identifier

        Returns:
            Order ID
        """
        order_id = str(uuid.uuid4())[:8]

        action_str = action.value if isinstance(action, OrderAction) else action

        order = Order(
            order_id=order_id,
            instrument=instrument,
            action=action if isinstance(action, OrderAction) else OrderAction(action),
            order_type=OrderType.STOP_MARKET,
            quantity=quantity,
            stop_price=stop_price,
            signal_name=signal_name,
            submitted_time=datetime.now()
        )

        self.order_tracker.add_order(order)

        command = self.protocol.encode_order_command(
            action=action_str,
            instrument=instrument,
            quantity=quantity,
            order_type="STOP_MARKET",
            stop_price=stop_price,
            signal_name=signal_name
        )
        self._send_command(command)

        return order_id

    def place_stop_limit_order(self, instrument: str, action: OrderAction,
                              quantity: int, stop_price: float,
                              limit_price: float, signal_name: str = "") -> str:
        """
        Place stop limit order

        Args:
            instrument: Trading instrument
            action: BUY or SELL
            quantity: Number of contracts
            stop_price: Stop trigger price
            limit_price: Limit price after stop triggered
            signal_name: Optional signal identifier

        Returns:
            Order ID
        """
        order_id = str(uuid.uuid4())[:8]

        action_str = action.value if isinstance(action, OrderAction) else action

        order = Order(
            order_id=order_id,
            instrument=instrument,
            action=action if isinstance(action, OrderAction) else OrderAction(action),
            order_type=OrderType.STOP_LIMIT,
            quantity=quantity,
            stop_price=stop_price,
            limit_price=limit_price,
            signal_name=signal_name,
            submitted_time=datetime.now()
        )

        self.order_tracker.add_order(order)

        command = self.protocol.encode_order_command(
            action=action_str,
            instrument=instrument,
            quantity=quantity,
            order_type="STOP_LIMIT",
            stop_price=stop_price,
            limit_price=limit_price,
            signal_name=signal_name
        )
        self._send_command(command)

        return order_id

    def cancel_order(self, order_id: str) -> bool:
        """
        Cancel an active order

        Args:
            order_id: ID of order to cancel

        Returns:
            True if cancel request was sent successfully
        """
        command = f"CANCEL|{order_id}".encode('utf-8')
        try:
            self._send_command(command)
            return True
        except Exception as e:
            if self.on_error:
                self.on_error(e)
            return False

    def cancel_all_orders(self, instrument: Optional[str] = None) -> int:
        """
        Cancel all active orders, optionally filtered by instrument

        Args:
            instrument: Optional instrument filter

        Returns:
            Number of orders cancelled
        """
        active_orders = self.get_active_orders(instrument)
        cancelled = 0

        for order in active_orders:
            if self.cancel_order(order.order_id):
                cancelled += 1

        return cancelled

    def modify_order(self, order_id: str, quantity: Optional[int] = None,
                    limit_price: Optional[float] = None,
                    stop_price: Optional[float] = None) -> bool:
        """
        Modify an existing order

        Args:
            order_id: ID of order to modify
            quantity: New quantity (optional)
            limit_price: New limit price (optional)
            stop_price: New stop price (optional)

        Returns:
            True if modify request was sent successfully
        """
        # Build modification command
        parts = ["MODIFY", order_id]

        if quantity is not None:
            parts.append(f"qty={quantity}")
        if limit_price is not None:
            parts.append(f"limit={limit_price}")
        if stop_price is not None:
            parts.append(f"stop={stop_price}")

        command = "|".join(parts).encode('utf-8')

        try:
            self._send_command(command)
            return True
        except Exception as e:
            if self.on_error:
                self.on_error(e)
            return False

    def place_bracket_order(self, instrument: str, action: OrderAction,
                           quantity: int, entry_price: Optional[float] = None,
                           stop_loss: float = 0.0, take_profit: float = 0.0,
                           signal_name: str = "") -> dict:
        """
        Place a bracket order (entry + stop loss + take profit)

        Args:
            instrument: Trading instrument
            action: BUY or SELL
            quantity: Number of contracts
            entry_price: Entry price (None for market order)
            stop_loss: Stop loss price
            take_profit: Take profit price
            signal_name: Optional signal identifier

        Returns:
            Dictionary with entry_id, stop_id, target_id
        """
        # Place entry order
        if entry_price is None:
            entry_id = self.place_market_order(instrument, action, quantity, signal_name)
        else:
            entry_id = self.place_limit_order(instrument, action, quantity, entry_price, signal_name)

        # Determine exit action (opposite of entry)
        exit_action = OrderAction.SELL if action == OrderAction.BUY else OrderAction.BUY

        # Place stop loss
        stop_id = None
        if stop_loss > 0:
            stop_id = self.place_stop_order(instrument, exit_action, quantity, stop_loss, f"{signal_name}_SL")

        # Place take profit
        target_id = None
        if take_profit > 0:
            target_id = self.place_limit_order(instrument, exit_action, quantity, take_profit, f"{signal_name}_TP")

        return {
            "entry_id": entry_id,
            "stop_id": stop_id,
            "target_id": target_id
        }

    # ========================================================================
    # Account Management
    # ========================================================================

    def get_account_info(self) -> AccountInfo:
        """Get current account information"""
        return self.account_manager.get_account_info()

    def get_account_balance(self) -> float:
        """Get current account balance"""
        return self.account_manager.get_balance()

    def get_buying_power(self) -> float:
        """Get current buying power"""
        return self.account_manager.get_buying_power()

    def get_daily_pnl(self) -> float:
        """Get today's total P&L"""
        return self.account_manager.get_daily_pnl()

    def get_total_pnl(self) -> float:
        """Get total P&L"""
        return self.account_manager.get_total_pnl()

    def is_account_healthy(self, min_balance: float = 0.0,
                          max_daily_loss: Optional[float] = None) -> tuple[bool, str]:
        """
        Check if account is in good health

        Args:
            min_balance: Minimum required balance
            max_daily_loss: Maximum allowed daily loss

        Returns:
            Tuple of (is_healthy, reason)
        """
        return self.account_manager.is_account_healthy(min_balance, max_daily_loss)

    def request_account_update(self):
        """Request account information update from NT8"""
        command = "REQUEST_ACCOUNT".encode('utf-8')
        self._send_command(command)

    # ========================================================================
    # Instrument Information
    # ========================================================================

    def get_instrument_details(self, instrument: str) -> Optional[dict]:
        """
        Request instrument details (tick size, point value, etc.)

        Args:
            instrument: Trading instrument

        Returns:
            Dictionary with instrument details (cached locally)
        """
        command = f"INSTRUMENT_INFO|{instrument}".encode('utf-8')
        self._send_command(command)
        # Note: Response will be async via message processing
        return None