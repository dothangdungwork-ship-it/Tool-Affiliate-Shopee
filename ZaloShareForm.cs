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
                "Hãy chọn một hoặc nhiều sản phẩm đã có link Affiliate trước khi gửi Zalo.",
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

        var shareWindow = new ZaloShareForm(string.Join(Environment.NewLine, lines));
        shareWindow.Show(this);
    }
}

internal sealed class ZaloShareForm : Form
{
    private readonly WebView2 zaloBrowser = new();
    private readonly TextBox messageBox = new();
    private readonly Label statusLabel = new();
    private readonly string profilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ToolAffiliate", "ZaloProfile");
    private bool initialized;

    public ZaloShareForm(string message)
    {
        Text = "Tool Affiliate - Chia sẻ link qua Zalo";
        Width = 1120;
        Height = 820;
        MinimumSize = new Size(850, 650);
        StartPosition = FormStartPosition.CenterParent;

        var instructions = new Label
        {
            Dock = DockStyle.Top,
            Height = 42,
            Padding = new Padding(10, 8, 8, 4),
            Text = "1) Đăng nhập Zalo nếu cần.  2) Tự chọn nhóm bạn muốn gửi ở danh sách chat.  3) Kiểm tra nội dung bên dưới rồi bấm Điền vào Zalo.  4) Tự kiểm tra và nhấn Gửi trong Zalo."
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
        var fillButton = new Button { Text = "Điền vào khung chat Zalo", Width = 210, Height = 32 };
        fillButton.Click += async (_, _) => await FillZaloComposerAsync();
        var copyButton = new Button { Text = "Sao chép nội dung", Width = 155, Height = 32 };
        copyButton.Click += (_, _) =>
        {
            Clipboard.SetText(messageBox.Text);
            statusLabel.Text = "Đã sao chép. Bạn có thể dán thủ công vào Zalo.";
        };
        actions.Controls.Add(fillButton);
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
            zaloBrowser.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                statusLabel.Text = args.IsSuccess
                    ? "Zalo Web đã tải. Hãy đăng nhập và mở nhóm cần gửi."
                    : "Không tải được Zalo Web. Kiểm tra kết nối Internet.";
            };
            zaloBrowser.CoreWebView2.Navigate("https://chat.zalo.me/");
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Không khởi tạo được Zalo Web: " + ex.Message;
            MessageBox.Show(this,
                "Không thể mở Zalo Web trong Tool Affiliate.\n" + ex.Message,
                "Zalo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task FillZaloComposerAsync()
    {
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

        try
        {
            var messageJson = JsonSerializer.Serialize(messageBox.Text);
            var script = """
            (() => {
              const visible = e => {
                const r = e.getBoundingClientRect();
                const s = getComputedStyle(e);
                return r.width > 0 && r.height > 0 &&
                  s.display !== 'none' && s.visibility !== 'hidden';
              };
              const candidates = [...document.querySelectorAll(
                '[contenteditable="true"], textarea, [role="textbox"]'
              )].filter(visible);
              if (!candidates.length)
                return { ok:false, message:'Chưa tìm thấy khung soạn tin. Hãy đăng nhập và mở một nhóm Zalo trước.' };
              const editor = candidates[candidates.length - 1];
              editor.focus();
              if (editor.tagName === 'TEXTAREA' || editor.tagName === 'INPUT') {
                const proto = editor.tagName === 'TEXTAREA'
                  ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
                const setter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;
                if (setter) setter.call(editor, __MESSAGE__);
                else editor.value = __MESSAGE__;
              } else {
                editor.replaceChildren();
                const lines = __MESSAGE__.split('\n');
                lines.forEach((line, index) => {
                  if (index) editor.appendChild(document.createElement('br'));
                  if (line) editor.appendChild(document.createTextNode(line));
                });
              }
              editor.dispatchEvent(new InputEvent('input', {
                bubbles:true, inputType:'insertText', data:__MESSAGE__
              }));
              editor.dispatchEvent(new Event('change', { bubbles:true }));
              return { ok:true, message:'Đã điền nội dung vào khung chat. Chưa gửi tin nhắn.' };
            })();
            """.Replace("__MESSAGE__", messageJson);

            var raw = await zaloBrowser.CoreWebView2.ExecuteScriptAsync(script);
            using var result = JsonDocument.Parse(raw);
            var root = result.RootElement;
            var ok = root.TryGetProperty("ok", out var okValue) && okValue.GetBoolean();
            var message = root.TryGetProperty("message", out var messageValue)
                ? messageValue.GetString() ?? "" : "Không rõ trạng thái.";
            statusLabel.Text = message;
            if (ok)
                MessageBox.Show(this,
                    "Nội dung đã được điền vào khung chat. Tool Affiliate chưa gửi tin.\n\nVui lòng kiểm tra đúng nhóm và nội dung, sau đó tự nhấn nút Gửi trong Zalo.",
                    "Xác nhận gửi Zalo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show(this, message, "Chưa thể điền tin nhắn",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Lỗi khi điền nội dung: " + ex.Message;
        }
    }
}
