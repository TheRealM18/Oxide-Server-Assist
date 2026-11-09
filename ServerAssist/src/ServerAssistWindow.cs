using System;
using ServerAssistMod;
using UnityEngine;
using UnityEngine.Scripting;

[Preserve]
public class XUiC_ServerAssistWindow : XUiController
{
    private const int RowCount = 10;
    private const int ButtonCount = 12;
    private const string FallbackIcon = "ui_game_symbol_hammer";

    private XUiV_Label titleLabel;
    private XUiV_Label subtitleLabel;
    private XUiV_Label statusLabel;
    private XUiV_Label inputLabel;

    private XUiC_TextInput input;
    private XUiC_SimpleButton submitButton;
    private XUiC_SimpleButton cancelButton;
    private XUiC_SimpleButton closeButton;
    private XUiC_SimpleButton refreshButton;
    private XUiC_SimpleButton hookBackButton;

    private XUiC_SimpleButton tabHome;
    private XUiC_SimpleButton tabShared;
    private XUiC_SimpleButton tabMine;

    private XUiV_Sprite panelBackground;
    private XUiV_Sprite panelBanner;
    private XUiV_Sprite submitHover;
    private XUiV_Sprite cancelHover;
    private XUiV_Sprite closeHover;
    private XUiV_Sprite refreshHover;
    private XUiV_Sprite hookBackHover;
    private XUiV_Sprite tabHomeHover;
    private XUiV_Sprite tabSharedHover;
    private XUiV_Sprite tabMineHover;

    private readonly XUiC_SimpleButton[] rowButtons = new XUiC_SimpleButton[RowCount];
    private readonly XUiV_Sprite[] rowIcons = new XUiV_Sprite[RowCount];
    private readonly XUiV_Sprite[] rowHovers = new XUiV_Sprite[RowCount];
    private readonly XUiV_Label[] rowLabels = new XUiV_Label[RowCount];
    private readonly XUiV_Label[] rowSubs = new XUiV_Label[RowCount];
    private readonly XUiC_SimpleButton[] actionButtons = new XUiC_SimpleButton[ButtonCount];
    private readonly XUiV_Sprite[] actionHovers = new XUiV_Sprite[ButtonCount];
    private readonly XUiV_Sprite[] actionIcons = new XUiV_Sprite[ButtonCount];
    private readonly XUiV_Label[] actionLabels = new XUiV_Label[ButtonCount];
    private readonly string[] rowActions = new string[RowCount];
    private readonly string[] buttonActions = new string[ButtonCount];

    private bool cardTextFallback;
    private bool applying;
    private bool hookChromeActive;
    private bool providerActive;

