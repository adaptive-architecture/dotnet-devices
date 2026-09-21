'use strict';

// State the page keeps: what the last discovery found, what the person selected, and the
// working copy of the job set that the form edits.
const state = {
  devices: [],
  device: null,
  engines: [],
  files: [],
  sets: [],
  set: null,
  job: 0,
  running: null,
  entries: [],
  unread: 0,
};

const $ = (id) => document.getElementById(id);

// ---------------------------------------------------------------- the log rail

function log(text, level) {
  const entry = { text, level: level || 'info', at: new Date() };
  state.entries.push(entry);
  if (passes(entry)) {
    $('lines').appendChild(line(entry));
    follow();
  }

  if (document.body.classList.contains('log-hidden')) {
    state.unread += 1;
    $('log-unread').textContent = String(state.unread);
    $('log-unread').classList.remove('hidden');
  }
}

function passes(entry) {
  const filter = $('log-filter').value;
  if (filter === 'error') { return entry.level === 'error'; }
  if (filter === 'warning') { return entry.level === 'error' || entry.level === 'warning'; }
  return true;
}

function line(entry) {
  const element = document.createElement('span');
  element.className = entry.level;
  if ($('log-time').checked) {
    const time = document.createElement('span');
    time.className = 'time';
    time.textContent = `${entry.at.toTimeString().slice(0, 8)} `;
    element.appendChild(time);
  }

  element.appendChild(document.createTextNode(`${entry.text}\n`));
  return element;
}

function follow() {
  if ($('log-follow').checked) {
    $('lines').scrollTop = $('lines').scrollHeight;
  }
}

function renderLog() {
  const lines = $('lines');
  lines.textContent = '';
  for (const entry of state.entries.filter(passes)) {
    lines.appendChild(line(entry));
  }
  follow();
}

// The text of the log as a file holds it: what "Copy" puts on the clipboard and what
// "Download" writes. The filter applies, because what is read is what is wanted.
function logText() {
  return state.entries.filter(passes)
    .map((entry) => ($('log-time').checked ? `${entry.at.toTimeString().slice(0, 8)} ${entry.text}` : entry.text))
    .join('\n');
}

function showLog(show) {
  document.body.classList.toggle('log-hidden', !show);
  if (show) {
    state.unread = 0;
    $('log-unread').classList.add('hidden');
    follow();
  }
}

$('log-toggle').addEventListener('click', () => showLog(document.body.classList.contains('log-hidden')));
$('log-hide').addEventListener('click', () => showLog(false));
$('log-clear').addEventListener('click', () => { state.entries = []; renderLog(); });
$('log-filter').addEventListener('change', renderLog);
$('log-time').addEventListener('change', renderLog);

$('log-copy').addEventListener('click', async () => {
  try {
    await navigator.clipboard.writeText(logText());
    $('log-copy').textContent = 'Copied';
    setTimeout(() => { $('log-copy').textContent = 'Copy'; }, 1500);
  } catch {
    // A browser that refuses the clipboard still allows a selection of the whole log.
    const range = document.createRange();
    range.selectNodeContents($('lines'));
    const selection = window.getSelection();
    selection.removeAllRanges();
    selection.addRange(range);
    $('log-copy').textContent = 'Selected — press Ctrl+C';
    setTimeout(() => { $('log-copy').textContent = 'Copy'; }, 2500);
  }
});

$('log-download').addEventListener('click', () => {
  const stamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
  download(new Blob([logText()], { type: 'text/plain' }), `printer-manager-${stamp}.log`);
});

function download(blob, name) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = name;
  link.click();
  URL.revokeObjectURL(url);
}

// ---------------------------------------------------------------- what an action reports

function box(button, name) {
  const holder = button.closest('.act');
  return holder ? holder.querySelector(name) : null;
}

function status(button, text, level) {
  const element = box(button, '.status');
  if (element) {
    element.textContent = text || '';
    element.className = `status${level ? ` ${level}` : ''}`;
  }
}

