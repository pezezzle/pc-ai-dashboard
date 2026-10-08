"use strict";
const $ = (selector) => document.querySelector(selector);
const $$ = (selector) => [...document.querySelectorAll(selector)];
const bridge = window.dashboardBridge ?? window.chrome?.webview;
let nativeVideo = false;
let nativePlaybackError = false;
let platform = 'windows';
let settings = { backgroundMode: 'gradient', youtubeUrl: '', localVideoPath: '', muted: true, volume: 25, dim: .5, cardOpacity: .76, accent: '#66e7c8', textColor: '#ecf3f6', fullscreen: true, displayId: '', codexSessionId: '', claudeSessionId: '', screenSaverTimerEnabled: false, screenSaverIdleMinutes: 10, screenSaverDashboardDisplayIds: null };
let availableDisplays = [];
let latest = null;
let player = null;
let youtubeReady = false;
let mediaSignature = '';
let localMediaUrl = '';
let playing = false;
let noticeTimer = 0;
const sparkHistory = { cpuLoad: [], gpuLoad: [] };
const video = $('#local-video');
const dialog = $('#settings-dialog');
const form = $('#settings-form');
const send = (type, data = {}) => bridge?.postMessage({ type, ...data });
const fmt = (value, digits = 0) => value == null || !Number.isFinite(value) ? '—' : value.toLocaleString('de-DE', { minimumFractionDigits: digits, maximumFractionDigits: digits });
const text = (selector, value) => { $(selector).textContent = value; };
function element(tag, className, value) { const el = document.createElement(tag); el.className = className; if (value !== undefined)
    el.textContent = value; return el; }
function showNotice(message) { text('#notice', message); $('#notice').style.display = 'block'; clearTimeout(noticeTimer); noticeTimer = window.setTimeout(() => { $('#notice').style.display = 'none'; }, 5500); }
function scale() {
    if (document.body.classList.contains('notebook')) {
        $('#stage').style.transform = '';
        $('#stage').style.left = '';
        $('#stage').style.top = '';
        return;
    }
    const s = Math.min(innerWidth / 1024, innerHeight / 600);
    $('#stage').style.transform = `scale(${s})`;
    $('#stage').style.left = `${(innerWidth - 1024 * s) / 2}px`;
    $('#stage').style.top = `${(innerHeight - 600 * s) / 2}px`;
}
addEventListener('resize', scale);
scale();
function clock() { text('#clock', new Date().toLocaleTimeString('de-DE')); if (latest)
    latest.ai.forEach(renderAi); }
setInterval(clock, 1000);
clock();
function countdown(epoch) {
    if (epoch == null)
        return 'Reset unbekannt';
    const seconds = epoch - Math.floor(Date.now() / 1000);
    if (seconds <= 0)
        return 'Reset fällig';
    const days = Math.floor(seconds / 86400), hours = Math.floor(seconds % 86400 / 3600), mins = Math.floor(seconds % 3600 / 60);
    return 'Reset in ' + (days ? `${days}T ${hours}h` : hours ? `${hours}h ${mins}m` : `${Math.max(1, mins)}m`);
}
function age(timestamp) { if (!timestamp)
    return 'Noch kein Abruf'; const sec = Math.max(0, Math.floor((Date.now() - new Date(timestamp).getTime()) / 1000)); return sec < 60 ? 'gerade eben' : sec < 3600 ? `vor ${Math.floor(sec / 60)} Min.` : sec < 86400 ? `vor ${Math.floor(sec / 3600)} Std.` : `vor ${Math.floor(sec / 86400)} Tagen`; }
function bar(value) { const track = element('div', 'track'); const fill = element('i', ''); fill.style.width = `${Math.max(0, Math.min(100, value ?? 0))}%`; if ((value ?? 0) >= 90)
    fill.style.background = '#ffad72'; track.append(fill); return track; }
