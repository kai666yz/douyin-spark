(() => {
 const visible=e=>!!(e&&e.getClientRects().length);
 const dialog=document.querySelector('[data-e2e="im-dialog"]');
 if(dialog&&visible(dialog))return 'open';
 const entry=[...document.querySelectorAll('[data-e2e="im-entry"]')].find(visible);
 if(entry){const label=[...entry.querySelectorAll('button,[role="button"],div,span,p')].find(e=>visible(e)&&e.textContent.trim()==='消息'&&e.children.length<3);(label||entry).click();return 'requested';}
 const login=[...document.querySelectorAll('[data-e2e="login-mask"], [data-e2e="login-dialog"]')].some(visible);
 return login?'login_required':'loading';
})()
