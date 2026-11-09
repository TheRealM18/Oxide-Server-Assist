using System;
using System.IO;
using System.Reflection;

namespace QLNPackBootstrap
{
    internal static class PackLogger
    {
        private static readonly object Sync = new object();

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Warn(string message)
        {
            Write("WARN", message);
        }

        public static void Error(string message)
        {
            Write("ERROR", message);
        }

        public static void Exception(string context, Exception exception)
        {
            Write("ERROR", context + ": " + exception);
        }

        private static void Write(string level, string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + message;

            // Use reflection so this class also works in a test harness that has
            // no 7 Days to Die assemblies loaded.
            GameLog("[QLNPack] " + message);

            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(PackPaths.LogsDir);
                    File.AppendAllText(PackPaths.LogFile, line + Environment.NewLine);
                }
            }
            catch
            {
                // Logging must never take the mod down.
            }
        }

        private static void GameLog(string message)
        {
            try
            {
                Type logType = Type.GetType("Log, Assembly-CSharp") ?? Type.GetType("Log, LogLibrary");
                if (logType == null)
                {
                    return;
                }

                MethodInfo method = logType.GetMethod(
                    "Out",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string) },
                    null);

                if (method != null)
                {
                    method.Invoke(null, new object[] { message });
                }
            }
            catch
            {
                // No game log available (test harness, early startup, etc.).
            }
        }
    }
}
