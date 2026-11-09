using System;
using System.Collections.Generic;
using System.Reflection;

namespace ServerAssistMod
{
    /// <summary>
    /// The client mod deliberately has no compile-time reference to Oxide.
    /// The server-side copy of this assembly forwards GUI requests to the
    /// ServerAssist Oxide plugin through reflection. The plugin owns all button
    /// data and decides which buttons a player may see or launch.
    /// </summary>
    public static class ServerAssistBridge
    {
        private const string PluginName = "ServerAssist";
        private const string PluginMethod = "HandleGuiRequest";

        public static string GetPlayerId(ClientInfo client)
        {
            if (client == null)
            {
                return null;
            }

            try
            {
                if (client.PlatformId != null && !string.IsNullOrEmpty(client.PlatformId.ReadablePlatformUserIdentifier))
                {
                    return client.PlatformId.ReadablePlatformUserIdentifier;
                }
            }
            catch
            {
                // Fall through to the cross-platform identifier.
            }

            try
            {
                if (client.CrossplatformId != null && !string.IsNullOrEmpty(client.CrossplatformId.ReadablePlatformUserIdentifier))
                {
                    return client.CrossplatformId.ReadablePlatformUserIdentifier;
                }
            }
            catch
            {
                // Ignore and return null below.
            }

            return null;
        }

        public static string HandleRequest(ClientInfo client, string payload)
        {
            bool profileProtocol = payload != null &&
                                   payload.StartsWith("@pp|", StringComparison.Ordinal);

            string playerId = GetPlayerId(client);
            if (string.IsNullOrEmpty(playerId))
            {
                return profileProtocol ? null : ErrorView("Could not determine your player id.");
            }

            // Any incoming request proves the client has the companion mod.
            ServerAssistServer.MarkCapable(client);

            try
            {
                object plugin = FindPlugin();
                if (plugin == null)
                {
                    // The optional PlayerProfiles protocol must stay silent when
                    // its plugin is not installed, otherwise merely loading the
                    // updated client mod would pop an error window.
                    return profileProtocol
                        ? null
                        : ErrorView("The ServerAssist Oxide plugin is not loaded on this server.");
                }

                // Older ServerAssist plugins return "Unknown GUI request" for
                // @pp payloads. Stay silent unless the companion-protocol route
                // is present, so an updated client mod never spams errors on an
                // older server plugin.
                if (profileProtocol && !SupportsCompanionProtocol(plugin))
                {
                    return null;
                }

                MethodInfo method = plugin.GetType().GetMethod(
                    PluginMethod,
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(string), typeof(string) },
                    null);

                if (method == null)
                {
                    return profileProtocol
                        ? null
                        : ErrorView("The ServerAssist plugin does not expose " + PluginMethod + ".");
                }

                object result = method.Invoke(plugin, new object[] { playerId, payload ?? string.Empty });
                string response = result as string;

                if (string.IsNullOrEmpty(response))
                {
                    return profileProtocol
                        ? null
                        : ErrorView("The ServerAssist plugin returned an empty response.");
                }

                return response;
            }
            catch (TargetInvocationException exception)
            {
                Exception inner = exception.InnerException ?? exception;
                Log.Error("[ServerAssist] plugin request failed: " + inner);
                return ErrorView("Server Assist request failed: " + inner.Message);
            }
            catch (Exception exception)
            {
                Log.Error("[ServerAssist] plugin request failed: " + exception);
                return ErrorView("Server Assist request failed: " + exception.Message);
            }
        }

        private static bool SupportsCompanionProtocol(object plugin)
        {
            try
            {
                return plugin != null &&
                       plugin.GetType().GetMethod(
                           "RouteCompanionProtocol",
                           BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                           null,
                           new[] { typeof(string), typeof(string) },
                           null) != null;
            }
            catch
            {
                return false;
            }
        }

        private static object FindPlugin()
        {
            Type interfaceType = FindType("Oxide.Core.Interface");
            if (interfaceType == null)
            {
                return null;
            }

            PropertyInfo oxideProperty = interfaceType.GetProperty("Oxide", BindingFlags.Public | BindingFlags.Static);
            object oxide = oxideProperty != null ? oxideProperty.GetValue(null, null) : null;
            if (oxide == null)
            {
                return null;
            }

            PropertyInfo managerProperty = oxide.GetType().GetProperty("RootPluginManager", BindingFlags.Public | BindingFlags.Instance);
            object manager = managerProperty != null ? managerProperty.GetValue(oxide, null) : null;
            if (manager == null)
            {
                return null;
            }

            MethodInfo getPlugin = manager.GetType().GetMethod(
                "GetPlugin",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(string) },
                null);

            if (getPlugin != null)
            {
                object plugin = getPlugin.Invoke(manager, new object[] { PluginName });
                if (plugin != null)
                {
                    return plugin;
                }
            }

            // Fallback: scan loaded plugins for the entry point. This keeps the
            // bridge working if the plugin file or class is renamed by its author.
            MethodInfo getPlugins = manager.GetType().GetMethod("GetPlugins", BindingFlags.Public | BindingFlags.Instance);
            if (getPlugins == null)
            {
                return null;
            }

            object enumerable = getPlugins.Invoke(manager, null);
            System.Collections.IEnumerable plugins = enumerable as System.Collections.IEnumerable;
            if (plugins == null)
            {
                return null;
            }

            foreach (object candidate in plugins)
            {
                if (candidate == null)
                {
                    continue;
                }

                // PermissionGui uses the same bridge method signature. Only
                // accept the ServerAssist plugin here, otherwise a missing
                // ServerAssist plugin would silently render PermissionGui data.
                if (!string.Equals(candidate.GetType().Name, PluginName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                MethodInfo candidateMethod = candidate.GetType().GetMethod(
                    PluginMethod,
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(string), typeof(string) },
                    null);

                if (candidateMethod != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static Type FindType(string fullName)
        {
            Type direct = Type.GetType(fullName + ", Oxide.Core");
            if (direct != null)
            {
                return direct;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    Type type = assemblies[i].GetType(fullName, false);
                    if (type != null)
                    {
                        return type;
                    }
                }
                catch
                {
                    // Ignore assemblies that cannot be inspected.
                }
            }

            return null;
        }

        public static string ErrorView(string message)
        {
            return "v1\n" +
                   "title=" + ServerAssistProtocol.Encode("Server Assist") + "\n" +
                   "subtitle=" + ServerAssistProtocol.Encode("Button data unavailable") + "\n" +
                   "status=" + ServerAssistProtocol.Encode(message) + "\n" +
                   "tab=home\n" +
                   "input=||0|\n" +
                   "row=0||" + ServerAssistProtocol.Encode("No data") + "|" + ServerAssistProtocol.Encode(message) + "|0|0\n" +
                   "btn=0|refresh|" + ServerAssistProtocol.Encode("Retry") + "|1\n";
        }
    }
}



