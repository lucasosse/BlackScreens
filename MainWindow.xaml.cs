using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Collections.Generic;
using System.Linq;

namespace BlackScreensWPF
{
    /// <summary>
    /// Logique d'interaction pour MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private event EventHandler Resized;

        public MainWindow()
        {
            CommonData.dataInstance.LogToFile.Debug("MainWindow()");
            InitializeComponent();
            this.tbTitle.Text = "BlackScreens " + typeof(MainWindow).Assembly.GetName().Version.ToString(3);
            CommonData.dataInstance.LogToFile.Info("MainWindow() "+ this.tbTitle.Text + " launching");
            this.DataContext = CommonData.dataInstance;
            notifyIcon.TrayLeftMouseDown += NotifyIcon_TrayLeftMouseDown;
            refreshMainWindowCurrentScreen();
            updateScreenNames();
            buildHotkeyEditor();
        }

        /// <summary>
        /// Refresh name of the current param window screen
        /// </summary>
        public void refreshMainWindowCurrentScreen()
        {
            CommonData.dataInstance.LogToFile.Debug("refreshMainWindowCurrentScreen()");
            System.Windows.Interop.WindowInteropHelper wih = new System.Windows.Interop.WindowInteropHelper(this);
            Screen currentScreen = Screen.FromHandle(wih.Handle);
            CommonData.dataInstance.ParamsScreenDeviceName = currentScreen.DeviceName;
        }

        private void NotifyIcon_TrayLeftMouseDown(object sender, RoutedEventArgs e)
        {
            // Raised from the tray icon's native window procedure: never let an exception escape
            App.Safe(toggleShowHideWindow, "NotifyIcon_TrayLeftMouseDown()");
        }

        /// <summary>
        /// True when the settings window is on screen (not hidden in the tray)
        /// </summary>
        public bool IsShownToUser => this.IsVisible && (this.WindowState != WindowState.Minimized);

        /// <summary>
        /// Send the settings window to the tray: hidden, not just minimized (a minimized window without
        /// taskbar button would linger as a small title bar above the taskbar)
        /// </summary>
        public void minimizeWindow()
        {
            this.WindowState = WindowState.Minimized;
            this.ShowInTaskbar = false;
            this.Hide();
        }

        /// <summary>
        /// Update all screens names (on launch and after any display change), and handle visibility of information on params window
        /// </summary>
        public void updateScreenNames()
        {
            String setImage = BlackScreens.Properties.Resources.setImage;
            String stopUsingImage = BlackScreens.Properties.Resources.stopUsingImage;
            CommonData.dataInstance.LogToFile.Debug("updateScreenNames()");
            IReadOnlyList<MonitorInfo> monitors = Monitors.GetAll();
            int nbScreens = monitors.Count;
            for (int screenNumber = 1; screenNumber <= CommonData.MaxScreens; screenNumber++)
            {
                MonitorInfo monitor = (screenNumber <= nbScreens) ? monitors[screenNumber - 1] : null;
                CommonData.dataInstance.SetScreenName(screenNumber, monitor?.FriendlyName ?? "");
                CommonData.dataInstance.SetScreenTooltipData(screenNumber, monitor?.Bounds ?? new System.Drawing.Rectangle());
            }
            // Update label for image usage, instead of black color
            tbScreen1SetImage.Text = String.IsNullOrEmpty(CommonData.dataInstance.ImageFileNameScreen1) ? setImage : stopUsingImage;
            tbScreen2SetImage.Text = String.IsNullOrEmpty(CommonData.dataInstance.ImageFileNameScreen2) ? setImage : stopUsingImage;
            tbScreen3SetImage.Text = String.IsNullOrEmpty(CommonData.dataInstance.ImageFileNameScreen3) ? setImage : stopUsingImage;

            tbScreen2AltKeyInfo.Visibility = (nbScreens > 1) ? Visibility.Visible : Visibility.Hidden;
            tbScreen2Name.Visibility = (nbScreens > 1) ? Visibility.Visible : Visibility.Collapsed;
            tbScreen2SetImage.Visibility = (nbScreens > 1) ? Visibility.Visible : Visibility.Collapsed;
            tbScreen3AltKeyInfo.Visibility = (nbScreens > 2) ? Visibility.Visible : Visibility.Hidden;
            tbScreen3Name.Visibility = (nbScreens > 2) ? Visibility.Visible : Visibility.Collapsed;
            tbScreen3SetImage.Visibility = (nbScreens > 2) ? Visibility.Visible : Visibility.Collapsed;
            tbScreen4AltKeyInfo.Visibility = (nbScreens > 3) ? Visibility.Visible : Visibility.Hidden;
            // Collapsed used to hide complete line (screen 4,5,6) if there is not at least 4 screens used (presume large majority of users cases!)
            tbScreen4Name.Visibility = (nbScreens > 3) ? Visibility.Visible : Visibility.Collapsed;
            tbScreen5AltKeyInfo.Visibility = (nbScreens > 4) ? Visibility.Visible : Visibility.Collapsed;
            tbScreen5Name.Visibility = (nbScreens > 4) ? Visibility.Visible : Visibility.Collapsed;
            tbScreen6AltKeyInfo.Visibility = (nbScreens > 5) ? Visibility.Visible : Visibility.Collapsed;
            tbScreen6Name.Visibility = (nbScreens > 5) ? Visibility.Visible : Visibility.Collapsed;
        }

