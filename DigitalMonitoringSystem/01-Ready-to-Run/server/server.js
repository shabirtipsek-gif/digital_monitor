'use strict';
// A Digital Monitoring System for Computers and Other IT Devices at Mukono District Local Government
// Zero-dependency server: REST API + live events (SSE) + SQLite + static dashboard.
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const net = require('node:net');
const crypto = require('node:crypto');
const { DatabaseSync } = require('node:sqlite');

const ROOT = path.resolve(__dirname, '..');
const DATA_DIR = path.join(ROOT, 'data');
const PUBLIC_DIR = path.join(ROOT, 'public');
const CONFIG_PATH = process.env.MDM_CONFIG || path.join(ROOT, 'config.json');
fs.mkdirSync(DATA_DIR, { recursive: true });

// ---------------------------------------------------------------- config
const defaults = { port: 5103, adminUsername: 'admin', adminPassword: 'Admin@123', jwtSecret: '', offlineAfterMinutes: 15, metricRetentionDays: 7, advertisedHost: '' };
let config = { ...defaults };
if (fs.existsSync(CONFIG_PATH)) {
  try { config = { ...defaults, ...JSON.parse(fs.readFileSync(CONFIG_PATH, 'utf8')) }; } catch (e) { console.error('config.json is not valid JSON:', e.message); process.exit(1); }
}
if (!config.jwtSecret || config.jwtSecret.length < 32) {
  config.jwtSecret = crypto.randomBytes(48).toString('hex');
  fs.writeFileSync(CONFIG_PATH, JSON.stringify(config, null, 2));
}
if (process.env.PORT) config.port = Number(process.env.PORT);
if (process.env.ADMIN_USERNAME) config.adminUsername = process.env.ADMIN_USERNAME;
if (process.env.ADMIN_PASSWORD) config.adminPassword = process.env.ADMIN_PASSWORD;
const DB_FILE = process.env.MDM_DB || path.join(DATA_DIR, 'mdm.db');

// ---------------------------------------------------------------- database
const db = new DatabaseSync(DB_FILE);
db.exec(`
PRAGMA journal_mode = WAL;
CREATE TABLE IF NOT EXISTS devices (id TEXT PRIMARY KEY, hostname TEXT NOT NULL, ip TEXT NOT NULL, vendor TEXT DEFAULT '', platform TEXT DEFAULT '', location TEXT DEFAULT '', status TEXT NOT NULL, cpu INTEGER DEFAULT 0, memory INTEGER DEFAULT 0, disk INTEGER DEFAULT 0, last_seen TEXT, managed INTEGER DEFAULT 1, token_nonce TEXT DEFAULT '', created_at TEXT);
CREATE TABLE IF NOT EXISTS metrics (id INTEGER PRIMARY KEY AUTOINCREMENT, device_id TEXT NOT NULL, cpu INTEGER, memory INTEGER, disk INTEGER, at_ms INTEGER NOT NULL, at TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS ix_metrics ON metrics(device_id, at_ms);
CREATE TABLE IF NOT EXISTS alerts (id TEXT PRIMARY KEY, title TEXT NOT NULL, severity TEXT NOT NULL, source TEXT NOT NULL, timestamp TEXT NOT NULL, acknowledged INTEGER DEFAULT 0, rule_id TEXT DEFAULT '', device_id TEXT DEFAULT '', resolved INTEGER DEFAULT 0);
CREATE TABLE IF NOT EXISTS policies (id TEXT PRIMARY KEY, name TEXT NOT NULL, category TEXT, scope TEXT, status TEXT, updated_at TEXT);
CREATE TABLE IF NOT EXISTS assignments (id TEXT PRIMARY KEY, device_id TEXT NOT NULL, policy_id TEXT NOT NULL, state TEXT, enforced_by TEXT, enforced_at TEXT, message TEXT, UNIQUE(device_id, policy_id));
CREATE TABLE IF NOT EXISTS rules (id TEXT PRIMARY KEY, name TEXT NOT NULL, metric TEXT NOT NULL, threshold INTEGER NOT NULL, duration_minutes INTEGER NOT NULL, severity TEXT NOT NULL, enabled INTEGER NOT NULL, updated_at TEXT);
CREATE TABLE IF NOT EXISTS pairings (id TEXT PRIMARY KEY, code TEXT UNIQUE NOT NULL, device_name TEXT, platform TEXT, device_id TEXT DEFAULT '', expires_at TEXT NOT NULL, used INTEGER DEFAULT 0);
`);
const now = () => new Date().toISOString();
const uid = (p) => `${p}-${crypto.randomBytes(8).toString('hex').toUpperCase()}`;

