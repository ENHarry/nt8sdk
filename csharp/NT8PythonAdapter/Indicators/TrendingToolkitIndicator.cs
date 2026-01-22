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
    /// Trending Market Toolkit - Ported from LuxAlgo Pine Script
    /// 
    /// Features:
    /// - Leg-based pivot detection for swing highs/lows
    /// - 5-point reversal pattern: Detects trend reversals using price containment
    /// - 4-point continuation pattern: Confirms trend continuation
    /// - Fibonacci retracement zones (0.5 and 0.618 levels) for entry timing
    /// </summary>
    public class TrendingToolkitIndicator : Indicator
    {
        #region Types
        public class SwingPoint
        {
            public int BarIndex { get; set; }
            public double PriceLevel { get; set; }
            public int SwingType { get; set; }  // 1=HIGH, 0=LOW
            
            public SwingPoint(int barIndex, double price, int swingType)
            {
                BarIndex = barIndex;
                PriceLevel = price;
                SwingType = swingType;
            }
        }
        
        public enum PatternType
        {
            None = 0,
            BullishReversal = 1,
            BearishReversal = -1,
            BullishContinuation = 2,
            BearishContinuation = -2
        }
        #endregion

        #region Variables
        private const int HIGH = 1;
        private const int LOW = 0;
        private const int MAX_SWING_POINTS = 20;
        
        private Series<double> swingLeg;
        private Series<double> pivotHighPrice;
        private Series<double> pivotLowPrice;
        private Series<int> pivotIndex;
        
        private List<SwingPoint> swingPoints = new List<SwingPoint>();
        
        // Fibonacci retracement zones
        private double fibTop = 0;
        private double fibBottom = 0;
        private bool inRetracementZone = false;
        private int currentTrendBias = 0;  // 1=BULLISH, -1=BEARISH, 0=NEUTRAL
        
        // Last detected pattern
        private PatternType lastPattern = PatternType.None;
        private int lastPatternBar = -1;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Trending Market Toolkit [LuxAlgo] - Pine Script Port";
                Name = "TrendingToolkitIndicator";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                
                // Parameters matching Pine Script
                PivotLength = 15;
                ReversalAreaThreshold = 0.5;  // Multiplied by ATR(200)
                
                AddPlot(Brushes.Green, "BullishSignal");
                AddPlot(Brushes.Red, "BearishSignal");
            }
            else if (State == State.DataLoaded)
            {
                swingLeg = new Series<double>(this);
                pivotHighPrice = new Series<double>(this);
                pivotLowPrice = new Series<double>(this);
                pivotIndex = new Series<int>(this);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < PivotLength)
            {
                swingLeg[0] = -1;  // Uninitialized
                pivotHighPrice[0] = High[0];
                pivotLowPrice[0] = Low[0];
                pivotIndex[0] = CurrentBar;
                Values[0][0] = 0;
                Values[1][0] = 0;
                return;
            }
            
            DetectLegBasedPivots();
            
            // Check for patterns
            lastPattern = PatternType.None;
            
            int reversalSignal = Detect5PointReversal();
            if (reversalSignal != 0)
            {
                lastPattern = reversalSignal == 1 ? PatternType.BullishReversal : PatternType.BearishReversal;
                lastPatternBar = CurrentBar;
                CalculateFibonacciZone(reversalSignal == 1);
            }
            
            int continuationSignal = Detect4PointContinuation();
            if (continuationSignal != 0 && lastPattern == PatternType.None)
            {
                lastPattern = continuationSignal == 1 ? PatternType.BullishContinuation : PatternType.BearishContinuation;
                lastPatternBar = CurrentBar;
                CalculateFibonacciZone(continuationSignal == 1);
            }
            
            // Update Fibonacci zone status
            if (inRetracementZone)
            {
                CheckFibonacciEntry();
            }
            
            // Plot signals
            Values[0][0] = ((int)lastPattern > 0) ? 1 : 0;  // Bullish
            Values[1][0] = ((int)lastPattern < 0) ? 1 : 0;  // Bearish
        }

        #region Pivot Detection
        /// <summary>
        /// LuxAlgo Leg-Based Pivot Detection
        /// Detects swing highs/lows using leg changes
        /// </summary>
        private void DetectLegBasedPivots()
        {
            // Carry forward previous values
            double currentLeg = CurrentBar > 0 ? swingLeg[1] : -1;
            double currentPivotHigh = CurrentBar > 0 ? pivotHighPrice[1] : High[0];
            double currentPivotLow = CurrentBar > 0 ? pivotLowPrice[1] : Low[0];
            int currentPivotIndex = CurrentBar > 0 ? pivotIndex[1] : 0;

            // Check if current bar is highest/lowest in lookback period
            bool isHighest = true;
            bool isLowest = true;

            for (int i = 1; i <= PivotLength && CurrentBar >= i; i++)
            {
                if (High[0] <= High[i])
                    isHighest = false;
                if (Low[0] >= Low[i])
                    isLowest = false;
            }

            // Determine new leg direction
            double newLeg = currentLeg;
            if (isHighest)
                newLeg = HIGH;
            else if (isLowest)
                newLeg = LOW;

            // Detect leg change = pivot confirmation
            bool legChanged = (newLeg != currentLeg) && (currentLeg != -1);

            if (legChanged)
            {
                if (newLeg == HIGH)  // Changed to HIGH leg = LOW pivot confirmed
                {
                    var lowPivot = new SwingPoint(currentPivotIndex, currentPivotLow, LOW);
                    swingPoints.Add(lowPivot);
                }
                else  // Changed to LOW leg = HIGH pivot confirmed
                {
                    var highPivot = new SwingPoint(currentPivotIndex, currentPivotHigh, HIGH);
                    swingPoints.Add(highPivot);
                }

                // Keep list manageable
                while (swingPoints.Count > MAX_SWING_POINTS)
                    swingPoints.RemoveAt(0);
            }

            // Update tracking for current leg
            if (newLeg == HIGH)
            {
                if (High[0] >= currentPivotHigh || currentLeg != HIGH)
                {
                    currentPivotHigh = High[0];
                    currentPivotIndex = CurrentBar;
                }
            }
            else if (newLeg == LOW)
            {
                if (Low[0] <= currentPivotLow || currentLeg != LOW)
                {
                    currentPivotLow = Low[0];
                    currentPivotIndex = CurrentBar;
                }
            }

            swingLeg[0] = newLeg;
            pivotHighPrice[0] = currentPivotHigh;
            pivotLowPrice[0] = currentPivotLow;
            pivotIndex[0] = currentPivotIndex;
        }
        #endregion

        #region Pattern Detection
        /// <summary>
        /// Detect 5-point reversal pattern (LuxAlgo)
        /// Bullish: point1=HIGH, price containment conditions met
        /// Bearish: point1=LOW, price containment conditions met
        /// </summary>
        private int Detect5PointReversal()
        {
            if (swingPoints.Count < 5)
                return 0;

            var point1 = swingPoints[swingPoints.Count - 5];
            var point2 = swingPoints[swingPoints.Count - 4];
            var point3 = swingPoints[swingPoints.Count - 3];
            var point4 = swingPoints[swingPoints.Count - 2];
            var point5 = swingPoints[swingPoints.Count - 1];

            // Bullish reversal: point1=HIGH and containment conditions
            bool bullishReversal = (
                point1.SwingType == HIGH &&
                PointInsideRange(point3, point1, point2) &&
                PointInsideRange(point2, point3, point4) &&
                PointInsideRange(point3, point5, point4)
            );

            // Bearish reversal: point1=LOW and containment conditions
            bool bearishReversal = (
                point1.SwingType == LOW &&
                PointInsideRange(point3, point2, point1) &&
                PointInsideRange(point2, point4, point3) &&
                PointInsideRange(point3, point4, point5)
            );

            if (bullishReversal)
            {
                currentTrendBias = 1;
                return 1;
            }
            else if (bearishReversal)
            {
                currentTrendBias = -1;
                return -1;
            }

            return 0;
        }

        /// <summary>
        /// Detect 4-point trend continuation pattern (LuxAlgo)
        /// </summary>
        private int Detect4PointContinuation()
        {
            if (swingPoints.Count < 4)
                return 0;

            var point1 = swingPoints[swingPoints.Count - 4];
            var point2 = swingPoints[swingPoints.Count - 3];
            var point3 = swingPoints[swingPoints.Count - 2];
            var point4 = swingPoints[swingPoints.Count - 1];

            // Bullish trend: point1=HIGH, point1 inside (point3, point2), point4 inside (point3, point2)
            bool bullishTrend = (
                point1.SwingType == HIGH &&
                PointInsideRange(point1, point3, point2) &&
                PointInsideRange(point4, point3, point2)
            );

            // Bearish trend: point1=LOW, point1 inside (point2, point3), point4 inside (point2, point3)
            bool bearishTrend = (
                point1.SwingType == LOW &&
                PointInsideRange(point1, point2, point3) &&
                PointInsideRange(point4, point2, point3)
            );

            if (bullishTrend)
            {
                currentTrendBias = 1;
                return 1;
            }
            else if (bearishTrend)
            {
                currentTrendBias = -1;
                return -1;
            }

            return 0;
        }

        /// <summary>
        /// Check if point is inside the range defined by top and bottom
        /// </summary>
        private bool PointInsideRange(SwingPoint point, SwingPoint top, SwingPoint bottom)
        {
            double topPrice = Math.Max(top.PriceLevel, bottom.PriceLevel);
            double bottomPrice = Math.Min(top.PriceLevel, bottom.PriceLevel);
            return point.PriceLevel <= topPrice && point.PriceLevel >= bottomPrice;
        }
        #endregion

        #region Fibonacci Zones
        /// <summary>
        /// Calculate Fibonacci retracement zone (0.5 and 0.618 levels)
        /// </summary>
        private void CalculateFibonacciZone(bool isBullish)
        {
            if (swingPoints.Count < 2)
                return;

            double maxPrice = double.MinValue;
            double minPrice = double.MaxValue;

            for (int i = Math.Max(0, swingPoints.Count - 5); i < swingPoints.Count; i++)
            {
                maxPrice = Math.Max(maxPrice, swingPoints[i].PriceLevel);
                minPrice = Math.Min(minPrice, swingPoints[i].PriceLevel);
            }

            double priceRange = maxPrice - minPrice;

            if (isBullish)
            {
                // Bullish: looking for pullback into fib zone from below
                fibTop = maxPrice - 0.5 * priceRange;
                fibBottom = maxPrice - 0.618 * priceRange;
            }
            else
            {
                // Bearish: looking for pullback into fib zone from above
                fibBottom = minPrice + 0.5 * priceRange;
                fibTop = minPrice + 0.618 * priceRange;
            }

            inRetracementZone = true;
        }

        /// <summary>
        /// Check if price is in Fibonacci zone for entry trigger
        /// </summary>
        private int CheckFibonacciEntry()
        {
            if (!inRetracementZone)
                return 0;

            double price = Close[0];

            if (price >= fibBottom && price <= fibTop)
            {
                if (currentTrendBias == 1)  // Bullish
                {
                    inRetracementZone = false;  // Reset after entry
                    return 1;
                }
                else if (currentTrendBias == -1)  // Bearish
                {
                    inRetracementZone = false;
                    return -1;
                }
            }

            return 0;
        }
        #endregion

        #region Properties
        [NinjaScriptProperty]
        [Range(2, int.MaxValue)]
        [Display(Name = "Pivot Length", Description = "Number of candles to confirm swing high/low", Order = 1, GroupName = "Parameters")]
        public int PivotLength { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 2.0)]
        [Display(Name = "Reversal Area Threshold", Description = "ATR multiplier for reversal zone", Order = 2, GroupName = "Parameters")]
        public double ReversalAreaThreshold { get; set; }

        // Public accessors for strategies
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> SwingLeg => swingLeg;

        [Browsable(false)]
        [XmlIgnore]
        public List<SwingPoint> SwingPoints => swingPoints;

        /// <summary>
        /// Returns the last detected pattern type
        /// </summary>
        public PatternType LastPattern => lastPattern;

        /// <summary>
        /// Returns current trend bias: 1=bullish, -1=bearish, 0=neutral
        /// </summary>
        public int TrendBias => currentTrendBias;

        /// <summary>
        /// Returns true if currently in HIGH leg (uptrend)
        /// </summary>
        public bool InUpLeg => swingLeg[0] == HIGH;

        /// <summary>
        /// Returns true if currently in LOW leg (downtrend)
        /// </summary>
        public bool InDownLeg => swingLeg[0] == LOW;

        /// <summary>
        /// Returns true if price is in Fibonacci retracement zone
        /// </summary>
        public bool InFibZone => inRetracementZone;

        /// <summary>
        /// Returns Fibonacci zone top price
        /// </summary>
        public double FibTop => fibTop;

        /// <summary>
        /// Returns Fibonacci zone bottom price
        /// </summary>
        public double FibBottom => fibBottom;

        /// <summary>
        /// Returns true if bullish reversal or continuation detected
        /// </summary>
        public bool BullishSignal => (int)lastPattern > 0;

        /// <summary>
        /// Returns true if bearish reversal or continuation detected
        /// </summary>
        public bool BearishSignal => (int)lastPattern < 0;

        /// <summary>
        /// Returns 1 for bullish, -1 for bearish, 0 for no signal
        /// </summary>
        public int Signal => lastPattern == PatternType.None ? 0 : ((int)lastPattern > 0 ? 1 : -1);
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.
namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
    {
        private TrendingToolkitIndicator[] cacheTrendingToolkitIndicator;
        public TrendingToolkitIndicator TrendingToolkitIndicator(int pivotLength, double reversalAreaThreshold)
        {
            return TrendingToolkitIndicator(Input, pivotLength, reversalAreaThreshold);
        }

        public TrendingToolkitIndicator TrendingToolkitIndicator(ISeries<double> input, int pivotLength, double reversalAreaThreshold)
        {
            if (cacheTrendingToolkitIndicator != null)
                for (int idx = 0; idx < cacheTrendingToolkitIndicator.Length; idx++)
                    if (cacheTrendingToolkitIndicator[idx] != null && cacheTrendingToolkitIndicator[idx].PivotLength == pivotLength && cacheTrendingToolkitIndicator[idx].ReversalAreaThreshold == reversalAreaThreshold && cacheTrendingToolkitIndicator[idx].EqualsInput(input))
                        return cacheTrendingToolkitIndicator[idx];
            return CacheIndicator<TrendingToolkitIndicator>(new TrendingToolkitIndicator() { PivotLength = pivotLength, ReversalAreaThreshold = reversalAreaThreshold }, input, ref cacheTrendingToolkitIndicator);
        }
    }
}

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
    {
        public Indicators.TrendingToolkitIndicator TrendingToolkitIndicator(int pivotLength, double reversalAreaThreshold)
        {
            return indicator.TrendingToolkitIndicator(Input, pivotLength, reversalAreaThreshold);
        }

        public Indicators.TrendingToolkitIndicator TrendingToolkitIndicator(ISeries<double> input, int pivotLength, double reversalAreaThreshold)
        {
            return indicator.TrendingToolkitIndicator(input, pivotLength, reversalAreaThreshold);
        }
    }
}
#endregion