    public override void Init()
    {
        base.Init();

        // The header label is the static SERVER ASSIST brand. The provider's
        // title is rendered in the content pane.
        titleLabel = Label("contentTitle");
        subtitleLabel = Label("subtitle");
        statusLabel = Label("status");
        inputLabel = Label("inputLabel");
        panelBackground = ChildView<XUiV_Sprite>(this, "panelBackground");
        panelBanner = ChildView<XUiV_Sprite>(this, "panelBanner");

        input = GetChildById("txtInput") as XUiC_TextInput;
        if (input != null)
        {
            input.OnSubmitHandler += OnInputSubmit;
        }

        submitButton = Button("btnSubmit");
        if (submitButton != null)
        {
            submitButton.OnPressed += (sender, mouseButton) => SubmitInput();
            submitHover = ChildView<XUiV_Sprite>(submitButton, "hover");
            AttachHover(submitButton, submitHover);
        }

        cancelButton = Button("btnCancelInput");
        if (cancelButton != null)
        {
            cancelButton.OnPressed += (sender, mouseButton) => Send("a|cancel");
            cancelHover = ChildView<XUiV_Sprite>(cancelButton, "hover");
            AttachHover(cancelButton, cancelHover);
        }

        closeButton = Button("btnCloseWindow");
        if (closeButton != null)
        {
            closeButton.OnPressed += (sender, mouseButton) => CloseWindow();
            closeHover = ChildView<XUiV_Sprite>(closeButton, "hover");
            AttachHover(closeButton, closeHover);
        }

        refreshButton = Button("btnRefresh");
        if (refreshButton != null)
        {
            refreshButton.OnPressed += (sender, mouseButton) => Send("a|refresh");
            refreshHover = ChildView<XUiV_Sprite>(refreshButton, "hover");
            AttachHover(refreshButton, refreshHover);
        }

        hookBackButton = Button("btnHookBack");
        if (hookBackButton != null)
        {
            hookBackButton.OnPressed += (sender, mouseButton) => Send("a|hook|back");
            hookBackHover = ChildView<XUiV_Sprite>(hookBackButton, "hover");
            AttachHover(hookBackButton, hookBackHover);
        }

        tabHome = Button("tabHome");
        if (tabHome != null)
        {
            tabHome.OnPressed += (sender, mouseButton) => Send("a|tab|home");
            tabHomeHover = ChildView<XUiV_Sprite>(tabHome, "hover");
            AttachHover(tabHome, tabHomeHover);
        }

        tabShared = Button("tabShared");
        if (tabShared != null)
        {
            tabShared.OnPressed += (sender, mouseButton) => Send("a|tab|shared");
            tabSharedHover = ChildView<XUiV_Sprite>(tabShared, "hover");
            AttachHover(tabShared, tabSharedHover);
        }

        tabMine = Button("tabMine");
        if (tabMine != null)
        {
            tabMine.OnPressed += (sender, mouseButton) => Send("a|tab|mine");
            tabMineHover = ChildView<XUiV_Sprite>(tabMine, "hover");
            AttachHover(tabMine, tabMineHover);
        }

        for (int i = 0; i < RowCount; i++)
        {
            int index = i;
            rowButtons[i] = Button("row" + i);
            if (rowButtons[i] != null)
            {
                rowButtons[i].OnPressed += (sender, mouseButton) => OnRowPressed(index, mouseButton);
                rowIcons[i] = ChildView<XUiV_Sprite>(rowButtons[i], "icon");
                rowHovers[i] = ChildView<XUiV_Sprite>(rowButtons[i], "hover");
                AttachHover(rowButtons[i], rowHovers[i]);
                rowLabels[i] = ChildView<XUiV_Label>(rowButtons[i], "label");
                rowSubs[i] = ChildView<XUiV_Label>(rowButtons[i], "sub");
                if (rowLabels[i] == null)
                {
                    cardTextFallback = true;
                }
            }

            actionButtons[i] = Button("btn" + i);
            if (actionButtons[i] != null)
            {
                actionButtons[i].OnPressed += (sender, mouseButton) => OnActionPressed(index);
                actionHovers[i] = ChildView<XUiV_Sprite>(actionButtons[i], "hover");
                AttachHover(actionButtons[i], actionHovers[i]);
                actionIcons[i] = ChildView<XUiV_Sprite>(actionButtons[i], "icon");
                actionLabels[i] = ChildView<XUiV_Label>(actionButtons[i], "btnLabel");
            }
        }

        int iconViews = 0;
        for (int i = 0; i < RowCount; i++)
        {
            if (rowIcons[i] != null)
            {
                iconViews++;
            }
        }

        Log.Out("[ServerAssist] XUi window initialized (cards=" + RowCount + ", iconViews=" + iconViews +
                ", textFallback=" + cardTextFallback + ")");
    }

    public override void OnOpen()
    {
        base.OnOpen();

        if (ServerAssistClient.HasPending)
        {
            ApplyPayload(ServerAssistClient.TakePending());
            return;
        }

        Send("a|open");
    }

    public void ApplyPayload(string payload)
    {
        ServerAssistView view = ServerAssistView.Parse(payload);
        Apply(view);

        if (view.HasLaunch)
        {
            ServerAssistClient.ExecuteLaunch(view, this);
        }
    }

    public void ShowLocalError(string message)
    {
        if (statusLabel != null)
        {
            statusLabel.Text = message ?? string.Empty;
        }
    }