if (db.prepare('SELECT COUNT(*) c FROM policies').get().c === 0) {
  const ins = db.prepare('INSERT INTO policies VALUES (?,?,?,?,?,?)');
  ins.run('POL-100', 'Standard Endpoint Baseline', 'Compliance', 'All managed computers', 'Active', now());
  ins.run('POL-101', 'Network Access Guardrail', 'Security', 'Perimeter devices', 'Active', now());
  ins.run('POL-102', 'Patch Enforcement Window', 'Operations', 'Managed fleet', 'Scheduled', now());
}
if (db.prepare('SELECT COUNT(*) c FROM rules').get().c === 0) {
  const ins = db.prepare('INSERT INTO rules VALUES (?,?,?,?,?,?,?,?)');
  ins.run('RULE-CPU', 'High CPU usage', 'cpu', 90, 5, 'High', 1, now());
  ins.run('RULE-MEM', 'High memory usage', 'memory', 90, 5, 'Medium', 1, now());
  ins.run('RULE-DISK', 'Disk almost full', 'disk', 90, 0, 'Medium', 1, now());
  ins.run('RULE-OFFLINE', 'Device stopped reporting', 'offline', config.offlineAfterMinutes, 0, 'High', 1, now());
}

function seedDistrictDemoFleet() {
  const departments = [
    'Administration', 'Finance', 'Education', 'Health', 'Works', 'Procurement',
    'Planning', 'Works', 'Community', 'Agriculture', 'Human Resource', 'Revenue'
  ];
  const profiles = [
    { prefix: 'WS', vendor: 'Dell', platform: 'Windows 11 Pro' },
    { prefix: 'LT', vendor: 'HP', platform: 'Windows 11 Pro' },
    { prefix: 'SR', vendor: 'Lenovo', platform: 'Windows 10 Pro' },
    { prefix: 'PR', vendor: 'Canon', platform: 'Printer' },
    { prefix: 'AP', vendor: 'Cisco', platform: 'Access Point' },
    { prefix: 'NVR', vendor: 'Hikvision', platform: 'CCTV Recorder' },
    { prefix: 'UPS', vendor: 'APC', platform: 'UPS' }
  ];

  const insert = db.prepare('INSERT INTO devices (id, hostname, ip, vendor, platform, location, status, cpu, memory, disk, last_seen, managed, token_nonce, created_at) VALUES (?, ?, ?, ?, ?, ?, ?, 0, 0, 0, NULL, 0, \'\', ?) ON CONFLICT(id) DO NOTHING');
  const migrateDemo = db.prepare(`
    UPDATE devices
    SET hostname=?, ip=?, vendor=?, platform=?, location=?, status='Demo only',
        cpu=0, memory=0, disk=0, last_seen=NULL, managed=0, token_nonce=''
    WHERE id=? AND (
      hostname LIKE 'MUK-WS-%' OR hostname LIKE 'MUK-LT-%' OR hostname LIKE 'MUK-SR-%'
      OR hostname LIKE 'MUK-PR-%' OR hostname LIKE 'MUK-AP-%' OR hostname LIKE 'MUK-NVR-%'
      OR hostname LIKE 'MUK-UPS-%' OR hostname LIKE 'DEMO-MUK-%'
    )
  `);
  const generatedAt = now();

  for (let i = 1; i <= 200; i++) {
    const profile = profiles[(i - 1) % profiles.length];
    const department = departments[(i - 1) % departments.length];
    const id = `MUK-DEV-${String(i).padStart(3, '0')}`;
    const number = String(i).padStart(3, '0');
    const hostname = `DEMO-MUK-${profile.prefix}-${number}`;
    const ip = i <= 100 ? `192.0.2.${i + 10}` : `198.51.100.${i - 90}`;

    // Convert earlier generated rows to unmistakable demo records; none represent live assets.
    migrateDemo.run(hostname, ip, `DEMO - ${profile.vendor}`, `DEMO - ${profile.platform}`, `DEMO ONLY - ${department} Office`, id);
    insert.run(id, hostname, ip, `DEMO - ${profile.vendor}`, `DEMO - ${profile.platform}`, `DEMO ONLY - ${department} Office`, 'Demo only', generatedAt);
  }

  db.prepare("DELETE FROM metrics WHERE device_id LIKE 'MUK-DEV-___'").run();
  db.prepare("DELETE FROM alerts WHERE device_id LIKE 'MUK-DEV-___' OR source LIKE 'MUK-WS-%' OR source LIKE 'MUK-LT-%' OR source LIKE 'MUK-SR-%' OR source LIKE 'MUK-PR-%' OR source LIKE 'MUK-AP-%' OR source LIKE 'MUK-NVR-%' OR source LIKE 'MUK-UPS-%' OR source LIKE 'DEMO-MUK-%'").run();
}

