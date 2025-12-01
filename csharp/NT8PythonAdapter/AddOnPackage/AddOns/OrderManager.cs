using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;

namespace NinjaTrader.NinjaScript.AddOns
{
    /// <summary>
    /// Manages order execution and tracking for the Python SDK
    /// </summary>
    public class OrderManager
    {
        private readonly Account account;
        private readonly Action<byte[]> sendMessageCallback;
        private readonly Action<string> logCallback;

        // Order tracking
        private readonly ConcurrentDictionary<string, Order> pythonIdToNT8Order;
        private readonly ConcurrentDictionary<Order, string> nt8OrderToPythonId;
        private readonly ConcurrentDictionary<string, PositionInfo> positionsByInstrument;

        // Lock for order submission
        private readonly object orderLock = new object();

        public OrderManager(Account account, Action<byte[]> sendMessageCallback, Action<string> logCallback)
        {
            this.account = account ?? throw new ArgumentNullException(nameof(account));
            this.sendMessageCallback = sendMessageCallback ?? throw new ArgumentNullException(nameof(sendMessageCallback));
            this.logCallback = logCallback ?? throw new ArgumentNullException(nameof(logCallback));

            pythonIdToNT8Order = new ConcurrentDictionary<string, Order>();
            nt8OrderToPythonId = new ConcurrentDictionary<Order, string>();
            positionsByInstrument = new ConcurrentDictionary<string, PositionInfo>();

            // Subscribe to account events
            account.OrderUpdate += OnOrderUpdate;
            account.ExecutionUpdate += OnExecutionUpdate;
        }

        #region Order Placement

