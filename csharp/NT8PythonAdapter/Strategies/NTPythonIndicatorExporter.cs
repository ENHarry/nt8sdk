#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{
    public class NTPythonIndicatorExporter : Strategy
    {
        private string outputPath;
        private string outgoingBaseDir;       
        private string outgoingPythonDir;
        private object fileLock = new object();
        
        // Reflection-based AddOn access (avoids compile-time dependency)
        private object adapterInstance = null;
        private MethodInfo publishMethod = null;
        private bool adapterSearched = false;

        // Indicators
        private RSI rsi;
        private MACD macd;
        private StochRSI stochRsi;
        // private ADX adx; // Replaced by DM
        private DM dm; // Includes ADX and DI+/-
        private StdDev stdDev;
        // Add other indicators as needed (Bollinger, SuperTrend if available as custom)
        
        // Custom variables for generic Supertrend calculation
        private ATR atr;
        
        // Heikin Ashi State
        private double haOpen, haClose, haHigh, haLow;
        private double haOpenPrev, haClosePrev;
        
        // Trending / Swing State
        private Swing swing;
        private double lastSwingHigh = 0;
        private double prevSwingHigh = 0;
        private double lastSwingLow = 0;
        private double prevSwingLow = 0;
        private int trendSignal = 0;
        
        // Divergence State
        // Storing recent peaks for Price and RSI
        // Simple list of (BarIndex, Value)
        private List<Tuple<int, double>> pricePeaks = new List<Tuple<int, double>>();
        private List<Tuple<int, double>> rsiPeaks = new List<Tuple<int, double>>();
        private int divergenceSignal = 0;

        // MULTI-TIMEFRAME INDICATORS
        // 5 MIN
        private RSI rsi5m;
        private MACD macd5m;
        private DM dm5m;
        private Swing swing5m;
        private int trendSignal5m = 0;
        private double lastSwingHigh5m = 0;
        private double lastSwingLow5m = 0;
        private double prevSwingHigh5m = 0;
        private double prevSwingLow5m = 0;

        // 15 MIN
        private RSI rsi15m;
        private MACD macd15m;
        private DM dm15m;
        private Swing swing15m;
        private int trendSignal15m = 0;
        private double lastSwingHigh15m = 0;
        private double lastSwingLow15m = 0;
        private double prevSwingHigh15m = 0;
        private double prevSwingLow15m = 0;

        // S/R LEVELS - Session and Previous Day
        private double sessionHigh = 0;
        private double sessionLow = double.MaxValue;
        private double prevDayHigh = 0;
        private double prevDayLow = 0;
        private double prevDayClose = 0;
        private DateTime lastSessionDate = DateTime.MinValue;
        
        // Additional swing levels for S/R (looking back further)
        private double swingHigh2 = 0;  // 2nd most recent swing high
        private double swingLow2 = 0;   // 2nd most recent swing low
        private double swingHigh3 = 0;  // 3rd most recent swing high
        private double swingLow3 = 0;   // 3rd most recent swing low

        // HISTORY FILE - Rolling 4 hours (240 bars for 1-min)
        private List<string> historyBuffer = new List<string>();
        private const int MAX_HISTORY_BARS = 240;
        private DateTime lastHistoryWrite = DateTime.MinValue;


        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Exports calculated indicator values to a file for Python consumption.";
                Name = "NTPythonIndicatorExporter";
                Calculate = Calculate.OnPriceChange;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 0;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 20;

                // Default Parameters
                ExportFileName = "indicator_data.txt";
                OutputDirectory = ""; 
                BackupExportFileName = "nt8_indicators.csv";
                BackupOutputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NT8_Python_Data");

            }
            else if (State == State.Configure)
            {
                // Add HTF Data Series
                AddDataSeries(BarsPeriodType.Minute, 5);  // BarsArray[1]
                AddDataSeries(BarsPeriodType.Minute, 15); // BarsArray[2]

                // Setup output directory - use NT8's standard outgoing/python path
                string ntPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NinjaTrader 8");
                outgoingBaseDir = Path.Combine(ntPath, "outgoing");
                outgoingPythonDir = Path.Combine(outgoingBaseDir, "python");

                try
                {
                    
                    if (!Directory.Exists(outgoingPythonDir))
                        Directory.CreateDirectory(outgoingPythonDir);
                    
                    outputPath = Path.Combine(outgoingPythonDir, "indicator_data.txt");
                    
                    Print($"🚀 Indicator Exporter initialized");
                    Print($"   Output Directory: {outgoingPythonDir}");
                    Print($"   Output File: {outputPath}");
                    
                    // Initialize HA
                    haOpen = 0; haClose = 0; haHigh = 0; haLow = 0;
                    haOpenPrev = 0; haClosePrev = 0;
                }
                catch (Exception ex)
                {
                    Print("Error creating directory: " + ex.Message);
                }
            }
            else if (State == State.DataLoaded)
            {
                // PRIMARY (1m)
                rsi = RSI(14, 3);
                macd = MACD(12, 26, 9);
                stochRsi = StochRSI(14);
                // adx = ADX(14);
                dm = DM(7); // Period 7 as requested
                atr = ATR(14);
                stdDev = StdDev(20);
                swing = Swing(15);
                
                // HTF 5m (BarsArray[1])
                rsi5m = RSI(BarsArray[1], 14, 3);
                macd5m = MACD(BarsArray[1], 12, 26, 9);
                dm5m = DM(BarsArray[1], 7);
                swing5m = Swing(BarsArray[1], 15);
                
                // HTF 15m (BarsArray[2])
                rsi15m = RSI(BarsArray[2], 14, 3);
                macd15m = MACD(BarsArray[2], 12, 26, 9);
                dm15m = DM(BarsArray[2], 7);
                swing15m = Swing(BarsArray[2], 15);

            }
            else if (State == State.Terminated)
            {
                // Cleanup if needed
            }
        }

        protected override void OnBarUpdate()
        {
            // More lenient bar requirements for faster startup
            if (CurrentBar < 20) return;
            
            // For multi-timeframe, only require minimum bars on HTF series
            // Primary (1m): 20 bars, HTF: at least 5 bars each
            if (CurrentBars[0] < 20) return;
            if (BarsArray.Length > 1 && CurrentBars[1] < 5) return;
            if (BarsArray.Length > 2 && CurrentBars[2] < 5) return;
            
            // Only process on Primary Bar events (1-min) 
            if (BarsInProgress != 0) return;

            try 
            {
                // ============================================
                // 1. UPDATE HEIKIN ASHI
                // ============================================
                // Formula:
                // HA_Close = (O + H + L + C) / 4
                // HA_Open = (Prev_HA_Open + Prev_HA_Close) / 2
                // HA_High = Max(H, HA_Open, HA_Close)
                // HA_Low = Min(L, HA_Open, HA_Close)
                
                if (haOpenPrev == 0) // Initialize
                {
                    haOpenPrev = Open[0];
                    haClosePrev = Close[0];
                }
                
                haClose = (Open[0] + High[0] + Low[0] + Close[0]) * 0.25;
                haOpen = (haOpenPrev + haClosePrev) * 0.5;
                haHigh = Math.Max(High[0], Math.Max(haOpen, haClose));
                haLow = Math.Min(Low[0], Math.Min(haOpen, haClose));
                
                // Update prev only on bar close? 
                // Strategies in 'OnPriceChange' run multiple times per bar.
                // We should only shift prev values when FirstTickOfBar is true.
                if (IsFirstTickOfBar)
                {
                     haOpenPrev = haOpen;
                     haClosePrev = haClose;
                }

                // ============================================
                // 2. UPDATE TRENDING (Swing Logic - 1m Primary)
                // ============================================
                
                if (IsFirstTickOfBar)
                {
                    // Update 1m Trend (also captures multiple swing levels for S/R)
                    UpdateSwingTrendWithSR(swing, ref lastSwingHigh, ref prevSwingHigh, ref lastSwingLow, ref prevSwingLow, ref trendSignal,
                                           ref swingHigh2, ref swingHigh3, ref swingLow2, ref swingLow3);
                    
                    // Update 5m Trend
                    UpdateSwingTrend(swing5m, ref lastSwingHigh5m, ref prevSwingHigh5m, ref lastSwingLow5m, ref prevSwingLow5m, ref trendSignal5m);
                    
                    // Update 15m Trend
                    UpdateSwingTrend(swing15m, ref lastSwingHigh15m, ref prevSwingHigh15m, ref lastSwingLow15m, ref prevSwingLow15m, ref trendSignal15m);
                }

                // ============================================
                // 2.5 UPDATE SESSION & PREV DAY S/R LEVELS
                // ============================================
                DateTime barDate = Time[0].Date;
                
                // Check for new session (day change)
                if (barDate != lastSessionDate)
                {
                    // Save previous day's data
                    if (lastSessionDate != DateTime.MinValue)
                    {
                        prevDayHigh = sessionHigh;
                        prevDayLow = sessionLow;
                        // Get previous close from the last bar of yesterday
                        if (CurrentBar > 0)
                            prevDayClose = Close[1];
                    }
                    
                    // Reset session tracking
                    sessionHigh = High[0];
                    sessionLow = Low[0];
                    lastSessionDate = barDate;
                }
                else
                {
                    // Update session high/low
                    if (High[0] > sessionHigh) sessionHigh = High[0];
                    if (Low[0] < sessionLow) sessionLow = Low[0];
                }

                // ============================================
                // 3. UPDATE DIVERGENCE (Simplified)
                // ============================================
                // Check for Bearish Divergence: Price Higher High, RSI Lower High
                if (lastSwingHigh > prevSwingHigh && prevSwingHigh > 0)
                {
                     // Need RSI at those specific bars... complex to map back.
                     // Fallback: Use simple slope check
                     divergenceSignal = 0;
                     // Implement placeholder for now
                }
                
                // ============================================
                // 4. PREPARE EXPORT
                // ============================================
                
                // Calculate custom values if needed
                double bbMiddle = SMA(20)[0];
                double bbUpper = bbMiddle + (stdDev[0] * 2);
                double bbLower = bbMiddle - (stdDev[0] * 2);
                
                StringBuilder sb = new StringBuilder();
                
                sb.Append(Time[0].ToString("o", System.Globalization.CultureInfo.InvariantCulture)); 
                sb.Append(",");
                
                // OHLCV
                sb.Append(Open[0]).Append(",");
                sb.Append(High[0]).Append(",");
                sb.Append(Low[0]).Append(",");
                sb.Append(Close[0]).Append(",");
                sb.Append(Volume[0]).Append(",");
                
                // Indicators (1m)
                sb.Append(rsi[0]).Append(",");
                sb.Append(macd.Value[0]).Append(",");   
                sb.Append(macd.Avg[0]).Append(",");     
                sb.Append(macd.Diff[0]).Append(",");    
                
                sb.Append(stochRsi[0]).Append(",");     // K
                double stoch_d = SMA(stochRsi, 3)[0];
                sb.Append(stoch_d).Append(",");         // D
                
                sb.Append(dm.ADXPlot[0]).Append(","); // Replaces ADX(14) with DM(7).ADX
                sb.Append(dm.DiPlus[0]).Append(",");
                sb.Append(dm.DiMinus[0]).Append(",");
                
                sb.Append(atr[0]).Append(",");
                
                sb.Append(bbUpper).Append(",");
                sb.Append(bbMiddle).Append(",");
                sb.Append(bbLower).Append(",");
                
                // HA & Trend (1m)
                sb.Append(haOpen).Append(",");
                sb.Append(haHigh).Append(",");
                sb.Append(haLow).Append(",");
                sb.Append(haClose).Append(",");
                sb.Append(trendSignal).Append(",");
                sb.Append(divergenceSignal).Append(",");
                
                // Liquidity (1m)
                sb.Append(lastSwingHigh).Append(",");
                sb.Append(lastSwingLow).Append(",");

                // HTF 5m
                sb.Append(rsi5m[0]).Append(",");
                sb.Append(macd5m.Value[0]).Append(",");  
                sb.Append(macd5m.Avg[0]).Append(",");     
                sb.Append(macd5m.Diff[0]).Append(","); 
                sb.Append(trendSignal5m).Append(",");

                // HTF 15m
                sb.Append(rsi15m[0]).Append(",");
                sb.Append(macd15m.Value[0]).Append(",");
                sb.Append(macd15m.Avg[0]).Append(",");     
                sb.Append(macd15m.Diff[0]).Append(",");
                sb.Append(trendSignal15m).Append(",");
                
                // S/R LEVELS - Session
                sb.Append(sessionHigh).Append(",");
                sb.Append(sessionLow).Append(",");
                
                // S/R LEVELS - Previous Day
                sb.Append(prevDayHigh).Append(",");
                sb.Append(prevDayLow).Append(",");
                sb.Append(prevDayClose).Append(",");
                
                // S/R LEVELS - Additional Swing Levels
                sb.Append(swingHigh2).Append(",");
                sb.Append(swingLow2).Append(",");
                sb.Append(swingHigh3).Append(",");
                sb.Append(swingLow3); // No comma at end

                string line = sb.ToString();

                // 1. SEND VIA TCP (Primary Low Latency Channel)
                // Use reflection to find NT8PythonAdapter.Instance at runtime
                if (!adapterSearched)
                {
                    adapterSearched = true;
                    try
                    {
                        // Search all loaded assemblies for the AddOn type
                        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            Type adapterType = asm.GetType("NinjaTrader.NinjaScript.AddOns.NT8PythonAdapter");
                            if (adapterType != null)
                            {
                                PropertyInfo instanceProp = adapterType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                                if (instanceProp != null)
                                {
                                    adapterInstance = instanceProp.GetValue(null);
                                    if (adapterInstance != null)
                                    {
                                        publishMethod = adapterType.GetMethod("PublishIndicatorData", new Type[] { typeof(string) });
                                        Print("NT8PythonAdapter found - TCP streaming enabled");
                                    }
                                }
                                break;
                            }
                        }
                        if (adapterInstance == null)
                            Print("NT8PythonAdapter not loaded - using file-based communication only");
                    }
                    catch (Exception ex)
                    {
                        Print("Error finding NT8PythonAdapter: " + ex.Message);
                    }
                }
                
                // Publish via TCP if adapter is available
                if (adapterInstance != null && publishMethod != null)
                {
                    try
                    {
                        publishMethod.Invoke(adapterInstance, new object[] { line });
                    }
                    catch { /* Silently fail TCP, file fallback will work */ }
                }

                // 2. WRITE TO FILE (Primary file-based channel)
                // Use atomic write pattern - write to temp, then rename
                if (string.IsNullOrEmpty(outgoingPythonDir))
                {
                    // Re-initialize if not set
                    string ntPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NinjaTrader 8");
                    outgoingBaseDir = Path.Combine(ntPath, "outgoing");
                    outgoingPythonDir = Path.Combine(outgoingBaseDir, "python");
                    if (!Directory.Exists(outgoingPythonDir))
                        Directory.CreateDirectory(outgoingPythonDir);
                }
                
                lock (fileLock)
                {
                    try
                    {
                        string tempPath = Path.Combine(outgoingPythonDir, "indicator_data.tmp");
                        string finalPath = Path.Combine(outgoingPythonDir, "indicator_data.txt");
                        
                        // Updated header with S/R levels
                        string header = "Time,Open,High,Low,Close,Volume,RSI,MACD,MACD_Signal,MACD_Hist,StochK,StochD,ADX,DI_Plus,DI_Minus,ATR,BB_Upper,BB_Middle,BB_Lower,HA_Open,HA_High,HA_Low,HA_Close,Trend_Signal,Div_Signal,Liq_High,Liq_Low,RSI_5m,MACD_5m,MACD_Signal_5m,MACD_Hist_5m,Trend_5m,RSI_15m,MACD_15m,MACD_Signal_15m,MACD_Hist_15m,Trend_15m,Session_High,Session_Low,PrevDay_High,PrevDay_Low,PrevDay_Close,Swing_High2,Swing_Low2,Swing_High3,Swing_Low3";
                        File.WriteAllText(tempPath, header + "\n" + line);
                        
                        // Atomic rename
                        if (File.Exists(finalPath))
                            File.Delete(finalPath);
                        File.Move(tempPath, finalPath);
                        
                        // ============================================
                        // HISTORY FILE - Append on bar close only
                        // ============================================
                        if (IsFirstTickOfBar && Time[0] > lastHistoryWrite)
                        {
                            lastHistoryWrite = Time[0];
                            
                            // Add to buffer
                            historyBuffer.Add(line);
                            
                            // Trim to max size
                            while (historyBuffer.Count > MAX_HISTORY_BARS)
                                historyBuffer.RemoveAt(0);
                            
                            // Write history file
                            string historyPath = Path.Combine(outgoingPythonDir, "indicator_history.csv");
                            string historyTempPath = Path.Combine(outgoingPythonDir, "indicator_history.tmp");
                            
                            StringBuilder histSb = new StringBuilder();
                            histSb.AppendLine(header);
                            foreach (string histLine in historyBuffer)
                                histSb.AppendLine(histLine);
                            
                            File.WriteAllText(historyTempPath, histSb.ToString());
                            if (File.Exists(historyPath))
                                File.Delete(historyPath);
                            File.Move(historyTempPath, historyPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        Print("File Write Error: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Print("Export Error: " + ex.Message);
            }
        }
        
        private void UpdateSwingTrend(Swing swingInd, ref double lastHigh, ref double prevHigh, ref double lastLow, ref double prevLow, ref int trendSig)
        {
             // Check last 20 bars for any swing points
             // This logic needs to be robust. 
             // We iterate back. If we find a swing point that is different from our 'last', we shift.
             
             // Optimization: Just check the current [0] and [1] bar of the indicator?
             // No, Swing indicator sets values in the past.
             // We need to look back 'Strength' bars usually.
             
             // Simple approach: Check last 50 bars. If we find a non-zero, capture it.
             
             double foundHigh = 0;
             double foundLow = 0;
             
             for (int i=0; i<50; i++)
             {
                 if (foundHigh == 0 && swingInd.SwingHigh[i] > double.Epsilon) foundHigh = swingInd.SwingHigh[i];
                 if (foundLow == 0 && swingInd.SwingLow[i] > double.Epsilon) foundLow = swingInd.SwingLow[i];
                 if (foundHigh != 0 && foundLow != 0) break;
             }
             
             if (foundHigh != 0 && Math.Abs(foundHigh - lastHigh) > 0.0001)
             {
                 prevHigh = lastHigh;
                 lastHigh = foundHigh;
             }
             
             if (foundLow != 0 && Math.Abs(foundLow - lastLow) > 0.0001)
             {
                 prevLow = lastLow;
                 lastLow = foundLow;
             }
             
             if (lastHigh > prevHigh && lastLow > prevLow) trendSig = 1;
             else if (lastHigh < prevHigh && lastLow < prevLow) trendSig = -1;
             else trendSig = 0;
        }
        
        private void UpdateSwingTrendWithSR(Swing swingInd, ref double lastHigh, ref double prevHigh, ref double lastLow, ref double prevLow, ref int trendSig,
                                            ref double high2, ref double high3, ref double low2, ref double low3)
        {
             // Extended version that captures multiple swing levels for S/R
             // Look back further to find up to 3 swing highs and 3 swing lows
             
             List<double> foundHighs = new List<double>();
             List<double> foundLows = new List<double>();
             
             for (int i=0; i<100; i++)
             {
                 double sh = swingInd.SwingHigh[i];
                 double sl = swingInd.SwingLow[i];
                 
                 if (sh > double.Epsilon && !foundHighs.Contains(sh) && foundHighs.Count < 3)
                     foundHighs.Add(sh);
                 if (sl > double.Epsilon && !foundLows.Contains(sl) && foundLows.Count < 3)
                     foundLows.Add(sl);
                     
                 if (foundHighs.Count >= 3 && foundLows.Count >= 3) break;
             }
             
             // Update primary swing high
             if (foundHighs.Count > 0 && Math.Abs(foundHighs[0] - lastHigh) > 0.0001)
             {
                 prevHigh = lastHigh;
                 lastHigh = foundHighs[0];
             }
             
             // Update primary swing low
             if (foundLows.Count > 0 && Math.Abs(foundLows[0] - lastLow) > 0.0001)
             {
                 prevLow = lastLow;
                 lastLow = foundLows[0];
             }
             
             // Capture additional S/R levels
             high2 = foundHighs.Count > 1 ? foundHighs[1] : 0;
             high3 = foundHighs.Count > 2 ? foundHighs[2] : 0;
             low2 = foundLows.Count > 1 ? foundLows[1] : 0;
             low3 = foundLows.Count > 2 ? foundLows[2] : 0;
             
             // Determine trend
             if (lastHigh > prevHigh && lastLow > prevLow) trendSig = 1;
             else if (lastHigh < prevHigh && lastLow < prevLow) trendSig = -1;
             else trendSig = 0;
        }


        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "Output Directory", Description = "Directory to save the output file", Order = 1, GroupName = "Parameters")]
        public string OutputDirectory { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Export File Name", Description = "Name of the main export file", Order = 2, GroupName = "Parameters")]
        public string ExportFileName { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Backup Output Directory", Description = "Backup directory for indicator history", Order = 3, GroupName = "Parameters")]
        public string BackupOutputDirectory { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Backup Export File Name", Description = "Name of the backup export file", Order = 4, GroupName = "Parameters")]
        public string BackupExportFileName { get; set; }
        #endregion
    }
}
