using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("AdminShop", "TheRealM18", "1.1.1")]
    [Description("An ServerAssist GUI hook that sells valued items and permanent permission products for dukes.")]
    public class AdminShop : CovalencePlugin
    {
        private const string PermUse = "adminshop.use";
        private const string PermAdmin = "adminshop.admin";
        private const string ServerAssistPluginName = "ServerAssist";
        private const string ServerAssistHookId = "adminshop";
        private const string PluginTitle = "Admin Shop";
        private const int UiRows = 10;
        private const int UiButtons = 10;
        private const int MinQuality = 1;
        private const int MaxQuality = 6;

        private const string IconShop = "sprite:sa_admin_shop";
        private const string IconItems = "sprite:sa_nav_items";
        private const string IconKits = "sprite:sa_admin_kits";
        private const string IconAirdrop = "sprite:sa_act_airdrop";
        private const string IconArmor = "sprite:sa_act_godmode";
        private const string IconMods = "sprite:sa_admin_suite";
        private const string IconFood = "sprite:sa_act_heal";
        private const string IconResources = "sprite:sa_qln_pack";
        private const string IconBlocks = "sprite:sa_nav_world";
        private const string IconBooks = "sprite:sa_nav_logs";
        private const string IconOther = "sprite:sa_nav_help";
        private const string IconPermissions = "sprite:sa_permission_gui";
        private const string IconSearch = "sprite:sa_nav_logs";
        private const string IconRefresh = "sprite:sa_plugin_hot_reload";
        private const string IconBack = "sprite:sa_nav_teleport";
        private const string IconGive = "sprite:sa_act_give";
        private const string IconPrice = "sprite:sa_admin_shop";
        private const string IconTrader = "sprite:sa_auction_house";
        private const string IconQuality = "sprite:sa_act_repair";
        private const string IconBundle = "sprite:sa_qln_pack";
        private const string IconWarning = "sprite:sa_act_warn";
        private const string IconOwned = "sprite:sa_act_godmode";

        private static readonly string[] CategoryOrder =
        {
            "all",
            "weapons",
            "ammo",
            "armor",
            "mods",
            "food",
            "resources",
            "blocks",
            "books",
            "other"
        };

        private static readonly FastTags<TagGroup.Global> TagWeapon =
            FastTags<TagGroup.Global>.Parse("weapon");

        private static readonly FastTags<TagGroup.Global> TagArmor =
            FastTags<TagGroup.Global>.Parse("armor");

        private static readonly FastTags<TagGroup.Global> TagAmmo =
            FastTags<TagGroup.Global>.Parse("ammo");

        private static readonly FastTags<TagGroup.Global> TagFood =
            FastTags<TagGroup.Global>.Parse("food,drink,medical,medicine");

        private static readonly FastTags<TagGroup.Global> TagBooks =
            FastTags<TagGroup.Global>.Parse("book,books,schematic,schematics,magazine,perkBook");

        private static readonly FastTags<TagGroup.Global> TagResource =
            FastTags<TagGroup.Global>.Parse("resource,resources,scrap");

        private Configuration config;
        private ShopCatalog catalog;
        private ItemClass currencyItem;
        private ItemValue currencyValue;

        private readonly List<PermissionProduct> permissionProducts = new List<PermissionProduct>();
        private readonly Dictionary<string, PermissionProduct> permissionProductsById =
            new Dictionary<string, PermissionProduct>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, Session> sessions =
            new Dictionary<string, Session>(StringComparer.OrdinalIgnoreCase);

        private bool loggedHookRegistration;
        private bool loggedEmptyCatalog;
        private bool loggedCurrencyMissing;

        #region Lifecycle

        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
            permission.RegisterPermission(PermAdmin, this);

            LoadConfig();

            AddCovalenceCommand(new[] { "shop", "adminshop" }, nameof(CommandShop));

            timer.Once(1f, EnsureDefaultPermissions);
            timer.Once(2f, TryRegisterHook);
            timer.Once(3f, () => BuildCatalog(false));
            timer.Once(5f, TryRegisterHook);
            loggedHookRegistration = false;

            TryRegisterHook();
        }

        private void OnServerInitialized()
        {
            EnsureDefaultPermissions();
            BuildCatalog(true);
            TryRegisterHook();
        }

        private void OnServerSave()
        {
            // The shop keeps no persistent player data. Catalog prices are
            // recomputed from the current game data.
        }

        private void OnServerAssistReady()
        {
            // ServerAssist calls this after its own reload or first start.
            // Registering again is safe because it replaces the existing id.
            TryRegisterHook();
        }

        private void Unload()
        {
            TryUnregisterHook();
            sessions.Clear();
        }

        private void OnUserDisconnected(IPlayer player)
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
                Interface.Oxide.LogWarning("[AdminShop] config could not be read: " + exception.Message);
                config = null;
            }

            if (config == null)
            {
                config = new Configuration();
            }

            if (config.PriceMultiplier <= 0f || float.IsNaN(config.PriceMultiplier) ||
                float.IsInfinity(config.PriceMultiplier))
            {
                config.PriceMultiplier = 10f;
            }

            if (float.IsNaN(config.MinEconomicValue) || float.IsInfinity(config.MinEconomicValue))
            {
                config.MinEconomicValue = 0f;
            }

            if (config.MaxQuantityPerPurchase <= 0)
            {
                config.MaxQuantityPerPurchase = 1000;
            }

            config.MaxQuantityPerPurchase = Math.Max(1, Math.Min(1000000, config.MaxQuantityPerPurchase));

            if (config.ItemQuality < MinQuality || config.ItemQuality > MaxQuality)
            {
                config.ItemQuality = MaxQuality;
            }

            if (string.IsNullOrEmpty(config.CurrencyItem))
            {
                config.CurrencyItem = "casinoCoin";
            }
            else
            {
                config.CurrencyItem = config.CurrencyItem.Trim();
                if (config.CurrencyItem.Length == 0)
                {
                    config.CurrencyItem = "casinoCoin";
                }
            }

            config.HookLabel = string.IsNullOrEmpty(config.HookLabel) ? string.Empty : config.HookLabel.Trim();
            if (config.HookLabel.Length == 0)
            {
                config.HookLabel = "Admin Shop";
            }

            config.HookDescription = string.IsNullOrEmpty(config.HookDescription)
                ? string.Empty
                : config.HookDescription.Trim();
            if (config.HookDescription.Length == 0)
            {
                config.HookDescription = "Buy items and permissions with dukes.";
            }

            if (config.HookPermission == null)
            {
                config.HookPermission = PermUse;
            }
            else
            {
                config.HookPermission = config.HookPermission.Trim();
            }

            NormalizePermissionProducts();

            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        private void EnsureDefaultPermissions()
        {
            if (config == null)
            {
                LoadConfig();
            }

            string adminGroup = Interface.Oxide.Config.Options.DefaultGroups.Administrators;
            if (!string.IsNullOrEmpty(adminGroup))
            {
                GrantGroupIfMissing(adminGroup, PermAdmin);
                GrantGroupIfMissing(adminGroup, PermUse);
            }

            if (!config.GrantUseToDefaultGroup)
            {
                return;
            }

            string playerGroup = Interface.Oxide.Config.Options.DefaultGroups.Players;
            if (!string.IsNullOrEmpty(playerGroup))
            {
                GrantGroupIfMissing(playerGroup, PermUse);
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

        #endregion

        #region Permission products

        private void NormalizePermissionProducts()
        {
            permissionProducts.Clear();
            permissionProductsById.Clear();

            if (config.PermissionProducts == null)
            {
                config.PermissionProducts = new List<PermissionProduct>();
            }

            HashSet<string> usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<PermissionProduct> normalized = new List<PermissionProduct>();
            for (int i = 0; i < config.PermissionProducts.Count; i++)
            {
                PermissionProduct product = config.PermissionProducts[i];
                if (product == null)
                {
                    continue;
                }

                product.Name = Clean(product.Name, 48);
                if (string.IsNullOrEmpty(product.Name))
                {
                    product.Name = "Permission " + (i + 1);
                }

                product.Id = MakeProductId(
                    string.IsNullOrEmpty(product.Id) ? product.Name : product.Id,
                    usedIds);

                product.Description = Clean(product.Description, 160);
                product.Permission = Clean(product.Permission, 96);
                product.Price = Math.Max(0, product.Price);

                List<string> nodes = new List<string>();
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (product.Permissions != null)
                {
                    for (int j = 0; j < product.Permissions.Count; j++)
                    {
                        string node = Clean(product.Permissions[j], 96);
                        if (!string.IsNullOrEmpty(node) && seen.Add(node))
                        {
                            nodes.Add(node);
                        }
                    }
                }

                product.Permissions = nodes;

                normalized.Add(product);
                permissionProducts.Add(product);
                permissionProductsById[product.Id] = product;
            }

            config.PermissionProducts = normalized;

            if (permissionProducts.Count > 0)
            {
                Interface.Oxide.LogInfo(
                    "[AdminShop] loaded " + permissionProducts.Count + " permission product(s).");
            }
        }

        private static string MakeProductId(string source, HashSet<string> usedIds)
        {
            StringBuilder builder = new StringBuilder();
            string text = source == null ? string.Empty : source.ToLowerInvariant();
            for (int i = 0; i < text.Length && builder.Length < 40; i++)
            {
                char c = text[i];
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
                baseId = "permission";
            }

            string id = baseId;
            int suffix = 2;
            while (usedIds.Contains(id))
            {
                id = baseId + "-" + suffix;
                suffix++;
            }

            usedIds.Add(id);
            return id;
        }

        private static List<string> GetProductPermissions(PermissionProduct product)
        {
            List<string> nodes = new List<string>();
            if (product == null)
            {
                return nodes;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddProductPermission(nodes, seen, product.Permission);
            if (product.Permissions != null)
            {
                for (int i = 0; i < product.Permissions.Count; i++)
                {
                    AddProductPermission(nodes, seen, product.Permissions[i]);
                }
            }

            return nodes;
        }

        private static void AddProductPermission(List<string> nodes, HashSet<string> seen, string node)
        {
            node = Clean(node, 96);
            if (!string.IsNullOrEmpty(node) && seen.Add(node))
            {
                nodes.Add(node);
            }
        }

        private static string GetProductPermissionSummary(PermissionProduct product)
        {
            List<string> nodes = GetProductPermissions(product);
            if (nodes.Count == 0)
            {
                return "(none configured)";
            }

            return Clean(string.Join(", ", nodes.ToArray()), 160);
        }

        private List<PermissionProduct> GetEnabledPermissionProducts()
        {
            List<PermissionProduct> result = new List<PermissionProduct>();
            for (int i = 0; i < permissionProducts.Count; i++)
            {
                if (permissionProducts[i].Enabled)
                {
                    result.Add(permissionProducts[i]);
                }
            }

            return result;
        }

        private bool HasEnabledPermissionProducts()
        {
            return CountEnabledPermissionProducts() > 0;
        }

        private int CountEnabledPermissionProducts()
        {
            int count = 0;
            for (int i = 0; i < permissionProducts.Count; i++)
            {
                if (permissionProducts[i].Enabled)
                {
                    count++;
                }
            }

            return count;
        }

        private bool CanGrantProduct(PermissionProduct product, out string reason)
        {
            reason = null;
            List<string> nodes = GetProductPermissions(product);
            if (nodes.Count == 0)
            {
                reason = "no permission nodes are configured";
                return false;
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                string node = nodes[i];
                if (node.IndexOf('*') >= 0)
                {
                    reason = "wildcard permissions are not sold";
                    return false;
                }

                if (!permission.PermissionExists(node))
                {
                    reason = "permission node '" + node + "' is not registered by a loaded plugin";
                    return false;
                }
            }

            return true;
        }

        private int CountOwnedPermissions(string playerId, List<string> nodes)
        {
            if (string.IsNullOrEmpty(playerId) || nodes == null)
            {
                return 0;
            }

            int owned = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (permission.UserHasPermission(playerId, nodes[i]))
                {
                    owned++;
                }
            }

            return owned;
        }

        private void SavePermissionData()
        {
            if (!permission.IsLoaded)
            {
                return;
            }

            try
            {
                permission.SaveData();
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[AdminShop] permission data could not be saved: " + exception.Message);
            }
        }

        private void BuyPermission(string playerId, Session session, string productId)
        {
            PermissionProduct product;
            if (string.IsNullOrEmpty(productId) || !permissionProductsById.TryGetValue(productId, out product))
            {
                session.Status = "That permission product is no longer available.";
                return;
            }

            if (!product.Enabled)
            {
                session.Status = "That permission product is disabled.";
                return;
            }

            List<string> nodes = GetProductPermissions(product);
            string reason;
            if (!CanGrantProduct(product, out reason))
            {
                session.Status = "Cannot buy: " + reason + ".";
                return;
            }

            int owned = CountOwnedPermissions(playerId, nodes);
            if (owned >= nodes.Count)
            {
                session.Status = "You already have this permission.";
                return;
            }

            EntityPlayer entity = FindEntity(playerId);
            if (entity == null)
            {
                session.Status = "You must be online to buy permissions.";
                return;
            }

            int price = Math.Max(0, product.Price);
            int dukes = CountCurrency(entity);
            if (dukes < price)
            {
                session.Status = "Not enough dukes. Need " + FormatNumber(price) + ", you have " +
                                 FormatNumber(dukes) + ".";
                return;
            }

            int removed = 0;
            if (price > 0)
            {
                try
                {
                    removed = RemoveCurrency(entity, price);
                }
                catch (Exception exception)
                {
                    Interface.Oxide.LogWarning("[AdminShop] permission currency removal failed: " + exception.Message);
                    session.Status = "Could not take the dukes from your inventory.";
                    return;
                }

                if (removed != price)
                {
                    if (removed > 0)
                    {
                        AddCurrency(entity, removed);
                    }

                    session.Status = "Could not take the dukes from your inventory.";
                    return;
                }
            }

            List<string> granted = new List<string>();
            string failedNode = null;
            for (int i = 0; i < nodes.Count; i++)
            {
                string node = nodes[i];
                if (permission.UserHasPermission(playerId, node))
                {
                    continue;
                }

                granted.Add(node);
                try
                {
                    permission.GrantUserPermission(playerId, node, null);
                }
                catch (Exception exception)
                {
                    Interface.Oxide.LogError(
                        "[AdminShop] could not grant permission '" + node + "' to " + playerId + ": " + exception);
                    failedNode = node;
                    break;
                }

                if (!permission.UserHasPermission(playerId, node))
                {
                    failedNode = node;
                    break;
                }
            }

            if (failedNode != null)
            {
                for (int i = 0; i < granted.Count; i++)
                {
                    try
                    {
                        permission.RevokeUserPermission(playerId, granted[i]);
                    }
                    catch
                    {
                        // Best-effort rollback.
                    }
                }

                SavePermissionData();

                if (price > 0)
                {
                    AddCurrency(entity, price);
                }

                session.Status = "Could not grant '" + failedNode + "'. No dukes were taken.";
                return;
            }

            SavePermissionData();

            session.Status = "Bought '" + product.Name + "' for " + FormatNumber(price) + " dukes.";

            if (config.LogPurchases)
            {
                IPlayer covPlayer = players.FindPlayerById(playerId);
                string name = covPlayer != null ? covPlayer.Name : playerId;
                Interface.Oxide.LogInfo(
                    "[AdminShop] " + name + " (" + playerId + ") bought permission product '" + product.Name +
                    "' (" + string.Join(", ", nodes.ToArray()) + ") for " + price + " dukes.");
            }
        }

        #endregion

        #region ServerAssist hook

        private void TryRegisterHook()
        {
            if (config == null)
            {
                LoadConfig();
            }

            Plugin serverAssist = plugins.Find(ServerAssistPluginName);
            if (serverAssist == null)
            {
                return;
            }

            object result = InvokePlugin(
                serverAssist,
                "RegisterGuiHook",
                new[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string) },
                new object[]
                {
                    ServerAssistHookId,
                    config.HookLabel,
                    config.HookDescription,
                    config.HookPermission,
                    "AdminShop",
                    nameof(HandleGuiRequest)
                });

            bool registered = result is bool && (bool)result;
            if (registered)
            {
                serverAssist.Call("SetPanelIcon", ServerAssistHookId, "sprite:sa_admin_shop");
            }

            if (registered && !loggedHookRegistration)
            {
                loggedHookRegistration = true;
                Interface.Oxide.LogInfo("[AdminShop] registered ServerAssist GUI hook '" + ServerAssistHookId + "'.");
            }
            else if (!registered && !loggedHookRegistration)
            {
                Interface.Oxide.LogWarning("[AdminShop] ServerAssist is loaded but the GUI hook could not be registered yet.");
            }
        }

        private void TryUnregisterHook()
        {
            Plugin serverAssist = plugins.Find(ServerAssistPluginName);
            if (serverAssist == null)
            {
                return;
            }

            InvokePlugin(
                serverAssist,
                "UnregisterGuiHook",
                new[] { typeof(string) },
                new object[] { ServerAssistHookId });
        }

        private bool OpenShopForPlayer(string playerId, string subAction)
        {
            Plugin serverAssist = plugins.Find(ServerAssistPluginName);
            if (serverAssist == null)
            {
                return false;
            }

            object result = InvokePlugin(
                serverAssist,
                "OpenHookForPlayer",
                new[] { typeof(string), typeof(string), typeof(string) },
                new object[] { playerId, ServerAssistHookId, subAction });

            return result is bool && (bool)result;
        }

        private object InvokePlugin(Plugin plugin, string methodName, Type[] parameterTypes, object[] args)
        {
            if (plugin == null)
            {
                return null;
            }

            try
            {
                MethodInfo method = plugin.GetType().GetMethod(
                    methodName,
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    parameterTypes,
                    null);

                if (method == null)
                {
                    Interface.Oxide.LogWarning("[AdminShop] " + plugin.Name + " does not expose " + methodName + ".");
                    return null;
                }

                return method.Invoke(plugin, args);
            }
            catch (TargetInvocationException exception)
            {
                Exception inner = exception.InnerException ?? exception;
                Interface.Oxide.LogError("[AdminShop] " + plugin.Name + "." + methodName + " failed: " + inner);
                return null;
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError("[AdminShop] " + plugin.Name + "." + methodName + " failed: " + exception);
                return null;
            }
        }

        #endregion

        #region GUI entry point

        /// <summary>
        /// Called by ServerAssist while this plugin's GUI hook is active.
        /// The return value is the ServerAssist line protocol.
        /// </summary>
        public string HandleGuiRequest(string playerId, string payload)
        {
            try
            {
                if (config == null)
                {
                    LoadConfig();
                }

                if (!permission.IsLoaded)
                {
                    return ErrorView("The Oxide permission system is not loaded.");
                }

                if (string.IsNullOrEmpty(playerId))
                {
                    return ErrorView("Could not determine your player id.");
                }

                if (!CanUse(playerId))
                {
                    return ErrorView("You do not have permission to use the Admin Shop.");
                }

                if (catalog == null || catalog.All.Count == 0 || currencyItem == null)
                {
                    BuildCatalog(false);
                }

                if ((catalog == null || catalog.All.Count == 0) && !HasEnabledPermissionProducts())
                {
                    return ErrorView("The item catalog is not ready yet. Check the server log.");
                }

                if (currencyItem == null)
                {
                    return ErrorView("The configured currency item '" + config.CurrencyItem + "' was not found.");
                }

                Session session = GetSession(playerId);

                if (string.IsNullOrEmpty(payload) || payload == "open")
                {
                    payload = "a|open";
                }

                if (payload.StartsWith("t|", StringComparison.Ordinal))
                {
                    return HandleText(playerId, session, Decode(payload.Substring(2)));
                }

                if (payload.StartsWith("a|", StringComparison.Ordinal))
                {
                    return HandleAction(playerId, session, payload.Substring(2));
                }

                return ErrorView("Unknown shop request.");
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogException("[AdminShop] GUI request failed", exception);
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

        private void ResetSession(Session session)
        {
            session.Screen = "home";
            session.Category = "all";
            session.Query = string.Empty;
            session.SelectedId = null;
            session.SelectedPermissionId = null;
            session.Page = 0;
            session.Status = string.Empty;
            ClearPrompt(session);
        }

        private void ClearPrompt(Session session)
        {
            session.InputMode = null;
            session.InputLabel = string.Empty;
            session.InputValue = string.Empty;
        }

        private void PromptInput(Session session, string mode, string label, string value)
        {
            session.InputMode = mode;
            session.InputLabel = label ?? string.Empty;
            session.InputValue = value ?? string.Empty;
        }

        private string ErrorView(string message)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("v2\n");
            builder.Append("protocol=2\n");
            builder.Append("title=").Append(Encode(PluginTitle)).Append('\n');
            builder.Append("subtitle=").Append(Encode("Shop unavailable")).Append('\n');
            builder.Append("status=").Append(Encode(message)).Append('\n');
            builder.Append("background=").Append(Encode(IconWarning)).Append('\n');
            builder.Append("banner=").Append(Encode(IconWarning)).Append('\n');
            builder.Append("input=||0|\n");
            builder.Append("row=0||").Append(Encode("No data")).Append('|').Append(Encode(message))
                .Append("|0|0|").Append(Encode(IconWarning)).Append("|ERROR|\n");
            builder.Append("btn=0|refresh|").Append(Encode("Retry")).Append("|1||")
                .Append(Encode(IconRefresh)).Append("|\n");
            return builder.ToString();
        }

        #endregion

        #region Actions

        private string HandleAction(string playerId, Session session, string action)
        {
            string[] parts = action.Split('|');
            string command = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
            session.Status = string.Empty;
            ClearPrompt(session);

            switch (command)
            {
                case "open":
                    ResetSession(session);
                    break;

                case "home":
                    session.Screen = "home";
                    session.Category = "all";
                    session.Query = string.Empty;
                    session.SelectedId = null;
                    session.SelectedPermissionId = null;
                    session.Page = 0;
                    ClearPrompt(session);
                    break;

                case "back":
                    ClearPrompt(session);
                    if (session.Screen == "permissiondetail")
                    {
                        session.Screen = "permissions";
                        session.SelectedPermissionId = null;
                    }
                    else if (session.Screen == "permissions")
                    {
                        session.Screen = "home";
                        session.SelectedPermissionId = null;
                        session.Page = 0;
                    }
                    else if (session.Screen == "detail")
                    {
                        session.Screen = "list";
                        session.SelectedId = null;
                    }
                    else if (session.Screen == "list")
                    {
                        session.Screen = "home";
                        session.SelectedId = null;
                        session.Page = 0;
                    }
                    break;

                case "cat":
                    if (parts.Length > 1)
                    {
                        session.Screen = "list";
                        session.Category = parts[1].ToLowerInvariant();
                        session.Query = string.Empty;
                        session.SelectedId = null;
                        session.SelectedPermissionId = null;
                        session.Page = 0;
                        ClearPrompt(session);
                    }
                    break;

                case "permissions":
                    session.Screen = "permissions";
                    session.SelectedPermissionId = null;
                    session.Page = 0;
                    ClearPrompt(session);
                    break;

                case "permission":
                    if (parts.Length > 1)
                    {
                        session.Screen = "permissiondetail";
                        session.SelectedPermissionId = parts[1];
                        ClearPrompt(session);
                    }
                    break;

                case "buypermission":
                    if (parts.Length > 1)
                    {
                        BuyPermission(playerId, session, parts[1]);
                    }
                    break;

                case "search":
                    PromptInput(session, "search", "Search items by name (blank = all)", session.Query);
                    break;

                case "refresh":
                    session.Status = "Refreshed.";
                    break;

                case "cancel":
                    ClearPrompt(session);
                    session.Status = "Input cancelled.";
                    break;

                case "page":
                    if (parts.Length > 1)
                    {
                        if (parts[1].Equals("next", StringComparison.OrdinalIgnoreCase))
                        {
                            session.Page++;
                        }
                        else
                        {
                            session.Page--;
                        }

                        if (session.Page < 0)
                        {
                            session.Page = 0;
                        }
                    }
                    break;

                case "detail":
                    if (parts.Length > 1)
                    {
                        session.Screen = "detail";
                        session.SelectedId = parts[1];
                        ClearPrompt(session);
                    }
                    break;

                case "buy":
                    if (parts.Length > 2)
                    {
                        int quantity;
                        if (int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out quantity))
                        {
                            BuyItem(playerId, session, parts[1], quantity);
                        }
                        else
                        {
                            session.Status = "Invalid quantity.";
                        }
                    }
                    break;

                case "qty":
                    if (parts.Length > 1)
                    {
                        session.SelectedId = parts[1];
                        session.Screen = "detail";
                        PromptInput(
                            session,
                            "qty",
                            "Quantity (1-" + config.MaxQuantityPerPurchase + ")",
                            "1");
                    }
                    break;

                default:
                    session.Status = "Unknown action: " + command;
                    break;
            }

            return Render(playerId, session);
        }

        private string HandleText(string playerId, Session session, string value)
        {
            string mode = session.InputMode;
            string text = value ?? string.Empty;
            ClearPrompt(session);

            if (string.IsNullOrEmpty(mode))
            {
                session.Status = "Nothing is waiting for text input.";
                return Render(playerId, session);
            }

            if (mode == "search")
            {
                session.Query = Clean(text, 64);
                session.Category = "search";
                session.Screen = "list";
                session.SelectedId = null;
                session.Page = 0;
                session.Status = session.Query.Length == 0 ? "Showing all items." : "Search: " + session.Query;
                return Render(playerId, session);
            }

            if (mode == "qty")
            {
                int quantity;
                if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out quantity) ||
                    quantity <= 0)
                {
                    session.Status = "Type a whole number greater than 0.";
                    PromptInput(
                        session,
                        "qty",
                        "Quantity (1-" + config.MaxQuantityPerPurchase + ")",
                        text);
                    return Render(playerId, session);
                }

                BuyItem(playerId, session, session.SelectedId, quantity);
                return Render(playerId, session);
            }

            session.Status = "Unknown input action.";
            return Render(playerId, session);
        }

        #endregion

        #region Purchase

        private void BuyItem(string playerId, Session session, string itemId, int quantity)
        {
            ShopEntry entry;
            if (string.IsNullOrEmpty(itemId) || catalog == null || !catalog.ById.TryGetValue(itemId, out entry))
            {
                session.Status = "That item is no longer in the shop.";
                return;
            }

            if (quantity <= 0)
            {
                session.Status = "Quantity must be at least 1.";
                return;
            }

            if (quantity > config.MaxQuantityPerPurchase)
            {
                session.Status = "The maximum quantity per purchase is " + config.MaxQuantityPerPurchase + ".";
                return;
            }

            EntityPlayer player = FindEntity(playerId);
            if (player == null)
            {
                session.Status = "You must be online to buy items.";
                return;
            }

            int unitPrice = GetUnitPrice(entry);
            long total = (long)unitPrice * quantity;
            if (total > int.MaxValue)
            {
                session.Status = "That purchase is too large.";
                return;
            }

            int dukes = CountCurrency(player);
            if (dukes < total)
            {
                session.Status = "Not enough dukes. Need " + FormatNumber(total) + ", you have " + FormatNumber(dukes) + ".";
                return;
            }

            int removed;
            try
            {
                removed = RemoveCurrency(player, (int)total);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[AdminShop] currency removal failed: " + exception.Message);
                session.Status = "Could not take the dukes from your inventory.";
                return;
            }

            if (removed != total)
            {
                if (removed > 0)
                {
                    AddCurrency(player, removed);
                }

                session.Status = "Could not take the dukes from your inventory.";
                return;
            }

            bool canFit;
            try
            {
                canFit = CanFitItems(player, entry.ItemClass, quantity);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[AdminShop] inventory check failed: " + exception.Message);
                canFit = false;
            }

            if (!canFit)
            {
                AddCurrency(player, (int)total);
                session.Status = "Not enough inventory space for " + quantity + "x " + entry.Name + ".";
                return;
            }

            int added = GiveItems(player, entry.ItemClass, quantity);
            int paid = (int)total;
            if (added < quantity)
            {
                int refund = (quantity - added) * unitPrice;
                AddCurrency(player, refund);
                paid -= refund;
                session.Status = "Inventory full: bought " + added + "/" + quantity + "x " + entry.Name +
                                 ", refunded " + FormatNumber(refund) + " dukes.";
            }
            else
            {
                session.Status = "Bought " + quantity + "x " + entry.Name + " for " + FormatNumber(total) + " dukes.";
            }

            if (config.LogPurchases && added > 0)
            {
                IPlayer covPlayer = players.FindPlayerById(playerId);
                string name = covPlayer != null ? covPlayer.Name : playerId;
                Interface.Oxide.LogInfo(
                    "[AdminShop] " + name + " (" + playerId + ") bought " + added + "x " + entry.Name +
                    " (type " + entry.TypeId + ") for " + paid + " dukes.");
            }
        }

        private EntityPlayer FindEntity(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return null;
            }

            IPlayer player = players.FindPlayerById(playerId);
            return player != null ? player.Object as EntityPlayer : null;
        }

        private int CountCurrency(EntityPlayer player)
        {
            if (player == null || currencyItem == null)
            {
                return 0;
            }

            ItemValue value = GetCurrencyValue();
            int count = 0;
            if (player.inventory != null)
            {
                count += player.inventory.GetItemCount(value);
            }

            if (player.bag != null)
            {
                count += player.bag.GetItemCount(value);
            }

            return count;
        }

        private int RemoveCurrency(EntityPlayer player, int amount)
        {
            if (player == null || currencyItem == null || amount <= 0)
            {
                return 0;
            }

            ItemValue value = GetCurrencyValue();
            int removed = 0;
            if (player.inventory != null)
            {
                removed += player.inventory.DecItem(value, amount);
            }

            int remaining = amount - removed;
            if (remaining > 0 && player.bag != null)
            {
                removed += player.bag.DecItem(value, remaining);
            }

            return removed;
        }

        private void AddCurrency(EntityPlayer player, int amount)
        {
            if (player == null || currencyItem == null || amount <= 0)
            {
                return;
            }

            int added = GiveItems(player, currencyItem, amount);
            if (added < amount)
            {
                Interface.Oxide.LogError(
                    "[AdminShop] could not refund " + (amount - added) + " " + config.CurrencyItem +
                    " to player '" + player.PlayerDisplayName + "'.");
            }
        }

        private ItemValue GetCurrencyValue()
        {
            if (currencyValue == null && currencyItem != null)
            {
                currencyValue = new ItemValue(currencyItem.Id);
            }

            return currencyValue;
        }

        private int GiveItems(EntityPlayer player, ItemClass itemClass, int count)
        {
            if (player == null || itemClass == null || count <= 0)
            {
                return 0;
            }

            int maxStack = GetMaxStack(itemClass);
            int remaining = count;

            try
            {
                while (remaining > 0)
                {
                    int amount = Math.Min(remaining, maxStack);
                    ItemStack stack = new ItemStack(CreateItemValue(itemClass), amount);
                    int added = AddToPlayer(player, stack);
                    if (added <= 0)
                    {
                        break;
                    }

                    remaining -= added;
                    if (added < amount)
                    {
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning(
                    "[AdminShop] could not give " + GetInternalName(itemClass) + " to " +
                    player.PlayerDisplayName + ": " + exception.Message);
            }

            return count - remaining;
        }

        private ItemValue CreateItemValue(ItemClass itemClass)
        {
            int quality = GetPurchaseQuality(itemClass);
            return new ItemValue(itemClass.Id, quality, quality, false);
        }

        private int GetPurchaseQuality(ItemClass itemClass)
        {
            if (itemClass == null || !itemClass.HasQuality)
            {
                return 1;
            }

            int quality = config.ItemQuality;
            if (quality < MinQuality || quality > MaxQuality)
            {
                quality = MaxQuality;
            }

            return quality;
        }

        private int GetMaxStack(ItemClass itemClass)
        {
            int maxStack = itemClass != null && itemClass.CanStack() ? itemClass.MaxCount : 1;
            return Math.Max(1, maxStack);
        }

        private int AddToPlayer(EntityPlayer player, ItemStack stack)
        {
            if (player == null || stack == null)
            {
                return 0;
            }

            int original = stack.count;
            ItemStack remaining = stack.Clone();

            if (player.bag != null)
            {
                player.bag.TryStackItem(0, remaining);
            }

            if (remaining.count > 0 && player.inventory != null)
            {
                player.inventory.TryStackItem(0, remaining);
            }

            if (remaining.count > 0 && player.bag != null && player.bag.AddItem(remaining))
            {
                remaining.count = 0;
            }
            else if (remaining.count > 0 && player.inventory != null && player.inventory.AddItem(remaining))
            {
                remaining.count = 0;
            }

            return original - remaining.count;
        }

        private bool CanFitItems(EntityPlayer player, ItemClass itemClass, int count)
        {
            if (player == null || itemClass == null)
            {
                return false;
            }

            int maxStack = GetMaxStack(itemClass);
            long space = 0;

            if (player.bag != null)
            {
                space += FreeSpace(player.bag.ItemGrid, itemClass, maxStack);
            }

            if (player.inventory != null)
            {
                space += FreeSpace(player.inventory, itemClass, maxStack);
            }

            return space >= count;
        }

        private static long FreeSpace(ItemStackGrid grid, ItemClass itemClass, int maxStack)
        {
            if (grid == null)
            {
                return 0;
            }

            long space = 0;
            for (int i = 0; i < grid.Length; i++)
            {
                ItemStack stack = grid.GetItem(i);
                if (stack == null || stack.IsEmpty())
                {
                    space += maxStack;
                    continue;
                }

                if (itemClass.CanStack() && stack.itemValue.type == itemClass.Id && stack.count < maxStack)
                {
                    space += maxStack - stack.count;
                }
            }

            return space;
        }

        private static long FreeSpace(Inventory inventory, ItemClass itemClass, int maxStack)
        {
            if (inventory == null)
            {
                return 0;
            }

            long space = 0;
            for (int i = 0; i < inventory.Length; i++)
            {
                ItemStack stack = inventory.GetStackAt(i);
                if (stack == null || stack.IsEmpty())
                {
                    space += maxStack;
                    continue;
                }

                if (itemClass.CanStack() && stack.itemValue.type == itemClass.Id && stack.count < maxStack)
                {
                    space += maxStack - stack.count;
                }
            }

            return space;
        }

        #endregion

        #region Pricing and catalog

        private void BuildCatalog(bool force)
        {
            if (config == null)
            {
                LoadConfig();
            }

            if (!force && catalog != null && catalog.All.Count > 0 && currencyItem != null)
            {
                return;
            }

            ItemClass resolvedCurrency = null;
            try
            {
                resolvedCurrency = ItemClass.GetItemClass(config.CurrencyItem, true);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[AdminShop] currency lookup failed: " + exception.Message);
            }

            currencyItem = resolvedCurrency;
            currencyValue = null;

            if (currencyItem == null)
            {
                if (!loggedCurrencyMissing)
                {
                    loggedCurrencyMissing = true;
                    Interface.Oxide.LogError(
                        "[AdminShop] currency item '" + config.CurrencyItem +
                        "' was not found. The shop cannot take payment.");
                }
            }
            else
            {
                loggedCurrencyMissing = false;
            }

            ItemClass[] items = ItemClass.list;
            if (items == null || items.Length == 0)
            {
                catalog = null;
                return;
            }

            ShopCatalog result = new ShopCatalog();
            for (int i = 1; i < items.Length; i++)
            {
                ItemClass itemClass = items[i];
                if (itemClass == null)
                {
                    continue;
                }

                try
                {
                    AddCatalogItem(result, itemClass, resolvedCurrency);
                }
                catch (Exception exception)
                {
                    Interface.Oxide.LogWarning(
                        "[AdminShop] skipped item '" + i + "': " + exception.Message);
                }
            }

            result.All.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            result.BuildCategories(CategoryOrder);

            catalog = result;

            if (result.All.Count == 0)
            {
                if (!loggedEmptyCatalog)
                {
                    loggedEmptyCatalog = true;
                    Interface.Oxide.LogWarning(
                        "[AdminShop] catalog built with 0 items; the shop will retry when it is opened.");
                }

                return;
            }

            loggedEmptyCatalog = false;
            Interface.Oxide.LogInfo(
                "[AdminShop] catalog built: " + result.All.Count + " items, currency=" + config.CurrencyItem + ".");
        }

        private void AddCatalogItem(ShopCatalog result, ItemClass itemClass, ItemClass resolvedCurrency)
        {
            if (itemClass.Id <= 0)
            {
                return;
            }

            if (resolvedCurrency != null && itemClass.Id == resolvedCurrency.Id)
            {
                // Do not sell the currency for itself.
                return;
            }

            if (itemClass.IsQuestItem)
            {
                return;
            }

            if (!config.IncludeBlocks && itemClass.IsBlock())
            {
                return;
            }

            if (!config.IncludeCreativeItems && !IsPlayerItem(itemClass))
            {
                return;
            }

            if (!config.IncludeHiddenItems && itemClass.UserHidden != null && itemClass.UserHidden.Value)
            {
                return;
            }

            float economicValue = GetEconomicValue(itemClass);
            if (float.IsNaN(economicValue) || float.IsInfinity(economicValue) ||
                economicValue <= config.MinEconomicValue)
            {
                return;
            }

            string name = GetDisplayName(itemClass);
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            ShopEntry entry = new ShopEntry
            {
                Id = itemClass.Id.ToString(CultureInfo.InvariantCulture),
                TypeId = itemClass.Id,
                ItemClass = itemClass,
                Name = name,
                SearchName = (name + " " + GetInternalName(itemClass)).ToLowerInvariant(),
                Category = Classify(itemClass),
                EconomicValue = economicValue,
                BundleSize = Math.Max(1, GetBundleSize(itemClass)),
                HasQuality = itemClass.HasQuality,
                MaxStack = GetMaxStack(itemClass)
            };

            if (!result.ById.ContainsKey(entry.Id))
            {
                result.ById.Add(entry.Id, entry);
                result.All.Add(entry);
            }
        }

        private static bool IsPlayerItem(ItemClass itemClass)
        {
            return itemClass.CreativeMode == EnumCreativeMode.All ||
                   itemClass.CreativeMode == EnumCreativeMode.Player;
        }

        private string GetDisplayName(ItemClass itemClass)
        {
            string name = null;
            try
            {
                name = itemClass.GetLocalizedItemName();
            }
            catch
            {
                // Fall back to the internal item name below.
            }

            if (string.IsNullOrEmpty(name))
            {
                name = GetInternalName(itemClass);
            }

            return Clean(name, 64);
        }

        private string Classify(ItemClass itemClass)
        {
            if (itemClass.IsBlock())
            {
                return "blocks";
            }

            if (itemClass is ItemClassModifier)
            {
                return "mods";
            }

            if (itemClass is ItemClassArmor || itemClass.HasAnyTags(TagArmor))
            {
                return "armor";
            }

            if (itemClass.HasAnyTags(TagWeapon))
            {
                return "weapons";
            }

            if (itemClass.HasAnyTags(TagAmmo))
            {
                return "ammo";
            }

            if (itemClass.HasAnyTags(TagFood))
            {
                return "food";
            }

            if (itemClass.HasAnyTags(TagBooks))
            {
                return "books";
            }

            if (itemClass.HasAnyTags(TagResource))
            {
                return "resources";
            }

            string name = GetInternalName(itemClass);
            if (name != null && name.StartsWith("resource", StringComparison.OrdinalIgnoreCase))
            {
                return "resources";
            }

            return "other";
        }

        private static float GetEconomicValue(ItemClass itemClass)
        {
            if (itemClass == null)
            {
                return 0f;
            }

            if (itemClass.IsBlock())
            {
                Block block = itemClass.GetBlock();
                if (block != null && block.EconomicValue > 0f)
                {
                    return block.EconomicValue;
                }
            }

            return itemClass.EconomicValue;
        }

        private static int GetBundleSize(ItemClass itemClass)
        {
            if (itemClass == null)
            {
                return 1;
            }

            if (itemClass.IsBlock())
            {
                Block block = itemClass.GetBlock();
                if (block != null && block.EconomicBundleSize > 0)
                {
                    return block.EconomicBundleSize;
                }
            }

            return itemClass.EconomicBundleSize;
        }

        private int GetUnitPrice(ShopEntry entry)
        {
            double price = GetTraderUnitValue(entry) * (double)config.PriceMultiplier;
            if (price < 1d)
            {
                price = 1d;
            }

            if (price > int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)Math.Ceiling(price);
        }

        private int GetTraderUnitValue(ShopEntry entry)
        {
            if (entry == null)
            {
                return 0;
            }

            double multiplier = 1d;
            if (config.UseTraderBuyMarkup)
            {
                multiplier *= GetTraderBuyMarkup();
            }

            if (config.UseTraderQualityMod && entry.HasQuality)
            {
                multiplier *= GetQualityMod(entry.ItemClass, GetPurchaseQuality(entry.ItemClass));
            }

            double value = entry.EconomicValue * multiplier / entry.BundleSize;
            if (value < 1d)
            {
                value = 1d;
            }

            if (value > int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)Math.Ceiling(value);
        }

        private static float GetTraderBuyMarkup()
        {
            float markup = TraderInfo.BuyMarkup;
            return markup > 0f ? markup : 1f;
        }

        private static float GetQualityMod(ItemClass itemClass, int quality)
        {
            float min = itemClass.TraderQualityMinMod;
            float max = itemClass.TraderQualityMaxMod;

            if (min <= 0f && max <= 0f)
            {
                min = TraderInfo.QualityMinMod;
                max = TraderInfo.QualityMaxMod;
            }

            if (min <= 0f)
            {
                min = 1f;
            }

            if (max <= 0f)
            {
                max = 1f;
            }

            float t = (Math.Max(MinQuality, Math.Min(MaxQuality, quality)) - 1f) / 5f;
            return min + (max - min) * t;
        }

        #endregion

        #region Rendering

        private sealed class RowInfo
        {
            public string Action = string.Empty;
            public string Label = string.Empty;
            public string Sub = string.Empty;
            public bool Enabled = true;
            public bool Selected;
            public string Icon = string.Empty;
            public string Badge = string.Empty;
            public string Tint = string.Empty;
        }

        private sealed class ButtonInfo
        {
            public string Action = string.Empty;
            public string Label = string.Empty;
            public string Tooltip = string.Empty;
            public bool Enabled = true;
            public string Icon = string.Empty;
            public string Tint = string.Empty;
        }

        private string Render(string playerId, Session session)
        {
            List<RowInfo> rows = new List<RowInfo>();
            List<ButtonInfo> buttons = new List<ButtonInfo>();
            string subtitle;

            if (session.Screen == "permissiondetail")
            {
                RenderPermissionDetail(playerId, session, rows, buttons, out subtitle);
            }
            else if (session.Screen == "permissions")
            {
                RenderPermissionList(playerId, session, rows, buttons, out subtitle);
            }
            else if (session.Screen == "detail")
            {
                RenderDetail(playerId, session, rows, buttons, out subtitle);
            }
            else if (session.Screen == "list")
            {
                RenderList(playerId, session, rows, buttons, out subtitle);
            }
            else
            {
                session.Screen = "home";
                RenderHome(playerId, session, rows, buttons, out subtitle);
            }

            StringBuilder builder = new StringBuilder(4096);
            string banner = GetPanelBanner(session);
            builder.Append("v2\n");
            builder.Append("protocol=2\n");
            builder.Append("title=").Append(Encode(PluginTitle)).Append('\n');
            builder.Append("subtitle=").Append(Encode(subtitle)).Append('\n');
            builder.Append("status=").Append(Encode(session.Status)).Append('\n');
            builder.Append("background=").Append(Encode(banner)).Append('\n');
            builder.Append("banner=").Append(Encode(banner)).Append('\n');
            builder.Append("input=")
                .Append(Encode(string.IsNullOrEmpty(session.InputMode) ? string.Empty : session.InputLabel))
                .Append('|')
                .Append(string.IsNullOrEmpty(session.InputMode) ? "0" : "1")
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
                    .Append(Encode(row.Tint)).Append('\n');
            }

            for (int i = 0; i < buttons.Count && i < UiButtons; i++)
            {
                ButtonInfo button = buttons[i];
                builder.Append("btn=").Append(i).Append('|')
                    .Append(Encode(button.Action)).Append('|')
                    .Append(Encode(button.Label)).Append('|')
                    .Append(button.Enabled ? "1" : "0").Append('|')
                    .Append(Encode(button.Tooltip)).Append('|')
                    .Append(Encode(button.Icon)).Append('|')
                    .Append(Encode(button.Tint)).Append('\n');
            }

            return builder.ToString();
        }

        private string GetPanelBanner(Session session)
        {
            if (session == null)
            {
                return IconShop;
            }

            if (session.Screen == "permissions" || session.Screen == "permissiondetail")
            {
                return IconPermissions;
            }

            if (session.Screen == "detail")
            {
                ShopEntry entry;
                if (catalog != null && !string.IsNullOrEmpty(session.SelectedId) &&
                    catalog.ById.TryGetValue(session.SelectedId, out entry))
                {
                    return GetItemIcon(entry);
                }

                return IconShop;
            }

            if (session.Screen == "list")
            {
                if (string.Equals(session.Category, "search", StringComparison.OrdinalIgnoreCase))
                {
                    return IconSearch;
                }

                return GetCategoryIcon(session.Category);
            }

            return IconShop;
        }

        private void RenderHome(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            int total = catalog != null ? catalog.All.Count : 0;
            int permissions = CountEnabledPermissionProducts();
            subtitle = "Item price x" + config.PriceMultiplier.ToString("0.##", CultureInfo.InvariantCulture) +
                       "  |  Dukes: " + GetDukesLabel(playerId) +
                       "  |  " + total + " items";
            if (permissions > 0)
            {
                subtitle += "  |  " + permissions + " permission" + (permissions == 1 ? string.Empty : "s");
            }

            for (int i = 0; i < CategoryOrder.Length; i++)
            {
                string key = CategoryOrder[i];
                int count = catalog != null ? catalog.CountFor(key) : 0;
                rows.Add(new RowInfo
                {
                    Action = "cat|" + key,
                    Label = GetCategoryTitle(key),
                    Sub = count + " item" + (count == 1 ? string.Empty : "s"),
                    Enabled = count > 0,
                    Selected = false,
                    Icon = GetCategoryIcon(key)
                });
            }

            if (catalog != null && catalog.All.Count > 0)
            {
                buttons.Add(new ButtonInfo
                {
                    Action = "search",
                    Label = "Search",
                    Tooltip = "Search every shop item by name.",
                    Icon = IconSearch
                });
            }

            if (HasEnabledPermissionProducts())
            {
                buttons.Add(new ButtonInfo
                {
                    Action = "permissions",
                    Label = "Permissions",
                    Tooltip = "Buy permanent permissions with dukes.",
                    Icon = IconPermissions
                });
            }

            buttons.Add(new ButtonInfo
            {
                Action = "refresh",
                Label = "Refresh",
                Tooltip = "Redraw the shop.",
                Icon = IconRefresh
            });
        }

        private void RenderList(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            if (catalog == null || catalog.All.Count == 0)
            {
                subtitle = "Items  |  catalog unavailable  |  Dukes: " + GetDukesLabel(playerId);
                rows.Add(new RowInfo
                {
                    Label = "Item catalog unavailable.",
                    Sub = "Run /adminshop reload or check the server log.",
                    Enabled = false,
                    Icon = IconWarning,
                    Badge = "ERROR"
                });
                buttons.Add(new ButtonInfo
                {
                    Action = "home",
                    Label = "Categories",
                    Tooltip = "Return to the category list.",
                    Icon = IconShop
                });
                buttons.Add(new ButtonInfo
                {
                    Action = "refresh",
                    Label = "Refresh",
                    Tooltip = "Redraw this list.",
                    Icon = IconRefresh
                });
                return;
            }

            List<ShopEntry> list;
            string title;
            if (string.Equals(session.Category, "search", StringComparison.OrdinalIgnoreCase))
            {
                list = catalog.Search(session.Query);
                title = "Search: " + (string.IsNullOrEmpty(session.Query) ? "(all items)" : session.Query);
            }
            else
            {
                list = catalog.GetCategory(session.Category);
                title = GetCategoryTitle(session.Category);
            }

            int pages = Math.Max(1, (list.Count + UiRows - 1) / UiRows);
            if (session.Page < 0)
            {
                session.Page = 0;
            }
            else if (session.Page >= pages)
            {
                session.Page = pages - 1;
            }

            subtitle = title + "  |  " + list.Count + " item" + (list.Count == 1 ? string.Empty : "s") +
                       "  |  page " + (session.Page + 1) + "/" + pages +
                       "  |  Dukes: " + GetDukesLabel(playerId);

            int start = session.Page * UiRows;
            for (int i = 0; i < UiRows && start + i < list.Count; i++)
            {
                ShopEntry entry = list[start + i];
                int price = GetUnitPrice(entry);
                string sub = FormatNumber(price) + " dukes  |  stack " + entry.MaxStack;
                if (entry.HasQuality)
                {
                    sub += "  |  QL" + GetPurchaseQuality(entry.ItemClass);
                }

                if (entry.BundleSize > 1)
                {
                    sub += "  |  bundle " + entry.BundleSize;
                }

                rows.Add(new RowInfo
                {
                    Action = "detail|" + entry.Id,
                    Label = entry.Name,
                    Sub = sub,
                    Enabled = true,
                    Icon = GetItemIcon(entry)
                });
            }

            if (list.Count == 0)
            {
                rows.Add(new RowInfo
                {
                    Label = "No items found.",
                    Sub = "Try another search or category.",
                    Enabled = false,
                    Icon = IconSearch
                });
            }

            buttons.Add(new ButtonInfo
            {
                Action = "search",
                Label = "Search",
                Tooltip = "Search every shop item by name.",
                Icon = IconSearch
            });
            buttons.Add(new ButtonInfo
            {
                Action = "home",
                Label = "Categories",
                Tooltip = "Return to the category list.",
                Icon = IconShop
            });

            if (session.Page > 0)
            {
                buttons.Add(new ButtonInfo
                {
                    Action = "page|prev",
                    Label = "< Prev",
                    Tooltip = "Previous page.",
                    Icon = IconBack
                });
            }

            if (session.Page < pages - 1)
            {
                buttons.Add(new ButtonInfo
                {
                    Action = "page|next",
                    Label = "Next >",
                    Tooltip = "Next page.",
                    Icon = IconBack
                });
            }

            buttons.Add(new ButtonInfo
            {
                Action = "refresh",
                Label = "Refresh",
                Tooltip = "Redraw this list.",
                Icon = IconRefresh
            });
        }

        private void RenderDetail(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            ShopEntry entry;
            if (catalog == null || string.IsNullOrEmpty(session.SelectedId) ||
                !catalog.ById.TryGetValue(session.SelectedId, out entry))
            {
                session.Screen = "list";
                session.SelectedId = null;
                RenderList(playerId, session, rows, buttons, out subtitle);
                return;
            }

            int unitPrice = GetUnitPrice(entry);
            int traderValue = GetTraderUnitValue(entry);
            int dukes = GetDukes(playerId);
            int quality = GetPurchaseQuality(entry.ItemClass);
            int affordable = unitPrice > 0 && dukes >= 0 ? dukes / unitPrice : 0;

            subtitle = entry.Name + "  |  " + FormatNumber(unitPrice) + " dukes each  |  Dukes: " +
                       (dukes >= 0 ? FormatNumber(dukes) : "offline");

            rows.Add(new RowInfo
            {
                Label = "Item",
                Sub = entry.Name,
                Enabled = false,
                Icon = GetItemIcon(entry)
            });
            rows.Add(new RowInfo
            {
                Label = "Unit Price",
                Sub = FormatNumber(unitPrice) + " dukes",
                Enabled = false,
                Icon = IconPrice
            });
            rows.Add(new RowInfo
            {
                Label = "Trader Value",
                Sub = FormatNumber(traderValue) + " dukes each",
                Enabled = false,
                Icon = IconTrader
            });
            rows.Add(new RowInfo
            {
                Label = "Quality",
                Sub = entry.HasQuality ? "QL" + quality : "not quality-based",
                Enabled = false,
                Icon = IconQuality
            });
            rows.Add(new RowInfo
            {
                Label = "Bundle Size",
                Sub = entry.BundleSize.ToString(CultureInfo.InvariantCulture),
                Enabled = false,
                Icon = IconBundle
            });
            rows.Add(new RowInfo
            {
                Label = "Your Dukes",
                Sub = dukes >= 0 ? FormatNumber(dukes) : "offline",
                Enabled = false,
                Icon = IconPrice
            });
            rows.Add(new RowInfo
            {
                Label = "Can Afford",
                Sub = affordable + " item" + (affordable == 1 ? string.Empty : "s"),
                Enabled = false,
                Icon = IconGive
            });
            rows.Add(new RowInfo
            {
                Label = "Max Purchase",
                Sub = config.MaxQuantityPerPurchase.ToString(CultureInfo.InvariantCulture),
                Enabled = false,
                Icon = IconItems
            });

            buttons.Add(new ButtonInfo
            {
                Action = "buy|" + entry.Id + "|1",
                Label = "Buy 1",
                Enabled = dukes >= unitPrice,
                Tooltip = FormatNumber(unitPrice) + " dukes",
                Icon = IconGive
            });
            buttons.Add(new ButtonInfo
            {
                Action = "buy|" + entry.Id + "|10",
                Label = "Buy 10",
                Enabled = dukes >= (long)unitPrice * 10 && config.MaxQuantityPerPurchase >= 10,
                Tooltip = FormatNumber((long)unitPrice * 10) + " dukes",
                Icon = IconGive
            });
            buttons.Add(new ButtonInfo
            {
                Action = "buy|" + entry.Id + "|100",
                Label = "Buy 100",
                Enabled = dukes >= (long)unitPrice * 100 && config.MaxQuantityPerPurchase >= 100,
                Tooltip = FormatNumber((long)unitPrice * 100) + " dukes",
                Icon = IconGive
            });
            buttons.Add(new ButtonInfo
            {
                Action = "qty|" + entry.Id,
                Label = "Custom Qty",
                Enabled = affordable > 0,
                Tooltip = "Type how many to buy.",
                Icon = IconGive
            });
            buttons.Add(new ButtonInfo
            {
                Action = "back",
                Label = "Back",
                Tooltip = "Return to the item list.",
                Icon = IconBack
            });
            buttons.Add(new ButtonInfo
            {
                Action = "home",
                Label = "Categories",
                Tooltip = "Return to the category list.",
                Icon = IconShop
            });
        }

        private void RenderPermissionList(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            List<PermissionProduct> list = GetEnabledPermissionProducts();
            int pages = Math.Max(1, (list.Count + UiRows - 1) / UiRows);
            if (session.Page < 0)
            {
                session.Page = 0;
            }
            else if (session.Page >= pages)
            {
                session.Page = pages - 1;
            }

            subtitle = "Permissions  |  " + list.Count + " product" + (list.Count == 1 ? string.Empty : "s") +
                       "  |  page " + (session.Page + 1) + "/" + pages +
                       "  |  Dukes: " + GetDukesLabel(playerId);

            int start = session.Page * UiRows;
            for (int i = 0; i < UiRows && start + i < list.Count; i++)
            {
                PermissionProduct product = list[start + i];
                List<string> nodes = GetProductPermissions(product);
                int owned = CountOwnedPermissions(playerId, nodes);
                bool allOwned = nodes.Count > 0 && owned >= nodes.Count;
                string reason;
                bool canGrant = CanGrantProduct(product, out reason);

                string sub = FormatNumber(product.Price) + " dukes";
                if (allOwned)
                {
                    sub += "  |  owned";
                }
                else if (!canGrant)
                {
                    sub += "  |  unavailable";
                }
                else if (owned > 0)
                {
                    sub += "  |  " + owned + "/" + nodes.Count + " owned";
                }

                string summary = GetProductPermissionSummary(product);
                if (!string.IsNullOrEmpty(summary))
                {
                    sub += "  |  " + summary;
                }

                string badge;
                string icon;
                if (allOwned)
                {
                    badge = "OWNED";
                    icon = IconOwned;
                }
                else if (!canGrant)
                {
                    badge = "LOCKED";
                    icon = IconWarning;
                }
                else if (nodes.Count > 1)
                {
                    badge = "BUNDLE";
                    icon = IconPermissions;
                }
                else
                {
                    badge = "PERM";
                    icon = IconPermissions;
                }

                rows.Add(new RowInfo
                {
                    Action = "permission|" + product.Id,
                    Label = product.Name,
                    Sub = Clean(sub, 200),
                    Enabled = true,
                    Icon = icon,
                    Badge = badge
                });
            }

            if (list.Count == 0)
            {
                rows.Add(new RowInfo
                {
                    Label = "No permissions are configured.",
                    Sub = "Add PermissionProducts to oxide/config/AdminShop.json.",
                    Enabled = false,
                    Icon = IconWarning,
                    Badge = "NONE"
                });
            }

            buttons.Add(new ButtonInfo
            {
                Action = "home",
                Label = "Categories",
                Tooltip = "Return to the category list.",
                Icon = IconShop
            });

            if (session.Page > 0)
            {
                buttons.Add(new ButtonInfo
                {
                    Action = "page|prev",
                    Label = "< Prev",
                    Tooltip = "Previous page.",
                    Icon = IconBack
                });
            }

            if (session.Page < pages - 1)
            {
                buttons.Add(new ButtonInfo
                {
                    Action = "page|next",
                    Label = "Next >",
                    Tooltip = "Next page.",
                    Icon = IconBack
                });
            }

            buttons.Add(new ButtonInfo
            {
                Action = "refresh",
                Label = "Refresh",
                Tooltip = "Redraw this list.",
                Icon = IconRefresh
            });
        }

        private void RenderPermissionDetail(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            PermissionProduct product;
            if (string.IsNullOrEmpty(session.SelectedPermissionId) ||
                !permissionProductsById.TryGetValue(session.SelectedPermissionId, out product))
            {
                session.Screen = "permissions";
                session.SelectedPermissionId = null;
                RenderPermissionList(playerId, session, rows, buttons, out subtitle);
                return;
            }

            List<string> nodes = GetProductPermissions(product);
            int dukes = GetDukes(playerId);
            bool online = dukes >= 0;
            int owned = CountOwnedPermissions(playerId, nodes);
            bool allOwned = nodes.Count > 0 && owned >= nodes.Count;
            string reason;
            bool canGrant = CanGrantProduct(product, out reason);
            bool affordable = online && dukes >= product.Price;
            bool buyEnabled = product.Enabled && !allOwned && canGrant && affordable;

            string status;
            string statusIcon;
            string statusBadge;
            if (!product.Enabled)
            {
                status = "disabled in config";
                statusIcon = IconWarning;
                statusBadge = "LOCKED";
            }
            else if (allOwned)
            {
                status = "owned";
                statusIcon = IconOwned;
                statusBadge = "OWNED";
            }
            else if (!canGrant)
            {
                status = "unavailable: " + reason;
                statusIcon = IconWarning;
                statusBadge = "LOCKED";
            }
            else if (owned > 0)
            {
                status = "partially owned (" + owned + "/" + nodes.Count + ")";
                statusIcon = IconPermissions;
                statusBadge = "BUNDLE";
            }
            else
            {
                status = "not owned";
                statusIcon = IconPermissions;
                statusBadge = nodes.Count > 1 ? "BUNDLE" : "PERM";
            }

            subtitle = product.Name + "  |  " + FormatNumber(product.Price) + " dukes  |  Dukes: " +
                       (online ? FormatNumber(dukes) : "offline");

            rows.Add(new RowInfo
            {
                Label = "Product",
                Sub = product.Name,
                Enabled = false,
                Icon = IconPermissions
            });
            rows.Add(new RowInfo
            {
                Label = "Price",
                Sub = FormatNumber(product.Price) + " dukes",
                Enabled = false,
                Icon = IconPrice
            });
            rows.Add(new RowInfo
            {
                Label = "Your Dukes",
                Sub = online ? FormatNumber(dukes) : "offline",
                Enabled = false,
                Icon = IconPrice
            });
            rows.Add(new RowInfo
            {
                Label = "Status",
                Sub = status,
                Enabled = false,
                Icon = statusIcon,
                Badge = statusBadge
            });
            rows.Add(new RowInfo
            {
                Label = "Permissions",
                Sub = GetProductPermissionSummary(product),
                Enabled = false,
                Icon = IconPermissions
            });
            rows.Add(new RowInfo
            {
                Label = "Type",
                Sub = nodes.Count == 0
                    ? "no permission nodes configured"
                    : (nodes.Count == 1
                        ? "permanent user permission"
                        : "permanent permission bundle (" + nodes.Count + " nodes)"),
                Enabled = false,
                Icon = IconQuality
            });
            rows.Add(new RowInfo
            {
                Label = "Description",
                Sub = string.IsNullOrEmpty(product.Description) ? "(none)" : product.Description,
                Enabled = false,
                Icon = IconBooks
            });
            rows.Add(new RowInfo
            {
                Label = "Product Id",
                Sub = product.Id,
                Enabled = false,
                Icon = IconBooks
            });

            string buyTooltip;
            if (!product.Enabled)
            {
                buyTooltip = "This product is disabled.";
            }
            else if (allOwned)
            {
                buyTooltip = "You already have this permission.";
            }
            else if (!canGrant)
            {
                buyTooltip = reason;
            }
            else if (!online)
            {
                buyTooltip = "You must be online.";
            }
            else if (dukes < product.Price)
            {
                buyTooltip = "Need " + FormatNumber(product.Price) + " dukes.";
            }
            else
            {
                buyTooltip = "Buy for " + FormatNumber(product.Price) + " dukes.";
            }

            buttons.Add(new ButtonInfo
            {
                Action = "buypermission|" + product.Id,
                Label = "Buy Permission",
                Enabled = buyEnabled,
                Tooltip = buyTooltip,
                Icon = IconGive
            });
            buttons.Add(new ButtonInfo
            {
                Action = "back",
                Label = "Back",
                Tooltip = "Return to the permission list.",
                Icon = IconBack
            });
            buttons.Add(new ButtonInfo
            {
                Action = "home",
                Label = "Categories",
                Tooltip = "Return to the category list.",
                Icon = IconShop
            });
            buttons.Add(new ButtonInfo
            {
                Action = "refresh",
                Label = "Refresh",
                Tooltip = "Redraw this page.",
                Icon = IconRefresh
            });
        }

        private string GetDukesLabel(string playerId)
        {
            int dukes = GetDukes(playerId);
            return dukes >= 0 ? FormatNumber(dukes) : "offline";
        }

        private int GetDukes(string playerId)
        {
            EntityPlayer player = FindEntity(playerId);
            return player != null ? CountCurrency(player) : -1;
        }

        private static string FormatNumber(long value)
        {
            return value.ToString("N0", CultureInfo.InvariantCulture);
        }

        private static string GetCategoryTitle(string key)
        {
            if (string.Equals(key, "all", StringComparison.OrdinalIgnoreCase)) return "All Items";
            if (string.Equals(key, "weapons", StringComparison.OrdinalIgnoreCase)) return "Weapons";
            if (string.Equals(key, "ammo", StringComparison.OrdinalIgnoreCase)) return "Ammo";
            if (string.Equals(key, "armor", StringComparison.OrdinalIgnoreCase)) return "Armor";
            if (string.Equals(key, "mods", StringComparison.OrdinalIgnoreCase)) return "Mods";
            if (string.Equals(key, "food", StringComparison.OrdinalIgnoreCase)) return "Food & Medical";
            if (string.Equals(key, "resources", StringComparison.OrdinalIgnoreCase)) return "Resources";
            if (string.Equals(key, "blocks", StringComparison.OrdinalIgnoreCase)) return "Blocks";
            if (string.Equals(key, "books", StringComparison.OrdinalIgnoreCase)) return "Books & Schematics";
            return "Other";
        }

        private static string GetCategoryIcon(string key)
        {
            if (string.Equals(key, "all", StringComparison.OrdinalIgnoreCase)) return IconItems;
            if (string.Equals(key, "weapons", StringComparison.OrdinalIgnoreCase)) return IconKits;
            if (string.Equals(key, "ammo", StringComparison.OrdinalIgnoreCase)) return IconAirdrop;
            if (string.Equals(key, "armor", StringComparison.OrdinalIgnoreCase)) return IconArmor;
            if (string.Equals(key, "mods", StringComparison.OrdinalIgnoreCase)) return IconMods;
            if (string.Equals(key, "food", StringComparison.OrdinalIgnoreCase)) return IconFood;
            if (string.Equals(key, "resources", StringComparison.OrdinalIgnoreCase)) return IconResources;
            if (string.Equals(key, "blocks", StringComparison.OrdinalIgnoreCase)) return IconBlocks;
            if (string.Equals(key, "books", StringComparison.OrdinalIgnoreCase)) return IconBooks;
            return IconOther;
        }

        private static string GetItemIcon(ShopEntry entry)
        {
            if (entry == null || entry.ItemClass == null)
            {
                return IconItems;
            }

            string itemName = GetInternalName(entry.ItemClass);
            return string.IsNullOrEmpty(itemName) ? IconItems : "item:" + itemName;
        }

        #endregion

        #region Commands

        private void CommandShop(IPlayer player, string command, string[] args)
        {
            if (config == null)
            {
                LoadConfig();
            }

            if (player != null && !CanUse(player.Id))
            {
                Reply(player, "You need the 'adminshop.use' permission.");
                return;
            }

            if (args != null && args.Length > 0)
            {
                string sub = args[0].ToLowerInvariant();
                switch (sub)
                {
                    case "reload":
                        if (!RequireAdmin(player))
                        {
                            return;
                        }

                        LoadConfig();
                        EnsureDefaultPermissions();
                        BuildCatalog(true);
                        TryRegisterHook();
                        Reply(player, "Reloaded config and rebuilt the catalog (" +
                                      (catalog != null ? catalog.All.Count : 0) + " items, " +
                                      permissionProducts.Count + " permission products).");
                        return;

                    case "price":
                        if (!RequireAdmin(player))
                        {
                            return;
                        }

                        CommandPrice(player, args);
                        return;

                    case "catalog":
                        if (!RequireAdmin(player))
                        {
                            return;
                        }

                        Reply(player, "Catalog: " + (catalog != null ? catalog.All.Count : 0) +
                                      " items, " + permissionProducts.Count + " permission products. Currency: " +
                                      config.CurrencyItem + ".");
                        return;

                    case "perms":
                    case "permissions":
                        if (!RequireAdmin(player))
                        {
                            return;
                        }

                        CommandPermissions(player);
                        return;

                    case "help":
                        Reply(player, "Usage: /shop [open] | /adminshop reload | price <item> | catalog | perms | help");
                        return;

                    case "open":
                        break;

                    default:
                        Reply(player, "Unknown subcommand. Try /adminshop help.");
                        return;
                }
            }

            if (player == null || string.IsNullOrEmpty(player.Id) ||
                player.Id.Equals("server_console", StringComparison.OrdinalIgnoreCase))
            {
                Reply(player, "Only players can open the Admin Shop GUI.");
                return;
            }

            if (catalog == null)
            {
                BuildCatalog(false);
            }

            if (!OpenShopForPlayer(player.Id, null))
            {
                Reply(player, "The Server Assist client mod is not installed or has not sent its hello yet.");
            }
        }

        private void CommandPrice(IPlayer player, string[] args)
        {
            if (catalog == null)
            {
                BuildCatalog(false);
            }

            if (catalog == null || args.Length < 2)
            {
                Reply(player, "Usage: /adminshop price <item name>");
                return;
            }

            string query = string.Join(" ", args.Skip(1).ToArray());
            ShopEntry entry = FindEntry(query);
            if (entry == null)
            {
                Reply(player, "No shop item matched '" + query + "'.");
                return;
            }

            Reply(player, entry.Name + " (type " + entry.TypeId + "): unit " + FormatNumber(GetUnitPrice(entry)) +
                          " dukes, trader " + FormatNumber(GetTraderUnitValue(entry)) +
                          ", bundle " + entry.BundleSize + ", max stack " + entry.MaxStack + ".");
        }

        private void CommandPermissions(IPlayer player)
        {
            if (permissionProducts.Count == 0)
            {
                Reply(player, "No permission products are configured. Add PermissionProducts to oxide/config/AdminShop.json.");
                return;
            }

            Reply(player, "Permission products:");
            for (int i = 0; i < permissionProducts.Count; i++)
            {
                PermissionProduct product = permissionProducts[i];
                List<string> nodes = GetProductPermissions(product);
                string reason;
                bool canGrant = CanGrantProduct(product, out reason);
                string status = !product.Enabled
                    ? "disabled"
                    : (canGrant ? "ready" : "unavailable: " + reason);

                Reply(player, "  " + product.Id + " | " + product.Name + " | " +
                              FormatNumber(product.Price) + " dukes | " + status + " | " +
                              (nodes.Count == 0 ? "(no nodes)" : string.Join(", ", nodes.ToArray())));
            }
        }

        private ShopEntry FindEntry(string query)
        {
            if (catalog == null || string.IsNullOrEmpty(query))
            {
                return null;
            }

            ShopEntry exact = catalog.All.FirstOrDefault(
                e => string.Equals(GetInternalName(e.ItemClass), query, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                return exact;
            }

            exact = catalog.All.FirstOrDefault(
                e => string.Equals(e.Name, query, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                return exact;
            }

            return catalog.All.FirstOrDefault(
                e => e.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     GetInternalName(e.ItemClass).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string GetInternalName(ItemClass itemClass)
        {
            if (itemClass == null)
            {
                return string.Empty;
            }

            try
            {
                string name = itemClass.GetItemName();
                return string.IsNullOrEmpty(name) ? string.Empty : name;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsServerConsole(string playerId)
        {
            return !string.IsNullOrEmpty(playerId) &&
                   playerId.Equals("server_console", StringComparison.OrdinalIgnoreCase);
        }

        private bool CanUse(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return false;
            }

            return HasAdminPermission(playerId) ||
                   permission.UserHasPermission(playerId, PermUse);
        }

        private bool HasAdminPermission(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return false;
            }

            return IsServerConsole(playerId) ||
                   permission.UserHasPermission(playerId, PermAdmin);
        }

        private bool RequireAdmin(IPlayer player)
        {
            if (player != null && HasAdminPermission(player.Id))
            {
                return true;
            }

            Reply(player, "You need the 'adminshop.admin' permission.");
            return false;
        }

        private void Reply(IPlayer player, string message)
        {
            if (player == null)
            {
                Interface.Oxide.LogInfo("[AdminShop] " + message);
                return;
            }

            player.Reply(string.IsNullOrEmpty(config.ChatPrefix) ? message : config.ChatPrefix + " " + message);
        }

        #endregion

        #region Protocol and helpers

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

        #endregion

        #region Data classes

        private sealed class Session
        {
            public string Screen = "home";
            public string Category = "all";
            public string Query = string.Empty;
            public string SelectedId;
            public string SelectedPermissionId;
            public int Page;
            public string InputMode;
            public string InputLabel = string.Empty;
            public string InputValue = string.Empty;
            public string Status = string.Empty;
        }

        private sealed class ShopEntry
        {
            public string Id = string.Empty;
            public int TypeId;
            public ItemClass ItemClass;
            public string Name = string.Empty;
            public string SearchName = string.Empty;
            public string Category = "other";
            public float EconomicValue;
            public int BundleSize = 1;
            public bool HasQuality;
            public int MaxStack = 1;
        }

        private sealed class ShopCatalog
        {
            public readonly List<ShopEntry> All = new List<ShopEntry>();
            public readonly Dictionary<string, ShopEntry> ById =
                new Dictionary<string, ShopEntry>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, List<ShopEntry>> Categories =
                new Dictionary<string, List<ShopEntry>>(StringComparer.OrdinalIgnoreCase);

            public void BuildCategories(string[] keys)
            {
                Categories.Clear();
                for (int i = 0; i < keys.Length; i++)
                {
                    Categories[keys[i]] = new List<ShopEntry>();
                }

                for (int i = 0; i < All.Count; i++)
                {
                    ShopEntry entry = All[i];
                    List<ShopEntry> list;
                    if (!Categories.TryGetValue(entry.Category, out list))
                    {
                        list = new List<ShopEntry>();
                        Categories[entry.Category] = list;
                    }

                    list.Add(entry);

                    if (string.Equals(entry.Category, "all", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    List<ShopEntry> all;
                    if (Categories.TryGetValue("all", out all))
                    {
                        all.Add(entry);
                    }
                }
            }

            public List<ShopEntry> GetCategory(string key)
            {
                List<ShopEntry> list;
                return Categories.TryGetValue(key ?? string.Empty, out list) ? list : new List<ShopEntry>();
            }

            public int CountFor(string key)
            {
                if (string.Equals(key, "all", StringComparison.OrdinalIgnoreCase))
                {
                    return All.Count;
                }

                List<ShopEntry> list;
                return Categories.TryGetValue(key ?? string.Empty, out list) ? list.Count : 0;
            }

            public List<ShopEntry> Search(string query)
            {
                if (string.IsNullOrEmpty(query))
                {
                    return All;
                }

                string needle = query.Trim().ToLowerInvariant();
                List<ShopEntry> results = new List<ShopEntry>();
                for (int i = 0; i < All.Count; i++)
                {
                    ShopEntry entry = All[i];
                    if (entry.SearchName.IndexOf(needle, StringComparison.Ordinal) >= 0)
                    {
                        results.Add(entry);
                    }
                }

                return results;
            }
        }

        private sealed class Configuration
        {
            public string ChatPrefix = "[AdminShop]";
            public float PriceMultiplier = 10f;
            public bool UseTraderBuyMarkup = true;
            public bool UseTraderQualityMod = true;
            public int ItemQuality = 6;
            public string CurrencyItem = "casinoCoin";
            public int MaxQuantityPerPurchase = 1000;
            public bool IncludeBlocks = true;
            public bool IncludeCreativeItems = false;
            public bool IncludeHiddenItems = false;
            public float MinEconomicValue = 0f;
            public bool GrantUseToDefaultGroup = true;
            public bool LogPurchases = true;
            public string HookLabel = "Admin Shop";
            public string HookDescription = "Buy items and permissions with dukes.";
            public string HookPermission = "adminshop.use";
            public List<PermissionProduct> PermissionProducts = new List<PermissionProduct>();
        }

        private sealed class PermissionProduct
        {
            public string Id = string.Empty;
            public string Name = string.Empty;
            public string Description = string.Empty;
            public string Permission = string.Empty;
            public List<string> Permissions = new List<string>();
            public int Price = 0;
            public bool Enabled = true;
        }

        #endregion
    }
}

