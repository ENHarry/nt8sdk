using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;

namespace NinjaTrader.NinjaScript.AddOns
{
    /// <summary>
    /// Manages market data subscriptions and streaming to Python
    /// </summary>
    public class MarketDataManager
    {
        private readonly Action<byte[]> sendMessageCallback;
        private readonly Action<string> logCallback;

        // Market data subscriptions
        private readonly ConcurrentDictionary<string, MarketDataSubscription> subscriptions;

        public MarketDataManager(Action<byte[]> sendMessageCallback, Action<string> logCallback)
        {
            this.sendMessageCallback = sendMessageCallback ?? throw new ArgumentNullException(nameof(sendMessageCallback));
            this.logCallback = logCallback ?? throw new ArgumentNullException(nameof(logCallback));

            subscriptions = new ConcurrentDictionary<string, MarketDataSubscription>();
        }

        #region Subscription Management

        /// <summary>
        /// Subscribe to market data for an instrument
        /// </summary>
        public bool Subscribe(string instrumentName)
        {
            try
            {
                instrumentName = (instrumentName ?? string.Empty).Trim();
                string normalizedKey = NormalizeInstrumentKey(instrumentName);

                if (subscriptions.ContainsKey(normalizedKey))
                {
                    logCallback?.Invoke($"Already subscribed to {instrumentName}");
                    return true;
                }

                // Get instrument
                Instrument instrument = Instrument.GetInstrument(instrumentName);
                if (instrument == null)
                {
                    logCallback?.Invoke($"ERROR: Instrument not found: {instrumentName}");
                    SendError(2001, $"Instrument not found: {instrumentName}");
                    return false;
                }

                // Create subscription
                MarketDataSubscription subscription = new MarketDataSubscription
                {
                    Instrument = instrument,
                    InstrumentName = instrumentName,
                    IsActive = true
                };

                // Subscribe to market data events
                if (instrument.MarketData != null)
                {
                    instrument.MarketData.Update -= OnMarketDataUpdate;
                    instrument.MarketData.Update += OnMarketDataUpdate;
                }

                subscriptions[normalizedKey] = subscription;

                logCallback?.Invoke($"Subscribed to market data: {instrumentName}");

                // Send instrument info
                SendInstrumentInfo(instrument);

                return true;
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"ERROR subscribing to {instrumentName}: {ex.Message}");
                SendError(2002, $"Subscription failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Unsubscribe from market data
        /// </summary>
        public bool Unsubscribe(string instrumentName)
        {
            try
            {
                string normalizedKey = NormalizeInstrumentKey(instrumentName);
                if (!subscriptions.TryRemove(normalizedKey, out MarketDataSubscription subscription))
                {
                    logCallback?.Invoke($"Not subscribed to {instrumentName}");
                    return false;
                }

                subscription.IsActive = false;

                // Unsubscribe from events
                if (subscription.Instrument?.MarketData != null)
                {
                    subscription.Instrument.MarketData.Update -= OnMarketDataUpdate;
                }

                logCallback?.Invoke($"Unsubscribed from market data: {instrumentName}");
                return true;
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"ERROR unsubscribing from {instrumentName}: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// Handle market data updates
        /// </summary>
        private void OnMarketDataUpdate(object sender, MarketDataEventArgs e)
        {
            try
            {
                // Get the instrument that triggered this event
                MarketData marketData = sender as MarketData;
                if (marketData == null || marketData.Instrument == null)
                    return;

                string instrumentKey = NormalizeInstrumentKey(marketData.Instrument.FullName);

                // Check if we're subscribed
                if (!subscriptions.TryGetValue(instrumentKey, out MarketDataSubscription subscription))
                    return;

                if (!subscription.IsActive)
                    return;

                // Extract tick data
                double price = e.MarketDataType == MarketDataType.Last ? e.Price : marketData.Last.Price;
                double bid = e.MarketDataType == MarketDataType.Bid ? e.Price : marketData.Bid.Price;
                double ask = e.MarketDataType == MarketDataType.Ask ? e.Price : marketData.Ask.Price;
                long volume = e.MarketDataType == MarketDataType.Last ? e.Volume : 1;

                // Send tick to Python
                SendTick(
                    instrumentName,
                    price,
                    volume,
                    bid,
                    ask
                );
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"ERROR in OnMarketDataUpdate: {ex.Message}");
            }
        }

        #endregion

        #region Data Sending

        /// <summary>
        /// Send tick data to Python
        /// </summary>
        private void SendTick(string instrumentName, double price, long volume, double bid, double ask)
        {
            try
            {
                double timestamp = BinaryProtocolHelper.GetCurrentTimestamp();

                byte[] message = BinaryProtocolHelper.EncodeTickData(
                    instrumentName,
                    timestamp,
                    price,
                    volume,
                    bid,
                    ask
                );

                sendMessageCallback(message);
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"ERROR sending tick: {ex.Message}");
            }
        }

        /// <summary>
        /// Send instrument information to Python
        /// </summary>
        public void SendInstrumentInfo(Instrument instrument)
        {
            try
            {
                if (instrument == null || instrument.MasterInstrument == null)
                    return;

                double tickSize = instrument.MasterInstrument.TickSize;
                double pointValue = instrument.MasterInstrument.PointValue;
                string exchange = "Unknown";

                byte[] message = BinaryProtocolHelper.EncodeInstrumentInfo(
                    instrument.FullName,
                    tickSize,
                    pointValue,
                    tickSize,  // min_move same as tick_size for most instruments
                    exchange
                );

                sendMessageCallback(message);

                logCallback?.Invoke($"Sent instrument info: {instrument.FullName}, tick={tickSize}, point={pointValue}");
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"ERROR sending instrument info: {ex.Message}");
            }
        }

        /// <summary>
        /// Send instrument info by name (on request)
        /// </summary>
        public bool SendInstrumentInfoByName(string instrumentName)
        {
            try
            {
                Instrument instrument = Instrument.GetInstrument(instrumentName);
                if (instrument == null)
                {
                    SendError(2003, $"Instrument not found: {instrumentName}");
                    return false;
                }

                SendInstrumentInfo(instrument);
                return true;
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"ERROR getting instrument info: {ex.Message}");
                SendError(2004, $"Failed to get instrument info: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Helper Methods

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
        /// Get list of subscribed instruments
        /// </summary>
        public List<string> GetSubscribedInstruments()
        {
            var instruments = new List<string>();
            foreach (var kvp in subscriptions)
            {
                instruments.Add(kvp.Value?.InstrumentName ?? kvp.Key);
            }
            return instruments;
        }

        #endregion

        #region Cleanup

        /// <summary>
        /// Cleanup all subscriptions
        /// </summary>
        public void Dispose()
        {
            foreach (var subscription in subscriptions.Values)
            {
                try
                {
                    subscription.IsActive = false;
                    if (subscription.Instrument?.MarketData != null)
                    {
                        subscription.Instrument.MarketData.Update -= OnMarketDataUpdate;
                    }
                }
                catch (Exception ex)
                {
                    logCallback?.Invoke($"ERROR disposing subscription: {ex.Message}");
                }
            }

            subscriptions.Clear();
        }

        #endregion
    }

    /// <summary>
    /// Represents a market data subscription
    /// </summary>
    internal class MarketDataSubscription
    {
        public Instrument Instrument { get; set; }
        public string InstrumentName { get; set; }
        public bool IsActive { get; set; }
        public DateTime SubscribeTime { get; set; } = DateTime.Now;
        public long TickCount { get; set; }
    }

        private static string NormalizeInstrumentKey(string instrumentName)
        {
            return string.IsNullOrWhiteSpace(instrumentName)
                ? string.Empty
                : instrumentName.Trim().ToUpperInvariant();
        }
}
