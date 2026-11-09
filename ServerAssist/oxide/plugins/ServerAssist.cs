using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("ServerAssist", "TheRealM18", "2.1.0")]
    [Description("Permission-aware launcher board for opening other XUi windows and running client commands.")]
    public class ServerAssist : CovalencePlugin
    {
        private const string PermUse = "serverassist.use";
        private const string PermAdmin = "serverassist.admin";
        private const string PermCustom = "serverassist.custom";
        private const string DataFileName = "ServerAssist/data";
        private const int UiRows = 10;
        private const int UiButtons = 12;

        private Configuration config;
        private StoredData data;
        private readonly Dictionary<string, Session> sessions = new Dictionary<string, Session>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GuiHook> guiHooks = new Dictionary<string, GuiHook>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GuiProvider> guiProviders = new Dictionary<string, GuiProvider>(StringComparer.OrdinalIgnoreCase);

        #region Lifecycle

        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
            permission.RegisterPermission(PermAdmin, this);
            permission.RegisterPermission(PermCustom, this);

            LoadConfig();
            LoadData();
            SeedExampleButtons();
            RegisterButtonPermissions();

            AddCovalenceCommand(new[] { "serverassist", "sa" }, nameof(CommandServerAssist), PermUse);

            Interface.Oxide.LogInfo(
                "[ServerAssist] UI rows per page=" + UiRows + ", action buttons=" + UiButtons + ".");

            timer.Once(1f, EnsureDefaultPermissions);
            timer.Once(2f, SeedExampleButtons);
            timer.Once(3f, NotifyServerAssistReady);
        }

        private void OnServerInitialized(bool initial)
        {
            EnsureDefaultPermissions();
            SeedExampleButtons();
            RegisterButtonPermissions();
            NotifyServerAssistReady();
        }

        private void OnServerSave()
        {
            SaveData();
        }

        private void Unload()
        {
            SaveData();
            sessions.Clear();
            guiHooks.Clear();
            guiProviders.Clear();
        }

        private void OnPluginUnloaded(Plugin plugin)
        {
            if (plugin == null)
            {
                return;
            }

            List<string> removeHooks = new List<string>();
            foreach (KeyValuePair<string, GuiHook> pair in guiHooks)
            {
                if (string.Equals(pair.Value.PluginName, plugin.Name, StringComparison.OrdinalIgnoreCase))
                {
                    removeHooks.Add(pair.Key);
                }
            }

            for (int i = 0; i < removeHooks.Count; i++)
            {
                UnregisterGuiHook(removeHooks[i]);
            }

            List<string> removeProviders = new List<string>();
            foreach (KeyValuePair<string, GuiProvider> pair in guiProviders)
            {
                if (string.Equals(pair.Value.PluginName, plugin.Name, StringComparison.OrdinalIgnoreCase))
                {
                    removeProviders.Add(pair.Key);
                }
            }

            for (int i = 0; i < removeProviders.Count; i++)
            {
                UnregisterGuiProvider(removeProviders[i]);
            }
        }

        private void OnUserDisconnected(IPlayer player, string reason)
        {
            if (player != null)
            {
                sessions.Remove(player.Id);
            }
        }

        protected override void LoadDefaultConfig()
        {
            config = new Configuration();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<Configuration>();
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[ServerAssist] config could not be read: " + exception.Message);
                config = null;
            }

            if (config == null)
            {
                config = new Configuration();
            }

            config.MaxSharedButtons = Math.Max(1, Math.Min(200, config.MaxSharedButtons));
            config.MaxPersonalButtons = Math.Max(0, Math.Min(100, config.MaxPersonalButtons));

            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        private void LoadData()
        {
            try
            {
                data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(DataFileName);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[ServerAssist] data could not be read: " + exception.Message);
                data = null;
            }

            NormalizeData();
            SaveData();
        }

        private void SaveData()
        {
            try
            {
                Interface.Oxide.DataFileSystem.WriteObject(DataFileName, data, true);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError("[ServerAssist] data could not be saved: " + exception);
            }
        }

        private void NormalizeData()
        {
            if (data == null)
            {
                data = new StoredData();
            }

            if (data.Favorites == null)
            {
                data.Favorites = new List<string>();
            }

            Dictionary<string, ButtonDef> shared = new Dictionary<string, ButtonDef>(StringComparer.OrdinalIgnoreCase);
            if (data.Shared != null)
            {
                foreach (KeyValuePair<string, ButtonDef> pair in data.Shared)
                {
                    if (string.IsNullOrEmpty(pair.Key))
                    {
                        continue;
                    }

                    ButtonDef def = pair.Value ?? new ButtonDef();
                    NormalizeButton(def, pair.Key);
                    shared[pair.Key] = def;
                }
            }

            Dictionary<string, Dictionary<string, ButtonDef>> personal =
                new Dictionary<string, Dictionary<string, ButtonDef>>(StringComparer.OrdinalIgnoreCase);
            if (data.Personal != null)
            {
                foreach (KeyValuePair<string, Dictionary<string, ButtonDef>> playerPair in data.Personal)
                {
                    if (string.IsNullOrEmpty(playerPair.Key) || playerPair.Value == null)
                    {
                        continue;
                    }

                    Dictionary<string, ButtonDef> buttons = new Dictionary<string, ButtonDef>(StringComparer.OrdinalIgnoreCase);
                    foreach (KeyValuePair<string, ButtonDef> pair in playerPair.Value)
                    {
                        if (string.IsNullOrEmpty(pair.Key))
                        {
                            continue;
                        }

                        ButtonDef def = pair.Value ?? new ButtonDef();
                        NormalizeButton(def, pair.Key);
                        buttons[pair.Key] = def;
                    }

                    personal[playerPair.Key] = buttons;
                }
            }

            // The old standalone PermissionGui window was replaced by a
            // server-side ServerAssist GUI hook. Remove the old seeded button
            // so existing installs do not keep a dead permGui target.
            ButtonDef legacy;
            if (shared.TryGetValue("permission-manager", out legacy) &&
                legacy != null &&
                string.Equals(legacy.TargetType, "window", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(legacy.Target, "permGui", StringComparison.OrdinalIgnoreCase))
            {
                shared.Remove("permission-manager");
            }

            // The vanilla player list target is unreliable in the current UI.
            // Remove the old seeded button from existing installs.
            shared.Remove("player-list");
            foreach (Dictionary<string, ButtonDef> buttons in personal.Values)
            {
                buttons.Remove("player-list");
            }

            data.Shared = shared;
            data.Personal = personal;
        }

        private static void NormalizeButton(ButtonDef def, string id)
        {
            def.Id = id;
            def.Label = Clean(def.Label, 48);
            if (string.IsNullOrEmpty(def.Label))
            {
                def.Label = "Unnamed Button";
            }

            def.Description = Clean(def.Description, 160);
            def.TargetType = string.Equals(def.TargetType, "command", StringComparison.OrdinalIgnoreCase) ? "command" : "window";
            def.Target = Clean(def.Target, 160);
            def.Permission = Clean(def.Permission, 96);
            def.Icon = Clean(def.Icon, 120);
            def.IconTint = Clean(def.IconTint, 16);
        }

        private static string Clean(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            value = value.Replace("\r", " ").Replace("\n", " ").Trim();
            if (value.Length > maxLength)
            {
                value = value.Substring(0, maxLength);
            }

            return value;
        }

        private void EnsureDefaultPermissions()
        {
            string adminGroup = Interface.Oxide.Config.Options.DefaultGroups.Administrators;
            if (!string.IsNullOrEmpty(adminGroup))
            {
                GrantGroupIfMissing(adminGroup, PermAdmin);
                GrantGroupIfMissing(adminGroup, PermUse);
                GrantGroupIfMissing(adminGroup, PermCustom);
            }

            string playerGroup = Interface.Oxide.Config.Options.DefaultGroups.Players;
            if (!string.IsNullOrEmpty(playerGroup))
            {
                if (config.GrantUseToDefaultGroup)
                {
                    GrantGroupIfMissing(playerGroup, PermUse);
                }

                if (config.GrantCustomToDefaultGroup)
                {
                    GrantGroupIfMissing(playerGroup, PermCustom);
                }
            }
        }

        private void GrantGroupIfMissing(string group, string permissionName)
        {
            if (string.IsNullOrEmpty(group) || !permission.GroupExists(group))
            {
                return;
            }

            if (!permission.GroupHasPermission(group, permissionName))
            {
                permission.GrantGroupPermission(group, permissionName, this);
            }
        }

        private void SeedExampleButtons()
        {
            if (!config.SeedExampleButtons || data.SeededExamples)
            {
                return;
            }

            data.SeededExamples = true;

            SaveData();
            RegisterButtonPermissions();
        }

        private void RegisterButtonPermissions()
        {
            if (data == null)
            {
                return;
            }

            foreach (ButtonDef def in data.Shared.Values)
            {
                RegisterButtonPermission(def);
            }

            foreach (Dictionary<string, ButtonDef> buttons in data.Personal.Values)
            {
                foreach (ButtonDef def in buttons.Values)
                {
                    RegisterButtonPermission(def);
                }
            }
        }

        private void GrantPersonalOwner(ButtonDef def, string playerId, string scope)
        {
            if (def == null || string.IsNullOrEmpty(playerId) ||
                !string.Equals(scope, "personal", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // A personal "auto" button should be immediately usable by the
            // player who made it. Only grant the button's own node; never
            // grant a permission the player typed in by hand. This prevents a
            // personal button from being used to self-grant a shared button's
            // permission.
            string expected = "serverassist.personal." + def.Id;
            if (string.Equals(def.Permission, expected, StringComparison.OrdinalIgnoreCase) &&
                !permission.UserHasPermission(playerId, def.Permission))
            {
                permission.GrantUserPermission(playerId, def.Permission, this);
            }
        }

        private void RegisterButtonPermission(ButtonDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.Permission) ||
                def.Permission.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Only own the per-button namespaces. A button may point at a
            // permission owned by another plugin (permissiongui.use, for
            // example); registering that here would steal its ownership.
            if (!def.Permission.StartsWith("serverassist.button.", StringComparison.OrdinalIgnoreCase) &&
                !def.Permission.StartsWith("serverassist.personal.", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                permission.RegisterPermission(def.Permission, this);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[ServerAssist] permission '" + def.Permission + "' could not be registered: " + exception.Message);
            }
        }

        #endregion

        #region GUI hooks

        private void NotifyServerAssistReady()
        {
            try
            {
                plugins.CallHook("OnServerAssistReady");
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[ServerAssist] OnServerAssistReady notification failed: " + exception.Message);
            }
        }

        /// <summary>
        /// Called by optional companion plugins. The hook appears as a home-row
        /// button and its view/actions are rendered through this one XUi window.
        /// The provider method must have the signature:
        ///     string MethodName(string playerId, string payload)
        /// and return the same line protocol used by ServerAssist.
        /// </summary>
        [HookMethod("RegisterGuiHook")]
        public bool RegisterGuiHook(string id, string label, string description, string permissionName, string pluginName, string methodName)
        {
            id = Clean(id, 48).ToLowerInvariant();
            label = Clean(label, 48);
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(label) ||
                string.IsNullOrEmpty(pluginName) || string.IsNullOrEmpty(methodName))
            {
                return false;
            }

            GuiHook existing;
            int order = guiHooks.TryGetValue(id, out existing) && existing != null
                ? existing.Order
                : guiHooks.Count * 10;

            GuiHook hook = new GuiHook
            {
                Id = id,
                Label = label,
                Description = Clean(description, 160),
                Permission = Clean(permissionName, 96),
                PluginName = pluginName.Trim(),
                MethodName = methodName.Trim(),
                Order = order
            };

            guiHooks[id] = hook;
            Interface.Oxide.LogInfo("[ServerAssist] registered GUI hook '" + id + "' from " + pluginName + "." + methodName);
            return true;
        }

        [HookMethod("RegisterPanel")]
        public bool RegisterPanel(
            string id,
            string label,
            string description,
            string permissionName,
            string pluginName,
            string methodName,
            string icon,
            int order)
        {
            bool registered = RegisterGuiHook(id, label, description, permissionName, pluginName, methodName);
            if (!registered)
            {
                return false;
            }

            GuiHook hook;
            if (guiHooks.TryGetValue(id, out hook) && hook != null)
            {
                hook.Icon = string.IsNullOrEmpty(icon) ? "ui_game_symbol_hammer" : Clean(icon, 120);
                hook.Order = order;
            }

            return true;
        }

        [HookMethod("OpenPanel")]
        public bool OpenPanel(string playerId, string panelId, string action)
        {
            return OpenHookForPlayerWithAction(playerId, panelId, action);
        }

        [HookMethod("UnregisterPanel")]
        public bool UnregisterPanel(string panelId)
        {
            return UnregisterGuiHook(panelId);
        }

        [HookMethod("SetPanelIcon")]
        public bool SetPanelIcon(string id, string icon)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            GuiHook hook;
            if (!guiHooks.TryGetValue(Clean(id, 48).ToLowerInvariant(), out hook) || hook == null)
            {
                return false;
            }

            hook.Icon = string.IsNullOrEmpty(icon) ? "ui_game_symbol_hammer" : Clean(icon, 120);
            return true;
        }

        [HookMethod("SetProviderIcon")]
        public bool SetProviderIcon(string providerId, string icon)
        {
            GuiProvider provider = FindGuiProvider(providerId);
            if (provider == null)
            {
                return false;
            }

            provider.Icon = string.IsNullOrEmpty(icon) ? "ui_game_symbol_hammer" : Clean(icon, 120);
            return true;
        }

        [HookMethod("GetPanelIcon")]
        public string GetPanelIcon(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return string.Empty;
            }

            GuiHook hook;
            if (guiHooks.TryGetValue(Clean(id, 48).ToLowerInvariant(), out hook) && hook != null)
            {
                return hook.Icon ?? string.Empty;
            }

            return string.Empty;
        }

        [HookMethod("GetPluginIcon")]
        public string GetPluginIcon(string pluginName)
        {
            if (string.IsNullOrEmpty(pluginName))
            {
                return string.Empty;
            }

            string name = pluginName.Trim();
            foreach (KeyValuePair<string, GuiHook> pair in guiHooks)
            {
                GuiHook hook = pair.Value;
                if (hook != null && string.Equals(hook.PluginName, name, StringComparison.OrdinalIgnoreCase))
                {
                    return hook.Icon ?? string.Empty;
                }
            }

            foreach (KeyValuePair<string, GuiProvider> pair in guiProviders)
            {
                GuiProvider provider = pair.Value;
                if (provider != null && string.Equals(provider.PluginName, name, StringComparison.OrdinalIgnoreCase))
                {
                    return provider.Icon ?? string.Empty;
                }
            }

            return string.Empty;
        }

        [HookMethod("UnregisterGuiHook")]
        public bool UnregisterGuiHook(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            bool removed = guiHooks.Remove(id);
            foreach (Session session in sessions.Values)
            {
                if (string.Equals(session.HookId, id, StringComparison.OrdinalIgnoreCase))
                {
                    session.HookId = null;
                }
            }

            return removed;
        }

        [HookMethod("OpenHookForPlayer")]
        public bool OpenHookForPlayer(string playerId, string hookId)
        {
            return OpenHookForPlayerWithAction(playerId, hookId, null);
        }

        // Kept for AdminShop and other plugins that use the three-argument
        // reflection signature documented in ServerAssist/update.md.
        public bool OpenHookForPlayer(string playerId, string hookId, string subAction)
        {
            return OpenHookForPlayerWithAction(playerId, hookId, subAction);
        }

        /// <summary>
        /// Opens the Server Assist on the client, enters a GUI hook, and optionally
        /// sends one provider action afterwards (for example "a|tab|users").
        /// </summary>
        [HookMethod("OpenHookForPlayerWithAction")]
        public bool OpenHookForPlayerWithAction(string playerId, string hookId, string subAction)
        {
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(hookId))
            {
                return false;
            }

            if (!SendClientPayload(playerId, "a|hook|open|" + hookId))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(subAction))
            {
                SendClientPayload(playerId, subAction);
            }

            return true;
        }

        [HookMethod("GetProviderHomeSupport")]
        public bool GetProviderHomeSupport()
        {
            return true;
        }

        [HookMethod("RegisterGuiProvider")]
        public bool RegisterGuiProvider(
            string providerId,
            string pluginName,
            string handlerMethod,
            string title,
            string tab1,
            string tab2,
            string tab3)
        {
            providerId = Clean(providerId, 48).ToLowerInvariant();
            pluginName = Clean(pluginName, 64);
            handlerMethod = Clean(handlerMethod, 64);
            if (string.IsNullOrEmpty(providerId) ||
                string.IsNullOrEmpty(pluginName) ||
                string.IsNullOrEmpty(handlerMethod))
            {
                return false;
            }

            GuiProvider provider = new GuiProvider
            {
                Id = providerId,
                PluginName = pluginName,
                HandlerMethod = handlerMethod,
                Title = Clean(title, 64),
                Tab1Label = Clean(tab1, 32),
                Tab2Label = Clean(tab2, 32),
                Tab3Label = Clean(tab3, 32),
                Icon = "ui_game_symbol_hammer"
            };

            if (string.IsNullOrEmpty(provider.Title))
            {
                provider.Title = "Server Assist";
            }

            if (string.IsNullOrEmpty(provider.Tab1Label))
            {
                provider.Tab1Label = "Home";
            }

            if (string.IsNullOrEmpty(provider.Tab2Label))
            {
                provider.Tab2Label = "Shared";
            }

            if (string.IsNullOrEmpty(provider.Tab3Label))
            {
                provider.Tab3Label = "My Buttons";
            }

            guiProviders[providerId] = provider;
            Interface.Oxide.LogInfo("[ServerAssist] registered GUI provider '" + providerId + "' from " + pluginName + "." + handlerMethod);
            return true;
        }

        [HookMethod("UnregisterGuiProvider")]
        public bool UnregisterGuiProvider(string providerId)
        {
            if (string.IsNullOrEmpty(providerId))
            {
                return false;
            }

            bool removed = guiProviders.Remove(providerId);

            foreach (Session session in sessions.Values)
            {
                if (string.Equals(session.ActiveProvider, providerId, StringComparison.OrdinalIgnoreCase))
                {
                    session.ActiveProvider = null;
                }
            }

            return removed;
        }

        [HookMethod("OpenGuiProvider")]
        public bool OpenGuiProvider(string playerId, string providerId, string payload = "a|open")
        {
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(providerId))
            {
                return false;
            }

            GuiProvider provider = FindGuiProvider(providerId);
            if (provider == null)
            {
                return false;
            }

            // Set the active provider on the server request path, not on the
            // command path. The request path is keyed by the platform id from
            // the sending ClientInfo, which is the same id used by all GUI
            // actions afterwards.
            string inner = string.IsNullOrEmpty(payload) ? "a|open" : payload;
            return SendClientPayload(playerId, "a|provider|" + provider.Id + "|" + Encode(inner));
        }

        private GuiProvider FindGuiProvider(string id)
        {
            GuiProvider provider;
            return !string.IsNullOrEmpty(id) && guiProviders.TryGetValue(id, out provider) ? provider : null;
        }

        private string RouteGuiProvider(string playerId, Session session, string payload)
        {
            GuiProvider provider = FindGuiProvider(session.ActiveProvider);
            if (provider == null)
            {
                return null;
            }

            string view = CallGuiProvider(provider, playerId, payload);
            return string.IsNullOrEmpty(view) ? null : AppendPanelImage(view, provider.Icon);
        }

        private string CallGuiProvider(GuiProvider provider, string playerId, string payload)
        {
            try
            {
                Plugin plugin = plugins.Find(provider.PluginName);
                if (plugin == null)
                {
                    Interface.Oxide.LogWarning("[ServerAssist] GUI provider '" + provider.Id + "' plugin is not loaded.");
                    return null;
                }

                MethodInfo method = plugin.GetType().GetMethod(
                    provider.HandlerMethod,
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(string), typeof(string) },
                    null);

                if (method == null)
                {
                    Interface.Oxide.LogWarning("[ServerAssist] GUI provider '" + provider.Id + "' method " + provider.HandlerMethod + " was not found.");
                    return null;
                }

                object result = method.Invoke(plugin, new object[] { playerId, payload ?? string.Empty });
                return result as string;
            }
            catch (TargetInvocationException exception)
            {
                Exception inner = exception.InnerException ?? exception;
                Interface.Oxide.LogError("[ServerAssist] GUI provider '" + provider.Id + "' failed: " + inner);
                return null;
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError("[ServerAssist] GUI provider '" + provider.Id + "' failed: " + exception);
                return null;
            }
        }

        /// <summary>
        /// Routes "@pp|&lt;hook-id&gt;|&lt;body&gt;" to a registered GUI hook
        /// without requiring the Server Assist window or hook session to be
        /// active. This is used by the optional PlayerProfiles client protocol.
        /// </summary>
        private string RouteCompanionProtocol(string playerId, string payload)
        {
            string body = payload.Substring(4);
            int separator = body.IndexOf('|');
            if (separator <= 0)
            {
                return null;
            }

            string hookId = body.Substring(0, separator);
            string protocolBody = body.Substring(separator + 1);

            GuiHook hook = FindGuiHook(hookId);
            if (hook == null || !CanUseGuiHook(playerId, hook))
            {
                return null;
            }

            return CallGuiHook(hook, playerId, "@pp|" + protocolBody);
        }

        private GuiHook FindGuiHook(string id)
        {
            GuiHook hook;
            return !string.IsNullOrEmpty(id) && guiHooks.TryGetValue(id, out hook) ? hook : null;
        }

        private bool CanUseGuiHook(string playerId, GuiHook hook)
        {
            if (hook == null)
            {
                return false;
            }

            return IsAdmin(playerId) ||
                   string.IsNullOrEmpty(hook.Permission) ||
                   permission.UserHasPermission(playerId, hook.Permission);
        }

        private List<GuiHook> VisibleGuiHooks(string playerId)
        {
            return guiHooks.Values
                .Where(h => CanUseGuiHook(playerId, h))
                .OrderBy(h => h.Order)
                .ThenBy(h => h.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string OpenGuiHook(string playerId, Session session, string id)
        {
            GuiHook hook = FindGuiHook(id);
            if (hook == null)
            {
                session.Status = "GUI hook not found.";
                return Render(playerId, session);
            }

            if (!CanUseGuiHook(playerId, hook))
            {
                session.Status = "You do not have permission to open '" + hook.Label + "'.";
                return Render(playerId, session);
            }

            session.Tab = "home";
            session.Page = 0;
            session.ActiveProvider = null;
            string view = CallGuiHook(hook, playerId, "a|open");
            if (string.IsNullOrEmpty(view))
            {
                session.HookId = null;
                session.Status = "GUI hook '" + hook.Label + "' did not return a view.";
                return Render(playerId, session);
            }

            session.HookId = hook.Id;
            session.SelectedId = null;
            session.Draft = null;
            ClearPrompt(session);
            session.Status = string.Empty;
            return AddHookChrome(view, hook);
        }

        private string RouteGuiHook(string playerId, Session session, string payload)
        {
            GuiHook hook = FindGuiHook(session.HookId);
            if (hook == null)
            {
                session.HookId = null;
                session.Status = "That GUI hook is no longer available.";
                return Render(playerId, session);
            }

            if (!CanUseGuiHook(playerId, hook))
            {
                session.HookId = null;
                session.Status = "You no longer have permission to use '" + hook.Label + "'.";
                return Render(playerId, session);
            }

            string view = CallGuiHook(hook, playerId, payload);
            if (string.IsNullOrEmpty(view))
            {
                session.HookId = null;
                session.Status = "GUI hook '" + hook.Label + "' did not return a view.";
                return Render(playerId, session);
            }

            return AddHookChrome(view, hook);
        }

        private string CallGuiHook(GuiHook hook, string playerId, string payload)
        {
            try
            {
                Plugin plugin = plugins.Find(hook.PluginName);
                if (plugin == null)
                {
                    return null;
                }

                MethodInfo method = plugin.GetType().GetMethod(
                    hook.MethodName,
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(string), typeof(string) },
                    null);

                if (method == null)
                {
                    Interface.Oxide.LogWarning("[ServerAssist] hook '" + hook.Id + "' method " + hook.MethodName + " was not found.");
                    return null;
                }

                object result = method.Invoke(plugin, new object[] { playerId, payload ?? string.Empty });
                return result as string;
            }
            catch (TargetInvocationException exception)
            {
                Exception inner = exception.InnerException ?? exception;
                Interface.Oxide.LogError("[ServerAssist] GUI hook '" + hook.Id + "' failed: " + inner);
                return null;
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError("[ServerAssist] GUI hook '" + hook.Id + "' failed: " + exception);
                return null;
            }
        }

        private static string AddHookChrome(string view, GuiHook hook)
        {
            if (string.IsNullOrEmpty(view))
            {
                return view;
            }

            // Companion protocol responses must not receive Server Assist
            // chrome; the client's protocol handler consumes them verbatim.
            if (view.StartsWith("@pp|", StringComparison.Ordinal))
            {
                return view;
            }

            // Appended last so these keys win over the provider's own tab value.
            string result = view + (view.EndsWith("\n", StringComparison.Ordinal) ? string.Empty : "\n") +
                            "chrome=hook\n" +
                            "tab=hook\n";
            return AppendPanelImage(result, hook != null ? hook.Icon : null);
        }

        private static string AppendPanelImage(string view, string icon)
        {
            if (string.IsNullOrEmpty(view) || string.IsNullOrEmpty(icon))
            {
                return view;
            }

            StringBuilder builder = new StringBuilder(view);
            if (builder.Length > 0 && builder[builder.Length - 1] != '\n')
            {
                builder.Append('\n');
            }

            if (view.IndexOf("background=", StringComparison.OrdinalIgnoreCase) < 0)
            {
                builder.Append("background=").Append(Encode(icon)).Append('\n');
            }

            if (view.IndexOf("banner=", StringComparison.OrdinalIgnoreCase) < 0)
            {
                builder.Append("banner=").Append(Encode(icon)).Append('\n');
            }

            return builder.ToString();
        }

        #endregion

        #region Permission helpers

        private bool IsAdmin(string playerId)
        {
            return !string.IsNullOrEmpty(playerId) &&
                   (playerId.Equals("server_console", StringComparison.OrdinalIgnoreCase) ||
                    permission.UserHasPermission(playerId, PermAdmin));
        }

        private bool CanUse(string playerId)
        {
            return IsAdmin(playerId) || permission.UserHasPermission(playerId, PermUse);
        }

        private bool CanManage(string playerId, string scope)
        {
            if (IsAdmin(playerId))
            {
                return true;
            }

            if (!string.Equals(scope, "personal", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return config.AllowPlayerCustomButtons && permission.UserHasPermission(playerId, PermCustom);
        }

        private bool CanLaunch(string playerId, ButtonDef def)
        {
            if (def == null || !def.Enabled)
            {
                return false;
            }

            if (IsAdmin(playerId))
            {
                return true;
            }

            if (string.IsNullOrEmpty(def.Permission))
            {
                return true;
            }

            return permission.UserHasPermission(playerId, def.Permission);
        }

        private bool IsVisibleOnHome(string playerId, ButtonDef def)
        {
            if (def == null)
            {
                return false;
            }

            if (IsAdmin(playerId))
            {
                return config.AdminSeesAllButtons || CanLaunch(playerId, def);
            }

            return CanLaunch(playerId, def);
        }

        private bool RequireManage(Session session, string playerId, string scope)
        {
            if (CanManage(playerId, scope))
            {
                return true;
            }

            session.Status = string.Equals(scope, "shared", StringComparison.OrdinalIgnoreCase)
                ? "You need the 'serverassist.admin' permission to manage shared buttons."
                : "You need the 'serverassist.custom' permission to manage personal buttons.";
            return false;
        }

        #endregion

        #region GUI request entry point

        /// <summary>
        /// Called by the ServerAssistMod bridge. Returns a serialized view model.
        /// </summary>
        public string HandleGuiRequest(string playerId, string payload)
        {
            try
            {
                if (!permission.IsLoaded)
                {
                    return ErrorView("The Oxide permission system is not loaded.");
                }

                if (string.IsNullOrEmpty(playerId))
                {
                    return ErrorView("Could not determine your player id.");
                }

                Session session = GetSession(playerId);
                if (string.IsNullOrEmpty(payload) || payload == "open")
                {
                    payload = "a|open";
                }

                // Optional companion protocols (for example PlayerProfiles).
                // The payload carries the target hook id so it can be routed
                // without an active Server Assist session:
                //
                //   @pp|<hook-id>|<protocol body>
                //
                // The hook receives the normal "@pp|<protocol body>" payload.
                if (payload.StartsWith("@pp|", StringComparison.Ordinal))
                {
                    return RouteCompanionProtocol(playerId, payload);
                }

                // Server-plugin entry point used by OpenGuiProvider. This is
                // handled here so the provider is attached to the platform id
                // that the client actually uses for the following actions.
                if (payload.StartsWith("a|provider|", StringComparison.OrdinalIgnoreCase))
                {
                    string[] providerParts = payload.Split(new[] { '|' }, 4);
                    if (providerParts.Length < 4)
                    {
                        return ErrorView("Malformed provider request.");
                    }

                    GuiProvider provider = FindGuiProvider(providerParts[2]);
                    if (provider == null)
                    {
                        return ErrorView("GUI provider not found.");
                    }

                    session.ActiveProvider = provider.Id;
                    session.HookId = null;
                    payload = Decode(providerParts[3]);
                    if (string.IsNullOrEmpty(payload))
                    {
                        payload = "a|open";
                    }
                }

                // /sa and /serverassist explicitly return to the ServerAssist
                // board. F7 sends a|open and may resume an active provider.
                if (string.Equals(payload, "a|home", StringComparison.OrdinalIgnoreCase) ||
                    payload.StartsWith("a|home|", StringComparison.OrdinalIgnoreCase))
                {
                    session.ActiveProvider = null;
                    session.HookId = null;

                    if (payload.Length > 6)
                    {
                        payload = "a|tab" + payload.Substring(6);
                    }
                    else
                    {
                        payload = "a|open";
                    }
                }

                // Providers are routed before ServerAssist's own permission
                // check. A player may have auctionhouse.use without
                // serverassist.use and still open the auction provider.
                if (!string.IsNullOrEmpty(session.ActiveProvider))
                {
                    string providerResponse = RouteGuiProvider(playerId, session, payload);
                    if (!string.IsNullOrEmpty(providerResponse))
                    {
                        return providerResponse;
                    }

                    session.ActiveProvider = null;
                }

                if (!CanUse(playerId))
                {
                    return ErrorView("You do not have permission to use the Server Assist.");
                }

                if (payload.StartsWith("t|", StringComparison.Ordinal))
                {
                    string value = Decode(payload.Substring(2));
                    return HandleText(playerId, session, value);
                }

                if (payload.StartsWith("a|", StringComparison.Ordinal))
                {
                    return HandleAction(playerId, session, payload.Substring(2));
                }

                return ErrorView("Unknown GUI request.");
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogException("[ServerAssist] GUI request failed", exception);
                return ErrorView("Internal error: " + exception.Message);
            }
        }

        private Session GetSession(string playerId)
        {
            Session session;
            if (!sessions.TryGetValue(playerId, out session))
            {
                session = new Session();
                sessions[playerId] = session;
            }

            return session;
        }

        private string ErrorView(string message)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("v2\n");
            builder.Append("protocol=2\n");
            builder.Append("title=").Append(Encode("Server Assist")).Append('\n');
            builder.Append("subtitle=").Append(Encode("Button data unavailable")).Append('\n');
            builder.Append("status=").Append(Encode(message)).Append('\n');
            builder.Append("tab=home\n");
            builder.Append("input=||0|\n");
            builder.Append("row=0||").Append(Encode("No data")).Append('|').Append(Encode(message)).Append("|0|0\n");
            builder.Append("btn=0|refresh|").Append(Encode("Retry")).Append("|1\n");
            return builder.ToString();
        }

        #endregion

        #region Actions

        private string HandleAction(string playerId, Session session, string action)
        {
            string[] parts = action.Split('|');
            string command = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
            session.Status = string.Empty;

            // While a provider hook is active, every action except the
            // ServerAssist chrome action is forwarded to that provider.
            if (!string.IsNullOrEmpty(session.HookId) &&
                !string.Equals(command, "hook", StringComparison.OrdinalIgnoreCase))
            {
                return RouteGuiHook(playerId, session, "a|" + action);
            }

            switch (command)
            {
                case "favorite":
                    ToggleFavoriteAction(playerId, session, parts);
                    break;

                case "hook":
                    if (parts.Length > 2 && parts[1].Equals("open", StringComparison.OrdinalIgnoreCase))
                    {
                        return OpenGuiHook(playerId, session, parts[2]);
                    }

                    if (parts.Length > 1 && parts[1].Equals("back", StringComparison.OrdinalIgnoreCase))
                    {
                        session.HookId = null;
                        session.SelectedId = null;
                        session.Draft = null;
                        ClearPrompt(session);
                        session.Tab = "home";
                        session.Page = 0;
                        session.Status = "Closed the GUI hook.";
                    }
                    break;

                case "providerpanel":
                    if (parts.Length > 2 && parts[1].Equals("open", StringComparison.OrdinalIgnoreCase))
                    {
                        GuiProvider provider = FindGuiProvider(parts[2]);
                        if (provider == null)
                        {
                            session.Status = "GUI provider not found.";
                            break;
                        }

                        ResetSession(session);
                        session.ActiveProvider = provider.Id;
                        string providerView = RouteGuiProvider(playerId, session, "a|open");
                        if (!string.IsNullOrEmpty(providerView))
                        {
                            return providerView;
                        }

                        session.ActiveProvider = null;
                        session.Status = "GUI provider '" + provider.Title + "' did not return a view.";
                    }
                    break;

                case "open":
                    session.HookId = null;
                    session.ActiveProvider = null;
                    ResetSession(session);
                    break;

                case "refresh":
                    session.Status = "Refreshed.";
                    break;

                case "cancel":
                    ClearPrompt(session);
                    session.Draft = null;
                    session.IconSearchActive = false;
                    session.Status = "Input cancelled.";
                    break;

                case "tab":
                    if (parts.Length > 1)
                    {
                        SwitchTab(playerId, session, parts[1]);
                    }
                    break;

                case "page":
                    if (parts.Length > 1)
                    {
                        int delta = parts[1].Equals("next", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
                        session.Page += delta;
                    }
                    break;

                case "launch":
                    if (parts.Length > 2)
                    {
                        return HandleLaunch(playerId, session, parts[1], parts[2]);
                    }
                    break;

                case "new":
                    if (parts.Length > 1)
                    {
                        StartNew(playerId, session, parts[1]);
                    }
                    break;

                case "edit":
                    if (parts.Length > 2)
                    {
                        SelectButton(playerId, session, parts[1], parts[2]);
                    }
                    break;

                case "editfield":
                    if (parts.Length > 3)
                    {
                        PromptField(playerId, session, parts[1], parts[2], parts[3]);
                    }
                    break;

                case "iconsearch":
                    if (parts.Length > 2)
                    {
                        PromptIconSearch(playerId, session, parts[1], parts[2]);
                    }
                    break;

                case "iconset":
                    if (parts.Length > 3)
                    {
                        SetButtonIcon(playerId, session, parts[1], parts[2], parts[3]);
                    }
                    break;

                case "iconcancel":
                    session.IconSearchActive = false;
                    session.IconSearchQuery = string.Empty;
                    session.Status = "Icon search closed.";
                    break;

                case "toggle":
                    if (parts.Length > 2)
                    {
                        ToggleButton(playerId, session, parts[1], parts[2]);
                    }
                    break;

                case "move":
                    if (parts.Length > 3)
                    {
                        MoveButton(playerId, session, parts[1], parts[2], parts[3]);
                    }
                    break;

                case "delete":
                    if (parts.Length > 2)
                    {
                        PromptDelete(playerId, session, parts[1], parts[2]);
                    }
                    break;

                case "back":
                    session.SelectedId = null;
                    ClearPrompt(session);
                    session.Draft = null;
                    session.Status = string.Empty;
                    if (string.Equals(session.Tab, "shared", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(session.Tab, "mine", StringComparison.OrdinalIgnoreCase))
                    {
                        session.Page = 0;
                    }
                    else
                    {
                        session.Tab = "home";
                    }
                    break;

                default:
                    session.Status = "Unknown action: " + command;
                    break;
            }

            return Render(playerId, session);
        }

        private void ResetSession(Session session)
        {
            session.Tab = "home";
            session.Scope = "shared";
            session.SelectedId = null;
            session.HookId = null;
            session.ActiveProvider = null;
            session.IconSearchActive = false;
            session.Page = 0;
            session.InputAction = null;
            session.InputLabel = string.Empty;
            session.InputValue = string.Empty;
            session.Status = string.Empty;
            session.Draft = null;
        }

        private void ClearPrompt(Session session)
        {
            session.InputAction = null;
            session.InputLabel = string.Empty;
            session.InputValue = string.Empty;
        }

        private void SwitchTab(string playerId, Session session, string tab)
        {
            ClearPrompt(session);
            session.Draft = null;
            session.SelectedId = null;
            session.IconSearchActive = false;
            session.Page = 0;
            session.Status = string.Empty;

            if (tab.Equals("shared", StringComparison.OrdinalIgnoreCase))
            {
                if (!CanManage(playerId, "shared"))
                {
                    session.Status = "You need the 'serverassist.admin' permission to manage shared buttons.";
                    session.Tab = "home";
                    return;
                }

                session.Tab = "shared";
                session.Scope = "shared";
                return;
            }

            if (tab.Equals("mine", StringComparison.OrdinalIgnoreCase))
            {
                if (!CanManage(playerId, "personal"))
                {
                    session.Status = "You need the 'serverassist.custom' permission to manage personal buttons.";
                    session.Tab = "home";
                    return;
                }

                session.Tab = "mine";
                session.Scope = "personal";
                return;
            }

            session.Tab = "home";
        }

        private void StartNew(string playerId, Session session, string scope)
        {
            if (!RequireManage(session, playerId, scope))
            {
                return;
            }

            int count = CountScope(playerId, scope);
            int limit = string.Equals(scope, "shared", StringComparison.OrdinalIgnoreCase)
                ? config.MaxSharedButtons
                : config.MaxPersonalButtons;

            if (count >= limit)
            {
                session.Status = "That button list is full (" + count + "/" + limit + ").";
                return;
            }

            session.Scope = NormalizeScope(scope);
            session.SelectedId = null;
            session.IconSearchActive = false;
            session.Draft = new ButtonDef
            {
                Label = string.Empty,
                Description = string.Empty,
                TargetType = "window",
                Target = string.Empty,
                Permission = string.Empty,
                Enabled = true,
                Order = NextOrder(playerId, scope)
            };

            Prompt(session, "new.label", "Button label", string.Empty);
        }

        private void SelectButton(string playerId, Session session, string scope, string id)
        {
            scope = NormalizeScope(scope);
            if (!RequireManage(session, playerId, scope))
            {
                return;
            }

            ButtonDef def = FindButton(playerId, scope, id);
            if (def == null)
            {
                session.Status = "Button not found.";
                return;
            }

            session.Scope = scope;
            session.Tab = scope == "personal" ? "mine" : "shared";
            session.SelectedId = def.Id;
            session.IconSearchActive = false;
            session.Status = string.Empty;
            ClearPrompt(session);
            session.Draft = null;
        }

        private string HandleLaunch(string playerId, Session session, string scope, string id)
        {
            scope = NormalizeScope(scope);
            ButtonDef def = FindButton(playerId, scope, id);
            if (def == null)
            {
                session.Status = "Button not found.";
                return Render(playerId, session);
            }

            if (!def.Enabled)
            {
                session.Status = "That button is disabled.";
                return Render(playerId, session);
            }

            if (!CanLaunch(playerId, def))
            {
                session.Status = "You do not have permission to use '" + def.Label + "'.";
                return Render(playerId, session);
            }

            if (string.IsNullOrEmpty(def.Target))
            {
                session.Status = "That button has no target configured.";
                return Render(playerId, session);
            }

            if (string.Equals(def.TargetType, "command", StringComparison.OrdinalIgnoreCase) &&
                !IsAdmin(playerId) && !config.AllowPlayerCommandButtons &&
                string.Equals(scope, "personal", StringComparison.OrdinalIgnoreCase))
            {
                session.Status = "Command buttons are disabled for player-made buttons.";
                return Render(playerId, session);
            }

            string view = Render(playerId, session);
            return "launch=" + Encode(def.TargetType) + "|" + Encode(def.Target) + "|1\n" + view;
        }

        private void ToggleButton(string playerId, Session session, string scope, string id)
        {
            scope = NormalizeScope(scope);
            if (!RequireManage(session, playerId, scope))
            {
                return;
            }

            ButtonDef def = FindButton(playerId, scope, id);
            if (def == null)
            {
                session.Status = "Button not found.";
                return;
            }

            def.Enabled = !def.Enabled;
            session.Status = "Button '" + def.Label + "' is now " + (def.Enabled ? "enabled." : "disabled.");
            SaveData();
        }

        private void MoveButton(string playerId, Session session, string scope, string id, string direction)
        {
            scope = NormalizeScope(scope);
            if (!RequireManage(session, playerId, scope))
            {
                return;
            }

            List<ButtonDef> list = CollectScope(playerId, scope);
            int index = list.FindIndex(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                session.Status = "Button not found.";
                return;
            }

            int target = direction.Equals("up", StringComparison.OrdinalIgnoreCase) ? index - 1 : index + 1;
            if (target < 0 || target >= list.Count)
            {
                session.Status = "That button is already at the edge of the list.";
                return;
            }

            ButtonDef current = list[index];
            list[index] = list[target];
            list[target] = current;

            for (int i = 0; i < list.Count; i++)
            {
                list[i].Order = (i + 1) * 10;
            }

            session.Status = "Button order updated.";
            SaveData();
        }

        private void PromptDelete(string playerId, Session session, string scope, string id)
        {
            scope = NormalizeScope(scope);
            if (!RequireManage(session, playerId, scope))
            {
                return;
            }

            ButtonDef def = FindButton(playerId, scope, id);
            if (def == null)
            {
                session.Status = "Button not found.";
                return;
            }

            session.Scope = scope;
            session.SelectedId = def.Id;
            Prompt(session, "confirmDelete", "Type DELETE to remove '" + def.Label + "'", string.Empty);
        }

        private void DeleteButton(Session session, string playerId)
        {
            if (!RequireManage(session, playerId, session.Scope))
            {
                return;
            }

            ButtonDef def = FindButton(playerId, session.Scope, session.SelectedId);
            if (def == null)
            {
                session.Status = "Button not found.";
                return;
            }

            Dictionary<string, ButtonDef> map = ScopeMap(playerId, session.Scope, false);
            if (map != null)
            {
                map.Remove(def.Id);
            }

            session.SelectedId = null;
            ClearPrompt(session);
            session.Status = "Button '" + def.Label + "' deleted.";
            SaveData();
        }

        #endregion

        #region Text submissions

        private string HandleText(string playerId, Session session, string value)
        {
            if (!string.IsNullOrEmpty(session.HookId))
            {
                string hookView = RouteGuiHook(playerId, session, "t|" + Encode(value));
                return hookView;
            }

            string action = session.InputAction;
            ClearPrompt(session);

            if (string.IsNullOrEmpty(action))
            {
                session.Status = "Nothing is waiting for text input.";
                return Render(playerId, session);
            }

            if (!RequireManage(session, playerId, session.Scope))
            {
                return Render(playerId, session);
            }

            if (action == "icons.search")
            {
                session.IconSearchQuery = Clean(value, 80);
                session.Page = 0;
                session.Status = string.IsNullOrEmpty(session.IconSearchQuery)
                    ? "Showing common item icons."
                    : "Showing items matching '" + session.IconSearchQuery + "'.";
                return Render(playerId, session);
            }

            if (action.StartsWith("new.", StringComparison.Ordinal))
            {
                HandleNewText(playerId, session, action, value);
                return Render(playerId, session);
            }

            if (action.StartsWith("edit.", StringComparison.Ordinal))
            {
                HandleEditText(playerId, session, action, value);
                return Render(playerId, session);
            }

            if (action == "confirmDelete")
            {
                if (string.Equals(value.Trim(), "DELETE", StringComparison.OrdinalIgnoreCase))
                {
                    DeleteButton(session, playerId);
                }
                else
                {
                    session.Status = "Deletion cancelled.";
                }

                return Render(playerId, session);
            }

            session.Status = "Unknown input action.";
            return Render(playerId, session);
        }

        private void HandleNewText(string playerId, Session session, string action, string value)
        {
            if (session.Draft == null)
            {
                session.Status = "The new-button draft was lost. Press New again.";
                return;
            }

            switch (action)
            {
                case "new.label":
                {
                    string label = Clean(value, 48);
                    if (string.IsNullOrEmpty(label))
                    {
                        Prompt(session, "new.label", "Button label (cannot be empty)", string.Empty);
                        return;
                    }

                    session.Draft.Label = label;
                    Prompt(session, "new.type", "Type: window or command", "window");
                    return;
                }

                case "new.type":
                {
                    string type = value.Trim().ToLowerInvariant();
                    if (type != "window" && type != "command")
                    {
                        Prompt(session, "new.type", "Type must be 'window' or 'command'", "window");
                        return;
                    }

                    if (type == "command" && !CanManage(playerId, "shared") &&
                        !config.AllowPlayerCommandButtons)
                    {
                        Prompt(session, "new.type", "Command buttons are disabled for player-made buttons. Use 'window'", "window");
                        return;
                    }

                    session.Draft.TargetType = type;
                    Prompt(session, "new.target", type == "window" ? "XUi window group name" : "Console command (without /)", string.Empty);
                    return;
                }

                case "new.target":
                {
                    string error;
                    string target = NormalizeTarget(value, session.Draft.TargetType, session.Scope, playerId, out error);
                    if (error != null)
                    {
                        Prompt(session, "new.target", error, string.Empty);
                        return;
                    }

                    session.Draft.Target = target;
                    Prompt(session, "new.permission", "Permission node (blank = everyone, auto = per-button node)", "auto");
                    return;
                }

                case "new.permission":
                {
                    string permissionName = NormalizePermission(value, session.Scope, playerId, string.Empty, out string error);
                    if (error != null)
                    {
                        Prompt(session, "new.permission", error, string.Empty);
                        return;
                    }

                    session.Draft.Permission = permissionName;
                    FinishNew(playerId, session);
                    return;
                }
            }
        }

        private void HandleEditText(string playerId, Session session, string action, string value)
        {
            ButtonDef def = FindButton(playerId, session.Scope, session.SelectedId);
            if (def == null)
            {
                session.Status = "Button not found.";
                return;
            }

            if (action == "edit.label")
            {
                string label = Clean(value, 48);
                if (string.IsNullOrEmpty(label))
                {
                    Prompt(session, "edit.label", "Button label (cannot be empty)", def.Label);
                    return;
                }

                def.Label = label;
                session.Status = "Label updated.";
            }
            else if (action == "edit.description")
            {
                def.Description = Clean(value, 160);
                session.Status = "Description updated.";
            }
            else if (action == "edit.icon")
            {
                string error;
                string icon = NormalizeIcon(value, out error);
                if (error != null)
                {
                    Prompt(session, "edit.icon", error, def.Icon);
                    return;
                }

                def.Icon = icon;
                session.Status = string.IsNullOrEmpty(icon) ? "Icon reset to default." : "Icon updated.";
            }
            else if (action == "edit.type")
            {
                string type = value.Trim().ToLowerInvariant();
                if (type != "window" && type != "command")
                {
                    Prompt(session, "edit.type", "Type must be 'window' or 'command'", def.TargetType);
                    return;
                }

                if (type == "command" && !CanManage(playerId, "shared") &&
                    !config.AllowPlayerCommandButtons)
                {
                    Prompt(session, "edit.type", "Command buttons are disabled for player-made buttons. Use 'window'", "window");
                    return;
                }

                def.TargetType = type;
                def.Target = string.Empty;
                session.Status = "Target type updated. Set the target next.";
                Prompt(session, "edit.target", type == "window" ? "XUi window group name" : "Console command (without /)", string.Empty);
            }
            else if (action == "edit.target")
            {
                string error;
                string target = NormalizeTarget(value, def.TargetType, session.Scope, playerId, out error);
                if (error != null)
                {
                    Prompt(session, "edit.target", error, def.Target);
                    return;
                }

                def.Target = target;
                session.Status = "Target updated.";
            }
            else if (action == "edit.permission")
            {
                string permissionName = NormalizePermission(value, session.Scope, playerId, def.Id, out string error);
                if (error != null)
                {
                    Prompt(session, "edit.permission", error, def.Permission);
                    return;
                }

                def.Permission = permissionName;
                RegisterButtonPermission(def);
                GrantPersonalOwner(def, playerId, session.Scope);
                session.Status = "Permission updated.";
            }
            else
            {
                session.Status = "Unknown edit field.";
                return;
            }

            SaveData();
        }

        private void FinishNew(string playerId, Session session)
        {
            if (session.Draft == null)
            {
                return;
            }

            string scope = NormalizeScope(session.Scope);
            if (!RequireManage(session, playerId, scope))
            {
                session.Draft = null;
                return;
            }

            Dictionary<string, ButtonDef> map = ScopeMap(playerId, scope, true);
            if (map == null)
            {
                session.Status = "Could not open that button list.";
                session.Draft = null;
                return;
            }

            int limit = scope == "shared" ? config.MaxSharedButtons : config.MaxPersonalButtons;
            if (map.Count >= limit)
            {
                session.Status = "That button list is full (" + map.Count + "/" + limit + ").";
                session.Draft = null;
                return;
            }

            string id = MakeButtonId(map, session.Draft.Label);
            ButtonDef def = session.Draft;
            def.Id = id;
            if (def.Permission.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                def.Permission = scope == "personal"
                    ? "serverassist.personal." + id
                    : "serverassist.button." + id;
            }

            map[id] = def;
            session.Draft = null;
            session.SelectedId = id;
            session.Tab = scope == "personal" ? "mine" : "shared";
            session.Scope = scope;
            session.Status = "Button '" + def.Label + "' created.";
            RegisterButtonPermission(def);
            GrantPersonalOwner(def, playerId, scope);
            if (string.IsNullOrEmpty(def.Permission))
            {
                session.Status += " It is visible to everyone.";
            }
            else
            {
                session.Status += " Grant '" + def.Permission + "' to allow access.";
            }

            SaveData();
        }

        private void PromptField(string playerId, Session session, string scope, string id, string field)
        {
            scope = NormalizeScope(scope);
            if (!RequireManage(session, playerId, scope))
            {
                return;
            }

            ButtonDef def = FindButton(playerId, scope, id);
            if (def == null)
            {
                session.Status = "Button not found.";
                return;
            }

            session.Scope = scope;
            session.SelectedId = def.Id;
            field = field.ToLowerInvariant();

            switch (field)
            {
                case "label":
                    Prompt(session, "edit.label", "Button label", def.Label);
                    break;
                case "description":
                    Prompt(session, "edit.description", "Description (blank allowed)", def.Description);
                    break;
                case "icon":
                    Prompt(session, "edit.icon", "Icon: item:name, sprite:name, atlas:name, or blank", def.Icon);
                    break;
                case "type":
                    Prompt(session, "edit.type", "Type: window or command", def.TargetType);
                    break;
                case "target":
                    Prompt(session, "edit.target", def.TargetType == "window" ? "XUi window group name" : "Console command (without /)", def.Target);
                    break;
                case "permission":
                    Prompt(session, "edit.permission", "Permission node (blank = everyone, auto = per-button node)", def.Permission);
                    break;
            }
        }

        private void PromptIconSearch(string playerId, Session session, string scope, string id)
        {
            scope = NormalizeScope(scope);
            if (!RequireManage(session, playerId, scope))
            {
                return;
            }

            ButtonDef def = FindButton(playerId, scope, id);
            if (def == null)
            {
                session.Status = "Button not found.";
                return;
            }

            session.Scope = scope;
            session.SelectedId = def.Id;
            session.IconSearchActive = true;
            session.IconSearchQuery = string.Empty;
            session.Page = 0;
            Prompt(session, "icons.search", "Search items by name", string.Empty);
        }

        private void SetButtonIcon(string playerId, Session session, string scope, string id, string icon)
        {
            scope = NormalizeScope(scope);
            if (!RequireManage(session, playerId, scope))
            {
                return;
            }

            ButtonDef def = FindButton(playerId, scope, id);
            if (def == null)
            {
                session.Status = "Button not found.";
                return;
            }

            string error;
            string normalized = NormalizeIcon(icon, out error);
            if (error != null)
            {
                session.Status = error;
                return;
            }

            def.Icon = normalized;
            session.IconSearchActive = false;
            session.IconSearchQuery = string.Empty;
            session.Status = string.IsNullOrEmpty(normalized) ? "Icon reset to default." : "Icon updated.";
            SaveData();
        }

        private static string NormalizeIcon(string input, out string error)
        {
            error = null;
            string value = Clean(input, 120);
            if (string.IsNullOrEmpty(value) || value.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (value.StartsWith("item:", StringComparison.OrdinalIgnoreCase))
            {
                string itemName = value.Substring(5).Trim();
                if (string.IsNullOrEmpty(itemName) || ItemClass.GetItemClass(itemName, true) == null)
                {
                    error = "Item not found.";
                    return null;
                }

                return "item:" + itemName;
            }

            if (value.StartsWith("sprite:", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("atlas:", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-' && c != '.')
                {
                    error = "Use item:name, sprite:name, atlas:name, or a plain sprite name.";
                    return null;
                }
            }

            return "sprite:" + value;
        }

        private void Prompt(Session session, string action, string label, string value)
        {
            session.InputAction = action;
            session.InputLabel = label;
            session.InputValue = value ?? string.Empty;
        }

        #endregion

        #region Button data helpers

        private string NormalizeScope(string scope)
        {
            return string.Equals(scope, "personal", StringComparison.OrdinalIgnoreCase) ? "personal" : "shared";
        }

        private int CountScope(string playerId, string scope)
        {
            Dictionary<string, ButtonDef> map = ScopeMap(playerId, scope, false);
            return map == null ? 0 : map.Count;
        }

        private int NextOrder(string playerId, string scope)
        {
            List<ButtonDef> list = CollectScope(playerId, scope);
            int max = 0;
            foreach (ButtonDef def in list)
            {
                if (def.Order > max)
                {
                    max = def.Order;
                }
            }

            return max + 10;
        }

        private Dictionary<string, ButtonDef> ScopeMap(string playerId, string scope, bool create)
        {
            if (string.Equals(scope, "personal", StringComparison.OrdinalIgnoreCase))
            {
                return PersonalMap(playerId, create);
            }

            return data.Shared;
        }

        private Dictionary<string, ButtonDef> PersonalMap(string playerId, bool create)
        {
            Dictionary<string, ButtonDef> map;
            if (!data.Personal.TryGetValue(playerId, out map))
            {
                if (!create)
                {
                    return null;
                }

                map = new Dictionary<string, ButtonDef>(StringComparer.OrdinalIgnoreCase);
                data.Personal[playerId] = map;
            }

            return map;
        }

        private ButtonDef FindButton(string playerId, string scope, string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            Dictionary<string, ButtonDef> map = ScopeMap(playerId, scope, false);
            if (map == null)
            {
                return null;
            }

            ButtonDef def;
            return map.TryGetValue(id, out def) ? def : null;
        }

        private List<ButtonDef> CollectScope(string playerId, string scope)
        {
            Dictionary<string, ButtonDef> map = ScopeMap(playerId, scope, false);
            if (map == null)
            {
                return new List<ButtonDef>();
            }

            return map.Values
                .OrderBy(d => d.Order)
                .ThenBy(d => d.Label, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string MakeButtonId(Dictionary<string, ButtonDef> map, string label)
        {
            StringBuilder builder = new StringBuilder();
            string source = label == null ? string.Empty : label.ToLowerInvariant();
            for (int i = 0; i < source.Length && builder.Length < 40; i++)
            {
                char c = source[i];
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    builder.Append(c);
                }
                else if (c == ' ' || c == '_' || c == '-' || c == '.')
                {
                    if (builder.Length > 0 && builder[builder.Length - 1] != '-')
                    {
                        builder.Append('-');
                    }
                }
            }

            string baseId = builder.ToString().Trim('-');
            if (string.IsNullOrEmpty(baseId))
            {
                baseId = "button";
            }

            string id = baseId;
            int suffix = 2;
            while (map.ContainsKey(id))
            {
                id = baseId + "-" + suffix;
                suffix++;
            }

            return id;
        }

        private string NormalizeTarget(string input, string type, string scope, string playerId, out string error)
        {
            error = null;
            string value = Clean(input, 160);
            if (value.Length == 0)
            {
                error = "Target cannot be empty.";
                return null;
            }

            if (string.Equals(type, "command", StringComparison.OrdinalIgnoreCase))
            {
                if (!CanManage(playerId, "shared") && !config.AllowPlayerCommandButtons)
                {
                    error = "Command buttons are disabled for player-made buttons.";
                    return null;
                }

                value = value.TrimStart('/');
                if (value.Length == 0)
                {
                    error = "Command cannot be empty.";
                    return null;
                }

                if (value.IndexOf('|') >= 0)
                {
                    error = "Command cannot contain the '|' character.";
                    return null;
                }

                return value;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool valid = char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.';
                if (!valid)
                {
                    error = "Window group names can only use letters, numbers, '_', '-' and '.'.";
                    return null;
                }
            }

            return value;
        }

        private string NormalizePermission(string input, string scope, string playerId, string buttonId, out string error)
        {
            error = null;
            string value = Clean(input, 96);
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (value.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(buttonId))
                {
                    return "auto";
                }

                return string.Equals(scope, "personal", StringComparison.OrdinalIgnoreCase)
                    ? "serverassist.personal." + buttonId
                    : "serverassist.button." + buttonId;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool valid = char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.';
                if (!valid)
                {
                    error = "Permission nodes can only use letters, numbers, '_', '-' and '.'.";
                    return null;
                }
            }

            return value;
        }

        #endregion

        #region Rendering

        private sealed class RowInfo
        {
            public string Action = string.Empty;
            public string Label = string.Empty;
            public string Sub = string.Empty;
            public string Icon = "ui_game_symbol_hammer";
            public string IconTint = string.Empty;
            public string Badge = string.Empty;
            public bool Enabled = true;
            public bool Selected;
        }

        private sealed class ButtonInfo
        {
            public string Action = string.Empty;
            public string Label = string.Empty;
            public string Tooltip = string.Empty;
            public string Icon = string.Empty;
            public string IconTint = string.Empty;
            public bool Enabled = true;
        }

        private string Render(string playerId, Session session)
        {
            List<RowInfo> rows = new List<RowInfo>();
            List<ButtonInfo> buttons = new List<ButtonInfo>();
            string subtitle;

            if (session.IconSearchActive && !string.IsNullOrEmpty(session.SelectedId))
            {
                RenderIconSearch(playerId, session, rows, buttons, out subtitle);
            }
            else if (!string.IsNullOrEmpty(session.SelectedId))
            {
                RenderEdit(playerId, session, rows, buttons, out subtitle);
            }
            else if (string.Equals(session.Tab, "shared", StringComparison.OrdinalIgnoreCase))
            {
                RenderScope(playerId, session, "shared", rows, buttons, out subtitle);
            }
            else if (string.Equals(session.Tab, "mine", StringComparison.OrdinalIgnoreCase))
            {
                RenderScope(playerId, session, "personal", rows, buttons, out subtitle);
            }
            else
            {
                session.Tab = "home";
                RenderHome(playerId, session, rows, buttons, out subtitle);
            }

            StringBuilder builder = new StringBuilder(4096);
            builder.Append("v2\n");
            builder.Append("protocol=2\n");
            builder.Append("title=").Append(Encode("Server Assist")).Append('\n');
            builder.Append("tab=").Append(Encode(session.Tab)).Append('\n');
            builder.Append("subtitle=").Append(Encode(subtitle)).Append('\n');
            builder.Append("status=").Append(Encode(session.Status)).Append('\n');
            builder.Append("input=")
                .Append(Encode(string.IsNullOrEmpty(session.InputAction) ? string.Empty : session.InputLabel))
                .Append('|')
                .Append(string.IsNullOrEmpty(session.InputAction) ? "0" : "1")
                .Append('|')
                .Append(Encode(session.InputValue))
                .Append('\n');

            for (int i = 0; i < rows.Count && i < UiRows; i++)
            {
                RowInfo row = rows[i];
                builder.Append("row=").Append(i).Append('|')
                    .Append(Encode(row.Action)).Append('|')
                    .Append(Encode(row.Label)).Append('|')
                    .Append(Encode(row.Sub)).Append('|')
                    .Append(row.Enabled ? "1" : "0").Append('|')
                    .Append(row.Selected ? "1" : "0").Append('|')
                    .Append(Encode(row.Icon)).Append('|')
                    .Append(Encode(row.Badge)).Append('|')
                    .Append(Encode(row.IconTint)).Append('\n');
            }

            for (int i = 0; i < buttons.Count && i < UiButtons; i++)
            {
                ButtonInfo button = buttons[i];
                builder.Append("btn=").Append(i).Append('|')
                    .Append(Encode(button.Action)).Append('|')
                    .Append(Encode(button.Label)).Append('|')
                    .Append(button.Enabled ? "1" : "0").Append('|')
                    .Append(Encode(button.Tooltip));

                if (!string.IsNullOrEmpty(button.Icon))
                {
                    builder.Append('|')
                        .Append(Encode(button.Icon)).Append('|')
                        .Append(Encode(button.IconTint));
                }

                builder.Append('\n');
            }

            return builder.ToString();
        }

        private static string DefaultIcon(ButtonDef def)
        {
            if (def == null)
            {
                return "ui_game_symbol_hammer";
            }

            if (string.Equals(def.TargetType, "command", StringComparison.OrdinalIgnoreCase))
            {
                return "ui_game_symbol_hammer";
            }

            string target = def.Target ?? string.Empty;
            if (target.Equals("players", StringComparison.OrdinalIgnoreCase))
            {
                return "ui_game_symbol_players";
            }

            if (target.Equals("map", StringComparison.OrdinalIgnoreCase))
            {
                return "ui_game_symbol_map";
            }

            return "ui_game_symbol_hammer";
        }

        private void ToggleFavoriteAction(string playerId, Session session, string[] parts)
        {
            if (session == null || parts == null || parts.Length < 4)
            {
                return;
            }

            string key = null;
            if (string.Equals(parts[1], "hook", StringComparison.OrdinalIgnoreCase))
            {
                key = "hook:" + parts[3];
            }
            else if (string.Equals(parts[1], "launch", StringComparison.OrdinalIgnoreCase))
            {
                string scope = parts[2];
                key = string.Equals(scope, "personal", StringComparison.OrdinalIgnoreCase)
                    ? "button:" + playerId + ":personal:" + parts[3]
                    : "button:shared:" + parts[3];
            }

            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            bool added = ToggleFavorite(key);
            session.Status = added ? "Added to favorites." : "Removed from favorites.";
        }

        private bool IsFavorite(string key)
        {
            if (string.IsNullOrEmpty(key) || data == null || data.Favorites == null)
            {
                return false;
            }

            for (int i = 0; i < data.Favorites.Count; i++)
            {
                if (string.Equals(data.Favorites[i], key, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool ToggleFavorite(string key)
        {
            if (string.IsNullOrEmpty(key) || data == null)
            {
                return false;
            }

            if (data.Favorites == null)
            {
                data.Favorites = new List<string>();
            }

            for (int i = data.Favorites.Count - 1; i >= 0; i--)
            {
                if (string.Equals(data.Favorites[i], key, StringComparison.OrdinalIgnoreCase))
                {
                    data.Favorites.RemoveAt(i);
                    SaveData();
                    return false;
                }
            }

            data.Favorites.Add(key);
            SaveData();
            return true;
        }

        private void RenderHome(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            List<ButtonDef> shared = CollectScope(playerId, "shared")
                .Where(d => IsVisibleOnHome(playerId, d))
                .ToList();
            List<ButtonDef> personal = CollectScope(playerId, "personal")
                .Where(d => IsVisibleOnHome(playerId, d))
                .ToList();

            List<HomeEntry> all = new List<HomeEntry>();
            foreach (GuiHook hook in VisibleGuiHooks(playerId))
            {
                string key = "hook:" + hook.Id;
                all.Add(new HomeEntry
                {
                    Hook = hook,
                    Key = key,
                    Order = hook.Order,
                    Label = "[GUI] " + hook.Label,
                    SortLabel = hook.Label,
                    Favorite = IsFavorite(key)
                });
            }

            foreach (GuiProvider provider in guiProviders.Values)
            {
                if (provider == null || string.IsNullOrEmpty(provider.Id))
                {
                    continue;
                }

                string key = "provider:" + provider.Id;
                all.Add(new HomeEntry
                {
                    Provider = provider,
                    Key = key,
                    Order = 50,
                    Label = "[GUI] " + (string.IsNullOrEmpty(provider.Title) ? provider.Id : provider.Title),
                    SortLabel = string.IsNullOrEmpty(provider.Title) ? provider.Id : provider.Title,
                    Favorite = IsFavorite(key)
                });
            }

            foreach (ButtonDef def in shared)
            {
                string key = "button:shared:" + def.Id;
                all.Add(new HomeEntry
                {
                    Def = def,
                    Scope = "shared",
                    Key = key,
                    Order = def.Order,
                    Label = def.Label,
                    SortLabel = def.Label,
                    Favorite = IsFavorite(key)
                });
            }

            foreach (ButtonDef def in personal)
            {
                string key = "button:" + playerId + ":personal:" + def.Id;
                all.Add(new HomeEntry
                {
                    Def = def,
                    Scope = "personal",
                    Key = key,
                    Order = def.Order,
                    Label = def.Label,
                    SortLabel = def.Label,
                    Favorite = IsFavorite(key)
                });
            }

            // Favorites first, then alphabetical by the label the player sees.
            // The plugin name and the registration order are deliberately not
            // used for the home board.
            all = all
                .OrderByDescending(d => d.Favorite)
                .ThenBy(d => d.SortLabel, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int pages = PageCount(all.Count);
            ClampPage(session, all.Count);
            subtitle = "Home  |  " + all.Count + " entr" + (all.Count == 1 ? "y" : "ies") + " available  |  page " + (session.Page + 1) + "/" + pages + "  |  " + UiRows + " per page";

            int start = session.Page * UiRows;
            for (int i = 0; i < UiRows && start + i < all.Count; i++)
            {
                HomeEntry entry = all[start + i];
                if (entry.Hook != null)
                {
                    rows.Add(new RowInfo
                    {
                        Action = "hook|open|" + entry.Hook.Id,
                        Label = (entry.Favorite ? "★ " : string.Empty) + entry.Label,
                        Sub = (string.IsNullOrEmpty(entry.Hook.Description) ? "Optional GUI hook" : entry.Hook.Description) +
                              (string.IsNullOrEmpty(entry.Hook.Permission) ? string.Empty : "  |  " + entry.Hook.Permission),
                        Icon = string.IsNullOrEmpty(entry.Hook.Icon) ? "ui_game_symbol_hammer" : entry.Hook.Icon,
                        Badge = entry.Favorite ? "FAVORITE" : "PLUGIN",
                        Enabled = true,
                        Selected = false
                    });
                    continue;
                }

                if (entry.Provider != null)
                {
                    GuiProvider provider = entry.Provider;
                    rows.Add(new RowInfo
                    {
                        Action = "providerpanel|open|" + provider.Id,
                        Label = (entry.Favorite ? "★ " : string.Empty) + entry.Label,
                        Sub = "Tabbed provider  |  " + provider.Tab1Label + " / " +
                              provider.Tab2Label + " / " + provider.Tab3Label,
                        Icon = string.IsNullOrEmpty(provider.Icon) ? "ui_game_symbol_hammer" : provider.Icon,
                        Badge = entry.Favorite ? "FAVORITE" : "PROVIDER",
                        Enabled = true,
                        Selected = false
                    });
                    continue;
                }

                ButtonDef def = entry.Def;
                bool canLaunch = CanLaunch(playerId, def);

                string sub = string.IsNullOrEmpty(def.Description) ? string.Empty : def.Description;
                sub += (sub.Length > 0 ? "  |  " : string.Empty) + def.TargetType + ": " + def.Target;
                if (!string.IsNullOrEmpty(def.Permission))
                {
                    sub += "  |  " + def.Permission;
                }

                if (!def.Enabled)
                {
                    sub += "  |  DISABLED";
                }
                else if (!canLaunch)
                {
                    sub += "  |  no permission";
                }

                if (entry.Scope == "personal")
                {
                    sub += "  |  personal";
                }

                rows.Add(new RowInfo
                {
                    Action = "launch|" + entry.Scope + "|" + def.Id,
                    Label = (entry.Favorite ? "★ " : string.Empty) + def.Label,
                    Sub = sub,
                    Icon = string.IsNullOrEmpty(def.Icon) ? DefaultIcon(def) : def.Icon,
                    IconTint = def.IconTint,
                    Badge = entry.Favorite ? "FAVORITE" : (entry.Scope == "personal" ? "PERSONAL" : string.Empty),
                    Enabled = canLaunch,
                    Selected = false
                });
            }

            if (all.Count == 0)
            {
                rows.Add(new RowInfo
                {
                    Label = "No buttons are available.",
                    Sub = CanManage(playerId, "personal") ? "Open My Buttons to create one." : "Ask an admin to grant you a button permission.",
                    Enabled = false
                });
            }

            AddPagingButtons(session, pages, buttons);
            if (CanManage(playerId, "shared"))
            {
                buttons.Add(new ButtonInfo { Action = "tab|shared", Label = "Manage Shared", Tooltip = "Create and edit server-wide buttons.", Icon = "sprite:sa_stream_admin_links" });
            }

            if (CanManage(playerId, "personal"))
            {
                buttons.Add(new ButtonInfo { Action = "tab|mine", Label = "My Buttons", Tooltip = "Create personal buttons for this player.", Icon = "sprite:sa_stream_admin_players" });
            }

            buttons.Add(new ButtonInfo { Action = "refresh", Label = "Refresh", Icon = "sprite:sa_stream_admin_reload" });
        }

        private void RenderScope(string playerId, Session session, string scope, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            if (!RequireManage(session, playerId, scope))
            {
                session.Tab = "home";
                session.SelectedId = null;
                RenderHome(playerId, session, rows, buttons, out subtitle);
                return;
            }

            List<ButtonDef> list = CollectScope(playerId, scope);
            int pages = PageCount(list.Count);
            ClampPage(session, list.Count);
            string scopeName = scope == "personal" ? "My Buttons" : "Shared Buttons";
            subtitle = scopeName + "  |  " + list.Count + " button(s)  |  page " + (session.Page + 1) + "/" + pages;

            int start = session.Page * UiRows;
            for (int i = 0; i < UiRows && start + i < list.Count; i++)
            {
                ButtonDef def = list[start + i];
                string marker = def.Enabled ? "[on] " : "[off] ";
                string sub = def.TargetType + ": " + def.Target;
                if (!string.IsNullOrEmpty(def.Permission))
                {
                    sub += "  |  " + def.Permission;
                }
                else
                {
                    sub += "  |  everyone";
                }

                rows.Add(new RowInfo
                {
                    Action = "edit|" + scope + "|" + def.Id,
                    Label = marker + def.Label,
                    Sub = sub,
                    Icon = string.IsNullOrEmpty(def.Icon) ? DefaultIcon(def) : def.Icon,
                    IconTint = def.IconTint,
                    Badge = def.Enabled ? string.Empty : "OFF",
                    Enabled = true,
                    Selected = string.Equals(session.SelectedId, def.Id, StringComparison.OrdinalIgnoreCase)
                });
            }

            if (list.Count == 0)
            {
                rows.Add(new RowInfo
                {
                    Label = "No buttons in this list yet.",
                    Sub = "Use New Button to create one.",
                    Enabled = false
                });
            }

            buttons.Add(new ButtonInfo { Action = "new|" + scope, Label = "New Button", Tooltip = "Create a button in this list.", Icon = "sprite:sa_stream_admin" });
            AddPagingButtons(session, pages, buttons);
            buttons.Add(new ButtonInfo { Action = "tab|home", Label = "Back to Home", Icon = "sprite:sa_stream_home" });
        }

        private void RenderEdit(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            string scope = NormalizeScope(session.Scope);
            if (!RequireManage(session, playerId, scope))
            {
                session.SelectedId = null;
                session.Tab = "home";
                RenderHome(playerId, session, rows, buttons, out subtitle);
                return;
            }

            ButtonDef def = FindButton(playerId, scope, session.SelectedId);
            if (def == null)
            {
                session.SelectedId = null;
                session.Status = "Button not found.";
                RenderScope(playerId, session, scope, rows, buttons, out subtitle);
                return;
            }

            session.Tab = scope == "personal" ? "mine" : "shared";
            subtitle = "Editing '" + def.Label + "'  |  " + (scope == "personal" ? "personal" : "shared") +
                       "  |  " + (def.Enabled ? "enabled" : "disabled");

            bool canLaunch = CanLaunch(playerId, def);
            rows.Add(new RowInfo { Label = "Label", Sub = def.Label, Enabled = false, Selected = false });
            rows.Add(new RowInfo { Label = "Description", Sub = string.IsNullOrEmpty(def.Description) ? "(none)" : def.Description, Enabled = false });
            rows.Add(new RowInfo { Label = "Icon", Sub = string.IsNullOrEmpty(def.Icon) ? "(default)" : def.Icon, Icon = string.IsNullOrEmpty(def.Icon) ? DefaultIcon(def) : def.Icon, IconTint = def.IconTint, Enabled = false });
            rows.Add(new RowInfo { Label = "Target Type", Sub = def.TargetType, Enabled = false });
            rows.Add(new RowInfo { Label = "Target", Sub = def.Target, Enabled = false });
            rows.Add(new RowInfo
            {
                Label = "Permission",
                Sub = string.IsNullOrEmpty(def.Permission) ? "(everyone)" : def.Permission,
                Enabled = false
            });
            rows.Add(new RowInfo
            {
                Label = "Status",
                Sub = (def.Enabled ? "enabled" : "disabled") +
                      (canLaunch ? "  |  you can launch it" : "  |  no permission"),
                Enabled = false
            });
            rows.Add(new RowInfo { Label = "Id", Sub = def.Id, Enabled = false });
            rows.Add(new RowInfo
            {
                Label = "Scope",
                Sub = scope == "personal" ? "Only this player's Server Assist" : "Every player with the permission",
                Enabled = false
            });

            buttons.Add(new ButtonInfo { Action = "editfield|" + scope + "|" + def.Id + "|label", Label = "Edit Label", Icon = "sprite:sa_stream_info" });
            buttons.Add(new ButtonInfo { Action = "editfield|" + scope + "|" + def.Id + "|description", Label = "Edit Description", Icon = "sprite:sa_stream_info" });
            buttons.Add(new ButtonInfo { Action = "editfield|" + scope + "|" + def.Id + "|icon", Label = "Edit Icon", Icon = "sprite:sa_stream_catalog" });
            buttons.Add(new ButtonInfo { Action = "iconsearch|" + scope + "|" + def.Id, Label = "Find Item Icon", Icon = "sprite:sa_stream_catalog" });
            buttons.Add(new ButtonInfo { Action = "editfield|" + scope + "|" + def.Id + "|type", Label = "Target Type", Icon = "sprite:sa_stream_link" });
            buttons.Add(new ButtonInfo { Action = "editfield|" + scope + "|" + def.Id + "|target", Label = "Edit Target", Icon = "sprite:sa_stream_link" });
            buttons.Add(new ButtonInfo { Action = "editfield|" + scope + "|" + def.Id + "|permission", Label = "Edit Permission", Icon = "sprite:sa_perm_plugin" });
            buttons.Add(new ButtonInfo { Action = "toggle|" + scope + "|" + def.Id, Label = def.Enabled ? "Disable" : "Enable", Icon = def.Enabled ? "sprite:sa_stream_status_paused" : "sprite:sa_stream_status_armed" });
            buttons.Add(new ButtonInfo { Action = "move|" + scope + "|" + def.Id + "|up", Label = "Move Up" });
            buttons.Add(new ButtonInfo { Action = "move|" + scope + "|" + def.Id + "|down", Label = "Move Down" });
            buttons.Add(new ButtonInfo { Action = "delete|" + scope + "|" + def.Id, Label = "Delete", Icon = "sprite:sa_act_ban" });
            buttons.Add(new ButtonInfo { Action = "back", Label = "Back", Icon = "sprite:sa_stream_home" });
        }

        private void RenderIconSearch(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            string scope = NormalizeScope(session.Scope);
            if (!RequireManage(session, playerId, scope))
            {
                session.IconSearchActive = false;
                session.SelectedId = null;
                session.Tab = "home";
                RenderHome(playerId, session, rows, buttons, out subtitle);
                return;
            }

            ButtonDef def = FindButton(playerId, scope, session.SelectedId);
            if (def == null)
            {
                session.IconSearchActive = false;
                session.SelectedId = null;
                session.Status = "Button not found.";
                RenderScope(playerId, session, scope, rows, buttons, out subtitle);
                return;
            }

            session.Tab = scope == "personal" ? "mine" : "shared";
            List<string> matches = new List<string>();
            string query = session.IconSearchQuery ?? string.Empty;
            ReadOnlyCollection<string> names = ItemClass.ItemNames;
            for (int i = 0; i < names.Count; i++)
            {
                string itemName = names[i];
                if (string.IsNullOrEmpty(itemName))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(query) &&
                    itemName.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    ItemClass itemClass = ItemClass.GetItemClass(itemName, true);
                    string localized = itemClass != null ? itemClass.GetLocalizedItemName() : string.Empty;
                    if (string.IsNullOrEmpty(localized) ||
                        localized.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                }

                matches.Add(itemName);
            }

            matches.Sort(StringComparer.OrdinalIgnoreCase);
            int pages = PageCount(matches.Count);
            ClampPage(session, matches.Count);
            subtitle = "Choose an item icon  |  " + matches.Count + " match(es)  |  page " + (session.Page + 1) + "/" + pages +
                       (string.IsNullOrEmpty(query) ? string.Empty : "  |  search: " + query);

            int start = session.Page * UiRows;
            for (int i = 0; i < UiRows && start + i < matches.Count; i++)
            {
                string itemName = matches[start + i];
                ItemClass itemClass = ItemClass.GetItemClass(itemName, true);
                string label = itemClass != null ? itemClass.GetLocalizedItemName() : itemName;
                if (string.IsNullOrEmpty(label))
                {
                    label = itemName;
                }

                rows.Add(new RowInfo
                {
                    Action = "iconset|" + scope + "|" + def.Id + "|item:" + itemName,
                    Label = label,
                    Sub = "item:" + itemName,
                    Icon = "item:" + itemName,
                    Enabled = true
                });
            }

            if (matches.Count == 0)
            {
                rows.Add(new RowInfo
                {
                    Label = "No items matched.",
                    Sub = "Try a shorter search, or clear the search to browse.",
                    Enabled = false
                });
            }

            AddPagingButtons(session, pages, buttons);
            buttons.Add(new ButtonInfo { Action = "iconsearch|" + scope + "|" + def.Id, Label = "Search Again", Icon = "sprite:sa_stream_catalog" });
            buttons.Add(new ButtonInfo { Action = "iconset|" + scope + "|" + def.Id + "|", Label = "Clear Icon", Icon = "sprite:sa_stream_link_cancel" });
            buttons.Add(new ButtonInfo { Action = "iconcancel", Label = "Back", Icon = "sprite:sa_stream_home" });
        }

        private void AddPagingButtons(Session session, int pages, List<ButtonInfo> buttons)
        {
            if (pages <= 1)
            {
                return;
            }

            if (session.Page > 0)
            {
                buttons.Add(new ButtonInfo { Action = "page|prev", Label = "< Prev" });
            }

            if (session.Page < pages - 1)
            {
                buttons.Add(new ButtonInfo { Action = "page|next", Label = "Next >" });
            }
        }

        private int PageCount(int count)
        {
            return Math.Max(1, (count + UiRows - 1) / UiRows);
        }

        private void ClampPage(Session session, int count)
        {
            int pages = PageCount(count);
            if (session.Page < 0)
            {
                session.Page = 0;
            }
            else if (session.Page >= pages)
            {
                session.Page = pages - 1;
            }
        }

        private sealed class HomeEntry
        {
            public GuiHook Hook;
            public GuiProvider Provider;
            public ButtonDef Def;
            public string Scope;
            public string Key = string.Empty;
            public string SortLabel = string.Empty;
            public bool Favorite;
            public int Order;
            public string Label = string.Empty;
        }

        #endregion

        #region Commands

        private void CommandServerAssist(IPlayer player, string command, string[] args)
        {
            if (args == null || args.Length == 0)
            {
                if (TryOpenGui(player))
                {
                    return;
                }

                Reply(player, "The graphical Server Assist is in the ServerAssist client mod. Install it on this client, or use /sa list / add / remove / reload.");
                return;
            }

            string sub = args[0].ToLowerInvariant();
            switch (sub)
            {
                case "gui":
                case "open":
                    if (!TryOpenGui(player))
                    {
                        Reply(player, "The Server Assist client mod is not installed or has not sent its hello yet.");
                    }
                    return;

                case "reload":
                    if (!RequireAdmin(player))
                    {
                        return;
                    }
                    LoadConfig();
                    LoadData();
                    SeedExampleButtons();
                    RegisterButtonPermissions();
                    EnsureDefaultPermissions();
                    Reply(player, "Reloaded ServerAssist config and data.");
                    return;

                case "list":
                    if (!RequireAdmin(player))
                    {
                        return;
                    }
                    ReplySharedList(player);
                    return;

                case "add":
                    if (!RequireAdmin(player))
                    {
                        return;
                    }
                    CommandAdd(player, args);
                    return;

                case "remove":
                    if (!RequireAdmin(player))
                    {
                        return;
                    }
                    if (args.Length < 2 || !data.Shared.Remove(args[1]))
                    {
                        Reply(player, "Usage: /sa remove <id>");
                        return;
                    }
                    SaveData();
                    Reply(player, "Shared button removed.");
                    return;

                case "help":
                    Reply(player, "Usage: /sa [open] | info | list | add <label> <window|command> <target> [permission] | remove <id> | reload");
                    return;

                case "info":
                    Reply(player, "ServerAssist 2.1.0  |  rows/page=" + UiRows + "  |  action buttons=" + UiButtons);
                    Reply(player, "Hooks=" + guiHooks.Count +
                                 "  |  shared=" + (data != null && data.Shared != null ? data.Shared.Count : 0) +
                                 "  |  favorites=" + (data != null && data.Favorites != null ? data.Favorites.Count : 0));
                    return;

                default:
                    Reply(player, "Unknown subcommand. Try /sa help.");
                    return;
            }
        }

        private void CommandAdd(IPlayer player, string[] args)
        {
            if (args.Length < 4)
            {
                Reply(player, "Usage: /sa add <label> <window|command> <target> [permission]");
                return;
            }

            string label = Clean(args[1], 48);
            if (string.IsNullOrEmpty(label))
            {
                Reply(player, "Label cannot be empty.");
                return;
            }

            string type = args[2].Trim().ToLowerInvariant();
            if (type != "window" && type != "command")
            {
                Reply(player, "Type must be window or command.");
                return;
            }

            string target = Clean(args[3], 160);
            if (string.IsNullOrEmpty(target))
            {
                Reply(player, "Target cannot be empty.");
                return;
            }

            string permissionName = args.Length > 4 ? Clean(args[4], 96) : string.Empty;
            string error;
            target = NormalizeTarget(target, type, "shared", player.Id, out error);
            if (error != null)
            {
                Reply(player, error);
                return;
            }

            permissionName = NormalizePermission(permissionName, "shared", player.Id, string.Empty, out error);
            if (error != null)
            {
                Reply(player, error);
                return;
            }

            if (data.Shared.Count >= config.MaxSharedButtons)
            {
                Reply(player, "The shared button list is full.");
                return;
            }

            string id = MakeButtonId(data.Shared, label);
            if (permissionName.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                permissionName = "serverassist.button." + id;
            }

            ButtonDef def = new ButtonDef
            {
                Id = id,
                Label = label,
                Description = string.Empty,
                TargetType = type,
                Target = target,
                Permission = permissionName,
                Enabled = true,
                Order = NextOrder(player.Id, "shared")
            };

            data.Shared[id] = def;
            SaveData();
            RegisterButtonPermission(def);
            Reply(player, "Created shared button '" + label + "' with id '" + id + "'.");
        }

        private void ReplySharedList(IPlayer player)
        {
            if (data.Shared.Count == 0)
            {
                Reply(player, "No shared buttons exist.");
                return;
            }

            Reply(player, "Shared buttons:");
            foreach (ButtonDef def in CollectScope(player.Id, "shared"))
            {
                Reply(player, "  " + def.Id + " | " + def.Label + " | " + def.TargetType + ": " + def.Target +
                             " | " + (string.IsNullOrEmpty(def.Permission) ? "everyone" : def.Permission) +
                             " | " + (def.Enabled ? "on" : "off"));
            }
        }

        private bool RequireAdmin(IPlayer player)
        {
            if (player != null && IsAdmin(player.Id))
            {
                return true;
            }

            Reply(player, "You need the 'serverassist.admin' permission.");
            return false;
        }

        private bool TryOpenGui(IPlayer player)
        {
            if (player == null || string.IsNullOrEmpty(player.Id) ||
                player.Id.Equals("server_console", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Session session = GetSession(player.Id);
            session.ActiveProvider = null;
            session.HookId = null;
            return SendClientPayload(player.Id, "a|home");
        }

        private bool SendClientPayload(string playerId, string payload)
        {
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(payload))
            {
                return false;
            }

            try
            {
                Type bridge = FindType("ServerAssistMod.ServerAssistServer");
                if (bridge == null)
                {
                    return false;
                }

                MethodInfo open = bridge.GetMethod(
                    "OpenForPlayer",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string), typeof(string) },
                    null);

                if (open == null)
                {
                    return false;
                }

                object result = open.Invoke(null, new object[] { playerId, payload });
                return result is bool && (bool)result;
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[ServerAssist] could not open the client window: " + exception.Message);
                return false;
            }
        }

        private static Type FindType(string fullName)
        {
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

        private void Reply(IPlayer player, string message)
        {
            if (player == null)
            {
                Interface.Oxide.LogInfo("[ServerAssist] " + message);
                return;
            }

            player.Reply(string.IsNullOrEmpty(config.ChatPrefix) ? message : config.ChatPrefix + " " + message);
        }

        #endregion

        #region Protocol

        private static string Encode(string value)
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

        private static string Decode(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("%0A", "\n").Replace("%7C", "|").Replace("%25", "%");
        }

        #endregion

        #region Data classes

        private sealed class Session
        {
            public string Tab = "home";
            public string Scope = "shared";
            public string SelectedId;
            public string HookId;
            public string ActiveProvider;
            public int Page;
            public string InputAction;
            public string InputLabel = string.Empty;
            public string InputValue = string.Empty;
            public string Status = string.Empty;
            public bool IconSearchActive;
            public string IconSearchQuery = string.Empty;
            public ButtonDef Draft;
        }

        private sealed class GuiHook
        {
            public string Id = string.Empty;
            public string Label = string.Empty;
            public string Description = string.Empty;
            public string Permission = string.Empty;
            public string PluginName = string.Empty;
            public string MethodName = string.Empty;
            public string Icon = "ui_game_symbol_hammer";
            public int Order;
        }

        private sealed class GuiProvider
        {
            public string Id = string.Empty;
            public string PluginName = string.Empty;
            public string HandlerMethod = string.Empty;
            public string Title = "Server Assist";
            public string Tab1Label = "Home";
            public string Tab2Label = "Shared";
            public string Tab3Label = "My Buttons";
            public string Icon = "ui_game_symbol_hammer";
        }

        private sealed class Configuration
        {
            public string ChatPrefix = "[ServerAssist]";
            public bool GrantUseToDefaultGroup = true;
            public bool GrantCustomToDefaultGroup = true;
            public bool SeedExampleButtons = true;
            public bool AdminSeesAllButtons = true;
            public bool AllowPlayerCustomButtons = true;
            public bool AllowPlayerCommandButtons = false;
            public int MaxSharedButtons = 60;
            public int MaxPersonalButtons = 20;
        }

        private sealed class ButtonDef
        {
            public string Id = string.Empty;
            public string Label = "Button";
            public string Description = string.Empty;
            public string TargetType = "window";
            public string Target = string.Empty;
            public string Permission = string.Empty;
            public string Icon = string.Empty;
            public string IconTint = string.Empty;
            public bool Enabled = true;
            public int Order = 0;
        }

        private sealed class StoredData
        {
            public int Version = 1;
            public bool SeededExamples;
            public List<string> Favorites = new List<string>();
            public Dictionary<string, ButtonDef> Shared = new Dictionary<string, ButtonDef>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, Dictionary<string, ButtonDef>> Personal =
                new Dictionary<string, Dictionary<string, ButtonDef>>(StringComparer.OrdinalIgnoreCase);
        }

        #endregion
    }
}







