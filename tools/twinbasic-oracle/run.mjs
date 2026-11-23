// Makes known changes in the twinBASIC IDE's open project, saving after each and keeping a copy of every save (#5139).
// Usage: node run.mjs <project file> <out folder> <change> [<change>...]   (port from TBORACLE_PORT, default 9341)
// Each step writes <out>/<n>-<change>.twinproj. Saves in one session follow each other, so a diff between neighbours
// shows only that step's change. The first step's save also carries what reopening does; run "none" first to see it.
// Changes:
//   new             (first step only, with the IDE on its front page) a new Standard EXE from the template, at
//                   its default options; its first save opens a file dialog, which dialog.ps1 must answer
//   none            save with no change
//   char            Form1.twin's [Description("")] becomes [Description("x")], through the editor
//   add-module      Sources > Add > Add Module (.TWIN), keeping the name the IDE offers (MyModule.twin)
//   rename-module   MyModule.twin becomes Helpers.twin, through Rename in the tree
//   remove-module   Helpers.twin is deleted permanently, through the tree
//   add-folder      Sources > Add > Add Folder, keeping the name the IDE offers
//   add-form        Sources > Add > Add Windows Form, keeping the name the IDE offers
//   setting         Project Settings: "Use Project ID for type library ID" to Yes, then Apply Changes (which saves)
//   show            no change: reports Form1.twin's [Description(...)] line as the editor holds it, then saves
//   external        replaces the project file on disk while the IDE has it open, as an atomic save does: the file
//                   named by TBORACLE_EXTERNAL (default C:\tbo\runs\external.twinproj) is copied beside the project
//                   and renamed over it. Reports whether the rename was allowed and, if not, whether writing the
//                   file in place was. Then waits 10 s, keeps a screenshot as <out>/<n>-external.png and reports
//                   the editor's line as "show" does. It saves nothing; the step's kept file is what landed on disk
// The two steps above measure what the IDE does with a container HexIDE wrote (#5143).
// Only the IDE's front end is driven: tree, menus and dialogs through the DOM with real pointer clicks, text through
// the editor's API. The compiler is launch-and-watch only.
import { copyFileSync, existsSync, mkdirSync, renameSync, rmSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { chromium } from 'playwright-core';

const port = process.env.TBORACLE_PORT ?? '9341';
const [project, out, ...steps] = process.argv.slice(2);
if (!project || !out || steps.length === 0) throw new Error('usage: node run.mjs <project file> <out folder> <change>...');

const mtime = (p) => (existsSync(p) ? statSync(p).mtimeMs : 0);
const node = (page, name) => page.locator(`.itemNode:text-is('${name}')`).first();
const menuItem = (page, text) => page.locator(`.contextMenuItem:visible:has-text('${text}')`).first();

async function pick(page, text) {
  // A tall submenu can open partly above the top of the window, out of the pointer's reach, as Add Folder does on a
  // 1024 x 720 screen. Then the item gets the mouse events a click would send it.
  const item = menuItem(page, text);
  const box = await item.boundingBox();
  if (box && box.y >= 0) return item.click();
  return item.evaluate((e) => {
    for (const t of ['mousedown', 'mouseup', 'click']) e.dispatchEvent(new MouseEvent(t, { bubbles: true, cancelable: true, view: window, button: 0 }));
  });
}

async function addToSources(page, item) {
  await node(page, 'Sources').click({ button: 'right' });
  await page.getByText('Add', { exact: true }).first().hover();
  await pick(page, item);
  await page.waitForTimeout(1000);
  await page.keyboard.press('Enter'); // keep the name the IDE offers in its inline box
  await page.waitForTimeout(1500);
}

// Form1.twin's [Description(...)] line, as the editor's model holds it.
async function descriptionLine(page) {
  await page.locator('.tabItemText', { hasText: 'Form1.twin' }).first().click();
  await page.waitForFunction(() => monaco.editor.getEditors().some((e) => e.getModel()?.getValue().includes('[Description(')),
    null, { timeout: 20000 });
  return page.evaluate(() => monaco.editor.getEditors().map((e) => e.getModel())
    .find((m) => m?.getValue().includes('[Description('))
    .getLinesContent().find((l) => l.includes('[Description(')));
}

async function treeCommand(page, name, command) {
  await node(page, name).click({ button: 'right' });
  await menuItem(page, command).click();
  await page.waitForTimeout(800);
}

const changes = {
  async new(page) {
    await page.locator('.fp2-card').first().waitFor({ timeout: 60000 });
    await page.locator('.fp2-card').filter({ hasText: /^Standard EXE$/ }).first().click();
    await page.getByText('Open', { exact: true }).first().click();
    await page.getByText('Continue', { exact: true }).first().click({ timeout: 20000 });
    await page.waitForFunction(() => [...document.querySelectorAll('.itemNode')].some((e) => e.innerText === 'Sources'),
      null, { timeout: 60000 });
    await page.waitForTimeout(3000);
    return 'Standard EXE from the template, default options';
  },
  async none() {},
  async char(page) {
    await page.locator('.tabItemText', { hasText: 'Form1.twin' }).first().click();
    await page.waitForFunction(() => monaco.editor.getEditors().some((e) => e.getModel()?.getValue().includes('[Description("")]')),
      null, { timeout: 20000 });
    return page.evaluate(() => {
      const e = monaco.editor.getEditors().find((x) => x.getModel()?.getValue().includes('[Description("")]'));
      const m = e.getModel();
      const line = m.getLinesContent().findIndex((l) => l.includes('[Description("")]')) + 1;
      const col = m.getLineContent(line).indexOf('("")') + 3; // between the quotes, 1-based
      e.executeEdits('oracle', [{ range: new monaco.Range(line, col, line, col), text: 'x' }]);
      return m.getLineContent(line);
    });
  },
  'add-module': (page) => addToSources(page, 'Add Module (.TWIN'),
  async 'rename-module'(page) {
    await treeCommand(page, 'MyModule.twin', 'Rename');
    await page.keyboard.press('Control+A');
    await page.keyboard.type('Helpers.twin', { delay: 20 });
    await page.keyboard.press('Enter');
    await page.waitForTimeout(1500);
  },
  async 'remove-module'(page) {
    await treeCommand(page, 'Helpers.twin', 'Delete Permanently');
    await page.getByText('Delete', { exact: true }).first().click(); // the IDE's confirmation
    await page.waitForTimeout(1500);
  },
  'add-folder': (page) => addToSources(page, 'Add Folder'),
  'add-form': (page) => addToSources(page, 'Add Windows Form'),
  async setting(page) {
    // Project Settings > "Use Project ID for type library ID" > Yes, then Apply Changes. Applying saves the project
    // itself and restarts the compiler from the file (its Debug Console says so).
    // Wait for the dialog's references list to load: Apply writes what it resolved there (package names, symbols)
    // into Settings too, so applying before it loads saves different bytes (measured 2026-11-20).
    const before = mtime(project);
    await page.locator("[title='Project Settings']").first().click();
    await page.getByText('twinBASIC - VB Compatibility Package (Forms)').first().waitFor({ timeout: 30000 });
    await page.waitForTimeout(2000);
    const yes = page.locator("[class~='project.useProjectIdForTypeLibraryId'] .tbps-boolSegment:text-is('Yes')").first();
    await yes.click({ timeout: 20000 });
    await page.getByText('Apply Changes', { exact: true }).first().click();
    const deadline = Date.now() + 30000;
    while (mtime(project) === before && Date.now() < deadline) await page.waitForTimeout(250);
    await page.waitForTimeout(3000);
    return 'project.useProjectIdForTypeLibraryId set to Yes through Project Settings';
  },
  async show(page) {
    return { editor: await descriptionLine(page) };
  },
  async external(page) {
    const source = process.env.TBORACLE_EXTERNAL ?? 'C:\\tbo\\runs\\external.twinproj';
    const temp = join(dirname(project), '.external-tmp.twinproj');
    copyFileSync(source, temp);
    let replaced = 'renamed over the open file';
    try {
      renameSync(temp, project); // MoveFileExW with MOVEFILE_REPLACE_EXISTING, as File.Move(overwrite) does
    } catch (e) {
      replaced = `rename refused (${e.code})`;
      rmSync(temp, { force: true });
      try {
        copyFileSync(source, project);
        replaced += '; written in place';
      } catch (e2) {
        replaced += `; writing in place refused too (${e2.code})`;
      }
    }
    await page.waitForTimeout(10000);
    const shot = join(out, `${steps.indexOf('external') + 1}-external.png`);
    await page.screenshot({ path: shot });
    return { replaced, editor: await descriptionLine(page), screenshot: shot };
  },
};

if (steps.indexOf('new') > 0) throw new Error('new can only be the first step');
for (const s of steps) if (!changes[s]) throw new Error(`unknown change ${s}; known: ${Object.keys(changes).join(', ')}`);
mkdirSync(out, { recursive: true });
const browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`);
try {
  const page = browser.contexts().flatMap((c) => c.pages()).find((p) => p.url().includes('/main.htm'));
  if (!page) throw new Error('no IDE page on this port');
  // Ready once the project is loaded: the editor API is up and the project tree shows its Sources folder.
  if (steps[0] !== 'new') {
    await page.waitForFunction(() => window.monaco && [...document.querySelectorAll('.itemNode')].some((e) => e.innerText === 'Sources'),
      null, { timeout: 60000 });
  }
  await page.waitForTimeout(3000);
  for (const [i, s] of steps.entries()) {
    const unchanged = mtime(project);
    const note = await changes[s](page);
    await page.waitForTimeout(1500);
    // Some changes make the IDE save on its own (Apply Changes in Project Settings). Then that save is the step's.
    // "external" saves nothing: what it keeps is what landed on disk.
    const savedBy = s === 'external' ? 'nobody' : mtime(project) !== unchanged ? 'the IDE, during the change' : 'CTRL+S';
    if (savedBy === 'CTRL+S') {
      await page.keyboard.press('Control+S');
      const deadline = Date.now() + 60000;
      while (mtime(project) === unchanged) {
        if (Date.now() > deadline) throw new Error(`step ${s}: the IDE did not rewrite ${project} within 60 s`);
        await page.waitForTimeout(250);
      }
    }
    await page.waitForTimeout(2000); // let the write finish
    const kept = join(out, `${i + 1}-${s}.twinproj`);
    copyFileSync(project, kept);
    console.log(JSON.stringify({ step: i + 1, change: s, savedBy, kept, bytes: statSync(kept).size, note: note ?? null }));
  }
} finally {
  await browser.close().catch(() => {});
}