function render(data) {
    latest = data;
    const map = new Map(data.metrics.map(m => [m.id, m]));
    if (data.capabilities?.platform === 'macos') {
        $('.cooling').hidden = true;
        $('.fans').hidden = true;
        const system = $('#mac-system');
        system.hidden = false;
        system.replaceChildren();
        for (const metric of data.metrics.filter(m => m.id === 'batteryLoad' || m.id === 'memoryPressure' || m.unit === 'RPM')) {
            const card = element('article', 'card');
            card.append(element('span', 'eyebrow', metric.id === 'memoryPressure' ? 'SPEICHERDRUCK' : metric.label));
            const value = metric.id === 'memoryPressure' ? ({ 1: 'Normal', 2: 'Erhöht', 4: 'Kritisch' }[metric.value ?? 0] ?? '—') : `${fmt(metric.value)} ${metric.unit}`;
            card.append(element('strong', '', value));
            system.append(card);
        }
    }
    $$('[data-metric]').forEach(el => { const metric = map.get(el.dataset.metric); const value = metric?.value; el.textContent = fmt(value, metric?.unit === '°C' || el.dataset.metric === 'ramUsed' ? 1 : 0); el.classList.toggle('unavailable', value == null); el.classList.toggle('hot', metric?.unit === '°C' && (value ?? 0) > (metric.id.startsWith('coolant') ? 50 : 85)); el.title = metric ? `${metric.label} · ${metric.source} · ${age(metric.updatedAt)}` : 'Kein Messwert'; });
    $$('[data-bar]').forEach(el => { el.style.width = `${Math.min(100, Math.max(0, map.get(el.dataset.bar)?.value ?? 0))}%`; });
    updateTemperatureBars(map);
    $$('.fan-meter').forEach(el => { const value = map.get(el.dataset.duty)?.value; const fill = el.querySelector('.fan-meter-fill'); fill.style.strokeDasharray = `${Math.min(100, Math.max(0, value ?? 0))} 100`; el.classList.toggle('unavailable', value == null); el.title = value == null ? 'PWM-Wert nicht verfügbar' : `PWM-Ansteuerung: ${fmt(value, 2)} % · Skala 0–100 %`; });
    const pump = map.get('coolantPump')?.value, radiator = map.get('coolantRadiator')?.value;
    text('#delta-temp', pump != null && radiator != null ? fmt(pump - radiator, 1) : '—');
    text('#ram-total', fmt(map.get('ramTotal')?.value, 0) + ' GB TOTAL');
    const ramTotal = map.get('ramTotal')?.value, ramUsed = map.get('ramUsed')?.value;
    text('#ram-free', ramTotal != null && ramUsed != null ? fmt(ramTotal - ramUsed, 1) + ' GB frei' : '— GB frei');
    for (const id of ['cpuLoad', 'gpuLoad']) {
        const value = map.get(id)?.value;
        if (value != null) {
            sparkHistory[id].push(value);
            if (sparkHistory[id].length > 60)
                sparkHistory[id].shift();
        }
        const svg = $(id === 'cpuLoad' ? '#cpu-spark' : '#gpu-spark');
        const points = sparkHistory[id].map((v, i, all) => `${i / Math.max(1, all.length - 1) * 150},${27 - v / 100 * 26}`).join(' ');
        let line = svg.querySelector('polyline');
        if (!line) {
            line = document.createElementNS('http://www.w3.org/2000/svg', 'polyline');
            svg.append(line);
        }
        line.setAttribute('points', points);
    }
    data.ai.forEach(renderAi);
    const drives = $('#drives');
    drives.replaceChildren();
    for (const d of data.drives) {
        const row = element('div', 'drive'), heading = element('div', 'drive-title');
        heading.append(element('span', '', d.name.replace(/\\$/, '') + (d.label ? ' · ' + d.label : '')), element('strong', '', fmt(d.usedPercent, 2) + ' %'));
        const info = element('div', 'drive-info');
        info.append(element('span', '', `${fmt(d.usedGb)} / ${fmt(d.totalGb)} GB`), element('span', '', `${fmt(d.totalGb - d.usedGb)} GB frei`));
        row.append(heading, info, bar(d.usedPercent));
        drives.append(row);
    }
    if (!data.drives.length)
        drives.append(element('div', 'empty', 'Keine Laufwerksdaten'));
    text('#hardware-state', data.hardwareStatus);
    text('#live-state span', data.metrics.some(m => m.value != null) ? 'SYSTEM LIVE' : 'VERBINDEN');
}
function updateTemperatureBars(map) {
    $$('[data-temp]').forEach(el => { const value = map.get(el.dataset.temp)?.value; el.style.height = `${Math.min(100, Math.max(0, (value ?? 0) / Number(settings.temperatureScaleMax ?? 100) * 100))}%`; el.closest('.temperature-gauge')?.classList.toggle('unavailable', value == null); });
}
function renderAi(provider) {
    const name = provider.name.toLowerCase();
    const card = $(`#${name}-card`);
    if (!card)
        return;
    const state = card.querySelector('.provider-state');
    state.textContent = provider.status;
    state.title = `${age(provider.updatedAt)}${provider.detail ? ' · ' + provider.detail : ''}`;
    const container = card.querySelector('.quotas');
    const scroll = container.scrollTop;
    container.replaceChildren();
    for (const q of provider.quotas) {
        const row = element('div', 'quota');
        const title = element('div', 'quota-title');
        title.append(element('span', '', q.label), element('strong', '', fmt(q.usedPercent) + '%'));
        const info = element('div', 'quota-detail');
        info.append(element('span', '', `${fmt(100 - q.usedPercent)}% verbleibend`), element('span', '', countdown(q.resetsAt)));
        const stale = provider.status !== 'Live' || (provider.updatedAt != null && Date.now() - new Date(provider.updatedAt).getTime() > 120000) || (q.resetsAt != null && q.resetsAt * 1000 <= Date.now());
        if (stale) {
            row.style.opacity = '.55';
            title.title = 'Letzter bekannter Wert · ' + age(provider.updatedAt);
        }
        row.append(title, info, bar(q.usedPercent));
        container.append(row);
    }
    if (!provider.quotas.length)
        container.append(element('div', 'empty', provider.detail ?? 'Noch keine Account-Limits verfügbar'));
    container.scrollTop = scroll;
    renderManualResets(card, provider);
    renderCredits(card, provider);
    const select = card.querySelector('.session-select');
    const key = name + 'SessionId';
    const signature = JSON.stringify(provider.sessions.map(s => [s.id, s.label]));
    if (select.dataset.signature !== signature) {
        select.replaceChildren();
        const auto = new Option('Letzter aktiver Chat', '');
        select.add(auto);
        provider.sessions.forEach(s => select.add(new Option(s.label, s.id)));
        select.dataset.signature = signature;
    }
    select.value = String(settings[key] ?? '');
    const session = provider.sessions.find(s => s.id === select.value) ?? provider.sessions[0];
    const ctx = card.querySelector('.context-value');
    ctx.replaceChildren();
    if (session) {
        const label = session.usedPercent == null ? `${fmt(session.tokens)} Tokens` : `${fmt(session.usedPercent)}%`;
        const detail = session.capacity != null ? `${fmt(session.tokens)} / ${fmt(session.capacity)}` : 'Letzte Eingabe';
        ctx.append(element('strong', '', label), element('span', '', detail), element('span', 'context-age', age(session.updatedAt)));
        ctx.title = 'Letzter gemessener Kontext · ' + new Date(session.updatedAt).toLocaleString('de-DE');
    }
    else
        ctx.append(element('span', '', name === 'claude' ? 'Ab nächster Claude-Code-Sitzung' : 'Noch keine lokalen Sitzungsdaten'));
}
function renderManualResets(card, provider) {
    const banner = card.querySelector('.manual-reset');
    if (!banner)
        return;
    const reset = provider.manualResets;
    const visible = reset != null && Number.isSafeInteger(reset.availableCount) && reset.availableCount > 0;
    banner.hidden = !visible;
    card.classList.toggle('has-manual-resets', visible);
    banner.replaceChildren();
    if (!visible || !reset)
        return;
    const stale = provider.status !== 'Live' || provider.updatedAt == null || Date.now() - new Date(provider.updatedAt).getTime() > 120000 || (reset.nextExpiresAt != null && reset.nextExpiresAt * 1000 <= Date.now());
    banner.classList.toggle('stale', stale);
    const title = stale ? `Letzter Stand: ${fmt(reset.availableCount)} Reset${reset.availableCount === 1 ? '' : 's'}` : `${fmt(reset.availableCount)} ${reset.availableCount === 1 ? 'manueller Reset' : 'manuelle Resets'} verfügbar`;
    banner.append(element('strong', '', '↻ ' + title));
    let detail = stale ? 'In Codex prüfen · ' + age(provider.updatedAt) : 'In Codex → Nutzung einlösen';
    if (!stale && reset.nextExpiresAt != null)
        detail = 'Bis ' + new Date(reset.nextExpiresAt * 1000).toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit' }) + ' · ' + detail;
    banner.append(element('span', '', detail));
    banner.title = 'Gespeicherte manuelle Limit-Resets, getrennt vom automatischen Reset-Countdown. In Codex unter Nutzung prüfen und selbst auslösen. Diese Anzeige verbraucht keinen Reset.' + (reset.nextExpiresAt != null ? '\nBekanntes Ablaufdatum: ' + new Date(reset.nextExpiresAt * 1000).toLocaleString('de-DE') : '') + '\nLetzter Abruf: ' + age(provider.updatedAt);
}
function renderCredits(card, provider) {
    const container = card.querySelector('.credits');
    container.replaceChildren();
    const credit = provider.credits;
    const line = element('div', 'credit-line');
    line.append(element('span', '', 'GUTHABEN'));
    const value = credit?.unlimited ? 'Unbegrenzt' : credit?.balance != null ? `${fmt(credit.balance, 2)} ${credit.unit}` : 'Nicht abrufbar';
    line.append(element('strong', '', value));
    container.append(line);
    let detail = credit?.detail ?? (credit?.balance != null || credit?.unlimited ? 'Aktueller Credit-Stand' : 'Anbieter liefert derzeit keinen Betrag');
    if (credit?.spent != null)
        detail = `Monat: ${fmt(credit.spent, 2)}${credit.limit != null ? ' / ' + fmt(credit.limit, 2) : ''} ${credit.unit}${credit.enabled ? '' : ' · Zusatznutzung aus'}`;
    const stale = provider.status !== 'Live' || (provider.updatedAt != null && Date.now() - new Date(provider.updatedAt).getTime() > 120000);
    if (stale && credit)
        detail = 'Letzter Stand · ' + age(provider.updatedAt);
    container.append(element('div', 'credit-detail', detail));
    container.classList.toggle('unavailable', !credit);
    container.title = `${credit?.detail ?? detail} · Aktualisierung alle 60 Sekunden · ${age(provider.updatedAt)}`;
}
function applyConfiguration(config) {
    nativeVideo = config.nativeVideo === true;
    document.body.classList.toggle('native-video', nativeVideo && config.settings.backgroundMode === 'local' && !!config.settings.localVideoPath);
    platform = config.platform ?? 'windows';
    document.body.dataset.platform = platform;
    settings = { ...config.settings, textColor: config.settings.textColor ?? '#ecf3f6', temperatureScaleMax: config.settings.temperatureScaleMax ?? 100, screenSaverTimerEnabled: config.settings.screenSaverTimerEnabled ?? false, screenSaverIdleMinutes: config.settings.screenSaverIdleMinutes ?? 10, screenSaverDashboardDisplayIds: config.settings.screenSaverDashboardDisplayIds ?? null };
    availableDisplays = config.displays;
    document.body.classList.toggle('screensaver', config.screenSaver === true);
    document.body.classList.toggle('screensaver-video-only', config.screenSaver === true && config.showDashboard === false);
    document.body.classList.toggle('notebook', settings.profile === 'notebook' && config.screenSaver !== true);
    scale();
    if (platform === 'macos') {
        text('#cpu-card .eyebrow .muted', 'SENSOREN / TOTAL');
        text('#gpu-card .eyebrow .muted', 'SENSOREN / TOTAL');
        text('#settings-status', 'Einstellungen bleiben auf diesem Mac.');
    }
    $$('[data-platform="windows"]').forEach(el => { el.hidden = platform === 'macos'; });
    $$('[data-platform="macos"]').forEach(el => { el.hidden = platform !== 'macos'; });
    if (platform === 'macos')
        settings.keepDashboardInBackground = config.settings.keepDashboardInBackground ?? false;
    document.body.dataset.displayId = config.displayId ?? '';
    updateScreenSaverControls();
    localMediaUrl = config.mediaUrl ?? '';
    applyAppearance();
    updateMuteButton();
    $('#volume').value = String(settings.volume);
    const displays = $('#display-select');
    displays.replaceChildren();
    config.displays.forEach(d => displays.add(new Option(d.label, d.id)));
    updateScreenSaverDisplays();
    for (const control of Array.from(form.elements))
        if (control instanceof HTMLInputElement || control instanceof HTMLSelectElement) {
            if (!control.name || !(control.name in settings))
                continue;
            if (control instanceof HTMLInputElement && control.type === 'checkbox')
                control.checked = Boolean(settings[control.name]);
            else
                control.value = String(settings[control.name]);
        }
    applyBackground();
    applyAudio();
    if (latest)
        latest.ai.forEach(renderAi);
}
function applyAppearance() {
    document.documentElement.style.setProperty('--accent', settings.accent);
    document.documentElement.style.setProperty('--text', settings.textColor);
    document.documentElement.style.setProperty('--muted', `color-mix(in srgb, ${settings.textColor} 63%, #13202c)`);
    document.documentElement.style.setProperty('--card-alpha', String(settings.cardOpacity));
    $('#shade').style.opacity = String(settings.dim);
    $$('[data-temp-max]').forEach(el => { el.textContent = `${settings.temperatureScaleMax} °C`; });
    if (latest)
        updateTemperatureBars(new Map(latest.metrics.map(m => [m.id, m])));
}
function save() { send('saveSettings', { settings }); if (!bridge)
    applyConfiguration({ settings, displays: availableDisplays }); }