        /// <summary>
        /// Place an order based on command from Python
        /// </summary>
        public bool PlaceOrder(OrderCommand cmd, string pythonOrderId)
        {
            try
            {
                logCallback?.Invoke($"Placing {cmd.OrderType} {cmd.Action} order: {cmd.Instrument} x{cmd.Quantity}");

                // Get instrument
                Instrument instrument = GetInstrument(cmd.Instrument);
                if (instrument == null)
                {
                    SendError(1001, $"Instrument not found: {cmd.Instrument}");
                    return false;
                }

                // Validate account
                if (account == null || account.ConnectionStatus != ConnectionStatus.Connected)
                {
                    SendError(1002, "Account not connected");
                    return false;
                }

                // Create order based on type
                Order order = null;

                lock (orderLock)
                {
                    switch (cmd.OrderType)
                    {
                        case "MARKET":
                            order = account.CreateOrder(
                                instrument,
                                cmd.Action == "BUY" ? OrderAction.Buy : OrderAction.Sell,
                                OrderType.Market,
                                TimeInForce.Day,
                                cmd.Quantity,
                                0,  // limit price
                                0,  // stop price
                                string.Empty,  // OCO ID
                                cmd.SignalName,
                                null  // from entry signal
                            );
                            break;

                        case "LIMIT":
                            order = account.CreateOrder(
                                instrument,
                                cmd.Action == "BUY" ? OrderAction.Buy : OrderAction.Sell,
                                OrderType.Limit,
                                TimeInForce.Day,
                                cmd.Quantity,
                                cmd.LimitPrice,
                                0,
                                string.Empty,
                                cmd.SignalName,
                                null
                            );
                            break;

                        case "STOP_MARKET":
                            order = account.CreateOrder(
                                instrument,
                                cmd.Action == "BUY" ? OrderAction.Buy : OrderAction.Sell,
                                OrderType.StopMarket,
                                TimeInForce.Day,
                                cmd.Quantity,
                                0,
                                cmd.StopPrice,
                                string.Empty,
                                cmd.SignalName,
                                null
                            );
                            break;

                        case "STOP_LIMIT":
                            order = account.CreateOrder(
                                instrument,
                                cmd.Action == "BUY" ? OrderAction.Buy : OrderAction.Sell,
                                OrderType.StopLimit,
                                TimeInForce.Day,
                                cmd.Quantity,
                                cmd.LimitPrice,
                                cmd.StopPrice,
                                string.Empty,
                                cmd.SignalName,
                                null
                            );
                            break;

                        default:
                            SendError(1003, $"Unsupported order type: {cmd.OrderType}");
                            return false;
                    }

                    if (order == null)
                    {
                        SendError(1004, "Failed to create order");
                        return false;
                    }

                    // Map Python ID to NT8 order
                    pythonIdToNT8Order[pythonOrderId] = order;
                    nt8OrderToPythonId[order] = pythonOrderId;

                    // Submit order
                    account.Submit(new[] { order });

                    logCallback?.Invoke($"Order submitted: {pythonOrderId} -> NT8 Order: {order.Name}");
                }

                return true;
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"ERROR placing order: {ex.Message}");
                SendError(1000, $"Order placement failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Cancel an order
        /// </summary>
        public bool CancelOrder(string pythonOrderId)
        {
            try
            {
                if (!pythonIdToNT8Order.TryGetValue(pythonOrderId, out Order order))
                {
                    SendError(1005, $"Order not found: {pythonOrderId}");
                    return false;
                }

                if (order.OrderState != OrderState.Working &&
                    order.OrderState != OrderState.Accepted &&
                    order.OrderState != OrderState.Submitted)
                {
                    SendError(1006, $"Order cannot be cancelled in state: {order.OrderState}");
                    return false;
                }

                account.Cancel(new[] { order });
                logCallback?.Invoke($"Cancelled order: {pythonOrderId}");
                return true;
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"ERROR cancelling order: {ex.Message}");
                SendError(1007, $"Order cancellation failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Modify an order
        /// </summary>
        public bool ModifyOrder(ModifyCommand cmd)
        {
            try
            {
                if (!pythonIdToNT8Order.TryGetValue(cmd.OrderId, out Order order))
                {
                    SendError(1008, $"Order not found: {cmd.OrderId}");
                    return false;
                }

                if (order.OrderState != OrderState.Working && order.OrderState != OrderState.Accepted)
                {
                    SendError(1009, $"Order cannot be modified in state: {order.OrderState}");
                    return false;
                }

                // Modify the order - commented out as Change API varies by NT8 version
                // For now, cancel and replace
                logCallback?.Invoke($"Order modification requested but not fully implemented - would need cancel/replace");

                // TODO: Implement proper order modification based on NT8 version
                // account.ChangeOrder(order, newQuantity, newLimit, newStop);

                logCallback?.Invoke($"Modified order: {cmd.OrderId}");
                return true;
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"ERROR modifying order: {ex.Message}");
                SendError(1010, $"Order modification failed: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// Handle order state changes
        /// </summary>
        private void OnOrderUpdate(object sender, OrderEventArgs e)
        {
            try
            {
                Order order = e.Order;

                // Check if this is one of our tracked orders
                if (!nt8OrderToPythonId.TryGetValue(order, out string pythonOrderId))
                    return;

                // Convert NT8 order state to protocol state
                byte state = ConvertOrderState(order.OrderState);

                // Send order update to Python
                byte[] message = BinaryProtocolHelper.EncodeOrderUpdate(
                    pythonOrderId,
                    state,
                    order.Filled,
                    order.Quantity - order.Filled,
                    order.AverageFillPrice,
                    BinaryProtocolHelper.GetCurrentTimestamp()
                );

                sendMessageCallback(message);

                logCallback?.Invoke($"Order update: {pythonOrderId} -> {order.OrderState}");

                // Clean up filled or cancelled orders
                if (order.OrderState == OrderState.Filled ||
                    order.OrderState == OrderState.Cancelled ||
                    order.OrderState == OrderState.Rejected)
                {
                    pythonIdToNT8Order.TryRemove(pythonOrderId, out _);
                    nt8OrderToPythonId.TryRemove(order, out _);
                }
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"ERROR in OnOrderUpdate: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle order executions
        /// </summary>
        private void OnExecutionUpdate(object sender, ExecutionEventArgs e)
        {
            try
            {
                Execution execution = e.Execution;
                Order order = execution.Order;

                // Update position tracking
                UpdatePosition(execution);

                // Also trigger order update for execution
                if (nt8OrderToPythonId.ContainsKey(order))
                {
                    // Manually call our order update logic instead of creating OrderEventArgs
                    if (nt8OrderToPythonId.TryGetValue(order, out string pythonOrderId))
                    {
                        byte state = ConvertOrderState(order.OrderState);
                        byte[] message = BinaryProtocolHelper.EncodeOrderUpdate(
                            pythonOrderId,
                            state,
                            order.Filled,
                            order.Quantity - order.Filled,
                            order.AverageFillPrice,
                            BinaryProtocolHelper.GetCurrentTimestamp()
                        );
                        sendMessageCallback(message);
                    }
                }
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"ERROR in OnExecutionUpdate: {ex.Message}");
            }
        }

        #endregion

        #region Position Tracking

        /// <summary>
        /// Update position based on execution
        /// </summary>
        private void UpdatePosition(Execution execution)
        {
            try
            {
                string instrumentName = execution.Instrument.FullName;

                PositionInfo posInfo = positionsByInstrument.GetOrAdd(
                    instrumentName,
                    _ => new PositionInfo { Instrument = instrumentName }
                );

                // Update position
                if (execution.Order.OrderAction == OrderAction.Buy)
                {
                    posInfo.Quantity += execution.Quantity;
                }
                else
                {
                    posInfo.Quantity -= execution.Quantity;
                }

                // Calculate average price
                if (posInfo.Quantity != 0)
                {
                    // Simplified - should track cost basis properly
                    posInfo.AveragePrice = execution.Price;
                }
                else
                {
                    posInfo.AveragePrice = 0;
                }

                // Send position update to Python
                SendPositionUpdate(posInfo);
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"ERROR updating position: {ex.Message}");
            }
        }

        /// <summary>
        /// Send position update to Python
        /// </summary>
        public void SendPositionUpdate(PositionInfo posInfo)
        {
            try
            {
                byte positionType = BinaryProtocolHelper.POSITION_FLAT;
                if (posInfo.Quantity > 0)
                    positionType = BinaryProtocolHelper.POSITION_LONG;
                else if (posInfo.Quantity < 0)
                    positionType = BinaryProtocolHelper.POSITION_SHORT;

                // Calculate unrealized P&L (simplified - needs current price)
                double unrealizedPnl = 0.0;  // Would need current market price to calculate

                byte[] message = BinaryProtocolHelper.EncodePositionUpdate(
                    posInfo.Instrument,
                    positionType,
                    Math.Abs(posInfo.Quantity),
                    posInfo.AveragePrice,
                    unrealizedPnl
                );

                sendMessageCallback(message);
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"ERROR sending position update: {ex.Message}");
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Get instrument by name
        /// </summary>
        private Instrument GetInstrument(string instrumentName)
        {
            try
            {
                return Instrument.GetInstrument(instrumentName);
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"ERROR getting instrument {instrumentName}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Convert NT8 OrderState to protocol state byte
        /// </summary>
        private byte ConvertOrderState(OrderState state)
        {
            return state switch
            {
                OrderState.Submitted => BinaryProtocolHelper.STATE_SUBMITTED,
                OrderState.Accepted => BinaryProtocolHelper.STATE_ACCEPTED,
                OrderState.Working => BinaryProtocolHelper.STATE_WORKING,
                OrderState.Filled => BinaryProtocolHelper.STATE_FILLED,
                OrderState.PartFilled => BinaryProtocolHelper.STATE_PART_FILLED,
                OrderState.Cancelled => BinaryProtocolHelper.STATE_CANCELLED,
                OrderState.Rejected => BinaryProtocolHelper.STATE_REJECTED,
                _ => BinaryProtocolHelper.STATE_SUBMITTED
            };
        }

        /// <summary>
        /// Send error message to Python
        /// </summary>
        private void SendError(int errorCode, string message)
        {
            try
            {
                byte[] errorMessage = BinaryProtocolHelper.EncodeError(errorCode, message);
                sendMessageCallback(errorMessage);
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"ERROR sending error message: {ex.Message}");
            }
        }

        /// <summary>
        /// Get all active orders
        /// </summary>
        public Dictionary<string, Order> GetActiveOrders()
        {
            return new Dictionary<string, Order>(pythonIdToNT8Order);
        }

        /// <summary>
        /// Get position for instrument
        /// </summary>
        public PositionInfo GetPosition(string instrumentName)
        {
            positionsByInstrument.TryGetValue(instrumentName, out PositionInfo posInfo);
            return posInfo ?? new PositionInfo { Instrument = instrumentName };
        }

        #endregion

        #region Cleanup

        /// <summary>
        /// Cleanup resources
        /// </summary>
        public void Dispose()
        {
            if (account != null)
            {
                account.OrderUpdate -= OnOrderUpdate;
                account.ExecutionUpdate -= OnExecutionUpdate;
            }

            pythonIdToNT8Order.Clear();
            nt8OrderToPythonId.Clear();
            positionsByInstrument.Clear();
        }

        #endregion
    }

    /// <summary>
    /// Position information tracking
    /// </summary>
    public class PositionInfo
    {
        public string Instrument { get; set; }
        public int Quantity { get; set; }
        public double AveragePrice { get; set; }
        public double UnrealizedPnl { get; set; }
        public double RealizedPnl { get; set; }
    }
}
