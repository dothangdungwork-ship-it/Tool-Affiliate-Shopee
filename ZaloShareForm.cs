using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ToolAffiliate;

public sealed partial class MainForm : Form
{
    private void InitializeZaloMenu()
    {
        var menu = new MenuStrip();
        var zaloMenu = new ToolStripMenuItem("Zalo");
        var sendItem = new ToolStripMenuItem("Gửi tin nhắn vào Zalo (1 hoặc nhiều nhóm)");
        sendItem.Click += (_, _) => OpenZaloForSelectedProducts();
        zaloMenu.DropDownItems.Add(sendItem);
        menu.Items.Add(zaloMenu);
        MainMenuStrip = menu;
        Controls.Add(menu);
        menu.BringToFront();
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
            var lines = new List<string> { "Mình chia sẻ một số sản phẩm Shopee:" };
            foreach (var product in selected)
            {
                lines.Add("");
                lines.Add("• " + (string.IsNullOrWhiteSpace(product.Name) ? "Sản phẩm Shopee" : product.Name.Trim()));
                lines.Add(product.AffiliateUrl.Trim());
            }
            lines.Add("");
            lines.Add("Link sản phẩm tham khảo. Bạn xem thông tin và giá hiện tại trên Shopee nhé!");
            initialMessage = string.Join(Environment.NewLine, lines);
        }

        new ZaloShareForm(initialMessage).Show(this);
    }
}

internal sealed class ZaloShareForm : Form
{
    private readonly WebView2 zaloBrowser = new();
    private readonly TextBox messageBox = new();
    private readonly TextBox groupsBox = new();
    private readonly NumericUpDown minDelayInput = new();
    private readonly NumericUpDown maxDelayInput = new();
    private readonly Label statusLabel = new();
    private readonly List<CoreWebView2Frame> frames = new();
    private readonly string profilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ToolAffiliate", "ZaloProfile");
    private readonly string savedGroupsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ToolAffiliate", "zalo-groups.txt");
    private bool initialized;
    private bool sending;
    private CancellationTokenSource? sendCts;

    public ZaloShareForm(string message)
    {
        Text = "Tool Affiliate - Gửi tin nhắn Zalo (Hỗ trợ nhiều nhóm & chống Spam)";
        Width = 1240;
        Height = 860;
        MinimumSize = new Size(960, 680);
        StartPosition = FormStartPosition.CenterParent;

        var instructions = new Label
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(10, 7, 8, 4),
            Text = "1) Đăng nhập Zalo nếu cần.  2) Nhập danh sách tên nhóm ở khung bên phải (mỗi dòng 1 nhóm) hoặc gửi lẻ vào nhóm đang mở.  3) Chỉnh thời gian nghỉ giữa các nhóm để tránh Zalo báo spam."
        };