seedDistrictDemoFleet();

// ---------------------------------------------------------------- tokens
const b64 = (b) => Buffer.from(b).toString('base64url');
const hmac = (s) => crypto.createHmac('sha256', config.jwtSecret).update(s).digest('base64url');
const safeEq = (a, b) => { const x = Buffer.from(String(a)), y = Buffer.from(String(b)); return x.length === y.length && crypto.timingSafeEqual(x, y); };
function signJwt(payload, hours) {
  const head = b64(JSON.stringify({ alg: 'HS256', typ: 'JWT' }));
  const body = b64(JSON.stringify({ ...payload, exp: Math.floor(Date.now() / 1000) + hours * 3600 }));
  return `${head}.${body}.${hmac(`${head}.${body}`)}`;
}
function verifyJwt(token) {
  const p = String(token || '').split('.');
  if (p.length !== 3 || !safeEq(p[2], hmac(`${p[0]}.${p[1]}`))) return null;
  try { const body = JSON.parse(Buffer.from(p[1], 'base64url').toString()); return body.exp > Date.now() / 1000 ? body : null; } catch { return null; }
}
function issueDeviceToken(deviceId) {
  const nonce = crypto.randomBytes(12).toString('hex');
  db.prepare('UPDATE devices SET token_nonce=? WHERE id=?').run(nonce, deviceId);
  const payload = b64(JSON.stringify({ d: deviceId, n: nonce, exp: Math.floor(Date.now() / 1000) + 90 * 86400 }));
  return `${payload}.${hmac(payload)}`;
}
function verifyDeviceToken(token) {
  const p = String(token || '').trim().split('.');
  if (p.length !== 2 || !safeEq(p[1], hmac(p[0]))) return null;
  try {
    const body = JSON.parse(Buffer.from(p[0], 'base64url').toString());
    if (body.exp <= Date.now() / 1000) return null;
    const row = db.prepare('SELECT token_nonce FROM devices WHERE id=?').get(body.d);
    return row && row.token_nonce === body.n ? body.d : null; // only the newest token per device works
  } catch { return null; }
}

// ---------------------------------------------------------------- helpers
const lanIp = () => { for (const list of Object.values(os.networkInterfaces())) for (const i of list || []) if (i.family === 'IPv4' && !i.internal) return i.address; return '127.0.0.1'; };
function baseUrl(req) {
  let host = (req.headers.host || '').replace(/:\d+$/, '');
  if (!host || ['localhost', '127.0.0.1', '::1', '[::1]'].includes(host)) host = config.advertisedHost || lanIp();
  return `http://${host}:${config.port}`;
}
const deviceRow = (d) => ({ id: d.id, hostname: d.hostname, ipAddress: d.ip, status: d.status, cpu: d.cpu, memory: d.memory, location: d.location });
const alertRow = (a) => ({ id: a.id, title: a.title, severity: a.severity, source: a.source, timestamp: a.timestamp, acknowledged: !!a.acknowledged, ruleId: a.rule_id });
function buildDashboard() {
  const devices = db.prepare('SELECT * FROM devices ORDER BY hostname COLLATE NOCASE').all();
  const alerts = db.prepare('SELECT * FROM alerts WHERE resolved=0 ORDER BY timestamp DESC').all();
  const policies = db.prepare('SELECT p.*, (SELECT COUNT(*) FROM assignments a WHERE a.policy_id=p.id) n FROM policies p ORDER BY p.id').all();
  const online = devices.filter((d) => d.status === 'Online');
  const avg = (k) => (online.length ? Math.round((online.reduce((s, d) => s + d[k], 0) / online.length) * 10) / 10 : 0);
  return {
    totalDevices: devices.length, onlineDevices: online.length,
    openAlerts: alerts.filter((a) => !a.acknowledged).length, criticalAlerts: alerts.filter((a) => a.severity === 'High').length,
    avgCpu: avg('cpu'), avgMemory: avg('memory'),
    devices: devices.map(deviceRow), alerts: alerts.map(alertRow),
    policies: policies.map((p) => ({ id: p.id, name: p.name, category: p.category, scope: p.scope, status: p.status, assignedDevices: p.n, updatedAt: p.updated_at })),
  };
}

// live updates to open dashboards
const clients = new Set();
function broadcast() {
  if (!clients.size) return;
  const msg = `event: dashboard\ndata: ${JSON.stringify(buildDashboard())}\n\n`;
  for (const res of clients) res.write(msg);
}