    private void Apply(ServerAssistView view)
    {
        applying = true;

        if (titleLabel != null)
        {
            titleLabel.Text = view.Title;
        }

        if (subtitleLabel != null)
        {
            subtitleLabel.Text = view.Subtitle;
        }

        if (statusLabel != null)
        {
            statusLabel.Text = view.Status;
        }

        ApplyPanelImage(view.Background, panelBackground, 0.12f);
        ApplyPanelImage(view.Banner, panelBanner, 1f);

        bool hookChrome = string.Equals(view.Chrome, "hook", StringComparison.OrdinalIgnoreCase);
        hookChromeActive = hookChrome;
        providerActive = !string.IsNullOrEmpty(view.Provider);
        SetTab(tabHome, view.Tab1Label, view.ActiveTab == "home");
        SetTab(tabShared, view.Tab2Label, view.ActiveTab == "shared");
        SetTab(tabMine, view.Tab3Label, view.ActiveTab == "mine");
        SetVisible(tabHome, !hookChrome);
        SetVisible(tabShared, !hookChrome);
        SetVisible(tabMine, !hookChrome);
        SetVisible(refreshButton, !hookChrome);
        SetVisible(hookBackButton, hookChrome);

        bool showInput = view.InputVisible;
        if (inputLabel != null)
        {
            inputLabel.Text = view.InputLabel;
            inputLabel.IsVisible = showInput;
        }

        if (input != null)
        {
            input.ViewComponent.IsVisible = showInput;
            input.Enabled = showInput;
            if (showInput)
            {
                input.Text = view.InputValue;
                if (!input.IsSelected)
                {
                    input.SetSelected(true, true);
                }
            }
        }

        SetVisible(submitButton, showInput);
        SetVisible(cancelButton, showInput);

        for (int i = 0; i < RowCount; i++)
        {
            ServerAssistRow row = i < view.Rows.Count ? view.Rows[i] : null;
            XUiC_SimpleButton button = rowButtons[i];
            if (button == null)
            {
                continue;
            }

            if (row == null || (string.IsNullOrEmpty(row.Label) && string.IsNullOrEmpty(row.Action)))
            {
                rowActions[i] = null;
                Hide(button);
                continue;
            }

            rowActions[i] = row.Action;

            if (cardTextFallback)
            {
                string text = (row.Selected ? "> " : "  ") + (row.Label ?? string.Empty);
                if (!string.IsNullOrEmpty(row.Sub))
                {
                    text += "   " + row.Sub;
                }

                button.Text = text;
            }
            else
            {
                button.Text = string.Empty;
                if (rowLabels[i] != null)
                {
                    SetLabel(rowLabels[i], row.Label, row.Enabled, true);
                }

                if (rowSubs[i] != null)
                {
                    SetLabel(rowSubs[i], row.Sub, row.Enabled, false);
                }
            }

            string sprite;
            string atlas;
            Color tint;
            ResolveIcon(row.Icon, row.IconTint, out sprite, out atlas, out tint);
            if (!row.Enabled)
            {
                tint.a *= 0.45f;
            }

            if (rowIcons[i] != null)
            {
                rowIcons[i].UIAtlas = atlas;
                rowIcons[i].SpriteName = sprite;
                rowIcons[i].Color = tint;
                rowIcons[i].IsVisible = true;
            }

            if (!cardTextFallback && rowLabels[i] != null && row.Selected)
            {
                rowLabels[i].Color = new Color(1f, 0.86f, 0.35f, 1f);
            }

            button.Enabled = row.Enabled;
            button.Tooltip = string.IsNullOrEmpty(row.Badge) ? row.Sub : row.Badge + "  |  " + row.Sub;
            button.ViewComponent.IsVisible = true;
        }

        for (int i = 0; i < ButtonCount; i++)
        {
            ServerAssistButton action = i < view.Buttons.Count ? view.Buttons[i] : null;
            XUiC_SimpleButton button = actionButtons[i];
            if (button == null)
            {
                continue;
            }

            if (action == null || string.IsNullOrEmpty(action.Action))
            {
                buttonActions[i] = null;
                Hide(button);
                continue;
            }

            buttonActions[i] = action.Action;
            button.Text = action.Label;
            button.Enabled = action.Enabled;
            button.Tooltip = action.Tooltip;

            string iconSprite = string.Empty;
            string iconAtlas = "UIAtlas";
            Color iconTint = Color.white;
            if (!string.IsNullOrEmpty(action.Icon))
            {
                ResolveIcon(action.Icon, action.IconTint, out iconSprite, out iconAtlas, out iconTint);
            }

            if (actionIcons[i] != null)
            {
                if (!string.IsNullOrEmpty(iconSprite))
                {
                    actionIcons[i].UIAtlas = iconAtlas;
                    actionIcons[i].SpriteName = iconSprite;
                    if (!action.Enabled)
                    {
                        iconTint.a *= 0.45f;
                    }

                    actionIcons[i].Color = iconTint;
                    actionIcons[i].IsVisible = true;
                }
                else
                {
                    actionIcons[i].IsVisible = false;
                }
            }

            if (actionLabels[i] != null)
            {
                bool hasIcon = !string.IsNullOrEmpty(iconSprite);
                actionLabels[i].Position = new Vector2i(hasIcon ? 30 : 0, -18);
                actionLabels[i].Size = new Vector2i(hasIcon ? 124 : 160, 34);
            }

            button.ViewComponent.IsVisible = true;
        }

        applying = false;
    }

