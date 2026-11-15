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

            // Panel bên phải: Thanh nút quét nhóm + thêm nhóm đang mở + nhập nhanh
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

        // LẤY TÊN NHÓM ĐANG MỞ TRỰC TIẾP TRÊN KHUNG CHAT BÊN PHẢI
        private async Task AddCurrentOpenGroupAsync()
        {
            if (zaloBrowser.CoreWebView2 is null) return;
            const string script = "(() => { try { const header = document.querySelector('header, #header, [data-id*=\"Header\"], .title-header, .thread-chat-title')?.closest('header, div') || document.body; const titleNodes = [...document.querySelectorAll('header .header-title, .thread-chat-title, [data-id*=\"div_Main_TabMsg_Header\"] span, .title-header, h1, h2, h3, span, div')].filter(e => { const r = e.getBoundingClientRect(); return r.left > 320 && r.top >= 0 && r.top < 95 && r.width > 30 && r.height > 14 && r.height < 45; }); for (const el of titleNodes) { const t = (el.innerText || '').split('\\n')[0].trim(); if (t && t.length > 1 && t.length < 90 && !/thành viên|members|tìm kiếm|search|vừa truy cập|online/i.test(t)) { return { ok: true, name: t }; } } return { ok: false, name: '' }; } catch (e) { return { ok: false, name: '' }; } })();";

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

        // QUÉT TOÀN BỘ NHÓM TỪ DANH BẠ NHÓM & CỘT TRÒ CHUYỆN ZALO WEB
        private async Task LoadGroupsFromZaloAsync()
        {
            if (zaloBrowser.CoreWebView2 is null)
            {
                MessageBox.Show(this, "Zalo Web chưa sẵn sàng.", "Zalo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            statusLabel.Text = "Đang tự động chuyển sang mục Danh bạ -> Danh sách nhóm của Zalo để quét...";
            var collectedGroups = new List<string>();

            try
            {
                // Bước 1: Thử bấm sang tab Danh bạ (Contacts) -> Danh sách nhóm (Joined Groups)
                const string openContactsGroupsScript = "(() => { try { const contactsTab = document.querySelector('[data-id*=\"Main_TabCT\"], [title*=\"Danh bạ\"], [title*=\"Contacts\"], .fa-Contact_28_Line, [data-translate-title*=\"STR_TAB_CONTACT\"]'); if (contactsTab) { (contactsTab.closest('[role=\"button\"], div, li') || contactsTab).click(); return { ok: true, step: 'contacts' }; } return { ok: false }; } catch { return { ok: false }; } })();";
                await ExecuteInMainAndFramesAsync(openContactsGroupsScript);
                await Task.Delay(700);

                const string clickGroupListMenuScript = "(() => { try { const nodes = [...document.querySelectorAll('div, span, p, li')].filter(e => { const r = e.getBoundingClientRect(); return r.left > 40 && r.left < 380 && r.top > 50 && r.top < 350 && r.width > 40 && r.height > 15; }); const grpMenu = nodes.find(e => /^(danh sách nhóm|nhóm và cộng đồng|group list|joined groups)$/i.test((e.innerText || '').trim())); if (grpMenu) { (grpMenu.closest('.conv-item, [role=\"listitem\"], div') || grpMenu).click(); return { ok: true }; } return { ok: false }; } catch { return { ok: false }; } })();";
                var openedGroupMenu = await ExecuteInMainAndFramesAsync(clickGroupListMenuScript);
                if (openedGroupMenu is not null && IsSuccessResult(openedGroupMenu))
                {
                    await Task.Delay(900);
                }

                // Bước 2: Quét toàn bộ các nhóm trong trang Danh sách nhóm & trong danh sách hội thoại
                const string extractGroupsScript = "(() => { try { const visible = e => { if (!e) return false; const r = e.getBoundingClientRect(); const s = getComputedStyle(e); return r.width > 30 && r.height > 14 && s.display !== 'none' && s.visibility !== 'hidden'; }; const ignore = /^(tất cả|chưa đọc|phân loại|tìm kiếm|danh sách bạn bè|danh sách nhóm|nhóm và cộng đồng|lời mời kết bạn|lời mời vào nhóm|my documents|cloud của tôi|truyền file|đồng bộ ngay|tải ngay|tin nhắn)$/i; const names = []; const addName = raw => { const clean = (raw || '').split('\\n')[0].replace(/\\s+/g, ' ').trim(); if (!clean || clean.length < 2 || clean.length > 95) return; if (ignore.test(clean) || /^\\d+\\s*(thành viên|members|nhóm|groups)/i.test(clean)) return; if (!names.includes(clean)) names.push(clean); }; const rightCards = [...document.querySelectorAll('.group-item, .card-info, [data-id*=\"Group\"], [class*=\"group\"]')].filter(visible); for (const card of rightCards) { const r = card.getBoundingClientRect(); if (r.left > 300) { const lines = (card.innerText || '').split('\\n').map(x => x.trim()).filter(Boolean); const hasMemberText = lines.some(l => /thành viên|members/i.test(l)); if (hasMemberText && lines.length > 0) addName(lines[0]); } } const allDivs = [...document.querySelectorAll('div, li, article')].filter(visible); for (const d of allDivs) { const r = d.getBoundingClientRect(); if (r.left > 300 && r.height >= 40 && r.height <= 130) { const text = d.innerText || ''; if (/\\d+\\s*(thành viên|members)/i.test(text)) { const lines = text.split('\\n').map(x => x.trim()).filter(Boolean); if (lines.length >= 2) addName(lines[0]); } } } const leftItems = [...document.querySelectorAll('.conv-item, [id^=\"friend-item-\"], .msg-item, [role=\"listitem\"]')].filter(visible); for (const el of leftItems) { const r = el.getBoundingClientRect(); if (r.left > 450) continue; const text = el.innerText || ''; const html = el.innerHTML || ''; const lines = text.split('\\n').map(x => x.trim()).filter(Boolean); if (lines.length === 0) continue; const secondLine = lines[1] || ''; const isGroupIndicator = html.includes('group') || html.includes('users') || html.includes('community') || /^[A-Za-zÀ-ỹ0-9_\\s]{1,25}:\\s/.test(secondLine) || /thành viên|nhóm|group|đã tạo nhóm|đã tham gia/i.test(text) || el.querySelectorAll('img').length > 1; if (isGroupIndicator && !/^bạn:/i.test(secondLine)) { addName(lines[0]); } } if (names.length === 0) { for (const el of leftItems) { const lines = (el.innerText || '').split('\\n').map(x => x.trim()).filter(Boolean); if (lines.length > 0) addName(lines[0]); } } return { ok: true, groups: names }; } catch (e) { return { ok: false, groups: [] }; } })();";

                var raw = await ExecuteInMainAndFramesAsync(extractGroupsScript);
                if (!string.IsNullOrWhiteSpace(raw) && raw != "null" && raw != "{}")
                {
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.TryGetProperty("groups", out var groupsArr))
                    {
                        var list = JsonSerializer.Deserialize<List<string>>(groupsArr.GetRawText());
                        if (list is not null) collectedGroups.AddRange(list);
                    }
                }

                // Quay trở lại tab Tin nhắn chính của Zalo
                const string backToMsgTabScript = "(() => { try { const msgTab = document.querySelector('[data-id*=\"Main_TabMsg\"], [title*=\"Tin nhắn\"], [title*=\"Messages\"], .fa-Message_28_Line, [data-translate-title*=\"STR_TAB_MESSAGE\"]'); if (msgTab) (msgTab.closest('[role=\"button\"], div, li') || msgTab).click(); return { ok: true }; } catch { return { ok: false }; } })();";
                await ExecuteInMainAndFramesAsync(backToMsgTabScript);
                await Task.Delay(500);

                // Quét thêm 1 lượt ở cột Tin nhắn để lấy các nhóm có tin nhắn dạng "Tên: nội dung" hoặc nhóm đang mở
                var rawMsgTab = await ExecuteInMainAndFramesAsync(extractGroupsScript);
                if (!string.IsNullOrWhiteSpace(rawMsgTab) && rawMsgTab != "null" && raw