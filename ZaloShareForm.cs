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
        var sendItem = new ToolStripMenuItem("Gửi link Affiliate của sản phẩm đang chọn");
        sendItem.Click += (_, _) => OpenZaloForSelectedProducts();
        zaloMenu.DropDownItems.Add(sendItem);
        menu.Items.Add(zaloMenu);
        MainMenuStrip = menu;
        Controls.Add(menu);
        menu.BringToFront();
    }

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
            selected.Add(current);

        if (selected.Count == 0)
        {
            MessageBox.Show(this,
                "Hãy chọn ít nhất một sản phẩm đã có link Affiliate trước khi gửi Zalo.",
                "Tool Affiliate", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var lines = new List<string> { "Mình chia sẻ một số sản phẩm Shopee:" };
        foreach (var product in selected)
        {
            lines.Add("");
            lines.Add("• " + (string.IsNullOrWhiteSpace(product.Name) ? "Sản phẩm Shopee" : product.Name.Trim()));
            lines.Add(product.AffiliateUrl.Trim());
        }
        lines.Add("");
        lines.Add("Link sản phẩm tham khảo. Bạn xem thông tin và giá hiện tại trên Shopee nhé!");

        new ZaloShareForm(string.Join(Environment.NewLine, lines)).Show(this);
    }
}

internal sealed class ZaloShareForm : Form
{
    private readonly WebView2 zaloBrowser = new();
    private readonly TextBox messageBox = new();
    private readonly Label statusLabel = new();
    private readonly List<CoreWebView2Frame> frames = new();
    private readonly string profilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ToolAffiliate", "ZaloProfile");
    private bool initialized;
    private bool sending;

    public ZaloShareForm(string message)
    {
        Text = "Tool Affiliate - Gửi link Affiliate qua Zalo";
        Width = 1120;
        Height = 820;
        MinimumSize = new Size(850, 650);
        StartPosition = FormStartPosition.CenterParent;

        var instructions = new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(10, 7, 8, 4),
            Text = "1) Đăng nhập Zalo nếu cần.  2) Mở đúng MỘT nhóm cần gửi.  3) Kiểm tra nội dung bên dưới.  4) Bấm GỬI TIN NHẮN để phần mềm tự điền và gửi vào nhóm đang mở."
        };