// ---------------------------------------------------------------- alert engine
function evaluateRules() {
  const t = Date.now();
  let changed = 0;
  const rules = db.prepare('SELECT * FROM rules WHERE enabled=1').all();
  const devices = db.prepare('SELECT * FROM devices WHERE managed=1').all();
  for (const rule of rules) {
    for (const d of devices) {
      let breached = false, detail = '';
      const last = d.last_seen ? Date.parse(d.last_seen) : 0;
      if (rule.metric === 'offline') {
        breached = !last || t - last > rule.threshold * 60000;
        detail = `no heartbeat for over ${rule.threshold} minutes`;
      } else if (['cpu', 'memory', 'disk'].includes(rule.metric)) {
        const windowMs = rule.duration_minutes > 0 ? rule.duration_minutes * 60000 : 120000;
        const rows = rule.duration_minutes > 0
          ? db.prepare(`SELECT ${rule.metric} v, at_ms FROM metrics WHERE device_id=? AND at_ms>=? ORDER BY at_ms`).all(d.id, t - windowMs)
          : db.prepare(`SELECT ${rule.metric} v, at_ms FROM metrics WHERE device_id=? ORDER BY at_ms DESC LIMIT 1`).all(d.id); // instant rule: latest sample only
        // a sustained breach needs every sample in the window above the threshold and enough history to span it
        const spans = rule.duration_minutes === 0 || (rows.length > 1 && rows[rows.length - 1].at_ms - rows[0].at_ms >= windowMs * 0.8);
        breached = d.status === 'Online' && last > t - 120000 && rows.length > 0 && spans && rows.every((r) => r.v > rule.threshold);
        detail = `${rule.metric.toUpperCase()} above ${rule.threshold}%${rule.duration_minutes ? ` for ${rule.duration_minutes} min` : ''}`;
      } else continue;
      const open = db.prepare('SELECT * FROM alerts WHERE rule_id=? AND device_id=? AND resolved=0').get(rule.id, d.id);
      if (breached && !open) {
        db.prepare('INSERT INTO alerts (id,title,severity,source,timestamp,rule_id,device_id) VALUES (?,?,?,?,?,?,?)')
          .run(uid('ALT'), `${rule.name}: ${detail}`, rule.severity, d.hostname, now(), rule.id, d.id); changed++;
      } else if (!breached && open) { db.prepare('UPDATE alerts SET resolved=1 WHERE id=?').run(open.id); changed++; }
    }
  }
  db.prepare("UPDATE alerts SET resolved=1 WHERE device_id<>'' AND resolved=0 AND (device_id NOT IN (SELECT id FROM devices) OR rule_id NOT IN (SELECT id FROM rules WHERE enabled=1))").run();
  return changed;
}
function refreshOffline() {
  const cutoff = Date.now() - config.offlineAfterMinutes * 60000;
  let changed = 0;
  for (const d of db.prepare('SELECT id,status,last_seen FROM devices WHERE managed=1').all()) {
    const last = d.last_seen ? Date.parse(d.last_seen) : 0;
    if (last < cutoff && d.status !== 'Offline') { db.prepare("UPDATE devices SET status='Offline', cpu=0, memory=0 WHERE id=?").run(d.id); changed++; }
  }
  return changed;
}
function maintenance() {
  try {
    const a = refreshOffline(), b = evaluateRules();
    db.prepare('DELETE FROM metrics WHERE at_ms<?').run(Date.now() - config.metricRetentionDays * 86400000);
    if (a || b) broadcast();
  } catch (e) { console.error('maintenance error', e); }
}
setInterval(maintenance, 30000).unref();

