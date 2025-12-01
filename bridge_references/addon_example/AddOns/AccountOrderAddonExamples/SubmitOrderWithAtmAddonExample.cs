//
// Copyright (C) 2020, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component
// Coded by NinjaTrader_ChelseaB
//
#region Using declarations
using NinjaTrader.Cbi;
using NinjaTrader.Core;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Gui.NinjaScript.AtmStrategy;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Xml.Linq;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.AccountOrderAddonExamples
{
	public class SubmitOrderWithAtmAddonExample : NinjaTrader.NinjaScript.AddOnBase
	{
		private NTMenuItem		ccNewMenu, newWindowMenuItem, subMenu;

		private void OnMenuItemClick(object sender, RoutedEventArgs e)
		{
			Globals.RandomDispatcher.InvokeAsync(new Action(() => new SubmitOrderWithAtmAddonExampleWindow().Show()));
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description			= @"";
				Name				= "SubmitOrderWithAtmAddonExample";
			}
		}
		
		protected override void OnWindowCreated(Window window)
		{
			/*
				* The following checks if the window created was the Control Center
				* If the window is the control center, the Control Center -> New menu is found by AutomationId with .FindFirst()
				* If the New menu is found, a menu item to open our addon is added
			*/
			ControlCenter controlCenter = window as ControlCenter;

            if (controlCenter == null)
                return;

            ccNewMenu	= controlCenter.FindFirst("ControlCenterMenuItemNew") as NTMenuItem;

            if (ccNewMenu == null)
                return;
			
			// Optionally, we can create a sub-menu in the Control Center New menu with the automationId WindowFrameWorkExamplesMenuItem
			if (controlCenter.FindFirst("AccountOrderExamplesMenuItem") as NTMenuItem == null)
			{
				subMenu = new NTMenuItem { Header = "Account Order Examples", Style = Application.Current.TryFindResource("MainMenuItem") as Style };

				// set an automationId on our submenu to identify it
				System.Windows.Automation.AutomationProperties.SetAutomationId(subMenu, "AccountOrderExamplesMenuItem");

				// Add the submenu to the Control Center -> New menu
				ccNewMenu.Items.Add(subMenu);
			}

			// Or add to an existing submenu if one exists with this automationid, to add multiple addons to the same submenu
			else
			{
				subMenu = controlCenter.FindFirst("AccountOrderExamplesMenuItem") as NTMenuItem;
			}

			// This is this menu item that actually opens the addon shell window. The Header is the menu item text
			newWindowMenuItem		= new NTMenuItem {
				Header	= "Submit Atm Order Example",
				Style	= Application.Current.TryFindResource("MainMenuItem") as Style
			};
			// A click handler is added to open our addon window when the menu item is clicked
			newWindowMenuItem.Click	+= OnMenuItemClick;

			// Add the menu item that opens the window to the sub menu instead of directly to the New menu
			subMenu.Items.Add(newWindowMenuItem);
		}

		protected override void OnWindowDestroyed(Window window)
		{
			// This checks if there is not a menu item or if the destroyed window is not the control center.
			if (newWindowMenuItem == null || !(window is ControlCenter) ||
					ccNewMenu == null ||
					!ccNewMenu.Items.Contains(subMenu))
				return;

			// remove the click handler
			newWindowMenuItem.Click -= OnMenuItemClick;

			// remove the addon window menu item
			subMenu.Items.Remove(newWindowMenuItem);
			newWindowMenuItem = null;

			// if the submenu is empty, remove it as well
			if (subMenu.Items.Count < 1)
			{
				ccNewMenu.Items.Remove(subMenu);
				subMenu = null;
			}
		}
	}

	public class SubmitOrderWithAtmAddonExampleWindow : NTWindow, IWorkspacePersistence
	{
		public SubmitOrderWithAtmAddonExampleWindow()
		{
			Caption		= "Submit Order With Atm Example";
			Width		= 400;
			Height		= 270;

			TabControl tabControl = new TabControl();
			TabControlManager.SetIsMovable(tabControl, true);
			TabControlManager.SetCanAddTabs(tabControl, true);
			TabControlManager.SetCanRemoveTabs(tabControl, true);
			TabControlManager.SetFactory(tabControl, new SubmitOrderWithAtmAddonExampleWindowFactory());

			Content = tabControl;
			tabControl.AddNTTabPage(new SubmitOrderWithAtmAddonExampleWindowTabPage());

			Loaded += (o, e) =>
			{
				if (WorkspaceOptions == null)
				{
					WorkspaceOptions = new WorkspaceOptions("SubmitOrderWithAtmAddonExampleWindow" + Guid.NewGuid().ToString("N"), this);
				}
			};
		}

		public void Restore(XDocument document, XElement element)
		{
			if (MainTabControl != null)
				MainTabControl.RestoreFromXElement(element);
		}

		public void Save(XDocument document, XElement element)
		{
			if (MainTabControl != null)
				MainTabControl.SaveToXElement(element);
		}

		public WorkspaceOptions WorkspaceOptions { get; set; }
	}

	public class SubmitOrderWithAtmAddonExampleWindowFactory : INTTabFactory
	{
		public NTWindow CreateParentWindow()
		{
			return new SubmitOrderWithAtmAddonExampleWindow();
		}

		public NTTabPage CreateTabPage(string typeName, bool isNewWindow = false)
		{
			return new SubmitOrderWithAtmAddonExampleWindowTabPage();
		}
	}

	public class SubmitOrderWithAtmAddonExampleWindowTabPage : NTTabPage
	{
		private AccountSelector		accountSelector;
		private AtmStrategySelector atmStrategySelector;
		private InstrumentSelector	instrumentSelector;
		private Button				submitOrderButton;

		public SubmitOrderWithAtmAddonExampleWindowTabPage()
		{
			Content = LoadPage();
		}

		private void AccountSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
		}

		private void AtmStrategySelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
		}

		public void Button_Click(object sender, RoutedEventArgs e)
		{
			if (accountSelector.SelectedAccount == null || instrumentSelector.Instrument == null || atmStrategySelector.Instrument == null || atmStrategySelector.SelectedAtmStrategy == null)
			{
				NinjaTrader.Code.Output.Process("account, instrument, or atm selection null", PrintTo.OutputTab1);
				return;
			}
			
			// the name of the order must be Entry or the order will get stuck in the intialize state
			Order buyMarketOrder = accountSelector.SelectedAccount.CreateOrder(atmStrategySelector.Instrument, OrderAction.Buy, OrderType.Market, TimeInForce.Day, 1, 0, 0, string.Empty, "Entry", null);
			NinjaTrader.NinjaScript.AtmStrategy.StartAtmStrategy(atmStrategySelector.SelectedAtmStrategy, buyMarketOrder);
		}

		public override void Cleanup()
		{
			accountSelector.SelectionChanged		-= AccountSelector_SelectionChanged;
			atmStrategySelector.SelectionChanged	-= AtmStrategySelector_SelectionChanged;
			submitOrderButton.Click					-= Button_Click;
			submitOrderButton						= null;
			instrumentSelector.InstrumentChanged	-= InstrumentSelector_InstrumentChanged;
			instrumentSelector						= null;
		}

		protected override string GetHeaderPart(string variable)
		{
			return "Tab";
		}

		private DependencyObject LoadPage()
		{
			Page page = new Page() /*{ Width = 1000, Height = 100 }*/;

			Grid grid = new Grid() { HorizontalAlignment = HorizontalAlignment.Center };

			foreach (int rowHeight in new int[] { 10, 30, 3, 30, 3, 30, 3, 30 })
				grid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(rowHeight) });

			foreach (int columnWidth in new int[] { 5, 340, 5 })
				grid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(columnWidth) });

			accountSelector						= new AccountSelector();
			accountSelector.SelectionChanged	+= AccountSelector_SelectionChanged;
			grid.Children.Add(accountSelector);
			Grid.SetRow(accountSelector, 3);

			atmStrategySelector	= new AtmStrategySelector()
			{
				AtmStrategySelectionMode	= AtmStrategySelectionMode.SelectActiveAtmStrategyOnOrderSubmission,
				Id							= Guid.NewGuid().ToString("N")
			};
			atmStrategySelector.SelectionChanged	+= AtmStrategySelector_SelectionChanged;
			atmStrategySelector.SetBinding(AtmStrategySelector.AccountProperty, new Binding { Source = accountSelector, Path = new PropertyPath("SelectedAccount") });
			grid.Children.Add(atmStrategySelector);
			Grid.SetRow(atmStrategySelector, 5);

			instrumentSelector						= new InstrumentSelector();
			instrumentSelector.InstrumentChanged	+= InstrumentSelector_InstrumentChanged;
			grid.Children.Add(instrumentSelector);
			Grid.SetRow(instrumentSelector, 1);

			submitOrderButton = new Button()
			{
				Background				= Brushes.Gray,
				BorderBrush				= Brushes.DimGray,
				Content					= "Submit Order",
				Height					= 30,
				HorizontalAlignment		= HorizontalAlignment.Right,
				Width					= 150
			};

			submitOrderButton.Click += Button_Click;
			Grid.SetRow(submitOrderButton, 7);
			grid.Children.Add(submitOrderButton);

			foreach (UIElement obj in grid.Children)
				Grid.SetColumn(obj, 1);

			page.Content	= grid;

			return page.Content as DependencyObject;
		}

		private void InstrumentSelector_InstrumentChanged(object sender, EventArgs e)
		{
			atmStrategySelector.Instrument	= instrumentSelector.Instrument;
		}

		protected override void Restore(XElement element) { }
		protected override void Save(XElement element) { }
	}
}
