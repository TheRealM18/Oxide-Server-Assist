using System;
using System.IO;
using System.Threading;

namespace QLNPackServer
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.Title = "QLN Pack Server";

            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "QLNPackServer.json");

            for (int index = 0; index < args.Length; index++)
            {
                if (string.Equals(args[index], "--config", StringComparison.OrdinalIgnoreCase) &&
                    index + 1 < args.Length)
                {
                    configPath = args[index + 1];
                    index++;
                }
                else if (string.Equals(args[index], "--help", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("QLNPackServer.exe [--config QLNPackServer.json]");
                    return 0;
                }
            }

            if (!Path.IsPathRooted(configPath))
            {
                configPath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, configPath));
            }

            if (!File.Exists(configPath))
            {
                Console.Error.WriteLine("Config not found: " + configPath);
                Console.Error.WriteLine("Copy the shipped QLNPackServer.json next to the exe, edit it, and run again.");
                return 2;
            }

            ServerConfig config = ServerConfig.Load(configPath);
            if (config == null)
            {
                Console.Error.WriteLine("Could not parse config: " + configPath);
                return 2;
            }

            config.Normalize(Path.GetDirectoryName(configPath));
            Log.Init(config.logFile);

            Log.Info("Config: " + configPath);
            Log.Info("Mods:   " + config.modsPath);
            Log.Info("Cache:  " + config.cacheDir);

            try
            {
                Directory.CreateDirectory(config.cacheDir);

                using (PackCatalog catalog = new PackCatalog(config))
                using (HttpFileServer server = new HttpFileServer(config, catalog))
                {
                    catalog.Start();

                    if (catalog.ModCount == 0)
                    {
                        Log.Warn("No pack mods were found. Check modsPath/include.");
                    }

                    server.Start();

                    ManualResetEventSlim stop = new ManualResetEventSlim(false);

                    Console.CancelKeyPress += (sender, eventArgs) =>
                    {
                        eventArgs.Cancel = true;
                        stop.Set();
                    };

                    AppDomain.CurrentDomain.ProcessExit += (sender, eventArgs) => stop.Set();

                    Log.Info("Press Ctrl+C to stop.");
                    stop.Wait();

                    Log.Info("Stopping.");
                    server.Stop();
                }
            }
            catch (Exception exception)
            {
                Log.Exception("Server failed", exception);
                return 1;
            }

            return 0;
        }
    }
}
