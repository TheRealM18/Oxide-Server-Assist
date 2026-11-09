using System;
using System.Collections.Generic;
using System.Reflection;

namespace ServerAssistMod
{
    /// <summary>
    /// Adds this mod's net packages to the game's known package table.
    /// The table is populated before mods load, so custom packages have to be
    /// injected the same way other 7 Days to Die code mods do it.
    /// </summary>
    public static class ServerAssistNetPackages
    {
        public static void RegisterAll(Assembly assembly)
        {
            try
            {
                FieldInfo field = typeof(NetPackageManager).GetField(
                    "knownPackageTypes",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                Dictionary<string, Type> known = field != null
                    ? field.GetValue(null) as Dictionary<string, Type>
                    : null;

                if (known == null)
                {
                    Log.Warning("[ServerAssist] NetPackageManager.knownPackageTypes was not found");
                    return;
                }

                int registered = 0;
                Type[] types = assembly.GetTypes();
                for (int i = 0; i < types.Length; i++)
                {
                    Type type = types[i];
                    if (!type.IsClass || type.IsAbstract || !typeof(NetPackage).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    if (known.ContainsKey(type.Name))
                    {
                        continue;
                    }

                    known.Add(type.Name, type);
                    registered++;
                }

                Log.Out("[ServerAssist] registered " + registered + " net package(s)");
            }
            catch (Exception exception)
            {
                Log.Error("[ServerAssist] net package registration failed: " + exception);
            }
        }
    }
}



