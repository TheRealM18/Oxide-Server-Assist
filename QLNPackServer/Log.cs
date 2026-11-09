using System;
using System.IO;

namespace QLNPackServer
{
    internal static class Log
    {
        private static readonly object Sync = new object();
        private static string _path;

        public static void Init(string path)
        {
            _path = path;

            try
            {
                if (!string.IsNullOrEmpty(_path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path));
                    File.AppendAllText(
                        _path,
                        Environment.NewLine +
                        "===== QLNPackServer started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " =====" +
                        Environment.NewLine);
                }
            }
            catch
            {
                // Console logging still works.
            }
        }

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

            try
            {
                Console.WriteLine(line);
            }
            catch
            {
                // No console (service mode).
            }

            try
            {
                lock (Sync)
                {
                    if (!string.IsNullOrEmpty(_path))
                    {
                        File.AppendAllText(_path, line + Environment.NewLine);
                    }
                }
            }
            catch
            {
                // Logging must never stop the server.
            }
        }
    }
}
