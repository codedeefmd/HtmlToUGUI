# HTML Deconstruction Tool Tutorial

## Overview

The HTML Deconstruction Tool (`HTML解构工具.html`) is an offline web page that converts ordinary HTML into an HTML structure annotated with `data-u-left`, `data-u-top`, `data-u-width`, and `data-u-height` attributes. The output is directly consumable by the HtmlToUGUI conversion pipeline.

For large pages, the code viewer initially shows the first 120 structure nodes. Inline `<style>` and `<script>` blocks longer than 8192 characters are collapsed too. Use **View Full Code** to expand them. **Copy**, **Download**, and file syncing always use the complete HTML.
For inputs over 100,000 characters, the input field turns off line wrapping to reduce loading stalls; the content remains scrollable and editable.

## Usage

### Method 1: Paste HTML Directly

1. Prefer starting the local proxy and opening `http://127.0.0.1:8888/`; opening `Tools/HTMLTools/HTML解构工具.html` directly also works
2. Paste HTML into the left input panel
3. Click **"Deconstruct HTML"**
4. Copy the output from the right panel
5. Paste into the HtmlToUGUI window and click **Start Convert**

### Method 2: Load via URL (local proxy recommended)

Start the local proxy below and open `http://127.0.0.1:8888/` in your browser. Enter a full `http://` or `https://` URL, click **Load URL**, then **Deconstruct HTML**. The proxy renders the page, measures elements, and takes screenshots in an isolated Chrome or Edge process. The tool progressively displays a scrollable static preview without laying out the entire site in an iframe. The proxy field can remain empty. Set the preview viewport dimensions before loading; changing the viewport or input HTML causes the next deconstruction to capture the new content.

For example, `https://www.frontendmentor.io/challenges/qr-code-component-iux_sIO_H` captures the entire Frontend Mentor challenge page, not just the QR component design image.

The result is a snapshot, and the preview image does not support page interactions. If background capture fails while the tool is served locally, it displays the error; opening the HTML file directly permits the older iframe fallback. Very long pages may show only the first part of the screenshot, while the full HTML and element coordinates are still returned. Successful deconstruction does not guarantee pixel-perfect reproduction in Unity, which depends on HtmlToUGUI's CSS and component support.

### Method 3: Load Local File

Click the **"Choose File"** button to load a local `.html` file directly.

## AI-Generated HTML Prompt Skills

To bypass the Deconstruction Tool entirely, attach the prompt skill files from `Tools/HTMLTools/AI生成HTML提示词/` to your AI prompting workflow:

### SKILL_动态定位 (Recommended)

- **File**: `SKILL_动态定位.md`
- **Approach**: Uses `getBoundingClientRect()` to capture element positions and dimensions, then injects `data-u-left/top/width/height` attributes
- **Result**: Cleaner, more semantic HTML output with better compatibility

### SKILL_绝对定位

- **File**: `SKILL_绝对定位.md`
- **Approach**: Uses CSS `position: absolute` with explicit `left/top/width/height` values
- **Note**: All elements must use `position: absolute`. Avoid `margin`, `display`, `border` and other layout-affecting properties

## CORS Proxy

Loading remote pages via URL is subject to browser CORS restrictions. The Node.js service in `Tools/HTMLTools/CORS代理/` listens only on this machine and provides resource fetching plus Chrome/Edge rendering for dynamic pages.

### Starting the Proxy

**Option A (Windows)**: Double-click `CORS代理.bat`
**Option A (macOS / Linux)**: Run `bash CORS代理.sh` in the terminal

**Option B**: Run in terminal:
```
node cors-proxy.js
```

Expected output:
```
✅ CORS 代理已启动：http://127.0.0.1:8888
```

### Configuring the Proxy

The default address needs no configuration. To enter it manually, use the **Local Proxy** field:
```
http://127.0.0.1:8888/
```

Then enter the target URL and click **"Load URL"**.

### Prerequisites

- Node.js 22.4 or newer is required for background capture (built-in WebSocket)
- Chrome or Edge must be installed for dynamic-page rendering; `HTML_TO_UGUI_BROWSER` can select a browser executable
- The proxy is intended for local development only; do not expose it publicly
