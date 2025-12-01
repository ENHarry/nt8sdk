using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using NinjaTrader.NinjaScript;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns
{
    /// <summary>
    /// Python SDK Adapter for NinjaTrader 8 - ENHANCED VERSION
    /// Provides full NT8 API integration with order execution, market data, and account tracking
    /// </summary>
    public class NT8PythonAdapter : AddOnBase
    {
        #region Fields

        // Named Pipe communication
        private NamedPipeServerStream pipeServer;
        private BinaryReader reader;
        private Thread readThread;
        private bool isRunning;
        private object pipeLock = new object();

        // Core managers
        private OrderManager orderManager;
        private MarketDataManager marketDataManager;
        private AccountDataManager accountDataManager;
        private MessageQueue messageQueue;

        // Account
        private Account tradingAccount;
        private string accountName = "Sim101";  // Default account

        // Statistics
        private long commandsProcessed;
        private DateTime startTime;

        #endregion

        #region Initialization

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Python SDK Adapter for NT8 - Full Integration";
                Name = "NT8PythonAdapter";

                startTime = DateTime.Now;
                commandsProcessed = 0;
            }
            else if (State == State.DataLoaded)
            {
                // Initialize when data is loaded (after SetDefaults)
                // Get trading account
                InitializeAccount();

                // Start pipe server
                StartPipeServer();
            }
            else if (State == State.Terminated)
            {
                Cleanup();
            }
        }

        /// <summary>
        /// Initialize trading account
        /// </summary>
        private void InitializeAccount()
        {
            try
            {
                // Try to get the first connected account
                tradingAccount = Account.All.FirstOrDefault(a =>
                    a.ConnectionStatus == ConnectionStatus.Connected &&
                    (a.Name == accountName || Account.All.Count == 1));

                if (tradingAccount == null)
                {
                    // Fall back to first available account
                    tradingAccount = Account.All.FirstOrDefault();
                }

                if (tradingAccount != null)
                {
                    Print($"Using account: {tradingAccount.Name} ({tradingAccount.ConnectionStatus})");
                }
                else
                {
                    Print("WARNING: No trading account available");
                }
            }
            catch (Exception ex)
            {
                Print($"ERROR initializing account: {ex.Message}");
            }
        }

        #endregion

        #region Named Pipe Server

        private void StartPipeServer()
        {
            try
            {
                pipeServer = new NamedPipeServerStream(
                    "NT8PythonSDK",
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous
                );

                Task.Run(() => WaitForConnection());
                Print("Python adapter waiting for connection on pipe: NT8PythonSDK");
            }
            catch (Exception ex)
            {
                Print($"ERROR starting pipe server: {ex.Message}");
            }
        }

        private async void WaitForConnection()
        {
            try
            {
                await pipeServer.WaitForConnectionAsync();

                lock (pipeLock)
                {
                    reader = new BinaryReader(pipeServer);
                }

                Print("Python client connected successfully!");

                // Initialize managers
                InitializeManagers();

                // Start reader thread
                isRunning = true;
                readThread = new Thread(ReadLoop);
                readThread.IsBackground = true;
                readThread.Start();

                // Send initial account update
                accountDataManager?.SendAccountUpdate("CONNECT");
            }
            catch (Exception ex)
            {
                Print($"ERROR during connection: {ex.Message}");
            }
        }

        /// <summary>
        /// Initialize all manager components
        /// </summary>
        private void InitializeManagers()
        {
            try
            {
                // Create message queue for outbound messages
                messageQueue = new MessageQueue(pipeServer, Print);
                messageQueue.Start();

                // Initialize order manager
                if (tradingAccount != null)
                {
                    orderManager = new OrderManager(tradingAccount, msg => messageQueue.Enqueue(msg), Print);
                    Print("Order manager initialized");
                }

                // Initialize market data manager
                marketDataManager = new MarketDataManager(msg => messageQueue.Enqueue(msg), Print);
                Print("Market data manager initialized");

                // Initialize account data manager
                if (tradingAccount != null)
                {
                    accountDataManager = new AccountDataManager(tradingAccount, msg => messageQueue.Enqueue(msg), Print, 1000);
                    Print("Account data manager initialized");
                }

                Print("All managers initialized successfully");
            }
            catch (Exception ex)
            {
                Print($"ERROR initializing managers: {ex.Message}");
            }
        }

        #endregion

        #region Message Reading

        private void ReadLoop()
        {
            byte[] buffer = new byte[4096];

            while (isRunning && pipeServer != null && pipeServer.IsConnected)
            {
                try
                {
                    int bytesRead = pipeServer.Read(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        ProcessCommand(buffer, bytesRead);
                        commandsProcessed++;
                    }
                }
                catch (IOException)
                {
                    Print("Python client disconnected");
                    break;
                }
                catch (Exception ex)
                {
                    Print($"ERROR reading from pipe: {ex.Message}");
                    break;
                }
            }

            // Attempt reconnection
            if (isRunning)
            {
                Print("Attempting to restart pipe server...");
                Thread.Sleep(1000);
                Cleanup();
                StartPipeServer();
            }
        }

        #endregion

        #region Command Processing

        private void ProcessCommand(byte[] data, int length)
        {
            try
            {
                // Check if this is a binary order command (starts with action byte 1 or 2)
                if (length >= 94 && (data[0] == BinaryProtocolHelper.ACTION_BUY || data[0] == BinaryProtocolHelper.ACTION_SELL))
                {
                    ProcessBinaryOrderCommand(data);
                    return;
                }

                // Check if this is a binary cancel/modify command
                if (length >= 32)
                {
                    // Try to parse as text command first
                    string command = Encoding.UTF8.GetString(data, 0, Math.Min(length, 100)).Trim();
                    string[] parts = command.Split('|');

                    if (parts.Length > 0)
                    {
                        ProcessTextCommand(parts, data, length);
                        return;
                    }
                }

                Print($"Unknown command format, length: {length}");
            }
            catch (Exception ex)
            {
                Print($"ERROR processing command: {ex.Message}");
                SendError(9999, $"Command processing error: {ex.Message}");
            }
        }

        /// <summary>
        /// Process binary order command from Python
        /// </summary>
        private void ProcessBinaryOrderCommand(byte[] data)
        {
            try
            {
                OrderCommand cmd = BinaryProtocolHelper.DecodeOrderCommand(data);

                if (orderManager == null)
                {
                    SendError(1100, "Order manager not initialized");
                    return;
                }

                // Generate order ID
                string pythonOrderId = Guid.NewGuid().ToString("N").Substring(0, 8);

                // Place order
                bool success = orderManager.PlaceOrder(cmd, pythonOrderId);

                if (success)
                {
                    Print($"Order placed: {cmd.OrderType} {cmd.Action} {cmd.Instrument} x{cmd.Quantity}");
                }
            }
            catch (Exception ex)
            {
                Print($"ERROR processing binary order command: {ex.Message}");
                SendError(1101, $"Order command failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Process text-based commands
        /// </summary>
        private void ProcessTextCommand(string[] parts, byte[] data, int length)
        {
            try
            {
                string commandType = parts[0].ToUpper();

                switch (commandType)
                {
                    case "SUBSCRIBE":
                        if (parts.Length >= 2 && marketDataManager != null)
                        {
                            marketDataManager.Subscribe(parts[1]);
                        }
                        break;

                    case "UNSUBSCRIBE":
                        if (parts.Length >= 2 && marketDataManager != null)
                        {
                            marketDataManager.Unsubscribe(parts[1]);
                        }
                        break;

                    case "CANCEL":
                        if (parts.Length >= 2 && orderManager != null)
                        {
                            string orderId = parts[1];
                            orderManager.CancelOrder(orderId);
                        }
                        else if (length >= 32)
                        {
                            // Binary cancel command
                            string orderId = BinaryProtocolHelper.DecodeCancelCommand(data);
                            orderManager?.CancelOrder(orderId);
                        }
                        break;

                    case "MODIFY":
                        if (length >= 52)
                        {
                            // Binary modify command
                            ModifyCommand modCmd = BinaryProtocolHelper.DecodeModifyCommand(data);
                            orderManager?.ModifyOrder(modCmd);
                        }
                        else if (parts.Length >= 2 && orderManager != null)
                        {
                            // Text-based modify (simplified)
                            Print($"Text-based modify not fully supported, use binary protocol");
                        }
                        break;

                    case "REQUEST_ACCOUNT":
                        accountDataManager?.HandleUpdateRequest();
                        break;

                    case "INSTRUMENT_INFO":
                        if (parts.Length >= 2 && marketDataManager != null)
                        {
                            marketDataManager.SendInstrumentInfoByName(parts[1]);
                        }
                        break;

                    case "STATUS":
                        SendStatusUpdate();
                        break;

                    default:
                        Print($"Unknown command: {commandType}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Print($"ERROR processing text command: {ex.Message}");
                SendError(1200, $"Text command failed: {ex.Message}");
            }
        }

        #endregion

        #region Status and Errors

        /// <summary>
        /// Send status update
        /// </summary>
        private void SendStatusUpdate()
        {
            try
            {
                var stats = messageQueue?.GetStats();
                string status = $"NT8PythonAdapter Status:\n" +
                              $"  Uptime: {(DateTime.Now - startTime).TotalMinutes:F1} min\n" +
                              $"  Commands Processed: {commandsProcessed}\n" +
                              $"  Queue: {stats?.ToString() ?? "N/A"}\n" +
                              $"  Account: {accountDataManager?.GetAccountSummary() ?? "N/A"}\n" +
                              $"  Subscriptions: {marketDataManager?.GetSubscribedInstruments()?.Count ?? 0}";

                Print(status);
            }
            catch (Exception ex)
            {
                Print($"ERROR sending status: {ex.Message}");
            }
        }

        /// <summary>
        /// Send error message to Python
        /// </summary>
        private void SendError(int errorCode, string message)
        {
            try
            {
                byte[] errorMessage = BinaryProtocolHelper.EncodeError(errorCode, message);
                messageQueue?.Enqueue(errorMessage);
            }
            catch (Exception ex)
            {
                Print($"ERROR sending error message: {ex.Message}");
            }
        }

        #endregion

        #region Cleanup

        private void Cleanup()
        {
            isRunning = false;

            // Stop message queue
            messageQueue?.Stop();
            messageQueue?.Dispose();

            // Stop reader thread
            if (readThread != null && readThread.IsAlive)
            {
                readThread.Join(2000);
            }

            // Dispose managers
            orderManager?.Dispose();
            marketDataManager?.Dispose();
            accountDataManager?.Dispose();

            // Close pipe
            lock (pipeLock)
            {
                try
                {
                    reader?.Close();
                    pipeServer?.Close();
                }
                catch { }

                reader = null;
                pipeServer = null;
            }

            Print("NT8PythonAdapter cleaned up");
        }

        #endregion
    }
}
