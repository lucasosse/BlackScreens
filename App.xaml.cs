using BlackScreenAppWPF;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;

namespace BlackScreensWPF
{
    public partial class App : System.Windows.Application
    {
        public KeyboardHook kh;

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
        /// from native code (keyboard hook, tray icon), where an exception would kill the process.
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
            // Check for duplicate launch of application
            String thisprocessname = Process.GetCurrentProcess().ProcessName;
            if (Process.GetProcesses().Count(p => p.ProcessName == thisprocessname) > 1)
            {
                Quit();
                return;
            }

            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
                CommonData.dataInstance.LogToFile.Fatal(args.ExceptionObject as Exception, "Unhandled exception");
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                CommonData.dataInstance.LogToFile.Error(args.Exception, "Unobserved task exception");
                args.SetObserved();
            };
            CommonData.dataInstance.LogToFile.Info("Starting BlackScreens " + typeof(App).Assembly.GetName().Version);

            // Hook for listen to Windows Keys
            kh = new KeyboardHook(false);
            kh.KeyDown += Kh_KeyDown;
            kh.hook();

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
            if ((CommonData.dataInstance.FirstAppLaunch == true) || (CommonData.dataInstance.ReduceAppOnLaunch == false))
            {
                CommonData.dataInstance.FParams.showWindow();
            }
            else
            {
                CommonData.dataInstance.FParams.updateScreenNames();
                CommonData.dataInstance.FParams.minimizeWindow();
            }
            CommonData.dataInstance.FirstAppLaunch = false;
        }

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
            String textAltToShow = "ALT + " + bwNumber;
            bwToShow.KeyToUse = "Use key " + textAltToShow + " to switch";
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
        /// Switch one screen, screenNumber from 1 to 6
        /// </summary>
        private void toggleScreen(int screenNumber)
        {
            IReadOnlyList<MonitorInfo> monitors = Monitors.GetAll();
            if (screenNumber > monitors.Count)
                return;
            BlackWindow currentBlackWindow = getOrCreateBlackWindow(screenNumber);
            if (currentBlackWindow.IsVisible)
            { // Hidding black screen window if already visible
                hideBlackWindow(currentBlackWindow);
            }
            else
            { // Showing Blackscreen window if not visible
                showBlackWindow(currentBlackWindow, monitors[screenNumber - 1], screenNumber);
            }
        }

        /// <summary>
        /// Called by Windows inside the low level keyboard hook. Must return quickly (Windows silently
        /// removes slow hooks) and must never throw, so the real work is queued on the dispatcher.
        /// </summary>
        private bool Kh_KeyDown(int wParam, KeyboardHookData lParam)
        {
            try
            {
                Keys keyData = (Keys)lParam.vkCode;
                // Testing alt+x key pressed
                if (!kh.AltHeld || kh.CtrlHeld || kh.ShiftHeld)
                    return false;

                if (keyData == Keys.D0)
                {
                    Dispatcher.BeginInvoke(() => Safe(toggleAllScreens, "toggleAllScreens()"));
                    return true;
                }
                int screenNumKey = keyData - Keys.D0;
                if ((screenNumKey >= 1) && (screenNumKey <= CommonData.MaxScreens) && (screenNumKey <= Screen.AllScreens.Length))
                {
                    Dispatcher.BeginInvoke(() => Safe(() => toggleScreen(screenNumKey), "toggleScreen(" + screenNumKey + ")"));
                    // If keyHandled == true, current keypressed will not be handled anymore by Windows
                    // (in certain cases, let it continue to be handled by Microsoft Windows launch Windows system beep sound each time alt+x key was pressed...)
                    return true;
                }
            }
            catch (Exception ex)
            {
                CommonData.dataInstance.LogToFile.Error(ex, "Kh_KeyDown() failed");
            }
            return false;
        }

        void App_Exit(object sender, ExitEventArgs e)
        {
            SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
            kh?.unhook();
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
