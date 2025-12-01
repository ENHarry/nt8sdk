#region Using declarations
using NinjaTrader.Cbi;
using NinjaTrader.Core;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using System;
using System.IO;
using System.Linq;
using System.Text;
#endregion

namespace NinjaTrader.NinjaScript.AddOns
{
    /// <summary>
    /// Python SDK Adapter - File-Based Communication
    /// Fast event-driven communication via FileSystemWatcher
    /// </summary>
    public class NT8PythonAdapter : AddOnBase
    {
        #region Fields

        private FileSystemWatcher incomingWatcher;
        private string incomingDir;
        private string outgoingDir;
        
        private Account tradingAccount;
        private long commandsProcessed;
        private DateTime startTime;

        #endregion

        #region Initialization

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Python SDK Adapter - File-Based Communication";
                Name = "NT8PythonAdapter";
            }
            else if (State == State.Configure)
            {
                startTime = DateTime.Now;
                commandsProcessed = 0;

                // Setup directories
                string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string ntPath = Path.Combine(documentsPath, "NinjaTrader 8");
                incomingDir = Path.Combine(ntPath, "incoming");
                outgoingDir = Path.Combine(ntPath, "outgoing");

                // Create directories
                Directory.CreateDirectory(incomingDir);
                Directory.CreateDirectory(outgoingDir);

                // Initialize account
                InitializeAccount();

                // Start file monitoring
                StartFileMonitoring();

                Print($"NT8 Python Adapter started - File-Based");
                Print($"Incoming: {incomingDir}");
                Print($"Outgoing: {outgoingDir}");
            }
            else if (State == State.Terminated)
            {
                Cleanup();
            }
        }

        private void InitializeAccount()
        {
            try
            {
                tradingAccount = Account.All.FirstOrDefault(a => a.ConnectionStatus == ConnectionStatus.Connected);
                if (tradingAccount != null)
                {
                    Print($"Using account: {tradingAccount.Name}");
                }
                else
                {
                    Print("WARNING: No connected account found");
                }
            }
            catch (Exception ex)
            {
                Print($"Error initializing account: {ex.Message}");
            }
        }

        #endregion

        #region File Monitoring

        private void StartFileMonitoring()
        {
            try
            {
                incomingWatcher = new FileSystemWatcher(incomingDir)
                {
                    Filter = "*.txt",
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };

                incomingWatcher.Created += OnCommandFileCreated;
                incomingWatcher.Changed += OnCommandFileCreated;

                Print("File monitoring started successfully");
            }
            catch (Exception ex)
            {
                Print($"Error starting file monitor: {ex.Message}");
            }
        }

        private void OnCommandFileCreated(object sender, FileSystemEventArgs e)
        {
            try
            {
                // Small delay to ensure file is fully written
                System.Threading.Thread.Sleep(10);

                // Read command file - can contain multiple lines
                string[] lines = File.ReadAllLines(e.FullPath);
                
                // Process each command line
                StringBuilder responses = new StringBuilder();
                foreach (string line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        string response = ProcessCommand(line.Trim());
                        if (!string.IsNullOrEmpty(response))
                            responses.AppendLine(response);
                        commandsProcessed++;
                    }
                }

                // Write response if any
                if (responses.Length > 0)
                {
                    string responseFile = Path.Combine(outgoingDir, Path.GetFileName(e.FullPath));
                    File.WriteAllText(responseFile, responses.ToString());
                }

                // Delete processed command file
                try { File.Delete(e.FullPath); } catch { }
            }
            catch (Exception ex)
            {
                Print($"Error processing command file: {ex.Message}");
            }
        }

        #endregion

        #region Command Processing

        private string ProcessCommand(string command)
        {
            try
            {
                // ATI uses semicolon delimiter
                string[] parts = command.Split(';');
                if (parts.Length == 0) return "ERROR|Invalid command";

                string cmd = parts[0].ToUpper().Trim();

                switch (cmd)
                {
                    case "PING":
                        return "PONG";

                    case "STATUS":
                        TimeSpan runtime = DateTime.Now - startTime;
                        return $"OK|Running {runtime:hh\\:mm\\:ss}|Commands: {commandsProcessed}|Account: {tradingAccount?.Name ?? "None"}";

                    case "ACCOUNT_INFO":
                        if (tradingAccount == null)
                            return "ERROR|No account connected";
                        return $"OK|{tradingAccount.Name}|{tradingAccount.ConnectionStatus}|" +
                               $"{tradingAccount.Get(AccountItem.BuyingPower, Currency.UsDollar)}|" +
                               $"{tradingAccount.Get(AccountItem.RealizedProfitLoss, Currency.UsDollar)}";

                    case "GET_POSITIONS":
                        return GetPositions();

                    case "GET_ORDERS":
                        return GetOrders();

                    case "PLACE":
                        return PlaceOrder(parts);

                    case "CANCEL":
                        return CancelOrder(parts);

                    case "CHANGE":
                        return ChangeOrder(parts);

                    case "CLOSEPOSITION":
                        return ClosePosition(parts);

                    case "CLOSESTRATEGY":
                        if (parts.Length < 13) return "ERROR|Missing strategy ID";
                        return $"ERROR|ATM Strategies not implemented";

                    case "CANCELALLORDERS":
                        return CancelAllOrders();

                    case "FLATTENEVERYTHING":
                        return FlattenEverything();

                    case "REVERSEPOSITION":
                        return ReversePosition(parts);

                    default:
                        return $"ERROR|Unknown command: {cmd}";
                }
            }
            catch (Exception ex)
            {
                return $"ERROR|{ex.Message}";
            }
        }

        #endregion

        #region Trading Operations

        private string GetPositions()
        {
            try
            {
                if (tradingAccount == null)
                    return "ERROR|No account";

                StringBuilder sb = new StringBuilder("OK");
                foreach (Position position in tradingAccount.Positions)
                {
                    if (position.MarketPosition != MarketPosition.Flat)
                    {
                        sb.Append($"|{position.Instrument.FullName},{position.MarketPosition},{position.Quantity},{position.AveragePrice},{position.GetUnrealizedProfitLoss(PerformanceUnit.Currency)}");
                    }
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"ERROR|{ex.Message}";
            }
        }

        private string GetOrders()
        {
            try
            {
                if (tradingAccount == null)
                    return "ERROR|No account";

                StringBuilder sb = new StringBuilder("OK");
                foreach (Order order in tradingAccount.Orders)
                {
                    if (order.OrderState != OrderState.Filled && order.OrderState != OrderState.Cancelled)
                    {
                        sb.Append($"|{order.Name},{order.Instrument.FullName},{order.OrderAction},{order.OrderType},{order.Quantity},{order.OrderState}");
                    }
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"ERROR|{ex.Message}";
            }
        }

        private string PlaceOrder(string[] parts)
        {
            try
            {
                // ATI Format: PLACE;<ACCOUNT>;<INSTRUMENT>;<ACTION>;<QTY>;<ORDER TYPE>;[LIMIT PRICE];[STOP PRICE];<TIF>;[OCO ID];[ORDER ID];[STRATEGY];[STRATEGY ID]
                if (parts.Length < 9) return "ERROR|Insufficient parameters for PLACE";

                string account = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                string instrument = parts.Length > 2 ? parts[2].Trim() : string.Empty;
                string actionStr = parts.Length > 3 ? parts[3].Trim().ToUpper() : string.Empty;
                int quantity = parts.Length > 4 && !string.IsNullOrEmpty(parts[4]) ? int.Parse(parts[4].Trim()) : 0;
                string orderTypeStr = parts.Length > 5 ? parts[5].Trim().ToUpper() : string.Empty;
                double limitPrice = parts.Length > 6 && !string.IsNullOrEmpty(parts[6]) ? double.Parse(parts[6].Trim()) : 0;
                double stopPrice = parts.Length > 7 && !string.IsNullOrEmpty(parts[7]) ? double.Parse(parts[7].Trim()) : 0;
                string tif = parts.Length > 8 ? parts[8].Trim().ToUpper() : "DAY";
                string orderId = parts.Length > 10 ? parts[10].Trim() : string.Empty;

                // Validate required fields
                if (string.IsNullOrEmpty(instrument)) return "ERROR|Missing instrument";
                if (quantity <= 0) return "ERROR|Invalid quantity";

                // Get instrument
                Instrument instr = Instrument.GetInstrument(instrument);
                if (instr == null) return $"ERROR|Invalid instrument: {instrument}";

                // Determine action
                OrderAction action = actionStr == "BUY" || actionStr == "BUYTOCOVER" ? OrderAction.Buy : OrderAction.Sell;

                // Determine order type and submit
                Order order = null;
                if (orderTypeStr == "MARKET")
                {
                    order = tradingAccount.CreateOrder(instr, action, OrderType.Market, TimeInForce.Day, quantity, 0, 0, string.Empty, orderId, null);
                }
                else if (orderTypeStr == "LIMIT")
                {
                    if (limitPrice <= 0) return "ERROR|Limit price required for LIMIT orders";
                    order = tradingAccount.CreateOrder(instr, action, OrderType.Limit, TimeInForce.Day, quantity, limitPrice, 0, string.Empty, orderId, null);
                }
                else if (orderTypeStr == "STOP" || orderTypeStr == "STOPMARKET")
                {
                    if (stopPrice <= 0) return "ERROR|Stop price required for STOP orders";
                    order = tradingAccount.CreateOrder(instr, action, OrderType.StopMarket, TimeInForce.Day, quantity, 0, stopPrice, string.Empty, orderId, null);
                }
                else if (orderTypeStr == "STOPLIMIT")
                {
                    if (limitPrice <= 0 || stopPrice <= 0) return "ERROR|Limit and stop prices required for STOPLIMIT orders";
                    order = tradingAccount.CreateOrder(instr, action, OrderType.StopLimit, TimeInForce.Day, quantity, limitPrice, stopPrice, string.Empty, orderId, null);
                }
                else
                {
                    return $"ERROR|Unsupported order type: {orderTypeStr}";
                }

                if (order != null)
                {
                    tradingAccount.Submit(new[] { order });
                    Print($"Order submitted: {action} {quantity} {instrument} @ {orderTypeStr}");
                }

                return string.Empty; // ATI returns empty on success
            }
            catch (Exception ex)
            {
                return $"ERROR|{ex.Message}";
            }
        }

        private string CancelOrder(string[] parts)
        {
            try
            {
                // ATI Format: CANCEL;;;;;;;;;;<ORDER ID>;;[STRATEGY ID]
                if (parts.Length < 11) return "ERROR|Missing order ID";
                string orderId = parts.Length > 10 ? parts[10].Trim() : string.Empty;
                
                if (string.IsNullOrEmpty(orderId)) return "ERROR|Order ID required";
                if (tradingAccount == null) return "ERROR|No account";

                Order order = tradingAccount.Orders.FirstOrDefault(o => o.Name == orderId);
                if (order == null) return $"ERROR|Order not found: {orderId}";

                tradingAccount.Cancel(new[] { order });
                Print($"Order cancelled: {orderId}");
                return string.Empty; // ATI returns empty on success
            }
            catch (Exception ex)
            {
                return $"ERROR|{ex.Message}";
            }
        }

        private string ChangeOrder(string[] parts)
        {
            try
            {
                // ATI Format: CHANGE;;;;<QUANTITY>;;<LIMIT PRICE>;<STOP PRICE>;;;<ORDER ID>;;[STRATEGY ID]
                if (parts.Length < 11) return "ERROR|Missing parameters";
                
                int quantity = parts.Length > 4 && !string.IsNullOrEmpty(parts[4]) ? int.Parse(parts[4].Trim()) : 0;
                double limitPrice = parts.Length > 6 && !string.IsNullOrEmpty(parts[6]) ? double.Parse(parts[6].Trim()) : 0;
                double stopPrice = parts.Length > 7 && !string.IsNullOrEmpty(parts[7]) ? double.Parse(parts[7].Trim()) : 0;
                string orderId = parts.Length > 10 ? parts[10].Trim() : string.Empty;
                
                if (string.IsNullOrEmpty(orderId)) return "ERROR|Order ID required";
                if (tradingAccount == null) return "ERROR|No account";

                Order order = tradingAccount.Orders.FirstOrDefault(o => o.Name == orderId);
                if (order == null) return $"ERROR|Order not found: {orderId}";

                // Change order - Note: NT8 Change method signature varies, use simple approach
                tradingAccount.Cancel(new[] { order });
                
                // Recreate with new parameters
                int newQty = quantity > 0 ? quantity : order.Quantity;
                double newLimit = limitPrice > 0 ? limitPrice : order.LimitPrice;
                double newStop = stopPrice > 0 ? stopPrice : order.StopPrice;

                Order newOrder = tradingAccount.CreateOrder(order.Instrument, order.OrderAction, order.OrderType, TimeInForce.Day, newQty, newLimit, newStop, string.Empty, orderId, null);
                tradingAccount.Submit(new[] { newOrder });
                
                Print($"Order changed: {orderId}");
                return string.Empty;
            }
            catch (Exception ex)
            {
                return $"ERROR|{ex.Message}";
            }
        }

        private string CancelAllOrders()
        {
            try
            {
                if (tradingAccount == null) return "ERROR|No account";

                var ordersToCancel = tradingAccount.Orders.Where(o =>
                    o.OrderState != OrderState.Filled &&
                    o.OrderState != OrderState.Cancelled).ToArray();

                if (ordersToCancel.Length > 0)
                    tradingAccount.Cancel(ordersToCancel);

                Print($"Cancelled {ordersToCancel.Length} orders");
                return string.Empty;
            }
            catch (Exception ex)
            {
                return $"ERROR|{ex.Message}";
            }
        }

        private string ReversePosition(string[] parts)
        {
            try
            {
                // ATI Format: Same as PLACE command
                // First close current position, then open opposite
                if (parts.Length < 9) return "ERROR|Insufficient parameters for REVERSEPOSITION";

                string instrument = parts.Length > 2 ? parts[2].Trim() : string.Empty;
                
                if (string.IsNullOrEmpty(instrument)) return "ERROR|Missing instrument";

                Instrument instr = Instrument.GetInstrument(instrument);
                if (instr == null) return $"ERROR|Invalid instrument: {instrument}";

                Position position = tradingAccount.Positions.FirstOrDefault(p => p.Instrument == instr);
                
                // Close existing position first
                if (position != null && position.MarketPosition != MarketPosition.Flat)
                {
                    OrderAction closeAction = position.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.Buy;
                    Order closeOrder = tradingAccount.CreateOrder(instr, closeAction, OrderType.Market, TimeInForce.Day, position.Quantity, 0, 0, string.Empty, "REVERSE_CLOSE", null);
                    tradingAccount.Submit(new[] { closeOrder });
                }

                // Then place the new order (same as PLACE command)
                return PlaceOrder(parts);
            }
            catch (Exception ex)
            {
                return $"ERROR|{ex.Message}";
            }
        }

        private string ClosePosition(string[] parts)
        {
            try
            {
                // ATI Format: CLOSEPOSITION;<ACCOUNT>;<INSTRUMENT>;;;;;;;;;;
                if (parts.Length < 3) return "ERROR|Missing parameters";
                
                string account = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                string instrument = parts.Length > 2 ? parts[2].Trim() : string.Empty;

                if (tradingAccount == null || (account != string.Empty && tradingAccount.Name != account))
                    return $"ERROR|Account mismatch";

                if (string.IsNullOrEmpty(instrument)) return "ERROR|Missing instrument";

                Instrument instr = Instrument.GetInstrument(instrument);
                if (instr == null) return $"ERROR|Invalid instrument: {instrument}";

                Position position = tradingAccount.Positions.FirstOrDefault(p => p.Instrument == instr);
                if (position == null || position.MarketPosition == MarketPosition.Flat)
                    return string.Empty; // ATI returns empty even if no position

                // Cancel any working orders for this instrument first
                var ordersToCancel = tradingAccount.Orders.Where(o =>
                    o.Instrument == instr &&
                    o.OrderState != OrderState.Filled &&
                    o.OrderState != OrderState.Cancelled).ToArray();

                if (ordersToCancel.Length > 0)
                    tradingAccount.Cancel(ordersToCancel);

                // Submit closing order
                OrderAction action = position.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.Buy;
                Order order = tradingAccount.CreateOrder(instr, action, OrderType.Market, TimeInForce.Day, position.Quantity, 0, 0, string.Empty, "CLOSE", null);
                tradingAccount.Submit(new[] { order });

                Print($"Position closed: {instrument}");
                return string.Empty;
            }
            catch (Exception ex)
            {
                return $"ERROR|{ex.Message}";
            }
        }

        private string FlattenEverything()
        {
            try
            {
                if (tradingAccount == null) return "ERROR|No account";

                // Cancel all orders
                var ordersToCancel = tradingAccount.Orders.Where(o =>
                    o.OrderState != OrderState.Filled &&
                    o.OrderState != OrderState.Cancelled).ToArray();

                if (ordersToCancel.Length > 0)
                    tradingAccount.Cancel(ordersToCancel);

                // Close all positions
                int closedPositions = 0;
                foreach (Position position in tradingAccount.Positions)
                {
                    if (position.MarketPosition != MarketPosition.Flat)
                    {
                        OrderAction action = position.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.Buy;
                        Order order = tradingAccount.CreateOrder(position.Instrument, action, OrderType.Market, TimeInForce.Day, position.Quantity, 0, 0, string.Empty, "FLATTEN", null);
                        tradingAccount.Submit(new[] { order });
                        closedPositions++;
                    }
                }

                Print($"Flattened: Cancelled {ordersToCancel.Length} orders, closed {closedPositions} positions");
                return string.Empty; // ATI returns empty on success
            }
            catch (Exception ex)
            {
                return $"ERROR|{ex.Message}";
            }
        }

        #endregion

        #region Cleanup

        private void Cleanup()
        {
            try
            {
                if (incomingWatcher != null)
                {
                    incomingWatcher.EnableRaisingEvents = false;
                    incomingWatcher.Dispose();
                    incomingWatcher = null;
                }
                Print("NT8 Python Adapter stopped");
            }
            catch (Exception ex)
            {
                Print($"Error during cleanup: {ex.Message}");
            }
        }

        #endregion
    }
}
