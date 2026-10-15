'use strict';
// Monitoring agent: sends CPU, memory and disk usage to the Digital Monitoring System server.
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

const CONFIG = process.env.MDM_AGENT_CONFIG || path.join(__dirname, 'appsettings.json');
const log = (m) => console.log(`[${new Date().toLocaleTimeString()}] ${m}`);

if (!fs.existsSync(CONFIG)) fs.writeFileSync(CONFIG, JSON.stringify({ ApiBaseUrl: 'http://localhost:5103', DeviceId: '', DeviceToken: '', IntervalSeconds: 30 }, null, 2));
let cfg;
try { cfg = JSON.parse(fs.readFileSync(CONFIG, 'utf8').replace(/^\uFEFF/, '')); } catch (e) { console.error(`appsettings.json is not valid JSON: ${e.message}`); process.exit(1); }

console.log('Digital Monitoring System - Monitoring Agent');
if (!cfg.DeviceId || !cfg.DeviceToken) {
  console.log('\nThis computer is not enrolled yet:');
  console.log(' 1. Sign in to the dashboard and open "Enroll a device".');
  console.log(' 2. Enroll this computer, then click "Download appsettings.json".');
  console.log(` 3. Replace ${CONFIG} with the downloaded file and start the agent again.`);
  process.exit(2);
}
const base = String(cfg.ApiBaseUrl).replace(/\/+$/, '');
const interval = Math.min(Math.max(Number(cfg.IntervalSeconds) || 30, 10), 3600) * 1000;
console.log(`Device ID : ${cfg.DeviceId}\nServer    : ${base}\nInterval  : ${interval / 1000}s\n`);

let prev = os.cpus();
function cpuPercent() {
  const cur = os.cpus(); let idle = 0, total = 0;
  cur.forEach((c, i) => { const p = prev[i] || c; for (const k of Object.keys(c.times)) { const d = c.times[k] - (p.times[k] || 0); total += d; if (k === 'idle') idle += d; } });
  prev = cur;
  return total > 0 ? Math.round((1 - idle / total) * 100) : 0;
}
const memPercent = () => Math.round((1 - os.freemem() / os.totalmem()) * 100);
function diskPercent() {
  try {
    const s = fs.statfsSync(process.platform === 'win32' ? `${process.env.SystemDrive || 'C:'}\\` : '/');
    return s.blocks ? Math.round((1 - s.bavail / s.blocks) * 100) : 0;
  } catch { return 0; }
}

async function beat() {
  const payload = { cpu: cpuPercent(), memory: memPercent(), disk: diskPercent(), agentVersion: '2.0', hostname: os.hostname() };
  const res = await fetch(`${base}/api/devices/${encodeURIComponent(cfg.DeviceId)}/heartbeat`, {
    method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Device-Token': cfg.DeviceToken.trim() },
    body: JSON.stringify(payload), signal: AbortSignal.timeout(15000),
  });
  const text = await res.text();
  if (!res.ok) {
    let msg = text; try { msg = JSON.parse(text).error || text; } catch { /* keep raw */ }
    throw new Error(res.status === 401 ? 'The server rejected this token. Re-issue the token from the dashboard and update appsettings.json.' : `Heartbeat failed (${res.status}): ${msg}`);
  }
  const r = JSON.parse(text);
  log(`Online | CPU ${payload.cpu}% | Memory ${payload.memory}% | Disk ${payload.disk}% | Samples ${r.sampleCount}${r.triggeredAlertIds?.length ? ` | Alerts raised: ${r.triggeredAlertIds.join(', ')}` : ''}`);
}
(async function loop() {
  for (;;) {
    try { await beat(); } catch (e) { log(`ERROR: ${e.cause?.code === 'ECONNREFUSED' || e.cause?.code === 'ENOTFOUND' ? `Cannot reach ${base}. Retrying...` : e.message}`); }
    await new Promise((r) => setTimeout(r, interval));
  }
})();