        public void showWindow()
        {
            // Only stay above other windows while a black window could cover this one
            this.Topmost = CommonData.dataInstance.BlackWindows.Any(bw => (bw != null) && bw.IsVisible);
            this.WindowState = WindowState.Normal;
            this.ShowInTaskbar = true;
            this.Show();
            this.Activate();
        }

        private void OnMinimizeButtonClick(object sender, RoutedEventArgs e)
        {
            minimizeWindow();
        }

        private void OnCloseButtonClick(object sender, RoutedEventArgs e)
        {
            // X keeps the app (and its hotkeys) running in the tray; Exit is in the tray menu and on the Exit button
            minimizeWindow();
            if (!CommonData.dataInstance.HideToTrayHintShown)
            {
                notifyIcon.ShowBalloonTip("BlackScreens is still running", "Hotkeys keep working. Use the tray icon to open settings or exit.", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
                CommonData.dataInstance.HideToTrayHintShown = true;
                CommonData.dataInstance.saveUserConfigFiles();
            }
        }

        private void sOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            CommonData.dataInstance.updateAllBlackWindowParams();
            CommonData.dataInstance.scheduleSave();
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Normal)
            {
                this.Width = 800;
                this.ShowInTaskbar = true;
                refreshMainWindowCurrentScreen();
            }
            else
                this.ShowInTaskbar = false;
        }

        private void bExit_Click(object sender, RoutedEventArgs e)
        {
            App.Quit();
        }

        private void bMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
            this.ShowInTaskbar = false;
        }

        void MainWindow_Resized(object sender, EventArgs e)
        {
            // In case param Window is moved by user along different screens
            refreshMainWindowCurrentScreen();
        }

        /// <summary>
        /// Windows system rezized used to handle EXIT MOVE by user, which is not handle by any WPF window native event
        /// </summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            //const int WM_ENTERSIZEMOVE = 0x0231;
            const int WM_EXITSIZEMOVE = 0x0232;

            if (msg == WM_EXITSIZEMOVE)
            {
                if (Resized != null)
                {
                    Resized(this, EventArgs.Empty);
                }
            }

            return IntPtr.Zero;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Hook for windows system resized by user only on EXIT MOVE
            // In order to have less event possible, compared to WPF native move event which launch nearly each mouse pixel move
            HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source.AddHook(new HwndSourceHook(WndProc));
            this.Resized += MainWindow_Resized;
        }

        private void notifyIcon_TrayRightMouseDown(object sender, RoutedEventArgs e)
        {
            App.Safe(() =>
            {
                if (!IsShownToUser)
                {
                    miShowHideParameters.Header = "Show Parameters";
                }
                else
                    miShowHideParameters.Header = "Hide Parameters";
                rebuildTrayActions();
            }, "notifyIcon_TrayRightMouseDown()");
        }

        public void toggleShowHideWindow()
        {
            if (!IsShownToUser)
            {
                showWindow();
            }
            else
                minimizeWindow();
        }

        private void miShowHideParameters_Click(object sender, RoutedEventArgs e)
        {
            toggleShowHideWindow();
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if ((e.Key == Key.Down) || (e.Key == Key.Left))
            {
                sOpacity.Value--;
            }

            if ((e.Key == Key.Up) || (e.Key == Key.Right))
            {
                sOpacity.Value++;
            }
            e.Handled = true;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            CommonData.dataInstance.saveUserConfigFiles();
            // Alt+F4 or similar while the app keeps running: hide instead, a closed window can't be shown again
            if (!App.IsShuttingDown)
            {
                e.Cancel = true;
                minimizeWindow();
            }
        }

        private String debugOneScreenToString(System.Drawing.Rectangle r, String screenName, int screenNumber)
        {
            String txt = "";
            txt += "Screen " + screenNumber + " : " + screenName + Environment.NewLine;
            txt += "Left " + r.Left + " / X = " + r.X + Environment.NewLine;
            txt += "Top " + r.Top + " / Y = " + r.Y + Environment.NewLine;
            txt += "Right " + r.Right + " / Width = " + r.Width + Environment.NewLine;
            txt += "Bottom " + r.Bottom + " / Height = " + r.Height + Environment.NewLine;
            txt += "Size.Height " + r.Size.Height + " / Size.Width = " + r.Size.Width + Environment.NewLine;
            txt += "Location.X " + r.Location.X + " / Location.Y = " + r.Location.Y + Environment.NewLine + Environment.NewLine;

            return txt;
        }

        /// <summary>
        ///  Debug all screens positioning information in ClipBoard
        /// </summary>
        private void debugAllScreensInClipboard()
        {
            String debugTxt = "";
            IReadOnlyList<MonitorInfo> monitors = Monitors.GetAll();
            for (int iMonitor = 0; iMonitor < monitors.Count; iMonitor++)
            {
                debugTxt += debugOneScreenToString(monitors[iMonitor].Bounds, monitors[iMonitor].FriendlyName, iMonitor + 1);
                debugTxt += "Device " + monitors[iMonitor].DeviceName + " / " + monitors[iMonitor].DevicePath + Environment.NewLine + Environment.NewLine;
            }
            System.Windows.Clipboard.SetText(debugTxt);
        }

        private void miAbout_Click(object sender, RoutedEventArgs e)
        {
            // @TODO
            spUpdatesHistory.Visibility = Visibility.Visible;
        }

        private void tbScreen1Name_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            debugAllScreensInClipboard();
        }

        private void expUpdatesHistory_Collapsed(object sender, RoutedEventArgs e)
        {
            spUpdatesHistory.Visibility = Visibility.Collapsed;
            expUpdatesHistory.IsExpanded = true;
        }

        private void Image_MouseDown(object sender, MouseButtonEventArgs e)
        {
            spUpdatesHistory.Visibility = spUpdatesHistory.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

         private void Hyperlink_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Hyperlink hl = (Hyperlink)sender;
            // .NET (Core) no longer shell-executes by default, which is needed to open a URL
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(hl.NavigateUri.AbsoluteUri) { UseShellExecute = true });
        }

        private void cbClickThrough_Click(object sender, RoutedEventArgs e)
        {
            CommonData.dataInstance.updateAllBlackWindowParams();
            CommonData.dataInstance.scheduleSave();
        }

        private void cbShowTexts_Click(object sender, RoutedEventArgs e)
        {
            CommonData.dataInstance.scheduleSave();
        }

        private void cbHideOnTaskBarOnStart_Click(object sender, RoutedEventArgs e)
        {
            CommonData.dataInstance.updateAllBlackWindowParams();
            CommonData.dataInstance.scheduleSave();
        }

        private void changeImage(TextBlock tbImageButton, string screenName, out String imageName)
        {
            imageName = "";
            if (tbImageButton.Text == "Set image")
            {
                System.Windows.Forms.OpenFileDialog openFileDialog = new System.Windows.Forms.OpenFileDialog();
                openFileDialog.Filter = "Image Files (*.png;*.jpg)|*.png;*.jpg";
                openFileDialog.Title = "Image file to use for "+ screenName;
                if (openFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    imageName = openFileDialog.FileName;
                    tbImageButton.Text = "Stop using image";
                }
            }
            else
            {
                tbImageButton.Text = "Set image";
            }
        }

        private void tbScreen1SetImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            String imageFileName = CommonData.dataInstance.ImageFileNameScreen1;
            changeImage(tbScreen1SetImage, "screen 1", out imageFileName);
            CommonData.dataInstance.ImageFileNameScreen1 = imageFileName;
            CommonData.dataInstance.updateAllBlackWindowParams();
            CommonData.dataInstance.scheduleSave();
        }

        private void tbScreen2SetImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            String imageFileName = CommonData.dataInstance.ImageFileNameScreen2;
            changeImage(tbScreen2SetImage, "screen 2", out imageFileName);
            CommonData.dataInstance.ImageFileNameScreen2 = imageFileName;
            CommonData.dataInstance.updateAllBlackWindowParams();
            CommonData.dataInstance.scheduleSave();
        }

        private void tbScreen3SetImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            String imageFileName = CommonData.dataInstance.ImageFileNameScreen3;
            changeImage(tbScreen3SetImage, "screen 3", out imageFileName);
            CommonData.dataInstance.ImageFileNameScreen3 = imageFileName;
            CommonData.dataInstance.updateAllBlackWindowParams();
            CommonData.dataInstance.scheduleSave();
        }
    }
}
