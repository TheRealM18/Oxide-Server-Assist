using System;
using System.Collections.Generic;
using UnityEngine;

namespace ServerAssistMod
{
    public static class ServerAssistClient
    {
        public const KeyCode ToggleKey = KeyCode.F7;
        public const KeyCode AlternateToggleKey = KeyCode.F6;
        private const string WindowGroupName = "serverAssist";

        private static string pendingPayload;
        private static bool helloSent;

        public static bool HasPending
        {
            get { return !string.IsNullOrEmpty(pendingPayload); }
        }

        public static string TakePending()
        {
            string value = pendingPayload;
            pendingPayload = null;
            return value;
        }

        public static void Reset()
        {
            pendingPayload = null;
            helloSent = false;
            PlayerProfilesClient.Reset();
        }

        public static void Update()
        {
            if (GameManager.IsDedicatedServer)
            {
                return;
            }

            if (!helloSent)
            {
                SendHello();
            }

            PlayerProfilesClient.Tick();

            XUi xui = GetXui();
            if (xui == null)
            {
                return;
            }

            if (UnityEngine.Input.GetKeyDown(ToggleKey) || UnityEngine.Input.GetKeyDown(AlternateToggleKey))
            {
                if (IsOpen(xui))
                {
                    CloseWindow(xui);
                    return;
                }

                if (!xui.playerUI.windowManager.IsModalWindowOpen())
                {
                    RequestOpen();
                }
            }
        }

        public static void Toggle()
        {
            XUi xui = GetXui();
            if (xui == null)
            {
                return;
            }

            if (IsOpen(xui))
            {
                CloseWindow(xui);
                return;
            }

            if (!xui.playerUI.windowManager.IsModalWindowOpen())
            {
                RequestOpen();
            }
        }

        /// <summary>
        /// Console-command variant. Unlike F7, this always leaves an active
        /// provider and returns to the ServerAssist home board.
        /// </summary>
        public static void ToggleHome()
        {
            XUi xui = GetXui();
            if (xui == null)
            {
                return;
            }

            if (IsOpen(xui))
            {
                CloseWindow(xui);
                return;
            }

            if (!xui.playerUI.windowManager.IsModalWindowOpen())
            {
                RequestOpen("a|home");
            }
        }

        private static bool IsOpen(XUi xui)
        {
            XUiWindowGroup group = XUiC_ServerAssistWindowGroup.FindWindowGroup(xui);
            return group != null && group.isShowing;
        }

        private static void CloseWindow(XUi xui)
        {
            XUiWindowGroup group = XUiC_ServerAssistWindowGroup.FindWindowGroup(xui);
            if (group != null)
            {
                xui.playerUI.windowManager.Close(group);
            }
        }

        public static void SendHello()
        {
            if (GameManager.Instance == null || GameManager.Instance.World == null)
            {
                return;
            }

            ConnectionManager connection = SingletonMonoBehaviour<ConnectionManager>.Instance;
            if (connection == null)
            {
                return;
            }

            try
            {
                connection.SendToServer(NetPackageManager.GetPackage<NetPackageServerAssistRequest>().Setup("hello"));
                helloSent = true;
                PlayerProfilesClient.SendHello();
            }
            catch (Exception exception)
            {
                Log.Warning("[ServerAssist] hello could not be sent yet: " + exception.Message);
            }
        }

        public static void SendRequest(string payload)
        {
            ConnectionManager connection = SingletonMonoBehaviour<ConnectionManager>.Instance;
            if (connection == null)
            {
                return;
            }

            try
            {
                connection.SendToServer(NetPackageManager.GetPackage<NetPackageServerAssistRequest>().Setup(payload ?? string.Empty));
            }
            catch (Exception exception)
            {
                Log.Error("[ServerAssist] request could not be sent: " + exception);
            }
        }

        public static void RequestOpen(string requestPayload = "a|open")
        {
            // Refresh the optional PlayerProfiles capability before the panel
            // request arrives. The server can then offer an in-place switch
            // instead of a reconnect.
            PlayerProfilesClient.SendHello();

            pendingPayload =
                "v1\n" +
                "title=" + ServerAssistProtocol.Encode("Server Assist") + "\n" +
                "subtitle=" + ServerAssistProtocol.Encode("Waiting for server") + "\n" +
                "status=" + ServerAssistProtocol.Encode("Loading buttons...") + "\n" +
                "tab=home\n" +
                "input=||0|\n" +
                "btn=0|refresh|" + ServerAssistProtocol.Encode("Retry") + "|1\n" +
                "btn=1|close|" + ServerAssistProtocol.Encode("Close") + "|1\n";

            XUi xui = GetXui();
            if (xui != null)
            {
                XUiC_ServerAssistWindowGroup.Open(xui);
            }

            SendRequest(string.IsNullOrEmpty(requestPayload) ? "a|open" : requestPayload);
        }

