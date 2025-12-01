"""Hybrid NT8 client combining DLL (orders) and file-based (L2 market data) interfaces."""

from __future__ import annotations

import logging
import os
from typing import Any, Dict, List

from .client_managed import NT8ManagedClient
from .client_filebased import NT8Client

logger = logging.getLogger(__name__)


class NT8HybridClient:
    """
    Hybrid client that uses:
    - DLL interface (NT8ManagedClient) for: place_order, modify_order, L1 market data, account values
    - File-based interface (NT8Client) for: get_positions, get_orders, L2 market depth, cancel_order, close_position

    Each function has a primary implementation with a fallback to the other interface.
    """

    def __init__(
        self,
        *,
        # DLL client params
        dll_path: str | None = None,
        host: str = "127.0.0.1",
        port: int = 4011,
        # File-based client params
        documents_dir: str | None = None,
        command_timeout: float | None = None,
        # Shared params
        account: str | None = None,
        auto_connect: bool = True,
    ) -> None:
        self.account = account or os.getenv("NT8_ACCOUNT") or "Sim101"

        # Initialize DLL client for orders/positions
        logger.info("Initializing DLL client for orders and positions...")
        self._dll_client = NT8ManagedClient(
            dll_path=dll_path,
            host=host,
            port=port,
            account=self.account,
            auto_connect=auto_connect,
        )

        # Initialize file-based client for L2 market data
        logger.info("Initializing file-based client for L2 market data...")
        self._file_client = NT8Client(
            documents_dir=documents_dir,
            command_timeout=command_timeout,
        )

        self._connected = self._dll_client._connected

    # ------------------------------------------------------------------
    # Lifecycle - delegate to both clients as needed
    # ------------------------------------------------------------------
    def connect(self, show_message: bool | None = None) -> bool:
        """Connect both clients."""
        dll_connected = self._dll_client.connect(show_message)
        self._connected = dll_connected
        return self._connected

    def authenticate(self) -> bool:
        """Authenticate (alias for connect)."""
        return self.connect()

    def tear_down(self) -> None:
        """Tear down both clients."""
        try:
            self._dll_client.tear_down()
        except Exception as e:
            logger.warning(f"DLL tear_down error: {e}")

    # ------------------------------------------------------------------
    # Market Data - L1 via DLL, L2 via File-based
    # ------------------------------------------------------------------
    def get_market_data(self, instrument: str, *, level: str = "L1") -> dict:
        """
        Get market data - L1 via DLL, L2 via File-based.

        Args:
            instrument: Trading instrument
            level: "L1" for DLL (fast), "L2" for file-based (depth)
        """
        if level.upper() == "L2":
            return self._file_client.get_market_data(instrument, level="L2")
        else:
            return self._dll_client.get_market_data(instrument)

    def get_market_depth(self, instrument: str) -> dict:
        """Get Level 2 market depth (DOM) via file-based client."""
        return self._file_client.get_market_data(instrument, level="L2")

    def subscribe_market_data(self, instrument: str) -> bool:
        """Subscribe to market data via file-based client (supports L2)."""
        return self._file_client.subscribe_market_data(instrument)

    def unsubscribe_market_data(self, instrument: str) -> bool:
        """Unsubscribe from market data via file-based client."""
        return self._file_client.unsubscribe_market_data(instrument)

    # DLL streaming data
    def get_last(self, instrument: str) -> float:
        """Get last price via DLL (faster)."""
        return self._dll_client.get_last(instrument)

    def get_bid(self, instrument: str) -> float:
        """Get bid price via DLL (faster)."""
        return self._dll_client.get_bid(instrument)

    def get_ask(self, instrument: str) -> float:
        """Get ask price via DLL (faster)."""
        return self._dll_client.get_ask(instrument)

    def subscribe(self, instrument: str) -> None:
        """Subscribe to streaming data via DLL."""
        self._dll_client.subscribe(instrument)

    def unsubscribe(self, instrument: str) -> None:
        """Unsubscribe from streaming data via DLL."""
        self._dll_client.unsubscribe(instrument)

    # ------------------------------------------------------------------
    # Order Management - DLL Primary with File-based fallback
    # ------------------------------------------------------------------
    def place_order(
        self,
        instrument: str = "",
        action: str = "",
        quantity: int = 0,
        order_type: str = "MARKET",
        limit_price: float = 0.0,
        stop_price: float = 0.0,
        tif: str = "",
        time_in_force: str = "GTC",
        oco: str = "",
        oco_id: str = "",
        order_id: str = "",
        strategy: str = "",
        strategy_id: str = "",
        account: str | None = None,
    ) -> Dict[str, Any] | str:
        """Place order via DLL client with file-based fallback."""
        # Support both tif and time_in_force, and both oco and oco_id
        effective_tif = tif or time_in_force
        effective_oco = oco or oco_id
        try:
            return self._dll_client.place_order(
                instrument=instrument,
                action=action,
                quantity=quantity,
                order_type=order_type,
                limit_price=limit_price,
                stop_price=stop_price,
                time_in_force=effective_tif,
                oco_id=effective_oco,
                order_id=order_id,
                strategy=strategy,
                strategy_id=strategy_id,
                account=account,
            )
        except Exception as e:
            logger.warning(f"DLL place_order failed: {e}, using file-based fallback")
            return self.place_order_file(
                instrument=instrument,
                action=action,
                quantity=quantity,
                order_type=order_type,
                limit_price=limit_price,
                stop_price=stop_price,
                tif=effective_tif,
                oco=effective_oco,
                order_id=order_id,
                strategy=strategy,
                account=account or self.account,
            )

    def place_order_file(
        self,
        instrument: str = "",
        action: str = "",
        quantity: int = 0,
        order_type: str = "MARKET",
        limit_price: float = 0.0,
        stop_price: float = 0.0,
        tif: str = "GTC",
        oco: str = "",
        order_id: str = "",
        strategy: str = "",
        account: str | None = None,
    ) -> str:
        """Place order via file-based client directly."""
        return self._file_client.place_order(
            account=account or self.account,
            instrument=instrument,
            action=action,
            quantity=quantity,
            order_type=order_type,
            limit_price=limit_price,
            stop_price=stop_price,
            tif=tif,
            oco=oco,
            order_id=order_id,
            strategy=strategy,
        )

    def modify_order(
        self,
        order_id: str,
        quantity: int | None = None,
        limit_price: float | None = None,
        stop_price: float | None = None,
    ) -> bool:
        """Modify order via DLL client with file-based fallback."""
        try:
            return self._dll_client.modify_order(order_id, quantity, limit_price, stop_price)
        except Exception as e:
            logger.warning(f"DLL modify_order failed: {e}, using file-based fallback")
            return self.modify_order_file(order_id, quantity, limit_price, stop_price)

    def modify_order_file(
        self,
        order_id: str,
        quantity: int | None = None,
        limit_price: float | None = None,
        stop_price: float | None = None,
    ) -> bool:
        """Modify order via file-based client directly."""
        return self._file_client.modify_order(
            order_id=order_id,
            quantity=quantity,
            limit_price=limit_price,
            stop_price=stop_price,
        )

    def get_order_status(self, order_id: str) -> str:
        """Get order status via DLL client."""
        return self._dll_client.get_order_status(order_id)

    def get_filled(self, order_id: str) -> int:
        """Get filled quantity via DLL client."""
        return self._dll_client.get_filled(order_id)

    def get_avg_fill_price(self, order_id: str) -> float:
        """Get average fill price via DLL client."""
        return self._dll_client.get_avg_fill_price(order_id)

    def new_order_id(self) -> str:
        """Generate new order ID via DLL client."""
        return self._dll_client.new_order_id()

    # ------------------------------------------------------------------
    # Order/Position Queries - File-based Primary with DLL fallback
    # ------------------------------------------------------------------
    def get_orders(self, account: str | None = None) -> List[Dict[str, Any]]:
        """Get orders via file-based client (full details) with DLL fallback."""
        try:
            return self._file_client.get_orders()
        except Exception as e:
            logger.warning(f"File-based get_orders failed: {e}, using DLL fallback")
            return self.get_orders_dll(account)

    def get_orders_dll(self, account: str | None = None) -> List[Dict[str, Any]]:
        """Get orders via DLL client directly (limited data)."""
        return self._dll_client.get_orders(account)

    def get_orders_file(self) -> List[Dict[str, Any]]:
        """Get orders via file-based client directly."""
        return self._file_client.get_orders()

    def get_positions(self, account: str | None = None) -> List[Dict[str, Any]]:
        """Get positions via file-based client (full details) with DLL fallback."""
        try:
            return self._file_client.get_positions()
        except Exception as e:
            logger.warning(f"File-based get_positions failed: {e}, using DLL fallback")
            return self.get_positions_dll(account)

    def get_positions_dll(self, account: str | None = None) -> List[Dict[str, Any]]:
        """Get positions via DLL client directly."""
        return self._dll_client.get_positions(account)

    def get_positions_file(self) -> List[Dict[str, Any]]:
        """Get positions via file-based client directly."""
        return self._file_client.get_positions()

    # ------------------------------------------------------------------
    # Cancel/Close Operations - File-based Primary with DLL fallback
    # ------------------------------------------------------------------
    def cancel_order(self, order_id: str) -> bool:
        """Cancel order via file-based client with DLL fallback."""
        try:
            return self._file_client.cancel_order(order_id)
        except Exception as e:
            logger.warning(f"File-based cancel_order failed: {e}, using DLL fallback")
            return self.cancel_order_dll(order_id)

    def cancel_order_dll(self, order_id: str) -> bool:
        """Cancel order via DLL client directly."""
        return self._dll_client.cancel_order(order_id)

    def cancel_order_file(self, order_id: str) -> bool:
        """Cancel order via file-based client directly."""
        return self._file_client.cancel_order(order_id)

    def cancel_all_orders(self, account: str | None = None) -> bool:
        """Cancel all orders via file-based client with DLL fallback."""
        try:
            return self._file_client.cancel_all_orders()
        except Exception as e:
            logger.warning(f"File-based cancel_all_orders failed: {e}, using DLL fallback")
            return self.cancel_all_orders_dll(account)

    def cancel_all_orders_dll(self, account: str | None = None) -> bool:
        """Cancel all orders via DLL client directly."""
        return self._dll_client.cancel_all_orders(account)

    def cancel_all_orders_file(self) -> bool:
        """Cancel all orders via file-based client directly."""
        return self._file_client.cancel_all_orders()

    def close_position(self, account: str, instrument: str) -> bool:
        """Close position via file-based client with DLL fallback."""
        try:
            return self._file_client.close_position(account, instrument)
        except Exception as e:
            logger.warning(f"File-based close_position failed: {e}, using DLL fallback")
            return self.close_position_dll(instrument, account)

    def close_position_dll(self, instrument: str, account: str | None = None) -> bool:
        """Close position via DLL client directly."""
        return self._dll_client.close_position(account, instrument)

    def close_position_file(self, account: str, instrument: str) -> bool:
        """Close position via file-based client directly."""
        return self._file_client.close_position(account, instrument)

    def flatten_everything(self, account: str | None = None) -> bool:
        """Flatten all positions via file-based client with DLL fallback."""
        try:
            return self._file_client.flatten_everything()
        except Exception as e:
            logger.warning(f"File-based flatten_everything failed: {e}, using DLL fallback")
            return self.flatten_everything_dll(account)

    def flatten_everything_dll(self, account: str | None = None) -> bool:
        """Flatten all positions via DLL client directly."""
        return self._dll_client.flatten_everything(account)

    def flatten_everything_file(self) -> bool:
        """Flatten all positions via file-based client directly."""
        return self._file_client.flatten_everything()

    def close_strategy(self, strategy_id: str) -> bool:
        """Close strategy via DLL client."""
        return self._dll_client.close_strategy(strategy_id)

    def reverse_position(
        self,
        instrument: str,
        action: str,
        quantity: int,
        order_type: str = "MARKET",
        limit_price: float = 0.0,
        stop_price: float = 0.0,
        tif: str = "GTC",
        oco: str = "",
        order_id: str = "",
        strategy: str = "",
        strategy_id: str = "",
        account: str | None = None,
    ) -> Dict[str, Any]:
        """Reverse position via DLL client with file-based fallback."""
        # Try DLL client first
        if self._dll_client:
            try:
                return self._dll_client.reverse_position(
                    instrument=instrument,
                    action=action,
                    quantity=quantity,
                    order_type=order_type,
                    limit_price=limit_price,
                    stop_price=stop_price,
                    tif=tif,
                    oco=oco,
                    order_id=order_id,
                    strategy=strategy,
                    strategy_id=strategy_id,
                    account=account,
                )
            except Exception:
                pass  # Fall through to file-based

        # Fall back to file-based client
        acct = account or self._default_account
        result_order_id = self._file_client.reverse_position(
            account=acct,
            instrument=instrument,
            action=action,
            quantity=quantity,
            order_type=order_type,
            limit_price=limit_price,
            stop_price=stop_price,
            time_in_force=tif,
            oco_id=oco,
            order_id=order_id,
            strategy=strategy,
            strategy_id=strategy_id,
        )
        return {"order_id": result_order_id, "success": True}

    def reverse_position_file(
        self,
        instrument: str,
        action: str,
        quantity: int,
        order_type: str = "MARKET",
        limit_price: float = 0.0,
        stop_price: float = 0.0,
        tif: str = "DAY",
        oco: str = "",
        order_id: str = "",
        strategy: str = "",
        strategy_id: str = "",
        account: str | None = None,
    ) -> str:
        """Reverse position via file-based client directly."""
        acct = account or self._default_account
        return self._file_client.reverse_position(
            account=acct,
            instrument=instrument,
            action=action,
            quantity=quantity,
            order_type=order_type,
            limit_price=limit_price,
            stop_price=stop_price,
            time_in_force=tif,
            oco_id=oco,
            order_id=order_id,
            strategy=strategy,
            strategy_id=strategy_id,
        )

    # ------------------------------------------------------------------
    # Account Info - File-based Primary with DLL fallback
    # ------------------------------------------------------------------
    def get_account_info(self, account: str | None = None) -> Dict[str, Any]:
        """Get account info via file-based client with DLL fallback."""
        try:
            return self._file_client.get_account_info(account)
        except Exception as e:
            logger.warning(f"File-based get_account_info failed: {e}, using DLL fallback")
            return self.get_account_info_dll(account)

    def get_account_info_dll(self, account: str | None = None) -> Dict[str, Any]:
        """Get account info via DLL client directly."""
        account_name = account or self.account
        info = self._dll_client.get_account_info(account_name)
        # Extract from SimpleNamespace structure
        if hasattr(info, 'balances') and info.balances:
            balance = info.balances[0]
            return {
                "name": account_name,
                "buying_power": getattr(balance, 'buying_power', 0),
                "cash_value": getattr(balance, 'cash_balance', 0),
                "pnl": getattr(balance, 'realized_pnl', 0),
                "status": "Connected" if self._connected else "Disconnected",
            }
        return {
            "name": account_name,
            "buying_power": 0,
            "cash_value": 0,
            "pnl": self._dll_client.get_realized_pnl(account_name),
            "status": "Connected" if self._connected else "Disconnected",
        }

    def get_account_info_file(self, account: str | None = None) -> Dict[str, Any]:
        """Get account info via file-based client directly."""
        return self._file_client.get_account_info(account)

    def get_accounts(self) -> List[str]:
        """Get accounts via file-based client with DLL fallback."""
        try:
            return self._file_client.get_accounts()
        except Exception as e:
            logger.warning(f"File-based get_accounts failed: {e}, using DLL fallback")
            return self.get_accounts_dll()

    def get_accounts_dll(self) -> List[str]:
        """Get accounts via DLL client directly."""
        return self._dll_client.get_accounts()

    def get_accounts_file(self) -> List[str]:
        """Get accounts via file-based client directly."""
        return self._file_client.get_accounts()

    def set_account(self, account: str) -> bool:
        """Set account via file-based client."""
        self.account = account
        return self._file_client.set_account(account)

    # DLL account value functions (primary)
    def get_buying_power(self, account: str | None = None) -> float:
        """Get buying power via DLL client with file-based fallback."""
        try:
            info = self._dll_client.get_account_info(account)
            if hasattr(info, 'balances') and info.balances:
                return getattr(info.balances[0], 'buying_power', 0)
            return 0
        except Exception as e:
            logger.warning(f"DLL get_buying_power failed: {e}, using file-based fallback")
            return self.get_buying_power_file(account)

    def get_buying_power_file(self, account: str | None = None) -> float:
        """Get buying power via file-based client directly."""
        info = self._file_client.get_account_info(account)
        return float(info.get("buying_power", 0))

    def get_cash_value(self, account: str | None = None) -> float:
        """Get cash value via DLL client with file-based fallback."""
        try:
            info = self._dll_client.get_account_info(account)
            if hasattr(info, 'balances') and info.balances:
                return getattr(info.balances[0], 'cash_balance', 0)
            return 0
        except Exception as e:
            logger.warning(f"DLL get_cash_value failed: {e}, using file-based fallback")
            return self.get_cash_value_file(account)

    def get_cash_value_file(self, account: str | None = None) -> float:
        """Get cash value via file-based client directly."""
        info = self._file_client.get_account_info(account)
        return float(info.get("cash_value", 0))

    def get_realized_pnl(self, account: str | None = None) -> float:
        """Get realized P&L via DLL client with file-based fallback."""
        try:
            return self._dll_client.get_realized_pnl(account)
        except Exception as e:
            logger.warning(f"DLL get_realized_pnl failed: {e}, using file-based fallback")
            return self.get_realized_pnl_file(account)

    def get_realized_pnl_file(self, account: str | None = None) -> float:
        """Get realized P&L via file-based client directly."""
        info = self._file_client.get_account_info(account)
        return float(info.get("pnl", 0))

    # ------------------------------------------------------------------
    # Auto-Breakeven - File-based only
    # ------------------------------------------------------------------
    def set_auto_breakeven(
        self,
        instrument: str,
        be1_offset: float = 5.0,
        be2_offset: float = 8.0,
        be3_offset: float = 12.0,
        position_side: str = "AUTO",
        offset_trigger: float = 1.2,
    ) -> dict:
        """Set auto-breakeven via file-based client."""
        return self._file_client.set_auto_breakeven(
            instrument, be1_offset, be2_offset, be3_offset, position_side, offset_trigger
        )

    # ------------------------------------------------------------------
    # Status and misc - File-based
    # ------------------------------------------------------------------
    def get_status(self) -> Dict[str, Any]:
        """Get status via file-based client."""
        return self._file_client.get_status()

    def ping(self) -> str:
        """Ping via file-based client."""
        return self._file_client.ping()

    # ------------------------------------------------------------------
    # Direct client access for advanced use
    # ------------------------------------------------------------------
    @property
    def dll_client(self) -> NT8ManagedClient:
        """Direct access to DLL client for advanced operations."""
        return self._dll_client

    @property
    def file_client(self) -> NT8Client:
        """Direct access to file-based client for advanced operations."""
        return self._file_client
