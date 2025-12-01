using System;
using System.Timers;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns
{
    /// <summary>
    /// Manages account data tracking and streaming to Python
    /// </summary>
    public class AccountDataManager
    {
        private readonly Account account;
        private readonly Action<byte[]> sendMessageCallback;
        private readonly Action<string> logCallback;

        // Periodic update timer
        private readonly Timer updateTimer;
        private readonly int updateIntervalMs;

        // Last known values for change detection
        private double lastCashValue;
        private double lastBuyingPower;
        private double lastRealizedPnl;
        private double lastUnrealizedPnl;

        public AccountDataManager(Account account, Action<byte[]> sendMessageCallback,
                                Action<string> logCallback, int updateIntervalMs = 1000)
        {
            this.account = account ?? throw new ArgumentNullException(nameof(account));
            this.sendMessageCallback = sendMessageCallback ?? throw new ArgumentNullException(nameof(sendMessageCallback));
            this.logCallback = logCallback ?? throw new ArgumentNullException(nameof(logCallback));
            this.updateIntervalMs = updateIntervalMs;

            // Initialize last values
            UpdateLastValues();

            // Subscribe to account events
            account.AccountItemUpdate += OnAccountItemUpdate;

            // Setup periodic update timer
            updateTimer = new Timer(updateIntervalMs);
            updateTimer.Elapsed += OnTimerElapsed;
            updateTimer.AutoReset = true;
            updateTimer.Start();

            logCallback?.Invoke($"Account data manager started for: {account.Name}");
        }

        #region Event Handlers

        /// <summary>
        /// Handle account item updates
        /// </summary>
        private void OnAccountItemUpdate(object sender, AccountItemEventArgs e)
        {
            try
            {
                // Send immediate update on significant changes
                bool significantChange = false;

                switch (e.AccountItem)
                {
                    case AccountItem.CashValue:
                    case AccountItem.BuyingPower:
                    case AccountItem.RealizedProfitLoss:
                    case AccountItem.GrossRealizedProfitLoss:
                        significantChange = true;
                        break;
                }

                if (significantChange)
                {
                    SendAccountUpdate("BALANCE");
                }
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"ERROR in OnAccountItemUpdate: {ex.Message}");
            }
        }

        /// <summary>
        /// Periodic timer for account updates
        /// </summary>
        private void OnTimerElapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                // Check if values have changed
                double currentCash = GetCashValue();
                double currentBuyingPower = GetBuyingPower();
                double currentRealized = GetRealizedPnl();
                double currentUnrealized = GetUnrealizedPnl();

                bool hasChanged =
                    Math.Abs(currentCash - lastCashValue) > 0.01 ||
                    Math.Abs(currentBuyingPower - lastBuyingPower) > 0.01 ||
                    Math.Abs(currentRealized - lastRealizedPnl) > 0.01 ||
                    Math.Abs(currentUnrealized - lastUnrealizedPnl) > 0.01;

                if (hasChanged)
                {
                    SendAccountUpdate("PERIODIC");
                    UpdateLastValues();
                }
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"ERROR in timer update: {ex.Message}");
            }
        }

        #endregion

        #region Account Data Retrieval

        /// <summary>
        /// Get current cash value
        /// </summary>
        private double GetCashValue()
        {
            try
            {
                return account.Get(AccountItem.CashValue, Currency.UsDollar);
            }
            catch
            {
                return 0.0;
            }
        }

        /// <summary>
        /// Get buying power
        /// </summary>
        private double GetBuyingPower()
        {
            try
            {
                return account.Get(AccountItem.BuyingPower, Currency.UsDollar);
            }
            catch
            {
                return 0.0;
            }
        }

        /// <summary>
        /// Get realized P&L
        /// </summary>
        private double GetRealizedPnl()
        {
            try
            {
                return account.Get(AccountItem.RealizedProfitLoss, Currency.UsDollar);
            }
            catch
            {
                return 0.0;
            }
        }

        /// <summary>
        /// Get unrealized P&L
        /// </summary>
        private double GetUnrealizedPnl()
        {
            try
            {
                // Calculate from positions
                double totalUnrealized = 0.0;
                foreach (var position in account.Positions)
                {
                    totalUnrealized += position.GetUnrealizedProfitLoss(PerformanceUnit.Currency);
                }
                return totalUnrealized;
            }
            catch
            {
                return 0.0;
            }
        }

        /// <summary>
        /// Get net liquidation value
        /// </summary>
        private double GetNetLiquidation()
        {
            try
            {
                return account.Get(AccountItem.NetLiquidation, Currency.UsDollar);
            }
            catch
            {
                double cash = GetCashValue();
                double unrealized = GetUnrealizedPnl();
                return cash + unrealized;
            }
        }

        /// <summary>
        /// Update last known values
        /// </summary>
        private void UpdateLastValues()
        {
            lastCashValue = GetCashValue();
            lastBuyingPower = GetBuyingPower();
            lastRealizedPnl = GetRealizedPnl();
            lastUnrealizedPnl = GetUnrealizedPnl();
        }

        #endregion

        #region Data Sending

        /// <summary>
        /// Send account update to Python
        /// </summary>
        public void SendAccountUpdate(string updateType = "BALANCE")
        {
            try
            {
                double cashValue = GetCashValue();
                double buyingPower = GetBuyingPower();
                double realizedPnl = GetRealizedPnl();
                double unrealizedPnl = GetUnrealizedPnl();
                double netLiquidation = GetNetLiquidation();

                byte[] message = BinaryProtocolHelper.EncodeAccountUpdate(
                    account.Name,
                    BinaryProtocolHelper.GetCurrentTimestamp(),
                    cashValue,
                    buyingPower,
                    realizedPnl,
                    unrealizedPnl,
                    netLiquidation,
                    updateType
                );

                sendMessageCallback(message);

                // Log occasionally, not on every update
                if (updateType != "PERIODIC" || DateTime.Now.Second % 10 == 0)
                {
                    logCallback?.Invoke($"Account update sent: Cash={cashValue:F2}, BP={buyingPower:F2}, P&L={realizedPnl + unrealizedPnl:F2}");
                }
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"ERROR sending account update: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle explicit account update request from Python
        /// </summary>
        public void HandleUpdateRequest()
        {
            try
            {
                SendAccountUpdate("REQUEST");
                logCallback?.Invoke("Account update request processed");
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"ERROR handling update request: {ex.Message}");
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Get account summary
        /// </summary>
        public string GetAccountSummary()
        {
            try
            {
                return $"Account: {account.Name}, " +
                       $"Status: {account.ConnectionStatus}, " +
                       $"Cash: {GetCashValue():C2}, " +
                       $"BP: {GetBuyingPower():C2}, " +
                       $"P&L: {(GetRealizedPnl() + GetUnrealizedPnl()):C2}";
            }
            catch (Exception ex)
            {
                return $"ERROR: {ex.Message}";
            }
        }

        /// <summary>
        /// Check if account is ready for trading
        /// </summary>
        public bool IsAccountReady()
        {
            try
            {
                return account != null &&
                       account.ConnectionStatus == ConnectionStatus.Connected &&
                       GetBuyingPower() > 0;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Cleanup

        /// <summary>
        /// Dispose and cleanup resources
        /// </summary>
        public void Dispose()
        {
            try
            {
                // Stop timer
                updateTimer?.Stop();
                updateTimer?.Dispose();

                // Unsubscribe from events
                if (account != null)
                {
                    account.AccountItemUpdate -= OnAccountItemUpdate;
                }

                logCallback?.Invoke("Account data manager disposed");
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"ERROR disposing account manager: {ex.Message}");
            }
        }

        #endregion
    }
}
