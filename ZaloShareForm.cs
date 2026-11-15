using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ToolAffiliate
{
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
        private readonly WebView2 zaloBrowser = new();
        private readonly TextBox apiKeyBox = new();
        private readonly TextBox affIdBox = new();
        private readonly TextBox subIdBox = new();
        private readonly TextBox keywordsBox = new();
        private readonly NumericUpDown batchCountInput = new();
        private readonly NumericUpDown cycleMinutesInput = new();
        private readonly TextBox messageBox = new();
        private readonly CheckedListBox groupsCheckList = new();
        private readonly TextBox manualGroupInput = new();
        private readonly NumericUpDown minDelayInput = new();
        private readonly NumericUpDown maxDelayInput = new();
        private readonly Label statusLabel = new();
        private readonly List<CoreWebView2Frame> frames = new();
        private readonly HashSet<string> sentItemIds = new(StringComparer.OrdinalIgnoreCase);

        private readonly string profilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolAffiliate", "ZaloProfile");
        private readonly string configFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolAffiliate", "addlivetag-config-v3.json");

        private bool initialized;
        private bool autoPilotRunning;
        private CancellationTokenSource? sendCts;
        private int keywordRotateIndex;

        public ZaloShareForm(string message)
        {
            Text = "Tool Affiliate - Gửi Tin Nhắn Zalo Tự Động (Quét & Tích Chọn Nhóm)";
            Width = 1280;
            Height = 900;
            MinimumSize = new Size(1040, 740);
            StartPosition = FormStartPosition.CenterParent;

            zaloBrowser.Dock = DockStyle.Fill;

            var bottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 350,
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
            minDelayInput.Minimum = 5; minDelayInput.Maximum = 600; minDelayInput.Value = 15;

            var lblDelay2 = new Label { Text = "đến", Left = 769, Top = 38, Width = 30 };
            maxDelayInput.SetBounds(800, 35, 52, 25);
            maxDelayInput.Minimum = 5; maxDelayInput.Maximum = 600; maxDelayInput.Value = 30;

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
                Text = "Nội dung tin nhắn sẽ gửi:",
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

            var grpTopPanel = new Panel { Dock = DockStyle.Top, Height = 28 };
            var grpLabel = new Label
            {
                Text = "Tích chọn NHÓM Zalo:",
                Dock = DockStyle.Left,
                Width = 145,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Padding = new Padding(0, 5, 0, 0)
            };
            var loadGroupsBtn = new Button
            {
                Text = "🔄 Quét toàn bộ Nhóm",
                Dock = DockStyle.Right,
                Width = 155,
                Height = 26,
                BackColor = Color.FromArgb(225, 240, 255)
            };
            var addCurrentChatBtn = new Button
            {
                Text = "➕ Thêm nhóm đang mở",
                Dock = DockStyle.Right,
                Width = 155,
                Height = 26,
                BackColor = Color.FromArgb(220, 248, 228)
            };
            grpTopPanel.Controls.Add(addCurrentChatBtn);
            grpTopPanel.Controls.Add(loadGroupsBtn);
            grpTopPanel.Controls.Add(grpLabel);

            var grpInputPanel = new Panel { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(0, 2, 0, 0) };
            manualGroupInput.Dock = DockStyle.Fill;
            manualGroupInput.PlaceholderText = "Hoặc gõ tên nhóm vào đây rồi bấm Thêm...";
            var addManualBtn = new Button { Text = "Thêm", Dock = DockStyle.Right, Width = 65, Height = 25 };
            var removeUncheckedBtn = new Button { Text = "Xóa dòng bỏ tích", Dock = DockStyle.Right, Width = 115, Height = 25 };
            grpInputPanel.Controls.Add(manualGroupInput);
            grpInputPanel.Controls.Add(addManualBtn);
            grpInputPanel.Controls.Add(removeUncheckedBtn);

            groupsCheckList.Dock = DockStyle.Fill;
            groupsCheckList.CheckOnClick = true;
            groupsCheckList.Font = new Font("Segoe UI", 9);

            splitBottom.Panel2.Controls.Add(groupsCheckList);
            splitBottom.Panel2.Controls.Add(grpInputPanel);
            splitBottom.Panel2.Controls.Add(grpTopPanel);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 42,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 6, 0, 0)
            };

            var autoPilotBtn = new Button
            {
                Text = "🚀 BẬT ZALO TỰ ĐỘNG (LẶP LẠI THEO CHU KỲ)",
                Width = 330,
                Height = 32,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                BackColor = Color.FromArgb(210, 245, 218)
            };

            var sendOnceBtn = new Button
            {
                Text = "⚡ Gửi ngay vào nhóm đã tích",
                Width = 205,
                Height = 32,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                BackColor = Color.FromArgb(255, 242, 204)
            };

            var fetchPreviewBtn = new Button
            {
                Text = "🔄 Lấy link Sale từ API",
                Width = 165,
                Height = 32,
                BackColor = Color.FromArgb(228, 240, 255)
            };

            var stopBtn = new Button
            {
                Text = "⏹ Dừng",
                Width = 100,
                Height = 32,
                Enabled = false,
                BackColor = Color.FromArgb(255, 228, 228)
            };

            LoadSavedConfig();

            loadGroupsBtn.Click += async (_, _) => await LoadGroupsFromZaloAsync();
            addCurrentChatBtn.Click += async (_, _) => await AddCurrentOpenGroupAsync();
            addManualBtn.Click += (_, _) =>
            {
                var name = manualGroupInput.Text.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    AddOrCheckGroupItem(name, true);
                    manualGroupInput.Clear();
                    SaveConfig();
                    statusLabel.Text = $"Đã thêm nhóm '{name}' vào danh sách tích chọn.";
                }
            };
            removeUncheckedBtn.Click += (_, _) =>
            {
                for (int i = groupsCheckList.Items.Count - 1; i >= 0; i--)
                {
                    if (!groupsCheckList.GetItemChecked(i))
                        groupsCheckList.Items.RemoveAt(i);
                }
                SaveConfig();
            };

            EventHandler saveHandler = (_, _) => SaveConfig();
            apiKeyBox.TextChanged += saveHandler;
            affIdBox.TextChanged += saveHandler;
            subIdBox.TextChanged += saveHandler;
            keywordsBox.TextChanged += saveHandler;
            groupsCheckList.ItemCheck += (_, _) => BeginInvoke(new Action(SaveConfig));
            batchCountInput.ValueChanged += saveHandler;
            cycleMinutesInput.ValueChanged += saveHandler;
            minDelayInput.ValueChanged += saveHandler;
            maxDelayInput.ValueChanged += saveHandler;

            autoPilotBtn.Click += async (_, _) => await RunAutoSendFlowAsync(loopForever: true, autoPilotBtn, sendOnceBtn, fetchPreviewBtn, stopBtn, loadGroupsBtn);
            sendOnceBtn.Click += async (_, _) => await RunAutoSendFlowAsync(loopForever: false, autoPilotBtn, sendOnceBtn, fetchPreviewBtn, stopBtn, loadGroupsBtn);

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
                statusLabel.Text = "Đang dừng tiến trình gửi...";
            };

            actions.Controls.AddRange(new Control[] { autoPilotBtn, sendOnceBtn, fetchPreviewBtn, stopBtn });

            statusLabel.Dock = DockStyle.Bottom;
            statusLabel.Height = 24;
            statusLabel.Padding = new Padding(4, 4, 0, 0);
            statusLabel.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            statusLabel.ForeColor = Color.FromArgb(25, 90, 160);
            statusLabel.Text = "Đang chuẩn bị Zalo Web...";

            bottom.Controls.Add(splitBottom);
            bottom.Controls.Add(configGroup);
            bottom.Controls.Add(actions);
            bottom.Controls.Add(statusLabel);

            Controls.Add(zaloBrowser);
            Controls.Add(bottom);

            Shown += async (_, _) =>
            {
                BeginInvoke(new Action(() =>
                {
                    if (splitBottom.ClientSize.Width > 600)
                        splitBottom.SplitterDistance = (int)(splitBottom.ClientSize.Width * 0.54);
                }));
                await InitializeZaloAsync();
            };

            FormClosed += (_, _) =>
            {
                sendCts?.Cancel();
                SaveConfig();
                try { zaloBrowser.CoreWebView2?.Stop(); } catch { }
                zaloBrowser.Dispose();
            };
        }

        private void AddOrCheckGroupItem(string groupName, bool isChecked)
        {
            var clean = groupName.Trim();
            if (string.IsNullOrWhiteSpace(clean)) return;

            for (int i = 0; i < groupsCheckList.Items.Count; i++)
            {
                if (string.Equals(groupsCheckList.Items[i]?.ToString(), clean, StringComparison.OrdinalIgnoreCase))
                {
                    if (isChecked) groupsCheckList.SetItemChecked(i, true);
                    return;
                }
            }
            groupsCheckList.Items.Add(clean, isChecked);
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
                    AddOrCheckGroupItem(g, checkedSet.Contains(g));
                }
            }
            catch { }
        }

        private void SaveConfig()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(configFilePath)!);
                var allList = new List<string>();
                var checkedList = new List<string>();

                for (int i = 0; i < groupsCheckList.Items.Count; i++)
                {
                    var s = groupsCheckList.Items[i]?.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    allList.Add(s);
                    if (groupsCheckList.GetItemChecked(i)) checkedList.Add(s);
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
                    SavedGroups = allList,
                    CheckedGroups = checkedList
                };
                File.WriteAllText(configFilePath, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private async Task InitializeZaloAsync()
        {
            if (initialized) return;
            initialized = true;
            try
            {
                Directory.CreateDirectory(profilePath);
                var environment = await CoreWebView2Environment.CreateAsync(null, profilePath);
                await zaloBrowser.EnsureCoreWebView2Async(environment);
                zaloBrowser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                zaloBrowser.CoreWebView2.Settings.IsStatusBarEnabled = true;
                zaloBrowser.CoreWebView2.FrameCreated += (_, args) =>
                {
                    try
                    {
                        var frame = args.Frame;
                        frames.Add(frame);
                        frame.Destroyed += (_, _) => frames.Remove(frame);
                    }
                    catch { }
                };
                zaloBrowser.CoreWebView2.NavigationCompleted += (_, args) =>
                {
                    statusLabel.Text = args.IsSuccess
                        ? "Zalo Web sẵn sàng. Bấm '🔄 Quét toàn bộ Nhóm' hoặc mở nhóm rồi bấm '➕ Thêm nhóm đang mở'."
                        : "Không tải được Zalo Web. Kiểm tra kết nối Internet.";
                };
                zaloBrowser.CoreWebView2.Navigate("https://chat.zalo.me/");
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Không khởi tạo được Zalo Web: " + ex.Message;
            }
        }

        private async Task AddCurrentOpenGroupAsync()
        {
            if (zaloBrowser.CoreWebView2 is null) return;
            const string script = "(() => { try { const titleNodes = [...document.querySelectorAll('header .header-title, .thread-chat-title, [data-id*=\"div_Main_TabMsg_Header\"] span, .title-header, h1, h2, h3, span, div')].filter(e => { const r = e.getBoundingClientRect(); return r.left > 320 && r.top >= 0 && r.top < 95 && r.width > 30 && r.height > 14 && r.height < 45; }); for (const el of titleNodes) { const t = (el.innerText || '').split('\\n')[0].trim(); if (t && t.length > 1 && t.length < 90 && !/thành viên|members|tìm kiếm|search|vừa truy cập|online/i.test(t)) { return { ok: true, name: t }; } } return { ok: false, name: '' }; } catch (e) { return { ok: false, name: '' }; } })();";

            try
            {
                var raw = await ExecuteInMainAndFramesAsync(script);
                if (!string.IsNullOrWhiteSpace(raw) && raw != "null")
                {
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean() &&
                        doc.RootElement.TryGetProperty("name", out var nameProp))
                    {
                        var groupName = nameProp.GetString()?.Trim() ?? "";
                        if (!string.IsNullOrWhiteSpace(groupName))
                        {
                            AddOrCheckGroupItem(groupName, true);
                            SaveConfig();
                            statusLabel.Text = $"Đã thêm và tích chọn nhóm đang mở: '{groupName}'!";
                            return;
                        }
                    }
                }
                statusLabel.Text = "Chưa nhận diện được tiêu đề nhóm đang mở. Hãy bấm vào 1 nhóm trên Zalo trước.";
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Lỗi đọc nhóm đang mở: " + ex.Message;
            }
        }

        // CHỈ QUÉT NHÓM (KHÔNG LẤY CÁ NHÂN / BẠN BÈ)
        private async Task LoadGroupsFromZaloAsync()
        {
            if (zaloBrowser.CoreWebView2 is null)
            {
                MessageBox.Show(this, "Zalo Web chưa sẵn sàng.", "Zalo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            statusLabel.Text = "Đang tự động lọc danh sách NHÓM từ Zalo Web...";
            var collectedGroups = new List<string>();

            try
            {
                const string openContactsGroupsScript = "(() => { try { const contactsTab = document.querySelector('[data-id*=\"Main_TabCT\"], [title*=\"Danh bạ\"], [title*=\"Contacts\"], .fa-Contact_28_Line, [data-translate-title*=\"STR_TAB_CONTACT\"]'); if (contactsTab) { (contactsTab.closest('[role=\"button\"], div, li') || contactsTab).click(); return { ok: true }; } return { ok: false }; } catch { return { ok: false }; } })();";
                await ExecuteInMainAndFramesAsync(openContactsGroupsScript);
                await Task.Delay(700);

                const string clickGroupListMenuScript = "(() => { try { const nodes = [...document.querySelectorAll('div, span, p, li')].filter(e => { const r = e.getBoundingClientRect(); return r.left > 40 && r.left < 380 && r.top > 50 && r.top < 350 && r.width > 40 && r.height > 15; }); const grpMenu = nodes.find(e => /^(danh sách nhóm|nhóm và cộng đồng|group list|joined groups)$/i.test((e.innerText || '').trim())); if (grpMenu) { (grpMenu.closest('.conv-item, [role=\"listitem\"], div') || grpMenu).click(); return { ok: true }; } return { ok: false }; } catch { return { ok: false }; } })();";
                var openedGroupMenu = await ExecuteInMainAndFramesAsync(clickGroupListMenuScript);
                if (openedGroupMenu is not null && IsSuccessResult(openedGroupMenu))
                {
                    await Task.Delay(900);
                }

                // Tuyệt đối KHÔNG lấy cá nhân (loại bỏ các dòng có chữ "[Thiệp] Gửi lời chào..." hoặc hội thoại 1-1)
                const string extractStrictGroupsScript = "(() => { try { const visible = e => { if (!e) return false; const r = e.getBoundingClientRect(); const s = getComputedStyle(e); return r.width > 30 && r.height > 14 && s.display !== 'none' && s.visibility !== 'hidden'; }; const ignore = /^(tất cả|chưa đọc|phân loại|tìm kiếm|danh sách bạn bè|danh sách nhóm|nhóm và cộng đồng|lời mời kết bạn|lời mời vào nhóm|my documents|cloud của tôi|truyền file|đồng bộ ngay|tải ngay|tin nhắn)$/i; const names = []; const addName = raw => { const clean = (raw || '').split('\\n')[0].replace(/\\s+/g, ' ').trim(); if (!clean || clean.length < 2 || clean.length > 95) return; if (ignore.test(clean) || /^\\d+\\s*(thành viên|members|nhóm|groups)/i.test(clean)) return; if (!names.includes(clean)) names.push(clean); }; const allDivs = [...document.querySelectorAll('div, li, article')].filter(visible); for (const d of allDivs) { const r = d.getBoundingClientRect(); if (r.left > 300 && r.height >= 40 && r.height <= 130) { const text = d.innerText || ''; if (/\\d+\\s*(thành viên|members)/i.test(text)) { const lines = text.split('\\n').map(x => x.trim()).filter(Boolean); if (lines.length >= 2) addName(lines[0]); } } } const leftItems = [...document.querySelectorAll('.conv-item, [id^=\"friend-item-\"], .msg-item, [role=\"listitem\"]')].filter(visible); for (const el of leftItems) { const r = el.getBoundingClientRect(); if (r.left > 450) continue; const text = el.innerText || ''; if (/gửi lời chào|kết bạn|đã là bạn bè/i.test(text)) continue; const html = el.innerHTML || ''; const lines = text.split('\\n').map(x => x.trim()).filter(Boolean); if (lines.length === 0) continue; const secondLine = lines[1] || ''; const thirdLine = lines[2] || ''; const isGroupIndicator = html.includes('group') || html.includes('users') || html.includes('community') || /^[A-Za-zÀ-ỹ0-9_\\s]{1,25}:\\s/.test(secondLine) || /^[A-Za-zÀ-ỹ0-9_\\s]{1,25}:\\s/.test(thirdLine) || /thành viên|nhóm|group|đã tạo nhóm|đã tham gia/i.test(text) || el.querySelectorAll('img').length > 1; if (isGroupIndicator) { addName(lines[0]); } } return { ok: true, groups: names }; } catch (e) { return { ok: false, groups: [] }; } })();";

                var raw = await ExecuteInMainAndFramesAsync(extractStrictGroupsScript);
                if (!string.IsNullOrWhiteSpace(raw) && raw != "null" && raw != "{}")
                {
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.TryGetProperty("groups", out var groupsArr))
                    {
                        var list = JsonSerializer.Deserialize<List<string>>(groupsArr.GetRawText());
                        if (list is not null) collectedGroups.AddRange(list);
                    }
                }

                const string backToMsgTabScript = "(() => { try { const msgTab = document.querySelector('[data-id*=\"Main_TabMsg\"], [title*=\"Tin nhắn\"], [title*=\"Messages\"], .fa-Message_28_Line, [data-translate-title*=\"STR_TAB_MESSAGE\"]'); if (msgTab) (msgTab.closest('[role=\"button\"], div, li') || msgTab).click(); return { ok: true }; } catch { return { ok: false }; } })();";
                await ExecuteInMainAndFramesAsync(backToMsgTabScript);
                await Task.Delay(500);

                var rawMsgTab = await ExecuteInMainAndFramesAsync(extractStrictGroupsScript);
                if (!string.IsNullOrWhiteSpace(rawMsgTab) && rawMsgTab != "null" && rawMsgTab != "{}")
                {
                    using var doc2 = JsonDocument.Parse(rawMsgTab);
                    if (doc2.RootElement.TryGetProperty("groups", out var groupsArr2))
                    {
                        var list2 = JsonSerializer.Deserialize<List<string>>(groupsArr2.GetRawText());
                        if (list2 is not null) collectedGroups.AddRange(list2);
                    }
                }

                await AddCurrentOpenGroupAsync();

                var distinctGroups = collectedGroups.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (distinctGroups.Count > 0)
                {
                    foreach (var name in distinctGroups)
                    {
                        AddOrCheckGroupItem(name, false);
                    }
                    SaveConfig();
                    statusLabel.Text = $"Đã lọc được {distinctGroups.Count} NHÓM! Bạn có thể bấm 'Xóa dòng bỏ tích' để dọn các mục không dùng.";
                    return;
                }

                statusLabel.Text = "Hãy mở nhóm trên Zalo rồi nhấn nút '➕ Thêm nhóm đang mở' để thêm chính xác 100%!";
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Lỗi quét danh sách nhóm: " + ex.Message;
            }
        }

        private List<string> GetTargetGroups()
        {
            var list = new List<string>();
            foreach (var item in groupsCheckList.CheckedItems)
            {
                if (item is string s && !string.IsNullOrWhiteSpace(s))
                {
                    list.Add(s);
                }
            }
            return list;
        }

        private async Task RunAutoSendFlowAsync(bool loopForever, Button autoBtn, Button onceBtn, Button previewBtn, Button stopBtn, Button loadBtn)
        {
            if (autoPilotRunning) return;
            if (zaloBrowser.CoreWebView2 is null)
            {
                MessageBox.Show(this, "Zalo Web chưa sẵn sàng.", "Zalo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            autoPilotRunning = true;
            autoBtn.Enabled = false;
            onceBtn.Enabled = false;
            previewBtn.Enabled = false;
            loadBtn.Enabled = false;
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
                        statusLabel.Text = $"[Đợt #{batchNumber}] Đang lấy sản phẩm sale từ Addlivetag API...";
                        var (fetchedOk, fetchedMsg) = await FetchAndBuildSaleMessageAsync(token);
                        statusLabel.Text = $"[Đợt #{batchNumber}] {fetchedMsg}";
                    }

                    if (string.IsNullOrWhiteSpace(messageBox.Text))
                    {
                        statusLabel.Text = "Chưa có nội dung tin nhắn! Hãy chọn sản phẩm, soạn tin, hoặc điền API Key Addlivetag.";
                        MessageBox.Show(this, statusLabel.Text, "Thiếu nội dung gửi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        break;
                    }

                    var groups = GetTargetGroups();
                    int minSec = (int)Math.Min(minDelayInput.Value, maxDelayInput.Value);
                    int maxSec = (int)Math.Max(minDelayInput.Value, maxDelayInput.Value);

                    if (groups.Count > 0)
                    {
                        int sentCount = 0;
                        for (int i = 0; i < groups.Count; i++)
                        {
                            token.ThrowIfCancellationRequested();
                            var groupName = groups[i];
                            statusLabel.Text = $"[Đợt #{batchNumber} - Nhóm {i + 1}/{groups.Count}] Đang mở nhóm '{groupName}'...";

                            bool opened = await SelectGroupByNameAsync(groupName, token);
                            if (!opened)
                            {
                                statusLabel.Text = $"[Đợt #{batchNumber}] Không tìm thấy nhóm '{groupName}', nhưng sẽ thử gửi nếu nhóm đã mở sẵn...";
                            }

                            await Task.Delay(1000, token);
                            var (ok, sendMsg) = await SendMessageToCurrentOpenChatAsync(token);
                            if (ok)
                            {
                                sentCount++;
                                statusLabel.Text = $"[Đợt #{batchNumber} - {i + 1}/{groups.Count}] Đã gửi thành công toàn bộ nội dung vào '{groupName}'!";
                            }
                            else
                            {
                                statusLabel.Text = $"[Đợt #{batchNumber} - {i + 1}/{groups.Count}] Lỗi gửi '{groupName}': {sendMsg}";
                            }

                            if (i < groups.Count - 1)
                            {
                                int delaySec = Random.Shared.Next(minSec, maxSec + 1);
                                await WaitWithCountdownAsync(delaySec, $"[Đợt #{batchNumber}] Đã gửi {sentCount}/{i + 1} nhóm. Nghỉ chống spam", token);
                            }
                        }
                        statusLabel.Text = $"Hoàn tất đợt #{batchNumber}: Đã gửi thành công {sentCount}/{groups.Count} nhóm!";
                    }
                    else
                    {
                        statusLabel.Text = $"[Đợt #{batchNumber}] Đang gửi vào nhóm đang mở trên Zalo Web...";
                        var (ok, sendMsg) = await SendMessageToCurrentOpenChatAsync(token);
                        statusLabel.Text = ok ? $"[Đợt #{batchNumber}] {sendMsg}" : $"Lỗi gửi: {sendMsg}";
                    }

                    if (!loopForever) break;

                    int cycleSeconds = (int)cycleMinutesInput.Value * 60;
                    await WaitWithCountdownAsync(cycleSeconds, $"[Xong đợt #{batchNumber}] Tự động gửi đợt tiếp theo sau", token);

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
                    onceBtn.Enabled = true;
                    previewBtn.Enabled = true;
                    loadBtn.Enabled = true;
                    stopBtn.Enabled = false;
                }
            }
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

        private async Task<bool> SelectGroupByNameAsync(string groupName, CancellationToken token)
        {
            var nameJson = JsonSerializer.Serialize(groupName.Trim());

            var clickDirectResult = await ExecuteInMainAndFramesAsync(BuildClickConversationScript(nameJson));
            if (clickDirectResult is not null && IsSuccessResult(clickDirectResult))
            {
                await Task.Delay(600, token);
                return true;
            }

            var searchResult = await ExecuteInMainAndFramesAsync(BuildTypeSearchBoxScript(nameJson));
            if (searchResult is not null && IsSuccessResult(searchResult))
            {
                await Task.Delay(1200, token);

                for (int attempt = 0; attempt < 4; attempt++)
                {
                    var clickAfterSearch = await ExecuteInMainAndFramesAsync(BuildClickConversationScript(nameJson));
                    if (clickAfterSearch is not null && IsSuccessResult(clickAfterSearch))
                    {
                        await Task.Delay(600, token);
                        return true;
                    }
                    await Task.Delay(500, token);
                }

                await ExecuteInMainAndFramesAsync(BuildSelectFirstSearchResultScript());
                await Task.Delay(700, token);
                return true;
            }

            return false;
        }

        private async Task<(bool Ok, string Message)> SendMessageToCurrentOpenChatAsync(CancellationToken token)
        {
            var normalizedText = messageBox.Text.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
            try { Clipboard.SetText(normalizedText); } catch { }

            var payload = JsonSerializer.Serialize(normalizedText);

            zaloBrowser.Focus();
            await Task.Delay(250, token);

            var fillScript = BuildFillScript(payload);
            var result = await ExecuteInMainAndFramesAsync(fillScript);
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

            // Chờ 750ms để Zalo nhận đủ tất cả các dòng trước khi bấm Gửi
            await Task.Delay(750, token);

            var triggerResult = await ExecuteInMainAndFramesAsync(BuildTriggerSendScript());
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

        private async Task<string?> ExecuteInMainAndFramesAsync(string script)
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

            foreach (var frame in frames.ToArray())
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
            "(() => { try { const target = (" + groupNameJson + " || '').toLowerCase().replace(/\\s+/g, ' ').trim(); if (!target) return { ok: false, message: 'Tên nhóm trống.' }; const headerTitle = document.querySelector('header .header-title, .thread-chat-title, [data-id*=\"div_Main_TabMsg_Header\"] span, .title-header'); if (headerTitle && (headerTitle.innerText || '').toLowerCase().replace(/\\s+/g, ' ').trim() === target) { return { ok: true, message: 'Nhóm đã được mở sẵn: ' + target }; } const visible = e => { if (!e) return false; const r = e.getBoundingClientRect(); const s = getComputedStyle(e); return r.width > 15 && r.height > 14 && r.left < 500 && r.top > 45 && s.display !== 'none' && s.visibility !== 'hidden' && Number(s.opacity || 1) > 0; }; const items = [...document.querySelectorAll('.conv-item, [id^=\"friend-item-\"], .msg-item, [data-id*=\"Conv\"], [role=\"listitem\"], .virtualized-scroll div')].filter(visible); let best = null; let bestScore = 0; for (const el of items) { if (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.isContentEditable) continue; const lines = (el.innerText || '').split('\\n').map(x => x.replace(/\\s+/g, ' ').trim()).filter(Boolean); if (lines.length === 0) continue; const firstLine = lines[0].toLowerCase(); const fullText = lines.join(' ').toLowerCase(); if (fullText.length > 220) continue; let score = 0; if (firstLine === target) score = 500 - fullText.length; else if (firstLine.includes(target)) score = 350 - firstLine.length; else if (fullText.includes(target)) score = 200 - Math.min(150, fullText.length); if (score > bestScore) { bestScore = score; best = el; } } if (!best) return { ok: false, message: 'Chưa thấy nhóm trong danh sách.' }; const row = best.closest('.conv-item, [id^=\"friend-item-\"], .msg-item, [role=\"listitem\"]') || best; row.scrollIntoView({ block: 'center' }); const rect = row.getBoundingClientRect(); const cx = rect.left + Math.min(80, rect.width / 2); const cy = rect.top + Math.min(25, rect.height / 2); const targetsToClick = [row, best]; for (const t of targetsToClick) { t.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, cancelable: true, clientX: cx, clientY: cy, button: 0 })); t.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true, clientX: cx, clientY: cy, button: 0 })); t.dispatchEvent(new PointerEvent('pointerup', { bubbles: true, cancelable: true, clientX: cx, clientY: cy, button: 0 })); t.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true, clientX: cx, clientY: cy, button: 0 })); t.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, clientX: cx, clientY: cy, button: 0 })); } return { ok: true, message: 'Đã mở nhóm: ' + target }; } catch (e) { return { ok: false, message: String(e) }; } })();";

        private static string BuildTypeSearchBoxScript(string groupNameJson) =>
            "(() => { try { const target = " + groupNameJson + "; const searchInput = document.querySelector('#contact-search-input, input[placeholder*=\"Tìm kiếm\"], input[placeholder*=\"Search\"], input[data-id*=\"txt_Main_Search\"]'); if (!searchInput) return { ok: false, message: 'Không tìm thấy ô Tìm kiếm trên Zalo Web.' }; searchInput.focus(); searchInput.click(); const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set; if (setter) setter.call(searchInput, ''); else searchInput.value = ''; searchInput.dispatchEvent(new Event('input', { bubbles: true })); if (setter) setter.call(searchInput, target); else searchInput.value = target; searchInput.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: target })); searchInput.dispatchEvent(new Event('change', { bubbles: true })); searchInput.dispatchEvent(new KeyboardEvent('keyup', { key: 'a', bubbles: true })); return { ok: true, message: 'Đã nhập tên nhóm vào ô tìm kiếm.' }; } catch (e) { return { ok: false, message: String(e) }; } })();";

        private static string BuildSelectFirstSearchResultScript() =>
            "(() => { try { const searchInput = document.querySelector('#contact-search-input, input[placeholder*=\"Tìm kiếm\"], input[placeholder*=\"Search\"]'); if (searchInput) { searchInput.focus(); searchInput.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', code: 'ArrowDown', keyCode: 40, which: 40, bubbles: true })); searchInput.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true })); return { ok: true, message: 'Đã chọn kết quả đầu tiên.' }; } return { ok: false, message: 'Không có ô tìm kiếm.' }; } catch (e) { return { ok: false, message: String(e) }; } })();";

        // ĐIỀN NGUYÊN KHỐI NHIỀU DÒNG VÀO ZALO MÀ KHÔNG GỌI insertLineBreak (TRÁNH ZALO GỬI SỚM DÒNG 1)
        private static string BuildFillScript(string messageJson) =>
            "(() => { try { const msg = " + messageJson + "; const visible = e => { if (!e) return false; const r = e.getBoundingClientRect(); const s = getComputedStyle(e); return r.width > 20 && r.height > 10 && s.display !== 'none' && s.visibility !== 'hidden' && Number(s.opacity || 1) > 0 && !e.disabled; }; const zaloSelectors = ['#richInput', '[id^=\"input_line_\"]', '.rich-input', '[data-translate-placeholder*=\"Nhập @\"]', '[data-translate-placeholder*=\"tin nhắn\"]', '[contenteditable=\"true\"]', '[role=\"textbox\"]', 'textarea']; let candidates = []; for (const sel of zaloSelectors) { for (const el of document.querySelectorAll(sel)) { if (visible(el) && !candidates.includes(el)) { const idAndClass = ((el.id || '') + ' ' + (el.className || '') + ' ' + (el.getAttribute('placeholder') || '')).toLowerCase(); if (/contact-search|search-input|tìm kiếm/.test(idAndClass)) continue; candidates.push(el); } } } candidates.sort((a, b) => { const ar = a.getBoundingClientRect(), br = b.getBoundingClientRect(); const aId = (a.id || '').includes('richInput') || (a.id || '').includes('input_line') ? 1000 : 0; const bId = (b.id || '').includes('richInput') || (b.id || '').includes('input_line') ? 1000 : 0; return (bId + br.bottom * 2 + br.width) - (aId + ar.bottom * 2 + ar.width); }); let editor = candidates[0]; if (!editor) { return { ok: false, message: 'Chưa nhận diện được ô nhập tin nhắn. Hãy mở một nhóm trên Zalo Web.' }; } if (editor.closest('[contenteditable=\"true\"]')) { editor = editor.closest('[contenteditable=\"true\"]'); } window.__zaloLastEditor = editor; editor.click(); editor.focus(); if (editor.isContentEditable || editor.getAttribute('contenteditable') === 'true') { const selection = window.getSelection(); const range = document.createRange(); range.selectNodeContents(editor); selection.removeAllRanges(); selection.addRange(range); document.execCommand('delete', false, null); let pastedOk = false; try { const dt = new DataTransfer(); dt.setData('text/plain', msg); const pasteEvent = new ClipboardEvent('paste', { bubbles: true, cancelable: true, clipboardData: dt }); editor.dispatchEvent(pasteEvent); const currentText = (editor.innerText || '').trim(); pastedOk = currentText.length >= Math.min(25, msg.length) && currentText.includes('http'); } catch {} if (!pastedOk) { editor.innerHTML = ''; const lines = msg.split('\\n'); lines.forEach((line, idx) => { const div = document.createElement('div'); div.id = 'input_line_' + idx; if (line.trim().length > 0) { div.textContent = line; } else { div.appendChild(document.createElement('br')); } editor.appendChild(div); }); const rEnd = document.createRange(); rEnd.selectNodeContents(editor); rEnd.collapse(false); selection.removeAllRanges(); selection.addRange(rEnd); editor.dispatchEvent(new InputEvent('input', { bubbles: true, cancelable: true, inputType: 'insertFromPaste', data: msg })); editor.dispatchEvent(new Event('change', { bubbles: true })); } } else { const proto = editor.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype; const setter = Object.getOwnPropertyDescriptor(proto, 'value')?.set; if (setter) setter.call(editor, msg); else editor.value = msg; editor.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: msg })); editor.dispatchEvent(new Event('change', { bubbles: true })); } return { ok: true, message: 'Đã điền đầy đủ toàn bộ các dòng vào ô chat Zalo.' }; } catch (err) { return { ok: false, message: 'Lỗi điền nội dung trên Zalo Web: ' + (err && err.message ? err.message : String(err)) }; } })();";

        private static string BuildTriggerSendScript() =>
            "(() => { try { const visible = e => { if (!e) return false; const r = e.getBoundingClientRect(); const s = getComputedStyle(e); return r.width > 10 && r.height > 10 && s.display !== 'none' && s.visibility !== 'hidden' && Number(s.opacity || 1) > 0 && !e.disabled; }; const editor = window.__zaloLastEditor || document.querySelector('#richInput, [contenteditable=\"true\"]'); const sendSelectors = ['[data-translate-title=\"STR_SEND\"]', '[title=\"Gửi tin nhắn\"]', '[title=\"Send message\"]', '.send-msg-btn', '.fa-Sent-msg_24_Line', '[data-id*=\"btn_Main_Send\"]']; let sendBtn = null; for (const sel of sendSelectors) { const found = document.querySelector(sel); if (found && visible(found)) { sendBtn = found.closest('div[role=\"button\"], button, .z--btn--v2, .send-msg-btn') || found; break; } } if (!sendBtn) { const allButtons = [...document.querySelectorAll('button, [role=\"button\"], .z--btn--v2, i.fa-Sent-msg_24_Line')].filter(visible); const matched = allButtons.find(b => { const meta = ((b.innerText || '') + ' ' + (b.title || '') + ' ' + (b.className || '') + ' ' + (b.getAttribute('aria-label') || '') + ' ' + (b.getAttribute('data-translate-title') || '')).toLowerCase(); return /str_send|gửi tin nhắn|send message|sent-msg|send-msg|^gửi$|^send$/i.test(meta.trim()); }); if (matched) sendBtn = matched.closest('div[role=\"button\"], button, .z--btn--v2') || matched; } if (sendBtn) { const rect = sendBtn.getBoundingClientRect(); const cx = rect.left + rect.width / 2; const cy = rect.top + rect.height / 2; sendBtn.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, cancelable: true, clientX: cx, clientY: cy })); sendBtn.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true, clientX: cx, clientY: cy })); sendBtn.dispatchEvent(new PointerEvent('pointerup', { bubbles: true, cancelable: true, clientX: cx, clientY: cy })); sendBtn.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true, clientX: cx, clientY: cy })); sendBtn.click(); return { ok: true, message: 'Đã tự động điền đầy đủ link và bấm nút Gửi thành công!' }; } if (editor) { editor.focus(); const enterOpts = { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, charCode: 13, bubbles: true, cancelable: true }; editor.dispatchEvent(new KeyboardEvent('keydown', enterOpts)); editor.dispatchEvent(new KeyboardEvent('keypress', enterOpts)); editor.dispatchEvent(new KeyboardEvent('keyup', enterOpts)); return { ok: true, message: 'Đã tự động điền đầy đủ link và nhấn Enter gửi thành công!' }; } return { ok: false, message: 'Không tìm thấy nút Gửi trên Zalo Web.' }; } catch (err) { return { ok: false, message: 'Lỗi kích hoạt nút Gửi: ' + (err && err.message ? err.message : String(err)) }; } })();";

        private async Task<(bool Ok, string Message)> FetchAndBuildSaleMessageAsync(CancellationToken token)
        {
            var apiKey = apiKeyBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return (false, "Hãy nhập API Key Addlivetag ở dòng cấu hình.");
            }

            var keywords = keywordsBox.Text
                .Split(new[] { ',', ';', '\n' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
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