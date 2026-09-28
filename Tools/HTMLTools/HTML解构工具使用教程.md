# HTML 解构工具使用教程

## 概述

HTML 解构工具 (`HTML解构工具.html`) 是一个离线网页工具，用于将普通的 HTML 代码转换为带有 `data-u-left`、`data-u-top`、`data-u-width`、`data-u-height` 属性的 HTML 结构。转换后的输出可直接被 HtmlToUGUI 工具转换为 UGUI 层级。

大型页面默认只显示前 120 个结构节点；超过 8192 字符的内联 `<style>` 或 `<script>` 也会折叠。点击 **查看完整代码** 可展开；**复制**、**下载** 和同步文件始终使用完整 HTML。
输入超过 10 万字符时，输入框会关闭自动换行以减少载入卡顿；内容仍可滚动和编辑。

## 使用方式

### 方式一：直接粘贴 HTML

1. 推荐启动本地代理并打开 `http://127.0.0.1:8888/`；直接打开 `Tools/HTMLTools/HTML解构工具.html` 也可以使用
2. 在左侧输入区粘贴 HTML 代码
3. 点击 **"解构 HTML"** 按钮
4. 右侧输出区生成携带 `data-u-*` 属性的 HTML
5. 将输出内容复制到 HtmlToUGUI 工具的输入框，点击 **开始转换**

### 方式二：通过 URL 加载（推荐启动本地代理）

先启动下方的本地代理，在浏览器打开 `http://127.0.0.1:8888/`，输入完整的 `http://` 或 `https://` 地址，点击 **“加载 URL”**，再点击 **“开始解构”**。代理会在独立的 Chrome 或 Edge 中完成渲染、坐标计算和截图；工具页逐段显示可滚动的静态预览，不再把整个网页放进 iframe 重新布局。无需填写“本地代理”输入框。请先设置右侧视口宽高，再加载 URL；修改视口或输入 HTML 后，再次解构会按新内容重新采集。

例如 `https://www.frontendmentor.io/challenges/qr-code-component-iux_sIO_H` 是 Frontend Mentor 的整个挑战介绍页，工具会解构该网页，而不是仅提取其中的二维码设计图。

浏览器抓取的是当时的页面快照；右侧预览是截图，不支持网页按钮交互。若从本地服务打开工具时后台采集失败，页面会显示错误原因；直接打开 HTML 文件时可退回旧版 iframe 预览。特别长的页面可能只显示前段截图，完整 HTML 和元素坐标仍会输出。解构成功也只说明 HTML、样式和元素位置已获取，Unity 中的还原程度仍取决于 HtmlToUGUI 支持的 CSS 与组件能力。

### 方式三：选择本地 HTML 文件

点击 **"选择文件"** 按钮，选择本地的 `.html` 文件直接载入。

## AI 生成 HTML 提示词

配合大语言模型生成可直接用于转换的 HTML，无需经过 HTML 解构工具预处理。

两种提示词文件位于 `Tools/HTMLTools/AI生成HTML提示词/`：

### SKILL_动态定位（推荐）

- **文件**：`SKILL_动态定位.md`
- **原理**：使用 `getBoundingClientRect()` 获取元素的精确位置和尺寸，自动注入 `data-u-left/top/width/height` 属性
- **优点**：输出优雅，标签语义清晰，兼容性好
- **使用方式**：将 `SKILL_动态定位.md` 的完整内容作为 System Prompt 或前置指令发给 AI（如 ChatGPT、Claude 等），然后描述你想要的 UI 界面

### SKILL_绝对定位

- **文件**：`SKILL_绝对定位.md`
- **原理**：通过 CSS `position: absolute` + `left/top/width/height` 确定每个元素的位置
- **适用场景**：AI 对动态定位理解不准确时使用此方案
- **注意**：所有元素必须使用 `position: absolute`，禁止使用 `margin`、`display`、`border` 等影响布局的属性

## CORS 代理

当需要从远程 URL 加载 HTML 页面时，浏览器存在跨域（CORS）限制。`Tools/HTMLTools/CORS代理/` 目录下提供了一个仅监听本机的 Node.js 服务，用于抓取资源和通过本机 Chrome/Edge 渲染动态网页。

### 启动方式

**Windows**：双击 `CORS代理.bat`
**macOS / Linux**：在终端中运行 `bash CORS代理.sh`

**方法二**：在终端中运行：
```
node cors-proxy.js
```

启动后，控制台输出：
```
✅ CORS 代理已启动：http://127.0.0.1:8888
```

### 配置代理

默认地址无需填写。若要手动填写，在 HTML 解构工具的 URL 输入框旁边的 **“本地代理”** 输入框中输入：
```
http://127.0.0.1:8888/
```

然后在 URL 输入框中输入目标网页地址，点击 **"加载 URL"** 即可。

### 前提条件

- 已安装 Node.js 22.4 或更新版本（后台采集使用内置 WebSocket）
- 已安装 Chrome 或 Edge（动态网页渲染需要；可通过 `HTML_TO_UGUI_BROWSER` 环境变量指定浏览器可执行文件）
- 代理仅在本地开发时使用，请勿暴露到公网
