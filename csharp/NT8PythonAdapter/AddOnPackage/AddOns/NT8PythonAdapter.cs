using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using NinjaTrader.NinjaScript;
using NinjaTrader.Cbi;
using NinjaTrader.Data;


namespace NinjaTrader.NinjaScript.AddOns
{
    /// <summary>
    /// Python SDK Adapter for NinjaTrader 8
    /// Provides Named Pipe IPC between Python and NT8
    /// </summary>
    public class NT8PythonAdapter : AddOnBase
    {
        private NamedPipeServerStream pipeServer;
        private BinaryWriter writer;
        private BinaryReader reader;
        private Thread readThread;
        private bool isRunning;
        
        private ConcurrentDictionary<string, Instrument> subscribedInstruments;
        private ConcurrentDictionary<string, Order> activeOrders;
        private object pipeLock = new object();
        
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Python SDK Adapter for NT8";
                Name = "NT8PythonAdapter";
                
                subscribedInstruments = new ConcurrentDictionary<string, Instrument>();
                activeOrders = new ConcurrentDictionary<string, Order>();
            }
            else if (State == State.Terminated)
            {
                Cleanup();
            }
        }
        
        protected override void OnStartUp()
        {
            base.OnStartUp();
            StartPipeServer();
        }
        
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
                    writer = new BinaryWriter(pipeServer);
                    reader = new BinaryReader(pipeServer);
                }
                
                isRunning = true;
                readThread = new Thread(ReadLoop);
                readThread.IsBackground = true;
                readThread.Start();
                
                Print("Python client connected successfully!");
            }
            catch (Exception ex)
            {
                Print($"ERROR during connection: {ex.Message}");
            }
        }
        
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
                    }
                }
                catch (IOException)
                {
                    // Pipe disconnected
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
        
        private void ProcessCommand(byte[] data, int length)
        {
            try
            {
                string command = Encoding.UTF8.GetString(data, 0, length).Trim();
                string[] parts = command.Split('|');
                
                if (parts.Length == 0) return;
                
                switch (parts[0])
                {
                    case "SUBSCRIBE":
                        if (parts.Length >= 2)
                            SubscribeMarketData(parts[1]);
                        break;
                        
                    case "UNSUBSCRIBE":
                        if (parts.Length >= 2)
                            UnsubscribeMarketData(parts[1]);
                        break;
                        
                    case "ORDER":
                        // Parse binary order command
                        ProcessOrderCommand(data);
                        break;
                        
                    default:
                        Print($"Unknown command: {parts[0]}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Print($"ERROR processing command: {ex.Message}");
            }
        }
        
        private void SubscribeMarketData(string instrumentName)
        {
            try
            {
                // In a real implementation, you would:
                // 1. Look up the Instrument object
                // 2. Subscribe to MarketData events
                // 3. Forward ticks to Python via pipe
                
                Print($"Subscribing to market data: {instrumentName}");
                
                // Placeholder - actual implementation depends on how you access instruments
                // You may need to create a NinjaScript strategy that handles subscriptions
            }
            catch (Exception ex)
            {
                Print($"ERROR subscribing to {instrumentName}: {ex.Message}");
            }
        }
        
        private void UnsubscribeMarketData(string instrumentName)
        {
            try
            {
                Print($"Unsubscribing from market data: {instrumentName}");
                subscribedInstruments.TryRemove(instrumentName, out _);
            }
            catch (Exception ex)
            {
                Print($"ERROR unsubscribing from {instrumentName}: {ex.Message}");
            }
        }
        
        private void ProcessOrderCommand(byte[] data)
        {
            try
            {
                // Parse binary order command
                // Implementation depends on your protocol
                Print("Processing order command...");
            }
            catch (Exception ex)
            {
                Print($"ERROR processing order: {ex.Message}");
            }
        }
        
        private void SendToPython(byte[] data)
        {
            try
            {
                lock (pipeLock)
                {
                    if (writer != null && pipeServer != null && pipeServer.IsConnected)
                    {
                        writer.Write(data);
                        writer.Flush();
                    }
                }
            }
            catch (Exception ex)
            {
                Print($"ERROR sending to Python: {ex.Message}");
            }
        }
        
        private void Cleanup()
        {
            isRunning = false;
            
            if (readThread != null && readThread.IsAlive)
            {
                readThread.Join(2000);
            }
            
            lock (pipeLock)
            {
                try
                {
                    writer?.Close();
                    reader?.Close();
                    pipeServer?.Close();
                }
                catch { }
                
                writer = null;
                reader = null;
                pipeServer = null;
            }
            
            subscribedInstruments?.Clear();
            activeOrders?.Clear();
        }
    }
}