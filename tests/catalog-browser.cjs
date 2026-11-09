// 离线集成测试：使用发行包自带的驱动，课程页由本地测试拦截。
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { start } = require('../runtime/internal/playwright/driver/package/lib/outofprocess.js');

async function waitFor(file, predicate) {
    for (let i = 0; i < 60; i++) {
        try {
            const value = JSON.parse(fs.readFileSync(file, 'utf8'));
            if (predicate(value)) return value;
        } catch (error) {
            if (!['ENOENT', 'EPERM', 'EBUSY'].includes(error.code)) throw error;
        }
        await new Promise(resolve => setTimeout(resolve, 250));
    }
    throw new Error('Timed out waiting for browser catalog');
}

async function test() {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'autovisor-catalog-test-'));
    const file = path.join(dir, 'catalog.json');
    const script = path.resolve(__dirname, '../src/catalog-capture.cjs');
    let client;
    try {
        client = await start({ NODE_OPTIONS: '--require="' + script.replaceAll('\\', '/') + '"', AUTOVISOR_PANEL_CATALOG: file });
        const browser = await client.playwright.chromium.launch({ channel: 'msedge', headless: true });
        try {
            const page = await browser.newPage();
            await page.route('**/*', route => route.fulfill({ contentType: 'text/html; charset=utf-8', body: `
                <h1>示例课程</h1>
                <div class="el-collapse-item">
                    <div class="el-collapse-item__header">第一章 背景</div>
                    <div class="chapter-item"><span>1.1</span><span class="item-name">背景</span><i class="finish-icon"></i></div>
                    <div class="chapter-item"><div class="chapter-content-second current">
                        <span>1.2</span><span class="item-name">现状</span><i class="el-progress" aria-valuenow="15"></i>
                    </div></div>
                </div>
                <div class="el-collapse-item">
                    <div class="el-collapse-item__header">第二章 发展</div>
                    <div hidden class="chapter-item"><span>2.1</span><span class="item-name">发展</span></div>
                </div>` }));
            await page.goto('https://wisdom-mooc.zhihuishu.com/study/index');
            const first = await waitFor(file, catalog => catalog.Lessons.length === 3);
            assert.equal(first.Title, '示例课程');
            assert.deepEqual(first.Lessons.map(x => [x.Chapter, x.Number, x.Title, x.Progress, x.Current]), [
                ['第一章 背景', '1.1', '背景', 100, false],
                ['第一章 背景', '1.2', '现状', 15, true],
                ['第二章 发展', '2.1', '发展', -1, false]
            ]);
            await page.locator('.el-progress').evaluate(node => node.setAttribute('aria-valuenow', '42'));
            await waitFor(file, catalog => catalog.Lessons[1]?.Progress === 42);
        } finally {
            await browser.close();
        }
    } finally {
        if (client) await client.stop();
        fs.rmSync(dir, { recursive: true, force: true });
    }
}

const watchdog = setTimeout(() => {
    console.error('Browser catalog test did not finish');
    process.exit(1);
}, 35000);
test().then(() => console.log('Browser catalog test passed.')).catch(error => {
    console.error(error);
    process.exitCode = 1;
}).finally(() => clearTimeout(watchdog));