// Says it twice on purpose: the line goes to the log, and the short form stays beside the
// button, so an action is never silent when the log is hidden.
function report(button, text, level) {
  log(text, level);
  status(button, text, level);
}

async function busy(button, work) {
  button.disabled = true;
  button.dataset.busy = 'true';
  status(button, 'Working...');
  try {
    await work();
  } finally {
    delete button.dataset.busy;
    button.disabled = false;
    updateScopes();
  }
}

// A button that sends to the selected channel says so while nothing is selected, instead
// of failing into the log when it is pressed.
function updateScopes() {
  const ready = channel() !== null;
  for (const button of document.querySelectorAll('[data-scope="channel"]')) {
    if (button.dataset.busy !== 'true') {
      button.disabled = !ready;
    }

    const hint = box(button, '.hint');
    if (hint) {
      hint.textContent = ready ? '' : 'Select a printer in the list on the left.';
    }
  }
}

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
  showLog(true);
  try {
    const response = await fetch(url, Object.assign({ signal: controller.signal }, init));
    if (!response.ok) {
      log(await response.text(), 'error');
      return false;
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

    return true;
  } catch (error) {
    if (error.name !== 'AbortError') {
      log(`The stream stopped: ${error.message}`, 'error');
    }

    return false;
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
    log(`Discovery found ${state.devices.length} printer(s).`);
    const previous = state.device;
    state.device = previous ? state.devices.find((d) => d.id === previous.id) || null : null;
    renderPrinters();
  } catch (error) {
    $('discovery-state').textContent = 'The discovery failed.';
    log(`The discovery failed: ${error.message}`, 'error');
  } finally {
    $('refresh').disabled = false;
    $('probe').disabled = false;
  }
}

// The printer is picked where the channel is picked, because one owns the other. Nothing
// is selected until a person selects it: the first entry is a prompt and not a device.
function renderPrinters() {
  const select = $('printer');
  select.textContent = '';
  const prompt = document.createElement('option');
  prompt.value = '';
  prompt.textContent = state.devices.length
    ? `Select one of ${state.devices.length} printer(s)`
    : 'No printer answered. Press "Probe subnet".';
  select.appendChild(prompt);

  for (const device of state.devices) {
    const option = document.createElement('option');
    option.value = device.id;
    option.textContent = `${device.name || '(no name)'} — ${device.summary}`;
    select.appendChild(option);
  }

  select.value = state.device ? state.device.id : '';
  renderChannels();
}

function onPrinterChange() {
  state.device = state.devices.find((d) => d.id === $('printer').value) || null;
  renderChannels();
}

function renderChannels() {
  const select = $('channel');
  select.textContent = '';
  if (!state.device) {
    const empty = document.createElement('option');
    empty.value = '';
    empty.textContent = 'No printer selected';
    select.appendChild(empty);
    $('printer-note').textContent = '';
    onChannelChange();
    renderPrinterTab();
    return;
  }

  $('printer-note').textContent = `${state.device.id}${state.device.location ? ` — ${state.device.location}` : ''}`;
  for (const channel of state.device.channels) {
    const option = document.createElement('option');
    option.value = channel.id;
    option.textContent = `${channel.scheme} — ${channel.address} (${channel.summary})`;
    select.appendChild(option);
  }

  onChannelChange();
  renderPrinterTab();
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
    : 'Every action on the Print, Job sets and Printer tabs sends to this channel.';
  if (selected && !selected.hasJobQueue) {
    $('raw').checked = true;
  }

  updateScopes();
  renderPrintOptions();
  renderJobs();
  checkAccepts();
}

// Which engines can render a PDF. There is no fixed answer: it depends on the packages this
// build referenced, so the page asks rather than assuming, and offers nothing where only one
// engine is registered.
async function loadEngines() {
  state.engines = await getJson('/api/pdf-engines');
  renderPrintOptions();
  renderJobs();
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

  renderJobs();
  checkAccepts();
}

