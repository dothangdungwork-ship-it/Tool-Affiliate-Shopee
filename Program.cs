using Microsoft.Web.WebView2.WinForms;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ShopeeAffiliateAgent;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

public sealed class MainForm : Form
{
    private readonly WebView2 browser = new();
    private readonly TextBox keyword = new();
    private readonly TextBox productUrl = new();
    private readonly TextBox affiliateUrl = new();
    private readonly DataGridView table = new();
    private readonly Label status = new();

    private const int MaxProducts = 100;

    public MainForm()
    {
        Text = "Shopee Affiliate Agent V2";
        Width = 1500;
        Height = 920;
        StartPosition = FormStartPosition.CenterScreen;

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 520,
            FixedPanel = FixedPanel.Panel1
        };
        Controls.Add(split);
        BuildLeftPanel(split.Panel1);
        BuildBrowserPanel(split.Panel2);

        status.Dock = DockStyle.Bottom;
        status.Height = 28;
        status.Text = "Dang khoi tao trinh duyet Shopee...";
        Controls.Add(status);

        Shown += async (_, _) =>
        {
            try
            {
                string profile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ShopeeAffiliateAgent", "WebViewProfile");
                Directory.CreateDirectory(profile);

                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, profile);
                await browser.EnsureCoreWebView2Async(env);
                browser.CoreWebView2.NavigationCompleted += (_, e) =>
                    status.Text = e.IsSuccess ? "Trang da tai. Dang nhap va thao tac trong khung ben phai." : "Trang khong tai duoc.";
                browser.CoreWebView2.Navigate("https://affiliate.shopee.vn/");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Khong khoi tao duoc WebView2: " + ex.Message,
                    "Shopee Affiliate Agent", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
    }

    private void BuildLeftPanel(Panel panel)
    {
        panel.Padding = new Padding(10);
        var title = new Label
        {
            Text = "SHOPEE AFFILIATE AGENT V2",
            Font = new System.Drawing.Font("Segoe UI", 15, System.Drawing.FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 40
        };
        panel.Controls.Add(title);

        panel.Controls.Add(MakeLabel("1. Tim san pham theo tu khoa", 48));
        keyword.SetBounds(10, 76, 330, 30);
        keyword.PlaceholderText = "VD: laptop gaming, RTX 5060...";
        panel.Controls.Add(keyword);
        AddButton(panel, "Tim Shopee", 350, 75, 145, SearchShopee);

        AddButton(panel, "Mo Shopee Affiliate", 10, 116, 235, () =>
            Navigate("https://affiliate.shopee.vn/"));
        AddButton(panel, "Mo trang Shopee", 255, 116, 240, () =>
            Navigate("https://shopee.vn/"));

        panel.Controls.Add(MakeLabel("2. Them link san pham", 160));
        productUrl.SetBounds(10, 188, 485, 30);
        productUrl.PlaceholderText = "Dan link san pham Shopee vao day";
        panel.Controls.Add(productUrl);
        AddButton(panel, "Them vao danh sach", 10, 225, 180, AddProduct);

        panel.Controls.Add(MakeLabel("3. Link Affiliate tra ve tu Shopee", 270));
        affiliateUrl.SetBounds(10, 298, 485, 30);
        affiliateUrl.PlaceholderText = "Dan link Affiliate sau khi tao tren Custom Link";
        panel.Controls.Add(affiliateUrl);
        AddButton(panel, "Gan link cho dong dang chon", 10, 335, 245, AttachAffiliate);
        AddButton(panel, "Mo Custom Link", 265, 335, 230, () =>
            Navigate("https://affiliate.shopee.vn/"));

        panel.Controls.Add(MakeLabel("4. Danh sach san pham (toi da 100)", 380));
        table.SetBounds(10, 408, 485, 320);
        table.AllowUserToAddRows = false;
        table.ReadOnly = true;
        table.MultiSelect = false;
        table.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        table.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        table.Columns.Add("number", "STT");
        table.Columns.Add("original", "Link san pham");
        table.Columns.Add("affiliate", "Link Affiliate");
        panel.Controls.Add(table);

        AddButton(panel, "Xoa dong", 10, 740, 110, () =>
        {
            if (table.SelectedRows.Count > 0)
                table.Rows.Remove(table.SelectedRows[0]);
            Renumber();
        });
        AddButton(panel, "Sao chep link goc", 130, 740, 165, CopyOriginal);
        AddButton(panel, "Sao chep link Affiliate", 305, 740, 190, CopyAffiliate);
        AddButton(panel, "Xuat CSV", 10, 780, 485, ExportCsv);

        var note = new Label
        {
            Text = "Ghi chu: Tim kiem mo ket qua Shopee trong khung ben phai. Ban tu chon san pham, sao chep URL va them vao bang. Tao link bang Custom Link chinh thuc, sau do dan link ket qua vao o tren de gan cho dong dang chon.",
            Left = 10, Top = 825, Width = 485, Height = 60
        };
        panel.Controls.Add(note);
    }

    private Label MakeLabel(string text, int top) => new()
    {
        Text = text, Left = 10, Top = top, Width = 480, Height = 24,
        Font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold)
    };

