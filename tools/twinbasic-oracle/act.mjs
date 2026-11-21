// One action against the twinBASIC IDE's front end over DevTools, for exploring and debugging the harness.
// Usage: node act.mjs <action> [arg...]   (port from TBORACLE_PORT, default 9341)
//   shot <file.png>         screenshot the IDE's page
//   eval <js>               evaluate an expression in the page and print the JSON result
//   clicktext <text>        click the first visible element whose text is exactly <text>
//   click <css>             click the first element matching <css>
//   hovertext <text>        move the pointer over the first visible element whose text is exactly <text>
//   rclicktext <text>       right-click it
//   key <keys>              press a key chord, e.g. Control+S
//   type <text>             type text where the focus is
// Only the front end is driven. The compiler is launch-and-watch only.
import { chromium } from 'playwright-core';

const port = process.env.TBORACLE_PORT ?? '9341';
const [action, ...args] = process.argv.slice(2);
const browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`);
try {
  const page = browser.contexts().flatMap((c) => c.pages()).find((p) => p.url().includes('/main.htm'));
  if (!page) throw new Error('no IDE page on this port');
  switch (action) {
    case 'shot':
      await page.screenshot({ path: args[0] });
      console.log(args[0]);
      break;
    case 'eval':
      console.log(JSON.stringify(await page.evaluate(args.join(' ')), null, 1)?.slice(0, 20000));
      break;
    case 'clicktext':
      await page.getByText(args.join(' '), { exact: true }).first().click({ timeout: 10000 });
      break;
    case 'click':
      await page.locator(args.join(' ')).first().click({ timeout: 10000 });
      break;
    case 'hovertext':
      await page.getByText(args.join(' '), { exact: true }).first().hover({ timeout: 10000 });
      break;
    case 'rclick':
      await page.locator(args.join(' ')).first().click({ button: 'right', timeout: 10000 });
      break;
    case 'rclicktext':
      await page.getByText(args.join(' '), { exact: true }).first().click({ button: 'right', timeout: 10000 });
      break;
    case 'key':
      await page.keyboard.press(args[0]);
      break;
    case 'type':
      await page.keyboard.type(args.join(' '), { delay: 20 });
      break;
    default:
      throw new Error(`unknown action ${action}`);
  }
} finally {
  await browser.close().catch(() => {});
}