    private void OnRowPressed(int index, int mouseButton)
    {
        if (applying || index < 0 || index >= rowActions.Length)
        {
            return;
        }

        string action = rowActions[index];
        if (string.IsNullOrEmpty(action))
        {
            return;
        }

        if (mouseButton == 1)
        {
            // Right-click favorites only make sense on the home board, not
            // inside a hook or a provider-owned screen.
            if (!hookChromeActive && !providerActive)
            {
                Send("a|favorite|" + action);
            }

            return;
        }

        Send("a|" + action);
    }

    private void OnActionPressed(int index)
    {
        if (applying || index < 0 || index >= buttonActions.Length)
        {
            return;
        }

        string action = buttonActions[index];
        if (string.IsNullOrEmpty(action))
        {
            return;
        }

        if (string.Equals(action, "close", StringComparison.OrdinalIgnoreCase))
        {
            CloseWindow();
            return;
        }

        Send("a|" + action);
    }

    private void OnInputSubmit(XUiController sender, string text)
    {
        SubmitInput();
    }

    private void SubmitInput()
    {
        if (applying || input == null || !input.ViewComponent.IsVisible)
        {
            return;
        }

        string text = input.Text ?? string.Empty;
        input.Text = string.Empty;
        Send("t|" + ServerAssistProtocol.Encode(text));
    }

    private void Send(string payload)
    {
        if (applying)
        {
            return;
        }

        ServerAssistClient.SendRequest(payload);
    }

    private void CloseWindow()
    {
        if (xui == null || xui.playerUI == null || xui.playerUI.windowManager == null)
        {
            return;
        }

        XUiWindowGroup group = XUiC_ServerAssistWindowGroup.FindWindowGroup(xui);
        if (group != null)
        {
            xui.playerUI.windowManager.Close(group);
        }
    }

    private XUiV_Label Label(string id)
    {
        XUiController controller = GetChildById(id);
        return controller != null ? controller.ViewComponent as XUiV_Label : null;
    }

    private XUiC_SimpleButton Button(string id)
    {
        return GetChildById(id) as XUiC_SimpleButton;
    }

    private static T ChildView<T>(XUiController parent, string id) where T : XUiView
    {
        if (parent == null)
        {
            return null;
        }

        XUiController child = parent.GetChildById(id);
        return child != null ? child.ViewComponent as T : null;
    }

    private static void AttachHover(XUiC_SimpleButton button, XUiV_Sprite hover)
    {
        if (button == null || hover == null)
        {
            return;
        }

        hover.IsVisible = false;
        button.OnHovered += (sender, isOver) =>
        {
            hover.IsVisible = isOver && button.Enabled;
        };
    }

    private void ApplyPanelImage(string key, XUiV_Sprite view, float alpha)
    {
        if (view == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(key))
        {
            view.IsVisible = false;
            return;
        }

        string sprite;
        string atlas;
        Color color;
        ResolveIcon(key, string.Empty, out sprite, out atlas, out color);
        if (string.IsNullOrEmpty(sprite))
        {
            view.IsVisible = false;
            return;
        }

        view.UIAtlas = atlas;
        view.SpriteName = sprite;
        color.a = alpha;
        view.Color = color;
        view.IsVisible = true;
    }

    private static void SetLabel(XUiV_Label label, string text, bool enabled, bool primary)
    {
        if (label == null)
        {
            return;
        }

        label.Text = text ?? string.Empty;
        label.Color = enabled
            ? (primary ? Color.white : new Color(0.78f, 0.82f, 0.86f, 1f))
            : new Color(0.5f, 0.5f, 0.5f, 0.7f);
        label.IsVisible = true;
    }

