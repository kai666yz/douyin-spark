(request => {
  // Operate only on the exact imported group ID and its proven active composer.
  // Never call an IM SDK, inspect message models, or choose a group by its name.
  const visible = el => !!(el && el.getClientRects().length);
  const get = (obj,key) => {try{return obj?.[key];}catch{return undefined;}};
  const scalar = value => typeof value==='string'?value:typeof value==='number'&&Number.isSafeInteger(value)?String(value):'';
  const valid = id => typeof id==='string'&&id.length>0&&id.length<=240&&/^[A-Za-z0-9_:.-]+$/.test(id);
  const idOf = model => scalar(get(model,'id'))||scalar(get(model,'conversationId'))||scalar(get(model,'conversation_id'));
  const groupOf = model => get(model,'type')===2||get(model,'conversationType')===2||get(model,'isGroup')===true||get(model,'isGroupConversation')===true;
  const normalize = text => String(text||'').replace(/\r\n/g,'\n').replace(/\u00a0/g,' ').replace(/\n$/,'');
  if(!request||!valid(request.id))return {status:'INVALID_ID'};
  if(location.protocol!=='https:'||!(location.hostname==='douyin.com'||location.hostname.endsWith('.douyin.com')))return {status:'UNTRUSTED_PAGE'};
  if([...document.querySelectorAll('#captcha_container, .captcha_verify_container, [data-e2e*="captcha"], iframe[src*="captcha"]')].some(visible))return {status:'VERIFICATION_REQUIRED'};
  if(location.pathname.includes('login')||[...document.querySelectorAll('[data-e2e="login-dialog"], [data-e2e="login-qrcode"]')].some(visible))return {status:'LOGIN_REQUIRED'};
  const models = node => {
    const result=[];
    let fiber=node?.[Object.keys(node||{}).find(key=>key.startsWith('__reactFiber$')||key.startsWith('__reactInternalInstance$'))];
    for(let depth=0;fiber&&depth<14;depth++,fiber=fiber.return){
      const props=fiber.memoizedProps||fiber.pendingProps;
      for(const key of ['conversation','curConversation','currentConversation','activeConversation']){const model=get(props,key);if(model&&typeof model==='object')result.push(model);}
      let context=fiber.dependencies?.firstContext;
      for(let i=0;context&&i<4;i++,context=context.next){
        const value=context.memoizedValue;
        for(const key of ['currentConversation','activeConversation']){const model=get(value,key);if(model&&typeof model==='object')result.push(model);}
      }
    }
    return result;
  };
  const rows=[...document.querySelectorAll('[data-e2e="conversation-item"], [data-e2e*="chat-list-item"], [data-e2e*="session-item"]')];
  const rowId = row => row.getAttribute('data-conversation-id')||row.getAttribute('data-conversationid')||idOf(models(row)[0]);
  const matches=rows.filter(row=>rowId(row)===request.id);
  if(request.phase==='select'){
    const exact=matches.filter(row=>visible(row)&&((row.getAttribute('data-type')==='group')||models(row).some(groupOf)));
    if(exact.length!==1)return {status:exact.length?'AMBIGUOUS_GROUP':'GROUP_NOT_LOADED'};
    const row=exact[0];row.scrollIntoView({block:'nearest'});
    row.dispatchEvent(new MouseEvent('mousedown',{bubbles:true,cancelable:true,view:window,button:0,buttons:1}));
    row.dispatchEvent(new MouseEvent('mouseup',{bubbles:true,cancelable:true,view:window,button:0,buttons:0}));
    row.click();return {status:'OPENED'};
  }
  if(request.phase==='scroll'){
    const root=document.querySelector('[data-e2e="im-dialog"], .imContainer');
    if(!root)return {status:'LIST_NOT_OPEN'};
    const nodes=[...root.querySelectorAll('*')];
    const scroller=nodes.find(el=>el.clientHeight>60&&el.scrollHeight>el.clientHeight+20&&/auto|scroll/.test(getComputedStyle(el).overflowY));
    if(!scroller)return {status:'SCROLL_UNAVAILABLE'};
    scroller.scrollBy(0,Math.max(350,scroller.clientHeight*.8));return {status:'SCROLLED'};
  }
  const editors=[...document.querySelectorAll('[contenteditable="true"], textarea[data-e2e*="chat"]')].filter(visible).filter(el=>!el.closest('[data-e2e="conversation-item"]'));
  if(editors.length!==1)return {status:'AMBIGUOUS_EDITOR'};
  const editor=editors[0];
  let scope=null,proof=false;
  for(let node=editor,depth=0;node&&depth<10;node=node.parentElement,depth++){
    if(node===document.body||node===document.documentElement)break;
    const marked=node.getAttribute('data-conversation-id')||node.getAttribute('data-active-conversation-id');
    const candidates=models(node).filter(m=>valid(idOf(m)));
    if(candidates.length&&(idOf(candidates[0])!==request.id||!groupOf(candidates[0])))return {status:'WRONG_ACTIVE_GROUP'};
    const model=candidates.find(m=>idOf(m)===request.id&&groupOf(m));
    if(marked===request.id&&node.getAttribute('data-type')==='group'||model){proof=true;scope=node;break;}
    if(marked&&marked!==request.id)return {status:'WRONG_ACTIVE_GROUP'};
  }
  if(!proof)return {status:'UNVERIFIED_ACTIVE_GROUP'};
  // Never select a send button outside the active composer area.
  const sendButton=()=>{
    for(let parent=editor.parentElement,depth=0;parent&&depth<6;parent=parent.parentElement,depth++){
      if(parent===document.body||parent===document.documentElement)break;
      const buttons=[...parent.querySelectorAll('button,[role="button"],[data-e2e*="send"]')].filter(visible).filter(el=>((el.textContent||'').trim()==='发送'||el.getAttribute('aria-label')==='发送')&&!el.disabled&&el.getAttribute('aria-disabled')!=='true');
      if(buttons.length>1)return null;
      if(buttons.length===1)return buttons[0];
    }
    return null;
  };
  if(request.phase==='inspect')return {status:'READY',draft:normalize(editor.value??editor.innerText).length>0,sendButton:!!sendButton()};
  if(request.phase==='fill'){
    if(typeof request.message!=='string'||!request.message.trim()||request.message.length>2000)return {status:'INVALID_MESSAGE'};
    if(normalize(editor.value??editor.innerText))return {status:'DRAFT_PRESENT'};
    editor.focus();
    if(editor.tagName==='TEXTAREA'){
      const setter=Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value').set;setter.call(editor,request.message);
      editor.dispatchEvent(new Event('input',{bubbles:true}));
    }else{
      document.execCommand('insertText',false,request.message);
      editor.dispatchEvent(new InputEvent('input',{bubbles:true,inputType:'insertText',data:request.message}));
    }
    return {status:normalize(editor.value??editor.innerText)===normalize(request.message)?'FILLED':'INPUT_UNSUPPORTED'};
  }
  if(request.phase==='submit'||request.phase==='keyboard-verify'){
    if(normalize(editor.value??editor.innerText)!==normalize(request.message))return {status:'DRAFT_CHANGED'};
    const keyboard=editor.matches('[data-slate-editor], .messageEditorinputArea')&&!!editor.closest('.messageMsgInputcontainer');
    if(request.phase==='keyboard-verify'){if(!keyboard)return {status:'SEND_BUTTON_UNAVAILABLE'};editor.focus();return {status:'KEYBOARD_READY'};}
    const button=sendButton();if(!button){if(keyboard){editor.focus();return {status:'KEYBOARD_READY'};}return {status:'SEND_BUTTON_UNAVAILABLE'};}
    // Persisting the attempt and the user's send authorization happen in C# first.
    button.click();return {status:'SUBMITTED'};
  }
  return {status:'UNKNOWN_PHASE'};
})