        zaloBrowser.Dock = DockStyle.Fill;

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 240, Padding = new Padding(10) };

        var splitBottom = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 8
        };

        // Khung bên trái: Nội dung tin nhắn
        var msgLabel = new Label { Text = "Nội dung tin nhắn (có thể chỉnh sửa):", Dock = DockStyle.Top, Height = 22, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
        messageBox.Multiline = true;
        messageBox.ScrollBars = ScrollBars.Vertical;
        messageBox.Dock = DockStyle.Fill;
        messageBox.Font = new Font("Segoe UI", 9);
        messageBox.Text = message;
        splitBottom.Panel1.Controls.Add(messageBox);
        splitBottom.Panel1.Controls.Add(msgLabel);

        // Khung bên phải: Danh sách nhóm cần gửi
        var grpLabel = new Label { Text = "Danh sách tên nhóm/người nhận (Mỗi dòng 1 tên chính xác):", Dock = DockStyle.Top, Height = 22, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
        groupsBox.Multiline = true;
        groupsBox.ScrollBars = ScrollBars.Vertical;
        groupsBox.Dock = DockStyle.Fill;
        groupsBox.Font = new Font("Segoe UI", 9);
        groupsBox.PlaceholderText = "Ví dụ:\nBấm đũ shopee\nHội săn sale Shopee\nNhóm nghiện Shopee...";
        if (File.Exists(savedGroupsFile))
        {
            try { groupsBox.Text = File.ReadAllText(savedGroupsFile); } catch { }
        }
        groupsBox.TextChanged += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(savedGroupsFile)!);
                File.WriteAllText(savedGroupsFile, groupsBox.Text);
            }
            catch { }
        };
        splitBottom.Panel2.Controls.Add(groupsBox);
        splitBottom.Panel2.Controls.Add(grpLabel);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 0)
        };

        var sendCurrentBtn = new Button { Text = "Gửi vào nhóm ĐANG MỞ", Width = 185, Height = 32 };
        var sendMultiBtn = new Button { Text = "GỬI TỰ ĐỘNG NHIỀU NHÓM", Width = 215, Height = 32, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
        var stopBtn = new Button { Text = "Dừng gửi", Width = 95, Height = 32, Enabled = false };
        var copyButton = new Button { Text = "Sao chép ND", Width = 110, Height = 32 };

        var delayLabel1 = new Label { Text = "Nghỉ chống spam từ:", AutoSize = true, Margin = new Padding(10, 8, 2, 0) };
        minDelayInput.Minimum = 5;
        minDelayInput.Maximum = 600;
        minDelayInput.Value = 15;
        minDelayInput.Width = 55;
        minDelayInput.Margin = new Padding(2, 4, 2, 0);

        var delayLabel2 = new Label { Text = "đến:", AutoSize = true, Margin = new Padding(4, 8, 2, 0) };
        maxDelayInput.Minimum = 5;
        maxDelayInput.Maximum = 600;
        maxDelayInput.Value = 30;
        maxDelayInput.Width = 55;
        maxDelayInput.Margin = new Padding(2, 4, 2, 0);

        var delayLabel3 = new Label { Text = "giây/nhóm", AutoSize = true, Margin = new Padding(2, 8, 0, 0) };

        sendCurrentBtn.Click += async (_, _) => await ConfirmAndSendSingleAsync(sendCurrentBtn, sendMultiBtn);
        sendMultiBtn.Click += async (_, _) => await ConfirmAndSendMultiGroupsAsync(sendCurrentBtn, sendMultiBtn, stopBtn);
        stopBtn.Click += (_, _) =>
        {
            sendCts?.Cancel();
            statusLabel.Text = "Đang yêu cầu dừng gửi...";
        };
        copyButton.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(messageBox.Text))
            {
                Clipboard.SetText(messageBox.Text);
                statusLabel.Text = "Đã sao chép nội dung.";
            }
        };

        actions.Controls.AddRange(new Control[]
        {
            sendCurrentBtn, sendMultiBtn, stopBtn, copyButton,
            delayLabel1, minDelayInput, delayLabel2, maxDelayInput, delayLabel3
        });

        statusLabel.Dock = DockStyle.Bottom;
        statusLabel.Height = 24;
        statusLabel.Padding = new Padding(4, 4, 0, 0);
        statusLabel.Text = "Đang chuẩn bị Zalo Web...";

        bottom.Controls.Add(splitBottom);
        bottom.Controls.Add(actions);
        bottom.Controls.Add(statusLabel);

        Controls.Add(zaloBrowser);
        Controls.Add(bottom);
        Controls.Add(instructions);

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
            try { zaloBrowser.CoreWebView2?.Stop(); } catch { }
            zaloBrowser.Dispose();
        };
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
                    ? "Zalo Web đã sẵn sàng. Nhập danh sách nhóm bên phải để gửi hàng loạt hoặc mở 1 nhóm để gửi lẻ."
                    : "Không tải được Zalo Web. Kiểm tra kết nối Internet.";
            };
            zaloBrowser.CoreWebView2.Navigate("https://chat.zalo.me/");
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Không khởi tạo được Zalo Web: " + ex.Message;
            MessageBox.Show(this, "Không thể mở Zalo Web.\n" + ex.Message,
                "Zalo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task ConfirmAndSendSingleAsync(Button singleBtn, Button multiBtn)
    {
        if (sending) return;
        if (zaloBrowser.CoreWebView2 is null)
        {
            statusLabel.Text = "Zalo Web chưa sẵn sàng.";
            return;
        }
        if (string.IsNullOrWhiteSpace(messageBox.Text))
        {
            MessageBox.Show(this, "Nội dung tin nhắn đang trống.", "Zalo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        sending = true;
        singleBtn.Enabled = false;
        multiBtn.Enabled = false;
        try
        {
            var (ok, message) = await SendMessageToCurrentOpenChatAsync();
            statusLabel.Text = message;
            MessageBox.Show(this, message, ok ? "Zalo" : "Chưa gửi được",
                MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Lỗi khi gửi: " + ex.Message;
            MessageBox.Show(this, statusLabel.Text, "Zalo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            sending = false;
            if (!IsDisposed)
            {
                singleBtn.Enabled = true;
                multiBtn.Enabled = true;
            }
        }
    }

    private async Task ConfirmAndSendMultiGroupsAsync(Button singleBtn, Button multiBtn, Button stopBtn)
    {
        if (sending) return;
        if (zaloBrowser.CoreWebView2 is null)
        {
            statusLabel.Text = "Zalo Web chưa sẵn sàng.";
            return;
        }
        if (string.IsNullOrWhiteSpace(messageBox.Text))
        {
            MessageBox.Show(this, "Nội dung tin nhắn đang trống.", "Zalo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var groups = groupsBox.Text
            .Replace("\r", "")
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (groups.Count == 0)
        {
            MessageBox.Show(this,
                "Bạn chưa nhập tên nhóm nào ở ô 'Danh sách tên nhóm/người nhận' phía bên phải.\n\nHãy nhập mỗi dòng 1 tên nhóm (ví dụ: Bấm đũ shopee) rồi thử lại.",
                "Chưa có danh sách nhóm", MessageBoxButtons.OK, MessageBoxIcon.Information);
            groupsBox.Focus();
            return;
        }

        int minSec = (int)Math.Min(minDelayInput.Value, maxDelayInput.Value);
        int maxSec = (int)Math.Max(minDelayInput.Value, maxDelayInput.Value);

        var confirm = MessageBox.Show(this,
            $"Phần mềm sẽ lần lượt tìm và gửi tin nhắn vào {groups.Count} nhóm:\n" +
            $"- {string.Join("\n- ", groups.Take(6))}{(groups.Count > 6 ? "\n- ..." : "")}\n\n" +
            $"Thời gian nghỉ ngẫu nhiên giữa các nhóm: {minSec} - {maxSec} giây để tránh Zalo báo spam.\n\n" +
            "Bắt đầu gửi tự động ngay bây giờ?",
            "Xác nhận gửi nhiều nhóm Zalo", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes) return;

        sending = true;
        singleBtn.Enabled = false;
        multiBtn.Enabled = false;
        stopBtn.Enabled = true;
        sendCts = new CancellationTokenSource();
        var token = sendCts.Token;

        int successCount = 0;
        var failedGroups = new List<string>();

        try
        {
            for (int i = 0; i < groups.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var groupName = groups[i];
                statusLabel.Text = $"[{i + 1}/{groups.Count}] Đang tìm và mở nhóm: {groupName}...";

                bool opened = await SelectGroupByNameAsync(groupName, token);
                if (!opened)
                {
                    failedGroups.Add($"{groupName} (Không tìm thấy nhóm)");
                }
                else
                {
                    await Task.Delay(750, token);
                    statusLabel.Text = $"[{i + 1}/{groups.Count}] Đang gửi tin nhắn vào nhóm: {groupName}...";
                    var (ok, msg) = await SendMessageToCurrentOpenChatAsync();
                    if (ok)
                    {
                        successCount++;
                        statusLabel.Text = $"[{i + 1}/{groups.Count}] Đã gửi thành công vào '{groupName}'!";
                    }
                    else
                    {
                        failedGroups.Add($"{groupName} ({msg})");
                    }
                }

                // Nếu chưa phải nhóm cuối cùng, thực hiện đếm ngược nghỉ chống spam
                if (i < groups.Count - 1)
                {
                    int waitSeconds = Random.Shared.Next(minSec, maxSec + 1);
                    for (int remain = waitSeconds; remain > 0; remain--)
                    {
                        token.ThrowIfCancellationRequested();
                        statusLabel.Text = $"Đã gửi {successCount}/{i + 1} nhóm. Đang nghỉ chống spam {remain} giây trước khi gửi nhóm tiếp theo ({groups[i + 1]})...";
                        await Task.Delay(1000, token);
                    }
                }
            }

            var summary = $"Hoàn tất gửi hàng loạt!\n- Thành công: {successCount}/{groups.Count} nhóm.";
            if (failedGroups.Count > 0)
            {
                summary += "\n- Chưa gửi được:\n  • " + string.Join("\n  • ", failedGroups);
            }
            statusLabel.Text = $"Hoàn tất: Đã gửi {successCount}/{groups.Count} nhóm.";
            MessageBox.Show(this, summary, "Kết quả gửi nhiều nhóm Zalo", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = $"Đã dừng gửi theo yêu cầu. (Đã gửi {successCount}/{groups.Count} nhóm)";
            MessageBox.Show(this, statusLabel.Text, "Đã dừng", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Lỗi khi gửi nhiều nhóm: " + ex.Message;
            MessageBox.Show(this, statusLabel.Text, "Zalo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            sending = false;
            if (!IsDisposed)
            {
                singleBtn.Enabled = true;
                multiBtn.Enabled = true;
                stopBtn.Enabled = false;
            }
        }
    }

    private async Task<bool> SelectGroupByNameAsync(string groupName, CancellationToken token)
    {
        var nameJson = JsonSerializer.Serialize(groupName.Trim());

        // Bước 1: Tìm trực tiếp trong danh sách hội thoại đang hiển thị bên trái
        var clickDirectResult = await ExecuteInMainAndFramesAsync(BuildClickConversationScript(nameJson));
        if (clickDirectResult is not null && IsSuccessResult(clickDirectResult))
        {
            return true;
        }

        // Bước 2: Nếu nhóm bị trôi xuống dưới, gõ tên nhóm vào ô Tìm kiếm (#contact-search-input) của Zalo
        var searchResult = await ExecuteInMainAndFramesAsync(BuildTypeSearchBoxScript(nameJson));
        if (searchResult is null || !IsSuccessResult(searchResult))
        {
            return false;
        }

        // Đợi Zalo lọc kết quả tìm kiếm rồi bấm vào nhóm khớp tên
        for (int attempt = 0; attempt < 5; attempt++)
        {
            await Task.Delay(550, token);
            var clickAfterSearch = await ExecuteInMainAndFramesAsync(BuildClickConversationScript(nameJson));
            if (clickAfterSearch is not null && IsSuccessResult(clickAfterSearch))
            {
                return true;
            }
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
            return (false, "Không tìm thấy khung soạn tin. Hãy mở một nhóm Zalo trước.");
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
            message: 'Chưa nhận diện được ô nhập tin nhắn. Hãy mở một cuộc trò chuyện trên Zalo Web rồi thử lại.'
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

        return { ok: true, message: 'Đã tự động điền đầy đủ link và gửi vào nhóm Zalo thành công!' };
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