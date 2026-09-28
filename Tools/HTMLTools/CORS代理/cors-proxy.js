// 本地开发服务：普通路径转发资源，/capture 在独立浏览器中计算坐标和截图，/render 保留旧接口。
// 浏览器解构工具只能读取同源内容，因此服务仅监听本机并为响应添加 CORS 头。
const http = require('http');
const https = require('https');
const fs = require('fs/promises');
const os = require('os');
const path = require('path');
const { execFile } = require('child_process');
const { promisify } = require('util');
const { URL } = require('url');
const { capturePage } = require('./browser-capture');

const execFileAsync = promisify(execFile);
const PORT = Number(process.env.HTML_TO_UGUI_PORT || 8888);
const HOST = '127.0.0.1';
const MAX_REDIRECTS = 5;
const MAX_RESPONSE_BYTES = 20 * 1024 * 1024;
class InputError extends Error {}
const browserCandidates = [
    process.env.HTML_TO_UGUI_BROWSER,
    'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
    'C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe',
    process.env.LOCALAPPDATA && path.join(process.env.LOCALAPPDATA, 'Google', 'Chrome', 'Application', 'chrome.exe'),
    'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
    'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
    '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
    '/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge',
    'google-chrome', 'chromium', 'chromium-browser', 'microsoft-edge'
].filter(Boolean);

function parseTargetUrl(urlStr) {
    const target = new URL(urlStr);
    if (target.protocol !== 'http:' && target.protocol !== 'https:') {
        throw new InputError('仅支持 HTTP 或 HTTPS 网页');
    }
    return target;
}

function doRequest(urlStr, redirectCount = 0) {
    return new Promise((resolve, reject) => {
        if (redirectCount > MAX_REDIRECTS) return reject(new Error('重定向次数过多'));
        const target = parseTargetUrl(urlStr);
        const client = target.protocol === 'https:' ? https : http;
        const request = client.get(target, {
            headers: {
                'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
                'Accept': '*/*',
                'Accept-Language': 'zh-CN,zh;q=0.9'
            }
        }, (upstream) => {
            if (upstream.statusCode >= 300 && upstream.statusCode < 400 && upstream.headers.location) {
                const redirectUrl = new URL(upstream.headers.location, target).href;
                upstream.resume();
                resolve(doRequest(redirectUrl, redirectCount + 1));
                return;
            }

            const chunks = [];
            let size = 0;
            upstream.on('data', (chunk) => {
                size += chunk.length;
                if (size > MAX_RESPONSE_BYTES) {
                    upstream.destroy(new Error('远程资源超过 20 MB 限制'));
                    return;
                }
                chunks.push(chunk);
            });
            upstream.on('end', () => resolve({
                statusCode: upstream.statusCode,
                contentType: upstream.headers['content-type'] || 'application/octet-stream',
                body: Buffer.concat(chunks)
            }));
            upstream.on('error', reject);
        });
        request.setTimeout(15000, () => request.destroy(new Error('远程请求超时')));
        request.on('error', reject);
    });
}

function parseViewport(widthValue, heightValue) {
    const width = Number(widthValue || 1920);
    const height = Number(heightValue || 1080);
    if (!Number.isInteger(width) || !Number.isInteger(height) || width < 320 || width > 4096 || height < 240 || height > 4096) {
        throw new InputError('视口尺寸必须在 320×240 至 4096×4096 范围内');
    }
    return { width, height };
}

async function readJsonBody(req) {
    const chunks = [];
    let size = 0;
    for await (const chunk of req) {
        size += chunk.length;
        if (size > MAX_RESPONSE_BYTES) throw new InputError('HTML 输入超过 20 MB 限制');
        chunks.push(chunk);
    }
    try { return JSON.parse(Buffer.concat(chunks).toString('utf8')); }
    catch { throw new InputError('请求体不是有效的 JSON'); }
}

async function renderPage(urlStr, width, height) {
    const target = parseTargetUrl(urlStr);
    const profile = await fs.mkdtemp(path.join(os.tmpdir(), 'htmltougui-render-'));
    let lastError;
    try {
        for (const browser of browserCandidates) {
            try {
                // 独立临时配置避免触碰用户的浏览器会话；虚拟时间给异步页面留出渲染窗口。
                const { stdout } = await execFileAsync(browser, [
                    '--headless=new', '--disable-gpu', '--disable-extensions',
                    '--no-first-run', '--no-default-browser-check',
                    `--user-data-dir=${profile}`, `--window-size=${width},${height}`,
                    '--virtual-time-budget=8000',
                    '--dump-dom', target.href
                ], { encoding: 'utf8', timeout: 40000, maxBuffer: MAX_RESPONSE_BYTES, windowsHide: true });
                if (!/<html\b/i.test(stdout) || !/<body\b/i.test(stdout)) {
                    throw new Error('浏览器没有返回可解析的页面');
                }
                if (stdout.includes('<div id="main-frame-error"') && stdout.includes('class="error-code"')) {
                    const code = stdout.match(/\bERR_[A-Z_]+\b/)?.[0] || '网络错误';
                    throw new Error(`浏览器无法打开网页：${code}`);
                }
                return stdout;
            } catch (error) {
                if (error.code !== 'ENOENT') lastError = error;
            }
        }
        throw new Error(`浏览器渲染失败：${lastError?.message || '未找到 Chrome 或 Edge'}`);
    } finally {
        // 只清理本次 mkdtemp 在系统临时目录创建的专属配置目录。
        const resolved = path.resolve(profile);
        if (path.dirname(resolved) === path.resolve(os.tmpdir()) && path.basename(resolved).startsWith('htmltougui-render-')) {
            await fs.rm(resolved, { recursive: true, force: true }).catch(() => {});
        }
    }
}

