using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using QLNPackBootstrap;

namespace CoreHarness
{
    internal static class Program
    {
        private static int _failures;

        private static int Main()
        {
            string root = Path.Combine(Path.GetTempPath(), "QLNPackCoreTest", Guid.NewGuid().ToString("N"));
            PackPaths.BootstrapDirOverride = Path.Combine(root, "Mods", "000_QLNPackBootstrap");
            Directory.CreateDirectory(PackPaths.BootstrapDir);

            try
            {
                TestJson();
                TestUrls();
                TestProviders();
                TestModsRoot();
                TestValidation();
                TestChanges();
                TestPackProfiles();
                TestPackSwitch();
                TestZipExtraction();
                TestFindModRoot();
                TestStagePending();
                TestSha256();
                TestSignature();
            }
            catch (Exception exception)
            {
                Console.WriteLine("UNHANDLED: " + exception);
                _failures++;
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }

            Console.WriteLine();
            Console.WriteLine(_failures == 0 ? "ALL TESTS PASSED" : _failures + " TEST(S) FAILED");
            return _failures == 0 ? 0 : 1;
        }

        private static void TestJson()
        {
            string json = "{" +
                          "\"schemaVersion\":1," +
                          "\"packVersion\":\"1.0.0\"," +
                          "\"gameVersion\":\"3.4\"," +
                          "\"mods\":[{" +
                          "\"name\":\"TestMod\"," +
                          "\"displayName\":\"Test Mod\"," +
                          "\"version\":\"1.2.3\"," +
                          "\"url\":\"https://github.com/x/y.zip\"," +
                          "\"sha256\":\"" + new string('a', 64) + "\"," +
                          "\"size\":123," +
                          "\"target\":\"client\"," +
                          "\"installFolder\":\"TestMod\"" +
                          "}]," +
                          "\"remove\":[]" +
                          "}";

            PackManifest manifest = JsonConvert.DeserializeObject<PackManifest>(json);
            Check("json manifest", manifest != null && manifest.Mods.Count == 1, "parse failed");
            Check("json fields",
                manifest != null &&
                manifest.PackVersion == "1.0.0" &&
                manifest.Mods[0].Name == "TestMod" &&
                manifest.Mods[0].Folder == "TestMod" &&
                manifest.Mods[0].IsClientSide,
                "field mismatch");

            PackManifest defaults = JsonConvert.DeserializeObject<PackManifest>(
                "{\"packVersion\":\"1.0.0\",\"mods\":[{\"name\":\"X\",\"version\":\"1\",\"url\":\"https://github.com/x\",\"sha256\":\"" +
                new string('a', 64) + "\",\"size\":1}]}");
            Check("json defaults",
                defaults != null &&
                defaults.Mods[0].Folder == "X" &&
                defaults.Mods[0].IsClientSide &&
                defaults.Mods[0].Target == "",
                "defaults mismatch");
        }

        private static void TestUrls()
        {
            Check("token no query",
                PackUrls.WithToken("http://server:8765/manifest.json", "a b") ==
                "http://server:8765/manifest.json?token=a%20b",
                "wrong url");

            Check("token existing query",
                PackUrls.WithToken("http://server:8765/manifest.json?x=1", "secret") ==
                "http://server:8765/manifest.json?x=1&token=secret",
                "wrong url");

            Check("token not duplicated",
                PackUrls.WithToken("http://server:8765/manifest.json?token=old", "new") ==
                "http://server:8765/manifest.json?token=old",
                "wrong url");

            Check("no token configured",
                PackUrls.WithToken("http://server:8765/manifest.json", "") ==
                "http://server:8765/manifest.json",
                "wrong url");
        }

