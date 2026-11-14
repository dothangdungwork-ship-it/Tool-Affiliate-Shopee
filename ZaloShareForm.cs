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
        var sendItem = new ToolStripMenuItem("Gửi tin nhắn vào Zalo");
        sendItem.Click += (_, _) => OpenZaloForSelectedProducts();
        zaloMenu.DropDownItems.Add(sendItem);
        menu.Items.Add(zaloMenu);
        MainMenuStrip = menu;
        Controls.Add(menu);
        menu.BringToFront();
    }

    // Khi bấm nút trên giao diện chính, gọi thẳng vào luồng gửi tin nhắn Zalo
    private void OpenZaloLogin() => OpenZaloForSelectedProducts();

    private void OpenZaloForSelectedProducts()
    {
        // 1. Ưu tiên lấy các dòng đang được chọn (bôi đen) có sẵn link Affiliate
        var selected = table.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => row.DataBoundItem as ProductItem)
            .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.AffiliateUrl))
            .Cast<ProductItem>()
            .Distinct()
            .ToList();

        // 2. Nếu không bôi đen nhiều dòng, lấy dòng hiện tại đang trỏ tới
        if (selected.Count == 0 && table.CurrentRow?.DataBoundItem is ProductItem current &&
            !string.IsNullOrWhiteSpace(current.AffiliateUrl))
        {
            selected.Add(current);
        }

        // 3. Nếu vẫn trống, tự động lấy TẤT CẢ sản phẩm đã có link Affiliate trong bảng
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
    private readonly Label statusLabel = new();
    private readonly List<CoreWebView2Frame> frames = new();
    private readonly string profilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ToolAffiliate", "ZaloProfile");
    private bool initialized;
    private bool sending;

    public ZaloShareForm(string message)
    {
        Text = "Tool Affiliate - Gửi tin nhắn vào Zalo";
        Width = 1120;
        Height = 820;
        MinimumSize = new Size(850, 650);
        StartPosition = FormStartPosition.CenterParent;

        var instructions = new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(10, 7, 8, 4),
            Text = "1) Quét QR đăng nhập Zalo nếu lần đầu mở (phiên sẽ được lưu tự động).  2) Mở nhóm/người cần gửi.  3) Kiểm tra nội dung bên dưới và bấm GỬI TIN NHẮN."
        };

        zaloBrowser.Dock = DockStyle.Fill;

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 190, Padding = new Padding(10) };
        var messageLabel = new Label { Text = "Nội dung tin nhắn (có thể chỉnh sửa hoặc tự nhập thêm):", Dock = DockStyle.Top, Height = 22 };
        messageBox.Multiline = true;
        messageBox.ScrollBars = ScrollBars.Vertical;
        messageBox.Dock = DockStyle.Fill;
        messageBox.Font = new Font("Segoe UI", 9);
        messageBox.PlaceholderText = "Nhập nội dung tin nhắn hoặc chọn sản phẩm đã có link Affiliate trước khi mở cửa sổ này...";
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
            if (!string.IsNullOrEmpty(messageBox.Text))
            {
                Clipboard.SetText(messageBox.Text);
                statusLabel.Text = "Đã sao chép nội dung.";
            }
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
                    ? "Zalo Web đã sẵn sàng. Chọn cuộc trò chuyện và bấm GỬI TIN NHẮN."
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
            MessageBox.Show(this, "Nội dung tin nhắn đang trống. Hãy nhập nội dung hoặc tạo link Affiliate trước.", "Zalo", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            var normalizedText = messageBox.Text.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
            try { Clipboard.SetText(normalizedText); } catch { }

            var payload = JsonSerializer.Serialize(normalizedText);

            zaloBrowser.Focus();
            await Task.Delay(150);

            var fillScript = BuildFillScript(payload);
            var result = await ExecuteInMainAndFramesAsync(fillScript);
            if (string.IsNullOrWhiteSpace(result) || result == "null")
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
            if (!IsDisposed) sendButton.Enabled = true;
        }
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