const server = http.createServer(async (req, res) => {
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
    res.setHeader('Access-Control-Allow-Headers', 'Content-Type');
    res.setHeader('Cache-Control', 'no-store');
    if (req.method === 'OPTIONS') { res.writeHead(204); res.end(); return; }
    if (req.method !== 'GET' && req.method !== 'POST') { res.writeHead(405); res.end('仅支持 GET、POST'); return; }

    try {
        const requestUrl = new URL(req.url, `http://${HOST}:${PORT}`);
        if (requestUrl.pathname === '/capture') {
            // URL 与粘贴的 HTML 使用同一条后台采集路径；主工具页不再挂载大型 iframe 计算坐标。
            const input = req.method === 'POST' ? await readJsonBody(req) : Object.fromEntries(requestUrl.searchParams);
            const { width, height } = parseViewport(input.width, input.height);
            const url = input.url ? parseTargetUrl(input.url).href : null;
            const html = typeof input.html === 'string' ? input.html : null;
            if ((url === null) === (html === null)) throw new InputError('必须且只能提供 url 或 html');
            if (req.method === 'GET' && !url) throw new InputError('GET /capture 需要 url 参数');
            if (html !== null && Buffer.byteLength(html) > MAX_RESPONSE_BYTES) throw new InputError('HTML 输入超过 20 MB 限制');
            const controller = new AbortController();
            const timeout = setTimeout(() => controller.abort(new Error('浏览器采集超过 60 秒')), 60000);
            res.once('close', () => { if (!res.writableEnded) controller.abort(new Error('客户端已断开')); });
            try {
                console.log(`正在采集: ${url || '粘贴的 HTML'} (${width}×${height})`);
                const result = await capturePage({ url, html, width, height, browserCandidates, signal: controller.signal });
                if (res.destroyed) return;
                res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
                res.end(JSON.stringify(result));
                console.log(`采集完成: ${result.total} 元素，${result.screenshots.length} 张预览图`);
            } finally { clearTimeout(timeout); }
            return;
        }
        if (req.method !== 'GET') throw new InputError('此接口仅支持 GET');
        if (requestUrl.pathname === '/') {
            // URL 模式直接从本地服务打开工具，避免 file:// 的浏览器权限差异。
            const toolHtml = await fs.readFile(path.join(__dirname, '..', 'HTML解构工具.html'));
            res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
            res.end(toolHtml);
            return;
        }
        if (requestUrl.pathname === '/render') {
            const targetUrl = requestUrl.searchParams.get('url');
            if (!targetUrl) { res.writeHead(400); res.end('缺少 url 参数'); return; }
            const { width, height } = parseViewport(requestUrl.searchParams.get('width'), requestUrl.searchParams.get('height'));
            console.log(`正在渲染: ${targetUrl}`);
            const html = await renderPage(targetUrl, width, height);
            res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
            res.end(html);
            console.log(`渲染完成，数据长度: ${Buffer.byteLength(html)}`);
            return;
        }

        // 保留旧版 /<编码后的 URL> 接口，供 HTML、CSS 等资源读取使用。
        const targetUrl = decodeURIComponent(req.url.slice(1));
        if (!targetUrl) { res.writeHead(400); res.end('请在路径中提供要抓取的网址'); return; }
        console.log(`正在获取: ${targetUrl}`);
        const result = await doRequest(targetUrl);
        res.writeHead(result.statusCode, { 'Content-Type': result.contentType });
        res.end(result.body);
        console.log(`获取完成，数据长度: ${result.body.length}`);
    } catch (error) {
        console.error(`请求失败: ${error.message}`);
        if (!res.destroyed && !res.headersSent) {
            res.writeHead(error instanceof TypeError || error instanceof InputError ? 400 : 502, { 'Content-Type': 'text/plain; charset=utf-8' });
            res.end(error.message);
        }
    }
});

server.listen(PORT, HOST, () => {
    console.log(`✅ CORS 代理已启动：http://${HOST}:${PORT}`);
    console.log(`   打开 http://${HOST}:${PORT}/ 使用 HTML 解构工具`);
    console.log('   URL/HTML 解构使用 /capture 在独立浏览器中计算坐标并生成预览');
    console.log(`   自定义 CORS 代理可填写 http://${HOST}:${PORT}/`);
});
