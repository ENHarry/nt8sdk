#region Using declarations

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Xml.Linq;
using System.Xml.Serialization;
using NinjaTrader.Core;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using System.Windows.Media.Media3D;

#endregion

//This namespace holds GUI items and is required.
//namespace NinjaTrader.NinjaScript.AddOns
namespace NinjaTrader.Gui.NinjaScript
{
    public class NTPythonIntegratorMainAddOn : AddOnBase
    {
        private NTMenuItem piMenuItem;
        private NTMenuItem ccNewMenuSubMenu;
        private NTMenuItem ccNewMenu;

        // Same as other NS objects. However there's a difference: this event could be called in any thread
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "AddOn which enables using python in NinjaTrader 8";
                Name = "NTPythonIntegrator";
            }
        }

        // Will be called as a new NTWindow is created. It will be called in the thread of that window
        protected override void OnWindowCreated(Window window)
        {
            /*
            * The following checks if the window created was the Control Center
            * If the window is the control center, the Control Center -> New menu is found by AutomationId with .FindFirst()
            * If the New menu is found, a menu item to open our addon is added
            */

            ControlCenter cc = window as ControlCenter;
            if (cc == null)
                return;

            /* Determine we want to place our AddOn in the Control Center's "New" menu
            Other menus can be accessed via the control's "Automation ID". For example: toolsMenuItem, workspacesMenuItem, connectionsMenuItem, helpMenuItem. */
            ccNewMenu = cc.FindFirst("ControlCenterMenuItemNew") as NTMenuItem;
            if (ccNewMenu == null)
                return;

            #region OptionalSubmenu
            // Optionally, we can create a sub-menu in the Control Center New menu with the automationId FTMenuItem
            if (cc.FindFirst("FTMenuItem") as NTMenuItem == null)
            {
                ccNewMenuSubMenu = new NTMenuItem { Header = "FalcoTrader AddOns", Style = Application.Current.TryFindResource("MainMenuItem") as Style };

                // set an automationId on our submenu to identify it
                System.Windows.Automation.AutomationProperties.SetAutomationId(ccNewMenuSubMenu, "FTMenuItem");

                // Add the submenu to the Control Center -> New menu
                ccNewMenu.Items.Add(ccNewMenuSubMenu);
            }
            // Or add to an existing submenu if one exists with this automationid, to add multiple addons to the same submenu
            else
            {
                ccNewMenuSubMenu = cc.FindFirst("FTMenuItem") as NTMenuItem;
            }
            #endregion

            // This is this menu item that actually opens the addon shell window. The Header is the menu item text
            piMenuItem = new NTMenuItem
            {
                Header = "PythonIntegrator",
                Style = Application.Current.TryFindResource("MainMenuItem") as Style
            };


            // Add our AddOn into the "New" menu
            ccNewMenuSubMenu?.Items.Add(piMenuItem);

            // Subscribe to the event for when the user presses our AddOn's menu item
            piMenuItem.Click += OnMenuItemClick;
        }

        // Will be called as a new NTWindow is destroyed. It will be called in the thread of that window
        protected override void OnWindowDestroyed(Window window)
        {

            // This checks if there is not a menu item or if the destroyed window is not the control center.
            if (piMenuItem == null || !(window is ControlCenter) ||
                ccNewMenu == null || !ccNewMenu.Items.Contains(ccNewMenuSubMenu))
                return;

            // remove the click handler
            piMenuItem.Click -= OnMenuItemClick;

            // remove the addon window menu item
            ccNewMenuSubMenu?.Items.Remove(piMenuItem);

            piMenuItem = null;

            // if the submenu is empty, remove it as well
            if (ccNewMenuSubMenu != null && ccNewMenuSubMenu.Items.Count < 1)
            {
                ccNewMenu.Items.Remove(ccNewMenuSubMenu);
                ccNewMenuSubMenu = null;
            }
        }

        // Open our AddOn's window when the menu item is clicked on
        private void OnMenuItemClick(object sender, RoutedEventArgs e)
        {
            Core.Globals.RandomDispatcher.BeginInvoke(new Action(() => new NTPyhtonIntegratorWindow().Show()));
        }
    }

    public class NTPythonIntegratorWindowFactory : INTTabFactory
    {
        // This class is simply to return the correct new objects when CreateParentWindow() and CreateTabPage() are called.
        public NTWindow CreateParentWindow()
        {
            return new NTPyhtonIntegratorWindow();
        }

        public NTTabPage CreateTabPage(string typeName, bool isNewWindow = false)
        {
            return new NTPythonIntegratorAddOn.NTPythonIntegratorPage();
        }
    }

    public class NTPyhtonIntegratorWindow : NTWindow, IWorkspacePersistence
    {

        private TabControl tabControl;

        public NTPyhtonIntegratorWindow()
        {
            // set Caption property (not Title), since Title is managed internally to properly combine selected Tab Header and Caption for display in the Windows taskbar
            // This is the name displayed in the top-left of the window
            Caption = "NinjaTrader8 Python Integrator";

            // Set the default dimensions of the window
            Width = 500;
            Height = 400;

            // This creates a tabControl which adds tabs to the window, which is optional
            tabControl = new TabControl();
            TabControlManager.SetIsMovable(tabControl, false);
            TabControlManager.SetCanAddTabs(tabControl, false);
            TabControlManager.SetCanRemoveTabs(tabControl, false);

            TabControlManager.SetFactory(tabControl, new NTPythonIntegratorWindowFactory());

            Content = tabControl;
            tabControl.AddNTTabPage(new NTPythonIntegratorAddOn.NTPythonIntegratorPage());

            // WorkspaceOptions property must be set
            Loaded += (o, e) =>
            {
                if (WorkspaceOptions == null)
                    WorkspaceOptions = new WorkspaceOptions("NTPythonIntegrator-" + Guid.NewGuid().ToString("N"), this);
            };
        }

        // IWorkspacePersistence member. Required for restoring window from workspace
        public void Restore(XDocument document, XElement element)
        {
            if (MainTabControl != null)
                MainTabControl.RestoreFromXElement(element);
        }

        // IWorkspacePersistence member. Required for saving window to workspace
        public void Save(XDocument document, XElement element)
        {
            if (MainTabControl != null)
                MainTabControl.SaveToXElement(element);
        }

        // IWorkspacePersistence member
        public WorkspaceOptions WorkspaceOptions
        { get; set; }
    }
}
