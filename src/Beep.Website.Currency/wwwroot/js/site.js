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
