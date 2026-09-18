'use strict';

// State the page keeps: what the last discovery found, and what the person selected.
const state = {
  devices: [],
  device: null,
  files: [],
  sets: [],
  uploadedSet: null,
  running: null,
  logMuted: false,
};

const $ = (id) => document.getElementById(id);

// ---------------------------------------------------------------- the log panel

function log(text, level) {
  const line = document.createElement('li');
  line.textContent = text;
  if (level && level !== 'info') {
    line.className = level;
  }
  const lines = $('lines');
  lines.appendChild(line);
  lines.scrollTop = lines.scrollHeight;
  openLog();
}

// The log opens itself when there is something to read, which is what the panel beside
// the form used to do by always being there. Closing it says "not now", and only the
// next run, or the button, brings it back.
function openLog() {
  if (!state.logMuted && !$('log').open) {
    $('log').showModal();
  }
}

$('log').addEventListener('close', () => { state.logMuted = true; });
$('log-close').addEventListener('click', () => { $('log').close(); });

$('log-toggle').addEventListener('click', () => {
  if ($('log').open) {
    $('log').close();
    return;
  }

  state.logMuted = false;
  openLog();
});

$('clear').addEventListener('click', () => { $('lines').textContent = ''; });

// Reads a text/event-stream answer as it arrives. EventSource cannot do this, because it
// sends a GET only and reconnects when the stream ends; a finished print job must not be
// printed again.
async function stream(url, init) {
  if (state.running) {
    state.running.abort();
  }

  const controller = new AbortController();
  state.running = controller;
  $('stop').classList.remove('hidden');
  state.logMuted = false;
  openLog();
  try {
    const response = await fetch(url, Object.assign({ signal: controller.signal }, init));
    if (!response.ok) {
      log(await response.text(), 'error');
      return;
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';
    for (;;) {
      const { value, done } = await reader.read();
      if (done) { break; }
      buffer += decoder.decode(value, { stream: true });
      let cut;
      while ((cut = buffer.indexOf('\n\n')) >= 0) {
        show(buffer.slice(0, cut));
        buffer = buffer.slice(cut + 2);
      }
    }
  } catch (error) {
    if (error.name !== 'AbortError') {
      log(`The stream stopped: ${error.message}`, 'error');
    }
  } finally {
    state.running = null;
    $('stop').classList.add('hidden');
  }
}

// One event holds one JSON line. A field the server did not send is left out.
function show(event) {
  for (const row of event.split('\n')) {
    if (!row.startsWith('data:')) { continue; }
    try {
      const item = JSON.parse(row.slice(5).trim());
      log(item.text, item.level);
    } catch {
      log(row.slice(5).trim());
    }
  }
}

$('stop').addEventListener('click', () => {
  if (state.running) {
    state.running.abort();
    log('Stopped watching. The printer keeps the job.', 'warning');
  }
});

async function getJson(url) {
  const response = await fetch(url);
  if (!response.ok) {
    throw new Error(await response.text());
  }
  return response.json();
}

// ---------------------------------------------------------------- the printer list

async function loadPrinters(refresh, probe) {
  $('discovery-state').textContent = probe ? 'Probing the local subnet. This takes minutes...' : 'Asking the network...';
  $('refresh').disabled = true;
  $('probe').disabled = true;
  try {
    state.devices = await getJson(`/api/printers?refresh=${refresh}&probe=${probe}`);
    $('discovery-state').textContent = `${state.devices.length} printer(s).`;
    renderPrinters();
  } catch (error) {
    $('discovery-state').textContent = '';
    log(`The discovery failed: ${error.message}`, 'error');
  } finally {
    $('refresh').disabled = false;
    $('probe').disabled = false;
  }
}

function renderPrinters() {
  const list = $('printers');
  list.textContent = '';
  if (state.devices.length === 0) {
    const empty = document.createElement('li');
    empty.className = 'note';
    empty.textContent = 'No printer answered. Press "Probe subnet" to look on TCP port 9100.';
    list.appendChild(empty);
    return;
  }

  for (const device of state.devices) {
    const item = document.createElement('li');
    item.innerHTML = '<div class="name"></div><div class="id"></div><div class="traits"></div>';
    item.querySelector('.name').textContent = device.name || '(no name)';
    item.querySelector('.id').textContent = device.id;
    item.querySelector('.traits').textContent = device.summary;
    item.addEventListener('click', () => select(device));
    if (state.device && state.device.id === device.id) {
      item.className = 'selected';
    }
    list.appendChild(item);
  }
}

function select(device) {
  state.device = device;
  renderPrinters();
  renderChannels();
  renderPrinterTab();
}

function renderChannels() {
  const select = $('channel');
  select.textContent = '';
  if (!state.device) { return; }
  for (const channel of state.device.channels) {
    const option = document.createElement('option');
    option.value = channel.id;
    option.textContent = `${channel.scheme} — ${channel.address} (${channel.summary})`;
    select.appendChild(option);
  }
  onChannelChange();
}

function channel() {
  if (!state.device) { return null; }
  return state.device.channels.find((c) => c.id === $('channel').value) || null;
}

// ---------------------------------------------------------------- the print tab

function onChannelChange() {
  const selected = channel();
  $('channel-note').textContent = selected
    ? `${selected.givesPassthrough ? 'Sends the bytes unchanged' : 'May convert the job'}; ` +
      `${selected.hasJobQueue ? 'the job can be watched' : 'no job queue, so there is no progress to watch'}.` +
      (selected.reads ? ` Reads: ${selected.reads}` : ' It reported no format.')
    : '';
  if (selected && !selected.hasJobQueue) {
    $('raw').checked = true;
  }
  renderOptions();
  checkAccepts();
}

async function loadFiles() {
  state.files = await getJson('/api/files');
  const select = $('file');
  select.textContent = '';
  for (const file of state.files.filter((f) => f.canPrint)) {
    const option = document.createElement('option');
    option.value = file.name;
    option.textContent = `${file.name} — ${file.contentType}`;
    select.appendChild(option);
  }
  checkAccepts();
}

function contentType() {
  const typed = $('content-type').value.trim();
  if (typed) { return typed; }
  if (uploading()) {
    const file = $('upload').files[0];
    return file ? guess(file.name) : '';
  }
  const chosen = state.files.find((f) => f.name === $('file').value);
  return chosen ? chosen.contentType : '';
}

function guess(name) {
  const known = {
    zpl: 'application/vnd.zebra-zpl',
    epl: 'application/vnd.eltron-epl',
    png: 'image/png',
    jpg: 'image/jpeg',
    jpeg: 'image/jpeg',
    pdf: 'application/pdf',
  };
  return known[name.split('.').pop().toLowerCase()] || '';
}

function uploading() {
  return document.querySelector('input[name="source"]:checked').value === 'upload';
}

// Only what the printer reported reaches the form. A printer that reported nothing is not
// asked, because the page would be inventing the choice.
function renderOptions() {
  const host = $('options');
  host.textContent = '';
  const selected = channel();
  const configuration = selected ? selected.configuration : null;
  const raw = $('raw').checked;

  host.appendChild(field('jobName', 'Job name', 'text'));
  host.appendChild(field('copies', 'Copies', 'number'));
  host.appendChild(field('pageRanges', 'Pages (1-3,5)', 'text'));

  if (raw) {
    host.appendChild(note('A raw send carries no job template, so the options below are not sent.'));
    return;
  }

  const type = contentType();
  if (type === 'image/png' || type === 'image/jpeg') {
    host.appendChild(choice('colorMode', 'Colour', ['Color', 'Monochrome']));
  }

  if (!configuration) { return; }
  if (configuration.orientations && configuration.orientations.length) {
    host.appendChild(choice('orientation', 'Rotation', configuration.orientations));
  }
  if (configuration.scalings && configuration.scalings.length) {
    host.appendChild(choice('scaling', 'Scaling', configuration.scalings));
  }
  if (configuration.qualities && configuration.qualities.length) {
    host.appendChild(choice('quality', 'Quality', configuration.qualities));
  }
  if (configuration.media && configuration.media.length) {
    host.appendChild(choice('mediaSize', 'Media', names(configuration.media)));
  }
  if (configuration.mediaSources && configuration.mediaSources.length) {
    host.appendChild(choice('mediaSource', 'Tray', names(configuration.mediaSources)));
  }
  if (configuration.mediaTypes && configuration.mediaTypes.length) {
    host.appendChild(choice('mediaType', 'Media type', configuration.mediaTypes));
  }
  if (configuration.outputBins && configuration.outputBins.length) {
    host.appendChild(choice('outputBin', 'Output bin', configuration.outputBins));
  }
  if (configuration.resolutionsDpi && configuration.resolutionsDpi.length) {
    host.appendChild(choice('resolutionDpi', 'Resolution (dpi)', configuration.resolutionsDpi.map(String)));
  }
  if (configuration.numberUpValues && configuration.numberUpValues.length) {
    host.appendChild(choice('numberUp', 'Pages on one sheet', configuration.numberUpValues.map(String)));
  }
  if (configuration.supportsDuplex) {
    host.appendChild(choice('duplex', 'Duplex', ['Simplex', 'LongEdge', 'ShortEdge']));
  }
}

// The Windows spooler reports "A4 (9)". The number belongs on the screen and not in the job.
function names(values) {
  return values.map((value) => value.replace(/ \(\d+\)$/, ''));
}

function field(name, label, type) {
  const wrapper = document.createElement('label');
  wrapper.textContent = label;
  const input = document.createElement('input');
  input.type = type;
  input.dataset.option = name;
  if (type === 'number') { input.min = '1'; }
  wrapper.appendChild(input);
  return wrapper;
}

function choice(name, label, values) {
  const wrapper = document.createElement('label');
  wrapper.textContent = label;
  const select = document.createElement('select');
  select.dataset.option = name;
  const blank = document.createElement('option');
  blank.value = '';
  blank.textContent = 'Printer default';
  select.appendChild(blank);
  for (const value of values) {
    const option = document.createElement('option');
    option.value = value;
    option.textContent = value;
    select.appendChild(option);
  }
  wrapper.appendChild(select);
  return wrapper;
}

function note(text) {
  const paragraph = document.createElement('p');
  paragraph.className = 'note';
  paragraph.textContent = text;
  return paragraph;
}

function options() {
  const result = {};
  for (const input of $('options').querySelectorAll('[data-option]')) {
    const value = input.value.trim();
    if (!value) { continue; }
    result[input.dataset.option] = input.type === 'number' || ['copies', 'numberUp', 'resolutionDpi'].includes(input.dataset.option)
      ? Number(value)
      : value;
  }
  return result;
}

// The tri-state answer of the library, before any paper is used.
async function checkAccepts() {
  const banner = $('accepts');
  const selected = channel();
  const type = contentType();
  if (!selected || !type) {
    banner.classList.add('hidden');
    return;
  }

  try {
    const answer = await getJson(`/api/printers/accepts?id=${encodeURIComponent(selected.id)}&contentType=${encodeURIComponent(type)}`);
    if (answer.accepts === true) {
      banner.classList.add('hidden');
      return;
    }
    banner.classList.remove('hidden');
    banner.textContent = answer.accepts === false
      ? `This channel does not report ${type}. A raw send prints nothing, or prints the source as text; ` +
        `a queue job may come out blank.${answer.reads ? ` This channel reads: ${answer.reads}` : ''}`
      : `This channel reports no format, so it is not known whether it reads ${type}. That is not a refusal.`;
  } catch {
    banner.classList.add('hidden');
  }
}

$('print').addEventListener('click', async () => {
  const selected = channel();
  if (!selected) {
    log('Select a printer first.', 'warning');
    return;
  }

  const raw = $('raw').checked;
  const upload = uploading() ? $('upload').files[0] : null;
  const what = upload ? upload.name : $('file').value;
  if (!what) {
    log('Select a file first.', 'warning');
    return;
  }

  if (!confirm(`Send '${what}' to ${selected.id}? This uses paper and ink.`)) {
    log('Cancelled; nothing was sent.', 'warning');
    return;
  }

  if (upload) {
    const form = new FormData();
    form.append('file', upload);
    form.append('printerId', selected.id);
    form.append('contentType', contentType());
    form.append('raw', String(raw));
    form.append('options', JSON.stringify(options()));
    await stream('/api/jobs/upload', { method: 'POST', body: form });
    return;
  }

  await stream('/api/jobs', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ printerId: selected.id, file: what, contentType: contentType(), raw, options: options() }),
  });
});

