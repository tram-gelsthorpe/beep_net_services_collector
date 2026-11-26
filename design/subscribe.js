// "Get rate alerts" dialog: desktop = centered modal, mobile (≤640px) = bottom sheet.
// Sample data only — load countries/banks/currencies from the real data source and POST the form to your backend.
(function(){
  const COUNTRIES = [
    { id:'VN', name:'Vietnam', banks:['Vietcombank','BIDV','VietinBank','Agribank','Techcombank','ACB','MB Bank','VPBank','Sacombank','HDBank','TPBank','VIB','SHB','SeABank','OCB','MSB','Eximbank','LPBank','Nam A Bank','Bac A Bank','ABBANK','PVcomBank','Kienlongbank','Saigonbank','BaoViet Bank','VietBank','NCB','PGBank','VietABank','GPBank'] },
    { id:'TH', name:'Thailand', banks:['[Bank 1]','[Bank 2]','[Bank 3]'] },
    { id:'SG', name:'Singapore', banks:['[Bank 1]','[Bank 2]','[Bank 3]'] },
    { id:'MY', name:'Malaysia', banks:['[Bank 1]','[Bank 2]','[Bank 3]'] }
  ];
  const CCY = [['USD','US Dollar'],['EUR','Euro'],['GBP','British Pound'],['JPY','Japanese Yen'],['AUD','Australian Dollar'],['CAD','Canadian Dollar'],['CHF','Swiss Franc'],['CNY','Chinese Yuan'],['HKD','Hong Kong Dollar'],['INR','Indian Rupee'],['DKK','Danish Krone']];
  const S = { country:'VN', banks:new Set(['Vietcombank','BIDV','Techcombank']), ccys:new Set(CCY.map(c=>c[0])), bq:'', cq:'' };
  const SEARCH = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#667085" stroke-width="2" stroke-linecap="round" aria-hidden="true"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg>';
  const BELL = '<svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="#C2410C" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M6 8a6 6 0 1 1 12 0c0 7 3 9 3 9H3s3-2 3-9"/><path d="M10.3 21a1.94 1.94 0 0 0 3.4 0"/></svg>';
  const checklist = (id, label, ph, allText) => `
    <div class="sub-field">
      <span class="sub-label">${label} <span class="sub-count" id="${id}Count"></span></span>
      <div class="checklist" role="group" aria-label="${label}">
        <label class="cl-search">${SEARCH}<input type="text" id="${id}Q" placeholder="${ph}" aria-label="${ph}"></label>
        <label class="cl-all"><input type="checkbox" id="${id}All"><span>${allText}</span></label>
        <div class="cl-list" id="${id}List"></div>
      </div>
      <span class="sub-err" id="${id}Err" hidden></span>
    </div>`;
  const root = document.createElement('div');
  root.className = 'sub-overlay'; root.hidden = true;
  root.innerHTML = `
  <section class="sub-dialog" role="dialog" aria-modal="true" aria-labelledby="subTitle">
    <span class="sub-handle" aria-hidden="true"></span>
    <form id="subForm" novalidate>
      <div class="sub-head">
        <span class="sub-icon">${BELL}</span>
        <div><h2 id="subTitle">Get rate alerts</h2><p>We’ll email you when your banks publish new rates.</p></div>
        <button type="button" class="sub-close" data-close-subscribe aria-label="Close"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#475467" stroke-width="2" stroke-linecap="round"><path d="M6 6l12 12M18 6 6 18"/></svg></button>
      </div>
      <label class="sub-field"><span class="sub-label">Email address</span><input type="email" id="subEmail" class="sub-input" placeholder="you@example.com" autocomplete="email"><span class="sub-err" id="emailErr" hidden>Enter a valid email address.</span></label>
      <label class="sub-field"><span class="sub-label">Country</span><select id="subCountry" class="sub-input sub-select">${COUNTRIES.map(c=>`<option value="${c.id}">${c.name}</option>`).join('')}</select></label>
      ${checklist('bank','Banks','Search banks','Select all banks')}
      ${checklist('ccy','Currencies','Search currencies','All currencies')}
      <button type="submit" class="btn btn-primary sub-submit">Subscribe to alerts</button>
      <p class="sub-note">One email per rate update. Unsubscribe any time from any email.</p>
    </form>
    <div class="sub-done" id="subDone" hidden>
      <span class="sub-done-icon"><svg width="30" height="30" viewBox="0 0 24 24" fill="none" stroke="#2547D0" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="m5 12 5 5 9-10"/></svg></span>
      <h2>You’re subscribed</h2>
      <p id="subSummary"></p>
      <p class="sub-note">Check your inbox to confirm your email address.</p>
      <div class="sub-done-actions"><button type="button" class="btn btn-secondary" id="subEdit">Edit alerts</button><button type="button" class="btn btn-primary" data-close-subscribe>Done</button></div>
    </div>
  </section>`;
  document.body.appendChild(root);
  const $ = id => document.getElementById(id);
  const country = () => COUNTRIES.find(c => c.id === S.country);
  function renderList(id, items, set, q, labelFn){
    const shown = items.filter(it => labelFn(it).toLowerCase().includes(q.toLowerCase()));
    $(id+'List').innerHTML = shown.length ? shown.map(it => {
      const key = Array.isArray(it) ? it[0] : it;
      return `<label class="cl-item"><input type="checkbox" value="${key}" ${set.has(key)?'checked':''}><span>${Array.isArray(it) ? `<b class="num">${it[0]}</b> <span class="muted">${it[1]}</span>` : it}</span></label>`;
    }).join('') : '<div class="cl-empty">No matches.</div>';
    const all = $(id+'All'); all.checked = set.size === items.length; all.indeterminate = set.size > 0 && set.size < items.length;
    $(id+'Count').textContent = `· ${set.size} of ${items.length} selected`;
  }
  function render(){
    renderList('bank', country().banks, S.banks, S.bq, b => b);
    renderList('ccy', CCY, S.ccys, S.cq, c => c.join(' '));
  }
  function bindList(id, items, set, qKey, keyFn){
    $(id+'Q').addEventListener('input', e => { S[qKey] = e.target.value; render(); });
    $(id+'List').addEventListener('change', e => { const v = e.target.value; e.target.checked ? set.add(v) : set.delete(v); render(); });
    $(id+'All').addEventListener('change', e => { set.clear(); if (e.target.checked) items().forEach(it => set.add(keyFn(it))); render(); });
  }
  bindList('bank', () => country().banks, S.banks, 'bq', b => b);
  bindList('ccy', () => CCY, S.ccys, 'cq', c => c[0]);
  $('subCountry').addEventListener('change', e => { S.country = e.target.value; S.banks.clear(); S.bq = ''; $('bankQ').value = ''; render(); });
  $('subForm').addEventListener('submit', e => {
    e.preventDefault();
    const email = $('subEmail').value.trim();
    const okEmail = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email);
    $('emailErr').hidden = okEmail;
    $('bankErr').hidden = S.banks.size > 0; $('bankErr').textContent = 'Choose at least one bank.';
    $('ccyErr').hidden = S.ccys.size > 0; $('ccyErr').textContent = 'Choose at least one currency.';
    if (!okEmail || !S.banks.size || !S.ccys.size) return;
    // TODO: POST { email, country: S.country, banks:[...S.banks], currencies:[...S.ccys] } to the backend.
    const banks = [...S.banks]; const b = banks.length <= 4 ? banks.join(', ') : `${banks.slice(0,3).join(', ')} and ${banks.length-3} more banks`;
    const c = S.ccys.size === CCY.length ? 'all currencies' : [...S.ccys].join(', ');
    $('subSummary').innerHTML = `We’ll email <b>${email}</b> when <b>${b}</b> in ${country().name} publish new rates for ${c}.`;
    $('subForm').hidden = true; $('subDone').hidden = false;
  });
  $('subEdit').onclick = () => { $('subForm').hidden = false; $('subDone').hidden = true; };
  let lastFocus = null;
  function open(){ lastFocus = document.activeElement; root.hidden = false; document.body.classList.add('sub-open'); render(); setTimeout(() => $('subEmail').focus(), 0); }
  function close(){ root.hidden = true; document.body.classList.remove('sub-open'); $('subForm').hidden = false; $('subDone').hidden = true; if (lastFocus) lastFocus.focus(); }
  document.querySelectorAll('[data-open-subscribe]').forEach(b => b.addEventListener('click', open));
  root.addEventListener('click', e => { if (e.target === root || e.target.closest('[data-close-subscribe]')) close(); });
  document.addEventListener('keydown', e => { if (e.key === 'Escape' && !root.hidden) close(); });
  if (location.hash === '#subscribe') open();
})();
