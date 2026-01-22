#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// Overbought/Oversold Indicator - Ported from TradingView Pine Script
    /// © ceyhun
    /// 
    /// Calculation:
    /// - ys1 = (high + low + close * 2) / 4  (weighted close)
    /// - rk3 = ema(ys1, n)
    /// - rk4 = stdev(ys1, n)
    /// - rk5 = (ys1 - rk3) * 100 / rk4
    /// - rk6 = ema(rk5, n)
    /// - up = ema(rk6, n)
    /// - down = ema(up, n)
    /// - Histogram: Oo (min), Ll (max)
    /// - Lines: up, down
    /// </summary>
    public class OverboughtOversoldIndicator : Indicator
    {
        #region Variables
        private Series<double> ys1;      // Weighted price
        private EMA emaYs1;              // EMA of ys1 (rk3)
        private StdDev stdDevYs1;        // StdDev of ys1 (rk4)
        private Series<double> rk5;      // Normalized value
        private EMA emaRk5;              // EMA of rk5 (rk6)
        private EMA emaRk6;              // EMA of rk6 (up)
        private EMA emaUp;               // EMA of up (down)
        
        private Series<double> upLine;   // Up line
        private Series<double> downLine; // Down line
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Overbought/Oversold Indicator - Pine Script Port © ceyhun";
                Name = "OverboughtOversoldIndicator";
                Calculate = Calculate.OnBarClose;
                IsOverlay = false;
                DisplayInDataBox = true;
                PaintPriceMarkers = false;
                
                // Default parameters
                Length = 5;
                
                // Plots matching Pine Script visual representation
                // Histogram plots (Oo and Ll)
                AddPlot(new Stroke(Brushes.Green, 2), PlotStyle.Bar, "Oo");  // Min histogram
                AddPlot(new Stroke(Brushes.Green, 2), PlotStyle.Bar, "Ll");  // Max histogram
                // Line plots (up and down)
                AddPlot(new Stroke(Brushes.Green, 2), PlotStyle.Line, "Up");
                AddPlot(new Stroke(Brushes.Red, 2), PlotStyle.Line, "Down");
                
                // Zero line
                AddLine(Brushes.Gray, 0, "Zero");
            }
            else if (State == State.DataLoaded)
            {
                ys1 = new Series<double>(this);
                rk5 = new Series<double>(this);
                upLine = new Series<double>(this);
                downLine = new Series<double>(this);
                
                // Initialize NT8 indicators - we'll apply to ys1 series
                // Note: We calculate ys1 first, then apply EMA/StdDev to it
            }
        }

        protected override void OnBarUpdate()
        {
            // Pine Script: ys1 = (high + low + close * 2) / 4
            ys1[0] = (High[0] + Low[0] + Close[0] * 2) / 4;
            
            if (CurrentBar < Length)
            {
                rk5[0] = 0;
                upLine[0] = 0;
                downLine[0] = 0;
                Values[0][0] = 0;  // Oo histogram
                Values[1][0] = 0;  // Ll histogram
                Values[2][0] = 0;  // Up line
                Values[3][0] = 0;  // Down line
                return;
            }

            // Pine Script: rk3 = ema(ys1, n)
            double alpha = 2.0 / (Length + 1);
            double rk3 = CalculateEMA(ys1, Length, 0);

            // Pine Script: rk4 = stdev(ys1, n) - using NT8's StdDev calculation method
            double rk4 = CalculateStdDev(ys1, Length);

            // Pine Script: rk5 = (ys1 - rk3) * 100 / rk4
            if (Math.Abs(rk4) > double.Epsilon)
            {
                rk5[0] = (ys1[0] - rk3) * 100 / rk4;
            }
            else
            {
                rk5[0] = 0;
            }

            // Pine Script: rk6 = ema(rk5, n)
            double rk6 = CalculateEMA(rk5, Length, 0);

            // Pine Script: up = ema(rk6, n)
            // For cascaded EMAs, we need to track them properly
            if (CurrentBar == Length)
            {
                upLine[0] = rk6;
                downLine[0] = rk6;
            }
            else
            {
                // up = ema(rk6, n) - but we need running EMA
                upLine[0] = alpha * rk6 + (1 - alpha) * upLine[1];
                
                // Pine Script: down = ema(up, n)
                downLine[0] = alpha * upLine[0] + (1 - alpha) * downLine[1];
            }

            // Pine Script:
            // Oo = iff(up < down, up, down)  -> Min
            // Ll = iff(up < down, down, up)  -> Max
            double Oo = Math.Min(upLine[0], downLine[0]);
            double Ll = Math.Max(upLine[0], downLine[0]);
            
            // Set plot values
            Values[0][0] = Oo;           // Oo histogram
            Values[1][0] = Ll;           // Ll histogram
            Values[2][0] = upLine[0];    // Up line
            Values[3][0] = downLine[0];  // Down line
            
            // Pine Script color logic:
            // b_color = iff(Oo[1] < Oo and Cc < Cc[1], #FFFF00, iff(up > down, #008000, #FF0000))
            Brush plotColor;
            if (CurrentBar > 0)
            {
                double Oo1 = Math.Min(upLine[1], downLine[1]);
                double Cc = Ll;  // Cc = Ll in Pine
                double Cc1 = Math.Max(upLine[1], downLine[1]);
                
                if (Oo1 < Oo && Cc < Cc1)
                {
                    plotColor = Brushes.Yellow;  // Reversal zone
                }
                else if (upLine[0] > downLine[0])
                {
                    plotColor = Brushes.Green;   // Bullish
                }
                else
                {
                    plotColor = Brushes.Red;     // Bearish
                }
            }
            else
            {
                plotColor = upLine[0] > downLine[0] ? Brushes.Green : Brushes.Red;
            }
            
            // Apply color to all plots
            PlotBrushes[0][0] = plotColor;  // Oo histogram
            PlotBrushes[1][0] = plotColor;  // Ll histogram
            PlotBrushes[2][0] = plotColor;  // Up line
            PlotBrushes[3][0] = plotColor;  // Down line
        }
        
        /// <summary>
        /// Calculate EMA using NT8's standard formula
        /// </summary>
        private double CalculateEMA(Series<double> series, int period, int barsAgo)
        {
            double alpha = 2.0 / (period + 1);
            double ema = series[barsAgo];
            
            int lookback = Math.Min(CurrentBar, period * 3);  // Use enough bars for convergence
            for (int i = lookback; i >= barsAgo; i--)
            {
                if (i == lookback)
                    ema = series[i];
                else
                    ema = alpha * series[i] + (1 - alpha) * ema;
            }
            return ema;
        }
        
        /// <summary>
        /// Calculate StdDev using NT8's standard formula (population std dev)
        /// </summary>
        private double CalculateStdDev(Series<double> series, int period)
        {
            int count = Math.Min(CurrentBar + 1, period);
            double sum = 0;
            
            for (int i = 0; i < count; i++)
                sum += series[i];
            
            double avg = sum / count;
            double sumSq = 0;
            
            for (int i = 0; i < count; i++)
                sumSq += (series[i] - avg) * (series[i] - avg);
            
            return Math.Sqrt(sumSq / count);
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Length", Order = 1, GroupName = "Parameters")]
        public int Length { get; set; }

        // Public accessors for strategies
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Up => upLine;

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Down => downLine;

        /// <summary>
        /// Returns true on bullish crossover (up crosses above down)
        /// </summary>
        public bool BuySignal => CurrentBar > 0 && upLine[0] > downLine[0] && upLine[1] <= downLine[1];

        /// <summary>
        /// Returns true on bearish crossunder (up crosses below down)
        /// </summary>
        public bool SellSignal => CurrentBar > 0 && upLine[0] < downLine[0] && upLine[1] >= downLine[1];

        /// <summary>
        /// Returns true if up > down (bullish)
        /// </summary>
        public bool IsBullish => upLine[0] > downLine[0];

        /// <summary>
        /// Returns true if up < down (bearish)
        /// </summary>
        public bool IsBearish => upLine[0] < downLine[0];

        /// <summary>
        /// Returns current overbought/oversold value (up - down)
        /// </summary>
        public double Value => upLine[0] - downLine[0];

        /// <summary>
        /// Returns true if transitioning from yellow (reversal zone)
        /// Pine: iff(Oo[1] < Oo and Cc < Cc[1], yellow...)
        /// </summary>
        public bool IsReversalZone
        {
            get
            {
                if (CurrentBar < 1) return false;
                double Oo = Math.Min(upLine[0], downLine[0]);
                double Oo1 = Math.Min(upLine[1], downLine[1]);
                double Cc = Math.Max(upLine[0], downLine[0]);
                double Cc1 = Math.Max(upLine[1], downLine[1]);
                return Oo1 < Oo && Cc < Cc1;
            }
        }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.
namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
    {
        private OverboughtOversoldIndicator[] cacheOverboughtOversoldIndicator;
        public OverboughtOversoldIndicator OverboughtOversoldIndicator(int length)
        {
            return OverboughtOversoldIndicator(Input, length);
        }

        public OverboughtOversoldIndicator OverboughtOversoldIndicator(ISeries<double> input, int length)
        {
            if (cacheOverboughtOversoldIndicator != null)
                for (int idx = 0; idx < cacheOverboughtOversoldIndicator.Length; idx++)
                    if (cacheOverboughtOversoldIndicator[idx] != null && cacheOverboughtOversoldIndicator[idx].Length == length && cacheOverboughtOversoldIndicator[idx].EqualsInput(input))
                        return cacheOverboughtOversoldIndicator[idx];
            return CacheIndicator<OverboughtOversoldIndicator>(new OverboughtOversoldIndicator() { Length = length }, input, ref cacheOverboughtOversoldIndicator);
        }
    }
}

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
    {
        public Indicators.OverboughtOversoldIndicator OverboughtOversoldIndicator(int length)
        {
            return indicator.OverboughtOversoldIndicator(Input, length);
        }

        public Indicators.OverboughtOversoldIndicator OverboughtOversoldIndicator(ISeries<double> input, int length)
        {
            return indicator.OverboughtOversoldIndicator(input, length);
        }
    }
}
#endregion
