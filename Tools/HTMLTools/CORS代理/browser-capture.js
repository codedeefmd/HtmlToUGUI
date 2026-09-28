// 独立浏览器采集器：在同一次页面渲染中读取结构、计算坐标并截取预览。
// 每个请求使用独立临时配置目录；调试端口只绑定本机，结束时只清理本请求创建的目录。
const fs = require('fs/promises');
const os = require('os');
const path = require('path');
const { spawn } = require('child_process');

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const MAX_SCREENSHOT_PIXELS = 16_000_000;
const SCREENSHOT_TILE_HEIGHT = 1600;
const PREVIEW_MAX_WIDTH = 960;
const MAX_CAPTURE_BYTES = 20 * 1024 * 1024;

// 这段函数在被采集的页面内执行。它与工具页的命名规则保持一致，坐标仍相对 body 左上角。
// 先读取所有几何信息，再写 data-u-*，避免属性选择器引发的二次布局改变测量值。
function annotateDocument() {
    const toCamelCase = str => {
        if (!str || typeof str !== 'string') return str;
        if (/^[a-z][a-zA-Z0-9]*$/.test(str)) return str;
        const parts = str.split(/[-_]+/);
        if (parts.length === 1 && str === parts[0]) return /^[A-Z]/.test(str) ? str.charAt(0).toLowerCase() + str.slice(1) : str;
        let result = parts[0].toLowerCase();
        for (let i = 1; i < parts.length; i++) if (parts[i].length > 0) result += parts[i].charAt(0).toUpperCase() + parts[i].slice(1).toLowerCase();
        return result;
    };
    const tagPurposeMap = {
        div: 'container', span: 'span', p: 'paragraph',
        h1: 'heading', h2: 'heading', h3: 'heading', h4: 'heading', h5: 'heading', h6: 'heading',
        ul: 'list', ol: 'orderedList', li: 'listItem', a: 'link', img: 'image',
        table: 'table', thead: 'tableHead', tbody: 'tableBody', tfoot: 'tableFoot', tr: 'tableRow', th: 'tableHeader', td: 'tableCell',
        form: 'form', input: 'input', button: 'button', select: 'select', textarea: 'textarea', label: 'label',
        section: 'section', article: 'article', nav: 'navigation', header: 'header', footer: 'footer', main: 'main', aside: 'aside',
        figure: 'figure', figcaption: 'figcaption', blockquote: 'blockquote', pre: 'pre', code: 'code',
        em: 'emphasis', strong: 'strong', small: 'small', br: 'break', hr: 'horizontalRule'
    };
    const body = document.body;
    if (!body) throw new Error('页面没有 body 元素');
    const rawHtml = '<!DOCTYPE html>\n' + document.documentElement.outerHTML;
    const elements = [body, ...body.querySelectorAll('*')].filter(el => el.tagName !== 'STYLE' && el.tagName !== 'SCRIPT');
    const bodyRect = body.getBoundingClientRect();
    const rects = elements.map(el => {
        const rect = el.getBoundingClientRect();
        return [Math.round(rect.left - bodyRect.left), Math.round(rect.top - bodyRect.top), Math.round(rect.width), Math.round(rect.height)];
    });
    elements.forEach((el, index) => {
        let name;
        if (el.id?.trim()) name = toCamelCase(el.id.trim());
        else if (el.hasAttribute('class') && el.getAttribute('class').trim()) name = toCamelCase(el.getAttribute('class').trim().split(/\s+/)[0]);
        else name = tagPurposeMap[el.tagName.toLowerCase()] || el.tagName.toLowerCase();
        const usedNames = new Set();
        for (let sibling = el.previousElementSibling; sibling; sibling = sibling.previousElementSibling) {
            if (sibling.hasAttribute('data-u-name')) usedNames.add(sibling.getAttribute('data-u-name'));
        }
        if (usedNames.has(name)) {
            let suffix = 2;
            while (usedNames.has(name + suffix)) suffix++;
            name += suffix;
        }
        el.setAttribute('data-u-name', name);
        ['left', 'top', 'width', 'height'].forEach((key, offset) => el.setAttribute('data-u-' + key, rects[index][offset]));
    });
    return {
        rawHtml,
        annotatedHtml: '<!DOCTYPE html>\n' + document.documentElement.outerHTML,
        total: elements.length,
        namedCount: elements.length,
        pageUrl: location.href
    };
}

