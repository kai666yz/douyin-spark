(() => {
  // Read only: whitelist conversation metadata on mounted message rows.
  // Do not traverse message objects, cookies, storage or global application state.
  const visible = e => !!(e && e.getClientRects().length);
  const roots = [document];
  for (let i=0;i<roots.length&&i<16;i++) for (const node of roots[i].querySelectorAll('*')) if(node.shadowRoot) roots.push(node.shadowRoot);
  const selector='[data-e2e="conversation-item"], [data-e2e*="chat-list-item"], [data-conversation-id], [data-conversationid], [data-e2e*="session-item"]';
  const all = roots.flatMap(root=>[...root.querySelectorAll(selector)]);
  // Clicking the app toolbar blurs Douyin's popup. Hidden, already-loaded IM rows
  // remain valid; unrelated hidden page elements must still be excluded.
  const rows = all.filter(row=>visible(row)||row.closest('[data-e2e="im-dialog"], .imContainer'));
  const output = new Map();
  const report = {version:'2.1.1',rows:rows.length,hiddenLoaded:0,reactModels:0,missingIdentity:0,missingName:0,accepted:0};
  const value = (o,k) => {try{return o?.[k];}catch{return undefined;}};
  const scalar = v => typeof v==='string'?v:typeof v==='number'&&Number.isSafeInteger(v)?String(v):'';
  const validId = s => typeof s==='string' && s.length>0 && s.length<=240 && /^[A-Za-z0-9_:.-]+$/.test(s);
  const validSec = s => typeof s==='string' && /^[A-Za-z0-9_-]+$/.test(s);
  const profile = s => {try{const u=new URL(s,location.href);return u.origin==='https://www.douyin.com'&&/^\/user\/[A-Za-z0-9_-]+\/?$/.test(u.pathname)?u.origin+u.pathname.replace(/\/$/,''):'';}catch{return '';}};
  for (const row of rows) {
    if(!visible(row))report.hiddenLoaded++;
    let model=null;
    const key=Object.keys(row).find(k=>k.startsWith('__reactFiber$')||k.startsWith('__reactInternalInstance$'));
    let fiber=key?row[key]:null;
    for(let level=0;fiber&&level<6;level++,fiber=fiber.return){
      const props=fiber.memoizedProps||fiber.pendingProps;
      if(props&&typeof props==='object'&&props.conversation&&typeof props.conversation==='object'){model=props.conversation;break;}
    }
    if(model)report.reactModels++;
    const a = row.querySelector('a[href*="/user/"]');
    let url=a?profile(a.href):'';
    let id=row.getAttribute('data-conversation-id')||row.getAttribute('data-conversationid')||row.getAttribute('data-session-id')||'';
    if(!id&&model)id=scalar(value(model,'id'))||scalar(value(model,'conversationId'))||scalar(value(model,'shortId'));
    if(!validId(id))id='';
    const sec=scalar(value(model,'toParticipantSecUserId'))||scalar(value(model,'_toParticipantSecUserId'));
    const type=value(model,'type')??value(model,'conversationType');
    const group=row.getAttribute('data-is-group')==='true'||row.getAttribute('data-type')==='group'||value(model,'isGroup')===true||value(model,'isGroupConversation')===true||type===2||!!row.querySelector('[data-e2e*="group-avatar"], [data-e2e*="group-icon"]');
    if(group)url='';else if(!url&&validSec(sec))url='https://www.douyin.com/user/'+sec;
    const title=row.querySelector('.conversationConversationItemtitle, [data-e2e*="name"], [class*="conversation-name"], [class*="session-name"], [class*="chat-name"], [class*="ConversationItemtitle"]:not([class*="Wrapper"])');
    const name=(title?.getAttribute('title')||title?.textContent||scalar(value(value(model,'coreInfo'),'name'))||a?.getAttribute('title')||a?.textContent||'').trim().slice(0,120);
    if(!name){report.missingName++;continue;}
    if(!id&&!url){report.missingIdentity++;continue;}
    const kind = group ? 'group' : url ? 'private' : 'unknown';
    output.set(id?'conversation:'+id:'profile:'+url,{name,kind,url,conversation_id:id});
  }
  report.accepted=output.size;
  window.__sparkConversationReadReport=report;
  return [...output.values()].slice(0,2000);
})()