function updateScreenSaverDisplays() {
    const container = $('#screensaver-displays');
    container.replaceChildren();
    for (const display of availableDisplays) {
        const label = element('label', '');
        const input = document.createElement('input');
        input.type = 'checkbox';
        input.dataset.displayId = display.id;
        input.checked = settings.screenSaverDashboardDisplayIds === null || settings.screenSaverDashboardDisplayIds.some(id => id.toLowerCase() === display.id.toLowerCase());
        input.onchange = () => {
            const disconnected = (settings.screenSaverDashboardDisplayIds ?? []).filter(id => !availableDisplays.some(d => d.id.toLowerCase() === id.toLowerCase()));
            settings.screenSaverDashboardDisplayIds = [...disconnected, ...$$('#screensaver-displays input:checked').map(c => c.dataset.displayId)];
            updateScreenSaverSelectionStatus();
            save();
        };
        label.append(input, document.createTextNode(display.label));
        container.append(label);
    }
    updateScreenSaverSelectionStatus();
}
function updateScreenSaverSelectionStatus() {
    const count = availableDisplays.filter(d => settings.screenSaverDashboardDisplayIds === null || settings.screenSaverDashboardDisplayIds.some(id => id.toLowerCase() === d.id.toLowerCase())).length;
    text('#screensaver-selection-status', `Dashboard: ${count} ${count === 1 ? 'Bildschirm' : 'Bildschirme'} · Nur Hintergrundvideo: ${availableDisplays.length - count}`);
    $('#screensaver-start').title = 'Bildschirmschoner auf allen Monitoren starten · Eingabe beendet ihn';
}
$('#screensaver-all').onclick = () => { settings.screenSaverDashboardDisplayIds = null; updateScreenSaverDisplays(); save(); };
$('#screensaver-none').onclick = () => { settings.screenSaverDashboardDisplayIds = []; updateScreenSaverDisplays(); save(); };
function updateScreenSaverControls() {
    const toggle = $('#screensaver-timer');
    const label = settings.screenSaverTimerEnabled ? 'Timer: an' : 'Timer: aus';
    if (toggle.textContent !== label)
        toggle.textContent = label;
    toggle.setAttribute('aria-pressed', String(settings.screenSaverTimerEnabled));
    toggle.title = settings.screenSaverTimerEnabled ? `Aktiv: nach ${settings.screenSaverIdleMinutes} Min. ohne Eingabe · Klicken zum Ausschalten` : 'Automatischen Bildschirmschoner einschalten';
    $('#screensaver-minutes').value = String(settings.screenSaverIdleMinutes);
}
$('#screensaver-start').onclick = () => send('startScreenSaver');
$('#screensaver-timer').onclick = () => { settings.screenSaverTimerEnabled = !settings.screenSaverTimerEnabled; updateScreenSaverControls(); save(); };
$('#screensaver-minutes').onchange = event => {
    const input = event.target;
    const value = input.valueAsNumber;
    if (!Number.isFinite(value) || !Number.isInteger(value) || value < 1 || value > 240) {
        updateScreenSaverControls();
        showNotice('Bitte eine Wartezeit zwischen 1 und 240 Minuten eingeben.');
        return;
    }
    settings.screenSaverIdleMinutes = value;
    updateScreenSaverControls();
    save();
};
window.openSettings = () => { if (!dialog.open)
    dialog.showModal(); };