// ---------------------------------------------------------------- the job set tab

async function loadSets() {
  state.sets = await getJson('/api/job-sets');
  const select = $('set');
  select.textContent = '';
  for (const set of state.sets) {
    const option = document.createElement('option');
    option.value = set.name;
    option.textContent = `${set.name} (${set.mode || 'queue'}, ${set.jobs.length} job(s))`;
    select.appendChild(option);
  }
  showSet();
}

function currentSet() {
  return state.uploadedSet || state.sets.find((s) => s.name === $('set').value) || null;
}

function showSet() {
  const set = currentSet();
  $('set-preview').textContent = set ? JSON.stringify(set, null, 2) : 'No job set.';
}

$('set-upload').addEventListener('change', async (event) => {
  const file = event.target.files[0];
  if (!file) {
    state.uploadedSet = null;
    showSet();
    return;
  }

  try {
    state.uploadedSet = JSON.parse(await file.text());
    log(`Read the job set '${file.name}'. It is used instead of the supplied one.`);
  } catch (error) {
    state.uploadedSet = null;
    log(`'${file.name}' is not a job set: ${error.message}`, 'error');
  }
  showSet();
});

$('run-set').addEventListener('click', async () => {
  const selected = channel();
  const set = currentSet();
  if (!selected || !set) {
    log('Select a printer and a job set first.', 'warning');
    return;
  }

  if (!confirm(`Run '${set.name}' — ${set.jobs.length} job(s) — on ${selected.id}? This uses paper and ink.`)) {
    log('Cancelled; nothing was sent.', 'warning');
    return;
  }

  await stream('/api/job-sets/run', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ printerId: selected.id, set }),
  });
});

