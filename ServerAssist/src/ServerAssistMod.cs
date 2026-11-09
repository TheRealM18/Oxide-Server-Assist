using System.Reflection;

namespace ServerAssistMod
{
    /// <summary>
    /// Client/server companion for the ServerAssist Oxide plugin.
    /// The client half draws the XUi window; the server half forwards
    /// requests to the plugin. Install this mod on the server and on every
    /// client that should see the GUI.
    /// </summary>
    public class ServerAssistMod : IModApi
    {
        public void InitMod(Mod _modInstance)
        {
            Log.Out("[ServerAssist] loading companion mod from " + _modInstance.Path);

            ServerAssistNetPackages.RegisterAll(Assembly.GetExecutingAssembly());

            ModEvents.GameUpdate.RegisterHandler(OnGameUpdate);
            ModEvents.PlayerSpawnedInWorld.RegisterHandler(OnPlayerSpawnedInWorld);
            ModEvents.PlayerDisconnected.RegisterHandler(OnPlayerDisconnected);
            ModEvents.WorldShuttingDown.RegisterHandler(OnWorldShuttingDown);
            ModEvents.ChatMessage.RegisterHandler(OnChatMessage);
        }

        private static void OnGameUpdate(ref ModEvents.SGameUpdateData _data)
        {
            ServerAssistClient.Update();
        }

        private static void OnPlayerSpawnedInWorld(ref ModEvents.SPlayerSpawnedInWorldData _data)
        {
            if (_data.IsLocalPlayer)
            {
                ServerAssistClient.SendHello();
                PlayerProfilesClient.OnLocalPlayerSpawned();
            }
        }

        private static void OnPlayerDisconnected(ref ModEvents.SPlayerDisconnectedData _data)
        {
            ServerAssistServer.Remove(_data.ClientInfo);
        }

        private static void OnWorldShuttingDown(ref ModEvents.SWorldShuttingDownData _data)
        {
            ServerAssistClient.Reset();
        }

        private static ModEvents.EModEventResult OnChatMessage(ref ModEvents.SChatMessageData _data)
        {
            return ServerAssistServer.HandleChat(ref _data);
        }
    }
}




