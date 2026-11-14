using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;

namespace ShopeeAffiliateAgent;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

public sealed class VisibleProduct
{
    public string Name { get; set; } = "";
    public string ProductUrl { get; set; } = "";
}

public sealed class AgentLinkResult
{
    public bool Ok { get; set; }
    public string AffiliateUrl { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class ProductItem
{
    public int Number { get; set; }
    public string Name { get; set; } = "";
    public string ProductUrl { get; set; } = "";
    public string AffiliateUrl { get; set; } = "";
    public string AvailabilityStatus { get; set; } = "Chưa kiểm tra";
    public string Status => string.IsNullOrWhiteSpace(AffiliateUrl) ? "Chờ tạo link" :
        AvailabilityStatus == "Chưa kiểm tra" ? "Đã có link" : AvailabilityStatus;
}

public sealed class MainForm : Form
{
    private const int MaxProducts = 100;
    private readonly BindingList<ProductItem> items = new();
    private readonly BindingSource source = new();
    private readonly WebView2 browser = new();
    private readonly TextBox keyword = new();
    private readonly TextBox bulkProducts = new();
    private readonly TextBox bulkAffiliates = new();
    private readonly DataGridView table = new();
    private readonly Label status = new();
    private readonly string dataFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShopeeAffiliateAgent", "products-v3.json");
    private bool loading;
    private bool agentRunning;
    private bool autoAgentAfterNavigation;
    private bool autoExtractAfterSearch;
    private bool showUnavailableOnly;
    private bool availabilityCheckRunning;
    private const string CustomLinkUrl = "https://affiliate.shopee.vn/offer/custom_link";
    private readonly string temporaryBrowserProfile = Path.Combine(Path.GetTempPath(), "ShopeeAffiliateAgent", Guid.NewGuid().ToString("N"));

    public MainForm()
    {
        Text = "Shopee Affiliate Agent V3 - Safe Assisted Workflow";
        Width = 1580;
        Height = 980;
        MinimumSize = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.None,
            SplitterWidth = 6
        };
        Controls.Add(split);
        BuildWorkspace(split.Panel1);
        BuildBrowser(split.Panel2);

        status.Dock = DockStyle.Bottom;
        status.Height = 28;
        status.Padding = new Padding(8, 5, 0, 0);
        Controls.Add(status);

        source.DataSource = items;
        table.DataSource = source;
        LoadData();
        UpdateStatus();

        Shown += async (_, _) =>
        {
            try
            {
                // Apply splitter constraints only after the form has its real client size.
                BeginInvoke(new Action(() =>
                {
                    int available = split.ClientSize.Width;
                    if (available >= 1090)
                    {
                        split.Panel1MinSize = 540;
                        split.Panel2MinSize = 540;
                        split.SplitterDistance = Math.Clamp(available / 2, 540, available - split.Panel2MinSize - split.SplitterWidth);
                    }
                    else
                    {
                        split.Panel1MinSize = 0;
                        split.Panel2MinSize = 0;
                        split.SplitterDistance = Math.Max(200, available / 2);
                    }
                }));
                // Mỗi lần mở ứng dụng dùng một profile tạm hoàn toàn mới.
                // Phiên Shopee chỉ tồn tại trong phiên chạy hiện tại, không được ghi nhớ giữa các lần mở.
                var legacyProfile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ShopeeAffiliateAgent", "WebViewProfile");
                TryDeleteDirectory(legacyProfile);
                Directory.CreateDirectory(temporaryBrowserProfile);
                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, temporaryBrowserProfile);
                await browser.EnsureCoreWebView2Async(env);
                browser.CoreWebView2.NavigationCompleted += async (_, e) =>
                {
                    status.Text = e.IsSuccess ? "Trình duyệt sẵn sàng." : "Không tải được trang hiện tại.";
                    if (e.IsSuccess && autoExtractAfterSearch &&
                        browser.Source?.AbsolutePath.StartsWith("/search", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        autoExtractAfterSearch = false;
                        await Task.Delay(4500);
                        await ExtractVisibleProductsAsync(10, true);
                    }
                    if (e.IsSuccess && autoAgentAfterNavigation &&
                        browser.Source?.AbsolutePath.Equals("/offer/custom_link", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        autoAgentAfterNavigation = false;
                        await Task.Delay(1800);
                        RunAffiliateAgent(true);
                    }
                };
                browser.CoreWebView2.Navigate("https://affiliate.shopee.vn/");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không khởi tạo được WebView2: " + ex.Message, "Shopee Affiliate Agent",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        FormClosing += (_, _) => SaveData();
        FormClosed += (_, _) =>
        {
            try
            {
                browser.CoreWebView2?.Stop();
                browser.Dispose();
            }
            catch { }
            TryDeleteDirectory(temporaryBrowserProfile);
        };
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        // WebView2 đôi khi nhả file profile chậm khi đóng; thử xóa lại vài lần.
        for (int attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch
            {
                Thread.Sleep(250);
            }
        }
    }

    private void BuildWorkspace(Control panel)
    {
        panel.Padding = new Padding(12);
        var heading = new Label
        {
            Text = "SHOPEE AFFILIATE AGENT V4",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 42
        };
        panel.Controls.Add(heading);

        panel.Controls.Add(LabelAt("1. Tìm sản phẩm theo từ khóa", 48));
        keyword.SetBounds(12, 76, 245, 30);
        keyword.PlaceholderText = "Ví dụ: bàn phím cơ, RTX 5060...";
        panel.Controls.Add(keyword);
        AddButton(panel, "Tìm từ khóa", 265, 75, 145, SearchShopee);
        AddButton(panel, "Lấy sản phẩm đang hiển thị", 418, 75, 172, ExtractVisibleProducts);

        AddButton(panel, "Mở Shopee Affiliate", 12, 114, 280, () => Navigate("https://affiliate.shopee.vn/"));
        AddButton(panel, "Mở Custom Link", 302, 114, 288, () => Navigate(CustomLinkUrl));
        AddButton(panel, "AI Agent: tự tạo link Affiliate", 12, 158, 578, () => RunAffiliateAgent());

        panel.Controls.Add(LabelAt("2. Danh sách sản phẩm (tối đa 100)", 200));
        AddButton(panel, "Kiểm tra sản phẩm không thể mua", 12, 224, 578, CheckAvailabilityForAll);
        table.SetBounds(12, 262, panel.ClientSize.Width - 24, 563);
        table.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        table.ReadOnly = true;
        table.AllowUserToAddRows = false;
        table.MultiSelect = true;
        table.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        table.AutoGenerateColumns = false;
        table.RowHeadersVisible = false;
        table.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        table.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ProductItem.Number), HeaderText = "STT", FillWeight = 35 });
        table.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ProductItem.Name), HeaderText = "Sản phẩm", FillWeight = 90 });
        table.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ProductItem.ProductUrl), HeaderText = "Link gốc", FillWeight = 125 });
        table.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ProductItem.AffiliateUrl), HeaderText = "Link Affiliate", FillWeight = 125 });
        table.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ProductItem.Status), HeaderText = "Trạng thái", FillWeight = 65 });
        panel.Controls.Add(table);

        var openButton = AddButton(panel, "Mở link gốc đã chọn", 12, 835, 185, OpenSelectedProduct);
        var copyButton = AddButton(panel, "Sao chép link Affiliate", 205, 835, 185, () => CopySelected(p => p.AffiliateUrl));
        var exportButton = AddButton(panel, "Xuất CSV", 398, 835, 192, ExportCsv);
        var filterButton = AddButton(panel, "Chỉ hiện sản phẩm không thể mua", 12, 870, 280, ToggleUnavailableFilter);
        var exportUnavailableButton = AddButton(panel, "Xuất DS không thể mua", 302, 870, 288, ExportUnavailableCsv);
        var deleteSelectedButton = AddButton(panel, "Xóa sản phẩm đã chọn", 12, 905, 578, DeleteSelectedProducts);

        var note = new Label
        {
            Text = "Tự động tìm sản phẩm, kiểm tra khả năng mua và chỉ tạo link Affiliate cho sản phẩm còn mua được. CAPTCHA/OTP sẽ yêu cầu bạn xử lý.",
            Left = 12, Top = 906, Width = panel.ClientSize.Width - 24, Height = 45,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = Color.DarkSlateGray
        };
        panel.Controls.Add(note);

        panel.Resize += (_, _) =>
        {
            int contentWidth = Math.Max(200, panel.ClientSize.Width - 24);
            keyword.Width = Math.Max(180, contentWidth - 365);
            var searchButton = panel.Controls.OfType<Button>().FirstOrDefault(b => b.Text == "Tìm từ khóa");
            var extractButton = panel.Controls.OfType<Button>().FirstOrDefault(b => b.Text == "Lấy sản phẩm đang hiển thị");
            if (searchButton is not null) { searchButton.Left = keyword.Right + 8; searchButton.Width = 130; }
            if (extractButton is not null) { extractButton.Left = searchButton?.Right + 8 ?? 0; extractButton.Width = Math.Max(130, panel.ClientSize.Width - extractButton.Left - 12); }

            foreach (var button in panel.Controls.OfType<Button>())
            {
                if (button.Text == "AI Agent: tự tạo link Affiliate" || button.Text == "Kiểm tra sản phẩm không thể mua")
                { button.Left = 12; button.Width = contentWidth; }
                else if (button.Text == "Mở Shopee Affiliate")
                { button.Left = 12; button.Width = (contentWidth - 10) / 2; }
                else if (button.Text == "Mở Custom Link")
                { button.Left = 22 + (contentWidth - 10) / 2; button.Width = (contentWidth - 10) / 2; }
            }

            table.Width = contentWidth;
            int actionY = panel.ClientSize.Height - 180;
            table.Height = Math.Max(260, actionY - table.Top - 10);
            int third = (contentWidth - 20) / 3;
            openButton.SetBounds(12, actionY, third, 32);
            copyButton.SetBounds(18 + third, actionY, third, 32);
            exportButton.SetBounds(24 + 2 * third, actionY, third, 32);
            int half = (contentWidth - 10) / 2;
            filterButton.SetBounds(12, actionY + 35, half, 32);
            exportUnavailableButton.SetBounds(22 + half, actionY + 35, half, 32);
            deleteSelectedButton.SetBounds(12, actionY + 70, contentWidth, 32);
            note.Top = actionY + 107;
            note.Width = contentWidth;
        };
    }

    private void BuildBrowser(Control panel)
    {
        browser.Dock = DockStyle.Fill;
        panel.Controls.Add(browser);
    }

    private Label LabelAt(string text, int top) => new()
    {
        Text = text, Left = 12, Top = top, Width = 570, Height = 24,
        Font = new Font("Segoe UI", 9, FontStyle.Bold)
    };

    private Button AddButton(Control parent, string text, int x, int y, int width, Action action)
    {
        var button = new Button { Text = text, Left = x, Top = y, Width = width, Height = 32 };
        button.Click += (_, _) => action();
        parent.Controls.Add(button);
        return button;
    }

    private void Navigate(string url)
    {
        if (browser.CoreWebView2 is null)
        {
            MessageBox.Show("Trình duyệt đang khởi tạo, vui lòng đợi.");
            return;
        }
        browser.CoreWebView2.Navigate(url);
    }

    private void SearchShopee()
    {
        var term = keyword.Text.Trim();
        if (term.Length == 0) { MessageBox.Show("Nhập từ khóa tìm kiếm."); return; }
        autoExtractAfterSearch = true;
        Navigate("https://shopee.vn/search?keyword=" + Uri.EscapeDataString(term));
        status.Text = "Đang tìm Shopee; sẽ tự lấy 10 link sản phẩm đầu tiên...";
    }

    private async void ExtractVisibleProducts() => await ExtractVisibleProductsAsync(10, false);

    private async Task ExtractVisibleProductsAsync(int limit, bool silent)
    {
        if (browser.CoreWebView2 is null)
        {
            if (!silent) MessageBox.Show("Trình duyệt chưa sẵn sàng.");
            return;
        }

        const string scriptTemplate = """
        (() => {
          const found = [];
          const seen = new Set();
          const anchors = [...document.querySelectorAll('a[href]')];
          for (const a of anchors) {
            let url;
            try { url = new URL(a.getAttribute('href'), location.href); } catch { continue; }
            if (!/(^|\.)shopee\.vn$/i.test(url.hostname)) continue;
            const path = decodeURIComponent(url.pathname);
            const productLike = /\/product\/\d+\/\d+/i.test(path) || /-i\.\d+\.\d+/i.test(path);
            if (!productLike) continue;
            const href = url.origin + url.pathname;
            if (seen.has(href)) continue;
            const card = a.closest('[data-sqe="item"], [data-testid*="item"], li') || a.parentElement?.parentElement || a.parentElement;
            const name = ((card?.innerText || a.innerText || a.getAttribute('aria-label') || '').replace(/\s+/g, ' ').trim()).slice(0, 240);
            seen.add(href);
            found.push({ name: name || 'Sản phẩm Shopee', productUrl: href });
            if (found.length >= __LIMIT__) break;
          }
          return { products: found, anchorCount: anchors.length,
            sample: anchors.slice(0, 12).map(a => a.getAttribute('href')).filter(Boolean) };
        })();
        """;

        var script = scriptTemplate.Replace("__LIMIT__", limit.ToString(System.Globalization.CultureInfo.InvariantCulture));

        try
        {
            // Shopee renders search results asynchronously; retry briefly while cards appear.
            List<VisibleProduct> products = new();
            for (int attempt = 0; attempt < 8 && products.Count == 0; attempt++)
            {
                if (attempt > 0) await Task.Delay(1000);
                var raw = await browser.CoreWebView2.ExecuteScriptAsync(script);
                using var resultDoc = JsonDocument.Parse(raw);
                var resultRoot = resultDoc.RootElement;
                if (resultRoot.TryGetProperty("products", out var productArray))
                    products = JsonSerializer.Deserialize<List<VisibleProduct>>(productArray.GetRawText(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            }
            if (products.Count == 0)
            {
                if (!silent) MessageBox.Show("Không tìm thấy link sản phẩm hợp lệ. Hãy kiểm tra trang kết quả đã tải xong chưa.");
                status.Text = "Không tìm thấy link sản phẩm hợp lệ trên trang kết quả Shopee.";
                return;
            }

            int added = 0, skipped = 0;
            foreach (var product in products.Take(limit))
            {
                if (!IsProductUrl(product.ProductUrl) || items.Count >= MaxProducts ||
                    items.Any(p => string.Equals(p.ProductUrl, product.ProductUrl, StringComparison.OrdinalIgnoreCase)))
                {
                    skipped++;
                    continue;
                }
                items.Add(new ProductItem
                {
                    Number = items.Count + 1,
                    Name = product.Name,
                    ProductUrl = product.ProductUrl
                });
                added++;
            }
            RefreshTable();
            SaveData();
            UpdateStatus();
            if (added > 0)
            {
                autoAgentAfterNavigation = true;
                status.Text = $"Đã tự lấy {added} link sản phẩm đầu tiên. Đang mở Custom Link để tạo Affiliate...";
                Navigate(CustomLinkUrl);
            }
            else if (!silent)
            {
                MessageBox.Show($"Đã đọc {products.Count} link đầu tiên nhưng không có sản phẩm mới (có thể đã tồn tại trong danh sách).");
            }
            else
            {
                status.Text = $"Đã tìm thấy {products.Count} link đầu tiên; không có link mới để thêm.";
            }
        }
        catch (Exception ex)
        {
            if (!silent) MessageBox.Show("Không đọc được danh sách sản phẩm: " + ex.Message);
            status.Text = "Lỗi lấy link sản phẩm: " + ex.Message;
        }
    }

    private void AddCurrentPage()
    {
        var current = browser.Source?.ToString() ?? "";
        if (!IsProductUrl(current))
        {
            MessageBox.Show("Hãy mở một trang sản phẩm Shopee trước. Không thể thêm trang tìm kiếm hoặc trang Affiliate.");
            return;
        }
        AddProducts(new[] { current });
    }

    private void AddBulkProducts()
    {
        var urls = SplitLines(bulkProducts.Text);
        AddProducts(urls);
        bulkProducts.Clear();
    }

    private void AddProducts(IEnumerable<string> urls)
    {
        int added = 0, skipped = 0;
        foreach (var raw in urls)
        {
            var url = raw.Trim();
            if (url.Length == 0) continue;
            if (!IsProductUrl(url) || items.Count >= MaxProducts ||
                items.Any(p => string.Equals(p.ProductUrl, url, StringComparison.OrdinalIgnoreCase)))
            {
                skipped++;
                continue;
            }
            items.Add(new ProductItem { Number = items.Count + 1, ProductUrl = url });
            added++;
        }
        SaveData();
        UpdateStatus();
        MessageBox.Show($"Đã thêm {added} sản phẩm. Bỏ qua {skipped} dòng không hợp lệ, trùng hoặc vượt giới hạn 100.");
    }

    private async void RunAffiliateAgent(bool silent = false)
    {
        if (agentRunning) return;
        if (browser.CoreWebView2 is null)
        {
            MessageBox.Show("Trình duyệt chưa sẵn sàng.");
            return;
        }

        var pending = items.Where(p => string.IsNullOrWhiteSpace(p.AffiliateUrl)).ToList();
        if (pending.Count == 0)
        {
            MessageBox.Show("Không còn sản phẩm nào đang chờ tạo link.");
            return;
        }

        if (!silent)
        {
            var confirm = MessageBox.Show(
                $"Agent sẽ lần lượt điền {pending.Count} URL vào trang Custom Link và bấm nút lấy link trên giao diện Shopee. Tiếp tục?",
                "Xác nhận chạy Affiliate Agent", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;
        }

        if (!string.Equals(browser.Source?.AbsolutePath, "/offer/custom_link", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Chưa ở đúng trang Custom Link. Agent sẽ mở trang chính thức rồi bạn hãy chạy lại.");
            Navigate(CustomLinkUrl);
            return;
        }

        agentRunning = true;
        int completed = 0;
        int removedUnavailable = 0;
        int skippedUnknown = 0;
        try
        {
            // Pre-check every queued product before generating any affiliate link.
            var buyableProducts = new List<ProductItem>();
            for (int i = 0; i < pending.Count; i++)
            {
                var product = pending[i];
                status.Text = $"Kiểm tra khả năng mua {i + 1}/{pending.Count}: {product.Name}";
                try
                {
                    product.AvailabilityStatus = await CheckProductAvailabilityAsync(product.ProductUrl);
                }
                catch
                {
                    product.AvailabilityStatus = "Không xác định - Lỗi tải";
                }
                if (IsUnavailable(product.AvailabilityStatus))
                {
                    items.Remove(product);
                    removedUnavailable++;
                }
                else if (product.AvailabilityStatus == "Có thể mua")
                {
                    buyableProducts.Add(product);
                }
                else
                {
                    skippedUnknown++;
                }
                RefreshTable();
                SaveData();
            }
            Renumber();
            RefreshTable();
            SaveData();

            if (buyableProducts.Count == 0)
            {
                MessageBox.Show($"Không tạo link nào. Đã tự loại {removedUnavailable} sản phẩm không thể mua; {skippedUnknown} sản phẩm chưa xác định được giữ lại để kiểm tra sau.");
                return;
            }

            await NavigateAndWaitAsync(CustomLinkUrl, 30000);
            await Task.Delay(1200);
            pending = buyableProducts;

            foreach (var product in pending)
            {
                status.Text = $"Agent đang tạo link {completed + 1}/{pending.Count}: {product.Name}";
                var input = JsonSerializer.Serialize(product.ProductUrl);
                const string submitScript = """
                (() => {
                  const visible = e => !!(e && (e.offsetWidth || e.offsetHeight || e.getClientRects().length));
                  const norm = s => (s || '').toLowerCase().replace(/\s+/g, ' ').trim();
                  const fields = [...document.querySelectorAll('textarea,input:not([type=hidden])')]
                    .filter(visible).filter(e => !e.disabled && !e.readOnly);
                  const scored = fields.map(e => {
                    const meta = norm([e.placeholder, e.getAttribute('aria-label'), e.name, e.id,
                      e.parentElement?.innerText, e.labels ? [...e.labels].map(x => x.innerText).join(' ') : ''].join(' '));
                    let score = 0;
                    // Shopee Custom Link currently labels its main URL box with this English placeholder.
                    if (e.tagName === 'TEXTAREA' && /(convert multiple links|input up to 5 links|shopee\\.vn)/i.test(meta)) score = 100;
                    else if (e.tagName === 'TEXTAREA') score = 20;
                    else if (/(product|商品|sản phẩm|url|link|đường dẫn)/i.test(meta)) score = 10;
                    return { e, score };
                  }).sort((a,b) => b.score-a.score);
                  const field = scored[0]?.e;
                  if (!field || (scored[0].score === 0 && fields.length !== 1))
                    return { ok:false, message:'Không tìm thấy ô nhập URL Custom Link. Hãy kiểm tra trang Shopee đã tải đầy đủ.' };
                  const setter = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(field), 'value')?.set;
                  if (setter) setter.call(field, __PRODUCT_URL_JSON__); else field.value = __PRODUCT_URL_JSON__;
                  field.dispatchEvent(new Event('input', { bubbles:true }));
                  field.dispatchEvent(new Event('change', { bubbles:true }));
                  const buttons = [...document.querySelectorAll('button,[role=button],input[type=submit]')]
                    .filter(visible).filter(e => !e.disabled);
                  const matches = buttons.filter(e => /(get link|generate|create link|create|tạo link|lấy link|tạo liên kết|lấy liên kết)/i.test(norm(e.innerText || e.value || e.getAttribute('aria-label'))));
                  if (matches.length !== 1)
                    return { ok:false, message:'Không tìm thấy duy nhất một nút tạo link phù hợp. Agent dừng để tránh bấm nhầm.' };
                  matches[0].click();
                  return { ok:true, message:'Đã gửi yêu cầu tạo link; đang chờ kết quả Shopee.' };
                })();
                """;
                var safeProductUrl = JsonSerializer.Serialize(product.ProductUrl);
                var raw = await browser.CoreWebView2.ExecuteScriptAsync(
                    submitScript.Replace("__PRODUCT_URL_JSON__", safeProductUrl));
                var submit = JsonSerializer.Deserialize<AgentLinkResult>(raw,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (submit is null || !submit.Ok)
                {
                    MessageBox.Show($"Agent đã dừng ở sản phẩm {completed + 1}/{pending.Count}.\\n{product.Name}\\n{submit?.Message ?? "Không nhận được xác nhận từ trang Custom Link."}\\n\\nCác link đã tạo trước đó đã được lưu; sản phẩm này chưa được đánh dấu hoàn tất.");
                    break;
                }

                AgentLinkResult? result = null;
                for (int attempt = 0; attempt < 36; attempt++)
                {
                    await Task.Delay(500);
                    const string readResultScript = """
                    (() => {
                      const visible = e => !!(e && (e.offsetWidth || e.offsetHeight || e.getClientRects().length));
                      const normalize = s => (s || '').trim().replace(/[\s<>"')\],.]+$/g, '');
                      const isShort = s => {
                        try {
                          const u = new URL(normalize(s));
                          return u.protocol === 'https:' && (u.hostname === 's.shopee.vn' || u.hostname === 'shope.ee');
                        } catch { return false; }
                      };
                      const nodes = [...document.querySelectorAll('body *')].filter(visible);
                      const modal = nodes.find(e => {
                        const t = (e.innerText || '').toLowerCase();
                        return t.includes('link of custom link') && t.length < 10000;
                      });
                      const root = modal || document.body;
                      const raw = [
                        ...[...root.querySelectorAll('input,textarea')].map(e => e.value || e.getAttribute('value') || e.innerText || e.textContent || ''),
                        ...[...root.querySelectorAll('a[href]')].map(e => e.href || ''),
                        root.innerText || root.textContent || ''
                      ].join('\n');
                      const urls = [...new Set((raw.match(/https?:\/\/[^\s"'<>]+/gi) || []).map(normalize).filter(isShort))];
                      if (urls.length !== 1) return { ok:false, affiliateUrl:'', message: urls.length > 1 ? 'Có nhiều link trong kết quả, cần kiểm tra thủ công.' : 'Đang chờ cửa sổ Link of Custom Link.' };
                      const close = [...root.querySelectorAll('button,[role=button],[aria-label],[title]')].find(e => {
                        const t = ((e.innerText || '') + ' ' + (e.getAttribute('aria-label') || '') + ' ' + (e.title || '')).toLowerCase();
                        return visible(e) && (t.includes('close') || t.includes('đóng'));
                      });
                      if (close) close.click();
                      return { ok:true, affiliateUrl:urls[0], message:'Đã đọc link từ cửa sổ Link of Custom Link.' };
                    })();
                    """;
                    var readRaw = await browser.CoreWebView2.ExecuteScriptAsync(readResultScript);
                    result = JsonSerializer.Deserialize<AgentLinkResult>(readRaw,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (result?.Ok == true && IsAffiliateUrl(result.AffiliateUrl)) break;
                }
                if (result is null || !result.Ok || !IsAffiliateUrl(result.AffiliateUrl))
                {
                    MessageBox.Show($"Agent đã dừng ở sản phẩm {completed + 1}/{pending.Count}.\n{product.Name}\n{result?.Message ?? "Không đọc được kết quả."}\n\nCác link đã tạo trước đó đã được lưu; sản phẩm này chưa được đánh dấu hoàn tất.");
                    break;
                }

                product.AffiliateUrl = result.AffiliateUrl;
                completed++;
                RefreshTable();
                SaveData();
                UpdateStatus();
                await Task.Delay(700);
            }

            if (completed == pending.Count)
                MessageBox.Show($"Hoàn tất! Đã tạo {completed} link cho sản phẩm mua được. Tự loại {removedUnavailable} sản phẩm không thể mua; giữ lại {skippedUnknown} sản phẩm chưa xác định.");
            else if (completed > 0)
                MessageBox.Show($"Đã tạo {completed}/{pending.Count} link cho sản phẩm mua được. Tự loại {removedUnavailable} sản phẩm không thể mua; giữ lại {skippedUnknown} sản phẩm chưa xác định.");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Affiliate Agent gặp lỗi và đã dừng: " + ex.Message);
        }
        finally
        {
            agentRunning = false;
            UpdateStatus();
        }
    }

    private void AttachBulkAffiliates()
    {
        var urls = SplitLines(bulkAffiliates.Text).Where(x => x.Length > 0).ToList();
        if (urls.Count == 0) { MessageBox.Show("Dán ít nhất một link Affiliate."); return; }

        var pending = items.Where(p => string.IsNullOrWhiteSpace(p.AffiliateUrl)).ToList();
        if (urls.Any(url => !IsAffiliateUrl(url)))
        {
            MessageBox.Show("Có dòng không phải link Shopee HTTPS hợp lệ. Chưa gắn link nào; hãy sửa danh sách rồi thử lại.");
            return;
        }
        if (urls.Count > pending.Count)
        {
            MessageBox.Show($"Có {pending.Count} sản phẩm đang chờ nhưng bạn dán {urls.Count} link. Chưa gắn link nào để tránh lệch thứ tự.");
            return;
        }
        int assigned = 0;
        foreach (var url in urls)
        {
            pending[assigned].AffiliateUrl = url;
            assigned++;
        }
        RefreshTable();
        SaveData();
        UpdateStatus();
        bulkAffiliates.Clear();
        MessageBox.Show($"Đã gắn {assigned} link theo thứ tự các sản phẩm đang chờ. Vui lòng kiểm tra lại từng cặp link trước khi sử dụng.");
    }

    private void DeleteSelectedProducts()
    {
        var selected = table.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => row.DataBoundItem as ProductItem)
            .Where(item => item is not null)
            .Cast<ProductItem>()
            .Distinct()
            .ToList();

        if (selected.Count == 0)
        {
            MessageBox.Show("Bạn hãy chọn một hoặc nhiều sản phẩm trong danh sách trước khi xóa.",
                "Chưa chọn sản phẩm", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Bạn có chắc muốn xóa {selected.Count} sản phẩm đã chọn khỏi danh sách?\\nThao tác này không thể hoàn tác.",
            "Xác nhận xóa sản phẩm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        if (confirm != DialogResult.Yes) return;

        foreach (var item in selected) items.Remove(item);
        Renumber();
        RefreshTable();
        SaveData();
        UpdateStatus();
        status.Text = $"Đã xóa {selected.Count} sản phẩm khỏi danh sách.";
    }

    private void RemoveSelected()
    {
        if (table.CurrentRow?.DataBoundItem is not ProductItem item) return;
        items.Remove(item);
        Renumber();
        RefreshTable();
        SaveData();
        UpdateStatus();
    }

    private void Renumber()
    {
        for (int i = 0; i < items.Count; i++) items[i].Number = i + 1;
    }

    private void RefreshTable()
    {
        source.ResetBindings(false);
        table.Refresh();
        ApplyUnavailableFilter();
    }

    private void ToggleUnavailableFilter()
    {
        showUnavailableOnly = !showUnavailableOnly;
        ApplyUnavailableFilter();
        foreach (Control control in Controls)
        {
            // Buttons live inside the left split panel; update the filter toggle label there.
        }
        var leftPanel = Controls.OfType<SplitContainer>().FirstOrDefault()?.Panel1;
        var filterButton = leftPanel?.Controls.OfType<Button>().FirstOrDefault(b => b.Text.Contains("sản phẩm không thể mua"));
        if (filterButton is not null)
            filterButton.Text = showUnavailableOnly ? "Hiện tất cả sản phẩm" : "Chỉ hiện sản phẩm không thể mua";
    }

    private void ApplyUnavailableFilter()
    {
        foreach (DataGridViewRow row in table.Rows)
        {
            if (row.DataBoundItem is ProductItem item)
                row.Visible = !showUnavailableOnly || IsUnavailable(item.AvailabilityStatus);
        }
    }

    private static bool IsUnavailable(string value) =>
        value.StartsWith("Không thể mua", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("Hết hàng", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("Ngừng bán", StringComparison.OrdinalIgnoreCase);

    private async void CheckAvailabilityForAll()
    {
        if (availabilityCheckRunning || agentRunning) return;
        if (browser.CoreWebView2 is null)
        {
            MessageBox.Show("Trình duyệt chưa sẵn sàng.");
            return;
        }
        var targets = items.Where(p => !string.IsNullOrWhiteSpace(p.ProductUrl)).ToList();
        if (targets.Count == 0) { MessageBox.Show("Danh sách chưa có sản phẩm."); return; }
        var confirm = MessageBox.Show(
            $"Agent sẽ mở lần lượt {targets.Count} link sản phẩm trên Shopee để kiểm tra còn mua được, hết hàng hay đã ngừng bán. Quá trình có thể mất vài phút. Tiếp tục?",
            "Kiểm tra khả năng mua", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        availabilityCheckRunning = true;
        int checkedCount = 0, unavailable = 0, unknown = 0;
        try
        {
            foreach (var product in targets)
            {
                status.Text = $"Đang kiểm tra {checkedCount + 1}/{targets.Count}: {product.Name}";
                try
                {
                    await NavigateAndWaitAsync(product.ProductUrl, 25000);
                    await Task.Delay(1300);
                    const string inspectScript = """
                    (() => {
                      const text = (document.body?.innerText || '').toLowerCase().replace(/\s+/g, ' ');
                      const title = (document.title || '').toLowerCase();
                      const has = terms => terms.some(t => text.includes(t) || title.includes(t));
                      if (has(['sản phẩm không tồn tại','không tìm thấy sản phẩm','sản phẩm này không tồn tại','sản phẩm đã bị xóa','sản phẩm đã bị ẩn','this product is unavailable','product not found','item not found','page not found']))
                        return { status:'Không thể mua - Bị ẩn/xóa', detail:'Trang Shopee báo sản phẩm không tồn tại hoặc không khả dụng.' };
                      if (has(['ngừng kinh doanh','ngừng bán','shop đã đóng cửa','shop is closed','temporarily unavailable','tạm ngừng bán']))
                        return { status:'Ngừng bán', detail:'Trang Shopee có dấu hiệu sản phẩm/shop ngừng bán.' };
                      if (has(['hết hàng','đã bán hết','sold out','out of stock','tạm hết hàng']))
                        return { status:'Hết hàng', detail:'Trang Shopee hiển thị hết hàng.' };
                      const buttons = [...document.querySelectorAll('button,[role=button]')].filter(e => {
                        const r=e.getBoundingClientRect(); return r.width>0 && r.height>0;
                      });
                      const buy = buttons.some(e => /mua ngay|thêm vào giỏ|buy now|add to cart/i.test(e.innerText || e.getAttribute('aria-label') || '') && !e.disabled && e.getAttribute('aria-disabled')!=='true');
                      const bodyReady = text.length > 250;
                      if (buy) return { status:'Có thể mua', detail:'Tìm thấy nút mua/thêm giỏ đang hoạt động.' };
                      if (!bodyReady || has(['đang tải','loading...','access denied','captcha','verify you are human']))
                        return { status:'Không xác định - Lỗi tải', detail:'Trang chưa tải đủ hoặc Shopee yêu cầu xác minh.' };
                      return { status:'Không xác định - Cần kiểm tra', detail:'Không đủ dấu hiệu chắc chắn để kết luận còn hàng hay không.' };
                    })();
                    """;
                    var raw = await browser.CoreWebView2.ExecuteScriptAsync(inspectScript);
                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;
                    product.AvailabilityStatus = root.TryGetProperty("status", out var st) ? st.GetString() ?? "Không xác định - Cần kiểm tra" : "Không xác định - Cần kiểm tra";
                    if (IsUnavailable(product.AvailabilityStatus)) unavailable++;
                    if (product.AvailabilityStatus.StartsWith("Không xác định", StringComparison.OrdinalIgnoreCase)) unknown++;
                }
                catch
                {
                    product.AvailabilityStatus = "Không xác định - Lỗi tải";
                    unknown++;
                }
                checkedCount++;
                RefreshTable();
                SaveData();
                await Task.Delay(500);
            }
            // Remove products confirmed hidden, deleted, out of stock, or discontinued.
            var removedItems = items.Where(p => IsUnavailable(p.AvailabilityStatus)).ToList();
            foreach (var item in removedItems) items.Remove(item);
            Renumber();
            showUnavailableOnly = false;
            RefreshTable();
            SaveData();
            int buyable = items.Count(p => p.AvailabilityStatus == "Có thể mua" && string.IsNullOrWhiteSpace(p.AffiliateUrl));
            int alreadyLinked = items.Count(p => p.AvailabilityStatus == "Có thể mua" && !string.IsNullOrWhiteSpace(p.AffiliateUrl));
            MessageBox.Show($"Đã kiểm tra {checkedCount}/{targets.Count} sản phẩm.\\nĐã loại khỏi danh sách {removedItems.Count} sản phẩm bị ẩn/xóa, hết hàng hoặc ngừng bán.\\nCòn lại {items.Count} sản phẩm; {buyable} sản phẩm mua được đang chờ tạo link, {alreadyLinked} sản phẩm đã có link và {unknown} sản phẩm chưa xác định.\\nAgent sẽ tự chuyển sang Custom Link và tạo link cho các sản phẩm mua được.");
            if (buyable > 0)
            {
                autoAgentAfterNavigation = true;
                Navigate(CustomLinkUrl);
            }
            else
            {
                Navigate(CustomLinkUrl);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Không thể hoàn tất kiểm tra: " + ex.Message);
        }
        finally
        {
            availabilityCheckRunning = false;
            UpdateStatus();
        }
    }

    private async Task<string> CheckProductAvailabilityAsync(string productUrl)
    {
        await NavigateAndWaitAsync(productUrl, 25000);
        await Task.Delay(1400);
        const string script = """
        (() => {
          const text = (document.body?.innerText || '').toLowerCase().replace(/\s+/g, ' ');
          const title = (document.title || '').toLowerCase();
          const has = terms => terms.some(t => text.includes(t) || title.includes(t));
          if (has(['sản phẩm không tồn tại','không tìm thấy sản phẩm','sản phẩm này không tồn tại','sản phẩm đã bị xóa','sản phẩm đã bị ẩn','this product is unavailable','product not found','item not found','page not found']))
            return 'Không thể mua - Bị ẩn/xóa';
          if (has(['ngừng kinh doanh','ngừng bán','shop đã đóng cửa','shop is closed','temporarily unavailable','tạm ngừng bán']))
            return 'Ngừng bán';
          if (has(['hết hàng','đã bán hết','sold out','out of stock','tạm hết hàng']))
            return 'Hết hàng';
          const visible = e => { const r=e.getBoundingClientRect(); return r.width>0 && r.height>0; };
          const buttons = [...document.querySelectorAll('button,[role=button]')].filter(visible);
          const buy = buttons.some(e => /mua ngay|thêm vào giỏ|buy now|add to cart/i.test(e.innerText || e.getAttribute('aria-label') || '') && !e.disabled && e.getAttribute('aria-disabled')!=='true');
          if (buy) return 'Có thể mua';
          if (text.length < 250 || /access denied|captcha|verify you are human|đang tải|loading/.test(text))
            return 'Không xác định - Lỗi tải';
          return 'Không xác định - Cần kiểm tra';
        })();
        """;
        var raw = await browser.CoreWebView2.ExecuteScriptAsync(script);
        return JsonSerializer.Deserialize<string>(raw) ?? "Không xác định - Cần kiểm tra";
    }

    private async Task NavigateAndWaitAsync(string url, int timeoutMs)
    {
        var core = browser.CoreWebView2 ?? throw new InvalidOperationException("WebView2 chưa sẵn sàng.");
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            core.NavigationCompleted -= Handler;
            tcs.TrySetResult(e.IsSuccess);
        }
        core.NavigationCompleted += Handler;
        core.Navigate(url);
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
        core.NavigationCompleted -= Handler;
        if (completed != tcs.Task || !await tcs.Task)
            throw new TimeoutException("Trang sản phẩm tải quá lâu hoặc bị chặn.");
    }

    private void ExportUnavailableCsv()
    {
        var matches = items.Where(p => IsUnavailable(p.AvailabilityStatus)).ToList();
        if (matches.Count == 0)
        {
            MessageBox.Show("Chưa có sản phẩm nào được xác định là không thể mua. Hãy chạy kiểm tra trước.");
            return;
        }
        using var dialog = new SaveFileDialog { Filter = "CSV UTF-8 (*.csv)|*.csv", FileName = "Shopee_San_Pham_Khong_The_Mua.csv" };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        var csv = new StringBuilder();
        csv.AppendLine("STT,Ten san pham,Product URL,Affiliate URL,Trang thai");
        foreach (var p in matches)
            csv.AppendLine(string.Join(",", new[] { CsvSafe(p.Number.ToString()), CsvSafe(p.Name),
                CsvSafe(p.ProductUrl), CsvSafe(p.AffiliateUrl), CsvSafe(p.AvailabilityStatus) }));
        File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true));
        MessageBox.Show($"Đã xuất {matches.Count} sản phẩm không thể mua.");
    }

    private void CopySelected(Func<ProductItem, string> selector)
    {
        if (table.CurrentRow?.DataBoundItem is not ProductItem item) return;
        var value = selector(item);
        if (string.IsNullOrWhiteSpace(value)) { MessageBox.Show("Sản phẩm chưa có link tương ứng."); return; }
        Clipboard.SetText(value);
        status.Text = "Đã sao chép link vào clipboard.";
    }

    private void OpenSelectedProduct()
    {
        if (table.CurrentRow?.DataBoundItem is ProductItem item && IsProductUrl(item.ProductUrl))
            Navigate(item.ProductUrl);
    }

    private void ExportCsv()
    {
        var checkedProducts = items.Where(p => p.AvailabilityStatus != "Chưa kiểm tra").ToList();
        var exportItems = checkedProducts.Count > 0
            ? items.Where(p => p.AvailabilityStatus == "Có thể mua").ToList()
            : items.ToList();
        if (exportItems.Count == 0)
        {
            MessageBox.Show("Chưa có sản phẩm nào được xác nhận có thể mua. Hãy chạy kiểm tra khả năng mua trước khi xuất.");
            return;
        }
        using var dialog = new SaveFileDialog { Filter = "CSV UTF-8 (*.csv)|*.csv", FileName = "Shopee_Affiliate_Link_Co_The_Mua.csv" };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        var csv = new StringBuilder();
        csv.AppendLine("STT,Ten san pham,Product URL,Affiliate URL,Trang thai");
        foreach (var p in exportItems)
            csv.AppendLine(string.Join(",", new[] {
                CsvSafe(p.Number.ToString()), CsvSafe(p.Name), CsvSafe(p.ProductUrl),
                CsvSafe(p.AffiliateUrl), CsvSafe(p.AvailabilityStatus == "Chưa kiểm tra" ? p.Status : p.AvailabilityStatus)
            }));
        File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true));
        MessageBox.Show($"Đã xuất {exportItems.Count} sản phẩm" +
            (checkedProducts.Count > 0 ? " đã xác nhận có thể mua." : ". Hãy kiểm tra khả năng mua để loại trừ sản phẩm hết hàng/ngừng bán."));
    }
    private void LoadData()
    {
        try
        {
            if (!File.Exists(dataFile)) return;
            var saved = JsonSerializer.Deserialize<List<ProductItem>>(File.ReadAllText(dataFile)) ?? new();
            loading = true;
            foreach (var item in saved.Take(MaxProducts))
                if (IsProductUrl(item.ProductUrl)) items.Add(item);
            Renumber();
            loading = false;
            RefreshTable();
        }
        catch (Exception ex)
        {
            loading = false;
            MessageBox.Show("Không đọc được dữ liệu đã lưu: " + ex.Message);
        }
    }

    private void SaveData()
    {
        if (loading) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dataFile)!);
            File.WriteAllText(dataFile, JsonSerializer.Serialize(items.ToList(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            status.Text = "Không lưu được dữ liệu: " + ex.Message;
        }
    }

    private void UpdateStatus()
    {
        int done = items.Count(p => !string.IsNullOrWhiteSpace(p.AffiliateUrl));
        int unavailable = items.Count(p => IsUnavailable(p.AvailabilityStatus));
        status.Text = $"Danh sách: {items.Count}/{MaxProducts} | Có link: {done} | Không thể mua: {unavailable} | Còn chờ: {items.Count - done}";
    }

    private static IEnumerable<string> SplitLines(string text) =>
        text.Replace("\r", "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static bool IsProductUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("shopee.vn", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".shopee.vn", StringComparison.OrdinalIgnoreCase)) &&
        !uri.Host.Equals("affiliate.shopee.vn", StringComparison.OrdinalIgnoreCase) &&
        (uri.AbsolutePath.Contains("/product/", StringComparison.OrdinalIgnoreCase) ||
         System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, @"-i\.\d+\.\d+"));

    private static bool IsAffiliateUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
        return uri.Host.Equals("shopee.vn", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.EndsWith(".shopee.vn", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Equals("shope.ee", StringComparison.OrdinalIgnoreCase);
    }

    private static string CsvSafe(string value)
    {
        if (value.Length > 0 && "=+-@".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}