function contentType() {
  const typed = $('content-type').value.trim();
  if (typed) { return typed; }
  if (uploading()) {
    const file = $('upload').files[0];
    return file ? guess(file.name) : '';
  }
  return typeOfFile($('file').value);
}

function typeOfFile(name) {
  const chosen = state.files.find((f) => f.name === name);
  return chosen ? chosen.contentType : guess(name || '');
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
// asked, because the page would be inventing the choice. The same builder fills the print
// tab and every job row of a set, so a job is edited with the controls it prints with.
function renderOptions(host, raw, type, values) {
  host.textContent = '';
  const configuration = channel() ? channel().configuration : null;

  host.appendChild(field('jobName', 'Job name', 'text', values));
  host.appendChild(field('copies', 'Copies', 'number', values));
  host.appendChild(field('pageRanges', 'Pages (1-3,5)', 'text', values));

  if (raw) {
    host.appendChild(note('A raw send carries no job template, so the options below are not sent.'));
    keep(host, values);
    return;
  }

  // Not a channel capability: it names the converter in this process, so it is offered
  // whatever the printer reported, and only where there is a choice to make.
  if (type === 'application/pdf' && state.engines.length > 1) {
    const names = state.engines.map((engine) => engine.name);
    const fallback = state.engines.find((engine) => engine.isDefault);
    host.appendChild(choice('converter', 'PDF engine', names, values, `${fallback ? fallback.name : names[0]} (default)`));
  }

  // An image converter reads the colour mode whatever the printer said, and a printer that
  // reported colour reads it for every other format the converter renders.
  if (type === 'image/png' || type === 'image/jpeg' || (configuration && configuration.supportsColor)) {
    host.appendChild(choice('colorMode', 'Colour', ['Color', 'Monochrome'], values));
  }

  if (!configuration) {
    keep(host, values);
    return;
  }

  if (configuration.orientations && configuration.orientations.length) {
    host.appendChild(choice('orientation', 'Rotation', configuration.orientations, values));
  }
  if (configuration.scalings && configuration.scalings.length) {
    host.appendChild(choice('scaling', 'Scaling', configuration.scalings, values));
  }
  if (configuration.qualities && configuration.qualities.length) {
    host.appendChild(choice('quality', 'Quality', configuration.qualities, values));
  }
  if (configuration.media && configuration.media.length) {
    host.appendChild(choice('mediaSize', 'Media', names(configuration.media), values));
  }
  if (configuration.mediaSources && configuration.mediaSources.length) {
    host.appendChild(choice('mediaSource', 'Tray', names(configuration.mediaSources), values));
  }
  if (configuration.mediaTypes && configuration.mediaTypes.length) {
    host.appendChild(choice('mediaType', 'Media type', configuration.mediaTypes, values));
  }
  if (configuration.outputBins && configuration.outputBins.length) {
    host.appendChild(choice('outputBin', 'Output bin', configuration.outputBins, values));
  }
  if (configuration.resolutionsDpi && configuration.resolutionsDpi.length) {
    host.appendChild(choice('resolutionDpi', 'Resolution (dpi)', configuration.resolutionsDpi.map(String), values));
  }
  if (configuration.numberUpValues && configuration.numberUpValues.length) {
    host.appendChild(choice('numberUp', 'Pages on one sheet', configuration.numberUpValues.map(String), values));
  }
  if (configuration.supportsDuplex) {
    host.appendChild(choice('duplex', 'Duplex', ['Simplex', 'LongEdge', 'ShortEdge'], values));
  }

  placement(host, type, values);
  keep(host, values);
}

// Geometry, not a channel capability: no printer advertises a label offset, so these are
// offered for every format the process can render and applied while the page becomes pixels.
// The anchor is what the offsets are measured from, which is why it comes first.
function placement(host, type, values) {
  if (type !== 'application/pdf' && type !== 'image/png' && type !== 'image/jpeg') {
    return;
  }

  host.appendChild(note('Placement is applied while the page is rendered, so a job that sets it is converted even where the printer reads the file itself.'));
  host.appendChild(choice('anchor', 'Anchor', [
    'TopLeft', 'TopCenter', 'TopRight',
    'CenterLeft', 'Center', 'CenterRight',
    'BottomLeft', 'BottomCenter', 'BottomRight',
  ], values, 'Centred'));
  host.appendChild(field('offsetXMillimeters', 'Offset right (mm)', 'number', values, { step: 'any' }));
  host.appendChild(field('offsetYMillimeters', 'Offset down (mm)', 'number', values, { step: 'any' }));
  host.appendChild(choice('smoothing', 'Smoothing', [
    { value: 'true', label: 'On' },
    { value: 'false', label: 'Off, for a sharp barcode' },
  ], values, 'Engine default'));

  if (type === 'application/pdf') {
    host.appendChild(choice('mediaSizeSource', 'Media size from', ['Printer', 'Document'], values, 'Printer'));
  }

  host.appendChild(field('mediaWidthMillimeters', 'Media width (mm)', 'number', values, { step: 'any' }));
  host.appendChild(field('mediaHeightMillimeters', 'Media height (mm)', 'number', values, { step: 'any' }));

  // The two rectangles differ by the strip the printer cannot mark, which is nothing on
  // label stock and a few millimetres on office paper.
  host.appendChild(choice('fitArea', 'Fit to', [
    { value: 'Printable', label: 'What the printer can mark' },
    { value: 'Physical', label: 'The whole sheet' },
  ], values, 'What the printer can mark'));

  if (type === 'application/pdf') {
    host.appendChild(field('documentPassword', 'Document password', 'password', values));
  }
}

// A value a job carries that this channel never offered is still shown, so editing a set on
// the wrong printer, on a channel that reported nothing, or in raw mode does not drop what
// the set asked for: what is on the form is what the run sends.
function keep(host, values) {
  for (const [name, value] of Object.entries(values || {})) {
    if (!host.querySelector(`[data-option="${name}"]`)) {
      host.appendChild(kept(name, value));
    }
  }
}

// The print tab keeps what was typed when the form is rebuilt.
function renderPrintOptions() {
  const host = $('options');
  const keep = host.childElementCount ? readOptions(host) : {};
  renderOptions(host, $('raw').checked, contentType(), keep);
}

// The Windows spooler reports "A4 (9)". The number belongs on the screen and not in the job.
function names(values) {
  return values.map((value) => value.replace(/ \(\d+\)$/, ''));
}

function field(name, label, type, values, attributes) {
  const wrapper = document.createElement('label');
  wrapper.textContent = label;
  const input = document.createElement('input');
  input.type = type;
  input.dataset.option = name;
  // An offset is signed and a fraction of a millimetre matters, so those fields say so
  // rather than taking the whole-number minimum every other count here has.
  if (type === 'number' && !attributes) { input.min = '1'; }
  for (const [key, value] of Object.entries(attributes || {})) { input.setAttribute(key, value); }
  if (values && values[name] !== undefined && values[name] !== null) { input.value = values[name]; }
  wrapper.appendChild(input);
  return wrapper;
}

function choice(name, label, options, values, blankLabel) {
  const wrapper = document.createElement('label');
  wrapper.textContent = label;
  const select = document.createElement('select');
  select.dataset.option = name;
  const blank = document.createElement('option');
  blank.value = '';
  blank.textContent = blankLabel || 'Printer default';
  select.appendChild(blank);
  for (const entry of options) {
    const option = document.createElement('option');
    // An entry is the value itself, or a value with a label to show instead of it.
    option.value = entry.value === undefined ? entry : entry.value;
    option.textContent = entry.label === undefined ? option.value : entry.label;
    select.appendChild(option);
  }

  const held = values ? values[name] : undefined;
  if (held !== undefined && held !== null && held !== '') {
    const offered = options.map((entry) => String(entry.value === undefined ? entry : entry.value));
    if (!offered.includes(String(held))) {
      const extra = document.createElement('option');
      extra.value = held;
      extra.textContent = `${held} (not reported by this channel)`;
      select.appendChild(extra);
    }

    select.value = String(held);
  }

  wrapper.appendChild(select);
  return wrapper;
}

function kept(name, value) {
  const wrapper = document.createElement('label');
  wrapper.textContent = `${name} (kept from the set)`;
  const input = document.createElement('input');
  input.type = 'text';
  input.dataset.option = name;
  input.value = value;
  wrapper.appendChild(input);
  return wrapper;
}

function note(text) {
  const paragraph = document.createElement('p');
  paragraph.className = 'note';
  paragraph.textContent = text;
  return paragraph;
}

const numbers = ['copies', 'numberUp', 'resolutionDpi'];

// The smoothing switch is a select, so it reads back as text and has to be sent as the
// boolean the job carries.
const booleans = ['smoothing'];

// Everything a job set may hold on disk. The document password is deliberately absent: it
// opens one document on one run, and a set is a file people mail to each other.
const secrets = ['documentPassword'];

function readOptions(host) {
  const result = {};
  for (const input of host.querySelectorAll('[data-option]')) {
    const value = input.value.trim();
    if (!value) { continue; }
    if (booleans.includes(input.dataset.option)) {
      result[input.dataset.option] = value === 'true';
      continue;
    }

    result[input.dataset.option] = input.type === 'number' || numbers.includes(input.dataset.option)
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
  const button = $('print');
  const selected = channel();
  const raw = $('raw').checked;
  const upload = uploading() ? $('upload').files[0] : null;
  const what = upload ? upload.name : $('file').value;
  if (!what) {
    report(button, 'Select a file first.', 'warning');
    return;
  }

  if (!confirm(`Send '${what}' to ${selected.id}? This uses paper and ink.`)) {
    report(button, 'Cancelled; nothing was sent.', 'warning');
    return;
  }

  await busy(button, async () => {
    const sent = upload
      ? await sendUpload(selected, upload, raw)
      : await stream('/api/jobs', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ printerId: selected.id, file: what, contentType: contentType(), raw, options: readOptions($('options')) }),
      });
    status(button, sent ? `Sent '${what}'. The log has the detail.` : 'The run ended early; read the log.', sent ? 'done' : 'error');
  });
});

