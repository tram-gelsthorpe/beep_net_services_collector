// "Get rate alerts" dialog (Pages/Shared/_Subscribe.cshtml): desktop = centred dialog, mobile (≤640px) = sheet
// from the bottom. Countries, banks and currencies come from /alerts/options; the form posts to /alerts/subscribe.
(() => {
	const root = document.querySelector('[data-subscribe]');
	if (!root) return;

	const text = JSON.parse(root.dataset.text);
	const format = (template, ...args) => template.replace(/\{(\d+)\}/g, (_, i) => args[+i] ?? '');
	const esc = value => String(value).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
	const $ = selector => root.querySelector(selector);

	const form = $('[data-sub-form]');
	const done = $('[data-sub-done]');
	const email = $('[data-sub-email]');
	const countrySelect = $('[data-sub-country]');

	// banks: [{ key: BIC, label }]; currencies: [{ key: code, label, name }]
	const S = {
		countries: [], country: null, loaded: false,
		lists: {
			banks: { items: [], selected: new Set(), query: '' },
			currencies: { items: [], selected: new Set(), query: '' },
		},
	};

	async function load(country) {
		const url = root.dataset.optionsUrl + (country ? `?country=${encodeURIComponent(country)}` : '');
		const response = await fetch(url, { headers: { Accept: 'application/json' } });
		if (!response.ok) throw new Error(`options ${response.status}`);
		const data = await response.json();
		S.countries = data.countries;
		S.country = data.country;
		S.lists.banks.items = data.banks.map(b => ({ key: b.bic, label: b.name }));
		S.lists.currencies.items = data.currencies.map(c => ({ key: c.code, label: c.code, name: c.name }));
		// A new country: no bank chosen yet; currencies keep what is still offered (all, the first time).
		S.lists.banks.selected = new Set(S.lists.banks.items.length === 1 ? [S.lists.banks.items[0].key] : []);
		const offered = new Set(S.lists.currencies.items.map(c => c.key));
		const kept = [...S.lists.currencies.selected].filter(c => offered.has(c));
		S.lists.currencies.selected = new Set(S.loaded && kept.length ? kept : offered);
		S.loaded = true;
		countrySelect.innerHTML = S.countries.map(c => `<option value="${esc(c.code)}">${esc(c.name)}</option>`).join('');
		countrySelect.value = S.country ?? '';
		render();
	}

	function renderList(id) {
		const list = S.lists[id];
		const q = list.query.toLowerCase();
		const shown = list.items.filter(it => `${it.label} ${it.name ?? ''}`.toLowerCase().includes(q));
		$(`[data-list="${id}"]`).innerHTML = shown.length
			? shown.map(it => `<label class="cl-item"><input type="checkbox" value="${esc(it.key)}" ${list.selected.has(it.key) ? 'checked' : ''}><span>${it.name
				? `<b class="num">${esc(it.label)}</b> <span class="muted">${esc(it.name)}</span>` : esc(it.label)}</span></label>`).join('')
			: `<div class="cl-empty">${esc(text.noMatches)}</div>`;
		const all = $(`[data-all="${id}"]`);
		all.checked = list.items.length > 0 && list.selected.size === list.items.length;
		all.indeterminate = list.selected.size > 0 && list.selected.size < list.items.length;
		$(`[data-count="${id}"]`).textContent = format(text.selected, list.selected.size, list.items.length);
	}

	function render() {
		renderList('banks');
		renderList('currencies');
	}

	for (const id of ['banks', 'currencies']) {
		const list = S.lists[id];
		$(`[data-search="${id}"]`).addEventListener('input', e => { list.query = e.target.value; renderList(id); });
		$(`[data-list="${id}"]`).addEventListener('change', e => {
			e.target.checked ? list.selected.add(e.target.value) : list.selected.delete(e.target.value);
			renderList(id);
		});
		$(`[data-all="${id}"]`).addEventListener('change', e => {
			list.selected = new Set(e.target.checked ? list.items.map(it => it.key) : []);
			renderList(id);
		});
	}

	countrySelect.addEventListener('change', () => {
		for (const id of ['banks', 'currencies']) { S.lists[id].query = ''; $(`[data-search="${id}"]`).value = ''; }
		load(countrySelect.value).catch(showServerError);
	});

	function showErrors(fields) {
		root.querySelectorAll('[data-err]').forEach(el => {
			const on = fields.includes(el.dataset.err);
			el.hidden = !on;
			if (on) el.textContent = text[el.dataset.err] ?? text.server;
		});
	}

	function showServerError() { showErrors(['server']); }

	function summary(updated) {
		const names = S.lists.banks.items.filter(b => S.lists.banks.selected.has(b.key)).map(b => b.label);
		const banks = names.length <= 4 ? names.join(', ') : format(text.moreBanks, names.slice(0, 3).join(', '), names.length - 3);
		const currencies = S.lists.currencies.selected.size === S.lists.currencies.items.length
			? text.allCurrencies : [...S.lists.currencies.selected].sort().join(', ');
		const country = S.countries.find(c => c.code === S.country)?.name ?? '';
		return format(esc(updated ? text.updated : text.summary),
			`<b>${esc(email.value.trim())}</b>`, `<b>${esc(banks)}</b>`, esc(country), esc(currencies));
	}

	form.addEventListener('submit', async e => {
		e.preventDefault();
		const value = email.value.trim();
		const missing = [];
		if (!/^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/.test(value)) missing.push('email');
		if (!S.lists.banks.selected.size) missing.push('banks');
		if (!S.lists.currencies.selected.size) missing.push('currencies');
		showErrors(missing);
		if (missing.length) return;

		const submit = form.querySelector('[type=submit]');
		submit.disabled = true;
		try {
			const response = await fetch(root.dataset.subscribeUrl, {
				method: 'POST',
				headers: { 'Content-Type': 'application/json', Accept: 'application/json', RequestVerificationToken: root.dataset.token },
				body: JSON.stringify({
					email: value, country: S.country,
					banks: [...S.lists.banks.selected], currencies: [...S.lists.currencies.selected],
				}),
			});
			const body = await response.json().catch(() => ({}));
			if (!response.ok) { showErrors(body.errors?.length ? body.errors : ['server']); return; }
			const updated = body.status === 'updated';
			$('[data-sub-summary]').innerHTML = summary(updated);
			$('[data-sub-confirm-note]').hidden = updated;
			form.hidden = true;
			done.hidden = false;
		} catch {
			showServerError();
		} finally {
			submit.disabled = false;
		}
	});

	$('[data-sub-edit]').addEventListener('click', () => { form.hidden = false; done.hidden = true; });

	let lastFocus = null;
	function open() {
		lastFocus = document.activeElement;
		root.hidden = false;
		document.body.classList.add('sub-open');
		showErrors([]);
		if (!S.loaded) load(null).catch(showServerError);
		setTimeout(() => email.focus(), 0);
	}
	function close() {
		root.hidden = true;
		document.body.classList.remove('sub-open');
		form.hidden = false;
		done.hidden = true;
		lastFocus?.focus();
	}

	document.querySelectorAll('[data-open-subscribe]').forEach(b => b.addEventListener('click', open));
	root.addEventListener('click', e => { if (e.target === root || e.target.closest('[data-close-subscribe]')) close(); });
	document.addEventListener('keydown', e => { if (e.key === 'Escape' && !root.hidden) close(); });
	if (location.hash === '#subscribe') open();
})();
