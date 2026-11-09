using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace QLNPackBootstrap
{
    /// <summary>
    /// A staged update. "Apply" entries are folders that must be replaced at the
    /// next launch, before the game loads their DLLs. "State" is the complete
    /// list of installed pack mods after the update is applied.
    /// </summary>
    internal sealed class PackPending
    {
        public string PackVersion = "";
        public string CreatedAt = "";
        public string StageRoot = "";
        public List<PendingMod> Apply = new List<PendingMod>();
        public List<string> Remove = new List<string>();
        public List<InstalledMod> State = new List<InstalledMod>();

        public static PackPending Load(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(path);
                XmlElement root = document.DocumentElement;
                if (root == null)
                {
                    return null;
                }

                PackPending pending = new PackPending
                {
                    PackVersion = ReadElement(root, "PackVersion"),
                    CreatedAt = ReadElement(root, "CreatedAt"),
                    StageRoot = ReadElement(root, "StageRoot")
                };

                XmlNode applyNode = root.SelectSingleNode("Apply");
                if (applyNode != null)
                {
                    foreach (XmlNode node in applyNode.SelectNodes("Mod"))
                    {
                        XmlElement element = node as XmlElement;
                        if (element == null)
                        {
                            continue;
                        }

                        pending.Apply.Add(new PendingMod
                        {
                            Folder = ReadElement(element, "Folder"),
                            Version = ReadElement(element, "Version"),
                            Sha256 = ReadElement(element, "Sha256"),
                            DisplayName = ReadElement(element, "DisplayName")
                        });
                    }
                }

                XmlNode removeNode = root.SelectSingleNode("Remove");
                if (removeNode != null)
                {
                    foreach (XmlNode node in removeNode.SelectNodes("Folder"))
                    {
                        string folder = node.InnerText == null ? "" : node.InnerText.Trim();
                        if (folder.Length > 0)
                        {
                            pending.Remove.Add(folder);
                        }
                    }
                }

                XmlNode stateNode = root.SelectSingleNode("State");
                if (stateNode != null)
                {
                    foreach (XmlNode node in stateNode.SelectNodes("Mod"))
                    {
                        XmlElement element = node as XmlElement;
                        if (element == null)
                        {
                            continue;
                        }

                        pending.State.Add(new InstalledMod
                        {
                            Folder = ReadElement(element, "Folder"),
                            Version = ReadElement(element, "Version"),
                            Sha256 = ReadElement(element, "Sha256")
                        });
                    }
                }

                return pending;
            }
            catch
            {
                return null;
            }
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            XmlDocument document = new XmlDocument();
            XmlElement root = document.CreateElement("PendingUpdate");
            document.AppendChild(root);

            AppendElement(document, root, "PackVersion", PackVersion ?? "");
            AppendElement(document, root, "CreatedAt", CreatedAt ?? DateTime.UtcNow.ToString("o"));
            AppendElement(document, root, "StageRoot", StageRoot ?? "");

            XmlElement apply = document.CreateElement("Apply");
            root.AppendChild(apply);
            foreach (PendingMod mod in Apply)
            {
                XmlElement element = document.CreateElement("Mod");
                AppendElement(document, element, "Folder", mod.Folder ?? "");
                AppendElement(document, element, "Version", mod.Version ?? "");
                AppendElement(document, element, "Sha256", mod.Sha256 ?? "");
                AppendElement(document, element, "DisplayName", mod.DisplayName ?? "");
                apply.AppendChild(element);
            }

            XmlElement remove = document.CreateElement("Remove");
            root.AppendChild(remove);
            foreach (string folder in Remove)
            {
                AppendElement(document, remove, "Folder", folder ?? "");
            }

            XmlElement state = document.CreateElement("State");
            root.AppendChild(state);
            foreach (InstalledMod mod in State)
            {
                XmlElement element = document.CreateElement("Mod");
                AppendElement(document, element, "Folder", mod.Folder ?? "");
                AppendElement(document, element, "Version", mod.Version ?? "");
                AppendElement(document, element, "Sha256", mod.Sha256 ?? "");
                state.AppendChild(element);
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

    internal sealed class PendingMod
    {
        public string Folder;
        public string Version;
        public string Sha256;
        public string DisplayName;
    }
}