function sendUpload(selected, upload, raw) {
  const form = new FormData();
  form.append('file', upload);
  form.append('printerId', selected.id);
  form.append('contentType', contentType());
  form.append('raw', String(raw));
  form.append('options', JSON.stringify(readOptions($('options'))));
  return stream('/api/jobs/upload', { method: 'POST', body: form });
}

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

  loadSet(shippedSet());
}

function shippedSet() {
  return state.sets.find((s) => s.name === $('set').value) || state.sets[0] || { name: 'New job set', mode: 'queue', jobs: [] };
}

// The form edits a copy. The file on disk, and the set the server listed, stay as they are.
function loadSet(set) {
  state.set = JSON.parse(JSON.stringify(set || { name: 'New job set', mode: 'queue', jobs: [] }));
  state.set.jobs = state.set.jobs || [];
  state.job = 0;
  $('set-name').value = state.set.name || '';
  $('set-description').value = state.set.description || '';
  $('set-mode').value = state.set.mode || 'queue';
  renderJobs();
}

// One job at a time. A set of nine jobs is a strip of nine tabs, and the job being edited
// gets the width its option grid needs.
function renderJobs() {
  if (!state.set) { return; }
  const jobs = state.set.jobs;
  state.job = Math.min(Math.max(state.job, 0), Math.max(jobs.length - 1, 0));

  const strip = $('job-tabs');
  strip.textContent = '';
  jobs.forEach((job, index) => {
    const tab = button(jobLabel(job, index), () => { state.job = index; renderJobs(); });
    tab.className = `job-tab${index === state.job ? ' active' : ''}`;
    tab.title = job.description || '';
    strip.appendChild(tab);
  });

  const add = button('+ Add a job', () => {
    // No file: the sample ships a deliberately broken PDF, so a guessed default would be a
    // job nobody asked for. The field offers every shipped name.
    jobs.push({ file: '', options: {} });
    state.job = jobs.length - 1;
    renderJobs();
  });
  add.className = 'job-tab add';
  strip.appendChild(add);

  const host = $('jobs');
  host.textContent = '';
  host.appendChild(jobs.length
    ? jobRow(jobs[state.job], state.job)
    : note('This set holds no job yet. Press "Add a job".'));
  showSetJson();
}

