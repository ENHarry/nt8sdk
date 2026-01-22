#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.IO;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// Multi-Timeframe Liquidity Sweep/Grab Indicator
    /// 
    /// Detects:
    /// 1. Liquidity grabs (single-candle sweeps with rejection)
    /// 2. Liquidity sweeps (multi-candle penetrations)
    /// 3. Premium/Discount zones with delta volume
    /// 
    /// Based on:
    /// - Liquidity Grabs by Flux Charts (TradingView)
    /// - Liquidity Sweeps by LuxAlgo (TradingView)
    /// </summary>
    public class LiquiditySweepGrabIndicator : Indicator
    {
        #region Nested Types
        
        public enum GrabType
        {
            None,
            BuysideSmall,
            BuysideMedium,
            BuysideLarge,
            SellsideSmall,
            SellsideMedium,
            SellsideLarge
        }
        
        public enum ZoneType
        {
            Premium,
            Discount,
            Equilibrium
        }
        
        public class LiquidityLevel
        {
            public double Price { get; set; }
            public int BarIndex { get; set; }
            public bool IsHigh { get; set; }
            public bool Broken { get; set; }
            public bool Mitigated { get; set; }
            public bool Taken { get; set; }
            public bool WickSweep { get; set; }
            public double Volume { get; set; }
        }
        
        public class LiquidityGrab
        {
            public GrabType Type { get; set; }
            public double Price { get; set; }
            public int BarIndex { get; set; }
            public double WickPrice { get; set; }
            public double ClosePrice { get; set; }
            public double BodySize { get; set; }
            public double WickSize { get; set; }
            public double WickBodyRatio { get; set; }
            public double Volume { get; set; }
            public int GrabSize { get; set; }
            public bool IsBuyside { get; set; }
            public double LiquidityLevel { get; set; }
            public DateTime Timestamp { get; set; }
        }
        
        #endregion
        
        #region Variables
        
        private List<LiquidityLevel> buysideLiquidity;
        private List<LiquidityLevel> sellsideLiquidity;
        private List<LiquidityGrab> grabHistory;
        private int lastBuysideGrabBar;
        private int lastSellsideGrabBar;
        
        // Premium/Discount zone tracking
        private double srRangeHigh;
        private double srRangeLow;
        private double macroRangeHigh;
        private double macroRangeLow;
        private double srDeltaPercent;
        private double macroDeltaPercent;
        
        // Signal state
        private string currentSignal;
        private double signalStrength;
        private string signalReason;
        
        #endregion
        
        #region Properties
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Pivot Length", Order = 1, GroupName = "Parameters")]
        public int PivotLength { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 10)]
        [Display(Name = "Wick Body Ratio", Order = 2, GroupName = "Parameters")]
        public double WickBodyRatio { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Grab Cooldown", Order = 3, GroupName = "Parameters")]
        public int GrabCooldown { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Liquidity Zones", Order = 4, GroupName = "Parameters")]
        public int MaxLiquidityZones { get; set; }
        
        [NinjaScriptProperty]
        [Range(10, 500)]
        [Display(Name = "SR Lookback", Order = 5, GroupName = "Premium/Discount")]
        public int SRLookback { get; set; }
        
        [NinjaScriptProperty]
        [Range(50, 1000)]
        [Display(Name = "Macro Lookback", Order = 6, GroupName = "Premium/Discount")]
        public int MacroLookback { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Export to File", Order = 7, GroupName = "Output")]
        public bool ExportToFile { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Output File Path", Order = 8, GroupName = "Output")]
        public string OutputFilePath { get; set; }
        
        // Output Series
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> BuysideGrab { get { return Values[0]; } }
        
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> SellsideGrab { get { return Values[1]; } }
        
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> SRDeltaVolume { get { return Values[2]; } }
        
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> MacroDeltaVolume { get { return Values[3]; } }
        
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> LiquidityGrabSignal { get { return Values[4]; } }
        
        // Readable properties for strategies
        public string CurrentSignal { get { return currentSignal; } }
        public double SignalStrength { get { return signalStrength; } }
        public List<LiquidityLevel> BuysideLevels { get { return buysideLiquidity; } }
        public List<LiquidityLevel> SellsideLevels { get { return sellsideLiquidity; } }
        public double Equilibrium { get { return (srRangeHigh + srRangeLow) / 2; } }
        public ZoneType CurrentZone
        {
            get
            {
                double eq = Equilibrium;
                if (Close[0] > eq) return ZoneType.Premium;
                if (Close[0] < eq) return ZoneType.Discount;
                return ZoneType.Equilibrium;
            }
        }
        
        #endregion
        
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Multi-Timeframe Liquidity Sweep/Grab Detection";
                Name = "LiquiditySweepGrab";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                
                // Default parameters
                PivotLength = 25;
                WickBodyRatio = 0.5;
                GrabCooldown = 3;
                MaxLiquidityZones = 5;
                SRLookback = 50;
                MacroLookback = 200;
                ExportToFile = false;
                OutputFilePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "NT8_Python_Data", "liquidity_grabs.csv"
                );
                
                AddPlot(Brushes.Red, "BuysideGrab");
                AddPlot(Brushes.Green, "SellsideGrab");
                AddPlot(Brushes.Orange, "SRDeltaVolume");
                AddPlot(Brushes.Purple, "MacroDeltaVolume");
                AddPlot(new Stroke(Brushes.Transparent), PlotStyle.Line, "LiquidityGrabSignal");  // Hidden signal series
            }
            else if (State == State.DataLoaded)
            {
                buysideLiquidity = new List<LiquidityLevel>();
                sellsideLiquidity = new List<LiquidityLevel>();
                grabHistory = new List<LiquidityGrab>();
                lastBuysideGrabBar = -GrabCooldown - 1;
                lastSellsideGrabBar = -GrabCooldown - 1;
                currentSignal = "NONE";
                signalStrength = 0.0;
                signalReason = "";
            }
        }
        
        protected override void OnBarUpdate()
        {
            if (CurrentBar < PivotLength + 1)
            {
                BuysideGrab[0] = 0;
                SellsideGrab[0] = 0;
                SRDeltaVolume[0] = 0;
                MacroDeltaVolume[0] = 0;
                LiquidityGrabSignal[0] = 0;
                return;
            }
            
            // Step 1: Detect pivot highs/lows for liquidity levels
            DetectPivots();
            
            // Step 2: Detect liquidity grabs
            var grabs = DetectLiquidityGrabs();
            
            // Step 3: Update level status
            UpdateLevelStatus();
            
            // Step 4: Calculate premium/discount zones
            CalculatePremiumDiscountZones();
            
            // Step 5: Generate trading signal
            GenerateSignal(grabs);
            
            // Step 6: Output plots
            OutputResults(grabs);
            
            // Step 7: Export to file if enabled
            if (ExportToFile && grabs.Count > 0)
            {
                ExportGrabs(grabs);
            }
        }
        
        #region Pivot Detection
        
        /// <summary>
        /// Detect pivot highs and lows using Pine Script's pivothigh/pivotlow logic.
        /// Pine's ta.pivothigh(length, length) checks 'length' bars on BOTH sides.
        /// A pivot high at bar[length] requires all bars from bar[0] to bar[2*length] 
        /// (excluding bar[length]) to have lower highs.
        /// </summary>
        private void DetectPivots()
        {
            // Need at least PivotLength bars on each side
            if (CurrentBar < PivotLength * 2) return;
            
            // Check the bar at index [PivotLength] - this is PivotLength bars ago
            double checkHigh = High[PivotLength];
            double checkLow = Low[PivotLength];
            double checkVolume = Volume[PivotLength];
            int checkBarIndex = CurrentBar - PivotLength;
            
            // Detect pivot high - Pine: ta.pivothigh(length, length)
            // Check that High[PivotLength] is higher than all bars from [0] to [2*PivotLength]
            bool isPivotHigh = true;
            for (int i = 0; i <= PivotLength * 2 && i <= CurrentBar; i++)
            {
                if (i == PivotLength) continue;  // Skip the bar we're checking
                if (High[i] >= checkHigh)
                {
                    isPivotHigh = false;
                    break;
                }
            }
            
            if (isPivotHigh)
            {
                buysideLiquidity.Add(new LiquidityLevel
                {
                    Price = checkHigh,
                    BarIndex = checkBarIndex,
                    IsHigh = true,
                    Volume = checkVolume
                });
                
                if (buysideLiquidity.Count > MaxLiquidityZones)
                    buysideLiquidity.RemoveAt(0);
            }
            
            // Detect pivot low - Pine: ta.pivotlow(length, length)
            bool isPivotLow = true;
            for (int i = 0; i <= PivotLength * 2 && i <= CurrentBar; i++)
            {
                if (i == PivotLength) continue;
                if (Low[i] <= checkLow)
                {
                    isPivotLow = false;
                    break;
                }
            }
            
            if (isPivotLow)
            {
                sellsideLiquidity.Add(new LiquidityLevel
                {
                    Price = checkLow,
                    BarIndex = checkBarIndex,
                    IsHigh = false,
                    Volume = checkVolume
                });
                
                if (sellsideLiquidity.Count > MaxLiquidityZones)
                    sellsideLiquidity.RemoveAt(0);
            }
        }
        
        #endregion
        
        #region Liquidity Grab Detection
        
        private List<LiquidityGrab> DetectLiquidityGrabs()
        {
            var grabs = new List<LiquidityGrab>();
            
            double currentHigh = High[0];
            double currentLow = Low[0];
            double currentClose = Close[0];
            double currentOpen = Open[0];
            double currentVolume = Volume[0];
            
            double bodySize = Math.Abs(currentClose - currentOpen);
            if (bodySize < TickSize) bodySize = TickSize;
            
            double bodyTop = Math.Max(currentClose, currentOpen);
            double bodyBottom = Math.Min(currentClose, currentOpen);
            
            // Check for buyside grab
            bool cooldownOkBuy = (CurrentBar - lastBuysideGrabBar) > GrabCooldown;
            
            foreach (var level in buysideLiquidity.ToList())
            {
                if (level.Mitigated || level.Taken) continue;
                
                if (currentHigh > level.Price && bodyTop < level.Price)
                {
                    double wickSize = currentHigh - bodyTop;
                    double wbr = wickSize / bodySize;
                    
                    // Pine: _grabSize := math.floor(math.min(curWBR / WBR, 3))
                    // Only trigger if grabSize > 0
                    int grabSize = (int)Math.Floor(Math.Min(wbr / WickBodyRatio, 3));
                    
                    if (grabSize > 0 && cooldownOkBuy)
                    {
                        GrabType grabType;
                        
                        switch (grabSize)
                        {
                            case 1: grabType = GrabType.BuysideSmall; break;
                            case 2: grabType = GrabType.BuysideMedium; break;
                            default: grabType = GrabType.BuysideLarge; break;
                        }
                        
                        var grab = new LiquidityGrab
                        {
                            Type = grabType,
                            Price = currentClose,
                            BarIndex = CurrentBar,
                            WickPrice = currentHigh,
                            ClosePrice = currentClose,
                            BodySize = bodySize,
                            WickSize = wickSize,
                            WickBodyRatio = wbr,
                            Volume = currentVolume,
                            GrabSize = grabSize,
                            IsBuyside = true,
                            LiquidityLevel = level.Price,
                            Timestamp = Time[0]
                        };
                        
                        grabs.Add(grab);
                        grabHistory.Add(grab);
                        lastBuysideGrabBar = CurrentBar;
                        level.WickSweep = true;
                        break;
                    }
                }
            }
            
            // Check for sellside grab
            bool cooldownOkSell = (CurrentBar - lastSellsideGrabBar) > GrabCooldown;
            
            foreach (var level in sellsideLiquidity.ToList())
            {
                if (level.Mitigated || level.Taken) continue;
                
                if (currentLow < level.Price && bodyBottom > level.Price)
                {
                    double wickSize = bodyBottom - currentLow;
                    double wbr = wickSize / bodySize;
                    
                    // Pine: _grabSize := math.floor(math.min(curWBR / WBR, 3))
                    // Only trigger if grabSize > 0
                    int grabSize = (int)Math.Floor(Math.Min(wbr / WickBodyRatio, 3));
                    
                    if (grabSize > 0 && cooldownOkSell)
                    {
                        GrabType grabType;
                        
                        switch (grabSize)
                        {
                            case 1: grabType = GrabType.SellsideSmall; break;
                            case 2: grabType = GrabType.SellsideMedium; break;
                            default: grabType = GrabType.SellsideLarge; break;
                        }
                        
                        var grab = new LiquidityGrab
                        {
                            Type = grabType,
                            Price = currentClose,
                            BarIndex = CurrentBar,
                            WickPrice = currentLow,
                            ClosePrice = currentClose,
                            BodySize = bodySize,
                            WickSize = wickSize,
                            WickBodyRatio = wbr,
                            Volume = currentVolume,
                            GrabSize = grabSize,
                            IsBuyside = false,
                            LiquidityLevel = level.Price,
                            Timestamp = Time[0]
                        };
                        
                        grabs.Add(grab);
                        grabHistory.Add(grab);
                        lastSellsideGrabBar = CurrentBar;
                        level.WickSweep = true;
                        break;
                    }
                }
            }
            
            return grabs;
        }
        
        #endregion
        
        #region Level Status Update
        
        private void UpdateLevelStatus()
        {
            double currentClose = Close[0];
            
            // Update buyside levels
            foreach (var level in buysideLiquidity.ToList())
            {
                if (!level.Mitigated)
                {
                    if (currentClose > level.Price)
                    {
                        level.Broken = true;
                    }
                    else if (level.Broken && currentClose < level.Price)
                    {
                        level.Mitigated = true;
                        level.Taken = true;
                    }
                }
            }
            
            // Update sellside levels
            foreach (var level in sellsideLiquidity.ToList())
            {
                if (!level.Mitigated)
                {
                    if (currentClose < level.Price)
                    {
                        level.Broken = true;
                    }
                    else if (level.Broken && currentClose > level.Price)
                    {
                        level.Mitigated = true;
                        level.Taken = true;
                    }
                }
            }
            
            // Cleanup old levels
            buysideLiquidity.RemoveAll(l => l.Mitigated && l.Taken);
            sellsideLiquidity.RemoveAll(l => l.Mitigated && l.Taken);
        }
        
        #endregion
        
        #region Premium/Discount Zones
        
        private void CalculatePremiumDiscountZones()
        {
            // SR Zone (short-term)
            int srBars = Math.Min(SRLookback, CurrentBar);
            double srHighest = double.MinValue;
            double srLowest = double.MaxValue;
            double srPosVol = 0;
            double srNegVol = 0;
            
            for (int i = 0; i < srBars; i++)
            {
                if (High[i] > srHighest) srHighest = High[i];
                if (Low[i] < srLowest) srLowest = Low[i];
                
                if (Close[i] > Open[i])
                    srPosVol += Volume[i];
                else if (Close[i] < Open[i])
                    srNegVol += Volume[i];
            }
            
            srRangeHigh = srHighest;
            srRangeLow = srLowest;
            
            // Pine stores negVol as NEGATIVE values, formula: (negVol/posVol + 1) * 100
            // C# stores as positive, so we negate: ((-srNegVol / srPosVol) + 1) * 100
            if (srPosVol > 0)
                srDeltaPercent = ((-srNegVol / srPosVol) + 1) * 100;
            else
                srDeltaPercent = 0;
            
            srDeltaPercent = Math.Max(-100, Math.Min(100, srDeltaPercent));
            
            // Macro Zone (long-term)
            int macroBars = Math.Min(MacroLookback, CurrentBar);
            double macroHighest = double.MinValue;
            double macroLowest = double.MaxValue;
            double macroPosVol = 0;
            double macroNegVol = 0;
            
            for (int i = 0; i < macroBars; i++)
            {
                if (High[i] > macroHighest) macroHighest = High[i];
                if (Low[i] < macroLowest) macroLowest = Low[i];
                
                if (Close[i] > Open[i])
                    macroPosVol += Volume[i];
                else if (Close[i] < Open[i])
                    macroNegVol += Volume[i];
            }
            
            macroRangeHigh = macroHighest;
            macroRangeLow = macroLowest;
            
            // Pine stores negVol as NEGATIVE values, formula: (negVol/posVol + 1) * 100
            // C# stores as positive, so we negate: ((-macroNegVol / macroPosVol) + 1) * 100
            if (macroPosVol > 0)
                macroDeltaPercent = ((-macroNegVol / macroPosVol) + 1) * 100;
            else
                macroDeltaPercent = 0;
            
            macroDeltaPercent = Math.Max(-100, Math.Min(100, macroDeltaPercent));
        }
        
        #endregion
        
        #region Signal Generation
        
        private void GenerateSignal(List<LiquidityGrab> grabs)
        {
            currentSignal = "NONE";
            signalStrength = 0.0;
            signalReason = "";
            
            if (grabs.Count == 0) return;
            
            var grab = grabs.Last();
            double baseStrength = grab.GrabSize / 3.0;
            var reasons = new List<string>();
            
            double eq = Equilibrium;
            ZoneType zone = CurrentZone;
            
            if (grab.IsBuyside)
            {
                // Buyside grab = bearish signal
                currentSignal = "SELL";
                reasons.Add($"Buyside grab at {grab.WickPrice:F2}");
                
                if (zone == ZoneType.Premium)
                {
                    signalStrength += 0.2;
                    reasons.Add("Price in PREMIUM zone");
                }
                
                if (srDeltaPercent < 0)
                {
                    signalStrength += 0.15;
                    reasons.Add($"Delta volume bearish: {srDeltaPercent:F1}%");
                }
            }
            else
            {
                // Sellside grab = bullish signal
                currentSignal = "BUY";
                reasons.Add($"Sellside grab at {grab.WickPrice:F2}");
                
                if (zone == ZoneType.Discount)
                {
                    signalStrength += 0.2;
                    reasons.Add("Price in DISCOUNT zone");
                }
                
                if (srDeltaPercent > 0)
                {
                    signalStrength += 0.15;
                    reasons.Add($"Delta volume bullish: {srDeltaPercent:F1}%");
                }
            }
            
            signalStrength += baseStrength * 0.5;
            signalStrength = Math.Min(1.0, signalStrength);
            signalReason = string.Join(" | ", reasons);
        }
        
        #endregion
        
        #region Output
        
        private void OutputResults(List<LiquidityGrab> grabs)
        {
            // Buyside grab indicator
            var buysideGrab = grabs.FirstOrDefault(g => g.IsBuyside);
            BuysideGrab[0] = buysideGrab != null ? buysideGrab.GrabSize : 0;
            
            // Sellside grab indicator
            var sellsideGrab = grabs.FirstOrDefault(g => !g.IsBuyside);
            SellsideGrab[0] = sellsideGrab != null ? sellsideGrab.GrabSize : 0;
            
            // Delta volumes
            SRDeltaVolume[0] = srDeltaPercent;
            MacroDeltaVolume[0] = macroDeltaPercent;
            
            // Set liquidity grab signal: 1=bullish (sellside grab), -1=bearish (buyside grab), 0=none
            // Sellside grab = price swept below support then reversed = BULLISH signal
            // Buyside grab = price swept above resistance then reversed = BEARISH signal
            if (sellsideGrab != null && currentSignal == "BUY")
                LiquidityGrabSignal[0] = 1;
            else if (buysideGrab != null && currentSignal == "SELL")
                LiquidityGrabSignal[0] = -1;
            else
                LiquidityGrabSignal[0] = 0;
            
            // Draw visuals
            if (buysideGrab != null)
            {
                Draw.Diamond(this, "BuyGrab" + CurrentBar, true, 0, High[0] + TickSize * 5,
                    Brushes.Red);
            }
            
            if (sellsideGrab != null)
            {
                Draw.Diamond(this, "SellGrab" + CurrentBar, true, 0, Low[0] - TickSize * 5,
                    Brushes.Green);
            }
        }
        
        private void ExportGrabs(List<LiquidityGrab> grabs)
        {
            try
            {
                string dir = Path.GetDirectoryName(OutputFilePath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                
                bool fileExists = File.Exists(OutputFilePath);
                
                using (var writer = new StreamWriter(OutputFilePath, true))
                {
                    if (!fileExists)
                    {
                        writer.WriteLine("Timestamp,Type,Price,WickPrice,GrabSize,IsBuyside,LiquidityLevel,Signal,Strength,Zone,SRDelta,MacroDelta");
                    }
                    
                    foreach (var grab in grabs)
                    {
                        writer.WriteLine($"{grab.Timestamp:yyyy-MM-dd HH:mm:ss}," +
                            $"{grab.Type},{grab.Price:F2},{grab.WickPrice:F2}," +
                            $"{grab.GrabSize},{grab.IsBuyside},{grab.LiquidityLevel:F2}," +
                            $"{currentSignal},{signalStrength:F2},{CurrentZone}," +
                            $"{srDeltaPercent:F1},{macroDeltaPercent:F1}");
                    }
                }
            }
            catch (Exception ex)
            {
                Print("LiquiditySweepGrab export error: " + ex.Message);
            }
        }
        
        #endregion
        
        #region Public Methods for Strategies
        
        /// <summary>
        /// Check if buy should be blocked due to nearby buyside liquidity.
        /// </summary>
        public bool ShouldBlockBuy(double tolerancePct = 0.5)
        {
            double currentPrice = Close[0];
            
            foreach (var level in buysideLiquidity.Where(l => !l.Mitigated && l.Price > currentPrice))
            {
                double distPct = (level.Price - currentPrice) / currentPrice * 100;
                if (distPct <= tolerancePct)
                    return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Check if sell should be blocked due to nearby sellside liquidity.
        /// </summary>
        public bool ShouldBlockSell(double tolerancePct = 0.5)
        {
            double currentPrice = Close[0];
            
            foreach (var level in sellsideLiquidity.Where(l => !l.Mitigated && l.Price < currentPrice))
            {
                double distPct = (currentPrice - level.Price) / currentPrice * 100;
                if (distPct <= tolerancePct)
                    return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Get nearest liquidity level above current price.
        /// </summary>
        public double GetNearestResistance()
        {
            var level = buysideLiquidity
                .Where(l => !l.Mitigated && l.Price > Close[0])
                .OrderBy(l => l.Price)
                .FirstOrDefault();
            
            return level?.Price ?? 0;
        }
        
        /// <summary>
        /// Get nearest liquidity level below current price.
        /// </summary>
        public double GetNearestSupport()
        {
            var level = sellsideLiquidity
                .Where(l => !l.Mitigated && l.Price < Close[0])
                .OrderByDescending(l => l.Price)
                .FirstOrDefault();
            
            return level?.Price ?? 0;
        }
        
        /// <summary>
        /// Check if price is in premium zone.
        /// </summary>
        public bool IsInPremiumZone()
        {
            return CurrentZone == ZoneType.Premium;
        }
        
        /// <summary>
        /// Check if price is in discount zone.
        /// </summary>
        public bool IsInDiscountZone()
        {
            return CurrentZone == ZoneType.Discount;
        }
        
        /// <summary>
        /// Get delta volume bias.
        /// </summary>
        public string GetDeltaVolumeBias()
        {
            if (srDeltaPercent > 20) return "BULLISH";
            if (srDeltaPercent < -20) return "BEARISH";
            return "NEUTRAL";
        }
        
        #endregion
    }
}
