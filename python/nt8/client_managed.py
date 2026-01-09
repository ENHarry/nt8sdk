"""Managed NT8 client powered by pythonnet and NinjaTrader.Client.dll."""

from __future__ import annotations

import os
import logging
from datetime import datetime
from functools import lru_cache
from pathlib import Path
from types import SimpleNamespace
from typing import Any, Dict, Iterable, List, Sequence, Tuple

logger = logging.getLogger(__name__)

DEFAULT_DLL_PATH = Path(
    os.getenv("NT8_CLIENT_DLL")
    or os.getenv("NINJATRADER_CLIENT_DLL")
    or r"C:\\Program Files\\NinjaTrader 8\\bin\\NinjaTrader.Client.dll"
)


def _resolve_dll_path(path: os.PathLike[str] | str | None) -> Path:
    candidate = Path(path or DEFAULT_DLL_PATH).expanduser()
    if not candidate.exists():
        raise FileNotFoundError(
            f"NinjaTrader.Client.dll not found at {candidate}. "
            "Override via NT8_CLIENT_DLL if installed elsewhere."
        )
    return candidate


@lru_cache(maxsize=1)
def _load_client_type(dll_path: str):
    try:
        import clr  # type: ignore
    except ImportError as exc:  # pragma: no cover - environment issue
        raise RuntimeError(
            "pythonnet is required for the managed NT8 client. "
            "Install it via `pip install pythonnet`."
        ) from exc

    clr.AddReference(dll_path)  # type: ignore[attr-defined]
    # Import after the CLR reference has been registered.
    from NinjaTrader.Client import Client  # type: ignore

    return Client


