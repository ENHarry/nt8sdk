#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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
    /// SuperTrend Indicator - Ported from TradingView Pine Script
    /// https://www.tradingview.com/script/r6dAP7yi/
    /// 
    /// Calculation:
    /// - Upper/Lower bands based on HL2 +/- (multiplier * ATR)
    /// - Trend flips when price crosses opposing band
    /// - Direction: 1 = uptrend (bullish), -1 = downtrend (bearish)
    /// </summary>
    public class SuperTrendIndicator : Indicator
    {
        #region Variables
        private ATR atr;
        private Series<double> upperBand;
        private Series<double> lowerBand;
        private Series<int> direction;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "SuperTrend Indicator - Pine Script Port";
                Name = "SuperTrendIndicator";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                PaintPriceMarkers = true;
                
                // Default parameters matching Pine Script
                Period = 10;
                Multiplier = 3.0;
                
                AddPlot(Brushes.Green, "UpperBand");
                AddPlot(Brushes.Red, "LowerBand");
                AddPlot(Brushes.Gray, "Direction");
            }
            else if (State == State.DataLoaded)
            {
                atr = ATR(Period);
                upperBand = new Series<double>(this);
                lowerBand = new Series<double>(this);
                direction = new Series<int>(this);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Period + 1)
            {
                upperBand[0] = 0;
                lowerBand[0] = 0;
                direction[0] = 0;
                Values[0][0] = 0;
                Values[1][0] = 0;
                Values[2][0] = 0;
                return;
            }

            double atrValue = atr[0];
            double hl2 = (High[0] + Low[0]) / 2.0;

            // Basic bands: Pine Script formula
            // up = src - (Multiplier * atr)
            // dn = src + (Multiplier * atr)
            double basicUpper = hl2 - (Multiplier * atrValue);
            double basicLower = hl2 + (Multiplier * atrValue);

            double prevUpper = upperBand[1];
            double prevLower = lowerBand[1];
            int prevDirection = direction[1];

            // Pine Script logic:
            // up := close[1] > up1 ? max(up, up1) : up
            double newUpper;
            if (Close[1] > prevUpper && prevUpper != 0)
            {
                newUpper = Math.Max(basicUpper, prevUpper);
            }
            else
            {
                newUpper = basicUpper;
            }

            // dn := close[1] < dn1 ? min(dn, dn1) : dn
            double newLower;
            if (Close[1] < prevLower && prevLower != 0)
            {
                newLower = Math.Min(basicLower, prevLower);
            }
            else
            {
                newLower = basicLower;
            }

            // Determine trend direction
            // trend := trend == -1 and close > dn1 ? 1 : trend == 1 and close < up1 ? -1 : trend
            int newDirection;
            if (prevDirection == -1 && Close[0] > prevLower)
            {
                newDirection = 1;  // Switch to uptrend
            }
            else if (prevDirection == 1 && Close[0] < prevUpper)
            {
                newDirection = -1;  // Switch to downtrend
            }
            else if (prevDirection == 0)
            {
                newDirection = Close[0] > hl2 ? 1 : -1;  // Initialize
            }
            else
            {
                newDirection = prevDirection;  // Continue current trend
            }

            upperBand[0] = newUpper;
            lowerBand[0] = newLower;
            direction[0] = newDirection;

            // Plot values
            Values[0][0] = newDirection == 1 ? newUpper : double.NaN;  // Show upper in uptrend
            Values[1][0] = newDirection == -1 ? newLower : double.NaN; // Show lower in downtrend
            Values[2][0] = newDirection;
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ATR Period", Order = 1, GroupName = "Parameters")]
        public int Period { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10.0)]
        [Display(Name = "Multiplier", Order = 2, GroupName = "Parameters")]
        public double Multiplier { get; set; }

        // Public accessors for use in strategies
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Upper => upperBand;

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Lower => lowerBand;

        [Browsable(false)]
        [XmlIgnore]
        public Series<int> Direction => direction;

        /// <summary>
        /// Returns true if trend just flipped to bullish
        /// </summary>
        public bool BuySignal => CurrentBar > 0 && direction[0] == 1 && direction[1] == -1;

        /// <summary>
        /// Returns true if trend just flipped to bearish
        /// </summary>
        public bool SellSignal => CurrentBar > 0 && direction[0] == -1 && direction[1] == 1;

        /// <summary>
        /// Returns true if currently in uptrend
        /// </summary>
        public bool IsUptrend => direction[0] == 1;

        /// <summary>
        /// Returns true if currently in downtrend
        /// </summary>
        public bool IsDowntrend => direction[0] == -1;
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.
namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
    {
        private SuperTrendIndicator[] cacheSuperTrendIndicator;
        public SuperTrendIndicator SuperTrendIndicator(int period, double multiplier)
        {
            return SuperTrendIndicator(Input, period, multiplier);
        }

        public SuperTrendIndicator SuperTrendIndicator(ISeries<double> input, int period, double multiplier)
        {
            if (cacheSuperTrendIndicator != null)
                for (int idx = 0; idx < cacheSuperTrendIndicator.Length; idx++)
                    if (cacheSuperTrendIndicator[idx] != null && cacheSuperTrendIndicator[idx].Period == period && cacheSuperTrendIndicator[idx].Multiplier == multiplier && cacheSuperTrendIndicator[idx].EqualsInput(input))
                        return cacheSuperTrendIndicator[idx];
            return CacheIndicator<SuperTrendIndicator>(new SuperTrendIndicator() { Period = period, Multiplier = multiplier }, input, ref cacheSuperTrendIndicator);
        }
    }
}

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
    {
        public Indicators.SuperTrendIndicator SuperTrendIndicator(int period, double multiplier)
        {
            return indicator.SuperTrendIndicator(Input, period, multiplier);
        }

        public Indicators.SuperTrendIndicator SuperTrendIndicator(ISeries<double> input, int period, double multiplier)
        {
            return indicator.SuperTrendIndicator(input, period, multiplier);
        }
    }
}
#endregion
