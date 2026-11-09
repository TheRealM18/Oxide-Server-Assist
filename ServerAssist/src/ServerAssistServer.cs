using System;
using System.Collections.Generic;

namespace ServerAssistMod
{
    /// <summary>
    /// Server-side plumbing for the GUI. It only tracks which clients can
    /// render the window and forwards requests to the Oxide plugin.
    /// </summary>
    public static class ServerAssistServer
    {
        private static readonly HashSet<int> capableClients = new HashSet<int>();

        public static bool OpenForPlayer(string playerId, string payload)
        {
            ClientInfo client = FindClient(playerId);
            if (client == null || !IsCapable(client))
            {
                return false;
            }

            HandleRequest(client, string.IsNullOrEmpty(payload) ? "a|open" : payload);
            return true;
        }

        private static ClientInfo FindClient(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return null;
            }

            ConnectionManager connection = SingletonMonoBehaviour<ConnectionManager>.Instance;
            if (connection == null || connection.Clients == null)
            {
                return null;
            }

            for (int i = 0; i < connection.Clients.list.Count; i++)
            {
                ClientInfo client = connection.Clients.list[i];
                string id = ServerAssistBridge.GetPlayerId(client);
                if (string.Equals(id, playerId, StringComparison.OrdinalIgnoreCase))
                {
                    return client;
                }
            }

            return null;
        }

        public static void HandleRequest(ClientInfo sender, string payload)
        {
            if (sender == null)
            {
                return;
            }

            MarkCapable(sender);

            if (string.Equals(payload, "hello", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string response = ServerAssistBridge.HandleRequest(sender, payload);
            SendResponse(sender, response);
        }

        public static void SendResponse(ClientInfo sender, string payload)
        {
            if (sender == null || string.IsNullOrEmpty(payload))
            {
                return;
            }

            try
            {
                sender.SendPackage(NetPackageManager.GetPackage<NetPackageServerAssistResponse>().Setup(payload));
            }
            catch (Exception exception)
            {
                Log.Error("[ServerAssist] failed to send GUI response: " + exception);
            }
        }

        public static void MarkCapable(ClientInfo client)
        {
            if (client == null)
            {
                return;
            }

            int key = ClientKey(client);
            if (key >= 0)
            {
                capableClients.Add(key);
            }
        }

        public static bool IsCapable(ClientInfo client)
        {
            if (client == null)
            {
                return false;
            }

            int key = ClientKey(client);
            return key >= 0 && capableClients.Contains(key);
        }

        public static void Remove(ClientInfo client)
        {
            if (client == null)
            {
                return;
            }

            int key = ClientKey(client);
            if (key >= 0)
            {
                capableClients.Remove(key);
            }
        }

        private static int ClientKey(ClientInfo client)
        {
            if (client.ClientNumber >= 0)
            {
                return client.ClientNumber;
            }

            return client.entityId;
        }

        public static ModEvents.EModEventResult HandleChat(ref ModEvents.SChatMessageData data)
        {
            if (data.ClientInfo == null || string.IsNullOrEmpty(data.Message))
            {
                return ModEvents.EModEventResult.Continue;
            }

            string message = data.Message.Trim();
            string payload = null;

            if (Matches(message, "/serverassist") || Matches(message, "!serverassist") ||
                Matches(message, "/sa") || Matches(message, "!sa"))
            {
                payload = "a|home";
            }
            else if (Matches(message, "/serverassist mine") || Matches(message, "!serverassist mine") ||
                     Matches(message, "/sa mine") || Matches(message, "!sa mine"))
            {
                payload = "a|home|mine";
            }
            else if (Matches(message, "/serverassist shared") || Matches(message, "!serverassist shared") ||
                     Matches(message, "/sa shared") || Matches(message, "!sa shared"))
            {
                payload = "a|home|shared";
            }

            if (payload == null || !IsCapable(data.ClientInfo))
            {
                // Let the Oxide command fall back to chat output when the
                // client does not have the companion mod installed.
                return ModEvents.EModEventResult.Continue;
            }

            HandleRequest(data.ClientInfo, payload);
            return ModEvents.EModEventResult.StopHandlersAndVanilla;
        }

        private static bool Matches(string message, string command)
        {
            return string.Equals(message, command, StringComparison.OrdinalIgnoreCase);
        }
    }
}