class NT8ManagedClient:
    """Thin Python wrapper over NinjaTrader.Client.Client via pythonnet."""

    def __init__(
        self,
        *,
        dll_path: os.PathLike[str] | str | None = None,
        host: str = "localhost",
        port: int = 36973,
        account: str | None = None,
        auto_connect: bool = True,
        show_connection_popup: bool = False,
    ) -> None:
        self.dll_path = _resolve_dll_path(dll_path)
        client_type = _load_client_type(str(self.dll_path))
        self._client = client_type()
        self._subscriptions: set[str] = set()
        self.account = account or os.getenv("NT8_ACCOUNT") or "Sim101"
        self._connected = False
        self._show_popup = 1 if show_connection_popup else 0

        # Volume tracking: accumulate trade sizes per time bar (1 second) per instrument
        self._bar_volume: Dict[str, float] = {}  # Volume for current bar
        self._bar_start_time: Dict[str, float] = {}  # Start time of current bar (unix timestamp)
        self._last_trade_price: Dict[str, float] = {}  # Track price to detect new trades
        self._bar_duration: float = 1.0  # Bar duration in seconds

        setup_result = self._client.SetUp(host, port)
        if setup_result != 0:
            logger.warning("NT8 DLL SetUp(%s, %s) returned %s", host, port, setup_result)

        if auto_connect:
            self.connect()

    # ------------------------------------------------------------------
    # Lifecycle helpers
    # ------------------------------------------------------------------
    def connect(self, show_message: bool | None = None) -> bool:
        """Attempt to connect to the NinjaTrader automation server."""
        flag = self._show_popup if show_message is None else (1 if show_message else 0)
        result = self._client.Connected(flag)
        self._connected = result == 0
        if not self._connected:
            logger.warning("NT8 DLL connection failed with code %s", result)
        return self._connected

    def authenticate(self) -> bool:
        """Alias kept for parity with existing adapters."""
        return self.connect()

    def tear_down(self) -> None:
        """Disconnect from NinjaTrader. Returns 0 on success, -1 on error."""
        try:
            result = self._client.TearDown()
            if result == 0:
                logger.info("NT8 DLL TearDown successful")
            else:
                logger.warning("NT8 DLL TearDown returned code: %s", result)
            self._connected = False
        except AttributeError:
            logger.debug("NT8 DLL TearDown method not available - connection may already be closed")
        except Exception as exc:  # pragma: no cover - defensive shutdown
            logger.debug("NT8 DLL TearDown raised %s: %s", type(exc).__name__, exc)

        self._connected = False
        self._subscriptions.clear()

    # ------------------------------------------------------------------
    # Helpers
    # ------------------------------------------------------------------
    def _resolve_account(self, explicit: str | None = None) -> str:
        account_name = (explicit or self.account or "").strip()
        if account_name:
            return account_name

        accounts = self.get_accounts()
        if accounts:
            return accounts[0]

        raise RuntimeError("No NT8 account is configured for the managed client")

    def _call_float(self, method_names: Sequence[str] | str, *args: Any) -> float:
        names = (method_names,) if isinstance(method_names, str) else tuple(method_names)
        for name in names:
            method = getattr(self._client, name, None)
            if not callable(method):
                continue
            try:
                value = method(*args)
            except TypeError as exc:
                logger.debug("NT8 method %s rejected args %s: %s", name, args, exc)
                continue
            except Exception as exc:  # pragma: no cover - defensive guard
                logger.warning("NT8 method %s failed: %s", name, exc)
                continue

            try:
                return float(value)  # type: ignore[arg-type]
            except (TypeError, ValueError):
                logger.debug("NT8 method %s returned non-numeric value %r", name, value)
                return 0.0

        return 0.0

    # ------------------------------------------------------------------
    # Market data helpers
    # ------------------------------------------------------------------
    def subscribe_market_data(self, instrument: str) -> None:
        self._ensure_connection()
        instrument = instrument.strip()
        if not instrument:
            raise ValueError("Instrument name is required")

        key = instrument.upper()
        if key in self._subscriptions:
            return

        rc = self._client.SubscribeMarketData(instrument)
        if rc != 0:
            raise RuntimeError(f"SubscribeMarketData failed for {instrument} (code {rc})")
        self._subscriptions.add(key)

    def unsubscribe_market_data(self, instrument: str) -> None:
        instrument = (instrument or "").strip()
        if not instrument:
            return

        rc = self._client.UnsubscribeMarketData(instrument)
        if rc != 0:
            logger.warning("UnsubscribeMarketData(%s) returned %s", instrument, rc)
        self._subscriptions.discard(instrument.upper())

    def _market_data(self, instrument: str, data_type: int) -> float:
        self._ensure_connection()
        value = self._client.MarketData(instrument, data_type)
        return float(value) if value is not None else 0.0

    def _capture_market_data_fields(self, instrument: str, max_fields: int = 8) -> Dict[str, Any]:
        snapshot: Dict[str, Any] = {"fields": {}, "raw_payload": None}
        method = getattr(self._client, "MarketData", None)
        if callable(method):
            for index in range(max_fields):
                try:
                    value = method(instrument, index)
                except TypeError:
                    break
                except Exception as exc:
                    logger.debug("MarketData(%s, %s) failed: %s", instrument, index, exc)
                    break
                if value is None:
                    continue
                try:
                    snapshot["fields"][index] = float(value)  # type: ignore[arg-type]
                except (TypeError, ValueError):
                    snapshot["fields"][index] = value
        else:
            logger.debug("Managed NT8 client exposes no MarketData() method")

        raw_payload = self._try_market_data_raw(instrument)
        if raw_payload is not None:
            snapshot["raw_payload"] = raw_payload
        return snapshot

    def _try_market_data_raw(self, instrument: str) -> Any:
        method = getattr(self._client, "MarketData", None)
        if not callable(method):
            return None
        try:
            return method(instrument)
        except TypeError:
            return None
        except Exception as exc:
            logger.debug("Raw MarketData(%s) call failed: %s", instrument, exc)
            return None

    def _fetch_level2_book(self, instrument: str) -> Dict[str, Any]:
        book = {"bids": [], "asks": [], "raw_depth": None}
        candidate_methods: Tuple[Tuple[str, Tuple[Any, ...]], ...] = (
            ("MarketDepth", (instrument,)),
            ("MarketDepth", (instrument, 10)),
            ("GetMarketDepth", (instrument,)),
            ("GetMarketDepth", (instrument, 10)),
        )

        for method_name, args in candidate_methods:
            method = getattr(self._client, method_name, None)
            if not callable(method):
                continue
            try:
                payload = method(*args)
            except TypeError:
                continue
            except Exception as exc:
                logger.debug("%s%r depth call failed: %s", method_name, args, exc)
                continue

            book["raw_depth"] = payload
            parsed = self._parse_depth_payload(payload)
            book["bids"] = parsed.get("bids", [])
            book["asks"] = parsed.get("asks", [])
            return book

        logger.debug("Managed NT8 client has no exposed market depth API")
        return book

    def _parse_depth_payload(self, payload: Any) -> Dict[str, List[Dict[str, float]]]:
        bids: List[Dict[str, float]] = []
        asks: List[Dict[str, float]] = []

        def _add(entry_list: List[Dict[str, float]], price: Any, size: Any) -> None:
            try:
                entry_list.append({"price": float(price), "size": float(size)})
            except (TypeError, ValueError):
                pass

        if isinstance(payload, str):
            separators = ";" if ";" in payload else "|"
            parts = [segment.strip() for segment in payload.split(separators) if segment.strip()]
            for part in parts:
                if "@" in part:
                    side, remainder = part.split("@", 1)
                elif ":" in part:
                    side, remainder = part.split(":", 1)
                else:
                    continue
                numbers = [token.strip() for token in remainder.split(",") if token.strip()]
                if len(numbers) < 2:
                    continue
                side_key = side.strip().upper()
                if side_key.startswith("B"):
                    _add(bids, numbers[0], numbers[1])
                elif side_key.startswith("A"):
                    _add(asks, numbers[0], numbers[1])
        elif isinstance(payload, dict):
            for key, target in (("bids", bids), ("bid", bids), ("asks", asks), ("ask", asks)):
                entries = payload.get(key)
                if isinstance(entries, (list, tuple)):
                    for row in entries:
                        if isinstance(row, dict):
                            _add(target, row.get("price"), row.get("size"))
                        elif isinstance(row, (list, tuple)) and len(row) >= 2:
                            _add(target, row[0], row[1])
        elif isinstance(payload, (list, tuple)):
            for row in payload:
                if not isinstance(row, (list, tuple)) or len(row) < 3:
                    continue
                side, price, size = row[0], row[1], row[2]
                if str(side).upper().startswith("B"):
                    _add(bids, price, size)
                elif str(side).upper().startswith("A"):
                    _add(asks, price, size)

        return {"bids": bids, "asks": asks}

    def get_market_data(self, instrument: str, *, level: str = "L1") -> Dict[str, Any]:
        instrument = instrument.strip()
        if not instrument:
            raise ValueError("Instrument name is required")

        normalized_level = (level or "L1").strip().upper()
        self.subscribe_market_data(instrument)

        snapshot = self._capture_market_data_fields(instrument)
        fields: Dict[int, Any] = snapshot.get("fields", {})
        logger.debug("Managed market data fields for %s: %s", instrument, fields)
        if snapshot.get("raw_payload") is not None:
            logger.debug("Managed raw MarketData payload for %s: %s", instrument, snapshot["raw_payload"])

        last = float(fields.get(0, 0.0) or 0.0)
        bid = float(fields.get(1, 0.0) or 0.0)
        ask = float(fields.get(2, 0.0) or 0.0)
        trade_size = float(fields.get(3, 0.0) or 0.0)
        timestamp = datetime.now()
        current_time = timestamp.timestamp()

        # Track volume per time bar (1 second bars)
        instrument_key = instrument.upper()
        if instrument_key not in self._bar_volume:
            self._bar_volume[instrument_key] = 0.0
            self._bar_start_time[instrument_key] = current_time
            self._last_trade_price[instrument_key] = 0.0

        # Check if we've moved to a new bar
        bar_start = self._bar_start_time[instrument_key]
        if current_time - bar_start >= self._bar_duration:
            # New bar - reset volume and update start time
            self._bar_volume[instrument_key] = 0.0
            self._bar_start_time[instrument_key] = current_time

        # Only add volume if there was a trade (trade_size > 0 and price changed)
        if trade_size > 0:
            # Check if this is a new trade (price changed or first trade)
            if last != self._last_trade_price[instrument_key]:
                self._bar_volume[instrument_key] += trade_size
                self._last_trade_price[instrument_key] = last

        volume = self._bar_volume[instrument_key]

        data: Dict[str, Any] = {
            "instrument": instrument,
            "level": normalized_level,
            "last": last,
            "bid": bid,
            "ask": ask,
            "volume": volume,
            "timestamp": timestamp,
            "raw_fields": fields,
        }

        if snapshot.get("raw_payload") is not None:
            data["raw_payload"] = snapshot["raw_payload"]

        if normalized_level == "L2":
            depth = self._fetch_level2_book(instrument)
            data.update(depth)

        return data

    def get_last(self, instrument: str) -> float:
        """Get last price for an instrument."""
        return self._market_data(instrument, 0)

    def get_bid(self, instrument: str) -> float:
        """Get bid price for an instrument."""
        return self._market_data(instrument, 1)

    def get_ask(self, instrument: str) -> float:
        """Get ask price for an instrument."""
        return self._market_data(instrument, 2)

    def get_volume(self, instrument: str) -> float:
        """Get current bar volume for an instrument."""
        instrument_key = instrument.upper()
        return self._bar_volume.get(instrument_key, 0.0)

    def reset_volume(self, instrument: str | None = None) -> None:
        """Reset bar volume counter(s).

        Args:
            instrument: Specific instrument to reset, or None to reset all
        """
        if instrument:
            instrument_key = instrument.upper()
            self._bar_volume[instrument_key] = 0.0
            self._bar_start_time[instrument_key] = datetime.now().timestamp()
            self._last_trade_price[instrument_key] = 0.0
        else:
            self._bar_volume.clear()
            self._bar_start_time.clear()
            self._last_trade_price.clear()

    def set_bar_duration(self, seconds: float) -> None:
        """Set the bar duration for volume tracking.

        Args:
            seconds: Bar duration in seconds (default is 1.0)
        """
        self._bar_duration = seconds

    def subscribe(self, instrument: str) -> None:
        """Subscribe to market data (alias for subscribe_market_data)."""
        self.subscribe_market_data(instrument)

    def unsubscribe(self, instrument: str) -> None:
        """Unsubscribe from market data (alias for unsubscribe_market_data)."""
        self.unsubscribe_market_data(instrument)

    def get_quotes(self, symbols: Sequence[str] | str) -> Dict[str, List[Dict[str, Any]]]:
        if isinstance(symbols, str):
            iterable: Iterable[str] = [symbols]
        else:
            iterable = symbols

        quotes: List[Dict[str, Any]] = []
        for symbol in iterable:
            if not symbol:
                continue
            md = self.get_market_data(symbol)
            quotes.append(
                {
                    "symbol": md["instrument"],
                    "bid_price": md["bid"],
                    "ask_price": md["ask"],
                    "last_price": md["last"],
                }
            )
        return {"quotes": quotes}

    # ------------------------------------------------------------------
    # Minimal account/order surfaces (stubs until full support lands)
    # ------------------------------------------------------------------
    def get_accounts(self) -> List[str]:
        method = getattr(self._client, "Accounts", None)
        accounts: List[str] = []

        if callable(method):
            try:
                payload = method()
                if isinstance(payload, str):
                    accounts = [entry.strip() for entry in payload.split("|") if entry.strip()]
                elif isinstance(payload, (list, tuple, set)):
                    accounts = [str(entry).strip() for entry in payload if str(entry).strip()]
            except Exception as exc:  # pragma: no cover - diagnostic only
                logger.debug("NT8 Accounts() call failed: %s", exc)

        if not accounts and self.account:
            accounts = [self.account]

        return accounts

    def get_account_info(self, account: str | None = None) -> SimpleNamespace:
        account_name = self._resolve_account(account)

        cash_balance = self._call_float(("CashValue", "Cash"), account_name)
        buying_power = self._call_float(("BuyingPower", "BuyingPowerValue"), account_name)
        realized_pnl = self._call_float(("RealizedPnL", "RealizedProfitLoss"), account_name)
        unrealized_pnl = self._call_float(("UnrealizedPnL", "UnrealizedProfitLoss", "OpenTradeEquity"), account_name)

        open_trade_equity = unrealized_pnl
        net_liquidation = cash_balance + open_trade_equity

        balance = SimpleNamespace(
            cash_balance=cash_balance,
            open_trade_equity=open_trade_equity,
            realized_pnl=realized_pnl,
            unrealized_pnl=unrealized_pnl,
            buying_power=buying_power or cash_balance,
            net_liquidation=net_liquidation if net_liquidation else cash_balance,
        )

        logger.debug(
            "NT8 balance snapshot %s | cash=%.2f unrealized=%.2f buying_power=%.2f",
            account_name,
            balance.cash_balance,
            balance.unrealized_pnl,
            balance.buying_power,
        )

        return SimpleNamespace(account_name=account_name, balances=[balance])

    def get_account_balance(self, account: str | None = None) -> SimpleNamespace:
        return self.get_account_info(account)

    # ------------------------------------------------------------------
    # Order Management (DLL Command interface)
    # ------------------------------------------------------------------
    def _command(
        self,
        command: str,
        account: str = "",
        instrument: str = "",
        action: str = "",
        quantity: int = 0,
        order_type: str = "",
        limit_price: float = 0,
        stop_price: float = 0,
        time_in_force: str = "",
        oco_id: str = "",
        order_id: str = "",
        strategy: str = "",
        strategy_id: str = "",
    ) -> int:
        """Execute an ATI command via the DLL Command function."""
        self._ensure_connection()
        method = getattr(self._client, "Command", None)
        if not callable(method):
            raise RuntimeError("NT8 DLL does not expose the Command function")

        try:
            result = method(
                command,
                account,
                instrument,
                action,
                quantity,
                order_type,
                limit_price,
                stop_price,
                time_in_force,
                oco_id,
                order_id,
                strategy,
                strategy_id,
            )
            if result is not None:
                try:
                    return int(result)  # type: ignore[arg-type]
                except (TypeError, ValueError):
                    return 0
            return 0
        except Exception as exc:
            logger.error("NT8 Command(%s) failed: %s", command, exc)
            raise

    def new_order_id(self) -> str:
        """Generate a new unique order ID via the DLL."""
        self._ensure_connection()
        method = getattr(self._client, "NewOrderId", None)
        if callable(method):
            try:
                oid = method()
                return str(oid) if oid else ""
            except Exception as exc:
                logger.warning("NewOrderId() failed: %s", exc)
        import uuid
        return str(uuid.uuid4())

    def place_order(
        self,
        account: str | None = None,
        instrument: str = "",
        action: str = "BUY",
        quantity: int = 1,
        order_type: str = "MARKET",
        limit_price: float = 0,
        stop_price: float = 0,
        time_in_force: str = "GTC",
        oco_id: str = "",
        order_id: str = "",
        strategy: str = "",
        strategy_id: str = "",
    ) -> Dict[str, Any]:
        """Place an order via the DLL Command interface."""
        account_name = self._resolve_account(account)
        if not order_id:
            order_id = self.new_order_id()

        # Generate strategy_id when strategy template is provided
        generated_strategy_id = strategy_id
        if strategy and not strategy_id:
            generated_strategy_id = self.new_order_id()

        if hasattr(action, "value"):
            action_str = str(getattr(action, "value")).upper()
        elif isinstance(action, str):
            action_str = action.upper()
        else:
            action_str = str(action).upper()
        if action_str.startswith("S"):
            action_str = "SELL"
        else:
            action_str = "BUY"

        if hasattr(order_type, "value"):
            order_type_str = str(getattr(order_type, "value")).upper()
        elif isinstance(order_type, str):
            order_type_str = order_type.upper()
        else:
            order_type_str = str(order_type).upper()
        type_mapping = {
            "STOP": "STOP",
            "STOPMKT": "STOP",
            "STOP_MARKET": "STOP",
            "LMT": "LIMIT",
            "LIMIT": "LIMIT",
            "MARKET": "MARKET",
            "MKT": "MARKET",
        }
        order_type_str = type_mapping.get(order_type_str, order_type_str)

        result = self._command(
            "PLACE",
            account_name,
            instrument,
            action_str,
            int(quantity),
            order_type_str,
            float(limit_price) if limit_price else 0,
            float(stop_price) if stop_price else 0,
            time_in_force.upper(),
            oco_id,
            order_id,
            strategy,
            generated_strategy_id,
        )

        if result != 0:
            raise RuntimeError(f"place_order failed with code {result}")

        logger.info("NT8 place_order: %s %s %d %s @ %s (id=%s)", action_str, instrument, quantity, order_type_str, limit_price or "MKT", order_id)
        return {
            "order_id": order_id,
            "strategy_id": generated_strategy_id if strategy else "",
        }

    def modify_order(
        self,
        order_id: str,
        quantity: int | None = None,
        limit_price: float | None = None,
        stop_price: float | None = None,
        strategy_id: str = "",
    ) -> bool:
        """Modify an existing order via the DLL Command interface."""
        result = self._command(
            "CHANGE",
            "",  # account
            "",  # instrument
            "",  # action
            int(quantity) if quantity is not None else 0,
            "",  # order_type
            float(limit_price) if limit_price is not None else 0,
            float(stop_price) if stop_price is not None else 0,
            "",  # time_in_force
            "",  # oco_id
            order_id,
            "",  # strategy
            strategy_id,
        )
        if result != 0:
            logger.warning("modify_order(%s) returned code %s", order_id, result)
            return False
        return True

    def cancel_order(self, order_id: str, strategy_id: str = "") -> bool:
        """Cancel an order via the DLL Command interface."""
        result = self._command(
            "CANCEL",
            "",  # account
            "",  # instrument
            "",  # action
            0,   # quantity
            "",  # order_type
            0,   # limit_price
            0,   # stop_price
            "",  # time_in_force
            "",  # oco_id
            order_id,
            "",  # strategy
            strategy_id,
        )
        if result != 0:
            logger.warning("cancel_order(%s) returned code %s", order_id, result)
            return False
        return True

    def cancel_all_orders(self, account: str | None = None) -> bool:
        """Cancel all active orders via the DLL Command interface."""
        result = self._command("CANCELALLORDERS")
        if result != 0:
            logger.warning("cancel_all_orders returned code %s", result)
            return False
        return True

    def close_position(self, account: str | None = None, instrument: str = "") -> bool:
        """Close a position via the DLL Command interface."""
        account_name = self._resolve_account(account)
        result = self._command(
            "CLOSEPOSITION",
            account_name,
            instrument,
        )
        if result != 0:
            logger.warning("close_position(%s, %s) returned code %s", account_name, instrument, result)
            return False
        return True

    def close_strategy(self, strategy_id: str) -> bool:
        """Close an ATM Strategy via the DLL Command interface."""
        result = self._command(
            "CLOSESTRATEGY",
            "",  # account
            "",  # instrument
            "",  # action
            0,   # quantity
            "",  # order_type
            0,   # limit_price
            0,   # stop_price
            "",  # time_in_force
            "",  # oco_id
            "",  # order_id
            "",  # strategy
            strategy_id,
        )
        if result != 0:
            logger.warning("close_strategy(%s) returned code %s", strategy_id, result)
            return False
        return True

    def flatten_everything(self) -> bool:
        """Flatten all positions and cancel all orders via the DLL Command interface."""
        result = self._command("FLATTENEVERYTHING")
        if result != 0:
            logger.warning("flatten_everything returned code %s", result)
            return False
        return True

    def reverse_position(
        self,
        account: str | None = None,
        instrument: str = "",
        action: str = "BUY",
        quantity: int = 1,
        order_type: str = "MARKET",
        limit_price: float = 0,
        stop_price: float = 0,
        time_in_force: str = "GTC",
        oco_id: str = "",
        order_id: str = "",
        strategy: str = "",
        strategy_id: str = "",
    ) -> str:
        """Reverse a position via the DLL Command interface."""
        account_name = self._resolve_account(account)
        if not order_id:
            order_id = self.new_order_id()

        action_str = "SELL" if str(action).upper().startswith("S") else "BUY"
        result = self._command(
            "REVERSEPOSITION",
            account_name,
            instrument,
            action_str,
            int(quantity),
            order_type.upper(),
            float(limit_price) if limit_price else 0,
            float(stop_price) if stop_price else 0,
            time_in_force.upper(),
            oco_id,
            order_id,
            strategy,
            strategy_id,
        )

        if result != 0:
            raise RuntimeError(f"reverse_position failed with code {result}")
        return order_id

    # ------------------------------------------------------------------
    # Position and Order Query (DLL direct methods)
    # ------------------------------------------------------------------
    def get_positions(self, account: str | None = None) -> List[Dict[str, Any]]:
        """Get current positions via DLL MarketPosition and AvgEntryPrice functions."""
        self._ensure_connection()
        account_name = self._resolve_account(account)
        positions: List[Dict[str, Any]] = []

        # Get list of instruments with positions from subscribed market data
        instruments = list(self._subscriptions)

        for instrument in instruments:
            market_pos = self._call_float(("MarketPosition",), instrument, account_name)

            if market_pos != 0:
                avg_price = self._call_float(("AvgEntryPrice",), instrument, account_name)

                if market_pos > 0:
                    position_type = "LONG"
                    quantity = int(market_pos)
                else:
                    position_type = "SHORT"
                    quantity = int(abs(market_pos))

                unrealized_pnl = self._call_float(("UnrealizedPnL", "UnrealizedProfitLoss"), instrument, account_name)
                positions.append({
                    "instrument": instrument,
                    "exch_sym": instrument,  # Alias for bot compatibility
                    "position": position_type,
                    "quantity": quantity,
                    "unrealized_pnl": unrealized_pnl,
                    "avg_price": avg_price,
                    "account": account_name,
                })

        return positions

    def get_orders(self, account: str | None = None) -> List[Dict[str, Any]]:
        """Get active orders via DLL Orders and OrderStatus functions."""
        self._ensure_connection()
        account_name = self._resolve_account(account)
        orders: List[Dict[str, Any]] = []

        orders_method = getattr(self._client, "Orders", None)
        if not callable(orders_method):
            logger.debug("NT8 DLL does not expose Orders() function")
            return orders

        try:
            orders_str = orders_method(account_name)
            if not orders_str:
                return orders

            order_ids = [oid.strip() for oid in str(orders_str).split("|") if oid.strip()]

            for order_id in order_ids:
                status = self.get_order_status(order_id)
                filled = self.get_filled(order_id)
                avg_fill_price = self.get_avg_fill_price(order_id)

                orders.append({
                    "id": order_id,
                    "order_id": order_id,  # Alias for bot compatibility
                    "name": order_id,
                    "state": status,
                    "status": status,  # Alias for bot compatibility
                    "filled": filled,
                    "avg_price": avg_fill_price,
                    "instrument": "",  # DLL Orders() doesn't provide instrument
                    "exch_sym": "",  # Alias - will be empty for DLL orders
                    "side": "",  # Not available from DLL
                    "quantity": 0,  # Not available from DLL
                    "limit_price": 0.0,  # Not available from DLL
                })

        except Exception as exc:
            logger.warning("get_orders failed: %s", exc)

        return orders

    def get_order_status(self, order_id: str) -> str:
        """Get order status via DLL OrderStatus function."""
        self._ensure_connection()
        method = getattr(self._client, "OrderStatus", None)
        if not callable(method):
            return "Unknown"

        try:
            status = method(order_id)
            return str(status) if status else "Unknown"
        except Exception as exc:
            logger.debug("OrderStatus(%s) failed: %s", order_id, exc)
            return "Unknown"

    def get_filled(self, order_id: str) -> int:
        """Get filled quantity via DLL Filled function."""
        self._ensure_connection()
        method = getattr(self._client, "Filled", None)
        if not callable(method):
            return 0

        try:
            filled = method(order_id)
            if filled is not None:
                try:
                    return int(filled)  # type: ignore[arg-type]
                except (TypeError, ValueError):
                    return 0
            return 0
        except Exception as exc:
            logger.debug("Filled(%s) failed: %s", order_id, exc)
            return 0

    def get_avg_fill_price(self, order_id: str) -> float:
        """Get average fill price via DLL AvgFillPrice function."""
        return self._call_float(("AvgFillPrice",), order_id)

    def get_strategies(self, account: str | None = None) -> List[str]:
        """Get ATM strategy IDs via DLL Strategies function."""
        self._ensure_connection()
        account_name = self._resolve_account(account)
        method = getattr(self._client, "Strategies", None)
        if not callable(method):
            return []

        try:
            strategies_str = method(account_name)
            if not strategies_str:
                return []
            return [sid.strip() for sid in str(strategies_str).split("|") if sid.strip()]
        except Exception as exc:
            logger.debug("Strategies(%s) failed: %s", account_name, exc)
            return []

    def get_strategy_position(self, strategy_id: str) -> int:
        """Get strategy position via DLL StrategyPosition function."""
        self._ensure_connection()
        method = getattr(self._client, "StrategyPosition", None)
        if not callable(method):
            return 0

        try:
            pos = method(strategy_id)
            if pos is not None:
                try:
                    return int(pos)  # type: ignore[arg-type]
                except (TypeError, ValueError):
                    return 0
            return 0
        except Exception as exc:
            logger.debug("StrategyPosition(%s) failed: %s", strategy_id, exc)
            return 0

    def get_stop_orders(self, strategy_id: str) -> List[str]:
        """Get Stop Loss order IDs for an ATM strategy via DLL StopOrders function."""
        self._ensure_connection()
        method = getattr(self._client, "StopOrders", None)
        if not callable(method):
            return []

        try:
            orders_str = method(strategy_id)
            if not orders_str:
                return []
            return [oid.strip() for oid in str(orders_str).split("|") if oid.strip()]
        except Exception as exc:
            logger.debug("StopOrders(%s) failed: %s", strategy_id, exc)
            return []

    def get_target_orders(self, strategy_id: str) -> List[str]:
        """Get Profit Target order IDs for an ATM strategy via DLL TargetOrders function."""
        self._ensure_connection()
        method = getattr(self._client, "TargetOrders", None)
        if not callable(method):
            return []

        try:
            orders_str = method(strategy_id)
            if not orders_str:
                return []
            return [oid.strip() for oid in str(orders_str).split("|") if oid.strip()]
        except Exception as exc:
            logger.debug("TargetOrders(%s) failed: %s", strategy_id, exc)
            return []

    def get_market_position(self, instrument: str, account: str | None = None) -> int:
        """Get market position directly via DLL MarketPosition function.

        Returns: 0 for flat, positive for long, negative for short.
        """
        account_name = self._resolve_account(account)
        return int(self._call_float(("MarketPosition",), instrument, account_name))

    def get_realized_pnl(self, account: str | None = None) -> float:
        """Get realized P&L via DLL RealizedPnL function."""
        account_name = self._resolve_account(account)
        return self._call_float(("RealizedPnL", "RealizedProfitLoss"), account_name)

    # ------------------------------------------------------------------
    # Utilities
    # ------------------------------------------------------------------
    def set_account(self, account: str) -> None:
        self.account = account

    def _ensure_connection(self) -> None:
        if not self._connected and not self.connect():
            raise RuntimeError("Unable to connect to NinjaTrader via DLL")

    def __del__(self) -> None:  # pragma: no cover - best effort cleanup
        try:
            self.tear_down()
        except Exception:
            pass
