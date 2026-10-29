(() => {
  // Read only. No messages, cookies, localStorage, key presses, or editor actions.
  const visible = e => !!(e && e.getClientRects().length);
  const rows = [...document.querySelectorAll('[data-e2e*="conversation-item"], [data-e2e*="chat-list-item"], [data-conversation-id], [data-conversationid], [data-e2e*="session-item"]')].filter(visible);
  const output = [];
  for (const row of rows) {
    const a = [...row.querySelectorAll('a[href*="/user/"]')].find(visible);
    const url = a ? a.href.split('?')[0].replace(/\/$/, '') : '';
    const id = row.getAttribute('data-conversation-id') || row.getAttribute('data-conversationid') || row.getAttribute('data-session-id') || '';
    const title = row.querySelector('[data-e2e*="name"], [class*="conversation-name"], [class*="session-name"], [class*="chat-name"], [title]');
    const name = (title?.getAttribute('title') || title?.textContent || a?.getAttribute('title') || a?.textContent || '').trim();
    const group = row.getAttribute('data-is-group') === 'true' || row.getAttribute('data-type') === 'group' || !!row.querySelector('[data-e2e*="group-avatar"], [data-e2e*="group-icon"]');
    const kind = group ? 'group' : url ? 'private' : 'unknown';
    if (name && (id || /^https:\/\/www\.douyin\.com\/user\/[A-Za-z0-9_-]+$/.test(url))) {
      output.push({name, kind, url, conversation_id: id});
    }
  }
  return output.slice(0, 2000);
})()
