// Converter page: converts between a foreign currency and the bank's currency (VND) at the bank's rates,
// which the page puts in data-converter. Foreign → VND uses the buy rate (transfer or cash);
// VND → foreign uses the sell rate.
(() => {
	const root = document.querySelector('[data-converter]');
	if (!root) return;

	const data = JSON.parse(root.dataset.converter);
	const $ = selector => document.querySelector(selector);
	const fmt = (n, digits) => n.toLocaleString('en-US', { minimumFractionDigits: digits, maximumFractionDigits: digits });
	const format = (template, ...args) => template.replace(/\{(\d+)\}/g, (_, i) => args[+i] ?? '');
	const quote = data.quote;
	const foreignAmounts = [100, 500, 1000, 5000, 10000];
	const quoteAmounts = [1e6, 5e6, 1e7, 5e7, 1e8];

	const state = { currency: data.currency, dir: 'f2v', mode: 'transfer', amount: 1000 };

	const amountInput = $('[data-amount]');
	const currencySelect = $('[data-currency]');
	const modeSeg = $('[data-mode-seg]');

	function rate() {
		const r = data.rates[state.currency];
		if (state.dir === 'v2f') return r.sell;
		return state.mode === 'cash' ? r.cash : r.transfer;
	}

	function render() {
		const r = data.rates[state.currency];
		const f2v = state.dir === 'f2v';
		if (r.cash == null) state.mode = 'transfer';
		const used = rate();

		currencySelect.value = state.currency;
		currencySelect.hidden = !f2v;
		$('[data-quote-in]').hidden = f2v;
		$('[data-to]').textContent = f2v ? quote : state.currency;

		document.querySelectorAll('[data-dir]').forEach(b => {
			const isF2V = b.dataset.dir === 'f2v';
			b.textContent = isF2V ? `${state.currency} → ${quote}` : `${quote} → ${state.currency}`;
			b.setAttribute('aria-pressed', String(isF2V === f2v));
		});

		if (document.activeElement !== amountInput) amountInput.value = fmt(state.amount, f2v ? 0 : 0);
		$('[data-result]').textContent = used == null || used === 0
			? '—'
			: f2v ? fmt(state.amount * used, 0) : fmt(state.amount / used, 2);

		fitResult();

		const which = f2v ? (state.mode === 'cash' ? data.text.buysCash : data.text.buysTransfer) : data.text.sells;
		$('[data-rate-line]').innerHTML = used == null
			? `— · <span class="nowrap">${which}</span>`
			: format(data.text.rateUsed, `<b dir="ltr">1 ${state.currency} = ${fmt(used, 2)} ${quote}</b>`) + ` · <span class="nowrap">${which}</span>`; // the rate's name stays on one line ("ธนาคารรับซื้อ (โอน)")

		modeSeg.hidden = !f2v;
		modeSeg.querySelectorAll('button').forEach(b => {
			b.setAttribute('aria-pressed', String(b.dataset.mode === state.mode));
			b.disabled = b.dataset.mode === 'cash' && r.cash == null;
		});

		$('[data-quick]').innerHTML = (f2v ? foreignAmounts : quoteAmounts)
			.map(v => `<button type="button" data-v="${v}">${fmt(v, 0)}</button>`).join('');

		$('[data-r-cash]').textContent = r.cash == null ? '—' : fmt(r.cash, 2);
		$('[data-r-transfer]').textContent = r.transfer == null ? '—' : fmt(r.transfer, 2);
		$('[data-r-sell]').textContent = r.sell == null ? '—' : fmt(r.sell, 2);

		const link = $('[data-compare-link]');
		link.textContent = format(data.text.compare, state.currency).replace(/ ([→←])/, ' $1'); // the arrow never alone on a line
		link.href = data.compareUrl.replace('__CUR__', encodeURIComponent(state.currency));

		document.querySelectorAll('[data-tile]').forEach(t => t.setAttribute('aria-pressed', String(t.dataset.tile === state.currency)));
		const bankCurrency = $('[data-bank-currency]');
		if (bankCurrency) bankCurrency.value = state.currency;
	}

	function readAmount() {
		const value = parseFloat(amountInput.value.replace(/[^0-9.]/g, ''));
		state.amount = isNaN(value) ? 0 : value;
	}

	function setDirection(dir) {
		state.dir = dir;
		state.amount = dir === 'f2v' ? 1000 : 1e7;
		render();
	}

	amountInput.addEventListener('input', () => { readAmount(); render(); });
	amountInput.addEventListener('change', () => { readAmount(); amountInput.value = fmt(state.amount, 0); render(); });
	currencySelect.addEventListener('change', () => { state.currency = currencySelect.value; render(); });
	document.querySelectorAll('[data-dir]').forEach(b => b.addEventListener('click', () => setDirection(b.dataset.dir)));
	$('[data-swap]').addEventListener('click', () => setDirection(state.dir === 'f2v' ? 'v2f' : 'f2v'));
	modeSeg.addEventListener('click', e => {
		const b = e.target.closest('button');
		if (b && !b.disabled) { state.mode = b.dataset.mode; render(); }
	});
	$('[data-quick]').addEventListener('click', e => {
		const b = e.target.closest('button');
		if (b) { state.amount = +b.dataset.v; amountInput.value = fmt(state.amount, 0); render(); }
	});
	document.querySelectorAll('[data-tile]').forEach(t => t.addEventListener('click', () => {
		state.currency = t.dataset.tile;
		render();
		root.scrollIntoView({ behavior: 'smooth', block: 'start' });
	}));

	// On a phone the currency picker shows the code only ("EUR"); otherwise "EUR · Euro".
	// The result stays on one line: on a narrow phone its font shrinks until the number fits (down to 14px).
	function fitResult() {
		const out = $('[data-result]');
		out.style.fontSize = '';
		let size = parseFloat(getComputedStyle(out).fontSize);
		while (out.scrollWidth > out.clientWidth && size > 14) out.style.fontSize = `${--size}px`;
	}
	window.addEventListener('resize', fitResult);
	document.fonts?.ready.then(fitResult); // the number font may load after the first render

	const narrow = window.matchMedia('(max-width: 640px)');
	function labelCurrencies() {
		currencySelect.querySelectorAll('option').forEach(o => {
			o.textContent = narrow.matches ? o.value : `${o.value} · ${o.dataset.name}`;
		});
	}
	narrow.addEventListener('change', labelCurrencies);
	labelCurrencies();

	render();
})();