function jobLabel(job, index) {
  return `${index + 1} · ${job.file || 'no file'}`;
}

function jobRow(job, index) {
  const row = document.createElement('article');
  row.className = 'job';

  const head = document.createElement('div');
  head.className = 'job-head';
  const number = document.createElement('span');
  number.className = 'job-number';
  number.textContent = `Job ${index + 1} of ${state.set.jobs.length}`;
  const spacer = document.createElement('span');
  spacer.className = 'spacer';
  head.append(number, spacer, button('Duplicate', () => {
    state.set.jobs.splice(index + 1, 0, JSON.parse(JSON.stringify(job)));
    state.job = index + 1;
    renderJobs();
  }), button('Remove', () => {
    state.set.jobs.splice(index, 1);
    state.job = index - 1;
    renderJobs();
  }));
  row.appendChild(head);

  const top = document.createElement('div');
  top.className = 'row';
  top.append(
    fileChoice(job, row),
    text('Content type', job.contentType || '', '', (value) => { job.contentType = value || undefined; onJobFileChange(job, row); }, 'from the file name'));
  row.appendChild(top);
  row.appendChild(text('Description', job.description || '', 'grow', (value) => { job.description = value || undefined; showSetJson(); }));

  const options = document.createElement('div');
  options.className = 'options';
  job.options = job.options || {};
  renderOptions(options, $('set-mode').value === 'raw', jobType(job), job.options);
  options.addEventListener('change', () => { job.options = readOptions(options); showSetJson(); });
  options.addEventListener('input', () => { job.options = readOptions(options); showSetJson(); });
  row.appendChild(options);
  return row;
}