    private void BuildBrowserPanel(Panel panel)
    {
        browser.Dock = DockStyle.Fill;
        panel.Controls.Add(browser);
    }

    private void AddButton(Panel panel, string text, int x, int y, int width, Action action)
    {
        var button = new Button { Text = text, Left = x, Top = y, Width = width, Height = 32 };
        button.Click += (_, _) => action();
        panel.Controls.Add(button);
    }

    private void Navigate(string url)
    {
        if (browser.CoreWebView2 == null)
        {
            MessageBox.Show("Trinh duyet dang khoi tao, vui long doi mot chut.");
            return;
        }
        browser.CoreWebView2.Navigate(url);
    }

    private void SearchShopee()
    {
        if (string.IsNullOrWhiteSpace(keyword.Text))
        {
            MessageBox.Show("Nhap tu khoa can tim.");
            return;
        }
        Navigate("https://shopee.vn/search?keyword=" + Uri.EscapeDataString(keyword.Text.Trim()));
    }

    private void AddProduct()
    {
        string url = productUrl.Text.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Host != "shopee.vn" && !uri.Host.EndsWith(".shopee.vn", StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("Vui long nhap URL Shopee hop le.");
            return;
        }
        if (table.Rows.Count >= MaxProducts)
        {
            MessageBox.Show("Danh sach da dat gioi han 100 san pham.");
            return;
        }
        bool exists = table.Rows.Cast<DataGridViewRow>()
            .Any(r => string.Equals(Convert.ToString(r.Cells["original"].Value), url, StringComparison.OrdinalIgnoreCase));
        if (exists)
        {
            MessageBox.Show("Link nay da co trong danh sach.");
            return;
        }
        table.Rows.Add(table.Rows.Count + 1, url, "");
        productUrl.Clear();
        status.Text = $"Da them {table.Rows.Count}/{MaxProducts} san pham.";
    }

    private void AttachAffiliate()
    {
        if (table.SelectedRows.Count == 0)
        {
            MessageBox.Show("Chon mot san pham trong bang truoc.");
            return;
        }
        string url = affiliateUrl.Text.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Host != "shopee.vn" && !uri.Host.EndsWith(".shopee.vn", StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("Link Affiliate khong hop le. Hay dan link Shopee do Custom Link tra ve.");
            return;
        }
        table.SelectedRows[0].Cells["affiliate"].Value = url;
        affiliateUrl.Clear();
        status.Text = "Da gan link Affiliate cho san pham dang chon.";
    }

    private void Renumber()
    {
        for (int i = 0; i < table.Rows.Count; i++)
            table.Rows[i].Cells["number"].Value = i + 1;
    }

    private void CopyOriginal() => CopySelected("original");
    private void CopyAffiliate() => CopySelected("affiliate");

    private void CopySelected(string column)
    {
        if (table.SelectedRows.Count == 0) return;
        string value = Convert.ToString(table.SelectedRows[0].Cells[column].Value) ?? "";
        if (!string.IsNullOrWhiteSpace(value)) Clipboard.SetText(value);
        else MessageBox.Show("Dong nay chua co du lieu.");
    }

    private void ExportCsv()
    {
        using var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "Shopee_Affiliate_Products.csv" };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        var sb = new StringBuilder();
        sb.AppendLine("STT,Product URL,Affiliate URL");
        foreach (DataGridViewRow row in table.Rows)
        {
            string a = Convert.ToString(row.Cells["number"].Value) ?? "";
            string b = Convert.ToString(row.Cells["original"].Value) ?? "";
            string c = Convert.ToString(row.Cells["affiliate"].Value) ?? "";
            sb.AppendLine($"{Csv(a)},{Csv(b)},{Csv(c)}");
        }
        File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
        MessageBox.Show("Da xuat CSV thanh cong.");
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
