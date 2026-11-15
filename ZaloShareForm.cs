using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ToolAffiliate
{
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

        private const string JsLoadGroupsB64 = "KCgpID0+IHsKICB0cnkgewogICAgY29uc3QgdmlzaWJsZSA9IGUgPT4gewogICAgICBpZiAoIWUpIHJldHVybiBmYWxzZTsKICAgICAgY29uc3QgciA9IGUuZ2V0Qm91bmRpbmdDbGllbnRSZWN0KCk7CiAgICAgIGNvbnN0IHMgPSBnZXRDb21wdXRlZFN0eWxlKGUpOwogICAgICByZXR1cm4gci53aWR0aCA+IDIwICYmIHIuaGVpZ2h0ID4gMTQgJiYgci5sZWZ0IDwgNTAwICYmCiAgICAgICAgcy5kaXNwbGF5ICE9PSAnbm9uZScgJiYgcy52aXNpYmlsaXR5ICE9PSAnaGlkZGVuJzsKICAgIH07CiAgICBjb25zdCBpdGVtcyA9IFsuLi5kb2N1bWVudC5xdWVyeVNlbGVjdG9yQWxsKCcuY29udi1pdGVtLCBbaWRePSJmcmllbmQtaXRlbS0iXSwgLm1zZy1pdGVtLCBbcm9sZT0ibGlzdGl0ZW0iXScpXS5maWx0ZXIodmlzaWJsZSk7CiAgICBjb25zdCBuYW1lcyA9IFtdOwogICAgZm9yIChjb25zdCBlbCBvZiBpdGVtcykgewogICAgICBpZiAoZWwudGFnTmFtZSA9PT0gJ0lOUFVUJyB8fCBlbC50YWdOYW1lID09PSAnVEVYVEFSRUEnIHx8IGVsLmlzQ29udGVudEVkaXRhYmxlKSBjb250aW51ZTsKICAgICAgY29uc3QgaHRtbCA9IGVsLmlubmVySFRNTCB8fCAnJzsKICAgICAgY29uc3QgdGV4dCA9IGVsLmlubmVyVGV4dCB8fCAnJzsKICAgICAgY29uc3QgaXNHcm91cCA9IGh0bWwuaW5jbHVkZXMoJ2F2YXRhci1ncm91cCcpIHx8CiAgICAgICAgICAgICAgICAgICAgICBodG1sLmluY2x1ZGVzKCdpYy1hdmF0YXItZ3JvdXAnKSB8fAogICAgICAgICAgICAgICAgICAgICAgaHRtbC5pbmNsdWRlcygnZmEtdXNlcnMnKSB8fAogICAgICAgICAgICAgICAgICAgICAgdGV4dC5pbmNsdWRlcygndGjDoG5oIHZpw6puJykgfHwKICAgICAgICAgICAgICAgICAgICAgIGVsLnF1ZXJ5U2VsZWN0b3IoJ3N2Z1tjbGFzcyo9Imdyb3VwIl0sIC5pYy1hdmF0YXItZ3JvdXAsIC5hdmF0YXItZ3JvdXAnKSAhPT0gbnVsbDsKICAgICAgaWYgKCFpc0dyb3VwKSBjb250aW51ZTsKICAgICAgY29uc3QgcmF3VGV4dCA9IHRleHQuc3BsaXQoJ1xuJylbMF0ucmVwbGFjZSgvXHMrL2csICcgJykudHJpbSgpOwogICAgICBpZiAocmF3VGV4dCAmJiByYXdUZXh0Lmxlbmd0aCA8IDEwMCAmJiAhbmFtZXMuaW5jbHVkZXMocmF3VGV4dCkpIHsKICAgICAgICBuYW1lcy5wdXNoKHJhd1RleHQpOwogICAgICB9CiAgICB9CiAgICByZXR1cm4geyBvazogdHJ1ZSwgZ3JvdXBzOiBuYW1lcyB9OwogIH0gY2F0Y2ggKGUpIHsKICAgIHJldHVybiB7IG9rOiBmYWxzZSwgZ3JvdXBzOiBbXSB9OwogIH0KfSkoKTs=";
        private const string JsClickConvB64 = "KCgpID0+IHsKICB0cnkgewogICAgY29uc3QgdGFyZ2V0ID0gKF9fR1JPVVBfTkFNRV9fIHx8ICcnKS50b0xvd2VyQ2FzZSgpLnJlcGxhY2UoL1xzKy9nLCAnICcpLnRyaW0oKTsKICAgIGlmICghdGFyZ2V0KSByZXR1cm4geyBvazogZmFsc2UsIG1lc3NhZ2U6ICdUw6puIG5ow7NtIHRy4buRbmcuJyB9OwogICAgY29uc3QgaGVhZGVyVGl0bGUgPSBkb2N1bWVudC5xdWVyeVNlbGVjdG9yKCdoZWFkZXIgLmhlYWRlci10aXRsZSwgLnRocmVhZC1jaGF0LXRpdGxlLCBbZGF0YS1pZCo9ImRpdl9NYWluX1RhYk1zZ19IZWFkZXIiXSBzcGFuLCAudGl0bGUtaGVhZGVyJyk7CiAgICBpZiAoaGVhZGVyVGl0bGUgJiYgKGhlYWRlclRpdGxlLmlubmVyVGV4dCB8fCAnJykudG9Mb3dlckNhc2UoKS5yZXBsYWNlKC9ccysvZywgJyAnKS50cmltKCkgPT09IHRhcmdldCkgewogICAgICByZXR1cm4geyBvazogdHJ1ZSwgbWVzc2FnZTogJ05ow7NtIMSRw6MgxJHGsOG7o2MgbeG7nyBz4bq1bjogJyArIHRhcmdldCB9OwogICAgfQogICAgY29uc3QgdmlzaWJsZSA9IGUgPT4gewogICAgICBpZiAoIWUpIHJldHVybiBmYWxzZTsKICAgICAgY29uc3QgciA9IGUuZ2V0Qm91bmRpbmdDbGllbnRSZWN0KCk7CiAgICAgIGNvbnN0IHMgPSBnZXRDb21wdXRlZFN0eWxlKGUpOwogICAgICByZXR1cm4gci53aWR0aCA+IDE1ICYmIHIuaGVpZ2h0ID4gMTQgJiYgci5sZWZ0IDwgNTAwICYmIHIudG9wID4gNDUgJiYKICAgICAgICBzLmRpc3BsYXkgIT09ICdub25lJyAmJiBzLnZpc2liaWxpdHkgIT09ICdoaWRkZW4nICYmIE51bWJlcihzLm9wYWNpdHkgfHwgMSkgPiAwOwogICAgfTsKICAgIGNvbnN0IGl0ZW1zID0gWy4uLmRvY3VtZW50LnF1ZXJ5U2VsZWN0b3JBbGwoJy5jb252LWl0ZW0sIFtpZF49ImZyaWVuZC1pdGVtLSJdLCAubXNnLWl0ZW0sIFtkYXRhLWlkKj0iQ29udiJdLCBbcm9sZT0ibGlzdGl0ZW0iXSwgLnZpcnR1YWxpemVkLXNjcm9sbCBkaXYnKV0uZmlsdGVyKHZpc2libGUpOwogICAgbGV0IGJlc3QgPSBudWxsOwogICAgbGV0IGJlc3RTY29yZSA9IDA7CiAgICBmb3IgKGNvbnN0IGVsIG9mIGl0ZW1zKSB7CiAgICAgIGlmIChlbC50YWdOYW1lID09PSAnSU5QVVQnIHx8IGVsLnRhZ05hbWUgPT09ICdURVhUQVJFQScgfHwgZWwuaXNDb250ZW50RWRpdGFibGUpIGNvbnRpbnVlOwogICAgICBjb25zdCBsaW5lcyA9IChlbC5pbm5lclRleHQgfHwgJycpLnNwbGl0KCdcbicpLm1hcCh4ID0+IHgucmVwbGFjZSgvXHMrL2csICcgJykudHJpbSgpKS5maWx0ZXIoQm9vbGVhbik7CiAgICAgIGlmIChsaW5lcy5sZW5ndGggPT09IDApIGNvbnRpbnVlOwogICAgICBjb25zdCBmaXJzdExpbmUgPSBsaW5lc1swXS50b0xvd2VyQ2FzZSgpOwogICAgICBjb25zdCBmdWxsVGV4dCA9IGxpbmVzLmpvaW4oJyAnKS50b0xvd2VyQ2FzZSgpOwogICAgICBpZiAoZnVsbFRleHQubGVuZ3RoID4gMjIwKSBjb250aW51ZTsKICAgICAgbGV0IHNjb3JlID0gMDsKICAgICAgaWYgKGZpcnN0TGluZSA9PT0gdGFyZ2V0KSBzY29yZSA9IDUwMCAtIGZ1bGxUZXh0Lmxlbmd0aDsKICAgICAgZWxzZSBpZiAoZmlyc3RMaW5lLmluY2x1ZGVzKHRhcmdldCkpIHNjb3JlID0gMzUwIC0gZmlyc3RMaW5lLmxlbmd0aDsKICAgICAgZWxzZSBpZiAoZnVsbFRleHQuaW5jbHVkZXModGFyZ2V0KSkgc2NvcmUgPSAyMDAgLSBNYXRoLm1pbigxNTAsIGZ1bGxUZXh0Lmxlbmd0aCk7CiAgICAgIGlmIChzY29yZSA+IGJlc3RTY29yZSkgewogICAgICAgIGJlc3RTY29yZSA9IHNjb3JlOwogICAgICAgIGJlc3QgPSBlbDsKICAgICAgfQogICAgfQogICAgaWYgKCFiZXN0KSByZXR1cm4geyBvazogZmFsc2UsIG1lc3NhZ2U6ICdDaMawYSB0aOG6pXkgbmjDs20gdHJvbmcgZGFuaCBzw6FjaC4nIH07CiAgICBjb25zdCByb3cgPSBiZXN0LmNsb3Nlc3QoJy5jb252LWl0ZW0sIFtpZF49ImZyaWVuZC1pdGVtLSJdLCAubXNnLWl0ZW0sIFtyb2xlPSJsaXN0aXRlbSJdJykgfHwgYmVzdDsKICAgIHJvdy5zY3JvbGxJbnRvVmlldyh7IGJsb2NrOiAnY2VudGVyJyB9KTsKICAgIGNvbnN0IHJlY3QgPSByb3cuZ2V0Qm91bmRpbmdDbGllbnRSZWN0KCk7CiAgICBjb25zdCBjeCA9IHJlY3QubGVmdCArIE1hdGgubWluKDgwLCByZWN0LndpZHRoIC8gMik7CiAgICBjb25zdCBjeSA9IHJlY3QudG9wICsgTWF0aC5taW4oMjUsIHJlY3QuaGVpZ2h0IC8gMik7CiAgICBjb25zdCB0YXJnZXRzVG9DbGljayA9IFtyb3csIGJlc3RdOwogICAgZm9yIChjb25zdCB0IG9mIHRhcmdldHNUb0NsaWNrKSB7CiAgICAgIHQuZGlzcGF0Y2hFdmVudChuZXcgUG9pbnRlckV2ZW50KCdwb2ludGVyZG93bicsIHsgYnViYmxlczogdHJ1ZSwgY2FuY2VsYWJsZTogdHJ1ZSwgY2xpZW50WDogY3gsIGNsaWVudFk6IGN5LCBidXR0b246IDAgfSkpOwogICAgICB0LmRpc3BhdGNoRXZlbnQobmV3IE1vdXNlRXZlbnQoJ21vdXNlZG93bicsIHsgYnViYmxlczogdHJ1ZSwgY2FuY2VsYWJsZTogdHJ1ZSwgY2xpZW50WDogY3gsIGNsaWVudFk6IGN5LCBidXR0b246IDAgfSkpOwogICAgICB0LmRpc3BhdGNoRXZlbnQobmV3IFBvaW50ZXJFdmVudCgncG9pbnRlcnVwJywgeyBidWJibGVzOiB0cnVlLCBjYW5jZWxhYmxlOiB0cnVlLCBjbGllbnRYOiBjeCwgY2xpZW50WTogY3ksIGJ1dHRvbjogMCB9KSk7CiAgICAgIHQuZGlzcGF0Y2hFdmVudChuZXcgTW91c2VFdmVudCgnbW91c2V1cCcsIHsgYnViYmxlczogdHJ1ZSwgY2FuY2VsYWJsZTogdHJ1ZSwgY2xpZW50WDogY3gsIGNsaWVudFk6IGN5LCBidXR0b246IDAgfSkpOwogICAgICB0LmRpc3BhdGNoRXZlbnQobmV3IE1vdXNlRXZlbnQoJ2NsaWNrJywgeyBidWJibGVzOiB0cnVlLCBjYW5jZWxhYmxlOiB0cnVlLCBjbGllbnRYOiBjeCwgY2xpZW50WTogY3ksIGJ1dHRvbjogMCB9KSk7CiAgICB9CiAgICByZXR1cm4geyBvazogdHJ1ZSwgbWVzc2FnZTogJ8SQw6MgbeG7nyBuaMOzbTogJyArIHRhcmdldCB9OwogIH0gY2F0Y2ggKGUpIHsKICAgIHJldHVybiB7IG9rOiBmYWxzZSwgbWVzc2FnZTogU3RyaW5nKGUpIH07CiAgfQp9KSgpOw==";
        private const string JsSearchBoxB64 = "KCgpID0+IHsKICB0cnkgewogICAgY29uc3QgdGFyZ2V0ID0gX19HUk9VUF9OQU1FX187CiAgICBjb25zdCBzZWFyY2hJbnB1dCA9IGRvY3VtZW50LnF1ZXJ5U2VsZWN0b3IoJyNjb250YWN0LXNlYXJjaC1pbnB1dCwgaW5wdXRbcGxhY2Vob2xkZXIqPSJUw6xtIGtp4bq/bSJdLCBpbnB1dFtwbGFjZWhvbGRlcio9IlNlYXJjaCJdLCBpbnB1dFtkYXRhLWlkKj0idHh0X01haW5fU2VhcmNoIl0nKTsKICAgIGlmICghc2VhcmNoSW5wdXQpIHJldHVybiB7IG9rOiBmYWxzZSwgbWVzc2FnZTogJ0tow7RuZyB0w6xtIHRo4bqleSDDtCBUw6xtIGtp4bq/bSB0csOqbiBaYWxvIFdlYi4nIH07CiAgICBzZWFyY2hJbnB1dC5mb2N1cygpOwogICAgc2VhcmNoSW5wdXQuY2xpY2soKTsKICAgIGNvbnN0IHNldHRlciA9IE9iamVjdC5nZXRPd25Qcm9wZXJ0eURlc2NyaXB0b3IoSFRNTElucHV0RWxlbWVudC5wcm90b3R5cGUsICd2YWx1ZScpPy5zZXQ7CiAgICBpZiAoc2V0dGVyKSBzZXR0ZXIuY2FsbChzZWFyY2hJbnB1dCwgJycpOwogICAgZWxzZSBzZWFyY2hJbnB1dC52YWx1ZSA9ICcnOwogICAgc2VhcmNoSW5wdXQuZGlzcGF0Y2hFdmVudChuZXcgRXZlbnQoJ2lucHV0JywgeyBidWJibGVzOiB0cnVlIH0pKTsKICAgIGlmIChzZXR0ZXIpIHNldHRlci5jYWxsKHNlYXJjaElucHV0LCB0YXJnZXQpOwogICAgZWxzZSBzZWFyY2hJbnB1dC52YWx1ZSA9IHRhcmdldDsKICAgIHNlYXJjaElucHV0LmRpc3BhdGNoRXZlbnQobmV3IElucHV0RXZlbnQoJ2lucHV0JywgeyBidWJibGVzOiB0cnVlLCBpbnB1dFR5cGU6ICdpbnNlcnRUZXh0JywgZGF0YTogdGFyZ2V0IH0pKTsKICAgIHNlYXJjaElucHV0LmRpc3BhdGNoRXZlbnQobmV3IEV2ZW50KCdjaGFuZ2UnLCB7IGJ1YmJsZXM6IHRydWUgfSkpOwogICAgc2VhcmNoSW5wdXQuZGlzcGF0Y2hFdmVudChuZXcgS2V5Ym9hcmRFdmVudCgna2V5dXAnLCB7IGtleTogJ2EnLCBidWJibGVzOiB0cnVlIH0pKTsKICAgIHJldHVybiB7IG9rOiB0cnVlLCBtZXNzYWdlOiAnxJDDoyBuaOG6rXAgdMOqbiBuaMOzbSB2w6BvIMO0IHTDrG0ga2nhur9tLicgfTsKICB9IGNhdGNoIChlKSB7CiAgICByZXR1cm4geyBvazogZmFsc2UsIG1lc3NhZ2U6IFN0cmluZyhlKSB9OwogIH0KfSkoKTs=";
        private const string JsSelectFirstB64 = "KCgpID0+IHsKICB0cnkgewogICAgY29uc3Qgc2VhcmNoSW5wdXQgPSBkb2N1bWVudC5xdWVyeVNlbGVjdG9yKCcjY29udGFjdC1zZWFyY2gtaW5wdXQsIGlucHV0W3BsYWNlaG9sZGVyKj0iVMOsbSBraeG6v20iXSwgaW5wdXRbcGxhY2Vob2xkZXIqPSJTZWFyY2giXScpOwogICAgaWYgKHNlYXJjaElucHV0KSB7CiAgICAgIHNlYXJjaElucHV0LmZvY3VzKCk7CiAgICAgIHNlYXJjaElucHV0LmRpc3BhdGNoRXZlbnQobmV3IEtleWJvYXJkRXZlbnQoJ2tleWRvd24nLCB7IGtleTogJ0Fycm93RG93bicsIGNvZGU6ICdBcnJvd0Rvd24nLCBrZXlDb2RlOiA0MCwgd2hpY2g6IDQwLCBidWJibGVzOiB0cnVlIH0pKTsKICAgICAgc2VhcmNoSW5wdXQuZGlzcGF0Y2hFdmVudChuZXcgS2V5Ym9hcmRFdmVudCgna2V5ZG93bicsIHsga2V5OiAnRW50ZXInLCBjb2RlOiAnRW50ZXInLCBrZXlDb2RlOiAxMywgd2hpY2g6IDEzLCBidWJibGVzOiB0cnVlIH0pKTsKICAgICAgcmV0dXJuIHsgb2s6IHRydWUsIG1lc3NhZ2U6ICfEkMOjIGNo4buNbiBr4bq/dCBxdeG6oyDEkeG6p3UgdGnDqm4uJyB9OwogICAgfQogICAgcmV0dXJuIHsgb2s6IGZhbHNlLCBtZXNzYWdlOiAnS2jDtG5nIGPDsyDDtCB0w6xtIGtp4bq/bS4nIH07CiAgfSBjYXRjaCAoZSkgewogICAgcmV0dXJuIHsgb2s6IGZhbHNlLCBtZXNzYWdlOiBTdHJpbmcoZSkgfTsKICB9Cn0pKCk7";
        private const string JsFillB64 = "KCgpID0+IHsKICB0cnkgewogICAgY29uc3QgbXNnID0gX19NRVNTQUdFX187CiAgICBjb25zdCB2aXNpYmxlID0gZSA9PiB7CiAgICAgIGlmICghZSkgcmV0dXJuIGZhbHNlOwogICAgICBjb25zdCByID0gZS5nZXRCb3VuZGluZ0NsaWVudFJlY3QoKTsKICAgICAgY29uc3QgcyA9IGdldENvbXB1dGVkU3R5bGUoZSk7CiAgICAgIHJldHVybiByLndpZHRoID4gMjAgJiYgci5oZWlnaHQgPiAxMCAmJiBzLmRpc3BsYXkgIT09ICdub25lJyAmJgogICAgICAgIHMudmlzaWJpbGl0eSAhPT0gJ2hpZGRlbicgJiYgTnVtYmVyKHMub3BhY2l0eSB8fCAxKSA+IDAgJiYgIWUuZGlzYWJsZWQ7CiAgICB9OwogICAgY29uc3QgemFsb1NlbGVjdG9ycyA9IFsKICAgICAgJyNyaWNoSW5wdXQnLAogICAgICAnW2lkXj0iaW5wdXRfbGluZV8iXScsCiAgICAgICcucmljaC1pbnB1dCcsCiAgICAgICdbZGF0YS10cmFuc2xhdGUtcGxhY2Vob2xkZXIqPSJOaOG6rXAgQCJdJywKICAgICAgJ1tkYXRhLXRyYW5zbGF0ZS1wbGFjZWhvbGRlcio9InRpbiBuaOG6r24iXScsCiAgICAgICdbY29udGVudGVkaXRhYmxlPSJ0cnVlIl0nLAogICAgICAnW3JvbGU9InRleHRib3giXScsCiAgICAgICd0ZXh0YXJlYScKICAgIF07CiAgICBsZXQgY2FuZGlkYXRlcyA9IFtdOwogICAgZm9yIChjb25zdCBzZWwgb2YgemFsb1NlbGVjdG9ycykgewogICAgICBmb3IgKGNvbnN0IGVsIG9mIGRvY3VtZW50LnF1ZXJ5U2VsZWN0b3JBbGwoc2VsKSkgewogICAgICAgIGlmICh2aXNpYmxlKGVsKSAmJiAhY2FuZGlkYXRlcy5pbmNsdWRlcyhlbCkpIHsKICAgICAgICAgIGNvbnN0IGlkQW5kQ2xhc3MgPSAoKGVsLmlkIHx8ICcnKSArICcgJyArIChlbC5jbGFzc05hbWUgfHwgJycpICsgJyAnICsgKGVsLmdldEF0dHJpYnV0ZSgncGxhY2Vob2xkZXInKSB8fCAnJykpLnRvTG93ZXJDYXNlKCk7CiAgICAgICAgICBpZiAoL2NvbnRhY3Qtc2VhcmNofHNlYXJjaC1pbnB1dHx0w6xtIGtp4bq/bS8udGVzdChpZEFuZENsYXNzKSkgY29udGludWU7CiAgICAgICAgICBjYW5kaWRhdGVzLnB1c2goZWwpOwogICAgICAgIH0KICAgICAgfQogICAgfQogICAgY2FuZGlkYXRlcy5zb3J0KChhLCBiKSA9PiB7CiAgICAgIGNvbnN0IGFyID0gYS5nZXRCb3VuZGluZ0NsaWVudFJlY3QoKSwgYnIgPSBiLmdldEJvdW5kaW5nQ2xpZW50UmVjdCgpOwogICAgICBjb25zdCBhSWQgPSAoYS5pZCB8fCAnJykuaW5jbHVkZXMoJ3JpY2hJbnB1dCcpIHx8IChhLmlkIHx8ICcnKS5pbmNsdWRlcygnaW5wdXRfbGluZScpID8gMTAwMCA6IDA7CiAgICAgIGNvbnN0IGJJZCA9IChiLmlkIHx8ICcnKS5pbmNsdWRlcygncmljaElucHV0JykgfHwgKGIuaWQgfHwgJycpLmluY2x1ZGVzKCdpbnB1dF9saW5lJykgPyAxMDAwIDogMDsKICAgICAgcmV0dXJuIChiSWQgKyBici5ib3R0b20gKiAyICsgYnIud2lkdGgpIC0gKGFJZCArIGFyLmJvdHRvbSAqIDIgKyBhci53aWR0aCk7CiAgICB9KTsKICAgIGxldCBlZGl0b3IgPSBjYW5kaWRhdGVzWzBdOwogICAgaWYgKCFlZGl0b3IpIHsKICAgICAgcmV0dXJuIHsKICAgICAgICBvazogZmFsc2UsCiAgICAgICAgbWVzc2FnZTogJ0NoxrBhIG5o4bqtbiBkaeG7h24gxJHGsOG7o2Mgw7Qgbmjhuq1wIHRpbiBuaOG6r24uIEjDo3kgbeG7nyBt4buZdCBuaMOzbSB0csOqbiBaYWxvIFdlYi4nCiAgICAgIH07CiAgICB9CiAgICBpZiAoZWRpdG9yLmNsb3Nlc3QoJ1tjb250ZW50ZWRpdGFibGU9InRydWUiXScpKSB7CiAgICAgIGVkaXRvciA9IGVkaXRvci5jbG9zZXN0KCdbY29udGVudGVkaXRhYmxlPSJ0cnVlIl0nKTsKICAgIH0KICAgIHdpbmRvdy5fX3phbG9MYXN0RWRpdG9yID0gZWRpdG9yOwogICAgZWRpdG9yLmNsaWNrKCk7CiAgICBlZGl0b3IuZm9jdXMoKTsKICAgIGlmIChlZGl0b3IuaXNDb250ZW50RWRpdGFibGUgfHwgZWRpdG9yLmdldEF0dHJpYnV0ZSgnY29udGVudGVkaXRhYmxlJykgPT09ICd0cnVlJykgewogICAgICBjb25zdCBzZWxlY3Rpb24gPSB3aW5kb3cuZ2V0U2VsZWN0aW9uKCk7CiAgICAgIGNvbnN0IHJhbmdlID0gZG9jdW1lbnQuY3JlYXRlUmFuZ2UoKTsKICAgICAgcmFuZ2Uuc2VsZWN0Tm9kZUNvbnRlbnRzKGVkaXRvcik7CiAgICAgIHNlbGVjdGlvbi5yZW1vdmVBbGxSYW5nZXMoKTsKICAgICAgc2VsZWN0aW9uLmFkZFJhbmdlKHJhbmdlKTsKICAgICAgZG9jdW1lbnQuZXhlY0NvbW1hbmQoJ2RlbGV0ZScsIGZhbHNlLCBudWxsKTsKICAgICAgY29uc3QgbGluZXMgPSBtc2cuc3BsaXQoJ1xuJyk7CiAgICAgIGZvciAobGV0IGkgPSAwOyBpIDwgbGluZXMubGVuZ3RoOyBpKyspIHsKICAgICAgICBpZiAoaSA+IDApIHsKICAgICAgICAgIGRvY3VtZW50LmV4ZWNDb21tYW5kKCdpbnNlcnRMaW5lQnJlYWsnLCBmYWxzZSwgbnVsbCk7CiAgICAgICAgfQogICAgICAgIGlmIChsaW5lc1tpXSkgewogICAgICAgICAgZG9jdW1lbnQuZXhlY0NvbW1hbmQoJ2luc2VydFRleHQnLCBmYWxzZSwgbGluZXNbaV0pOwogICAgICAgIH0KICAgICAgfQogICAgICBpZiAoIShlZGl0b3IuaW5uZXJUZXh0IHx8ICcnKS50cmltKCkpIHsKICAgICAgICBlZGl0b3IuaW5uZXJIVE1MID0gJyc7CiAgICAgICAgbGluZXMuZm9yRWFjaCgobGluZSwgaWR4KSA9PiB7CiAgICAgICAgICBjb25zdCBkaXYgPSBkb2N1bWVudC5jcmVhdGVFbGVtZW50KCdkaXYnKTsKICAgICAgICAgIGRpdi5pZCA9ICdpbnB1dF9saW5lXycgKyBpZHg7CiAgICAgICAgICBpZiAobGluZSkgZGl2LnRleHRDb250ZW50ID0gbGluZTsKICAgICAgICAgIGVsc2UgZGl2LmFwcGVuZENoaWxkKGRvY3VtZW50LmNyZWF0ZUVsZW1lbnQoJ2JyJykpOwogICAgICAgICAgZWRpdG9yLmFwcGVuZENoaWxkKGRpdik7CiAgICAgICAgfSk7CiAgICAgIH0KICAgICAgZWRpdG9yLmRpc3BhdGNoRXZlbnQobmV3IElucHV0RXZlbnQoJ2lucHV0JywgeyBidWJibGVzOiB0cnVlLCBpbnB1dFR5cGU6ICdpbnNlcnRUZXh0JywgZGF0YTogbXNnIH0pKTsKICAgICAgZWRpdG9yLmRpc3BhdGNoRXZlbnQobmV3IEV2ZW50KCdjaGFuZ2UnLCB7IGJ1YmJsZXM6IHRydWUgfSkpOwogICAgfSBlbHNlIHsKICAgICAgY29uc3QgcHJvdG8gPSBlZGl0b3IudGFnTmFtZSA9PT0gJ1RFWFRBUkVBJwogICAgICAgID8gSFRNTFRleHRBcmVhRWxlbWVudC5wcm90b3R5cGUgOiBIVE1MSW5wdXRFbGVtZW50LnByb3RvdHlwZTsKICAgICAgY29uc3Qgc2V0dGVyID0gT2JqZWN0LmdldE93blByb3BlcnR5RGVzY3JpcHRvcihwcm90bywgJ3ZhbHVlJyk/LnNldDsKICAgICAgaWYgKHNldHRlcikgc2V0dGVyLmNhbGwoZWRpdG9yLCBtc2cpOyBlbHNlIGVkaXRvci52YWx1ZSA9IG1zZzsKICAgICAgZWRpdG9yLmRpc3BhdGNoRXZlbnQobmV3IElucHV0RXZlbnQoJ2lucHV0JywgeyBidWJibGVzOiB0cnVlLCBpbnB1dFR5cGU6ICdpbnNlcnRUZXh0JywgZGF0YTogbXNnIH0pKTsKICAgICAgZWRpdG9yLmRpc3BhdGNoRXZlbnQobmV3IEV2ZW50KCdjaGFuZ2UnLCB7IGJ1YmJsZXM6IHRydWUgfSkpOwogICAgfQogICAgcmV0dXJuIHsgb2s6IHRydWUsIG1lc3NhZ2U6ICfEkMOjIMSRaeG7gW4gxJHhuqd5IMSR4bunIG7hu5lpIGR1bmcgdsOgbyDDtCBjaGF0IFphbG8uJyB9OwogIH0gY2F0Y2ggKGVycikgewogICAgcmV0dXJuIHsgb2s6IGZhbHNlLCBtZXNzYWdlOiAnTOG7l2kgxJFp4buBbiBu4buZaSBkdW5nIHRyw6puIFphbG8gV2ViOiAnICsgKGVyciAmJiBlcnIubWVzc2FnZSA/IGVyci5tZXNzYWdlIDogU3RyaW5nKGVycikpIH07CiAgfQp9KSgpOw==";
        private const string JsTriggerSendB64 = "KCgpID0+IHsKICB0cnkgewogICAgY29uc3QgdmlzaWJsZSA9IGUgPT4gewogICAgICBpZiAoIWUpIHJldHVybiBmYWxzZTsKICAgICAgY29uc3QgciA9IGUuZ2V0Qm91bmRpbmdDbGllbnRSZWN0KCk7CiAgICAgIGNvbnN0IHMgPSBnZXRDb21wdXRlZFN0eWxlKGUpOwogICAgICByZXR1cm4gci53aWR0aCA+IDEwICYmIHIuaGVpZ2h0ID4gMTAgJiYgcy5kaXNwbGF5ICE9PSAnbm9uZScgJiYKICAgICAgICBzLnZpc2liaWxpdHkgIT09ICdoaWRkZW4nICYmIE51bWJlcihzLm9wYWNpdHkgfHwgMSkgPiAwICYmICFlLmRpc2FibGVkOwogICAgfTsKICAgIGNvbnN0IGVkaXRvciA9IHdpbmRvdy5fX3phbG9MYXN0RWRpdG9yIHx8IGRvY3VtZW50LnF1ZXJ5U2VsZWN0b3IoJyNyaWNoSW5wdXQsIFtjb250ZW50ZWRpdGFibGU9InRydWUiXScpOwogICAgY29uc3Qgc2VuZFNlbGVjdG9ycyA9IFsKICAgICAgJ1tkYXRhLXRyYW5zbGF0ZS10aXRsZT0iU1RSX1NFTkQiXScsCiAgICAgICdbdGl0bGU9Ikfhu61pIHRpbiBuaOG6r24iXScsCiAgICAgICdbdGl0bGU9IlNlbmQgbWVzc2FnZSJdJywKICAgICAgJy5zZW5kLW1zZy1idG4nLAogICAgICAnLmZhLVNlbnQtbXNnXzI0X0xpbmUnLAogICAgICAnW2RhdGEtaWQqPSJidG5fTWFpbl9TZW5kIl0nCiAgICBdOwogICAgbGV0IHNlbmRCdG4gPSBudWxsOwogICAgZm9yIChjb25zdCBzZWwgb2Ygc2VuZFNlbGVjdG9ycykgewogICAgICBjb25zdCBmb3VuZCA9IGRvY3VtZW50LnF1ZXJ5U2VsZWN0b3Ioc2VsKTsKICAgICAgaWYgKGZvdW5kICYmIHZpc2libGUoZm91bmQpKSB7CiAgICAgICAgc2VuZEJ0biA9IGZvdW5kLmNsb3Nlc3QoJ2Rpdltyb2xlPSJidXR0b24iXSwgYnV0dG9uLCAuei0tYnRuLS12MiwgLnNlbmQtbXNnLWJ0bicpIHx8IGZvdW5kOwogICAgICAgIGJyZWFrOwogICAgICB9CiAgICB9CiAgICBpZiAoIXNlbmRCdG4pIHsKICAgICAgY29uc3QgYWxsQnV0dG9ucyA9IFsuLi5kb2N1bWVudC5xdWVyeVNlbGVjdG9yQWxsKCdidXR0b24sIFtyb2xlPSJidXR0b24iXSwgLnotLWJ0bi0tdjIsIGkuZmEtU2VudC1tc2dfMjRfTGluZScpXS5maWx0ZXIodmlzaWJsZSk7CiAgICAgIGNvbnN0IG1hdGNoZWQgPSBhbGxCdXR0b25zLmZpbmQoYiA9PiB7CiAgICAgICAgY29uc3QgbWV0YSA9ICgoYi5pbm5lclRleHQgfHwgJycpICsgJyAnICsgKGIudGl0bGUgfHwgJycpICsgJyAnICsgKGIuY2xhc3NOYW1lIHx8ICcnKSArICcgJyArIChiLmdldEF0dHJpYnV0ZSgnYXJpYS1sYWJlbCcpIHx8ICcnKSArICcgJyArIChiLmdldEF0dHJpYnV0ZSgnZGF0YS10cmFuc2xhdGUtdGl0bGUnKSB8fCAnJykpLnRvTG93ZXJDYXNlKCk7CiAgICAgICAgcmV0dXJuIC9zdHJfc2VuZHxn4butaSB0aW4gbmjhuq9ufHNlbmQgbWVzc2FnZXxzZW50LW1zZ3xzZW5kLW1zZ3xeZ+G7rWkkfF5zZW5kJC9pLnRlc3QobWV0YS50cmltKCkpOwogICAgICB9KTsKICAgICAgaWYgKG1hdGNoZWQpIHNlbmRCdG4gPSBtYXRjaGVkLmNsb3Nlc3QoJ2Rpdltyb2xlPSJidXR0b24iXSwgYnV0dG9uLCAuei0tYnRuLS12MicpIHx8IG1hdGNoZWQ7CiAgICB9CiAgICBpZiAoc2VuZEJ0bikgewogICAgICBjb25zdCByZWN0ID0gc2VuZEJ0bi5nZXRCb3VuZGluZ0NsaWVudFJlY3QoKTsKICAgICAgY29uc3QgY3ggPSByZWN0LmxlZnQgKyByZWN0LndpZHRoIC8gMjsKICAgICAgY29uc3QgY3kgPSByZWN0LnRvcCArIHJlY3QuaGVpZ2h0IC8gMjsKICAgICAgc2VuZEJ0bi5kaXNwYXRjaEV2ZW50KG5ldyBQb2ludGVyRXZlbnQoJ3BvaW50ZXJkb3duJywgeyBidWJibGVzOiB0cnVlLCBjYW5jZWxhYmxlOiB0cnVlLCBjbGllbnRYOiBjeCwgY2xpZW50WTogY3kgfSkpOwogICAgICBzZW5kQnRuLmRpc3BhdGNoRXZlbnQobmV3IE1vdXNlRXZlbnQoJ21vdXNlZG93bicsIHsgYnViYmxlczogdHJ1ZSwgY2FuY2VsYWJsZTogdHJ1ZSwgY2xpZW50WDogY3gsIGNsaWVudFk6IGN5IH0pKTsKICAgICAgc2VuZEJ0bi5kaXNwYXRjaEV2ZW50KG5ldyBQb2ludGVyRXZlbnQoJ3BvaW50ZXJ1cCcsIHsgYnViYmxlczogdHJ1ZSwgY2FuY2VsYWJsZTogdHJ1ZSwgY2xpZW50WDogY3gsIGNsaWVudFk6IGN5IH0pKTsKICAgICAgc2VuZEJ0bi5kaXNwYXRjaEV2ZW50KG5ldyBNb3VzZUV2ZW50KCdtb3VzZXVwJywgeyBidWJibGVzOiB0cnVlLCBjYW5jZWxhYmxlOiB0cnVlLCBjbGllbnRYOiBjeCwgY2xpZW50WTogY3kgfSkpOwogICAgICBzZW5kQnRuLmNsaWNrKCk7CiAgICAgIHJldHVybiB7IG9rOiB0cnVlLCBtZXNzYWdlOiAnxJDDoyB04buxIMSR4buZbmcgxJFp4buBbiDEkeG6p3kgxJHhu6cgbGluayB2w6AgYuG6pW0gbsO6dCBH4butaSB0aMOgbmggY8O0bmchJyB9OwogICAgfQogICAgaWYgKGVkaXRvcikgewogICAgICBlZGl0b3IuZm9jdXMoKTsKICAgICAgY29uc3QgZW50ZXJPcHRzID0gewogICAgICAgIGtleTogJ0VudGVyJywgY29kZTogJ0VudGVyJywga2V5Q29kZTogMTMsIHdoaWNoOiAxMywgY2hhckNvZGU6IDEzLAogICAgICAgIGJ1YmJsZXM6IHRydWUsIGNhbmNlbGFibGU6IHRydWUKICAgICAgfTsKICAgICAgZWRpdG9yLmRpc3BhdGNoRXZlbnQobmV3IEtleWJvYXJkRXZlbnQoJ2tleWRvd24nLCBlbnRlck9wdHMpKTsKICAgICAgZWRpdG9yLmRpc3BhdGNoRXZlbnQobmV3IEtleWJvYXJkRXZlbnQoJ2tleXByZXNzJywgZW50ZXJPcHRzKSk7CiAgICAgIGVkaXRvci5kaXNwYXRjaEV2ZW50KG5ldyBLZXlib2FyZEV2ZW50KCdrZXl1cCcsIGVudGVyT3B0cykpOwogICAgICByZXR1cm4geyBvazogdHJ1ZSwgbWVzc2FnZTogJ8SQw6MgdOG7sSDEkeG7mW5nIMSRaeG7gW4gxJHhuqd5IMSR4bunIGxpbmsgdsOgIG5o4bqlbiBFbnRlciBn4butaSB0aMOgbmggY8O0bmchJyB9OwogICAgfQogICAgcmV0dXJuIHsgb2s6IGZhbHNlLCBtZXNzYWdlOiAnS2jDtG5nIHTDrG0gdGjhuqV5IG7DunQgR+G7rWkgdHLDqm4gWmFsbyBXZWIuJyB9OwogIH0gY2F0Y2ggKGVycikgewogICAgcmV0dXJuIHsgb2s6IGZhbHNlLCBtZXNzYWdlOiAnTOG7l2kga8OtY2ggaG/huqF0IG7DunQgR+G7rWk6ICcgKyAoZXJyICYmIGVyci5tZXNzYWdlID8gZXJyLm1lc3NhZ2UgOiBTdHJpbmcoZXJyKSkgfTsKICB9Cn0pKCk7";

        private static string DecodeB64(string b64) => Encoding.UTF8.GetString(Convert.FromBase64String(b64));

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

            var configGroup = new Panel { Dock = DockStyle.Top, Height = 72 };

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

            autoPilotBtn.Click += async (_, _) => await RunAutoSendFlowAsync(true, autoPilotBtn, sendOnceBtn, fetchPreviewBtn, stopBtn, loadGroupsBtn);
            sendOnceBtn.Click += async (_, _) => await RunAutoSendFlowAsync(false, autoPilotBtn, sendOnceBtn, fetchPreviewBtn, stopBtn, loadGroupsBtn);

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
            string script = DecodeB64(JsLoadGroupsB64);

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
                        var (_, fetchedMsg) = await FetchAndBuildSaleMessageAsync(token);
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

        private static string BuildClickConversationScript(string groupNameJson) =>
            DecodeB64(JsClickConvB64).Replace("__GROUP_NAME__", groupNameJson);

        private static string BuildTypeSearchBoxScript(string groupNameJson) =>
            DecodeB64(JsSearchBoxB64).Replace("__GROUP_NAME__", groupNameJson);

        private static string BuildSelectFirstSearchResultScript() =>
            DecodeB64(JsSelectFirstB64);

        private static string BuildFillScript(string messageJson) =>
            DecodeB64(JsFillB64).Replace("__MESSAGE__", messageJson);

        private static string BuildTriggerSendScript() =>
            DecodeB64(JsTriggerSendB64);

        private async Task<(bool Ok, string Message)> FetchAndBuildSaleMessageAsync(CancellationToken token)
        {
            var apiKey = apiKeyBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return (false, "Hãy nhập API Key Addlivetag ở dòng cấu hình.");
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
