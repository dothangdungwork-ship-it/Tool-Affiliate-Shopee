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
    public string GroupsText { get; set; } = "";
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
    // Đã bỏ thanh menu Zalo cũ ở góc trên để tránh trùng lặp,
    // chỉ dùng duy nhất nút "Gửi tin nhắn vào Zalo" trên giao diện chính.
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
    private readonly TextBox groupsBox = new();
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
        "ToolAffiliate", "addlivetag-config.json");

    private bool initialized;
    private bool autoPilotRunning;
    private CancellationTokenSource? sendCts;
    private int keywordRotateIndex;

    public ZaloShareForm(string message)
    {
        Text = "Tool Affiliate - Tự Động Gửi Tin Nhắn Zalo (Addlivetag Auto)";
        Width = 1260;
        Height = 880;
        MinimumSize = new Size(1020, 720);
        StartPosition = FormStartPosition.CenterParent;

        // Phần trên: Khung Zalo Web chiếm toàn bộ không gian chính
        zaloBrowser.Dock = DockStyle.Fill;

        // Phần dưới: Gom toàn bộ phần Zalo Tự Động xuống khu vực gửi tin nhắn Zalo
        var bottom = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 320,
            Padding = new Padding(10, 8, 10, 6),
            BackColor = Color.FromArgb(246, 248, 252)
        };

        // 1. Khung cấu hình Addlivetag & Thời gian tự động (Nằm gọn trong phần gửi tin nhắn bên dưới)
        var configGroup = new Panel
        {
            Dock = DockStyle.Top,
            Height = 72
        };

        var lblKey = new Label { Text = "API Key Addlivetag:", Left = 0, Top = 6, Width = 125, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
        apiKeyBox.SetBounds(126, 3, 250, 25);
        apiKeyBox.PlaceholderText = "Dán X-API-Key từ addlivetag.com...";

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

        var lblDelay1 = new Label { Text = "Nghỉ chống spam giữa các nhóm:", Left = 592, Top = 38, Width = 195 };
        minDelayInput.SetBounds(788, 35, 52, 25);
        minDelayInput.Minimum = 5;
        minDelayInput.Maximum = 600;
        minDelayInput.Value = 15;

        var lblDelay2 = new Label { Text = "đến", Left = 845, Top = 38, Width = 30 };
        maxDelayInput.SetBounds(876, 35, 52, 25);
        maxDelayInput.Minimum = 5;
        maxDelayInput.Maximum = 600;
        maxDelayInput.Value = 30;

        var lblDelay3 = new Label { Text = "giây/nhóm", Left = 934, Top = 38, Width = 80 };

        configGroup.Controls.AddRange(new Control[]
        {
            lblKey, apiKeyBox, lblAffId, affIdBox, lblSubId, subIdBox,
            lblCount, batchCountInput, lblCycle, cycleMinutesInput,
            lblKw, keywordsBox, lblDelay1, minDelayInput, lblDelay2, maxDelayInput, lblDelay3
        });

        // 2. Khung chia đôi: Nội dung tin nhắn tự động (Trái) & Danh sách nhóm Zalo (Phải)
        var splitBottom = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 8
        };

        var msgLabel = new Label
        {
            Text = "Nội dung tin nhắn (tự động điền khi chạy Auto hoặc chỉnh sửa trực tiếp):",
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

        var grpLabel = new Label
        {
            Text = "Danh sách tên nhóm Zalo tự động gửi (Mỗi dòng 1 nhóm):",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        groupsBox.Multiline = true;
        groupsBox.ScrollBars = ScrollBars.Vertical;
        groupsBox.Dock = DockStyle.Fill;
        groupsBox.Font = new Font("Segoe UI", 9);
        groupsBox.PlaceholderText = "Ví dụ:\nBấm đũ shopee\nHội săn sale Shopee...";
        splitBottom.Panel2.Controls.Add(groupsBox);
        splitBottom.Panel2.Controls.Add(grpLabel);

        // 3. Thanh nút điều khiển Auto duy nhất ở dưới cùng
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
            Text = "🚀 BẬT ZALO TỰ ĐỘNG (TỰ LẤY LINK SALE & TỰ GỬI)",
            Width = 360,
            Height = 32,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            BackColor = Color.FromArgb(210, 245, 218)
        };

        var fetchPreviewBtn = new Button
        {
            Text = "🔄 Lấy thử link Sale mới",
            Width = 175,
            Height = 32,
            BackColor = Color.FromArgb(228, 240, 255)
        };

        var stopBtn = new Button
        {
            Text = "⏹ Dừng tự động",
            Width = 135,
            Height = 32,
            Enabled = false,
            BackColor = Color.FromArgb(255, 228, 228)
        };

        LoadSavedConfig();

        EventHandler saveHandler = (_, _) => SaveConfig();
        apiKeyBox.TextChanged += saveHandler;
        affIdBox.TextChanged += saveHandler;
        subIdBox.TextChanged += saveHandler;
        keywordsBox.TextChanged += saveHandler;
        groupsBox.TextChanged += saveHandler;
        batchCountInput.ValueChanged += saveHandler;
        cycleMinutesInput.ValueChanged += saveHandler;
        minDelayInput.ValueChanged += saveHandler;
        maxDelayInput.ValueChanged += saveHandler;

        autoPilotBtn.Click += async (_, _) => await StartFullAutoPilotAsync(autoPilotBtn, fetchPreviewBtn, stopBtn);

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
            statusLabel.Text = "Đang dừng chế độ tự động...";
        };

        actions.Controls.AddRange(new Control[] { autoPilotBtn, fetchPreviewBtn, stopBtn });

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
                    splitBottom.SplitterDistance = (int)(splitBottom.ClientSize.Width * 0.62);
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
            groupsBox.Text = cfg.GroupsText ?? "";
        }
        catch { }
    }

    private void SaveConfig()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configFilePath)!);
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
                GroupsText = groupsBox.Text
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
                    ? "Zalo Web sẵn sàng. Hãy điền thông tin bên dưới và bấm 'BẬT ZALO TỰ ĐỘNG'."
                    : "Không tải được Zalo Web. Kiểm tra kết nối Internet.";
            };
            zaloBrowser.CoreWebView2.Navigate("https://chat.zalo.me/");
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Không khởi tạo được Zalo Web: " + ex.Message;
        }
    }

    private async Task StartFullAutoPilotAsync(Button autoBtn, Button previewBtn, Button stopBtn)
    {
        if (autoPilotRunning) return;
        if (zaloBrowser.CoreWebView2 is null)
        {
            MessageBox.Show(this, "Zalo Web chưa sẵn sàng.", "Zalo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var groups = GetTargetGroups();
        if (groups.Count == 0 && string.IsNullOrWhiteSpace(apiKeyBox.Text) && string.IsNullOrWhiteSpace(messageBox.Text))
        {
            MessageBox.Show(this,
                "Vui lòng nhập API Key Addlivetag (hoặc nội dung tin nhắn) và danh sách nhóm Zalo ở khung bên dưới trước khi bật tự động.",
                "Thiếu thông tin", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        autoPilotRunning = true;
        autoBtn.Enabled = false;
        previewBtn.Enabled = false;
        stopBtn.Enabled = true;

        sendCts = new CancellationTokenSource();
        var token = sendCts.Token;
        int batchNumber = 0;

        try
        {
            while (!token.IsCancellationRequested)
            {
                batchNumber++;

                // 1. Tự động lấy sản phẩm sale mới từ Addlivetag nếu có API Key
                if (!string.IsNullOrWhiteSpace(apiKeyBox.Text))
                {
                    statusLabel.Text = $"[Đợt #{batchNumber}] Đang tự động lấy sản phẩm sale từ Addlivetag API...";
                    var (fetchedOk, fetchedMsg) = await FetchAndBuildSaleMessageAsync(token);
                    statusLabel.Text = $"[Đợt #{batchNumber}] {fetchedMsg}";
                    if (!fetchedOk && string.IsNullOrWhiteSpace(messageBox.Text))
                    {
                        await WaitWithCountdownAsync(60, "Chưa lấy được sản phẩm, tự thử lại sau", token);
                        continue;
                    }
                }

                if (string.IsNullOrWhiteSpace(messageBox.Text))
                {
                    statusLabel.Text = "Nội dung tin nhắn đang trống. Hãy nhập API Key Addlivetag hoặc nội dung tin nhắn.";
                    break;
                }

                // 2. Tự động gửi vào các nhóm Zalo (không cần bấm xác nhận)
                groups = GetTargetGroups();
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

                        if (await SelectGroupByNameAsync(groupName, token))
                        {
                            await Task.Delay(800, token);
                            var (ok, _) = await SendMessageToCurrentOpenChatAsync();
                            if (ok) sentCount++;
                        }

                        if (i < groups.Count - 1)
                        {
                            int delaySec = Random.Shared.Next(minSec, maxSec + 1);
                            await WaitWithCountdownAsync(delaySec, $"[Đợt #{batchNumber}] Đã gửi {sentCount}/{i + 1} nhóm. Nghỉ chống spam", token);
                        }
                    }
                }
                else
                {
                    statusLabel.Text = $"[Đợt #{batchNumber}] Đang tự động gửi vào nhóm đang mở...";
                    await SendMessageToCurrentOpenChatAsync();
                }

                // 3. Nghỉ hết chu kỳ phút trước khi sang đợt tự động tiếp theo
                int cycleSeconds = (int)cycleMinutesInput.Value * 60;
                await WaitWithCountdownAsync(cycleSeconds, $"[Xong đợt #{batchNumber}] Tự động lấy sale & gửi đợt tiếp theo sau", token);
            }
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "Đã dừng chế độ Zalo tự động.";
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Lỗi khi chạy tự động: " + ex.Message;
        }
        finally
        {
            autoPilotRunning = false;
            if (!IsDisposed)
            {
                autoBtn.Enabled = true;
                previewBtn.Enabled = true;
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

    private async Task<(bool Ok, string Message)> FetchAndBuildSaleMessageAsync(CancellationToken token)
    {
        var apiKey = apiKeyBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return (false, "Hãy nhập API Key Addlivetag ở dòng cấu hình bên dưới.");
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
            return (false, "Không lấy được sản phẩm từ Addlivetag API. Hãy kiểm tra lại API Key.");
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

    private List<string> GetTargetGroups() =>
        groupsBox.Text
            .Replace("\r", "")
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private async Task<bool> SelectGroupByNameAsync(string groupName, CancellationToken token)
    {
        var nameJson = JsonSerializer.Serialize(groupName.Trim());

        var clickDirectResult = await ExecuteInMainAndFramesAsync(BuildClickConversationScript(nameJson));
        if (clickDirectResult is not null && IsSuccessResult(clickDirectResult))
            return true;

        var searchResult = await ExecuteInMainAndFramesAsync(BuildTypeSearchBoxScript(nameJson));
        if (searchResult is null || !IsSuccessResult(searchResult))
            return false;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            await Task.Delay(550, token);
            var clickAfterSearch = await ExecuteInMainAndFramesAsync(BuildClickConversationScript(nameJson));
            if (clickAfterSearch is not null && IsSuccessResult(clickAfterSearch))
                return true;
        }

        return false;
    }

    private async Task<(bool Ok, string Message)> SendMessageToCurrentOpenChatAsync()
    {
        var normalizedText = messageBox.Text.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
        try { Clipboard.SetText(normalizedText); } catch { }

        var payload = JsonSerializer.Serialize(normalizedText);

        zaloBrowser.Focus();
        await Task.Delay(150);

        var fillScript = BuildFillScript(payload);
        var result = await ExecuteInMainAndFramesAsync(fillScript);
        if (string.IsNullOrWhiteSpace(result) || result == "null")
        {
            return (false, "Không tìm thấy khung soạn tin Zalo.");
        }

        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;
        var ok = root.TryGetProperty("ok", out var okValue) && okValue.GetBoolean();
        var message = root.TryGetProperty("message", out var messageValue)
            ? messageValue.GetString() ?? "" : "Không xác định được kết quả.";

        if (ok)
        {
            await Task.Delay(450);
            var triggerResult = await ExecuteInMainAndFramesAsync(BuildTriggerSendScript());
            if (!string.IsNullOrWhiteSpace(triggerResult) && triggerResult != "null")
            {
                try
                {
                    using var trigDoc = JsonDocument.Parse(triggerResult);
                    if (trigDoc.RootElement.TryGetProperty("message", out var trigMsg) &&
                        !string.IsNullOrWhiteSpace(trigMsg.GetString()))
                    {
                        message = trigMsg.GetString()!;
                    }
                }
                catch { }
            }
        }

        return (ok, message);
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

        const visible = e => {
          if (!e) return false;
          const r = e.getBoundingClientRect();
          const s = getComputedStyle(e);
          return r.width > 20 && r.height > 14 && r.left < 520 &&
            s.display !== 'none' && s.visibility !== 'hidden' && Number(s.opacity || 1) > 0;
        };

        const allNodes = [...document.querySelectorAll('.conv-item, [id^="friend-item-"], .msg-item, [role="listitem"], div, span')].filter(visible);
        let best = null;
        let bestScore = 0;

        for (const el of allNodes) {
          if (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.isContentEditable) continue;
          const rawText = (el.innerText || el.textContent || '').replace(/\s+/g, ' ').trim();
          if (!rawText || rawText.length > 180) continue;
          const lower = rawText.toLowerCase();
          const firstLine = (el.innerText || '').split('\n')[0].replace(/\s+/g, ' ').trim().toLowerCase();

          let score = 0;
          if (lower === target || firstLine === target) score = 300 - rawText.length;
          else if (firstLine.includes(target)) score = 200 - firstLine.length;
          else if (lower.includes(target) && rawText.length < target.length + 50) score = 100;

          if (score > bestScore) {
            bestScore = score;
            best = el;
          }
        }

        if (!best) return { ok: false, message: 'Chưa thấy nhóm trong danh sách.' };

        const clickable = best.closest('.conv-item, [id^="friend-item-"], .msg-item, [role="listitem"], [tabindex]') || best;
        clickable.scrollIntoView({ block: 'center' });
        const rect = clickable.getBoundingClientRect();
        const opts = { bubbles: true, cancelable: true, clientX: rect.left + 30, clientY: rect.top + 15 };
        clickable.dispatchEvent(new MouseEvent('mousedown', opts));
        clickable.dispatchEvent(new MouseEvent('mouseup', opts));
        clickable.click();

        return { ok: true, message: 'Đã chọn nhóm: ' + target };
      } catch (e) {
        return { ok: false, message: String(e) };
      }
    })();
    """.Replace("__GROUP_NAME__", groupNameJson);

    private static string BuildTypeSearchBoxScript(string groupNameJson) => """
    (() => {
      try {
        const target = __GROUP_NAME__;
        const searchInput = document.querySelector('#contact-search-input, input[placeholder*="Tìm kiếm"], input[placeholder*="Search"]');
        if (!searchInput) return { ok: false, message: 'Không tìm thấy ô Tìm kiếm trên Zalo Web.' };

        searchInput.focus();
        searchInput.click();
        const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
        if (setter) setter.call(searchInput, target);
        else searchInput.value = target;

        searchInput.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: target }));
        searchInput.dispatchEvent(new Event('change', { bubbles: true }));
        return { ok: true, message: 'Đã nhập tên nhóm vào ô tìm kiếm.' };
      } catch (e) {
        return { ok: false, message: String(e) };
      }
    })();
    """.Replace("__GROUP_NAME__", groupNameJson);

    private static string BuildFillScript(string messageJson) => """
    (() => {
      try {
        const msg = __MESSAGE__;
        const visible = e => {
          if (!e) return false;
          const r = e.getBoundingClientRect();
          const s = getComputedStyle(e);
          return r.width > 20 && r.height > 10 && s.display !== 'none' &&
            s.visibility !== 'hidden' && Number(s.opacity || 1) > 0 && !e.disabled;
        };

        const zaloSelectors = [
          '#richInput',
          '[id^="input_line_"]',
          '.rich-input',
          '[data-translate-placeholder*="Nhập @"]',
          '[data-translate-placeholder*="tin nhắn"]',
          '[contenteditable="true"]',
          '[role="textbox"]',
          'textarea'
        ];

        let candidates = [];
        for (const sel of zaloSelectors) {
          for (const el of document.querySelectorAll(sel)) {
            if (visible(el) && !candidates.includes(el)) {
              const idAndClass = ((el.id || '') + ' ' + (el.className || '') + ' ' + (el.getAttribute('placeholder') || '')).toLowerCase();
              if (/contact-search|search-input|tìm kiếm/.test(idAndClass)) continue;
              candidates.push(el);
            }
          }
        }

        candidates.sort((a, b) => {
          const ar = a.getBoundingClientRect(), br = b.getBoundingClientRect();
          const aId = (a.id || '').includes('richInput') || (a.id || '').includes('input_line') ? 1000 : 0;
          const bId = (b.id || '').includes('richInput') || (b.id || '').includes('input_line') ? 1000 : 0;
          return (bId + br.bottom * 2 + br.width) - (aId + ar.bottom * 2 + ar.width);
        });

        let editor = candidates[0];
        if (!editor) {
          return {
            ok: false,
            message: 'Chưa nhận diện được ô nhập tin nhắn. Hãy mở một cuộc trò chuyện trên Zalo Web.'
          };
        }

        if (editor.closest('[contenteditable="true"]')) {
          editor = editor.closest('[contenteditable="true"]');
        }

        window.__zaloLastEditor = editor;
        editor.click();
        editor.focus();

        if (editor.isContentEditable || editor.getAttribute('contenteditable') === 'true') {
          const selection = window.getSelection();
          const range = document.createRange();
          range.selectNodeContents(editor);
          selection.removeAllRanges();
          selection.addRange(range);
          document.execCommand('delete', false, null);

          let pasted = false;
          try {
            const dt = new DataTransfer();
            dt.setData('text/plain', msg);
            const pasteEvent = new ClipboardEvent('paste', {
              bubbles: true,
              cancelable: true,
              clipboardData: dt
            });
            editor.dispatchEvent(pasteEvent);
            pasted = (editor.innerText || '').includes('http') || (editor.innerText || '').trim().length >= msg.length / 2;
          } catch {}

          if (!pasted) {
            try {
              document.execCommand('paste');
              pasted = (editor.innerText || '').includes('http') || (editor.innerText || '').trim().length >= msg.length / 2;
            } catch {}
          }

          if (!pasted) {
            const lines = msg.split('\n');
            for (let i = 0; i < lines.length; i++) {
              if (i > 0) {
                const shiftEnter = new KeyboardEvent('keydown', {
                  key: 'Enter', code: 'Enter', keyCode: 13, which: 13,
                  shiftKey: true, bubbles: true, cancelable: true
                });
                editor.dispatchEvent(shiftEnter);
                document.execCommand('insertLineBreak', false, null);
              }
              if (lines[i]) {
                document.execCommand('insertText', false, lines[i]);
              }
            }
          }

          editor.dispatchEvent(new Event('input', { bubbles: true }));
        } else {
          const proto = editor.tagName === 'TEXTAREA'
            ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
          const setter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;
          if (setter) setter.call(editor, msg); else editor.value = msg;
          editor.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: msg }));
          editor.dispatchEvent(new Event('change', { bubbles: true }));
        }

        return { ok: true, message: 'Đã tự động điền và gửi tin nhắn vào nhóm Zalo thành công!' };
      } catch (err) {
        return { ok: false, message: 'Lỗi điền nội dung trên Zalo Web: ' + (err && err.message ? err.message : String(err)) };
      }
    })();
    """.Replace("__MESSAGE__", messageJson);

    private static string BuildTriggerSendScript() => """
    (() => {
      try {
        const visible = e => {
          if (!e) return false;
          const r = e.getBoundingClientRect();
          const s = getComputedStyle(e);
          return r.width > 12 && r.height > 12 && s.display !== 'none' &&
            s.visibility !== 'hidden' && Number(s.opacity || 1) > 0 && !e.disabled;
        };

        const editor = window.__zaloLastEditor || document.querySelector('#richInput, [contenteditable="true"]');

        const sendSelectors = [
          '[data-translate-title="STR_SEND"]',
          '[title="Gửi tin nhắn"]',
          '[title="Send message"]',
          '.send-msg-btn',
          '.fa-Sent-msg_24_Line'
        ];

        let sendBtn = null;
        for (const sel of sendSelectors) {
          const found = document.querySelector(sel);
          if (found && visible(found)) {
            sendBtn = found.closest('div[role="button"], button, .z--btn--v2') || found;
            break;
          }
        }

        if (!sendBtn) {
          const allButtons = [...document.querySelectorAll('button, [role="button"], .z--btn--v2')].filter(visible);
          sendBtn = allButtons.find(b => {
            const meta = ((b.innerText || '') + ' ' + (b.title || '') + ' ' + (b.getAttribute('aria-label') || '') + ' ' + (b.getAttribute('data-translate-title') || '')).toLowerCase();
            return /str_send|gửi tin nhắn|send message|^gửi$|^send$/i.test(meta.trim());
          });
        }

        if (sendBtn) {
          sendBtn.click();
          return { ok: true, message: 'Đã tự động điền đầy đủ link và gửi vào nhóm Zalo thành công!' };
        }

        if (editor) {
          editor.focus();
          const enterDown = new KeyboardEvent('keydown', {
            key: 'Enter', code: 'Enter', keyCode: 13, which: 13,
            bubbles: true, cancelable: true
          });
          const enterPress = new KeyboardEvent('keypress', {
            key: 'Enter', code: 'Enter', keyCode: 13, which: 13,
            bubbles: true, cancelable: true
          });
          const enterUp = new KeyboardEvent('keyup', {
            key: 'Enter', code: 'Enter', keyCode: 13, which: 13,
            bubbles: true, cancelable: true
          });
          editor.dispatchEvent(enterDown);
          editor.dispatchEvent(enterPress);
          editor.dispatchEvent(enterUp);
        }

        return { ok: true, message: 'Đã tự động điền đầy đủ link và gửi vào nhóm Zalo thành công!' };
      } catch (err) {
        return { ok: false, message: 'Đã điền tin nhắn nhưng lỗi kích hoạt nút Gửi: ' + (err && err.message ? err.message : String(err)) };
      }
    })();
    """;
}
