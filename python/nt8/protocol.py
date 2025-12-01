import struct
from datetime import datetime


class BinaryProtocol:
    """Efficient binary protocol for NT8 communication"""

    # Message type identifiers
    MSG_TICK = 1
    MSG_ORDER_UPDATE = 2
    MSG_POSITION_UPDATE = 3
    MSG_ACCOUNT_UPDATE = 4
    MSG_DEPTH = 5
    MSG_INSTRUMENT_INFO = 6
    MSG_ERROR = 99
    
    @staticmethod
    def encode_order_command(action: str, instrument: str, quantity: int,
                            order_type: str, limit_price: float = 0.0,
                            stop_price: float = 0.0, tif: str = "DAY",
                            signal_name: str = "") -> bytes:
        """
        Encode order command as binary message
        Format: action(1) + instrument(32) + quantity(4) + type(1) + 
                tif(8) + limit(8) + stop(8) + signal_name(32)
        Total: 94 bytes
        """
        action_byte = 1 if action == "BUY" else 2
        type_map = {"MARKET": 1, "LIMIT": 2, "STOP_MARKET": 3, "STOP_LIMIT": 4}
        type_byte = type_map.get(order_type, 1)
        tif_bytes = tif.encode('utf-8')[:8].ljust(8, b'\\x00')
        instrument_bytes = instrument.encode('utf-8')[:32].ljust(32, b'\\x00')
        signal_bytes = signal_name.encode('utf-8')[:32].ljust(32, b'\\x00')
        
        return struct.pack('B32sIB8sdd32s',
            action_byte, instrument_bytes, quantity, type_byte,
            tif_bytes, limit_price, stop_price, signal_bytes)
    
    @staticmethod
    def decode_tick_data(data: bytes) -> dict:
        """
        Decode tick data message
        Format: msg_type(1) + timestamp(8) + price(8) + volume(8) + 
                bid(8) + ask(8) + instrument(32)
        Total: 73 bytes
        """
        unpacked = struct.unpack('Bddqdd32s', data)
        return {
            'timestamp': unpacked[1],
            'price': unpacked[2],
            'volume': unpacked[3],
            'bid': unpacked[4],
            'ask': unpacked[5],
            'instrument': unpacked[6].decode('utf-8').rstrip('\\x00')
        }
    
    @staticmethod
    def decode_order_update(data: bytes) -> dict:
        """
        Decode order update message
        Format: msg_type(1) + order_id(32) + state(1) + filled(4) + 
                remaining(4) + avg_price(8) + timestamp(8)
        Total: 58 bytes
        """
        unpacked = struct.unpack('B32sBIIdd', data)
        state_map = {1: "SUBMITTED", 2: "ACCEPTED", 3: "WORKING", 
                     4: "FILLED", 5: "PART_FILLED", 6: "CANCELLED", 7: "REJECTED"}
        
        return {
            'order_id': unpacked[1].decode('utf-8').rstrip('\\x00'),
            'state': state_map.get(unpacked[2], "UNKNOWN"),
            'filled': unpacked[3],
            'remaining': unpacked[4],
            'avg_price': unpacked[5],
            'timestamp': unpacked[6]
        }
    
    @staticmethod
    def decode_position_update(data: bytes) -> dict:
        """Decode position update message"""
        unpacked = struct.unpack('B32sBidd', data)
        position_map = {0: "FLAT", 1: "LONG", 2: "SHORT"}

        return {
            'instrument': unpacked[1].decode('utf-8').rstrip('\\x00'),
            'position': position_map.get(unpacked[2], "FLAT"),
            'quantity': unpacked[3],
            'avg_price': unpacked[4],
            'unrealized_pnl': unpacked[5]
        }

    @staticmethod
    def decode_account_update(data: bytes) -> dict:
        """
        Decode account update message
        Format: msg_type(1) + account_name(32) + timestamp(8) + cash_value(8) +
                buying_power(8) + realized_pnl(8) + unrealized_pnl(8) +
                net_liquidation(8) + update_type(16)
        Total: 97 bytes
        """
        try:
            unpacked = struct.unpack('B32sddddddd16s', data)

            return {
                'account_name': unpacked[1].decode('utf-8').rstrip('\\x00'),
                'timestamp': unpacked[2],
                'cash_value': unpacked[3],
                'buying_power': unpacked[4],
                'realized_pnl': unpacked[5],
                'unrealized_pnl': unpacked[6],
                'net_liquidation': unpacked[7],
                'update_type': unpacked[8].decode('utf-8').rstrip('\\x00')
            }
        except struct.error:
            # Fallback for partial updates
            return {
                'account_name': 'Unknown',
                'timestamp': 0.0,
                'cash_value': 0.0,
                'buying_power': 0.0,
                'realized_pnl': 0.0,
                'unrealized_pnl': 0.0,
                'net_liquidation': 0.0,
                'update_type': 'BALANCE'
            }

    @staticmethod
    def decode_instrument_info(data: bytes) -> dict:
        """
        Decode instrument information message
        Format: msg_type(1) + instrument(32) + tick_size(8) + point_value(8) +
                min_move(8) + exchange(16)
        Total: 73 bytes
        """
        try:
            unpacked = struct.unpack('B32sddd16s', data)

            return {
                'instrument': unpacked[1].decode('utf-8').rstrip('\\x00'),
                'tick_size': unpacked[2],
                'point_value': unpacked[3],
                'min_move': unpacked[4],
                'exchange': unpacked[5].decode('utf-8').rstrip('\\x00')
            }
        except struct.error:
            return {
                'instrument': 'Unknown',
                'tick_size': 0.25,
                'point_value': 12.50,
                'min_move': 0.25,
                'exchange': 'Unknown'
            }

    @staticmethod
    def encode_cancel_command(order_id: str) -> bytes:
        """
        Encode order cancellation command
        Format: order_id(32)
        """
        order_id_bytes = order_id.encode('utf-8')[:32].ljust(32, b'\\x00')
        return order_id_bytes

    @staticmethod
    def encode_modify_command(order_id: str, quantity: int = 0,
                              limit_price: float = 0.0,
                              stop_price: float = 0.0) -> bytes:
        """
        Encode order modification command
        Format: order_id(32) + quantity(4) + limit_price(8) + stop_price(8)
        Total: 52 bytes
        """
        order_id_bytes = order_id.encode('utf-8')[:32].ljust(32, b'\\x00')
        return struct.pack('32sIdd', order_id_bytes, quantity, limit_price, stop_price)