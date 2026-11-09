using System;
using System.Collections.Generic;
using System.Text;

namespace ServerAssistMod
{
    /// <summary>
    /// Line protocol used between the ServerAssist Oxide plugin and this client mod.
    /// Every dynamic field is percent-escaped so the pipe delimiter stays unambiguous.
    /// </summary>
    public static class ServerAssistProtocol
    {
        public static string Encode(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '%': builder.Append("%25"); break;
                    case '|': builder.Append("%7C"); break;
                    case '\n': builder.Append("%0A"); break;
                    case '\r': break;
                    default: builder.Append(c); break;
                }
            }

            return builder.ToString();
        }

        public static string Decode(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("%0A", "\n").Replace("%7C", "|").Replace("%25", "%");
        }
    }

    public sealed class ServerAssistRow
    {
        public int Index;
        public string Action = string.Empty;
        public string Label = string.Empty;
        public string Sub = string.Empty;
        public string Icon = string.Empty;
        public string IconTint = string.Empty;
        public string Badge = string.Empty;
        public bool Enabled;
        public bool Selected;
    }

    public sealed class ServerAssistButton
    {
        public int Index;
        public string Action = string.Empty;
        public string Label = string.Empty;
        public string Tooltip = string.Empty;
        public string Icon = string.Empty;
        public string IconTint = string.Empty;
        public bool Enabled;
    }

    public sealed class ServerAssistView
    {
        public string Title = "Server Assist";
        public string Subtitle = string.Empty;
        public string Status = string.Empty;
        public string InputLabel = string.Empty;
        public string InputValue = string.Empty;
        public bool InputVisible;
        public string ActiveTab = "home";
        public string Chrome = "normal";
        public string Provider = string.Empty;
        public string Background = string.Empty;
        public string Banner = string.Empty;
        public string Tab1Label = "Home";
        public string Tab2Label = "Shared";
        public string Tab3Label = "My Buttons";

        // A launch is a server-approved instruction. The client must not derive
        // launch targets from local button state.
        public string LaunchType = string.Empty;
        public string LaunchTarget = string.Empty;
        public bool LaunchClose = true;

        public readonly List<ServerAssistRow> Rows = new List<ServerAssistRow>();
        public readonly List<ServerAssistButton> Buttons = new List<ServerAssistButton>();

        public bool HasLaunch
        {
            get
            {
                return !string.IsNullOrEmpty(LaunchType) &&
                       !string.IsNullOrEmpty(LaunchTarget);
            }
        }

        public static ServerAssistView Parse(string payload)
        {
            ServerAssistView view = new ServerAssistView();
            if (string.IsNullOrEmpty(payload))
            {
                return view;
            }

            string[] lines = payload.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0)
                {
                    continue;
                }

                int equals = line.IndexOf('=');
                if (equals <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, equals);
                string value = line.Substring(equals + 1);

                switch (key)
                {
                    case "title":
                        view.Title = ServerAssistProtocol.Decode(value);
                        break;
                    case "subtitle":
                        view.Subtitle = ServerAssistProtocol.Decode(value);
                        break;
                    case "status":
                        view.Status = ServerAssistProtocol.Decode(value);
                        break;
                    case "tab":
                        view.ActiveTab = ServerAssistProtocol.Decode(value);
                        break;
                    case "chrome":
                        view.Chrome = ServerAssistProtocol.Decode(value);
                        break;
                    case "provider":
                        view.Provider = ServerAssistProtocol.Decode(value);
                        break;
                    case "background":
                        view.Background = ServerAssistProtocol.Decode(value);
                        break;
                    case "banner":
                        view.Banner = ServerAssistProtocol.Decode(value);
                        break;
                    case "tab1label":
                        view.Tab1Label = ServerAssistProtocol.Decode(value);
                        break;
                    case "tab2label":
                        view.Tab2Label = ServerAssistProtocol.Decode(value);
                        break;
                    case "tab3label":
                        view.Tab3Label = ServerAssistProtocol.Decode(value);
                        break;
                    case "input":
                    {
                        string[] parts = value.Split(new[] { '|' }, 3);
                        if (parts.Length >= 3)
                        {
                            view.InputLabel = ServerAssistProtocol.Decode(parts[0]);
                            view.InputVisible = parts[1] == "1";
                            view.InputValue = ServerAssistProtocol.Decode(parts[2]);
                        }
                        break;
                    }
                    case "launch":
                    {
                        string[] parts = value.Split(new[] { '|' }, 3);
                        if (parts.Length >= 2)
                        {
                            view.LaunchType = ServerAssistProtocol.Decode(parts[0]);
                            view.LaunchTarget = ServerAssistProtocol.Decode(parts[1]);
                            view.LaunchClose = parts.Length < 3 || parts[2] != "0";
                        }
                        break;
                    }
                    case "row":
                    {
                        string[] parts = value.Split(new[] { '|' }, 9);
                        if (parts.Length < 6)
                        {
                            break;
                        }

                        int index;
                        if (!int.TryParse(parts[0], out index))
                        {
                            break;
                        }

                        ServerAssistRow row = new ServerAssistRow
                        {
                            Index = index,
                            Action = ServerAssistProtocol.Decode(parts[1]),
                            Label = ServerAssistProtocol.Decode(parts[2]),
                            Sub = ServerAssistProtocol.Decode(parts[3]),
                            Enabled = parts[4] == "1",
                            Selected = parts[5] == "1",
                            Icon = parts.Length > 6 ? ServerAssistProtocol.Decode(parts[6]) : string.Empty,
                            Badge = parts.Length > 7 ? ServerAssistProtocol.Decode(parts[7]) : string.Empty,
                            IconTint = parts.Length > 8 ? ServerAssistProtocol.Decode(parts[8]) : string.Empty
                        };

                        while (view.Rows.Count <= index)
                        {
                            view.Rows.Add(new ServerAssistRow());
                        }

                        view.Rows[index] = row;
                        break;
                    }
                    case "btn":
                    {
                        string[] parts = value.Split(new[] { '|' }, 7);
                        if (parts.Length < 4)
                        {
                            break;
                        }

                        int index;
                        if (!int.TryParse(parts[0], out index))
                        {
                            break;
                        }

                        ServerAssistButton button = new ServerAssistButton
                        {
                            Index = index,
                            Action = ServerAssistProtocol.Decode(parts[1]),
                            Label = ServerAssistProtocol.Decode(parts[2]),
                            Enabled = parts[3] == "1",
                            Tooltip = parts.Length > 4 ? ServerAssistProtocol.Decode(parts[4]) : string.Empty,
                            Icon = parts.Length > 5 ? ServerAssistProtocol.Decode(parts[5]) : string.Empty,
                            IconTint = parts.Length > 6 ? ServerAssistProtocol.Decode(parts[6]) : string.Empty
                        };

                        while (view.Buttons.Count <= index)
                        {
                            view.Buttons.Add(new ServerAssistButton());
                        }

                        view.Buttons[index] = button;
                        break;
                    }
                }
            }

            return view;
        }
    }
}



