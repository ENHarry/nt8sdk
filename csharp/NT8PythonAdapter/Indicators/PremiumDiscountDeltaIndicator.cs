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
    /// Premium & Discount with Delta Volume Indicator
    /// 
    /// Based on BigBeluga's Pine Script indicator.
    /// 
    /// Identifies:
    /// - Premium zones (above equilibrium) - selling zones
    /// - Discount zones (below equilibrium) - buying zones
    /// - Delta volume (buying vs selling pressure)
    /// - Equilibrium line (fair value)
    /// 
    /// Use for:
    /// - Filtering entries (buy in discount, sell in premium)
    /// - Confirming direction with delta volume
    /// - Identifying institutional order flow
    /// </summary>
    public class PremiumDiscountDeltaIndicator : Indicator
    {
        #region Nested Types
        
        public enum ZoneType
        {
            Premium,
            Discount,
            Equilibrium
        }
        
        public class ZoneInfo
        {
            public ZoneType Type { get; set; }
            public double UpperBound { get; set; }
            public double LowerBound { get; set; }
            public double Equilibrium { get; set; }
            public double DeltaPercent { get; set; }
            public double BuyVolume { get; set; }
            public double SellVolume { get; set; }
            public double TotalVolume { get; set; }
        }
        
        #endregion
        
        #region Variables
        
        // Short-term zone
        private double srHighest;
        private double srLowest;
        private double srEquilibrium;
        private double srDeltaPercent;
        private double srBuyVolume;
        private double srSellVolume;
        
        // Macro zone
        private double macroHighest;
        private double macroLowest;
        private double macroEquilibrium;
        private double macroDeltaPercent;
        private double macroBuyVolume;
        private double macroSellVolume;
        
        // ATR for box scaling
        private double atrValue;
        
        #endregion
        
        #region Properties
        
        [NinjaScriptProperty]
        [Display(Name = "Show SR Zone", Order = 1, GroupName = "Display")]
        public bool ShowSRZone { get; set; }
        
        [NinjaScriptProperty]
        [Range(10, 500)]
        [Display(Name = "SR Lookback Period", Order = 2, GroupName = "SR Zone")]
        public int SRPeriod { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Show Macro Zone", Order = 3, GroupName = "Display")]
        public bool ShowMacroZone { get; set; }
        
        [NinjaScriptProperty]
        [Range(50, 1000)]
        [Display(Name = "Macro Lookback Period", Order = 4, GroupName = "Macro Zone")]
        public int MacroPeriod { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Discount Color", Order = 5, GroupName = "Colors")]
        public Brush DiscountColor { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Premium Color", Order = 6, GroupName = "Colors")]
        public Brush PremiumColor { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Export to File", Order = 7, GroupName = "Output")]
        public bool ExportToFile { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Output File Path", Order = 8, GroupName = "Output")]
        public string OutputFilePath { get; set; }
        
        // Plot series
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Equilibrium { get { return Values[0]; } }
        
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> SRDelta { get { return Values[1]; } }
        
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> MacroDelta { get { return Values[2]; } }
        
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> ZoneSignal { get { return Values[3]; } }
        
        // Public accessors
        public ZoneInfo CurrentSRZone
        {
            get
            {
                return new ZoneInfo
                {
                    Type = GetZoneType(srEquilibrium),
                    UpperBound = srHighest,
                    LowerBound = srLowest,
                    Equilibrium = srEquilibrium,
                    DeltaPercent = srDeltaPercent,
                    BuyVolume = srBuyVolume,
                    SellVolume = srSellVolume,
                    TotalVolume = srBuyVolume + srSellVolume
                };
            }
        }
        
        public ZoneInfo CurrentMacroZone
        {
            get
            {
                return new ZoneInfo
                {
                    Type = GetZoneType(macroEquilibrium),
                    UpperBound = macroHighest,
                    LowerBound = macroLowest,
                    Equilibrium = macroEquilibrium,
                    DeltaPercent = macroDeltaPercent,
                    BuyVolume = macroBuyVolume,
                    SellVolume = macroSellVolume,
                    TotalVolume = macroBuyVolume + macroSellVolume
                };
            }
        }
        
        #endregion
        
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Premium & Discount with Delta Volume";
                Name = "PremiumDiscountDelta";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                
                // Default parameters
                ShowSRZone = true;
                SRPeriod = 50;
                ShowMacroZone = true;
                MacroPeriod = 200;
                DiscountColor = Brushes.CornflowerBlue;
                PremiumColor = Brushes.Salmon;
                ExportToFile = false;
                OutputFilePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "NT8_Python_Data", "premium_discount.csv"
                );
                
                AddPlot(Brushes.White, "Equilibrium");
                AddPlot(Brushes.Orange, "SRDelta");
                AddPlot(Brushes.Purple, "MacroDelta");
                AddPlot(Brushes.Yellow, "ZoneSignal");
            }
        }
        
        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(SRPeriod, MacroPeriod))
            {
                Equilibrium[0] = Close[0];
                SRDelta[0] = 0;
                MacroDelta[0] = 0;
                ZoneSignal[0] = 0;
                return;
            }
            
            // Calculate ATR for scaling
            CalculateATR();
            
            // Calculate SR Zone
            if (ShowSRZone)
                CalculateSRZone();
            
            // Calculate Macro Zone
            if (ShowMacroZone)
                CalculateMacroZone();
            
            // Output
            OutputResults();
            
            // Export
            if (ExportToFile)
                ExportData();
        }
        
        #region Calculations
        
        private void CalculateATR()
        {
            // 200-period ATR
            double trSum = 0;
            int atrPeriod = Math.Min(200, CurrentBar);
            
            for (int i = 0; i < atrPeriod; i++)
            {
                double tr = High[i] - Low[i];
                if (i < CurrentBar)
                {
                    tr = Math.Max(tr, Math.Abs(High[i] - Close[i + 1]));
                    tr = Math.Max(tr, Math.Abs(Low[i] - Close[i + 1]));
                }
                trSum += tr;
            }
            
            atrValue = (trSum / atrPeriod) * 0.8;
        }
        
        private void CalculateSRZone()
        {
            srHighest = double.MinValue;
            srLowest = double.MaxValue;
            srBuyVolume = 0;
            srSellVolume = 0;
            
            for (int i = 0; i < SRPeriod && i <= CurrentBar; i++)
            {
                if (High[i] > srHighest) srHighest = High[i];
                if (Low[i] < srLowest) srLowest = Low[i];
                
                if (Close[i] > Open[i])
                    srBuyVolume += Volume[i];
                else if (Close[i] < Open[i])
                    srSellVolume += Volume[i];
            }
            
            srEquilibrium = (srHighest + srLowest) / 2;
            
            // Calculate delta percent - Pine: deltaVolSR := (negVolSR.avg() / posVolSR.avg() + 1) * 100
            // Pine stores negVol as NEGATIVE values: negVolSR.set(i, -volume[i])
            // So: (-50/100 + 1) * 100 = 50% when sell volume is half of buy volume
            // This means more buying = positive delta, more selling = negative delta
            if (srBuyVolume > 0)
                srDeltaPercent = ((-srSellVolume / srBuyVolume) + 1) * 100;
            else
                srDeltaPercent = 0;
            
            // Pine clamps to -100 to 100
            srDeltaPercent = Math.Max(-100, Math.Min(100, srDeltaPercent));
        }
        
        private void CalculateMacroZone()
        {
            macroHighest = double.MinValue;
            macroLowest = double.MaxValue;
            macroBuyVolume = 0;
            macroSellVolume = 0;
            
            for (int i = 0; i < MacroPeriod && i <= CurrentBar; i++)
            {
                if (High[i] > macroHighest) macroHighest = High[i];
                if (Low[i] < macroLowest) macroLowest = Low[i];
                
                if (Close[i] > Open[i])
                    macroBuyVolume += Volume[i];
                else if (Close[i] < Open[i])
                    macroSellVolume += Volume[i];
            }
            
            macroEquilibrium = (macroHighest + macroLowest) / 2;
            
            // Calculate delta percent - same formula as SR
            if (macroBuyVolume > 0)
                macroDeltaPercent = ((-macroSellVolume / macroBuyVolume) + 1) * 100;
            else
                macroDeltaPercent = 0;
            
            // Pine clamps to -100 to 100
            macroDeltaPercent = Math.Max(-100, Math.Min(100, macroDeltaPercent));
        }
        
        private ZoneType GetZoneType(double equilibrium)
        {
            if (Close[0] > equilibrium)
                return ZoneType.Premium;
            if (Close[0] < equilibrium)
                return ZoneType.Discount;
            return ZoneType.Equilibrium;
        }
        
        #endregion
        
        #region Output
        
        private void OutputResults()
        {
            // Equilibrium line
            Equilibrium[0] = ShowSRZone ? srEquilibrium : macroEquilibrium;
            
            // Delta volumes
            SRDelta[0] = srDeltaPercent;
            MacroDelta[0] = macroDeltaPercent;
            
            // Zone signal: 1 = Premium (sell bias), -1 = Discount (buy bias), 0 = Equilibrium
            ZoneType zone = GetZoneType(srEquilibrium);
            ZoneSignal[0] = zone == ZoneType.Premium ? 1 : (zone == ZoneType.Discount ? -1 : 0);
            
            // Draw equilibrium line
            if (ShowSRZone)
            {
                Draw.Line(this, "SREq" + CurrentBar, false, SRPeriod, srEquilibrium, 0, srEquilibrium,
                    Brushes.White, DashStyleHelper.Dash, 1);
            }
            
            // Draw zones (only on last bar for performance)
            if (CurrentBar == BarsArray[0].Count - 1)
            {
                DrawZones();
            }
        }
        
        private void DrawZones()
        {
            if (ShowSRZone)
            {
                // Premium zone (above equilibrium)
                Draw.Rectangle(this, "SRPremium", false,
                    SRPeriod, srHighest + atrValue,
                    -50, srEquilibrium,
                    PremiumColor, PremiumColor, 20);
                
                // Discount zone (below equilibrium)
                Draw.Rectangle(this, "SRDiscount", false,
                    SRPeriod, srEquilibrium,
                    -50, srLowest - atrValue,
                    DiscountColor, DiscountColor, 20);
                
                // Labels
                Draw.Text(this, "PremiumLabel", "PREMIUM", -25, srHighest,
                    PremiumColor);
                Draw.Text(this, "DiscountLabel", "DISCOUNT", -25, srLowest,
                    DiscountColor);
                Draw.Text(this, "SRDeltaLabel",
                    $"SR Delta: {srDeltaPercent:F1}%", -25, srEquilibrium,
                    srDeltaPercent > 0 ? DiscountColor : PremiumColor);
            }
            
            if (ShowMacroZone)
            {
                // Macro zones with lower opacity
                Draw.Rectangle(this, "MacroPremium", false,
                    MacroPeriod, macroHighest + atrValue,
                    -70, macroEquilibrium,
                    PremiumColor, PremiumColor, 40);
                
                Draw.Rectangle(this, "MacroDiscount", false,
                    MacroPeriod, macroEquilibrium,
                    -70, macroLowest - atrValue,
                    DiscountColor, DiscountColor, 40);
                
                Draw.Text(this, "MacroDeltaLabel",
                    $"Macro Delta: {macroDeltaPercent:F1}%", -70, macroEquilibrium,
                    macroDeltaPercent > 0 ? DiscountColor : PremiumColor);
            }
        }
        
        private void ExportData()
        {
            if (CurrentBar % 100 != 0 && CurrentBar != BarsArray[0].Count - 1)
                return;
            
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
                        writer.WriteLine("Timestamp,Price,SREquilibrium,SRHigh,SRLow,SRDeltaPct,SRBuyVol,SRSellVol," +
                            "MacroEquilibrium,MacroHigh,MacroLow,MacroDeltaPct,MacroBuyVol,MacroSellVol,ZoneType");
                    }
                    
                    writer.WriteLine($"{Time[0]:yyyy-MM-dd HH:mm:ss},{Close[0]:F2}," +
                        $"{srEquilibrium:F2},{srHighest:F2},{srLowest:F2},{srDeltaPercent:F1}," +
                        $"{srBuyVolume:F0},{srSellVolume:F0}," +
                        $"{macroEquilibrium:F2},{macroHighest:F2},{macroLowest:F2},{macroDeltaPercent:F1}," +
                        $"{macroBuyVolume:F0},{macroSellVolume:F0},{GetZoneType(srEquilibrium)}");
                }
            }
            catch (Exception ex)
            {
                Print("PremiumDiscountDelta export error: " + ex.Message);
            }
        }
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// Check if current price is in premium zone.
        /// </summary>
        public bool IsInPremiumZone()
        {
            return GetZoneType(srEquilibrium) == ZoneType.Premium;
        }
        
        /// <summary>
        /// Check if current price is in discount zone.
        /// </summary>
        public bool IsInDiscountZone()
        {
            return GetZoneType(srEquilibrium) == ZoneType.Discount;
        }
        
        /// <summary>
        /// Get delta volume bias.
        /// </summary>
        public string GetDeltaBias()
        {
            if (srDeltaPercent > 20) return "BULLISH";
            if (srDeltaPercent < -20) return "BEARISH";
            return "NEUTRAL";
        }
        
        /// <summary>
        /// Check if buy should be allowed based on zone and delta.
        /// Buy is favorable in discount zone with bullish delta.
        /// </summary>
        public bool IsBuyFavorable()
        {
            return IsInDiscountZone() && srDeltaPercent >= 0;
        }
        
        /// <summary>
        /// Check if sell should be allowed based on zone and delta.
        /// Sell is favorable in premium zone with bearish delta.
        /// </summary>
        public bool IsSellFavorable()
        {
            return IsInPremiumZone() && srDeltaPercent <= 0;
        }
        
        /// <summary>
        /// Get distance from equilibrium as percentage.
        /// </summary>
        public double GetDistanceFromEquilibrium()
        {
            if (srEquilibrium == 0) return 0;
            return (Close[0] - srEquilibrium) / srEquilibrium * 100;
        }
        
        /// <summary>
        /// Get position within range (0-100, where 50 is equilibrium).
        /// </summary>
        public double GetRangePosition()
        {
            if (srHighest == srLowest) return 50;
            return (Close[0] - srLowest) / (srHighest - srLowest) * 100;
        }
        
        #endregion
    }
}
