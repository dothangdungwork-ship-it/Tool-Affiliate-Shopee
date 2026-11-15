using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ToolAffiliate
{
    public sealed class FbGroupEntry
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public bool Checked { get; set; }
        public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Url : $"{Name} | {Url}";
    }

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
        public List<FbGroupEntry> FbGroups { get; set; } = new();
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
        private readonly TabControl browserTabs = new();
        private readonly WebView2 zaloBrowser = new();
        private readonly WebView2 fbBrowser = new();

        private readonly TextBox apiKeyBox = new();
        private readonly TextBox affIdBox = new();
        private readonly TextBox subIdBox = new();
        private readonly TextBox keywordsBox = new();
        private readonly NumericUpDown batchCountInput = new();
        private readonly NumericUpDown cycleMinutesInput = new();
        private readonly TextBox messageBox = new();

        private readonly CheckedListBox zaloGroupsCheckList = new();
        private readonly TextBox manualZaloGroupInput = new();

        private readonly CheckedListBox fbGroupsCheckList = new();
        private readonly TextBox manualFbGroupInput = new();

        private readonly NumericUpDown minDelayInput = new();
        private readonly NumericUpDown maxDelayInput = new();
        private readonly Label statusLabel = new();
        private readonly List<CoreWebView2Frame> zaloFrames = new();
        private readonly HashSet<string> sentItemIds = new(StringComparer.OrdinalIgnoreCase);

        private readonly string zaloProfilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolAffiliate", "ZaloProfile");
        private readonly string fbProfilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolAffiliate", "FacebookProfile");
        private readonly string configFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolAffiliate", "addlivetag-config-v4.json");

        private bool initialized;
        private bool autoPilotRunning;
        private CancellationTokenSource? sendCts;
        private int keywordRotateIndex;

        private const string B64_SCAN_FB_GROUPS = "KCgpID0+IHsKICB0cnkgewogICAgY29uc3QgYW5jaG9ycyA9IFsuLi5kb2N1bWVudC5xdWVyeVNlbGVjdG9yQWxsKCdhW2hyZWYqPSIvZ3JvdXBzLyJdJyldOwogICAgY29uc3QgZm91bmQgPSBbXTsKICAgIGNvbnN0IHNlZW4gPSBuZXcgU2V0KCk7CiAgICBjb25zdCBpZ25vcmVOYW1lcyA9IC9eKG5ow7NtfGdyb3Vwc3xraMOhbSBwaMOhfGRpc2NvdmVyfGLhuqNuZyBmZWVkIGPhu6dhIGLhuqFufHlvdXIgZmVlZHx04bqhbyBuaMOzbSBt4bubaXxjcmVhdGUgbmV3IGdyb3VwfHhlbSBuaMOzbXx2aWV3IGdyb3VwfHRoYW0gZ2lhfGpvaW4pJC9pOwogICAgZm9yIChjb25zdCBhIG9mIGFuY2hvcnMpIHsKICAgICAgbGV0IHU7CiAgICAgIHRyeSB7IHUgPSBuZXcgVVJMKGEuaHJlZiwgbG9jYXRpb24ub3JpZ2luKTsgfSBjYXRjaCB7IGNvbnRpbnVlOyB9CiAgICAgIGNvbnN0IG0gPSB1LnBhdGhuYW1lLm1hdGNoKC9eXC9ncm91cHNcLyhbXlwvPyNdKylcLz8kL2kpOwogICAgICBpZiAoIW0pIGNvbnRpbnVlOwogICAgICBjb25zdCBzbHVnID0gbVsxXTsKICAgICAgaWYgKC9eKGZlZWR8ZGlzY292ZXJ8am9pbnN8Y3JlYXRlfG5vdGlmaWNhdGlvbnN8c2VhcmNofGNhdGVnb3JpZXMpJC9pLnRlc3Qoc2x1ZykpIGNvbnRpbnVlOwogICAgICBjb25zdCBjbGVhblVybCA9ICdodHRwczovL3d3dy5mYWNlYm9vay5jb20vZ3JvdXBzLycgKyBzbHVnICsgJy8nOwogICAgICBpZiAoc2Vlbi5oYXMoY2xlYW5VcmwpKSBjb250aW51ZTsKICAgICAgY29uc3QgcmF3VGV4dCA9IChhLmlubmVyVGV4dCB8fCBhLmdldEF0dHJpYnV0ZSgnYXJpYS1sYWJlbCcpIHx8ICcnKS5zcGxpdCgnXG4nKS5tYXAoeCA9PiB4LnRyaW0oKSkuZmlsdGVyKEJvb2xlYW4pWzBdIHx8ICcnOwogICAgICBpZiAoIXJhd1RleHQgfHwgcmF3VGV4dC5sZW5ndGggPCAyIHx8IHJhd1RleHQubGVuZ3RoID4gMTAwIHx8IGlnbm9yZU5hbWVzLnRlc3QocmF3VGV4dCkpIGNvbnRpbnVlOwogICAgICBzZWVuLmFkZChjbGVhblVybCk7CiAgICAgIGZvdW5kLnB1c2goeyBuYW1lOiByYXdUZXh0LCB1cmw6IGNsZWFuVXJsIH0pOwogICAgfQogICAgcmV0dXJuIHsgb2s6IHRydWUsIGdyb3VwczogZm91bmQgfTsKICB9IGNhdGNoIChlKSB7CiAgICByZXR1cm4geyBvazogZmFsc2UsIGdyb3VwczogW10gfTsKICB9Cn0pKCk7";
        private const string B64_OPEN_FB_COMPOSER = "KCgpID0+IHsKICB0cnkgewogICAgY29uc3QgdmlzaWJsZSA9IGUgPT4gewogICAgICBpZiAoIWUpIHJldHVybiBmYWxzZTsKICAgICAgY29uc3QgciA9IGUuZ2V0Qm91bmRpbmdDbGllbnRSZWN0KCk7CiAgICAgIHJldHVybiByLndpZHRoID4gNjAgJiYgci5oZWlnaHQgPiAyMCAmJiByLnRvcCA+IDUwICYmIHIudG9wIDwgNzUwOwogICAgfTsKICAgIGNvbnN0IGNhbmRpZGF0ZXMgPSBbLi4uZG9jdW1lbnQucXVlcnlTZWxlY3RvckFsbCgnW3JvbGU9ImJ1dHRvbiJdLCBzcGFuLCBkaXYnKV0uZmlsdGVyKHZpc2libGUpOwogICAgY29uc3QgdHJpZ2dlciA9IGNhbmRpZGF0ZXMuZmluZChlID0+IHsKICAgICAgY29uc3QgdCA9IChlLmlubmVyVGV4dCB8fCBlLmdldEF0dHJpYnV0ZSgnYXJpYS1sYWJlbCcpIHx8ICcnKS50cmltKCkudG9Mb3dlckNhc2UoKTsKICAgICAgcmV0dXJuIC9eKGLhuqFuIHZp4bq/dCBnw6wgxJFpfHZp4bq/dCBnw6wgxJHDs3xi4bqhbiDEkWFuZyBuZ2jEqSBnw6x8d3JpdGUgc29tZXRoaW5nfGNyZWF0ZSBhIHB1YmxpYyBwb3N0fHThuqFvIGLDoGkgdmnhur90KS9pLnRlc3QodCk7CiAgICB9KTsKICAgIGlmICghdHJpZ2dlcikgcmV0dXJuIHsgb2s6IGZhbHNlLCBtZXNzYWdlOiAnS2jDtG5nIHTDrG0gdGjhuqV5IMO0IFThuqFvIGLDoGkgdmnhur90IHRyb25nIG5ow7NtIG7DoHkuJyB9OwogICAgY29uc3QgYnRuID0gdHJpZ2dlci5jbG9zZXN0KCdbcm9sZT0iYnV0dG9uIl0nKSB8fCB0cmlnZ2VyOwogICAgYnRuLnNjcm9sbEludG9WaWV3KHsgYmxvY2s6ICdjZW50ZXInIH0pOwogICAgYnRuLmNsaWNrKCk7CiAgICByZXR1cm4geyBvazogdHJ1ZSB9OwogIH0gY2F0Y2ggKGUpIHsKICAgIHJldHVybiB7IG9rOiBmYWxzZSwgbWVzc2FnZTogU3RyaW5nKGUpIH07CiAgfQp9KSgpOw==";
        private const string B64_FILL_FB_FN = "KChtc2cpID0+IHsKICB0cnkgewogICAgY29uc3QgdmlzaWJsZSA9IGUgPT4gewogICAgICBpZiAoIWUpIHJldHVybiBmYWxzZTsKICAgICAgY29uc3QgciA9IGUuZ2V0Qm91bmRpbmdDbGllbnRSZWN0KCk7CiAgICAgIHJldHVybiByLndpZHRoID4gMTIwICYmIHIuaGVpZ2h0ID4gMzA7CiAgICB9OwogICAgY29uc3QgZGlhbG9nID0gZG9jdW1lbnQucXVlcnlTZWxlY3RvcignW3JvbGU9ImRpYWxvZyJdJykgfHwgZG9jdW1lbnQuYm9keTsKICAgIGNvbnN0IGVkaXRvcnMgPSBbLi4uZGlhbG9nLnF1ZXJ5U2VsZWN0b3JBbGwoJ1tjb250ZW50ZWRpdGFibGU9InRydWUiXVtyb2xlPSJ0ZXh0Ym94Il0sIFtjb250ZW50ZWRpdGFibGU9InRydWUiXSwgdGV4dGFyZWEnKV0uZmlsdGVyKHZpc2libGUpOwogICAgY29uc3QgZWRpdG9yID0gZWRpdG9yc1swXTsKICAgIGlmICghZWRpdG9yKSByZXR1cm4geyBvazogZmFsc2UsIG1lc3NhZ2U6ICdDaMawYSBoaeG7h24gw7Qgbmjhuq1wIG7hu5lpIGR1bmcgYsOgaSB2aeG6v3QgRmFjZWJvb2suJyB9OwogICAgZWRpdG9yLmZvY3VzKCk7CiAgICBlZGl0b3IuY2xpY2soKTsKICAgIGxldCBpbnNlcnRlZCA9IGZhbHNlOwogICAgdHJ5IHsKICAgICAgY29uc3QgZHQgPSBuZXcgRGF0YVRyYW5zZmVyKCk7CiAgICAgIGR0LnNldERhdGEoJ3RleHQvcGxhaW4nLCBtc2cpOwogICAgICBjb25zdCBwYXN0ZUV2dCA9IG5ldyBDbGlwYm9hcmRFdmVudCgncGFzdGUnLCB7IGJ1YmJsZXM6IHRydWUsIGNhbmNlbGFibGU6IHRydWUsIGNsaXBib2FyZERhdGE6IGR0IH0pOwogICAgICBlZGl0b3IuZGlzcGF0Y2hFdmVudChwYXN0ZUV2dCk7CiAgICAgIGluc2VydGVkID0gKGVkaXRvci5pbm5lclRleHQgfHwgJycpLnRyaW0oKS5sZW5ndGggPiAxMDsKICAgIH0gY2F0Y2gge30KICAgIGlmICghaW5zZXJ0ZWQpIHsKICAgICAgdHJ5IHsgaW5zZXJ0ZWQgPSBkb2N1bWVudC5leGVjQ29tbWFuZCgnaW5zZXJ0VGV4dCcsIGZhbHNlLCBtc2cpOyB9IGNhdGNoIHt9CiAgICB9CiAgICBpZiAoIShlZGl0b3IuaW5uZXJUZXh0IHx8ICcnKS50cmltKCkpIHsKICAgICAgZWRpdG9yLmRpc3BhdGNoRXZlbnQobmV3IElucHV0RXZlbnQoJ2JlZm9yZWlucHV0JywgeyBidWJibGVzOiB0cnVlLCBjYW5jZWxhYmxlOiB0cnVlLCBpbnB1dFR5cGU6ICdpbnNlcnRUZXh0JywgZGF0YTogbXNnIH0pKTsKICAgICAgZWRpdG9yLnRleHRDb250ZW50ID0gbXNnOwogICAgICBlZGl0b3IuZGlzcGF0Y2hFdmVudChuZXcgSW5wdXRFdmVudCgnaW5wdXQnLCB7IGJ1YmJsZXM6IHRydWUsIGlucHV0VHlwZTogJ2luc2VydFRleHQnLCBkYXRhOiBtc2cgfSkpOwogICAgfQogICAgcmV0dXJuIHsgb2s6IHRydWUgfTsKICB9IGNhdGNoIChlKSB7CiAgICByZXR1cm4geyBvazogZmFsc2UsIG1lc3NhZ2U6IFN0cmluZyhlKSB9OwogIH0KfSk=";
        private const string B64_CLICK_FB_POST = "KCgpID0+IHsKICB0cnkgewogICAgY29uc3QgZGlhbG9nID0gZG9jdW1lbnQucXVlcnlTZWxlY3RvcignW3JvbGU9ImRpYWxvZyJdJykgfHwgZG9jdW1lbnQuYm9keTsKICAgIGNvbnN0IGJ1dHRvbnMgPSBbLi4uZGlhbG9nLnF1ZXJ5U2VsZWN0b3JBbGwoJ1thcmlhLWxhYmVsPSLEkMSDbmciXSwgW2FyaWEtbGFiZWw9IlBvc3QiXSwgW3JvbGU9ImJ1dHRvbiJdJyldLmZpbHRlcihlID0+IHsKICAgICAgY29uc3QgciA9IGUuZ2V0Qm91bmRpbmdDbGllbnRSZWN0KCk7CiAgICAgIHJldHVybiByLndpZHRoID4gNDAgJiYgci5oZWlnaHQgPiAyMCAmJiBlLmdldEF0dHJpYnV0ZSgnYXJpYS1kaXNhYmxlZCcpICE9PSAndHJ1ZScgJiYgIWUuZGlzYWJsZWQ7CiAgICB9KTsKICAgIGNvbnN0IHBvc3RCdG4gPSBidXR0b25zLmZpbmQoYiA9PiB7CiAgICAgIGNvbnN0IGxhYmVsID0gKGIuZ2V0QXR0cmlidXRlKCdhcmlhLWxhYmVsJykgfHwgYi5pbm5lclRleHQgfHwgJycpLnRyaW0oKS50b0xvd2VyQ2FzZSgpOwogICAgICByZXR1cm4gbGFiZWwgPT09ICfEkcSDbmcnIHx8IGxhYmVsID09PSAncG9zdCc7CiAgICB9KTsKICAgIGlmICghcG9zdEJ0bikgcmV0dXJuIHsgb2s6IGZhbHNlLCBtZXNzYWdlOiAnS2jDtG5nIHTDrG0gdGjhuqV5IG7DunQgxJDEg25nIChob+G6t2MgbsO6dCDEkMSDbmcgxJFhbmcgYuG7iyBraMOzYSkuJyB9OwogICAgcG9zdEJ0bi5jbGljaygpOwogICAgcmV0dXJuIHsgb2s6IHRydWUsIG1lc3NhZ2U6ICfEkMOjIHThu7EgxJHhu5luZyDEkWnhu4FuIHbDoCBi4bqlbSBuw7p0IMSQxINuZyBsw6puIG5ow7NtIEZhY2Vib29rIScgfTsKICB9IGNhdGNoIChlKSB7CiAgICByZXR1cm4geyBvazogZmFsc2UsIG1lc3NhZ2U6IFN0cmluZyhlKSB9OwogIH0KfSkoKTs=";
        private const string B64_ADD_OPEN_ZALO = "KCgpID0+IHsKICB0cnkgewogICAgY29uc3QgdGl0bGVOb2RlcyA9IFsuLi5kb2N1bWVudC5xdWVyeVNlbGVjdG9yQWxsKCdoZWFkZXIgLmhlYWRlci10aXRsZSwgLnRocmVhZC1jaGF0LXRpdGxlLCBbZGF0YS1pZCo9ImRpdl9NYWluX1RhYk1zZ19IZWFkZXIiXSBzcGFuLCAudGl0bGUtaGVhZGVyLCBoMSwgaDIsIGgzLCBzcGFuLCBkaXYnKV0uZmlsdGVyKGUgPT4gewogICAgICBjb25zdCByID0gZS5nZXRCb3VuZGluZ0NsaWVudFJlY3QoKTsKICAgICAgcmV0dXJuIHIubGVmdCA+IDMyMCAmJiByLnRvcCA+PSAwICYmIHIudG9wIDwgOTUgJiYgci53aWR0aCA+IDMwICYmIHIuaGVpZ2h0ID4gMTQgJiYgci5oZWlnaHQgPCA0NTsKICAgIH0pOwogICAgZm9yIChjb25zdCBlbCBvZiB0aXRsZU5vZGVzKSB7CiAgICAgIGNvbnN0IHQgPSAoZWwuaW5uZXJUZXh0IHx8ICcnKS5zcGxpdCgnXG4nKVswXS50cmltKCk7CiAgICAgIGlmICh0ICYmIHQubGVuZ3RoID4gMSAmJiB0Lmxlbmd0aCA8IDkwICYmICEvdGjDoG5oIHZpw6pufG1lbWJlcnN8dMOsbSBraeG6v218c2VhcmNofHbhu6thIHRydXkgY+G6rXB8b25saW5lL2kudGVzdCh0KSkgewogICAgICAgIHJldHVybiB7IG9rOiB0cnVlLCBuYW1lOiB0IH07CiAgICAgIH0KICAgIH0KICAgIHJldHVybiB7IG9rOiBmYWxzZSwgbmFtZTogJycgfTsKICB9IGNhdGNoIChlKSB7CiAgICByZXR1cm4geyBvazogZmFsc2UsIG5hbWU6ICcnIH07CiAgfQp9KSgpOw==";
        private const string B64_OPEN_ZALO_CONTACTS = "KCgpID0+IHsKICB0cnkgewogICAgY29uc3QgY29udGFjdHNUYWIgPSBkb2N1bWVudC5xdWVyeVNlbGVjdG9yKCdbZGF0YS1pZCo9Ik1haW5fVGFiQ1QiXSwgW3RpdGxlKj0iRGFuaCBi4bqhIl0sIFt0aXRsZSo9IkNvbnRhY3RzIl0sIC5mYS1Db250YWN0XzI4X0xpbmUsIFtkYXRhLXRyYW5zbGF0ZS10aXRsZSo9IlNUUl9UQUJfQ09OVEFDVCJdJyk7CiAgICBpZiAoY29udGFjdHNUYWIpIHsKICAgICAgKGNvbnRhY3RzVGFiLmNsb3Nlc3QoJ1tyb2xlPSJidXR0b24iXSwgZGl2LCBsaScpIHx8IGNvbnRhY3RzVGFiKS5jbGljaygpOwogICAgICByZXR1cm4geyBvazogdHJ1ZSB9OwogICAgfQogICAgcmV0dXJuIHsgb2s6IGZhbHNlIH07CiAgfSBjYXRjaCB7CiAgICByZXR1cm4geyBvazogZmFsc2UgfTsKICB9Cn0pKCk7";
        private const string B64_CLICK_ZALO_GROUP_MENU = "KCgpID0+IHsKICB0cnkgewogICAgY29uc3Qgbm9kZXMgPSBbLi4uZG9jdW1lbnQucXVlcnlTZWxlY3RvckFsbCgnZGl2LCBzcGFuLCBwLCBsaScpXS5maWx0ZXIoZSA9PiB7CiAgICAgIGNvbnN0IHIgPSBlLmdldEJvdW5kaW5nQ2xpZW50UmVjdCgpOwogICAgICByZXR1cm4gci5sZWZ0ID4gNDAgJiYgci5sZWZ0IDwgMzgwICYmIHIudG9wID4gNTAgJiYgci50b3AgPCAzNTAgJiYgci53aWR0aCA+IDQwICYmIHIuaGVpZ2h0ID4gMTU7CiAgICB9KTsKICAgIGNvbnN0IGdycE1lbnUgPSBub2Rlcy5maW5kKGUgPT4gL14oZGFuaCBzw6FjaCBuaMOzbXxuaMOzbSB2w6AgY+G7mW5nIMSR4buTbmd8Z3JvdXAgbGlzdHxqb2luZWQgZ3JvdXBzKSQvaS50ZXN0KChlLmlubmVyVGV4dCB8fCAnJykudHJpbSgpKSk7CiAgICBpZiAoZ3JwTWVudSkgewogICAgICAoZ3JwTWVudS5jbG9zZXN0KCcuY29udi1pdGVtLCBbcm9sZT0ibGlzdGl0ZW0iXSwgZGl2JykgfHwgZ3JwTWVudSkuY2xpY2soKTsKICAgICAgcmV0dXJuIHsgb2s6IHRydWUgfTsKICAgIH0KICAgIHJldHVybiB7IG9rOiBmYWxzZSB9OwogIH0gY2F0Y2ggewogICAgcmV0dXJuIHsgb2s6IGZhbHNlIH07CiAgfQp9KSgpOw==";
        private const string B64_EXTRACT_ZALO_GROUPS = "KCgpID0+IHsKICB0cnkgewogICAgY29uc3QgdmlzaWJsZSA9IGUgPT4gewogICAgICBpZiAoIWUpIHJldHVybiBmYWxzZTsKICAgICAgY29uc3QgciA9IGUuZ2V0Qm91bmRpbmdDbGllbnRSZWN0KCk7CiAgICAgIGNvbnN0IHMgPSBnZXRDb21wdXRlZFN0eWxlKGUpOwogICAgICByZXR1cm4gci53aWR0aCA+IDMwICYmIHIuaGVpZ2h0ID4gMTQgJiYgcy5kaXNwbGF5ICE9PSAnbm9uZScgJiYgcy52aXNpYmlsaXR5ICE9PSAnaGlkZGVuJzsKICAgIH07CiAgICBjb25zdCBpZ25vcmUgPSAvXih04bqldCBj4bqjfGNoxrBhIMSR4buNY3xwaMOibiBsb+G6oWl8dMOsbSBraeG6v218ZGFuaCBzw6FjaCBi4bqhbiBiw6h8ZGFuaCBzw6FjaCBuaMOzbXxuaMOzbSB2w6AgY+G7mW5nIMSR4buTbmd8bOG7nWkgbeG7nWkga+G6v3QgYuG6oW58bOG7nWkgbeG7nWkgdsOgbyBuaMOzbXxteSBkb2N1bWVudHN8Y2xvdWQgY+G7p2EgdMO0aXx0cnV54buBbiBmaWxlfMSR4buTbmcgYuG7mSBuZ2F5fHThuqNpIG5nYXl8dGluIG5o4bqvbikkL2k7CiAgICBjb25zdCBuYW1lcyA9IFtdOwogICAgY29uc3QgYWRkTmFtZSA9IHJhdyA9PiB7CiAgICAgIGNvbnN0IGNsZWFuID0gKHJhdyB8fCAnJykuc3BsaXQoJ1xuJylbMF0ucmVwbGFjZSgvXHMrL2csICcgJykudHJpbSgpOwogICAgICBpZiAoIWNsZWFuIHx8IGNsZWFuLmxlbmd0aCA8IDIgfHwgY2xlYW4ubGVuZ3RoID4gOTUpIHJldHVybjsKICAgICAgaWYgKGlnbm9yZS50ZXN0KGNsZWFuKSB8fCAvXlxkK1xzKih0aMOgbmggdmnDqm58bWVtYmVyc3xuaMOzbXxncm91cHMpL2kudGVzdChjbGVhbikpIHJldHVybjsKICAgICAgaWYgKCFuYW1lcy5pbmNsdWRlcyhjbGVhbikpIG5hbWVzLnB1c2goY2xlYW4pOwogICAgfTsKICAgIGNvbnN0IGFsbERpdnMgPSBbLi4uZG9jdW1lbnQucXVlcnlTZWxlY3RvckFsbCgnZGl2LCBsaSwgYXJ0aWNsZScpXS5maWx0ZXIodmlzaWJsZSk7CiAgICBmb3IgKGNvbnN0IGQgb2YgYWxsRGl2cykgewogICAgICBjb25zdCByID0gZC5nZXRCb3VuZGluZ0NsaWVudFJlY3QoKTsKICAgICAgaWYgKHIubGVmdCA+IDMwMCAmJiByLmhlaWdodCA+PSA0MCAmJiByLmhlaWdodCA8PSAxMzApIHsKICAgICAgICBjb25zdCB0ZXh0ID0gZC5pbm5lclRleHQgfHwgJyc7CiAgICAgICAgaWYgKC9cZCtccyoodGjDoG5oIHZpw6pufG1lbWJlcnMpL2kudGVzdCh0ZXh0KSkgewogICAgICAgICAgY29uc3QgbGluZXMgPSB0ZXh0LnNwbGl0KCdcbicpLm1hcCh4ID0+IHgudHJpbSgpKS5maWx0ZXIoQm9vbGVhbik7CiAgICAgICAgICBpZiAobGluZXMubGVuZ3RoID49IDIpIGFkZE5hbWUobGluZXNbMF0pOwogICAgICAgIH0KICAgICAgfQogICAgfQogICAgY29uc3QgbGVmdEl0ZW1zID0gWy4uLmRvY3VtZW50LnF1ZXJ5U2VsZWN0b3JBbGwoJy5jb252LWl0ZW0sIFtpZF49ImZyaWVuZC1pdGVtLSJdLCAubXNnLWl0ZW0sIFtyb2xlPSJsaXN0aXRlbSJdJyldLmZpbHRlcih2aXNpYmxlKTsKICAgIGZvciAoY29uc3QgZWwgb2YgbGVmdEl0ZW1zKSB7CiAgICAgIGNvbnN0IHIgPSBlbC5nZXRCb3VuZGluZ0NsaWVudFJlY3QoKTsKICAgICAgaWYgKHIubGVmdCA+IDQ1MCkgY29udGludWU7CiAgICAgIGNvbnN0IHRleHQgPSBlbC5pbm5lclRleHQgfHwgJyc7CiAgICAgIGlmICgvZ+G7rWkgbOG7nWkgY2jDoG98a+G6v3QgYuG6oW58xJHDoyBsw6AgYuG6oW4gYsOoL2kudGVzdCh0ZXh0KSkgY29udGludWU7CiAgICAgIGNvbnN0IGh0bWwgPSBlbC5pbm5lckhUTUwgfHwgJyc7CiAgICAgIGNvbnN0IGxpbmVzID0gdGV4dC5zcGxpdCgnXG4nKS5tYXAoeCA9PiB4LnRyaW0oKSkuZmlsdGVyKEJvb2xlYW4pOwogICAgICBpZiAobGluZXMubGVuZ3RoID09PSAwKSBjb250aW51ZTsKICAgICAgY29uc3Qgc2Vjb25kTGluZSA9IGxpbmVzWzFdIHx8ICcnOwogICAgICBjb25zdCB0aGlyZExpbmUgPSBsaW5lc1syXSB8fCAnJzsKICAgICAgY29uc3QgaXNHcm91cEluZGljYXRvciA9IGh0bWwuaW5jbHVkZXMoJ2dyb3VwJykgfHwgaHRtbC5pbmNsdWRlcygndXNlcnMnKSB8fCBodG1sLmluY2x1ZGVzKCdjb21tdW5pdHknKSB8fCAvXltBLVphLXrDgC3hu7kwLTlfXHNdezEsMjV9OlxzLy50ZXN0KHNlY29uZExpbmUpIHx8IC9eW0EtWmEtesOALeG7uTAtOV9cc117MSwyNX06XHMvLnRlc3QodGhpcmRMaW5lKSB8fCAvdGjDoG5oIHZpw6pufG5ow7NtfGdyb3VwfMSRw6MgdOG6oW8gbmjDs218xJHDoyB0aGFtIGdpYS9pLnRlc3QodGV4dCkgfHwgZWwucXVlcnlTZWxlY3RvckFsbCgnaW1nJykubGVuZ3RoID4gMTsKICAgICAgaWYgKGlzR3JvdXBJbmRpY2F0b3IpIHsKICAgICAgICBhZGROYW1lKGxpbmVzWzBdKTsKICAgICAgfQogICAgfQogICAgcmV0dXJuIHsgb2s6IHRydWUsIGdyb3VwczogbmFtZXMgfTsKICB9IGNhdGNoIChlKSB7CiAgICByZXR1cm4geyBvazogZmFsc2UsIGdyb3VwczogW10gfTsKICB9Cn0pKCk7";
        private const string B64_BACK_TO_MSG_TAB = "KCgpID0+IHsKICB0cnkgewogICAgY29uc3QgbXNnVGFiID0gZG9jdW1lbnQucXVlcnlTZWxlY3RvcignW2RhdGEtaWQqPSJNYWluX1RhYk1zZyJdLCBbdGl0bGUqPSJUaW4gbmjhuq9uIl0sIFt0aXRsZSo9Ik1lc3NhZ2VzIl0sIC5mYS1NZXNzYWdlXzI4X0xpbmUsIFtkYXRhLXRyYW5zbGF0ZS10aXRsZSo9IlNUUl9UQUJfTUVTU0FHRSJdJyk7CiAgICBpZiAobXNnVGFiKSAobXNnVGFiLmNsb3Nlc3QoJ1tyb2xlPSJidXR0b24iXSwgZGl2LCBsaScpIHx8IG1zZ1RhYikuY2xpY2soKTsKICAgIHJldHVybiB7IG9rOiB0cnVlIH07CiAgfSBjYXRjaCB7CiAgICByZXR1cm4geyBvazogZmFsc2UgfTsKICB9Cn0pKCk7";
        private const string B64_CLICK_ZALO_CONV_FN = "KChyYXdHcm91cCkgPT4gewogIHRyeSB7CiAgICBjb25zdCB0YXJnZXQgPSAocmF3R3JvdXAgfHwgJycpLnRvTG93ZXJDYXNlKCkucmVwbGFjZSgvXHMrL2csICcgJykudHJpbSgpOwogICAgaWYgKCF0YXJnZXQpIHJldHVybiB7IG9rOiBmYWxzZSwgbWVzc2FnZTogJ1TDqm4gbmjDs20gdHLhu5FuZy4nIH07CiAgICBjb25zdCBoZWFkZXJUaXRsZSA9IGRvY3VtZW50LnF1ZXJ5U2VsZWN0b3IoJ2hlYWRlciAuaGVhZGVyLXRpdGxlLCAudGhyZWFkLWNoYXQtdGl0bGUsIFtkYXRhLWlkKj0iZGl2X01haW5fVGFiTXNnX0hlYWRlciJdIHNwYW4sIC50aXRsZS1oZWFkZXInKTsKICAgIGlmIChoZWFkZXJUaXRsZSAmJiAoaGVhZGVyVGl0bGUuaW5uZXJUZXh0IHx8ICcnKS50b0xvd2VyQ2FzZSgpLnJlcGxhY2UoL1xzKy9nLCAnICcpLnRyaW0oKSA9PT0gdGFyZ2V0KSB7CiAgICAgIHJldHVybiB7IG9rOiB0cnVlLCBtZXNzYWdlOiAnTmjDs20gxJHDoyDEkcaw4bujYyBt4bufIHPhurVuOiAnICsgdGFyZ2V0IH07CiAgICB9CiAgICBjb25zdCB2aXNpYmxlID0gZSA9PiB7CiAgICAgIGlmICghZSkgcmV0dXJuIGZhbHNlOwogICAgICBjb25zdCByID0gZS5nZXRCb3VuZGluZ0NsaWVudFJlY3QoKTsKICAgICAgY29uc3QgcyA9IGdldENvbXB1dGVkU3R5bGUoZSk7CiAgICAgIHJldHVybiByLndpZHRoID4gMTUgJiYgci5oZWlnaHQgPiAxNCAmJiByLmxlZnQgPCA1MDAgJiYgci50b3AgPiA0NSAmJiBzLmRpc3BsYXkgIT09ICdub25lJyAmJiBzLnZpc2liaWxpdHkgIT09ICdoaWRkZW4nICYmIE51bWJlcihzLm9wYWNpdHkgfHwgMSkgPiAwOwogICAgfTsKICAgIGNvbnN0IGl0ZW1zID0gWy4uLmRvY3VtZW50LnF1ZXJ5U2VsZWN0b3JBbGwoJy5jb252LWl0ZW0sIFtpZF49ImZyaWVuZC1pdGVtLSJdLCAubXNnLWl0ZW0sIFtkYXRhLWlkKj0iQ29udiJdLCBbcm9sZT0ibGlzdGl0ZW0iXSwgLnZpcnR1YWxpemVkLXNjcm9sbCBkaXYnKV0uZmlsdGVyKHZpc2libGUpOwogICAgbGV0IGJlc3QgPSBudWxsOwogICAgbGV0IGJlc3RTY29yZSA9IDA7CiAgICBmb3IgKGNvbnN0IGVsIG9mIGl0ZW1zKSB7CiAgICAgIGlmIChlbC50YWdOYW1lID09PSAnSU5QVVQnIHx8IGVsLnRhZ05hbWUgPT09ICdURVhUQVJFQScgfHwgZWwuaXNDb250ZW50RWRpdGFibGUpIGNvbnRpbnVlOwogICAgICBjb25zdCBsaW5lcyA9IChlbC5pbm5lclRleHQgfHwgJycpLnNwbGl0KCdcbicpLm1hcCh4ID0+IHgucmVwbGFjZSgvXHMrL2csICcgJykudHJpbSgpKS5maWx0ZXIoQm9vbGVhbik7CiAgICAgIGlmIChsaW5lcy5sZW5ndGggPT09IDApIGNvbnRpbnVlOwogICAgICBjb25zdCBmaXJzdExpbmUgPSBsaW5lc1swXS50b0xvd2VyQ2FzZSgpOwogICAgICBjb25zdCBmdWxsVGV4dCA9IGxpbmVzLmpvaW4oJyAnKS50b0xvd2VyQ2FzZSgpOwogICAgICBpZiAoZnVsbFRleHQubGVuZ3RoID4gMjIwKSBjb250aW51ZTsKICAgICAgbGV0IHNjb3JlID0gMDsKICAgICAgaWYgKGZpcnN0TGluZSA9PT0gdGFyZ2V0KSBzY29yZSA9IDUwMCAtIGZ1bGxUZXh0Lmxlbmd0aDsKICAgICAgZWxzZSBpZiAoZmlyc3RMaW5lLmluY2x1ZGVzKHRhcmdldCkpIHNjb3JlID0gMzUwIC0gZmlyc3RMaW5lLmxlbmd0aDsKICAgICAgZWxzZSBpZiAoZnVsbFRleHQuaW5jbHVkZXModGFyZ2V0KSkgc2NvcmUgPSAyMDAgLSBNYXRoLm1pbigxNTAsIGZ1bGxUZXh0Lmxlbmd0aCk7CiAgICAgIGlmIChzY29yZSA+IGJlc3RTY29yZSkgewogICAgICAgIGJlc3RTY29yZSA9IHNjb3JlOwogICAgICAgIGJlc3QgPSBlbDsKICAgICAgfQogICAgfQogICAgaWYgKCFiZXN0KSByZXR1cm4geyBvazogZmFsc2UsIG1lc3NhZ2U6ICdDaMawYSB0aOG6pXkgbmjDs20gdHJvbmcgZGFuaCBzw6FjaC4nIH07CiAgICBjb25zdCByb3cgPSBiZXN0LmNsb3Nlc3QoJy5jb252LWl0ZW0sIFtpZF49ImZyaWVuZC1pdGVtLSJdLCAubXNnLWl0ZW0sIFtyb2xlPSJsaXN0aXRlbSJdJykgfHwgYmVzdDsKICAgIHJvdy5zY3JvbGxJbnRvVmlldyh7IGJsb2NrOiAnY2VudGVyJyB9KTsKICAgIGNvbnN0IHJlY3QgPSByb3cuZ2V0Qm91bmRpbmdDbGllbnRSZWN0KCk7CiAgICBjb25zdCBjeCA9IHJlY3QubGVmdCArIE1hdGgubWluKDgwLCByZWN0LndpZHRoIC8gMik7CiAgICBjb25zdCBjeSA9IHJlY3QudG9wICsgTWF0aC5taW4oMjUsIHJlY3QuaGVpZ2h0IC8gMik7CiAgICBjb25zdCB0YXJnZXRzVG9DbGljayA9IFtyb3csIGJlc3RdOwogICAgZm9yIChjb25zdCB0IG9mIHRhcmdldHNUb0NsaWNrKSB7CiAgICAgIHQuZGlzcGF0Y2hFdmVudChuZXcgUG9pbnRlckV2ZW50KCdwb2ludGVyZG93bicsIHsgYnViYmxlczogdHJ1ZSwgY2FuY2VsYWJsZTogdHJ1ZSwgY2xpZW50WDogY3gsIGNsaWVudFk6IGN5LCBidXR0b246IDAgfSkpOwogICAgICB0LmRpc3BhdGNoRXZlbnQobmV3IE1vdXNlRXZlbnQoJ21vdXNlZG93bicsIHsgYnViYmxlczogdHJ1ZSwgY2FuY2VsYWJsZTogdHJ1ZSwgY2xpZW50WDogY3gsIGNsaWVudFk6IGN5LCBidXR0b246IDAgfSkpOwogICAgICB0LmRpc3BhdGNoRXZlbnQobmV3IFBvaW50ZXJFdmVudCgncG9pbnRlcnVwJywgeyBidWJibGVzOiB0cnVlLCBjYW5jZWxhYmxlOiB0cnVlLCBjbGllbnRYOiBjeCwgY2xpZW50WTogY3ksIGJ1dHRvbjogMCB9KSk7CiAgICAgIHQuZGlzcGF0Y2hFdmVudChuZXcgTW91c2VFdmVudCgnbW91c2V1cCcsIHsgYnViYmxlczogdHJ1ZSwgY2FuY2VsYWJsZTogdHJ1ZSwgY2xpZW50WDogY3gsIGNsaWVudFk6IGN5LCBidXR0b246IDAgfSkpOwogICAgICB0LmRpc3BhdGNoRXZlbnQobmV3IE1vdXNlRXZlbnQoJ2NsaWNrJywgeyBidWJibGVzOiB0cnVlLCBjYW5jZWxhYmxlOiB0cnVlLCBjbGllbnRYOiBjeCwgY2xpZW50WTogY3ksIGJ1dHRvbjogMCB9KSk7CiAgICB9CiAgICByZXR1cm4geyBvazogdHJ1ZSwgbWVzc2FnZTogJ8SQw6MgbeG7nyBuaMOzbTogJyArIHRhcmdldCB9OwogIH0gY2F0Y2ggKGUpIHsKICAgIHJldHVybiB7IG9rOiBmYWxzZSwgbWVzc2FnZTogU3RyaW5nKGUpIH07CiAgfQp9KQ==";
        private const string B64_TYPE_ZALO_SEARCH_FN = "KCh0YXJnZXQpID0+IHsKICB0cnkgewogICAgY29uc3Qgc2VhcmNoSW5wdXQgPSBkb2N1bWVudC5xdWVyeVNlbGVjdG9yKCcjY29udGFjdC1zZWFyY2gtaW5wdXQsIGlucHV0W3BsYWNlaG9sZGVyKj0iVMOsbSBraeG6v20iXSwgaW5wdXRbcGxhY2Vob2xkZXIqPSJTZWFyY2giXSwgaW5wdXRbZGF0YS1pZCo9InR4dF9NYWluX1NlYXJjaCJdJyk7CiAgICBpZiAoIXNlYXJjaElucHV0KSByZXR1cm4geyBvazogZmFsc2UsIG1lc3NhZ2U6ICdLaMO0bmcgdMOsbSB0aOG6pXkgw7QgVMOsbSBraeG6v20gdHLDqm4gWmFsbyBXZWIuJyB9OwogICAgc2VhcmNoSW5wdXQuZm9jdXMoKTsKICAgIHNlYXJjaElucHV0LmNsaWNrKCk7CiAgICBjb25zdCBzZXR0ZXIgPSBPYmplY3QuZ2V0T3duUHJvcGVydHlEZXNjcmlwdG9yKEhUTUxJbnB1dEVsZW1lbnQucHJvdG90eXBlLCAndmFsdWUnKT8uc2V0OwogICAgaWYgKHNldHRlcikgc2V0dGVyLmNhbGwoc2VhcmNoSW5wdXQsICcnKTsKICAgIGVsc2Ugc2VhcmNoSW5wdXQudmFsdWUgPSAnJzsKICAgIHNlYXJjaElucHV0LmRpc3BhdGNoRXZlbnQobmV3IEV2ZW50KCdpbnB1dCcsIHsgYnViYmxlczogdHJ1ZSB9KSk7CiAgICBpZiAoc2V0dGVyKSBzZXR0ZXIuY2FsbChzZWFyY2hJbnB1dCwgdGFyZ2V0KTsKICAgIGVsc2Ugc2VhcmNoSW5wdXQudmFsdWUgPSB0YXJnZXQ7CiAgICBzZWFyY2hJbnB1dC5kaXNwYXRjaEV2ZW50KG5ldyBJbnB1dEV2ZW50KCdpbnB1dCcsIHsgYnViYmxlczogdHJ1ZSwgaW5wdXRUeXBlOiAnaW5zZXJ0VGV4dCcsIGRhdGE6IHRhcmdldCB9KSk7CiAgICBzZWFyY2hJbnB1dC5kaXNwYXRjaEV2ZW50KG5ldyBFdmVudCgnY2hhbmdlJywgeyBidWJibGVzOiB0cnVlIH0pKTsKICAgIHNlYXJjaElucHV0LmRpc3BhdGNoRXZlbnQobmV3IEtleWJvYXJkRXZlbnQoJ2tleXVwJywgeyBrZXk6ICdhJywgYnViYmxlczogdHJ1ZSB9KSk7CiAgICByZXR1cm4geyBvazogdHJ1ZSwgbWVzc2FnZTogJ8SQw6Mgbmjhuq1wIHTDqm4gbmjDs20gdsOgbyDDtCB0w6xtIGtp4bq/bS4nIH07CiAgfSBjYXRjaCAoZSkgewogICAgcmV0dXJuIHsgb2s6IGZhbHNlLCBtZXNzYWdlOiBTdHJpbmcoZSkgfTsKICB9Cn0p";
        private const string B64_SELECT_FIRST_SEARCH = "KCgpID0+IHsKICB0cnkgewogICAgY29uc3Qgc2VhcmNoSW5wdXQgPSBkb2N1bWVudC5xdWVyeVNlbGVjdG9yKCcjY29udGFjdC1zZWFyY2gtaW5wdXQsIGlucHV0W3BsYWNlaG9sZGVyKj0iVMOsbSBraeG6v20iXSwgaW5wdXRbcGxhY2Vob2xkZXIqPSJTZWFyY2giXScpOwogICAgaWYgKHNlYXJjaElucHV0KSB7CiAgICAgIHNlYXJjaElucHV0LmZvY3VzKCk7CiAgICAgIHNlYXJjaElucHV0LmRpc3BhdGNoRXZlbnQobmV3IEtleWJvYXJkRXZlbnQoJ2tleWRvd24nLCB7IGtleTogJ0Fycm93RG93bicsIGNvZGU6ICdBcnJvd0Rvd24nLCBrZXlDb2RlOiA0MCwgd2hpY2g6IDQwLCBidWJibGVzOiB0cnVlIH0pKTsKICAgICAgc2VhcmNoSW5wdXQuZGlzcGF0Y2hFdmVudChuZXcgS2V5Ym9hcmRFdmVudCgna2V5ZG93bicsIHsga2V5OiAnRW50ZXInLCBjb2RlOiAnRW50ZXInLCBrZXlDb2RlOiAxMywgd2hpY2g6IDEzLCBidWJibGVzOiB0cnVlIH0pKTsKICAgICAgcmV0dXJuIHsgb2s6IHRydWUsIG1lc3NhZ2U6ICfEkMOjIGNo4buNbiBr4bq/dCBxdeG6oyDEkeG6p3UgdGnDqm4uJyB9OwogICAgfQogICAgcmV0dXJuIHsgb2s6IGZhbHNlLCBtZXNzYWdlOiAnS2jDtG5nIGPDsyDDtCB0w6xtIGtp4bq/bS4nIH07CiAgfSBjYXRjaCAoZSkgewogICAgcmV0dXJuIHsgb2s6IGZhbHNlLCBtZXNzYWdlOiBTdHJpbmcoZSkgfTsKICB9Cn0pKCk7";
        private const string B64_FILL_ZALO_FN = "KChtc2cpID0+IHsKICB0cnkgewogICAgY29uc3QgdmlzaWJsZSA9IGUgPT4gewogICAgICBpZiAoIWUpIHJldHVybiBmYWxzZTsKICAgICAgY29uc3QgciA9IGUuZ2V0Qm91bmRpbmdDbGllbnRSZWN0KCk7CiAgICAgIGNvbnN0IHMgPSBnZXRDb21wdXRlZFN0eWxlKGUpOwogICAgICByZXR1cm4gci53aWR0aCA+IDIwICYmIHIuaGVpZ2h0ID4gMTAgJiYgcy5kaXNwbGF5ICE9PSAnbm9uZScgJiYgcy52aXNpYmlsaXR5ICE9PSAnaGlkZGVuJyAmJiBOdW1iZXIocy5vcGFjaXR5IHx8IDEpID4gMCAmJiAhZS5kaXNhYmxlZDsKICAgIH07CiAgICBjb25zdCB6YWxvU2VsZWN0b3JzID0gWycjcmljaElucHV0JywgJ1tpZF49ImlucHV0X2xpbmVfIl0nLCAnLnJpY2gtaW5wdXQnLCAnW2RhdGEtdHJhbnNsYXRlLXBsYWNlaG9sZGVyKj0iTmjhuq1wIEAiXScsICdbZGF0YS10cmFuc2xhdGUtcGxhY2Vob2xkZXIqPSJ0aW4gbmjhuq9uIl0nLCAnW2NvbnRlbnRlZGl0YWJsZT0idHJ1ZSJdJywgJ1tyb2xlPSJ0ZXh0Ym94Il0nLCAndGV4dGFyZWEnXTsKICAgIGxldCBjYW5kaWRhdGVzID0gW107CiAgICBmb3IgKGNvbnN0IHNlbCBvZiB6YWxvU2VsZWN0b3JzKSB7CiAgICAgIGZvciAoY29uc3QgZWwgb2YgZG9jdW1lbnQucXVlcnlTZWxlY3RvckFsbChzZWwpKSB7CiAgICAgICAgaWYgKHZpc2libGUoZWwpICYmICFjYW5kaWRhdGVzLmluY2x1ZGVzKGVsKSkgewogICAgICAgICAgY29uc3QgaWRBbmRDbGFzcyA9ICgoZWwuaWQgfHwgJycpICsgJyAnICsgKGVsLmNsYXNzTmFtZSB8fCAnJykgKyAnICcgKyAoZWwuZ2V0QXR0cmlidXRlKCdwbGFjZWhvbGRlcicpIHx8ICcnKSkudG9Mb3dlckNhc2UoKTsKICAgICAgICAgIGlmICgvY29udGFjdC1zZWFyY2h8c2VhcmNoLWlucHV0fHTDrG0ga2nhur9tLy50ZXN0KGlkQW5kQ2xhc3MpKSBjb250aW51ZTsKICAgICAgICAgIGNhbmRpZGF0ZXMucHVzaChlbCk7CiAgICAgICAgfQogICAgICB9CiAgICB9CiAgICBjYW5kaWRhdGVzLnNvcnQoKGEsIGIpID0+IHsKICAgICAgY29uc3QgYXIgPSBhLmdldEJvdW5kaW5nQ2xpZW50UmVjdCgpLCBiciA9IGIuZ2V0Qm91bmRpbmdDbGllbnRSZWN0KCk7CiAgICAgIGNvbnN0IGFJZCA9IChhLmlkIHx8ICcnKS5pbmNsdWRlcygncmljaElucHV0JykgfHwgKGEuaWQgfHwgJycpLmluY2x1ZGVzKCdpbnB1dF9saW5lJykgPyAxMDAwIDogMDsKICAgICAgY29uc3QgYklkID0gKGIuaWQgfHwgJycpLmluY2x1ZGVzKCdyaWNoSW5wdXQnKSB8fCAoYi5pZCB8fCAnJykuaW5jbHVkZXMoJ2lucHV0X2xpbmUnKSA/IDEwMDAgOiAwOwogICAgICByZXR1cm4gKGJJZCArIGJyLmJvdHRvbSAqIDIgKyBici53aWR0aCkgLSAoYUlkICsgYXIuYm90dG9tICogMiArIGFyLndpZHRoKTsKICAgIH0pOwogICAgbGV0IGVkaXRvciA9IGNhbmRpZGF0ZXNbMF07CiAgICBpZiAoIWVkaXRvcikgewogICAgICByZXR1cm4geyBvazogZmFsc2UsIG1lc3NhZ2U6ICdDaMawYSBuaOG6rW4gZGnhu4duIMSRxrDhu6NjIMO0IG5o4bqtcCB0aW4gbmjhuq9uLiBIw6N5IG3hu58gbeG7mXQgbmjDs20gdHLDqm4gWmFsbyBXZWIuJyB9OwogICAgfQogICAgaWYgKGVkaXRvci5jbG9zZXN0KCdbY29udGVudGVkaXRhYmxlPSJ0cnVlIl0nKSkgewogICAgICBlZGl0b3IgPSBlZGl0b3IuY2xvc2VzdCgnW2NvbnRlbnRlZGl0YWJsZT0idHJ1ZSJdJyk7CiAgICB9CiAgICB3aW5kb3cuX196YWxvTGFzdEVkaXRvciA9IGVkaXRvcjsKICAgIGVkaXRvci5jbGljaygpOwogICAgZWRpdG9yLmZvY3VzKCk7CiAgICBpZiAoZWRpdG9yLmlzQ29udGVudEVkaXRhYmxlIHx8IGVkaXRvci5nZXRBdHRyaWJ1dGUoJ2NvbnRlbnRlZGl0YWJsZScpID09PSAndHJ1ZScpIHsKICAgICAgY29uc3Qgc2VsZWN0aW9uID0gd2luZG93LmdldFNlbGVjdGlvbigpOwogICAgICBjb25zdCByYW5nZSA9IGRvY3VtZW50LmNyZWF0ZVJhbmdlKCk7CiAgICAgIHJhbmdlLnNlbGVjdE5vZGVDb250ZW50cyhlZGl0b3IpOwogICAgICBzZWxlY3Rpb24ucmVtb3ZlQWxsUmFuZ2VzKCk7CiAgICAgIHNlbGVjdGlvbi5hZGRSYW5nZShyYW5nZSk7CiAgICAgIGRvY3VtZW50LmV4ZWNDb21tYW5kKCdkZWxldGUnLCBmYWxzZSwgbnVsbCk7CiAgICAgIGxldCBwYXN0ZWRPayA9IGZhbHNlOwogICAgICB0cnkgewogICAgICAgIGNvbnN0IGR0ID0gbmV3IERhdGFUcmFuc2ZlcigpOwogICAgICAgIGR0LnNldERhdGEoJ3RleHQvcGxhaW4nLCBtc2cpOwogICAgICAgIGNvbnN0IHBhc3RlRXZlbnQgPSBuZXcgQ2xpcGJvYXJkRXZlbnQoJ3Bhc3RlJywgeyBidWJibGVzOiB0cnVlLCBjYW5jZWxhYmxlOiB0cnVlLCBjbGlwYm9hcmREYXRhOiBkdCB9KTsKICAgICAgICBlZGl0b3IuZGlzcGF0Y2hFdmVudChwYXN0ZUV2ZW50KTsKICAgICAgICBjb25zdCBjdXJyZW50VGV4dCA9IChlZGl0b3IuaW5uZXJUZXh0IHx8ICcnKS50cmltKCk7CiAgICAgICAgcGFzdGVkT2sgPSBjdXJyZW50VGV4dC5sZW5ndGggPj0gTWF0aC5taW4oMjUsIG1zZy5sZW5ndGgpICYmIGN1cnJlbnRUZXh0LmluY2x1ZGVzKCdodHRwJyk7CiAgICAgIH0gY2F0Y2gge30KICAgICAgaWYgKCFwYXN0ZWRPaykgewogICAgICAgIGVkaXRvci5pbm5lckhUTUwgPSAnJzsKICAgICAgICBjb25zdCBsaW5lcyA9IG1zZy5zcGxpdCgnXG4nKTsKICAgICAgICBsaW5lcy5mb3JFYWNoKChsaW5lLCBpZHgpID0+IHsKICAgICAgICAgIGNvbnN0IGRpdiA9IGRvY3VtZW50LmNyZWF0ZUVsZW1lbnQoJ2RpdicpOwogICAgICAgICAgZGl2LmlkID0gJ2lucHV0X2xpbmVfJyArIGlkeDsKICAgICAgICAgIGlmIChsaW5lLnRyaW0oKS5sZW5ndGggPiAwKSB7CiAgICAgICAgICAgIGRpdi50ZXh0Q29udGVudCA9IGxpbmU7CiAgICAgICAgICB9IGVsc2UgewogICAgICAgICAgICBkaXYuYXBwZW5kQ2hpbGQoZG9jdW1lbnQuY3JlYXRlRWxlbWVudCgnYnInKSk7CiAgICAgICAgICB9CiAgICAgICAgICBlZGl0b3IuYXBwZW5kQ2hpbGQoZGl2KTsKICAgICAgICB9KTsKICAgICAgICBjb25zdCByRW5kID0gZG9jdW1lbnQuY3JlYXRlUmFuZ2UoKTsKICAgICAgICByRW5kLnNlbGVjdE5vZGVDb250ZW50cyhlZGl0b3IpOwogICAgICAgIHJFbmQuY29sbGFwc2UoZmFsc2UpOwogICAgICAgIHNlbGVjdGlvbi5yZW1vdmVBbGxSYW5nZXMoKTsKICAgICAgICBzZWxlY3Rpb24uYWRkUmFuZ2UockVuZCk7CiAgICAgICAgZWRpdG9yLmRpc3BhdGNoRXZlbnQobmV3IElucHV0RXZlbnQoJ2lucHV0JywgeyBidWJibGVzOiB0cnVlLCBjYW5jZWxhYmxlOiB0cnVlLCBpbnB1dFR5cGU6ICdpbnNlcnRGcm9tUGFzdGUnLCBkYXRhOiBtc2cgfSkpOwogICAgICAgIGVkaXRvci5kaXNwYXRjaEV2ZW50KG5ldyBFdmVudCgnY2hhbmdlJywgeyBidWJibGVzOiB0cnVlIH0pKTsKICAgICAgfQogICAgfSBlbHNlIHsKICAgICAgY29uc3QgcHJvdG8gPSBlZGl0b3IudGFnTmFtZSA9PT0gJ1RFWFRBUkVBJyA/IEhUTUxUZXh0QXJlYUVsZW1lbnQucHJvdG90eXBlIDogSFRNTElucHV0RWxlbWVudC5wcm90b3R5cGU7CiAgICAgIGNvbnN0IHNldHRlciA9IE9iamVjdC5nZXRPd25Qcm9wZXJ0eURlc2NyaXB0b3IocHJvdG8sICd2YWx1ZScpPy5zZXQ7CiAgICAgIGlmIChzZXR0ZXIpIHNldHRlci5jYWxsKGVkaXRvciwgbXNnKTsKICAgICAgZWxzZSBlZGl0b3IudmFsdWUgPSBtc2c7CiAgICAgIGVkaXRvci5kaXNwYXRjaEV2ZW50KG5ldyBJbnB1dEV2ZW50KCdpbnB1dCcsIHsgYnViYmxlczogdHJ1ZSwgaW5wdXRUeXBlOiAnaW5zZXJ0VGV4dCcsIGRhdGE6IG1zZyB9KSk7CiAgICAgIGVkaXRvci5kaXNwYXRjaEV2ZW50KG5ldyBFdmVudCgnY2hhbmdlJywgeyBidWJibGVzOiB0cnVlIH0pKTsKICAgIH0KICAgIHJldHVybiB7IG9rOiB0cnVlLCBtZXNzYWdlOiAnxJDDoyDEkWnhu4FuIMSR4bqneSDEkeG7pyB0b8OgbiBi4buZIGPDoWMgZMOybmcgdsOgbyDDtCBjaGF0IFphbG8uJyB9OwogIH0gY2F0Y2ggKGVycikgewogICAgcmV0dXJuIHsgb2s6IGZhbHNlLCBtZXNzYWdlOiAnTOG7l2kgxJFp4buBbiBu4buZaSBkdW5nIHRyw6puIFphbG8gV2ViOiAnICsgKGVyciAmJiBlcnIubWVzc2FnZSA/IGVyci5tZXNzYWdlIDogU3RyaW5nKGVycikpIH07CiAgfQp9KQ==";
        private const string B64_TRIGGER_ZALO_SEND = "KCgpID0+IHsKICB0cnkgewogICAgY29uc3QgdmlzaWJsZSA9IGUgPT4gewogICAgICBpZiAoIWUpIHJldHVybiBmYWxzZTsKICAgICAgY29uc3QgciA9IGUuZ2V0Qm91bmRpbmdDbGllbnRSZWN0KCk7CiAgICAgIGNvbnN0IHMgPSBnZXRDb21wdXRlZFN0eWxlKGUpOwogICAgICByZXR1cm4gci53aWR0aCA+IDEwICYmIHIuaGVpZ2h0ID4gMTAgJiYgcy5kaXNwbGF5ICE9PSAnbm9uZScgJiYgcy52aXNpYmlsaXR5ICE9PSAnaGlkZGVuJyAmJiBOdW1iZXIocy5vcGFjaXR5IHx8IDEpID4gMCAmJiAhZS5kaXNhYmxlZDsKICAgIH07CiAgICBjb25zdCBlZGl0b3IgPSB3aW5kb3cuX196YWxvTGFzdEVkaXRvciB8fCBkb2N1bWVudC5xdWVyeVNlbGVjdG9yKCcjcmljaElucHV0LCBbY29udGVudGVkaXRhYmxlPSJ0cnVlIl0nKTsKICAgIGNvbnN0IHNlbmRTZWxlY3RvcnMgPSBbJ1tkYXRhLXRyYW5zbGF0ZS10aXRsZT0iU1RSX1NFTkQiXScsICdbdGl0bGU9Ikfhu61pIHRpbiBuaOG6r24iXScsICdbdGl0bGU9IlNlbmQgbWVzc2FnZSJdJywgJy5zZW5kLW1zZy1idG4nLCAnLmZhLVNlbnQtbXNnXzI0X0xpbmUnLCAnW2RhdGEtaWQqPSJidG5fTWFpbl9TZW5kIl0nXTsKICAgIGxldCBzZW5kQnRuID0gbnVsbDsKICAgIGZvciAoY29uc3Qgc2VsIG9mIHNlbmRTZWxlY3RvcnMpIHsKICAgICAgY29uc3QgZm91bmQgPSBkb2N1bWVudC5xdWVyeVNlbGVjdG9yKHNlbCk7CiAgICAgIGlmIChmb3VuZCAmJiB2aXNpYmxlKGZvdW5kKSkgewogICAgICAgIHNlbmRCdG4gPSBmb3VuZC5jbG9zZXN0KCdkaXZbcm9sZT0iYnV0dG9uIl0sIGJ1dHRvbiwgLnotLWJ0bi0tdjIsIC5zZW5kLW1zZy1idG4nKSB8fCBmb3VuZDsKICAgICAgICBicmVhazsKICAgICAgfQogICAgfQogICAgaWYgKCFzZW5kQnRuKSB7CiAgICAgIGNvbnN0IGFsbEJ1dHRvbnMgPSBbLi4uZG9jdW1lbnQucXVlcnlTZWxlY3RvckFsbCgnYnV0dG9uLCBbcm9sZT0iYnV0dG9uIl0sIC56LS1idG4tLXYyLCBpLmZhLVNlbnQtbXNnXzI0X0xpbmUnKV0uZmlsdGVyKHZpc2libGUpOwogICAgICBjb25zdCBtYXRjaGVkID0gYWxsQnV0dG9ucy5maW5kKGIgPT4gewogICAgICAgIGNvbnN0IG1ldGEgPSAoKGIuaW5uZXJUZXh0IHx8ICcnKSArICcgJyArIChiLnRpdGxlIHx8ICcnKSArICcgJyArIChiLmNsYXNzTmFtZSB8fCAnJykgKyAnICcgKyAoYi5nZXRBdHRyaWJ1dGUoJ2FyaWEtbGFiZWwnKSB8fCAnJykgKyAnICcgKyAoYi5nZXRBdHRyaWJ1dGUoJ2RhdGEtdHJhbnNsYXRlLXRpdGxlJykgfHwgJycpKS50b0xvd2VyQ2FzZSgpOwogICAgICAgIHJldHVybiAvc3RyX3NlbmR8Z+G7rWkgdGluIG5o4bqvbnxzZW5kIG1lc3NhZ2V8c2VudC1tc2d8c2VuZC1tc2d8Xmfhu61pJHxec2VuZCQvaS50ZXN0KG1ldGEudHJpbSgpKTsKICAgICAgfSk7CiAgICAgIGlmIChtYXRjaGVkKSBzZW5kQnRuID0gbWF0Y2hlZC5jbG9zZXN0KCdkaXZbcm9sZT0iYnV0dG9uIl0sIGJ1dHRvbiwgLnotLWJ0bi0tdjInKSB8fCBtYXRjaGVkOwogICAgfQogICAgaWYgKHNlbmRCdG4pIHsKICAgICAgY29uc3QgcmVjdCA9IHNlbmRCdG4uZ2V0Qm91bmRpbmdDbGllbnRSZWN0KCk7CiAgICAgIGNvbnN0IGN4ID0gcmVjdC5sZWZ0ICsgcmVjdC53aWR0aCAvIDI7CiAgICAgIGNvbnN0IGN5ID0gcmVjdC50b3AgKyByZWN0LmhlaWdodCAvIDI7CiAgICAgIHNlbmRCdG4uZGlzcGF0Y2hFdmVudChuZXcgUG9pbnRlckV2ZW50KCdwb2ludGVyZG93bicsIHsgYnViYmxlczogdHJ1ZSwgY2FuY2VsYWJsZTogdHJ1ZSwgY2xpZW50WDogY3gsIGNsaWVudFk6IGN5IH0pKTsKICAgICAgc2VuZEJ0bi5kaXNwYXRjaEV2ZW50KG5ldyBNb3VzZUV2ZW50KCdtb3VzZWRvd24nLCB7IGJ1YmJsZXM6IHRydWUsIGNhbmNlbGFibGU6IHRydWUsIGNsaWVudFg6IGN4LCBjbGllbnRZOiBjeSB9KSk7CiAgICAgIHNlbmRCdG4uZGlzcGF0Y2hFdmVudChuZXcgUG9pbnRlckV2ZW50KCdwb2ludGVydXAnLCB7IGJ1YmJsZXM6IHRydWUsIGNhbmNlbGFibGU6IHRydWUsIGNsaWVudFg6IGN4LCBjbGllbnRZOiBjeSB9KSk7CiAgICAgIHNlbmRCdG4uZGlzcGF0Y2hFdmVudChuZXcgTW91c2VFdmVudCgnbW91c2V1cCcsIHsgYnViYmxlczogdHJ1ZSwgY2FuY2VsYWJsZTogdHJ1ZSwgY2xpZW50WDogY3gsIGNsaWVudFk6IGN5IH0pKTsKICAgICAgc2VuZEJ0bi5jbGljaygpOwogICAgICByZXR1cm4geyBvazogdHJ1ZSwgbWVzc2FnZTogJ8SQw6MgdOG7sSDEkeG7mW5nIMSRaeG7gW4gxJHhuqd5IMSR4bunIGxpbmsgdsOgIGLhuqVtIG7DunQgR+G7rWkgdGjDoG5oIGPDtG5nIScgfTsKICAgIH0KICAgIGlmIChlZGl0b3IpIHsKICAgICAgZWRpdG9yLmZvY3VzKCk7CiAgICAgIGNvbnN0IGVudGVyT3B0cyA9IHsga2V5OiAnRW50ZXInLCBjb2RlOiAnRW50ZXInLCBrZXlDb2RlOiAxMywgd2hpY2g6IDEzLCBjaGFyQ29kZTogMTMsIGJ1YmJsZXM6IHRydWUsIGNhbmNlbGFibGU6IHRydWUgfTsKICAgICAgZWRpdG9yLmRpc3BhdGNoRXZlbnQobmV3IEtleWJvYXJkRXZlbnQoJ2tleWRvd24nLCBlbnRlck9wdHMpKTsKICAgICAgZWRpdG9yLmRpc3BhdGNoRXZlbnQobmV3IEtleWJvYXJkRXZlbnQoJ2tleXByZXNzJywgZW50ZXJPcHRzKSk7CiAgICAgIGVkaXRvci5kaXNwYXRjaEV2ZW50KG5ldyBLZXlib2FyZEV2ZW50KCdrZXl1cCcsIGVudGVyT3B0cykpOwogICAgICByZXR1cm4geyBvazogdHJ1ZSwgbWVzc2FnZTogJ8SQw6MgdOG7sSDEkeG7mW5nIMSRaeG7gW4gxJHhuqd5IMSR4bunIGxpbmsgdsOgIG5o4bqlbiBFbnRlciBn4butaSB0aMOgbmggY8O0bmchJyB9OwogICAgfQogICAgcmV0dXJuIHsgb2s6IGZhbHNlLCBtZXNzYWdlOiAnS2jDtG5nIHTDrG0gdGjhuqV5IG7DunQgR+G7rWkgdHLDqm4gWmFsbyBXZWIuJyB9OwogIH0gY2F0Y2ggKGVycikgewogICAgcmV0dXJuIHsgb2s6IGZhbHNlLCBtZXNzYWdlOiAnTOG7l2kga8OtY2ggaG/huqF0IG7DunQgR+G7rWk6ICcgKyAoZXJyICYmIGVyci5tZXNzYWdlID8gZXJyLm1lc3NhZ2UgOiBTdHJpbmcoZXJyKSkgfTsKICB9Cn0pKCk7";

        private static string DecodeB64(string b64) => Encoding.UTF8.GetString(Convert.FromBase64String(b64));

        public ZaloShareForm(string message)
        {
            Text = "Tool Affiliate - Tự Động Gửi Zalo & Đăng Nhóm Facebook (Addlivetag Auto)";
            Width = 1340;
            Height = 920;
            MinimumSize = new Size(1080, 760);
            StartPosition = FormStartPosition.CenterParent;

            browserTabs.Dock = DockStyle.Fill;
            browserTabs.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);

            var zaloTabPage = new TabPage("💬 Trình duyệt Zalo Web");
            zaloBrowser.Dock = DockStyle.Fill;
            zaloTabPage.Controls.Add(zaloBrowser);

            var fbTabPage = new TabPage("📘 Trình duyệt Facebook (Đăng Nhóm)");
            fbBrowser.Dock = DockStyle.Fill;
            fbTabPage.Controls.Add(fbBrowser);

            browserTabs.TabPages.Add(zaloTabPage);
            browserTabs.TabPages.Add(fbTabPage);

            var bottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 360,
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
            minDelayInput.Minimum = 5; minDelayInput.Maximum = 600; minDelayInput.Value = 20;

            var lblDelay2 = new Label { Text = "đến", Left = 769, Top = 38, Width = 30 };
            maxDelayInput.SetBounds(800, 35, 52, 25);
            maxDelayInput.Minimum = 5; maxDelayInput.Maximum = 600; maxDelayInput.Value = 40;

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
                Text = "Nội dung gửi Zalo & đăng Nhóm Facebook:",
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

            var targetTabs = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
            var zaloGroupsTab = new TabPage("💬 Tích chọn Nhóm Zalo");
            var fbGroupsTab = new TabPage("📘 Tích chọn Nhóm Facebook");

            var zaloGrpTop = new Panel { Dock = DockStyle.Top, Height = 28 };
            var loadZaloBtn = new Button { Text = "🔄 Quét Nhóm Zalo", Dock = DockStyle.Right, Width = 145, Height = 26, BackColor = Color.FromArgb(225, 240, 255) };
            var addOpenZaloBtn = new Button { Text = "➕ Thêm nhóm đang mở", Dock = DockStyle.Left, Width = 160, Height = 26, BackColor = Color.FromArgb(220, 248, 228) };
            zaloGrpTop.Controls.Add(loadZaloBtn);
            zaloGrpTop.Controls.Add(addOpenZaloBtn);

            var zaloGrpInputPanel = new Panel { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(0, 2, 0, 0) };
            manualZaloGroupInput.Dock = DockStyle.Fill;
            manualZaloGroupInput.PlaceholderText = "Gõ tên nhóm Zalo rồi bấm Thêm...";
            var addManualZaloBtn = new Button { Text = "Thêm", Dock = DockStyle.Right, Width = 60, Height = 25 };
            var cleanZaloBtn = new Button { Text = "Xóa bỏ tích", Dock = DockStyle.Right, Width = 95, Height = 25 };
            zaloGrpInputPanel.Controls.Add(manualZaloGroupInput);
            zaloGrpInputPanel.Controls.Add(addManualZaloBtn);
            zaloGrpInputPanel.Controls.Add(cleanZaloBtn);

            zaloGroupsCheckList.Dock = DockStyle.Fill;
            zaloGroupsCheckList.CheckOnClick = true;
            zaloGroupsCheckList.Font = new Font("Segoe UI", 9, FontStyle.Regular);
            zaloGroupsTab.Controls.Add(zaloGroupsCheckList);
            zaloGroupsTab.Controls.Add(zaloGrpInputPanel);
            zaloGroupsTab.Controls.Add(zaloGrpTop);

            var fbGrpTop = new Panel { Dock = DockStyle.Top, Height = 28 };
            var loadFbBtn = new Button { Text = "🔄 Quét Nhóm FB đã tham gia", Dock = DockStyle.Right, Width = 185, Height = 26, BackColor = Color.FromArgb(225, 240, 255) };
            var addOpenFbBtn = new Button { Text = "➕ Thêm nhóm FB đang mở", Dock = DockStyle.Left, Width = 175, Height = 26, BackColor = Color.FromArgb(220, 248, 228) };
            fbGrpTop.Controls.Add(loadFbBtn);
            fbGrpTop.Controls.Add(addOpenFbBtn);

            var fbGrpInputPanel = new Panel { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(0, 2, 0, 0) };
            manualFbGroupInput.Dock = DockStyle.Fill;
            manualFbGroupInput.PlaceholderText = "Dán link nhóm FB (https://www.facebook.com/groups/...) rồi bấm Thêm...";
            var addManualFbBtn = new Button { Text = "Thêm", Dock = DockStyle.Right, Width = 60, Height = 25 };
            var cleanFbBtn = new Button { Text = "Xóa bỏ tích", Dock = DockStyle.Right, Width = 95, Height = 25 };
            fbGrpInputPanel.Controls.Add(manualFbGroupInput);
            fbGrpInputPanel.Controls.Add(addManualFbBtn);
            fbGrpInputPanel.Controls.Add(cleanFbBtn);

            fbGroupsCheckList.Dock = DockStyle.Fill;
            fbGroupsCheckList.CheckOnClick = true;
            fbGroupsCheckList.Font = new Font("Segoe UI", 9, FontStyle.Regular);
            fbGroupsTab.Controls.Add(fbGroupsCheckList);
            fbGroupsTab.Controls.Add(fbGrpInputPanel);
            fbGroupsTab.Controls.Add(fbGrpTop);

            targetTabs.TabPages.Add(zaloGroupsTab);
            targetTabs.TabPages.Add(fbGroupsTab);
            splitBottom.Panel2.Controls.Add(targetTabs);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 42,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 6, 0, 0)
            };

            var autoBothBtn = new Button
            {
                Text = "🚀 BẬT AUTO CẢ ZALO + FACEBOOK (LẶP LẠI)",
                Width = 310,
                Height = 32,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(210, 245, 218)
            };

            var sendZaloOnceBtn = new Button
            {
                Text = "⚡ Gửi ngay Zalo",
                Width = 135,
                Height = 32,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(255, 242, 204)
            };

            var sendFbOnceBtn = new Button
            {
                Text = "📘 Đăng ngay Nhóm FB",
                Width = 165,
                Height = 32,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(218, 232, 255)
            };

            var fetchPreviewBtn = new Button
            {
                Text = "🔄 Lấy Sale từ API",
                Width = 140,
                Height = 32,
                BackColor = Color.FromArgb(235, 242, 250)
            };

            var stopBtn = new Button
            {
                Text = "⏹ Dừng",
                Width = 95,
                Height = 32,
                Enabled = false,
                BackColor = Color.FromArgb(255, 228, 228)
            };

            LoadSavedConfig();

            loadZaloBtn.Click += async (_, _) =>
            {
                browserTabs.SelectedIndex = 0;
                await LoadGroupsFromZaloAsync();
            };
            addOpenZaloBtn.Click += async (_, _) =>
            {
                browserTabs.SelectedIndex = 0;
                await AddCurrentOpenZaloGroupAsync();
            };
            addManualZaloBtn.Click += (_, _) =>
            {
                var name = manualZaloGroupInput.Text.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    AddOrCheckZaloGroupItem(name, true);
                    manualZaloGroupInput.Clear();
                    SaveConfig();
                }
            };
            cleanZaloBtn.Click += (_, _) =>
            {
                for (int i = zaloGroupsCheckList.Items.Count - 1; i >= 0; i--)
                    if (!zaloGroupsCheckList.GetItemChecked(i)) zaloGroupsCheckList.Items.RemoveAt(i);
                SaveConfig();
            };

            loadFbBtn.Click += async (_, _) =>
            {
                browserTabs.SelectedIndex = 1;
                targetTabs.SelectedIndex = 1;
                await LoadGroupsFromFacebookAsync();
            };
            addOpenFbBtn.Click += (_, _) =>
            {
                browserTabs.SelectedIndex = 1;
                targetTabs.SelectedIndex = 1;
                AddCurrentOpenFacebookGroup();
            };
            addManualFbBtn.Click += (_, _) =>
            {
                var raw = manualFbGroupInput.Text.Trim();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var url = NormalizeFbGroupUrl(raw);
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        AddOrCheckFbGroupItem(raw, url, true);
                        manualFbGroupInput.Clear();
                        SaveConfig();
                    }
                    else
                    {
                        MessageBox.Show(this, "Hãy dán đường dẫn nhóm Facebook hợp lệ (ví dụ: https://www.facebook.com/groups/123456).", "Facebook", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            };
            cleanFbBtn.Click += (_, _) =>
            {
                for (int i = fbGroupsCheckList.Items.Count - 1; i >= 0; i--)
                    if (!fbGroupsCheckList.GetItemChecked(i)) fbGroupsCheckList.Items.RemoveAt(i);
                SaveConfig();
            };

            EventHandler saveHandler = (_, _) => SaveConfig();
            apiKeyBox.TextChanged += saveHandler;
            affIdBox.TextChanged += saveHandler;
            subIdBox.TextChanged += saveHandler;
            keywordsBox.TextChanged += saveHandler;
            zaloGroupsCheckList.ItemCheck += (_, _) => BeginInvoke(new Action(SaveConfig));
            fbGroupsCheckList.ItemCheck += (_, _) => BeginInvoke(new Action(SaveConfig));
            batchCountInput.ValueChanged += saveHandler;
            cycleMinutesInput.ValueChanged += saveHandler;
            minDelayInput.ValueChanged += saveHandler;
            maxDelayInput.ValueChanged += saveHandler;

            autoBothBtn.Click += async (_, _) => await RunMasterFlowAsync(runZalo: true, runFb: true, loopForever: true, autoBothBtn, sendZaloOnceBtn, sendFbOnceBtn, fetchPreviewBtn, stopBtn);
            sendZaloOnceBtn.Click += async (_, _) => await RunMasterFlowAsync(runZalo: true, runFb: false, loopForever: false, autoBothBtn, sendZaloOnceBtn, sendFbOnceBtn, fetchPreviewBtn, stopBtn);
            sendFbOnceBtn.Click += async (_, _) => await RunMasterFlowAsync(runZalo: false, runFb: true, loopForever: false, autoBothBtn, sendZaloOnceBtn, sendFbOnceBtn, fetchPreviewBtn, stopBtn);

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
                statusLabel.Text = "Đang dừng tiến trình tự động...";
            };

            actions.Controls.AddRange(new Control[] { autoBothBtn, sendZaloOnceBtn, sendFbOnceBtn, fetchPreviewBtn, stopBtn });

            statusLabel.Dock = DockStyle.Bottom;
            statusLabel.Height = 24;
            statusLabel.Padding = new Padding(4, 4, 0, 0);
            statusLabel.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            statusLabel.ForeColor = Color.FromArgb(25, 90, 160);
            statusLabel.Text = "Đang khởi tạo trình duyệt Zalo & Facebook...";

            bottom.Controls.Add(splitBottom);
            bottom.Controls.Add(configGroup);
            bottom.Controls.Add(actions);
            bottom.Controls.Add(statusLabel);

            Controls.Add(browserTabs);
            Controls.Add(bottom);

            Shown += async (_, _) =>
            {
                BeginInvoke(new Action(() =>
                {
                    if (splitBottom.ClientSize.Width > 600)
                        splitBottom.SplitterDistance = (int)(splitBottom.ClientSize.Width * 0.50);
                }));
                await InitializeBrowsersAsync();
            };

            FormClosed += (_, _) =>
            {
                sendCts?.Cancel();
                SaveConfig();
                try { zaloBrowser.CoreWebView2?.Stop(); } catch { }
                try { fbBrowser.CoreWebView2?.Stop(); } catch { }
                zaloBrowser.Dispose();
                fbBrowser.Dispose();
            };
        }

        private async Task InitializeBrowsersAsync()
        {
            if (initialized) return;
            initialized = true;
            try
            {
                Directory.CreateDirectory(zaloProfilePath);
                var zaloEnv = await CoreWebView2Environment.CreateAsync(null, zaloProfilePath);
                await zaloBrowser.EnsureCoreWebView2Async(zaloEnv);
                zaloBrowser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                zaloBrowser.CoreWebView2.FrameCreated += (_, args) =>
                {
                    try
                    {
                        var frame = args.Frame;
                        zaloFrames.Add(frame);
                        frame.Destroyed += (_, _) => zaloFrames.Remove(frame);
                    }
                    catch { }
                };
                zaloBrowser.CoreWebView2.Navigate("https://chat.zalo.me/");

                Directory.CreateDirectory(fbProfilePath);
                var fbEnv = await CoreWebView2Environment.CreateAsync(null, fbProfilePath);
                await fbBrowser.EnsureCoreWebView2Async(fbEnv);
                fbBrowser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                fbBrowser.CoreWebView2.Navigate("https://www.facebook.com/groups/joins/");

                statusLabel.Text = "Zalo & Facebook đã sẵn sàng! Hãy đăng nhập (nếu cần), quét/tích chọn nhóm rồi bấm BẬT AUTO.";
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Lỗi khởi tạo trình duyệt: " + ex.Message;
            }
        }

        private void AddOrCheckZaloGroupItem(string groupName, bool isChecked)
        {
            var clean = groupName.Trim();
            if (string.IsNullOrWhiteSpace(clean)) return;
            for (int i = 0; i < zaloGroupsCheckList.Items.Count; i++)
            {
                if (string.Equals(zaloGroupsCheckList.Items[i]?.ToString(), clean, StringComparison.OrdinalIgnoreCase))
                {
                    if (isChecked) zaloGroupsCheckList.SetItemChecked(i, true);
                    return;
                }
            }
            zaloGroupsCheckList.Items.Add(clean, isChecked);
        }

        private void AddOrCheckFbGroupItem(string name, string url, bool isChecked)
        {
            var cleanUrl = NormalizeFbGroupUrl(url);
            if (string.IsNullOrWhiteSpace(cleanUrl)) return;
            var cleanName = string.IsNullOrWhiteSpace(name) ? cleanUrl : name.Trim();

            for (int i = 0; i < fbGroupsCheckList.Items.Count; i++)
            {
                if (fbGroupsCheckList.Items[i] is FbGroupEntry existing &&
                    string.Equals(existing.Url, cleanUrl, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(cleanName) && existing.Name == existing.Url)
                        existing.Name = cleanName;
                    if (isChecked) fbGroupsCheckList.SetItemChecked(i, true);
                    return;
                }
            }
            fbGroupsCheckList.Items.Add(new FbGroupEntry { Name = cleanName, Url = cleanUrl, Checked = isChecked }, isChecked);
        }

        private static string NormalizeFbGroupUrl(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var s = raw.Trim();
            if (s.Contains('|')) s = s.Split('|').Last().Trim();
            if (!s.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                s = "https://www.facebook.com/groups/" + s.Trim('/');
            if (Uri.TryCreate(s, UriKind.Absolute, out var uri) &&
                uri.Host.Contains("facebook.com", StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath.StartsWith("/groups/", StringComparison.OrdinalIgnoreCase))
            {
                var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 2 && !string.Equals(segments[1], "joins", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(segments[1], "feed", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(segments[1], "discover", StringComparison.OrdinalIgnoreCase))
                {
                    return $"https://www.facebook.com/groups/{segments[1]}/";
                }
            }
            return "";
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
                    AddOrCheckZaloGroupItem(g, checkedSet.Contains(g));
                }

                if (cfg.FbGroups != null)
                {
                    foreach (var fg in cfg.FbGroups)
                    {
                        AddOrCheckFbGroupItem(fg.Name, fg.Url, fg.Checked);
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
                var allZalo = new List<string>();
                var checkedZalo = new List<string>();

                for (int i = 0; i < zaloGroupsCheckList.Items.Count; i++)
                {
                    var s = zaloGroupsCheckList.Items[i]?.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    allZalo.Add(s);
                    if (zaloGroupsCheckList.GetItemChecked(i)) checkedZalo.Add(s);
                }

                var fbList = new List<FbGroupEntry>();
                for (int i = 0; i < fbGroupsCheckList.Items.Count; i++)
                {
                    if (fbGroupsCheckList.Items[i] is FbGroupEntry entry)
                    {
                        entry.Checked = fbGroupsCheckList.GetItemChecked(i);
                        fbList.Add(entry);
                    }
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
                    SavedGroups = allZalo,
                    CheckedGroups = checkedZalo,
                    FbGroups = fbList
                };
                File.WriteAllText(configFilePath, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private async Task RunMasterFlowAsync(bool runZalo, bool runFb, bool loopForever, Button autoBtn, Button zaloBtn, Button fbBtn, Button previewBtn, Button stopBtn)
        {
            if (autoPilotRunning) return;
            autoPilotRunning = true;
            autoBtn.Enabled = false;
            zaloBtn.Enabled = false;
            fbBtn.Enabled = false;
            previewBtn.Enabled = false;
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
                        statusLabel.Text = $"[Đợt #{batchNumber}] Đang lấy sản phẩm sale mới từ Addlivetag API...";
                        var (_, fetchedMsg) = await FetchAndBuildSaleMessageAsync(token);
                        statusLabel.Text = $"[Đợt #{batchNumber}] {fetchedMsg}";
                    }

                    if (string.IsNullOrWhiteSpace(messageBox.Text))
                    {
                        statusLabel.Text = "Chưa có nội dung bài đăng! Hãy chọn sản phẩm, nhập nội dung, hoặc điền API Key Addlivetag.";
                        MessageBox.Show(this, statusLabel.Text, "Thiếu nội dung", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        break;
                    }

                    int minSec = (int)Math.Min(minDelayInput.Value, maxDelayInput.Value);
                    int maxSec = (int)Math.Max(minDelayInput.Value, maxDelayInput.Value);

                    if (runZalo && zaloBrowser.CoreWebView2 is not null)
                    {
                        var zaloGroups = GetCheckedZaloGroups();
                        if (zaloGroups.Count > 0)
                        {
                            browserTabs.SelectedIndex = 0;
                            int sentZalo = 0;
                            for (int i = 0; i < zaloGroups.Count; i++)
                            {
                                token.ThrowIfCancellationRequested();
                                var groupName = zaloGroups[i];
                                statusLabel.Text = $"[Zalo {i + 1}/{zaloGroups.Count}] Đang mở nhóm '{groupName}'...";

                                await SelectZaloGroupByNameAsync(groupName, token);
                                await Task.Delay(1000, token);

                                var (ok, _) = await SendMessageToCurrentOpenZaloChatAsync(token);
                                if (ok) sentZalo++;

                                if (i < zaloGroups.Count - 1 || (runFb && GetCheckedFbGroups().Count > 0))
                                {
                                    int delaySec = Random.Shared.Next(minSec, maxSec + 1);
                                    await WaitWithCountdownAsync(delaySec, $"[Zalo] Đã gửi {sentZalo}/{i + 1} nhóm. Nghỉ chống spam", token);
                                }
                            }
                        }
                        else if (!runFb)
                        {
                            browserTabs.SelectedIndex = 0;
                            statusLabel.Text = "[Zalo] Đang gửi vào cuộc trò chuyện đang mở...";
                            await SendMessageToCurrentOpenZaloChatAsync(token);
                        }
                    }

                    if (runFb && fbBrowser.CoreWebView2 is not null)
                    {
                        var fbGroups = GetCheckedFbGroups();
                        if (fbGroups.Count > 0)
                        {
                            browserTabs.SelectedIndex = 1;
                            int postedFb = 0;
                            for (int i = 0; i < fbGroups.Count; i++)
                            {
                                token.ThrowIfCancellationRequested();
                                var grp = fbGroups[i];
                                statusLabel.Text = $"[Facebook {i + 1}/{fbGroups.Count}] Đang mở nhóm '{grp.Name}'...";

                                var (ok, fbMsg) = await PostToFacebookGroupAsync(grp.Url, messageBox.Text, token);
                                if (ok)
                                {
                                    postedFb++;
                                    statusLabel.Text = $"[Facebook {i + 1}/{fbGroups.Count}] Đã đăng thành công vào '{grp.Name}'!";
                                }
                                else
                                {
                                    statusLabel.Text = $"[Facebook {i + 1}/{fbGroups.Count}] Chưa đăng được '{grp.Name}': {fbMsg}";
                                }

                                if (i < fbGroups.Count - 1)
                                {
                                    int delaySec = Random.Shared.Next(minSec, maxSec + 1);
                                    await WaitWithCountdownAsync(delaySec, $"[Facebook] Đã đăng {postedFb}/{i + 1} nhóm. Nghỉ chống checkpoint", token);
                                }
                            }
                        }
                        else if (!runZalo)
                        {
                            browserTabs.SelectedIndex = 1;
                            var currentUrl = fbBrowser.Source?.ToString() ?? "";
                            if (currentUrl.Contains("/groups/", StringComparison.OrdinalIgnoreCase))
                            {
                                statusLabel.Text = "[Facebook] Đang đăng vào nhóm đang mở trên màn hình...";
                                var (_, fbMsg) = await PostToFacebookGroupAsync(currentUrl, messageBox.Text, token);
                                statusLabel.Text = fbMsg;
                            }
                            else
                            {
                                MessageBox.Show(this, "Bạn chưa tích chọn nhóm Facebook nào! Hãy qua tab '📘 Tích chọn Nhóm Facebook' bấm '🔄 Quét Nhóm FB đã tham gia' rồi tích chọn.", "Facebook Auto", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                        }
                    }

                    if (!loopForever) break;

                    int cycleSeconds = (int)cycleMinutesInput.Value * 60;
                    await WaitWithCountdownAsync(cycleSeconds, $"[Xong đợt #{batchNumber}] Tự động chạy đợt tiếp theo sau", token);

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
                    zaloBtn.Enabled = true;
                    fbBtn.Enabled = true;
                    previewBtn.Enabled = true;
                    stopBtn.Enabled = false;
                }
            }
        }

        private List<string> GetCheckedZaloGroups()
        {
            var list = new List<string>();
            foreach (var item in zaloGroupsCheckList.CheckedItems)
                if (item is string s && !string.IsNullOrWhiteSpace(s)) list.Add(s);
            return list;
        }

        private List<FbGroupEntry> GetCheckedFbGroups()
        {
            var list = new List<FbGroupEntry>();
            foreach (var item in fbGroupsCheckList.CheckedItems)
                if (item is FbGroupEntry g && !string.IsNullOrWhiteSpace(g.Url)) list.Add(g);
            return list;
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

        private void AddCurrentOpenFacebookGroup()
        {
            var url = fbBrowser.Source?.ToString() ?? "";
            var cleanUrl = NormalizeFbGroupUrl(url);
            if (string.IsNullOrWhiteSpace(cleanUrl))
            {
                MessageBox.Show(this, "Trang Facebook hiện tại chưa phải là 1 nhóm cụ thể. Hãy bấm vào 1 nhóm trên Facebook trước.", "Facebook", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var title = fbBrowser.CoreWebView2?.DocumentTitle?.Replace("| Facebook", "").Trim() ?? cleanUrl;
            AddOrCheckFbGroupItem(title, cleanUrl, true);
            SaveConfig();
            statusLabel.Text = $"Đã thêm và tích chọn nhóm FB: {title}";
        }

        private async Task LoadGroupsFromFacebookAsync()
        {
            if (fbBrowser.CoreWebView2 is null) return;
            statusLabel.Text = "Đang mở trang Danh sách nhóm Facebook của bạn và quét...";

            try
            {
                if (fbBrowser.Source?.AbsolutePath.Contains("/groups/joins", StringComparison.OrdinalIgnoreCase) != true)
                {
                    fbBrowser.CoreWebView2.Navigate("https://www.facebook.com/groups/joins/");
                    await Task.Delay(3200);
                }

                await fbBrowser.CoreWebView2.ExecuteScriptAsync("window.scrollBy(0, 900);");
                await Task.Delay(1200);
                await fbBrowser.CoreWebView2.ExecuteScriptAsync("window.scrollTo(0, 0);");
                await Task.Delay(500);

                var raw = await fbBrowser.CoreWebView2.ExecuteScriptAsync(DecodeB64(B64_SCAN_FB_GROUPS));
                if (!string.IsNullOrWhiteSpace(raw) && raw != "null")
                {
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.TryGetProperty("groups", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        int added = 0;
                        foreach (var item in arr.EnumerateArray())
                        {
                            var name = GetFirstString(item, "name");
                            var url = GetFirstString(item, "url");
                            if (!string.IsNullOrWhiteSpace(url))
                            {
                                AddOrCheckFbGroupItem(name, url, false);
                                added++;
                            }
                        }
                        SaveConfig();
                        if (added > 0)
                        {
                            statusLabel.Text = $"Đã quét được {added} nhóm Facebook! Hãy tích chọn các nhóm bạn muốn đăng bài.";
                            return;
                        }
                    }
                }
                statusLabel.Text = "Chưa quét được nhóm. Hãy chắc chắn bạn đã đăng nhập Facebook trên tab Trình duyệt Facebook.";
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Lỗi quét nhóm Facebook: " + ex.Message;
            }
        }

        private async Task<(bool Ok, string Message)> PostToFacebookGroupAsync(string groupUrl, string content, CancellationToken token)
        {
            if (fbBrowser.CoreWebView2 is null) return (false, "Facebook chưa sẵn sàng.");

            var normalizedText = string.Join("\n", content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)).Trim();
            try { Clipboard.SetText(normalizedText); } catch { }

            fbBrowser.CoreWebView2.Navigate(groupUrl);
            await Task.Delay(4200, token);

            var openRaw = await fbBrowser.CoreWebView2.ExecuteScriptAsync(DecodeB64(B64_OPEN_FB_COMPOSER));
            if (string.IsNullOrWhiteSpace(openRaw) || !IsSuccessResult(openRaw))
            {
                return (false, "Không mở được khung viết bài (nhóm có thể đã tắt quyền đăng hoặc chưa tham gia).");
            }

            await Task.Delay(2200, token);

            var payloadJson = JsonSerializer.Serialize(normalizedText);
            string fillFbScript = DecodeB64(B64_FILL_FB_FN) + "(" + payloadJson + ");";

            var fillRaw = await fbBrowser.CoreWebView2.ExecuteScriptAsync(fillFbScript);
            if (string.IsNullOrWhiteSpace(fillRaw) || !IsSuccessResult(fillRaw))
            {
                return (false, "Không điền được chữ vào khung đăng bài Facebook.");
            }

            await Task.Delay(2000, token);

            var postRaw = await fbBrowser.CoreWebView2.ExecuteScriptAsync(DecodeB64(B64_CLICK_FB_POST));
            if (!string.IsNullOrWhiteSpace(postRaw) && IsSuccessResult(postRaw))
            {
                await Task.Delay(3500, token);
                return (true, "Đã đăng bài thành công!");
            }

            return (false, "Đã điền nội dung nhưng chưa bấm được nút Đăng.");
        }

        private async Task AddCurrentOpenZaloGroupAsync()
        {
            if (zaloBrowser.CoreWebView2 is null) return;
            try
            {
                var raw = await ExecuteInZaloFramesAsync(DecodeB64(B64_ADD_OPEN_ZALO));
                if (!string.IsNullOrWhiteSpace(raw) && raw != "null")
                {
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean() &&
                        doc.RootElement.TryGetProperty("name", out var nameProp))
                    {
                        var groupName = nameProp.GetString()?.Trim() ?? "";
                        if (!string.IsNullOrWhiteSpace(groupName))
                        {
                            AddOrCheckZaloGroupItem(groupName, true);
                            SaveConfig();
                            statusLabel.Text = $"Đã thêm và tích chọn nhóm Zalo: '{groupName}'!";
                            return;
                        }
                    }
                }
                statusLabel.Text = "Hãy bấm mở 1 nhóm trên Zalo Web trước khi bấm nút này.";
            }
            catch { }
        }

        private async Task LoadGroupsFromZaloAsync()
        {
            if (zaloBrowser.CoreWebView2 is null) return;
            statusLabel.Text = "Đang tự động lọc danh sách NHÓM từ Zalo Web...";
            var collectedGroups = new List<string>();

            try
            {
                await ExecuteInZaloFramesAsync(DecodeB64(B64_OPEN_ZALO_CONTACTS));
                await Task.Delay(700);

                var openedGroupMenu = await ExecuteInZaloFramesAsync(DecodeB64(B64_CLICK_ZALO_GROUP_MENU));
                if (openedGroupMenu is not null && IsSuccessResult(openedGroupMenu))
                {
                    await Task.Delay(900);
                }

                var raw = await ExecuteInZaloFramesAsync(DecodeB64(B64_EXTRACT_ZALO_GROUPS));
                if (!string.IsNullOrWhiteSpace(raw) && raw != "null" && raw != "{}")
                {
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.TryGetProperty("groups", out var groupsArr))
                    {
                        var list = JsonSerializer.Deserialize<List<string>>(groupsArr.GetRawText());
                        if (list is not null) collectedGroups.AddRange(list);
                    }
                }

                await ExecuteInZaloFramesAsync(DecodeB64(B64_BACK_TO_MSG_TAB));
                await Task.Delay(500);

                var distinctGroups = collectedGroups.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (distinctGroups.Count > 0)
                {
                    foreach (var name in distinctGroups) AddOrCheckZaloGroupItem(name, false);
                    SaveConfig();
                    statusLabel.Text = $"Đã lọc được {distinctGroups.Count} nhóm Zalo!";
                    return;
                }
                statusLabel.Text = "Hãy mở nhóm trên Zalo rồi nhấn '➕ Thêm nhóm đang mở' để thêm nhanh!";
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Lỗi quét nhóm Zalo: " + ex.Message;
            }
        }

        private async Task<bool> SelectZaloGroupByNameAsync(string groupName, CancellationToken token)
        {
            var nameJson = JsonSerializer.Serialize(groupName.Trim());

            var clickDirectResult = await ExecuteInZaloFramesAsync(BuildClickConversationScript(nameJson));
            if (clickDirectResult is not null && IsSuccessResult(clickDirectResult))
            {
                await Task.Delay(600, token);
                return true;
            }

            var searchResult = await ExecuteInZaloFramesAsync(BuildTypeSearchBoxScript(nameJson));
            if (searchResult is not null && IsSuccessResult(searchResult))
            {
                await Task.Delay(1200, token);
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    var clickAfterSearch = await ExecuteInZaloFramesAsync(BuildClickConversationScript(nameJson));
                    if (clickAfterSearch is not null && IsSuccessResult(clickAfterSearch))
                    {
                        await Task.Delay(600, token);
                        return true;
                    }
                    await Task.Delay(500, token);
                }
                await ExecuteInZaloFramesAsync(BuildSelectFirstSearchResultScript());
                await Task.Delay(700, token);
                return true;
            }
            return false;
        }

        private async Task<(bool Ok, string Message)> SendMessageToCurrentOpenZaloChatAsync(CancellationToken token)
        {
            var normalizedText = string.Join("\n", messageBox.Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)).Trim();
            try { Clipboard.SetText(normalizedText); } catch { }

            var payload = JsonSerializer.Serialize(normalizedText);

            zaloBrowser.Focus();
            await Task.Delay(250, token);

            var fillScript = BuildFillScript(payload);
            var result = await ExecuteInZaloFramesAsync(fillScript);
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

            await Task.Delay(750, token);

            var triggerResult = await ExecuteInZaloFramesAsync(BuildTriggerSendScript());
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

        private async Task<string?> ExecuteInZaloFramesAsync(string script)
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

            foreach (var frame in zaloFrames.ToArray())
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

        private static string BuildClickConversationScript(string groupNameJson) =>
            DecodeB64(B64_CLICK_ZALO_CONV_FN) + "(" + groupNameJson + ");";

        private static string BuildTypeSearchBoxScript(string groupNameJson) =>
            DecodeB64(B64_TYPE_ZALO_SEARCH_FN) + "(" + groupNameJson + ");";

        private static string BuildSelectFirstSearchResultScript() =>
            DecodeB64(B64_SELECT_FIRST_SEARCH);

        private static string BuildFillScript(string messageJson) =>
            DecodeB64(B64_FILL_ZALO_FN) + "(" + messageJson + ");";

        private static string BuildTriggerSendScript() =>
            DecodeB64(B64_TRIGGER_ZALO_SEND);

        private async Task<(bool Ok, string Message)> FetchAndBuildSaleMessageAsync(CancellationToken token)
        {
            var apiKey = apiKeyBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return (false, "Hãy nhập API Key Addlivetag ở dòng cấu hình.");
            }

            var keywords = keywordsBox.Text
                .Split(new[] { ",", ";", "\r", "\n" }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
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
                return (false, "Không lấy được sản phẩm từ Addlivetag API. Sẽ dùng nội dung tin nhắn hiện tại.");
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
    }
}
