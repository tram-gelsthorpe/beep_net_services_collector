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
