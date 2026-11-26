// Shared language dropdown for all pages.
(function(){
  const LANGS = [['EN','English'],['VI','Tiếng Việt'],['TH','ไทย'],['ZH','中文'],['JA','日本語'],['KO','한국어'],['FR','Français']];
  const lang = document.getElementById('lang'); if(!lang) return;
  const btn = document.getElementById('langBtn'), menu = document.getElementById('langMenu'), label = document.getElementById('langLabel');
  const backdrop = document.createElement('div'); backdrop.className = 'lang-backdrop'; document.body.appendChild(backdrop);
  let current = localStorage.getItem('beep-lang') || 'EN';
  function setOpen(open){ lang.classList.toggle('open', open); document.body.classList.toggle('lang-open', open); btn.setAttribute('aria-expanded', open); }
  function render(){
    const found = LANGS.find(l => l[0] === current) || LANGS[0];
    label.textContent = window.matchMedia('(max-width:640px)').matches ? found[0] : found[1];
    menu.innerHTML = '<li class="lang-title">Language</li>';
    LANGS.forEach(([code,name])=>{
      const li = document.createElement('li'), b = document.createElement('button');
      b.type = 'button'; b.setAttribute('role','option'); b.setAttribute('aria-selected', code === current);
      b.innerHTML = `<span class="code">${code}</span><span class="name">${name}</span>`;
      b.onclick = () => { current = code; try{ localStorage.setItem('beep-lang', code); }catch(e){} setOpen(false); render(); };
      li.appendChild(b); menu.appendChild(li);
    });
  }
  render();
  window.addEventListener('resize', render);
  btn.onclick = (e) => { e.stopPropagation(); setOpen(!lang.classList.contains('open')); };
  backdrop.onclick = () => setOpen(false);
  document.addEventListener('click', e => { if(!lang.contains(e.target)) setOpen(false); });
  document.addEventListener('keydown', e => { if(e.key === 'Escape') setOpen(false); });
})();
