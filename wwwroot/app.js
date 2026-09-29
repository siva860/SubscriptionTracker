// Change this to your currency code (USD, EUR, INR, GBP...).
const CURRENCY = 'USD';

const money = n => new Intl.NumberFormat(undefined, { style: 'currency', currency: CURRENCY }).format(n);
const $ = s => document.querySelector(s);
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

async function api(path, method = 'GET', body) {
  const res = await fetch('/api' + path, {
    method,
    headers: body ? { 'Content-Type': 'application/json' } : {},
    body: body ? JSON.stringify(body) : undefined
  });
  if (!res.ok) {
    const p = await res.json().catch(() => ({}));
    throw new Error(Object.values(p.errors ?? {}).flat().join(' ') || p.title || 'Request failed');
  }
  return res.status === 204 ? null : res.json();
}

let subs = [], an = null, editingId = null, allCategories = [];

function toast(msg) {
  const t = $('#toast'); t.textContent = msg; t.classList.add('show');
  clearTimeout(toast.h); toast.h = setTimeout(() => t.classList.remove('show'), 2200);
}

async function load() {
  const p = new URLSearchParams({ status: $('#fstatus').value });
  if ($('#q').value.trim()) p.set('q', $('#q').value.trim());
  if ($('#fcat').value) p.set('category', $('#fcat').value);
  [subs, an] = await Promise.all([api('/subscriptions?' + p), api('/analysis')]);
  render();
}

const days = n => n === 0 ? 'today' : n === 1 ? 'tomorrow' : `in ${n} days`;

function render() {
  // Header summary
  const waste = an.potentialMonthlySavings;
  const kept = Math.max(an.monthlyTotal - an.overlapMonthly - an.idleMonthly, 0);
  const pct = v => an.monthlyTotal ? v / an.monthlyTotal * 100 : 0;
  $('#headline').textContent = waste > 0
    ? `${money(waste)} a month is going to subscriptions you don't need.`
    : 'No wasted subscriptions found.';
  $('#sub').textContent = `You spend ${money(an.monthlyTotal)} a month (${money(an.annualTotal)} a year). Cutting the flagged ones saves ${money(waste * 12)} a year.`;
  $('#leak').innerHTML =
    `<span style="width:${pct(kept)}%;background:var(--ok)"></span>` +
    `<span style="width:${pct(an.overlapMonthly)}%;background:var(--overlap)"></span>` +
    `<span style="width:${pct(an.idleMonthly)}%;background:var(--idle)"></span>`;
  $('#lk').textContent = money(kept); $('#lo').textContent = money(an.overlapMonthly); $('#li').textContent = money(an.idleMonthly);

  // Dashboard
  const max = Math.max(...an.byCategory.map(c => c.monthly), 1);
  $('#cats').innerHTML = an.byCategory.length ? an.byCategory.map(c => `
    <div class="catrow"><div class="top"><span>${esc(c.category)} <span class="muted">(${c.count})</span></span><b>${money(c.monthly)}</b></div>
    <div class="track"><div class="fill" style="width:${c.monthly / max * 100}%"></div></div></div>`).join('')
    : '<p class="muted">Add a subscription to see spend by category.</p>';
  $('#renewals').innerHTML = an.renewals.length ? '<ul class="plain">' + an.renewals.map(r =>
    `<li><span>${esc(r.name)}<br><span class="muted">${esc(r.date)} · ${days(r.daysAway)}</span></span><b>${money(r.cost)}</b></li>`).join('') + '</ul>'
    : '<p class="muted">Nothing renews in the next 14 days.</p>';

  // Subscriptions table
  $('#rows').innerHTML = subs.length ? subs.map(s => {
    const tags = !s.active ? '<span class="tag off">Cancelled</span>'
      : s.flags.length ? s.flags.map(f => `<span class="tag ${f}">${f}</span>`).join('') : '<span class="tag ok">Fine</span>';
    const acts = s.active
      ? `<button class="link" data-act="use" data-id="${s.id}">Log use</button><button class="link" data-act="edit" data-id="${s.id}">Edit</button><button class="link danger" data-act="cancel" data-id="${s.id}">Cancel plan</button>`
      : `<button class="link" data-act="reactivate" data-id="${s.id}">Reactivate</button><button class="link danger" data-act="delete" data-id="${s.id}">Delete</button>`;
    return `<tr class="${s.active ? '' : 'gone'}">
      <td class="sub">${esc(s.name)}<small>${esc(s.category)}${s.cycle !== 'Monthly' ? ` · ${money(s.cost)} ${s.cycle.toLowerCase()}` : ''}${s.notes ? ` · ${esc(s.notes)}` : ''}</small></td>
      <td class="num">${money(s.monthlyCost)}</td><td class="num">${s.usesLast30}</td>
      <td>${s.lastUsed ? esc(s.lastUsed) : '<span class="muted">Never</span>'}</td>
      <td>${s.active ? esc(s.nextRenewal) : '<span class="muted">-</span>'}</td>
      <td>${tags}</td><td>${acts}</td></tr>`;
  }).join('') : '<tr><td colspan="7">No subscriptions match. Add one or change the filters.</td></tr>';

  // Savings view
  $('#overlaps').innerHTML = an.overlaps.length ? an.overlaps.map(o => {
    const keep = o.subscriptions.find(s => s.id === o.keepId);
    return `<h3 style="margin:14px 0 4px;font-size:1rem">${esc(o.category)} <span class="muted">· save ${money(o.monthlySavings)}/mo</span></h3>
      <ul class="plain"><li><span>${esc(keep.name)}<br><span class="muted">Keep · used ${keep.usesLast30}× in 30 days</span></span><b>${money(keep.monthlyCost)}</b></li>` +
      o.subscriptions.filter(s => s.id !== o.keepId).map(s =>
        `<li><span>${esc(s.name)}<br><span class="muted">Used ${s.usesLast30}× in 30 days</span></span><span><b>${money(s.monthlyCost)}</b> <button class="link danger" data-act="cancel" data-id="${s.id}">Cancel plan</button></span></li>`).join('') + '</ul>';
  }).join('') : '<p class="muted">No two active subscriptions share a category.</p>';

  $('#idle').innerHTML = an.idle.length ? '<ul class="plain">' + an.idle.map(i =>
    `<li><span>${esc(i.subscription.name)}<br><span class="muted">${esc(i.reason)}</span></span><span><b>${money(i.subscription.monthlyCost)}</b> <button class="link danger" data-act="cancel" data-id="${i.subscription.id}">Cancel plan</button></span></li>`).join('') + '</ul>'
    : '<p class="muted">Everything active is being used.</p>';
}

