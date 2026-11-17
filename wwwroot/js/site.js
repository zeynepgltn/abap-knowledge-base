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
 // Arama sonucunda açılan notta aranan ifade işaretlenir ve ilk eşleşmeye kaydırılır.
 // İfade bir arada geçmiyorsa kelimeler ayrı ayrı işaretlenir.
 const docBody=document.querySelector('.doc-body[data-highlight]');
 const query=docBody?.dataset.highlight.trim();
 if(query){
  // Sunucudaki aramayla aynı: İ/I/ı/i birbirinin yerine geçer.
  const esc=s=>s.replace(/[.*+?^${}()|[\]\\]/g,'\\$&').replace(/[iİIı]/g,'[iİIı]');
  const mark=pattern=>{
   const rx=new RegExp(pattern,'gi'), nodes=[], walker=document.createTreeWalker(docBody,NodeFilter.SHOW_TEXT);
   while(walker.nextNode()){rx.lastIndex=0; if(rx.test(walker.currentNode.nodeValue))nodes.push(walker.currentNode);}
   for(const node of nodes){
    const frag=document.createDocumentFragment(); let last=0; const text=node.nodeValue; rx.lastIndex=0;
    for(let m;(m=rx.exec(text));){
     if(!m[0].length){rx.lastIndex++;continue;}
     frag.append(text.slice(last,m.index)); const el=document.createElement('mark'); el.className='hl'; el.textContent=m[0]; frag.append(el); last=m.index+m[0].length;
    }
    frag.append(text.slice(last)); node.replaceWith(frag);
   }
   return nodes.length>0;
  };
  if(!mark(esc(query))){const words=query.split(/\s+/).filter(w=>w.length>1); if(words.length>1)mark(words.map(esc).join('|'));}
  docBody.querySelector('mark.hl')?.scrollIntoView({block:'center'});
 }
 // Firma alanı sadece Projeler kategorisi seçiliyken gösterilir.
 const categorySelect=document.querySelector('#CategoryId'), companyField=document.querySelector('#companyField');
 if(categorySelect&&companyField){
  const sync=()=>{companyField.hidden=categorySelect.value!==categorySelect.dataset.projectsId;};
  categorySelect.addEventListener('change',sync); sync();
 }
 // Nota resim ekleme: "Resim ekle" ile seç, açıklama kutusuna yapıştır (Ctrl+V) veya sürükle-bırak.
 // Her resim formdaki dosya alanına "yeni-<no>.<uzantı>" adıyla eklenir ve imlecin yerine "[yeni-resim:<no>]" satırı yazılır;
 // sunucu kaydederken bu satırı "[resim:N]" yapar. Önizlemedeki × resmi ve satırını kaldırır.
 for(const tools of document.querySelectorAll('[data-image-tools]')){
  const form=tools.closest('form'), input=tools.querySelector('[data-image-input]'), previews=tools.querySelector('[data-image-previews]');
  const body=form?.querySelector('textarea[name="Body"]'); if(!form||!input||!body)continue;
  const files=new DataTransfer(); let next=1;
  const ext={'image/png':'png','image/jpeg':'jpg','image/gif':'gif','image/webp':'webp','image/bmp':'bmp'};
  const insertLine=text=>{
   const s=body.selectionStart??body.value.length, e=body.selectionEnd??s, v=body.value;
   const pre=v.slice(0,s), post=v.slice(e);
   const line=(pre&&!pre.endsWith('\n')?'\n':'')+text+(post.startsWith('\n')?'':'\n');
   body.value=pre+line+post; const pos=pre.length+line.length; body.setSelectionRange(pos,pos); body.focus();
  };
  const add=file=>{
   if(!ext[file.type]){alert(file.name+': sadece PNG, JPEG, GIF, WEBP veya BMP resim eklenebilir.');return;}
   if(file.size>10*1024*1024){alert(file.name+': resim 10 MB\'tan büyük olamaz.');return;}
   const no=next++, token='[yeni-resim:'+no+']';
   const named=new File([file],'yeni-'+no+'.'+ext[file.type],{type:file.type});
   files.items.add(named); input.files=files.files; insertLine(token);
   const thumb=document.createElement('div'); thumb.className='image-thumb';
   const img=document.createElement('img'); img.src=URL.createObjectURL(file); img.alt='Yeni resim '+no;
   const remove=document.createElement('button'); remove.type='button'; remove.className='image-remove'; remove.textContent='×'; remove.title='Resmi kaldır';
   const label=document.createElement('span'); label.textContent=token;
   remove.addEventListener('click',()=>{
    const i=[...files.files].findIndex(f=>f.name===named.name); if(i>=0)files.items.remove(i); input.files=files.files;
    body.value=body.value.replace(new RegExp('^[ \\t]*'+token.replace(/[[\]]/g,'\\$&')+'[ \\t]*\\n?','m'),'');
    URL.revokeObjectURL(img.src); thumb.remove();
   });
   thumb.append(img,remove,label); previews.append(thumb);
  };
  tools.querySelector('[data-image-pick]')?.addEventListener('click',()=>{
   // Seçim penceresi dosya alanını sıfırlar: ayrı bir geçici alan kullanılır, seçilenler listeye eklenir.
   const pick=document.createElement('input'); pick.type='file'; pick.accept=input.accept; pick.multiple=true;
   pick.addEventListener('change',()=>{for(const f of pick.files)add(f);}); pick.click();
  });
  body.addEventListener('paste',e=>{
   const images=[...(e.clipboardData?.files||[])].filter(f=>f.type.startsWith('image/'));
   if(images.length===0)return; e.preventDefault(); images.forEach(add);
  });
  body.addEventListener('dragover',e=>{if([...e.dataTransfer.items].some(i=>i.kind==='file'))e.preventDefault();});
  body.addEventListener('drop',e=>{
   const images=[...e.dataTransfer.files].filter(f=>f.type.startsWith('image/'));
   if(images.length===0)return; e.preventDefault(); body.focus(); images.forEach(add);
  });
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
