using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BlackScreensWPF
{
    /// <summary>
    /// Hotkey editor and tray menu actions of the parameters window
    /// </summary>
    public partial class MainWindow
    {
        private const string RecordingText = "Press keys...";
        private readonly Dictionary<string, System.Windows.Controls.TextBox> hotkeyBoxes = new Dictionary<string, System.Windows.Controls.TextBox>();
        private readonly Dictionary<string, TextBlock> hotkeyStatus = new Dictionary<string, TextBlock>();
        private readonly List<System.Windows.Controls.MenuItem> trayActionItems = new List<System.Windows.Controls.MenuItem>();

        /// <summary>
        /// One row per action: label, recording box, status (e.g. "Already used by another app")
        /// </summary>
        private void buildHotkeyEditor()
        {
            int row = 0;
            foreach (string action in HotkeyActions.List())
            {
                gridHotkeys.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                TextBlock label = new TextBlock { Text = HotkeyActions.Label(action), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
                Grid.SetRow(label, row);
                gridHotkeys.Children.Add(label);

                System.Windows.Controls.TextBox box = new System.Windows.Controls.TextBox
                {
                    Tag = action,
                    FontSize = 14,
                    IsReadOnly = true,
                    IsReadOnlyCaretVisible = false,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x26, 0x26, 0x26)),
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55)),
                    Padding = new Thickness(4, 1, 4, 1),
                    Margin = new Thickness(0, 2, 10, 2)
                };
                box.GotKeyboardFocus += HotkeyBox_GotKeyboardFocus;
                box.LostKeyboardFocus += HotkeyBox_LostKeyboardFocus;
                box.PreviewKeyDown += HotkeyBox_PreviewKeyDown;
                Grid.SetRow(box, row);
                Grid.SetColumn(box, 1);
                gridHotkeys.Children.Add(box);
                hotkeyBoxes[action] = box;

                TextBlock status = new TextBlock { FontSize = 12, Foreground = System.Windows.Media.Brushes.Orange, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(status, row);
                Grid.SetColumn(status, 2);
                gridHotkeys.Children.Add(status);
                hotkeyStatus[action] = status;

                row++;
            }
        }

        /// <summary>
        /// Refresh everything showing a hotkey: editor boxes and errors, and the per screen labels
        /// </summary>
        public void updateHotkeyTexts()
        {
            Dictionary<string, string> errors = App.Instance.HotkeyErrors;
            foreach (string action in HotkeyActions.List())
            {
                string text = CommonData.dataInstance.Hotkeys.GetValueOrDefault(action, "");
                if (hotkeyBoxes.TryGetValue(action, out System.Windows.Controls.TextBox box) && !box.IsKeyboardFocused)
                    box.Text = text;
                if (hotkeyStatus.TryGetValue(action, out TextBlock status))
                    status.Text = errors.TryGetValue(action, out string error) ? "⚠ " + error : "";
            }

            TextBlock[] screenLabels = { tbScreen1AltKeyInfo, tbScreen2AltKeyInfo, tbScreen3AltKeyInfo, tbScreen4AltKeyInfo, tbScreen5AltKeyInfo, tbScreen6AltKeyInfo };
            for (int screenNumber = 1; screenNumber <= screenLabels.Length; screenNumber++)
            {
                string text = CommonData.dataInstance.Hotkeys.GetValueOrDefault(HotkeyActions.Screen(screenNumber), "");
                screenLabels[screenNumber - 1].Text = String.IsNullOrEmpty(text)
                    ? "Screen " + screenNumber + ": no hotkey"
                    : text + " : switch screen " + screenNumber;
            }
        }

        private void HotkeyBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // Release the global hotkeys so pressing the current combination reaches this box
            App.Safe(() => App.Instance.suspendHotkeys(), "suspendHotkeys()");
            ((System.Windows.Controls.TextBox)sender).Text = RecordingText;
        }

        private void HotkeyBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            App.Safe(() => App.Instance.applyHotkeys(), "applyHotkeys()");
        }

        private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            e.Handled = true;
            System.Windows.Controls.TextBox box = (System.Windows.Controls.TextBox)sender;
            string action = (string)box.Tag;
            // With Alt held WPF reports the real key in SystemKey
            Key key = (e.Key == Key.System) ? e.SystemKey : e.Key;
            ModifierKeys modifiers = Keyboard.Modifiers;

            if (key == Key.Escape && modifiers == ModifierKeys.None)
            {
                endRecording();
                return;
            }
            if ((key == Key.Back || key == Key.Delete) && modifiers == ModifierKeys.None)
            {
                setHotkey(action, "");
                return;
            }
            if (Hotkey.IsModifierKey(key))
            {
                // Show the modifiers while the user is building the combination
                string partial = new Hotkey(modifiers, Key.A).ToString();
                box.Text = partial.Substring(0, partial.Length - 1) + "...";
                return;
            }
            Hotkey hotkey = new Hotkey(modifiers, key);
            if (!hotkey.IsValid || (modifiers == ModifierKeys.Alt && key == Key.F4))
            {
                hotkeyStatus[action].Text = "⚠ Add Ctrl, Alt, Shift or Win (or use an F-key)";
                return;
            }
            setHotkey(action, hotkey.ToString());
        }

        private void setHotkey(string action, string hotkeyText)
        {
            CommonData.dataInstance.Hotkeys[action] = hotkeyText;
            CommonData.dataInstance.scheduleSave();
            hotkeyBoxes[action].Text = hotkeyText;
            endRecording();
        }

        /// <summary>
        /// Leave the recording box; losing focus registers the hotkeys again
        /// </summary>
        private void endRecording()
        {
            // Forget the box as the window's focused element too, otherwise WPF hands focus straight back to
            // it and the next press (even of the new hotkey) is recorded again instead of triggering
            FocusManager.SetFocusedElement(this, null);
            Keyboard.ClearFocus();
            Keyboard.Focus(this);
        }

        private void tbResetHotkeys_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            CommonData.dataInstance.Hotkeys = HotkeyActions.Defaults();
            CommonData.dataInstance.scheduleSave();
            App.Instance.applyHotkeys();
        }

        /// <summary>
        /// Tray menu: one checkable item per connected screen, then "all" and "all but mouse's screen"
        /// </summary>
        private void rebuildTrayActions()
        {
            System.Windows.Controls.ContextMenu menu = notifyIcon.ContextMenu;
            foreach (System.Windows.Controls.MenuItem item in trayActionItems)
                menu.Items.Remove(item);
            trayActionItems.Clear();

            IReadOnlyList<MonitorInfo> monitors = Monitors.GetAll();
            List<(string action, string header, bool isChecked)> entries = new List<(string, string, bool)>();
            for (int screenNumber = 1; screenNumber <= monitors.Count && screenNumber <= CommonData.MaxScreens; screenNumber++)
                entries.Add((HotkeyActions.Screen(screenNumber), "Black out screen " + screenNumber + " (" + monitors[screenNumber - 1].FriendlyName + ")", App.Instance.IsScreenBlack(screenNumber)));
            entries.Add((HotkeyActions.All, "Black out all screens", false));
            entries.Add((HotkeyActions.Others, "Black out all but this screen", false));

            int insertAt = menu.Items.IndexOf(sepTrayActions);
            foreach (var entry in entries)
            {
                string hotkeyText = CommonData.dataInstance.Hotkeys.GetValueOrDefault(entry.action, "");
                System.Windows.Controls.MenuItem item = new System.Windows.Controls.MenuItem
                {
                    Header = entry.header,
                    InputGestureText = hotkeyText,
                    IsCheckable = false,
                    IsChecked = entry.isChecked,
                    Tag = entry.action
                };
                item.Click += TrayActionItem_Click;
                menu.Items.Insert(insertAt++, item);
                trayActionItems.Add(item);
            }
        }

        private void TrayActionItem_Click(object sender, RoutedEventArgs e)
        {
            string action = (string)((System.Windows.Controls.MenuItem)sender).Tag;
            App.Safe(() => App.Instance.RunAction(action, "toggle"), "tray " + action);
        }
    }
}