$('#settings-button').onclick = window.openSettings;
$('#screensaver-config').onclick = () => { window.openSettings(); $('#screensaver-settings').scrollIntoView({ block: 'center' }); };
$('#close-settings').onclick = () => dialog.close();
for (const name of ['accent', 'textColor', 'cardOpacity', 'dim']) {
    const control = form.elements.namedItem(name);
    const update = () => { settings[name] = control.type === 'range' ? Number(control.value) : control.value; applyAppearance(); save(); text('#settings-status', 'Darstellung automatisch gespeichert.'); };
    control.addEventListener('input', update);
    control.addEventListener('change', update);
}
dialog.addEventListener('click', event => { if (event.target === dialog) {
    const r = dialog.getBoundingClientRect();
    if (event.clientX < r.left || event.clientX > r.right || event.clientY < r.top || event.clientY > r.bottom)
        dialog.close();
} });
form.onsubmit = event => {
    event.preventDefault();
    const next = { ...settings };
    for (const c of Array.from(form.elements))
        if ((c instanceof HTMLInputElement || c instanceof HTMLSelectElement) && c.name in next) {
            next[c.name] = c instanceof HTMLInputElement && c.type === 'checkbox' ? c.checked : typeof next[c.name] === 'number' ? Number(c.value) : c.value;
        }
    if (next.backgroundMode === 'youtube' && !youtubeId(String(next.youtubeUrl))) {
        showNotice('Bitte einen gültigen YouTube-Link oder eine Video-ID eingeben.');
        return;
    }
    settings = next;
    save();
    dialog.close();
    showNotice('Einstellungen gespeichert');
};
for (const [selector, type] of Object.entries({ '#pick-video': 'pickVideo', '#capture': 'capture', '#open-data': 'openData', '#minimize': 'minimize', '#exit': 'exit' }))
    $(selector).onclick = () => { if (selector === '#capture')
        dialog.close(); send(type); };
