using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace BlackScreensWPF
{
    /// <summary>
    /// Everything a hotkey, tray item or command line can trigger
    /// </summary>
    public static class HotkeyActions
    {
        public const string All = "all";
        /// <summary>Black out every screen except the one under the mouse</summary>
        public const string Others = "others";

        public static string Screen(int screenNumber) => "screen" + screenNumber;

        /// <summary>Editable actions in display order</summary>
        public static IEnumerable<string> List()
        {
            for (int i = 1; i <= CommonData.MaxScreens; i++)
                yield return Screen(i);
            yield return All;
            yield return Others;
        }

        public static string Label(string action)
        {
            if (action == All) return "All screens";
            if (action == Others) return "All but the mouse's screen";
            return "Screen " + action.Substring("screen".Length);
        }

        /// <summary>Default hotkeys; screens 3 to 6 are unset so nothing is grabbed for monitors most people don't have</summary>
        public static Dictionary<string, string> Defaults() => new Dictionary<string, string>
        {
            { Screen(1), "Ctrl+Alt+F1" },
            { Screen(2), "Ctrl+Alt+F2" },
            { Screen(3), "" },
            { Screen(4), "" },
            { Screen(5), "" },
            { Screen(6), "" },
            { All, "Ctrl+Alt+F12" },
            { Others, "Ctrl+Alt+F11" },
        };
    }

    /// <summary>
    /// A key combination like Ctrl+Alt+F1
    /// </summary>
    public readonly struct Hotkey : IEquatable<Hotkey>
    {
        public ModifierKeys Modifiers { get; }
        public Key Key { get; }
        public bool IsEmpty => Key == Key.None;

        public Hotkey(ModifierKeys modifiers, Key key)
        {
            Modifiers = modifiers;
            Key = key;
        }

        /// <summary>
        /// A combination worth registering globally: needs a modifier, except for keys nobody types with
        /// (F-keys, Pause, Scroll Lock)
        /// </summary>
        public bool IsValid
        {
            get
            {
                if (IsEmpty || IsModifierKey(Key))
                    return false;
                if (Modifiers != ModifierKeys.None)
                    return true;
                return (Key >= Key.F1 && Key <= Key.F24) || Key == Key.Pause || Key == Key.Scroll;
            }
        }

        public static bool IsModifierKey(Key key) =>
            key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt ||
            key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin || key == Key.System;

        public override string ToString()
        {
            if (IsEmpty)
                return "";
            List<string> parts = new List<string>();
            if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            parts.Add(KeyName(Key));
            return String.Join("+", parts);
        }

        private static string KeyName(Key key)
        {
            if (key >= Key.D0 && key <= Key.D9) return ((int)(key - Key.D0)).ToString();
            if (key >= Key.NumPad0 && key <= Key.NumPad9) return "Num" + (int)(key - Key.NumPad0);
            if (key == Key.Scroll) return "ScrollLock";
            return key.ToString();
        }

        /// <summary>
        /// Parse "Ctrl+Alt+F1"; empty or invalid text gives an empty hotkey
        /// </summary>
        public static Hotkey Parse(string text)
        {
            if (String.IsNullOrWhiteSpace(text))
                return default;
            ModifierKeys modifiers = ModifierKeys.None;
            Key key = Key.None;
            foreach (string rawPart in text.Split('+'))
            {
                string part = rawPart.Trim();
                switch (part.ToLowerInvariant())
                {
                    case "ctrl": case "control": modifiers |= ModifierKeys.Control; continue;
                    case "shift": modifiers |= ModifierKeys.Shift; continue;
                    case "alt": modifiers |= ModifierKeys.Alt; continue;
                    case "win": case "windows": modifiers |= ModifierKeys.Windows; continue;
                }
                if (part.Length == 1 && Char.IsDigit(part[0]))
                    key = Key.D0 + (part[0] - '0');
                else if (part.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && part.Length == 4 && Char.IsDigit(part[3]))
                    key = Key.NumPad0 + (part[3] - '0');
                else if (String.Equals(part, "ScrollLock", StringComparison.OrdinalIgnoreCase))
                    key = Key.Scroll;
                else if (!Enum.TryParse(part, true, out key))
                    return default;
            }
            return new Hotkey(modifiers, key);
        }

        public bool Equals(Hotkey other) => Modifiers == other.Modifiers && Key == other.Key;
        public override bool Equals(object obj) => obj is Hotkey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Modifiers, Key);
    }

    /// <summary>
    /// System wide hotkeys through RegisterHotKey. Unlike the old low level keyboard hook, Windows tracks the
    /// modifiers itself, nothing can time out, and a combination already owned by another app is reported.
    /// </summary>
    public sealed class HotkeyManager : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

        private readonly HwndSource source;
        private readonly Dictionary<int, string> actionsById = new Dictionary<int, string>();
        private int nextId = 1;

        /// <summary>Raised on the UI thread with the action of the pressed hotkey</summary>
        public event Action<string> HotkeyPressed;

        public HotkeyManager()
        {
            // Message-only window: receives WM_HOTKEY, never shown
            HwndSourceParameters parameters = new HwndSourceParameters("BlackScreensHotkeys") { ParentWindow = new IntPtr(-3), WindowStyle = 0 };
            source = new HwndSource(parameters);
            source.AddHook(WndProc);
        }

        /// <summary>
        /// Replace all registered hotkeys. Returns, per action, an error message for the ones that could not be
        /// registered (invalid, duplicated, or already used by another application).
        /// </summary>
        public Dictionary<string, string> RegisterAll(IReadOnlyDictionary<string, string> hotkeysByAction)
        {
            UnregisterAll();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            HashSet<Hotkey> used = new HashSet<Hotkey>();
            foreach (KeyValuePair<string, string> entry in hotkeysByAction)
            {
                if (String.IsNullOrWhiteSpace(entry.Value))
                    continue;
                Hotkey hotkey = Hotkey.Parse(entry.Value);
                if (!hotkey.IsValid)
                {
                    errors[entry.Key] = "Not a valid hotkey";
                    continue;
                }
                if (!used.Add(hotkey))
                {
                    errors[entry.Key] = "Used twice";
                    continue;
                }
                int id = nextId++;
                if (RegisterHotKey(source.Handle, id, ToNativeModifiers(hotkey.Modifiers) | MOD_NOREPEAT, (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key)))
                {
                    actionsById[id] = entry.Key;
                }
                else
                {
                    errors[entry.Key] = "Already used by another app";
                    CommonData.dataInstance.LogToFile.Warn("RegisterHotKey failed for " + hotkey + " (" + entry.Key + "), error " + Marshal.GetLastWin32Error());
                }
            }
            CommonData.dataInstance.LogToFile.Info("Hotkeys registered: " + String.Join(", ",
                actionsById.Values.Select(a => a + "=" + hotkeysByAction[a])));
            return errors;
        }

        public void UnregisterAll()
        {
            foreach (int id in actionsById.Keys)
                UnregisterHotKey(source.Handle, id);
            actionsById.Clear();
        }

        private static uint ToNativeModifiers(ModifierKeys modifiers)
        {
            uint result = 0;
            if (modifiers.HasFlag(ModifierKeys.Alt)) result |= MOD_ALT;
            if (modifiers.HasFlag(ModifierKeys.Control)) result |= MOD_CONTROL;
            if (modifiers.HasFlag(ModifierKeys.Shift)) result |= MOD_SHIFT;
            if (modifiers.HasFlag(ModifierKeys.Windows)) result |= MOD_WIN;
            return result;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && actionsById.TryGetValue(wParam.ToInt32(), out string action))
            {
                handled = true;
                HotkeyPressed?.Invoke(action);
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            UnregisterAll();
            source.RemoveHook(WndProc);
            source.Dispose();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
