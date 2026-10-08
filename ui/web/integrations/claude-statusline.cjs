const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
let input = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', chunk => { input += chunk; });
process.stdin.on('end', () => {
  try {
    const data = JSON.parse(input);
    const root = path.join(process.env.PC_AI_DASHBOARD_DATA || (process.platform === 'darwin' ? path.join(os.homedir(), 'Library', 'Application Support', 'PcAiDashboard') : path.join(process.env.LOCALAPPDATA || path.join(os.homedir(), '.local', 'share'), 'PcAiDashboard')), 'claude-sessions');
    fs.mkdirSync(root, { recursive: true });
    const id = String(data.session_id || 'current').replace(/[^a-zA-Z0-9_-]/g, '_').slice(0, 100);
    const label = data.session_name || path.basename(data.workspace?.current_dir || data.cwd || '') || 'Claude Code';
    const output = { session_id: id, label, context_window: data.context_window || {}, rate_limits: data.rate_limits || {}, updated_at: new Date().toISOString() };
    const file = path.join(root, id + '.json');
    // Each process has its own temporary file so simultaneous sessions never collide.
    const tmp = file + '.' + process.pid + '.tmp';
    fs.writeFileSync(tmp, JSON.stringify(output));
    fs.renameSync(tmp, file);
    const ctx = data.context_window?.used_percentage;
    process.stdout.write((data.model?.display_name || 'Claude') + (ctx == null ? '' : ' · Kontext ' + Math.round(ctx) + '%'));
  } catch { process.stdout.write('Claude'); }
});
