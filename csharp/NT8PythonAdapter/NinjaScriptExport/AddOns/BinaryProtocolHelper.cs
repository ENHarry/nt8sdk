using System;
using System.Text;

namespace NinjaTrader.NinjaScript.AddOns
{
    /// <summary>
    /// Binary protocol helper for encoding/decoding messages between C# and Python
    /// Matches the protocol defined in Python's protocol.py
    /// </summary>
    public static class BinaryProtocolHelper
    {
        // Message type identifiers (must match Python protocol.py)
        public const byte MSG_TICK = 1;
        public const byte MSG_ORDER_UPDATE = 2;
        public const byte MSG_POSITION_UPDATE = 3;
        public const byte MSG_ACCOUNT_UPDATE = 4;
        public const byte MSG_DEPTH = 5;
        public const byte MSG_INSTRUMENT_INFO = 6;
        public const byte MSG_ERROR = 99;

        // Order action bytes
        public const byte ACTION_BUY = 1;
        public const byte ACTION_SELL = 2;

        // Order type bytes
        public const byte TYPE_MARKET = 1;
        public const byte TYPE_LIMIT = 2;
        public const byte TYPE_STOP_MARKET = 3;
        public const byte TYPE_STOP_LIMIT = 4;

        // Order state bytes
        public const byte STATE_SUBMITTED = 1;
        public const byte STATE_ACCEPTED = 2;
        public const byte STATE_WORKING = 3;
        public const byte STATE_FILLED = 4;
        public const byte STATE_PART_FILLED = 5;
        public const byte STATE_CANCELLED = 6;
        public const byte STATE_REJECTED = 7;

        // Position types
        public const byte POSITION_FLAT = 0;
        public const byte POSITION_LONG = 1;
        public const byte POSITION_SHORT = 2;

        #region Decode Methods (From Python)

        /// <summary>
        /// Decode binary order command from Python
        /// Format: action(1) + instrument(32) + quantity(4) + type(1) + tif(8) + limit(8) + stop(8) + signal(32)
        /// Total: 94 bytes
        /// </summary>
        public static OrderCommand DecodeOrderCommand(byte[] data)
        {
            if (data == null || data.Length < 94)
                throw new ArgumentException("Invalid order command data");

            var cmd = new OrderCommand();
            int offset = 0;

            // Action (1 byte)
            cmd.Action = data[offset] == ACTION_BUY ? "BUY" : "SELL";
            offset += 1;

            // Instrument (32 bytes)
            cmd.Instrument = Encoding.UTF8.GetString(data, offset, 32).TrimEnd('\0');
            offset += 32;

            // Quantity (4 bytes)
            cmd.Quantity = BitConverter.ToInt32(data, offset);
            offset += 4;

            // Order Type (1 byte)
            byte orderType = data[offset];
            cmd.OrderType = orderType switch
            {
                TYPE_MARKET => "MARKET",
                TYPE_LIMIT => "LIMIT",
                TYPE_STOP_MARKET => "STOP_MARKET",
                TYPE_STOP_LIMIT => "STOP_LIMIT",
                _ => "MARKET"
            };
            offset += 1;

            // Time in Force (8 bytes)
            cmd.TimeInForce = Encoding.UTF8.GetString(data, offset, 8).TrimEnd('\0');
            offset += 8;

            // Limit Price (8 bytes)
            cmd.LimitPrice = BitConverter.ToDouble(data, offset);
            offset += 8;

            // Stop Price (8 bytes)
            cmd.StopPrice = BitConverter.ToDouble(data, offset);
            offset += 8;

            // Signal Name (32 bytes)
            cmd.SignalName = Encoding.UTF8.GetString(data, offset, 32).TrimEnd('\0');

            return cmd;
        }

        /// <summary>
        /// Decode cancel order command
        /// Format: order_id(32)
        /// </summary>
        public static string DecodeCancelCommand(byte[] data)
        {
            if (data == null || data.Length < 32)
                throw new ArgumentException("Invalid cancel command data");

            return Encoding.UTF8.GetString(data, 0, 32).TrimEnd('\0');
        }

        /// <summary>
        /// Decode modify order command
        /// Format: order_id(32) + quantity(4) + limit_price(8) + stop_price(8)
        /// Total: 52 bytes
        /// </summary>
        public static ModifyCommand DecodeModifyCommand(byte[] data)
        {
            if (data == null || data.Length < 52)
                throw new ArgumentException("Invalid modify command data");

            var cmd = new ModifyCommand();
            int offset = 0;

            // Order ID (32 bytes)
            cmd.OrderId = Encoding.UTF8.GetString(data, offset, 32).TrimEnd('\0');
            offset += 32;

            // Quantity (4 bytes)
            cmd.Quantity = BitConverter.ToInt32(data, offset);
            offset += 4;

            // Limit Price (8 bytes)
            cmd.LimitPrice = BitConverter.ToDouble(data, offset);
            offset += 8;

            // Stop Price (8 bytes)
            cmd.StopPrice = BitConverter.ToDouble(data, offset);

            return cmd;
        }