// ---------------------------------------------------------------- the printer tab

function renderPrinterTab() {
  const device = state.device;
  if (!device) {
    $('printer-details').textContent = 'Select a printer.';
    return;
  }

  const lines = [
    `${device.name}`,
    `  device    ${device.key}`,
    `  id        ${device.id}`,
  ];
  if (device.manufacturer || device.model) { lines.push(`  make      ${[device.manufacturer, device.model].filter(Boolean).join(' ')}`); }
  if (device.serialNumber) { lines.push(`  serial    ${device.serialNumber}`); }
  if (device.location) { lines.push(`  location  ${device.location}`); }
  if (device.contributedBy.length) { lines.push(`  found by  ${device.contributedBy.join(', ')}`); }
  if (device.statusSources.length) { lines.push(`  answers   ${device.statusSources.join(', ')}`); }
  if (device.commandSets.length) { lines.push(`  cmdsets   ${device.commandSets.join(', ')}`); }

  lines.push('  channels');
  for (const c of device.channels) {
    lines.push(`    ${c.scheme.padEnd(7)} ${c.address.padEnd(22)} ${c.summary}`);
    lines.push(`      id      ${c.id}`);
    if (c.reads) { lines.push(`      reads   ${c.reads}`); }
    const configuration = c.configuration;
    if (!configuration) {
      lines.push('      no answer, which is not "no capabilities"');
      continue;
    }
    push(lines, 'formats', configuration.documentFormats);
    push(lines, 'rotation', configuration.orientations);
    push(lines, 'scaling', configuration.scalings);
    push(lines, 'media', configuration.media);
    push(lines, 'trays', configuration.mediaSources);
    push(lines, 'types', configuration.mediaTypes);
    push(lines, 'bins', configuration.outputBins);
    push(lines, 'quality', configuration.qualities);
    push(lines, 'dpi', configuration.resolutionsDpi);
    push(lines, 'n-up', configuration.numberUpValues);
    lines.push(`      duplex  ${tri(configuration.supportsDuplex)}, colour ${tri(configuration.supportsColor)}, ranges ${tri(configuration.supportsPageRanges)}`);
  }

  $('printer-details').textContent = lines.join('\n');
}

