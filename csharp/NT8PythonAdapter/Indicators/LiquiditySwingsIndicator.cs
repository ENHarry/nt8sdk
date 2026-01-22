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
    // Enums must be at namespace level for NinjaTrader wrapper generation
    public enum LiquiditySwingArea
    {
        WickExtremity,
        FullRange
    }
    
    public enum LiquidityFilterType
    {
        Count,
        Volume
    }
    
    /// <summary>
    /// Liquidity Swings Indicator - Based on LuxAlgo's Pine Script
    /// 
    /// Identifies swing highs/lows as liquidity levels and tracks when
    /// these levels are swept by price action. Includes volume-based
    /// filtering and multi-timeframe support.
    /// 
    /// Features:
    /// - Pivot high/low detection for liquidity zones
    /// - Volume filtering for significant levels
    /// - Sweep tracking (wick vs full break)
    /// - Intrabar precision option
    /// </summary>
    public class LiquiditySwingsIndicator : Indicator
    {
        #region Nested Types
        
        public class SwingLevel
        {
            public double Price { get; set; }
            public double Top { get; set; }
            public double Bottom { get; set; }
            public int BarIndex { get; set; }
            public bool IsHigh { get; set; }
            public bool Crossed { get; set; }
            public int TouchCount { get; set; }
            public double TotalVolume { get; set; }
            public DateTime CreatedTime { get; set; }
        }
        
        #endregion
        
        #region Variables
        
        private SwingLevel currentHighSwing;
        private SwingLevel currentLowSwing;
        private List<SwingLevel> swingHighHistory;
        private List<SwingLevel> swingLowHistory;
        
        // Pivot tracking
        private Series<double> pivotHighSeries;
        private Series<double> pivotLowSeries;
        
        #endregion
        
        #region Properties
        
        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Pivot Lookback", Order = 1, GroupName = "Parameters")]
        public int PivotLookback { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 1)]
        [Display(Name = "Swing Area (0=Wick, 1=Full)", Order = 2, GroupName = "Parameters")]
        public int AreaMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 1)]
        [Display(Name = "Filter By (0=Count, 1=Volume)", Order = 3, GroupName = "Filter")]
        public int FilterMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "Filter Value", Order = 4, GroupName = "Filter")]
        public double FilterValue { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "Max History", Order = 5, GroupName = "Display")]
        public int MaxHistory { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Show Swing High", Order = 6, GroupName = "Display")]
        public bool ShowSwingHigh { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Show Swing Low", Order = 7, GroupName = "Display")]
        public bool ShowSwingLow { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Export to File", Order = 8, GroupName = "Output")]
        public bool ExportToFile { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Output File Path", Order = 9, GroupName = "Output")]
        public string OutputFilePath { get; set; }
        
        // Plot series
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> SwingHigh { get { return Values[0]; } }
        
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> SwingLow { get { return Values[1]; } }
        
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> HighVolume { get { return Values[2]; } }
        
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> LowVolume { get { return Values[3]; } }
        
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> NearestResistance { get { return Values[4]; } }
        
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> NearestSupport { get { return Values[5]; } }
        
        // Public accessors for strategies
        public SwingLevel CurrentSwingHigh { get { return currentHighSwing; } }
        public SwingLevel CurrentSwingLow { get { return currentLowSwing; } }
        public List<SwingLevel> SwingHighLevels { get { return swingHighHistory; } }
        public List<SwingLevel> SwingLowLevels { get { return swingLowHistory; } }
        
        #endregion
        
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Liquidity Swings - Identifies swing high/low liquidity levels";
                Name = "LiquiditySwings";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                
                // Default parameters
                PivotLookback = 14;
                AreaMode = 0;  // 0=WickExtremity, 1=FullRange
                FilterMode = 0;  // 0=Count, 1=Volume
                FilterValue = 0;
                MaxHistory = 100;
                ShowSwingHigh = true;
                ShowSwingLow = true;
                ExportToFile = false;
                OutputFilePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "NT8_Python_Data", "liquidity_swings.csv"
                );
                
                AddPlot(Brushes.Red, "SwingHigh");
                AddPlot(Brushes.Teal, "SwingLow");
                AddPlot(Brushes.Orange, "HighVolume");
                AddPlot(Brushes.Cyan, "LowVolume");
                AddPlot(new Stroke(Brushes.Transparent), PlotStyle.Line, "NearestResistance");  // Hidden series
                AddPlot(new Stroke(Brushes.Transparent), PlotStyle.Line, "NearestSupport");  // Hidden series
            }
            else if (State == State.DataLoaded)
            {
                pivotHighSeries = new Series<double>(this);
                pivotLowSeries = new Series<double>(this);
                swingHighHistory = new List<SwingLevel>();
                swingLowHistory = new List<SwingLevel>();
                
                currentHighSwing = new SwingLevel();
                currentLowSwing = new SwingLevel();
            }
        }
        
        protected override void OnBarUpdate()
        {
            // Pine's ta.pivothigh(length, length) requires 'length' bars on each side
            // So we need at least PivotLookback * 2 bars
            if (CurrentBar < PivotLookback * 2)
            {
                pivotHighSeries[0] = High[0];
                pivotLowSeries[0] = Low[0];
                SwingHigh[0] = 0;
                SwingLow[0] = 0;
                HighVolume[0] = 0;
                LowVolume[0] = 0;
                NearestResistance[0] = 0;
                NearestSupport[0] = 0;
                return;
            }
            
            // Detect pivots
            DetectPivots();
            
            // Track swing high
            TrackSwingHigh();
            
            // Track swing low
            TrackSwingLow();
            
            // Output
            OutputResults();
            
            // Update nearest resistance/support series
            NearestResistance[0] = GetNearestResistance(Close[0]);
            NearestSupport[0] = GetNearestSupport(Close[0]);
            
            // Export
            if (ExportToFile && (CurrentBar % 100 == 0 || CurrentBar == BarsArray[0].Count - 1))
            {
                ExportSwings();
            }
        }
        
        #region Pivot Detection
        
        private void DetectPivots()
        {
            bool isPivotHigh = true;
            bool isPivotLow = true;
            
            double checkHigh = High[PivotLookback];
            double checkLow = Low[PivotLookback];
            
            for (int i = 0; i <= PivotLookback * 2; i++)
            {
                if (i == PivotLookback) continue;
                
                if (i <= CurrentBar && High[i] >= checkHigh)
                    isPivotHigh = false;
                if (i <= CurrentBar && Low[i] <= checkLow)
                    isPivotLow = false;
            }
            
            pivotHighSeries[0] = isPivotHigh ? checkHigh : pivotHighSeries[1];
            pivotLowSeries[0] = isPivotLow ? checkLow : pivotLowSeries[1];
            
            // Create new swing high
            if (isPivotHigh && ShowSwingHigh)
            {
                int swingBar = CurrentBar - PivotLookback;
                double swingTop = checkHigh;
                double swingBottom = AreaMode == 0  // 0=WickExtremity
                    ? Math.Max(Close[PivotLookback], Open[PivotLookback])
                    : Low[PivotLookback];
                
                currentHighSwing = new SwingLevel
                {
                    Price = checkHigh,
                    Top = swingTop,
                    Bottom = swingBottom,
                    BarIndex = swingBar,
                    IsHigh = true,
                    Crossed = false,
                    TouchCount = 0,
                    TotalVolume = 0,
                    CreatedTime = Time[PivotLookback]
                };
                
                swingHighHistory.Add(currentHighSwing);
                if (swingHighHistory.Count > MaxHistory)
                    swingHighHistory.RemoveAt(0);
            }
            
            // Create new swing low
            if (isPivotLow && ShowSwingLow)
            {
                int swingBar = CurrentBar - PivotLookback;
                double swingTop = AreaMode == 0  // 0=WickExtremity
                    ? Math.Min(Close[PivotLookback], Open[PivotLookback])
                    : High[PivotLookback];
                double swingBottom = checkLow;
                
                currentLowSwing = new SwingLevel
                {
                    Price = checkLow,
                    Top = swingTop,
                    Bottom = swingBottom,
                    BarIndex = swingBar,
                    IsHigh = false,
                    Crossed = false,
                    TouchCount = 0,
                    TotalVolume = 0,
                    CreatedTime = Time[PivotLookback]
                };
                
                swingLowHistory.Add(currentLowSwing);
                if (swingLowHistory.Count > MaxHistory)
                    swingLowHistory.RemoveAt(0);
            }
        }
        
        #endregion
        
        #region Swing Tracking
        
        private void TrackSwingHigh()
        {
            if (currentHighSwing == null || currentHighSwing.Crossed) return;
            
            // Count touches and volume
            if (Low[PivotLookback] < currentHighSwing.Top &&
                High[PivotLookback] > currentHighSwing.Bottom)
            {
                currentHighSwing.TouchCount++;
                currentHighSwing.TotalVolume += Volume[PivotLookback];
            }
            
            // Check for cross
            if (Close[0] > currentHighSwing.Top)
            {
                currentHighSwing.Crossed = true;
            }
        }
        
        private void TrackSwingLow()
        {
            if (currentLowSwing == null || currentLowSwing.Crossed) return;
            
            // Count touches and volume
            if (Low[PivotLookback] < currentLowSwing.Top &&
                High[PivotLookback] > currentLowSwing.Bottom)
            {
                currentLowSwing.TouchCount++;
                currentLowSwing.TotalVolume += Volume[PivotLookback];
            }
            
            // Check for cross
            if (Close[0] < currentLowSwing.Bottom)
            {
                currentLowSwing.Crossed = true;
            }
        }
        
        #endregion
        
        #region Output
        
        private void OutputResults()
        {
            // Filter check
            double highTarget = FilterMode == 0  // 0=Count
                ? currentHighSwing?.TouchCount ?? 0
                : currentHighSwing?.TotalVolume ?? 0;
            
            double lowTarget = FilterMode == 0  // 0=Count
                ? currentLowSwing?.TouchCount ?? 0
                : currentLowSwing?.TotalVolume ?? 0;
            
            // Output values
            SwingHigh[0] = (ShowSwingHigh && currentHighSwing != null && !currentHighSwing.Crossed && highTarget >= FilterValue)
                ? currentHighSwing.Price : 0;
            
            SwingLow[0] = (ShowSwingLow && currentLowSwing != null && !currentLowSwing.Crossed && lowTarget >= FilterValue)
                ? currentLowSwing.Price : 0;
            
            HighVolume[0] = currentHighSwing?.TotalVolume ?? 0;
            LowVolume[0] = currentLowSwing?.TotalVolume ?? 0;
            
            // Draw levels
            if (ShowSwingHigh && currentHighSwing != null && !currentHighSwing.Crossed && highTarget >= FilterValue)
            {
                Draw.Line(this, "SwingHighLine" + currentHighSwing.BarIndex, false,
                    CurrentBar - currentHighSwing.BarIndex, currentHighSwing.Price,
                    0, currentHighSwing.Price, Brushes.Red, DashStyleHelper.Solid, 1);
            }
            
            if (ShowSwingLow && currentLowSwing != null && !currentLowSwing.Crossed && lowTarget >= FilterValue)
            {
                Draw.Line(this, "SwingLowLine" + currentLowSwing.BarIndex, false,
                    CurrentBar - currentLowSwing.BarIndex, currentLowSwing.Price,
                    0, currentLowSwing.Price, Brushes.Teal, DashStyleHelper.Solid, 1);
            }
        }
        
        private void ExportSwings()
        {
            try
            {
                string dir = Path.GetDirectoryName(OutputFilePath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                
                using (var writer = new StreamWriter(OutputFilePath, false))
                {
                    writer.WriteLine("Type,Price,Top,Bottom,BarIndex,Crossed,TouchCount,TotalVolume,CreatedTime");
                    
                    foreach (var swing in swingHighHistory)
                    {
                        writer.WriteLine($"HIGH,{swing.Price:F2},{swing.Top:F2},{swing.Bottom:F2}," +
                            $"{swing.BarIndex},{swing.Crossed},{swing.TouchCount},{swing.TotalVolume:F0}," +
                            $"{swing.CreatedTime:yyyy-MM-dd HH:mm:ss}");
                    }
                    
                    foreach (var swing in swingLowHistory)
                    {
                        writer.WriteLine($"LOW,{swing.Price:F2},{swing.Top:F2},{swing.Bottom:F2}," +
                            $"{swing.BarIndex},{swing.Crossed},{swing.TouchCount},{swing.TotalVolume:F0}," +
                            $"{swing.CreatedTime:yyyy-MM-dd HH:mm:ss}");
                    }
                }
            }
            catch (Exception ex)
            {
                Print("LiquiditySwings export error: " + ex.Message);
            }
        }
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// Get all active (uncrossed) swing high levels.
        /// </summary>
        public List<SwingLevel> GetActiveSwingHighs()
        {
            return swingHighHistory.Where(s => !s.Crossed).ToList();
        }
        
        /// <summary>
        /// Get all active (uncrossed) swing low levels.
        /// </summary>
        public List<SwingLevel> GetActiveSwingLows()
        {
            return swingLowHistory.Where(s => !s.Crossed).ToList();
        }
        
        /// <summary>
        /// Get nearest resistance level.
        /// </summary>
        public double GetNearestResistance(double price)
        {
            var level = swingHighHistory
                .Where(s => !s.Crossed && s.Price > price)
                .OrderBy(s => s.Price)
                .FirstOrDefault();
            
            return level?.Price ?? 0;
        }
        
        /// <summary>
        /// Get nearest support level.
        /// </summary>
        public double GetNearestSupport(double price)
        {
            var level = swingLowHistory
                .Where(s => !s.Crossed && s.Price < price)
                .OrderByDescending(s => s.Price)
                .FirstOrDefault();
            
            return level?.Price ?? 0;
        }
        
        /// <summary>
        /// Check if price is near a swing high (resistance).
        /// </summary>
        public bool IsNearResistance(double price, double tolerancePercent = 0.5)
        {
            foreach (var swing in swingHighHistory.Where(s => !s.Crossed))
            {
                double distPct = (swing.Price - price) / price * 100;
                if (distPct > 0 && distPct <= tolerancePercent)
                    return true;
            }
            return false;
        }
        
        /// <summary>
        /// Check if price is near a swing low (support).
        /// </summary>
        public bool IsNearSupport(double price, double tolerancePercent = 0.5)
        {
            foreach (var swing in swingLowHistory.Where(s => !s.Crossed))
            {
                double distPct = (price - swing.Price) / price * 100;
                if (distPct > 0 && distPct <= tolerancePercent)
                    return true;
            }
            return false;
        }
        
        #endregion
    }
}
