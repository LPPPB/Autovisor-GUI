'use strict';

// Runs only in the panel-launched Playwright driver. No debugging port or page actions.
const fs = require('node:fs');
const path = require('node:path');
const { createRequire } = require('node:module');

function readCatalog() {
    if (location.protocol !== 'https:' ||
        !/(^|\.)zhihuishu\.com$/i.test(location.hostname)) return null;
    const layouts = [
        ['.chapter-item', '.item-name', '.finish-icon', '.chapter-content-second.current', '.el-progress'],
        ['.child-info.hasvideo', '.child-name', '.child-check', '.current', '[role="progressbar"]'],
        ['.clearfix.video', '#lessonOrder', '.time_icofinish', '.current_play', '.progress-num'],
        ['.file-item', 'span[title]', '.icon-finish', '.active', '.rate']
    ];
    const layout = layouts.find(([selector]) => document.querySelector(selector));
    if (!layout) return null;
    const [selector, title, finish, current, progress] = layout;
    const clean = value => (value || '').replace(/\s+/g, ' ').trim().slice(0, 250);
    const numberPrefix = /^([0-9]+(?:[.．][0-9]+)+)(?=$|[^0-9.．])/;
    const Lessons = [];
    for (const row of Array.from(document.querySelectorAll(selector)).slice(0, 3000)) {
        const name = row.querySelector(title);
        if (!name) continue;
        const rawTitle = clean(name.getAttribute('title') || name.textContent);
        const numberedTitle = rawTitle.match(numberPrefix);
        const number = (numberedTitle || clean(row.textContent).match(numberPrefix))?.[1] || '';
        const chapter = row.closest('.el-collapse-item, .chapter-info, .chapter');
        const heading = chapter?.querySelector(':scope > .el-collapse-item__header, :scope > .chapter-name, :scope > .chapter-title');
        const label = clean(heading?.textContent) || (number ? '第 ' + number.split(/[.．]/)[0] + ' 章' : '课程小节');
        const meter = row.querySelector(progress);
        const rawProgress = clean(meter?.getAttribute('aria-valuenow') || meter?.textContent);
        const percent = /^(\d+(?:\.\d+)?)\s*%?$/.exec(rawProgress);
        const recorded = percent && Number(percent[1]) <= 100 ? Math.floor(Number(percent[1])) : -1;
        const lessonTitle = numberedTitle ? clean(rawTitle.slice(numberedTitle[0].length)) : rawTitle;
        if (!lessonTitle) continue;
        Lessons.push({
            Chapter: label,
            Number: number,
            Title: lessonTitle,
            Progress: row.querySelector(finish) ? 100 : recorded,
            Current: row.matches(current) || Boolean(row.querySelector(current))
        });
    }
    if (!Lessons.length) return null;
    const heading = document.querySelector('.course-name, .source-name, h1');
    return { Title: clean(heading?.textContent), Lessons };
}

function observe(output, PageDispatcher) {
    const pages = new Set();
    const original = PageDispatcher.from;
    PageDispatcher.from = function (scope, page) {
        const dispatcher = original.call(this, scope, page);
        if (page && !pages.has(page)) {
            pages.add(page);
            page.once('close', () => pages.delete(page));
        }
        return dispatcher;
    };

    const expression = '(' + readCatalog.toString() + ')()';
    let busy = false;
    let previous = '';
    let misses = 0;
    setInterval(async () => {
        if (busy) return;
        busy = true;
        try {
            let catalog = null;
            for (const page of [...pages].reverse()) {
                const frame = page.mainFrame();
                if (!/^https:\/\/(?:[^/]+\.)?zhihuishu\.com(?=[:/]|$)/i.test(frame.url())) continue;
                try {
                    catalog = await frame.nonStallingEvaluateInExistingContext(expression, 'main');
                } catch { /* A navigation or browser dialog can interrupt this sample. */ }
                if (catalog) break;
            }
            if (catalog) misses = 0;
            else if (++misses < 3 || !previous) return;
            const data = JSON.stringify(catalog);
            if (data === previous) return;
            fs.writeFileSync(output + '.tmp', data, 'utf8');
            fs.renameSync(output + '.tmp', output);
            previous = data;
        } catch { /* Catalog capture is optional; keep the upstream driver running. */ }
        finally { busy = false; }
    }, 2000).unref();
}

const output = process.env.AUTOVISOR_PANEL_CATALOG;
if (output && process.argv[2] === 'run-driver' && path.basename(process.argv[1] || '') === 'cli.js') {
    setImmediate(() => {
        try {
            const fromDriver = createRequire(path.resolve(process.argv[1]));
            if (fromDriver('./package.json').version !== '1.59.1') return;
            const { PageDispatcher } = fromDriver('./lib/server/dispatchers/pageDispatcher.js');
            observe(output, PageDispatcher);
        } catch { /* Unsupported driver. The panel can still show log-based progress. */ }
    });
}