for (const name of ['pumpChannel', 'topChannel', 'sideChannel', 'bottomChannel', 'backChannel', 'pumpTempChannel', 'radiatorTempChannel', 'caseTempChannel']) {
    const select = form.elements.namedItem(name);
    const count = name.includes('Temp') ? 4 : 8;
    for (let i = 0; i < count; i++)
        select.add(new Option('Kanal ' + (i + 1), String(i)));
}
$$('.session-select').forEach(select => { select.onchange = () => { const name = select.closest('article').id.startsWith('codex') ? 'codex' : 'claude'; settings[name + 'SessionId'] = select.value; save(); }; });
$('#fullscreen').onclick = () => send(platform === 'macos' ? 'toggleFullscreen' : settings.fullscreen ? 'windowed' : 'fullscreen');
$('#keep-dashboard-background').onchange = event => { settings.keepDashboardInBackground = event.target.checked; save(); };
$('#mute').onclick = () => { settings.muted = !settings.muted; applyAudio(); save(); };
$('#volume').oninput = event => { settings.volume = Number(event.target.value); applyAudio(); };
$('#volume').onchange = save;
$('#play').onclick = () => { if (nativeVideo && settings.backgroundMode === 'local') {
    send('videoToggle');
    return;
} if (settings.backgroundMode === 'local') {
    if (video.paused)
        void video.play().catch(() => showNotice('Video bitte über den Player starten.'));
    else
        video.pause();
}
else if (player && youtubeReady) {
    if (player.getPlayerState() === 1)
        player.pauseVideo();
    else
        player.playVideo();
}
else
    showNotice('Wähle zuerst ein Video in den Einstellungen.'); };
