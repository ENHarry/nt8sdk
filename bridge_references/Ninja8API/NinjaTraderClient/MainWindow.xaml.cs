using NinjaTrader.Client;
using System;
using System.Windows;
using System.Windows.Controls;

namespace NinjaTraderClient
{
	// set Build -> Configuration Manager -> Active solution platform -> x64
	// (this is currently referencing the C:\Program Files\bin\NinjaTrader.Client.dll)
	public partial class MainWindow : Window, IDisposable
	{
		private double					askPriceReceive, bidPriceReceive, lastPriceReceive, priceSend;
		private bool					disposed, shuttingDown, sendLive, sendingData, receivingData;
		private string					instrumentReceive, instrumentSend;
		private int						mdSubscribed, size;
		private Client					myClient;
		private System.Timers.Timer		timerReceive, timerSend;

		public MainWindow()
		{
			InitializeComponent();

			disposed	= false;
			Closed		+= MainWindow_Closed;
			Loaded		+= MainWindow_Loaded;
		}
		
		// Public implementation of Dispose pattern callable by consumers.
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		// Protected implementation of Dispose pattern.
		protected virtual void Dispose(bool disposing)
		{
			if (disposed)
				return;

			disposed = true;
		}

		private void MainWindow_Closed(object sender, EventArgs e)
		{
			if (shuttingDown)
				return;

			if (receivingData)
				myClient.UnsubscribeMarketData(instrumentReceive);

			myClient.TearDown();

			shuttingDown	= true;
			Application.Current.Shutdown();
		}

		private void MainWindow_Loaded(object sender, RoutedEventArgs e)
		{
			myClient				= new Client();
			shuttingDown			= false;
			sendLive				= true;
			sendingData				= false;
			receivingData			= false;
			mdSubscribed			= 0;
			size					= 1;
			timerReceive			= new System.Timers.Timer() { Interval = 1000 };
			timerReceive.Elapsed	+= MarketDataTimerElapsed;
			timerSend				= new System.Timers.Timer() { Interval = 1000 };
			timerSend.Elapsed		+= LastTimerElapsed;
			
			int connect = myClient.Connected(1);

			Console.WriteLine(string.Format("{0} | connect: {1}", DateTime.Now, connect.ToString()));
		}

		private void ToggleReceive(object sender, RoutedEventArgs e)
		{
			instrumentReceive = InstrumentInput.Text;

			Button button = sender as Button;

			switch(receivingData)
			{
				case false:
					mdSubscribed			= myClient.SubscribeMarketData(instrumentReceive);
					if (mdSubscribed != 0)
						break;

					receivingData			= true;
					timerReceive.Enabled	= true;
					button.Content			= string.Format("Stop Receiving {0}", instrumentReceive);
					break;

				case true:
					mdSubscribed			= myClient.UnsubscribeMarketData(instrumentReceive);
					receivingData			= false;
					timerReceive.Enabled	= false;
					button.Content			= string.Format("Receive {0}", instrumentReceive);
					break;
			}
		}

		private void ToggleSend(object sender, RoutedEventArgs e)
		{
			instrumentSend = InstrumentInput.Text;

			if (!sendLive)
			{
				priceSend = 1.00;

				DateTime dt = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 13, 30, 0);

				for (int i = 0; i < 300; i++)
				{
					Console.WriteLine(string.Format("{0}, {1}, {2}, {3}, {4}", instrumentSend, i, 10 + i * 0.01, 1, dt.AddSeconds(i).ToString("yyyyMMddHHmmms")));
					myClient.LastPlayback(instrumentSend, 10 + i * 0.01, 1, dt.AddSeconds(i).ToString("yyyyMMddHHmmss"));
				}

				return;
			}

			Button button = sender as Button;

			switch (sendingData)
			{
				case true:
					timerSend.Enabled	= false;
					button.Content		= string.Format("Send {0}", instrumentSend);
					sendingData			= false;
					break;

				case false:
					priceSend			= 1.00;
					timerSend.Enabled	= true;
					button.Content		= string.Format("Stop Sending {0}", instrumentSend);
					sendingData			= true;
					break;
			}
		}
		
		private void LastTimerElapsed(object sender, System.Timers.ElapsedEventArgs args)
		{
			if (priceSend < 1.04)
				priceSend += .01;
			else
				priceSend = 1.00;

			int success = myClient.Last(instrumentSend, priceSend, size);

			Console.WriteLine(string.Format("{0} | last success: {1}", DateTime.Now, success));
		}

		private void MarketDataTimerElapsed(object sender, System.Timers.ElapsedEventArgs args)
		{
			askPriceReceive		= myClient.MarketData(instrumentReceive, 2);
			bidPriceReceive		= myClient.MarketData(instrumentReceive, 1);
			lastPriceReceive	= myClient.MarketData(instrumentReceive, 0);

			this.Dispatcher.InvokeAsync(new Action(() =>
			{
				AskTextBlock.Text	= askPriceReceive.ToString("N2");
				BidTextBlock.Text	= bidPriceReceive.ToString("N2");
				LastTextBlock.Text	= lastPriceReceive.ToString("N2");
			}));

			Console.WriteLine(string.Format( "{0} | {1} | Last: {2}, Ask: {3}, Bid: {4}", DateTime.Now, instrumentReceive, lastPriceReceive, askPriceReceive, bidPriceReceive ));
		}

		private void SetSendType(object sender, RoutedEventArgs e)
		{
			RadioButton button = sender as RadioButton;

			if (button.Content.ToString() == "Live")
				sendLive = true;

			else if (button.Content.ToString() == "Historical")
				sendLive = false;
		}
	}
}
