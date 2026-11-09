using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("AuctionHouse", "TheRealM18", "1.1.1")]
    [Description("A server-side auction house with a ServerAssist GUI and a configurable listing fee.")]
    public class AuctionHouse : CovalencePlugin
    {
        #region Constants

        private const string PermUse = "auctionhouse.use";
        private const string PermSell = "auctionhouse.sell";
        private const string PermAdmin = "auctionhouse.admin";

        private const string DataFileName = "AuctionHouse/listings";
        private const string ProviderId = "auctionhouse";

        private const int UiRows = 10;
        private const int UiButtons = 12;

        private const string IconAuction = "sprite:sa_auction_house";
        private const string IconShop = "sprite:sa_admin_shop";
        private const string IconItems = "sprite:sa_nav_items";
        private const string IconListings = "sprite:sa_nav_logs";
        private const string IconGive = "sprite:sa_act_give";
        private const string IconCancel = "sprite:sa_act_ban";
        private const string IconTime = "sprite:sa_act_time";
        private const string IconWarn = "sprite:sa_act_warn";
        private const string IconBackpack = "sprite:sa_dynamic_backpack";
        private const string IconSeller = "sprite:sa_nav_players";
        private const string IconRefresh = "sprite:sa_plugin_hot_reload";
        private const string IconServer = "sprite:sa_nav_server";

        private const string StateActive = "Active";
        private const string StateSold = "Sold";
        private const string StateExpired = "Expired";
        private const string StateCancelled = "Cancelled";
        private const string StateBroken = "Broken";

        #endregion

        #region Fields

        private Configuration config;
        private StoredData data;
        private Timer expireTimer;

        private bool currencyValid;
        private bool serverAssistGuiReady;
        private bool serverAssistWarningLogged;
        private bool serverAssistPanelFallback;

        private ItemValue currencyValue;
        private readonly HashSet<string> blockedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GuiSession> sessions =
            new Dictionary<string, GuiSession>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Lifecycle

        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
            permission.RegisterPermission(PermSell, this);
            permission.RegisterPermission(PermAdmin, this);

            LoadConfig();
            LoadData();
            ResolveCurrency();
            StartExpireTimer();

            AddCovalenceCommand(new[] { "auction", "auc", "market" }, nameof(CommandAuction));

            timer.Once(1f, EnsureDefaultPermissions);
            timer.Once(2f, TryRegisterServerAssist);
            timer.Once(10f, TryRegisterServerAssist);
        }

        private void OnServerInitialized()
        {
            EnsureDefaultPermissions();
            ResolveCurrency();
            ProcessExpiredListings();
            StartExpireTimer();
            TryRegisterServerAssist();

            if (!currencyValid)
            {
                Interface.Oxide.LogError("[AuctionHouse] Currency item '" + config.CurrencyItem +
                                        "' could not be found. Selling and buying are disabled until the config is fixed.");
            }

            Interface.Oxide.LogInfo("[AuctionHouse] Loaded " + CountActiveListings() + " active listing(s).");
        }

        private void OnServerSave()
        {
            SaveData();
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin != null && string.Equals(plugin.Name, "ServerAssist", StringComparison.OrdinalIgnoreCase))
            {
                timer.Once(0.5f, TryRegisterServerAssist);
            }
        }

        private void OnPluginUnloaded(Plugin plugin)
        {
            if (plugin != null && string.Equals(plugin.Name, "ServerAssist", StringComparison.OrdinalIgnoreCase))
            {
                serverAssistGuiReady = false;
            }
        }

        private void OnUserConnected(IPlayer player)
        {
            if (player == null || string.IsNullOrEmpty(player.Id))
            {
                return;
            }

            DeliverPending(player);
        }

        private void OnUserDisconnected(IPlayer player)
        {
            if (player != null && !string.IsNullOrEmpty(player.Id))
            {
                sessions.Remove(player.Id);
            }
        }

        private void Unload()
        {
            UnregisterServerAssist();

            if (expireTimer != null)
            {
                timer.Destroy(ref expireTimer);
            }

            SaveData();
            sessions.Clear();
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
                Interface.Oxide.LogWarning("[AuctionHouse] config could not be read: " + exception.Message);
                config = null;
            }

            if (config == null)
            {
                config = new Configuration();
            }

            NormalizeConfig();
            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        private void NormalizeConfig()
        {
            config.ListingFee = Math.Max(0, Math.Min(1000000000, config.ListingFee));
            config.ListingFeePercent = Math.Max(0.0, Math.Min(1000.0, config.ListingFeePercent));
            config.MinimumPrice = Math.Max(1, config.MinimumPrice);
            config.MaximumPrice = Math.Max(config.MinimumPrice, config.MaximumPrice);
            config.ListingDurationHours = Math.Max(1, Math.Min(8760, config.ListingDurationHours));
            config.MaxListingsPerPlayer = Math.Max(0, Math.Min(1000, config.MaxListingsPerPlayer));
            config.MaxActiveListings = Math.Max(0, Math.Min(100000, config.MaxActiveListings));
            config.ExpireCheckSeconds = Math.Max(10, Math.Min(3600, config.ExpireCheckSeconds));
            config.SalesTaxPercent = Math.Max(0.0, Math.Min(100.0, config.SalesTaxPercent));
            config.KeepHistoryEntries = Math.Max(0, Math.Min(10000, config.KeepHistoryEntries));

            string mode = string.IsNullOrEmpty(config.ListingFeeMode) ? "Flat" : config.ListingFeeMode.Trim();
            if (!string.Equals(mode, "Flat", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(mode, "Percent", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(mode, "FlatAndPercent", StringComparison.OrdinalIgnoreCase))
            {
                mode = "Flat";
            }

            config.ListingFeeMode = mode;

            Dictionary<string, int> fees = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (config.ListingFeesByItem != null)
            {
                foreach (KeyValuePair<string, int> pair in config.ListingFeesByItem)
                {
                    if (!string.IsNullOrEmpty(pair.Key))
                    {
                        fees[pair.Key] = Math.Max(0, Math.Min(1000000000, pair.Value));
                    }
                }
            }

            config.ListingFeesByItem = fees;

            blockedItems.Clear();
            if (config.BlockedItems != null)
            {
                foreach (string item in config.BlockedItems)
                {
                    if (!string.IsNullOrEmpty(item))
                    {
                        blockedItems.Add(item.Trim());
                    }
                }
            }
        }

        private void LoadData()
        {
            try
            {
                data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(DataFileName);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[AuctionHouse] data could not be read: " + exception.Message);
                data = null;
            }

            NormalizeData();
        }

        private void SaveData()
        {
            if (data == null)
            {
                return;
            }

            try
            {
                PruneHistory();
                Interface.Oxide.DataFileSystem.WriteObject(DataFileName, data, true);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError("[AuctionHouse] data could not be saved: " + exception);
            }
        }

        private void NormalizeData()
        {
            if (data == null)
            {
                data = new StoredData();
            }

            if (data.Listings == null)
            {
                data.Listings = new List<Listing>();
            }

            if (data.Pending == null)
            {
                data.Pending = new Dictionary<string, PendingDelivery>(StringComparer.OrdinalIgnoreCase);
            }

            Dictionary<string, PendingDelivery> pending =
                new Dictionary<string, PendingDelivery>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, PendingDelivery> pair in data.Pending)
            {
                if (string.IsNullOrEmpty(pair.Key))
                {
                    continue;
                }

                PendingDelivery delivery = pair.Value ?? new PendingDelivery();
                if (delivery.Items == null)
                {
                    delivery.Items = new List<PendingItem>();
                }

                if (delivery.Messages == null)
                {
                    delivery.Messages = new List<string>();
                }

                pending[pair.Key] = delivery;
            }

            data.Pending = pending;

            long highestId = 0;
            data.Listings.RemoveAll(listing => listing == null);

            foreach (Listing listing in data.Listings)
            {
                if (listing.Id > highestId)
                {
                    highestId = listing.Id;
                }

                if (string.IsNullOrEmpty(listing.State))
                {
                    listing.State = StateActive;
                }

                if (listing.Count <= 0)
                {
                    listing.Count = 1;
                }

                if (listing.Price <= 0 && config != null)
                {
                    listing.Price = config.MinimumPrice;
                }

                if (string.IsNullOrEmpty(listing.SellerId))
                {
                    listing.State = StateBroken;
                }

                if (string.Equals(listing.State, StateActive, StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrEmpty(listing.ItemData))
                {
                    listing.State = StateBroken;
                }

                if (listing.CreatedUtc <= 0)
                {
                    listing.CreatedUtc = DateTime.UtcNow.Ticks;
                }

                if (listing.ExpiresUtc <= 0)
                {
                    listing.ExpiresUtc = listing.CreatedUtc +
                                       TimeSpan.FromHours(config == null ? 24 : config.ListingDurationHours).Ticks;
                }
            }

            if (data.NextId <= highestId)
            {
                data.NextId = highestId + 1;
            }

            if (data.NextId <= 0)
            {
                data.NextId = 1;
            }
        }

        private void ResolveCurrency()
        {
            currencyValue = null;
            currencyValid = false;

            if (config == null || string.IsNullOrEmpty(config.CurrencyItem) || ItemClass.list == null)
            {
                return;
            }

            ItemValue value = ItemClass.GetItem(config.CurrencyItem, true);
            if (value == null || value.type == 0 || value.ItemClass == null)
            {
                return;
            }

            currencyValue = value;
            currencyValid = true;
        }

        private void EnsureDefaultPermissions()
        {
            string adminGroup = Interface.Oxide.Config.Options.DefaultGroups.Administrators;
            if (!string.IsNullOrEmpty(adminGroup))
            {
                GrantGroupIfMissing(adminGroup, PermAdmin);
                GrantGroupIfMissing(adminGroup, PermUse);
                GrantGroupIfMissing(adminGroup, PermSell);
            }

            string playerGroup = Interface.Oxide.Config.Options.DefaultGroups.Players;
            if (!string.IsNullOrEmpty(playerGroup))
            {
                if (config.GrantUseToDefaultGroup)
                {
                    GrantGroupIfMissing(playerGroup, PermUse);
                }

                if (config.GrantSellToDefaultGroup)
                {
                    GrantGroupIfMissing(playerGroup, PermSell);
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

        private void StartExpireTimer()
        {
            if (expireTimer != null)
            {
                timer.Destroy(ref expireTimer);
            }

            expireTimer = timer.Every(config.ExpireCheckSeconds, ProcessExpiredListings);
        }

        #endregion

        #region ServerAssist integration

        private void OnServerAssistReady()
        {
            timer.Once(0.5f, TryRegisterServerAssist);
        }

        private Plugin FindServerAssist()
        {
            return plugins.Find("ServerAssist");
        }

        private void TryRegisterServerAssist()
        {
            Plugin serverAssist = FindServerAssist();
            if (serverAssist == null || !serverAssist.IsLoaded)
            {
                serverAssistGuiReady = false;
                return;
            }

            try
            {
                // Newer ServerAssist builds list providers on the home board.
                // Builds without that capability register as a normal home panel
                // instead, so AuctionHouse is still reachable from Home.
                object support = serverAssist.Call("GetProviderHomeSupport");
                bool providerHomeSupported = support is bool && (bool)support;

                if (providerHomeSupported)
                {
                    object result = serverAssist.Call(
                        "RegisterGuiProvider",
                        ProviderId,
                        Name,
                        nameof(HandleAuctionGuiRequest),
                        "Auction House",
                        "Browse",
                        "My Listings",
                        "Sell");

                    serverAssistGuiReady = result is bool && (bool)result;

                    if (serverAssistGuiReady)
                    {
                        serverAssist.Call("SetProviderIcon", ProviderId, IconAuction);
                        serverAssist.Call("UnregisterPanel", ProviderId);
                        serverAssistPanelFallback = false;
                        Interface.Oxide.LogInfo("[AuctionHouse] Registered the ServerAssist GUI provider.");
                    }
                    else
                    {
                        // Provider mode is unavailable for some reason; a normal
                        // home panel still makes the Auction House reachable.
                        object panelResult = serverAssist.Call(
                            "RegisterPanel",
                            ProviderId,
                            "Auction House",
                            "Buy and sell items in the Auction House.",
                            PermUse,
                            Name,
                            nameof(HandleAuctionGuiRequest),
                            IconAuction,
                            20);

                        serverAssistGuiReady = panelResult is bool && (bool)panelResult;

                        if (serverAssistGuiReady)
                        {
                            serverAssist.Call("UnregisterGuiProvider", ProviderId);
                            serverAssistPanelFallback = true;
                            Interface.Oxide.LogInfo("[AuctionHouse] Registered the ServerAssist home panel fallback.");
                        }
                    }
                }
                else
                {
                    object result = serverAssist.Call(
                        "RegisterPanel",
                        ProviderId,
                        "Auction House",
                        "Buy and sell items in the Auction House.",
                        PermUse,
                        Name,
                        nameof(HandleAuctionGuiRequest),
                        IconAuction,
                        20);

                    serverAssistGuiReady = result is bool && (bool)result;

                    if (serverAssistGuiReady)
                    {
                        serverAssist.Call("UnregisterGuiProvider", ProviderId);
                        serverAssistPanelFallback = true;
                        Interface.Oxide.LogInfo("[AuctionHouse] Registered the ServerAssist home panel.");
                    }
                }

                if (!serverAssistGuiReady && !serverAssistWarningLogged)
                {
                    serverAssistWarningLogged = true;
                    Interface.Oxide.LogWarning(
                        "[AuctionHouse] ServerAssist did not accept the Auction House panel. " +
                        "The chat commands still work. ServerAssist 2.0.0 or newer is required.");
                }
            }
            catch (MissingMethodException)
            {
                serverAssistGuiReady = false;

                if (!serverAssistWarningLogged)
                {
                    serverAssistWarningLogged = true;
                    Interface.Oxide.LogWarning(
                        "[AuctionHouse] ServerAssist does not expose the provider/panel API yet. " +
                        "The chat commands still work. ServerAssist 2.0.0 or newer is required.");
                }
            }
            catch (Exception exception)
            {
                serverAssistGuiReady = false;
                Interface.Oxide.LogWarning("[AuctionHouse] Could not register with ServerAssist: " + exception.Message);
            }
        }

        private void UnregisterServerAssist()
        {
            Plugin serverAssist = FindServerAssist();
            if (serverAssist == null || !serverAssist.IsLoaded)
            {
                serverAssistGuiReady = false;
                return;
            }

            if (serverAssistGuiReady)
            {
                try
                {
                    serverAssist.Call("UnregisterGuiProvider", ProviderId);
                }
                catch
                {
                    // ServerAssist may already be unloading.
                }

                try
                {
                    serverAssist.Call("UnregisterPanel", ProviderId);
                }
                catch
                {
                    // ServerAssist may already be unloading.
                }
            }

            serverAssistGuiReady = false;
            serverAssistPanelFallback = false;
        }

        private bool TryOpenGui(IPlayer player, string payload)
        {
            if (player == null || string.IsNullOrEmpty(player.Id) || player.IsServer)
            {
                return false;
            }

            if (!CanUse(player.Id))
            {
                return false;
            }

            if (!serverAssistGuiReady)
            {
                TryRegisterServerAssist();
            }

            Plugin serverAssist = FindServerAssist();
            if (!serverAssistGuiReady || serverAssist == null || !serverAssist.IsLoaded)
            {
                return false;
            }

            try
            {
                string openPayload = string.IsNullOrEmpty(payload) ? "a|open" : payload;
                object result = serverAssistPanelFallback
                    ? serverAssist.Call("OpenPanel", player.Id, ProviderId, openPayload)
                    : serverAssist.Call("OpenGuiProvider", player.Id, ProviderId, openPayload);

                return result is bool && (bool)result;
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[AuctionHouse] Could not open the ServerAssist GUI: " + exception.Message);
                return false;
            }
        }

        /// <summary>
        /// Entry point called by ServerAssist for this provider. The payload uses the
        /// same line protocol as ServerAssist itself: "a|..." for actions and
        /// "t|..." for submitted text.
        /// </summary>
        public string HandleAuctionGuiRequest(string playerId, string payload)
        {
            try
            {
                if (!CanUse(playerId))
                {
                    return ErrorView("You do not have permission to use the Auction House.");
                }

                GuiSession session = GetSession(playerId);
                if (string.IsNullOrEmpty(payload) || string.Equals(payload, "open", StringComparison.OrdinalIgnoreCase))
                {
                    payload = "a|open";
                }

                if (payload.StartsWith("t|", StringComparison.Ordinal))
                {
                    return HandleInput(playerId, session, Decode(payload.Substring(2)));
                }

                if (payload.StartsWith("a|", StringComparison.Ordinal))
                {
                    return HandleAction(playerId, session, payload.Substring(2));
                }

                return ErrorView("Unknown Auction House GUI request.");
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogException("[AuctionHouse] GUI request failed", exception);
                return ErrorView("Internal error: " + exception.Message);
            }
        }

        private GuiSession GetSession(string playerId)
        {
            GuiSession session;
            if (!sessions.TryGetValue(playerId, out session))
            {
                session = new GuiSession();
                sessions[playerId] = session;
            }

            return session;
        }

        private void ResetSession(GuiSession session)
        {
            session.Tab = "home";
            session.Page = 0;
            session.SelectedId = 0;
            session.DraftPrice = 0;
            session.DraftQuantity = 0;
            session.InputAction = null;
            session.InputLabel = string.Empty;
            session.InputValue = string.Empty;
            session.Status = string.Empty;
        }

        private string HandleAction(string playerId, GuiSession session, string action)
        {
            string[] parts = action.Split('|');
            string command = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
            session.Status = string.Empty;

            switch (command)
            {
                case "open":
                    ResetSession(session);
                    break;

                case "refresh":
                    session.Status = "Refreshed.";
                    break;

                case "tab":
                    if (parts.Length > 1)
                    {
                        SwitchTab(session, parts[1]);
                    }
                    break;

                case "page":
                    if (parts.Length > 1)
                    {
                        session.Page += string.Equals(parts[1], "next", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
                    }
                    break;

                case "view":
                    if (parts.Length > 1)
                    {
                        long id;
                        if (long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                        {
                            session.SelectedId = id;
                            session.Page = 0;
                        }
                    }
                    break;

                case "manage":
                    if (parts.Length > 1)
                    {
                        long id;
                        if (long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                        {
                            session.SelectedId = id;
                            session.Page = 0;
                        }
                    }
                    break;

                case "back":
                    session.SelectedId = 0;
                    session.Status = string.Empty;
                    break;

                case "buy":
                    if (parts.Length > 1)
                    {
                        BuyFromGui(playerId, session, parts[1]);
                    }
                    break;

                case "cancel":
                    if (parts.Length > 1)
                    {
                        CancelFromGui(playerId, session, parts[1]);
                    }
                    else
                    {
                        ClearInput(session);
                        session.Status = "Input cancelled.";
                    }
                    break;

                case "setprice":
                    BeginPriceInput(playerId, session);
                    break;

                case "setqty":
                    BeginQuantityInput(playerId, session);
                    break;

                case "list":
                    ListHeldFromGui(playerId, session);
                    break;

                default:
                    session.Status = "Unknown action: " + command;
                    break;
            }

            return Render(playerId, session);
        }

        private string HandleInput(string playerId, GuiSession session, string value)
        {
            string action = session.InputAction;
            ClearInput(session);

            if (string.IsNullOrEmpty(action))
            {
                session.Status = "Nothing is waiting for text input.";
                return Render(playerId, session);
            }

            if (string.Equals(action, "price", StringComparison.OrdinalIgnoreCase))
            {
                int price;
                if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out price))
                {
                    session.Status = "Enter a whole-number price.";
                    return Render(playerId, session);
                }

                if (price < config.MinimumPrice || price > config.MaximumPrice)
                {
                    session.Status = "The price must be between " + config.MinimumPrice + " and " +
                                     config.MaximumPrice + ".";
                    return Render(playerId, session);
                }

                session.DraftPrice = price;
                session.Status = "Price set to " + price + " " + CurrencyName() + ".";
                return Render(playerId, session);
            }

            if (string.Equals(action, "quantity", StringComparison.OrdinalIgnoreCase))
            {
                IPlayer player = FindPlayer(playerId);
                EntityPlayer entity = GetEntity(player);
                if (entity == null)
                {
                    session.Status = "Your player entity is not available.";
                    return Render(playerId, session);
                }

                ItemStack held = GetHeldStack(entity);
                if (held == null || held.IsEmpty())
                {
                    session.Status = "You are not holding an item.";
                    return Render(playerId, session);
                }

                int quantity;
                if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out quantity))
                {
                    session.Status = "Enter a whole-number quantity.";
                    return Render(playerId, session);
                }

                if (quantity < 1 || quantity > held.count)
                {
                    session.Status = "The quantity must be between 1 and " + held.count + ".";
                    return Render(playerId, session);
                }

                session.DraftQuantity = quantity;
                session.Status = "Quantity set to " + quantity + ".";
                return Render(playerId, session);
            }

            session.Status = "Unknown input action.";
            return Render(playerId, session);
        }

        private void SwitchTab(GuiSession session, string tab)
        {
            session.SelectedId = 0;
            session.Page = 0;
            session.Status = string.Empty;
            ClearInput(session);

            if (string.Equals(tab, "shared", StringComparison.OrdinalIgnoreCase))
            {
                session.Tab = "shared";
                return;
            }

            if (string.Equals(tab, "mine", StringComparison.OrdinalIgnoreCase))
            {
                session.Tab = "mine";
                return;
            }

            session.Tab = "home";
        }

        private void BeginPriceInput(string playerId, GuiSession session)
        {
            if (!CanSell(playerId))
            {
                session.Status = "You need the 'auctionhouse.sell' permission.";
                return;
            }

            EntityPlayer entity = GetEntity(FindPlayer(playerId));
            ItemStack held = GetHeldStack(entity);
            if (held == null || held.IsEmpty())
            {
                session.Status = "Hold the item you want to sell in your selected toolbelt slot.";
                return;
            }

            Prompt(session, "price", "Price for " + ItemName(held.itemValue) + " x" + held.count,
                session.DraftPrice > 0 ? session.DraftPrice.ToString(CultureInfo.InvariantCulture) : string.Empty);
            session.Tab = "mine";
        }

        private void BeginQuantityInput(string playerId, GuiSession session)
        {
            EntityPlayer entity = GetEntity(FindPlayer(playerId));
            ItemStack held = GetHeldStack(entity);
            if (held == null || held.IsEmpty())
            {
                session.Status = "You are not holding an item.";
                return;
            }

            Prompt(session, "quantity", "Quantity (1-" + held.count + ")",
                session.DraftQuantity > 0 ? session.DraftQuantity.ToString(CultureInfo.InvariantCulture) : held.count.ToString(CultureInfo.InvariantCulture));
            session.Tab = "mine";
        }

        private void ListHeldFromGui(string playerId, GuiSession session)
        {
            IPlayer player = FindPlayer(playerId);
            if (!CanSell(playerId))
            {
                session.Status = "You need the 'auctionhouse.sell' permission.";
                return;
            }

            EntityPlayer entity = GetEntity(player);
            ItemStack held = GetHeldStack(entity);
            if (held == null || held.IsEmpty())
            {
                session.Status = "Hold the item you want to sell in your selected toolbelt slot.";
                return;
            }

            if (session.DraftPrice <= 0)
            {
                session.Status = "Set a price first.";
                return;
            }

            int quantity = session.DraftQuantity;
            if (quantity <= 0 || quantity > held.count)
            {
                quantity = held.count;
            }

            string itemName = ItemName(held.itemValue);
            int price = session.DraftPrice;

            string error;
            if (!TryCreateListing(player, entity, held, quantity, price, out error))
            {
                session.Status = error;
                return;
            }

            session.DraftPrice = 0;
            session.DraftQuantity = 0;
            session.SelectedId = 0;
            session.Tab = "shared";
            session.Page = 0;
            session.Status = "Listed " + itemName + " x" + quantity + " for " +
                             FormatPrice(price) + ".";
        }

        private void BuyFromGui(string playerId, GuiSession session, string id)
        {
            long listingId;
            if (!long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out listingId))
            {
                session.Status = "That listing id is not valid.";
                return;
            }

            IPlayer buyer = FindPlayer(playerId);
            string error;
            if (!TryBuy(buyer, listingId, out error))
            {
                session.Status = error;
                return;
            }

            session.SelectedId = 0;
            session.Tab = "home";
            session.Status = "Purchase complete.";
        }

        private void CancelFromGui(string playerId, GuiSession session, string id)
        {
            long listingId;
            if (!long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out listingId))
            {
                session.Status = "That listing id is not valid.";
                return;
            }

            IPlayer seller = FindPlayer(playerId);
            string error;
            if (!TryCancel(seller, listingId, false, out error))
            {
                session.Status = error;
                return;
            }

            session.SelectedId = 0;
            session.Tab = "shared";
            session.Status = "Listing cancelled and the item was returned.";
        }

        private void Prompt(GuiSession session, string action, string label, string value)
        {
            session.InputAction = action;
            session.InputLabel = label;
            session.InputValue = value ?? string.Empty;
        }

        private void ClearInput(GuiSession session)
        {
            session.InputAction = null;
            session.InputLabel = string.Empty;
            session.InputValue = string.Empty;
        }

        private string ErrorView(string message)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("v2\n");
            builder.Append("protocol=2\n");
            builder.Append("title=").Append(Encode("Auction House")).Append('\n');
            builder.Append("subtitle=").Append(Encode("Auction data unavailable")).Append('\n');
            builder.Append("status=").Append(Encode(message)).Append('\n');
            builder.Append("tab=home\n");
            builder.Append("tab1label=").Append(Encode("Browse")).Append('\n');
            builder.Append("tab2label=").Append(Encode("My Listings")).Append('\n');
            builder.Append("tab3label=").Append(Encode("Sell")).Append('\n');
            builder.Append("background=").Append(Encode(IconAuction)).Append('\n');
            builder.Append("banner=").Append(Encode(IconAuction)).Append('\n');
            builder.Append("input=||0|\n");
            builder.Append("row=0||").Append(Encode("No data")).Append('|').Append(Encode(message))
                .Append("|0|0|").Append(Encode(IconWarn)).Append("||\n");
            builder.Append("btn=0|refresh|").Append(Encode("Retry")).Append("|1|")
                .Append(Encode("Reload the view.")).Append('|')
                .Append(Encode(IconRefresh)).Append("|\n");
            builder.Append("btn=1|home|").Append(Encode("SA Home")).Append("|1|")
                .Append(Encode("Return to Server Assist.")).Append('|')
                .Append(Encode(IconServer)).Append("|\n");
            return builder.ToString();
        }

        #endregion

        #region Rendering

        private string Render(string playerId, GuiSession session)
        {
            List<RowInfo> rows = new List<RowInfo>();
            List<ButtonInfo> buttons = new List<ButtonInfo>();
            string subtitle;

            if (session.SelectedId > 0)
            {
                RenderDetail(playerId, session, rows, buttons, out subtitle);
            }
            else if (string.Equals(session.Tab, "shared", StringComparison.OrdinalIgnoreCase))
            {
                RenderMyListings(playerId, session, rows, buttons, out subtitle);
            }
            else if (string.Equals(session.Tab, "mine", StringComparison.OrdinalIgnoreCase))
            {
                RenderSell(playerId, session, rows, buttons, out subtitle);
            }
            else
            {
                session.Tab = "home";
                RenderBrowse(playerId, session, rows, buttons, out subtitle);
            }

            StringBuilder builder = new StringBuilder(4096);
            builder.Append("v2\n");
            builder.Append("protocol=2\n");
            builder.Append("title=").Append(Encode("Auction House")).Append('\n');
            builder.Append("tab=").Append(Encode(session.Tab)).Append('\n');
            builder.Append("tab1label=").Append(Encode("Browse")).Append('\n');
            builder.Append("tab2label=").Append(Encode("My Listings")).Append('\n');
            builder.Append("tab3label=").Append(Encode("Sell")).Append('\n');
            builder.Append("subtitle=").Append(Encode(subtitle)).Append('\n');
            builder.Append("status=").Append(Encode(session.Status)).Append('\n');
            builder.Append("background=").Append(Encode(IconAuction)).Append('\n');
            builder.Append("banner=").Append(Encode(CurrentBanner(session))).Append('\n');
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
                    .Append(Encode(button.Tooltip)).Append('|')
                    .Append(Encode(button.Icon)).Append('|')
                    .Append(Encode(button.IconTint)).Append('\n');
            }

            return builder.ToString();
        }

        private static string CurrentBanner(GuiSession session)
        {
            if (session == null)
            {
                return IconAuction;
            }

            if (session.SelectedId > 0)
            {
                return IconAuction;
            }

            if (string.Equals(session.Tab, "shared", StringComparison.OrdinalIgnoreCase))
            {
                return IconListings;
            }

            if (string.Equals(session.Tab, "mine", StringComparison.OrdinalIgnoreCase))
            {
                return IconGive;
            }

            return IconItems;
        }

        private void RenderBrowse(string playerId, GuiSession session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            List<Listing> listings = GetBrowseListings();
            int pages = PageCount(listings.Count);
            ClampPage(session, listings.Count);

            subtitle = "Browse  |  " + listings.Count + " lot(s)  |  page " + (session.Page + 1) + "/" + pages +
                       "  |  balance: " + CurrencyAmount(GetCurrencyCount(FindPlayer(playerId)));

            int start = session.Page * UiRows;
            for (int i = 0; i < UiRows && start + i < listings.Count; i++)
            {
                Listing listing = listings[start + i];
                rows.Add(new RowInfo
                {
                    Action = "view|" + listing.Id.ToString(CultureInfo.InvariantCulture),
                    Label = listing.ItemName + (listing.Count > 1 ? " x" + listing.Count : string.Empty),
                    Sub = FormatPrice(listing.Price) + "  |  " + listing.SellerName + "  |  " + TimeLeft(listing),
                    Icon = ListingIcon(listing),
                    Badge = ListingBadge(playerId, listing),
                    Enabled = true
                });
            }

            if (listings.Count == 0)
            {
                rows.Add(new RowInfo
                {
                    Label = "No active listings.",
                    Sub = "Hold an item and open the Sell tab to create one.",
                    Icon = IconItems,
                    Enabled = false
                });
            }

            AddPagingButtons(session, pages, buttons);
            buttons.Add(new ButtonInfo { Action = "refresh", Label = "Refresh", Icon = IconRefresh });
            buttons.Add(new ButtonInfo { Action = "tab|shared", Label = "My Listings", Icon = IconListings });
            buttons.Add(new ButtonInfo { Action = "tab|mine", Label = "Sell Item", Icon = IconGive });
            buttons.Add(new ButtonInfo { Action = "close", Label = "Close" });
            buttons.Add(new ButtonInfo { Action = "home", Label = "SA Home", Tooltip = "Return to Server Assist.", Icon = IconServer });
        }

        private void RenderMyListings(string playerId, GuiSession session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            List<Listing> listings = GetPlayerListings(playerId);
            int pages = PageCount(listings.Count);
            ClampPage(session, listings.Count);

            subtitle = "My Listings  |  " + listings.Count + " active  |  page " + (session.Page + 1) + "/" + pages;

            int start = session.Page * UiRows;
            for (int i = 0; i < UiRows && start + i < listings.Count; i++)
            {
                Listing listing = listings[start + i];
                rows.Add(new RowInfo
                {
                    Action = "manage|" + listing.Id.ToString(CultureInfo.InvariantCulture),
                    Label = listing.ItemName + (listing.Count > 1 ? " x" + listing.Count : string.Empty),
                    Sub = FormatPrice(listing.Price) + "  |  " + TimeLeft(listing),
                    Icon = ListingIcon(listing),
                    Badge = IsExpiringSoon(listing) ? "SOON" : "ACTIVE",
                    Enabled = true,
                    Selected = session.SelectedId == listing.Id
                });
            }

            if (listings.Count == 0)
            {
                rows.Add(new RowInfo
                {
                    Label = "You have no active listings.",
                    Sub = "Use the Sell tab while holding an item.",
                    Icon = IconListings,
                    Enabled = false
                });
            }

            AddPagingButtons(session, pages, buttons);
            buttons.Add(new ButtonInfo { Action = "refresh", Label = "Refresh", Icon = IconRefresh });
            buttons.Add(new ButtonInfo { Action = "tab|home", Label = "Browse", Icon = IconItems });
            buttons.Add(new ButtonInfo { Action = "tab|mine", Label = "Sell Item", Icon = IconGive });
            buttons.Add(new ButtonInfo { Action = "close", Label = "Close" });
            buttons.Add(new ButtonInfo { Action = "home", Label = "SA Home", Tooltip = "Return to Server Assist.", Icon = IconServer });
        }

        private void RenderSell(string playerId, GuiSession session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            IPlayer player = FindPlayer(playerId);
            EntityPlayer entity = GetEntity(player);
            ItemStack held = GetHeldStack(entity);
            bool hasItem = held != null && !held.IsEmpty();

            if (!hasItem)
            {
                subtitle = "Sell  |  hold an item in your selected toolbelt slot";
                rows.Add(new RowInfo
                {
                    Label = "No item is held.",
                    Sub = "Select a toolbelt slot that contains the item you want to sell.",
                    Icon = IconBackpack,
                    Enabled = false
                });
            }
            else
            {
                if (session.DraftQuantity <= 0 || session.DraftQuantity > held.count)
                {
                    session.DraftQuantity = held.count;
                }

                int fee = FeeFor(held.itemValue, session.DraftPrice);
                subtitle = "Sell  |  " + ItemName(held.itemValue) + " x" + held.count +
                           "  |  balance: " + CurrencyAmount(GetCurrencyCount(player));

                rows.Add(new RowInfo
                {
                    Label = "Held item",
                    Sub = ItemName(held.itemValue) + " x" + held.count + QualitySuffix(held.itemValue),
                    Icon = ItemIcon(held.itemValue),
                    Badge = "HELD",
                    Enabled = false
                });
                rows.Add(new RowInfo
                {
                    Action = "setprice",
                    Label = "Set Price",
                    Sub = session.DraftPrice > 0 ? FormatPrice(session.DraftPrice) : "not set",
                    Icon = IconShop,
                    Enabled = true
                });
                rows.Add(new RowInfo
                {
                    Action = "setqty",
                    Label = "Set Quantity",
                    Sub = session.DraftQuantity + " of " + held.count,
                    Icon = IconBackpack,
                    Enabled = held.count > 1
                });
                rows.Add(new RowInfo
                {
                    Action = "list",
                    Label = "List Item",
                    Sub = session.DraftPrice > 0 ? "Listing fee: " + FormatPrice(fee) : "Set a price first",
                    Icon = IconGive,
                    Enabled = session.DraftPrice > 0
                });
                rows.Add(new RowInfo
                {
                    Label = "Duration",
                    Sub = config.ListingDurationHours + " hour(s)",
                    Icon = IconTime,
                    Enabled = false
                });
                rows.Add(new RowInfo
                {
                    Label = "Sales tax",
                    Sub = config.SalesTaxPercent.ToString("0.##", CultureInfo.InvariantCulture) + "% of the sale price",
                    Icon = IconShop,
                    Enabled = false
                });
                rows.Add(new RowInfo
                {
                    Label = "Balance",
                    Sub = CurrencyAmount(GetCurrencyCount(player)),
                    Icon = CurrencyIcon(),
                    Enabled = false
                });
            }

            buttons.Add(new ButtonInfo { Action = "refresh", Label = "Refresh", Icon = IconRefresh });
            buttons.Add(new ButtonInfo { Action = "tab|home", Label = "Browse", Icon = IconItems });
            buttons.Add(new ButtonInfo { Action = "tab|shared", Label = "My Listings", Icon = IconListings });
            buttons.Add(new ButtonInfo { Action = "close", Label = "Close" });
            buttons.Add(new ButtonInfo { Action = "home", Label = "SA Home", Tooltip = "Return to Server Assist.", Icon = IconServer });
        }

        private void RenderDetail(string playerId, GuiSession session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            Listing listing = FindListing(session.SelectedId);
            if (listing == null)
            {
                session.SelectedId = 0;
                session.Tab = "home";
                session.Status = "That listing no longer exists.";
                RenderBrowse(playerId, session, rows, buttons, out subtitle);
                return;
            }

            bool own = string.Equals(listing.SellerId, playerId, StringComparison.OrdinalIgnoreCase);
            bool active = string.Equals(listing.State, StateActive, StringComparison.OrdinalIgnoreCase);

            subtitle = "Listing #" + listing.Id + "  |  " + listing.State +
                       "  |  " + (own ? "your listing" : "seller: " + listing.SellerName);

            if (active)
            {
                if (own)
                {
                    rows.Add(new RowInfo
                    {
                        Action = "cancel|" + listing.Id,
                        Label = "Cancel Listing",
                        Sub = "Return the item to your inventory",
                        Icon = IconCancel,
                        Badge = "YOURS",
                        Enabled = true
                    });

                    if (config.AllowBuyOwnListings)
                    {
                        rows.Add(new RowInfo
                        {
                            Action = "buy|" + listing.Id,
                            Label = "Buy Own Listing",
                            Sub = FormatPrice(listing.Price),
                            Icon = IconShop,
                            Enabled = true
                        });
                    }
                }
                else
                {
                    rows.Add(new RowInfo
                    {
                        Action = "buy|" + listing.Id,
                        Label = "Buy Now",
                        Sub = "Pay " + FormatPrice(listing.Price),
                        Icon = IconShop,
                        Badge = "BUY",
                        Enabled = true
                    });
                }
            }

            rows.Add(new RowInfo
            {
                Label = "Item",
                Sub = listing.ItemName,
                Icon = ListingIcon(listing),
                Badge = own ? "YOURS" : string.Empty,
                Enabled = false
            });
            rows.Add(new RowInfo { Label = "Quantity", Sub = listing.Count.ToString(CultureInfo.InvariantCulture), Icon = IconBackpack, Enabled = false });
            rows.Add(new RowInfo { Label = "Price", Sub = FormatPrice(listing.Price), Icon = IconShop, Enabled = false });
            rows.Add(new RowInfo { Label = "Seller", Sub = listing.SellerName, Icon = IconSeller, Enabled = false });
            rows.Add(new RowInfo { Label = "Fee paid", Sub = FormatPrice(listing.FeePaid), Icon = IconShop, Enabled = false });
            rows.Add(new RowInfo
            {
                Label = "Expires",
                Sub = active ? TimeLeft(listing) : "not active",
                Icon = active ? IconTime : IconWarn,
                Enabled = false
            });
            rows.Add(new RowInfo
            {
                Label = "State",
                Sub = listing.State,
                Icon = StateIcon(listing),
                Badge = listing.State.ToUpperInvariant(),
                Enabled = false
            });

            buttons.Add(new ButtonInfo { Action = "back", Label = "Back" });
            buttons.Add(new ButtonInfo { Action = "refresh", Label = "Refresh", Icon = IconRefresh });
            buttons.Add(new ButtonInfo { Action = "close", Label = "Close" });
            buttons.Add(new ButtonInfo { Action = "home", Label = "SA Home", Tooltip = "Return to Server Assist.", Icon = IconServer });
        }

        private void AddPagingButtons(GuiSession session, int pages, List<ButtonInfo> buttons)
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

        private void ClampPage(GuiSession session, int count)
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

        #region Auction operations

        private bool TryCreateListing(IPlayer player, EntityPlayer entity, ItemStack held, int quantity, int price, out string error)
        {
            error = null;

            if (player == null || entity == null || held == null || held.IsEmpty())
            {
                error = "You are not holding an item.";
                return false;
            }

            if (!CanSell(player.Id))
            {
                error = "You need the 'auctionhouse.sell' permission.";
                return false;
            }

            if (!currencyValid)
            {
                error = "The configured currency item is not available on this server.";
                return false;
            }

            if (quantity < 1 || quantity > held.count)
            {
                error = "The quantity must be between 1 and " + held.count + ".";
                return false;
            }

            if (price < config.MinimumPrice || price > config.MaximumPrice)
            {
                error = "The price must be between " + config.MinimumPrice + " and " + config.MaximumPrice + ".";
                return false;
            }

            ItemValue itemValue = held.itemValue;
            if (itemValue == null || itemValue.IsEmpty())
            {
                error = "The held item is not valid.";
                return false;
            }

            string shortName = ItemShortName(itemValue);
            string itemName = ItemName(itemValue);
            string itemData = SerializeItemValue(itemValue);

            if (!config.AllowListingCurrency && string.Equals(shortName, ItemShortName(currencyValue), StringComparison.OrdinalIgnoreCase))
            {
                error = "You cannot list the currency item.";
                return false;
            }

            if (blockedItems.Contains(shortName) || blockedItems.Contains(itemName))
            {
                error = "That item cannot be listed.";
                return false;
            }

            if (string.IsNullOrEmpty(itemData))
            {
                error = "That item could not be saved to the auction database.";
                return false;
            }

            List<Listing> playerListings = GetPlayerListings(player.Id);
            if (config.MaxListingsPerPlayer > 0 && playerListings.Count >= config.MaxListingsPerPlayer)
            {
                error = "You already have " + playerListings.Count + " active listing(s). The limit is " +
                        config.MaxListingsPerPlayer + ".";
                return false;
            }

            if (config.MaxActiveListings > 0 && CountActiveListings() >= config.MaxActiveListings)
            {
                error = "The auction house is full. Try again later.";
                return false;
            }

            int fee = FeeFor(itemValue, price);
            int balance = GetCurrencyCount(player);
            if (balance < fee)
            {
                error = "The listing fee is " + FormatPrice(fee) + ", but you only have " + FormatPrice(balance) + ".";
                return false;
            }

            if (fee > 0 && RemoveCurrency(entity, fee) != fee)
            {
                error = "The listing fee could not be taken from your inventory.";
                return false;
            }

            ItemStack taken;
            if (!TakeHeldStack(entity, quantity, out taken))
            {
                if (fee > 0)
                {
                    GiveCurrency(player, fee);
                }

                error = "The held item changed before it could be listed.";
                return false;
            }

            Listing listing = new Listing
            {
                Id = data.NextId++,
                SellerId = player.Id,
                SellerName = player.Name,
                ItemData = itemData,
                ItemName = itemName,
                ItemShortName = shortName,
                Count = taken.count,
                Price = price,
                FeePaid = fee,
                CreatedUtc = DateTime.UtcNow.Ticks,
                ExpiresUtc = DateTime.UtcNow.AddHours(config.ListingDurationHours).Ticks,
                State = StateActive
            };

            data.Listings.Add(listing);
            SaveData();

            Reply(player, "Listed " + listing.ItemName + " x" + listing.Count + " for " + FormatPrice(price) +
                         ". Fee paid: " + FormatPrice(fee) + ".");
            return true;
        }

        private bool TryBuy(IPlayer buyer, long listingId, out string error)
        {
            error = null;

            if (buyer == null || string.IsNullOrEmpty(buyer.Id))
            {
                error = "Could not identify the buyer.";
                return false;
            }

            Listing listing = FindListing(listingId);
            if (listing == null || !string.Equals(listing.State, StateActive, StringComparison.OrdinalIgnoreCase))
            {
                error = "That listing is no longer available.";
                return false;
            }

            if (listing.ExpiresUtc <= DateTime.UtcNow.Ticks)
            {
                ExpireListing(listing);
                SaveData();
                error = "That listing has expired.";
                return false;
            }

            if (!config.AllowBuyOwnListings &&
                string.Equals(listing.SellerId, buyer.Id, StringComparison.OrdinalIgnoreCase))
            {
                error = "You cannot buy your own listing.";
                return false;
            }

            if (!currencyValid)
            {
                error = "The configured currency item is not available on this server.";
                return false;
            }

            EntityPlayer buyerEntity = GetEntity(buyer);
            if (buyerEntity == null)
            {
                error = "Your player entity is not available.";
                return false;
            }

            ItemValue itemValue = DeserializeItemValue(listing.ItemData);
            if (itemValue == null || itemValue.IsEmpty())
            {
                listing.State = StateBroken;
                SaveData();
                error = "That listing's item data is corrupt. An admin has been notified by the server log.";
                Interface.Oxide.LogError("[AuctionHouse] Listing #" + listing.Id + " has corrupt item data.");
                return false;
            }

            ItemStack item = new ItemStack(itemValue, listing.Count);
            if (!CanReceiveItem(buyerEntity, item))
            {
                error = "You do not have enough free inventory space.";
                return false;
            }

            int balance = GetCurrencyCount(buyer);
            if (balance < listing.Price)
            {
                error = "You need " + FormatPrice(listing.Price) + " but you only have " + FormatPrice(balance) + ".";
                return false;
            }

            int removed = RemoveCurrency(buyerEntity, listing.Price);
            if (removed != listing.Price)
            {
                if (removed > 0)
                {
                    GiveCurrency(buyer, removed);
                }

                error = "The payment could not be taken from your inventory.";
                return false;
            }

            GiveItem(buyerEntity, item);

            int tax = CalculateTax(listing.Price);
            int sellerAmount = Math.Max(0, listing.Price - tax);

            listing.State = StateSold;
            listing.BuyerId = buyer.Id;
            listing.BuyerName = buyer.Name;
            listing.SoldUtc = DateTime.UtcNow.Ticks;
            listing.TaxPaid = tax;
            listing.SellerPaid = sellerAmount;
            listing.ItemData = string.Empty;

            PaySeller(listing, sellerAmount);
            SaveData();

            Reply(buyer, "Purchased " + listing.ItemName + " x" + listing.Count + " for " + FormatPrice(listing.Price) + ".");
            NotifySellerOfSale(listing, sellerAmount, tax);
            BroadcastSale(listing);
            Interface.Oxide.LogInfo("[AuctionHouse] " + buyer.Name + " bought listing #" + listing.Id + " (" +
                                    listing.ItemName + " x" + listing.Count + ") for " + listing.Price + ".");
            return true;
        }

        private bool TryCancel(IPlayer seller, long listingId, bool byAdmin, out string error)
        {
            error = null;

            if (seller == null || string.IsNullOrEmpty(seller.Id))
            {
                error = "Could not identify the seller.";
                return false;
            }

            Listing listing = FindListing(listingId);
            if (listing == null || !string.Equals(listing.State, StateActive, StringComparison.OrdinalIgnoreCase))
            {
                error = "That listing is no longer active.";
                return false;
            }

            if (!byAdmin && !string.Equals(listing.SellerId, seller.Id, StringComparison.OrdinalIgnoreCase))
            {
                error = "That listing does not belong to you.";
                return false;
            }

            listing.State = StateCancelled;
            listing.SoldUtc = DateTime.UtcNow.Ticks;

            ReturnListingItem(listing, byAdmin ? "An admin removed your listing" : "Your listing was cancelled");
            if (config.RefundFeeOnCancel && listing.FeePaid > 0)
            {
                QueueOrGiveCurrency(listing.SellerId, listing.FeePaid, "Listing fee refund for #" + listing.Id);
            }

            SaveData();
            Reply(seller, "Listing #" + listing.Id + " was cancelled.");
            return true;
        }

        private void ExpireListing(Listing listing)
        {
            if (listing == null || !string.Equals(listing.State, StateActive, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            listing.State = StateExpired;
            listing.SoldUtc = DateTime.UtcNow.Ticks;

            if (config.RefundExpiredItems)
            {
                ReturnListingItem(listing, "Your listing expired");
            }
            else
            {
                listing.ItemData = string.Empty;
            }

            NotifySellerOfExpiry(listing);
        }

        private void ProcessExpiredListings()
        {
            if (data == null || data.Listings == null || ItemClass.list == null)
            {
                return;
            }

            long now = DateTime.UtcNow.Ticks;
            int expired = 0;

            foreach (Listing listing in data.Listings)
            {
                if (listing != null &&
                    string.Equals(listing.State, StateActive, StringComparison.OrdinalIgnoreCase) &&
                    listing.ExpiresUtc <= now)
                {
                    ExpireListing(listing);
                    expired++;
                }
            }

            if (expired > 0)
            {
                SaveData();
                Interface.Oxide.LogInfo("[AuctionHouse] Expired " + expired + " listing(s).");
            }
        }

        private void ReturnListingItem(Listing listing, string reason)
        {
            if (listing == null)
            {
                return;
            }

            ItemValue itemValue = DeserializeItemValue(listing.ItemData);
            listing.ItemData = string.Empty;

            if (itemValue == null || itemValue.IsEmpty())
            {
                listing.State = StateBroken;
                Interface.Oxide.LogError("[AuctionHouse] Could not restore the item for listing #" + listing.Id + ".");
                QueueOrGiveCurrency(listing.SellerId, listing.FeePaid,
                    "Listing #" + listing.Id + " could not be restored; the fee was refunded.");
                return;
            }

            ItemStack item = new ItemStack(itemValue, listing.Count);
            IPlayer seller = covalence.Players.FindPlayerById(listing.SellerId);

            if (seller != null && seller.IsConnected)
            {
                EntityPlayer entity = GetEntity(seller);
                if (entity != null)
                {
                    GiveItem(entity, item);
                    return;
                }
            }

            QueueItem(listing.SellerId, item, listing.ItemName, reason);
        }

        private void PaySeller(Listing listing, int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            QueueOrGiveCurrency(listing.SellerId, amount, "Auction sale #" + listing.Id + " paid " + amount +
                                                           " " + CurrencyName() + ".");
        }

        private void NotifySellerOfSale(Listing listing, int sellerAmount, int tax)
        {
            if (!config.NotifyOnSale)
            {
                return;
            }

            IPlayer seller = covalence.Players.FindPlayerById(listing.SellerId);
            if (seller != null && seller.IsConnected)
            {
                Reply(seller, listing.ItemName + " x" + listing.Count + " sold for " + FormatPrice(listing.Price) +
                              ". You receive " + FormatPrice(sellerAmount) +
                              (tax > 0 ? " after " + FormatPrice(tax) + " tax." : "."));
            }
        }

        private void NotifySellerOfExpiry(Listing listing)
        {
            if (!config.NotifyOnExpire)
            {
                return;
            }

            IPlayer seller = covalence.Players.FindPlayerById(listing.SellerId);
            if (seller != null && seller.IsConnected)
            {
                Reply(seller, "Listing #" + listing.Id + " (" + listing.ItemName + " x" + listing.Count +
                              ") expired." + (config.RefundExpiredItems ? " The item was returned." : string.Empty));
            }
        }

        private void BroadcastSale(Listing listing)
        {
            if (!config.BroadcastSales)
            {
                return;
            }

            string message = listing.BuyerName + " bought " + listing.ItemName + " x" + listing.Count +
                             " from " + listing.SellerName + " for " + FormatPrice(listing.Price) + ".";

            foreach (IPlayer player in covalence.Players.Connected)
            {
                if (player != null)
                {
                    Reply(player, message);
                }
            }
        }

        private void DeliverPending(IPlayer player)
        {
            if (data == null || data.Pending == null || player == null || string.IsNullOrEmpty(player.Id))
            {
                return;
            }

            PendingDelivery pending;
            if (!data.Pending.TryGetValue(player.Id, out pending) || pending == null)
            {
                return;
            }

            bool changed = false;

            if (pending.Currency > 0)
            {
                int amount = pending.Currency;
                if (GiveCurrency(player, amount))
                {
                    pending.Currency = 0;
                    Reply(player, "Auction House paid you " + FormatPrice(amount) + ".");
                    changed = true;
                }
            }

            if (pending.Items != null && pending.Items.Count > 0)
            {
                List<PendingItem> remaining = new List<PendingItem>();

                foreach (PendingItem pendingItem in pending.Items)
                {
                    ItemValue itemValue = DeserializeItemValue(pendingItem.ItemData);
                    if (itemValue == null || itemValue.IsEmpty())
                    {
                        Interface.Oxide.LogError("[AuctionHouse] Could not restore a pending item for " + player.Name +
                                                " (" + pendingItem.ItemName + ").");
                        changed = true;
                        continue;
                    }

                    ItemStack item = new ItemStack(itemValue, pendingItem.Count);
                    EntityPlayer entity = GetEntity(player);
                    if (entity != null)
                    {
                        GiveItem(entity, item);
                    }
                    else
                    {
                        remaining.Add(pendingItem);
                        continue;
                    }

                    Reply(player, "Auction House: " + (string.IsNullOrEmpty(pendingItem.Reason) ? "Returned" : pendingItem.Reason) +
                                  " - " + pendingItem.ItemName + " x" + pendingItem.Count + ".");
                    changed = true;
                }

                pending.Items = remaining;
            }

            bool fullyDelivered = (pending.Items == null || pending.Items.Count == 0) && pending.Currency <= 0;

            if (fullyDelivered)
            {
                foreach (string message in pending.Messages)
                {
                    if (!string.IsNullOrEmpty(message))
                    {
                        Reply(player, message);
                    }
                }

                pending.Messages.Clear();
                data.Pending.Remove(player.Id);            }

            if (changed || fullyDelivered)
            {
                SaveData();
            }
        }

        private void QueueOrGiveCurrency(string playerId, int amount, string message)
        {
            if (amount <= 0 || string.IsNullOrEmpty(playerId))
            {
                return;
            }

            IPlayer player = covalence.Players.FindPlayerById(playerId);
            if (player != null && player.IsConnected)
            {
                if (GiveCurrency(player, amount))
                {
                    if (!string.IsNullOrEmpty(message))
                    {
                        Reply(player, message);
                    }

                    return;
                }
            }

            PendingDelivery pending = GetPending(playerId, true);
            pending.Currency += amount;
            if (!string.IsNullOrEmpty(message))
            {
                pending.Messages.Add(message);
            }
        }

        private void QueueItem(string playerId, ItemStack item, string itemName, string reason)
        {
            if (string.IsNullOrEmpty(playerId) || item == null || item.IsEmpty())
            {
                return;
            }

            PendingDelivery pending = GetPending(playerId, true);
            pending.Items.Add(new PendingItem
            {
                ItemData = SerializeItemValue(item.itemValue),
                Count = item.count,
                ItemName = itemName,
                Reason = reason,
                CreatedUtc = DateTime.UtcNow.Ticks
            });
        }

        private PendingDelivery GetPending(string playerId, bool create)
        {
            PendingDelivery pending;
            if (data.Pending.TryGetValue(playerId, out pending))
            {
                if (pending.Items == null)
                {
                    pending.Items = new List<PendingItem>();
                }

                if (pending.Messages == null)
                {
                    pending.Messages = new List<string>();
                }

                return pending;
            }

            if (!create)
            {
                return null;
            }

            pending = new PendingDelivery();
            data.Pending[playerId] = pending;
            return pending;
        }

        private int CalculateTax(int price)
        {
            if (config.SalesTaxPercent <= 0.0 || price <= 0)
            {
                return 0;
            }

            return Math.Max(0, Math.Min(price, (int)Math.Ceiling(price * config.SalesTaxPercent / 100.0)));
        }

        private int FeeFor(ItemValue itemValue, int price)
        {
            if (config == null)
            {
                return 0;
            }

            int flat = config.ListingFee;
            string shortName = ItemShortName(itemValue);

            if (!string.IsNullOrEmpty(shortName) &&
                config.ListingFeesByItem != null &&
                config.ListingFeesByItem.TryGetValue(shortName, out int overrideFee))
            {
                flat = overrideFee;
            }

            int percentFee = 0;
            if (config.ListingFeePercent > 0.0 && price > 0)
            {
                percentFee = (int)Math.Ceiling(price * config.ListingFeePercent / 100.0);
            }

            if (string.Equals(config.ListingFeeMode, "Percent", StringComparison.OrdinalIgnoreCase))
            {
                return Math.Max(0, percentFee);
            }

            if (string.Equals(config.ListingFeeMode, "FlatAndPercent", StringComparison.OrdinalIgnoreCase))
            {
                return Math.Max(0, flat + percentFee);
            }

            return Math.Max(0, flat);
        }

        private string FeeRuleDescription(ItemValue itemValue)
        {
            if (string.Equals(config.ListingFeeMode, "Percent", StringComparison.OrdinalIgnoreCase))
            {
                return config.ListingFeePercent.ToString("0.##", CultureInfo.InvariantCulture) + "% of the price";
            }

            if (string.Equals(config.ListingFeeMode, "FlatAndPercent", StringComparison.OrdinalIgnoreCase))
            {
                return FormatPrice(FeeFor(itemValue, 0)) + " + " +
                       config.ListingFeePercent.ToString("0.##", CultureInfo.InvariantCulture) + "% of the price";
            }

            return FormatPrice(FeeFor(itemValue, 0));
        }

        #endregion

        #region Inventory and item helpers

        private IPlayer FindPlayer(string playerId)
        {
            return string.IsNullOrEmpty(playerId) ? null : covalence.Players.FindPlayerById(playerId);
        }

        private EntityPlayer GetEntity(IPlayer player)
        {
            return player == null ? null : player.Object as EntityPlayer;
        }

        private ItemStack GetHeldStack(EntityPlayer entity)
        {
            if (entity == null || entity.inventory == null)
            {
                return null;
            }

            int slot = entity.inventory.SelectedSlot;
            if (slot < 0 || slot >= entity.inventory.SlotCount)
            {
                return null;
            }

            return entity.inventory.GetStackAt(slot);
        }

        private bool TakeHeldStack(EntityPlayer entity, int quantity, out ItemStack taken)
        {
            taken = null;

            ItemStack held = GetHeldStack(entity);
            if (held == null || held.IsEmpty() || quantity < 1 || quantity > held.count)
            {
                return false;
            }

            taken = new ItemStack(held.itemValue.Clone(), quantity);

            if (quantity >= held.count)
            {
                held.Clear();
            }
            else
            {
                held.count -= quantity;
            }

            entity.inventory.SetStackAt(entity.inventory.SelectedSlot, held);
            return true;
        }

        private bool CanReceiveItem(EntityPlayer entity, ItemStack item)
        {
            if (entity == null || item == null || item.IsEmpty())
            {
                return false;
            }

            if (entity.bag != null && entity.bag.CanTakeItem(item))
            {
                return true;
            }

            if (entity.inventory != null)
            {
                for (int i = 0; i < entity.inventory.SlotCount; i++)
                {
                    ItemStack slot = entity.inventory.GetStackAt(i);
                    if (slot == null || slot.IsEmpty() || slot.CanStackWith(item))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void GiveItem(EntityPlayer entity, ItemStack item)
        {
            if (entity == null || item == null || item.IsEmpty())
            {
                return;
            }

            ItemClass itemClass = item.itemValue == null ? null : item.itemValue.ItemClass;
            int maxStack = itemClass == null ? 1 : Math.Max(1, itemClass.MaxCount);
            int remaining = item.count;

            while (remaining > 0)
            {
                int chunk = Math.Min(remaining, maxStack);
                ItemStack part = new ItemStack(item.itemValue.Clone(), chunk);
                GiveSingleItem(entity, part);
                remaining -= chunk;
            }
        }

        private void GiveSingleItem(EntityPlayer entity, ItemStack item)
        {
            if (entity.bag != null)
            {
                ItemStack bagCopy = item.Clone();
                if (entity.bag.AddItem(bagCopy))
                {
                    return;
                }
            }

            if (entity.inventory != null)
            {
                ItemStack beltCopy = item.Clone();
                if (entity.inventory.AddItem(beltCopy))
                {
                    return;
                }
            }

            GameManager.Instance.ItemDropServer(item.Clone(), entity.GetPosition(), Vector3.zero, entity.entityId);
        }

        private int GetCurrencyCount(IPlayer player)
        {
            EntityPlayer entity = GetEntity(player);
            if (entity == null || !currencyValid)
            {
                return 0;
            }

            int count = 0;
            if (entity.bag != null)
            {
                count += entity.bag.GetItemCount(currencyValue);
            }

            if (entity.inventory != null)
            {
                count += entity.inventory.GetItemCount(currencyValue);
            }

            return count;
        }

        private int RemoveCurrency(EntityPlayer entity, int amount)
        {
            if (entity == null || !currencyValid || amount <= 0)
            {
                return 0;
            }

            int remaining = amount;

            if (entity.bag != null && remaining > 0)
            {
                remaining -= entity.bag.DecItem(currencyValue, remaining);
            }

            if (entity.inventory != null && remaining > 0)
            {
                remaining -= entity.inventory.DecItem(currencyValue, remaining);
            }

            return amount - remaining;
        }

        private bool GiveCurrency(IPlayer player, int amount)
        {
            if (player == null || amount <= 0 || !currencyValid)
            {
                return false;
            }

            EntityPlayer entity = GetEntity(player);
            if (entity == null)
            {
                return false;
            }

            GiveItem(entity, new ItemStack(currencyValue.Clone(), amount));
            return true;
        }

        private static string SerializeItemValue(ItemValue itemValue)
        {
            if (itemValue == null || itemValue.IsEmpty())
            {
                return string.Empty;
            }

            try
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    PooledBinaryWriter writer = new PooledBinaryWriter();
                    writer.SetBaseStream(stream);
                    itemValue.Write(writer);
                    writer.Flush();
                    return Convert.ToBase64String(stream.ToArray());
                }
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError("[AuctionHouse] Could not serialize an item: " + exception);
                return string.Empty;
            }
        }

        private static ItemValue DeserializeItemValue(string value)
        {
            if (string.IsNullOrEmpty(value) || ItemClass.list == null)
            {
                return null;
            }

            try
            {
                byte[] bytes = Convert.FromBase64String(value);
                using (MemoryStream stream = new MemoryStream(bytes))
                {
                    PooledBinaryReader reader = new PooledBinaryReader();
                    reader.SetBaseStream(stream);
                    ItemValue itemValue = new ItemValue();
                    itemValue.Read(reader);
                    return itemValue;
                }
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError("[AuctionHouse] Could not deserialize an item: " + exception);
                return null;
            }
        }

        private static string ItemName(ItemValue itemValue)
        {
            if (itemValue == null)
            {
                return "Unknown item";
            }

            ItemClass itemClass = itemValue.ItemClass;
            if (itemClass == null)
            {
                return "Unknown item";
            }

            string name = itemClass.GetLocalizedItemName();
            if (string.IsNullOrEmpty(name))
            {
                name = itemClass.GetItemName();
            }

            if (string.IsNullOrEmpty(name))
            {
                name = itemClass.Name;
            }

            return string.IsNullOrEmpty(name) ? "Unknown item" : name;
        }

        private static string ItemShortName(ItemValue itemValue)
        {
            if (itemValue == null)
            {
                return string.Empty;
            }

            ItemClass itemClass = itemValue.ItemClass;
            return itemClass == null ? string.Empty : itemClass.Name;
        }

        private static string ItemIcon(ItemValue itemValue)
        {
            string shortName = ItemShortName(itemValue);
            return string.IsNullOrEmpty(shortName) ? IconItems : "item:" + shortName;
        }

        private static string ListingIcon(Listing listing)
        {
            if (listing == null || string.IsNullOrEmpty(listing.ItemShortName))
            {
                return IconAuction;
            }

            return "item:" + listing.ItemShortName;
        }

        private static bool IsExpiringSoon(Listing listing)
        {
            if (listing == null)
            {
                return false;
            }

            long remaining = listing.ExpiresUtc - DateTime.UtcNow.Ticks;
            return remaining > 0 && remaining <= TimeSpan.FromHours(1).Ticks;
        }

        private static string ListingBadge(string playerId, Listing listing)
        {
            if (listing == null)
            {
                return string.Empty;
            }

            if (string.Equals(listing.SellerId, playerId, StringComparison.OrdinalIgnoreCase))
            {
                return "YOURS";
            }

            if (IsExpiringSoon(listing))
            {
                return "SOON";
            }

            return listing.Count > 1 ? "x" + listing.Count : string.Empty;
        }

        private static string StateIcon(Listing listing)
        {
            if (listing == null)
            {
                return IconWarn;
            }

            if (string.Equals(listing.State, StateActive, StringComparison.OrdinalIgnoreCase))
            {
                return IconAuction;
            }

            if (string.Equals(listing.State, StateSold, StringComparison.OrdinalIgnoreCase))
            {
                return IconShop;
            }

            if (string.Equals(listing.State, StateCancelled, StringComparison.OrdinalIgnoreCase))
            {
                return IconCancel;
            }

            return IconWarn;
        }

        private string CurrencyIcon()
        {
            if (currencyValid && currencyValue != null)
            {
                string shortName = ItemShortName(currencyValue);
                if (!string.IsNullOrEmpty(shortName))
                {
                    return "item:" + shortName;
                }
            }

            return IconShop;
        }

        private static string QualitySuffix(ItemValue itemValue)
        {
            if (itemValue == null || itemValue.Quality <= 0)
            {
                return string.Empty;
            }

            return "  |  quality " + itemValue.Quality;
        }

        #endregion

        #region Listing helpers

        private Listing FindListing(long id)
        {
            if (data == null || data.Listings == null)
            {
                return null;
            }

            for (int i = 0; i < data.Listings.Count; i++)
            {
                Listing listing = data.Listings[i];
                if (listing != null && listing.Id == id)
                {
                    return listing;
                }
            }

            return null;
        }

        private List<Listing> GetBrowseListings()
        {
            List<Listing> listings = data.Listings
                .Where(listing => listing != null &&
                                  string.Equals(listing.State, StateActive, StringComparison.OrdinalIgnoreCase) &&
                                  listing.ExpiresUtc > DateTime.UtcNow.Ticks)
                .ToList();

            string sort = config.BrowseSort == null ? "Newest" : config.BrowseSort.Trim();

            if (string.Equals(sort, "PriceLow", StringComparison.OrdinalIgnoreCase))
            {
                return listings.OrderBy(listing => listing.Price).ThenBy(listing => listing.Id).ToList();
            }

            if (string.Equals(sort, "PriceHigh", StringComparison.OrdinalIgnoreCase))
            {
                return listings.OrderByDescending(listing => listing.Price).ThenBy(listing => listing.Id).ToList();
            }

            if (string.Equals(sort, "Name", StringComparison.OrdinalIgnoreCase))
            {
                return listings.OrderBy(listing => listing.ItemName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(listing => listing.Id)
                    .ToList();
            }

            return listings.OrderByDescending(listing => listing.CreatedUtc).ThenBy(listing => listing.Id).ToList();
        }

        private List<Listing> GetPlayerListings(string playerId)
        {
            return data.Listings
                .Where(listing => listing != null &&
                                  string.Equals(listing.State, StateActive, StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(listing.SellerId, playerId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(listing => listing.CreatedUtc)
                .ThenBy(listing => listing.Id)
                .ToList();
        }

        private int CountActiveListings()
        {
            if (data == null || data.Listings == null)
            {
                return 0;
            }

            return data.Listings.Count(listing => listing != null &&
                                                  string.Equals(listing.State, StateActive, StringComparison.OrdinalIgnoreCase));
        }

        private void PruneHistory()
        {
            if (data == null || data.Listings == null)
            {
                return;
            }

            if (config.KeepHistoryEntries <= 0)
            {
                data.Listings.RemoveAll(listing => listing != null &&
                                                   !string.Equals(listing.State, StateActive, StringComparison.OrdinalIgnoreCase));
                return;
            }

            List<Listing> history = data.Listings
                .Where(listing => listing != null &&
                                  !string.Equals(listing.State, StateActive, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(listing => listing.SoldUtc > 0 ? listing.SoldUtc : listing.CreatedUtc)
                .ToList();

            if (history.Count <= config.KeepHistoryEntries)
            {
                return;
            }

            HashSet<long> remove = new HashSet<long>();
            for (int i = config.KeepHistoryEntries; i < history.Count; i++)
            {
                remove.Add(history[i].Id);
            }

            data.Listings.RemoveAll(listing => listing != null && remove.Contains(listing.Id));
        }

        private string TimeLeft(Listing listing)
        {
            long remaining = listing.ExpiresUtc - DateTime.UtcNow.Ticks;
            if (remaining <= 0)
            {
                return "expired";
            }

            TimeSpan span = TimeSpan.FromTicks(remaining);
            if (span.TotalDays >= 1)
            {
                return ((int)span.TotalDays) + "d " + span.Hours + "h";
            }

            if (span.TotalHours >= 1)
            {
                return ((int)span.TotalHours) + "h " + span.Minutes + "m";
            }

            return Math.Max(1, span.Minutes) + "m";
        }

        private string CurrencyName()
        {
            return string.IsNullOrEmpty(config.CurrencyDisplayName) ? config.CurrencyItem : config.CurrencyDisplayName;
        }

        private string CurrencyAmount(int amount)
        {
            return amount.ToString("N0", CultureInfo.InvariantCulture) + " " + CurrencyName();
        }

        private string FormatPrice(int amount)
        {
            return CurrencyAmount(amount);
        }

        #endregion

        #region Commands

        private void CommandAuction(IPlayer player, string command, string[] args)
        {
            if (player == null)
            {
                return;
            }

            string sub = args == null || args.Length == 0 ? string.Empty : args[0].ToLowerInvariant();

            switch (sub)
            {
                case "":
                    OpenDefault(player);
                    return;

                case "help":
                    ShowHelp(player);
                    return;

                case "browse":
                case "open":
                    OpenBrowse(player);
                    return;

                case "mine":
                case "listings":
                    OpenMine(player);
                    return;

                case "sell":
                    CommandSell(player, args);
                    return;

                case "buy":
                    CommandBuy(player, args);
                    return;

                case "cancel":
                    CommandCancel(player, args);
                    return;

                case "list":
                    CommandList(player, args);
                    return;

                case "fee":
                    CommandFee(player, args);
                    return;

                case "status":
                    CommandStatus(player);
                    return;

                case "reload":
                    CommandReload(player);
                    return;

                case "expire":
                    CommandExpire(player);
                    return;

                case "remove":
                    CommandRemove(player, args);
                    return;

                default:
                    ShowHelp(player);
                    return;
            }
        }

        private void OpenDefault(IPlayer player)
        {
            if (!CanUse(player.Id))
            {
                Reply(player, "You need the 'auctionhouse.use' permission.");
                return;
            }

            if (TryOpenGui(player, "a|open"))
            {
                return;
            }

            if (player.IsServer)
            {
                CommandList(player, null);
                return;
            }

            Reply(player, "Auction House GUI is unavailable. Use /auction browse, /auction sell <price>, or /auction help.");
        }

        private void OpenBrowse(IPlayer player)
        {
            if (TryOpenGui(player, "a|tab|home"))
            {
                return;
            }

            CommandList(player, null);
        }

        private void OpenMine(IPlayer player)
        {
            if (TryOpenGui(player, "a|tab|shared"))
            {
                return;
            }

            if (!CanSell(player.Id))
            {
                Reply(player, "You need the 'auctionhouse.sell' permission.");
                return;
            }

            List<Listing> listings = GetPlayerListings(player.Id);
            if (listings.Count == 0)
            {
                Reply(player, "You have no active listings.");
                return;
            }

            Reply(player, "Your active listings:");
            foreach (Listing listing in listings)
            {
                Reply(player, "  #" + listing.Id + " | " + listing.ItemName + " x" + listing.Count +
                              " | " + FormatPrice(listing.Price) + " | " + TimeLeft(listing));
            }
        }

        private void CommandSell(IPlayer player, string[] args)
        {
            if (player.IsServer)
            {
                Reply(player, "Only connected players can sell items.");
                return;
            }

            if (!CanSell(player.Id))
            {
                Reply(player, "You need the 'auctionhouse.sell' permission.");
                return;
            }

            if (args == null || args.Length < 2)
            {
                Reply(player, "Usage: /auction sell <price> [quantity]");
                return;
            }

            int price;
            if (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out price))
            {
                Reply(player, "The price must be a whole number.");
                return;
            }

            EntityPlayer entity = GetEntity(player);
            ItemStack held = GetHeldStack(entity);
            if (held == null || held.IsEmpty())
            {
                Reply(player, "Hold the item you want to sell in your selected toolbelt slot.");
                return;
            }

            int quantity = held.count;
            if (args.Length >= 3)
            {
                if (!int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out quantity))
                {
                    Reply(player, "The quantity must be a whole number.");
                    return;
                }
            }

            string error;
            if (!TryCreateListing(player, entity, held, quantity, price, out error))
            {
                Reply(player, error);
                return;
            }
        }

        private void CommandBuy(IPlayer player, string[] args)
        {
            if (!CanUse(player.Id))
            {
                Reply(player, "You need the 'auctionhouse.use' permission.");
                return;
            }

            long listingId;
            if (args == null || args.Length < 2 ||
                !long.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out listingId))
            {
                Reply(player, "Usage: /auction buy <listing id>");
                return;
            }

            string error;
            if (!TryBuy(player, listingId, out error))
            {
                Reply(player, error);
            }
        }

        private void CommandCancel(IPlayer player, string[] args)
        {
            if (!CanSell(player.Id))
            {
                Reply(player, "You need the 'auctionhouse.sell' permission.");
                return;
            }

            long listingId;
            if (args == null || args.Length < 2 ||
                !long.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out listingId))
            {
                Reply(player, "Usage: /auction cancel <listing id>");
                return;
            }

            string error;
            if (!TryCancel(player, listingId, false, out error))
            {
                Reply(player, error);
            }
        }

        private void CommandList(IPlayer player, string[] args)
        {
            if (!CanUse(player.Id))
            {
                Reply(player, "You need the 'auctionhouse.use' permission.");
                return;
            }

            List<Listing> listings = GetBrowseListings();
            if (listings.Count == 0)
            {
                Reply(player, "No active listings.");
                return;
            }

            int page = 0;
            if (args != null && args.Length >= 2)
            {
                int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out page);
            }

            int pages = PageCount(listings.Count);
            page = Math.Max(0, Math.Min(pages - 1, page - 1));
            int start = page * UiRows;

            Reply(player, "Auction House  |  page " + (page + 1) + "/" + pages + "  |  " + listings.Count + " lot(s)");
            for (int i = 0; i < UiRows && start + i < listings.Count; i++)
            {
                Listing listing = listings[start + i];
                Reply(player, "  #" + listing.Id + " | " + listing.ItemName +
                              (listing.Count > 1 ? " x" + listing.Count : string.Empty) +
                              " | " + FormatPrice(listing.Price) + " | " + listing.SellerName +
                              " | " + TimeLeft(listing));
            }
        }

        private void CommandFee(IPlayer player, string[] args)
        {
            if (!CanSell(player.Id))
            {
                Reply(player, "You need the 'auctionhouse.sell' permission.");
                return;
            }

            int price = 0;
            if (args != null && args.Length >= 2)
            {
                int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out price);
            }

            EntityPlayer entity = GetEntity(player);
            ItemStack held = GetHeldStack(entity);
            ItemValue itemValue = held != null && !held.IsEmpty() ? held.itemValue : null;

            Reply(player, "Listing fee: " + FormatPrice(FeeFor(itemValue, price)) +
                          (price > 0 ? " for a " + FormatPrice(price) + " listing." : "."));
        }

        private void CommandReload(IPlayer player)
        {
            if (!CanAdmin(player.Id))
            {
                Reply(player, "You need the 'auctionhouse.admin' permission.");
                return;
            }

            LoadConfig();
            LoadData();
            ResolveCurrency();
            StartExpireTimer();
            TryRegisterServerAssist();

            Reply(player, "Reloaded Auction House config and data. Currency valid: " + currencyValid + ".");
        }

        private void CommandStatus(IPlayer player)
        {
            if (!CanAdmin(player.Id))
            {
                Reply(player, "You need the 'auctionhouse.admin' permission.");
                return;
            }

            Reply(player, "Auction House status:");
            Reply(player, "  ServerAssist provider: " +
                          (!serverAssistGuiReady
                              ? "unavailable"
                              : (serverAssistPanelFallback
                                  ? "registered (home panel fallback)"
                                  : "registered (provider)")));
            Reply(player, "  Currency item: " + config.CurrencyItem + " (" +
                          (currencyValid ? "loaded" : "not found") + ")");
            Reply(player, "  Active listings: " + CountActiveListings() + " / " +
                          (config.MaxActiveListings > 0 ? config.MaxActiveListings.ToString(CultureInfo.InvariantCulture) : "unlimited"));
            Reply(player, "  Listing fee: " + FeeRuleDescription(currencyValue));
            Reply(player, "  Expiry check: every " + config.ExpireCheckSeconds + " second(s)");
        }

        private void CommandExpire(IPlayer player)
        {
            if (!CanAdmin(player.Id))
            {
                Reply(player, "You need the 'auctionhouse.admin' permission.");
                return;
            }

            ProcessExpiredListings();
            Reply(player, "Expiry check complete.");
        }

        private void CommandRemove(IPlayer player, string[] args)
        {
            if (!CanAdmin(player.Id))
            {
                Reply(player, "You need the 'auctionhouse.admin' permission.");
                return;
            }

            long listingId;
            if (args == null || args.Length < 2 ||
                !long.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out listingId))
            {
                Reply(player, "Usage: /auction remove <listing id>");
                return;
            }

            string error;
            if (!TryAdminRemove(player, listingId, out error))
            {
                Reply(player, error);
            }
        }

        private bool TryAdminRemove(IPlayer admin, long listingId, out string error)
        {
            error = null;

            Listing listing = FindListing(listingId);
            if (listing == null)
            {
                error = "That listing was not found.";
                return false;
            }

            if (string.Equals(listing.State, StateActive, StringComparison.OrdinalIgnoreCase))
            {
                return TryCancel(admin, listingId, true, out error);
            }

            data.Listings.Remove(listing);
            SaveData();
            Reply(admin, "Listing record #" + listing.Id + " was removed.");
            return true;
        }

        private void ShowHelp(IPlayer player)
        {
            Reply(player, "Auction House commands:");
            Reply(player, "  /auction  - open the Auction House");
            Reply(player, "  /auction browse [page]  - browse active listings");
            Reply(player, "  /auction sell <price> [quantity]  - list the held item");
            Reply(player, "  /auction mine  - show your active listings");
            Reply(player, "  /auction buy <id>  - buy a listing by id");
            Reply(player, "  /auction cancel <id>  - cancel your listing");
            Reply(player, "  /auction fee [price]  - show the listing fee");
            Reply(player, "  /auction list [page]  - list active listings in chat");
            Reply(player, "  /auction help  - show this help");
            if (CanAdmin(player.Id))
            {
                Reply(player, "  /auction status  - show provider, currency and listing status");
                Reply(player, "  /auction reload  - reload config and data");
                Reply(player, "  /auction expire  - run an expiry check");
                Reply(player, "  /auction remove <id>  - admin-remove a listing");
            }
        }

        #endregion

        #region Permissions and messages

        private bool CanUse(string playerId)
        {
            return !string.IsNullOrEmpty(playerId) &&
                   (playerId.Equals("server_console", StringComparison.OrdinalIgnoreCase) ||
                    permission.UserHasPermission(playerId, PermUse) ||
                    permission.UserHasPermission(playerId, PermAdmin));
        }

        private bool CanSell(string playerId)
        {
            return !string.IsNullOrEmpty(playerId) &&
                   (playerId.Equals("server_console", StringComparison.OrdinalIgnoreCase) ||
                    permission.UserHasPermission(playerId, PermSell) ||
                    permission.UserHasPermission(playerId, PermAdmin));
        }

        private bool CanAdmin(string playerId)
        {
            return !string.IsNullOrEmpty(playerId) &&
                   (playerId.Equals("server_console", StringComparison.OrdinalIgnoreCase) ||
                    permission.UserHasPermission(playerId, PermAdmin));
        }

        private void Reply(IPlayer player, string message)
        {
            if (player == null)
            {
                Interface.Oxide.LogInfo("[AuctionHouse] " + message);
                return;
            }

            player.Reply(string.IsNullOrEmpty(config.ChatPrefix) ? message : config.ChatPrefix + " " + message);
        }

        #endregion

        #region Data classes

        private sealed class GuiSession
        {
            public string Tab = "home";
            public int Page;
            public long SelectedId;
            public int DraftPrice;
            public int DraftQuantity;
            public string InputAction;
            public string InputLabel = string.Empty;
            public string InputValue = string.Empty;
            public string Status = string.Empty;
        }

        private sealed class RowInfo
        {
            public string Action = string.Empty;
            public string Label = string.Empty;
            public string Sub = string.Empty;
            public string Icon = IconAuction;
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

        private sealed class Configuration
        {
            public string ChatPrefix = "[Auction]";

            // Listing fee. "Flat" charges ListingFee, "Percent" charges
            // ListingFeePercent of the price, and "FlatAndPercent" charges both.
            public string ListingFeeMode = "Flat";
            public int ListingFee = 100;
            public double ListingFeePercent = 2.5;
            public Dictionary<string, int> ListingFeesByItem = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            public int MinimumPrice = 1;
            public int MaximumPrice = 100000000;
            public int ListingDurationHours = 24;
            public int MaxListingsPerPlayer = 10;
            public int MaxActiveListings = 500;
            public string BrowseSort = "Newest";

            public string CurrencyItem = "casinoCoin";
            public string CurrencyDisplayName = "Casino Coins";
            public double SalesTaxPercent = 5.0;

            public int ExpireCheckSeconds = 60;
            public bool RefundExpiredItems = true;
            public bool RefundFeeOnCancel = false;
            public bool AllowBuyOwnListings = false;
            public bool AllowListingCurrency = false;
            public List<string> BlockedItems = new List<string>();

            public bool NotifyOnSale = true;
            public bool NotifyOnExpire = true;
            public bool BroadcastSales = false;

            public bool GrantUseToDefaultGroup = true;
            public bool GrantSellToDefaultGroup = true;

            public int KeepHistoryEntries = 200;
        }

        private sealed class Listing
        {
            public long Id;
            public string SellerId = string.Empty;
            public string SellerName = string.Empty;
            public string ItemData = string.Empty;
            public string ItemName = string.Empty;
            public string ItemShortName = string.Empty;
            public int Count;
            public int Price;
            public int FeePaid;
            public long CreatedUtc;
            public long ExpiresUtc;
            public string State = StateActive;
            public string BuyerId = string.Empty;
            public string BuyerName = string.Empty;
            public long SoldUtc;
            public int TaxPaid;
            public int SellerPaid;
        }

        private sealed class PendingItem
        {
            public string ItemData = string.Empty;
            public int Count;
            public string ItemName = string.Empty;
            public string Reason = string.Empty;
            public long CreatedUtc;
        }

        private sealed class PendingDelivery
        {
            public int Currency;
            public List<PendingItem> Items = new List<PendingItem>();
            public List<string> Messages = new List<string>();
        }

        private sealed class StoredData
        {
            public int Version = 1;
            public long NextId = 1;
            public List<Listing> Listings = new List<Listing>();
            public Dictionary<string, PendingDelivery> Pending =
                new Dictionary<string, PendingDelivery>(StringComparer.OrdinalIgnoreCase);
        }

        #endregion
    }
}