        zaloBrowser.Dock = DockStyle.Fill;

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 190, Padding = new Padding(10) };
        var messageLabel = new Label { Text = "Nội dung tin nhắn (có thể chỉnh sửa):", Dock = DockStyle.Top, Height = 22 };
        messageBox.Multiline = true;
        messageBox.ScrollBars = ScrollBars.Vertical;
        messageBox.Dock = DockStyle.Fill;
        messageBox.Font = new Font("Segoe UI", 9);
        messageBox.Text = message;

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        var sendButton = new Button { Text = "GỬI TIN NHẮN VÀO NHÓM ĐANG MỞ", Width = 300, Height = 32 };
        sendButton.Click += async (_, _) => await ConfirmAndSendAsync(sendButton);
        var copyButton = new Button { Text = "Sao chép nội dung", Width = 155, Height = 32 };
        copyButton.Click += (_, _) =>
        {
            Clipboard.SetText(messageBox.Text);
            statusLabel.Text = "Đã sao chép nội dung.";
        };
        actions.Controls.Add(sendButton);
        actions.Controls.Add(copyButton);

        statusLabel.Dock = DockStyle.Bottom;
        statusLabel.Height = 24;
        statusLabel.Padding = new Padding(4, 4, 0, 0);
        statusLabel.Text = "Đang chuẩn bị Zalo Web...";

        bottom.Controls.Add(messageBox);
        bottom.Controls.Add(messageLabel);
        bottom.Controls.Add(actions);
        bottom.Controls.Add(statusLabel);
        Controls.Add(zaloBrowser);
        Controls.Add(bottom);
        Controls.Add(instructions);

        Shown += async (_, _) => await InitializeZaloAsync();
        FormClosed += (_, _) =>
        {
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
                    ? "Zalo Web đã tải. Đăng nhập và mở đúng một nhóm trước khi gửi."
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

    private async Task ConfirmAndSendAsync(Button sendButton)
    {
        if (sending) return;
        if (zaloBrowser.CoreWebView2 is null)
        {
            statusLabel.Text = "Zalo Web chưa sẵn sàng.";
            return;
        }
        if (string.IsNullOrWhiteSpace(messageBox.Text))
        {
            statusLabel.Text = "Nội dung tin nhắn đang trống.";
            return;
        }

        var answer = MessageBox.Show(this,
            "Phần mềm sẽ gửi nội dung bên dưới vào cuộc trò chuyện Zalo ĐANG MỞ.\n\n" +
            "Hãy chắc chắn đây là đúng nhóm và bạn có quyền chia sẻ nội dung này.\n\n" +
            "Bạn xác nhận gửi ngay bây giờ?",
            "Xác nhận gửi tin nhắn Zalo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;

        sending = true;
        sendButton.Enabled = false;
        try
        {
            var payload = JsonSerializer.Serialize(messageBox.Text);
            var script = BuildSendScript(payload);
            var result = await ExecuteInMainAndFramesAsync(script);
            if (result is null)
            {
                statusLabel.Text = "Không tìm thấy khung soạn tin. Hãy đăng nhập và mở một nhóm Zalo trước.";
                MessageBox.Show(this, statusLabel.Text, "Không gửi được", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var document = JsonDocument.Parse(result);
            var root = document.RootElement;
            var ok = root.TryGetProperty("ok", out var okValue) && okValue.GetBoolean();
            var message = root.TryGetProperty("message", out var messageValue)
                ? messageValue.GetString() ?? "" : "Không xác định được kết quả.";
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
            sendButton.Enabled = true;
        }
    }

    private async Task<string?> ExecuteInMainAndFramesAsync(string script)
    {
        var core = zaloBrowser.CoreWebView2;
        if (core is null) return null;

        try
        {
            var result = await core.ExecuteScriptAsync(script);
            if (IsSuccessResult(result)) return result;
        }
        catch { }

        foreach (var frame in frames.ToArray())
        {
            try
            {
                var result = await frame.ExecuteScriptAsync(script);
                if (IsSuccessResult(result)) return result;
            }
            catch { }
        }
        return null;
    }

    private static bool IsSuccessResult(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.TryGetProperty("ok", out var value) && value.GetBoolean();
        }
        catch { return false; }
    }

    private static string BuildSendScript(string messageJson) => """
    (() => {
      const msg = __MESSAGE__;
      const visible = e => {
        const r = e.getBoundingClientRect();
        const s = getComputedStyle(e);
        return r.width > 0 && r.height > 0 && s.display !== 'none' &&
          s.visibility !== 'hidden' && !e.disabled;
      };
      const selectors = [
        '[contenteditable="true"]', '[role="textbox"]', 'textarea',
        'input[placeholder*="tin nhắn" i]', '[data-placeholder*="tin nhắn" i]'
      ];
      const candidates = [...new Set(selectors.flatMap(s => [...document.querySelectorAll(s)]))]
        .filter(visible);
      if (!candidates.length)
        return {ok:false, message:'Không tìm thấy khung soạn tin trong trang/khung hiện tại.'};

      const editor = candidates.sort((a,b) => {
        const ar=a.getBoundingClientRect(), br=b.getBoundingClientRect();
        return (br.bottom-br.top) - (ar.bottom-ar.top);
      })[0];
      editor.focus();

      if (editor.isContentEditable || editor.getAttribute('contenteditable') === 'true') {
        editor.replaceChildren();
        const lines = msg.split('\n');
        lines.forEach((line, i) => {
          if (i) editor.appendChild(document.createElement('br'));
          if (line) editor.appendChild(document.createTextNode(line));
        });
      } else {
        const proto = editor.tagName === 'TEXTAREA'
          ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
        const setter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;
        if (setter) setter.call(editor, msg); else editor.value = msg;
      }
      editor.dispatchEvent(new InputEvent('input', {
        bubbles:true, inputType:'insertText', data:msg
      }));
      editor.dispatchEvent(new Event('change', {bubbles:true}));

      const buttons = [...document.querySelectorAll('button,[role="button"],[aria-label],[title]')]
        .filter(visible);
      const sendButton = buttons.find(b => {
        const label = [
          b.innerText, b.getAttribute('aria-label'), b.getAttribute('title'),
          b.getAttribute('data-tooltip')
        ].filter(Boolean).join(' ').trim().toLowerCase();
        return /^(gửi|send|gửi tin nhắn|send message)$/.test(label);
      });

      if (sendButton) {
        sendButton.click();
        return {ok:true, message:'Đã thao tác nút Gửi trong nhóm đang mở. Hãy kiểm tra tin nhắn trong cuộc trò chuyện.'};
      }

      editor.dispatchEvent(new KeyboardEvent('keydown', {
        key:'Enter', code:'Enter', keyCode:13, which:13, bubbles:true, cancelable:true
      }));
      editor.dispatchEvent(new KeyboardEvent('keyup', {
        key:'Enter', code:'Enter', keyCode:13, which:13, bubbles:true, cancelable:true
      }));
      return {ok:true, message:'Đã gửi lệnh Enter vào khung chat. Hãy kiểm tra cuộc trò chuyện để xác nhận tin đã được gửi.'};
    })();
    """.Replace("__MESSAGE__", messageJson);
}


public sealed partial class MainForm
{
    private void OpenZaloLogin()
    {
        using var login = new ZaloLoginForm();
        login.ShowDialog(this);
    }
}

internal sealed class ZaloLoginForm : Form
{
    private readonly WebView2 browser = new();
    private readonly Label status = new();
    private readonly string profilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ToolAffiliate", "ZaloProfile");
    private bool initialized;

    public ZaloLoginForm()
    {
        Text = "Đăng nhập Zalo bằng mã QR";
        Width = 1100;
        Height = 800;
        MinimumSize = new Size(800, 600);
        StartPosition = FormStartPosition.CenterParent;

        var instruction = new Label
        {
            Dock = DockStyle.Top,
            Height = 42,
            Padding = new Padding(10, 8, 8, 4),
            Text = "Đăng nhập Zalo bằng QR hoặc phương thức Zalo cung cấp. Phiên đăng nhập được lưu trên máy này để dùng lại."
        };
        browser.Dock = DockStyle.Fill;
        status.Dock = DockStyle.Bottom;
        status.Height = 28;
        status.Padding = new Padding(8, 6, 0, 0);
        status.Text = "Đang mở Zalo Web...";

        Controls.Add(browser);
        Controls.Add(status);
        Controls.Add(instruction);

        Shown += async (_, _) => await InitializeAsync();
        FormClosed += (_, _) =>
        {
            try { browser.CoreWebView2?.Stop(); } catch { }
            browser.Dispose();
        };
    }

    private async Task InitializeAsync()
    {
        if (initialized) return;
        initialized = true;
        try
        {
            Directory.CreateDirectory(profilePath);
            var environment = await CoreWebView2Environment.CreateAsync(null, profilePath);
            await browser.EnsureCoreWebView2Async(environment);
            browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            browser.CoreWebView2.Settings.IsStatusBarEnabled = true;
            browser.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                status.Text = args.IsSuccess
                    ? "Zalo Web đã mở. Quét mã QR để đăng nhập nếu được yêu cầu."
                    : "Không tải được Zalo Web. Hãy kiểm tra Internet.";
            };
            browser.CoreWebView2.Navigate("https://chat.zalo.me/");
        }
        catch (Exception ex)
        {
            status.Text = "Không thể khởi tạo Zalo Web: " + ex.Message;
            MessageBox.Show(this, status.Text, "Zalo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
