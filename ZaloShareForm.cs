using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ToolAffiliate
{
    public sealed class FbGroupEntry
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public bool Checked { get; set; }
        public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Url : $"{Name} | {Url}";
    }

    public sealed class AddlivetagConfig
    {
        public string ApiKey { get; set; } = "";
        public string AffiliateId { get; set; } = "";
        public string SubId1 { get; set; } = "zalo";
        public string SaleKeywords { get; set; } = "flash sale, giảm giá, deal 1k, tai nghe, gia dụng";
        public int ProductsPerBatch { get; set; } = 5;
        public int AutoCycleMinutes { get; set; } = 30;
        public int MinDelaySeconds { get; set; } = 15;
        public int MaxDelaySeconds { get; set; } = 30;
        public List<string> SavedGroups { get; set; } = new();
        public List<string> CheckedGroups { get; set; } = new();
        public List<FbGroupEntry> FbGroups { get; set; } = new();
    }

    public sealed class SaleDealItem
    {
        public string ItemId { get; set; } = "";
        public string Name { get; set; } = "";
        public string PriceText { get; set; } = "";
        public string CommissionText { get; set; } = "";
        public string ProductUrl { get; set; } = "";
        public string AffiliateUrl { get; set; } = "";
    }

    public sealed partial class MainForm : Form
    {
        private void InitializeZaloMenu() { }
        private void OpenZaloLogin() => OpenZaloForSelectedProducts();

        private void OpenZaloForSelectedProducts()
        {
            var selected = table.SelectedRows
                .Cast<DataGridViewRow>()
                .Select(row => row.DataBoundItem as ProductItem)
                .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.AffiliateUrl))
                .Cast<ProductItem>()
                .Distinct()
                .ToList();

            if (selected.Count == 0 && table.CurrentRow?.DataBoundItem is ProductItem current &&
                !string.IsNullOrWhiteSpace(current.AffiliateUrl))
            {
                selected.Add(current);
            }

            if (selected.Count == 0)
            {
                selected = items.Where(item => !string.IsNullOrWhiteSpace(item.AffiliateUrl)).ToList();
            }

            string initialMessage = "";
            if (selected.Count > 0)
            {
                var lines = new List<string> { "🔥 Tổng hợp Deal Sale Shopee đang giảm giá sâu:" };
                foreach (var product in selected)
                {
                    lines.Add("");
                    lines.Add("• " + (string.IsNullOrWhiteSpace(product.Name) ? "Sản phẩm Shopee" : product.Name.Trim()));
                    lines.Add("👉 Mua ngay: " + product.AffiliateUrl.Trim());
                }
                lines.Add("");
                lines.Add("Số lượng mã giảm giá có hạn, mọi người bấm vào link xem giá ưu đãi trên Shopee nhé!");
                initialMessage = string.Join(Environment.NewLine, lines);
            }

            new ZaloShareForm(initialMessage).Show(this);
        }
    }

    internal sealed class ZaloShareForm : Form
    {
        private static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(25) };
        private readonly TabControl browserTabs = new();
        private readonly WebView2 zaloBrowser = new();
        private readonly WebView2 fbBrowser = new();

        private readonly TextBox apiKeyBox = new();
        private readonly TextBox affIdBox = new();
        private readonly TextBox subIdBox = new();
        private readonly TextBox keywordsBox = new();
        private readonly NumericUpDown batchCountInput = new();
        private readonly NumericUpDown cycleMinutesInput = new();
        private readonly TextBox messageBox = new();

        private readonly CheckedListBox zaloGroupsCheckList = new();
        private readonly TextBox manualZaloGroupInput = new();

        private readonly CheckedListBox fbGroupsCheckList = new();
        private readonly TextBox manualFbGroupInput = new();

        private readonly NumericUpDown minDelayInput = new();
        private readonly NumericUpDown maxDelayInput = new();
        private readonly Label statusLabel = new();
        private readonly List<CoreWebView2Frame> zaloFrames = new();
        private readonly HashSet<string> sentItemIds = new(StringComparer.OrdinalIgnoreCase);

        private readonly string zaloProfilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolAffiliate", "ZaloProfile");
        private readonly string fbProfilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolAffiliate", "FacebookProfile");
        private readonly string configFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolAffiliate", "addlivetag-config-v4.json");

        private bool initialized;
        private bool autoPilotRunning;
        private CancellationTokenSource? sendCts;
        private int keywordRotateIndex;

        public ZaloShareForm(string message)
        {
            Text = "Tool Affiliate - Tự Động Gửi Zalo & Đăng Nhóm Facebook (Addlivetag Auto)";
            Width = 1340;
            Height = 920;
            MinimumSize = new Size(1080, 760);
            StartPosition = FormStartPosition.CenterParent;

            browserTabs.Dock = DockStyle.Fill;
            browserTabs.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);

            var zaloTabPage = new TabPage("💬 Trình duyệt Zalo Web");
            zaloBrowser.Dock = DockStyle.Fill;
            zaloTabPage.Controls.Add(zaloBrowser);

            var fbTabPage = new TabPage("📘 Trình duyệt Facebook (Đăng Nhóm)");
            fbBrowser.Dock = DockStyle.Fill;
            fbTabPage.Controls.Add(fbBrowser);

            browserTabs.TabPages.Add(zaloTabPage);
            browserTabs.TabPages.Add(fbTabPage);

            var bottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 360,
                Padding = new Padding(10, 8, 10, 6),
                BackColor = Color.FromArgb(246, 248, 252)
            };

            var configGroup = new Panel { Dock = DockStyle.Top, Height = 70 };

            var lblKey = new Label { Text = "API Key Addlivetag:", Left = 0, Top = 6, Width = 125, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
            apiKeyBox.SetBounds(126, 3, 250, 25);
            apiKeyBox.PlaceholderText = "Để trống nếu dùng sẵn link từ bảng...";

            var lblAffId = new Label { Text = "Affiliate ID:", Left = 386, Top = 6, Width = 75, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
            affIdBox.SetBounds(462, 3, 120, 25);
            affIdBox.PlaceholderText = "VD: 1739201xxxx";

            var lblSubId = new Label { Text = "SubID:", Left = 592, Top = 6, Width = 45 };
            subIdBox.SetBounds(638, 3, 75, 25);
            subIdBox.Text = "zalo";

            var lblCount = new Label { Text = "Số SP/đợt:", Left = 725, Top = 6, Width = 68 };
            batchCountInput.SetBounds(794, 3, 52, 25);
            batchCountInput.Minimum = 1; batchCountInput.Maximum = 20; batchCountInput.Value = 5;

            var lblCycle = new Label { Text = "Lặp lại sau (phút):", Left = 858, Top = 6, Width = 112 };
            cycleMinutesInput.SetBounds(972, 3, 58, 25);
            cycleMinutesInput.Minimum = 1; cycleMinutesInput.Maximum = 1440; cycleMinutesInput.Value = 30;

            var lblKw = new Label { Text = "Từ khóa săn Sale:", Left = 0, Top = 38, Width = 125, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
            keywordsBox.SetBounds(126, 35, 456, 25);
            keywordsBox.PlaceholderText = "flash sale, giảm giá, deal 1k, tai nghe, đồ gia dụng...";

            var lblDelay1 = new Label { Text = "Nghỉ chống spam:", Left = 592, Top = 38, Width = 115 };
            minDelayInput.SetBounds(712, 35, 52, 25);
            minDelayInput.Minimum = 5; minDelayInput.Maximum = 600; minDelayInput.Value = 20;

            var lblDelay2 = new Label { Text = "đến", Left = 769, Top = 38, Width = 30 };
            maxDelayInput.SetBounds(800, 35, 52, 25);
            maxDelayInput.Minimum = 5; maxDelayInput.Maximum = 600; maxDelayInput.Value = 40;

            var lblDelay3 = new Label { Text = "giây/nhóm", Left = 858, Top = 38, Width = 80 };

            configGroup.Controls.AddRange(new Control[]
            {
                lblKey, apiKeyBox, lblAffId, affIdBox, lblSubId, subIdBox,
                lblCount, batchCountInput, lblCycle, cycleMinutesInput,
                lblKw, keywordsBox, lblDelay1, minDelayInput, lblDelay2, maxDelayInput, lblDelay3
            });

            var splitBottom = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 8
            };

            var msgLabel = new Label
            {
                Text = "Nội dung gửi Zalo & đăng Nhóm Facebook:",
                Dock = DockStyle.Top,
                Height = 22,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            messageBox.Multiline = true;
            messageBox.ScrollBars = ScrollBars.Vertical;
            messageBox.Dock = DockStyle.Fill;
            messageBox.Font = new Font("Segoe UI", 9);
            messageBox.Text = message;
            splitBottom.Panel1.Controls.Add(messageBox);
            splitBottom.Panel1.Controls.Add(msgLabel);

            var targetTabs = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
            var zaloGroupsTab = new TabPage("💬 Tích chọn Nhóm Zalo");
            var fbGroupsTab = new TabPage("📘 Tích chọn Nhóm Facebook");

            var zaloGrpTop = new Panel { Dock = DockStyle.Top, Height = 28 };
            var loadZaloBtn = new Button { Text = "🔄 Quét Nhóm Zalo", Dock = DockStyle.Right, Width = 145, Height = 26, BackColor = Color.FromArgb(225, 240, 255) };
            var addOpenZaloBtn = new Button { Text = "➕ Thêm nhóm đang mở", Dock = DockStyle.Left, Width = 160, Height = 26, BackColor = Color.FromArgb(220, 248, 228) };
            zaloGrpTop.Controls.Add(loadZaloBtn);
            zaloGrpTop.Controls.Add(addOpenZaloBtn);

            var zaloGrpInputPanel = new Panel { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(0, 2, 0, 0) };
            manualZaloGroupInput.Dock = DockStyle.Fill;
            manualZaloGroupInput.PlaceholderText = "Gõ tên nhóm Zalo rồi bấm Thêm...";
            var addManualZaloBtn = new Button { Text = "Thêm", Dock = DockStyle.Right, Width = 60, Height = 25 };
            var cleanZaloBtn = new Button { Text = "Xóa bỏ tích", Dock = DockStyle.Right, Width = 95, Height = 25 };
            zaloGrpInputPanel.Controls.Add(manualZaloGroupInput);
            zaloGrpInputPanel.Controls.Add(addManualZaloBtn);
            zaloGrpInputPanel.Controls.Add(cleanZaloBtn);

            zaloGroupsCheckList.Dock = DockStyle.Fill;
            zaloGroupsCheckList.CheckOnClick = true;
            zaloGroupsCheckList.Font = new Font("Segoe UI", 9, FontStyle.Regular);
            zaloGroupsTab.Controls.Add(zaloGroupsCheckList);
            zaloGroupsTab.Controls.Add(zaloGrpInputPanel);
            zaloGroupsTab.Controls.Add(zaloGrpTop);

            var fbGrpTop = new Panel { Dock = DockStyle.Top, Height = 28 };
            var loadFbBtn = new Button { Text = "🔄 Quét Nhóm FB đã tham gia", Dock = DockStyle.Right, Width = 185, Height = 26, BackColor = Color.FromArgb(225, 240, 255) };
            var addOpenFbBtn = new Button { Text = "➕ Thêm nhóm FB đang mở", Dock = DockStyle.Left, Width = 175, Height = 26, BackColor = Color.FromArgb(220, 248, 228) };
            fbGrpTop.Controls.Add(loadFbBtn);
            fbGrpTop.Controls.Add(addOpenFbBtn);

            var fbGrpInputPanel = new Panel { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(0, 2, 0, 0) };
            manualFbGroupInput.Dock = DockStyle.Fill;
            manualFbGroupInput.PlaceholderText = "Dán link nhóm FB (https://www.facebook.com/groups/...) rồi bấm Thêm...";
            var addManualFbBtn = new Button { Text = "Thêm", Dock = DockStyle.Right, Width = 60, Height = 25 };
            var cleanFbBtn = new Button { Text = "Xóa bỏ tích", Dock = DockStyle.Right, Width = 95, Height = 25 };
            fbGrpInputPanel.Controls.Add(manualFbGroupInput);
            fbGrpInputPanel.Controls.Add(addManualFbBtn);
            fbGrpInputPanel.Controls.Add(cleanFbBtn);

            fbGroupsCheckList.Dock = DockStyle.Fill;
            fbGroupsCheckList.CheckOnClick = true;
            fbGroupsCheckList.Font = new Font("Segoe UI", 9, FontStyle.Regular);
            fbGroupsTab.Controls.Add(fbGroupsCheckList);
            fbGroupsTab.Controls.Add(fbGrpInputPanel);
            fbGroupsTab.Controls.Add(fbGrpTop);

            targetTabs.TabPages.Add(zaloGroupsTab);
            targetTabs.TabPages.Add(fbGroupsTab);
            splitBottom.Panel2.Controls.Add(targetTabs);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 42,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 6, 0, 0)
            };

            var autoBothBtn = new Button
            {
                Text = "🚀 BẬT AUTO CẢ ZALO + FACEBOOK (LẶP LẠI)",
                Width = 310,
                Height = 32,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(210, 245, 218)
            };

            var sendZaloOnceBtn = new Button
            {
                Text = "⚡ Gửi ngay Zalo",
                Width = 135,
                Height = 32,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(255, 242, 204)
            };

            var sendFbOnceBtn = new Button
            {
                Text = "📘 Đăng ngay Nhóm FB",
                Width = 165,
                Height = 32,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(218, 232, 255)
            };

            var fetchPreviewBtn = new Button
            {
                Text = "🔄 Lấy Sale từ API",
                Width = 140,
                Height = 32,
                BackColor = Color.FromArgb(235, 242, 250)
            };

            var stopBtn = new Button
            {
                Text = "⏹ Dừng",
                Width = 95,
                Height = 32,
                Enabled = false,
                BackColor = Color.FromArgb(255, 228, 228)
            };

            LoadSavedConfig();

            loadZaloBtn.Click += async (_, _) =>
            {
                browserTabs.SelectedIndex = 0;
                await LoadGroupsFromZaloAsync();
            };
            addOpenZaloBtn.Click += async (_, _) =>
            {
                browserTabs.SelectedIndex = 0;
                await AddCurrentOpenZaloGroupAsync();
            };
            addManualZaloBtn.Click += (_, _) =>
            {
                var name = manualZaloGroupInput.Text.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    AddOrCheckZaloGroupItem(name, true);
                    manualZaloGroupInput.Clear();
                    SaveConfig();
                }
            };
            cleanZaloBtn.Click += (_, _) =>
            {
                for (int i = zaloGroupsCheckList.Items.Count - 1; i >= 0; i--)
                    if (!zaloGroupsCheckList.GetItemChecked(i)) zaloGroupsCheckList.Items.RemoveAt(i);
                SaveConfig();
            };

            loadFbBtn.Click += async (_, _) =>
            {
                browserTabs.SelectedIndex = 1;
                targetTabs.SelectedIndex = 1;
                await LoadGroupsFromFacebookAsync();
            };
            addOpenFbBtn.Click += (_, _) =>
            {
                browserTabs.SelectedIndex = 1;
                targetTabs.SelectedIndex = 1;
                AddCurrentOpenFacebookGroup();
            };
            addManualFbBtn.Click += (_, _) =>
            {
                var raw = manualFbGroupInput.Text.Trim();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var url = NormalizeFbGroupUrl(raw);
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        AddOrCheckFbGroupItem(raw, url, true);
                        manualFbGroupInput.Clear();
                        SaveConfig();
                    }
                    else
                    {
                        MessageBox.Show(this, "Hãy dán đường dẫn nhóm Facebook hợp lệ (ví dụ: https://www.facebook.com/groups/123456).", "Facebook", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            };
            cleanFbBtn.Click += (_, _) =>
            {
                for (int i = fbGroupsCheckList.Items.Count - 1; i >= 0; i--)
                    if (!fbGroupsCheckList.GetItemChecked(i)) fbGroupsCheckList.Items.RemoveAt(i);
                SaveConfig();
            };

            EventHandler saveHandler = (_, _) => SaveConfig();
            apiKeyBox.TextChanged += saveHandler;
            affIdBox.TextChanged += saveHandler;
            subIdBox.TextChanged += saveHandler;
            keywordsBox.TextChanged += saveHandler;
            zaloGroupsCheckList.ItemCheck += (_, _) => BeginInvoke(new Action(SaveConfig));
            fbGroupsCheckList.ItemCheck += (_, _) => BeginInvoke(new Action(SaveConfig));
            batchCountInput.ValueChanged += saveHandler;
            cycleMinutesInput.ValueChanged += saveHandler;
            minDelayInput.ValueChanged += saveHandler;
            maxDelayInput.ValueChanged += saveHandler;

            autoBothBtn.Click += async (_, _) => await RunMasterFlowAsync(runZalo: true, runFb: true, loopForever: true, autoBothBtn, sendZaloOnceBtn, sendFbOnceBtn, fetchPreviewBtn, stopBtn);
            sendZaloOnceBtn.Click += async (_, _) => await RunMasterFlowAsync(runZalo: true, runFb: false, loopForever: false, autoBothBtn, sendZaloOnceBtn, sendFbOnceBtn, fetchPreviewBtn, stopBtn);
            sendFbOnceBtn.Click += async (_, _) => await RunMasterFlowAsync(runZalo: false, runFb: true, loopForever: false, autoBothBtn, sendZaloOnceBtn, sendFbOnceBtn, fetchPreviewBtn, stopBtn);

            fetchPreviewBtn.Click += async (_, _) =>
            {
                fetchPreviewBtn.Enabled = false;
                try
                {
                    var (ok, msg) = await FetchAndBuildSaleMessageAsync(CancellationToken.None);
                    statusLabel.Text = msg;
                    if (!ok) MessageBox.Show(this, msg, "Addlivetag API", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                finally
                {
                    if (!IsDisposed) fetchPreviewBtn.Enabled = true;
                }
            };

            stopBtn.Click += (_, _) =>
            {
                sendCts?.Cancel();
                statusLabel.Text = "Đang dừng tiến trình tự động...";
            };

            actions.Controls.AddRange(new Control[] { autoBothBtn, sendZaloOnceBtn, sendFbOnceBtn, fetchPreviewBtn, stopBtn });

            statusLabel.Dock = DockStyle.Bottom;
            statusLabel.Height = 24;
            statusLabel.Padding = new Padding(4, 4, 0, 0);
            statusLabel.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            statusLabel.ForeColor = Color.FromArgb(25, 90, 160);
            statusLabel.Text = "Đang khởi tạo trình duyệt Zalo & Facebook...";

            bottom.Controls.Add(splitBottom);
            bottom.Controls.Add(configGroup);
            bottom.Controls.Add(actions);
            bottom.Controls.Add(statusLabel);

            Controls.Add(browserTabs);
            Controls.Add(bottom);

            Shown += async (_, _) =>
            {
                BeginInvoke(new Action(() =>
                {
                    if (splitBottom.ClientSize.Width > 600)
                        splitBottom.SplitterDistance = (int)(splitBottom.ClientSize.Width * 0.50);
                }));
                await InitializeBrowsersAsync();
            };

            FormClosed += (_, _) =>
            {
                sendCts?.Cancel();
                SaveConfig();
                try { zaloBrowser.CoreWebView2?.Stop(); } catch { }
                try { fbBrowser.CoreWebView2?.Stop(); } catch { }
                zaloBrowser.Dispose();
                fbBrowser.Dispose();
            };
        }

        private async Task InitializeBrowsersAsync()
        {
            if (initialized) return;
            initialized = true;
            try
            {
                Directory.CreateDirectory(zaloProfilePath);
                var zaloEnv = await CoreWebView2Environment.CreateAsync(null, zaloProfilePath);
                await zaloBrowser.EnsureCoreWebView2Async(zaloEnv);
                zaloBrowser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                zaloBrowser.CoreWebView2.FrameCreated += (_, args) =>
                {
                    try
                    {
                        var frame = args.Frame;
                        zaloFrames.Add(frame);
                        frame.Destroyed += (_, _) => zaloFrames.Remove(frame);
                    }
                    catch { }
                };
                zaloBrowser.CoreWebView2.Navigate("https://chat.zalo.me/");

                Directory.CreateDirectory(fbProfilePath);
                var fbEnv = await CoreWebView2Environment.CreateAsync(null, fbProfilePath);
                await fbBrowser.EnsureCoreWebView2Async(fbEnv);
                fbBrowser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                fbBrowser.CoreWebView2.Navigate("https://www.facebook.com/groups/joins/");

                statusLabel.Text = "Zalo & Facebook đã sẵn sàng! Hãy đăng nhập (nếu cần), quét/tích chọn nhóm rồi bấm BẬT AUTO.";
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Lỗi khởi tạo trình duyệt: " + ex.Message;
            }
        }

        private void AddOrCheckZaloGroupItem(string groupName, bool isChecked)
        {
            var clean = groupName.Trim();
            if (string.IsNullOrWhiteSpace(clean)) return;
            for (int i = 0; i < zaloGroupsCheckList.Items.Count; i++)
            {
                if (string.Equals(zaloGroupsCheckList.Items[i]?.ToString(), clean, StringComparison.OrdinalIgnoreCase))
                {
                    if (isChecked) zaloGroupsCheckList.SetItemChecked(i, true);
                    return;
                }
            }
            zaloGroupsCheckList.Items.Add(clean, isChecked);
        }

        private void AddOrCheckFbGroupItem(string name, string url, bool isChecked)
        {
            var cleanUrl = NormalizeFbGroupUrl(url);
            if (string.IsNullOrWhiteSpace(cleanUrl)) return;
            var cleanName = string.IsNullOrWhiteSpace(name) ? cleanUrl : name.Trim();

            for (int i = 0; i < fbGroupsCheckList.Items.Count; i++)
            {
                if (fbGroupsCheckList.Items[i] is FbGroupEntry existing &&
                    string.Equals(existing.Url, cleanUrl, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(cleanName) && existing.Name == existing.Url)
                        existing.Name = cleanName;
                    if (isChecked) fbGroupsCheckList.SetItemChecked(i, true);
                    return;
                }
            }
            fbGroupsCheckList.Items.Add(new FbGroupEntry { Name = cleanName, Url = cleanUrl, Checked = isChecked }, isChecked);
        }

        private static string NormalizeFbGroupUrl(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var s = raw.Trim();
            if (s.Contains('|')) s = s.Split('|').Last().Trim();
            if (!s.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                s = "https://www.facebook.com/groups/" + s.Trim('/');
            if (Uri.TryCreate(s, UriKind.Absolute, out var uri) &&
                uri.Host.Contains("facebook.com", StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath.StartsWith("/groups/", StringComparison.OrdinalIgnoreCase))
            {
                var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 2 && !string.Equals(segments[1], "joins", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(segments[1], "feed", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(segments[1], "discover", StringComparison.OrdinalIgnoreCase))
                {
                    return $"https://www.facebook.com/groups/{segments[1]}/";
                }
            }
            return "";
        }

        private void LoadSavedConfig()
        {
            try
            {
                if (!File.Exists(configFilePath)) return;
                var cfg = JsonSerializer.Deserialize<AddlivetagConfig>(File.ReadAllText(configFilePath));
                if (cfg is null) return;
                apiKeyBox.Text = cfg.ApiKey ?? "";
                affIdBox.Text = cfg.AffiliateId ?? "";
                subIdBox.Text = string.IsNullOrWhiteSpace(cfg.SubId1) ? "zalo" : cfg.SubId1;
                keywordsBox.Text = string.IsNullOrWhiteSpace(cfg.SaleKeywords) ? "flash sale, giảm giá, gia dụng" : cfg.SaleKeywords;
                batchCountInput.Value = Math.Clamp(cfg.ProductsPerBatch, 1, 20);
                cycleMinutesInput.Value = Math.Clamp(cfg.AutoCycleMinutes, 1, 1440);
                minDelayInput.Value = Math.Clamp(cfg.MinDelaySeconds, 5, 600);
                maxDelayInput.Value = Math.Clamp(cfg.MaxDelaySeconds, 5, 600);

                var checkedSet = (cfg.CheckedGroups ?? new List<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var allGroups = cfg.SavedGroups ?? new List<string>();
                foreach (var g in allGroups.Concat(checkedSet).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    AddOrCheckZaloGroupItem(g, checkedSet.Contains(g));
                }

                if (cfg.FbGroups != null)
                {
                    foreach (var fg in cfg.FbGroups)
                    {
                        AddOrCheckFbGroupItem(fg.Name, fg.Url, fg.Checked);
                    }
                }
            }
            catch { }
        }

        private void SaveConfig()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(configFilePath)!);
                var allZalo = new List<string>();
                var checkedZalo = new List<string>();

                for (int i = 0; i < zaloGroupsCheckList.Items.Count; i++)
                {
                    var s = zaloGroupsCheckList.Items[i]?.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    allZalo.Add(s);
                    if (zaloGroupsCheckList.GetItemChecked(i)) checkedZalo.Add(s);
                }

                var fbList = new List<FbGroupEntry>();
                for (int i = 0; i < fbGroupsCheckList.Items.Count; i++)
                {
                    if (fbGroupsCheckList.Items[i] is FbGroupEntry entry)
                    {
                        entry.Checked = fbGroupsCheckList.GetItemChecked(i);
                        fbList.Add(entry);
                    }
                }

                var cfg = new AddlivetagConfig
                {
                    ApiKey = apiKeyBox.Text.Trim(),
                    AffiliateId = affIdBox.Text.Trim(),
                    SubId1 = subIdBox.Text.Trim(),
                    SaleKeywords = keywordsBox.Text.Trim(),
                    ProductsPerBatch = (int)batchCountInput.Value,
                    AutoCycleMinutes = (int)cycleMinutesInput.Value,
                    MinDelaySeconds = (int)minDelayInput.Value,
                    MaxDelaySeconds = (int)maxDelayInput.Value,
                    SavedGroups = allZalo,
                    CheckedGroups = checkedZalo,
                    FbGroups = fbList
                };
                File.WriteAllText(configFilePath, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private async Task RunMasterFlowAsync(bool runZalo, bool runFb, bool loopForever, Button autoBtn, Button zaloBtn, Button fbBtn, Button previewBtn, Button stopBtn)
        {
            if (autoPilotRunning) return;
            autoPilotRunning = true;
            autoBtn.Enabled = false;
            zaloBtn.Enabled = false;
            fbBtn.Enabled = false;
            previewBtn.Enabled = false;
            stopBtn.Enabled = true;

            sendCts = new CancellationTokenSource();
            var token = sendCts.Token;
            int batchNumber = 0;

            try
            {
                do
                {
                    batchNumber++;

                    if (!string.IsNullOrWhiteSpace(apiKeyBox.Text))
                    {
                        statusLabel.Text = $"[Đợt #{batchNumber}] Đang lấy sản phẩm sale mới từ Addlivetag API...";
                        var (_, fetchedMsg) = await FetchAndBuildSaleMessageAsync(token);
                        statusLabel.Text = $"[Đợt #{batchNumber}] {fetchedMsg}";
                    }

                    if (string.IsNullOrWhiteSpace(messageBox.Text))
                    {
                        statusLabel.Text = "Chưa có nội dung bài đăng! Hãy chọn sản phẩm, nhập nội dung, hoặc điền API Key Addlivetag.";
                        MessageBox.Show(this, statusLabel.Text, "Thiếu nội dung", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        break;
                    }

                    int minSec = (int)Math.Min(minDelayInput.Value, maxDelayInput.Value);
                    int maxSec = (int)Math.Max(minDelayInput.Value, maxDelayInput.Value);

                    if (runZalo && zaloBrowser.CoreWebView2 is not null)
                    {
                        var zaloGroups = GetCheckedZaloGroups();
                        if (zaloGroups.Count > 0)
                        {
                            browserTabs.SelectedIndex = 0;
                            int sentZalo = 0;
                            for (int i = 0; i < zaloGroups.Count; i++)
                            {
                                token.ThrowIfCancellationRequested();
                                var groupName = zaloGroups[i];
                                statusLabel.Text = $"[Zalo {i + 1}/{zaloGroups.Count}] Đang mở nhóm '{groupName}'...";

                                await SelectZaloGroupByNameAsync(groupName, token);
                                await Task.Delay(1000, token);

                                var (ok, _) = await SendMessageToCurrentOpenZaloChatAsync(token);
                                if (ok) sentZalo++;

                                if (i < zaloGroups.Count - 1 || (runFb && GetCheckedFbGroups().Count > 0))
                                {
                                    int delaySec = Random.Shared.Next(minSec, maxSec + 1);
                                    await WaitWithCountdownAsync(delaySec, $"[Zalo] Đã gửi {sentZalo}/{i + 1} nhóm. Nghỉ chống spam", token);
                                }
                            }
                        }
                        else if (!runFb)
                        {
                            browserTabs.SelectedIndex = 0;
                            statusLabel.Text = "[Zalo] Đang gửi vào cuộc trò chuyện đang mở...";
                            await SendMessageToCurrentOpenZaloChatAsync(token);
                        }
                    }

                    if (runFb && fbBrowser.CoreWebView2 is not null)
                    {
                        var fbGroups = GetCheckedFbGroups();
                        if (fbGroups.Count > 0)
                        {
                            browserTabs.SelectedIndex = 1;
                            int postedFb = 0;
                            for (int i = 0; i < fbGroups.Count; i++)
                            {
                                token.ThrowIfCancellationRequested();
                                var grp = fbGroups[i];
                                statusLabel.Text = $"[Facebook {i + 1}/{fbGroups.Count}] Đang mở nhóm '{grp.Name}'...";

                                var (ok, fbMsg) = await PostToFacebookGroupAsync(grp.Url, messageBox.Text, token);
                                if (ok)
                                {
                                    postedFb++;
                                    statusLabel.Text = $"[Facebook {i + 1}/{fbGroups.Count}] Đã đăng thành công vào '{grp.Name}'!";
                                }
                                else
                                {
                                    statusLabel.Text = $"[Facebook {i + 1}/{fbGroups.Count}] Chưa đăng được '{grp.Name}': {fbMsg}";
                                }

                                if (i < fbGroups.Count - 1)
                                {
                                    int delaySec = Random.Shared.Next(minSec, maxSec + 1);
                                    await WaitWithCountdownAsync(delaySec, $"[Facebook] Đã đăng {postedFb}/{i + 1} nhóm. Nghỉ chống checkpoint", token);
                                }
                            }
                        }
                        else if (!runZalo)
                        {
                            browserTabs.SelectedIndex = 1;
                            var currentUrl = fbBrowser.Source?.ToString() ?? "";
                            if (currentUrl.Contains("/groups/", StringComparison.OrdinalIgnoreCase))
                            {
                                statusLabel.Text = "[Facebook] Đang đăng vào nhóm đang mở trên màn hình...";
                                var (_, fbMsg) = await PostToFacebookGroupAsync(currentUrl, messageBox.Text, token);
                                statusLabel.Text = fbMsg;
                            }
                            else
                            {
                                MessageBox.Show(this, "Bạn chưa tích chọn nhóm Facebook nào! Hãy qua tab '📘 Tích chọn Nhóm Facebook' bấm '🔄 Quét Nhóm FB đã tham gia' rồi tích chọn.", "Facebook Auto", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                        }
                    }

                    if (!loopForever) break;

                    int cycleSeconds = (int)cycleMinutesInput.Value * 60;
                    await WaitWithCountdownAsync(cycleSeconds, $"[Xong đợt #{batchNumber}] Tự động chạy đợt tiếp theo sau", token);

                } while (!token.IsCancellationRequested);
            }
            catch (OperationCanceledException)
            {
                statusLabel.Text = "Đã dừng tiến trình tự động.";
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Lỗi: " + ex.Message;
            }
            finally
            {
                autoPilotRunning = false;
                if (!IsDisposed)
                {
                    autoBtn.Enabled = true;
                    zaloBtn.Enabled = true;
                    fbBtn.Enabled = true;
                    previewBtn.Enabled = true;
                    stopBtn.Enabled = false;
                }
            }
        }

        private List<string> GetCheckedZaloGroups()
        {
            var list = new List<string>();
            foreach (var item in zaloGroupsCheckList.CheckedItems)
                if (item is string s && !string.IsNullOrWhiteSpace(s)) list.Add(s);
            return list;
        }

        private List<FbGroupEntry> GetCheckedFbGroups()
        {
            var list = new List<FbGroupEntry>();
            foreach (var item in fbGroupsCheckList.CheckedItems)
                if (item is FbGroupEntry g && !string.IsNullOrWhiteSpace(g.Url)) list.Add(g);
            return list;
        }

        private async Task WaitWithCountdownAsync(int seconds, string prefix, CancellationToken token)
        {
            for (int remain = seconds; remain > 0; remain--)
            {
                token.ThrowIfCancellationRequested();
                if (remain >= 60)
                    statusLabel.Text = $"{prefix}: {remain / 60} phút {remain % 60} giây...";
                else
                    statusLabel.Text = $"{prefix}: {remain} giây...";
                await Task.Delay(1000, token);
            }
        }

        private void AddCurrentOpenFacebookGroup()
        {
            var url = fbBrowser.Source?.ToString() ?? "";
            var cleanUrl = NormalizeFbGroupUrl(url);
            if (string.IsNullOrWhiteSpace(cleanUrl))
            {
                MessageBox.Show(this, "Trang Facebook hiện tại chưa phải là 1 nhóm cụ thể. Hãy bấm vào 1 nhóm trên Facebook trước.", "Facebook", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var title = fbBrowser.CoreWebView2?.DocumentTitle?.Replace("| Facebook", "").Trim() ?? cleanUrl;
            AddOrCheckFbGroupItem(title, cleanUrl, true);
            SaveConfig();
            statusLabel.Text = $"Đã thêm và tích chọn nhóm FB: {title}";
        }

        private async Task LoadGroupsFromFacebookAsync()
        {
            if (fbBrowser.CoreWebView2 is null) return;
            statusLabel.Text = "Đang mở trang Danh sách nhóm Facebook của bạn và quét...";

            try
            {
                if (fbBrowser.Source?.AbsolutePath.Contains("/groups/joins", StringComparison.OrdinalIgnoreCase) != true)
                {
                    fbBrowser.CoreWebView2.Navigate("https://www.facebook.com/groups/joins/");
                    await Task.Delay(3200);
                }

                const string scrollScript = "(() => { window.scrollBy(0, 900); return true; })();";
                await fbBrowser.CoreWebView2.ExecuteScriptAsync(scrollScript);
                await Task.Delay(1200);
                await fbBrowser.CoreWebView2.ExecuteScriptAsync("(() => { window.scrollTo(0, 0); return true; })();");
                await Task.Delay(500);

                const string scanFbGroupsScript = "(() => { try { const anchors = [...document.querySelectorAll('a[href*="/groups/"]')]; const found = []; const seen = new Set(); const ignoreNames = /^(nhóm|groups|khám phá|discover|bảng feed của bạn|your feed|tạo nhóm mới|create new group|xem nhóm|view group|tham gia|join)$/i; for (const a of anchors) { let u; try { u = new URL(a.href, location.origin); } catch { continue; } const m = u.pathname.match(/^\/groups\/([^\/?#]+)\/?$/i); if (!m) continue; const slug = m[1]; if (/^(feed|discover|joins|create|notifications|search|categories)$/i.test(slug)) continue; const cleanUrl = 'https://www.facebook.com/groups/' + slug + '/'; if (seen.has(cleanUrl)) continue; const rawText = (a.innerText || a.getAttribute('aria-label') || '').split('\n').map(x => x.trim()).filter(Boolean)[0] || ''; if (!rawText || rawText.length < 2 || rawText.length > 100 || ignoreNames.test(rawText)) continue; seen.add(cleanUrl); found.push({ name: rawText, url: cleanUrl }); } return { ok: true, groups: found }; } catch (e) { return { ok: false, groups: [] }; } })();";

                var raw = await fbBrowser.CoreWebView2.ExecuteScriptAsync(scanFbGroupsScript);
                if (!string.IsNullOrWhiteSpace(raw) && raw != "null")
                {
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.TryGetProperty("groups", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        int added = 0;
                        foreach (var item in arr.EnumerateArray())
                        {
                            var name = GetFirstString(item, "name");
                            var url = GetFirstString(item, "url");
                            if (!string.IsNullOrWhiteSpace(url))
                            {
                                AddOrCheckFbGroupItem(name, url, false);
                                added++;
                            }
                        }
                        SaveConfig();
                        if (added > 0)
                        {
                            statusLabel.Text = $"Đã quét được {added} nhóm Facebook! Hãy tích chọn các nhóm bạn muốn đăng bài.";
                            return;
                        }
                    }
                }
                statusLabel.Text = "Chưa quét được nhóm. Hãy chắc chắn bạn đã đăng nhập Facebook trên tab Trình duyệt Facebook.";
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Lỗi quét nhóm Facebook: " + ex.Message;
            }
        }

        private async Task<(bool Ok, string Message)> PostToFacebookGroupAsync(string groupUrl, string content, CancellationToken token)
        {
            if (fbBrowser.CoreWebView2 is null) return (false, "Facebook chưa sẵn sàng.");

            var normalizedText = content.Replace("
", "
").Replace("", "
").Trim();
            try { Clipboard.SetText(normalizedText); } catch { }

            fbBrowser.CoreWebView2.Navigate(groupUrl);
            await Task.Delay(4200, token);

            const string openComposerScript = "(() => { try { const visible = e => { if (!e) return false; const r = e.getBoundingClientRect(); return r.width > 60 && r.height > 20 && r.top > 50 && r.top < 750; }; const candidates = [...document.querySelectorAll('[role="button"], span, div')].filter(visible); const trigger = candidates.find(e => { const t = (e.innerText || e.getAttribute('aria-label') || '').trim().toLowerCase(); return /^(bạn viết gì đi|viết gì đó|bạn đang nghĩ gì|write something|create a public post|tạo bài viết)/i.test(t); }); if (!trigger) return { ok: false, message: 'Không tìm thấy ô Tạo bài viết trong nhóm này.' }; const btn = trigger.closest('[role="button"]') || trigger; btn.scrollIntoView({ block: 'center' }); btn.click(); return { ok: true }; } catch (e) { return { ok: false, message: String(e) }; } })();";

            var openRaw = await fbBrowser.CoreWebView2.ExecuteScriptAsync(openComposerScript);
            if (string.IsNullOrWhiteSpace(openRaw) || !IsSuccessResult(openRaw))
            {
                return (false, "Không mở được khung viết bài (nhóm có thể đã tắt quyền đăng hoặc chưa tham gia).");
            }

            await Task.Delay(2200, token);

            var payloadJson = JsonSerializer.Serialize(normalizedText);
            string fillFbScript = "(() => { try { const msg = " + payloadJson + "; const visible = e => { if (!e) return false; const r = e.getBoundingClientRect(); return r.width > 120 && r.height > 30; }; const dialog = document.querySelector('[role="dialog"]') || document.body; const editors = [...dialog.querySelectorAll('[contenteditable="true"][role="textbox"], [contenteditable="true"], textarea')].filter(visible); const editor = editors[0]; if (!editor) return { ok: false, message: 'Chưa hiện ô nhập nội dung bài viết Facebook.' }; editor.focus(); editor.click(); let inserted = false; try { const dt = new DataTransfer(); dt.setData('text/plain', msg); const pasteEvt = new ClipboardEvent('paste', { bubbles: true, cancelable: true, clipboardData: dt }); editor.dispatchEvent(pasteEvt); inserted = (editor.innerText || '').trim().length > 10; } catch {} if (!inserted) { try { inserted = document.execCommand('insertText', false, msg); } catch {} } if (!(editor.innerText || '').trim()) { editor.dispatchEvent(new InputEvent('beforeinput', { bubbles: true, cancelable: true, inputType: 'insertText', data: msg })); editor.textContent = msg; editor.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: msg })); } return { ok: true }; } catch (e) { return { ok: false, message: String(e) }; } })();";

            var fillRaw = await fbBrowser.CoreWebView2.ExecuteScriptAsync(fillFbScript);
            if (string.IsNullOrWhiteSpace(fillRaw) || !IsSuccessResult(fillRaw))
            {
                return (false, "Không điền được chữ vào khung đăng bài Facebook.");
            }

            await Task.Delay(2000, token);

            const string clickPostBtnScript = "(() => { try { const dialog = document.querySelector('[role="dialog"]') || document.body; const buttons = [...dialog.querySelectorAll('[aria-label="Đăng"], [aria-label="Post"], [role="button"]')].filter(e => { const r = e.getBoundingClientRect(); return r.width > 40 && r.height > 20 && e.getAttribute('aria-disabled') !== 'true' && !e.disabled; }); const postBtn = buttons.find(b => { const label = (b.getAttribute('aria-label') || b.innerText || '').trim().toLowerCase(); return label === 'đăng' || label === 'post'; }); if (!postBtn) return { ok: false, message: 'Không tìm thấy nút Đăng (hoặc nút Đăng đang bị khóa).' }; postBtn.click(); return { ok: true, message: 'Đã tự động điền và bấm nút Đăng lên nhóm Facebook!' }; } catch (e) { return { ok: false, message: String(e) }; } })();";

            var postRaw = await fbBrowser.CoreWebView2.ExecuteScriptAsync(clickPostBtnScript);
            if (!string.IsNullOrWhiteSpace(postRaw) && IsSuccessResult(postRaw))
            {
                await Task.Delay(3500, token);
                return (true, "Đã đăng bài thành công!");
            }

            return (false, "Đã điền nội dung nhưng chưa bấm được nút Đăng.");
        }

        private async Task AddCurrentOpenZaloGroupAsync()
        {
            if (zaloBrowser.CoreWebView2 is null) return;
            const string script = "(() => { try { const titleNodes = [...document.querySelectorAll('header .header-title, .thread-chat-title, [data-id*="div_Main_TabMsg_Header"] span, .title-header, h1, h2, h3, span, div')].filter(e => { const r = e.getBoundingClientRect(); return r.left > 320 && r.top >= 0 && r.top < 95 && r.width > 30 && r.height > 14 && r.height < 45; }); for (const el of titleNodes) { const t = (el.innerText || '').split('\n')[0].trim(); if (t && t.length > 1 && t.length < 90 && !/thành viên|members|tìm kiếm|search|vừa truy cập|online/i.test(t)) { return { ok: true, name: t }; } } return { ok: false, name: '' }; } catch (e) { return { ok: false, name: '' }; } })();";

            try
            {
                var raw = await ExecuteInZaloFramesAsync(script);
                if (!string.IsNullOrWhiteSpace(raw) && raw != "null")
                {
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean() &&
                        doc.RootElement.TryGetProperty("name", out var nameProp))
                    {
                        var groupName = nameProp.GetString()?.Trim() ?? "";
                        if (!string.IsNullOrWhiteSpace(groupName))
                        {
                            AddOrCheckZaloGroupItem(groupName, true);
                            SaveConfig();
                            statusLabel.Text = $"Đã thêm và tích chọn nhóm Zalo: '{groupName}'!";
                            return;
                        }
                    }
                }
                statusLabel.Text = "Hãy bấm mở 1 nhóm trên Zalo Web trước khi bấm nút này.";
            }
            catch { }
        }

        private async Task LoadGroupsFromZaloAsync()
        {
            if (zaloBrowser.CoreWebView2 is null) return;
            statusLabel.Text = "Đang tự động lọc danh sách NHÓM từ Zalo Web...";
            var collectedGroups = new List<string>();

            try
            {
                const string openContactsGroupsScript = "(() => { try { const contactsTab = document.querySelector('[data-id*="Main_TabCT"], [title*="Danh bạ"], [title*="Contacts"], .fa-Contact_28_Line, [data-translate-title*="STR_TAB_CONTACT"]'); if (contactsTab) { (contactsTab.closest('[role="button"], div, li') || contactsTab).click(); return { ok: true }; } return { ok: false }; } catch { return { ok: false }; } })();";
                await ExecuteInZaloFramesAsync(openContactsGroupsScript);
                await Task.Delay(700);

                const string clickGroupListMenuScript = "(() => { try { const nodes = [...document.querySelectorAll('div, span, p, li')].filter(e => { const r = e.getBoundingClientRect(); return r.left > 40 && r.left < 380 && r.top > 50 && r.top < 350 && r.width > 40 && r.height > 15; }); const grpMenu = nodes.find(e => /^(danh sách nhóm|nhóm và cộng đồng|group list|joined groups)$/i.test((e.innerText || '').trim())); if (grpMenu) { (grpMenu.closest('.conv-item, [role="listitem"], div') || grpMenu).click(); return { ok: true }; } return { ok: false }; } catch { return { ok: false }; } })();";
                var openedGroupMenu = await ExecuteInZaloFramesAsync(clickGroupListMenuScript);
                if (openedGroupMenu is not null && IsSuccessResult(openedGroupMenu))
                {
                    await Task.Delay(900);
                }

                const string extractStrictGroupsScript = "(() => { try { const visible = e => { if (!e) return false; const r = e.getBoundingClientRect(); const s = getComputedStyle(e); return r.width > 30 && r.height > 14 && s.display !== 'none' && s.visibility !== 'hidden'; }; const ignore = /^(tất cả|chưa đọc|phân loại|tìm kiếm|danh sách bạn bè|danh sách nhóm|nhóm và cộng đồng|lời mời kết bạn|lời mời vào nhóm|my documents|cloud của tôi|truyền file|đồng bộ ngay|tải ngay|tin nhắn)$/i; const names = []; const addName = raw => { const clean = (raw || '').split('\n')[0].replace(/\s+/g, ' ').trim(); if (!clean || clean.length < 2 || clean.length > 95) return; if (ignore.test(clean) || /^\d+\s*(thành viên|members|nhóm|groups)/i.test(clean)) return; if (!names.includes(clean)) names.push(clean); }; const allDivs = [...document.querySelectorAll('div, li, article')].filter(visible); for (const d of allDivs) { const r = d.getBoundingClientRect(); if (r.left > 300 && r.height >= 40 && r.height <= 130) { const text = d.innerText || ''; if (/\d+\s*(thành viên|members)/i.test(text)) { const lines = text.split('\n').map(x => x.trim()).filter(Boolean); if (lines.length >= 2) addName(lines[0]); } } } const leftItems = [...document.querySelectorAll('.conv-item, [id^="friend-item-"], .msg-item, [role="listitem"]')].filter(visible); for (const el of leftItems) { const r = el.getBoundingClientRect(); if (r.left > 450) continue; const text = el.innerText || ''; if (/gửi lời chào|kết bạn|đã là bạn bè/i.test(text)) continue; const html = el.innerHTML || ''; const lines = text.split('\n').map(x => x.trim()).filter(Boolean); if (lines.length === 0) continue; const secondLine = lines[1] || ''; const thirdLine = lines[2] || ''; const isGroupIndicator = html.includes('group') || html.includes('users') || html.includes('community') || /^[A-Za-zÀ-ỹ0-9_\s]{1,25}:\s/.test(secondLine) || /^[A-Za-zÀ-ỹ0-9_\s]{1,25}:\s/.test(thirdLine) || /thành viên|nhóm|group|đã tạo nhóm|đã tham gia/i.test(text) || el.querySelectorAll('img').length > 1; if (isGroupIndicator) { addName(lines[0]); } } return { ok: true, groups: names }; } catch (e) { return { ok: false, groups: [] }; } })();";

                var raw = await ExecuteInZaloFramesAsync(extractStrictGroupsScript);
                if (!string.IsNullOrWhiteSpace(raw) && raw != "null" && raw != "{}")
                {
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.TryGetProperty("groups", out var groupsArr))
                    {
                        var list = JsonSerializer.Deserialize<List<string>>(groupsArr.GetRawText());
                        if (list is not null) collectedGroups.AddRange(list);
                    }
                }

                const string backToMsgTabScript = "(() => { try { const msgTab = document.querySelector('[data-id*="Main_TabMsg"], [title*="Tin nhắn"], [title*="Messages"], .fa-Message_28_Line, [data-translate-title*="STR_TAB_MESSAGE"]'); if (msgTab) (msgTab.closest('[role="button"], div, li') || msgTab).click(); return { ok: true }; } catch { return { ok: false }; } })();";
                await ExecuteInZaloFramesAsync(backToMsgTabScript);
                await Task.Delay(500);

                var distinctGroups = collectedGroups.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (distinctGroups.Count > 0)
                {
                    foreach (var name in distinctGroups) AddOrCheckZaloGroupItem(name, false);
                    SaveConfig();
                    statusLabel.Text = $"Đã lọc được {distinctGroups.Count} nhóm Zalo!";
                    return;
                }
                statusLabel.Text = "Hãy mở nhóm trên Zalo rồi nhấn '➕ Thêm nhóm đang mở' để thêm nhanh!";
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Lỗi quét nhóm Zalo: " + ex.Message;
            }
        }

        private async Task<bool> SelectZaloGroupByNameAsync(string groupName, CancellationToken token)
        {
            var nameJson = JsonSerializer.Serialize(groupName.Trim());

            var clickDirectResult = await ExecuteInZaloFramesAsync(BuildClickConversationScript(nameJson));
            if (clickDirectResult is not null && IsSuccessResult(clickDirectResult))
            {
                await Task.Delay(600, token);
                return true;
            }

            var searchResult = await ExecuteInZaloFramesAsync(BuildTypeSearchBoxScript(nameJson));
            if (searchResult is not null && IsSuccessResult(searchResult))
            {
                await Task.Delay(1200, token);
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    var clickAfterSearch = await ExecuteInZaloFramesAsync(BuildClickConversationScript(nameJson));
                    if (clickAfterSearch is not null && IsSuccessResult(clickAfterSearch))
                    {
                        await Task.Delay(600, token);
                        return true;
                    }
                    await Task.Delay(500, token);
                }
                await ExecuteInZaloFramesAsync(BuildSelectFirstSearchResultScript());
                await Task.Delay(700, token);
                return true;
            }
            return false;
        }

        private async Task<(bool Ok, string Message)> SendMessageToCurrentOpenZaloChatAsync(CancellationToken token)
        {
            var normalizedText = messageBox.Text.Replace("
", "
").Replace("", "
").Trim();
            try { Clipboard.SetText(normalizedText); } catch { }

            var payload = JsonSerializer.Serialize(normalizedText);

            zaloBrowser.Focus();
            await Task.Delay(250, token);

            var fillScript = BuildFillScript(payload);
            var result = await ExecuteInZaloFramesAsync(fillScript);
            if (string.IsNullOrWhiteSpace(result) || result == "null")
            {
                return (false, "Không tìm thấy ô nhập tin nhắn Zalo.");
            }

            using var document = JsonDocument.Parse(result);
            var root = document.RootElement;
            var ok = root.TryGetProperty("ok", out var okValue) && okValue.GetBoolean();
            var message = root.TryGetProperty("message", out var messageValue)
                ? messageValue.GetString() ?? "" : "Không xác định được kết quả.";

            if (!ok) return (false, message);

            await Task.Delay(750, token);

            var triggerResult = await ExecuteInZaloFramesAsync(BuildTriggerSendScript());
            if (!string.IsNullOrWhiteSpace(triggerResult) && triggerResult != "null")
            {
                try
                {
                    using var trigDoc = JsonDocument.Parse(triggerResult);
                    var trigOk = trigDoc.RootElement.TryGetProperty("ok", out var tOk) && tOk.GetBoolean();
                    if (trigDoc.RootElement.TryGetProperty("message", out var trigMsg) &&
                        !string.IsNullOrWhiteSpace(trigMsg.GetString()))
                    {
                        message = trigMsg.GetString()!;
                    }
                    return (trigOk, message);
                }
                catch { }
            }

            return (true, message);
        }

        private async Task<string?> ExecuteInZaloFramesAsync(string script)
        {
            var core = zaloBrowser.CoreWebView2;
            if (core is null) return null;

            string? lastErrorResult = null;
            try
            {
                var result = await core.ExecuteScriptAsync(script);
                if (IsSuccessResult(result)) return result;
                if (!string.IsNullOrWhiteSpace(result) && result != "null" && result != "{}") lastErrorResult = result;
            }
            catch { }

            foreach (var frame in zaloFrames.ToArray())
            {
                try
                {
                    var result = await frame.ExecuteScriptAsync(script);
                    if (IsSuccessResult(result)) return result;
                    if (!string.IsNullOrWhiteSpace(result) && result != "null" && result != "{}") lastErrorResult = result;
                }
                catch { }
            }
            return lastErrorResult;
        }

        private static bool IsSuccessResult(string raw)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(raw) || raw == "null" || raw == "{}") return false;
                using var doc = JsonDocument.Parse(raw);
                return doc.RootElement.TryGetProperty("ok", out var value) && value.GetBoolean();
            }
            catch { return false; }
        }

        private static string BuildClickConversationScript(string groupNameJson) =>
            "(() => { try { const target = (" + groupNameJson + " || '').toLowerCase().replace(/\s+/g, ' ').trim(); if (!target) return { ok: false, message: 'Tên nhóm trống.' }; const headerTitle = document.querySelector('header .header-title, .thread-chat-title, [data-id*="div_Main_TabMsg_Header"] span, .title-header'); if (headerTitle && (headerTitle.innerText || '').toLowerCase().replace(/\s+/g, ' ').trim() === target) { return { ok: true, message: 'Nhóm đã được mở sẵn: ' + target }; } const visible = e => { if (!e) return false; const r = e.getBoundingClientRect(); const s = getComputedStyle(e); return r.width > 15 && r.height > 14 && r.left < 500 && r.top > 45 && s.display !== 'none' && s.visibility !== 'hidden' && Number(s.opacity || 1) > 0; }; const items = [...document.querySelectorAll('.conv-item, [id^="friend-item-"], .msg-item, [data-id*="Conv"], [role="listitem"], .virtualized-scroll div')].filter(visible); let best = null; let bestScore = 0; for (const el of items) { if (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.isContentEditable) continue; const lines = (el.innerText || '').split('\n').map(x => x.replace(/\s+/g, ' ').trim()).filter(Boolean); if (lines.length === 0) continue; const firstLine = lines[0].toLowerCase(); const fullText = lines.join(' ').toLowerCase(); if (fullText.length > 220) continue; let score = 0; if (firstLine === target) score = 500 - fullText.length; else if (firstLine.includes(target)) score = 350 - firstLine.length; else if (fullText.includes(target)) score = 200 - Math.min(150, fullText.length); if (score > bestScore) { bestScore = score; best = el; } } if (!best) return { ok: false, message: 'Chưa thấy nhóm trong danh sách.' }; const row = best.closest('.conv-item, [id^="friend-item-"], .msg-item, [role="listitem"]') || best; row.scrollIntoView({ block: 'center' }); const rect = row.getBoundingClientRect(); const cx = rect.left + Math.min(80, rect.width / 2); const cy = rect.top + Math.min(25, rect.height / 2); const targetsToClick = [row, best]; for (const t of targetsToClick) { t.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, cancelable: true, clientX: cx, clientY: cy, button: 0 })); t.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true, clientX: cx, clientY: cy, button: 0 })); t.dispatchEvent(new PointerEvent('pointerup', { bubbles: true, cancelable: true, clientX: cx, clientY: cy, button: 0 })); t.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true, clientX: cx, clientY: cy, button: 0 })); t.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, clientX: cx, clientY: cy, button: 0 })); } return { ok: true, message: 'Đã mở nhóm: ' + target }; } catch (e) { return { ok: false, message: String(e) }; } })();";

        private static string BuildTypeSearchBoxScript(string groupNameJson) =>
            "(() => { try { const target = " + groupNameJson + "; const searchInput = document.querySelector('#contact-search-input, input[placeholder*="Tìm kiếm"], input[placeholder*="Search"], input[data-id*="txt_Main_Search"]'); if (!searchInput) return { ok: false, message: 'Không tìm thấy ô Tìm kiếm trên Zalo Web.' }; searchInput.focus(); searchInput.click(); const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set; if (setter) setter.call(searchInput, ''); else searchInput.value = ''; searchInput.dispatchEvent(new Event('input', { bubbles: true })); if (setter) setter.call(searchInput, target); else searchInput.value = target; searchInput.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: target })); searchInput.dispatchEvent(new Event('change', { bubbles: true })); searchInput.dispatchEvent(new KeyboardEvent('keyup', { key: 'a', bubbles: true })); return { ok: true, message: 'Đã nhập tên nhóm vào ô tìm kiếm.' }; } catch (e) { return { ok: false, message: String(e) }; } })();";

        private static string BuildSelectFirstSearchResultScript() =>
            "(() => { try { const searchInput = document.querySelector('#contact-search-input, input[placeholder*="Tìm kiếm"], input[placeholder*="Search"]'); if (searchInput) { searchInput.focus(); searchInput.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', code: 'ArrowDown', keyCode: 40, which: 40, bubbles: true })); searchInput.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true })); return { ok: true, message: 'Đã chọn kết quả đầu tiên.' }; } return { ok: false, message: 'Không có ô tìm kiếm.' }; } catch (e) { return { ok: false, message: String(e) }; } })();";

        private static string BuildFillScript(string messageJson) =>
            "(() => { try { const msg = " + messageJson + "; const visible = e => { if (!e) return false; const r = e.getBoundingClientRect(); const s = getComputedStyle(e); return r.width > 20 && r.height > 10 && s.display !== 'none' && s.visibility !== 'hidden' && Number(s.opacity || 1) > 0 && !e.disabled; }; const zaloSelectors = ['#richInput', '[id^="input_line_"]', '.rich-input', '[data-translate-placeholder*="Nhập @"]', '[data-translate-placeholder*="tin nhắn"]', '[contenteditable="true"]', '[role="textbox"]', 'textarea']; let candidates = []; for (const sel of zaloSelectors) { for (const el of document.querySelectorAll(sel)) { if (visible(el) && !candidates.includes(el)) { const idAndClass = ((el.id || '') + ' ' + (el.className || '') + ' ' + (el.getAttribute('placeholder') || '')).toLowerCase(); if (/contact-search|search-input|tìm kiếm/.test(idAndClass)) continue; candidates.push(el); } } } candidates.sort((a, b) => { const ar = a.getBoundingClientRect(), br = b.getBoundingClientRect(); const aId = (a.id || '').includes('richInput') || (a.id || '').includes('input_line') ? 1000 : 0; const bId = (b.id || '').includes('richInput') || (b.id || '').includes('input_line') ? 1000 : 0; return (bId + br.bottom * 2 + br.width) - (aId + ar.bottom * 2 + ar.width); }); let editor = candidates[0]; if (!editor) { return { ok: false, message: 'Chưa nhận diện được ô nhập tin nhắn. Hãy mở một nhóm trên Zalo Web.' }; } if (editor.closest('[contenteditable="true"]')) { editor = editor.closest('[contenteditable="true"]'); } window.__zaloLastEditor = editor; editor.click(); editor.focus(); if (editor.isContentEditable || editor.getAttribute('contenteditable') === 'true') { const selection = window.getSelection(); const range = document.createRange(); range.selectNodeContents(editor); selection.removeAllRanges(); selection.addRange(range); document.execCommand('delete', false, null); let pastedOk = false; try { const dt = new DataTransfer(); dt.setData('text/plain', msg); const pasteEvent = new ClipboardEvent('paste', { bubbles: true, cancelable: true, clipboardData: dt }); editor.dispatchEvent(pasteEvent); const currentText = (editor.innerText || '').trim(); pastedOk = currentText.length >= Math.min(25, msg.length) && currentText.includes('http'); } catch {} if (!pastedOk) { editor.innerHTML = ''; const lines = msg.split('\n'); lines.forEach((line, idx) => { const div = document.createElement('div'); div.id = 'input_line_' + idx; if (line.trim().length > 0) { div.textContent = line; } else { div.appendChild(document.createElement('br')); } editor.appendChild(div); }); const rEnd = document.createRange(); rEnd.selectNodeContents(editor); rEnd.collapse(false); selection.removeAllRanges(); selection.addRange(rEnd); editor.dispatchEvent(new InputEvent('input', { bubbles: true, cancelable: true, inputType: 'insertFromPaste', data: msg })); editor.dispatchEvent(new Event('change', { bubbles: true })); } } else { const proto = editor.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype; const setter = Object.getOwnPropertyDescriptor(proto, 'value')?.set; if (setter) setter.call(editor, msg); else editor.value = msg; editor.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: msg })); editor.dispatchEvent(new Event('change', { bubbles: true })); } return { ok: true, message: 'Đã điền đầy đủ toàn bộ các dòng vào ô chat Zalo.' }; } catch (err) { return { ok: false, message: 'Lỗi điền nội dung trên Zalo Web: ' + (err && err.message ? err.message : String(err)) }; } })();";

        private static string BuildTriggerSendScript() =>
            "(() => { try { const visible = e => { if (!e) return false; const r = e.getBoundingClientRect(); const s = getComputedStyle(e); return r.width > 10 && r.height > 10 && s.display !== 'none' && s.visibility !== 'hidden' && Number(s.opacity || 1) > 0 && !e.disabled; }; const editor = window.__zaloLastEditor || document.querySelector('#richInput, [contenteditable="true"]'); const sendSelectors = ['[data-translate-title="STR_SEND"]', '[title="Gửi tin nhắn"]', '[title="Send message"]', '.send-msg-btn', '.fa-Sent-msg_24_Line', '[data-id*="btn_Main_Send"]']; let sendBtn = null; for (const sel of sendSelectors) { const found = document.querySelector(sel); if (found && visible(found)) { sendBtn = found.closest('div[role="button"], button, .z--btn--v2, .send-msg-btn') || found; break; } } if (!sendBtn) { const allButtons = [...document.querySelectorAll('button, [role="button"], .z--btn--v2, i.fa-Sent-msg_24_Line')].filter(visible); const matched = allButtons.find(b => { const meta = ((b.innerText || '') + ' ' + (b.title || '') + ' ' + (b.className || '') + ' ' + (b.getAttribute('aria-label') || '') + ' ' + (b.getAttribute('data-translate-title') || '')).toLowerCase(); return /str_send|gửi tin nhắn|send message|sent-msg|send-msg|^gửi$|^send$/i.test(meta.trim()); }); if (matched) sendBtn = matched.closest('div[role="button"], button, .z--btn--v2') || matched; } if (sendBtn) { const rect = sendBtn.getBoundingClientRect(); const cx = rect.left + rect.width / 2; const cy = rect.top + rect.height / 2; sendBtn.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, cancelable: true, clientX: cx, clientY: cy })); sendBtn.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true, clientX: cx, clientY: cy })); sendBtn.dispatchEvent(new PointerEvent('pointerup', { bubbles: true, cancelable: true, clientX: cx, clientY: cy })); sendBtn.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true, clientX: cx, clientY: cy })); sendBtn.click(); return { ok: true, message: 'Đã tự động điền đầy đủ link và bấm nút Gửi thành công!' }; } if (editor) { editor.focus(); const enterOpts = { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, charCode: 13, bubbles: true, cancelable: true }; editor.dispatchEvent(new KeyboardEvent('keydown', enterOpts)); editor.dispatchEvent(new KeyboardEvent('keypress', enterOpts)); editor.dispatchEvent(new KeyboardEvent('keyup', enterOpts)); return { ok: true, message: 'Đã tự động điền đầy đủ link và nhấn Enter gửi thành công!' }; } return { ok: false, message: 'Không tìm thấy nút Gửi trên Zalo Web.' }; } catch (err) { return { ok: false, message: 'Lỗi kích hoạt nút Gửi: ' + (err && err.message ? err.message : String(err)) }; } })();";

        private async Task<(bool Ok, string Message)> FetchAndBuildSaleMessageAsync(CancellationToken token)
        {
            var apiKey = apiKeyBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return (false, "Hãy nhập API Key Addlivetag ở dòng cấu hình.");
            }

            var keywords = keywordsBox.Text
                .Split(new[] { ',', ';', '
' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToList();
            if (keywords.Count == 0) keywords.Add("");

            var currentKeyword = keywords[keywordRotateIndex % keywords.Count];
            keywordRotateIndex++;

            int needed = (int)batchCountInput.Value;
            var deals = await QueryAddlivetagDealsAsync(apiKey, currentKeyword, needed * 3, token);
            if (deals.Count == 0 && !string.IsNullOrEmpty(currentKeyword))
            {
                deals = await QueryAddlivetagDealsAsync(apiKey, "", needed * 3, token);
            }

            if (deals.Count == 0)
            {
                return (false, "Không lấy được sản phẩm từ Addlivetag API. Sẽ dùng nội dung tin nhắn hiện tại.");
            }

            var selectedDeals = deals
                .Where(d => !string.IsNullOrWhiteSpace(d.ProductUrl) && (string.IsNullOrEmpty(d.ItemId) || !sentItemIds.Contains(d.ItemId)))
                .Take(needed)
                .ToList();

            if (selectedDeals.Count == 0)
            {
                sentItemIds.Clear();
                selectedDeals = deals.Where(d => !string.IsNullOrWhiteSpace(d.ProductUrl)).Take(needed).ToList();
            }

            foreach (var d in selectedDeals)
            {
                if (!string.IsNullOrEmpty(d.ItemId)) sentItemIds.Add(d.ItemId);
            }

            await PopulateAffiliateLinksAsync(apiKey, selectedDeals, token);

            var lines = new List<string>
            {
                string.IsNullOrWhiteSpace(currentKeyword)
                    ? "🔥 TỔNG HỢP DEAL SALE SHOPEE ĐANG GIẢM SÂU:"
                    : $"🔥 SĂN DEAL SALE SHOPEE ({currentKeyword.ToUpperInvariant()}):"
            };

            foreach (var deal in selectedDeals)
            {
                lines.Add("");
                var pricePart = !string.IsNullOrWhiteSpace(deal.PriceText) ? $" — 💰 Giá chỉ: {deal.PriceText}" : "";
                lines.Add($"• {deal.Name}{pricePart}");
                lines.Add($"👉 Mua ngay: {(string.IsNullOrWhiteSpace(deal.AffiliateUrl) ? deal.ProductUrl : deal.AffiliateUrl)}");
            }

            lines.Add("");
            lines.Add("⚡ Vào link áp thêm mã giảm giá trên Shopee trước khi hết lượt nhé!");

            messageBox.Text = string.Join(Environment.NewLine, lines);
            return (true, $"Đã tự động lấy {selectedDeals.Count} sản phẩm sale & tạo link Affiliate!");
        }

        private async Task<List<SaleDealItem>> QueryAddlivetagDealsAsync(string apiKey, string keyword, int limit, CancellationToken token)
        {
            var results = new List<SaleDealItem>();
            var encodedKw = Uri.EscapeDataString(keyword ?? "");
            var encodedKey = Uri.EscapeDataString(apiKey);

            var candidateUrls = new[]
            {
                $"https://data.addlivetag.com/offers/product-offer.php?keyword={encodedKw}&page=1&limit={limit}&key={encodedKey}",
                $"https://data.addlivetag.com/search/market.php?q={encodedKw}&page=1&limit={limit}&key={encodedKey}",
                $"https://data.addlivetag.com/offers/shopee-offer.php?page=1&limit={limit}&key={encodedKey}"
            };

            foreach (var endpoint in candidateUrls)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
                    req.Headers.Add("X-API-Key", apiKey);
                    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    using var resp = await http.SendAsync(req, token);
                    if (!resp.IsSuccessStatusCode) continue;

                    var json = await resp.Content.ReadAsStringAsync(token);
                    using var doc = JsonDocument.Parse(json);
                    ExtractDealsFromJson(doc.RootElement, results);

                    if (results.Count > 0) break;
                }
                catch { }
            }

            return results;
        }

        private static void ExtractDealsFromJson(JsonElement root, List<SaleDealItem> collector)
        {
            if (root.ValueKind == JsonValueKind.Object)
            {
                string name = GetFirstString(root, "productName", "item_name", "name", "title", "offerName");
                string link = GetFirstString(root, "productLink", "originalLink", "item_url", "url", "offerLink", "link");
                string itemId = GetFirstString(root, "itemId", "item_id", "id");
                string shopId = GetFirstString(root, "shopId", "shop_id");

                if (string.IsNullOrWhiteSpace(link) && !string.IsNullOrWhiteSpace(shopId) && !string.IsNullOrWhiteSpace(itemId))
                {
                    link = $"https://shopee.vn/product/{shopId}/{itemId}";
                }

                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(link) && link.Contains("shopee", StringComparison.OrdinalIgnoreCase))
                {
                    string price = FormatVndPrice(GetFirstString(root, "price", "salePrice", "priceMin", "discountPrice"));
                    string comm = FormatVndPrice(GetFirstString(root, "commission", "sellerComFinal", "shopeeComFinal"));
                    string existingAff = GetFirstString(root, "affLink", "short_link", "shortLink", "offerLink");

                    collector.Add(new SaleDealItem
                    {
                        ItemId = itemId,
                        Name = name,
                        PriceText = price,
                        CommissionText = comm,
                        ProductUrl = CleanShopeeProductUrl(link),
                        AffiliateUrl = existingAff.StartsWith("https://s.shopee.vn", StringComparison.OrdinalIgnoreCase) ? existingAff : ""
                    });
                    return;
                }

                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                    {
                        ExtractDealsFromJson(prop.Value, collector);
                    }
                }
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    ExtractDealsFromJson(item, collector);
                }
            }
        }

        private async Task PopulateAffiliateLinksAsync(string apiKey, List<SaleDealItem> deals, CancellationToken token)
        {
            var affId = affIdBox.Text.Trim();
            var sub1 = CleanSubId(subIdBox.Text.Trim());
            if (string.IsNullOrEmpty(sub1)) sub1 = "zalo";

            for (int i = 0; i < deals.Count; i += 5)
            {
                var chunk = deals.Skip(i).Take(5).ToList();
                try
                {
                    var payloadObj = new Dictionary<string, object>
                    {
                        ["urls"] = chunk.Select(x => x.ProductUrl).ToArray(),
                        ["sub1"] = sub1
                    };
                    if (!string.IsNullOrWhiteSpace(affId)) payloadObj["aff_id"] = affId;

                    using var req = new HttpRequestMessage(HttpMethod.Post, "https://addlivetag.com/api/v1/shopee-link.php");
                    req.Headers.Add("X-API-Key", apiKey);
                    req.Content = new StringContent(JsonSerializer.Serialize(payloadObj), Encoding.UTF8, "application/json");

                    using var resp = await http.SendAsync(req, token);
                    if (resp.IsSuccessStatusCode)
                    {
                        var raw = await resp.Content.ReadAsStringAsync(token);
                        using var doc = JsonDocument.Parse(raw);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("ok", out var okProp) && okProp.GetBoolean() &&
                            root.TryGetProperty("data", out var dataArr) && dataArr.ValueKind == JsonValueKind.Array)
                        {
                            int idx = 0;
                            foreach (var el in dataArr.EnumerateArray())
                            {
                                if (idx < chunk.Count && el.TryGetProperty("ok", out var itemOk) && itemOk.GetBoolean())
                                {
                                    var shortLink = GetFirstString(el, "short_link", "shortLink");
                                    if (!string.IsNullOrWhiteSpace(shortLink))
                                        chunk[idx].AffiliateUrl = shortLink;
                                }
                                idx++;
                            }
                        }
                    }
                }
                catch { }
            }

            foreach (var deal in deals)
            {
                if (string.IsNullOrWhiteSpace(deal.AffiliateUrl))
                {
                    if (!string.IsNullOrWhiteSpace(affId))
                    {
                        var cleanOrigin = CleanShopeeProductUrl(deal.ProductUrl);
                        deal.AffiliateUrl = $"https://s.shopee.vn/an_redir?origin_link={Uri.EscapeDataString(cleanOrigin)}&affiliate_id={Uri.EscapeDataString(affId)}&sub_id={sub1}-autodeal";
                    }
                    else
                    {
                        deal.AffiliateUrl = deal.ProductUrl;
                    }
                }
            }
        }

        private static string CleanShopeeProductUrl(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
            }
            return url;
        }

        private static string CleanSubId(string raw)
        {
            var sb = new StringBuilder();
            foreach (char c in raw)
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            }
            return sb.ToString();
        }

        private static string GetFirstString(JsonElement obj, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (obj.TryGetProperty(k, out var val))
                {
                    if (val.ValueKind == JsonValueKind.String) return val.GetString() ?? "";
                    if (val.ValueKind == JsonValueKind.Number) return val.GetRawText();
                }
            }
            return "";
        }

        private static string FormatVndPrice(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            if (decimal.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var num))
            {
                if (num > 100000000) num /= 100000;
                return string.Format(System.Globalization.CultureInfo.GetCultureInfo("vi-VN"), "{0:N0}đ", num);
            }
            return raw;
        }
    }
}