        public static void OnResponse(string payload)
        {
            if (string.IsNullOrEmpty(payload))
            {
                return;
            }

            if (PlayerProfilesClient.TryHandle(payload))
            {
                return;
            }

            XUi xui = GetXui();
            if (xui == null)
            {
                return;
            }

            XUiWindowGroup shown = XUiC_ServerAssistWindowGroup.FindWindowGroup(xui);
            if (shown != null && shown.isShowing)
            {
                XUiC_ServerAssistWindow window = XUiC_ServerAssistWindowGroup.FindWindow(xui);
                if (window != null)
                {
                    window.ApplyPayload(payload);
                    return;
                }
            }

            pendingPayload = payload;
            XUiC_ServerAssistWindowGroup.Open(xui);
        }

        /// <summary>
        /// Executes a launch instruction that was approved by the server.
        /// Window groups are opened locally. Commands run through the local
        /// console; the server-side copies of those commands still enforce
        /// their own permissions.
        /// </summary>
        public static void ExecuteLaunch(ServerAssistView view, XUiC_ServerAssistWindow window)
        {
            if (view == null || !view.HasLaunch)
            {
                return;
            }

            XUi xui = GetXui();
            if (xui == null)
            {
                return;
            }

            string type = view.LaunchType == null ? string.Empty : view.LaunchType.Trim().ToLowerInvariant();
            string target = (view.LaunchTarget ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(target))
            {
                return;
            }

            if (type == "window")
            {
                if (string.Equals(target, WindowGroupName, StringComparison.OrdinalIgnoreCase))
                {
                    if (window != null)
                    {
                        window.ShowLocalError("That button points at the Server Assist window itself.");
                    }
                    return;
                }

                XUiController group = xui.FindWindowGroupByName(target);
                if (group == null || group.WindowGroup == null)
                {
                    if (window != null)
                    {
                        window.ShowLocalError("Window group '" + target + "' was not found on this client.");
                    }
                    return;
                }

                if (view.LaunchClose)
                {
                    CloseWindow(xui);
                }

                // The vanilla player list has no close button. Opening it
                // modal leaves it trapped over the HUD; vanilla opens it as a
                // normal cursor window and closes it with Escape/its UI key.
                bool modal = !string.Equals(target, "players", StringComparison.OrdinalIgnoreCase);
                if (!modal)
                {
                    group.WindowGroup.isEscClosable = true;
                }

                xui.playerUI.windowManager.Open(group.WindowGroup, modal);
                return;
            }

            if (type == "command")
            {
                string command = target.TrimStart('/');
                if (string.IsNullOrEmpty(command))
                {
                    return;
                }

                try
                {
                    SdtdConsole console = SdtdConsole.Instance;
                    if (console == null)
                    {
                        if (window != null)
                        {
                            window.ShowLocalError("The local console is not available.");
                        }
                        return;
                    }

                    if (view.LaunchClose)
                    {
                        CloseWindow(xui);
                    }

                    List<string> output = console.ExecuteSync(command, null);
                    if (output != null)
                    {
                        for (int i = 0; i < output.Count; i++)
                        {
                            Log.Out("[ServerAssist] " + output[i]);
                        }
                    }
                }
                catch (Exception exception)
                {
                    Log.Error("[ServerAssist] command launch failed: " + exception);
                    if (window != null)
                    {
                        window.ShowLocalError("Command failed: " + exception.Message);
                    }
                }
            }
        }

        public static XUi GetXui()
        {
            if (GameManager.Instance == null || GameManager.Instance.World == null)
            {
                return null;
            }

            // Use the player-owned UI first. It is the XUi that actually
            // parsed the in-game Config/XUi_InGame XML, which is the same
            // pattern QLN and the vanilla interaction code use.
            EntityPlayerLocal player = GameManager.Instance.World.GetPrimaryPlayer();
            if (player != null && player.PlayerUI != null && player.PlayerUI.xui != null)
            {
                return player.PlayerUI.xui;
            }

            LocalPlayerUI ui = LocalPlayerUI.primaryUI;
            if (ui == null && player != null)
            {
                ui = LocalPlayerUI.GetUIForPlayer(player);
            }

            return ui != null ? ui.xui : null;
        }
    }
}