// Chrome 在临时配置目录写出随机端口，避免占用固定端口或连接到用户正在使用的浏览器。
async function waitForDebugPort(profile, browser, signal) {
    for (let attempt = 0; attempt < 100; attempt++) {
        signal?.throwIfAborted();
        try {
            const [port, endpoint] = (await fs.readFile(path.join(profile, 'DevToolsActivePort'), 'utf8')).trim().split(/\r?\n/);
            if (/^\d+$/.test(port) && endpoint?.startsWith('/devtools/browser/')) return `ws://127.0.0.1:${port}${endpoint}`;
        } catch (error) { if (error.code !== 'ENOENT') throw error; }
        if (browser.exitCode !== null) throw new Error(`浏览器提前退出（${browser.exitCode}）`);
        await sleep(100);
    }
    throw new Error('浏览器调试端口启动超时');
}

// CDP 命令按 id 对应异步响应；连接中断和取消时必须让所有未完成命令立即失败。
function connectCdp(url, signal) {
    return new Promise((resolve, reject) => {
        const socket = new WebSocket(url);
        const pending = new Map();
        let nextId = 0;
        let opened = false;
        const closeError = new Error('浏览器调试连接已关闭');
        const startupTimer = setTimeout(() => reject(new Error('浏览器调试连接超时')), 10000);
        startupTimer.unref();
        const failPending = error => {
            for (const task of pending.values()) { clearTimeout(task.timer); task.reject(error); }
            pending.clear();
        };
        socket.addEventListener('message', event => {
            let message;
            try { message = JSON.parse(String(event.data)); } catch { return; }
            if (!message.id) return;
            const task = pending.get(message.id);
            if (!task) return;
            pending.delete(message.id);
            clearTimeout(task.timer);
            if (message.error) task.reject(new Error(`${task.method}: ${message.error.message}`));
            else task.resolve(message.result);
        });
        socket.addEventListener('close', () => { clearTimeout(startupTimer); failPending(closeError); if (!opened) reject(closeError); });
        socket.addEventListener('error', () => { clearTimeout(startupTimer); failPending(closeError); if (!opened) reject(closeError); });
        signal?.addEventListener('abort', () => {
            clearTimeout(startupTimer);
            socket.close();
            failPending(signal.reason || closeError);
            if (!opened) reject(signal.reason || closeError);
        }, { once: true });
        socket.addEventListener('open', () => {
            opened = true;
            clearTimeout(startupTimer);
            resolve({
                close: () => socket.close(),
                send: (method, params = {}, sessionId, timeout = 20000) => new Promise((accept, fail) => {
                    signal?.throwIfAborted();
                    const id = ++nextId;
                    const timer = setTimeout(() => { pending.delete(id); fail(new Error(`${method} 超时`)); }, timeout);
                    pending.set(id, { resolve: accept, reject: fail, timer, method });
                    try { socket.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) })); }
                    catch (error) { clearTimeout(timer); pending.delete(id); fail(error); }
                })
            });
        }, { once: true });
    });
}

// 所有页面脚本均在隔离 Chrome 中执行，工具页面只读取采集结果。
async function evaluate(cdp, session, expression, timeout = 20000) {
    const response = await cdp.send('Runtime.evaluate', {
        expression, awaitPromise: true, returnByValue: true, silent: true, timeout
    }, session, timeout + 1000);
    if (response.exceptionDetails) throw new Error(response.exceptionDetails.text || '页面脚本执行失败');
    if (response.result?.type === 'object' && response.result.subtype === 'error') throw new Error(response.result.description || '页面脚本执行失败');
    return response.result?.value;
}

// readyState 是有上限的加载门槛；不等待可能长期不断流的 networkIdle。
async function waitForPage(cdp, session, signal) {
    for (let attempt = 0; attempt < 120; attempt++) {
        signal?.throwIfAborted();
        const state = await evaluate(cdp, session, 'document.readyState');
        if (state === 'complete') return;
        await sleep(200);
    }
    throw new Error('页面加载超时');
}

