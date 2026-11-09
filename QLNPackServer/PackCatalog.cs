using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Xml;

namespace QLNPackServer
{
    public sealed class PackEntry
    {
        public string Folder;
        public string SafeFolder;
        public string Name;
        public string DisplayName;
        public string Version;
        public string ZipName;
        public string ZipPath;
        public string Sha256;
        public long Size;
    }

    public sealed class PackCatalog : IDisposable
    {
        private readonly ServerConfig _config;
        private readonly object _sync = new object();
        private readonly Dictionary<string, byte[]> _signatureCache = new Dictionary<string, byte[]>();
        private readonly Dictionary<string, ZipInfo> _zipInfo = new Dictionary<string, ZipInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly RSACryptoServiceProvider _signingKey;
        private Timer _timer;

        private List<PackEntry> _entries = new List<PackEntry>();
        private Dictionary<string, string> _zipFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string _catalogVersion = "0";
        private string _catalogGeneratedAt = DateTime.UtcNow.ToString("o");
        private string _manifestCacheBase;
        private string _manifestCacheVersion;
        private byte[] _manifestCacheBytes;
        private int _refreshing;

        public PackCatalog(ServerConfig config)
        {
            _config = config;

            if (!string.IsNullOrWhiteSpace(config.signingKeyXml))
            {
                try
                {
                    if (File.Exists(config.signingKeyXml))
                    {
                        RSACryptoServiceProvider key = new RSACryptoServiceProvider();

                        try
                        {
                            key.FromXmlString(File.ReadAllText(config.signingKeyXml));
                            _signingKey = key;
                            Log.Info("Loaded manifest signing key: " + config.signingKeyXml);
                        }
                        catch
                        {
                            key.Dispose();
                            throw;
                        }
                    }
                    else
                    {
                        Log.Warn("Signing key not found: " + config.signingKeyXml);
                    }
                }
                catch (Exception exception)
                {
                    Log.Exception("Could not load signing key", exception);
                }
            }
        }

        public void Start()
        {
            Refresh();

            if (_config.refreshSeconds > 0)
            {
                TimeSpan interval = TimeSpan.FromSeconds(_config.refreshSeconds);
                _timer = new Timer(_ => Refresh(), null, interval, interval);
            }
        }