async function refreshCategories() {
  const all = await api('/subscriptions?status=all');
  allCategories = [...new Set(all.map(s => s.category))].sort();
  const cur = $('#fcat').value;
  $('#fcat').innerHTML = '<option value="">All categories</option>' + allCategories.map(c => `<option>${esc(c)}</option>`).join('');
  $('#fcat').value = allCategories.includes(cur) ? cur : '';
  $('#catlist').innerHTML = allCategories.map(c => `<option value="${esc(c)}">`).join('');
}

const reload = async () => { await refreshCategories(); await load(); };

// ----- Tabs -----
document.querySelectorAll('nav [role=tab]').forEach(b => b.onclick = () => {
  document.querySelectorAll('nav [role=tab]').forEach(x => x.setAttribute('aria-selected', x === b));
  document.querySelectorAll('.view').forEach(v => v.hidden = v.id !== b.dataset.tab);
});

// ----- Filters -----
let t; $('#q').oninput = () => { clearTimeout(t); t = setTimeout(load, 250); };
$('#fcat').onchange = load; $('#fstatus').onchange = load;

// ----- Form -----
function openForm(s) {
  editingId = s?.id ?? null;
  const f = $('#form'); $('#err').textContent = '';
  $('#dtitle').textContent = s ? 'Edit subscription' : 'Add subscription';
  f.name.value = s?.name ?? ''; f.category.value = s?.category ?? '';
  f.cost.value = s?.cost ?? ''; f.cycle.value = s?.cycle ?? 'Monthly';
  f.nextRenewal.value = s?.nextRenewal ?? new Date(Date.now() + 30 * 864e5).toISOString().slice(0, 10);
  f.notes.value = s?.notes ?? '';
  $('#dlg').showModal(); f.name.focus();
}
$('#add').onclick = () => openForm();
$('#cancel').onclick = () => $('#dlg').close();
$('#form').onsubmit = async e => {
  e.preventDefault();
  const f = e.target;
  const body = { name: f.name.value, category: f.category.value, cost: +f.cost.value, cycle: f.cycle.value,
    nextRenewal: f.nextRenewal.value, notes: f.notes.value || null };
  try {
    await api(editingId ? `/subscriptions/${editingId}` : '/subscriptions', editingId ? 'PUT' : 'POST', body);
    $('#dlg').close(); toast(editingId ? 'Saved' : 'Added'); reload();
  } catch (err) { $('#err').textContent = err.message; }
};

// ----- Row and list actions -----
document.addEventListener('click', async e => {
  const b = e.target.closest('[data-act]'); if (!b) return;
  const id = +b.dataset.id, act = b.dataset.act;
  try {
    if (act === 'edit') return openForm(subs.find(s => s.id === id));
    if (act === 'delete' && !confirm('Delete this subscription and its usage history?')) return;
    if (act === 'cancel' && !confirm('Mark this plan as cancelled? You can reactivate it later.')) return;
    if (act === 'delete') await api(`/subscriptions/${id}`, 'DELETE');
    else await api(`/subscriptions/${id}/${act === 'use' ? 'usage' : act}`, 'POST');
    toast({ use: 'Use logged', cancel: 'Cancelled', reactivate: 'Reactivated', delete: 'Deleted' }[act]);
    reload();
  } catch (err) { toast(err.message); }
});

reload();
