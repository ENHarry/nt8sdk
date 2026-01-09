#region Using declarations
using NinjaTrader.Cbi;
using NinjaTrader.Core;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
#endregion

namespace NinjaTrader.NinjaScript.AddOns
{
    internal class OrderSnapshot
    {
        public string ClientOrderId { get; set; }
        public string UserTag { get; set; }
        public string Instrument { get; set; }
        public string Action { get; set; }
        public string Type { get; set; }
        public int Quantity { get; set; }
        public string State { get; set; }
        public string NativeOrderId { get; set; }
        public int Filled { get; set; }
        public int Remaining { get; set; }
        public double AveragePrice { get; set; }
        public DateTime LastUpdate { get; set; }
    }

    /// <summary>
    /// Auto-Breakeven Configuration for position management
    /// </summary>
    public class AutoBreakevenConfig
    {
        public Instrument Instrument { get; set; }
        public double EntryPrice { get; set; }
        public double BE1Offset { get; set; }
        public double BE2Offset { get; set; }
        public double BE3Offset { get; set; }
        public double OffsetTrigger { get; set; }
        public bool IsLong { get; set; }
        public int CurrentLevel { get; set; } // 0=initial, 1=BE1, 2=BE2, 3=BE3
        public double InitialStop { get; set; }
        public DateTime LastUpdate { get; set; }
    }

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
    private string incomingBaseDir;
    private string outgoingBaseDir;
        
        private Account tradingAccount;
        private long commandsProcessed;
        private DateTime startTime;
        private System.Collections.Generic.Dictionary<string, AutoBreakevenConfig> activeBreakevens;
        private System.Threading.Timer breakevenTimer;

        private readonly Dictionary<string, string> nativeOrderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly object nativeOrderLock = new object();
        private readonly Dictionary<string, OrderSnapshot> orderSnapshots = new Dictionary<string, OrderSnapshot>(StringComparer.OrdinalIgnoreCase);
        private readonly object orderSnapshotLock = new object();
        private readonly Dictionary<Order, string> orderToClientIds = new Dictionary<Order, string>();
        private readonly object orderMapLock = new object();
        private readonly Dictionary<string, string> orderIdToUserTags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> userTagToOrderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly object orderAliasLock = new object();
        private static int orderIdSequence;
        private double cachedBuyingPower = double.NaN;
        private double cachedCashValue = double.NaN;
        private double cachedRealizedPnl = double.NaN;

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
                incomingBaseDir = Path.Combine(ntPath, "incoming");
                outgoingBaseDir = Path.Combine(ntPath, "outgoing");
                incomingDir = Path.Combine(incomingBaseDir, "python");
                outgoingDir = Path.Combine(outgoingBaseDir, "python");

                // Ensure NinjaTrader and private adapter directories exist so FileSystemWatcher starts cleanly
                Directory.CreateDirectory(incomingBaseDir);
                Directory.CreateDirectory(outgoingBaseDir);
                Directory.CreateDirectory(incomingDir);
                Directory.CreateDirectory(outgoingDir);

                // Initialize account
                InitializeAccount();

                // Initialize auto-breakeven tracking
                activeBreakevens = new System.Collections.Generic.Dictionary<string, AutoBreakevenConfig>();

                // Start file monitoring
                StartFileMonitoring();

                // Set up breakeven monitoring timer (check every 100ms)
                breakevenTimer = new System.Threading.Timer(MonitorBreakevens, null, 100, 100);

                Print($"NT8 Python Adapter started - File-Based with Auto-Breakeven monitoring");
                Print($"Incoming base: {incomingBaseDir}");
                Print($"Outgoing base: {outgoingBaseDir}");
                Print($"Adapter incoming: {incomingDir}");
                Print($"Adapter outgoing: {outgoingDir}");
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
                    AttachAccountEvents(tradingAccount);
                    RefreshAccountSnapshot(tradingAccount);
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

        private void AttachAccountEvents(Account account)
        {
            if (account == null)
                return;

            account.OrderUpdate += OnOrderUpdate;
            account.AccountItemUpdate += OnAccountItemUpdate;
        }

        private void DetachAccountEvents(Account account)
        {
            if (account == null)
                return;

            account.OrderUpdate -= OnOrderUpdate;
            account.AccountItemUpdate -= OnAccountItemUpdate;
        }

        private void RefreshAccountSnapshot(Account account)
        {
            if (account == null)
                return;

            cachedBuyingPower = account.Get(AccountItem.BuyingPower, Currency.UsDollar);
            cachedCashValue = account.Get(AccountItem.CashValue, Currency.UsDollar);
            cachedRealizedPnl = account.Get(AccountItem.RealizedProfitLoss, Currency.UsDollar);
        }

        private double GetAccountMetric(ref double cacheField, params AccountItem[] priorities)
        {
            if (tradingAccount == null || priorities == null || priorities.Length == 0)
                return 0;

            foreach (AccountItem item in priorities)
            {
                double value = SafeAccountGet(item);
                if (IsMeaningfulMetric(value))
                {
                    cacheField = value;
                    return value;
                }
            }

            return double.IsNaN(cacheField) ? 0 : cacheField;
        }