function jobType(job) {
  return job.contentType || typeOfFile(job.file || '');
}

// A different file reads a different format, so the options it can carry change with it.
function onJobFileChange(job, row) {
  const options = row.querySelector('.options');
  renderOptions(options, $('set-mode').value === 'raw', jobType(job), job.options);
  const tab = $('job-tabs').children[state.job];
  if (tab) { tab.textContent = jobLabel(job, state.job); }
  showSetJson();
}

// The server reads the file from PrintFiles/, so the job picks one of those and does not
// type a name. A set that arrived naming something else keeps that name, and says so.
function fileChoice(job, row) {
  const wrapper = document.createElement('label');
  wrapper.className = 'grow';
  wrapper.textContent = 'File';
  const select = document.createElement('select');
  const printable = state.files.filter((f) => f.canPrint);
  if (!job.file || !printable.some((f) => f.name === job.file)) {
    const other = document.createElement('option');
    other.value = job.file || '';
    other.textContent = job.file ? `${job.file} — not in PrintFiles/` : 'Choose a file';
    select.appendChild(other);
  }

  for (const file of printable) {
    const option = document.createElement('option');
    option.value = file.name;
    option.textContent = `${file.name} — ${file.contentType}`;
    select.appendChild(option);
  }

  select.value = job.file || '';
  select.addEventListener('change', () => { job.file = select.value; onJobFileChange(job, row); });
  wrapper.appendChild(select);
  return wrapper;
}