        private static void TestProviders()
        {
            string drive = PackDownloadResolver.Normalize(
                "https://drive.google.com/file/d/ABC123_-xyz/view?usp=sharing");
            Check("google drive direct",
                drive == "https://drive.usercontent.google.com/download?id=ABC123_-xyz&export=download&confirm=t",
                drive);

            string driveOpen = PackDownloadResolver.Normalize(
                "https://drive.google.com/open?id=FILE_42");
            Check("google drive open id",
                driveOpen.IndexOf("id=FILE_42", StringComparison.Ordinal) >= 0,
                driveOpen);

            string driveKey = PackDownloadResolver.Normalize(
                "https://drive.google.com/file/d/ABC123/view?resourcekey=0-abc_123");
            Check("google drive resource key",
                driveKey.IndexOf("resourcekey=0-abc_123", StringComparison.Ordinal) >= 0,
                driveKey);

            string dropbox = PackDownloadResolver.Normalize(
                "https://www.dropbox.com/scl/fi/abc/TestMod.zip?rlkey=key123&dl=0");
            Check("dropbox dl=1",
                dropbox.IndexOf("dl=1", StringComparison.Ordinal) >= 0 &&
                dropbox.IndexOf("rlkey=key123", StringComparison.Ordinal) >= 0,
                dropbox);

            string dropboxNoDl = PackDownloadResolver.Normalize(
                "https://www.dropbox.com/s/abc/TestMod.zip");
            Check("dropbox adds dl=1",
                dropboxNoDl.IndexOf("dl=1", StringComparison.Ordinal) >= 0,
                dropboxNoDl);

            string oneDrive = PackDownloadResolver.Normalize(
                "https://1drv.ms/u/s!AbCdEf");
            Check("onedrive download=1",
                oneDrive.IndexOf("download=1", StringComparison.Ordinal) >= 0,
                oneDrive);

            string html = "<html><body>" +
                          "<form id=\"download-form\" action=\"https://drive.usercontent.google.com/download\">" +
                          "<input type=\"hidden\" name=\"id\" value=\"FILEID\">" +
                          "<input type=\"hidden\" name=\"export\" value=\"download\">" +
                          "<input type=\"hidden\" name=\"confirm\" value=\"t\">" +
                          "<input type=\"hidden\" name=\"uuid\" value=\"UUID-1\">" +
                          "</form></body></html>";

            string next;
            bool resolved = PackDownloadResolver.TryResolveGoogleDriveHtml(html, "https://drive.google.com/file/d/FILEID/view", out next);
            Check("google drive confirm page", resolved, "not resolved");
            Check("google drive confirm url",
                resolved &&
                next.IndexOf("id=FILEID", StringComparison.Ordinal) >= 0 &&
                next.IndexOf("confirm=t", StringComparison.Ordinal) >= 0 &&
                next.IndexOf("uuid=UUID-1", StringComparison.Ordinal) >= 0,
                next ?? "");

            Check("html detection",
                PackDownloadResolver.IsHtml(Encoding.UTF8.GetBytes("  <!DOCTYPE html><html>")) &&
                !PackDownloadResolver.IsHtml(new byte[] { 0x50, 0x4B, 0x03, 0x04 }),
                "html detection failed");

            Check("plain url unchanged",
                PackDownloadResolver.Normalize("https://example.com/file.zip") ==
                "https://example.com/file.zip",
                "plain url changed");
        }

        private static void TestModsRoot()
        {
            PackPaths.ModsRootOverride = "CustomMods";
            string expectedRelative = Path.GetFullPath(
                Path.Combine(PackPaths.BootstrapDir, "CustomMods"));
            Check("mods root relative override", PackPaths.ModsRoot == expectedRelative, PackPaths.ModsRoot);

            string absolute = Path.Combine(Path.GetTempPath(), "QLNPackAbsoluteMods");
            PackPaths.ModsRootOverride = absolute;
            Check("mods root absolute override",
                PackPaths.ModsRoot == Path.GetFullPath(absolute),
                PackPaths.ModsRoot);

            PackPaths.ModsRootOverride = null;

            string expectedDefault = Path.GetDirectoryName(PackPaths.BootstrapDir);
            Check("mods root default", PackPaths.ModsRoot == expectedDefault, PackPaths.ModsRoot);
        }

