// "Filter currencies" box of each bank card: hides the rows whose code or name doesn't contain the text.
document.querySelectorAll('[data-rates]').forEach(section => {
	const input = section.querySelector('[data-filter]');
	const rows = [...section.querySelectorAll('tbody tr[data-search]')];
	const noMatch = section.querySelector('tbody tr.no-match');
	if (!input) return;

	input.addEventListener('input', () => {
		const text = input.value.trim().toLowerCase();
		let shown = 0;
		rows.forEach(row => {
			const match = !text || row.dataset.search.includes(text);
			row.hidden = !match;
			if (match) shown++;
		});
		if (noMatch) noMatch.hidden = shown > 0;
	});
});

// Header pop-ups: the language menu and, on mobile, the page menu. One is open at a time; a click outside,
// the backdrop or Escape closes it. On mobile the page dims behind the language menu.
(() => {
	const lang = document.querySelector('[data-lang]');
	const langButton = lang?.querySelector('.lang-btn');
	const menuButton = document.querySelector('[data-menu]');
	const nav = document.getElementById('main-nav');
	const backdrop = document.querySelector('[data-backdrop]');

	function setLang(open) {
		if (!lang) return;
		lang.classList.toggle('open', open);
		document.body.classList.toggle('lang-open', open);
		langButton.setAttribute('aria-expanded', String(open));
	}

	function setMenu(open) {
		if (!menuButton) return;
		nav.classList.toggle('open', open);
		menuButton.setAttribute('aria-expanded', String(open));
	}

	langButton?.addEventListener('click', e => {
		e.stopPropagation();
		const open = !lang.classList.contains('open');
		setMenu(false);
		setLang(open);
		if (open) lang.querySelector('.lang-menu a[aria-current="true"], .lang-menu a')?.focus();
	});

	menuButton?.addEventListener('click', e => {
		e.stopPropagation();
		const open = !nav.classList.contains('open');
		setLang(false);
		setMenu(open);
	});

	backdrop?.addEventListener('click', () => setLang(false));
	document.addEventListener('click', e => {
		if (lang && !lang.contains(e.target)) setLang(false);
		if (nav && !nav.contains(e.target)) setMenu(false);
	});
	document.addEventListener('keydown', e => {
		if (e.key !== 'Escape') return;
		if (lang?.classList.contains('open')) { setLang(false); langButton.focus(); }
		if (nav?.classList.contains('open')) { setMenu(false); menuButton.focus(); }
	});
})();

// Compare banks: Countries multi-select (a checklist dropdown as wide as its field). One country compares
// prices in its currency; two or more compare by spread. Done (or Compare) submits the form.
(() => {
	const ms = document.querySelector('[data-ms]');
	if (!ms) return;

	const trigger = ms.querySelector('.ms-trigger');
	const panel = ms.querySelector('.ms-panel');
	const search = ms.querySelector('[data-ms-search]');
	const all = ms.querySelector('[data-ms-all]');
	const items = [...ms.querySelectorAll('[data-ms-item]')];
	const boxes = items.map(i => i.querySelector('input'));
	const format = (template, ...args) => template.replace(/\{(\d+)\}/g, (_, i) => args[+i] ?? '');
	const t = ms.dataset;

	function render() {
		const chosen = items.filter((_, i) => boxes[i].checked).map(i => i.dataset.msItem);
		all.checked = chosen.length === boxes.length;
		all.indeterminate = chosen.length > 0 && chosen.length < boxes.length;
		ms.querySelector('[data-ms-label]').textContent = !chosen.length ? t.textChoose
			: chosen.length === boxes.length && chosen.length > 1 ? t.textAll
			: chosen.length <= 2 ? chosen.join(', ') : format(t.textMore, chosen[0], chosen.length - 1);
		ms.querySelector('[data-ms-count]').textContent = chosen.length;
		ms.querySelector('[data-ms-hint]').textContent = !chosen.length ? t.textNone
			: chosen.length === 1 ? format(t.textOne, chosen[0]) : format(t.textMany, chosen.length);
		ms.querySelector('.btn-done').disabled = !chosen.length;
	}

	function setOpen(open) {
		panel.hidden = !open;
		trigger.setAttribute('aria-expanded', String(open));
		if (open) search.focus();
	}

	trigger.addEventListener('click', () => setOpen(panel.hidden));
	document.addEventListener('click', e => { if (!ms.contains(e.target)) setOpen(false); });
	document.addEventListener('keydown', e => { if (e.key === 'Escape' && !panel.hidden) { setOpen(false); trigger.focus(); } });
	search.addEventListener('input', () => {
		const q = search.value.toLowerCase();
		let shown = 0;
		items.forEach(i => { i.hidden = !i.dataset.msItem.toLowerCase().includes(q); shown += i.hidden ? 0 : 1; });
		ms.querySelector('[data-ms-empty]').hidden = shown > 0;
	});
	boxes.forEach(b => b.addEventListener('change', render));
	all.addEventListener('change', () => { boxes.forEach(b => { b.checked = all.checked; }); render(); });
	render();
})();
