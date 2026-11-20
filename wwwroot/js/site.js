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
 // Komut alanı (SAP OK-code): "/" tuşu her ekrandan görünen komut alanına getirir, Esc bırakır.
 // Kod mu arama mı ayrımını sunucu yapar (TCodes.cs); boş komut gönderilmez.
 const okcodes=[...document.querySelectorAll('[data-okcode]')];
 const visibleOkcode=()=>okcodes.find(i=>i.offsetParent!==null&&getComputedStyle(i).visibility!=='hidden');
 document.addEventListener('keydown',e=>{
  if(e.key!=='/'||e.ctrlKey||e.metaKey||e.altKey)return;
  const t=e.target; if(t.isContentEditable||/^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName))return;
  const field=visibleOkcode(); if(!field)return;
  e.preventDefault(); field.focus(); field.select();
 });
 for(const field of okcodes){
  field.addEventListener('keydown',e=>{if(e.key==='Escape'){field.value='';field.blur();}});
  field.form?.addEventListener('submit',e=>{if(!field.value.trim()){e.preventDefault();field.focus();}});
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
  let dragged=null, before=null, beforeGroup=null;
  const ids=()=>[...reorderList.querySelectorAll('.note-row')].map(r=>+r.dataset.id);
  // Alt kategori grupları: satırın üstündeki ilk başlık onun alt kategorisidir ("" = Genel).
  const groupOf=row=>{let el=row.previousElementSibling; while(el&&!el.matches('.note-group'))el=el.previousElementSibling; return el?el.dataset.sub:null;};
  const recount=()=>reorderList.querySelectorAll('.note-group').forEach(g=>{
   let n=0, el=g.nextElementSibling; while(el&&!el.matches('.note-group')){if(el.matches('.note-row'))n++; el=el.nextElementSibling;}
   const c=g.querySelector('[data-group-count]'); if(c)c.textContent=n; g.classList.toggle('is-empty',n===0);
  });
  reorderList.addEventListener('dragstart',e=>{
   dragged=e.target.closest('.note-row'); if(!dragged)return;
   before=ids().join(); beforeGroup=groupOf(dragged); dragged.classList.add('dragging');
   e.dataTransfer.effectAllowed='move'; e.dataTransfer.setData('text/plain',dragged.dataset.id);
  });
  reorderList.addEventListener('dragover',e=>{
   if(!dragged)return; e.preventDefault();
   const head=e.target.closest('.note-group'); if(head){head.after(dragged); return;}
   const over=e.target.closest('.note-row'); if(!over||over===dragged)return;
   const box=over.getBoundingClientRect();
   over.parentNode.insertBefore(dragged, e.clientY<box.top+box.height/2?over:over.nextSibling);
  });
  reorderList.addEventListener('drop',e=>e.preventDefault());
  reorderList.addEventListener('dragend',async()=>{
   if(!dragged)return; const row=dragged; row.classList.remove('dragging'); dragged=null;
   const order=ids(), group=groupOf(row); if(order.join()===before&&group===beforeGroup)return;
   const token=reorderList.querySelector('input[name="__RequestVerificationToken"]')?.value;
   try{
    if(group!==beforeGroup){
     const m=await fetch('/Subcategories/Move',{method:'POST',headers:{'Content-Type':'application/json','RequestVerificationToken':token},body:JSON.stringify({id:+row.dataset.id,subcategoryId:group?+group:null})});
     if(!m.ok)throw new Error(); recount();
    }
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
 // Alt kategori seçimi: sadece seçili kategorinin alt kategorileri görünür; hiç yoksa alan gizlenir.
 const subField=document.querySelector('[data-subcategory-field]');
 if(categorySelect&&subField){
  const subSelect=subField.querySelector('select');
  const syncSubs=()=>{
   let any=false;
   for(const o of subSelect.options){ if(!o.dataset.category)continue; const show=o.dataset.category===categorySelect.value; o.hidden=!show; o.disabled=!show; any||=show; }
   if(subSelect.selectedOptions[0]?.disabled)subSelect.value='';
   subField.hidden=!any;
  };
  categorySelect.addEventListener('change',syncSubs); syncSubs();
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
 // Yapışkan notlar sütunu (sağda sabit): geniş ekranda açık/kapalı durumu hatırlanır;
 // dar ekranda kapalı başlar, 📌 ile içeriğin üstüne kayan panel olarak açılır (Esc kapatır).
 const dock=document.querySelector('[data-sticky-dock]');
 if(dock){
  const wide=matchMedia('(min-width: 1200px)'), root=document.documentElement;
  const isOpen=()=>wide.matches?root.dataset.dock!=='closed':dock.classList.contains('show');
  const sync=()=>{const open=isOpen(); dock.inert=!open; document.querySelectorAll('[data-dock-toggle]').forEach(b=>b.setAttribute('aria-expanded',open));};
  const toggle=()=>{
   if(wide.matches){
    const close=root.dataset.dock!=='closed';
    if(close)root.dataset.dock='closed'; else delete root.dataset.dock;
    try{localStorage.setItem('stickyDock',close?'closed':'open');}catch(e){}
   } else dock.classList.toggle('show');
   sync();
  };
  document.querySelectorAll('[data-dock-toggle]').forEach(b=>b.addEventListener('click',toggle));
  document.addEventListener('keydown',e=>{if(e.key==='Escape'&&!wide.matches&&dock.classList.contains('show')){dock.classList.remove('show');sync();}});
  document.addEventListener('pointerdown',e=>{
   if(wide.matches||!dock.classList.contains('show')||dock.contains(e.target)||e.target.closest('[data-dock-toggle]'))return;
   dock.classList.remove('show'); sync();
  });
  wide.addEventListener('change',()=>{dock.classList.remove('show');sync();});
  sync();
 }
 // Yapışkan notlar: yazdıkça (0,7 sn sonra) kaydeder; renk, ✓ tamamlandı, × sil, ⠿ ile sürükleyip sıralama.
 const board=document.querySelector('[data-sticky-board]');
 if(board){
  const token=document.querySelector('input[name="__RequestVerificationToken"]')?.value;
  const empty=document.querySelector('[data-sticky-empty]'), template=document.querySelector('[data-sticky-template]');
  const post=(action,data,keepalive=false)=>fetch('/Sticky/'+action,{method:'POST',keepalive,headers:{'Content-Type':'application/json','RequestVerificationToken':token},body:JSON.stringify(data)})
   .then(r=>{if(!r.ok)throw new Error(r.status);return r;});
  const pending=new Map(); // id -> {timer, card}
  const status=(card,text)=>{const s=card.querySelector('[data-status]'); if(s)s.textContent=text;};
  const fit=t=>{t.style.height='auto';t.style.height=t.scrollHeight+'px';};
  const counter=document.querySelector('[data-sticky-count]');
  const refreshEmpty=()=>{empty.hidden=board.children.length>0; if(counter)counter.textContent=board.querySelectorAll('.sticky:not(.done)').length;};
  const saveText=(card,keepalive=false)=>{
   const id=+card.dataset.id, p=pending.get(id); if(p){clearTimeout(p);pending.delete(id);}
   status(card,'Kaydediliyor…');
   return post('Update',{id,text:card.querySelector('textarea').value},keepalive)
    .then(()=>status(card,'Kaydedildi')).catch(()=>status(card,'⚠ Kaydedilemedi'));
  };
  // Resimler: her biri ayrı yüklenir; karttaki küçük resme tıklayınca büyük açılır.
  const imageTypes=['image/png','image/jpeg','image/gif','image/webp','image/bmp'];
  const imageFiles=list=>[...(list||[])].filter(f=>f.type.startsWith('image/'));
  const wireImage=(card,fig)=>fig.querySelector('[data-image-remove]').addEventListener('click',async()=>{
   if(!confirm('Bu resim silinsin mi?'))return;
   try{await post('DeleteImage',{id:+fig.dataset.imageId}); fig.remove();}catch{status(card,'⚠ Resim silinemedi');}
  });
  const addImage=(card,id)=>{
   const fig=document.createElement('figure'); fig.className='sticky-image'; fig.dataset.imageId=id;
   const a=document.createElement('a'); a.href='/Sticky/Image/'+id; a.target='_blank'; a.rel='noopener'; a.title='Büyük aç';
   const img=document.createElement('img'); img.src=a.href; img.alt='Yapışkan not resmi'; a.append(img);
   const remove=document.createElement('button'); remove.type='button'; remove.dataset.imageRemove=''; remove.title='Resmi kaldır'; remove.setAttribute('aria-label','Resmi kaldır'); remove.textContent='×';
   fig.append(a,remove); card.querySelector('[data-images]').append(fig); wireImage(card,fig);
   card.querySelector('textarea').placeholder='Açıklama ekle…';
  };
  const uploadImages=async(card,files)=>{
   for(const file of files){
    if(!imageTypes.includes(file.type)){alert((file.name||'Resim')+': sadece PNG, JPEG, GIF, WEBP veya BMP eklenebilir.');continue;}
    if(file.size>10*1024*1024){alert((file.name||'Resim')+': resim 10 MB\'tan büyük olamaz.');continue;}
    status(card,'Resim yükleniyor…');
    const form=new FormData(); form.append('id',card.dataset.id); form.append('file',file,file.name||'resim.png');
    try{
     const r=await fetch('/Sticky/AddImage',{method:'POST',headers:{'RequestVerificationToken':token},body:form});
     if(!r.ok)throw new Error(r.status);
     addImage(card,(await r.json()).id); status(card,'Kaydedildi');
    }catch{status(card,'⚠ Resim eklenemedi');}
   }
  };
  const lastActive=except=>[...board.children].filter(c=>c!==except&&!c.classList.contains('done')).pop();
  const wire=card=>{
   const text=card.querySelector('textarea'); fit(text);
   text.addEventListener('input',()=>{
    fit(text); const id=+card.dataset.id; clearTimeout(pending.get(id));
    status(card,'Yazılıyor…'); pending.set(id,setTimeout(()=>saveText(card),700));
   });
   text.addEventListener('blur',()=>{if(pending.has(+card.dataset.id))saveText(card);});
   card.addEventListener('focusout',()=>setTimeout(()=>{
    if(!card.classList.contains('is-new')||card.contains(document.activeElement)||text.value.trim()||card.querySelector('[data-image-id]')||!card.isConnected)return;
    post('Delete',{id:+card.dataset.id}).then(()=>{card.classList.add('is-leaving'); setTimeout(()=>{card.remove(); refreshEmpty();},180);}).catch(()=>{});
   },250));
   // Renk: karttaki renk noktası altı renklik seçiciyi açar; seçince kapanır ve odak noktaya döner.
   const colorBtn=card.querySelector('[data-color-toggle]'), colors=card.querySelector('[data-colors]');
   const showColors=open=>{colors.hidden=!open; colorBtn.setAttribute('aria-expanded',open); if(open)(colors.querySelector('.on')||colors.querySelector('button')).focus();};
   colorBtn.addEventListener('click',()=>showColors(colors.hidden));
   colors.addEventListener('keydown',e=>{if(e.key==='Escape'){showColors(false);colorBtn.focus();}});
   for(const dot of card.querySelectorAll('[data-color]'))
    dot.addEventListener('click',()=>{
     const color=dot.dataset.color; card.className=card.className.replace(/\bc-\w+/,'c-'+color);
     card.querySelectorAll('[data-color]').forEach(d=>{d.classList.toggle('on',d===dot);d.setAttribute('aria-pressed',d===dot);});
     colorBtn.title='Renk: '+dot.title; colorBtn.setAttribute('aria-label','Rengi değiştir (şu an '+dot.title+')');
     showColors(false); colorBtn.focus();
     post('Update',{id:+card.dataset.id,color}).catch(()=>status(card,'⚠ Kaydedilemedi'));
    });
   card.querySelector('[data-done]').addEventListener('click',e=>{
    const done=!card.classList.contains('done'); card.classList.toggle('done',done);
    e.currentTarget.title=done?'Geri al':'Tamamlandı'; e.currentTarget.setAttribute('aria-pressed',done);
    // Tamamlanan sona, geri alınan tamamlanmamışların sonuna taşınır.
    if(done)board.append(card); else {const last=lastActive(card); last?last.after(card):board.prepend(card);}
    refreshEmpty();
    post('Update',{id:+card.dataset.id,done}).then(saveOrder).catch(()=>status(card,'⚠ Kaydedilemedi'));
   });
   card.querySelectorAll('[data-image-id]').forEach(fig=>wireImage(card,fig));
   // Panodan sadece resim gelirse (ekran görüntüsü gibi) resim eklenir; metin de varsa normal yapıştırılır.
   text.addEventListener('paste',e=>{
    const files=imageFiles(e.clipboardData?.files);
    if(files.length===0||e.clipboardData.getData('text/plain'))return;
    e.preventDefault(); uploadImages(card,files);
   });
   card.addEventListener('dragover',e=>{
    if(dragged||![...e.dataTransfer.items].some(i=>i.kind==='file'))return;
    e.preventDefault(); card.classList.add('drop-target');
   });
   card.addEventListener('dragleave',e=>{if(!card.contains(e.relatedTarget))card.classList.remove('drop-target');});
   card.addEventListener('drop',e=>{
    card.classList.remove('drop-target'); if(dragged)return;
    const files=imageFiles(e.dataTransfer.files); if(files.length===0)return;
    e.preventDefault(); e.stopPropagation(); uploadImages(card,files);
   });
   card.querySelector('[data-image-pick]').addEventListener('click',()=>{
    const pick=document.createElement('input'); pick.type='file'; pick.accept=imageTypes.join(','); pick.multiple=true;
    pick.addEventListener('change',()=>uploadImages(card,[...pick.files])); pick.click();
   });
   card.querySelector('[data-delete]').addEventListener('click',async()=>{
    if((text.value.trim()||card.querySelector('[data-image-id]'))&&!confirm('Bu not silinsin mi?'))return;
    try{
     await post('Delete',{id:+card.dataset.id}); clearTimeout(pending.get(+card.dataset.id)); pending.delete(+card.dataset.id);
     card.classList.add('is-leaving'); setTimeout(()=>{card.remove(); refreshEmpty();},matchMedia('(prefers-reduced-motion: reduce)').matches?0:180);
    }
    catch{status(card,'⚠ Silinemedi');}
   });
   // Sadece ⠿ tutamacından sürüklenir; böylece metin seçimi bozulmaz.
   const handle=card.querySelector('.sticky-handle');
   handle.addEventListener('pointerdown',()=>{card.draggable=true;});
   card.addEventListener('dragend',()=>{card.draggable=false;});
  };
  const saveOrder=()=>post('Reorder',[...board.children].map(c=>+c.dataset.id)).catch(()=>{});
  let dragged=null;
  board.addEventListener('dragstart',e=>{dragged=e.target.closest('.sticky'); if(!dragged)return; dragged.classList.add('dragging'); e.dataTransfer.effectAllowed='move'; e.dataTransfer.setData('text/plain',dragged.dataset.id);});
  board.addEventListener('dragover',e=>{
   if(!dragged)return; e.preventDefault();
   const over=e.target.closest('.sticky'); if(!over||over===dragged)return;
   const box=over.getBoundingClientRect();
   over.parentNode.insertBefore(dragged,e.clientY<box.top+box.height/2?over:over.nextSibling);
  });
  board.addEventListener('drop',e=>e.preventDefault());
  board.addEventListener('dragend',()=>{if(!dragged)return; dragged.classList.remove('dragging'); dragged=null; saveOrder();});
  board.querySelectorAll('.sticky').forEach(wire);
  document.querySelector('[data-sticky-add]')?.addEventListener('click',async()=>{
   try{
    const r=await post('Create',{id:0}); const {id,date}=await r.json();
    const card=template.content.firstElementChild.cloneNode(true);
    card.dataset.id=id; status(card,date); card.classList.add('is-new'); board.prepend(card); wire(card); refreshEmpty();
    card.querySelector('textarea').focus();
   }catch{alert('Not eklenemedi.');}
  });
  // Sayfadan çıkarken bekleyen kayıtlar gönderilir.
  addEventListener('pagehide',()=>{for(const id of [...pending.keys()]){const card=board.querySelector(`[data-id="${id}"]`); if(card)saveText(card,true);}});
 }
 // data-confirm taşıyan formlar göndermeden önce onay ister (ör. yarım quiz'i yenileme).
 for(const form of document.querySelectorAll('form[data-confirm]'))
  form.addEventListener('submit',e=>{if(form.dataset.confirm&&!confirm(form.dataset.confirm))e.preventDefault();});
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
