using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;

namespace BlackScreensWPF
{
    public partial class App : System.Windows.Application
    {
        private HotkeyManager hotkeyManager;
        private SingleInstance singleInstance;

        public static App Instance => (App)Current;

        /// <summary>Per action, why its hotkey could not be registered (shown in settings)</summary>
        public Dictionary<string, string> HotkeyErrors { get; private set; } = new Dictionary<string, string>();

        /// <summary>
        /// True once the app is really quitting. Until then, closing a window only hides it
        /// (a closed WPF window can never be shown again, which used to crash on the next tray click).
        /// </summary>
        public static bool IsShuttingDown { get; private set; }

        // Displays often report several changes in a row while waking up, so refresh once they settle
        private DispatcherTimer displayChangeTimer;

        /// <summary>
        /// Quit the application for real
        /// </summary>
        public static void Quit()
        {
            IsShuttingDown = true;
            Current.Shutdown();
        }

        /// <summary>
        /// Run an action, logging instead of crashing on failure. Used for every entry point called
        /// from native code (tray icon, hotkey window), where an exception would kill the process.
        /// </summary>
        public static void Safe(Action action, string where)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                CommonData.dataInstance.LogToFile.Error(ex, where + " failed");
            }
        }

        void App_Startup(object sender, StartupEventArgs e)
        {
            // For testing languages localization purpose
            //System.Threading.Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo("en");
            // Already running: hand our command line over (no arguments = open its settings) and leave
            singleInstance = new SingleInstance();
            if (!singleInstance.IsFirstInstance)
            {
                SingleInstance.SendToRunningInstance(e.Args);
                Quit();
                return;
            }
            // --tray: started at login, stay in the tray whatever the preferences say.
            // Same when started with a command (e.g. "toggle 2" from a shortcut) while not yet running.
            bool hasCommand = e.Args.Any(a => !a.StartsWith("--"));
            bool startInTray = hasCommand || e.Args.Any(a => String.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));

            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
                CommonData.dataInstance.LogToFile.Fatal(args.ExceptionObject as Exception, "Unhandled exception");
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                CommonData.dataInstance.LogToFile.Error(args.Exception, "Unobserved task exception");
                args.SetObserved();
            };
            CommonData.dataInstance.LogToFile.Info("Starting BlackScreens " + typeof(App).Assembly.GetName().Version);

            displayChangeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            displayChangeTimer.Tick += (s, args) =>
            {
                displayChangeTimer.Stop();
                Safe(refreshAfterDisplayChange, "refreshAfterDisplayChange()");
            };
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;

            CommonData.dataInstance.FParams = new MainWindow();
            CommonData.dataInstance.loadUserConfigFile(CommonData.dataInstance.FParams);
            CommonData.dataInstance.LogToFile.Debug("App/xaml.cs.App_Startup() FirstAppLaunch = "+ CommonData.dataInstance.FirstAppLaunch);
            CommonData.dataInstance.LogToFile.Debug("App/xaml.cs.App_Startup() ReduceAppOnLaunch = " + CommonData.dataInstance.ReduceAppOnLaunch);

            hotkeyManager = new HotkeyManager();
            hotkeyManager.HotkeyPressed += action => Safe(() => RunAction(action, "toggle"), "hotkey " + action);
            applyHotkeys();

            if (!startInTray && ((CommonData.dataInstance.FirstAppLaunch == true) || (CommonData.dataInstance.ReduceAppOnLaunch == false)))
            {
                CommonData.dataInstance.FParams.showWindow();
            }
            else
            {
                CommonData.dataInstance.FParams.updateScreenNames();
                CommonData.dataInstance.FParams.minimizeWindow();
            }
            if (CommonData.dataInstance.FirstAppLaunch)
            {
                CommonData.dataInstance.FirstAppLaunch = false;
                CommonData.dataInstance.saveUserConfigFiles();
            }

            singleInstance.StartListening(args => Dispatcher.BeginInvoke(() => Safe(() => handleCommand(args), "handleCommand()")));
            if (hasCommand)
                Safe(() => handleCommand(e.Args), "handleCommand()");
        }

        /// <summary>
        /// (Re)register all hotkeys from the preferences
        /// </summary>
        public void applyHotkeys()
        {
            HotkeyErrors = hotkeyManager.RegisterAll(CommonData.dataInstance.Hotkeys);
            CommonData.dataInstance.FParams.updateHotkeyTexts();
        }

        /// <summary>
        /// Release all hotkeys while the user records a new one in settings, so the keys reach the editor
        /// </summary>
        public void suspendHotkeys()
        {
            hotkeyManager.UnregisterAll();
        }

        /// <summary>
        /// Command line received from a later launch of BlackScreens (or our own, when started with one):
        /// settings | exit | toggle|on|off 1-6|all|others
        /// </summary>
        private void handleCommand(string[] args)
        {
            CommonData.dataInstance.LogToFile.Info("Command received: '" + String.Join(" ", args) + "'");
            string[] words = args.Where(a => !a.StartsWith("--")).Select(a => a.ToLowerInvariant()).ToArray();
            if (words.Length == 0)
            {
                // Plain relaunch opens settings; a second "--tray" start (e.g. at login) does nothing
                if (args.Length > 0)
                    return;
                words = new[] { "settings" };
            }
            switch (words[0])
            {
                case "settings":
                case "show":
                    CommonData.dataInstance.FParams.showWindow();
                    break;
                case "exit":
                case "quit":
                    Quit();
                    break;
                case "install-shortcuts":
                    Shortcuts.Install();
                    break;
                case "remove-shortcuts":
                    Shortcuts.Remove();
                    break;
                case "toggle":
                case "on":
                case "off":
                    string action = parseTarget(words.ElementAtOrDefault(1));
                    if (action == null)
                        CommonData.dataInstance.LogToFile.Warn("Command '" + words[0] + "' needs a target: 1-6, all or others");
                    else
                        RunAction(action, words[0]);
                    break;
                default:
                    CommonData.dataInstance.LogToFile.Warn("Unknown command '" + words[0] + "'");
                    break;
            }
        }

        /// <summary>
        /// "2" / "screen2" / "all" / "others" to an action name, null if not understood
        /// </summary>
        private static string parseTarget(string target)
        {
            if (target == null)
                return null;
            if (target == HotkeyActions.All || target == HotkeyActions.Others)
                return target;
            if (target.StartsWith("screen"))
                target = target.Substring("screen".Length);
            if (int.TryParse(target, out int screenNumber) && screenNumber >= 1 && screenNumber <= CommonData.MaxScreens)
                return HotkeyActions.Screen(screenNumber);
            return null;
        }

        /// <summary>
        /// Run an action (see HotkeyActions) from a hotkey, the tray menu or the command line.
        /// mode: "toggle", "on" or "off".
        /// </summary>
        public void RunAction(string action, string mode)
        {
            CommonData.dataInstance.LogToFile.Info("Action " + mode + " " + action);
            if (action == HotkeyActions.All)
            {
                if (mode == "on") showAllBlackWindows(Monitors.GetAll());
                else if (mode == "off") hideAllBlackWindows(CommonData.dataInstance.BlackWindows);
                else toggleAllScreens();
            }
            else if (action == HotkeyActions.Others)
            {
                bool anyBlack = numberOfActiveBlackScreen(CommonData.dataInstance.BlackWindows) > 0;
                if (mode == "on" || (mode == "toggle" && !anyBlack)) blackOutAllButMouseScreen();
                else hideAllBlackWindows(CommonData.dataInstance.BlackWindows);
            }
            else if (action.StartsWith("screen") && int.TryParse(action.Substring("screen".Length), out int screenNumber))
            {
                setScreen(screenNumber, mode);
            }
        }

        /// <summary>
        /// Is the black window of this screen number (1 to 6) currently shown
        /// </summary>
        public bool IsScreenBlack(int screenNumber) => CommonData.dataInstance.BlackWindows[screenNumber - 1]?.IsVisible ?? false;

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            CommonData.dataInstance.LogToFile.Error(e.Exception, "Unhandled UI exception");
            // Keep running: a failed toggle is much better than losing the hotkeys until next boot
            e.Handled = true;
        }

        private void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e)
        {
            // SystemEvents can fire on its own thread
            Dispatcher.BeginInvoke(() =>
            {
                displayChangeTimer.Stop();
                displayChangeTimer.Start();
            });
        }

        private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
                SystemEvents_DisplaySettingsChanged(sender, e);
        }

        /// <summary>
        /// After monitors were added, removed, rearranged or woke up: put every visible black window
        /// back on the physical monitor it was covering, or hide it if that monitor is gone.
        /// </summary>
        private void refreshAfterDisplayChange()
        {
            IReadOnlyList<MonitorInfo> monitors = Monitors.GetAll();
            CommonData.dataInstance.LogToFile.Info("Display change: " + String.Join(", ",
                monitors.Select((m, i) => (i + 1) + "=" + m.FriendlyName + " " + m.Bounds)));

            BlackWindow[] blackWindows = CommonData.dataInstance.BlackWindows;
            List<string> blackedDevicePaths = blackWindows
                .Where(bw => (bw != null) && bw.IsVisible && (bw.Monitor != null))
                .Select(bw => bw.Monitor.DevicePath)
                .ToList();
            hideAllBlackWindows(blackWindows);

            for (int iMonitor = 0; iMonitor < monitors.Count && iMonitor < CommonData.MaxScreens; iMonitor++)
            {
                if (blackedDevicePaths.Contains(monitors[iMonitor].DevicePath))
                    showBlackWindow(getOrCreateBlackWindow(iMonitor + 1), monitors[iMonitor], iMonitor + 1);
            }

            CommonData.dataInstance.FParams.updateScreenNames();
        }

        /// <summary>
        /// Return number of active BlackScreen, meaning count of BlackWindow which are currently visible
        /// </summary>
        /// <returns></returns>
        private int numberOfActiveBlackScreen(BlackWindow[] bwArray)
        {
            int nbActiveBlackScreens = 0;
            foreach (BlackWindow bw in bwArray)
            {
                if ((bw != null) && (bw.IsVisible))
                {
                    nbActiveBlackScreens++;
                }
            }
            return nbActiveBlackScreens;
        }

        /// <summary>
        /// BlackWindow for this screen number (1 to 6), created the first time it is needed
        /// </summary>
        private BlackWindow getOrCreateBlackWindow(int screenNumber)
        {
            BlackWindow[] blackWindows = CommonData.dataInstance.BlackWindows;
            if (blackWindows[screenNumber - 1] == null)
            {
                BlackWindow bw = new BlackWindow();
                bw.ScreenNumber = screenNumber;
                bw.updateBlackWindowParams();
                blackWindows[screenNumber - 1] = bw;
            }
            return blackWindows[screenNumber - 1];
        }

        /// <summary>
        /// Hide this specific BlackWindow
        /// </summary>
        /// <param name="bsTohide"></param>
        private void hideBlackWindow(BlackWindow bsTohide)
        {
            if (bsTohide != null)
            {
                bsTohide.HideWindow();
                CommonData.dataInstance.FParams.Topmost = false;
            }
        }

        /// <summary>
        /// Show a specific BlackWindow on associated screen
        /// </summary>
        /// <param name="bwToShow">BlackWindow to show</param>
        /// <param name="monitor">Monitor to cover</param>
        /// <param name="bwNumber">BlackWindow number (1 to 6)</param>
        private void showBlackWindow(BlackWindow bwToShow, MonitorInfo monitor, int bwNumber)
        {
            string hotkeyText = CommonData.dataInstance.Hotkeys.GetValueOrDefault(HotkeyActions.Screen(bwNumber), "");
            bwToShow.KeyToUse = String.IsNullOrEmpty(hotkeyText) ? "" : "Use " + hotkeyText + " to switch";
            bwToShow.ScreenNumber = bwNumber;
            bwToShow.ShowWindow(monitor);
            // Hidding parameters window if it's on the same black screen to switch visible
            CommonData.dataInstance.FParams.refreshMainWindowCurrentScreen();
            if (CommonData.dataInstance.ParamsScreenDeviceName == monitor.DeviceName)
            {
                CommonData.dataInstance.FParams.minimizeWindow();
            }
        }

        /// <summary>
        /// Show all BlackWindows for all existing screens
        /// </summary>
        private void showAllBlackWindows(IReadOnlyList<MonitorInfo> monitors)
        {
            for (int iMonitor = 0; iMonitor < monitors.Count && iMonitor < CommonData.MaxScreens; iMonitor++)
            {
                showBlackWindow(getOrCreateBlackWindow(iMonitor + 1), monitors[iMonitor], iMonitor + 1);
            }
        }

        /// <summary>
        /// Hide all black Windows for all existing screens
        /// </summary>
        /// <param name="blackWindows"></param>
        private void hideAllBlackWindows(BlackWindow[] blackWindows)
        {
            for (int iBw = 0; iBw < blackWindows.Length; iBw++)
            {
                hideBlackWindow(blackWindows[iBw]);
            }
        }

        /// <summary>
        /// Switch all screens: show all if at most half of them are black, else hide all
        /// </summary>
        private void toggleAllScreens()
        {
            IReadOnlyList<MonitorInfo> monitors = Monitors.GetAll();
            int nbOfActiveScreen = numberOfActiveBlackScreen(CommonData.dataInstance.BlackWindows);
            if (nbOfActiveScreen <= ((Double)monitors.Count / 2))
                showAllBlackWindows(monitors);
            else
                hideAllBlackWindows(CommonData.dataInstance.BlackWindows);
        }

        /// <summary>
        /// Black window on every monitor except the one the mouse is on
        /// </summary>
        private void blackOutAllButMouseScreen()
        {
            IReadOnlyList<MonitorInfo> monitors = Monitors.GetAll();
            string mouseDeviceName = Screen.FromPoint(Cursor.Position).DeviceName;
            for (int iMonitor = 0; iMonitor < monitors.Count && iMonitor < CommonData.MaxScreens; iMonitor++)
            {
                if (monitors[iMonitor].DeviceName == mouseDeviceName)
                    hideBlackWindow(CommonData.dataInstance.BlackWindows[iMonitor]);
                else if (!IsScreenBlack(iMonitor + 1))
                    showBlackWindow(getOrCreateBlackWindow(iMonitor + 1), monitors[iMonitor], iMonitor + 1);
            }
        }

        /// <summary>
        /// Switch one screen, screenNumber from 1 to 6, mode "toggle", "on" or "off"
        /// </summary>
        private void setScreen(int screenNumber, string mode)
        {
            IReadOnlyList<MonitorInfo> monitors = Monitors.GetAll();
            if (screenNumber < 1 || screenNumber > monitors.Count || screenNumber > CommonData.MaxScreens)
                return;
            BlackWindow currentBlackWindow = getOrCreateBlackWindow(screenNumber);
            bool show = (mode == "on") || (mode == "toggle" && !currentBlackWindow.IsVisible);
            if (!show)
            { // Hidding black screen window if already visible
                hideBlackWindow(currentBlackWindow);
            }
            else if (!currentBlackWindow.IsVisible)
            { // Showing Blackscreen window if not visible
                showBlackWindow(currentBlackWindow, monitors[screenNumber - 1], screenNumber);
            }
        }

        void App_Exit(object sender, ExitEventArgs e)
        {
            SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
            hotkeyManager?.Dispose();
            singleInstance?.Dispose();
        }

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            // Windows logoff / shutdown: windows may be torn down without the usual Closing path
            IsShuttingDown = true;
            CommonData.dataInstance.saveUserConfigFiles();
            base.OnSessionEnding(e);
        }
    }
}
