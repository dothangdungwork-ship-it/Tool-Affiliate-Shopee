# Tool Affiliate

Windows desktop helper for managing a Shopee Affiliate product-link queue without API access.

## V3 features

- Embedded WebView2 opens Shopee and Shopee Affiliate so the user can sign in directly.
- Search Shopee by keyword from the app.
- Add the currently open product page, or paste multiple product URLs (one per line).
- Validate product URLs and reject duplicate entries; maximum 100 products.
- Paste multiple Affiliate URLs returned by Shopee's official Custom Link workflow. The app assigns them in order to products still waiting for a link.
- Batch assignment is all-or-nothing when a URL is invalid or there are more Affiliate URLs than pending products, reducing accidental row shifts.
- Automatically saves the product queue as JSON under the current Windows user's LocalAppData folder.
- Export the queue to UTF-8 CSV, including original URL, Affiliate URL and status.
- Opens/copies selected product links.

## Safe operating model

This app does **not** use Shopee APIs, private endpoints, browser-cookie extraction, page scraping, CAPTCHA/OTP bypass, or unattended account automation. The user signs in and creates Affiliate links inside Shopee's own supported interface. The app only manages URLs the user explicitly adds or pastes.

The no-API workflow is therefore assisted rather than fully unattended: product discovery and queue management are streamlined, while Shopee remains responsible for generating the Affiliate URL. Availability and exact menu labels in Shopee may change.

## Build

The GitHub Actions workflow builds a self-contained Windows x64 publish artifact on pushes to main or manual workflow dispatch.

Requirements for local development:
- .NET 8 SDK
- Windows 10/11
- WebView2 Runtime (normally present on supported Windows systems; install the Evergreen Runtime if missing)

Run locally:

```powershell
dotnet restore
dotnet run
```

## Data and privacy

The queue is stored locally at:

`%LOCALAPPDATA%\\ShopeeAffiliateAgent\\products-v3.json`

The embedded browser profile is stored locally at:

`%LOCALAPPDATA%\\ShopeeAffiliateAgent\\WebViewProfile`

Do not share this profile folder. It may contain the browser's local sign-in state. The app does not read or export browser cookies.
