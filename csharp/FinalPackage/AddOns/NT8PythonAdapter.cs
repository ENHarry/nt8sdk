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

                // Read command file
                string command = File.ReadAllText(e.FullPath);
                
                // Process command
                string response = ProcessCommand(command);

                // Write response
                string responseFile = Path.Combine(outgoingDir, Path.GetFileName(e.FullPath));
                File.WriteAllText(responseFile, response);

                // Delete processed command file
                try { File.Delete(e.FullPath); } catch { }

                commandsProcessed++;
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
                string[] parts = command.Split('|');
                if (parts.Length == 0) return "ERROR|Invalid command";

                string cmd = parts[0].ToUpper();

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
                        if (parts.Length < 2) return "ERROR|Missing order ID";
                        return CancelOrder(parts[1]);

                    case "CLOSEPOSITION":
                        if (parts.Length < 3) return "ERROR|Missing parameters";
                        return ClosePosition(parts[1], parts[2]);

                    case "FLATTENEVERYTHING":
                        return FlattenEverything();

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
                // Format: PLACE|account|instrument|action|quantity|orderType|limitPrice|stopPrice|tif
                if (parts.Length < 6)
                    return "ERROR|Insufficient parameters";

                string instrument = parts[2];
                OrderAction action = parts[3].ToUpper() == "BUY" ? OrderAction.Buy : OrderAction.Sell;
                int quantity = int.Parse(parts[4]);
                string orderType = parts[5].ToUpper();

                // Get instrument
                Instrument instr = Instrument.GetInstrument(instrument);
                if (instr == null)
                    return $"ERROR|Invalid instrument: {instrument}";

                // Create order based on type
                Order order = null;
                if (orderType == "MARKET")
                {
                    order = tradingAccount.CreateOrder(instr, action, OrderType.Market, TimeInForce.Day, quantity, 0, 0, "", "PythonSDK", null);
                }
                else if (orderType == "LIMIT" && parts.Length > 6)
                {
                    double limitPrice = double.Parse(parts[6]);
                    order = tradingAccount.CreateOrder(instr, action, OrderType.Limit, TimeInForce.Day, quantity, limitPrice, 0, "", "PythonSDK", null);
                }

                if (order != null)
                {
                    tradingAccount.Submit(new[] { order });
                    Print($"Order submitted: {action} {quantity} {instrument}");
                    return $"OK|{order.Name}";
                }

                return "ERROR|Failed to create order";
            }
            catch (Exception ex)
            {
                return $"ERROR|{ex.Message}";
            }
        }

        private string CancelOrder(string orderName)
        {
            try
            {
                if (tradingAccount == null)
                    return "ERROR|No account";

                Order order = tradingAccount.Orders.FirstOrDefault(o => o.Name == orderName);
                if (order == null)
                    return $"ERROR|Order not found: {orderName}";

                tradingAccount.Cancel(new[] { order });
                Print($"Order cancelled: {orderName}");
                return "OK";
            }
            catch (Exception ex)
            {
                return $"ERROR|{ex.Message}";
            }
        }

        private string ClosePosition(string account, string instrument)
        {
            try
            {
                if (tradingAccount == null || tradingAccount.Name != account)
                    return $"ERROR|Account mismatch: {account}";

                Instrument instr = Instrument.GetInstrument(instrument);
                if (instr == null)
                    return $"ERROR|Invalid instrument: {instrument}";

                Position position = tradingAccount.Positions.FirstOrDefault(p => p.Instrument == instr);
                if (position == null || position.MarketPosition == MarketPosition.Flat)
                    return "OK|No position to close";

                // Create closing order
                OrderAction action = position.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.Buy;
                Order order = tradingAccount.CreateOrder(instr, action, OrderType.Market, TimeInForce.Day, position.Quantity, 0, 0, "", "PythonSDK", null);

                tradingAccount.Submit(new[] { order });
                Print($"Position closed: {instrument}");
                return $"OK|{order.Name}";
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
                if (tradingAccount == null)
                    return "ERROR|No account";

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
                        Order order = tradingAccount.CreateOrder(position.Instrument, action, OrderType.Market, TimeInForce.Day, position.Quantity, 0, 0, "", "PythonSDK", null);
                        tradingAccount.Submit(new[] { order });
                        closedPositions++;
                    }
                }

                Print($"Flattened: Cancelled {ordersToCancel.Length} orders, closed {closedPositions} positions");
                return $"OK|Cancelled: {ordersToCancel.Length}, Closed: {closedPositions}";
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