// 对外合同：提供 URL 或 HTML、CSS 像素视口和取消信号，返回原始/标注 HTML 与分段截图。
// 截图可因像素上限被截断，HTML/坐标仍覆盖整个主文档；跨域 iframe 内部不在元素列表中。
async function capturePage({ url, html, width, height, browserCandidates, signal }) {
    if (typeof WebSocket !== 'function') throw new Error('浏览器采集需要 Node.js 22.4 或更新版本');
    const profile = await fs.mkdtemp(path.join(os.tmpdir(), 'htmltougui-capture-'));
    let browser, cdp;
    const stopBrowser = () => { if (browser?.exitCode === null) browser.kill(); cdp?.close(); };
    signal?.addEventListener('abort', stopBrowser, { once: true });
    try {
        let startError;
        for (const candidate of browserCandidates) {
            try {
                browser = spawn(candidate, [
                    '--headless=new', '--disable-gpu', '--disable-extensions', '--no-first-run', '--no-default-browser-check',
                    '--remote-debugging-address=127.0.0.1', '--remote-debugging-port=0',
                    `--user-data-dir=${profile}`, 'about:blank'
                ], { windowsHide: true, stdio: 'ignore' });
                await new Promise((resolve, reject) => {
                    browser.once('spawn', resolve);
                    browser.once('error', reject);
                });
                break;
            } catch (error) { startError = error; browser = null; }
        }
        if (!browser) throw new Error(`未能启动 Chrome 或 Edge：${startError?.message || '未找到浏览器'}`);
        cdp = await connectCdp(await waitForDebugPort(profile, browser, signal), signal);
        const { targetId } = await cdp.send('Target.createTarget', { url: 'about:blank' });
        const { sessionId } = await cdp.send('Target.attachToTarget', { targetId, flatten: true });
        await cdp.send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: false }, sessionId);
        await cdp.send('Page.enable', {}, sessionId);
        await cdp.send('Runtime.enable', {}, sessionId);
        if (url) {
            const navigation = await cdp.send('Page.navigate', { url }, sessionId, 30000);
            if (navigation.errorText) throw new Error(`浏览器无法打开网页：${navigation.errorText}`);
            if (navigation.isDownload) throw new Error('此地址返回文件下载，不是网页');
        } else {
            const frame = await cdp.send('Page.getFrameTree', {}, sessionId);
            await cdp.send('Page.setDocumentContent', { frameId: frame.frameTree.frame.id, html }, sessionId, 30000);
        }
        await waitForPage(cdp, sessionId, signal);
        // 字体和两个绘制帧有明确上限；持续请求的动态页面不会把整个任务无限拖住。
        await evaluate(cdp, sessionId, `(async () => {
            await Promise.race([document.fonts?.ready || Promise.resolve(), new Promise(resolve => setTimeout(resolve, 4000))]);
            await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
            return true;
        })()`, 6000);
        const failure = await evaluate(cdp, sessionId, `document.querySelector('#main-frame-error .error-code')?.textContent || ''`);
        if (failure) throw new Error(`浏览器无法打开网页：${failure.trim()}`);
        // 截图早于 data-u-* 写入，保证预览反映原网页，而不是属性选择器可能触发的样式变化。
        const metrics = await cdp.send('Page.getLayoutMetrics', {}, sessionId);
        const pageHeight = Math.max(height, Math.ceil(metrics.cssContentSize?.height || height));
        const captureHeight = Math.min(pageHeight, Math.floor(MAX_SCREENSHOT_PIXELS / width));
        // 预览容器远窄于采集视口；下采样只影响截图像素，不改变 HTML 中的 CSS 像素坐标。
        const previewScale = Math.min(1, PREVIEW_MAX_WIDTH / width);
        const screenshots = [];
        let screenshotBytes = 0;
        let previewHeight = 0;
        for (let top = 0; top < captureHeight; top += SCREENSHOT_TILE_HEIGHT) {
            signal?.throwIfAborted();
            const tileHeight = Math.min(SCREENSHOT_TILE_HEIGHT, captureHeight - top);
            const image = await cdp.send('Page.captureScreenshot', {
                format: 'png', fromSurface: true, captureBeyondViewport: true,
                clip: { x: 0, y: top, width, height: tileHeight, scale: previewScale }
            }, sessionId, 20000);
            const tileBytes = Buffer.byteLength(image.data, 'base64');
            if (screenshotBytes + tileBytes > MAX_CAPTURE_BYTES) {
                if (!screenshots.length) throw new Error('第一页预览图超过 20 MB 限制');
                break;
            }
            screenshotBytes += tileBytes;
            screenshots.push({ top, height: tileHeight, data: image.data });
            previewHeight = top + tileHeight;
        }
        const result = await evaluate(cdp, sessionId, `(${annotateDocument.toString()})()`, 20000);
        if (!result?.annotatedHtml || !result.rawHtml) throw new Error('浏览器未返回可解析的 HTML');
        return {
            ...result, screenshots, viewportWidth: width, viewportHeight: height,
            pageHeight, previewScale, previewTruncated: previewHeight < pageHeight
        };
    } finally {
        signal?.removeEventListener('abort', stopBrowser);
        try { await cdp?.send('Browser.close', {}, undefined, 2000); } catch {}
        stopBrowser();
        const resolved = path.resolve(profile);
        if (path.dirname(resolved) === path.resolve(os.tmpdir()) && path.basename(resolved).startsWith('htmltougui-capture-')) {
            await fs.rm(resolved, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 }).catch(() => {});
        }
    }
}

module.exports = { capturePage };
