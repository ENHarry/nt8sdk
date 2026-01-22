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
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// Price Structure Indicator - Detects Break of Structure (BOS) and Change of Character (ChoCH)
    /// 
    /// Tracks swing highs/lows to identify:
    /// - Higher Highs (HH) / Higher Lows (HL) = Bullish Structure
    /// - Lower Highs (LH) / Lower Lows (LL) = Bearish Structure
    /// - Break of Structure (BOS): Continuation break in direction of trend
    /// - Change of Character (ChoCH): Structure break against trend direction
    /// 
    /// Signals:
    /// +1 = Bullish BOS (continuation of uptrend)
    /// -1 = Bearish BOS (continuation of downtrend)
    /// +2 = Bullish ChoCH (bearish structure broken, potential reversal up)
    /// -2 = Bearish ChoCH (bullish structure broken, potential reversal down)
    /// </summary>
    public class PriceStructureIndicator : Indicator
    {
        #region Enums
        
        public enum StructureType
        {
            Unknown = 0,
            Bullish = 1,    // HH + HL sequence
            Bearish = -1    // LH + LL sequence
        }
        
        public enum SignalType
        {
            None = 0,
            BullishBOS = 1,
            BearishBOS = -1,
            BullishChoCH = 2,
            BearishChoCH = -2
        }
        
        #endregion
        
        #region Variables
        
        // Swing tracking
        private List<double> swingHighs;
        private List<double> swingLows;
        private List<int> swingHighBars;
        private List<int> swingLowBars;
        
        // Current swing levels
        private double currentSwingHigh;
        private double currentSwingLow;
        private double prevSwingHigh;
        private double prevSwingLow;
        
        // Structure tracking
        private StructureType currentStructure;
        private StructureType prevStructure;
        
        // Signal state
        private bool isBOS;
        private bool isChoCH;
        private SignalType currentSignal;
        
        // For pivot detection
        private int pivotStrength;
        
        #endregion
        
        #region Properties
        
        [NinjaScriptProperty]
        [Range(2, 50)]
        [Display(Name = "Swing Lookback", Description = "Bars to look back for swing point confirmation", Order = 1, GroupName = "Parameters")]
        public int SwingLookback { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Draw Swing Points", Description = "Draw markers at swing highs/lows", Order = 2, GroupName = "Display")]
        public bool DrawSwingPoints { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Draw Structure Labels", Description = "Draw BOS/ChoCH labels on chart", Order = 3, GroupName = "Display")]
        public bool DrawStructureLabels { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Draw Structure Lines", Description = "Draw lines connecting structure levels", Order = 4, GroupName = "Display")]
        public bool DrawStructureLines { get; set; }
        
        // Output Series
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> StructureSignal { get { return Values[0]; } }
        
        // Public accessors
        [Browsable(false)]
        [XmlIgnore]
        public bool IsBullishStructure { get { return currentStructure == StructureType.Bullish; } }
        
        [Browsable(false)]
        [XmlIgnore]
        public bool IsBearishStructure { get { return currentStructure == StructureType.Bearish; } }
        
        [Browsable(false)]
        [XmlIgnore]
        public bool IsBOS { get { return isBOS; } }
        
        [Browsable(false)]
        [XmlIgnore]
        public bool IsChoCH { get { return isChoCH; } }
        
        [Browsable(false)]
        [XmlIgnore]
        public double LastSwingHigh { get { return currentSwingHigh; } }
        
        [Browsable(false)]
        [XmlIgnore]
        public double LastSwingLow { get { return currentSwingLow; } }
        
        [Browsable(false)]
        [XmlIgnore]
        public SignalType CurrentSignalType { get { return currentSignal; } }
        
        [Browsable(false)]
        [XmlIgnore]
        public StructureType CurrentStructure { get { return currentStructure; } }
        
        #endregion
        
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Price Structure Indicator - Detects BOS (Break of Structure) and ChoCH (Change of Character)";
                Name = "PriceStructure";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                
                // Default parameters
                SwingLookback = 5;
                DrawSwingPoints = true;
                DrawStructureLabels = true;
                DrawStructureLines = false;
                
                AddPlot(new Stroke(Brushes.Transparent), PlotStyle.Line, "StructureSignal");
            }
            else if (State == State.DataLoaded)
            {
                swingHighs = new List<double>();
                swingLows = new List<double>();
                swingHighBars = new List<int>();
                swingLowBars = new List<int>();
                
                currentSwingHigh = 0;
                currentSwingLow = 0;
                prevSwingHigh = 0;
                prevSwingLow = 0;
                
                currentStructure = StructureType.Unknown;
                prevStructure = StructureType.Unknown;
                
                pivotStrength = SwingLookback;
            }
        }
        
        protected override void OnBarUpdate()
        {
            if (CurrentBar < SwingLookback * 2)
            {
                StructureSignal[0] = 0;
                return;
            }
            
            // Reset per-bar signals
            isBOS = false;
            isChoCH = false;
            currentSignal = SignalType.None;
            
            // Detect swing points
            DetectSwingPoints();
            
            // Analyze structure
            AnalyzeStructure();
            
            // Detect BOS/ChoCH
            DetectStructureBreaks();
            
            // Output signal
            StructureSignal[0] = (int)currentSignal;
        }
        
        #region Swing Detection
        
        private void DetectSwingPoints()
        {
            int lookback = SwingLookback;
            
            // Check for swing high (pivot high) - need lookback bars on each side
            if (CurrentBar >= lookback * 2)
            {
                bool isSwingHigh = true;
                double pivotHigh = High[lookback];
                
                // Check bars before pivot
                for (int i = 1; i <= lookback; i++)
                {
                    if (High[lookback + i] >= pivotHigh)
                    {
                        isSwingHigh = false;
                        break;
                    }
                }
                
                // Check bars after pivot
                if (isSwingHigh)
                {
                    for (int i = 1; i <= lookback; i++)
                    {
                        if (High[lookback - i] > pivotHigh)
                        {
                            isSwingHigh = false;
                            break;
                        }
                    }
                }
                
                if (isSwingHigh)
                {
                    int pivotBar = CurrentBar - lookback;
                    
                    // Avoid duplicates
                    if (swingHighBars.Count == 0 || swingHighBars.Last() != pivotBar)
                    {
                        prevSwingHigh = currentSwingHigh;
                        currentSwingHigh = pivotHigh;
                        swingHighs.Add(pivotHigh);
                        swingHighBars.Add(pivotBar);
                        
                        // Keep only recent swings (last 20)
                        if (swingHighs.Count > 20)
                        {
                            swingHighs.RemoveAt(0);
                            swingHighBars.RemoveAt(0);
                        }
                        
                        if (DrawSwingPoints)
                        {
                            Draw.Dot(this, "SH_" + pivotBar, true, lookback, pivotHigh, Brushes.Red);
                        }
                    }
                }
            }
            
            // Check for swing low (pivot low)
            if (CurrentBar >= lookback * 2)
            {
                bool isSwingLow = true;
                double pivotLow = Low[lookback];
                
                // Check bars before pivot
                for (int i = 1; i <= lookback; i++)
                {
                    if (Low[lookback + i] <= pivotLow)
                    {
                        isSwingLow = false;
                        break;
                    }
                }
                
                // Check bars after pivot
                if (isSwingLow)
                {
                    for (int i = 1; i <= lookback; i++)
                    {
                        if (Low[lookback - i] < pivotLow)
                        {
                            isSwingLow = false;
                            break;
                        }
                    }
                }
                
                if (isSwingLow)
                {
                    int pivotBar = CurrentBar - lookback;
                    
                    // Avoid duplicates
                    if (swingLowBars.Count == 0 || swingLowBars.Last() != pivotBar)
                    {
                        prevSwingLow = currentSwingLow;
                        currentSwingLow = pivotLow;
                        swingLows.Add(pivotLow);
                        swingLowBars.Add(pivotBar);
                        
                        // Keep only recent swings
                        if (swingLows.Count > 20)
                        {
                            swingLows.RemoveAt(0);
                            swingLowBars.RemoveAt(0);
                        }
                        
                        if (DrawSwingPoints)
                        {
                            Draw.Dot(this, "SL_" + pivotBar, true, lookback, pivotLow, Brushes.Lime);
                        }
                    }
                }
            }
        }
        
        #endregion
        
        #region Structure Analysis
        
        private void AnalyzeStructure()
        {
            if (swingHighs.Count < 2 || swingLows.Count < 2)
            {
                currentStructure = StructureType.Unknown;
                return;
            }
            
            // Get last two swing highs and lows
            double lastHigh = swingHighs[swingHighs.Count - 1];
            double prevHigh = swingHighs[swingHighs.Count - 2];
            double lastLow = swingLows[swingLows.Count - 1];
            double prevLow = swingLows[swingLows.Count - 2];
            
            prevStructure = currentStructure;
            
            // Determine structure
            bool higherHigh = lastHigh > prevHigh;
            bool higherLow = lastLow > prevLow;
            bool lowerHigh = lastHigh < prevHigh;
            bool lowerLow = lastLow < prevLow;
            
            // Bullish structure: HH + HL
            if (higherHigh && higherLow)
            {
                currentStructure = StructureType.Bullish;
            }
            // Bearish structure: LH + LL
            else if (lowerHigh && lowerLow)
            {
                currentStructure = StructureType.Bearish;
            }
            // Mixed/transition - keep previous or mark unknown
            else if (higherHigh && lowerLow)
            {
                // Could be transitioning - keep previous for now
                // This is often seen at reversals
            }
            else if (lowerHigh && higherLow)
            {
                // Consolidation/ranging
                currentStructure = StructureType.Unknown;
            }
        }
        
        #endregion
        
        #region BOS/ChoCH Detection
        
        private void DetectStructureBreaks()
        {
            if (swingHighs.Count < 2 || swingLows.Count < 2)
                return;
            
            double lastSwingHigh = swingHighs[swingHighs.Count - 1];
            double lastSwingLow = swingLows[swingLows.Count - 1];
            
            // Check for Break of Structure (BOS) - continuation
            // Bullish BOS: In bullish structure, price breaks above last swing high
            if (currentStructure == StructureType.Bullish || prevStructure == StructureType.Bullish)
            {
                if (Close[0] > lastSwingHigh && Close[1] <= lastSwingHigh)
                {
                    // This is a bullish continuation BOS
                    isBOS = true;
                    currentSignal = SignalType.BullishBOS;
                    
                    if (DrawStructureLabels)
                    {
                        Draw.Text(this, "BOS_" + CurrentBar, "BOS↑", 0, Low[0] - (2 * TickSize * 10), Brushes.Lime);
                    }
                    
                    Print($"PriceStructure: Bullish BOS at {Time[0]} - Price broke above swing high {lastSwingHigh:F2}");
                }
            }
            
            // Bearish BOS: In bearish structure, price breaks below last swing low
            if (currentStructure == StructureType.Bearish || prevStructure == StructureType.Bearish)
            {
                if (Close[0] < lastSwingLow && Close[1] >= lastSwingLow)
                {
                    // This is a bearish continuation BOS
                    isBOS = true;
                    currentSignal = SignalType.BearishBOS;
                    
                    if (DrawStructureLabels)
                    {
                        Draw.Text(this, "BOS_" + CurrentBar, "BOS↓", 0, High[0] + (2 * TickSize * 10), Brushes.Red);
                    }
                    
                    Print($"PriceStructure: Bearish BOS at {Time[0]} - Price broke below swing low {lastSwingLow:F2}");
                }
            }
            
            // Check for Change of Character (ChoCH) - reversal
            // Bullish ChoCH: Was in bearish structure (LH+LL), now breaks above last swing HIGH
            if ((prevStructure == StructureType.Bearish || currentStructure == StructureType.Bearish) && !isBOS)
            {
                if (Close[0] > lastSwingHigh && Close[1] <= lastSwingHigh)
                {
                    // Breaking swing high in bearish structure = bullish ChoCH
                    isChoCH = true;
                    currentSignal = SignalType.BullishChoCH;
                    currentStructure = StructureType.Bullish;  // Structure has changed
                    
                    if (DrawStructureLabels)
                    {
                        Draw.Text(this, "ChoCH_" + CurrentBar, "ChoCH↑", 0, Low[0] - (3 * TickSize * 10), Brushes.Cyan);
                    }
                    
                    Print($"PriceStructure: BULLISH ChoCH at {Time[0]} - Bearish structure broken! Price: {Close[0]:F2} > SwingHigh: {lastSwingHigh:F2}");
                }
            }
            
            // Bearish ChoCH: Was in bullish structure (HH+HL), now breaks below last swing LOW
            if ((prevStructure == StructureType.Bullish || currentStructure == StructureType.Bullish) && !isBOS)
            {
                if (Close[0] < lastSwingLow && Close[1] >= lastSwingLow)
                {
                    // Breaking swing low in bullish structure = bearish ChoCH
                    isChoCH = true;
                    currentSignal = SignalType.BearishChoCH;
                    currentStructure = StructureType.Bearish;  // Structure has changed
                    
                    if (DrawStructureLabels)
                    {
                        Draw.Text(this, "ChoCH_" + CurrentBar, "ChoCH↓", 0, High[0] + (3 * TickSize * 10), Brushes.Magenta);
                    }
                    
                    Print($"PriceStructure: BEARISH ChoCH at {Time[0]} - Bullish structure broken! Price: {Close[0]:F2} < SwingLow: {lastSwingLow:F2}");
                }
            }
        }
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// Check if price is near a key structure level
        /// </summary>
        public bool IsNearStructureLevel(double price, double toleranceATR)
        {
            if (currentSwingHigh > 0 && Math.Abs(price - currentSwingHigh) <= toleranceATR)
                return true;
            if (currentSwingLow > 0 && Math.Abs(price - currentSwingLow) <= toleranceATR)
                return true;
            return false;
        }
        
        /// <summary>
        /// Get the nearest structure level (support or resistance)
        /// </summary>
        public double GetNearestStructureLevel(double price)
        {
            double distToHigh = currentSwingHigh > 0 ? Math.Abs(price - currentSwingHigh) : double.MaxValue;
            double distToLow = currentSwingLow > 0 ? Math.Abs(price - currentSwingLow) : double.MaxValue;
            
            return distToHigh < distToLow ? currentSwingHigh : currentSwingLow;
        }
        
        /// <summary>
        /// Check if we have a valid sequence of higher highs and higher lows
        /// </summary>
        public bool HasHigherHighHigherLow(int count = 2)
        {
            if (swingHighs.Count < count || swingLows.Count < count)
                return false;
            
            // Check if last 'count' swing highs are ascending
            for (int i = swingHighs.Count - count; i < swingHighs.Count - 1; i++)
            {
                if (swingHighs[i + 1] <= swingHighs[i])
                    return false;
            }
            
            // Check if last 'count' swing lows are ascending
            for (int i = swingLows.Count - count; i < swingLows.Count - 1; i++)
            {
                if (swingLows[i + 1] <= swingLows[i])
                    return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// Check if we have a valid sequence of lower highs and lower lows
        /// </summary>
        public bool HasLowerHighLowerLow(int count = 2)
        {
            if (swingHighs.Count < count || swingLows.Count < count)
                return false;
            
            // Check if last 'count' swing highs are descending
            for (int i = swingHighs.Count - count; i < swingHighs.Count - 1; i++)
            {
                if (swingHighs[i + 1] >= swingHighs[i])
                    return false;
            }
            
            // Check if last 'count' swing lows are descending
            for (int i = swingLows.Count - count; i < swingLows.Count - 1; i++)
            {
                if (swingLows[i + 1] >= swingLows[i])
                    return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// Get structure strength based on consecutive HH/HL or LH/LL count
        /// </summary>
        public int GetStructureStrength()
        {
            int strength = 0;
            
            if (currentStructure == StructureType.Bullish)
            {
                // Count consecutive higher highs
                for (int i = swingHighs.Count - 1; i > 0; i--)
                {
                    if (swingHighs[i] > swingHighs[i - 1])
                        strength++;
                    else
                        break;
                }
            }
            else if (currentStructure == StructureType.Bearish)
            {
                // Count consecutive lower lows
                for (int i = swingLows.Count - 1; i > 0; i--)
                {
                    if (swingLows[i] < swingLows[i - 1])
                        strength++;
                    else
                        break;
                }
            }
            
            return strength;
        }
        
        #endregion
    }
}
