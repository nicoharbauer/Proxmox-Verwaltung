using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ProxmoxVEGui
{
    public partial class MainForm : Form
    {
        private readonly ProxmoxClient _client;
        private List<PveNode> _cachedNodes = new List<PveNode>();
        private bool _webViewInitialized = false;
        private string _lastSelectedKey = "";
        private TreeNode _lastSelectableTreeNode;
        private ConfigPanel _configPanel;

        private readonly Color _treeBackColor = Color.FromArgb(15, 23, 42);
        private readonly Color _treeTextColor = Color.FromArgb(226, 232, 240);
        private readonly Color _treeMutedTextColor = Color.FromArgb(148, 163, 184);
        private readonly Color _treeGroupTextColor = Color.FromArgb(203, 213, 225);
        private readonly Color _treeAccentColor = Color.FromArgb(249, 115, 22);
        private readonly Color _treeLineColor = Color.FromArgb(71, 85, 105);
        private readonly Color _treeSelectedBackColor = Color.FromArgb(249, 115, 22);
        private readonly Color _treeSelectedTextColor = Color.White;

        private readonly Color _statusRunningColor = Color.FromArgb(34, 197, 94);
        private readonly Color _statusStoppedColor = Color.FromArgb(239, 68, 68);
        private Color _resourceStatusDefaultColor = Color.FromArgb(96, 165, 250);

        // Resource context menu (right click on VM/LXC)
        private ContextMenuStrip _resourceContextMenu;
        private ToolStripMenuItem _contextStartItem;
        private ToolStripMenuItem _contextStopItem;

        // TreeView scrollbar
        private ModernScrollbarPart _treeScrollTrack;
        private ModernScrollbarPart _treeScrollThumb;
        private TreeViewNativeScrollbarHider _treeScrollbarHider;

        private bool _treeScrollDragging = false;
        private int _treeScrollDragOffsetY = 0;

        private const int TreeScrollbarWidth = 8;
        private const int TreeScrollbarMargin = 5;
        private const int TreeScrollbarMinThumbHeight = 42;

        // DataGridView scrollbar
        private ModernScrollbarPart _gridScrollTrack;
        private ModernScrollbarPart _gridScrollThumb;
        private DataGridViewNativeScrollbarHider _gridScrollbarHider;

        private bool _gridScrollDragging = false;
        private int _gridScrollDragOffsetY = 0;

        private const int GridScrollbarWidth = 8;
        private const int GridScrollbarMargin = 5;
        private const int GridScrollbarMinThumbHeight = 42;

        // Tree refresh button next to "DATACENTER TREE"
        private Control _treeTitleControl;
        private Control _treeTitleParentControl;
        private bool _treeRefreshButtonConfigured = false;

        // Modern account / logout display
        private ModernAccountPanel _accountLogoutPanel;
        private Label _accountIconLabel;
        private Label _accountTitleLabel;
        private Label _accountUserLabel;
        private Label _accountLogoutLabel;

        // Split status label: "Status:" bleibt normal, nur der Statuswert wird farbig.
        private Label _resourceStatusPrefixLabel;
        private Label _resourceStatusValueLabel;

        // Datacenter dashboard card: only visible when the Datacenter node is selected.
        private ModernDashboardCardPanel _datacenterOverviewPanel;
        private Label _datacenterTotalValueLabel;
        private Label _datacenterCardTitleLabel;
        private Label _datacenterCardSubtitleLabel;
        private Label _datacenterOnlineValueLabel;
        private Label _datacenterOnlineTextLabel;
        private Label _datacenterOfflineValueLabel;
        private Label _datacenterOfflineTextLabel;
        private Label _datacenterTotalTextLabel;
        private readonly Dictionary<Control, bool> _dashboardOriginalVisibility = new Dictionary<Control, bool>();
        private bool _dashboardOriginalVisibilityCaptured = false;

        // Icon-only action buttons
        private ToolTip _actionButtonToolTip;

        // Runtime swap: action buttons <-> create buttons
        private bool _buttonSwapLayoutCaptured = false;
        private Control _actionButtonsOriginalParent;
        private Control _createButtonsOriginalParent;
        private Rectangle _actionButtonsOriginalBounds = Rectangle.Empty;
        private Rectangle _createButtonsOriginalBounds = Rectangle.Empty;
        private Size _createVmOriginalSize = Size.Empty;
        private Size _createLxcOriginalSize = Size.Empty;

        public MainForm(ProxmoxClient client)
        {
            _client = client;
            InitializeComponent();
            ApplyApplicationIcon();
            ApplyWindowedFullscreenMode();
        }

        private void ApplyWindowedFullscreenMode()
        {
            // Vollbild im Fenstermodus:
            // Das Fenster startet maximiert, bleibt aber ein normales verschiebbares Windows-Fenster.
            SuspendLayout();

            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState = FormWindowState.Maximized;
            MaximizeBox = true;
            MinimizeBox = true;
            ShowIcon = true;
            ShowInTaskbar = true;
            MinimumSize = new Size(1100, 700);

            ResumeLayout(false);
        }

        private async void MainForm_Load(object sender, EventArgs e)
        {
            ApplyWindowedFullscreenMode();

            lblVersion.Text = "v" + typeof(Program).Assembly.GetName().Version.ToString(3);
            btnCheckUpdate.BorderRadius = 4;
            ApplyTabButtonDesign();
            CaptureButtonSwapOriginalLayout();
            SetupActionIconButtons();
            SetupTreeRefreshButton();
            SetupAccountLogoutPanel();

            _resourceStatusDefaultColor = lblResourceStatus.ForeColor;
            SetupSplitResourceStatusLabels();
            EnsureDatacenterOverviewCard();

            ApplyTreeViewDesign();
            ApplyGridScrollbar();

            _configPanel = new ConfigPanel(_client);
            panelContentContainer.Controls.Add(_configPanel);

            await RefreshDataAsync();
            SelectDatacenterNode();

            timerRefresh.Start();

            // Run background update check silently without blocking startup load
            _ = Task.Run(() => Updater.CheckForUpdatesAsync(this, silent: true));
        }

        // ─────────────────────────────────────────────────────────────────────
        // TREEVIEW SCROLLBAR
        // ─────────────────────────────────────────────────────────────────────

        private void ApplyTreeViewDesign()
        {
            treeResources.BackColor = _treeBackColor;
            treeResources.ForeColor = _treeTextColor;
            treeResources.LineColor = _treeLineColor;
            treeResources.BorderStyle = BorderStyle.None;

            treeResources.HideSelection = false;
            treeResources.HotTracking = false;
            treeResources.FullRowSelect = false;

            treeResources.ShowLines = true;
            treeResources.ShowRootLines = true;
            treeResources.ShowPlusMinus = true;
            treeResources.ShowNodeToolTips = false;

            treeResources.ItemHeight = 23;
            treeResources.Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            treeResources.Scrollable = true;

            treeResources.DrawMode = TreeViewDrawMode.OwnerDrawText;
            treeResources.DrawNode -= treeResources_DrawNode;
            treeResources.DrawNode += treeResources_DrawNode;

            treeResources.MouseWheel -= treeResources_MouseWheel;
            treeResources.MouseWheel += treeResources_MouseWheel;

            treeResources.MouseEnter -= treeResources_MouseEnter;
            treeResources.MouseEnter += treeResources_MouseEnter;

            treeResources.AfterExpand -= treeResources_AfterExpandCollapse;
            treeResources.AfterExpand += treeResources_AfterExpandCollapse;

            treeResources.AfterCollapse -= treeResources_AfterExpandCollapse;
            treeResources.AfterCollapse += treeResources_AfterExpandCollapse;

            treeResources.BeforeSelect -= treeResources_BeforeSelect;
            treeResources.BeforeSelect += treeResources_BeforeSelect;

            treeResources.AfterSelect -= treeResources_AfterSelect;
            treeResources.AfterSelect += treeResources_AfterSelect;

            treeResources.KeyDown -= treeResources_KeyDown;
            treeResources.KeyDown += treeResources_KeyDown;

            treeResources.NodeMouseClick -= treeResources_NodeMouseClick;
            treeResources.NodeMouseClick += treeResources_NodeMouseClick;

            treeResources.Resize -= treeResources_ResizeOrMove;
            treeResources.Resize += treeResources_ResizeOrMove;

            treeResources.LocationChanged -= treeResources_ResizeOrMove;
            treeResources.LocationChanged += treeResources_ResizeOrMove;

            _treeScrollbarHider = new TreeViewNativeScrollbarHider();
            _treeScrollbarHider.MouseWheelScrollRequested += TreeNativeScrollbar_MouseWheelScrollRequested;
            _treeScrollbarHider.Attach(treeResources);

            InitializeResourceContextMenu();
            CreateTreeScrollbar();
            PositionTreeScrollbar();
            HideNativeTreeScrollbars();
            UpdateTreeScrollbar();
        }

        private void CreateTreeScrollbar()
        {
            if (_treeScrollTrack == null)
            {
                _treeScrollTrack = new ModernScrollbarPart
                {
                    FillColor = Color.FromArgb(30, 41, 59),
                    HoverColor = Color.FromArgb(30, 41, 59),
                    Radius = 4,
                    Cursor = Cursors.Hand,
                    Visible = true
                };

                _treeScrollTrack.MouseDown += TreeScrollbarTrack_MouseDown;
                _treeScrollTrack.MouseMove += TreeScrollbar_MouseMove;
                _treeScrollTrack.MouseUp += TreeScrollbar_MouseUp;
                _treeScrollTrack.MouseWheel += TreeScrollbar_MouseWheel;
                _treeScrollTrack.MouseEnter += TreeScrollbar_MouseEnter;
            }

            if (_treeScrollTrack.Parent != treeResources)
            {
                _treeScrollTrack.Parent?.Controls.Remove(_treeScrollTrack);
                treeResources.Controls.Add(_treeScrollTrack);
            }

            if (_treeScrollThumb == null)
            {
                _treeScrollThumb = new ModernScrollbarPart
                {
                    FillColor = Color.FromArgb(249, 115, 22),
                    HoverColor = Color.FromArgb(251, 146, 60),
                    Radius = 4,
                    Cursor = Cursors.Hand,
                    Visible = true
                };

                _treeScrollThumb.MouseDown += TreeScrollbarThumb_MouseDown;
                _treeScrollThumb.MouseMove += TreeScrollbar_MouseMove;
                _treeScrollThumb.MouseUp += TreeScrollbar_MouseUp;
                _treeScrollThumb.MouseWheel += TreeScrollbar_MouseWheel;
                _treeScrollThumb.MouseEnter += TreeScrollbar_MouseEnter;
            }

            if (_treeScrollThumb.Parent != _treeScrollTrack)
            {
                _treeScrollThumb.Parent?.Controls.Remove(_treeScrollThumb);
                _treeScrollTrack.Controls.Add(_treeScrollThumb);
            }

            _treeScrollTrack.BringToFront();
            _treeScrollThumb.BringToFront();
            treeResources.Controls.SetChildIndex(_treeScrollTrack, 0);
        }

        private void PositionTreeScrollbar()
        {
            if (_treeScrollTrack == null) return;

            int x = treeResources.ClientSize.Width - TreeScrollbarWidth - TreeScrollbarMargin;
            int y = TreeScrollbarMargin;
            int height = treeResources.ClientSize.Height - (TreeScrollbarMargin * 2);

            if (height < 30 || x < 0)
            {
                _treeScrollTrack.Visible = false;
                return;
            }

            _treeScrollTrack.Bounds = new Rectangle(x, y, TreeScrollbarWidth, height);
            _treeScrollTrack.Visible = true;
            _treeScrollTrack.BringToFront();

            if (_treeScrollTrack.Parent == treeResources)
            {
                treeResources.Controls.SetChildIndex(_treeScrollTrack, 0);
            }
        }

        private void HideNativeTreeScrollbars()
        {
            _treeScrollbarHider?.HideNow();
        }

        private void SetupTreeRefreshButton()
        {
            try
            {
                if (btnRefresh == null) return;

                _treeTitleControl = FindDatacenterTreeTitleControl(this);
                if (_treeTitleControl == null || _treeTitleControl.Parent == null) return;

                Control targetParent = _treeTitleControl.Parent;

                btnRefresh.Text = "↻";
                btnRefresh.Width = 28;
                btnRefresh.Height = 28;
                btnRefresh.Cursor = Cursors.Hand;
                btnRefresh.TabStop = false;
                btnRefresh.BackColor = Color.Transparent;
                btnRefresh.ForeColor = Color.FromArgb(249, 115, 22);
                btnRefresh.Font = new Font("Segoe UI Symbol", 12F, FontStyle.Bold);
                btnRefresh.Visible = true;
                btnRefresh.Enabled = true;

                if (btnRefresh is ButtonBase refreshButtonBase)
                {
                    refreshButtonBase.FlatStyle = FlatStyle.Flat;
                    refreshButtonBase.TextAlign = ContentAlignment.MiddleCenter;
                    refreshButtonBase.Image = null;
                    refreshButtonBase.FlatAppearance.BorderSize = 0;
                    refreshButtonBase.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 41, 59);
                    refreshButtonBase.FlatAppearance.MouseDownBackColor = Color.FromArgb(51, 65, 85);
                }

                SetControlPropertyIfAvailable(btnRefresh, "BorderRadius", 14);
                SetControlPropertyIfAvailable(btnRefresh, "BorderSize", 0);
                SetControlPropertyIfAvailable(btnRefresh, "BorderColor", Color.Transparent);

                if (btnRefresh.Parent != targetParent)
                {
                    btnRefresh.Parent?.Controls.Remove(btnRefresh);
                    targetParent.Controls.Add(btnRefresh);
                }

                btnRefresh.BringToFront();
                PositionTreeRefreshButton();

                if (!_treeRefreshButtonConfigured)
                {
                    _treeTitleControl.LocationChanged += TreeRefreshPositionChanged;
                    _treeTitleControl.SizeChanged += TreeRefreshPositionChanged;
                    targetParent.Resize += TreeRefreshPositionChanged;

                    _treeTitleParentControl = targetParent;
                    _treeRefreshButtonConfigured = true;
                }
                else if (_treeTitleParentControl != targetParent)
                {
                    if (_treeTitleParentControl != null)
                        _treeTitleParentControl.Resize -= TreeRefreshPositionChanged;

                    targetParent.Resize += TreeRefreshPositionChanged;
                    _treeTitleParentControl = targetParent;
                }
            }
            catch
            {
                // Der Refresh-Button ist rein optisch. Die App darf dadurch nicht blockieren.
            }
        }

        private void SetupAccountLogoutPanel()
        {
            try
            {
                if (btnLogout == null || btnLogout.Parent == null) return;

                Control targetParent = btnLogout.Parent;

                if (_accountLogoutPanel == null)
                {
                    _accountLogoutPanel = new ModernAccountPanel
                    {
                        Width = 238,
                        Height = 52,
                        Cursor = Cursors.Hand,
                        TabStop = false,
                        BackColor = Color.Transparent,
                        Anchor = AnchorStyles.Top | AnchorStyles.Right
                    };

                    _accountLogoutPanel.Click += AccountLogoutControl_Click;
                    _accountLogoutPanel.Resize += AccountLogoutPanel_Resize;

                    _accountIconLabel = new Label
                    {
                        Text = "\uE77B",
                        AutoSize = false,
                        TextAlign = ContentAlignment.MiddleCenter,
                        BackColor = Color.Transparent,
                        ForeColor = Color.White,
                        Font = new Font("Segoe MDL2 Assets", 13F, FontStyle.Regular),
                        Cursor = Cursors.Hand
                    };

                    _accountTitleLabel = new Label
                    {
                        Text = "Angemeldet mit",
                        AutoSize = false,
                        AutoEllipsis = false,
                        TextAlign = ContentAlignment.MiddleLeft,
                        BackColor = Color.Transparent,
                        ForeColor = Color.FromArgb(148, 163, 184),
                        Font = new Font("Segoe UI", 7.25F, FontStyle.Regular),
                        Cursor = Cursors.Hand
                    };

                    _accountUserLabel = new Label
                    {
                        Text = "",
                        AutoSize = false,
                        AutoEllipsis = true,
                        TextAlign = ContentAlignment.MiddleLeft,
                        BackColor = Color.Transparent,
                        ForeColor = Color.FromArgb(248, 250, 252),
                        Font = new Font("Segoe UI", 8.35F, FontStyle.Bold),
                        Cursor = Cursors.Hand
                    };

                    _accountLogoutLabel = new Label
                    {
                        Text = "Abmelden",
                        AutoSize = false,
                        AutoEllipsis = false,
                        TextAlign = ContentAlignment.MiddleLeft,
                        BackColor = Color.Transparent,
                        ForeColor = Color.FromArgb(251, 146, 60),
                        Font = new Font("Segoe UI", 7.35F, FontStyle.Bold),
                        Cursor = Cursors.Hand
                    };

                    _accountIconLabel.Click += AccountLogoutControl_Click;
                    _accountTitleLabel.Click += AccountLogoutControl_Click;
                    _accountUserLabel.Click += AccountLogoutControl_Click;
                    _accountLogoutLabel.Click += AccountLogoutControl_Click;

                    _accountLogoutPanel.Controls.Add(_accountIconLabel);
                    _accountLogoutPanel.Controls.Add(_accountTitleLabel);
                    _accountLogoutPanel.Controls.Add(_accountUserLabel);
                    _accountLogoutPanel.Controls.Add(_accountLogoutLabel);
                }

                string signedInDisplayName = GetSignedInDisplayName();
                string accountDisplayName = GetCompactSignedInDisplayName(signedInDisplayName);

                _accountTitleLabel.Text = "Angemeldet mit";
                _accountUserLabel.Text = accountDisplayName;
                _accountLogoutLabel.Text = "Abmelden";

                _actionButtonToolTip?.SetToolTip(_accountLogoutPanel, signedInDisplayName);
                _actionButtonToolTip?.SetToolTip(_accountIconLabel, signedInDisplayName);
                _actionButtonToolTip?.SetToolTip(_accountTitleLabel, signedInDisplayName);
                _actionButtonToolTip?.SetToolTip(_accountUserLabel, signedInDisplayName);
                _actionButtonToolTip?.SetToolTip(_accountLogoutLabel, "Abmelden");

                if (_accountLogoutPanel.Parent != targetParent)
                {
                    _accountLogoutPanel.Parent?.Controls.Remove(_accountLogoutPanel);
                    targetParent.Controls.Add(_accountLogoutPanel);
                }

                btnLogout.Visible = false;

                targetParent.Resize -= AccountLogoutParent_Resize;
                targetParent.Resize += AccountLogoutParent_Resize;

                LayoutAccountLogoutPanel();
                PositionAccountLogoutPanel();

                _accountLogoutPanel.BringToFront();
                LayoutActionIconButtons();
            }
            catch
            {
                ApplyFallbackLogoutButtonDesign();
            }
        }

        private void AccountLogoutParent_Resize(object sender, EventArgs e)
        {
            UpdateAccountLogoutPanelSize();
            LayoutAccountLogoutPanel();
            PositionAccountLogoutPanel();
        }

        private void AccountLogoutPanel_Resize(object sender, EventArgs e)
        {
            LayoutAccountLogoutPanel();
        }

        private void UpdateAccountLogoutPanelSize()
        {
            if (_accountLogoutPanel == null || _accountTitleLabel == null) return;

            string title = _accountTitleLabel.Text ?? string.Empty;
            int titleWidth = TextRenderer.MeasureText(
                title,
                _accountTitleLabel.Font,
                new Size(1000, 24),
                TextFormatFlags.SingleLine
            ).Width;

            int desiredWidth = Math.Max(238, Math.Min(320, titleWidth + 130));
            _accountLogoutPanel.Width = desiredWidth;
            _accountLogoutPanel.Height = 52;
        }

        private void PositionAccountLogoutPanel()
        {
            if (_accountLogoutPanel == null || btnLogout == null || _accountLogoutPanel.Parent == null) return;

            Control parent = _accountLogoutPanel.Parent;

            int preferredWidth = 238;
            int maxWidth = Math.Max(190, parent.ClientSize.Width - 16);
            int panelWidth = Math.Min(preferredWidth, maxWidth);
            int panelHeight = 52;

            _accountLogoutPanel.Size = new Size(panelWidth, panelHeight);

            int x = parent.ClientSize.Width - panelWidth - 12;
            int y = btnLogout.Top + ((btnLogout.Height - panelHeight) / 2);

            x = ClampInt(x, 8, Math.Max(8, parent.ClientSize.Width - panelWidth - 8));
            y = ClampInt(y, 6, Math.Max(6, parent.ClientSize.Height - panelHeight - 6));

            _accountLogoutPanel.Location = new Point(x, y);
            LayoutAccountLogoutPanel();
            _accountLogoutPanel.BringToFront();
        }

        private void LayoutAccountLogoutPanel()
        {
            if (_accountLogoutPanel == null ||
                _accountIconLabel == null ||
                _accountTitleLabel == null ||
                _accountUserLabel == null ||
                _accountLogoutLabel == null)
            {
                return;
            }

            int panelHeight = _accountLogoutPanel.Height;
            int iconSize = 30;
            int paddingLeft = 12;
            int iconTop = Math.Max(0, (panelHeight - iconSize) / 2);

            _accountIconLabel.Bounds = new Rectangle(paddingLeft, iconTop, iconSize, iconSize);

            int textLeft = paddingLeft + iconSize + 10;
            int textWidth = Math.Max(1, _accountLogoutPanel.Width - textLeft - 12);

            _accountTitleLabel.Bounds = new Rectangle(textLeft, 6, textWidth, 13);
            _accountUserLabel.Bounds = new Rectangle(textLeft, 18, textWidth, 17);
            _accountLogoutLabel.Bounds = new Rectangle(textLeft, 35, textWidth, 13);
        }

        private string GetCompactSignedInDisplayName(string displayName)
        {
            string cleanDisplayName = (displayName ?? "").Trim();

            if (string.IsNullOrWhiteSpace(cleanDisplayName))
                return "Proxmox";

            int hostSeparatorIndex = cleanDisplayName.IndexOf(" @ ", StringComparison.Ordinal);
            if (hostSeparatorIndex > 0)
                return cleanDisplayName.Substring(0, hostSeparatorIndex).Trim();

            return cleanDisplayName;
        }

        private void AccountLogoutControl_Click(object sender, EventArgs e)
        {
            btnLogout_Click(btnLogout, EventArgs.Empty);
        }

        private void ApplyFallbackLogoutButtonDesign()
        {
            try
            {
                if (btnLogout == null) return;

                btnLogout.Visible = true;
                btnLogout.Text = "Angemeldet mit " + GetSignedInDisplayName() + "\r\nAbmelden";
                btnLogout.Width = Math.Max(btnLogout.Width, 200);
                btnLogout.Height = Math.Max(btnLogout.Height, 48);
                btnLogout.Cursor = Cursors.Hand;
                btnLogout.ForeColor = Color.White;
                btnLogout.BackColor = Color.FromArgb(30, 41, 59);
                btnLogout.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);

                if (btnLogout is ButtonBase logoutButtonBase)
                {
                    logoutButtonBase.TextAlign = ContentAlignment.MiddleCenter;
                    logoutButtonBase.FlatStyle = FlatStyle.Flat;
                    logoutButtonBase.FlatAppearance.BorderSize = 1;
                    logoutButtonBase.FlatAppearance.BorderColor = Color.FromArgb(249, 115, 22);
                    logoutButtonBase.FlatAppearance.MouseOverBackColor = Color.FromArgb(51, 65, 85);
                    logoutButtonBase.FlatAppearance.MouseDownBackColor = Color.FromArgb(249, 115, 22);
                }

                SetControlPropertyIfAvailable(btnLogout, "BorderRadius", 12);
                SetControlPropertyIfAvailable(btnLogout, "BorderSize", 1);
                SetControlPropertyIfAvailable(btnLogout, "BorderColor", Color.FromArgb(249, 115, 22));
            }
            catch
            {
                // Auch das Fallback darf den Start nicht blockieren.
            }
        }

        private string GetSignedInDisplayName()
        {
            string username = ReadClientStringProperty("Username");

            if (string.IsNullOrWhiteSpace(username))
                username = ReadClientStringProperty("UserName");

            if (string.IsNullOrWhiteSpace(username))
                username = ReadClientStringProperty("User");

            if (string.IsNullOrWhiteSpace(username))
                username = ReadClientStringProperty("Login");

            if (string.IsNullOrWhiteSpace(username))
                username = ReadClientStringProperty("Email");

            string host = (_client.Host ?? "").Trim();

            if (string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(host))
                return "Proxmox";

            if (string.IsNullOrWhiteSpace(username))
                return host;

            if (string.IsNullOrWhiteSpace(host))
                return username.Trim();

            if (username.IndexOf(host, StringComparison.OrdinalIgnoreCase) >= 0)
                return username.Trim();

            return username.Trim() + " @ " + host;
        }

        private string ReadClientStringProperty(string propertyName)
        {
            try
            {
                if (_client == null) return "";

                var property = _client.GetType().GetProperty(propertyName);
                if (property == null || !property.CanRead) return "";

                object value = property.GetValue(_client, null);
                return value == null ? "" : value.ToString().Trim();
            }
            catch
            {
                return "";
            }
        }

        private void TreeRefreshPositionChanged(object sender, EventArgs e)
        {
            PositionTreeRefreshButton();
        }

        private void PositionTreeRefreshButton()
        {
            if (_treeTitleControl == null || btnRefresh == null || btnRefresh.Parent == null) return;

            int spacing = 8;
            int x = _treeTitleControl.Right + spacing;
            int y = _treeTitleControl.Top + Math.Max(0, (_treeTitleControl.Height - btnRefresh.Height) / 2);

            int maxX = Math.Max(0, btnRefresh.Parent.ClientSize.Width - btnRefresh.Width - 8);
            btnRefresh.Location = new Point(Math.Min(x, maxX), y);
            btnRefresh.BringToFront();
        }

        private Control FindDatacenterTreeTitleControl(Control parent)
        {
            if (parent == null) return null;

            foreach (Control control in parent.Controls)
            {
                string text = (control.Text ?? "").Trim();

                if (!string.IsNullOrWhiteSpace(text) &&
                    text.Replace("\r", " ").Replace("\n", " ").Trim()
                        .IndexOf("DATACENTER TREE", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return control;
                }

                Control childResult = FindDatacenterTreeTitleControl(control);
                if (childResult != null) return childResult;
            }

            return null;
        }

        private void SetControlPropertyIfAvailable(Control control, string propertyName, object value)
        {
            if (control == null) return;

            try
            {
                var property = control.GetType().GetProperty(propertyName);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(control, value, null);
                }
            }
            catch
            {
                // Nicht jedes Button-Control besitzt diese Design-Properties.
            }
        }

        private void CaptureButtonSwapOriginalLayout()
        {
            if (_buttonSwapLayoutCaptured) return;

            try
            {
                Control[] actionButtons = GetActionButtons().Where(b => b != null && b.Parent != null).ToArray();

                if (actionButtons.Length > 0)
                {
                    _actionButtonsOriginalParent = actionButtons[0].Parent;
                    _actionButtonsOriginalBounds = GetCombinedBounds(actionButtons, _actionButtonsOriginalParent);
                }

                Control createVmButton = FindControlByName("btnCreateVm");
                Control createLxcButton = FindControlByName("btnCreateLxc");

                if (createVmButton != null)
                    _createVmOriginalSize = createVmButton.Size;

                if (createLxcButton != null)
                    _createLxcOriginalSize = createLxcButton.Size;

                Control[] createButtons =
                {
                    createVmButton,
                    createLxcButton
                };

                Control firstCreateButton = createButtons.FirstOrDefault(b => b != null && b.Parent != null);

                if (firstCreateButton != null)
                {
                    _createButtonsOriginalParent = firstCreateButton.Parent;
                    _createButtonsOriginalBounds = GetCombinedBounds(createButtons, _createButtonsOriginalParent);
                }

                _buttonSwapLayoutCaptured =
                    _actionButtonsOriginalParent != null &&
                    _createButtonsOriginalParent != null &&
                    !_actionButtonsOriginalBounds.IsEmpty &&
                    !_createButtonsOriginalBounds.IsEmpty;
            }
            catch
            {
                _buttonSwapLayoutCaptured = false;
            }
        }

        private Control[] GetActionButtons()
        {
            return new Control[]
            {
                btnStart,
                btnStop,
                btnShutdown,
                btnReboot,
                btnDelete
            };
        }

        private Control[] GetCreateButtons()
        {
            return new Control[]
            {
                FindControlByName("btnCreateVm"),
                FindControlByName("btnCreateLxc")
            };
        }

        private Control FindControlByName(string controlName)
        {
            if (string.IsNullOrWhiteSpace(controlName)) return null;

            try
            {
                Control[] matches = Controls.Find(controlName, true);
                return matches != null && matches.Length > 0 ? matches[0] : null;
            }
            catch
            {
                return null;
            }
        }

        private Rectangle GetCombinedBounds(IEnumerable<Control> controls, Control parent)
        {
            Rectangle result = Rectangle.Empty;
            bool hasBounds = false;

            if (controls == null || parent == null)
                return Rectangle.Empty;

            foreach (Control control in controls)
            {
                if (control == null || control.Parent != parent)
                    continue;

                if (!hasBounds)
                {
                    result = control.Bounds;
                    hasBounds = true;
                }
                else
                {
                    result = Rectangle.Union(result, control.Bounds);
                }
            }

            return hasBounds ? result : Rectangle.Empty;
        }

        private void MoveControlsToParent(IEnumerable<Control> controls, Control targetParent)
        {
            if (controls == null || targetParent == null)
                return;

            foreach (Control control in controls)
            {
                if (control == null)
                    continue;

                if (control.Parent != targetParent)
                {
                    if (control.Parent != null)
                        control.Parent.Controls.Remove(control);

                    targetParent.Controls.Add(control);
                }

                control.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                control.Visible = true;
            }
        }

        private void ConfigureCreateButton(Control button, string text, string tooltipText)
        {
            if (button == null) return;

            bool isLxcButton = text.IndexOf("LXC", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isKvmButton = text.IndexOf("KVM", StringComparison.OrdinalIgnoreCase) >= 0;

            Color baseColor = isLxcButton
                ? Color.FromArgb(14, 165, 233)
                : isKvmButton
                    ? Color.FromArgb(34, 197, 94)
                    : Color.FromArgb(249, 115, 22);

            button.Text = text;
            button.Cursor = Cursors.Hand;
            button.Visible = true;
            button.ForeColor = Color.White;
            button.BackColor = baseColor;
            button.Font = new Font("Segoe UI", 9F, FontStyle.Bold);

            if (button is ButtonBase buttonBase)
            {
                buttonBase.TextAlign = ContentAlignment.MiddleCenter;
                buttonBase.Image = null;
                buttonBase.Padding = new Padding(0);
                buttonBase.FlatStyle = FlatStyle.Flat;
                buttonBase.UseVisualStyleBackColor = false;
                buttonBase.FlatAppearance.BorderSize = 0;
                buttonBase.FlatAppearance.BorderColor = baseColor;
                buttonBase.FlatAppearance.MouseOverBackColor = LightenColor(baseColor, 14);
                buttonBase.FlatAppearance.MouseDownBackColor = DarkenColor(baseColor, 18);
            }

            SetControlPropertyIfAvailable(button, "BorderRadius", 10);
            SetControlPropertyIfAvailable(button, "BorderSize", 0);
            SetControlPropertyIfAvailable(button, "BorderColor", Color.Transparent);
            _actionButtonToolTip?.SetToolTip(button, tooltipText);
        }

        private void SetupCreateButtonsInActionArea()
        {
            Control[] createButtons = GetCreateButtons();
            Control createVmButton = createButtons.Length > 0 ? createButtons[0] : null;
            Control createLxcButton = createButtons.Length > 1 ? createButtons[1] : null;

            ConfigureCreateButton(createVmButton, "KVM erstellen", "Neue KVM erstellen");
            ConfigureCreateButton(createLxcButton, "LXC erstellen", "Neuen LXC Container erstellen");

            LayoutCreateButtonsInOldActionPosition();
        }

        private void LayoutCreateButtonsInOldActionPosition()
        {
            if (!_buttonSwapLayoutCaptured || _actionButtonsOriginalParent == null)
                return;

            Control[] createButtons = GetCreateButtons().Where(b => b != null).ToArray();
            if (createButtons.Length == 0)
                return;

            MoveControlsToParent(createButtons, _actionButtonsOriginalParent);

            int spacing = 10;
            int buttonHeight = 34;

            int areaLeft = !_actionButtonsOriginalBounds.IsEmpty
                ? Math.Max(8, _actionButtonsOriginalBounds.Left)
                : 8;

            int areaTop = !_actionButtonsOriginalBounds.IsEmpty
                ? _actionButtonsOriginalBounds.Top
                : createButtons[0].Top;

            int areaHeight = !_actionButtonsOriginalBounds.IsEmpty
                ? Math.Max(buttonHeight, _actionButtonsOriginalBounds.Height)
                : buttonHeight;

            int maxRight = _actionButtonsOriginalParent.ClientSize.Width - 8;

            // Gewünschter Abstand zwischen Benutzerbereich und "VM/LXC erstellen".
            const int accountToCreateButtonsGap = 5;

            if (_accountLogoutPanel != null && _accountLogoutPanel.Parent == _actionButtonsOriginalParent)
                maxRight = _accountLogoutPanel.Left - accountToCreateButtonsGap;

            maxRight = Math.Max(areaLeft + 1, maxRight);

            int availableWidth = Math.Max(1, maxRight - areaLeft);

            int vmWidth = 120;
            int lxcWidth = 120;

            if (_createVmOriginalSize.Width > 0)
                vmWidth = Math.Max(118, Math.Min(132, _createVmOriginalSize.Width));

            if (_createLxcOriginalSize.Width > 0)
                lxcWidth = Math.Max(118, Math.Min(132, _createLxcOriginalSize.Width));

            int totalWidth;

            if (createButtons.Length == 1)
            {
                vmWidth = Math.Min(vmWidth, availableWidth);
                totalWidth = vmWidth;
            }
            else
            {
                totalWidth = vmWidth + lxcWidth + spacing;

                if (totalWidth > availableWidth)
                {
                    int compressedSpacing = Math.Min(spacing, Math.Max(0, availableWidth - 2));
                    int equalWidth = Math.Max(1, (availableWidth - compressedSpacing) / 2);

                    spacing = compressedSpacing;
                    vmWidth = equalWidth;
                    lxcWidth = equalWidth;
                    totalWidth = vmWidth + lxcWidth + spacing;
                }
            }

            int x = Math.Max(areaLeft, maxRight - totalWidth);
            int y = areaTop + Math.Max(0, (areaHeight - buttonHeight) / 2);

            if (createButtons.Length > 0)
            {
                createButtons[0].Bounds = new Rectangle(x, y, vmWidth, buttonHeight);
                createButtons[0].BringToFront();
                x += vmWidth + spacing;
            }

            if (createButtons.Length > 1)
            {
                createButtons[1].Bounds = new Rectangle(x, y, lxcWidth, buttonHeight);
                createButtons[1].BringToFront();
            }
        }

        private void SetupActionIconButtons()
        {
            _actionButtonToolTip = new ToolTip
            {
                AutoPopDelay = 5000,
                InitialDelay = 350,
                ReshowDelay = 100,
                ShowAlways = true
            };

            ApplyActionIconButton(btnStart, "\uE768", "Starten", Color.FromArgb(34, 197, 94));
            ApplyActionIconButton(btnStop, "\uE15B", "Stoppen", Color.FromArgb(239, 68, 68));
            ApplyActionIconButton(btnShutdown, "\uE7E8", "Herunterfahren", Color.FromArgb(245, 158, 11));
            ApplyActionIconButton(btnReboot, "\uE895", "Neustarten", Color.FromArgb(59, 130, 246));
            ApplyActionIconButton(btnDelete, "\uE74D", "Löschen", Color.FromArgb(220, 38, 38));

            if (btnStart != null && btnStart.Parent != null)
            {
                btnStart.Parent.Resize -= ActionButtonsParent_Resize;
                btnStart.Parent.Resize += ActionButtonsParent_Resize;
            }

            if (_actionButtonsOriginalParent != null)
            {
                _actionButtonsOriginalParent.Resize -= ActionButtonsParent_Resize;
                _actionButtonsOriginalParent.Resize += ActionButtonsParent_Resize;
            }

            if (_createButtonsOriginalParent != null && _createButtonsOriginalParent != _actionButtonsOriginalParent)
            {
                _createButtonsOriginalParent.Resize -= ActionButtonsParent_Resize;
                _createButtonsOriginalParent.Resize += ActionButtonsParent_Resize;
            }

            LayoutActionIconButtons();
            SetupCreateButtonsInActionArea();
            RefreshActionButtonVisualStates();
        }

        private void ActionButtonsParent_Resize(object sender, EventArgs e)
        {
            LayoutActionIconButtons();
            LayoutCreateButtonsInOldActionPosition();
            PositionAccountLogoutPanel();
        }

        private void LayoutActionIconButtons()
        {
            Control[] buttons = GetActionButtons();
            Control firstButton = buttons.FirstOrDefault(b => b != null);
            if (firstButton == null) return;

            Control parent = _buttonSwapLayoutCaptured && _createButtonsOriginalParent != null
                ? _createButtonsOriginalParent
                : firstButton.Parent;

            if (parent == null) return;

            MoveControlsToParent(buttons, parent);

            int buttonWidth = 42;
            int buttonHeight = 34;
            int spacing = 7;
            int rightMargin = 12;
            int accountGap = 14;

            Control[] visibleButtons = buttons
                .Where(b => b != null && b.Parent == parent && b.Visible)
                .ToArray();

            if (visibleButtons.Length == 0) return;

            int fallbackLeft = _buttonSwapLayoutCaptured && !_createButtonsOriginalBounds.IsEmpty
                ? _createButtonsOriginalBounds.Left
                : firstButton.Left;

            int areaTop = _buttonSwapLayoutCaptured && !_createButtonsOriginalBounds.IsEmpty
                ? _createButtonsOriginalBounds.Top
                : firstButton.Top;

            int areaHeight = _buttonSwapLayoutCaptured && !_createButtonsOriginalBounds.IsEmpty
                ? Math.Max(buttonHeight, _createButtonsOriginalBounds.Height)
                : buttonHeight;

            int y = areaTop + Math.Max(0, (areaHeight - buttonHeight) / 2) - 2;

            int maxRight = parent.ClientSize.Width - rightMargin;
            if (_accountLogoutPanel != null && _accountLogoutPanel.Parent == parent)
                maxRight = _accountLogoutPanel.Left - accountGap;

            int totalButtonWidth = (visibleButtons.Length * buttonWidth) + ((visibleButtons.Length - 1) * spacing);

            // Power-Buttons rechtsbündig setzen: entweder direkt am rechten Rand
            // oder, falls das Login-Panel im selben Parent liegt, direkt links daneben.
            int x = Math.Max(8, maxRight - totalButtonWidth);

            // Falls der Parent sehr schmal ist, nicht weiter nach links als die alte Position rutschen.
            if (x < fallbackLeft && fallbackLeft + totalButtonWidth <= maxRight)
                x = fallbackLeft;

            foreach (Control button in visibleButtons)
            {
                button.Bounds = new Rectangle(x, y, buttonWidth, buttonHeight);
                button.BringToFront();

                x += buttonWidth + spacing;
            }

            LayoutCreateButtonsInOldActionPosition();

            if (_accountLogoutPanel != null && _accountLogoutPanel.Parent == parent)
                _accountLogoutPanel.BringToFront();
        }

        private void ApplyActionIconButton(Control button, string iconGlyph, string tooltipText, Color accentColor)
        {
            if (button == null) return;

            button.Text = iconGlyph;
            button.Font = new Font("Segoe MDL2 Assets", 13F, FontStyle.Regular);
            button.Cursor = Cursors.Hand;
            button.Width = 42;
            button.Height = 34;
            button.ForeColor = Color.White;
            button.BackColor = accentColor;
            button.Tag = accentColor;

            if (button is ButtonBase buttonBase)
            {
                buttonBase.Image = null;
                buttonBase.TextAlign = ContentAlignment.MiddleCenter;
                buttonBase.Padding = new Padding(0);
                buttonBase.FlatStyle = FlatStyle.Flat;
                buttonBase.UseVisualStyleBackColor = false;
                buttonBase.FlatAppearance.BorderSize = 0;
                buttonBase.FlatAppearance.BorderColor = accentColor;
                buttonBase.FlatAppearance.MouseOverBackColor = LightenColor(accentColor, 18);
                buttonBase.FlatAppearance.MouseDownBackColor = DarkenColor(accentColor, 15);
            }

            SetControlPropertyIfAvailable(button, "BorderRadius", 9);
            SetControlPropertyIfAvailable(button, "BorderSize", 0);
            SetControlPropertyIfAvailable(button, "BorderColor", Color.Transparent);

            _actionButtonToolTip?.SetToolTip(button, tooltipText);
        }

        private void RefreshActionButtonVisualStates()
        {
            SetActionButtonVisualState(btnStart, Color.FromArgb(34, 197, 94));
            SetActionButtonVisualState(btnStop, Color.FromArgb(239, 68, 68));
            SetActionButtonVisualState(btnShutdown, Color.FromArgb(245, 158, 11));
            SetActionButtonVisualState(btnReboot, Color.FromArgb(59, 130, 246));
            SetActionButtonVisualState(btnDelete, Color.FromArgb(220, 38, 38));
        }

        private void SetActionButtonVisualState(Control button, Color accentColor)
        {
            if (button == null) return;

            bool enabled = button.Enabled;

            button.BackColor = enabled ? accentColor : Color.FromArgb(30, 41, 59);
            button.ForeColor = enabled ? Color.White : Color.FromArgb(148, 163, 184);

            if (button is ButtonBase buttonBase)
            {
                buttonBase.UseVisualStyleBackColor = false;
                buttonBase.FlatAppearance.BorderSize = 0;
                buttonBase.FlatAppearance.BorderColor = enabled ? accentColor : Color.FromArgb(51, 65, 85);
                buttonBase.FlatAppearance.MouseOverBackColor = enabled ? LightenColor(accentColor, 18) : Color.FromArgb(30, 41, 59);
                buttonBase.FlatAppearance.MouseDownBackColor = enabled ? DarkenColor(accentColor, 15) : Color.FromArgb(30, 41, 59);
            }

            button.Invalidate();
        }

        private Color LightenColor(Color color, int amount)
        {
            return Color.FromArgb(
                Math.Min(255, color.R + amount),
                Math.Min(255, color.G + amount),
                Math.Min(255, color.B + amount)
            );
        }

        private Color DarkenColor(Color color, int amount)
        {
            return Color.FromArgb(
                Math.Max(0, color.R - amount),
                Math.Max(0, color.G - amount),
                Math.Max(0, color.B - amount)
            );
        }

        private Bitmap CreateGlyphBitmap(string glyph, Color color)
        {
            Bitmap bitmap = new Bitmap(20, 20);

            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Font font = new Font("Segoe MDL2 Assets", 11F, FontStyle.Regular))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                TextRenderer.DrawText(
                    graphics,
                    glyph,
                    font,
                    new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                );
            }

            return bitmap;
        }

        private void ApplyTabButtonDesign()
        {
            // Dashboard / Console / Configuration eckig darstellen.
            // BorderRadius 0 verursacht bei der vorhandenen Custom-Button-Klasse
            // den WinForms/GDI+-Fehler "Ungültiger Parameter".
            // Radius 1 ist optisch eckig, aber GDI+-sicher.
            ApplySafeSquareButtonStyle(btnTabDashboard);
            ApplySafeSquareButtonStyle(btnTabConsole);

            if (btnTabConfig != null)
            {
                ApplySafeSquareButtonStyle(btnTabConfig);
            }
        }

        private void ApplySafeSquareButtonStyle(Control button)
        {
            if (button == null) return;

            try
            {
                var borderRadiusProperty = button.GetType().GetProperty("BorderRadius");
                if (borderRadiusProperty != null && borderRadiusProperty.CanWrite)
                {
                    borderRadiusProperty.SetValue(button, 1, null);
                }
            }
            catch
            {
                // Design darf die Anwendung nicht blockieren.
            }

            button.Resize -= SquareButton_Resize;
            button.Resize += SquareButton_Resize;
            ApplySquareButtonRegion(button);
        }

        private void SquareButton_Resize(object sender, EventArgs e)
        {
            ApplySquareButtonRegion(sender as Control);
        }

        private void ApplySquareButtonRegion(Control button)
        {
            if (button == null || button.Width <= 0 || button.Height <= 0) return;

            try
            {
                button.Region?.Dispose();
                button.Region = new Region(new Rectangle(0, 0, button.Width, button.Height));
            }
            catch
            {
                // Falls Region auf einem Control nicht gesetzt werden kann, ignorieren.
            }
        }

        private void ApplyApplicationIcon()
        {
            try
            {
                string[] possibleIconPaths =
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "img", "logo.ico"),
                    Path.Combine(Application.StartupPath, "assets", "img", "logo.ico"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.ico")
                };

                foreach (string iconPath in possibleIconPaths)
                {
                    if (File.Exists(iconPath))
                    {
                        this.Icon = new Icon(iconPath);
                        return;
                    }
                }
            }
            catch
            {
                // Icon ist optional. Die App soll trotzdem starten.
            }
        }

        private void UpdateTreeScrollbar()
        {
            if (_treeScrollTrack == null || _treeScrollThumb == null) return;

            HideNativeTreeScrollbars();

            List<TreeNode> visibleNodes = GetVisibleTreeNodes();
            int totalVisibleNodes = visibleNodes.Count;
            int visibleCapacity = GetTreeVisibleCapacity();

            if (totalVisibleNodes <= 0)
            {
                _treeScrollTrack.Visible = false;
                return;
            }

            bool needsScrollbar = totalVisibleNodes > visibleCapacity;

            _treeScrollTrack.Visible = true;
            _treeScrollTrack.Enabled = true;
            _treeScrollTrack.BringToFront();

            if (_treeScrollTrack.Parent == treeResources)
            {
                treeResources.Controls.SetChildIndex(_treeScrollTrack, 0);
            }

            int trackHeight = Math.Max(1, _treeScrollTrack.Height);

            if (!needsScrollbar)
            {
                _treeScrollThumb.FillColor = Color.FromArgb(71, 85, 105);
                _treeScrollThumb.HoverColor = Color.FromArgb(100, 116, 139);
                _treeScrollThumb.Bounds = new Rectangle(0, 0, TreeScrollbarWidth, trackHeight);
                _treeScrollThumb.Visible = true;
                _treeScrollThumb.BringToFront();
                _treeScrollThumb.Invalidate();
                _treeScrollTrack.Invalidate();
                return;
            }

            _treeScrollThumb.FillColor = Color.FromArgb(249, 115, 22);
            _treeScrollThumb.HoverColor = Color.FromArgb(251, 146, 60);

            int thumbHeight = Math.Max(
                TreeScrollbarMinThumbHeight,
                (int)Math.Round((double)visibleCapacity / totalVisibleNodes * trackHeight)
            );

            thumbHeight = Math.Min(trackHeight, thumbHeight);

            int maxThumbTop = Math.Max(0, trackHeight - thumbHeight);
            int maxTopIndex = Math.Max(1, totalVisibleNodes - visibleCapacity);

            int topIndex = 0;

            if (treeResources.TopNode != null)
            {
                topIndex = visibleNodes.IndexOf(treeResources.TopNode);
                if (topIndex < 0) topIndex = 0;
            }

            topIndex = ClampInt(topIndex, 0, maxTopIndex);

            int thumbTop = maxTopIndex > 0
                ? (int)Math.Round((double)topIndex / maxTopIndex * maxThumbTop)
                : 0;

            _treeScrollThumb.Bounds = new Rectangle(0, thumbTop, TreeScrollbarWidth, thumbHeight);
            _treeScrollThumb.Visible = true;
            _treeScrollThumb.BringToFront();

            _treeScrollThumb.Invalidate();
            _treeScrollTrack.Invalidate();
        }

        private List<TreeNode> GetVisibleTreeNodes()
        {
            var nodes = new List<TreeNode>();

            foreach (TreeNode node in treeResources.Nodes)
            {
                AddVisibleTreeNode(node, nodes);
            }

            return nodes;
        }

        private void AddVisibleTreeNode(TreeNode node, List<TreeNode> nodes)
        {
            if (node == null) return;

            nodes.Add(node);

            if (!node.IsExpanded) return;

            foreach (TreeNode child in node.Nodes)
            {
                AddVisibleTreeNode(child, nodes);
            }
        }

        private int GetTreeVisibleCapacity()
        {
            int itemHeight = Math.Max(1, treeResources.ItemHeight);
            int usableHeight = Math.Max(1, treeResources.ClientSize.Height - (TreeScrollbarMargin * 2));

            return Math.Max(1, usableHeight / itemHeight);
        }

        private void ScrollTreeBy(int delta)
        {
            List<TreeNode> visibleNodes = GetVisibleTreeNodes();
            if (visibleNodes.Count == 0) return;

            int visibleCapacity = GetTreeVisibleCapacity();
            int maxTopIndex = Math.Max(0, visibleNodes.Count - visibleCapacity);

            int currentIndex = 0;
            if (treeResources.TopNode != null)
            {
                currentIndex = visibleNodes.IndexOf(treeResources.TopNode);
                if (currentIndex < 0) currentIndex = 0;
            }

            int newIndex = ClampInt(currentIndex + delta, 0, maxTopIndex);

            if (newIndex >= 0 && newIndex < visibleNodes.Count)
            {
                treeResources.TopNode = visibleNodes[newIndex];
            }

            HideNativeTreeScrollbars();
            UpdateTreeScrollbar();
        }

        private void ScrollTreeToThumbTop(int thumbTop)
        {
            if (_treeScrollTrack == null || _treeScrollThumb == null) return;

            List<TreeNode> visibleNodes = GetVisibleTreeNodes();
            if (visibleNodes.Count == 0) return;

            int visibleCapacity = GetTreeVisibleCapacity();
            int maxTopIndex = Math.Max(0, visibleNodes.Count - visibleCapacity);

            int maxThumbTop = Math.Max(1, _treeScrollTrack.Height - _treeScrollThumb.Height);
            int cleanThumbTop = ClampInt(thumbTop, 0, maxThumbTop);

            double ratio = (double)cleanThumbTop / maxThumbTop;
            int targetIndex = ClampInt((int)Math.Round(ratio * maxTopIndex), 0, maxTopIndex);

            if (targetIndex >= 0 && targetIndex < visibleNodes.Count)
            {
                treeResources.TopNode = visibleNodes[targetIndex];
            }

            HideNativeTreeScrollbars();
            UpdateTreeScrollbar();
        }

        private void treeResources_MouseEnter(object sender, EventArgs e)
        {
            if (treeResources != null && !treeResources.IsDisposed && treeResources.CanFocus)
                treeResources.Focus();
        }

        private void TreeScrollbar_MouseEnter(object sender, EventArgs e)
        {
            if (sender is Control control && control.CanFocus)
                control.Focus();
        }

        private void treeResources_MouseWheel(object sender, MouseEventArgs e)
        {
            if (e.Delta < 0) ScrollTreeBy(3);
            else if (e.Delta > 0) ScrollTreeBy(-3);
        }

        private void TreeNativeScrollbar_MouseWheelScrollRequested(int delta)
        {
            if (delta < 0) ScrollTreeBy(3);
            else if (delta > 0) ScrollTreeBy(-3);
        }

        private void TreeScrollbar_MouseWheel(object sender, MouseEventArgs e)
        {
            if (e.Delta < 0) ScrollTreeBy(3);
            else if (e.Delta > 0) ScrollTreeBy(-3);
        }

        private void treeResources_KeyDown(object sender, KeyEventArgs e)
        {
            BeginInvoke(new Action(() =>
            {
                PositionTreeScrollbar();
                HideNativeTreeScrollbars();
                UpdateTreeScrollbar();
            }));
        }

        private void treeResources_AfterExpandCollapse(object sender, TreeViewEventArgs e)
        {
            BeginInvoke(new Action(() =>
            {
                PositionTreeScrollbar();
                HideNativeTreeScrollbars();
                UpdateTreeScrollbar();
            }));
        }

        private void treeResources_ResizeOrMove(object sender, EventArgs e)
        {
            PositionTreeScrollbar();
            HideNativeTreeScrollbars();
            UpdateTreeScrollbar();
        }

        private void TreeScrollbarTrack_MouseDown(object sender, MouseEventArgs e)
        {
            if (_treeScrollThumb == null) return;

            int visibleCapacity = GetTreeVisibleCapacity();

            if (e.Y < _treeScrollThumb.Top) ScrollTreeBy(-visibleCapacity);
            else if (e.Y > _treeScrollThumb.Bottom) ScrollTreeBy(visibleCapacity);
        }

        private void TreeScrollbarThumb_MouseDown(object sender, MouseEventArgs e)
        {
            if (_treeScrollThumb == null) return;

            _treeScrollDragging = true;
            _treeScrollDragOffsetY = e.Y;
            _treeScrollThumb.Capture = true;
            _treeScrollThumb.FillColor = Color.FromArgb(234, 88, 12);
            _treeScrollThumb.Invalidate();
        }

        private void TreeScrollbar_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_treeScrollDragging || _treeScrollTrack == null || _treeScrollThumb == null) return;

            Point mouseInTrack;

            if (sender == _treeScrollThumb)
                mouseInTrack = _treeScrollTrack.PointToClient(_treeScrollThumb.PointToScreen(e.Location));
            else
                mouseInTrack = e.Location;

            int newThumbTop = mouseInTrack.Y - _treeScrollDragOffsetY;
            ScrollTreeToThumbTop(newThumbTop);
        }

        private void TreeScrollbar_MouseUp(object sender, MouseEventArgs e)
        {
            if (_treeScrollThumb == null) return;

            _treeScrollDragging = false;
            _treeScrollThumb.Capture = false;

            UpdateTreeScrollbar();
        }

        private void treeResources_DrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            if (e.Node == null) return;

            bool isSelected = (e.State & TreeNodeStates.Selected) == TreeNodeStates.Selected;
            var tag = e.Node.Tag as ResourceTag;

            Color backColor = isSelected ? _treeSelectedBackColor : _treeBackColor;
            Color textColor;

            if (isSelected)
            {
                textColor = _treeSelectedTextColor;
            }
            else if (e.Node.Level == 0)
            {
                textColor = _treeAccentColor;
            }
            else if (e.Node.Nodes.Count > 0)
            {
                textColor = _treeGroupTextColor;
            }
            else
            {
                textColor = _treeMutedTextColor;
            }

            int rightPadding = TreeScrollbarWidth + TreeScrollbarMargin + 10;

            Rectangle backgroundBounds = new Rectangle(
                e.Bounds.Left - 4,
                e.Bounds.Top,
                Math.Max(1, treeResources.ClientSize.Width - e.Bounds.Left - rightPadding + 4),
                e.Bounds.Height
            );

            using (SolidBrush backgroundBrush = new SolidBrush(backColor))
            {
                e.Graphics.FillRectangle(backgroundBrush, backgroundBounds);
            }

            bool isVmOrLxc = tag != null && (tag.Type == "vm" || tag.Type == "lxc");

            int dotSize = 10;
            int dotSpacing = 7;
            int textLeft = e.Bounds.Left;

            if (isVmOrLxc)
            {
                bool isRunning = false;

                if (tag.Type == "vm" && tag.Data is PveVm vm)
                    isRunning = string.Equals(vm.Status, "running", StringComparison.OrdinalIgnoreCase);

                if (tag.Type == "lxc" && tag.Data is PveLxc lxc)
                    isRunning = string.Equals(lxc.Status, "running", StringComparison.OrdinalIgnoreCase);

                Color dotColor = isRunning ? _statusRunningColor : _statusStoppedColor;

                int dotX = e.Bounds.Left;
                int dotY = e.Bounds.Top + ((e.Bounds.Height - dotSize) / 2);

                using (SolidBrush dotBrush = new SolidBrush(dotColor))
                using (Pen dotBorderPen = new Pen(Color.FromArgb(15, 23, 42), 1))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.FillEllipse(dotBrush, dotX, dotY, dotSize, dotSize);
                    e.Graphics.DrawEllipse(dotBorderPen, dotX, dotY, dotSize, dotSize);
                    e.Graphics.SmoothingMode = SmoothingMode.None;
                }

                textLeft = e.Bounds.Left + dotSize + dotSpacing;
            }

            Rectangle textBounds = new Rectangle(
                textLeft,
                e.Bounds.Top,
                Math.Max(1, treeResources.ClientSize.Width - textLeft - rightPadding),
                e.Bounds.Height
            );

            TextRenderer.DrawText(
                e.Graphics,
                e.Node.Text,
                treeResources.Font,
                textBounds,
                textColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis
            );
        }

        private void SelectDatacenterNode()
        {
            if (treeResources.Nodes.Count > 0)
            {
                treeResources.SelectedNode = treeResources.Nodes[0];
                ResetTreeScrollToTop();

                BeginInvoke(new Action(() =>
                {
                    ResetTreeScrollToTop();
                }));
            }
        }

        private void ResetTreeScrollToTop()
        {
            if (treeResources == null || treeResources.IsDisposed || treeResources.Nodes.Count == 0)
                return;

            try
            {
                TreeNode firstNode = treeResources.Nodes[0];

                if (firstNode != null)
                {
                    treeResources.TopNode = firstNode;

                    if (treeResources.SelectedNode == null)
                        treeResources.SelectedNode = firstNode;

                    firstNode.EnsureVisible();
                    treeResources.TopNode = firstNode;
                }

                PositionTreeScrollbar();
                HideNativeTreeScrollbars();
                UpdateTreeScrollbar();
                treeResources.Invalidate();
            }
            catch
            {
                // Der Tree soll beim Start oben bleiben. Ein Scroll-Reset darf die App nicht blockieren.
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // RESOURCE CONTEXT MENU (RIGHT CLICK VM/LXC)
        // ─────────────────────────────────────────────────────────────────────

        private void InitializeResourceContextMenu()
        {
            if (_resourceContextMenu != null) return;

            _resourceContextMenu = new ContextMenuStrip
            {
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.FromArgb(226, 232, 240),
                ShowImageMargin = true,
                ShowCheckMargin = false,
                Renderer = new ModernContextMenuRenderer()
            };

            _contextStartItem = new ToolStripMenuItem("Starten")
            {
                ForeColor = Color.FromArgb(226, 232, 240),
                Image = CreateGlyphBitmap("\uE768", Color.FromArgb(34, 197, 94))
            };
            _contextStartItem.Click += (s, e) => PerformPowerAction("start");

            _contextStopItem = new ToolStripMenuItem("Stoppen")
            {
                ForeColor = Color.FromArgb(226, 232, 240),
                Image = CreateGlyphBitmap("\uE15B", Color.FromArgb(239, 68, 68))
            };
            _contextStopItem.Click += (s, e) => PerformPowerAction("stop");

            _resourceContextMenu.Items.Add(_contextStartItem);
            _resourceContextMenu.Items.Add(_contextStopItem);
        }

        private void treeResources_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Button != MouseButtons.Right || e.Node == null) return;

            var tag = e.Node.Tag as ResourceTag;
            if (tag == null || (tag.Type != "vm" && tag.Type != "lxc")) return;

            treeResources.SelectedNode = e.Node;
            UpdateUIForSelectedResource(tag);
            UpdateResourceContextMenuState(tag);

            _resourceContextMenu.Show(treeResources, e.Location);
        }

        private void UpdateResourceContextMenuState(ResourceTag tag)
        {
            if (_contextStartItem == null || _contextStopItem == null) return;

            bool isVmOrLxc = tag != null && (tag.Type == "vm" || tag.Type == "lxc");
            string status = GetResourceStatus(tag);
            bool isRunning = string.Equals(status, "running", StringComparison.OrdinalIgnoreCase);

            _contextStartItem.Enabled = isVmOrLxc && !isRunning;
            _contextStopItem.Enabled = isVmOrLxc && isRunning;

            if (tag != null)
            {
                string title = tag.Type == "vm" ? "VM" : "LXC";
                _contextStartItem.Text = $"{title} starten";
                _contextStopItem.Text = $"{title} stoppen";
            }
        }

        private string GetResourceStatus(ResourceTag tag)
        {
            if (tag == null) return "";

            if (tag.Type == "vm" && tag.Data is PveVm vm)
                return vm.Status ?? "";

            if (tag.Type == "lxc" && tag.Data is PveLxc lxc)
                return lxc.Status ?? "";

            return "";
        }

        private void SetupSplitResourceStatusLabels()
        {
            try
            {
                if (lblResourceStatus == null || lblResourceStatus.Parent == null) return;

                Control parent = lblResourceStatus.Parent;

                if (_resourceStatusPrefixLabel == null)
                {
                    _resourceStatusPrefixLabel = new Label
                    {
                        AutoSize = false,
                        BackColor = lblResourceStatus.BackColor,
                        ForeColor = _resourceStatusDefaultColor,
                        Font = lblResourceStatus.Font,
                        TextAlign = lblResourceStatus.TextAlign,
                        Visible = false
                    };

                    parent.Controls.Add(_resourceStatusPrefixLabel);
                }

                if (_resourceStatusValueLabel == null)
                {
                    _resourceStatusValueLabel = new Label
                    {
                        AutoSize = false,
                        BackColor = lblResourceStatus.BackColor,
                        ForeColor = _resourceStatusDefaultColor,
                        Font = lblResourceStatus.Font,
                        TextAlign = lblResourceStatus.TextAlign,
                        Visible = false
                    };

                    parent.Controls.Add(_resourceStatusValueLabel);
                }

                lblResourceStatus.LocationChanged -= ResourceStatusBaseLabel_LayoutChanged;
                lblResourceStatus.SizeChanged -= ResourceStatusBaseLabel_LayoutChanged;
                lblResourceStatus.FontChanged -= ResourceStatusBaseLabel_LayoutChanged;
                lblResourceStatus.ParentChanged -= ResourceStatusBaseLabel_LayoutChanged;

                lblResourceStatus.LocationChanged += ResourceStatusBaseLabel_LayoutChanged;
                lblResourceStatus.SizeChanged += ResourceStatusBaseLabel_LayoutChanged;
                lblResourceStatus.FontChanged += ResourceStatusBaseLabel_LayoutChanged;
                lblResourceStatus.ParentChanged += ResourceStatusBaseLabel_LayoutChanged;

                SyncSplitResourceStatusLabelsLayout();
            }
            catch
            {
                // Die Status-Farbtrennung ist nur optisch und darf die App nicht blockieren.
            }
        }

        private void ResourceStatusBaseLabel_LayoutChanged(object sender, EventArgs e)
        {
            SyncSplitResourceStatusLabelsLayout();
        }

        private void SyncSplitResourceStatusLabelsLayout()
        {
            if (lblResourceStatus == null ||
                _resourceStatusPrefixLabel == null ||
                _resourceStatusValueLabel == null ||
                lblResourceStatus.Parent == null)
            {
                return;
            }

            if (_resourceStatusPrefixLabel.Parent != lblResourceStatus.Parent)
            {
                _resourceStatusPrefixLabel.Parent?.Controls.Remove(_resourceStatusPrefixLabel);
                lblResourceStatus.Parent.Controls.Add(_resourceStatusPrefixLabel);
            }

            if (_resourceStatusValueLabel.Parent != lblResourceStatus.Parent)
            {
                _resourceStatusValueLabel.Parent?.Controls.Remove(_resourceStatusValueLabel);
                lblResourceStatus.Parent.Controls.Add(_resourceStatusValueLabel);
            }

            _resourceStatusPrefixLabel.BackColor = lblResourceStatus.BackColor;
            _resourceStatusValueLabel.BackColor = lblResourceStatus.BackColor;
            _resourceStatusPrefixLabel.Font = lblResourceStatus.Font;
            _resourceStatusValueLabel.Font = lblResourceStatus.Font;

            int prefixWidth = TextRenderer.MeasureText(
                "Status:",
                lblResourceStatus.Font,
                new Size(1000, lblResourceStatus.Height),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding
            ).Width + 4;

            prefixWidth = Math.Min(prefixWidth, Math.Max(1, lblResourceStatus.Width));

            _resourceStatusPrefixLabel.Bounds = new Rectangle(
                lblResourceStatus.Left,
                lblResourceStatus.Top,
                prefixWidth,
                lblResourceStatus.Height
            );

            _resourceStatusValueLabel.Bounds = new Rectangle(
                lblResourceStatus.Left + prefixWidth,
                lblResourceStatus.Top,
                Math.Max(1, lblResourceStatus.Width - prefixWidth),
                lblResourceStatus.Height
            );

            _resourceStatusPrefixLabel.BringToFront();
            _resourceStatusValueLabel.BringToFront();
        }

        private void SetResourceStatusLabel(string text, string rawStatus = null)
        {
            if (lblResourceStatus == null) return;

            SetupSplitResourceStatusLabels();

            string displayText = text ?? string.Empty;
            string statusPrefix = "Status:";

            bool isStatusLine = displayText.StartsWith(statusPrefix, StringComparison.OrdinalIgnoreCase);

            if (isStatusLine && _resourceStatusPrefixLabel != null && _resourceStatusValueLabel != null)
            {
                string statusValue = displayText.Substring(statusPrefix.Length).Trim();

                lblResourceStatus.Visible = false;

                SyncSplitResourceStatusLabelsLayout();

                _resourceStatusPrefixLabel.Text = statusPrefix;
                _resourceStatusPrefixLabel.ForeColor = _resourceStatusDefaultColor;
                _resourceStatusPrefixLabel.Visible = true;

                _resourceStatusValueLabel.Text = statusValue;
                _resourceStatusValueLabel.ForeColor = GetResourceStatusColor(rawStatus);
                _resourceStatusValueLabel.Visible = true;

                _resourceStatusPrefixLabel.BringToFront();
                _resourceStatusValueLabel.BringToFront();
                return;
            }

            if (_resourceStatusPrefixLabel != null)
                _resourceStatusPrefixLabel.Visible = false;

            if (_resourceStatusValueLabel != null)
                _resourceStatusValueLabel.Visible = false;

            lblResourceStatus.Visible = true;
            lblResourceStatus.Text = displayText;
            lblResourceStatus.ForeColor = _resourceStatusDefaultColor;
        }

        private Color GetResourceStatusColor(string rawStatus)
        {
            string status = (rawStatus ?? string.Empty).Trim();

            if (string.Equals(status, "running", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "online", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "active", StringComparison.OrdinalIgnoreCase))
            {
                return _statusRunningColor;
            }

            if (string.Equals(status, "stopped", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "offline", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "inactive", StringComparison.OrdinalIgnoreCase))
            {
                return _statusStoppedColor;
            }

            return _resourceStatusDefaultColor;
        }

        // ─────────────────────────────────────────────────────────────────────
        // DATAGRIDVIEW SCROLLBAR
        // ─────────────────────────────────────────────────────────────────────

        private void ApplyGridScrollbar()
        {
            // Disable ALL native scrollbars – we draw our own overlay instead
            gridTasks.ScrollBars = ScrollBars.None;
            gridTasks.Scroll += (s, e) => BeginInvoke(new Action(UpdateGridScrollbar));
            gridTasks.MouseWheel += (s, e) => ScrollGridBy(e.Delta < 0 ? 3 : -3);

            _gridScrollbarHider = new DataGridViewNativeScrollbarHider();
            _gridScrollbarHider.Attach(gridTasks);

            _gridScrollTrack = new ModernScrollbarPart
            {
                FillColor = Color.FromArgb(30, 41, 59),
                HoverColor = Color.FromArgb(30, 41, 59),
                Radius = 4,
                Cursor = Cursors.Hand,
                Visible = true
            };
            _gridScrollTrack.MouseDown += GridScrollbarTrack_MouseDown;
            _gridScrollTrack.MouseMove += GridScrollbar_MouseMove;
            _gridScrollTrack.MouseUp += GridScrollbar_MouseUp;
            _gridScrollTrack.MouseWheel += GridScrollbar_MouseWheel;

            _gridScrollThumb = new ModernScrollbarPart
            {
                FillColor = Color.FromArgb(249, 115, 22),
                HoverColor = Color.FromArgb(251, 146, 60),
                Radius = 4,
                Cursor = Cursors.Hand,
                Visible = true
            };
            _gridScrollThumb.MouseDown += GridScrollbarThumb_MouseDown;
            _gridScrollThumb.MouseMove += GridScrollbar_MouseMove;
            _gridScrollThumb.MouseUp += GridScrollbar_MouseUp;
            _gridScrollThumb.MouseWheel += GridScrollbar_MouseWheel;

            _gridScrollTrack.Controls.Add(_gridScrollThumb);
            gridTasks.Controls.Add(_gridScrollTrack);
            _gridScrollTrack.BringToFront();

            gridTasks.Resize += (s, e) => { PositionGridScrollbar(); UpdateGridScrollbar(); };

            PositionGridScrollbar();
            UpdateGridScrollbar();
        }

        private void PositionGridScrollbar()
        {
            if (_gridScrollTrack == null) return;

            int x = gridTasks.ClientSize.Width - GridScrollbarWidth - GridScrollbarMargin;
            int y = GridScrollbarMargin;
            int height = gridTasks.ClientSize.Height - (GridScrollbarMargin * 2);

            if (height < 30 || x < 0)
            {
                _gridScrollTrack.Visible = false;
                return;
            }

            _gridScrollTrack.Bounds = new Rectangle(x, y, GridScrollbarWidth, height);
            _gridScrollTrack.Visible = true;
            _gridScrollTrack.BringToFront();
        }

        private void UpdateGridScrollbar()
        {
            if (_gridScrollTrack == null || _gridScrollThumb == null) return;

            _gridScrollbarHider?.HideNow();

            int totalRows = gridTasks.Rows.Count;
            if (totalRows == 0)
            {
                _gridScrollTrack.Visible = false;
                return;
            }

            int visibleRows = GetGridVisibleRowCount();
            _gridScrollTrack.Visible = true;
            _gridScrollTrack.BringToFront();

            int trackHeight = Math.Max(1, _gridScrollTrack.Height);
            bool needsScrollbar = totalRows > visibleRows;

            if (!needsScrollbar)
            {
                _gridScrollThumb.FillColor = Color.FromArgb(71, 85, 105);
                _gridScrollThumb.HoverColor = Color.FromArgb(100, 116, 139);
                _gridScrollThumb.Bounds = new Rectangle(0, 0, GridScrollbarWidth, trackHeight);
                _gridScrollThumb.Visible = true;
                _gridScrollThumb.Invalidate();
                _gridScrollTrack.Invalidate();
                return;
            }

            _gridScrollThumb.FillColor = Color.FromArgb(249, 115, 22);
            _gridScrollThumb.HoverColor = Color.FromArgb(251, 146, 60);

            int thumbHeight = Math.Max(
                GridScrollbarMinThumbHeight,
                (int)Math.Round((double)visibleRows / totalRows * trackHeight)
            );
            thumbHeight = Math.Min(trackHeight, thumbHeight);

            int maxThumbTop = Math.Max(0, trackHeight - thumbHeight);
            int maxScroll = Math.Max(1, totalRows - visibleRows);

            int firstVisible = gridTasks.FirstDisplayedScrollingRowIndex >= 0
                ? gridTasks.FirstDisplayedScrollingRowIndex
                : 0;

            int thumbTop = maxScroll > 0
                ? (int)Math.Round((double)ClampInt(firstVisible, 0, maxScroll) / maxScroll * maxThumbTop)
                : 0;

            _gridScrollThumb.Bounds = new Rectangle(0, thumbTop, GridScrollbarWidth, thumbHeight);
            _gridScrollThumb.Visible = true;
            _gridScrollThumb.Invalidate();
            _gridScrollTrack.Invalidate();
        }

        private int GetGridVisibleRowCount()
        {
            int rowHeight = gridTasks.RowTemplate.Height > 0 ? gridTasks.RowTemplate.Height : 23;
            rowHeight = Math.Max(1, rowHeight);
            int usable = Math.Max(1, gridTasks.ClientSize.Height - gridTasks.ColumnHeadersHeight);
            return Math.Max(1, usable / rowHeight);
        }

        private void ScrollGridBy(int delta)
        {
            int totalRows = gridTasks.Rows.Count;
            if (totalRows == 0) return;

            int current = gridTasks.FirstDisplayedScrollingRowIndex >= 0
                ? gridTasks.FirstDisplayedScrollingRowIndex
                : 0;

            int visibleRows = GetGridVisibleRowCount();
            int newIndex = ClampInt(current + delta, 0, Math.Max(0, totalRows - visibleRows));

            gridTasks.FirstDisplayedScrollingRowIndex = newIndex;
            _gridScrollbarHider?.HideNow();
            UpdateGridScrollbar();
        }

        private void GridScrollbarTrack_MouseDown(object sender, MouseEventArgs e)
        {
            if (_gridScrollThumb == null) return;

            int visibleRows = GetGridVisibleRowCount();
            if (e.Y < _gridScrollThumb.Top) ScrollGridBy(-visibleRows);
            else if (e.Y > _gridScrollThumb.Bottom) ScrollGridBy(visibleRows);
        }

        private void GridScrollbarThumb_MouseDown(object sender, MouseEventArgs e)
        {
            _gridScrollDragging = true;
            _gridScrollDragOffsetY = e.Y;
            _gridScrollThumb.Capture = true;
            _gridScrollThumb.FillColor = Color.FromArgb(234, 88, 12);
            _gridScrollThumb.Invalidate();
        }

        private void GridScrollbar_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_gridScrollDragging || _gridScrollTrack == null || _gridScrollThumb == null) return;

            Point mouseInTrack = sender == _gridScrollThumb
                ? _gridScrollTrack.PointToClient(_gridScrollThumb.PointToScreen(e.Location))
                : e.Location;

            int newThumbTop = ClampInt(
                mouseInTrack.Y - _gridScrollDragOffsetY,
                0,
                Math.Max(0, _gridScrollTrack.Height - _gridScrollThumb.Height)
            );

            int maxThumbTop = Math.Max(1, _gridScrollTrack.Height - _gridScrollThumb.Height);
            int totalRows = gridTasks.Rows.Count;
            int visibleRows = GetGridVisibleRowCount();
            int maxScroll = Math.Max(1, totalRows - visibleRows);

            int targetRow = ClampInt(
                (int)Math.Round((double)newThumbTop / maxThumbTop * maxScroll),
                0,
                Math.Max(0, totalRows - 1)
            );

            if (totalRows > 0)
                gridTasks.FirstDisplayedScrollingRowIndex = targetRow;

            _gridScrollbarHider?.HideNow();
            UpdateGridScrollbar();
        }

        private void GridScrollbar_MouseUp(object sender, MouseEventArgs e)
        {
            _gridScrollDragging = false;
            if (_gridScrollThumb != null) _gridScrollThumb.Capture = false;
            UpdateGridScrollbar();
        }

        private void GridScrollbar_MouseWheel(object sender, MouseEventArgs e)
        {
            ScrollGridBy(e.Delta < 0 ? 3 : -3);
        }

        // ─────────────────────────────────────────────────────────────────────
        // DATACENTER OVERVIEW CARD
        // ─────────────────────────────────────────────────────────────────────

        private void EnsureDatacenterOverviewCard()
        {
            if (panelDashboard == null) return;

            if (_datacenterOverviewPanel == null)
            {
                _datacenterOverviewPanel = new ModernDashboardCardPanel
                {
                    Name = "datacenterOverviewPanel",
                    BackColor = Color.Transparent,
                    FillColor = Color.FromArgb(17, 24, 39),
                    BorderColor = Color.FromArgb(51, 65, 85),
                    Radius = 18,
                    Visible = false,
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
                };
                _datacenterOverviewPanel.Resize += DatacenterOverviewPanel_Resize;

                _datacenterCardTitleLabel = CreateDatacenterCardLabel(
                    "Datacenter Status",
                    new Font("Segoe UI", 14.5F, FontStyle.Bold),
                    Color.FromArgb(248, 250, 252),
                    ContentAlignment.MiddleLeft
                );

                _datacenterCardSubtitleLabel = CreateDatacenterCardLabel(
                    string.Empty,
                    new Font("Segoe UI", 8.7F, FontStyle.Regular),
                    Color.FromArgb(148, 163, 184),
                    ContentAlignment.MiddleLeft
                );
                _datacenterCardSubtitleLabel.Visible = false;

                _datacenterOnlineValueLabel = CreateDatacenterCardLabel(
                    "0",
                    new Font("Segoe UI", 22F, FontStyle.Bold),
                    _statusRunningColor,
                    ContentAlignment.MiddleLeft
                );

                _datacenterOnlineTextLabel = CreateDatacenterCardLabel(
                    "Nodes online",
                    new Font("Segoe UI", 9F, FontStyle.Regular),
                    Color.FromArgb(203, 213, 225),
                    ContentAlignment.MiddleLeft
                );

                _datacenterOfflineValueLabel = CreateDatacenterCardLabel(
                    "0",
                    new Font("Segoe UI", 22F, FontStyle.Bold),
                    _statusStoppedColor,
                    ContentAlignment.MiddleLeft
                );

                _datacenterOfflineTextLabel = CreateDatacenterCardLabel(
                    "Nodes offline",
                    new Font("Segoe UI", 9F, FontStyle.Regular),
                    Color.FromArgb(203, 213, 225),
                    ContentAlignment.MiddleLeft
                );

                _datacenterTotalValueLabel = CreateDatacenterCardLabel(
                    "0",
                    new Font("Segoe UI", 22F, FontStyle.Bold),
                    Color.FromArgb(249, 115, 22),
                    ContentAlignment.MiddleCenter
                );

                _datacenterTotalTextLabel = CreateDatacenterCardLabel(
                    "Nodes gesamt",
                    new Font("Segoe UI", 9F, FontStyle.Regular),
                    Color.FromArgb(203, 213, 225),
                    ContentAlignment.MiddleCenter
                );

                _datacenterOverviewPanel.Controls.Add(_datacenterCardTitleLabel);
                _datacenterOverviewPanel.Controls.Add(_datacenterCardSubtitleLabel);
                _datacenterOverviewPanel.Controls.Add(_datacenterOnlineValueLabel);
                _datacenterOverviewPanel.Controls.Add(_datacenterOnlineTextLabel);
                _datacenterOverviewPanel.Controls.Add(_datacenterOfflineValueLabel);
                _datacenterOverviewPanel.Controls.Add(_datacenterOfflineTextLabel);
                _datacenterOverviewPanel.Controls.Add(_datacenterTotalValueLabel);
                _datacenterOverviewPanel.Controls.Add(_datacenterTotalTextLabel);

                panelDashboard.Controls.Add(_datacenterOverviewPanel);
                panelDashboard.Resize -= PanelDashboard_ResizeForDatacenterOverview;
                panelDashboard.Resize += PanelDashboard_ResizeForDatacenterOverview;
            }

            PositionDatacenterOverviewCard();
            LayoutDatacenterOverviewCard();
            UpdateDatacenterOverviewCard();
        }

        private Label CreateDatacenterCardLabel(string text, Font font, Color color, ContentAlignment alignment)
        {
            return new Label
            {
                Text = text,
                Font = font,
                ForeColor = color,
                BackColor = Color.Transparent,
                AutoSize = false,
                AutoEllipsis = true,
                TextAlign = alignment
            };
        }

        private void PanelDashboard_ResizeForDatacenterOverview(object sender, EventArgs e)
        {
            PositionDatacenterOverviewCard();
            LayoutDatacenterOverviewCard();
        }

        private void DatacenterOverviewPanel_Resize(object sender, EventArgs e)
        {
            LayoutDatacenterOverviewCard();
        }

        private void PositionDatacenterOverviewCard()
        {
            if (panelDashboard == null || _datacenterOverviewPanel == null) return;

            int margin = 24;

            if (panelDashboard.ClientSize.Width < 420)
                margin = 12;

            int width = Math.Max(220, panelDashboard.ClientSize.Width - (margin * 2));
            int availableHeight = Math.Max(1, panelDashboard.ClientSize.Height - (margin * 2));

            // Die Datacenter-Card soll kompakt bleiben, damit der Cluster-Log darunter
            // nicht unnötig weit nach unten rutscht. Bei schmalen Fenstern wird sie höher,
            // weil Diagramm und Werte dann untereinander stehen.
            int preferredHeight = panelDashboard.ClientSize.Width < 520 ? 245 : 175;
            int height = Math.Max(160, Math.Min(preferredHeight, availableHeight));

            _datacenterOverviewPanel.Bounds = new Rectangle(margin, margin, width, height);
        }

        private void LayoutDatacenterOverviewCard()
        {
            if (_datacenterOverviewPanel == null) return;

            int width = _datacenterOverviewPanel.ClientSize.Width;
            int height = _datacenterOverviewPanel.ClientSize.Height;
            if (width <= 0 || height <= 0) return;

            int padding = width < 520 ? 22 : 32;
            int titleTop = 20;
            int titleHeight = 30;

            _datacenterCardTitleLabel.Bounds = new Rectangle(
                padding,
                titleTop,
                Math.Max(1, width - (padding * 2)),
                titleHeight
            );

            if (_datacenterCardSubtitleLabel != null)
            {
                _datacenterCardSubtitleLabel.Text = string.Empty;
                _datacenterCardSubtitleLabel.Visible = false;
                _datacenterCardSubtitleLabel.Bounds = Rectangle.Empty;
            }

            Label[] valueLabels =
            {
                _datacenterOnlineValueLabel,
                _datacenterOfflineValueLabel,
                _datacenterTotalValueLabel
            };

            Label[] textLabels =
            {
                _datacenterOnlineTextLabel,
                _datacenterOfflineTextLabel,
                _datacenterTotalTextLabel
            };

            foreach (Label label in valueLabels.Concat(textLabels).Where(l => l != null))
            {
                label.TextAlign = ContentAlignment.MiddleCenter;
                label.Visible = true;
            }

            int metricTop = titleTop + titleHeight + 28;
            int valueHeight = 38;
            int textHeight = 22;

            bool stackedLayout = width < 420;

            if (!stackedLayout)
            {
                int availableWidth = Math.Max(1, width - (padding * 2));
                int columnGap = width < 640 ? 16 : 28;
                int columnWidth = Math.Max(1, (availableWidth - (columnGap * 2)) / 3);
                int startX = padding;

                for (int i = 0; i < 3; i++)
                {
                    int x = startX + (i * (columnWidth + columnGap));

                    if (valueLabels[i] != null)
                        valueLabels[i].Bounds = new Rectangle(x, metricTop, columnWidth, valueHeight);

                    if (textLabels[i] != null)
                        textLabels[i].Bounds = new Rectangle(x, metricTop + valueHeight - 2, columnWidth, textHeight);
                }

                return;
            }

            int rowHeight = 54;
            int rowGap = 8;
            int rowWidth = Math.Max(1, width - (padding * 2));
            int y = metricTop - 4;

            for (int i = 0; i < 3; i++)
            {
                if (valueLabels[i] != null)
                    valueLabels[i].Bounds = new Rectangle(padding, y, rowWidth, 30);

                if (textLabels[i] != null)
                    textLabels[i].Bounds = new Rectangle(padding, y + 28, rowWidth, 20);

                y += rowHeight + rowGap;
            }
        }

        private void CaptureDashboardOriginalVisibility()
        {
            if (panelDashboard == null || _dashboardOriginalVisibilityCaptured) return;

            _dashboardOriginalVisibility.Clear();

            foreach (Control control in panelDashboard.Controls)
            {
                if (control == _datacenterOverviewPanel) continue;
                _dashboardOriginalVisibility[control] = control.Visible;
            }

            _dashboardOriginalVisibilityCaptured = true;
        }

        private void ShowDatacenterOverviewCard()
        {
            EnsureDatacenterOverviewCard();
            if (panelDashboard == null || _datacenterOverviewPanel == null) return;

            CaptureDashboardOriginalVisibility();

            foreach (Control control in panelDashboard.Controls)
            {
                if (control == _datacenterOverviewPanel) continue;
                control.Visible = false;
            }

            _datacenterOverviewPanel.Visible = true;
            _datacenterOverviewPanel.BringToFront();
            UpdateDatacenterOverviewCard();
        }

        private void HideDatacenterOverviewCard()
        {
            if (panelDashboard == null) return;

            if (_datacenterOverviewPanel != null)
                _datacenterOverviewPanel.Visible = false;

            if (!_dashboardOriginalVisibilityCaptured) return;

            foreach (KeyValuePair<Control, bool> item in _dashboardOriginalVisibility.ToList())
            {
                Control control = item.Key;
                if (control == null || control.IsDisposed || control == _datacenterOverviewPanel) continue;
                if (control.Parent == panelDashboard)
                    control.Visible = item.Value;
            }
        }

        private void UpdateDatacenterOverviewCard()
        {
            int totalNodes = _cachedNodes != null ? _cachedNodes.Count : 0;
            int onlineNodes = _cachedNodes != null
                ? _cachedNodes.Count(n => string.Equals(n.Status, "online", StringComparison.OrdinalIgnoreCase))
                : 0;
            int offlineNodes = Math.Max(0, totalNodes - onlineNodes);

            if (_datacenterOnlineValueLabel != null)
                _datacenterOnlineValueLabel.Text = onlineNodes.ToString();

            if (_datacenterOfflineValueLabel != null)
                _datacenterOfflineValueLabel.Text = offlineNodes.ToString();

            if (_datacenterTotalValueLabel != null)
                _datacenterTotalValueLabel.Text = totalNodes.ToString();

            if (_datacenterTotalTextLabel != null)
                _datacenterTotalTextLabel.Text = "Nodes gesamt";

            if (_datacenterCardSubtitleLabel != null)
            {
                _datacenterCardSubtitleLabel.Text = string.Empty;
                _datacenterCardSubtitleLabel.Visible = false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // DATA REFRESH
        // ─────────────────────────────────────────────────────────────────────

        public async Task RefreshDataAsync()
        {
            lblSelectedResource.Text = "Loading cluster information...";
            treeResources.BeginUpdate();

            treeResources.Nodes.Clear();

            var datacenterNode = new TreeNode("🌐 Datacenter")
            {
                Tag = new ResourceTag { Type = "datacenter", Name = "Datacenter" },
                ToolTipText = "Datacenter"
            };
            treeResources.Nodes.Add(datacenterNode);

            var apiNodes = await _client.GetNodesAsync();
            _cachedNodes = apiNodes;

            if (apiNodes.Count == 0)
            {
                lblSelectedResource.Text = "Error: No nodes detected (Unauthorized or connection issue)";
                lblSelectedResource.ForeColor = Color.FromArgb(239, 68, 68);
            }
            else
            {
                lblSelectedResource.ForeColor = Color.White;
            }

            foreach (var node in apiNodes)
            {
                var isOnline = node.Status == "online";
                string statusDot = isOnline ? "🟢" : "🔴";
                string nodeDisplayName = $"{statusDot} 🗄️ {node.Node}";

                var nodeTreeNode = new TreeNode(nodeDisplayName)
                {
                    Tag = new ResourceTag { Type = "node", NodeName = node.Node, Name = node.Node, Data = node },
                    ToolTipText = nodeDisplayName
                };
                datacenterNode.Nodes.Add(nodeTreeNode);

                if (isOnline)
                {
                    var vms = await _client.GetVmsAsync(node.Node);
                    var vmsGroupNode = new TreeNode("🖥️ Virtual Machines")
                    {
                        Tag = new ResourceTag { Type = "group_vm", NodeName = node.Node },
                        ToolTipText = "Virtual Machines"
                    };
                    nodeTreeNode.Nodes.Add(vmsGroupNode);

                    foreach (var vm in vms)
                    {
                        if (vm.IsTemplate) continue;

                        string vmDisplay = $"🖥️ [{vm.VmId}] {vm.Name}";
                        vmsGroupNode.Nodes.Add(new TreeNode(vmDisplay)
                        {
                            Tag = new ResourceTag { Type = "vm", NodeName = node.Node, VmId = vm.VmId, Name = vm.Name, Data = vm },
                            ToolTipText = vmDisplay
                        });
                    }

                    var lxcs = await _client.GetLxcsAsync(node.Node);
                    var lxcsGroupNode = new TreeNode("📦 Containers (LXC)")
                    {
                        Tag = new ResourceTag { Type = "group_lxc", NodeName = node.Node },
                        ToolTipText = "Containers (LXC)"
                    };
                    nodeTreeNode.Nodes.Add(lxcsGroupNode);

                    foreach (var lxc in lxcs)
                    {
                        string lxcDisplay = $"📦 [{lxc.VmId}] {lxc.Name}";
                        lxcsGroupNode.Nodes.Add(new TreeNode(lxcDisplay)
                        {
                            Tag = new ResourceTag { Type = "lxc", NodeName = node.Node, VmId = lxc.VmId, Name = lxc.Name, Data = lxc },
                            ToolTipText = lxcDisplay
                        });
                    }

                    var storages = await _client.GetStorageAsync(node.Node);
                    var storageGroupNode = new TreeNode("💾 Storage Pools")
                    {
                        Tag = new ResourceTag { Type = "group_storage", NodeName = node.Node },
                        ToolTipText = "Storage Pools"
                    };
                    nodeTreeNode.Nodes.Add(storageGroupNode);

                    foreach (var store in storages)
                    {
                        string storeDisplay = store.Active
                            ? $"💾 {store.Storage} ({store.Type})"
                            : $"⚠️ 💾 {store.Storage} ({store.Type})";

                        storageGroupNode.Nodes.Add(new TreeNode(storeDisplay)
                        {
                            Tag = new ResourceTag { Type = "storage", NodeName = node.Node, Name = store.Storage, Data = store },
                            ToolTipText = store.Active ? storeDisplay : "Nicht erreichbar"
                        });
                    }
                }
            }

            treeResources.ExpandAll();
            treeResources.EndUpdate();
            ResetTreeScrollToTop();

            BeginInvoke(new Action(() =>
            {
                ResetTreeScrollToTop();
            }));

            lblSelectedResource.Text = "Cluster ready.";

            await RefreshTasksLogAsync();

            if (treeResources.SelectedNode != null)
            {
                var tag = treeResources.SelectedNode.Tag as ResourceTag;
                UpdateUIForSelectedResource(tag);
            }

            BeginInvoke(new Action(() =>
            {
                PositionTreeScrollbar();
                HideNativeTreeScrollbars();
                UpdateTreeScrollbar();
                treeResources.Invalidate();
            }));
        }

        private async Task RefreshTasksLogAsync()
        {
            string fallbackNode = _cachedNodes.FirstOrDefault(n => n.Status == "online")?.Node;
            var tasks = await _client.GetTasksAsync(fallbackNode);

            gridTasks.Rows.Clear();
            foreach (var task in tasks)
            {
                string timeStr = DateTimeOffset.FromUnixTimeSeconds(task.StartTime).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
                string desc = $"{task.Type.ToUpper()} {task.Id}";

                int index = gridTasks.Rows.Add(timeStr, task.Node, task.User, desc, task.Status);
                var row = gridTasks.Rows[index];

                if (task.Status == "OK")
                    row.Cells[4].Style.ForeColor = Color.FromArgb(34, 197, 94);
                else if (task.Status == "RUNNING")
                    row.Cells[4].Style.ForeColor = Color.FromArgb(59, 130, 246);
                else
                    row.Cells[4].Style.ForeColor = Color.FromArgb(239, 68, 68);
            }

            BeginInvoke(new Action(() =>
            {
                PositionGridScrollbar();
                _gridScrollbarHider?.HideNow();
                UpdateGridScrollbar();
            }));
        }

        // ─────────────────────────────────────────────────────────────────────
        // SELECTION & UI UPDATE
        // ─────────────────────────────────────────────────────────────────────

        private bool IsTreeCategoryNode(TreeNode node)
        {
            var tag = node?.Tag as ResourceTag;
            if (tag == null) return false;

            return tag.Type == "group_vm" ||
                   tag.Type == "group_lxc" ||
                   tag.Type == "group_storage";
        }

        private void treeResources_BeforeSelect(object sender, TreeViewCancelEventArgs e)
        {
            if (!IsTreeCategoryNode(e.Node)) return;

            e.Cancel = true;

            BeginInvoke(new Action(() =>
            {
                if (_lastSelectableTreeNode != null &&
                    _lastSelectableTreeNode.TreeView == treeResources &&
                    treeResources.SelectedNode != _lastSelectableTreeNode)
                {
                    treeResources.SelectedNode = _lastSelectableTreeNode;
                }

                PositionTreeScrollbar();
                HideNativeTreeScrollbars();
                UpdateTreeScrollbar();
                treeResources.Invalidate();
            }));
        }

        private void treeResources_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (IsTreeCategoryNode(e.Node))
            {
                BeginInvoke(new Action(() =>
                {
                    if (_lastSelectableTreeNode != null &&
                        _lastSelectableTreeNode.TreeView == treeResources &&
                        treeResources.SelectedNode != _lastSelectableTreeNode)
                    {
                        treeResources.SelectedNode = _lastSelectableTreeNode;
                    }

                    treeResources.Invalidate();
                }));
                return;
            }

            var tag = e.Node.Tag as ResourceTag;
            if (tag == null) return;

            _lastSelectableTreeNode = e.Node;
            _lastSelectedKey = $"{tag.Type}_{tag.NodeName}_{tag.VmId}";
            UpdateUIForSelectedResource(tag);

            BeginInvoke(new Action(() =>
            {
                PositionTreeScrollbar();
                HideNativeTreeScrollbars();
                UpdateTreeScrollbar();
                treeResources.Invalidate();
            }));
        }

        private void UpdateUIForSelectedResource(ResourceTag tag)
        {
            if (tag == null) return;

            lblSelectedResource.Text = tag.Name ?? "Resource";
            UpdateActionButtonsState(tag);

            bool isConfigurable = tag.Type == "vm" || tag.Type == "lxc";
            if (btnTabConfig != null)
            {
                btnTabConfig.Enabled = isConfigurable;
                if (!isConfigurable && _configPanel != null && _configPanel.Visible)
                {
                    SwitchToTab("dashboard");
                }
            }

            if (tag.Type == "datacenter")
            {
                ShowDatacenterOverviewCard();
                SetResourceStatusLabel($"Nodes: {_cachedNodes.Count(n => string.Equals(n.Status, "online", StringComparison.OrdinalIgnoreCase))} / {_cachedNodes.Count} Online");

                btnTabConsole.Enabled = false;
                lblConsoleWarning.Text = "Select a specific node, VM or LXC to open an interactive console shell.";
                SwitchToTab("dashboard");
                ShowDatacenterOverviewCard();
            }
            else if (tag.Type == "node")
            {
                HideDatacenterOverviewCard();
                var node = tag.Data as PveNode;
                if (node != null)
                {
                    lblResourceType.Text = "Resource Type: Physical Node";
                    lblResourceID.Text = "Node Name: " + node.Node;
                    lblSpecsCores.Text = "CPU Cores: " + node.MaxCpu;
                    lblSpecsMemory.Text = "Total Memory: " + FormatBytes(node.MaxMem);
                    lblUptime.Text = "Node Uptime: " + FormatUptime(node.Uptime);
                    SetResourceStatusLabel("Status: " + node.Status.ToUpper(), node.Status);

                    lblDetailNode.Text = "Host Node: " + node.Node;
                    lblDetailHa.Text = "HA State: Local Node";
                    lblDetailIp.Text = "IP Address: " + _client.Host;
                    lblDetailDisk.Text = "Local Disk: " + FormatBytes(node.MaxDisk);

                    chartCpu.AddValue(node.Cpu * 100);
                    chartRam.AddValue(node.MaxMem > 0 ? ((double)node.Mem / node.MaxMem) * 100 : 0);
                }

                btnTabConsole.Enabled = true;
                btnTabConsole.Text = "Node Shell";
                lblConsoleWarning.Text = "Connecting to Node host shell...";
            }
            else if (tag.Type == "vm")
            {
                HideDatacenterOverviewCard();
                var vm = tag.Data as PveVm;
                if (vm != null)
                {
                    lblResourceType.Text = "Resource Type: Qemu VM";
                    lblResourceID.Text = "VM ID: " + vm.VmId;
                    lblSpecsCores.Text = "vCPU Cores: " + vm.MaxCpu;
                    lblSpecsMemory.Text = "RAM Allocated: " + FormatBytes(vm.MaxMem);
                    lblUptime.Text = "VM Uptime: " + FormatUptime(vm.Uptime);
                    SetResourceStatusLabel("Status: " + vm.Status.ToUpper(), vm.Status);

                    lblDetailNode.Text = "Host Node: " + tag.NodeName;
                    lblDetailHa.Text = "HA State: Managed";
                    lblDetailIp.Text = "IP Address: Querying...";
                    lblDetailDisk.Text = "Disk usage: N/A";

                    chartCpu.AddValue(vm.Cpu * 100);
                    chartRam.AddValue(vm.MaxMem > 0 ? ((double)vm.Mem / vm.MaxMem) * 100 : 0);

                    FetchVmIpAddressAsync(tag.NodeName, vm.VmId);
                }

                btnTabConsole.Enabled = true;
                btnTabConsole.Text = "noVNC Console";
                lblConsoleWarning.Text = "Connecting to VNC console...";
            }
            else if (tag.Type == "lxc")
            {
                HideDatacenterOverviewCard();
                var lxc = tag.Data as PveLxc;
                if (lxc != null)
                {
                    lblResourceType.Text = "Resource Type: LXC Container";
                    lblResourceID.Text = "Container ID: " + lxc.VmId;
                    lblSpecsCores.Text = "CPU Cores: " + lxc.MaxCpu;
                    lblSpecsMemory.Text = "RAM Allocated: " + FormatBytes(lxc.MaxMem);
                    lblUptime.Text = "Container Uptime: " + FormatUptime(lxc.Uptime);
                    SetResourceStatusLabel("Status: " + lxc.Status.ToUpper(), lxc.Status);

                    lblDetailNode.Text = "Host Node: " + tag.NodeName;
                    lblDetailHa.Text = "HA State: Managed";
                    lblDetailIp.Text = "IP Address: Querying...";
                    lblDetailDisk.Text = "Disk usage: N/A";

                    chartCpu.AddValue(lxc.Cpu * 100);
                    chartRam.AddValue(lxc.MaxMem > 0 ? ((double)lxc.Mem / lxc.MaxMem) * 100 : 0);

                    FetchLxcIpAddressAsync(tag.NodeName, lxc.VmId);
                }

                btnTabConsole.Enabled = true;
                btnTabConsole.Text = "Container Shell";
                lblConsoleWarning.Text = "Connecting to container terminal...";
            }
            else if (tag.Type == "storage")
            {
                HideDatacenterOverviewCard();
                var store = tag.Data as PveStorage;
                if (store != null)
                {
                    lblResourceType.Text = "Resource Type: Storage Pool";
                    lblResourceID.Text = "Storage: " + store.Storage;
                    lblSpecsCores.Text = "Type: " + store.Type;
                    lblSpecsMemory.Text = "Capacity: " + FormatBytes(store.Total);
                    lblUptime.Text = "Used: " + FormatBytes(store.Used);
                    SetResourceStatusLabel("Status: " + (store.Active ? "ACTIVE" : "INACTIVE"), store.Active ? "active" : "inactive");

                    lblDetailNode.Text = "Host Node: " + tag.NodeName;
                    lblDetailHa.Text = "HA State: Local Storage";
                    lblDetailIp.Text = "Shared: Local Device";
                    lblDetailDisk.Text = "Free: " + FormatBytes(store.Total - store.Used);

                    chartCpu.AddValue(0);
                    chartRam.AddValue(store.Total > 0 ? ((double)store.Used / store.Total) * 100 : 0);
                }

                btnTabConsole.Enabled = false;
                lblConsoleWarning.Text = "Select a specific node, VM or LXC to open an interactive console shell.";
                SwitchToTab("dashboard");
            }

            if (panelConsole.Visible)
            {
                LoadConsoleForSelectedResource();
            }
        }

        private async void FetchVmIpAddressAsync(string node, int vmid)
        {
            string ip = await _client.GetVmIpAsync(node, vmid);

            if (treeResources.SelectedNode != null)
            {
                var tag = treeResources.SelectedNode.Tag as ResourceTag;
                if (tag != null && tag.Type == "vm" && tag.VmId == vmid)
                    lblDetailIp.Text = "IP Address: " + ip;
            }
        }

        private async void FetchLxcIpAddressAsync(string node, int vmid)
        {
            string ip = await _client.GetLxcIpAsync(node, vmid);

            if (treeResources.SelectedNode != null)
            {
                var tag = treeResources.SelectedNode.Tag as ResourceTag;
                if (tag != null && tag.Type == "lxc" && tag.VmId == vmid)
                    lblDetailIp.Text = "IP Address: " + ip;
            }
        }

        private void UpdateActionButtonsState(ResourceTag tag)
        {
            if (tag == null || tag.Type == "datacenter" || tag.Type == "group_vm" ||
                tag.Type == "group_lxc" || tag.Type == "group_storage" || tag.Type == "storage")
            {
                btnStart.Enabled = false;
                btnStop.Enabled = false;
                btnShutdown.Enabled = false;
                btnReboot.Enabled = false;
                btnDelete.Enabled = false;
                RefreshActionButtonVisualStates();
                return;
            }

            if (tag.Type == "node")
            {
                btnStart.Enabled = false;
                btnStop.Enabled = false;
                btnShutdown.Enabled = false;
                btnReboot.Enabled = false;
                btnDelete.Enabled = false;
                RefreshActionButtonVisualStates();
                return;
            }

            btnDelete.Enabled = true;

            string status = "";
            if (tag.Type == "vm" && tag.Data is PveVm vm) status = vm.Status;
            if (tag.Type == "lxc" && tag.Data is PveLxc lxc) status = lxc.Status;

            if (status == "running")
            {
                btnStart.Enabled = false;
                btnStop.Enabled = true;
                btnShutdown.Enabled = true;
                btnReboot.Enabled = true;
            }
            else
            {
                btnStart.Enabled = true;
                btnStop.Enabled = false;
                btnShutdown.Enabled = false;
                btnReboot.Enabled = false;
            }

            RefreshActionButtonVisualStates();
        }

        // ─────────────────────────────────────────────────────────────────────
        // TAB SWITCHING & CONSOLE
        // ─────────────────────────────────────────────────────────────────────

        private void SwitchToTab(string tabName)
        {
            panelDashboard.Visible = false;
            panelConsole.Visible = false;
            if (_configPanel != null) _configPanel.Visible = false;

            ResetTabButtonColors();

            if (tabName == "dashboard")
            {
                panelDashboard.Visible = true;
                btnTabDashboard.BackColor = Color.FromArgb(249, 115, 22);
                btnTabDashboard.ForeColor = Color.White;
            }
            else if (tabName == "console")
            {
                panelConsole.Visible = true;
                btnTabConsole.BackColor = Color.FromArgb(249, 115, 22);
                btnTabConsole.ForeColor = Color.White;
                LoadConsoleForSelectedResource();
            }
            else if (tabName == "config")
            {
                if (_configPanel != null)
                {
                    _configPanel.Visible = true;
                    btnTabConfig.BackColor = Color.FromArgb(249, 115, 22);
                    btnTabConfig.ForeColor = Color.White;
                    LoadConfigForSelectedResource();
                }
            }

            BeginInvoke(new Action(() =>
            {
                PositionTreeScrollbar();
                HideNativeTreeScrollbars();
                UpdateTreeScrollbar();
                treeResources.Invalidate();
            }));
        }

        private void ResetTabButtonColors()
        {
            var transparent = Color.Transparent;
            var inactiveText = Color.FromArgb(203, 213, 225);

            btnTabDashboard.BackColor = transparent;
            btnTabDashboard.ForeColor = inactiveText;

            btnTabConsole.BackColor = transparent;
            btnTabConsole.ForeColor = inactiveText;

            if (btnTabConfig != null)
            {
                btnTabConfig.BackColor = transparent;
                btnTabConfig.ForeColor = inactiveText;
            }
        }

        private async void LoadConfigForSelectedResource()
        {
            var node = treeResources.SelectedNode;
            if (node == null || _configPanel == null) return;

            var tag = node.Tag as ResourceTag;
            if (tag == null || (tag.Type != "vm" && tag.Type != "lxc")) return;

            await _configPanel.LoadConfigAsync(tag.NodeName, tag.VmId, tag.Type);
        }

        private async void LoadConsoleForSelectedResource()
        {
            var node = treeResources.SelectedNode;
            if (node == null) return;

            var tag = node.Tag as ResourceTag;
            if (tag == null || tag.Type == "datacenter" || tag.Type == "group_vm" ||
                tag.Type == "group_lxc" || tag.Type == "group_storage" || tag.Type == "storage")
            {
                webViewConsole.Visible = false;
                lblConsoleWarning.Visible = true;
                lblConsoleWarning.Text = "Select a VM, Container or Node to view its interactive Console/Shell.";
                return;
            }

            string consoleUrl = "";
            if (tag.Type == "node")
                consoleUrl = $"https://{_client.Host}:{_client.Port}/?console=shell&novnc=1&node={tag.NodeName}";
            else if (tag.Type == "vm")
                consoleUrl = $"https://{_client.Host}:{_client.Port}/?console=kvm&novnc=1&vmid={tag.VmId}&node={tag.NodeName}";
            else if (tag.Type == "lxc")
            {
                consoleUrl = $"https://{_client.Host}:{_client.Port}/?console=lxc&xtermjs=1&vmid={tag.VmId}&node={tag.NodeName}";
            }

            lblConsoleWarning.Visible = true;
            webViewConsole.Visible = false;

            try
            {
                if (!_webViewInitialized)
                {
                    var options = new CoreWebView2EnvironmentOptions("--ignore-certificate-errors");
                    string userDataFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProxmoxVEGui", "WebView2");
                    var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
                    await webViewConsole.EnsureCoreWebView2Async(env);
                    _webViewInitialized = true;
                }

                var cookie = webViewConsole.CoreWebView2.CookieManager.CreateCookie("PVEAuthCookie", _client.Ticket, _client.Host, "/");
                webViewConsole.CoreWebView2.CookieManager.AddOrUpdateCookie(cookie);

                webViewConsole.CoreWebView2.Navigate(consoleUrl);

                lblConsoleWarning.Visible = false;
                webViewConsole.Visible = true;
            }
            catch (Exception ex)
            {
                lblConsoleWarning.Text = $"Failed to load console: {ex.Message}";
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // BUTTON EVENTS
        // ─────────────────────────────────────────────────────────────────────

        private void btnTabDashboard_Click(object sender, EventArgs e) => SwitchToTab("dashboard");
        private void btnTabConsole_Click(object sender, EventArgs e) => SwitchToTab("console");
        private void btnTabConfig_Click(object sender, EventArgs e) => SwitchToTab("config");

        private async void btnRefresh_Click(object sender, EventArgs e)
        {
            await RefreshDataAsync();
        }

        private async void btnCheckUpdate_Click(object sender, EventArgs e)
        {
            btnCheckUpdate.Enabled = false;
            btnCheckUpdate.Text = "Checking...";
            await Updater.CheckForUpdatesAsync(this, silent: false);
            btnCheckUpdate.Enabled = true;
            btnCheckUpdate.Text = "Check Update";
        }

        private async void PerformPowerAction(string action)
        {
            var node = treeResources.SelectedNode;
            if (node == null) return;

            var tag = node.Tag as ResourceTag;
            if (tag == null || (tag.Type != "vm" && tag.Type != "lxc")) return;

            lblSelectedResource.Text = $"Executing power action ({action})...";
            bool success = await _client.VMActionAsync(tag.NodeName, tag.VmId, tag.Type == "vm" ? "qemu" : "lxc", action);

            if (success)
            {
                MessageBox.Show($"Command '{action}' sent successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                await RefreshDataAsync();
            }
            else
            {
                MessageBox.Show($"Failed to send command '{action}'.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                await RefreshDataAsync();
            }
        }

        private void btnStart_Click(object sender, EventArgs e) => PerformPowerAction("start");
        private void btnStop_Click(object sender, EventArgs e) => PerformPowerAction("stop");
        private void btnShutdown_Click(object sender, EventArgs e) => PerformPowerAction("shutdown");
        private void btnReboot_Click(object sender, EventArgs e) => PerformPowerAction("reboot");

        private async void btnDelete_Click(object sender, EventArgs e)
        {
            var node = treeResources.SelectedNode;
            if (node == null) return;

            var tag = node.Tag as ResourceTag;
            if (tag == null || (tag.Type != "vm" && tag.Type != "lxc")) return;

            var confirm = MessageBox.Show(
                $"Are you sure you want to permanently delete [{tag.VmId}] {tag.Name}?",
                "Confirm Delete",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (confirm == DialogResult.Yes)
            {
                lblSelectedResource.Text = "Deleting resource...";
                bool success = await _client.DeleteResourceAsync(tag.NodeName, tag.VmId, tag.Type == "vm" ? "qemu" : "lxc");

                if (success)
                {
                    MessageBox.Show("Resource deleted successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    await RefreshDataAsync();
                }
                else
                {
                    MessageBox.Show("Failed to delete resource. Ensure it is powered off.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    await RefreshDataAsync();
                }
            }
        }

        private void btnLogout_Click(object sender, EventArgs e)
        {
            DeleteRememberLoginFromDisk();
            this.Close();
        }

        private void DeleteRememberLoginFromDisk()
        {
            try
            {
                string profilesFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ProxmoxVEGui"
                );

                string rememberLoginFile = Path.Combine(profilesFolder, "remember_login.xml");

                if (File.Exists(rememberLoginFile))
                {
                    File.Delete(rememberLoginFile);
                }
            }
            catch
            {
                // Abmelden soll nicht blockiert werden, falls die Datei gerade gesperrt ist.
            }
        }

        private void btnCreateVm_Click(object sender, EventArgs e) => OpenCreateDialog("vm");
        private void btnCreateLxc_Click(object sender, EventArgs e) => OpenCreateDialog("lxc");

        private void OpenCreateDialog(string type)
        {
            var onlineNodes = _cachedNodes.Where(n => n.Status == "online").Select(n => n.Node).ToList();

            if (onlineNodes.Count == 0)
            {
                MessageBox.Show("No online nodes available to host new resources.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            int nextId = 100;
            var allResourceIds = new HashSet<int>();

            foreach (var node in treeResources.Nodes[0].Nodes)
            {
                var tn = node as TreeNode;
                if (tn == null) continue;

                foreach (TreeNode group in tn.Nodes)
                {
                    foreach (TreeNode resource in group.Nodes)
                    {
                        var tag = resource.Tag as ResourceTag;
                        if (tag != null && (tag.Type == "vm" || tag.Type == "lxc"))
                            allResourceIds.Add(tag.VmId);
                    }
                }
            }

            if (allResourceIds.Count > 0)
                nextId = allResourceIds.Max() + 1;

            var dialog = new CreateResourceDialog(this, _client, type, onlineNodes, nextId);
            dialog.ShowDialog();
        }

        // ─────────────────────────────────────────────────────────────────────
        // TIMER REFRESH
        // ─────────────────────────────────────────────────────────────────────

        private async void timerRefresh_Tick(object sender, EventArgs e)
        {
            try
            {
                await RefreshTasksLogAsync();

                var apiNodes = await _client.GetNodesAsync();
                _cachedNodes = apiNodes;

                if (treeResources.SelectedNode != null)
                {
                    var tag = treeResources.SelectedNode.Tag as ResourceTag;
                    if (tag == null) return;

                    if (tag.Type == "datacenter")
                    {
                        UpdateDatacenterOverviewCard();
                        SetResourceStatusLabel($"Nodes: {_cachedNodes.Count(n => string.Equals(n.Status, "online", StringComparison.OrdinalIgnoreCase))} / {_cachedNodes.Count} Online");
                    }
                    else if (tag.Type == "node")
                    {
                        var freshNode = _cachedNodes.FirstOrDefault(n => n.Node == tag.NodeName);
                        if (freshNode != null)
                        {
                            tag.Data = freshNode;
                            lblUptime.Text = "Node Uptime: " + FormatUptime(freshNode.Uptime);
                            SetResourceStatusLabel("Status: " + freshNode.Status.ToUpper(), freshNode.Status);
                            chartCpu.AddValue(freshNode.Cpu * 100);
                            chartRam.AddValue(freshNode.MaxMem > 0 ? ((double)freshNode.Mem / freshNode.MaxMem) * 100 : 0);
                        }
                    }
                    else if (tag.Type == "vm")
                    {
                        var vms = await _client.GetVmsAsync(tag.NodeName);
                        var freshVm = vms.FirstOrDefault(v => v.VmId == tag.VmId);
                        if (freshVm != null)
                        {
                            tag.Data = freshVm;
                            lblUptime.Text = "VM Uptime: " + FormatUptime(freshVm.Uptime);
                            SetResourceStatusLabel("Status: " + freshVm.Status.ToUpper(), freshVm.Status);
                            chartCpu.AddValue(freshVm.Cpu * 100);
                            chartRam.AddValue(freshVm.MaxMem > 0 ? ((double)freshVm.Mem / freshVm.MaxMem) * 100 : 0);
                            UpdateActionButtonsState(tag);
                            treeResources.Invalidate();
                        }
                    }
                    else if (tag.Type == "lxc")
                    {
                        var lxcs = await _client.GetLxcsAsync(tag.NodeName);
                        var freshLxc = lxcs.FirstOrDefault(l => l.VmId == tag.VmId);
                        if (freshLxc != null)
                        {
                            tag.Data = freshLxc;
                            lblUptime.Text = "Container Uptime: " + FormatUptime(freshLxc.Uptime);
                            SetResourceStatusLabel("Status: " + freshLxc.Status.ToUpper(), freshLxc.Status);
                            chartCpu.AddValue(freshLxc.Cpu * 100);
                            chartRam.AddValue(freshLxc.MaxMem > 0 ? ((double)freshLxc.Mem / freshLxc.MaxMem) * 100 : 0);
                            UpdateActionButtonsState(tag);
                            treeResources.Invalidate();
                        }
                    }
                }

                BeginInvoke(new Action(() =>
                {
                    PositionTreeScrollbar();
                    HideNativeTreeScrollbars();
                    UpdateTreeScrollbar();
                    treeResources.Invalidate();
                }));
            }
            catch
            {
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private static int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;

            while (Math.Round(number / 1024) >= 1 && counter < suffixes.Length - 1)
            {
                number /= 1024;
                counter++;
            }

            return string.Format("{0:n1} {1}", number, suffixes[counter]);
        }

        public static string FormatUptime(long seconds)
        {
            var time = TimeSpan.FromSeconds(seconds);

            if (time.TotalDays >= 1)
                return string.Format("{0}d {1}h {2}m", (int)time.TotalDays, time.Hours, time.Minutes);

            if (time.TotalHours >= 1)
                return string.Format("{0}h {1}m", time.Hours, time.Minutes);

            return string.Format("{0}m {1}s", time.Minutes, time.Seconds);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SUPPORT CLASSES
    // ─────────────────────────────────────────────────────────────────────────

    public class ResourceTag
    {
        public string Type { get; set; }
        public string NodeName { get; set; }
        public int VmId { get; set; }
        public string Name { get; set; }
        public object Data { get; set; }
    }

    public class ModernDashboardCardPanel : Panel
    {
        public Color FillColor { get; set; } = Color.FromArgb(17, 24, 39);
        public Color BorderColor { get; set; } = Color.FromArgb(51, 65, 85);
        public int Radius { get; set; } = 18;

        public ModernDashboardCardPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true
            );

            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            if (rect.Width <= 0 || rect.Height <= 0) return;

            using (GraphicsPath path = CreateRoundedRectanglePath(rect, Radius))
            using (SolidBrush brush = new SolidBrush(FillColor))
            using (Pen borderPen = new Pen(BorderColor, 1))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(borderPen, path);
            }
        }

        private GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();

            if (rect.Width <= 0 || rect.Height <= 0)
                return path;

            if (radius <= 0)
            {
                path.AddRectangle(rect);
                path.CloseFigure();
                return path;
            }

            radius = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
            int diameter = Math.Max(1, radius * 2);

            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();

            return path;
        }
    }

    public class NodeStatusDonutChart : Control
    {
        public int OnlineCount { get; set; }
        public int OfflineCount { get; set; }
        public Color OnlineColor { get; set; } = Color.FromArgb(34, 197, 94);
        public Color OfflineColor { get; set; } = Color.FromArgb(239, 68, 68);
        public Color EmptyColor { get; set; } = Color.FromArgb(51, 65, 85);
        public Color TextColor { get; set; } = Color.FromArgb(248, 250, 252);
        public Color MutedTextColor { get; set; } = Color.FromArgb(148, 163, 184);
        public Color HoleColor { get; set; } = Color.FromArgb(17, 24, 39);

        public NodeStatusDonutChart()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true
            );

            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.CompositingQuality = CompositingQuality.HighQuality;

            int online = Math.Max(0, OnlineCount);
            int offline = Math.Max(0, OfflineCount);
            int total = online + offline;

            int padding = 6;
            int size = Math.Min(Width, Height) - (padding * 2);
            if (size <= 20) return;

            Rectangle outerRect = new Rectangle(
                (Width - size) / 2,
                (Height - size) / 2,
                size,
                size
            );

            int ringThickness = Math.Max(18, size / 5);
            int innerSize = Math.Max(1, size - (ringThickness * 2));
            Rectangle innerRect = new Rectangle(
                outerRect.Left + ringThickness,
                outerRect.Top + ringThickness,
                innerSize,
                innerSize
            );

            using (SolidBrush emptyBrush = new SolidBrush(EmptyColor))
            using (SolidBrush onlineBrush = new SolidBrush(OnlineColor))
            using (SolidBrush offlineBrush = new SolidBrush(OfflineColor))
            using (SolidBrush holeBrush = new SolidBrush(HoleColor))
            {
                // Kein DrawArc mit dickem Pen verwenden: dabei wird der Kreis am Rand abgeschnitten.
                // Stattdessen wird ein echter Donut aus gefüllten Ellipsen/Pies gezeichnet.
                if (total <= 0)
                {
                    e.Graphics.FillEllipse(emptyBrush, outerRect);
                }
                else if (online > 0 && offline <= 0)
                {
                    e.Graphics.FillEllipse(onlineBrush, outerRect);
                }
                else if (offline > 0 && online <= 0)
                {
                    e.Graphics.FillEllipse(offlineBrush, outerRect);
                }
                else
                {
                    float onlineSweep = (float)(online * 360.0 / total);
                    onlineSweep = Math.Max(0.1F, Math.Min(359.9F, onlineSweep));
                    float offlineSweep = 360.0F - onlineSweep;

                    e.Graphics.FillPie(onlineBrush, outerRect, -90F, onlineSweep);
                    e.Graphics.FillPie(offlineBrush, outerRect, -90F + onlineSweep, offlineSweep);
                }

                e.Graphics.FillEllipse(holeBrush, innerRect);
            }

            string mainText = total.ToString();
            string subText = total == 1 ? "Node" : "Nodes";

            using (Font mainFont = new Font("Segoe UI", Math.Max(16F, size / 5.8F), FontStyle.Bold))
            using (Font subFont = new Font("Segoe UI", Math.Max(8.5F, size / 14.5F), FontStyle.Regular))
            using (SolidBrush mainBrush = new SolidBrush(TextColor))
            using (SolidBrush subBrush = new SolidBrush(MutedTextColor))
            {
                SizeF mainSize = e.Graphics.MeasureString(mainText, mainFont);
                SizeF subSize = e.Graphics.MeasureString(subText, subFont);

                float centerX = Width / 2F;
                float centerY = Height / 2F;
                float mainY = centerY - mainSize.Height + 5;
                float subY = centerY + 4;

                e.Graphics.DrawString(mainText, mainFont, mainBrush, centerX - (mainSize.Width / 2F), mainY);
                e.Graphics.DrawString(subText, subFont, subBrush, centerX - (subSize.Width / 2F), subY);
            }
        }
    }

    public class ModernAccountPanel : Panel
    {
        private bool _hover;

        public Color FillColor { get; set; } = Color.FromArgb(17, 24, 39);
        public Color HoverFillColor { get; set; } = Color.FromArgb(24, 35, 52);
        public Color BorderColor { get; set; } = Color.FromArgb(220, 249, 115, 22);
        public int Radius { get; set; } = 13;

        public ModernAccountPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true
            );

            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            if (rect.Width <= 0 || rect.Height <= 0) return;

            using (GraphicsPath path = CreateRoundedRectanglePath(rect, Radius))
            using (SolidBrush brush = new SolidBrush(_hover ? HoverFillColor : FillColor))
            using (Pen borderPen = new Pen(BorderColor, 1))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(borderPen, path);
            }

            int iconSize = 28;
            Rectangle iconRect = new Rectangle(13, Math.Max(0, (Height - iconSize) / 2), iconSize, iconSize);

            using (GraphicsPath iconPath = CreateRoundedRectanglePath(iconRect, iconSize / 2))
            using (SolidBrush iconBrush = new SolidBrush(Color.FromArgb(249, 115, 22)))
            {
                e.Graphics.FillPath(iconBrush, iconPath);
            }
        }

        private GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();

            if (rect.Width <= 0 || rect.Height <= 0)
                return path;

            if (radius <= 0)
            {
                path.AddRectangle(rect);
                path.CloseFigure();
                return path;
            }

            radius = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
            int diameter = Math.Max(1, radius * 2);

            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();

            return path;
        }
    }

    public class ModernScrollbarPart : Panel
    {
        private bool _hover;

        public Color FillColor { get; set; } = Color.FromArgb(249, 115, 22);
        public Color HoverColor { get; set; } = Color.FromArgb(251, 146, 60);
        public int Radius { get; set; } = 4;

        public ModernScrollbarPart()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.Selectable,
                true
            );

            BackColor = Color.Transparent;
            TabStop = false;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;

            if (CanFocus)
                Focus();

            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            if (rect.Width <= 0 || rect.Height <= 0) return;

            using (GraphicsPath path = CreateRoundedRectanglePath(rect, Radius))
            using (SolidBrush brush = new SolidBrush(_hover ? HoverColor : FillColor))
            {
                e.Graphics.FillPath(brush, path);
            }
        }

        private GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();

            if (rect.Width <= 0 || rect.Height <= 0)
                return path;

            if (radius <= 0)
            {
                path.AddRectangle(rect);
                path.CloseFigure();
                return path;
            }

            radius = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
            int diameter = Math.Max(1, radius * 2);

            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();

            return path;
        }
    }

    public class ModernContextMenuRenderer : ToolStripProfessionalRenderer
    {
        public ModernContextMenuRenderer() : base(new ModernContextMenuColors())
        {
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            Rectangle rect = new Rectangle(Point.Empty, e.Item.Size);

            if (e.Item.Selected && e.Item.Enabled)
            {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(249, 115, 22)))
                {
                    e.Graphics.FillRectangle(brush, rect);
                }
            }
            else
            {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(15, 23, 42)))
                {
                    e.Graphics.FillRectangle(brush, rect);
                }
            }
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(15, 23, 42)))
            {
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            Rectangle rect = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using (Pen pen = new Pen(Color.FromArgb(51, 65, 85)))
            {
                e.Graphics.DrawRectangle(pen, rect);
            }
        }
    }

    public class ModernContextMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(15, 23, 42);
        public override Color ImageMarginGradientBegin => Color.FromArgb(15, 23, 42);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(15, 23, 42);
        public override Color ImageMarginGradientEnd => Color.FromArgb(15, 23, 42);
        public override Color MenuItemSelected => Color.FromArgb(249, 115, 22);
        public override Color MenuItemBorder => Color.FromArgb(249, 115, 22);
        public override Color MenuBorder => Color.FromArgb(51, 65, 85);
        public override Color SeparatorDark => Color.FromArgb(51, 65, 85);
        public override Color SeparatorLight => Color.FromArgb(51, 65, 85);
    }

    public class TreeViewNativeScrollbarHider : NativeWindow
    {
        private TreeView _treeView;

        public event Action<int> MouseWheelScrollRequested;

        private const int SB_BOTH = 3;
        private const int WM_PAINT = 0x000F;
        private const int WM_SIZE = 0x0005;
        private const int WM_NCPAINT = 0x0085;
        private const int WM_VSCROLL = 0x0115;
        private const int WM_HSCROLL = 0x0114;
        private const int WM_MOUSEWHEEL = 0x020A;

        [DllImport("user32.dll")]
        private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

        public void Attach(TreeView treeView)
        {
            _treeView = treeView;
            if (_treeView == null) return;

            _treeView.HandleCreated += TreeView_HandleCreated;
            _treeView.HandleDestroyed += TreeView_HandleDestroyed;

            if (_treeView.IsHandleCreated)
            {
                AssignHandle(_treeView.Handle);
                HideNow();
            }
        }

        public void HideNow()
        {
            if (_treeView == null || _treeView.IsDisposed || !_treeView.IsHandleCreated) return;
            ShowScrollBar(_treeView.Handle, SB_BOTH, false);
        }

        private void TreeView_HandleCreated(object sender, EventArgs e)
        {
            if (_treeView == null) return;
            AssignHandle(_treeView.Handle);
            HideNow();
        }

        private void TreeView_HandleDestroyed(object sender, EventArgs e)
        {
            ReleaseHandle();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEWHEEL)
            {
                int delta = GetMouseWheelDelta(m.WParam);
                MouseWheelScrollRequested?.Invoke(delta);
                HideNow();
                return;
            }

            base.WndProc(ref m);

            if (m.Msg == WM_PAINT || m.Msg == WM_SIZE || m.Msg == WM_NCPAINT ||
                m.Msg == WM_VSCROLL || m.Msg == WM_HSCROLL)
            {
                HideNow();
            }
        }

        private static int GetMouseWheelDelta(IntPtr wParam)
        {
            long value = wParam.ToInt64();
            return unchecked((short)((value >> 16) & 0xffff));
        }
    }

    public class DataGridViewNativeScrollbarHider : NativeWindow
    {
        private DataGridView _grid;

        private const int SB_VERT = 1;
        private const int SB_BOTH = 3;
        private const int WM_PAINT = 0x000F;
        private const int WM_SIZE = 0x0005;
        private const int WM_NCPAINT = 0x0085;
        private const int WM_NCCALCSIZE = 0x0083;
        private const int WM_VSCROLL = 0x0115;
        private const int WM_HSCROLL = 0x0114;
        private const int WM_MOUSEWHEEL = 0x020A;

        // Style bits
        private const int GWL_STYLE = -16;
        private const int WS_VSCROLL = 0x00200000;
        private const int WS_HSCROLL = 0x00100000;

        [DllImport("user32.dll")]
        private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public void Attach(DataGridView grid)
        {
            _grid = grid;
            if (_grid == null) return;

            _grid.HandleCreated += (s, e) => { AssignHandle(_grid.Handle); HideNow(); };
            _grid.HandleDestroyed += (s, e) => ReleaseHandle();

            if (_grid.IsHandleCreated)
            {
                AssignHandle(_grid.Handle);
                HideNow();
            }
        }

        public void HideNow()
        {
            if (_grid == null || _grid.IsDisposed || !_grid.IsHandleCreated) return;

            // Remove scroll style bits so Windows stops reserving NC space for them
            int style = GetWindowLong(_grid.Handle, GWL_STYLE);
            int newStyle = style & ~WS_VSCROLL & ~WS_HSCROLL;
            if (style != newStyle)
                SetWindowLong(_grid.Handle, GWL_STYLE, newStyle);

            ShowScrollBar(_grid.Handle, SB_BOTH, false);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WM_PAINT || m.Msg == WM_SIZE ||
                m.Msg == WM_NCPAINT || m.Msg == WM_NCCALCSIZE ||
                m.Msg == WM_VSCROLL || m.Msg == WM_HSCROLL ||
                m.Msg == WM_MOUSEWHEEL)
            {
                HideNow();
            }
        }
    }
}