        private double SafeAccountGet(AccountItem item)
        {
            try
            {
                return tradingAccount?.Get(item, Currency.UsDollar) ?? double.NaN;
            }
            catch
            {
                return double.NaN;
            }
        }

        private static bool IsMeaningfulMetric(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return false;

            if (Math.Abs(value) < double.Epsilon)
                return false;

            return true;
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
                        double buyingPowerMetric = GetAccountMetric(ref cachedBuyingPower,
                            AccountItem.BuyingPower,
                            AccountItem.ExcessIntradayMargin,
                            AccountItem.NetLiquidation);

                        double realizedMetric = GetAccountMetric(ref cachedRealizedPnl,
                            AccountItem.RealizedProfitLoss,
                            AccountItem.GrossRealizedProfitLoss);

                        double cashMetric = GetAccountMetric(ref cachedCashValue,
                            AccountItem.CashValue,
                            AccountItem.NetLiquidation,
                            AccountItem.ExcessIntradayMargin);

                        return $"OK|{tradingAccount.Name}|{tradingAccount.ConnectionStatus}|" +
                               $"{buyingPowerMetric}|" +
                               $"{realizedMetric}|" +
                               $"{cashMetric}";

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

                    case "GET_ACCOUNTS":
                        return GetAccounts();

                    case "SET_ACCOUNT":
                        return SetAccount(parts);

                    case "SUBSCRIBE_MARKET_DATA":
                        return SubscribeMarketData(parts);

                    case "UNSUBSCRIBE_MARKET_DATA":
                        return UnsubscribeMarketData(parts);

                    case "GET_MARKET_DATA":
                        return GetMarketData(parts);

                    case "AUTO_BREAKEVEN":
                        return SetAutoBreakeven(parts);

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
                List<string> staleKeys;

                lock (orderSnapshotLock)
                {
                    foreach (Order order in tradingAccount.Orders)
                    {
                        UpdateOrderSnapshot(order);
                    }

                    DateTime cutoff = Core.Globals.Now.AddMinutes(-30);
                    staleKeys = orderSnapshots
                        .Where(kvp => kvp.Value.LastUpdate < cutoff)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (string key in staleKeys)
                    {
                        orderSnapshots.Remove(key);
                    }

                    foreach (OrderSnapshot snapshot in orderSnapshots.Values.OrderByDescending(s => s.LastUpdate))
                    {
                        string avgPrice = snapshot.AveragePrice.ToString("G", CultureInfo.InvariantCulture);
                        string userTag = snapshot.UserTag ?? string.Empty;
                        sb.Append($"|{snapshot.ClientOrderId},{snapshot.Instrument},{snapshot.Action},{snapshot.Type},{snapshot.Quantity},{snapshot.State},{snapshot.NativeOrderId},{snapshot.Filled},{snapshot.Remaining},{avgPrice},{userTag}");
                    }
                }

                if (staleKeys.Count > 0)
                {
                    var staleSet = new HashSet<string>(staleKeys, StringComparer.OrdinalIgnoreCase);

                    lock (nativeOrderLock)
                    {
                        foreach (string key in staleKeys)
                        {
                            nativeOrderIds.Remove(key);
                        }
                    }

                    lock (orderMapLock)
                    {
                        var ordersToRemove = orderToClientIds
                            .Where(kvp => staleSet.Contains(kvp.Value))
                            .Select(kvp => kvp.Key)
                            .ToList();

                        foreach (Order order in ordersToRemove)
                        {
                            orderToClientIds.Remove(order);
                        }
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
                // OCO ID at position 9 - links orders together so when one fills/cancels, the others are cancelled
                string ocoId = parts.Length > 9 ? parts[9].Trim() : string.Empty;
                string requestedOrderId = parts.Length > 10 ? parts[10].Trim() : string.Empty;
                string strategyId = parts.Length > 12 ? parts[12].Trim() : string.Empty;
                string userTag = !string.IsNullOrEmpty(requestedOrderId) ? requestedOrderId : strategyId;

                if (tradingAccount == null)
                    return "ERROR|No account connected";

                // Validate required fields
                if (string.IsNullOrEmpty(instrument)) return "ERROR|Missing instrument";
                if (quantity <= 0) return "ERROR|Invalid quantity";

                // Get instrument
                Instrument instr = Instrument.GetInstrument(instrument);
                if (instr == null) return $"ERROR|Invalid instrument: {instrument}";

                string adapterOrderId = GenerateDeterministicOrderId(instr.FullName ?? instrument);

                // Determine action
                OrderAction action = actionStr == "BUY" || actionStr == "BUYTOCOVER" ? OrderAction.Buy : OrderAction.Sell;

                string normalizedOrderType = orderTypeStr.Replace("_", string.Empty);

                // Log OCO ID if provided
                if (!string.IsNullOrEmpty(ocoId))
                {
                    Print($"OCO order: {adapterOrderId} linked to OCO group: {ocoId}");
                }

                // Determine order type and submit
                // Pass ocoId to CreateOrder (8th parameter) to link orders in OCO group
                Order order = null;
                if (normalizedOrderType == "MARKET")
                {
                    order = tradingAccount.CreateOrder(instr, action, OrderType.Market, TimeInForce.Day, quantity, 0, 0, ocoId, adapterOrderId, null);
                }
                else if (normalizedOrderType == "LIMIT")
                {
                    if (limitPrice <= 0) return "ERROR|Limit price required for LIMIT orders";
                    order = tradingAccount.CreateOrder(instr, action, OrderType.Limit, TimeInForce.Day, quantity, limitPrice, 0, ocoId, adapterOrderId, null);
                }
                else if (normalizedOrderType == "STOP" || normalizedOrderType == "STOPMARKET")
                {
                    if (stopPrice <= 0) return "ERROR|Stop price required for STOP orders";
                    order = tradingAccount.CreateOrder(instr, action, OrderType.StopMarket, TimeInForce.Day, quantity, 0, stopPrice, ocoId, adapterOrderId, null);
                }
                else if (normalizedOrderType == "STOPLIMIT")
                {
                    if (limitPrice <= 0 || stopPrice <= 0) return "ERROR|Limit and stop prices required for STOPLIMIT orders";
                    order = tradingAccount.CreateOrder(instr, action, OrderType.StopLimit, TimeInForce.Day, quantity, limitPrice, stopPrice, ocoId, adapterOrderId, null);
                }
                else
                {
                    return $"ERROR|Unsupported order type: {orderTypeStr}";
                }

                if (order != null)
                {
                    tradingAccount.Submit(new[] { order });
                    AssociateOrderWithClientId(order, adapterOrderId);
                    if (!string.IsNullOrEmpty(userTag))
                        RegisterUserTag(adapterOrderId, userTag);

                    Print($"Order submitted: {action} {quantity} {instrument} @ {orderTypeStr} ({adapterOrderId}{(string.IsNullOrEmpty(userTag) ? string.Empty : $" | tag={userTag}")})");

                    string nativeId = WaitForNativeOrderId(order, adapterOrderId);
                    if (string.IsNullOrEmpty(nativeId))
                    {
                        Print($"WARNING: Native order ID still empty for {adapterOrderId} after timeout");
                    }

                    return $"OK|{adapterOrderId}|{nativeId}";
                }

                return "ERROR|Failed to create order";
            }
            catch (Exception ex)
            {
                return $"ERROR|{ex.Message}";
            }
        }

        private string WaitForNativeOrderId(Order order, string clientOrderId, int timeoutMilliseconds = 3000)
        {
            if (order == null)
                return string.Empty;

            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            string cacheKey = !string.IsNullOrEmpty(clientOrderId) ? clientOrderId : GetClientOrderKey(order);

            while (DateTime.UtcNow < deadline)
            {
                string nativeId = GetNativeIdSnapshot(order, cacheKey);
                if (!string.IsNullOrEmpty(nativeId))
                    return nativeId;

                Order matchingOrder = null;
                try
                {
                    matchingOrder = tradingAccount?.Orders?.FirstOrDefault(o => string.Equals(o.Name, cacheKey, StringComparison.OrdinalIgnoreCase));
                }
                catch
                {
                    matchingOrder = null;
                }

                if (matchingOrder != null)
                {
                    nativeId = ResolveNativeOrderId(matchingOrder, null);
                    if (!string.IsNullOrEmpty(nativeId))
                    {
                        lock (nativeOrderLock)
                        {
                            nativeOrderIds[cacheKey] = nativeId;
                        }
                        return nativeId;
                    }
                }

                System.Threading.Thread.Sleep(25);
            }

            return GetNativeIdSnapshot(order, cacheKey) ?? string.Empty;
        }

        private string GetNativeIdSnapshot(Order order, string cacheKey)
        {
            string directId = ResolveNativeOrderId(order, null);
            if (!string.IsNullOrEmpty(directId))
                return directId;

            if (string.IsNullOrEmpty(cacheKey))
                return string.Empty;

            lock (nativeOrderLock)
            {
                if (nativeOrderIds.TryGetValue(cacheKey, out string nativeId) && !string.IsNullOrEmpty(nativeId))
                {
                    return nativeId;
                }
            }

            return string.Empty;
        }

        private void AssociateOrderWithClientId(Order order, string clientOrderId)
        {
            if (order == null || string.IsNullOrEmpty(clientOrderId))
                return;

            lock (orderMapLock)
            {
                orderToClientIds[order] = clientOrderId;
            }
        }

        private void RemoveOrderAssociation(Order order)
        {
            if (order == null)
                return;

            string adapterOrderId = string.Empty;
            lock (orderMapLock)
            {
                if (orderToClientIds.TryGetValue(order, out adapterOrderId))
                {
                    orderToClientIds.Remove(order);
                }
            }

            if (!string.IsNullOrEmpty(adapterOrderId))
            {
                RemoveUserTagAssociation(adapterOrderId);
            }
        }

        private string GetClientOrderKey(Order order)
        {
            if (order == null)
                return string.Empty;

            lock (orderMapLock)
            {
                if (orderToClientIds.TryGetValue(order, out string clientId))
                    return clientId;
            }

            return order.Name ?? string.Empty;
        }

        private string GetUserTagForOrderId(string orderId)
        {
            if (string.IsNullOrEmpty(orderId))
                return string.Empty;

            lock (orderAliasLock)
            {
                if (orderIdToUserTags.TryGetValue(orderId, out string tag))
                    return tag;
            }

            return string.Empty;
        }

        private void RegisterUserTag(string orderId, string userTag)
        {
            if (string.IsNullOrEmpty(orderId) || string.IsNullOrEmpty(userTag))
                return;

            lock (orderAliasLock)
            {
                orderIdToUserTags[orderId] = userTag;
                userTagToOrderIds[userTag] = orderId;
            }
        }

        private void RemoveUserTagAssociation(string orderId)
        {
            if (string.IsNullOrEmpty(orderId))
                return;

            lock (orderAliasLock)
            {
                if (orderIdToUserTags.TryGetValue(orderId, out string tag))
                {
                    orderIdToUserTags.Remove(orderId);
                    if (!string.IsNullOrEmpty(tag) && userTagToOrderIds.TryGetValue(tag, out string mapped) && string.Equals(mapped, orderId, StringComparison.OrdinalIgnoreCase))
                    {
                        userTagToOrderIds.Remove(tag);
                    }
                }
            }
        }

        private string ResolveAdapterOrderId(string suppliedKey)
        {
            if (string.IsNullOrEmpty(suppliedKey))
                return string.Empty;

            lock (orderAliasLock)
            {
                if (orderIdToUserTags.ContainsKey(suppliedKey))
                    return suppliedKey;

                if (userTagToOrderIds.TryGetValue(suppliedKey, out string mappedId))
                    return mappedId;
            }

            return suppliedKey;
        }

        private static string NormalizeInstrumentName(string instrument)
        {
            if (string.IsNullOrWhiteSpace(instrument))
                return "ORDER";

            char[] buffer = instrument.Trim().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
            string normalized = new string(buffer);
            while (normalized.Contains("--"))
            {
                normalized = normalized.Replace("--", "-");
            }
            normalized = normalized.Trim('-');
            return string.IsNullOrEmpty(normalized) ? "ORDER" : normalized;
        }

        private string GenerateDeterministicOrderId(string instrument)
        {
            string name = NormalizeInstrumentName(instrument);
            DateTime timestamp = DateTime.UtcNow;
            int seq = Interlocked.Increment(ref orderIdSequence) % 10000;
            return $"{name}_{timestamp:yyyyMMdd-HHmmssfff}_{seq:D4}";
        }

        private void UpdateOrderSnapshot(Order order, OrderEventArgs eventArgs = null)
        {
            if (order == null)
                return;

            string clientKey = GetClientOrderKey(order);
            if (string.IsNullOrEmpty(clientKey))
                return;

            lock (orderSnapshotLock)
            {
                if (!orderSnapshots.TryGetValue(clientKey, out OrderSnapshot snapshot))
                {
                    snapshot = new OrderSnapshot
                    {
                        ClientOrderId = clientKey
                    };
                    orderSnapshots[clientKey] = snapshot;
                }

                snapshot.ClientOrderId = clientKey;
                snapshot.UserTag = GetUserTagForOrderId(clientKey);
                snapshot.Instrument = order.Instrument?.FullName ?? string.Empty;
                snapshot.Action = order.OrderAction.ToString();
                snapshot.Type = order.OrderType.ToString();
                snapshot.Quantity = order.Quantity;
                snapshot.State = order.OrderState.ToString();
                string resolvedId = ResolveNativeOrderId(order, eventArgs);
                if (!string.IsNullOrEmpty(resolvedId))
                    snapshot.NativeOrderId = resolvedId;
                snapshot.Filled = order.Filled;
                snapshot.Remaining = order.Quantity - order.Filled;
                snapshot.AveragePrice = order.AverageFillPrice;
                snapshot.LastUpdate = Core.Globals.Now;
            }
        }

        private string ResolveNativeOrderId(Order order, OrderEventArgs eventArgs)
        {
            if (order != null)
            {
                string nativeId = order.OrderId;
                if (!string.IsNullOrEmpty(nativeId))
                    return nativeId;

                nativeId = ReadStringProperty(order.GetType(), order, "Token");
                if (!string.IsNullOrEmpty(nativeId))
                    return nativeId;

                nativeId = ReadGuidProperty(order.GetType(), order, "Token");
                if (!string.IsNullOrEmpty(nativeId))
                    return nativeId;
            }

            if (eventArgs != null)
            {
                string nativeId = ReadStringProperty(eventArgs.GetType(), eventArgs, "OrderId");
                if (!string.IsNullOrEmpty(nativeId))
                    return nativeId;

                nativeId = ReadStringProperty(eventArgs.GetType(), eventArgs, "NativeOrderId");
                if (!string.IsNullOrEmpty(nativeId))
                    return nativeId;

                nativeId = ReadGuidProperty(eventArgs.GetType(), eventArgs, "OrderId");
                if (!string.IsNullOrEmpty(nativeId))
                    return nativeId;
            }

            return string.Empty;
        }

        private string ReadStringProperty(Type type, object instance, string memberName)
        {
            if (type == null || instance == null || string.IsNullOrEmpty(memberName))
                return string.Empty;

            try
            {
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
                PropertyInfo property = type.GetProperty(memberName, flags);
                if (property != null)
                {
                    object value = property.GetValue(instance);
                    if (value is string s && !string.IsNullOrEmpty(s))
                        return s;
                }

                FieldInfo field = type.GetField(memberName, flags);
                if (field != null)
                {
                    object value = field.GetValue(instance);
                    if (value is string s && !string.IsNullOrEmpty(s))
                        return s;
                }
            }
            catch
            {
                // ignored intentionally
            }

            return string.Empty;
        }

        private string ReadGuidProperty(Type type, object instance, string memberName)
        {
            if (type == null || instance == null || string.IsNullOrEmpty(memberName))
                return string.Empty;

            try
            {
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
                PropertyInfo property = type.GetProperty(memberName, flags);
                if (property != null)
                {
                    object value = property.GetValue(instance);
                    if (value is Guid guid && guid != Guid.Empty)
                        return guid.ToString("N");
                }

                FieldInfo field = type.GetField(memberName, flags);
                if (field != null)
                {
                    object value = field.GetValue(instance);
                    if (value is Guid guid && guid != Guid.Empty)
                        return guid.ToString("N");
                }
            }
            catch
            {
                // ignored intentionally
            }

            return string.Empty;
        }

        private string CancelOrder(string[] parts)
        {
            try
            {
                // ATI Format: CANCEL;;;;;;;;;;<ORDER ID>;;[STRATEGY ID]
                if (parts.Length < 11) return "ERROR|Missing order ID";
                string suppliedId = parts.Length > 10 ? parts[10].Trim() : string.Empty;
                string adapterOrderId = ResolveAdapterOrderId(suppliedId);
                
                if (string.IsNullOrEmpty(adapterOrderId)) return "ERROR|Order ID required";
                if (tradingAccount == null) return "ERROR|No account";

                Order order = tradingAccount.Orders.FirstOrDefault(o => string.Equals(GetClientOrderKey(o), adapterOrderId, StringComparison.OrdinalIgnoreCase));
                if (order == null) return $"ERROR|Order not found: {suppliedId}";

                tradingAccount.Cancel(new[] { order });
                Print($"Order cancelled: {adapterOrderId}");
                return $"OK|Order cancelled: {adapterOrderId}";
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
                string suppliedId = parts.Length > 10 ? parts[10].Trim() : string.Empty;
                string adapterOrderId = ResolveAdapterOrderId(suppliedId);

                if (string.IsNullOrEmpty(adapterOrderId)) return "ERROR|Order ID required";
                if (tradingAccount == null) return "ERROR|No account";

                Order order = tradingAccount.Orders.FirstOrDefault(o => string.Equals(GetClientOrderKey(o), adapterOrderId, StringComparison.OrdinalIgnoreCase));
                if (order == null) return $"ERROR|Order not found: {suppliedId}";

                // Check if order is in a modifiable state
                if (order.OrderState != OrderState.Working && order.OrderState != OrderState.Accepted && order.OrderState != OrderState.Submitted)
                {
                    return $"ERROR|Order not modifiable (state: {order.OrderState})";
                }

                // Calculate new values (use existing if not provided)
                int newQty = quantity > 0 ? quantity : order.Quantity;
                double newLimit = limitPrice > 0 ? limitPrice : order.LimitPrice;
                double newStop = stopPrice > 0 ? stopPrice : order.StopPrice;

                // Store OCO info to preserve relationship
                string originalOco = order.Oco ?? string.Empty;
                
                // Cancel existing order
                tradingAccount.Cancel(new[] { order });
                
                // Recreate with same OCO to preserve relationship
                // Using the same OCO ID links the new order with any other orders sharing that OCO
                Order newOrder = tradingAccount.CreateOrder(order.Instrument, order.OrderAction, order.OrderType, TimeInForce.Day, newQty, newLimit, newStop, originalOco, adapterOrderId, null);
                tradingAccount.Submit(new[] { newOrder });
                AssociateOrderWithClientId(newOrder, adapterOrderId);
                string existingTag = GetUserTagForOrderId(adapterOrderId);
                if (!string.IsNullOrEmpty(existingTag))
                    RegisterUserTag(adapterOrderId, existingTag);
                
                Print($"Order changed: {adapterOrderId} qty={newQty} limit={newLimit} stop={newStop} oco={originalOco}");
                return $"OK|Order changed: {adapterOrderId}";
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
                return $"OK|Cancelled {ordersToCancel.Length} orders";
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
                    return $"OK|No position for {instrument}";

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
                return $"OK|Position closed: {instrument}";
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
                return $"OK|Cancelled {ordersToCancel.Length}|Closed {closedPositions}";
            }
            catch (Exception ex)
            {
                return $"ERROR|{ex.Message}";
            }
        }

        private string GetAccounts()
        {
            try
            {
                StringBuilder sb = new StringBuilder("OK");
                
                // Get all available accounts
                foreach (Account account in Account.All)
                {
                    sb.Append($"|{account.Name}");
                }
                
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"ERROR|{ex.Message}";
            }
        }

        private string SetAccount(string[] parts)
        {
            try
            {
                if (parts.Length < 2) return "ERROR|Missing account name";
                
                string accountName = parts[1].Trim();
                if (string.IsNullOrEmpty(accountName)) return "ERROR|Invalid account name";
                
                Account newAccount = Account.All.FirstOrDefault(a => a.Name == accountName);
                if (newAccount == null) return $"ERROR|Account not found: {accountName}";
                
                if (tradingAccount != null)
                {
                    DetachAccountEvents(tradingAccount);
                }

                tradingAccount = newAccount;
                AttachAccountEvents(tradingAccount);
                RefreshAccountSnapshot(tradingAccount);
                Print($"Active account set to: {accountName}");
                
                return $"OK|Account set to {accountName}";
            }
            catch (Exception ex)
            {
                return $"ERROR|{ex.Message}";
            }
        }

        private string SubscribeMarketData(string[] parts)
        {
            try
            {
                if (parts.Length < 2) return "ERROR|Missing instrument";
                
                string instrumentName = parts[1].Trim();
                if (string.IsNullOrEmpty(instrumentName)) return "ERROR|Invalid instrument";
                
                Instrument instrument = Instrument.GetInstrument(instrumentName);
                if (instrument == null) return $"ERROR|Instrument not found: {instrumentName}";
                
                // Subscribe to market data (Level 1)
                if (instrument.MasterInstrument.Name != null)
                {
                    Print($"Subscribing to market data for {instrumentName}");
                    return $"OK|Subscribed to {instrumentName}";
                }
                
                return $"ERROR|Failed to subscribe to {instrumentName}";
            }
            catch (Exception ex)
            {
                return $"ERROR|{ex.Message}";
            }
        }

        private string UnsubscribeMarketData(string[] parts)
        {
            try
            {
                if (parts.Length < 2) return "ERROR|Missing instrument";
                
                string instrumentName = parts[1].Trim();
                Print($"Unsubscribing from market data for {instrumentName}");
                
                return $"OK|Unsubscribed from {instrumentName}";
            }
            catch (Exception ex)
            {
                return $"ERROR|{ex.Message}";
            }
        }

        private string GetMarketData(string[] parts)
        {
            try
            {
                if (parts.Length < 2) return "ERROR|Missing instrument";
                
                string instrumentName = parts[1].Trim();
                if (string.IsNullOrEmpty(instrumentName)) return "ERROR|Invalid instrument";
                
                Instrument instrument = Instrument.GetInstrument(instrumentName);
                if (instrument == null) return $"ERROR|Instrument not found: {instrumentName}";
                
                // For now, return placeholder values since we need proper market data subscription
                // This would require implementing OnMarketData events which is more complex
                return $"OK|0.0|0.0|0.0|{DateTime.Now:HH:mm:ss.fff}";
            }
            catch (Exception ex)
            {
                return $"ERROR|{ex.Message}";
            }
        }

        private string SetAutoBreakeven(string[] parts)
        {
            try
            {
                // AUTO_BREAKEVEN;<INSTRUMENT>;<BE1_OFFSET>;<BE2_OFFSET>;<BE3_OFFSET>;<POSITION_SIDE>;<OFFSET_TRIGGER>
                if (parts.Length < 7) return "ERROR|Missing breakeven parameters";
                
                string instrument = parts[1].Trim();
                double be1Offset = double.Parse(parts[2].Trim());
                double be2Offset = double.Parse(parts[3].Trim());
                double be3Offset = double.Parse(parts[4].Trim());
                string positionSide = parts[5].Trim().ToUpper();
                double offsetTrigger = parts.Length > 6 ? double.Parse(parts[6].Trim()) : 1.2; // Default offset trigger
                
                if (string.IsNullOrEmpty(instrument)) return "ERROR|Missing instrument";
                
                Instrument instr = Instrument.GetInstrument(instrument);
                if (instr == null) return $"ERROR|Invalid instrument: {instrument}";
                
                // Get current position
                Position position = tradingAccount.Positions.FirstOrDefault(p => p.Instrument == instr);
                if (position == null || position.MarketPosition == MarketPosition.Flat)
                    return "ERROR|No position found for breakeven";
                
                // Validate position matches expected side
                bool isLong = position.MarketPosition == MarketPosition.Long;
                if ((positionSide == "LONG" && !isLong) || (positionSide == "SHORT" && isLong))
                    return $"ERROR|Position side mismatch. Expected: {positionSide}, Actual: {position.MarketPosition}";
                
                double entryPrice = position.AveragePrice;
                double tickSize = instr.MasterInstrument.TickSize;
                
                // Use NT8's built-in Auto Breakeven functionality
                // This requires the Strategy.SetAutoBreakeven method which dynamically manages stops
                
                // Since we're in an AddOn, we need to simulate the auto-breakeven behavior
                // Store the breakeven configuration for this position
                var beConfig = new AutoBreakevenConfig
                {
                    Instrument = instr,
                    EntryPrice = entryPrice,
                    BE1Offset = be1Offset,
                    BE2Offset = be2Offset, 
                    BE3Offset = be3Offset,
                    OffsetTrigger = offsetTrigger,
                    IsLong = isLong,
                    CurrentLevel = 0, // No breakeven level triggered yet
                    InitialStop = isLong ? entryPrice - (10 * tickSize) : entryPrice + (10 * tickSize) // Initial stop 10 ticks away
                };
                
                // Store configuration (you'd need a dictionary to track multiple positions)
                // For now, we'll create the initial stop and set up monitoring
                
                // Calculate initial stop price (10 ticks from entry as per example)
                double initialStopPrice = beConfig.InitialStop;
                OrderAction stopAction = isLong ? OrderAction.Sell : OrderAction.Buy;
                
                // Cancel any existing auto-breakeven orders for this instrument
                var existingOrders = tradingAccount.Orders.Where(o => 
                    o.Instrument == instr && 
                    o.OrderState == OrderState.Working &&
                    (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit) &&
                    o.Name.Contains("AutoBE")).ToList();
                
                foreach (var order in existingOrders)
                {
                    tradingAccount.Cancel(new[] { order });
                }
                
                // Create initial stop order
                Order initialStopOrder = tradingAccount.CreateOrder(
                    instr,
                    stopAction,
                    OrderType.StopMarket,
                    TimeInForce.Gtc,
                    position.Quantity,
                    0, // Limit price
                    initialStopPrice, // Initial stop price
                    "", // OCO ID
                    $"AutoBE_Initial_{DateTime.Now:HHmmss}", // Order name
                    null // Custom order
                );
                
                if (initialStopOrder != null)
                {
                    tradingAccount.Submit(new[] { initialStopOrder });
                    
                    // Store the breakeven configuration for monitoring
                    string configKey = $"{instr.FullName}_{entryPrice}_{DateTime.Now:HHmmss}";
                    activeBreakevens[configKey] = beConfig;
                    
                    Print($"Auto-Breakeven initialized for {instrument}:");
                    Print($"  Entry: {entryPrice:F2}, Initial Stop: {initialStopPrice:F2}");
                    Print($"  BE1 Trigger: {(isLong ? entryPrice + (be1Offset + offsetTrigger) * tickSize : entryPrice - (be1Offset + offsetTrigger) * tickSize):F2}");
                    Print($"  BE2 Trigger: {(isLong ? entryPrice + (be2Offset + offsetTrigger) * tickSize : entryPrice - (be2Offset + offsetTrigger) * tickSize):F2}");
                    Print($"  BE3 Trigger: {(isLong ? entryPrice + (be3Offset + offsetTrigger) * tickSize : entryPrice - (be3Offset + offsetTrigger) * tickSize):F2}");
                    
                    return $"OK|Auto-Breakeven activated: Entry={entryPrice:F2}, Initial Stop={initialStopPrice:F2}, BE Levels={be1Offset}/{be2Offset}/{be3Offset} ticks, Trigger Offset={offsetTrigger} ticks";
                }
                else
                {
                    return "ERROR|Failed to create initial stop order";
                }
            }
            catch (Exception ex)
            {
                return $"ERROR|{ex.Message}";
            }
        }

        private void OnOrderUpdate(object sender, OrderEventArgs e)
        {
            try
            {
                Order order = e?.Order;
                if (order == null)
                    return;

                string nativeId = ResolveNativeOrderId(order, e);
                string key = GetClientOrderKey(order);
                if (string.IsNullOrEmpty(key))
                    return;

                if (!string.IsNullOrEmpty(nativeId))
                {
                    lock (nativeOrderLock)
                    {
                        nativeOrderIds[key] = nativeId;
                    }
                }

                UpdateOrderSnapshot(order, e);

                if (order.OrderState == OrderState.Filled ||
                    order.OrderState == OrderState.Cancelled ||
                    order.OrderState == OrderState.Rejected)
                {
                    RemoveOrderAssociation(order);
                }
            }
            catch (Exception ex)
            {
                Print($"Error tracking native order id: {ex.Message}");
            }
        }

        private void OnAccountItemUpdate(object sender, AccountItemEventArgs e)
        {
            try
            {
                if (e == null || e.Currency != Currency.UsDollar)
                    return;

                switch (e.AccountItem)
                {
                    case AccountItem.BuyingPower:
                        cachedBuyingPower = e.Value;
                        break;
                    case AccountItem.CashValue:
                        cachedCashValue = e.Value;
                        break;
                    case AccountItem.RealizedProfitLoss:
                        cachedRealizedPnl = e.Value;
                        break;
                }
            }
            catch (Exception ex)
            {
                Print($"Error tracking account metrics: {ex.Message}");
            }
        }

        #endregion

        #region Cleanup

        private void Cleanup()
        {
            try
            {
                if (breakevenTimer != null)
                {
                    breakevenTimer.Dispose();
                    breakevenTimer = null;
                }

                if (tradingAccount != null)
                {
                    DetachAccountEvents(tradingAccount);
                }

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

        #region Auto-Breakeven Monitoring

        private void MonitorBreakevens(object state)
        {
            try
            {
                if (activeBreakevens == null || activeBreakevens.Count == 0)
                    return;

                foreach (var kvp in activeBreakevens.ToList())
                {
                    var config = kvp.Value;
                    var currentPosition = tradingAccount?.Positions?.FirstOrDefault(p => p.Instrument == config.Instrument);
                    
                    if (currentPosition == null || currentPosition.MarketPosition == MarketPosition.Flat)
                    {
                        // Position closed, remove from monitoring
                        activeBreakevens.Remove(kvp.Key);
                        continue;
                    }

                    // Get current market price
                    double currentPrice = config.Instrument.MarketData?.Last?.Price ?? 0;
                    if (currentPrice == 0) continue;

                    double tickSize = config.Instrument.MasterInstrument.TickSize;
                    bool shouldUpdateStop = false;
                    double newStopPrice = 0;
                    int newLevel = config.CurrentLevel;

                    if (config.IsLong)
                    {
                        // Long position: check for breakeven triggers above entry
                        double be1Trigger = config.EntryPrice + (config.BE1Offset + config.OffsetTrigger) * tickSize;
                        double be2Trigger = config.EntryPrice + (config.BE2Offset + config.OffsetTrigger) * tickSize;
                        double be3Trigger = config.EntryPrice + (config.BE3Offset + config.OffsetTrigger) * tickSize;

                        if (currentPrice >= be3Trigger && config.CurrentLevel < 3)
                        {
                            newStopPrice = config.EntryPrice + config.BE3Offset * tickSize;
                            newLevel = 3;
                            shouldUpdateStop = true;
                        }
                        else if (currentPrice >= be2Trigger && config.CurrentLevel < 2)
                        {
                            newStopPrice = config.EntryPrice + config.BE2Offset * tickSize;
                            newLevel = 2;
                            shouldUpdateStop = true;
                        }
                        else if (currentPrice >= be1Trigger && config.CurrentLevel < 1)
                        {
                            newStopPrice = config.EntryPrice + config.BE1Offset * tickSize;
                            newLevel = 1;
                            shouldUpdateStop = true;
                        }
                    }
                    else
                    {
                        // Short position: check for breakeven triggers below entry
                        double be1Trigger = config.EntryPrice - (config.BE1Offset + config.OffsetTrigger) * tickSize;
                        double be2Trigger = config.EntryPrice - (config.BE2Offset + config.OffsetTrigger) * tickSize;
                        double be3Trigger = config.EntryPrice - (config.BE3Offset + config.OffsetTrigger) * tickSize;

                        if (currentPrice <= be3Trigger && config.CurrentLevel < 3)
                        {
                            newStopPrice = config.EntryPrice - config.BE3Offset * tickSize;
                            newLevel = 3;
                            shouldUpdateStop = true;
                        }
                        else if (currentPrice <= be2Trigger && config.CurrentLevel < 2)
                        {
                            newStopPrice = config.EntryPrice - config.BE2Offset * tickSize;
                            newLevel = 2;
                            shouldUpdateStop = true;
                        }
                        else if (currentPrice <= be1Trigger && config.CurrentLevel < 1)
                        {
                            newStopPrice = config.EntryPrice - config.BE1Offset * tickSize;
                            newLevel = 1;
                            shouldUpdateStop = true;
                        }
                    }

                    if (shouldUpdateStop)
                    {
                        // Cancel existing stop orders for this instrument
                        var existingStops = tradingAccount.Orders.Where(o => 
                            o.Instrument == config.Instrument && 
                            o.OrderState == OrderState.Working &&
                            (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit) &&
                            o.Name.Contains("AutoBE")).ToList();

                        foreach (var stopOrder in existingStops)
                        {
                            tradingAccount.Cancel(new[] { stopOrder });
                        }

                        // Create new stop order at breakeven level
                        OrderAction stopAction = config.IsLong ? OrderAction.Sell : OrderAction.Buy;
                        
                        Order newStopOrder = tradingAccount.CreateOrder(
                            config.Instrument,
                            stopAction,
                            OrderType.StopMarket,
                            TimeInForce.Gtc,
                            currentPosition.Quantity,
                            0, // Limit price
                            Math.Round(newStopPrice / tickSize) * tickSize, // Rounded stop price
                            "", // OCO ID
                            $"AutoBE_L{newLevel}_{DateTime.Now:HHmmss}", // Order name
                            null // Custom order
                        );

                        if (newStopOrder != null)
                        {
                            tradingAccount.Submit(new[] { newStopOrder });
                            config.CurrentLevel = newLevel;
                            config.LastUpdate = DateTime.Now;
                            
                            Print($"Auto-Breakeven Level {newLevel} activated for {config.Instrument.FullName}: Stop moved to {newStopPrice:F2} (Current: {currentPrice:F2})");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Print($"Error in breakeven monitoring: {ex.Message}");
            }
        }



        #endregion
    }
}
