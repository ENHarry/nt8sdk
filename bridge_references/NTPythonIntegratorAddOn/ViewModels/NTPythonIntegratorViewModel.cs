using Microsoft.Win32;
using NinjaTrader.Code;
using NinjaTrader.Core;
using NinjaTrader.NinjaScript;
using NTPythonIntegratorAddOn.Helpers;
using Python.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace NTPythonIntegratorAddOn.ViewModels
{
    public class NTPythonIntegratorViewModel : ViewModelBase
    {

        #region Fields

        private string  _pythonDllFile;
        private bool    _usingVenv;
        private string  _pathToVenv;
        private string  _pythonConfig;
        private bool    _isEngineInitialized;
        private IntPtr  _threadState;

        private ICommand _selectPythonDllCommand;
        private ICommand _selectPathToVenvCommand;
        private ICommand _initializePythonCommand;
        private ICommand _shutdownPythonCommand;

        #endregion

        #region Constructors

        public NTPythonIntegratorViewModel()
        {
            _pythonDllFile = "Please select python DLL";
            _usingVenv = false;
            _pathToVenv = "Please select path to virtual environment";
            _pythonConfig = "Python not configured";
            _isEngineInitialized = false;
            _selectPythonDllCommand = new RelayCommand(param => SelectPythonDll(), param => true);
            _selectPathToVenvCommand = new RelayCommand(param => SelectPathToVenv(), param => true);
            _initializePythonCommand = new RelayCommand(param => InitializePython(), param => true);
            _shutdownPythonCommand = new RelayCommand(param => ShutdownPython(), param => true);
        }

        #endregion

        #region Commands

        public ICommand SelectPythonDllCommand
        {
            get
            {
                return _selectPythonDllCommand;
            }
        }

        public ICommand SelectPathToVenvCommand
        {
            get
            {
                return _selectPathToVenvCommand;
            }
        }

        public ICommand InitalizePythonCommand 
        {
            get
            { 
                return _initializePythonCommand;
            }
        }

        public ICommand ShutdownPythonCommand 
        { 
            get
            {
                return _shutdownPythonCommand;
            }
        }

        #endregion

        #region Properties

        public string PythonDllFile
        {
            get => _pythonDllFile;
            set => SetProperty(ref _pythonDllFile, value);
        }

        public bool UsingVenv
        {
            get => _usingVenv;
            set => SetProperty(ref _usingVenv, value);
        }
        public string PathToVenv
        {
            get => _pathToVenv;
            set => SetProperty(ref _pathToVenv, value);
        }

        public string PythonConfig
        {
            get => _pythonConfig;
            set => SetProperty(ref _pythonConfig, value);
        }

        public bool IsEngineIntialized
        {
            get;
        }

        #endregion

        #region Command Functions

        // Command Functions
        private void SelectPythonDll()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();

            openFileDialog.Filter = "dll files (*.dll)|*.dll|All files (*.*)|*.*";

            if (openFileDialog.ShowDialog() == true)
            {

                //Output.Process("Selected pythonDll file: " + openFileDialog.FileName, PrintTo.OutputTab1);
                PythonDllFile = openFileDialog.FileName;

                Output.Process("_pythonDllFile: " + _pythonDllFile, PrintTo.OutputTab1);
            }
        }

        private void SelectPathToVenv()
        {

            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    PathToVenv = dialog.SelectedPath;
                }

            }
        }

        private void InitializePython()
        {
            // define folders
            var pythonDll = new FileInfo(_pythonDllFile);

            // virtual environment
            if (_usingVenv)
            {
                var pathToVirtualEnv = new DirectoryInfo(_pathToVenv);
                var lib = Path.Combine(pathToVirtualEnv.FullName, "Lib");
                var sitePackages = Path.Combine(lib, "site-packages");
                var binPath = Path.Combine(pathToVirtualEnv.FullName, "Scripts"); // Scripts on Windows, bin on Linux
                var pythonPath = $"{sitePackages};{lib}";

                // set environment variables
                var path = Environment.GetEnvironmentVariable("PATH").TrimEnd(';');
                //bool containsSearchResult = path.Contains(binPath);
                bool containsSearchResult = path.Contains(pathToVirtualEnv.FullName);

                if (!containsSearchResult)
                {
                    path = $"{path};{pathToVirtualEnv.FullName};{binPath}";
                }

                Environment.SetEnvironmentVariable("PATH", path);
                Environment.SetEnvironmentVariable("PYTHONPATH", pythonPath);
                Environment.SetEnvironmentVariable("VIRTUAL_ENV", pathToVirtualEnv.FullName);
            }
            else
            {
                // set environment variables
                var path = Environment.GetEnvironmentVariable("PATH").TrimEnd(';');
                Environment.SetEnvironmentVariable("PATH", path);
            }

            try
            {
                Output.Process("Entered try block", PrintTo.OutputTab1);
                // update PythonDLL & PythonPath
                Runtime.PythonDLL = pythonDll.FullName;
                Output.Process("PythonDLL set", PrintTo.OutputTab1);

                PythonEngine.Initialize();
                _threadState = PythonEngine.BeginAllowThreads();
                Output.Process("PythonEngine Initalized", PrintTo.OutputTab1);

            } catch (System.TypeInitializationException e)
            {
                Output.Process($"FATAL, Unable to load Python, dll={Runtime.PythonDLL}", PrintTo.OutputTab1);
                throw new Exception($"FATAL, Unable to load Python, dll={Runtime.PythonDLL}", e);
            }
            catch (Exception e)
            {
                Output.Process($"Python initialization Exception, {e.Message}, Stack Trace: {e.StackTrace}", PrintTo.OutputTab1);
                throw new Exception($"Python initialization Exception, {e.Message}, Stack Trace: {e.StackTrace}", e);
            }

            if (PythonEngine.IsInitialized)
            {
                _isEngineInitialized = true;

                PythonConfig = String.Format("Python Version: {0}, {1}\n", PythonEngine.Version.Trim(), Runtime.PythonDLL);
                PythonConfig += String.Format("Python Home: {0}\n", PythonEngine.PythonHome);
                PythonConfig += String.Format("Python Path: {0}\n", PythonEngine.PythonPath);
                PythonConfig += String.Format("Path: {0}\n", Environment.GetEnvironmentVariable("PATH"));

                Output.Process("PythonConfig: " + _pythonConfig, PrintTo.OutputTab1);

            }
        }

        private void ShutdownPython()
        {
            if (PythonEngine.IsInitialized)
            {
                PythonEngine.EndAllowThreads(_threadState);
                PythonEngine.Shutdown();

                _isEngineInitialized = false;

                PythonConfig = "Python Engine unloaded.";
                Output.Process("PythonConfig: " + _pythonConfig, PrintTo.OutputTab1);
            }
        }

        #endregion

    }
}