        #endregion

        #region Encode Methods (To Python)

        /// <summary>
        /// Encode tick data message
        /// Format: msg_type(1) + timestamp(8) + price(8) + volume(8) + bid(8) + ask(8) + instrument(32)
        /// Total: 73 bytes
        /// </summary>
        public static byte[] EncodeTickData(string instrument, double timestamp, double price,
                                           long volume, double bid, double ask)
        {
            byte[] data = new byte[73];
            int offset = 0;

            // Message type
            data[offset] = MSG_TICK;
            offset += 1;

            // Timestamp
            Buffer.BlockCopy(BitConverter.GetBytes(timestamp), 0, data, offset, 8);
            offset += 8;

            // Price
            Buffer.BlockCopy(BitConverter.GetBytes(price), 0, data, offset, 8);
            offset += 8;

            // Volume
            Buffer.BlockCopy(BitConverter.GetBytes(volume), 0, data, offset, 8);
            offset += 8;

            // Bid
            Buffer.BlockCopy(BitConverter.GetBytes(bid), 0, data, offset, 8);
            offset += 8;

            // Ask
            Buffer.BlockCopy(BitConverter.GetBytes(ask), 0, data, offset, 8);
            offset += 8;

            // Instrument (32 bytes, null-padded)
            byte[] instrumentBytes = Encoding.UTF8.GetBytes(instrument);
            Buffer.BlockCopy(instrumentBytes, 0, data, offset, Math.Min(instrumentBytes.Length, 32));

            return data;
        }

        /// <summary>
        /// Encode order update message
        /// Format: msg_type(1) + order_id(32) + state(1) + filled(4) + remaining(4) + avg_price(8) + timestamp(8)
        /// Total: 58 bytes
        /// </summary>
        public static byte[] EncodeOrderUpdate(string orderId, byte state, int filled,
                                              int remaining, double avgPrice, double timestamp)
        {
            byte[] data = new byte[58];
            int offset = 0;

            // Message type
            data[offset] = MSG_ORDER_UPDATE;
            offset += 1;

            // Order ID (32 bytes, null-padded)
            byte[] orderIdBytes = Encoding.UTF8.GetBytes(orderId);
            Buffer.BlockCopy(orderIdBytes, 0, data, offset, Math.Min(orderIdBytes.Length, 32));
            offset += 32;

            // State
            data[offset] = state;
            offset += 1;

            // Filled
            Buffer.BlockCopy(BitConverter.GetBytes(filled), 0, data, offset, 4);
            offset += 4;

            // Remaining
            Buffer.BlockCopy(BitConverter.GetBytes(remaining), 0, data, offset, 4);
            offset += 4;

            // Average Price
            Buffer.BlockCopy(BitConverter.GetBytes(avgPrice), 0, data, offset, 8);
            offset += 8;

            // Timestamp
            Buffer.BlockCopy(BitConverter.GetBytes(timestamp), 0, data, offset, 8);

            return data;
        }

        /// <summary>
        /// Encode position update message
        /// Format: msg_type(1) + instrument(32) + position(1) + quantity(4) + avg_price(8) + unrealized_pnl(8)
        /// Total: 54 bytes
        /// </summary>
        public static byte[] EncodePositionUpdate(string instrument, byte position, int quantity,
                                                  double avgPrice, double unrealizedPnl)
        {
            byte[] data = new byte[54];
            int offset = 0;

            // Message type
            data[offset] = MSG_POSITION_UPDATE;
            offset += 1;

            // Instrument (32 bytes, null-padded)
            byte[] instrumentBytes = Encoding.UTF8.GetBytes(instrument);
            Buffer.BlockCopy(instrumentBytes, 0, data, offset, Math.Min(instrumentBytes.Length, 32));
            offset += 32;

            // Position type
            data[offset] = position;
            offset += 1;

            // Quantity
            Buffer.BlockCopy(BitConverter.GetBytes(quantity), 0, data, offset, 4);
            offset += 4;

            // Average Price
            Buffer.BlockCopy(BitConverter.GetBytes(avgPrice), 0, data, offset, 8);
            offset += 8;

            // Unrealized P&L
            Buffer.BlockCopy(BitConverter.GetBytes(unrealizedPnl), 0, data, offset, 8);

            return data;
        }