function text(label, value, className, onChange, placeholder) {
  const wrapper = document.createElement('label');
  wrapper.textContent = label;
  if (className) { wrapper.className = className; }
  const input = document.createElement('input');
  input.type = 'text';
  input.value = value;
  if (placeholder) { input.placeholder = placeholder; }
  input.addEventListener('input', () => onChange(input.value.trim()));
  wrapper.appendChild(input);
  return wrapper;
}

function button(label, onClick) {
  const element = document.createElement('button');
  element.type = 'button';
  element.textContent = label;
  element.addEventListener('click', onClick);
  return element;
}

// What the form holds, in the shape PrintJobs/*.json holds. An empty field is left out, so
// the JSON reads like a set a person would write.
function setPayload() {
  return {
    name: $('set-name').value.trim() || undefined,
    description: $('set-description').value.trim() || undefined,
    mode: $('set-mode').value,
    jobs: state.set.jobs.map((job) => ({
      file: job.file || undefined,
      description: job.description || undefined,
      contentType: job.contentType || undefined,
      options: job.options && Object.keys(job.options).length ? job.options : undefined,
    })),
  };
}

// What the set looks like as a file: the same payload the run sends, minus the secrets. A
// job set is a file people mail to each other, and a document password has no business
// travelling in one.
function savedSetPayload() {
  const payload = setPayload();
  for (const job of payload.jobs) {
    if (!job.options) { continue; }
    const kept = { ...job.options };
    for (const name of secrets) { delete kept[name]; }
    job.options = Object.keys(kept).length ? kept : undefined;
  }
  return payload;
}

function showSetJson() {
  $('set-json').textContent = JSON.stringify(savedSetPayload(), null, 2);
}

$('set').addEventListener('change', () => loadSet(shippedSet()));
$('set-name').addEventListener('input', showSetJson);
$('set-description').addEventListener('input', showSetJson);
$('set-mode').addEventListener('change', renderJobs);
$('reset-set').addEventListener('click', () => {
  loadSet(shippedSet());
  log(`Reset to the shipped set '${$('set').value}'.`);
});
$('download-set').addEventListener('click', () => {
  const name = ($('set-name').value.trim() || 'job-set').toLowerCase().replace(/[^a-z0-9]+/g, '-');
  download(new Blob([JSON.stringify(savedSetPayload(), null, 2)], { type: 'application/json' }), `${name}.json`);
});
$('copy-set').addEventListener('click', async () => {
  try {
    await navigator.clipboard.writeText(JSON.stringify(savedSetPayload(), null, 2));
    $('copy-set').textContent = 'Copied';
    setTimeout(() => { $('copy-set').textContent = 'Copy the JSON'; }, 1500);
  } catch (error) {
    log(`The clipboard refused the JSON: ${error.message}`, 'warning');
  }
});

$('set-upload').addEventListener('change', async (event) => {
  const file = event.target.files[0];
  if (!file) { return; }

  try {
    loadSet(JSON.parse(await file.text()));
    log(`Read the job set '${file.name}'. The form holds it now.`);
  } catch (error) {
    log(`'${file.name}' is not a job set: ${error.message}`, 'error');
  }
});

