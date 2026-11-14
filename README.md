# Shopee Affiliate Agent

Windows desktop helper using WebView2. It opens Shopee/Affiliate pages inside the app, lets the user search by keyword in the embedded page, queue up to 100 manually selected Shopee product URLs, associate Affiliate URLs returned by Shopee Custom Link, and export CSV.

## Build

GitHub Actions builds a self-contained Windows x64 publish artifact on push to `main` or via **Actions → Build Shopee Affiliate Agent EXE → Run workflow**.

Download artifact: `ShopeeAffiliateAgent-Windows-x64`.

## Requirements

- Windows 10/11 x64
- Microsoft Edge WebView2 Runtime (Evergreen)

## Important limitations

This project does not scrape private Shopee pages, read browser cookies, automate private endpoints, or bypass account protections. Without an authorized Shopee Affiliate API, product selection and Custom Link creation remain on Shopee's official pages; the app manages the resulting URLs locally.