        /// <summary>
        /// Encode account update message
        /// Format: msg_type(1) + account_name(32) + timestamp(8) + cash_value(8) + buying_power(8) +
        ///         realized_pnl(8) + unrealized_pnl(8) + net_liquidation(8) + update_type(16)
        /// Total: 97 bytes
        /// </summary>
        public static byte[] EncodeAccountUpdate(string accountName, double timestamp,
                                                double cashValue, double buyingPower,
                                                double realizedPnl, double unrealizedPnl,
                                                double netLiquidation, string updateType)
        {
            byte[] data = new byte[97];
            int offset = 0;

            // Message type
            data[offset] = MSG_ACCOUNT_UPDATE;
            offset += 1;

            // Account Name (32 bytes, null-padded)
            byte[] accountBytes = Encoding.UTF8.GetBytes(accountName);
            Buffer.BlockCopy(accountBytes, 0, data, offset, Math.Min(accountBytes.Length, 32));
            offset += 32;

            // Timestamp
            Buffer.BlockCopy(BitConverter.GetBytes(timestamp), 0, data, offset, 8);
            offset += 8;

            // Cash Value
            Buffer.BlockCopy(BitConverter.GetBytes(cashValue), 0, data, offset, 8);
            offset += 8;

            // Buying Power
            Buffer.BlockCopy(BitConverter.GetBytes(buyingPower), 0, data, offset, 8);
            offset += 8;

            // Realized P&L
            Buffer.BlockCopy(BitConverter.GetBytes(realizedPnl), 0, data, offset, 8);
            offset += 8;

            // Unrealized P&L
            Buffer.BlockCopy(BitConverter.GetBytes(unrealizedPnl), 0, data, offset, 8);
            offset += 8;

            // Net Liquidation
            Buffer.BlockCopy(BitConverter.GetBytes(netLiquidation), 0, data, offset, 8);
            offset += 8;

            // Update Type (16 bytes, null-padded)
            byte[] updateTypeBytes = Encoding.UTF8.GetBytes(updateType);
            Buffer.BlockCopy(updateTypeBytes, 0, data, offset, Math.Min(updateTypeBytes.Length, 16));

            return data;
        }

        /// <summary>
        /// Encode instrument info message
        /// Format: msg_type(1) + instrument(32) + tick_size(8) + point_value(8) + min_move(8) + exchange(16)
        /// Total: 73 bytes
        /// </summary>
        public static byte[] EncodeInstrumentInfo(string instrument, double tickSize,
                                                 double pointValue, double minMove, string exchange)
        {
            byte[] data = new byte[73];
            int offset = 0;

            // Message type
            data[offset] = MSG_INSTRUMENT_INFO;
            offset += 1;

            // Instrument (32 bytes, null-padded)
            byte[] instrumentBytes = Encoding.UTF8.GetBytes(instrument);
            Buffer.BlockCopy(instrumentBytes, 0, data, offset, Math.Min(instrumentBytes.Length, 32));
            offset += 32;

            // Tick Size
            Buffer.BlockCopy(BitConverter.GetBytes(tickSize), 0, data, offset, 8);
            offset += 8;

            // Point Value
            Buffer.BlockCopy(BitConverter.GetBytes(pointValue), 0, data, offset, 8);
            offset += 8;

            // Min Move
            Buffer.BlockCopy(BitConverter.GetBytes(minMove), 0, data, offset, 8);
            offset += 8;

            // Exchange (16 bytes, null-padded)
            byte[] exchangeBytes = Encoding.UTF8.GetBytes(exchange);
            Buffer.BlockCopy(exchangeBytes, 0, data, offset, Math.Min(exchangeBytes.Length, 16));

            return data;
        }

        /// <summary>
        /// Encode error message
        /// Format: msg_type(1) + error_code(4) + message(128)
        /// Total: 133 bytes
        /// </summary>
        public static byte[] EncodeError(int errorCode, string message)
        {
            byte[] data = new byte[133];
            int offset = 0;

            // Message type
            data[offset] = MSG_ERROR;
            offset += 1;

            // Error Code
            Buffer.BlockCopy(BitConverter.GetBytes(errorCode), 0, data, offset, 4);
            offset += 4;

            // Message (128 bytes, null-padded)
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            Buffer.BlockCopy(messageBytes, 0, data, offset, Math.Min(messageBytes.Length, 128));

            return data;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Convert DateTime to Unix timestamp (seconds since epoch)
        /// </summary>
        public static double ToUnixTimestamp(DateTime dateTime)
        {
            return (dateTime.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        /// <summary>
        /// Get current Unix timestamp
        /// </summary>
        public static double GetCurrentTimestamp()
        {
            return ToUnixTimestamp(DateTime.UtcNow);
        }

        #endregion
    }

    #region Data Classes

    /// <summary>
    /// Represents a decoded order command from Python
    /// </summary>
    public class OrderCommand
    {
        public string Action { get; set; }         // "BUY" or "SELL"
        public string Instrument { get; set; }     // e.g., "ES 03-25"
        public int Quantity { get; set; }
        public string OrderType { get; set; }      // "MARKET", "LIMIT", "STOP_MARKET", "STOP_LIMIT"
        public string TimeInForce { get; set; }    // "DAY", "GTC", etc.
        public double LimitPrice { get; set; }
        public double StopPrice { get; set; }
        public string SignalName { get; set; }
    }

    /// <summary>
    /// Represents a decoded modify command from Python
    /// </summary>
    public class ModifyCommand
    {
        public string OrderId { get; set; }
        public int Quantity { get; set; }
        public double LimitPrice { get; set; }
        public double StopPrice { get; set; }
    }

    #endregion
}