// A capability the printer did not report is not a capability it denied.
function tri(value) {
  return value === null || value === undefined ? 'not reported' : value ? 'yes' : 'no';
}

function push(lines, label, values) {
  if (values && values.length) {
    lines.push(`      ${label.padEnd(7)} ${values.join(', ')}`);
  }
}

$('read-status').addEventListener('click', async () => {
  const selected = channel() || (state.device ? { id: state.device.id } : null);
  if (!selected) {
    log('Select a printer first.', 'warning');
    return;
  }

  try {
    const status = await getJson(`/api/printers/status?id=${encodeURIComponent(selected.id)}`);
    const parts = [status.state, status.isAcceptingJobs ? 'accepting jobs' : 'not accepting jobs'];
    if (status.serialNumber) { parts.push(`serial ${status.serialNumber}`); }
    if (status.lifetimePageCount !== null && status.lifetimePageCount !== undefined) { parts.push(`${status.lifetimePageCount} pages`); }
    if (status.detail) { parts.push(status.detail); }
    for (const marker of status.markers) { parts.push(marker); }
    log(`${selected.id}: ${parts.join('; ')}`, 'done');
  } catch (error) {
    log(`No status: ${error.message}`, 'error');
  }
});

// ---------------------------------------------------------------- diagnostics