function cinema(on) { document.body.classList.toggle('cinema', on); $('#leave-cinema').hidden = !on; video.controls = on; $('#native-player-controls').hidden = !(on && nativeVideo && settings.backgroundMode === 'local'); }
$('#cinema').onclick = () => cinema(true);
$('#leave-cinema').onclick = () => cinema(false);
addEventListener('keydown', event => { if (event.key === 'Escape')
    cinema(false); });
video.addEventListener('play', () => setPlaying(true));
video.addEventListener('pause', () => setPlaying(false));
video.addEventListener('error', () => { text('#media-state', 'Video konnte nicht geladen werden'); showNotice('Die Videodatei oder das Format konnte nicht geladen werden.'); });
function buttonIcon(button, icon, paths) {
    if (button.dataset.icon === icon)
        return;
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    for (const [name, value] of Object.entries({ viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', 'stroke-width': '1.8', 'stroke-linecap': 'round', 'stroke-linejoin': 'round', 'aria-hidden': 'true' }))
        svg.setAttribute(name, value);
    for (const d of paths) {
        const path = document.createElementNS(svg.namespaceURI, 'path');
        path.setAttribute('d', d);
        svg.append(path);
    }
    button.replaceChildren(svg);
    button.dataset.icon = icon;
}
function updateMuteButton() {
    const button = $('#mute');
    const speaker = 'M11 5 6 9H3v6h3l5 4V5Z';
    buttonIcon(button, settings.muted ? 'volume-off' : 'volume-on', settings.muted ? [speaker, 'M16 9l5 6M21 9l-5 6'] : [speaker, 'M15 8a6 6 0 0 1 0 8M18 5a10 10 0 0 1 0 14']);
    button.title = settings.muted ? 'Ton einschalten' : 'Ton ausschalten';
    button.setAttribute('aria-label', button.title);
    button.setAttribute('aria-pressed', String(settings.muted));
}
function setPlaying(value) {
    playing = value;
    const button = $('#play');
    buttonIcon(button, playing ? 'pause' : 'play', playing ? ['M8 5v14M16 5v14'] : ['M7 4v16l13-8L7 4Z']);
    button.title = playing ? 'Video pausieren' : 'Video starten';
    button.setAttribute('aria-label', button.title);
}
function applyAudio() { updateMuteButton(); if (nativeVideo && settings.backgroundMode === 'local')
    send('videoAudio', { muted: settings.muted, volume: settings.volume }); video.muted = settings.muted; video.volume = settings.volume / 100; if (player && youtubeReady) {
    player.setVolume(settings.volume);
    if (settings.muted)
        player.mute();
    else
        player.unMute();
} }
function youtubeId(value) {
    const input = value.trim();
    if (/^[A-Za-z0-9_-]{11}$/.test(input))
        return input;
    try {
        const url = new URL(input);
        const host = url.hostname.toLowerCase();
        let id = null;
        if (host === 'youtu.be')
            id = url.pathname.split('/')[1];
        else if (['youtube.com', 'www.youtube.com', 'm.youtube.com', 'www.youtube-nocookie.com', 'youtube-nocookie.com'].includes(host))
            id = url.searchParams.get('v') ?? (/^\/(embed|shorts|live)\//.test(url.pathname) ? url.pathname.split('/')[2] : null);
        return id && /^[A-Za-z0-9_-]{11}$/.test(id) ? id : null;
    }
    catch {
        return null;
    }
}
function applyBackground() {
    const signature = `${settings.backgroundMode}|${settings.youtubeUrl}|${settings.localVideoPath}|${localMediaUrl}|${nativeVideo}`;
    if (signature === mediaSignature)
        return;
    mediaSignature = signature;
    player?.destroy();
    player = null;
    youtubeReady = false;
    setPlaying(false);
    video.pause();
    video.removeAttribute('src');
    video.load();
    video.style.display = 'none';
    let mount = $('#youtube');
    if (!mount) {
        mount = element('div', '');
        mount.id = 'youtube';
        $('#background').prepend(mount);
    }
    mount.replaceChildren();
    if (nativeVideo && settings.backgroundMode === 'local' && settings.localVideoPath) {
        text('#media-state', 'LOKALES VIDEO');
    }
    else if (settings.backgroundMode === 'local' && settings.localVideoPath && localMediaUrl) {
        video.src = localMediaUrl;
        video.style.display = 'block';
        applyAudio();
        void video.play().catch(() => showNotice('Klicke auf ▶, um das Video zu starten.'));
        text('#media-state', 'LOKALES VIDEO');
    }
    else if (settings.backgroundMode === 'youtube') {
        const id = youtubeId(settings.youtubeUrl);
        if (!id) {
            text('#media-state', 'YouTube-Link fehlt');
            return;
        }
        text('#media-state', 'YOUTUBE · ' + id);
        if (window.YT?.Player)
            createYoutube(id);
        else {
            window.onYouTubeIframeAPIReady = () => { if (settings.backgroundMode === 'youtube') {
                const current = youtubeId(settings.youtubeUrl);
                if (current)
                    createYoutube(current);
            } };
            if (!document.querySelector('#youtube-api')) {
                const script = document.createElement('script');
                script.id = 'youtube-api';
                script.src = 'https://www.youtube.com/iframe_api';
                script.onerror = () => { text('#media-state', 'YouTube ist nicht erreichbar'); showNotice('YouTube konnte nicht geladen werden.'); };
                document.head.append(script);
            }
        }
    }
    else
        text('#media-state', 'RUHIGER HINTERGRUND');
}
function createYoutube(id) {
    if (!window.YT)
        return;
    player = new window.YT.Player('youtube', { videoId: id, width: '100%', height: '100%', playerVars: { autoplay: 1, mute: settings.muted ? 1 : 0, controls: 1, playsinline: 1, loop: 1, playlist: id, origin: location.origin, enablejsapi: 1 }, events: {
            onReady: () => { youtubeReady = true; applyAudio(); player?.playVideo(); },
            onStateChange: (event) => setPlaying(event.data === 1),
            onAutoplayBlocked: () => { showNotice('YouTube wartet auf einen Klick: ▶ oder „Player bedienen“.'); setPlaying(false); },
            onError: (event) => { text('#media-state', 'YOUTUBE · FEHLER ' + event.data); showNotice(event.data === 101 || event.data === 150 ? 'Dieses Video erlaubt keine Einbettung. Bitte ein anderes auswählen.' : 'YouTube-Wiedergabe nicht verfügbar (Fehler ' + event.data + ').'); }
        } });
}
function renderPlayback(data) {
    if (!nativeVideo || settings.backgroundMode !== 'local')
        return;
    setPlaying(data.paused === false);
    text('#native-video-play', data.paused === false ? 'Pause' : 'Abspielen');
    const position = $('#native-video-position');
    const duration = Number(data.duration) || 0, current = Number(data.time) || 0;
    position.max = String(duration);
    position.disabled = duration <= 0;
    if (!position.matches(':active'))
        position.value = String(current);
    const stamp = (seconds) => { const value = Math.floor(seconds); return `${Math.floor(value / 3600)}:${String(Math.floor(value / 60) % 60).padStart(2, '0')}:${String(value % 60).padStart(2, '0')}`; };
    text('#native-video-time', `${stamp(current)} / ${stamp(duration)}`);
    const failed = Number(data.error) > 0;
    if (failed && !nativePlaybackError) {
        text('#media-state', 'Video konnte nicht geladen werden');
        showNotice('Die Videodatei oder das Format konnte nicht geladen werden.');
    }
    nativePlaybackError = failed;
}
$('#native-video-play').onclick = () => send('videoToggle');
$('#native-video-position').onchange = event => send('videoSeek', { seconds: event.target.valueAsNumber });
bridge?.addEventListener('message', event => { const data = event.data; if (data.type === 'snapshot')
    render(data.data);
else if (data.type === 'configuration')
    applyConfiguration(data);
else if (data.type === 'playback')
    renderPlayback(data);
else if (data.type === 'notice')
    showNotice(String(data.message)); });
window.dashboardTest = { applyConfiguration, render, getSettings: () => settings };
send('ready');