// ---------------------------------------------------------------- routing
const routes = [];
const route = (method, pattern, auth, handler) => routes.push({ method, re: new RegExp(`^${pattern.replace(/:(\w+)/g, '(?<$1>[^/]+)')}$`), auth, handler });
const send = (res, status, body) => { const s = JSON.stringify(body); res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store' }); res.end(s); };
const fail = (status, error, extra) => Object.assign(new Error(error), { status, extra });

const failures = new Map();
route('POST', '/api/auth/login', null, ({ body, req }) => {
  const ip = req.socket.remoteAddress, rec = failures.get(ip) || { n: 0, until: 0 };
  if (rec.until > Date.now()) throw fail(429, 'Too many failed attempts. Wait a few minutes and try again.');
  const u = String(body.username || '').trim(), p = String(body.password || '');
  if (u && p && u.toLowerCase() === String(config.adminUsername).toLowerCase() && safeEq(p, config.adminPassword)) {
    failures.delete(ip);
    return { username: config.adminUsername, role: 'Administrator', token: signJwt({ sub: config.adminUsername, role: 'Administrator' }, 8) };
  }
  rec.n++; if (rec.n >= 8) { rec.until = Date.now() + 5 * 60000; rec.n = 0; } failures.set(ip, rec);
  throw fail(401, 'Invalid username or password.');
});
route('GET', '/health', null, () => ({ status: 'ok', service: 'DigitalMonitoringSystem', version: '2.0' }));
route('GET', '/api/dashboard', 'admin', () => buildDashboard());
route('GET', '/api/system/overview', 'admin', () => { const s = buildDashboard(); return { serviceName: 'DigitalMonitoringSystem', status: s.openAlerts === 0 ? 'Healthy' : 'Degraded', lastSyncUtc: now(), summary: s }; });

const deviceFull = (d) => ({ ...deviceRow(d), vendor: d.vendor, platform: d.platform, disk: d.disk, lastSeen: d.last_seen, isManaged: !!d.managed });
route('GET', '/api/devices', 'admin', () => db.prepare('SELECT * FROM devices ORDER BY hostname COLLATE NOCASE').all().map(deviceFull));
route('GET', '/api/devices/:id', 'admin', ({ params }) => { const d = db.prepare('SELECT * FROM devices WHERE id=?').get(params.id); if (!d) throw fail(404, 'Device not found.'); return deviceFull(d); });
route('DELETE', '/api/devices/:id', 'admin', ({ params }) => {
  const d = db.prepare('SELECT id FROM devices WHERE id=?').get(params.id); if (!d) throw fail(404, 'Device not found.');
  db.prepare('DELETE FROM metrics WHERE device_id=?').run(d.id); db.prepare('DELETE FROM assignments WHERE device_id=?').run(d.id);
  db.prepare('DELETE FROM devices WHERE id=?').run(d.id); evaluateRules(); broadcast();
  return { deviceId: d.id, status: 'removed' };
});
route('GET', '/api/devices/:id/metrics', 'admin', ({ params, query }) => {
  if (!db.prepare('SELECT 1 FROM devices WHERE id=?').get(params.id)) throw fail(404, 'Device not found.');
  const win = Math.min(Math.max(parseInt(query.get('windowMinutes')) || 60, 1), 10080), max = Math.min(Math.max(parseInt(query.get('maxSamples')) || 120, 1), 1000);
  const rows = db.prepare('SELECT * FROM metrics WHERE device_id=? AND at_ms>=? ORDER BY at_ms DESC LIMIT ?').all(params.id, Date.now() - win * 60000, max).reverse();
  const avg = (k) => (rows.length ? Math.round((rows.reduce((s, r) => s + r[k], 0) / rows.length) * 10) / 10 : 0);
  const peak = (k) => (rows.length ? Math.max(...rows.map((r) => r[k])) : 0);
  return { deviceId: params.id, windowMinutes: win, sampleCount: rows.length, avgCpu: avg('cpu'), avgMemory: avg('memory'), peakCpu: peak('cpu'), peakMemory: peak('memory'),
    samples: rows.map((r) => ({ id: String(r.id), deviceId: r.device_id, cpu: r.cpu, memory: r.memory, disk: r.disk, capturedAt: r.at, capturedAtEpochMs: r.at_ms })) };
});
route('POST', '/api/devices/enroll', 'admin', ({ body, req }) => {
  const hostname = String(body.hostname || '').trim(), ip = String(body.ipAddress || '').trim();
  if (!hostname || !ip) throw fail(400, 'Device enrollment requires hostname and IP address.');
  if (hostname.length > 100 || !/^[\w.\- ]+$/.test(hostname)) throw fail(400, 'The hostname contains characters that are not allowed.');
  if (!net.isIP(ip)) throw fail(400, 'The IP address is not a valid IPv4 or IPv6 address.');
  const clean = (v) => String(v || '').trim().slice(0, 100);
  const existing = db.prepare('SELECT * FROM devices WHERE lower(hostname)=lower(?)').get(hostname);
  if (existing && !existing.managed) {
    throw fail(409, 'This hostname belongs to a demo-only inventory record. Enroll the actual computer using its verified hostname.');
  }
  let id, reEnrolled = false, status = 'Managed';
  if (existing) { // the same computer enrolled again: keep its history, refresh details, new token
    id = existing.id; reEnrolled = true; status = existing.status;
    db.prepare("UPDATE devices SET ip=?, vendor=COALESCE(NULLIF(?,''),vendor), platform=COALESCE(NULLIF(?,''),platform), location=COALESCE(NULLIF(?,''),location), managed=1 WHERE id=?")
      .run(ip, clean(body.vendor), clean(body.platform), clean(body.location), id);
  } else {
    id = uid('DEV');
    db.prepare('INSERT INTO devices (id,hostname,ip,vendor,platform,location,status,last_seen,managed,created_at) VALUES (?,?,?,?,?,?,?,?,1,?)')
      .run(id, hostname, ip, clean(body.vendor), clean(body.platform), clean(body.location), 'Managed', now(), now());
  }
  const enrollmentToken = issueDeviceToken(id); broadcast();
  return { id, hostname: existing ? existing.hostname : hostname, status, isManaged: true, policyState: 'Pending', enrollmentToken, apiBaseUrl: baseUrl(req), reEnrolled };
});
route('POST', '/api/devices/:id/token', 'admin', ({ params, req }) => {
  const d = db.prepare('SELECT * FROM devices WHERE id=?').get(params.id); if (!d) throw fail(404, 'Device not found.');
  if (!d.managed) throw fail(409, 'Demo-only inventory records cannot receive monitoring tokens.');
  return { deviceId: d.id, hostname: d.hostname, enrollmentToken: issueDeviceToken(d.id), apiBaseUrl: baseUrl(req) };
});
route('POST', '/api/devices/:id/heartbeat', 'adminOrDevice', ({ params, body, auth }) => {
  if (auth.role === 'Device' && auth.deviceId !== params.id) throw fail(403, 'This token belongs to a different device.');
  const d = db.prepare('SELECT * FROM devices WHERE id=?').get(params.id); if (!d) throw fail(404, 'Device not found. It may have been removed; re-enroll it.');
  if (!d.managed) throw fail(409, 'Demo-only inventory records cannot report monitoring data.');
  const pct = (v) => Math.min(100, Math.max(0, Math.round(Number(v) || 0)));
  const cpu = pct(body.cpu), memory = pct(body.memory), disk = pct(body.disk), t = Date.now(), at = new Date(t).toISOString();
  db.prepare('INSERT INTO metrics (device_id,cpu,memory,disk,at_ms,at) VALUES (?,?,?,?,?,?)').run(d.id, cpu, memory, disk, t, at);
  db.prepare("UPDATE devices SET cpu=?, memory=?, disk=?, status='Online', last_seen=? WHERE id=?").run(cpu, memory, disk, at, d.id);
  const before = new Set(db.prepare('SELECT id FROM alerts WHERE resolved=0').all().map((a) => a.id));
  evaluateRules();
  const created = db.prepare('SELECT id FROM alerts WHERE resolved=0 AND device_id=?').all(d.id).map((a) => a.id).filter((i) => !before.has(i));
  broadcast();
  return { deviceId: d.id, status: 'Online', lastSeen: at, sampleCount: db.prepare('SELECT COUNT(*) c FROM metrics WHERE device_id=?').get(d.id).c, triggeredAlertIds: created };
});

route('GET', '/api/alerts', 'admin', () => db.prepare('SELECT * FROM alerts WHERE resolved=0 ORDER BY timestamp DESC').all().map(alertRow));
route('POST', '/api/alerts/:id/acknowledge', 'admin', ({ params }) => {
  if (!db.prepare('SELECT 1 FROM alerts WHERE id=?').get(params.id)) throw fail(404, 'Alert not found.');
  db.prepare('UPDATE alerts SET acknowledged=1 WHERE id=?').run(params.id); broadcast(); return { alertId: params.id, status: 'acknowledged' };
});

route('GET', '/api/policies', 'admin', () => buildDashboard().policies);
route('GET', '/api/devices/:id/policies', 'admin', ({ params }) => db.prepare('SELECT * FROM assignments WHERE device_id=?').all(params.id).map((a) => ({ id: a.id, deviceId: a.device_id, policyId: a.policy_id, state: a.state, enforcedBy: a.enforced_by, enforcedAt: a.enforced_at, message: a.message })));
route('POST', '/api/devices/:deviceId/policies/:policyId/enforce', 'admin', ({ params, auth }) => {
  const d = db.prepare('SELECT * FROM devices WHERE id=?').get(params.deviceId), p = db.prepare('SELECT * FROM policies WHERE id=?').get(params.policyId);
  if (!d || !p) throw fail(400, 'Device or policy was not found.', { status: 'rejected' });
  if (!d.managed) throw fail(400, 'Only managed devices can receive policies.', { status: 'rejected' });
  const message = `${p.name} recorded as assigned to ${d.hostname}. Operating-system enforcement depends on the agent version.`;
  db.prepare("INSERT INTO assignments (id,device_id,policy_id,state,enforced_by,enforced_at,message) VALUES (?,?,?,'Assigned',?,?,?) ON CONFLICT(device_id,policy_id) DO UPDATE SET state='Assigned', enforced_by=excluded.enforced_by, enforced_at=excluded.enforced_at, message=excluded.message")
    .run(uid('ASG'), d.id, p.id, auth.username, now(), message);
  db.prepare("UPDATE policies SET status='Active', updated_at=? WHERE id=?").run(now(), p.id); broadcast();
  return { deviceId: d.id, policyId: p.id, status: 'Assigned', message };
});
route('DELETE', '/api/devices/:deviceId/policies/:policyId/assign', 'admin', ({ params }) => {
  const r = db.prepare('DELETE FROM assignments WHERE device_id=? AND policy_id=?').run(params.deviceId, params.policyId);
  if (!r.changes) throw fail(404, 'That policy is not assigned to the device.'); broadcast(); return { status: 'unassigned' };
});

const ruleRow = (r) => ({ id: r.id, name: r.name, metric: r.metric, threshold: r.threshold, durationMinutes: r.duration_minutes, severity: r.severity, enabled: !!r.enabled, updatedAt: r.updated_at });
function validateRule(b) {
  const metric = String(b.metric || '').toLowerCase(), severity = ['High', 'Medium', 'Info'].find((s) => s.toLowerCase() === String(b.severity || '').toLowerCase());
  if (!String(b.name || '').trim()) throw fail(400, 'Rule name is required.');
  if (!['cpu', 'memory', 'disk', 'offline'].includes(metric)) throw fail(400, 'Metric must be cpu, memory, disk or offline.');
  if (!severity) throw fail(400, 'Severity must be High, Medium or Info.');
  const threshold = Math.round(Number(b.threshold)), dur = Math.round(Number(b.durationMinutes ?? 0));
  if (!(threshold >= 1) || (metric !== 'offline' && threshold > 100)) throw fail(400, 'Threshold is out of range.');
  return [String(b.name).trim(), metric, threshold, Math.max(0, dur || 0), severity, b.enabled === false ? 0 : 1];
}
route('GET', '/api/alert-rules', 'admin', () => db.prepare('SELECT * FROM rules ORDER BY name').all().map(ruleRow));
route('POST', '/api/alert-rules', 'admin', ({ body }) => { const id = uid('RULE'); db.prepare('INSERT INTO rules VALUES (?,?,?,?,?,?,?,?)').run(id, ...validateRule(body), now()); return ruleRow(db.prepare('SELECT * FROM rules WHERE id=?').get(id)); });
route('PUT', '/api/alert-rules/:id', 'admin', ({ params, body }) => {
  if (!db.prepare('SELECT 1 FROM rules WHERE id=?').get(params.id)) throw fail(404, 'Rule not found.');
  db.prepare('UPDATE rules SET name=?,metric=?,threshold=?,duration_minutes=?,severity=?,enabled=?,updated_at=? WHERE id=?').run(...validateRule(body), now(), params.id);
  return ruleRow(db.prepare('SELECT * FROM rules WHERE id=?').get(params.id));
});
route('DELETE', '/api/alert-rules/:id', 'admin', ({ params }) => { if (!db.prepare('DELETE FROM rules WHERE id=?').run(params.id).changes) throw fail(404, 'Rule not found.'); evaluateRules(); broadcast(); return { status: 'deleted' }; });

route('POST', '/api/mobile/pair', 'admin', ({ body, req }) => {
  const code = `MDM-${crypto.randomInt(100000, 1000000)}`, expires = new Date(Date.now() + 600000).toISOString();
  db.prepare('INSERT INTO pairings (id,code,device_name,platform,expires_at) VALUES (?,?,?,?,?)').run(uid('PAIR'), code, String(body.deviceName || 'Mobile Device').trim().slice(0, 100), String(body.platform || 'Android').trim().slice(0, 50), expires);
  const u = new URL(baseUrl(req));
  return { pairingCode: code, host: u.hostname, port: Number(u.port), fingerprint: `SHA256:MDM-LOCAL:${u.hostname}`, expiresAt: expires };
});
route('POST', '/api/mobile/pair/complete', null, ({ body, req }) => {
  const rec = db.prepare('SELECT * FROM pairings WHERE code=?').get(String(body.pairingCode || '').trim().toUpperCase());
  if (!rec) throw fail(400, 'Pairing code was not found.');
  if (rec.used) throw fail(400, 'Pairing code has already been used.');
  if (Date.parse(rec.expires_at) <= Date.now()) throw fail(400, 'Pairing code has expired.');
  const id = uid('DEV'), ip = String(body.ipAddress || 'Unknown').trim().slice(0, 64);
  db.prepare("INSERT INTO devices (id,hostname,ip,vendor,platform,location,status,last_seen,managed,created_at) VALUES (?,?,?,'Mobile',?,?,'Online',?,1,?)")
    .run(id, rec.device_name, ip, rec.platform, String(body.location || 'Mobile').trim().slice(0, 100), now(), now());
  db.prepare('UPDATE pairings SET used=1, device_id=? WHERE id=?').run(id, rec.id); broadcast();
  return { deviceId: id, deviceName: rec.device_name, enrollmentToken: issueDeviceToken(id), apiBaseUrl: baseUrl(req) };
});

// ---------------------------------------------------------------- server
function authenticate(req, url) {
  const dev = req.headers['x-device-token'];
  const header = String(req.headers.authorization || '');
  const deviceToken = dev || (/^Device\s+/i.test(header) ? header.replace(/^Device\s+/i, '') : null);
  if (deviceToken) { const id = verifyDeviceToken(deviceToken); return id ? { role: 'Device', deviceId: id } : null; }
  const jwt = header.startsWith('Bearer ') ? header.slice(7) : url.searchParams.get('access_token');
  const body = verifyJwt(jwt);
  return body && body.role === 'Administrator' ? { role: 'Administrator', username: body.sub } : null;
}
const MIME = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8', '.svg': 'image/svg+xml', '.png': 'image/png', '.json': 'application/json', '.ico': 'image/x-icon' };
function serveStatic(req, res, pathname) {
  let file = path.normalize(path.join(PUBLIC_DIR, decodeURIComponent(pathname)));
  if (!file.startsWith(PUBLIC_DIR)) { res.writeHead(403); return res.end(); }
  if (!fs.existsSync(file) || fs.statSync(file).isDirectory()) file = path.join(PUBLIC_DIR, 'index.html');
  if (!fs.existsSync(file)) { res.writeHead(404); return res.end('Dashboard files are missing (public/index.html).'); }
  const ext = path.extname(file);
  res.writeHead(200, { 'Content-Type': MIME[ext] || 'application/octet-stream', 'Cache-Control': ext === '.html' ? 'no-cache' : 'public, max-age=3600' });
  fs.createReadStream(file).pipe(res);
}
const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, 'http://x');
  try {
    if (req.method === 'OPTIONS') { res.writeHead(204, { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': 'Content-Type, Authorization, X-Device-Token', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS' }); return res.end(); }
    res.setHeader('Access-Control-Allow-Origin', '*');
    if (url.pathname === '/api/events' && req.method === 'GET') {
      const a = authenticate(req, url); if (!a || a.role !== 'Administrator') return send(res, 401, { error: 'Unauthorized' });
      res.writeHead(200, { 'Content-Type': 'text/event-stream', 'Cache-Control': 'no-cache', Connection: 'keep-alive' });
      res.write(`event: dashboard\ndata: ${JSON.stringify(buildDashboard())}\n\n`);
      clients.add(res); const ping = setInterval(() => res.write(': ping\n\n'), 25000);
      req.on('close', () => { clearInterval(ping); clients.delete(res); }); return;
    }
    if (!url.pathname.startsWith('/api/') && url.pathname !== '/health') return serveStatic(req, res, url.pathname);
    const r = routes.find((x) => x.method === req.method && x.re.test(url.pathname));
    if (!r) return send(res, 404, { error: 'Not found.' });
    let auth = null;
    if (r.auth) {
      auth = authenticate(req, url);
      if (!auth) return send(res, 401, { error: 'Unauthorized' });
      if (r.auth === 'admin' && auth.role !== 'Administrator') return send(res, 403, { error: 'Administrator access required.' });
    }
    let body = {};
    if (['POST', 'PUT'].includes(req.method)) {
      const chunks = []; let size = 0;
      for await (const c of req) { size += c.length; if (size > 1e6) throw fail(413, 'Request too large.'); chunks.push(c); }
      const raw = Buffer.concat(chunks).toString();
      if (raw) { try { body = JSON.parse(raw); } catch { throw fail(400, 'Request body is not valid JSON.'); } }
    }
    const params = r.re.exec(url.pathname).groups || {};
    send(res, 200, await r.handler({ req, body, params, query: url.searchParams, auth }));
  } catch (e) {
    if (e.status) return send(res, e.status, { error: e.message, ...(e.extra || {}) });
    console.error(e); send(res, 500, { error: 'Internal server error.' });
  }
});
server.on('error', (e) => {
  console.error(e.code === 'EADDRINUSE' ? `Port ${config.port} is already in use. Close the other copy of this program (or change "port" in config.json).` : e.message);
  process.exit(1);
});
server.listen(config.port, '0.0.0.0', () => {
  console.log('==============================================================');
  console.log(' DIGITAL MONITORING SYSTEM - MUKONO DISTRICT LOCAL GOVERNMENT');
  console.log('==============================================================');
  console.log(` Open on this computer : http://localhost:${config.port}`);
  console.log(` Open from the network : http://${lanIp()}:${config.port}`);
  console.log(` Administrator login   : ${config.adminUsername} / ${config.adminPassword === defaults.adminPassword ? config.adminPassword + '   (change it in config.json)' : '(set in config.json)'}`);
  console.log(' Keep this window open. Press Ctrl+C to stop.');
  maintenance();
});
module.exports = { server, db, config };