        private static void TestValidation()
        {
            PackConfig config = NewConfig();
            PackManifest manifest = NewManifest();
            string error;
            Check("valid manifest", PackInstaller.ValidateManifest(manifest, config, out error), error);

            PackManifest bad = NewManifest();
            bad.Mods[0].InstallFolder = "../evil";
            Check("reject path traversal", !PackInstaller.ValidateManifest(bad, config, out error), "should reject");

            bad = NewManifest();
            bad.PackVersion = "..";
            Check("reject pack version traversal", !PackInstaller.ValidateManifest(bad, config, out error), "should reject");

            bad = NewManifest();
            bad.Mods[0].Sha256 = "nope";
            Check("reject bad hash", !PackInstaller.ValidateManifest(bad, config, out error), "should reject");

            bad = NewManifest();
            bad.Mods[0].Url = "http://github.com/x/y.zip";
            Check("reject http by default", !PackInstaller.ValidateManifest(bad, config, out error), "should reject");

            config.AllowInsecureHttp = true;
            Check("allow http when configured", PackInstaller.ValidateManifest(bad, config, out error), error);
        }

        private static void TestChanges()
        {
            PackConfig config = NewConfig();
            PackManifest manifest = NewManifest();
            string error;

            List<ModChange> changes = PackInstaller.ComputeChanges(manifest, new PackState(), config, out error);
            Check("new mod detected", changes != null && changes.Count == 1 && changes[0].Kind == ModChangeKind.New, error);

            Directory.CreateDirectory(PackInstaller.GetTargetPath("TestMod"));
            File.WriteAllText(Path.Combine(PackInstaller.GetTargetPath("TestMod"), "ModInfo.xml"), "<xml />");
            PackState state = new PackState
            {
                PackVersion = manifest.PackVersion,
                Mods = new List<InstalledMod>
                {
                    new InstalledMod
                    {
                        Folder = "TestMod",
                        Version = manifest.Mods[0].Version,
                        Sha256 = manifest.Mods[0].Sha256
                    }
                }
            };

            changes = PackInstaller.ComputeChanges(manifest, state, config, out error);
            Check("up-to-date mod ignored", changes != null && changes.Count == 0, error);

            // ModInfo.xml is optional for mods that only ship Config/XUi/assets.
            File.Delete(Path.Combine(PackInstaller.GetTargetPath("TestMod"), "ModInfo.xml"));
            Directory.CreateDirectory(Path.Combine(PackInstaller.GetTargetPath("TestMod"), "Config"));
            File.WriteAllText(
                Path.Combine(PackInstaller.GetTargetPath("TestMod"), "Config", "items.xml"),
                "<xml />");

            changes = PackInstaller.ComputeChanges(manifest, state, config, out error);
            Check("up-to-date mod without ModInfo ignored", changes != null && changes.Count == 0, error);
        }

        private static void TestZipExtraction()
        {
            string zipPath = Path.Combine(PackPaths.DataDir, "test.zip");
            Directory.CreateDirectory(PackPaths.DataDir);

            using (FileStream stream = File.Create(zipPath))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "ModInfo.xml", "<xml />");
                WriteEntry(archive, "sub/file.txt", "hello");
            }

            string extractDir = Path.Combine(PackPaths.DataDir, "extract");
            PackInstaller.ExtractZipSafe(zipPath, extractDir, 100, 1024 * 1024);
            Check("extract ModInfo", File.Exists(Path.Combine(extractDir, "ModInfo.xml")), "missing");
            Check("extract subfolder", File.Exists(Path.Combine(extractDir, "sub", "file.txt")), "missing");

