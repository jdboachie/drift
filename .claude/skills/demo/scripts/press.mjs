// node press.mjs <key> [ctrl] [shift] [alt] : presses a key in the demo's front page through the web engine's
// debugging port (DevTools input, not the OS keyboard). Drift's shortcuts work from there:
// `node press.mjs comma ctrl` opens Settings (a bare `,` is PowerShell's array operator), `node press.mjs d ctrl` bookmarks the page.
const [key, ...mods] = process.argv.slice(2);
if (!key) { console.log('usage: node press.mjs <key> [ctrl] [shift] [alt]'); process.exit(1); }
const named = { ',': ['Comma', 188], comma: ['Comma', 188], '.': ['Period', 190], '/': ['Slash', 191], Tab: ['Tab', 9], Enter: ['Enter', 13], Escape: ['Escape', 27] };
const [code, vk] = named[key] ?? (/^[a-z]$/i.test(key) ? [`Key${key.toUpperCase()}`, key.toUpperCase().charCodeAt(0)]
  : /^[0-9]$/.test(key) ? [`Digit${key}`, key.charCodeAt(0)] : [key, 0]);
const modifiers = (mods.includes('alt') ? 1 : 0) | (mods.includes('ctrl') ? 2 : 0) | (mods.includes('shift') ? 8 : 0);
const targets = await (await fetch('http://127.0.0.1:9444/json/list')).json();
const pages = targets.filter((t) => t.type === 'page' && t.url.startsWith('http') && !t.url.includes('robots.txt'));
if (pages.length === 0) { console.log('no page'); process.exit(1); }
const open = async (t) => {
  const ws = new WebSocket(t.webSocketDebuggerUrl);
  await new Promise((r) => ws.addEventListener('open', r));
  let id = 0;
  const send = (method, params) => new Promise((r) => {
    const mine = ++id;
    ws.addEventListener('message', function on(e) {
      const m = JSON.parse(e.data);
      if (m.id === mine) { ws.removeEventListener('message', on); r(m); }
    });
    ws.send(JSON.stringify({ id: mine, method, params }));
  });
  return { ws, send };
};
// The page you're looking at: Essentials and pinned tabs load in the background too, and a key sent to one of those
// never reaches Drift's shortcuts.
let target = pages[0], ws, send;
for (const t of pages) {
  const c = await open(t);
  const r = await c.send('Runtime.evaluate', { expression: 'document.visibilityState', returnByValue: true });
  if (r.result?.result?.value === 'visible') { target = t; ({ ws, send } = c); break; }
  c.ws.close();
}
if (!ws) ({ ws, send } = await open(target));
const ev = { key, code, windowsVirtualKeyCode: vk, nativeVirtualKeyCode: vk, modifiers };
await send('Input.dispatchKeyEvent', { ...ev, type: 'rawKeyDown' });
await send('Input.dispatchKeyEvent', { ...ev, type: 'keyUp' });
console.log(`pressed ${[...mods, key].join('+')} in ${target.title}`);
ws.close();