        public void Refresh()
        {
            if (Interlocked.Exchange(ref _refreshing, 1) == 1)
            {
                return;
            }

            try
            {
                if (!Directory.Exists(_config.modsPath))
                {
                    Log.Warn("Mods folder does not exist: " + _config.modsPath);
                    Publish(new List<PackEntry>(), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                    return;
                }

                List<string> candidates = GetCandidateFolders();
                List<PackEntry> entries = new List<PackEntry>();
                Dictionary<string, string> zipFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> safeFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (string folderPath in candidates)
                {
                    string folderName = Path.GetFileName(folderPath);
                    if (IsExcluded(folderName))
                    {
                        continue;
                    }

                    ModInfo info = ReadModInfo(folderPath);
                    if (info == null)
                    {
                        Log.Warn("Skipping " + folderName + ": no usable folder name");
                        continue;
                    }

                    string safeFolder = SanitizeFolder(folderName);
                    if (string.IsNullOrEmpty(safeFolder) ||
                        string.Equals(safeFolder, "000_QLNPackBootstrap", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(safeFolder, "Data", StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Warn("Skipping " + folderName + ": unsafe install folder");
                        continue;
                    }

                    if (!safeFolders.Add(safeFolder))
                    {
                        Log.Warn("Skipping " + folderName + ": duplicate install folder " + safeFolder);
                        continue;
                    }

                    string fingerprint = ComputeFingerprint(folderPath);
                    string zipName = safeFolder + "-" + fingerprint.Substring(0, 12) + ".zip";
                    string zipPath = Path.Combine(_config.cacheDir, zipName);

                    if (!File.Exists(zipPath))
                    {
                        CreateZip(folderPath, zipPath);
                    }

                    if (!File.Exists(zipPath))
                    {
                        Log.Warn("Skipping " + folderName + ": could not create zip");
                        continue;
                    }

                    FileInfo zipFile = new FileInfo(zipPath);
                    ZipInfo zipInfo;

                    if (!_zipInfo.TryGetValue(zipPath, out zipInfo) ||
                        zipInfo.Size != zipFile.Length ||
                        zipInfo.LastWriteTicks != zipFile.LastWriteTimeUtc.Ticks)
                    {
                        zipInfo = new ZipInfo
                        {
                            Sha256 = ComputeSha256(zipPath),
                            Size = zipFile.Length,
                            LastWriteTicks = zipFile.LastWriteTimeUtc.Ticks
                        };

                        _zipInfo[zipPath] = zipInfo;
                    }

                    entries.Add(new PackEntry
                    {
                        Folder = folderName,
                        SafeFolder = safeFolder,
                        Name = info.Name,
                        DisplayName = info.DisplayName,
                        Version = info.Version,
                        ZipName = zipName,
                        ZipPath = zipPath,
                        Sha256 = zipInfo.Sha256,
                        Size = zipInfo.Size
                    });

                    zipFiles[zipName] = zipPath;
                    Log.Info("Serving " + info.DisplayName + " v" + info.Version + " as " + zipName);
                }

                // Drop cached hash data for zips that are no longer referenced.
                HashSet<string> currentZipPaths = new HashSet<string>(
                    zipFiles.Values,
                    StringComparer.OrdinalIgnoreCase);

                foreach (string cachedZip in _zipInfo.Keys.ToList())
                {
                    if (!currentZipPaths.Contains(cachedZip))
                    {
                        _zipInfo.Remove(cachedZip);
                    }
                }

                try
                {
                    HashSet<string> keepNames = new HashSet<string>(
                        zipFiles.Keys,
                        StringComparer.OrdinalIgnoreCase);

                    foreach (string cachedZip in Directory.GetFiles(_config.cacheDir, "*.zip"))
                    {
                        if (!keepNames.Contains(Path.GetFileName(cachedZip)))
                        {
                            File.Delete(cachedZip);
                        }
                    }
                }
                catch (Exception exception)
                {
                    Log.Warn("Could not prune old cache zips: " + exception.Message);
                }

                entries.Sort((left, right) =>
                    string.Compare(left.SafeFolder, right.SafeFolder, StringComparison.OrdinalIgnoreCase));

                Publish(entries, zipFiles);
            }
            catch (Exception exception)
            {
                Log.Exception("Catalog refresh failed", exception);
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
        }

        public byte[] BuildManifest(string baseUrl)
        {
            baseUrl = (baseUrl ?? "").TrimEnd('/');
            List<PackEntry> entries;
            string version;
            string generatedAt;

            lock (_sync)
            {
                entries = _entries;
                version = _catalogVersion;
                generatedAt = _catalogGeneratedAt;

                if (_manifestCacheBytes != null &&
                    _manifestCacheVersion == version &&
                    string.Equals(_manifestCacheBase, baseUrl, StringComparison.Ordinal))
                {
                    return _manifestCacheBytes;
                }
            }

            List<Dictionary<string, object>> mods = new List<Dictionary<string, object>>();

            foreach (PackEntry entry in entries)
            {
                mods.Add(new Dictionary<string, object>
                {
                    ["name"] = entry.Name,
                    ["displayName"] = entry.DisplayName,
                    ["version"] = entry.Version,
                    ["url"] = baseUrl + "/mods/" + Uri.EscapeDataString(entry.ZipName),
                    ["sha256"] = entry.Sha256,
                    ["size"] = entry.Size,
                    ["target"] = "client",
                    ["installFolder"] = entry.SafeFolder,
                    ["required"] = true
                });
            }

            string packVersion = string.Equals(_config.packVersion, "auto", StringComparison.OrdinalIgnoreCase)
                ? "auto-" + version
                : _config.packVersion;

            Dictionary<string, object> manifest = new Dictionary<string, object>
            {
                ["schemaVersion"] = 1,
                ["packVersion"] = packVersion,
                ["gameVersion"] = _config.gameVersion,
                ["generatedAt"] = generatedAt,
                ["notes"] = _config.notes ?? "",
                ["mods"] = mods,
                ["remove"] = _config.remove
            };

            JavaScriptSerializer serializer = new JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue
            };

            string json = serializer.Serialize(manifest);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            lock (_sync)
            {
                if (_catalogVersion == version)
                {
                    _manifestCacheBase = baseUrl;
                    _manifestCacheVersion = version;
                    _manifestCacheBytes = bytes;
                }
            }

            return bytes;
        }

        public byte[] GetSignature(byte[] manifest)
        {
            if (_signingKey == null)
            {
                return null;
            }

            string key = ComputeSha256(manifest);

            lock (_sync)
            {
                byte[] cached;
                if (_signatureCache.TryGetValue(key, out cached))
                {
                    return cached;
                }
            }

            byte[] signature = _signingKey.SignData(
                manifest,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            lock (_sync)
            {
                if (_signatureCache.Count > 8)
                {
                    _signatureCache.Clear();
                }

                _signatureCache[key] = signature;
            }

            return signature;
        }

        public string GetZipPath(string fileName)
        {
            lock (_sync)
            {
                string path;
                return _zipFiles.TryGetValue(fileName, out path) && File.Exists(path) ? path : null;
            }
        }

        public int ModCount
        {
            get
            {
                lock (_sync)
                {
                    return _entries.Count;
                }
            }
        }

        public void Dispose()
        {
            if (_timer != null)
            {
                _timer.Dispose();
            }

            if (_signingKey != null)
            {
                _signingKey.Dispose();
            }
        }

        private void Publish(List<PackEntry> entries, Dictionary<string, string> zipFiles)
        {
            string version = ComputeCatalogVersion(entries);

            lock (_sync)
            {
                _entries = entries;
                _zipFiles = zipFiles;
                _catalogVersion = version;
                _catalogGeneratedAt = DateTime.UtcNow.ToString("o");
                _manifestCacheBase = null;
                _manifestCacheVersion = null;
                _manifestCacheBytes = null;
                _signatureCache.Clear();
            }

            Log.Info("Catalog refreshed: " + entries.Count + " mod(s), version " + version);
        }

        private List<string> GetCandidateFolders()
        {
            List<string> result = new List<string>();

            if (_config.include.Count > 0)
            {
                foreach (string name in _config.include)
                {
                    string path = Path.Combine(_config.modsPath, name);
                    if (Directory.Exists(path))
                    {
                        result.Add(path);
                    }
                    else
                    {
                        Log.Warn("Included mod folder not found: " + path);
                    }
                }

                return result;
            }

            foreach (string path in Directory.GetDirectories(_config.modsPath))
            {
                result.Add(path);
            }

            return result;
        }

        private bool IsExcluded(string folderName)
        {
            return _config.exclude.Any(
                value => string.Equals(value, folderName, StringComparison.OrdinalIgnoreCase));
        }

        private ModInfo ReadModInfo(string folder)
        {
            string folderName = Path.GetFileName(folder);
            string safeName = SanitizeFolder(folderName);

            string modInfoPath = Path.Combine(folder, "ModInfo.xml");
            if (File.Exists(modInfoPath))
            {
                try
                {
                    XmlDocument document = new XmlDocument();
                    document.Load(modInfoPath);
                    XmlElement root = document.DocumentElement;
                    if (root != null)
                    {
                        string name = GetValue(root, "Name");
                        if (!string.IsNullOrWhiteSpace(name) &&
                            System.Text.RegularExpressions.Regex.IsMatch(name, "^[0-9A-Za-z_\\-]+$"))
                        {
                            string displayName = GetValue(root, "DisplayName");
                            string version = GetValue(root, "Version");

                            return new ModInfo
                            {
                                Name = name,
                                DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName,
                                Version = string.IsNullOrWhiteSpace(version) ? "0.0.0" : version
                            };
                        }
                    }
                }
                catch
                {
                    // Fall back to the folder name below.
                }
            }

            // ModInfo.xml is optional. Pure Config/XUi/asset mods still load in
            // game, so serve them using the folder name as the identity.
            if (string.IsNullOrEmpty(safeName))
            {
                return null;
            }

            return new ModInfo
            {
                Name = safeName,
                DisplayName = folderName,
                Version = "0.0.0"
            };
        }

        private static string GetValue(XmlElement root, string name)
        {
            XmlNode node = root.SelectSingleNode(name);
            if (node == null)
            {
                // Older mods wrap the fields in <ModInfo>...</ModInfo>.
                node = root.SelectSingleNode("ModInfo/" + name);
            }

            if (node == null)
            {
                return "";
            }

            XmlElement element = node as XmlElement;
            if (element != null)
            {
                string value = element.GetAttribute("value");
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return (node.InnerText ?? "").Trim();
        }

        private static string SanitizeFolder(string name)
        {
            string safe = System.Text.RegularExpressions.Regex.Replace(name, "[^0-9A-Za-z_\\-]", "_");
            return safe.Trim('_');
        }

        private string ComputeFingerprint(string folder)
        {
            try
            {
                string[] files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);

                StringBuilder builder = new StringBuilder();
                foreach (string file in files)
                {
                    FileInfo info = new FileInfo(file);
                    string relative = file.Substring(folder.Length).TrimStart(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);

                    builder.Append(relative).Append('|')
                        .Append(info.Length).Append('|')
                        .Append(info.LastWriteTimeUtc.Ticks).Append('\n');
                }

                return ComputeSha256(Encoding.UTF8.GetBytes(builder.ToString()));
            }
            catch (Exception exception)
            {
                Log.Warn("Could not fingerprint " + folder + ": " + exception.Message);
                return DateTime.UtcNow.Ticks.ToString("x");
            }
        }

        private void CreateZip(string folder, string zipPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath));

            string temp = zipPath + ".tmp";

            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }

                ZipFile.CreateFromDirectory(folder, temp, CompressionLevel.Optimal, false);

                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }

                File.Move(temp, zipPath);
                Log.Info("Created zip " + Path.GetFileName(zipPath));
            }
            catch (Exception exception)
            {
                try
                {
                    if (File.Exists(temp))
                    {
                        File.Delete(temp);
                    }
                }
                catch
                {
                    // Best effort.
                }

                Log.Exception("Could not zip " + folder, exception);
            }
        }

        private static string ComputeCatalogVersion(List<PackEntry> entries)
        {
            StringBuilder builder = new StringBuilder();
            foreach (PackEntry entry in entries)
            {
                builder.Append(entry.Name).Append('|')
                    .Append(entry.Version).Append('|')
                    .Append(entry.Sha256).Append('\n');
            }

            string hash = ComputeSha256(Encoding.UTF8.GetBytes(builder.ToString()));
            return hash.Substring(0, 12);
        }

        private static string ComputeSha256(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(stream));
            }
        }

        private static string ComputeSha256(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(data));
            }
        }

        private static string ToHex(byte[] hash)
        {
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private sealed class ModInfo
        {
            public string Name;
            public string DisplayName;
            public string Version;
        }

        private sealed class ZipInfo
        {
            public string Sha256;
            public long Size;
            public long LastWriteTicks;
        }
    }
}