    private void ResolveIcon(string icon, string tint, out string sprite, out string atlas, out Color color)
    {
        sprite = FallbackIcon;
        atlas = "UIAtlas";
        color = Color.white;

        if (string.IsNullOrEmpty(icon))
        {
            return;
        }

        string value = icon.Trim();
        if (value.StartsWith("item:", StringComparison.OrdinalIgnoreCase))
        {
            string itemName = value.Substring(5).Trim();
            ItemClass itemClass = ItemClass.GetItemClass(itemName, true);
            if (itemClass != null)
            {
                string iconName = itemClass.GetIconName();
                if (!string.IsNullOrEmpty(iconName))
                {
                    sprite = iconName;
                    atlas = BestAtlas(ItemAtlasFor(iconName), iconName);
                }
            }
        }
        else if (value.StartsWith("sprite:", StringComparison.OrdinalIgnoreCase))
        {
            string spriteName = value.Substring(7).Trim();
            if (!string.IsNullOrEmpty(spriteName))
            {
                sprite = spriteName;
                atlas = BestAtlas("UIAtlas", spriteName);
            }
        }
        else if (value.StartsWith("atlas:", StringComparison.OrdinalIgnoreCase))
        {
            string atlasValue = value.Substring(6);
            int separator = atlasValue.LastIndexOf(':');
            string atlasName = (separator >= 0 ? atlasValue.Substring(0, separator) : string.Empty).Trim();
            string spriteName = (separator >= 0 ? atlasValue.Substring(separator + 1) : atlasValue).Trim();
            if (!string.IsNullOrEmpty(atlasName))
            {
                atlas = atlasName;
            }

            if (!string.IsNullOrEmpty(spriteName))
            {
                sprite = spriteName;
                atlas = BestAtlas(atlas, spriteName);
            }
        }
        else if (!string.IsNullOrEmpty(value))
        {
            sprite = value;
            atlas = BestAtlas("UIAtlas", sprite);
        }

        if (string.IsNullOrEmpty(sprite))
        {
            sprite = FallbackIcon;
            atlas = "UIAtlas";
        }

        if (!string.IsNullOrEmpty(tint) && !ColorUtility.TryParseHtmlString(tint, out color))
        {
            color = Color.white;
        }
    }

    private string BestAtlas(string preferred, string spriteName)
    {
        string fallback = string.IsNullOrEmpty(preferred) ? "UIAtlas" : preferred;
        if (string.IsNullOrEmpty(spriteName) || xui == null)
        {
            return fallback;
        }

        if (AtlasHasSprite(fallback, spriteName))
        {
            return fallback;
        }

        try
        {
            foreach (var pair in xui.allMultiSourceAtlases)
            {
                if (pair.Value != null && AtlasHasSprite(pair.Key, spriteName))
                {
                    return pair.Key;
                }
            }
        }
        catch
        {
            // Fall through to the preferred atlas.
        }

        return fallback;
    }

    private bool AtlasHasSprite(string atlasName, string spriteName)
    {
        if (xui == null || string.IsNullOrEmpty(atlasName) || string.IsNullOrEmpty(spriteName))
        {
            return false;
        }

        try
        {
            INGUIAtlas atlas = xui.GetAtlasByName(atlasName, spriteName);
            return atlas != null && atlas.GetSprite(spriteName) != null;
        }
        catch
        {
            return false;
        }
    }

    private static string ItemAtlasFor(string iconName)
    {
        if (string.IsNullOrEmpty(iconName))
        {
            return "UIAtlas";
        }

        string value = iconName.Trim();
        if (value.StartsWith("ui_", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("sa_", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("menu_", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("symbol_", StringComparison.OrdinalIgnoreCase))
        {
            return "UIAtlas";
        }

        // 7 Days to Die stores item art in the ItemIconAtlas manager. Mods can
        // add their own item PNGs to the same folder and they resolve here
        // without copying or re-registering the texture.
        return "ItemIconAtlas";
    }

    private static void SetTab(XUiC_SimpleButton button, string label, bool active)
    {
        if (button == null)
        {
            return;
        }

        button.Text = active ? "[" + label + "]" : label;
        button.Enabled = true;
        button.ViewComponent.IsVisible = true;
    }

    private static void SetVisible(XUiC_SimpleButton button, bool visible)
    {
        if (button == null)
        {
            return;
        }

        button.ViewComponent.IsVisible = visible;
        button.Enabled = visible;
    }

    private static void Hide(XUiC_SimpleButton button)
    {
        if (button == null)
        {
            return;
        }

        button.Text = string.Empty;
        button.Tooltip = string.Empty;
        button.Enabled = false;
        button.ViewComponent.IsVisible = false;
    }
}




