using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("DynamicBackpack", "TheRealM18", "2.2.0")]
    [Description("Permission-based virtual backpack pages rendered by Server Assist. Each page holds ten items; MaxPages and backpack.N permissions control how many pages are available.")]
    public class DynamicBackpack : CovalencePlugin
    {
        private const int Columns = 10;
        private const int MaxPagesLimit = 1000;
        private const int MaxStoredSlots = Columns * MaxPagesLimit;
        private const string PanelId = "backpack";
        private const string ServerAssistIcon = "sprite:sa_dynamic_backpack";

        private Configuration config;
        private readonly Dictionary<string, Bag> bags =
            new Dictionary<string, Bag>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> pagesCache =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> currentPage =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> searchQuery =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> searchPage =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> inputMode =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> dirty =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> overflowDropped =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private Timer saveTimer;
        private Timer rowTimer;
        private bool ready;

        #region Lifecycle

        private void Init()
        {
            LoadConfig();
            RegisterPermissions();

            AddCovalenceCommand(new[] { "backpack", "dbp" }, nameof(CommandBackpack));

            saveTimer = timer.Repeat(Math.Max(5f, config.SaveIntervalSeconds), -1, FlushDirty);
            rowTimer = timer.Repeat(30f, -1, RecheckRows);

            timer.Once(2f, EnsureDefaultPermissions);
            timer.Once(2f, RegisterServerAssistPanel);
            timer.Once(10f, RegisterServerAssistPanel);
            ready = true;

            Interface.Oxide.LogInfo(
                "[DynamicBackpack] loaded. Pages: " + config.PermissionPrefix + ".1 - " +
                config.PermissionPrefix + "." + config.MaxPages +
                ". UI is provided by Server Assist.");
        }

        private void OnServerInitialized()
        {
            EnsureDefaultPermissions();
            RegisterServerAssistPanel();
        }

        private void OnServerSave()
        {
            FlushDirty();
        }

        private void OnServerShutdown()
        {
            FlushDirty();
        }

        private void OnServerAssistReady()
        {
            RegisterServerAssistPanel();
        }

        private void Unload()
        {
            ready = false;
            FlushDirty();

            if (saveTimer != null)
            {
                saveTimer.Destroy();
                saveTimer = null;
            }

            if (rowTimer != null)
            {
                rowTimer.Destroy();
                rowTimer = null;
            }

            try
            {
                Plugin serverAssist = plugins.Find("ServerAssist");
                if (serverAssist != null)
                {
                    serverAssist.Call("UnregisterPanel", PanelId);
                }
            }
            catch
            {
                // Server Assist may already be unloading.
            }

            bags.Clear();
            pagesCache.Clear();
            currentPage.Clear();
            searchQuery.Clear();
            searchPage.Clear();
            inputMode.Clear();
            dirty.Clear();
            overflowDropped.Clear();
        }

        private void OnUserConnected(IPlayer player)
        {
            if (player == null || string.IsNullOrEmpty(player.Id))
            {
                return;
            }

            EnsureBag(player.Id);
            pagesCache[player.Id] = GetPages(player.Id);
        }

        private void OnUserDisconnected(IPlayer player)
        {
            if (player == null || string.IsNullOrEmpty(player.Id))
            {
                return;
            }

            SavePlayer(player.Id);
            bags.Remove(player.Id);
            pagesCache.Remove(player.Id);
            currentPage.Remove(player.Id);
            searchQuery.Remove(player.Id);
            searchPage.Remove(player.Id);
            inputMode.Remove(player.Id);
            dirty.Remove(player.Id);
            overflowDropped.Remove(player.Id);
        }

        private void OnUserPermissionGranted(string playerId, string permission)
        {
            if (IsBackpackPermission(permission))
            {
                RefreshRows(playerId);
            }
        }

        private void OnUserPermissionRevoked(string playerId, string permission)
        {
            if (IsBackpackPermission(permission))
            {
                RefreshRows(playerId);
            }
        }

        private void OnGroupPermissionGranted(string groupName, string permission)
        {
            if (IsBackpackPermission(permission))
            {
                RecheckRows();
            }
        }

        private void OnGroupPermissionRevoked(string groupName, string permission)
        {
            if (IsBackpackPermission(permission))
            {
                RecheckRows();
            }
        }

        private void OnUserGroupAdded(string playerId, string groupName)
        {
            RefreshRows(playerId);
        }

        private void OnUserGroupRemoved(string playerId, string groupName)
        {
            RefreshRows(playerId);
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
                Interface.Oxide.LogWarning(
                    "[DynamicBackpack] config could not be read: " + exception.Message);
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
            if (string.IsNullOrEmpty(config.ChatPrefix))
            {
                config.ChatPrefix = "[Backpack]";
            }

            config.PermissionPrefix = (config.PermissionPrefix ?? "backpack").Trim().TrimEnd('.');
            if (string.IsNullOrEmpty(config.PermissionPrefix))
            {
                config.PermissionPrefix = "backpack";
            }

            config.MaxPages = Clamp(config.MaxPages, 1, MaxPagesLimit);
            config.DefaultPages = Clamp(config.DefaultPages, 0, config.MaxPages);
            config.AdminPages = Clamp(config.AdminPages, 0, config.MaxPages);
            config.SaveIntervalSeconds = Clamp(config.SaveIntervalSeconds, 5, 600);

            if (string.IsNullOrEmpty(config.AdminPermission))
            {
                config.AdminPermission = config.PermissionPrefix + ".admin";
            }
            else
            {
                config.AdminPermission = config.AdminPermission.Trim();
                if (string.IsNullOrEmpty(config.AdminPermission))
                {
                    config.AdminPermission = config.PermissionPrefix + ".admin";
                }
            }
        }

        private void RegisterPermissions()
        {
            for (int i = 1; i <= config.MaxPages; i++)
            {
                permission.RegisterPermission(config.PermissionPrefix + "." + i, this);
            }

            permission.RegisterPermission(config.PermissionPrefix + ".*", this);

            if (!string.IsNullOrEmpty(config.AdminPermission))
            {
                permission.RegisterPermission(config.AdminPermission, this);
            }
        }

        private void EnsureDefaultPermissions()
        {
            string adminGroup = Interface.Oxide.Config.Options.DefaultGroups.Administrators;
            string playerGroup = Interface.Oxide.Config.Options.DefaultGroups.Players;

            if (config.GrantAdminToAdministrators && !string.IsNullOrEmpty(adminGroup))
            {
                GrantGroupIfMissing(adminGroup, config.AdminPermission);
                if (config.AdminPages > 0)
                {
                    GrantGroupIfMissing(adminGroup, config.PermissionPrefix + "." + config.AdminPages);
                }
            }

            if (config.GrantFirstPageToPlayers && !string.IsNullOrEmpty(playerGroup))
            {
                GrantGroupIfMissing(playerGroup, config.PermissionPrefix + ".1");
            }
        }

        private void GrantGroupIfMissing(string group, string permissionName)
        {
            if (string.IsNullOrEmpty(group) || string.IsNullOrEmpty(permissionName) ||
                !permission.GroupExists(group))
            {
                return;
            }

            if (!permission.GroupHasPermission(group, permissionName))
            {
                permission.GrantGroupPermission(group, permissionName, this);
            }
        }

        #endregion

        #region Server Assist integration

        private void RegisterServerAssistPanel()
        {
            Plugin serverAssist = plugins.Find("ServerAssist");
            if (serverAssist == null)
            {
                return;
            }

            try
            {
                bool registered = false;

                // Server Assist 2.x: RegisterPanel carries the card icon.
                try
                {
                    object panelResult = serverAssist.Call(
                        "RegisterPanel",
                        PanelId,
                        "Backpack",
                        "Permission-based virtual storage pages.",
                        config.PermissionPrefix + ".1",
                        "DynamicBackpack",
                        nameof(HandleBackpackGui),
                        ServerAssistIcon,
                        30);

                    registered = panelResult is bool && (bool)panelResult;
                }
                catch
                {
                    // Older Server Assist builds do not expose RegisterPanel.
                }

                // Older Server Assist builds only expose RegisterGuiHook.
                // Register the hook first, then apply the same icon.
                if (!registered)
                {
                    try
                    {
                        object hookResult = serverAssist.Call(
                            "RegisterGuiHook",
                            PanelId,
                            "Backpack",
                            "Permission-based virtual storage pages.",
                            config.PermissionPrefix + ".1",
                            "DynamicBackpack",
                            nameof(HandleBackpackGui));

                        registered = hookResult is bool && (bool)hookResult;
                    }
                    catch
                    {
                        registered = false;
                    }
                }

                // Apply the icon explicitly too. This covers a Server Assist
                // build whose RegisterPanel accepted the panel but ignored or
                // overwrote the icon argument.
                if (registered)
                {
                    bool iconApplied = false;
                    try
                    {
                        object iconResult = serverAssist.Call(
                            "SetPanelIcon", PanelId, ServerAssistIcon);
                        iconApplied = iconResult is bool && (bool)iconResult;
                    }
                    catch
                    {
                        // RegisterPanel already applied the icon on 2.x builds.
                    }

                    if (!iconApplied)
                    {
                        Interface.Oxide.LogWarning(
                            "[DynamicBackpack] could not apply Server Assist icon " +
                            ServerAssistIcon + ". Check the ServerAssist version and " +
                            "the ServerAssistArt UI atlas on clients.");
                    }
                }

                Interface.Oxide.LogInfo(
                    registered
                        ? "[DynamicBackpack] registered the backpack panel with Server Assist (" +
                          ServerAssistIcon + ")."
                        : "[DynamicBackpack] Server Assist did not accept the backpack panel.");
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning(
                    "[DynamicBackpack] could not register the Server Assist panel: " +
                    exception.Message);
            }
        }

        /// <summary>
        /// Server Assist hook entry point. Every row card and action button in
        /// the backpack page lands here with a payload such as:
        ///   a|open
        ///   a|page|3
        ///   a|withdraw|21
        ///   a|deposit
        ///   a|refresh
        /// Returns the Server Assist line protocol.
        /// </summary>
        public string HandleBackpackGui(string playerId, string payload)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return RenderError("Could not determine your player id.");
            }

            if (!ready)
            {
                return RenderError("The backpack is still loading. Try again in a moment.");
            }

            Bag bag = EnsureBag(playerId);
            int pages = GetPages(playerId);
            EnsureCapacity(playerId, bag, pages);

            string status = string.Empty;
            string request = payload ?? string.Empty;

            if (request.StartsWith("t|", StringComparison.Ordinal))
            {
                status = HandleTextInput(playerId, Decode(request.Substring(2)));
            }
            else
            {
                string action = request;
                if (action.StartsWith("a|", StringComparison.OrdinalIgnoreCase))
                {
                    action = action.Substring(2);
                }

                string[] parts = action.Split('|');
                string verb = parts.Length > 0 ? parts[0].Trim().ToLowerInvariant() : string.Empty;

                switch (verb)
                {
                    case "":
                    case "open":
                    case "refresh":
                        if (verb == "open")
                        {
                            inputMode.Remove(playerId);
                            ClearSearch(playerId);
                        }
                        break;

                    case "page":
                        status = HandlePageAction(
                            playerId, parts.Length > 1 ? parts[1] : string.Empty, pages);
                        break;

                    case "prompt":
                        if (parts.Length > 1)
                        {
                            string mode = parts[1].Trim().ToLowerInvariant();
                            if (mode == "page" || mode == "search")
                            {
                                inputMode[playerId] = mode;
                            }
                        }
                        break;

                    case "search":
                        if (parts.Length > 1 &&
                            parts[1].Equals("clear", StringComparison.OrdinalIgnoreCase))
                        {
                            inputMode.Remove(playerId);
                            ClearSearch(playerId);
                            status = "Search cleared.";
                        }
                        break;

                    case "withdraw":
                        if (parts.Length > 1)
                        {
                            int slot;
                            if (int.TryParse(parts[1], out slot))
                            {
                                WithdrawStack(playerId, bag, slot, out status);
                            }
                            else
                            {
                                status = "That backpack slot is not valid.";
                            }
                        }
                        break;

                    case "deposit":
                        DepositHeld(playerId, bag, GetCurrentPage(playerId, pages), out status);
                        break;

                    case "cancel":
                        inputMode.Remove(playerId);
                        status = "Input cancelled.";
                        break;

                    case "nop":
                        status = "That card is not available.";
                        break;

                    case "back":
                        // The toolbar Back button uses hook|back and is handled by
                        // Server Assist. This is only a safety net.
                        status = string.Empty;
                        break;

                    default:
                        status = "Unknown backpack action.";
                        break;
                }
            }

            return RenderBackpackView(playerId, bag, pages, status);
        }

        private string HandlePageAction(string playerId, string token, int pages)
        {
            token = (token ?? string.Empty).Trim();

            int requested;
            if (int.TryParse(token, out requested))
            {
                ClearSearch(playerId);
                int maxBackpackPage = pages > 0 ? Math.Min(config.MaxPages, pages) : 1;
                int page = Clamp(requested, 1, maxBackpackPage);
                currentPage[playerId] = page;
                return "Showing page " + page + ".";
            }

            string key = token.ToLowerInvariant();
            if (key != "prev" && key != "next" && key != "first" &&
                key != "last" && key != "-10" && key != "+10")
            {
                return "Unknown page action.";
            }

            string query = GetSearchQuery(playerId);
            bool searching = !string.IsNullOrEmpty(query);
            int current;
            int max;

            if (searching)
            {
                Bag bag = EnsureBag(playerId);
                List<int> matches = FindMatchingSlots(
                    bag, Clamp(pages, 0, config.MaxPages) * Columns, query);
                max = Math.Max(1, (matches.Count + Columns - 1) / Columns);
                current = Clamp(GetSearchPage(playerId), 1, max);
            }
            else
            {
                max = pages > 0 ? Math.Min(config.MaxPages, pages) : 1;
                current = GetCurrentPage(playerId, pages);
            }

            switch (key)
            {
                case "prev":
                    current--;
                    break;
                case "next":
                    current++;
                    break;
                case "first":
                    current = 1;
                    break;
                case "last":
                    current = max;
                    break;
                case "-10":
                    current -= 10;
                    break;
                case "+10":
                    current += 10;
                    break;
            }

            current = Clamp(current, 1, max);
            if (searching)
            {
                searchPage[playerId] = current;
            }
            else
            {
                currentPage[playerId] = current;
            }

            return string.Empty;
        }

        private string HandleTextInput(string playerId, string text)
        {
            string mode;
            if (!inputMode.TryGetValue(playerId, out mode) || string.IsNullOrEmpty(mode))
            {
                return "Nothing is waiting for text input.";
            }

            text = (text ?? string.Empty).Trim();
            int pages = GetPages(playerId);

            if (mode == "page")
            {
                int requested;
                if (!int.TryParse(text, out requested))
                {
                    return "Enter a page number.";
                }

                int max = pages > 0 ? Math.Min(config.MaxPages, pages) : 1;
                if (requested < 1 || requested > max)
                {
                    return "Page must be between 1 and " + max + ".";
                }

                inputMode.Remove(playerId);
                ClearSearch(playerId);
                currentPage[playerId] = requested;
                return "Showing page " + requested + ".";
            }

            if (mode == "search")
            {
                if (string.IsNullOrEmpty(text))
                {
                    inputMode.Remove(playerId);
                    ClearSearch(playerId);
                    return "Search cleared.";
                }

                if (text.Length > 60)
                {
                    text = text.Substring(0, 60);
                }

                searchQuery[playerId] = text;
                searchPage[playerId] = 1;
                inputMode[playerId] = "search";
                return "Searching for \"" + text + "\".";
            }

            inputMode.Remove(playerId);
            return "Unknown input.";
        }

        private string RenderBackpackView(string playerId, Bag bag, int pages, string status)
        {
            int unlockedSlots = Clamp(pages, 0, config.MaxPages) * Columns;
            string query = GetSearchQuery(playerId);
            bool searching = !string.IsNullOrEmpty(query);
            List<int> matches = searching
                ? FindMatchingSlots(bag, unlockedSlots, query)
                : null;

            int maxPage;
            int page;
            if (searching)
            {
                maxPage = Math.Max(1, (matches.Count + Columns - 1) / Columns);
                page = Clamp(GetSearchPage(playerId), 1, maxPage);
                searchPage[playerId] = page;
            }
            else
            {
                maxPage = pages > 0 ? Math.Min(config.MaxPages, pages) : 1;
                page = GetCurrentPage(playerId, pages);
            }

            int used = CountUsed(bag, unlockedSlots);

            StringBuilder builder = new StringBuilder(4096);
            builder.Append("v2\n");
            builder.Append("protocol=2\n");
            builder.Append("title=").Append(Encode(searching ? "Backpack Search" : "Backpack")).Append('\n');
            builder.Append("subtitle=").Append(Encode(
                BuildSubtitle(pages, page, maxPage, used, unlockedSlots, searching, query,
                    matches != null ? matches.Count : 0))).Append('\n');
            builder.Append("status=").Append(Encode(status ?? string.Empty)).Append('\n');
            AppendInput(builder, playerId, pages);

            if (searching)
            {
                AppendSearchRows(builder, bag, matches, page);
            }
            else
            {
                AppendPageRows(builder, bag, pages, page, unlockedSlots);
            }

            AppendButtons(builder, pages, page, maxPage, searching);
            return builder.ToString();
        }

        private void AppendInput(StringBuilder builder, string playerId, int pages)
        {
            string mode;
            if (!inputMode.TryGetValue(playerId, out mode) || string.IsNullOrEmpty(mode))
            {
                builder.Append("input=||0|\n");
                return;
            }

            if (mode == "page")
            {
                int max = pages > 0 ? Math.Min(config.MaxPages, pages) : 1;
                int page = GetCurrentPage(playerId, pages);
                builder.Append("input=")
                    .Append(Encode("Backpack page (1-" + max + ")"))
                    .Append("|1|")
                    .Append(Encode(page.ToString()))
                    .Append('\n');
                return;
            }

            if (mode == "search")
            {
                builder.Append("input=")
                    .Append(Encode("Search items (blank clears)"))
                    .Append("|1|")
                    .Append(Encode(GetSearchQuery(playerId)))
                    .Append('\n');
                return;
            }

            builder.Append("input=||0|\n");
        }

        private void AppendPageRows(
            StringBuilder builder, Bag bag, int pages, int page, int unlockedSlots)
        {
            int baseSlot = (page - 1) * Columns;

            for (int card = 0; card < Columns; card++)
            {
                int slot = baseSlot + card;
                bool unlocked = pages > 0 && slot < unlockedSlots;
                ItemStack stack = slot >= 0 && slot < bag.SlotCount ? bag.ItemGrid[slot] : null;
                bool hasItem = unlocked && stack != null && !stack.IsEmpty();

                if (!unlocked)
                {
                    AppendRow(builder, card, "nop", "Locked",
                        "Requires " + config.PermissionPrefix + "." + Math.Max(1, page),
                        false, "sprite:ui_game_symbol_backpack", "LOCKED");
                }
                else if (!hasItem)
                {
                    AppendRow(builder, card, "nop", "Empty slot",
                        "Page " + page + "  |  Slot " + (card + 1),
                        false, "sprite:ui_game_symbol_backpack", "EMPTY");
                }
                else
                {
                    AppendItemRow(builder, card, slot, stack, "x" + stack.count, true);
                }
            }
        }

        private void AppendSearchRows(
            StringBuilder builder, Bag bag, List<int> matches, int page)
        {
            if (matches == null || matches.Count == 0)
            {
                AppendRow(builder, 0, "nop", "No matching items",
                    "Try a different search", false,
                    "sprite:ui_game_symbol_backpack", "NONE");
                return;
            }

            int start = (page - 1) * Columns;
            for (int card = 0; card < Columns; card++)
            {
                int index = start + card;
                if (index >= matches.Count)
                {
                    break;
                }

                int slot = matches[index];
                ItemStack stack = slot >= 0 && slot < bag.SlotCount ? bag.ItemGrid[slot] : null;
                if (stack == null || stack.IsEmpty())
                {
                    continue;
                }

                string sub = "x" + stack.count +
                             "  |  Page " + (slot / Columns + 1) +
                             "  |  Slot " + (slot % Columns + 1);
                AppendItemRow(builder, card, slot, stack, sub, true);
            }
        }

        private void AppendItemRow(
            StringBuilder builder, int card, int slot, ItemStack stack,
            string sub, bool enabled)
        {
            ItemClass itemClass = stack.itemValue.ItemClass;
            string itemName = itemClass != null ? itemClass.GetItemName() : null;
            string localized = itemClass != null ? itemClass.GetLocalizedItemName() : null;

            string label = string.IsNullOrEmpty(localized)
                ? (string.IsNullOrEmpty(itemName) ? "Unknown item" : itemName)
                : localized;

            if (stack.itemValue.HasQuality && stack.itemValue.Quality > 0)
            {
                sub += "  |  Quality " + stack.itemValue.Quality;
            }

            string icon = string.IsNullOrEmpty(itemName)
                ? "sprite:ui_game_symbol_backpack"
                : "item:" + itemName;

            AppendRow(builder, card, "withdraw|" + slot, label, sub, enabled, icon,
                "x" + stack.count);
        }

        private static void AppendRow(
            StringBuilder builder, int index, string action, string label,
            string sub, bool enabled, string icon, string badge)
        {
            builder.Append("row=").Append(index).Append('|')
                .Append(Encode(action)).Append('|')
                .Append(Encode(label)).Append('|')
                .Append(Encode(sub)).Append('|')
                .Append(enabled ? "1" : "0").Append('|')
                .Append("0").Append('|')
                .Append(Encode(icon)).Append('|')
                .Append(Encode(badge)).Append('|')
                .Append(Encode(string.Empty)).Append('\n');
        }

        private void AppendButtons(
            StringBuilder builder, int pages, int page, int maxPage, bool searching)
        {
            int index = 0;
            AppendButton(builder, index++, "page|prev", "Prev", page > 1,
                searching ? "Previous search results page" : "Previous backpack page");
            AppendButton(builder, index++, "page|next", "Next", page < maxPage,
                searching ? "Next search results page" : "Next backpack page");
            AppendButton(builder, index++, "page|first", "First", page > 1,
                searching ? "First search results page" : "First backpack page");
            AppendButton(builder, index++, "page|last", "Last", page < maxPage,
                searching ? "Last search results page" : "Last backpack page");
            AppendButton(builder, index++, "page|-10", "-10", page > 1,
                searching ? "Back 10 result pages" : "Back 10 backpack pages");
            AppendButton(builder, index++, "page|+10", "+10", page < maxPage,
                searching ? "Forward 10 result pages" : "Forward 10 backpack pages");
            AppendButton(builder, index++, "prompt|page", "Go To Page", pages > 0,
                "Enter a backpack page number");
            AppendButton(builder, index++, "prompt|search", "Search", pages > 0,
                "Search every unlocked page for an item");
            AppendButton(builder, index++, "search|clear", "Clear Search", searching,
                "Clear the current item search");
            AppendButton(builder, index++, "deposit", "Deposit Held", pages > 0,
                "Store the item you are holding in the current page");
            AppendButton(builder, index++, "refresh", "Refresh", true,
                "Reload the page view");
        }

        private static void AppendButton(
            StringBuilder builder, int index, string action, string label,
            bool enabled, string tooltip)
        {
            builder.Append("btn=").Append(index).Append('|')
                .Append(Encode(action)).Append('|')
                .Append(Encode(label)).Append('|')
                .Append(enabled ? "1" : "0").Append('|')
                .Append(Encode(tooltip)).Append('\n');
        }

        private string RenderError(string message)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("v2\n");
            builder.Append("protocol=2\n");
            builder.Append("title=").Append(Encode("Backpack")).Append('\n');
            builder.Append("subtitle=").Append(Encode("Backpack unavailable")).Append('\n');
            builder.Append("status=").Append(Encode(message)).Append('\n');
            builder.Append("input=||0|\n");
            AppendRow(builder, 0, "nop", "Backpack", message, false,
                "sprite:ui_game_symbol_backpack", "ERROR");
            return builder.ToString();
        }

        private string BuildSubtitle(
            int pages, int page, int maxPage, int used, int unlockedSlots,
            bool searching, string query, int matchCount)
        {
            if (searching)
            {
                return "Search: " + query + "  |  " + matchCount +
                       " matching stack(s)  |  Result page " + page + " of " + maxPage;
            }

            if (pages <= 0)
            {
                return "No pages unlocked  |  Ask an admin for " +
                       config.PermissionPrefix + ".1";
            }

            return "Page " + page + " of " + maxPage +
                   "  |  " + unlockedSlots + " slots unlocked" +
                   "  |  " + used + " stack(s) stored";
        }

        private int GetCurrentPage(string playerId, int pages)
        {
            int page;
            if (!currentPage.TryGetValue(playerId, out page))
            {
                page = 1;
            }

            int maxPage = pages > 0 ? Math.Min(config.MaxPages, pages) : 1;
            page = Clamp(page, 1, maxPage);
            currentPage[playerId] = page;
            return page;
        }

        private void SetCurrentPage(string playerId, int page)
        {
            if (!string.IsNullOrEmpty(playerId))
            {
                currentPage[playerId] = page;
            }
        }

        private string GetSearchQuery(string playerId)
        {
            string query;
            return playerId != null && searchQuery.TryGetValue(playerId, out query)
                ? (query ?? string.Empty)
                : string.Empty;
        }

        private int GetSearchPage(string playerId)
        {
            int page;
            return playerId != null && searchPage.TryGetValue(playerId, out page)
                ? page
                : 1;
        }

        private void ClearSearch(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return;
            }

            searchQuery.Remove(playerId);
            searchPage.Remove(playerId);
        }

        private static List<int> FindMatchingSlots(Bag bag, int allowedSlots, string query)
        {
            List<int> matches = new List<int>();
            if (bag == null || string.IsNullOrEmpty(query))
            {
                return matches;
            }

            int limit = Math.Min(allowedSlots, bag.SlotCount);
            for (int i = 0; i < limit; i++)
            {
                ItemStack stack = bag.ItemGrid[i];
                if (stack == null || stack.IsEmpty())
                {
                    continue;
                }

                ItemClass itemClass = stack.itemValue.ItemClass;
                if (itemClass == null)
                {
                    continue;
                }

                string localized = itemClass.GetLocalizedItemName();
                string internalName = itemClass.GetItemName();

                if ((!string.IsNullOrEmpty(localized) &&
                     localized.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(internalName) &&
                     internalName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    matches.Add(i);
                }
            }

            return matches;
        }

        #endregion

        #region Item movement

        private bool WithdrawStack(string playerId, Bag bag, int slot, out string status)
        {
            int rows = GetPages(playerId);
            int unlockedSlots = Clamp(rows, 0, config.MaxPages) * Columns;

            if (bag == null || slot < 0 || slot >= bag.SlotCount)
            {
                status = "That backpack slot is not valid.";
                return false;
            }

            if (slot >= unlockedSlots)
            {
                status = "That backpack page is locked.";
                return false;
            }

            ItemStack stack = bag.ItemGrid[slot];
            if (stack == null || stack.IsEmpty())
            {
                status = "That backpack slot is empty.";
                return false;
            }

            EntityPlayer entity = GetEntity(playerId);
            if (entity == null)
            {
                status = "You must be in game to withdraw items.";
                return false;
            }

            ItemStack copy = stack.Clone();
            string itemName = GetDisplayName(copy.itemValue);

            if (entity.inventory.AddItem(copy))
            {
                bag.ItemGrid[slot].Clear();
                dirty.Add(playerId);
                status = "Withdrew x" + copy.count + " " + itemName + ".";
                return true;
            }

            if (DropAtFeet(entity, copy))
            {
                bag.ItemGrid[slot].Clear();
                dirty.Add(playerId);
                status = "Inventory full. Dropped x" + copy.count + " " + itemName + " at your feet.";
                return true;
            }

            status = "Your inventory is full and the item could not be dropped.";
            return false;
        }

        private bool DepositHeld(string playerId, Bag bag, int page, out string status)
        {
            int rows = GetPages(playerId);
            int allowed = Clamp(rows, 0, config.MaxPages) * Columns;
            if (allowed <= 0)
            {
                status = "No backpack pages are unlocked.";
                return false;
            }

            EntityPlayer entity = GetEntity(playerId);
            if (entity == null)
            {
                status = "You must be in game to deposit items.";
                return false;
            }

            ItemStack held = entity.inventory.holdingItemStack;
            if (held == null || held.IsEmpty())
            {
                status = "You are not holding an item.";
                return false;
            }

            ItemStack copy = held.Clone();
            string itemName = GetDisplayName(copy.itemValue);
            List<int> order = BuildSlotOrder(page, allowed);

            int remaining;
            int stored = StoreInBag(bag, order, copy, out remaining);
            if (stored <= 0)
            {
                status = "There is no room for " + itemName + " in your unlocked pages.";
                return false;
            }

            entity.inventory.DecHoldingItem(stored);
            dirty.Add(playerId);

            status = remaining > 0
                ? "Stored x" + stored + " " + itemName + " (" + remaining + " left in hand; pages are full)."
                : "Stored x" + stored + " " + itemName + ".";
            return true;
        }

        private static int StoreInBag(Bag bag, List<int> order, ItemStack source, out int remaining)
        {
            remaining = 0;
            if (bag == null || source == null || source.IsEmpty() || order == null || order.Count == 0)
            {
                return 0;
            }

            int original = source.count;
            remaining = original;
            if (remaining <= 0)
            {
                return 0;
            }

            ItemClass itemClass = source.itemValue.ItemClass;
            int maxStack = itemClass != null ? itemClass.MaxCount : 1;
            if (maxStack < 1)
            {
                maxStack = 1;
            }

            ItemStack probe = new ItemStack(source.itemValue.Clone(), 1);

            // Merge into matching stacks first.
            for (int k = 0; k < order.Count && remaining > 0; k++)
            {
                int index = order[k];
                if (index < 0 || index >= bag.SlotCount)
                {
                    continue;
                }

                ItemStack slot = bag.ItemGrid[index];
                if (slot == null || slot.IsEmpty())
                {
                    continue;
                }

                if (!slot.CanStackWith(probe, true))
                {
                    continue;
                }

                int room = maxStack - slot.count;
                if (room <= 0)
                {
                    continue;
                }

                int move = Math.Min(room, remaining);
                slot.count += move;
                remaining -= move;
            }

            // Then take empty unlocked slots.
            for (int k = 0; k < order.Count && remaining > 0; k++)
            {
                int index = order[k];
                if (index < 0 || index >= bag.SlotCount)
                {
                    continue;
                }

                ItemStack slot = bag.ItemGrid[index];
                if (slot != null && !slot.IsEmpty())
                {
                    continue;
                }

                int move = Math.Min(maxStack, remaining);
                bag.ItemGrid.SetItem(index, new ItemStack(source.itemValue.Clone(), move));
                remaining -= move;
            }

            return original - remaining;
        }

        private static List<int> BuildSlotOrder(int page, int allowed)
        {
            List<int> order = new List<int>(allowed);
            HashSet<int> added = new HashSet<int>();
            int baseSlot = (Math.Max(1, page) - 1) * Columns;

            // Current page first so deposit fills what the player is looking at.
            for (int i = 0; i < Columns; i++)
            {
                int slot = baseSlot + i;
                if (slot >= 0 && slot < allowed && added.Add(slot))
                {
                    order.Add(slot);
                }
            }

            // Then every other unlocked slot. The HashSet keeps this O(n) even
            // when a player has hundreds of pages.
            for (int slot = 0; slot < allowed; slot++)
            {
                if (added.Add(slot))
                {
                    order.Add(slot);
                }
            }

            return order;
        }

        private static bool DropAtFeet(EntityPlayer entity, ItemStack stack)
        {
            if (entity == null || stack == null || GameManager.Instance == null ||
                GameManager.Instance.World == null)
            {
                return false;
            }

            try
            {
                Vector3 pos = entity.position;
                pos.y += 0.9f;
                GameManager.Instance.DropContentInLootContainerServer(
                    entity.entityId,
                    "DroppedLootContainer",
                    pos,
                    new[] { stack },
                    true);
                return true;
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError(
                    "[DynamicBackpack] could not drop an item at the player's feet: " + exception);
                return false;
            }
        }

        private static EntityPlayer GetEntity(string playerId)
        {
            ClientInfo client = FindClient(playerId);
            if (client == null || GameManager.Instance == null || GameManager.Instance.World == null)
            {
                return null;
            }

            return GameManager.Instance.World.GetEntity(client.entityId) as EntityPlayer;
        }

        private static int CountUsed(Bag bag, int unlockedSlots)
        {
            if (bag == null)
            {
                return 0;
            }

            int used = 0;
            int max = Math.Min(Math.Max(0, unlockedSlots), bag.SlotCount);
            for (int i = 0; i < max; i++)
            {
                ItemStack stack = bag.ItemGrid[i];
                if (stack != null && !stack.IsEmpty())
                {
                    used++;
                }
            }

            return used;
        }

        private static string GetDisplayName(ItemValue value)
        {
            if (value == null)
            {
                return "item";
            }

            ItemClass itemClass = value.ItemClass;
            if (itemClass == null)
            {
                return "item";
            }

            string localized = itemClass.GetLocalizedItemName();
            if (!string.IsNullOrEmpty(localized))
            {
                return localized;
            }

            string name = itemClass.GetItemName();
            return string.IsNullOrEmpty(name) ? "item" : name;
        }

        #endregion

        #region Commands

        private void CommandBackpack(IPlayer player, string command, string[] args)
        {
            if (player == null || player.IsServer)
            {
                Interface.Oxide.LogInfo(
                    "[DynamicBackpack] This command must be used by an in-game player.");
                return;
            }

            string sub = args != null && args.Length > 0
                ? args[0].Trim().ToLowerInvariant()
                : string.Empty;

            switch (sub)
            {
                case "":
                case "open":
                    OpenBackpack(player);
                    break;
                case "info":
                case "pages":
                case "rows":
                    ShowInfo(player);
                    break;
                case "save":
                    SavePlayer(player.Id);
                    Reply(player, "Backpack saved.");
                    break;
                case "reload":
                    if (!RequireAdmin(player))
                    {
                        return;
                    }

                    LoadConfig();
                    RegisterPermissions();
                    EnsureDefaultPermissions();
                    Reply(player, "Configuration reloaded.");
                    break;
                case "help":
                    ShowHelp(player);
                    break;
                default:
                    ShowHelp(player);
                    break;
            }
        }

        private void OpenBackpack(IPlayer player)
        {
            int rows = GetPages(player.Id);
            if (rows <= 0)
            {
                Reply(player, "No backpack pages are unlocked. Ask an admin for '" +
                              config.PermissionPrefix + ".1'.");
                return;
            }

            Plugin serverAssist = plugins.Find("ServerAssist");
            if (serverAssist == null)
            {
                Reply(player,
                    "Server Assist is required to open the backpack. Install the ServerAssist plugin and client mod.");
                return;
            }

            object result = serverAssist.Call("OpenPanel", player.Id, PanelId, "a|open");
            if (!(result is bool) || !(bool)result)
            {
                Reply(player,
                    "Server Assist could not open the backpack yet. Try again in a moment.");
            }
        }

        private void ShowInfo(IPlayer player)
        {
            int rows = GetPages(player.Id);
            Bag bag = EnsureBag(player.Id);
            int used = CountUsed(bag, rows * Columns);

            Reply(player, "Pages unlocked: " + rows +
                         ". Slots: " + (rows * Columns) +
                         ". Stored stacks: " + used + ".");

            if (rows <= 0)
            {
                Reply(player, "No pages are unlocked. Ask an admin for '" +
                              config.PermissionPrefix + ".1'.");
            }
            else
            {
                Reply(player, "You can use pages 1-" + rows + " in Server Assist.");
            }

            if (player.HasPermission(config.AdminPermission))
            {
                Reply(player, "Admin permission: " + config.AdminPermission);
            }
        }

        private void ShowHelp(IPlayer player)
        {
            Reply(player, "/backpack - open the backpack pages in Server Assist");
            Reply(player, "/backpack info - show unlocked pages and usage");
            Reply(player, "/backpack save - force-save your backpack");
            Reply(player, "/backpack reload - reload config (admin)");
        }

        private bool RequireAdmin(IPlayer player)
        {
            if (player != null &&
                (player.IsAdmin || permission.UserHasPermission(player.Id, config.AdminPermission)))
            {
                return true;
            }

            Reply(player, "You need the '" + config.AdminPermission + "' permission.");
            return false;
        }

        #endregion

        #region Permissions and pages

        private int GetPages(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return 0;
            }

            bool admin = false;
            if (!string.IsNullOrEmpty(config.AdminPermission) &&
                permission.UserHasPermission(playerId, config.AdminPermission))
            {
                admin = true;
            }
            else
            {
                IPlayer player = players.FindPlayerById(playerId);
                admin = player != null && player.IsAdmin;
            }

            int pages = admin ? config.AdminPages : config.DefaultPages;
            string prefix = config.PermissionPrefix + ".";

            try
            {
                string[] permissions = permission.GetUserPermissions(playerId);
                if (permissions != null)
                {
                    for (int i = 0; i < permissions.Length; i++)
                    {
                        pages = ApplyPagePermission(permissions[i], pages, prefix);
                    }
                }

                string[] groups = permission.GetUserGroups(playerId);
                if (groups != null)
                {
                    for (int i = 0; i < groups.Length; i++)
                    {
                        string[] groupPermissions =
                            permission.GetGroupPermissions(groups[i], true);
                        if (groupPermissions == null)
                        {
                            continue;
                        }

                        for (int j = 0; j < groupPermissions.Length; j++)
                        {
                            pages = ApplyPagePermission(
                                groupPermissions[j], pages, prefix);
                        }
                    }
                }
            }
            catch
            {
                // Permission data can be briefly unavailable during reload.
                // Fall back to the highest registered tier in that case.
                for (int i = config.MaxPages; i >= 1; i--)
                {
                    if (permission.UserHasPermission(playerId, prefix + i))
                    {
                        if (i > pages)
                        {
                            pages = i;
                        }

                        break;
                    }
                }
            }

            return Clamp(pages, 0, config.MaxPages);
        }

        private int ApplyPagePermission(string name, int pages, string prefix)
        {
            if (string.IsNullOrEmpty(name))
            {
                return pages;
            }

            if (string.Equals(name, prefix + "*", StringComparison.OrdinalIgnoreCase))
            {
                return config.MaxPages;
            }

            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return pages;
            }

            int value;
            if (int.TryParse(name.Substring(prefix.Length), out value) && value > pages)
            {
                return value;
            }

            return pages;
        }

        private bool IsBackpackPermission(string permissionName)
        {
            if (string.IsNullOrEmpty(permissionName))
            {
                return false;
            }

            return string.Equals(permissionName, config.AdminPermission, StringComparison.OrdinalIgnoreCase) ||
                   permissionName.StartsWith(config.PermissionPrefix + ".", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(permissionName, config.PermissionPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshRows(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return;
            }

            int rows = GetPages(playerId);
            int oldRows;
            bool changed = !pagesCache.TryGetValue(playerId, out oldRows) || oldRows != rows;
            pagesCache[playerId] = rows;

            if (!changed)
            {
                return;
            }

            if (rows > oldRows)
            {
                overflowDropped.Remove(playerId);
                EnsureCapacity(playerId, EnsureBag(playerId), rows);
            }
            else if (rows < oldRows)
            {
                int dropped;
                ClampBag(playerId, EnsureBag(playerId), rows, out dropped);
            }
        }

        private void RecheckRows()
        {
            foreach (IPlayer player in players.Connected)
            {
                if (player == null || string.IsNullOrEmpty(player.Id))
                {
                    continue;
                }

                int rows = GetPages(player.Id);
                int oldRows;
                if (!pagesCache.TryGetValue(player.Id, out oldRows))
                {
                    pagesCache[player.Id] = rows;
                    continue;
                }

                if (oldRows != rows)
                {
                    RefreshRows(player.Id);
                }
            }
        }

        #endregion

        #region Bag storage

        private Bag EnsureBag(string playerId)
        {
            Bag bag;
            if (playerId != null && bags.TryGetValue(playerId, out bag))
            {
                return bag;
            }

            bag = LoadBag(playerId);
            if (playerId != null)
            {
                bags[playerId] = bag;
            }

            return bag;
        }

        private Bag LoadBag(string playerId)
        {
            if (!string.IsNullOrEmpty(playerId))
            {
                try
                {
                    PlayerBackpackData data =
                        Interface.Oxide.DataFileSystem.ReadObject<PlayerBackpackData>(
                            DataFileName(playerId));

                    if (data != null && !string.IsNullOrEmpty(data.Data))
                    {
                        Bag loaded = DecodeBag(data.Data);
                        if (loaded != null)
                        {
                            int oldSlots = loaded.SlotCount;
                            Bag normalized = NormalizeBag(playerId, loaded);
                            if (normalized != null)
                            {
                                if (oldSlots != normalized.SlotCount)
                                {
                                    Interface.Oxide.LogInfo(
                                        "[DynamicBackpack] migrated " + playerId + " from " +
                                        oldSlots + " to " + normalized.SlotCount + " slots.");
                                }

                                return normalized;
                            }
                        }

                        Interface.Oxide.LogWarning(
                            "[DynamicBackpack] ignoring an invalid saved backpack for " + playerId);
                    }
                }
                catch (Exception exception)
                {
                    Interface.Oxide.LogWarning(
                        "[DynamicBackpack] could not load " + playerId + ": " + exception.Message);
                }
            }

            return CreateEmptyBag(playerId);
        }

        private void SavePlayer(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return;
            }

            Bag bag;
            if (!bags.TryGetValue(playerId, out bag) || bag == null)
            {
                return;
            }

            string encoded = EncodeBag(bag);
            if (string.IsNullOrEmpty(encoded))
            {
                return;
            }

            try
            {
                IPlayer player = players.FindPlayerById(playerId);
                PlayerBackpackData data = new PlayerBackpackData
                {
                    Data = encoded,
                    Pages = GetPages(playerId),
                    Name = player != null ? player.Name : string.Empty,
                    UpdatedUtc = DateTime.UtcNow.ToString("o")
                };

                Interface.Oxide.DataFileSystem.WriteObject(
                    DataFileName(playerId), data, true);
                dirty.Remove(playerId);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError(
                    "[DynamicBackpack] could not save " + playerId + ": " + exception);
            }
        }

        private void FlushDirty()
        {
            if (dirty.Count == 0)
            {
                return;
            }

            string[] pending = dirty.ToArray();
            for (int i = 0; i < pending.Length; i++)
            {
                SavePlayer(pending[i]);
            }
        }

        private Bag CreateEmptyBag(string playerId)
        {
            int pages = Math.Max(1, Math.Min(GetPages(playerId), config.MaxPages));
            return new Bag(
                new Vector2i(Columns, pages),
                XUiC_ItemStack.StackLocationTypes.LootContainer,
                null);
        }

        /// <summary>
        /// Keeps an older or larger saved bag. Existing slot indexes are never
        /// moved. The bag is only grown when the player's page permission needs
        /// more slots. Bags larger than the configured limit stay larger so a
        /// temporary permission/config reduction does not destroy items.
        /// </summary>
        private Bag NormalizeBag(string playerId, Bag bag)
        {
            if (bag == null)
            {
                return null;
            }

            if (bag.SlotCount > MaxStoredSlots)
            {
                return null;
            }

            int pages = Math.Max(1, Math.Min(GetPages(playerId), config.MaxPages));
            int required = pages * Columns;
            if (bag.SlotCount < required)
            {
                bag.Resize(new Vector2i(Columns, pages));
            }

            return bag;
        }

        private void EnsureCapacity(string playerId, Bag bag, int pages)
        {
            if (bag == null)
            {
                return;
            }

            int wantedPages = Math.Max(1, Math.Min(pages, config.MaxPages));
            int required = wantedPages * Columns;
            if (bag.SlotCount < required)
            {
                bag.Resize(new Vector2i(Columns, wantedPages));
            }
        }

        private bool ClampBag(string playerId, Bag bag, int rows, out int droppedStacks)
        {
            droppedStacks = 0;
            if (bag == null)
            {
                return true;
            }

            int allowed = Clamp(rows, 0, config.MaxPages) * Columns;
            if (allowed >= bag.SlotCount)
            {
                overflowDropped.Remove(playerId);
                return true;
            }

            List<ItemStack> overflow = null;
            for (int i = allowed; i < bag.SlotCount; i++)
            {
                ItemStack stack = bag.ItemGrid[i];
                if (stack != null && !stack.IsEmpty())
                {
                    if (overflow == null)
                    {
                        overflow = new List<ItemStack>();
                    }

                    overflow.Add(stack.Clone());
                }
            }

            if (overflow == null || overflow.Count == 0)
            {
                return true;
            }

            if (!config.DropOverflowOnDowngrade)
            {
                return false;
            }

            bool alreadyDropped = overflowDropped.Contains(playerId);
            if (!alreadyDropped)
            {
                if (!TryDropOverflow(playerId, overflow))
                {
                    return false;
                }

                overflowDropped.Add(playerId);
                droppedStacks = overflow.Count;
            }

            for (int i = allowed; i < bag.SlotCount; i++)
            {
                bag.ItemGrid[i].Clear();
            }

            dirty.Add(playerId);
            return true;
        }

        private bool TryDropOverflow(string playerId, List<ItemStack> overflow)
        {
            if (overflow == null || overflow.Count == 0)
            {
                return false;
            }

            EntityPlayer entity = GetEntity(playerId);
            if (entity == null)
            {
                return false;
            }

            if (!DropAtFeet(entity, overflow[0]))
            {
                return false;
            }

            for (int i = 1; i < overflow.Count; i++)
            {
                DropAtFeet(entity, overflow[i]);
            }

            Interface.Oxide.LogInfo(
                "[DynamicBackpack] dropped " + overflow.Count +
                " overflow stack(s) for " + playerId + " at their backpack limit.");
            return true;
        }

        private static string EncodeBag(Bag bag)
        {
            if (bag == null)
            {
                return string.Empty;
            }

            try
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    using (PooledBinaryWriter writer = new PooledBinaryWriter())
                    {
                        writer.SetBaseStream(stream);
                        bag.Write(writer, StreamModeWrite.Persistency);
                        writer.Flush();
                    }

                    return Convert.ToBase64String(stream.ToArray());
                }
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError("[DynamicBackpack] could not encode bag: " + exception);
                return string.Empty;
            }
        }

        private static Bag DecodeBag(string base64)
        {
            if (string.IsNullOrEmpty(base64))
            {
                return null;
            }

            try
            {
                byte[] bytes = Convert.FromBase64String(base64);
                using (MemoryStream stream = new MemoryStream(bytes))
                {
                    using (PooledBinaryReader reader = new PooledBinaryReader())
                    {
                        reader.SetBaseStream(stream);
                        return Bag.Read(
                            reader,
                            XUiC_ItemStack.StackLocationTypes.LootContainer,
                            StreamModeRead.Persistency,
                            null);
                    }
                }
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning(
                    "[DynamicBackpack] invalid bag payload: " + exception.Message);
                return null;
            }
        }

        private static string DataFileName(string playerId)
        {
            return "DynamicBackpack/" + SafeId(playerId);
        }

        private static string SafeId(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return "unknown";
            }

            StringBuilder builder = new StringBuilder(playerId.Length);
            for (int i = 0; i < playerId.Length; i++)
            {
                char c = playerId[i];
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')
                {
                    builder.Append(c);
                }
                else
                {
                    builder.Append('_');
                }
            }

            return builder.ToString();
        }

        #endregion

        #region Helpers

        private static ClientInfo FindClient(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return null;
            }

            ConnectionManager connection = SingletonMonoBehaviour<ConnectionManager>.Instance;
            if (connection == null || connection.Clients == null || connection.Clients.list == null)
            {
                return null;
            }

            for (int i = 0; i < connection.Clients.list.Count; i++)
            {
                ClientInfo client = connection.Clients.list[i];
                if (client != null &&
                    string.Equals(GetPlayerId(client), playerId, StringComparison.OrdinalIgnoreCase))
                {
                    return client;
                }
            }

            return null;
        }

        private static string GetPlayerId(ClientInfo client)
        {
            if (client == null)
            {
                return null;
            }

            try
            {
                if (client.PlatformId != null &&
                    !string.IsNullOrEmpty(client.PlatformId.ReadablePlatformUserIdentifier))
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
                if (client.CrossplatformId != null &&
                    !string.IsNullOrEmpty(client.CrossplatformId.ReadablePlatformUserIdentifier))
                {
                    return client.CrossplatformId.ReadablePlatformUserIdentifier;
                }
            }
            catch
            {
                // Ignore.
            }

            return null;
        }

        private void Reply(IPlayer player, string message)
        {
            if (player == null || player.IsServer)
            {
                Interface.Oxide.LogInfo("[DynamicBackpack] " + message);
                return;
            }

            string prefix = string.IsNullOrEmpty(config.ChatPrefix) ? string.Empty : config.ChatPrefix + " ";
            player.Reply(prefix + message);
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

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        #endregion

        #region Classes

        private sealed class Configuration
        {
            public string ChatPrefix = "[Backpack]";
            public string PermissionPrefix = "backpack";
            public int MaxPages = 100;
            public int DefaultPages = 0;
            public bool GrantFirstPageToPlayers = true;
            public string AdminPermission = "backpack.admin";
            public int AdminPages = 10;
            public bool GrantAdminToAdministrators = true;
            public int SaveIntervalSeconds = 15;
            public bool DropOverflowOnDowngrade = false;
        }

        private sealed class PlayerBackpackData
        {
            public string Data = string.Empty;
            public int Pages;
            public string Name = string.Empty;
            public string UpdatedUtc = string.Empty;
        }

        #endregion
    }
}



