document.addEventListener('DOMContentLoaded',()=>{
 // Tema: kayıtlı seçim yoksa sistem tercihi kullanılır; butona basınca açık/koyu arasında geçilir ve hatırlanır.
 const themeBtn=document.querySelector('#themeToggle');
 if(themeBtn){
  const root=document.documentElement;
  const isDark=()=>root.dataset.theme?root.dataset.theme==='dark':matchMedia('(prefers-color-scheme: dark)').matches;
  const render=()=>{const dark=isDark();themeBtn.textContent=dark?'☀':'☾';themeBtn.setAttribute('aria-label',dark?'Açık temaya geç':'Koyu temaya geç');themeBtn.title=themeBtn.getAttribute('aria-label');};
  themeBtn.addEventListener('click',()=>{root.dataset.theme=isDark()?'light':'dark';try{localStorage.setItem('theme',root.dataset.theme);}catch(e){}render();});
  matchMedia('(prefers-color-scheme: dark)').addEventListener('change',render);
  render();
 }
 // Kategori/firma ve sayfa listelerinde seçili satır görünür olsun (sayfanın kendisi kaydırılmadan).
 for(const list of document.querySelectorAll('.note-list')){
  const rows=list.querySelectorAll('.active'); const row=rows[rows.length-1]; if(!row)continue;
  const top=row.offsetTop-list.offsetTop;
  if(top+row.offsetHeight>list.scrollTop+list.clientHeight)list.scrollTop=top-list.clientHeight/3;
 }
 // Sayfa listesinde sürükle-bırak ile sıralama; bırakınca yeni sıra sunucuya kaydedilir.
 const reorderList=document.querySelector('.note-list[data-reorder="true"]');
 if(reorderList){
  let dragged=null, before=null;
  const ids=()=>[...reorderList.querySelectorAll('.note-row')].map(r=>+r.dataset.id);
  reorderList.addEventListener('dragstart',e=>{
   dragged=e.target.closest('.note-row'); if(!dragged)return;
   before=ids().join(); dragged.classList.add('dragging');
   e.dataTransfer.effectAllowed='move'; e.dataTransfer.setData('text/plain',dragged.dataset.id);
  });
  reorderList.addEventListener('dragover',e=>{
   if(!dragged)return; e.preventDefault();
   const over=e.target.closest('.note-row'); if(!over||over===dragged)return;
   const box=over.getBoundingClientRect();
   over.parentNode.insertBefore(dragged, e.clientY<box.top+box.height/2?over:over.nextSibling);
  });
  reorderList.addEventListener('drop',e=>e.preventDefault());
  reorderList.addEventListener('dragend',async()=>{
   if(!dragged)return; dragged.classList.remove('dragging'); dragged=null;
   const order=ids(); if(order.join()===before)return;
   const token=reorderList.querySelector('input[name="__RequestVerificationToken"]')?.value;
   try{
    const r=await fetch('/Notes/Reorder',{method:'POST',headers:{'Content-Type':'application/json','RequestVerificationToken':token},body:JSON.stringify(order)});
    if(!r.ok)throw new Error();
   }catch{alert('Sıralama kaydedilemedi; sayfa yenilenecek.');location.reload();}
  });
 }
 // Firma alanı sadece Projeler kategorisi seçiliyken gösterilir.
 const categorySelect=document.querySelector('#CategoryId'), companyField=document.querySelector('#companyField');
 if(categorySelect&&companyField){
  const sync=()=>{companyField.hidden=categorySelect.value!==categorySelect.dataset.projectsId;};
  categorySelect.addEventListener('change',sync); sync();
 }
 const btn=document.querySelector('#suggestBtn'); if(!btn)return;
 btn.addEventListener('click',async()=>{
  const token=document.querySelector('input[name="__RequestVerificationToken"]')?.value;
  const suggestions=document.querySelector('#suggestions');suggestions.textContent='Öneriler hazırlanıyor...';
  try {
   const response=await fetch('/Notes/Suggest',{method:'POST',headers:{'Content-Type':'application/json','RequestVerificationToken':token},body:JSON.stringify({title:document.querySelector('#Title').value,body:document.querySelector('#Body').value,code:document.querySelector('#Code').value})});
   if(!response.ok)throw new Error('İstek başarısız'); const list=await response.json();suggestions.replaceChildren();
   if(list.length===0){suggestions.textContent='Güvenilir öneri bulunamadı. Kategoriyi elle seç.';return;}
   for(const item of list){const b=document.createElement('button');b.type='button';b.className='suggest';b.textContent=item.category+' · '+item.evidence;b.addEventListener('click',()=>{const select=document.querySelector('#CategoryId');const opt=[...select.options].find(x=>x.text===item.category);if(opt){select.value=opt.value;select.dispatchEvent(new Event('change'));}});suggestions.appendChild(b);}
  }catch(e){suggestions.textContent='Öneri alınamadı: '+e.message;}
 });
});
