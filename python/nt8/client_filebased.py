"""
NT8 Client - File-Based Communication
Fast event-driven communication using file monitoring
"""

import logging
import os
import time
import uuid
from datetime import datetime
from pathlib import Path
from typing import Any, Dict, List, Optional


logger = logging.getLogger(__name__)


class NT8Client:
    """Fast file-based communication with NinjaTrader 8"""

    def __init__(self, documents_dir: Optional[str] = None, command_timeout: Optional[float] = None):
        """Initialize NT8 client with file-based communication."""
        if documents_dir is None:
            documents_dir = os.path.expanduser("~/Documents")

        nt_dir = Path(documents_dir) / "NinjaTrader 8"
        incoming_base = nt_dir / "incoming"
        outgoing_base = nt_dir / "outgoing"
        self.incoming_dir = incoming_base / "python"
        self.outgoing_dir = outgoing_base / "python"

        # Ensure the shared ATI directories exist before enabling watcher access.
        incoming_base.mkdir(parents=True, exist_ok=True)
        outgoing_base.mkdir(parents=True, exist_ok=True)
        self.incoming_dir.mkdir(parents=True, exist_ok=True)
        self.outgoing_dir.mkdir(parents=True, exist_ok=True)

        # Track the native NT8 order IDs and user tags so higher level code can correlate
        self._native_order_ids: Dict[str, str] = {}
        self._order_tags: Dict[str, str] = {}

        env_timeout = os.getenv("NT8_COMMAND_TIMEOUT")
        default_timeout = 5.0
        if env_timeout:
            try:
                default_timeout = float(env_timeout)
            except ValueError:
                pass
        if command_timeout is not None:
            try:
                default_timeout = float(command_timeout)
            except ValueError:
                pass
        self.default_command_timeout = max(default_timeout, 1.0)

    def _format_command(self, *fields: object) -> str:
        """Pad or trim the command to the 13-field ATI layout."""
        string_fields = ["" if field is None else str(field) for field in fields]
        if len(string_fields) > 13:
            raise ValueError("ATI command accepts up to 13 fields")
        string_fields.extend([""] * (13 - len(string_fields)))
        return ";".join(string_fields[:13])

    def send_command(self, command: str, timeout: Optional[float] = None) -> str:
        """Send command and wait for response."""
        effective_timeout = timeout if timeout is not None else self.default_command_timeout
        if effective_timeout <= 0:
            effective_timeout = self.default_command_timeout
        cmd_id = str(uuid.uuid4())
        cmd_file = self.incoming_dir / f"oif{cmd_id}.txt"
        response_file = self.outgoing_dir / f"oif{cmd_id}.txt"

        try:
            cmd_file.write_text(command)

            start_time = time.time()
            while time.time() - start_time < effective_timeout:
                if response_file.exists():
                    # Retry reading the file if it's locked
                    response = None
                    for retry in range(3):
                        try:
                            response = response_file.read_text()
                            break
                        except (OSError, PermissionError):
                            if retry < 2:
                                time.sleep(0.05)
                                continue
                            else:
                                raise
                    
                    # Retry deleting the file if it's locked
                    for retry in range(3):
                        try:
                            response_file.unlink()
                            break
                        except (OSError, PermissionError):
                            if retry < 2:
                                time.sleep(0.05)
                                continue
                    
                    return response if response is not None else "ERROR|File read failed"
                time.sleep(0.01)

            raise TimeoutError(f"No response after {effective_timeout}s")

        finally:
            if cmd_file.exists():
                try:
                    cmd_file.unlink()
                except OSError:
                    pass

    def ping(self) -> str:
        """Test connection to NT8."""
        try:
            response = self.send_command(self._format_command("PING"), timeout=2.0)
            return response.strip()
        except TimeoutError:
            return "TIMEOUT"

    def get_status(self) -> dict:
        """Get adapter status."""
        response = self.send_command(self._format_command("STATUS"))
        parts = response.split("|")

        if parts and parts[0] == "OK":
            return {
                "status": parts[1] if len(parts) > 1 else "",
                "commands_processed": parts[2] if len(parts) > 2 else "",
                "account": parts[3] if len(parts) > 3 else ""
            }
        raise RuntimeError(f"Status error: {response}")

    def get_account_info(self, account: Optional[str] = None) -> dict:
        """Get account information."""
        response = self.send_command(self._format_command("ACCOUNT_INFO"))
        parts = response.split("|")

        if parts and parts[0] == "OK":
            return {
                "name": parts[1] if len(parts) > 1 else "",
                "status": parts[2] if len(parts) > 2 else "",
                "buying_power": parts[3] if len(parts) > 3 else "",
                "pnl": parts[4] if len(parts) > 4 else "",
                "cash_value": parts[5] if len(parts) > 5 else "",
            }
        raise RuntimeError(f"Account error: {response}")

    def get_positions(self) -> list:
        """Get all open positions."""
        response = self.send_command(self._format_command("GET_POSITIONS"))
        parts = response.strip().split("|")

        if not parts or parts[0] != "OK":
            raise RuntimeError(f"Positions error: {response}")

        positions = []
        # If only "OK" is returned, that means no positions
        if len(parts) == 1:
            return positions
            
        for payload in parts[1:]:
            if payload.strip():
                pos_parts = payload.split(",")
                if len(pos_parts) >= 5:
                    try:
                        positions.append({
                            "instrument": pos_parts[0],
                            "position": pos_parts[1],
                            "quantity": int(pos_parts[2]),
                            "avg_price": float(pos_parts[3]),
                            "unrealized_pnl": float(pos_parts[4])
                        })
                    except (ValueError, IndexError):
                        continue  # Skip malformed position data

        return positions

    def get_orders(self) -> list:
        """Get all active orders."""
        response = self.send_command(self._format_command("GET_ORDERS"))
        parts = response.strip().split("|")

        if not parts or parts[0] != "OK":
            raise RuntimeError(f"Orders error: {response}")

        orders = []
        # If only "OK" is returned, that means no orders
        if len(parts) == 1:
            return orders
            
        for payload in parts[1:]:
            if payload.strip():
                order_parts = payload.split(",")
                if len(order_parts) >= 6:
                    try:
                        order_info = {
                            "name": order_parts[0],
                            "instrument": order_parts[1],
                            "action": order_parts[2],
                            "type": order_parts[3],
                            "quantity": int(order_parts[4]),
                            "state": order_parts[5]
                        }
                        if len(order_parts) >= 7 and order_parts[6]:
                            order_info["id"] = order_parts[6]
                        if len(order_parts) >= 8 and order_parts[7]:
                            order_info["filled"] = int(float(order_parts[7]))
                        if len(order_parts) >= 9 and order_parts[8]:
                            order_info["remaining"] = int(float(order_parts[8]))
                        if len(order_parts) >= 10 and order_parts[9]:
                            try:
                                order_info["avg_price"] = float(order_parts[9])
                            except ValueError:
                                order_info["avg_price"] = 0.0
                        if len(order_parts) >= 11 and order_parts[10]:
                            order_info["tag"] = order_parts[10]
                            self._order_tags[order_parts[0]] = order_parts[10]
                        orders.append(order_info)
                    except (ValueError, IndexError):
                        continue  # Skip malformed order data

        return orders

    def place_order(
        self,
        account: str,
        instrument: str,
        action: str,
        quantity: int,
        order_type: str = "MARKET",
        limit_price: float = 0,
        stop_price: float = 0,
        time_in_force: str = "DAY",
        oco_id: str = "",
        order_id: Optional[str] = None,
        strategy: str = "",
        strategy_id: str = "",
        user_tag: Optional[str] = None,
    ) -> str:
        """Place an order via the ATI layout, letting the adapter assign the final ID."""

        command = self._format_command(
            "PLACE",
            account,
            instrument,
            action.upper(),
            quantity,
            order_type.upper(),
            limit_price if limit_price else "",
            stop_price if stop_price else "",
            time_in_force.upper(),
            oco_id,
            order_id or "",
            strategy,
            user_tag if user_tag is not None else strategy_id,
        )

        response = self.send_command(command)
        if response.strip().startswith("ERROR"):
            raise RuntimeError(f"Order error: {response}")

        parts = response.strip().split("|")
        if not parts or parts[0] != "OK" or len(parts) < 2:
            raise RuntimeError(f"Unexpected order response: {response}")

        adapter_order_id = parts[1].strip() or (order_id or "")
        if len(parts) >= 3 and parts[2].strip():
            self._native_order_ids[adapter_order_id] = parts[2].strip()
        if user_tag:
            self._order_tags[adapter_order_id] = user_tag

        return adapter_order_id

    def get_native_order_id(self, client_order_id: str) -> Optional[str]:
        """Return the NT8-generated order ID, if available."""
        return self._native_order_ids.get(client_order_id)

    def get_order_tag(self, client_order_id: str) -> Optional[str]:
        """Return the user-supplied tag associated with an order, if any."""
        return self._order_tags.get(client_order_id)

    def cancel_order(self, order_id: str) -> bool:
        """Cancel an order by ID."""
        command = self._format_command("CANCEL", "", "", "", "", "", "", "", "", "", order_id)
        response = self.send_command(command)
        if response.strip().startswith("ERROR"):
            raise RuntimeError(f"Cancel error: {response}")
        return True

    def modify_order(
        self,
        order_id: str,
        quantity: Optional[int] = None,
        limit_price: Optional[float] = None,
        stop_price: Optional[float] = None,
    ) -> bool:
        """Modify an existing order."""
        # ATI Format: CHANGE;;;;<QUANTITY>;;<LIMIT PRICE>;<STOP PRICE>;;;<ORDER ID>;;
        command = self._format_command(
            "CHANGE",
            "",  # account
            "",  # instrument
            "",  # action
            quantity if quantity is not None else "",
            "",  # order_type
            limit_price if limit_price is not None else "",
            stop_price if stop_price is not None else "",
            "",  # time_in_force
            "",  # oco_id
            order_id,
            "",  # strategy
            ""   # strategy_id
        )
        response = self.send_command(command)
        if response.strip().startswith("ERROR"):
            raise RuntimeError(f"Modify error: {response}")
        return True

    def close_position(self, account: str, instrument: str) -> bool:
        """Close position for the supplied instrument."""
        command = self._format_command("CLOSEPOSITION", account, instrument)
        response = self.send_command(command)
        if response.strip().startswith("ERROR"):
            raise RuntimeError(f"Close position error: {response}")
        return True

    def flatten_everything(self) -> bool:
        """Close all positions and cancel all orders."""
        response = self.send_command(self._format_command("FLATTENEVERYTHING"))
        if response.strip().startswith("ERROR"):
            raise RuntimeError(f"Flatten error: {response}")
        return True

    def reverse_position(
        self,
        account: str,
        instrument: str,
        action: str,
        quantity: int,
        order_type: str = "MARKET",
        limit_price: float = 0,
        stop_price: float = 0,
        time_in_force: str = "DAY",
        oco_id: str = "",
        order_id: Optional[str] = None,
        strategy: str = "",
        strategy_id: str = "",
    ) -> str:
        """Reverse position via the ATI REVERSEPOSITION command.

        This command closes the current position and opens a new position in the opposite direction.
        Format: REVERSEPOSITION;<ACCOUNT>;<INSTRUMENT>;<ACTION>;<QTY>;<ORDER TYPE>;[LIMIT PRICE];[STOP PRICE];<TIF>;[OCO ID];[ORDER ID];[STRATEGY];[STRATEGY ID]

        Args:
            account: Trading account name
            instrument: Instrument symbol (e.g., "ES 12-24")
            action: "BUY" or "SELL" - the direction of the NEW position
            quantity: Number of contracts for the new position
            order_type: "MARKET", "LIMIT", "STOP", "STOPLIMIT"
            limit_price: Limit price (for LIMIT/STOPLIMIT orders)
            stop_price: Stop price (for STOP/STOPLIMIT orders)
            time_in_force: "DAY", "GTC", etc.
            oco_id: OCO group ID (optional)
            order_id: Custom order ID (optional)
            strategy: ATM strategy name (optional)
            strategy_id: Strategy ID (optional)

        Returns:
            Order ID of the reverse position order
        """
        command = self._format_command(
            "REVERSEPOSITION",
            account,
            instrument,
            action.upper(),
            quantity,
            order_type.upper(),
            limit_price if limit_price else "",
            stop_price if stop_price else "",
            time_in_force.upper(),
            oco_id,
            order_id or "",
            strategy,
            strategy_id,
        )

        response = self.send_command(command)
        if response.strip().startswith("ERROR"):
            raise RuntimeError(f"Reverse position error: {response}")

        parts = response.strip().split("|")
        if not parts or parts[0] != "OK" or len(parts) < 2:
            raise RuntimeError(f"Unexpected reverse position response: {response}")

        adapter_order_id = parts[1].strip() or (order_id or "")
        if len(parts) >= 3 and parts[2].strip():
            self._native_order_ids[adapter_order_id] = parts[2].strip()

        return adapter_order_id

    def get_accounts(self) -> list:
        """Get list of available accounts."""
        response = self.send_command(self._format_command("GET_ACCOUNTS"))
        parts = response.split("|")
        
        if parts and parts[0] == "OK":
            return [part for part in parts[1:] if part]
        raise RuntimeError(f"Accounts error: {response}")

    def set_account(self, account_name: str) -> bool:
        """Set the active trading account."""
        response = self.send_command(self._format_command("SET_ACCOUNT", account_name))
        if response.strip().startswith("ERROR"):
            raise RuntimeError(f"Set account error: {response}")
        return True

    def cancel_all_orders(self) -> bool:
        """Cancel all active orders."""
        response = self.send_command(self._format_command("CANCELALLORDERS"))
        if response.strip().startswith("ERROR"):
            raise RuntimeError(f"Cancel all orders error: {response}")
        return True

    def subscribe_market_data(self, instrument: str) -> bool:
        """Subscribe to Level 1 market data for an instrument."""
        response = self.send_command(self._format_command("SUBSCRIBE_MARKET_DATA", instrument))
        if response.strip().startswith("ERROR"):
            raise RuntimeError(f"Subscribe market data error: {response}")
        return True

    def set_auto_breakeven(self, instrument: str, be1_offset: float = 5.0, 
                          be2_offset: float = 8.0, be3_offset: float = 12.0,
                          position_side: str = "AUTO", offset_trigger: float = 1.2) -> dict:
        """
        Set up Auto-Breakeven levels for a position using NT8's dynamic breakeven logic.
        
        Args:
            instrument: Trading instrument (e.g., "ES 12-25")
            be1_offset: First breakeven offset in ticks (default: 5)
            be2_offset: Second breakeven offset in ticks (default: 8)
            be3_offset: Third breakeven offset in ticks (default: 12)
            position_side: Position side ("LONG", "SHORT", or "AUTO" to detect)
            offset_trigger: Additional trigger offset in ticks (default: 1.2)
            
        Returns:
            dict: Breakeven configuration details
            
        Example:
            # For LONG position @ 25100:
            # BE1 trigger: 25100 + (5 + 1.2) = 25106.2, new stop: 25105
            # BE2 trigger: 25100 + (8 + 1.2) = 25109.2, new stop: 25108  
            # BE3 trigger: 25100 + (12 + 1.2) = 25113.2, new stop: 25112
            result = client.set_auto_breakeven("NQ 12-25", 5, 8, 12, "AUTO", 1.2)
        """
        # Auto-detect position side if not specified
        if position_side == "AUTO":
            try:
                positions = self.get_positions()
                for pos in positions:
                    if instrument.replace(" ", "").upper() in pos['instrument'].replace(" ", "").upper():
                        position_side = pos['position'].upper()
                        break
                else:
                    raise RuntimeError(f"No position found for {instrument}")
            except Exception as e:
                raise RuntimeError(f"Failed to auto-detect position side: {e}")
        
        command = self._format_command("AUTO_BREAKEVEN", instrument, be1_offset, be2_offset, be3_offset, position_side, offset_trigger)
        response = self.send_command(command)
        
        if response.strip().startswith("ERROR"):
            raise RuntimeError(f"Auto-Breakeven error: {response}")
            
        # Parse the response to return breakeven details
        if response.startswith("OK|Breakeven set:"):
            parts = response.split(": ")[1].split(", ")
            return {
                "status": "success",
                "entry_price": float(parts[0].split("=")[1]),
                "be1_price": float(parts[1].split("=")[1]),
                "be2_price": float(parts[2].split("=")[1]),
                "be3_price": float(parts[3].split("=")[1]),
                "instrument": instrument,
                "position_side": position_side
            }
        
        return {"status": "success", "message": response}

    def unsubscribe_market_data(self, instrument: str) -> bool:
        """Unsubscribe from market data for an instrument."""
        response = self.send_command(self._format_command("UNSUBSCRIBE_MARKET_DATA", instrument))
        if response.strip().startswith("ERROR"):
            raise RuntimeError(f"Unsubscribe market data error: {response}")
        return True

    def get_market_data(self, instrument: str, *, level: str = "L1") -> dict:
        """Get Level 1 (default) or Level 2 market data for an instrument."""

        normalized_level = (level or "L1").strip().upper()
        response = self.send_command(self._format_command("GET_MARKET_DATA", instrument, normalized_level))
        
        logger.info("print out of market data response: %s", response)
        
        parts = response.strip().split("|")

        if not parts or parts[0] != "OK":
            raise RuntimeError(f"Market data error: {response}")

        payload = parts[1:]
        reported_level = normalized_level
        if payload and payload[0] in {"L1", "L2"}:
            reported_level = payload[0]
            payload = payload[1:]

        logger.info("print out of market data payload: %s", payload)

        if len(payload) < 3:
            raise RuntimeError(f"Invalid market data format: {response}")

        bid = float(payload[0]) if payload[0] else 0.0
        ask = float(payload[1]) if len(payload) > 1 and payload[1] else 0.0
        last = float(payload[2]) if len(payload) > 2 and payload[2] else 0.0
        volume = float(payload[3]) if len(payload) > 3 and payload[3] else 0.0
        timestamp = datetime.now().isoformat(timespec="milliseconds")

        data = {
            "instrument": instrument,
            "level": reported_level,
            "bid": bid,
            "ask": ask,
            "last": last,
            "volume": volume,
            "timestamp": timestamp,
        }

        depth_payload = payload[4:]
        if reported_level == "L2" or depth_payload:
            data.update(self._parse_market_depth(depth_payload))

        return data

    def _parse_market_depth(self, payload: List[str]) -> Dict[str, Any]:
        """Best-effort parser for Level 2 depth payloads."""
        bids: List[Dict[str, float]] = []
        asks: List[Dict[str, float]] = []

        for entry in payload:
            normalized = (entry or "").strip()
            if not normalized:
                continue

            side = None
            remainder = normalized
            if "@" in normalized:
                side, remainder = normalized.split("@", 1)
            elif ":" in normalized:
                side, remainder = normalized.split(":", 1)

            if not side or not remainder:
                continue

            side = side.strip().upper()
            parts = [p.strip() for p in remainder.split(",") if p.strip()]
            if len(parts) < 2:
                continue

            try:
                price = float(parts[0])
                size = float(parts[1])
            except ValueError:
                continue

            record = {"price": price, "size": size}
            if side.startswith("B"):
                bids.append(record)
            elif side.startswith("A"):
                asks.append(record)

        return {"bids": bids, "asks": asks, "raw_depth": payload}
