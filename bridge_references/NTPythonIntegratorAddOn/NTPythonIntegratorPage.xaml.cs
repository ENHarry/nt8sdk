using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml.Linq;
using NinjaTrader;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NTPythonIntegratorAddOn.ViewModels;


namespace NTPythonIntegratorAddOn
//namespace NinjaTrader.Gui.NinjaScript
{
    /// <summary>
    /// Interaction logic for NTPythonIntegratorPage.xaml
    /// </summary>
    public partial class NTPythonIntegratorPage
    {
        private NTPythonIntegratorViewModel viewModel;
        public NTPythonIntegratorPage()
        {
            //var viewModel = new NTPythonIntegratorViewModel();
            viewModel = new NTPythonIntegratorViewModel();  
            DataContext = viewModel;

            InitializeComponent();
        }

        // NTTabPage member. Required to determine the text for the tab header name
        protected override string GetHeaderPart(string variable)
        {
            /*switch (variable)
            {
                case "@INSTRUMENT":
                    return Instrument == null ? "New Tab" : Instrument.MasterInstrument.Name;
                case "@INSTRUMENT_FULL":
                    return Instrument == null ? "New Tab" : Instrument.FullName;
            }*/
            return "PythonIntegrator";
        }

        // NTTabPage member. Required for restoring elements from workspace
        protected override void Restore(XElement element)
        {
            if (element == null)
                return;

            XElement rootNode = element.Element("PythonConfig");
            if (rootNode != null)
            {
                XElement pythonDllFileElement = rootNode.Element("PythonDllFile");
                if (pythonDllFileElement != null)
                    viewModel.PythonDllFile = pythonDllFileElement.Value.ToString();

                XElement usingVenvElement = rootNode.Element("UsingVenv");
                if (usingVenvElement != null)
                    viewModel.UsingVenv = Boolean.Parse(usingVenvElement.Value.ToString());

                XElement pathToVenvElement = rootNode.Element("PathToVenv");
                if (pathToVenvElement != null)
                    viewModel.PathToVenv = pathToVenvElement.Value.ToString();

            }
        }

        // NTTabPage member. Required for storing elements to workspace
        protected override void Save(XElement element)
        {
            if (element == null)
                return;

            if (element.Element("PythonConfig") != null)
                element.Element("PythonConfig").Remove();

            // Create root node for the addon: 
            XElement rootElement = new XElement("PythonConfig");

            rootElement.Add(new XElement("PythonDllFile", viewModel.PythonDllFile));
            rootElement.Add(new XElement("UsingVenv", viewModel.UsingVenv));
            rootElement.Add(new XElement("PathToVenv", viewModel.PathToVenv));

            element.Add(rootElement);
        }
    }
}
