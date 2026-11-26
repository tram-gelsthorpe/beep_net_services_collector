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

// Language menu: the button opens and closes it; a click outside or Escape closes it.
document.querySelectorAll('[data-lang]').forEach(lang => {
	const button = lang.querySelector('.lang-btn');
	const setOpen = open => {
		lang.classList.toggle('open', open);
		button.setAttribute('aria-expanded', String(open));
	};

	button.addEventListener('click', () => {
		const open = !lang.classList.contains('open');
		setOpen(open);
		if (open) lang.querySelector('.lang-menu a[aria-current="true"], .lang-menu a')?.focus();
	});
	document.addEventListener('click', e => { if (!lang.contains(e.target)) setOpen(false); });
	lang.addEventListener('keydown', e => {
		if (e.key === 'Escape' && lang.classList.contains('open')) {
			setOpen(false);
			button.focus();
		}
	});
});
