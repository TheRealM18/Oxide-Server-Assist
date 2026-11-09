using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace QLNPackBootstrap
{
    internal sealed class PackState
    {
        public string PackVersion = "";
        public string ActivePackId = "";
        public string InstalledAt = "";
        public List<InstalledMod> Mods = new List<InstalledMod>();

        public static PackState Load(string path)
        {
            PackState state = new PackState();
            if (!File.Exists(path))
            {
                return state;
            }

            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(path);
                XmlElement root = document.DocumentElement;
                if (root == null)
                {
                    return state;
                }

                state.PackVersion = ReadElement(root, "PackVersion");
                state.ActivePackId = ReadElement(root, "ActivePackId");
                state.InstalledAt = ReadElement(root, "InstalledAt");

                XmlNode modsNode = root.SelectSingleNode("Mods");
                if (modsNode != null)
                {
                    foreach (XmlNode node in modsNode.SelectNodes("Mod"))
                    {
                        XmlElement element = node as XmlElement;
                        if (element == null)
                        {
                            continue;
                        }

                        state.Mods.Add(new InstalledMod
                        {
                            Folder = ReadElement(element, "Folder"),
                            Version = ReadElement(element, "Version"),
                            Sha256 = ReadElement(element, "Sha256")
                        });
                    }
                }
            }
            catch
            {
                // A corrupt state file just means everything is treated as new.
            }

            return state;
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            XmlDocument document = new XmlDocument();
            XmlElement root = document.CreateElement("PackState");
            document.AppendChild(root);

            AppendElement(document, root, "PackVersion", PackVersion ?? "");
            AppendElement(document, root, "ActivePackId", ActivePackId ?? "");
            AppendElement(document, root, "InstalledAt", InstalledAt ?? DateTime.UtcNow.ToString("o"));

            XmlElement mods = document.CreateElement("Mods");
            root.AppendChild(mods);

            foreach (InstalledMod mod in Mods)
            {
                XmlElement element = document.CreateElement("Mod");
                AppendElement(document, element, "Folder", mod.Folder ?? "");
                AppendElement(document, element, "Version", mod.Version ?? "");
                AppendElement(document, element, "Sha256", mod.Sha256 ?? "");
                mods.AppendChild(element);
            }

            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = new UTF8Encoding(false)
            };

            using (XmlWriter writer = XmlWriter.Create(path, settings))
            {
                document.Save(writer);
            }
        }

        private static string ReadElement(XmlElement parent, string name)
        {
            XmlNode node = parent.SelectSingleNode(name);
            return node == null ? "" : (node.InnerText ?? "").Trim();
        }

        private static void AppendElement(XmlDocument document, XmlElement parent, string name, string value)
        {
            XmlElement element = document.CreateElement(name);
            element.InnerText = value;
            parent.AppendChild(element);
        }
    }

    internal sealed class InstalledMod
    {
        public string Folder;
        public string Version;
        public string Sha256;
    }
}
