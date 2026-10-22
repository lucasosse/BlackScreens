using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace BlackScreensWPF
{
    public class CommonData : INotifyPropertyChanged
    {
        /// <summary>Hotkeys and UI support screens 1 to 6</summary>
        public const int MaxScreens = 6;

        public static CommonData dataInstance = new CommonData();

        private int opacity = 90;
        private readonly string[] screenNames = { "", "", "", "", "", "" };
        private readonly Rectangle[] screenTooltipData = new Rectangle[MaxScreens];
        private string imageFileNameScreen1 = "";
        private string imageFileNameScreen2 = "";
        private string imageFileNameScreen3 = "";
        private string imageFileNameScreen4 = "";
        private string imageFileNameScreen5 = "";
        private string imageFileNameScreen6 = "";
        private string paramsScreenDeviceName = "";
        private MainWindow fParams = null;
        // One slot per screen number, independent of how many monitors are connected right now
        private BlackWindow[] blackWindows = new BlackWindow[MaxScreens];
        private bool hideTexts = true;
        private bool clickThrough = false;
        private bool reduceAppOnLaunch = false;
        private bool firstAppLaunch = true;
        private bool hideToTrayHintShown = false;
        private Dictionary<string, string> hotkeys = HotkeyActions.Defaults();
        private int msDelayMouseCursorHide = 3000;
        private Logger logToFile = null;
        private string optionFileLocation = "";
        private String appDataBaseFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        private String appDataCompleteFolderUri = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\BlackScreens";

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        // Users options
        public int Opacity { get => opacity; set => opacity = value; }
        public bool HideTexts { get => hideTexts; set => hideTexts = value; }
        public bool ClickThrough { get => clickThrough; set => clickThrough = value; }
        public bool ReduceAppOnLaunch { get => reduceAppOnLaunch; set => reduceAppOnLaunch = value; }
        public bool FirstAppLaunch { get => firstAppLaunch; set => firstAppLaunch = value; }
        public bool HideToTrayHintShown { get => hideToTrayHintShown; set => hideToTrayHintShown = value; }
        /// <summary>Hotkey text per action (see HotkeyActions), "" = disabled</summary>
        public Dictionary<string, string> Hotkeys { get => hotkeys; set => hotkeys = value; }

        /// <summary>Screen name shown in the parameters window, screenNumber from 1 to 6</summary>
        public void SetScreenName(int screenNumber, string name)
        {
            screenNames[screenNumber - 1] = name ?? "";
            OnPropertyChanged("Screen" + screenNumber + "Name");
        }

        /// <summary>Screen bounds shown in the parameters window tooltips, screenNumber from 1 to 6</summary>
        public void SetScreenTooltipData(int screenNumber, Rectangle bounds)
        {
            screenTooltipData[screenNumber - 1] = bounds;
            OnPropertyChanged("Screen" + screenNumber + "TooltipData");
        }

        public string GetScreenName(int screenNumber) => screenNames[screenNumber - 1];
        public Rectangle GetScreenTooltipData(int screenNumber) => screenTooltipData[screenNumber - 1];

        // Bound by name in MainWindow.xaml
        public string Screen1Name => screenNames[0];
        public string Screen2Name => screenNames[1];
        public string Screen3Name => screenNames[2];
        public string Screen4Name => screenNames[3];
        public string Screen5Name => screenNames[4];
        public string Screen6Name => screenNames[5];
        public Rectangle Screen1TooltipData => screenTooltipData[0];
        public Rectangle Screen2TooltipData => screenTooltipData[1];
        public Rectangle Screen3TooltipData => screenTooltipData[2];
        public Rectangle Screen4TooltipData => screenTooltipData[3];
        public Rectangle Screen5TooltipData => screenTooltipData[4];
        public Rectangle Screen6TooltipData => screenTooltipData[5];

        public String ImageFileNameScreen1 { get => imageFileNameScreen1; set => imageFileNameScreen1 = value; }
        public String ImageFileNameScreen2 { get => imageFileNameScreen2; set => imageFileNameScreen2 = value; }
        public String ImageFileNameScreen3 { get => imageFileNameScreen3; set => imageFileNameScreen3 = value; }
        public String ImageFileNameScreen4 { get => imageFileNameScreen4; set => imageFileNameScreen4 = value; }
        public String ImageFileNameScreen5 { get => imageFileNameScreen5; set => imageFileNameScreen5 = value; }
        public String ImageFileNameScreen6 { get => imageFileNameScreen6; set => imageFileNameScreen6 = value; }
        // Keep MainWindow handle, use in BlackWindows and App.cs
        public MainWindow FParams { get => fParams; set => fParams = value; }
        // BlackWindow used for each screen number (index 0 = screen 1)
        public BlackWindow[] BlackWindows { get => blackWindows; set => blackWindows = value; }
        public string ParamsScreenDeviceName { get => paramsScreenDeviceName; set => paramsScreenDeviceName = value; }
        public int MsDelayMouseCursorHide { get => msDelayMouseCursorHide; set => msDelayMouseCursorHide = value; }
        public string OptionFileLocation { get => optionFileLocation; set => optionFileLocation = value; }
        public string AppDataBaseFolder { get => appDataBaseFolder; set => appDataBaseFolder = value; }
        public string AppDataCompleteFolderUri { get => appDataCompleteFolderUri; set => appDataCompleteFolderUri = value; }
        public Logger LogToFile
        {
            get => logToFile; set => logToFile = value;
        }

        static CommonData()
        {
            if (!Directory.Exists(dataInstance.AppDataCompleteFolderUri))
            {
                Directory.CreateDirectory(dataInstance.AppDataCompleteFolderUri);
            }
            var logFactory = new LogFactory();
            String fileConfigLogUri = dataInstance.AppDataCompleteFolderUri + "\\NLog.config";
            if (File.Exists(fileConfigLogUri))
            {
                // Custom logging config (e.g. Debug level) dropped in by the user
                logFactory.Setup().LoadConfigurationFromFile(fileConfigLogUri);
            }
            else
            {
                // Default: always keep a small log of starts, warnings and errors
                LoggingConfiguration config = new LoggingConfiguration();
                FileTarget fileTarget = new FileTarget("logfile")
                {
                    FileName = dataInstance.AppDataCompleteFolderUri + "\\BlackScreens.log",
                    Layout = "${longdate} ${level:uppercase=true} ${message}${onexception:${newline}${exception:format=tostring}}",
                    ArchiveAboveSize = 1_000_000,
                    MaxArchiveFiles = 2
                };
                config.AddRule(LogLevel.Info, LogLevel.Fatal, fileTarget);
                logFactory.Configuration = config;
            }
            dataInstance.LogToFile = logFactory.GetCurrentClassLogger();

            dataInstance.LogToFile.Debug("Creating CommonData static instance");
        }

        /// <summary>
        /// Update all BlackWindows Opacity parameter
        /// </summary>
        public void updateAllBlackWindowParams()
        {
            foreach (BlackWindow bw in blackWindows)
            {
                if (bw != null)
                    bw.updateBlackWindowParams();
            }
        }

        private System.Windows.Threading.DispatcherTimer saveTimer;

        /// <summary>
        /// Save preferences shortly after a change (debounced: the opacity slider fires on every step),
        /// so nothing is lost if Windows shuts down or the app is killed. Call from the UI thread.
        /// </summary>
        public void scheduleSave()
        {
            if (saveTimer == null)
            {
                saveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                saveTimer.Tick += (s, e) =>
                {
                    saveTimer.Stop();
                    saveUserConfigFiles();
                };
            }
            saveTimer.Stop();
            saveTimer.Start();
        }

        /// <summary>
        /// Save user config file by XmlSerializer
        /// </summary>
        public void saveUserConfigFiles()
        {
            UserPreferences up = new UserPreferences
            {
                Opacity = CommonData.dataInstance.Opacity,
                ShowTextsOnBlackScreens = !CommonData.dataInstance.HideTexts,
                ClickThrough = CommonData.dataInstance.ClickThrough,
                MsDelayMouseCursorHide = CommonData.dataInstance.MsDelayMouseCursorHide,
                FirstAppLaunch = CommonData.dataInstance.FirstAppLaunch,
                ReduceAppOnLaunch = CommonData.dataInstance.ReduceAppOnLaunch,
                HideToTrayHintShown = CommonData.dataInstance.HideToTrayHintShown,
                ImageFileNameScreen1 = CommonData.dataInstance.ImageFileNameScreen1,
                ImageFileNameScreen2 = CommonData.dataInstance.ImageFileNameScreen2,
                ImageFileNameScreen3 = CommonData.dataInstance.ImageFileNameScreen3,
                ImageFileNameScreen4 = CommonData.dataInstance.ImageFileNameScreen4,
                ImageFileNameScreen5 = CommonData.dataInstance.ImageFileNameScreen5,
                ImageFileNameScreen6 = CommonData.dataInstance.ImageFileNameScreen6,
                Hotkeys = CommonData.dataInstance.Hotkeys.Select(h => new HotkeyPref { Action = h.Key, Keys = h.Value }).ToList()
            };

            XmlSerializer mySerializer = new XmlSerializer(typeof(UserPreferences));
            try
            {
                using (StreamWriter myWriter = new StreamWriter(AppDataCompleteFolderUri + "\\BlackScreensPrefs.xml"))
                {
                    mySerializer.Serialize(myWriter, up);
                }
            }
            catch (Exception ex)
            {
                LogToFile.Warn(ex, "saveUserConfigFiles() failed");
            }
        }

        /// <summary>
        /// Load user config file by XmlSerializer
        /// </summary>
        public void loadUserConfigFile(MainWindow mainWindow)
        {
            UserPreferences up;

            CommonData.dataInstance.OptionFileLocation = AppDataCompleteFolderUri + "\\BlackScreensPrefs.xml";
            XmlSerializer mySerializer = new XmlSerializer(typeof(UserPreferences));

            CommonData.dataInstance.LogToFile.Debug("loadUserConfigFile()");

            // Default values
            CommonData.dataInstance.ClickThrough = false;
            CommonData.dataInstance.HideTexts = false;
            CommonData.dataInstance.Opacity = 90;
            CommonData.dataInstance.MsDelayMouseCursorHide = 3000;
            // New installs start in the tray (the window still opens on the very first launch)
            CommonData.dataInstance.ReduceAppOnLaunch = true;
            CommonData.dataInstance.FirstAppLaunch = true;
            CommonData.dataInstance.HideToTrayHintShown = false;
            CommonData.dataInstance.Hotkeys = HotkeyActions.Defaults();
            CommonData.dataInstance.ImageFileNameScreen1 = "";
            CommonData.dataInstance.ImageFileNameScreen2 = "";
            CommonData.dataInstance.ImageFileNameScreen3 = "";
            CommonData.dataInstance.ImageFileNameScreen4 = "";
            CommonData.dataInstance.ImageFileNameScreen5 = "";
            CommonData.dataInstance.ImageFileNameScreen6 = "";

            if (!File.Exists(CommonData.dataInstance.OptionFileLocation))
                return;
            try
            {
                using (FileStream myFileStream = new FileStream(CommonData.dataInstance.OptionFileLocation, FileMode.Open, FileAccess.Read))
                {
                    up = (UserPreferences)mySerializer.Deserialize(myFileStream);
                }
                CommonData.dataInstance.Opacity = up.Opacity;
                CommonData.dataInstance.HideTexts = !up.ShowTextsOnBlackScreens;
                CommonData.dataInstance.ClickThrough = up.ClickThrough;
                CommonData.dataInstance.ReduceAppOnLaunch = up.ReduceAppOnLaunch;
                CommonData.dataInstance.FirstAppLaunch = up.FirstAppLaunch;
                CommonData.dataInstance.HideToTrayHintShown = up.HideToTrayHintShown;
                CommonData.dataInstance.MsDelayMouseCursorHide = up.MsDelayMouseCursorHide;
                CommonData.dataInstance.ImageFileNameScreen1 = up.ImageFileNameScreen1;
                CommonData.dataInstance.ImageFileNameScreen2 = up.ImageFileNameScreen2;
                CommonData.dataInstance.ImageFileNameScreen3 = up.ImageFileNameScreen3;
                CommonData.dataInstance.ImageFileNameScreen4 = up.ImageFileNameScreen4;
                CommonData.dataInstance.ImageFileNameScreen5 = up.ImageFileNameScreen5;
                CommonData.dataInstance.ImageFileNameScreen6 = up.ImageFileNameScreen6;
                if (up.Hotkeys != null)
                {
                    foreach (HotkeyPref hotkeyPref in up.Hotkeys)
                    {
                        // Unknown actions (from a newer version) are ignored
                        if (CommonData.dataInstance.Hotkeys.ContainsKey(hotkeyPref.Action ?? ""))
                            CommonData.dataInstance.Hotkeys[hotkeyPref.Action] = hotkeyPref.Keys ?? "";
                    }
                }
            }
            catch (Exception ex)
            {
                LogToFile.Warn(ex, "loadUserConfigFile() failed, using defaults");
            }
        }
    }
}
