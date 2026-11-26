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

// Date fields (input[data-datepicker]): a calendar pop-up styled like the dropdown lists and as wide as its field,
// instead of the browser's own. The input becomes hidden and keeps the value the server reads (yyyy-mm-dd); a button
// shows it as dd/mm/yyyy, or data-empty ("Latest") when there is none. Days after today can't be chosen.
document.querySelectorAll('input[data-datepicker]').forEach(input => {
	const field = input.closest('.field');
	const text = input.dataset;
	const lang = document.documentElement.lang || 'en';
	const pad = n => String(n).padStart(2, '0');
	const iso = date => `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
	const parse = value => {
		const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value || '');
		return m ? new Date(+m[1], +m[2] - 1, +m[3]) : null;
	};
	const addDays = (date, n) => new Date(date.getFullYear(), date.getMonth(), date.getDate() + n);
	const today = new Date();
	today.setHours(0, 0, 0, 0);

	let firstDay = 1;
	try {
		const locale = new Intl.Locale(lang);
		const week = locale.getWeekInfo?.() ?? locale.weekInfo;
		if (week) firstDay = week.firstDay % 7;
	} catch { }
	const monthTitle = new Intl.DateTimeFormat(lang, { month: 'long', year: 'numeric' });
	const weekdayShort = new Intl.DateTimeFormat(lang, { weekday: 'short' });
	const weekdayNarrow = new Intl.DateTimeFormat(lang, { weekday: 'narrow' });
	const fullDate = new Intl.DateTimeFormat(lang, { dateStyle: 'full' });

	const el = (tag, className, props = {}) => Object.assign(document.createElement(tag), { className }, props);
	const icon = (path) => {
		const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
		svg.setAttribute('viewBox', '0 0 24 24');
		svg.setAttribute('aria-hidden', 'true');
		svg.innerHTML = path;
		return svg;
	};

	input.type = 'hidden';
	const trigger = el('button', 'date-trigger', { type: 'button' });
	trigger.setAttribute('aria-haspopup', 'dialog');
	trigger.setAttribute('aria-expanded', 'false');
	const value = el('span', 'date-value');
	trigger.append(value, icon('<rect x="3.5" y="5" width="17" height="15.5" rx="2.5" fill="none" stroke="currentColor" stroke-width="1.8"/><path d="M3.5 10h17M8 3v4M16 3v4" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/>'));
	input.after(trigger);

	const pop = el('div', 'date-pop', { hidden: true });
	pop.setAttribute('role', 'dialog');
	pop.setAttribute('aria-label', field.querySelector('span')?.textContent ?? '');
	field.append(pop);

	let view; // the first day of the month shown

	function showValue() {
		const date = parse(input.value);
		value.textContent = date ? `${pad(date.getDate())}/${pad(date.getMonth() + 1)}/${date.getFullYear()}` : text.empty;
		trigger.classList.toggle('empty', !date);
	}

	function render(focus) {
		const chosen = parse(input.value);
		const year = view.getFullYear(), month = view.getMonth();
		const first = new Date(year, month, 1);
		const start = addDays(first, -((first.getDay() - firstDay + 7) % 7));
		const weeks = Math.ceil(((first.getDay() - firstDay + 7) % 7 + new Date(year, month + 1, 0).getDate()) / 7);
		const compact = field.offsetWidth < 300;
		pop.classList.toggle('compact', compact);
		const weekdayName = compact ? weekdayNarrow : weekdayShort;
		pop.replaceChildren();

		const head = el('div', 'date-head');
		const prev = el('button', 'date-nav', { type: 'button', title: text.prev, onclick: () => move(-1) });
		prev.setAttribute('aria-label', text.prev);
		prev.append(icon('<path d="M15 6l-6 6 6 6" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>'));
		const next = el('button', 'date-nav', { type: 'button', title: text.next, onclick: () => move(1) });
		next.setAttribute('aria-label', text.next);
		next.append(icon('<path d="M9 6l6 6-6 6" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>'));
		next.disabled = year > today.getFullYear() || (year === today.getFullYear() && month >= today.getMonth());
		const title = monthTitle.format(first);
		const heading = el('div', 'date-title', { textContent: title.charAt(0).toLocaleUpperCase(lang) + title.slice(1) });
		heading.setAttribute('aria-live', 'polite');
		head.append(prev, heading, next);

		const grid = el('div', 'date-grid');
		grid.setAttribute('role', 'grid');
		for (let i = 0; i < 7; i++)
			grid.append(el('span', 'date-wd', { textContent: weekdayName.format(addDays(start, i)) }));
		const target = focus && focus.getMonth() === month ? focus : chosen && chosen.getMonth() === month && chosen.getFullYear() === year ? chosen
			: today.getMonth() === month && today.getFullYear() === year ? today : first;
		for (let i = 0; i < weeks * 7; i++) {
			const date = addDays(start, i);
			const day = el('button', 'date-day', { type: 'button', textContent: date.getDate() });
			day.dataset.date = iso(date);
			day.setAttribute('aria-label', fullDate.format(date));
			day.tabIndex = +date === +target ? 0 : -1;
			if (date.getMonth() !== month) day.classList.add('out');
			if (+date === +today) day.classList.add('today');
			if (chosen && +date === +chosen) day.setAttribute('aria-selected', 'true');
			day.disabled = date > today;
			grid.append(day);
		}

		const foot = el('div', 'date-foot');
		foot.append(
			el('button', '', { type: 'button', textContent: text.today, onclick: () => choose(today) }),
			el('button', '', { type: 'button', textContent: text.clear, onclick: () => choose(null) }));
		pop.append(head, grid, foot);
		if (focus !== undefined) grid.querySelector('.date-day[tabindex="0"]')?.focus();
	}

	function move(months, focus) {
		view = new Date(view.getFullYear(), view.getMonth() + months, 1);
		render(focus ?? null);
	}

	function setOpen(open) {
		if (open === !pop.hidden) return;
		pop.hidden = !open;
		trigger.setAttribute('aria-expanded', String(open));
		if (open) {
			const date = parse(input.value) ?? today;
			view = new Date(date.getFullYear(), date.getMonth(), 1);
			render(date);
		}
	}

	function choose(date) {
		const changed = input.value !== (date ? iso(date) : '');
		input.value = date ? iso(date) : '';
		showValue();
		setOpen(false);
		trigger.focus();
		if (changed) input.dispatchEvent(new Event('change', { bubbles: true }));
	}

	trigger.addEventListener('click', () => setOpen(pop.hidden));
	// The pop-up is inside the field's <label>: a click on it must not reach the label, which would click the button.
	pop.addEventListener('click', e => {
		e.preventDefault();
		const day = e.target.closest('.date-day');
		if (day && !day.disabled) choose(parse(day.dataset.date));
	});
	pop.addEventListener('keydown', e => {
		if (e.key === 'Escape') { e.preventDefault(); setOpen(false); trigger.focus(); return; }
		const day = e.target.closest('.date-day');
		if (!day) return;
		const date = parse(day.dataset.date);
		const steps = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 };
		let to;
		if (e.key in steps) to = addDays(date, steps[e.key]);
		else if (e.key === 'Home') to = addDays(date, -((date.getDay() - firstDay + 7) % 7));
		else if (e.key === 'End') to = addDays(date, 6 - ((date.getDay() - firstDay + 7) % 7));
		else if (e.key === 'PageUp' || e.key === 'PageDown') to = new Date(date.getFullYear(), date.getMonth() + (e.key === 'PageUp' ? -1 : 1), date.getDate());
		else return;
		e.preventDefault();
		if (to > today) to = today;
		view = new Date(to.getFullYear(), to.getMonth(), 1);
		render(to);
	});
	// composedPath: a click on the month arrows re-renders the pop-up, so its target is no longer in the page.
	document.addEventListener('click', e => { if (!e.composedPath().includes(field)) setOpen(false); });
	field.addEventListener('focusout', e => { if (!pop.hidden && e.relatedTarget && !field.contains(e.relatedTarget)) setOpen(false); });

	showValue();
});