            string badZip = Path.Combine(PackPaths.DataDir, "bad.zip");
            using (FileStream stream = File.Create(badZip))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "../evil.txt", "bad");
            }

            bool rejected = false;
            try
            {
                PackInstaller.ExtractZipSafe(badZip, Path.Combine(PackPaths.DataDir, "bad"), 100, 1024 * 1024);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }

            Check("reject zip slip", rejected, "should throw");
        }

        private static void TestFindModRoot()
        {
            string root = Path.Combine(PackPaths.DataDir, "root-test");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "ModInfo.xml"), "<xml />");
            Check("find root ModInfo", PackInstaller.FindModRoot(root) == root, "wrong root");

            string nestedRoot = Path.Combine(PackPaths.DataDir, "nested-test");
            string nested = Path.Combine(nestedRoot, "TestMod");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "ModInfo.xml"), "<xml />");
            Check("find nested ModInfo", PackInstaller.FindModRoot(nestedRoot) == nested, "wrong nested root");

            string plainRoot = Path.Combine(PackPaths.DataDir, "plain-root-test");
            Directory.CreateDirectory(plainRoot);
            File.WriteAllText(Path.Combine(plainRoot, "Config.xml"), "<xml />");
            Check("find root without ModInfo", PackInstaller.FindModRoot(plainRoot) == plainRoot, "wrong plain root");

            string wrappedRoot = Path.Combine(PackPaths.DataDir, "wrapped-no-modinfo-test");
            string wrapped = Path.Combine(wrappedRoot, "WrappedMod");
            Directory.CreateDirectory(wrapped);
            File.WriteAllText(Path.Combine(wrapped, "Config.xml"), "<xml />");
            Check("unwrap single folder without ModInfo", PackInstaller.FindModRoot(wrappedRoot) == wrapped, "wrong wrapped root");

            string mixedRoot = Path.Combine(PackPaths.DataDir, "mixed-no-modinfo-test");
            Directory.CreateDirectory(Path.Combine(mixedRoot, "WrappedMod"));
            File.WriteAllText(Path.Combine(mixedRoot, "readme.txt"), "root file");
            File.WriteAllText(Path.Combine(mixedRoot, "WrappedMod", "Config.xml"), "<xml />");
            Check("keep root when wrapper has root files", PackInstaller.FindModRoot(mixedRoot) == mixedRoot, "wrong mixed root");

            string contentRoot = Path.Combine(PackPaths.DataDir, "config-no-modinfo-test");
            Directory.CreateDirectory(Path.Combine(contentRoot, "Config"));
            File.WriteAllText(Path.Combine(contentRoot, "Config", "items.xml"), "<xml />");
            Check("keep Config folder as mod content", PackInstaller.FindModRoot(contentRoot) == contentRoot, "wrong content root");
        }

        private static void TestStagePending()
        {
            PackConfig config = NewConfig();
            PackManifest manifest = NewManifest();
            string stageRoot = Path.Combine(PackPaths.StagingDir, manifest.PackVersion);
            string stagedMod = Path.Combine(stageRoot, "TestMod");
            Directory.CreateDirectory(stagedMod);
            File.WriteAllText(Path.Combine(stagedMod, "ModInfo.xml"), "<xml />");

            string error;
            List<ModChange> changes = PackInstaller.ComputeChanges(manifest, new PackState(), config, out error);
            if (changes == null || changes.Count != 1)
            {
                Check("stage changes", false, error);
                return;
            }

            PackPending pending;
            bool ok = PackInstaller.StagePending(manifest, changes, stageRoot, config, out pending, out error);
            Check("stage pending", ok, error);
            Check("pending file exists", File.Exists(PackPaths.PendingFile), "missing");
            Check("pending has one mod", pending != null && pending.Apply.Count == 1, "wrong count");
            Check("staged folder preserved", Directory.Exists(stagedMod), "missing");

            PackPending loaded = PackPending.Load(PackPaths.PendingFile);
            Check("pending round trip", loaded != null && loaded.Apply.Count == 1, "load failed");

            // Clean up the target created by TestChanges so later runs are independent.
            try { Directory.Delete(PackInstaller.GetTargetPath("TestMod"), true); } catch { }
        }

        private static void TestSha256()
        {
            string path = Path.Combine(PackPaths.DataDir, "hash.txt");
            File.WriteAllText(path, "hello", new UTF8Encoding(false));

            string expected;
            using (SHA256 sha = SHA256.Create())
            {
                expected = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            }

            Check("sha256", PackInstaller.ComputeSha256(path) == expected, "hash mismatch");
        }

        private static void TestSignature()
        {
            byte[] data = Encoding.UTF8.GetBytes("{\"packVersion\":\"1.0.0\"}");

            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(3072))
            {
                rsa.PersistKeyInCsp = false;
                string publicKey = rsa.ToXmlString(false);
                byte[] signature = rsa.SignData(
                    data,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);

                string error;
                Check("signature valid", PackSignature.Verify(data, signature, publicKey, out error), error);

                byte[] tampered = Encoding.UTF8.GetBytes("{\"packVersion\":\"1.0.1\"}");
                Check("signature rejects tampering",
                    !PackSignature.Verify(tampered, signature, publicKey, out error),
                    "should reject");
            }
        }

        private static void TestPackProfiles()
        {
            File.WriteAllText(
                PackPaths.ConfigFile,
                "<PackBootstrap>" +
                "<ManifestUrl>https://github.com/OWNER/REPO/releases/latest/download/manifest.json</ManifestUrl>" +
                "</PackBootstrap>",
                new UTF8Encoding(false));

            PackConfig legacy = PackConfig.Load(PackPaths.ConfigFile);
            Check("legacy pack profile", legacy.Packs.Count == 2, "expected vanilla + default");
            Check("legacy default id", legacy.DefaultPackId == "default", legacy.DefaultPackId);
            Check("legacy vanilla", legacy.FindPack("vanilla") != null && legacy.FindPack("vanilla").IsVanilla, "missing vanilla");

            File.WriteAllText(
                PackPaths.ConfigFile,
                "<PackBootstrap>" +
                "<DefaultPackId>qln</DefaultPackId>" +
                "<Packs>" +
                "<Pack><Id>qln</Id><Name>QLN</Name><ManifestUrl>https://example.com/qln.json</ManifestUrl></Pack>" +
                "<Pack><Id>other</Id><Name>Other</Name><ManifestUrl>https://example.com/other.json</ManifestUrl></Pack>" +
                "</Packs>" +
                "</PackBootstrap>",
                new UTF8Encoding(false));

            PackConfig multi = PackConfig.Load(PackPaths.ConfigFile);
            Check("multi pack count", multi.Packs.Count == 3, "expected vanilla + two packs");
            Check("multi pack default", multi.DefaultPackId == "qln", multi.DefaultPackId);
            Check("multi pack names",
                multi.FindPack("qln") != null && multi.FindPack("qln").Name == "QLN" &&
                multi.FindPack("other") != null,
                "profiles missing");

            File.Delete(PackPaths.ConfigFile);
        }

        private static void TestPackSwitch()
        {
            PackConfig config = NewConfig();
            config.DefaultPackId = "packA";

            string packA = PackInstaller.GetTargetPath("PackAMod");
            string shared = PackInstaller.GetTargetPath("SharedMod");
            Directory.CreateDirectory(packA);
            Directory.CreateDirectory(shared);
            File.WriteAllText(Path.Combine(packA, "Config.xml"), "<xml />");
            File.WriteAllText(Path.Combine(shared, "Config.xml"), "<xml />");

            PackState state = new PackState
            {
                PackVersion = "1.0.0",
                ActivePackId = "packA",
                Mods = new List<InstalledMod>
                {
                    new InstalledMod { Folder = "PackAMod", Version = "1.0.0", Sha256 = new string('a', 64) },
                    new InstalledMod { Folder = "SharedMod", Version = "1.0.0", Sha256 = new string('b', 64) }
                }
            };

            PackManifest target = new PackManifest
            {
                PackVersion = "2.0.0",
                Mods = new List<PackModEntry>
                {
                    new PackModEntry
                    {
                        Name = "SharedMod",
                        Version = "1.0.0",
                        Sha256 = new string('b', 64),
                        Size = 10,
                        InstallFolder = "SharedMod"
                    },
                    new PackModEntry
                    {
                        Name = "PackBMod",
                        Version = "1.0.0",
                        Sha256 = new string('c', 64),
                        Size = 10,
                        InstallFolder = "PackBMod"
                    }
                }
            };

            string error;
            List<ModChange> changes = PackInstaller.ComputeChanges(target, state, config, "packB", out error);
            int removes = changes == null ? -1 : changes.Count(change => change.Kind == ModChangeKind.Remove);
            int adds = changes == null ? -1 : changes.Count(change => change.Kind != ModChangeKind.Remove);
            Check("pack switch changes", changes != null && removes == 1 && adds == 1, error);
            Check("pack switch removes old",
                changes != null && changes.Any(change => change.Kind == ModChangeKind.Remove && change.Folder == "PackAMod"),
                "PackAMod should be removed");
            Check("pack switch keeps shared",
                changes != null && !changes.Any(change => change.Folder == "SharedMod"),
                "SharedMod should be kept");

            changes = PackInstaller.ComputeChanges(null, state, config, "vanilla", out error);
            Check("vanilla removes managed mods",
                changes != null && changes.Count(change => change.Kind == ModChangeKind.Remove) == 2,
                error);

            string stageRoot = Path.Combine(PackPaths.StagingDir, "vanilla-test");
            Directory.CreateDirectory(stageRoot);
            PackPending pending = null;
            bool staged = changes != null &&
                          PackInstaller.StagePending(null, changes, stageRoot, config, "vanilla", out pending, out error);
            Check("vanilla stage active pack",
                staged && pending != null && pending.ActivePackId == "vanilla" &&
                pending.Apply.Count == 0 && pending.Remove.Count == 2,
                error);

            try { File.Delete(PackPaths.PendingFile); } catch { }

            try { Directory.Delete(packA, true); } catch { }
            try { Directory.Delete(shared, true); } catch { }
            try { Directory.Delete(stageRoot, true); } catch { }
        }

        private static PackConfig NewConfig()
        {
            PackConfig config = new PackConfig
            {
                ManifestUrl = "https://github.com/OWNER/REPO/releases/latest/download/manifest.json",
                AllowInsecureHttp = false,
                MaxModBytes = 10 * 1024 * 1024,
                MaxTotalBytes = 50 * 1024 * 1024,
                MaxEntries = 1000,
                MaxUncompressedBytes = 50 * 1024 * 1024
            };
            config.AllowedHosts.Add("github.com");
            config.AllowedHosts.Add("objects.githubusercontent.com");
            return config;
        }

        private static PackManifest NewManifest()
        {
            return new PackManifest
            {
                PackVersion = "1.0.0",
                GameVersion = "3.4",
                Mods = new List<PackModEntry>
                {
                    new PackModEntry
                    {
                        Name = "TestMod",
                        DisplayName = "Test Mod",
                        Version = "1.2.3",
                        Url = "https://github.com/OWNER/REPO/releases/download/v1.0.0/TestMod.zip",
                        Sha256 = new string('a', 64),
                        Size = 100,
                        Target = "client",
                        InstallFolder = "TestMod",
                        Required = true
                    }
                },
                Remove = new List<string>()
            };
        }

        private static void WriteEntry(ZipArchive archive, string name, string content)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name);
            using (Stream stream = entry.Open())
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
            }
        }

        private static void Check(string name, bool condition, string detail)
        {
            if (condition)
            {
                Console.WriteLine("PASS: " + name);
            }
            else
            {
                Console.WriteLine("FAIL: " + name + " - " + (detail ?? ""));
                _failures++;
            }
        }
    }
}
