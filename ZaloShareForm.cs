using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ToolAffiliate;

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
    public List<string> SelectedGroups { get; set; } = new();
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
    private void InitializeZaloMenu()
    {
    }

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
            selected = items
                .Where(item => !string.IsNullOrWhiteSpace(item.AffiliateUrl))
                .ToList();
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
        "ToolAffiliate", "addlivetag-config-v2.json");

    private bool initialized;
    private bool autoPilotRunning;
    private CancellationTokenSource? sendCts;
    private int keywordRotateIndex;

    public ZaloShareForm(string message)
    {
        Text = "Tool Affiliate - Gửi Tin Nhắn Zalo Tự Động (Chỉ Lọc Nhóm)";
        Width = 1280;
        Height = 900;
        MinimumSize = new Size(1020, 720);
        StartPosition = FormStartPosition.CenterParent;

        zaloBrowser.Dock = DockStyle.Fill;

        var bottom = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 340,
            Padding = new Padding(10, 8, 10, 6),
            BackColor = Color.FromArgb(246, 248, 252)
        };

        var configGroup = new Panel
        {
            Dock = DockStyle.Top,
            Height = 72
        };

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
        batchCountInput.Minimum = 1;
        batchCountInput.Maximum = 20;
        batchCountInput.Value = 5;

        var lblCycle = new Label { Text = "Lặp lại sau (phút):", Left = 858, Top = 6, Width = 112 };
        cycleMinutesInput.SetBounds(972, 3, 58, 25);
        cycleMinutesInput.Minimum = 1;
        cycleMinutesInput.Maximum = 1440;
        cycleMinutesInput.Value = 30;

        var lblKw = new Label { Text = "Từ khóa săn Sale:", Left = 0, Top = 38, Width = 125, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
        keywordsBox.SetBounds(126, 35, 456, 25);
        keywordsBox.PlaceholderText = "flash sale, giảm giá, deal 1k, tai nghe, đồ gia dụng...";

        var lblDelay1 = new Label { Text = "Nghỉ chống spam:", Left = 592, Top = 38, Width = 115 };
        minDelayInput.SetBounds(712, 35, 52, 25);
        minDelayInput.Minimum = 5;
        minDelayInput.Maximum = 600;
        minDelayInput.Value = 15;

        var lblDelay2 = new Label { Text = "đến", Left = 769, Top = 38, Width = 30 };
        maxDelayInput.SetBounds(800, 35, 52, 25);
        maxDelayInput.Minimum = 5;
        maxDelayInput.Maximum = 600;
        maxDelayInput.Value = 30;

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
            Text = "Tích chọn NHÓM Zalo cần gửi:",
            Dock = DockStyle.Left,
            Width = 195,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Padding = new Padding(0, 5, 0, 0)
        };
        var loadGroupsBtn = new Button
        {
            Text = "🔄 Tải danh sách nhóm từ Zalo",
            Dock = DockStyle.Right,
            Width = 205,
            Height = 26,
            BackColor = Color.FromArgb(230, 242, 255)
        };
        grpTopPanel.Controls.Add(loadGroupsBtn);
        grpTopPanel.Controls.Add(grpLabel);

        groupsCheckList.Dock = DockStyle.Fill;
        groupsCheckList.CheckOnClick = true;
        groupsCheckList.Font = new Font("Segoe UI", 9);

        splitBottom.Panel2.Controls.Add(groupsCheckList);
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
                    splitBottom.SplitterDistance = (int)(splitBottom.ClientSize.Width * 0.58);
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

            if (cfg.SelectedGroups != null && cfg.SelectedGroups.Count > 0)
            {
                foreach (var g in cfg.SelectedGroups)
                {
                    if (!groupsCheckList.Items.Contains(g))
                        groupsCheckList.Items.Add(g, true);
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
            var checkedList = new List<string>();
            foreach (var item in groupsCheckList.CheckedItems)
            {
                if (item is string s && !string.IsNullOrWhiteSpace(s)) checkedList.Add(s);
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
                SelectedGroups = checkedList
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
                    ? "Zalo Web sẵn sàng. Bấm '🔄 Tải danh sách nhóm từ Zalo' để hiện danh sách nhóm và tích chọn."
                    : "Không tải được Zalo Web. Kiểm tra kết nối Internet.";
            };
            zaloBrowser.CoreWebView2.Navigate("https://chat.zalo.me/");
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Không khởi tạo được Zalo Web: " + ex.Message;
        }
    }

    private async Task LoadGroupsFromZaloAsync()
    {
        if (zaloBrowser.CoreWebView2 is null)
        {
            MessageBox.Show(this, "Zalo Web chưa sẵn sàng.", "Zalo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        statusLabel.Text = "Đang quét và lọc danh sách NHÓM từ Zalo Web...";
        const string script = """
        (() => {
          try {
            const visible = e => {
              if (!e) return false;
              const r = e.getBoundingClientRect();
              const s = getComputedStyle(e);
              return r.width > 20 && r.height > 14 && r.left < 500 &&
                s.display !== 'none' && s.visibility !== 'hidden';
            };
            const items = [...document.querySelectorAll('.conv-item, [id^="friend-item-"], .msg-item, [role="listitem"]')].filter(visible);
            const names = [];
            for (const el of items) {
              if (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.isContentEditable) continue;
              
              const html = el.innerHTML || '';
              const text = el.innerText || '';
              
              const isGroup = html.includes('avatar-group') || 
                              html.includes('ic-avatar-group') || 
                              html.includes('fa-users') || 
                              text.includes('thành viên') ||
                              el.querySelector('svg[class*="group"], .ic-avatar-group, .avatar-group') !== null;

              if (!isGroup) continue;

              const rawText = text.split('\n')[0].replace(/\s+/g, ' ').trim();
              if (rawText && rawText.length < 100 && !names.includes(rawText)) {
                names.push(rawText);
              }
            }
            return { ok: true, groups: names };
          } catch (e) {
            return { ok: false, groups: [] };
          }
        })();
        """;

        try
        {
            var raw = await ExecuteInMainAndFramesAsync(script);
            if (string.IsNullOrWhiteSpace(raw) || raw == "null" || raw == "{}")
            {
                raw = await zaloBrowser.CoreWebView2.ExecuteScriptAsync(script);
            }

            if (!string.IsNullOrWhiteSpace(raw) && raw != "null" && raw != "{}")
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("ok", out var okProp) && okProp.GetBoolean() &&
                    doc.RootElement.TryGetProperty("groups", out var groupsArr))
                {
                    var fetchedList = JsonSerializer.Deserialize<List<string>>(groupsArr.GetRawText());
                    if (fetchedList is not null && fetchedList.Count > 0)
                    {
                        var previouslyChecked = groupsCheckList.CheckedItems.Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
                        
                        groupsCheckList.Items.Clear();
                        foreach (var name in fetchedList)
                        {
                            bool shouldCheck = previouslyChecked.Contains(name);
                            groupsCheckList.Items.Add(name, shouldCheck);
                        }

                        statusLabel.Text = $"Đã lọc và tải thành công {fetchedList.Count} NHÓM từ Zalo!";
                        return;
                    }
                }
            }
            statusLabel.Text = "Không tìm thấy nhóm nào. Hãy chắc chắn bạn đã đăng nhập Zalo Web.";
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
                            statusLabel.Text = $"[Đợt #{batchNumber} - {i + 1}/{groups.Count}] Đã gửi thành công vào '{groupName}'!";
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
        var payload = JsonSerializer.Serialize(normalizedText);

        zaloBrowser.Focus();
        await Task.Delay(200, token);

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

        await Task.Delay(600, token);

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

    private static string BuildClickConversationScript(string groupNameJson) => """
    (() => {
      try {
        const target = (__GROUP_NAME__ || '').toLowerCase().replace(/\s+/g, ' ').trim();
        if (!target) return { ok: false, message: 'Tên nhóm trống.' };

        const headerTitle = document.querySelector('header .header-title, .thread-chat-title, [data-id*="div_Main_TabMsg_Header"] span, .title-header');
        if (headerTitle && (headerTitle.innerText || '').toLowerCase().replace(/\s+/g, ' ').trim() === target) {
          return { ok: true, message: 'Nhóm đã được mở sẵn: ' + target };
        }

        const visible = e => {
          if (!e) return false;
          const r = e.getBoundingClientRect();
          const s = getComputedStyle(e);
          return r.width > 15 && r.height > 14 && r.left < 500 && r.top > 45 &&
            s.display !== 'none' && s.visibility !== 'hidden' && Number(s.opacity || 1) > 0;
        };

        const items = [...document.querySelectorAll('.conv-item, [id^="friend-item-"], .msg-item, [data-id*="Conv"], [role="listitem"], .virtualized-scroll div')].filter(visible);
        let best = null;
        let bestScore = 0;

        for (const el of items) {
          if (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.isContentEditable) continue;
          const lines = (el.innerText || '').split('\n').map(x => x.replace(/\s+/g, ' ').trim()).filter(Boolean);
          if (lines.length === 0) continue;
          const firstLine = lines[0].toLowerCase();
          const fullText = lines.join(' ').toLowerCase();
          if (fullText.length > 220) continue;

          let score = 0;
          if (firstLine === target) score = 500 - fullText.length;
          else if (firstLine.includes(target)) score = 350 - firstLine.length;
          else if (fullText.includes(target)) score = 200 - Math.min(150, fullText.length);

          if (score > bestScore) {
            bestScore = score;
            best = el;
          }
        }

        if (!best) return { ok: false, message: 'Chưa thấy nhóm trong danh sách.' };

        const row = best.closest('.conv-item, [id^="friend-item-"], .msg-item, [role="listitem"]') || best;
        row.scrollIntoView({ block: 'center' });
        const rect = row.getBoundingClientRect();
        const cx = rect.left + Math.min(80, rect.width / 2);
        const cy = rect.top + Math.min(25, rect.height / 2);

        const targetsToClick = [row, best];
        for (const t of targetsToClick) {
          t.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, cancelable: true, clientX: cx, clientY: cy, button: 0 }));
          t.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true, clientX: cx, clientY: cy, button: 0 }));
          t.dispatchEvent(new PointerEvent('pointerup', { bubbles: true, cancelable: true, clientX: cx, clientY: cy, button: 0 }));
          t.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true, clientX: cx, clientY: cy, button: 0 }));
          t.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, clientX: cx, clientY: cy, button: 0 }));
        }

        return { ok: true, message: 'Đã mở nhóm: ' + target };
      } catch (e) {
        return { ok: false, message: String(e) };
      }
    })();
    """.Replace("__GROUP_NAME__", groupNameJson);