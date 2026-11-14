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
    public string Status => string.IsNullOrWhiteSpace(AffiliateUrl) ? "Chờ tạo link" : "Đã có link";
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
    private const string CustomLinkUrl = "https://affiliate.shopee.vn/offer/custom_link";

    public MainForm()
    {
        Text = "Shopee Affiliate Agent V3 - Safe Assisted Workflow";
        Width = 1580;
        Height = 980;
        MinimumSize = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 610, FixedPanel = FixedPanel.Panel1 };
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
                var profile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ShopeeAffiliateAgent", "WebViewProfile");
                Directory.CreateDirectory(profile);
                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, profile);
                await browser.EnsureCoreWebView2Async(env);
                browser.CoreWebView2.NavigationCompleted += async (_, e) =>
                {
                    status.Text = e.IsSuccess ? "Trình duyệt sẵn sàng." : "Không tải được trang hiện tại.";
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

        panel.Controls.Add(LabelAt("2. Thêm sản phẩm", 158));
        AddButton(panel, "Thêm URL trang đang mở", 12, 185, 280, AddCurrentPage);
        AddButton(panel, "Sao chép link sản phẩm đang chọn", 302, 185, 288, () => CopySelected(p => p.ProductUrl));

        bulkProducts.SetBounds(12, 225, 578, 76);
        bulkProducts.Multiline = true;
        bulkProducts.ScrollBars = ScrollBars.Vertical;
        bulkProducts.PlaceholderText = "Dán nhiều URL sản phẩm Shopee (mỗi dòng một link, tối đa 100 sản phẩm)...";
        panel.Controls.Add(bulkProducts);
        AddButton(panel, "Thêm danh sách URL", 12, 306, 280, AddBulkProducts);
        AddButton(panel, "Xóa sản phẩm đã chọn", 302, 306, 288, RemoveSelected);

        panel.Controls.Add(LabelAt("3. Gắn link Affiliate theo thứ tự hàng đợi", 348));
        bulkAffiliates.SetBounds(12, 375, 578, 76);
        bulkAffiliates.Multiline = true;
        bulkAffiliates.ScrollBars = ScrollBars.Vertical;
        bulkAffiliates.PlaceholderText = "Dán các link Affiliate Shopee do Custom Link chính thức trả về, mỗi dòng một link...";
        panel.Controls.Add(bulkAffiliates);
        AddButton(panel, "Gắn link lần lượt cho sản phẩm đang chờ", 12, 456, 578, AttachBulkAffiliates);
        AddButton(panel, "AI Agent: tự tạo link Affiliate", 12, 492, 578, RunAffiliateAgent);

        panel.Controls.Add(LabelAt("4. Danh sách sản phẩm (tối đa 100)", 534));
        table.SetBounds(12, 561, 578, 264);
        table.ReadOnly = true;
        table.AllowUserToAddRows = false;
        table.MultiSelect = false;
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

        AddButton(panel, "Mở link gốc đã chọn", 12, 835, 185, OpenSelectedProduct);
        AddButton(panel, "Sao chép link Affiliate", 205, 835, 185, () => CopySelected(p => p.AffiliateUrl));
        AddButton(panel, "Xuất CSV", 398, 835, 192, ExportCsv);

        var note = new Label
        {
            Text = "Sau khi lấy sản phẩm đang hiển thị, Agent tự mở Custom Link và lần lượt tạo link Affiliate trên giao diện Shopee. Cần đăng nhập trước; CAPTCHA/OTP sẽ yêu cầu bạn xử lý. Không đọc cookie, không gọi API nội bộ.",
            Left = 12, Top = 875, Width = 578, Height = 58,
            ForeColor = Color.DarkSlateGray
        };
        panel.Controls.Add(note);
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

    private void AddButton(Control parent, string text, int x, int y, int width, Action action)
    {
        var button = new Button { Text = text, Left = x, Top = y, Width = width, Height = 32 };
        button.Click += (_, _) => action();
        parent.Controls.Add(button);
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
        Navigate("https://shopee.vn/search?keyword=" + Uri.EscapeDataString(term));
    }

    private async void ExtractVisibleProducts()
    {
        if (browser.CoreWebView2 is null)
        {
            MessageBox.Show("Trình duyệt chưa sẵn sàng.");
            return;
        }

        const string script = """
        (() => {
          const visible = e => !!(e && (e.offsetWidth || e.offsetHeight || e.getClientRects().length));
          const found = [];
          const seen = new Set();
          for (const a of document.querySelectorAll('a[href]')) {
            if (!visible(a)) continue;
            let href = a.href || '';
            try { href = new URL(href, location.href).href; } catch { continue; }
            if (!/^https:\/\/((www|m)\.)?shopee\.vn\//i.test(href)) continue;
            if (!(/\/product\//i.test(href) || /-i\.\d+\.\d+/i.test(href))) continue;
            const card = a.closest('[data-sqe="item"], [data-testid*="item"], li') || a.parentElement;
            const name = ((a.innerText || a.getAttribute('aria-label') || (card && card.innerText) || '').replace(/\s+/g, ' ').trim()).slice(0, 240);
            const key = href.split('?')[0];
            if (seen.has(key)) continue;
            seen.add(key);
            found.push({ name: name || 'Sản phẩm Shopee', productUrl: key });
          }
          return found.slice(0, 100);
        })();
        """;

        try
        {
            var raw = await browser.CoreWebView2.ExecuteScriptAsync(script);
            var products = JsonSerializer.Deserialize<List<VisibleProduct>>(raw,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            if (products.Count == 0)
            {
                MessageBox.Show("Không tìm thấy sản phẩm trong nội dung đang hiển thị. Hãy mở trang kết quả Shopee, chờ tải xong và thử lại.");
                return;
            }

            int added = 0, skipped = 0;
            foreach (var product in products)
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
                status.Text = $"Đã thêm {added} sản phẩm. Đang tự mở Custom Link...";
                Navigate(CustomLinkUrl);
            }
            else
            {
                MessageBox.Show($"Đã đọc {products.Count} sản phẩm đang hiển thị; không có sản phẩm mới để tạo link.");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Không đọc được danh sách sản phẩm: " + ex.Message);
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
        try
        {
            foreach (var product in pending)
            {
                status.Text = $"Agent đang tạo link {completed + 1}/{pending.Count}: {product.Name}";
                var input = JsonSerializer.Serialize(product.ProductUrl);
                var script = """
                (async (productUrl) => {
                  const visible = e => !!(e && (e.offsetWidth || e.offsetHeight || e.getClientRects().length));
                  const norm = s => (s || '').toLowerCase().replace(/\s+/g, ' ').trim();
                  const fields = [...document.querySelectorAll('input:not([type=hidden]), textarea')]
                    .filter(visible).filter(e => !e.disabled && !e.readOnly);
                  const scored = fields.map(e => {
                    const meta = norm([e.placeholder, e.getAttribute('aria-label'), e.name, e.id,
                      e.parentElement?.innerText, e.labels ? [...e.labels].map(x => x.innerText).join(' ') : ''].join(' '));
                    return { e, score: /(product|商品|sản phẩm|url|link|đường dẫn)/i.test(meta) ? 2 : 0 };
                  }).sort((a,b) => b.score-a.score);
                  let field = scored[0]?.e;
                  if (!field || (scored[0].score === 0 && fields.length !== 1))
                    return { ok:false, message:'Không xác định chắc chắn ô nhập URL sản phẩm trên trang Custom Link.' };

                  const setter = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(field), 'value')?.set;
                  if (setter) setter.call(field, productUrl); else field.value = productUrl;
                  field.dispatchEvent(new Event('input', { bubbles:true }));
                  field.dispatchEvent(new Event('change', { bubbles:true }));

                  const buttons = [...document.querySelectorAll('button,[role=button],input[type=submit]')]
                    .filter(visible).filter(e => !e.disabled);
                  const matches = buttons.filter(e => /(get link|generate|create link|tạo link|lấy link|tạo liên kết|lấy liên kết)/i.test(norm(e.innerText || e.value || e.getAttribute('aria-label'))));
                  if (matches.length !== 1)
                    return { ok:false, message:'Không tìm thấy duy nhất một nút tạo link phù hợp. Agent dừng để tránh bấm nhầm.' };

                  const before = new Set([...document.querySelectorAll('a[href],input,textarea')].map(e => e.href || e.value || ''));
                  matches[0].click();

                  const isCandidate = value => {
                    try {
                      const u = new URL(value, location.href);
                      return u.protocol === 'https:' &&
                        (u.hostname === 'shope.ee' || u.hostname.endsWith('.shopee.vn')) &&
                        value !== productUrl && !value.includes('/search?');
                    } catch { return false; }
                  };

                  for (let i=0; i<36; i++) {
                    await new Promise(resolve => setTimeout(resolve, 500));
                    const candidates = [
                      ...[...document.querySelectorAll('input,textarea')].map(e => e.value),
                      ...[...document.querySelectorAll('a[href]')].map(e => e.href)
                    ].filter(v => v && isCandidate(v) && !before.has(v));
                    const unique = [...new Set(candidates)];
                    if (unique.length === 1) return { ok:true, affiliateUrl:unique[0], message:'Đã lấy được link Affiliate' };
                    if (unique.length > 1) {
                      const short = unique.filter(v => /shope\.ee/i.test(v));
                      if (short.length === 1) return { ok:true, affiliateUrl:short[0], message:'Đã lấy link rút gọn Affiliate' };
                    }
                  }
                  return { ok:false, message:'Đã bấm tạo link nhưng chưa đọc được link kết quả. Agent dừng; hãy kiểm tra trang Custom Link.' };
                })(__PRODUCT_URL_JSON__);
                """;
                script = script.Replace("__PRODUCT_URL_JSON__", input);
                var raw = await browser.CoreWebView2.ExecuteScriptAsync(script);
                var result = JsonSerializer.Deserialize<AgentLinkResult>(raw,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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
                MessageBox.Show($"Hoàn tất! Agent đã tạo và lưu {completed} link Affiliate.");
            else if (completed > 0)
                MessageBox.Show($"Agent đã tạo và lưu {completed}/{pending.Count} link. Các sản phẩm còn lại vẫn ở trạng thái chờ.");
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
        using var dialog = new SaveFileDialog { Filter = "CSV UTF-8 (*.csv)|*.csv", FileName = "Shopee_Affiliate_V4.csv" };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        var csv = new StringBuilder();
        csv.AppendLine("STT,Ten san pham,Product URL,Affiliate URL,Trang thai");
        foreach (var p in items)
            csv.AppendLine(string.Join(",", new[] {
                CsvSafe(p.Number.ToString()), CsvSafe(p.Name), CsvSafe(p.ProductUrl),
                CsvSafe(p.AffiliateUrl), CsvSafe(p.Status)
            }));
        File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true));
        MessageBox.Show("Đã xuất CSV thành công.");
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
        status.Text = $"Danh sách: {items.Count}/{MaxProducts} sản phẩm | Đã có link: {done} | Còn chờ: {items.Count - done} | Tự lưu trên máy tính";
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
