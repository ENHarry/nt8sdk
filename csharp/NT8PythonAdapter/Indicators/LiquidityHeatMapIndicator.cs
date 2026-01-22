#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// Dynamic Liquidity HeatMap Profile - Ported from Pine Script
    /// 
    /// Features:
    /// - Volume-weighted price level detection
    /// - Pivot high/low detection for liquidity zones
    /// - Dynamic bin resolution for level clustering
    /// - Maximum liquidity point detection (POC-like)
    /// - Integration with mean reversion signals
    /// </summary>
    public class LiquidityHeatMapIndicator : Indicator
    {
        #region Types
        public class LiquidityLevel
        {
            public double PriceLevel { get; set; }
            public double Volume { get; set; }
            public int BarIndex { get; set; }
            public bool IsPivotHigh { get; set; }
            public bool IsPivotLow { get; set; }
            
            public LiquidityLevel(double price, double vol, int barIdx, bool pivHigh = false, bool pivLow = false)
            {
                PriceLevel = price;
                Volume = vol;
                BarIndex = barIdx;
                IsPivotHigh = pivHigh;
                IsPivotLow = pivLow;
            }
        }
        
        public class VolumeBin
        {
            public double LowPrice { get; set; }
            public double HighPrice { get; set; }
            public double TotalVolume { get; set; }
            public int BarCount { get; set; }
            
            public double MidPrice => (LowPrice + HighPrice) / 2;
        }
        #endregion

        #region Variables
        private List<LiquidityLevel> liquidityLevels = new List<LiquidityLevel>();
        private List<VolumeBin> volumeBins = new List<VolumeBin>();
        private const int MAX_LEVELS = 200;
        
        // Pivot detection
        private Series<double> pivotHighSeries;
        private Series<double> pivotLowSeries;
        
        // Key levels
        private double pointOfControl = 0;  // Maximum liquidity level
        private double valueAreaHigh = 0;
        private double valueAreaLow = 0;
        
        // Current bar analysis
        private double nearestResistance = 0;
        private double nearestSupport = 0;
        private bool priceAtLiquidity = false;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Dynamic Liquidity HeatMap Profile - Pine Script Port";
                Name = "LiquidityHeatMapIndicator";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                
                // Parameters matching Pine Script
                PivotLength = 10;
                BinCount = 25;
                LookbackPeriod = 200;
                LiquidityThreshold = 70;  // Percentile for high liquidity
                ProximityTicks = 8;  // How close price must be to level
                
                AddPlot(Brushes.Cyan, "LiquidityLevel");
                AddPlot(Brushes.Yellow, "PointOfControl");
            }
            else if (State == State.DataLoaded)
            {
                pivotHighSeries = new Series<double>(this);
                pivotLowSeries = new Series<double>(this);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < PivotLength)
            {
                pivotHighSeries[0] = High[0];
                pivotLowSeries[0] = Low[0];
                Values[0][0] = 0;
                Values[1][0] = 0;
                return;
            }
            
            // Detect pivot points
            DetectPivots();
            
            // Add current bar's volume data
            AddBarLiquidity();
            
            // Build volume profile bins
            BuildVolumeProfile();
            
            // Calculate key levels
            CalculateKeyLevels();
            
            // Check proximity to liquidity
            CheckLiquidityProximity();
            
            // Plot values
            Values[0][0] = priceAtLiquidity ? 1 : 0;
            Values[1][0] = pointOfControl;
        }

        #region Pivot Detection
        /// <summary>
        /// Detect pivot highs and lows for liquidity zones
        /// </summary>
        private void DetectPivots()
        {
            // Check for pivot high (highest high in lookback window)
            bool isPivotHigh = true;
            bool isPivotLow = true;
            int halfPivot = PivotLength / 2;
            
            if (CurrentBar < PivotLength)
            {
                pivotHighSeries[0] = pivotHighSeries[1];
                pivotLowSeries[0] = pivotLowSeries[1];
                return;
            }
            
            double checkHigh = High[halfPivot];
            double checkLow = Low[halfPivot];
            
            for (int i = 0; i <= PivotLength; i++)
            {
                if (i == halfPivot)
                    continue;
                    
                if (High[i] >= checkHigh)
                    isPivotHigh = false;
                if (Low[i] <= checkLow)
                    isPivotLow = false;
            }
            
            pivotHighSeries[0] = isPivotHigh ? checkHigh : pivotHighSeries[1];
            pivotLowSeries[0] = isPivotLow ? checkLow : pivotLowSeries[1];
            
            // Add confirmed pivots as high-priority liquidity levels
            if (isPivotHigh)
            {
                var pivotLevel = new LiquidityLevel(
                    checkHigh, 
                    Volume[halfPivot] * 2,  // Weight pivot levels higher
                    CurrentBar - halfPivot,
                    pivHigh: true
                );
                liquidityLevels.Add(pivotLevel);
            }
            
            if (isPivotLow)
            {
                var pivotLevel = new LiquidityLevel(
                    checkLow, 
                    Volume[halfPivot] * 2,  // Weight pivot levels higher
                    CurrentBar - halfPivot,
                    pivLow: true
                );
                liquidityLevels.Add(pivotLevel);
            }
            
            // Cleanup old levels
            CleanupOldLevels();
        }
        
        /// <summary>
        /// Remove levels outside lookback period
        /// </summary>
        private void CleanupOldLevels()
        {
            int cutoffBar = CurrentBar - LookbackPeriod;
            liquidityLevels.RemoveAll(l => l.BarIndex < cutoffBar);
            
            // Keep list manageable
            while (liquidityLevels.Count > MAX_LEVELS)
            {
                // Remove lowest volume level (keep high liquidity)
                var minVolLevel = liquidityLevels.OrderBy(l => l.Volume).First();
                liquidityLevels.Remove(minVolLevel);
            }
        }
        #endregion

        #region Volume Profile
        /// <summary>
        /// Add current bar's volume to liquidity data
        /// </summary>
        private void AddBarLiquidity()
        {
            // Add high and low prices with volume weighting
            var highLevel = new LiquidityLevel(High[0], Volume[0] * 0.3, CurrentBar);
            var lowLevel = new LiquidityLevel(Low[0], Volume[0] * 0.3, CurrentBar);
            var closeLevel = new LiquidityLevel(Close[0], Volume[0] * 0.4, CurrentBar);
            
            liquidityLevels.Add(highLevel);
            liquidityLevels.Add(lowLevel);
            liquidityLevels.Add(closeLevel);
        }
        
        /// <summary>
        /// Build volume profile bins from liquidity levels
        /// </summary>
        private void BuildVolumeProfile()
        {
            if (liquidityLevels.Count == 0)
                return;
                
            volumeBins.Clear();
            
            // Find price range
            double maxPrice = liquidityLevels.Max(l => l.PriceLevel);
            double minPrice = liquidityLevels.Min(l => l.PriceLevel);
            double range = maxPrice - minPrice;
            
            if (range < TickSize)
                return;
                
            double binSize = range / BinCount;
            
            // Create bins
            for (int i = 0; i < BinCount; i++)
            {
                var bin = new VolumeBin
                {
                    LowPrice = minPrice + (i * binSize),
                    HighPrice = minPrice + ((i + 1) * binSize),
                    TotalVolume = 0,
                    BarCount = 0
                };
                
                // Sum volume in this bin
                foreach (var level in liquidityLevels)
                {
                    if (level.PriceLevel >= bin.LowPrice && level.PriceLevel < bin.HighPrice)
                    {
                        bin.TotalVolume += level.Volume;
                        bin.BarCount++;
                    }
                }
                
                volumeBins.Add(bin);
            }
        }
        #endregion

        #region Key Levels
        /// <summary>
        /// Calculate Point of Control and Value Area
        /// </summary>
        private void CalculateKeyLevels()
        {
            if (volumeBins.Count == 0)
                return;
                
            // Find POC (bin with highest volume)
            var pocBin = volumeBins.OrderByDescending(b => b.TotalVolume).FirstOrDefault();
            if (pocBin != null)
            {
                pointOfControl = pocBin.MidPrice;
            }
            
            // Calculate Value Area (70% of total volume around POC)
            double totalVolume = volumeBins.Sum(b => b.TotalVolume);
            double targetVolume = totalVolume * 0.70;
            
            // Start from POC and expand outward
            var sortedByDistFromPOC = volumeBins
                .OrderBy(b => Math.Abs(b.MidPrice - pointOfControl))
                .ToList();
            
            double accumulatedVolume = 0;
            double vaHigh = pointOfControl;
            double vaLow = pointOfControl;
            
            foreach (var bin in sortedByDistFromPOC)
            {
                accumulatedVolume += bin.TotalVolume;
                vaHigh = Math.Max(vaHigh, bin.HighPrice);
                vaLow = Math.Min(vaLow, bin.LowPrice);
                
                if (accumulatedVolume >= targetVolume)
                    break;
            }
            
            valueAreaHigh = vaHigh;
            valueAreaLow = vaLow;
            
            // Find nearest resistance/support based on high-volume bins
            var significantBins = volumeBins
                .Where(b => b.TotalVolume > volumeBins.Average(x => x.TotalVolume))
                .ToList();
            
            nearestResistance = double.MaxValue;
            nearestSupport = 0;
            
            foreach (var bin in significantBins)
            {
                if (bin.MidPrice > Close[0] && bin.MidPrice < nearestResistance)
                    nearestResistance = bin.MidPrice;
                if (bin.MidPrice < Close[0] && bin.MidPrice > nearestSupport)
                    nearestSupport = bin.MidPrice;
            }
            
            if (nearestResistance == double.MaxValue)
                nearestResistance = valueAreaHigh;
            if (nearestSupport == 0)
                nearestSupport = valueAreaLow;
        }
        
        /// <summary>
        /// Check if current price is near a high-liquidity level
        /// </summary>
        private void CheckLiquidityProximity()
        {
            priceAtLiquidity = false;
            double proximityRange = ProximityTicks * TickSize;
            double currentPrice = Close[0];
            
            // Check if near POC
            if (Math.Abs(currentPrice - pointOfControl) <= proximityRange)
            {
                priceAtLiquidity = true;
                return;
            }
            
            // Check pivot levels
            foreach (var level in liquidityLevels.Where(l => l.IsPivotHigh || l.IsPivotLow))
            {
                if (Math.Abs(currentPrice - level.PriceLevel) <= proximityRange)
                {
                    priceAtLiquidity = true;
                    return;
                }
            }
            
            // Check high-volume bins
            double volumeThreshold = volumeBins.Count > 0 
                ? volumeBins.Average(b => b.TotalVolume) + (volumeBins.Max(b => b.TotalVolume) - volumeBins.Average(b => b.TotalVolume)) * (LiquidityThreshold / 100.0)
                : 0;
                
            foreach (var bin in volumeBins.Where(b => b.TotalVolume >= volumeThreshold))
            {
                if (currentPrice >= bin.LowPrice && currentPrice <= bin.HighPrice)
                {
                    priceAtLiquidity = true;
                    return;
                }
            }
        }
        #endregion

        #region Properties
        [NinjaScriptProperty]
        [Range(2, 50)]
        [Display(Name = "Pivot Length", Description = "Bars to confirm pivot high/low", Order = 1, GroupName = "Parameters")]
        public int PivotLength { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Bin Count", Description = "Number of price bins for volume profile", Order = 2, GroupName = "Parameters")]
        public int BinCount { get; set; }

        [NinjaScriptProperty]
        [Range(50, 500)]
        [Display(Name = "Lookback Period", Description = "Bars of liquidity data to analyze", Order = 3, GroupName = "Parameters")]
        public int LookbackPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(50, 95)]
        [Display(Name = "Liquidity Threshold", Description = "Percentile for high liquidity classification", Order = 4, GroupName = "Parameters")]
        public int LiquidityThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Proximity Ticks", Description = "Ticks distance to consider 'at' level", Order = 5, GroupName = "Parameters")]
        public int ProximityTicks { get; set; }

        // Public accessors for strategies
        
        /// <summary>
        /// Point of Control - highest liquidity price level
        /// </summary>
        [Browsable(false)]
        [XmlIgnore]
        public double POC => pointOfControl;

        /// <summary>
        /// Value Area High - upper boundary of 70% volume
        /// </summary>
        [Browsable(false)]
        [XmlIgnore]
        public double VAH => valueAreaHigh;

        /// <summary>
        /// Value Area Low - lower boundary of 70% volume
        /// </summary>
        [Browsable(false)]
        [XmlIgnore]
        public double VAL => valueAreaLow;

        /// <summary>
        /// Nearest resistance level based on high volume
        /// </summary>
        [Browsable(false)]
        [XmlIgnore]
        public double NearestResistance => nearestResistance;

        /// <summary>
        /// Nearest support level based on high volume
        /// </summary>
        [Browsable(false)]
        [XmlIgnore]
        public double NearestSupport => nearestSupport;

        /// <summary>
        /// True if price is currently at a high-liquidity level
        /// </summary>
        public bool AtLiquidityLevel => priceAtLiquidity;

        /// <summary>
        /// True if price is in the value area
        /// </summary>
        public bool InValueArea => Close[0] >= valueAreaLow && Close[0] <= valueAreaHigh;

        /// <summary>
        /// True if price is above POC (bullish bias)
        /// </summary>
        public bool AbovePOC => Close[0] > pointOfControl;

        /// <summary>
        /// True if price is below POC (bearish bias)
        /// </summary>
        public bool BelowPOC => Close[0] < pointOfControl;

        /// <summary>
        /// Returns liquidity-based signal: 1=long (bouncing from support), -1=short (rejected from resistance), 0=none
        /// </summary>
        public int LiquiditySignal
        {
            get
            {
                if (!priceAtLiquidity)
                    return 0;
                    
                // Near support + bullish candle
                double proximityRange = ProximityTicks * TickSize;
                if (Math.Abs(Close[0] - nearestSupport) <= proximityRange && Close[0] > Open[0])
                    return 1;
                    
                // Near resistance + bearish candle
                if (Math.Abs(Close[0] - nearestResistance) <= proximityRange && Close[0] < Open[0])
                    return -1;
                    
                return 0;
            }
        }

        /// <summary>
        /// All liquidity levels
        /// </summary>
        [Browsable(false)]
        [XmlIgnore]
        public List<LiquidityLevel> Levels => liquidityLevels;

        /// <summary>
        /// Volume profile bins
        /// </summary>
        [Browsable(false)]
        [XmlIgnore]
        public List<VolumeBin> Bins => volumeBins;
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.
namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
    {
        private LiquidityHeatMapIndicator[] cacheLiquidityHeatMapIndicator;
        public LiquidityHeatMapIndicator LiquidityHeatMapIndicator(int pivotLength, int binCount, int lookbackPeriod, int liquidityThreshold, int proximityTicks)
        {
            return LiquidityHeatMapIndicator(Input, pivotLength, binCount, lookbackPeriod, liquidityThreshold, proximityTicks);
        }

        public LiquidityHeatMapIndicator LiquidityHeatMapIndicator(ISeries<double> input, int pivotLength, int binCount, int lookbackPeriod, int liquidityThreshold, int proximityTicks)
        {
            if (cacheLiquidityHeatMapIndicator != null)
                for (int idx = 0; idx < cacheLiquidityHeatMapIndicator.Length; idx++)
                    if (cacheLiquidityHeatMapIndicator[idx] != null && cacheLiquidityHeatMapIndicator[idx].PivotLength == pivotLength && cacheLiquidityHeatMapIndicator[idx].BinCount == binCount && cacheLiquidityHeatMapIndicator[idx].LookbackPeriod == lookbackPeriod && cacheLiquidityHeatMapIndicator[idx].LiquidityThreshold == liquidityThreshold && cacheLiquidityHeatMapIndicator[idx].ProximityTicks == proximityTicks && cacheLiquidityHeatMapIndicator[idx].EqualsInput(input))
                        return cacheLiquidityHeatMapIndicator[idx];
            return CacheIndicator<LiquidityHeatMapIndicator>(new LiquidityHeatMapIndicator() { PivotLength = pivotLength, BinCount = binCount, LookbackPeriod = lookbackPeriod, LiquidityThreshold = liquidityThreshold, ProximityTicks = proximityTicks }, input, ref cacheLiquidityHeatMapIndicator);
        }
    }
}

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
    {
        public Indicators.LiquidityHeatMapIndicator LiquidityHeatMapIndicator(int pivotLength, int binCount, int lookbackPeriod, int liquidityThreshold, int proximityTicks)
        {
            return indicator.LiquidityHeatMapIndicator(Input, pivotLength, binCount, lookbackPeriod, liquidityThreshold, proximityTicks);
        }

        public Indicators.LiquidityHeatMapIndicator LiquidityHeatMapIndicator(ISeries<double> input, int pivotLength, int binCount, int lookbackPeriod, int liquidityThreshold, int proximityTicks)
        {
            return indicator.LiquidityHeatMapIndicator(input, pivotLength, binCount, lookbackPeriod, liquidityThreshold, proximityTicks);
        }
    }
}
#endregion
