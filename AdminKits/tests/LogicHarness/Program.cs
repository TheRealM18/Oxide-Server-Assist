using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;

internal static class Program
{
    private const string GameManaged =
        @"C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die\7DaysToDie_Data\Managed";

    private const string OxideManaged =
        @"C:\Users\robert\Downloads\Oxide.SevenDaysToDie\7DaysToDieServer_Data\Managed";

    private static int failures;

    private static int Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;

        string pluginPath = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "PluginCompile", "bin", "AdminKitsPluginCompileCheck.dll"));

        if (!File.Exists(pluginPath))
        {
            Console.WriteLine("Plugin assembly not found: " + pluginPath);
            return 2;
        }

        Assembly assembly = Assembly.Load(File.ReadAllBytes(pluginPath));
        Type pluginType = assembly.GetType("Oxide.Plugins.AdminKits", true);
        Type configType = pluginType.GetNestedType("Configuration", BindingFlags.NonPublic);
        object config = Activator.CreateInstance(configType, true);
        configType.GetField("DefaultItemQuality").SetValue(config, 1);
        object plugin = FormatterServices.GetUninitializedObject(pluginType);
        pluginType.GetField("config", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(plugin, config);

        MethodInfo parse = pluginType.GetMethod("ParseItemSpec", BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo makeId = pluginType.GetMethod("MakeKitId", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo encode = pluginType.GetMethod("Encode", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo decode = pluginType.GetMethod("Decode", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo formatDuration = pluginType.GetMethod("FormatDuration", BindingFlags.NonPublic | BindingFlags.Static);

        CheckSpec(parse, plugin, "9mmBullet 100 6", "9mmBullet", 100, 6);
        CheckSpec(parse, plugin, "9mm Bullet 100 6", "9mm Bullet", 100, 6);
        CheckSpec(parse, plugin, "foodCanChili 10", "foodCanChili", 10, 1);
        CheckSpec(parse, plugin, "medicalFirstAidBandage", "medicalFirstAidBandage", 1, 1);
        CheckSpec(parse, plugin, "7.62mm Bullet 200", "7.62mm Bullet", 200, 1);

        HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CheckString("MakeKitId simple", (string)makeId.Invoke(null, new object[] { "Starter Kit", used }), "starter-kit");
        CheckString("MakeKitId duplicate", (string)makeId.Invoke(null, new object[] { "Starter Kit", used }), "starter-kit-2");
        CheckString("MakeKitId punctuation", (string)makeId.Invoke(null, new object[] { "VIP / Elite!", new HashSet<string>(StringComparer.OrdinalIgnoreCase) }), "vip-elite");

        string encoded = (string)encode.Invoke(null, new object[] { "a|b%c\nd" });
        CheckString("Encode", encoded, "a%7Cb%25c%0Ad");
        CheckString("Decode", (string)decode.Invoke(null, new object[] { encoded }), "a|b%c\nd");

        CheckString("FormatDuration minutes", (string)formatDuration.Invoke(null, new object[] { TimeSpan.FromMinutes(90) }), "1h 30m");
        CheckString("FormatDuration days", (string)formatDuration.Invoke(null, new object[] { TimeSpan.FromHours(27) }), "1d 3h");

        CheckNormalization(pluginType, plugin);
        CheckRenderProtocol(pluginType, plugin);
        CheckKitStatusIcons(pluginType, plugin);
        CheckIconDefaults(pluginType, plugin);
        CheckAllRowsHaveIcons(pluginType, plugin);

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "ALL LOGIC CHECKS PASSED" : failures + " LOGIC CHECK(S) FAILED");
        return failures == 0 ? 0 : 1;
    }

    private static void CheckAllRowsHaveIcons(Type pluginType, object plugin)
    {
        Type sessionType = pluginType.GetNestedType("Session", BindingFlags.NonPublic);
        object session = Activator.CreateInstance(sessionType, true);
        sessionType.GetField("SelectedKitId").SetValue(session, "starter-kit");
        sessionType.GetField("SelectedItemIndex").SetValue(session, 0);
        sessionType.GetField("Page").SetValue(session, 0);

        MethodInfo render = pluginType.GetMethod("Render", BindingFlags.NonPublic | BindingFlags.Instance);
        string[] screens = { "edit", "limits", "items", "itemdetail", "confirmdelete", "confirmclearitems", "help" };

        for (int s = 0; s < screens.Length; s++)
        {
            sessionType.GetField("Screen").SetValue(session, screens[s]);
            string view = (string)render.Invoke(plugin, new object[] { "player-1", session });
            string[] lines = view.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            bool allIcons = true;
            int rowCount = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith("row=", StringComparison.Ordinal))
                {
                    continue;
                }

                rowCount++;
                string[] parts = lines[i].Split('|');
                if (parts.Length < 9 || string.IsNullOrEmpty(parts[6]))
                {
                    allIcons = false;
                }
            }

            Report(allIcons && rowCount > 0, "all rows have icons: " + screens[s],
                "every row iconed", allIcons ? rowCount + " rows" : "empty icon");
        }
    }

    private static void CheckIconDefaults(Type pluginType, object plugin)
    {
        MethodInfo buttonIcon = pluginType.GetMethod("DefaultButtonIcon", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo rowIcon = pluginType.GetMethod("DefaultRowIcon", BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo screenArt = pluginType.GetMethod("ScreenArt", BindingFlags.NonPublic | BindingFlags.Instance);

        Type sessionType = pluginType.GetNestedType("Session", BindingFlags.NonPublic);
        object session = Activator.CreateInstance(sessionType, true);

        CheckIcon("collect button", (string)buttonIcon.Invoke(null, new object[] { "collect|starter" }), "sprite:sa_nav_items");
        CheckIcon("give button", (string)buttonIcon.Invoke(null, new object[] { "give|starter" }), "sprite:sa_act_give");
        CheckIcon("edit button", (string)buttonIcon.Invoke(null, new object[] { "edit|starter" }), "sprite:sa_act_repair");
        CheckIcon("delete button", (string)buttonIcon.Invoke(null, new object[] { "delete|starter" }), "sprite:sa_act_ban");
        CheckIcon("refresh button", (string)buttonIcon.Invoke(null, new object[] { "refresh" }), "sprite:sa_plugin_hot_reload");
        CheckIcon("back button", (string)buttonIcon.Invoke(null, new object[] { "back" }), "");

        Type rowType = pluginType.GetNestedType("RowInfo", BindingFlags.NonPublic);
        object row = Activator.CreateInstance(rowType, true);
        rowType.GetField("Label").SetValue(row, "Permission");
        sessionType.GetField("Screen").SetValue(session, "detail");
        CheckIcon("permission row", (string)rowIcon.Invoke(plugin, new object[] { session, row }), "sprite:sa_permission_gui");

        rowType.GetField("Label").SetValue(row, "Warning");
        sessionType.GetField("Screen").SetValue(session, "confirmdelete");
        CheckIcon("warning row", (string)rowIcon.Invoke(plugin, new object[] { session, row }), "sprite:sa_act_warn");

        rowType.GetField("Label").SetValue(row, "Manage Kits");
        rowType.GetField("Action").SetValue(row, "manage");
        sessionType.GetField("Screen").SetValue(session, "home");
        CheckIcon("manage row", (string)rowIcon.Invoke(plugin, new object[] { session, row }), "sprite:sa_nav_logs");

        sessionType.GetField("Screen").SetValue(session, "detail");
        CheckIcon("detail banner", (string)screenArt.Invoke(plugin, new object[] { session }), "sprite:sa_admin_kits");
        sessionType.GetField("Screen").SetValue(session, "list");
        CheckIcon("list banner", (string)screenArt.Invoke(plugin, new object[] { session }), "sprite:sa_nav_items");
        sessionType.GetField("Screen").SetValue(session, "help");
        CheckIcon("help banner", (string)screenArt.Invoke(plugin, new object[] { session }), "sprite:sa_nav_help");
    }

    private static void CheckIcon(string label, string actual, string expected)
    {
        Report(actual == expected, label, expected, actual);
    }

    private static void CheckRenderProtocol(Type pluginType, object plugin)
    {
        Type sessionType = pluginType.GetNestedType("Session", BindingFlags.NonPublic);
        object session = Activator.CreateInstance(sessionType, true);
        sessionType.GetField("Screen").SetValue(session, "help");

        MethodInfo render = pluginType.GetMethod("Render", BindingFlags.NonPublic | BindingFlags.Instance);
        string view = (string)render.Invoke(plugin, new object[] { "player-1", session });

        int rowCount = 0;
        int buttonCount = 0;
        bool rowsShaped = true;
        bool buttonsShaped = true;
        string[] lines = view.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("row=", StringComparison.Ordinal))
            {
                rowCount++;
                rowsShaped &= lines[i].Split('|').Length == 9;
            }
            else if (lines[i].StartsWith("btn=", StringComparison.Ordinal))
            {
                buttonCount++;
                buttonsShaped &= lines[i].Split('|').Length == 7;
            }
        }

        Report(view.Contains("banner=sprite:sa_nav_help"), "render help banner", "sprite:sa_nav_help",
            view.Contains("banner=sprite:sa_nav_help") ? "sprite:sa_nav_help" : "missing");
        Report(view.Contains("background=sprite:sa_nav_help"), "render help background", "sprite:sa_nav_help",
            view.Contains("background=sprite:sa_nav_help") ? "sprite:sa_nav_help" : "missing");
        Report(rowCount == 9, "render help row count", "9", rowCount.ToString());
        Report(buttonCount == 2, "render help button count", "2", buttonCount.ToString());
        Report(rowsShaped, "render row field count", "9", rowsShaped ? "9" : "wrong");
        Report(buttonsShaped, "render button field count", "7", buttonsShaped ? "7" : "wrong");

        Type itemType = pluginType.GetNestedType("KitItem", BindingFlags.NonPublic);
        object item = Activator.CreateInstance(itemType, true);
        itemType.GetField("Item").SetValue(item, "gunPistol");
        itemType.GetField("Data").SetValue(item, string.Empty);
        MethodInfo itemIcon = pluginType.GetMethod("ItemIcon", BindingFlags.NonPublic | BindingFlags.Instance);
        string icon = (string)itemIcon.Invoke(plugin, new object[] { item });
        Report(icon == "item:gunPistol", "ItemIcon uses item sprite", "item:gunPistol", icon);
    }

    private static void CheckKitStatusIcons(Type pluginType, object plugin)
    {
        Type kitType = pluginType.GetNestedType("KitDefinition", BindingFlags.NonPublic);
        MethodInfo badgeMethod = pluginType.GetMethod("GetKitStateBadge", BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo iconMethod = pluginType.GetMethod("GetKitStateIcon", BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo tintMethod = pluginType.GetMethod("GetKitStateTint", BindingFlags.NonPublic | BindingFlags.Instance);

        object kit = Activator.CreateInstance(kitType, true);
        kitType.GetField("Id").SetValue(kit, "starter");
        kitType.GetField("Enabled").SetValue(kit, false);
        CheckKitState(plugin, kit, badgeMethod, iconMethod, tintMethod, "OFF",
            "sprite:sa_act_freeze", "#9AA0A6");

        kitType.GetField("Enabled").SetValue(kit, true);
        kitType.GetField("Hidden").SetValue(kit, true);
        CheckKitState(plugin, kit, badgeMethod, iconMethod, tintMethod, "HIDDEN",
            "sprite:sa_act_spectate", "#C79BFF");
    }

    private static void CheckKitState(object plugin, object kit, MethodInfo badgeMethod, MethodInfo iconMethod,
        MethodInfo tintMethod, string expectedBadge, string expectedIcon, string expectedTint)
    {
        string badge = (string)badgeMethod.Invoke(plugin, new object[] { "server_console", kit });
        string icon = (string)iconMethod.Invoke(plugin, new object[] { "server_console", kit });
        string tint = (string)tintMethod.Invoke(plugin, new object[] { "server_console", kit });

        Report(badge == expectedBadge && icon == expectedIcon && tint == expectedTint,
            "kit state " + (expectedBadge.Length == 0 ? "normal" : expectedBadge),
            expectedBadge + "/" + expectedIcon + "/" + expectedTint,
            badge + "/" + icon + "/" + tint);
    }

    private static void CheckNormalization(Type pluginType, object plugin)
    {
        Type dataType = pluginType.GetNestedType("StoredData", BindingFlags.NonPublic);
        Type kitType = pluginType.GetNestedType("KitDefinition", BindingFlags.NonPublic);
        Type itemType = pluginType.GetNestedType("KitItem", BindingFlags.NonPublic);
        Type claimType = pluginType.GetNestedType("ClaimRecord", BindingFlags.NonPublic);

        object data = Activator.CreateInstance(dataType, true);
        var kits = (System.Collections.IDictionary)dataType.GetField("Kits").GetValue(data);
        var claims = (System.Collections.IDictionary)dataType.GetField("Claims").GetValue(data);

        object kit = Activator.CreateInstance(kitType, true);
        kitType.GetField("Id").SetValue(kit, "Starter Kit");
        kitType.GetField("Name").SetValue(kit, "Starter Kit");
        kitType.GetField("Permission").SetValue(kit, "auto");
        kitType.GetField("CooldownMinutes").SetValue(kit, -50);
        kitType.GetField("MaxUses").SetValue(kit, -1);
        object item = Activator.CreateInstance(itemType, true);
        itemType.GetField("Item").SetValue(item, "9mmBullet");
        itemType.GetField("Count").SetValue(item, 0);
        itemType.GetField("Data").SetValue(item, string.Empty);
        var items = (System.Collections.IList)kitType.GetField("Items").GetValue(kit);
        items.Add(item);
        kits["raw-key"] = kit;

        object emptyKit = Activator.CreateInstance(kitType, true);
        kitType.GetField("Id").SetValue(emptyKit, "");
        kitType.GetField("Name").SetValue(emptyKit, "");
        kitType.GetField("Items").SetValue(emptyKit, null);
        kits["Boss Kit"] = emptyKit;

        Type claimMapType = typeof(Dictionary<,>).MakeGenericType(typeof(string), claimType);
        object orphanMap = Activator.CreateInstance(claimMapType);
        object orphan = Activator.CreateInstance(claimType, true);
        claimType.GetField("Count").SetValue(orphan, 5);
        ((System.Collections.IDictionary)orphanMap)["does-not-exist"] = orphan;
        claims["player-1"] = orphanMap;

        pluginType.GetField("data", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(plugin, data);
        pluginType.GetMethod("NormalizeData", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(plugin, null);

        kits = (System.Collections.IDictionary)dataType.GetField("Kits").GetValue(data);

        Report(kits.Count == 2, "NormalizeData keeps both kits", "2", kits.Count.ToString());

        object normalized = null;
        foreach (System.Collections.DictionaryEntry entry in kits)
        {
            object candidate = entry.Value;
            string name = (string)kitType.GetField("Name").GetValue(candidate);
            if (name == "Starter Kit")
            {
                normalized = candidate;
                Report((string)entry.Key == "starter-kit", "NormalizeData slug id", "starter-kit", entry.Key.ToString());
            }
        }

        Report(normalized != null, "NormalizeData finds normalized kit", "present",
            normalized != null ? "present" : "missing");

        if (normalized != null)
        {
            Report((string)kitType.GetField("Permission").GetValue(normalized) == "adminkits.kit.starter-kit",
                "NormalizeData resolves auto permission", "adminkits.kit.starter-kit",
                (string)kitType.GetField("Permission").GetValue(normalized));
            Report((int)kitType.GetField("CooldownMinutes").GetValue(normalized) == 0,
                "NormalizeData clamps cooldown", "0",
                kitType.GetField("CooldownMinutes").GetValue(normalized).ToString());
            Report((int)kitType.GetField("MaxUses").GetValue(normalized) == 0,
                "NormalizeData clamps max uses", "0",
                kitType.GetField("MaxUses").GetValue(normalized).ToString());
            var normalizedItems = (System.Collections.IList)kitType.GetField("Items").GetValue(normalized);
            Report(normalizedItems.Count == 1 && (int)itemType.GetField("Count").GetValue(normalizedItems[0]) == 1,
                "NormalizeData clamps item count", "1",
                normalizedItems.Count == 1 ? itemType.GetField("Count").GetValue(normalizedItems[0]).ToString() : "no item");
        }

        Report(claims.Count == 0, "NormalizeData removes orphaned claims", "0", claims.Count.ToString());

        MethodInfo findKit = pluginType.GetMethod("FindKit", BindingFlags.NonPublic | BindingFlags.Instance);
        object byName = findKit.Invoke(plugin, new object[] { "Starter Kit" });
        Report(byName == normalized, "FindKit by name", "normalized kit",
            byName == normalized ? "normalized kit" : "other");
    }

    private static void CheckSpec(MethodInfo parse, object plugin, string spec, string name, int count, int quality)
    {
        object[] args = { spec, null, 0, 0 };
        bool ok = (bool)parse.Invoke(plugin, args);
        string actualName = args[1] as string;
        int actualCount = (int)args[2];
        int actualQuality = (int)args[3];

        bool pass = ok && actualName == name && actualCount == count && actualQuality == quality;
        Report(pass, "ParseItemSpec \"" + spec + "\"",
            name + "/" + count + "/" + quality,
            actualName + "/" + actualCount + "/" + actualQuality);
    }

    private static void CheckString(string label, string actual, string expected)
    {
        Report(actual == expected, label, expected, actual);
    }

    private static void Report(bool pass, string label, string expected, string actual)
    {
        if (pass)
        {
            Console.WriteLine("PASS  " + label);
            return;
        }

        failures++;
        Console.WriteLine("FAIL  " + label + "  expected='" + expected + "' actual='" + actual + "'");
    }

    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name;
        string[] directories = name.StartsWith("Oxide", StringComparison.OrdinalIgnoreCase)
            ? new[] { OxideManaged, GameManaged }
            : new[] { GameManaged, OxideManaged };
        for (int i = 0; i < directories.Length; i++)
        {
            string path = Path.Combine(directories[i], name + ".dll");
            if (File.Exists(path))
            {
                // Load bytes so .NET does not apply the network-location
                // sandbox rules to assemblies under the user's Downloads folder.
                return Assembly.Load(File.ReadAllBytes(path));
            }
        }

        return null;
    }
}
