#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
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

#endregion



#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		
		private ninZaSuperTrend4U[] cacheninZaSuperTrend4U;

		
		public ninZaSuperTrend4U ninZaSuperTrend4U(ninZaSuperTrend4U_MAType mAType, int mAPeriod, int offsetMultiplier, int offsetPeriod)
		{
			return ninZaSuperTrend4U(Input, mAType, mAPeriod, offsetMultiplier, offsetPeriod);
		}


		
		public ninZaSuperTrend4U ninZaSuperTrend4U(ISeries<double> input, ninZaSuperTrend4U_MAType mAType, int mAPeriod, int offsetMultiplier, int offsetPeriod)
		{
			if (cacheninZaSuperTrend4U != null)
				for (int idx = 0; idx < cacheninZaSuperTrend4U.Length; idx++)
					if (cacheninZaSuperTrend4U[idx].MAType == mAType && cacheninZaSuperTrend4U[idx].MAPeriod == mAPeriod && cacheninZaSuperTrend4U[idx].OffsetMultiplier == offsetMultiplier && cacheninZaSuperTrend4U[idx].OffsetPeriod == offsetPeriod && cacheninZaSuperTrend4U[idx].EqualsInput(input))
						return cacheninZaSuperTrend4U[idx];
			return CacheIndicator<ninZaSuperTrend4U>(new ninZaSuperTrend4U(){ MAType = mAType, MAPeriod = mAPeriod, OffsetMultiplier = offsetMultiplier, OffsetPeriod = offsetPeriod }, input, ref cacheninZaSuperTrend4U);
		}

	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		
		public Indicators.ninZaSuperTrend4U ninZaSuperTrend4U(ninZaSuperTrend4U_MAType mAType, int mAPeriod, int offsetMultiplier, int offsetPeriod)
		{
			return indicator.ninZaSuperTrend4U(Input, mAType, mAPeriod, offsetMultiplier, offsetPeriod);
		}


		
		public Indicators.ninZaSuperTrend4U ninZaSuperTrend4U(ISeries<double> input , ninZaSuperTrend4U_MAType mAType, int mAPeriod, int offsetMultiplier, int offsetPeriod)
		{
			return indicator.ninZaSuperTrend4U(input, mAType, mAPeriod, offsetMultiplier, offsetPeriod);
		}
	
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		
		public Indicators.ninZaSuperTrend4U ninZaSuperTrend4U(ninZaSuperTrend4U_MAType mAType, int mAPeriod, int offsetMultiplier, int offsetPeriod)
		{
			return indicator.ninZaSuperTrend4U(Input, mAType, mAPeriod, offsetMultiplier, offsetPeriod);
		}


		
		public Indicators.ninZaSuperTrend4U ninZaSuperTrend4U(ISeries<double> input , ninZaSuperTrend4U_MAType mAType, int mAPeriod, int offsetMultiplier, int offsetPeriod)
		{
			return indicator.ninZaSuperTrend4U(input, mAType, mAPeriod, offsetMultiplier, offsetPeriod);
		}

	}
}

#endregion
