using System;
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine.Scripting;

[Preserve]
public class XUiC_ServerAssistWindowGroup : XUiController
{
    public static XUiC_ServerAssistWindowGroup Find(XUi _xui)
    {
        XUiWindowGroup group = FindWindowGroup(_xui);
        return group != null ? group.Controller as XUiC_ServerAssistWindowGroup : null;
    }

    public static XUiC_ServerAssistWindow FindWindow(XUi _xui)
    {
        XUiWindowGroup group = FindWindowGroup(_xui);
        if (group == null)
        {
            return null;
        }

        XUiC_ServerAssistWindowGroup typedGroup = group.Controller as XUiC_ServerAssistWindowGroup;
        if (typedGroup != null)
        {
            XUiC_ServerAssistWindow window = typedGroup.GetChildByType<XUiC_ServerAssistWindow>();
            if (window != null)
            {
                return window;
            }
        }

        if (group.Windows != null)
        {
            for (int i = 0; i < group.Windows.Count; i++)
            {
                XUiV_Window view = group.Windows[i];
                if (view == null)
                {
                    continue;
                }

                XUiC_ServerAssistWindow window = view.Controller as XUiC_ServerAssistWindow;
                if (window != null)
                {
                    return window;
                }

                if (view.Controller != null &&
                    string.Equals(view.Controller.GetType().Name, "XUiC_ServerAssistWindow", StringComparison.Ordinal))
                {
                    return view.Controller as XUiC_ServerAssistWindow;
                }
            }
        }

        return null;
    }

    public static XUiWindowGroup FindWindowGroup(XUi _xui)
    {
        if (_xui == null)
        {
            return null;
        }

        // Prefer the actual XUiWindowGroup object. Looking up the controller
        // first is less reliable in some XUi builds because the controller's
        // WindowGroup back-reference can still be null during startup.
        if (_xui.WindowGroups != null)
        {
            for (int i = 0; i < _xui.WindowGroups.Count; i++)
            {
                XUiWindowGroup candidate = _xui.WindowGroups[i];
                if (candidate == null)
                {
                    continue;
                }

                if (string.Equals(candidate.Id, "serverAssist", System.StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }

                if (candidate.Controller is XUiC_ServerAssistWindowGroup)
                {
                    return candidate;
                }

                if (candidate.Controller != null &&
                    string.Equals(candidate.Controller.GetType().Name, "XUiC_ServerAssistWindowGroup", System.StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
        }

        XUiController direct = _xui.FindWindowGroupByName("serverAssist");
        if (direct == null)
        {
            direct = _xui.FindWindowGroupByName("serverassist");
        }

        if (direct != null && direct.WindowGroup != null)
        {
            return direct.WindowGroup;
        }

        XUiWindowGroup byId = _xui.GetWindowGroupById("serverAssist");
        if (byId == null)
        {
            byId = _xui.GetWindowGroupById("serverassist");
        }

        if (byId != null)
        {
            return byId;
        }

        // Last resort: XUi keeps a controller-type index that can contain a
        // group even when the name/ID lookup tables were rebuilt.
        try
        {
            FieldInfo indexField = typeof(XUi).GetField(
                "windowsAndGroupsByControllerType",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            IDictionary index = indexField != null ? indexField.GetValue(_xui) as IDictionary : null;
            if (index != null)
            {
                foreach (DictionaryEntry entry in index)
                {
                    IEnumerable controllers = entry.Value as IEnumerable;
                    if (controllers == null)
                    {
                        continue;
                    }

                    foreach (object item in controllers)
                    {
                        XUiController controller = item as XUiController;
                        if (controller == null ||
                            !string.Equals(controller.GetType().Name, "XUiC_ServerAssistWindowGroup", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (controller.WindowGroup != null)
                        {
                            return controller.WindowGroup;
                        }
                    }
                }
            }
        }
        catch (Exception exception)
        {
            Log.Warning("[ServerAssist] controller-index fallback failed: " + exception.Message);
        }

        return null;
    }

    public static void Open(XUi _xui)
    {
        XUiWindowGroup group = FindWindowGroup(_xui);
        if (group == null)
        {
            // Some XUi builds keep the group in the window-manager name map
            // even when the controller/group lookup tables do not expose it.
            try
            {
                _xui.playerUI.windowManager.Open("serverAssist", _bModal: true);
                Log.Out("[ServerAssist] opened window group through the name fallback");
                return;
            }
            catch (Exception exception)
            {
                Log.Warning("[ServerAssist] name fallback failed: " + exception.Message);
            }

            LogGroupDiagnostics(_xui);
            return;
        }

        _xui.playerUI.windowManager.Open(group, _bModal: true);
    }

    private static void LogGroupDiagnostics(XUi _xui)
    {
        StringBuilder builder = new StringBuilder();
        if (_xui != null && _xui.WindowGroups != null)
        {
            builder.Append("count=").Append(_xui.WindowGroups.Count);
            for (int i = 0; i < _xui.WindowGroups.Count; i++)
            {
                XUiWindowGroup group = _xui.WindowGroups[i];
                if (group == null)
                {
                    continue;
                }

                string id = group.Id ?? string.Empty;
                string controller = group.Controller != null ? group.Controller.GetType().Name : "null";
                if (id.IndexOf("admin", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    id.IndexOf("helper", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    controller.IndexOf("ServerAssist", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    builder.Append(" | ").Append(id).Append(" -> ").Append(controller);
                }
            }
        }
        else
        {
            builder.Append("WindowGroups=null");
        }

        Log.Warning("[ServerAssist] window group was not found. Is Config/XUi_InGame/xui.xml installed? " + builder);
    }

    public override void OnOpen()
    {
        base.OnOpen();
        Log.Out("[ServerAssist] window opened");
    }

    public override void OnClose()
    {
        base.OnClose();
        Log.Out("[ServerAssist] window closed");
    }
}