$('correlate').addEventListener('click', async () => {
  const tracer = $('tracer').checked;
  if (tracer && !confirm('Create a held job that carries no document on each printer with an empty queue, then cancel it?')) {
    log('Cancelled; the queues were only read.', 'warning');
    return;
  }

  await stream('/api/diagnostics/correlate', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ tracer }),
  });
});

$('details').addEventListener('click', async () => {
  const host = $('host').value.trim();
  if (!host) {
    log('Enter a host first.', 'warning');
    return;
  }

  try {
    const answer = await getJson(`/api/diagnostics/details?host=${encodeURIComponent(host)}`);
    log(`IPP  ${answer.host}: ${answer.ipp}`);
    log(`SNMP ${answer.host}: ${answer.snmp}`);
  } catch (error) {
    log(error.message, 'error');
  }
});

$('windows-checks').addEventListener('click', async () => {
  const queue = $('queue').value.trim();
  if (!queue) {
    log('Enter a print queue name first.', 'warning');
    return;
  }

  const print = $('spooler-print').checked;
  if (print && !confirm(`Send a tiny test payload to '${queue}' twice, as two copies?`)) {
    log('Cancelled; nothing was sent.', 'warning');
    return;
  }

  await stream('/api/diagnostics/windows-spooler', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ queue, print }),
  });
});

// ---------------------------------------------------------------- wiring

for (const tab of document.querySelectorAll('.tab')) {
  tab.addEventListener('click', () => {
    for (const other of document.querySelectorAll('.tab')) { other.classList.remove('active'); }
    for (const panel of document.querySelectorAll('.panel')) { panel.classList.add('hidden'); }
    tab.classList.add('active');
    $(`panel-${tab.dataset.tab}`).classList.remove('hidden');
  });
}

$('refresh').addEventListener('click', () => loadPrinters(true, false));
$('probe').addEventListener('click', () => loadPrinters(true, true));
$('channel').addEventListener('change', onChannelChange);
$('file').addEventListener('change', () => { renderOptions(); checkAccepts(); });
$('upload').addEventListener('change', () => { renderOptions(); checkAccepts(); });
$('content-type').addEventListener('change', () => { renderOptions(); checkAccepts(); });
$('raw').addEventListener('change', renderOptions);
$('set').addEventListener('change', () => { state.uploadedSet = null; showSet(); });
for (const radio of document.querySelectorAll('input[name="source"]')) {
  radio.addEventListener('change', () => { renderOptions(); checkAccepts(); });
}

document.querySelector('.tab').click();
renderOptions();
loadFiles().catch((error) => log(error.message, 'error'));
loadSets().catch((error) => log(error.message, 'error'));
loadPrinters(false, false);