$('run-set').addEventListener('click', async () => {
  const button = $('run-set');
  const selected = channel();
  const set = setPayload();
  if (!set.jobs.length) {
    report(button, 'The set holds no job.', 'warning');
    return;
  }

  const nameless = set.jobs.findIndex((job) => !job.file);
  if (nameless >= 0) {
    report(button, `Job ${nameless + 1} names no file.`, 'warning');
    return;
  }

  if (!confirm(`Run '${set.name || 'the job set'}' — ${set.jobs.length} job(s) — on ${selected.id}? This uses paper and ink.`)) {
    report(button, 'Cancelled; nothing was sent.', 'warning');
    return;
  }

  await busy(button, async () => {
    const ran = await stream('/api/job-sets/run', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ printerId: selected.id, set }),
    });
    status(button, ran ? 'The set ran. The log holds the outcome of each job.' : 'The run ended early; read the log.', ran ? 'done' : 'error');
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
  const button = $('read-status');
  const selected = channel();
  await busy(button, async () => {
    try {
      const reading = await getJson(`/api/printers/status?id=${encodeURIComponent(selected.id)}`);
      const parts = [reading.state, reading.isAcceptingJobs ? 'accepting jobs' : 'not accepting jobs'];
      if (reading.serialNumber) { parts.push(`serial ${reading.serialNumber}`); }
      if (reading.lifetimePageCount !== null && reading.lifetimePageCount !== undefined) { parts.push(`${reading.lifetimePageCount} pages`); }
      if (reading.detail) { parts.push(reading.detail); }
      for (const marker of reading.markers) { parts.push(marker); }
      log(`${selected.id}: ${parts.join('; ')}`, 'done');
      status(button, parts.join('; '), 'done');
    } catch (error) {
      report(button, `No status: ${error.message}`, 'error');
    }
  });
});

// ---------------------------------------------------------------- diagnostics

$('correlate').addEventListener('click', async () => {
  const button = $('correlate');
  const tracer = $('tracer').checked;
  if (tracer && !confirm('Create a held job that carries no document on each printer with an empty queue, then cancel it?')) {
    report(button, 'Cancelled; the queues were only read.', 'warning');
    return;
  }

  await busy(button, async () => {
    const ran = await stream('/api/diagnostics/correlate', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ tracer }),
    });
    status(button, ran ? 'Compared. The log holds what each queue answered.' : 'It ended early; read the log.', ran ? 'done' : 'error');
  });
});

$('details').addEventListener('click', async () => {
  const button = $('details');
  const host = $('host').value.trim();
  if (!host) {
    report(button, 'Enter a host first.', 'warning');
    return;
  }

  await busy(button, async () => {
    try {
      const answer = await getJson(`/api/diagnostics/details?host=${encodeURIComponent(host)}`);
      log(`IPP  ${answer.host}: ${answer.ipp}`);
      log(`SNMP ${answer.host}: ${answer.snmp}`);
      status(button, `${answer.host} answered. The log holds both readings.`, 'done');
    } catch (error) {
      report(button, error.message, 'error');
    }
  });
});

$('windows-checks').addEventListener('click', async () => {
  const button = $('windows-checks');
  const queue = $('queue').value.trim();
  if (!queue) {
    report(button, 'Enter a print queue name first.', 'warning');
    return;
  }

  const print = $('spooler-print').checked;
  if (print && !confirm(`Send a tiny test payload to '${queue}' twice, as two copies?`)) {
    report(button, 'Cancelled; nothing was sent.', 'warning');
    return;
  }

  await busy(button, async () => {
    const ran = await stream('/api/diagnostics/windows-spooler', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ queue, print }),
    });
    status(button, ran ? 'Gathered. The log holds the evidence.' : 'It ended early; read the log.', ran ? 'done' : 'error');
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
$('printer').addEventListener('change', onPrinterChange);
$('channel').addEventListener('change', onChannelChange);
$('file').addEventListener('change', () => { renderPrintOptions(); checkAccepts(); });
$('upload').addEventListener('change', () => { renderPrintOptions(); checkAccepts(); });
$('content-type').addEventListener('change', () => { renderPrintOptions(); checkAccepts(); });
$('raw').addEventListener('change', renderPrintOptions);
for (const radio of document.querySelectorAll('input[name="source"]')) {
  radio.addEventListener('change', () => { renderPrintOptions(); checkAccepts(); });
}

document.querySelector('.tab').click();
updateScopes();
onChannelChange();
loadEngines().catch((error) => log(error.message, 'error'));
loadFiles().catch((error) => log(error.message, 'error'));
loadSets().catch((error) => log(error.message, 'error'));
loadPrinters(false, false);
