"""Read-only local DOM fixture. Does not visit Douyin or use a real profile."""
from pathlib import Path
import tempfile
from playwright.sync_api import sync_playwright

ROOT=Path(__file__).resolve().parents[1]

def main():
    html='''<html><body>
    <div data-e2e="conversation-item" data-conversation-id="dm_1"><span data-e2e="chat-name">朋友甲</span><a href="https://www.douyin.com/user/peer_qa">个人主页</a><div>聊天正文不能抓取</div></div>
    <div data-e2e="chat-list-item" data-conversation-id="g_1" data-type="group"><span data-e2e="chat-name">同名群</span></div>
    <div data-e2e="chat-list-item" data-conversation-id="g_2" data-type="group"><span data-e2e="chat-name">同名群</span></div>
    <div data-e2e="session-item" data-conversation-id="pending"><span data-e2e="chat-name">待补主页</span></div>
    <div data-e2e="chat-list-item"><span data-e2e="chat-name">只有昵称</span></div>
    <div data-e2e="chat-list-item" data-conversation-id="hidden" style="display:none"><span data-e2e="chat-name">隐藏项</span></div>
    <textarea id="editor">untouched</textarea><script>window.sent=0;document.querySelector('textarea').addEventListener('input',()=>window.sent++);</script>
    </body></html>'''
    with tempfile.TemporaryDirectory(prefix='spark-conversation-dom-') as d, sync_playwright() as p:
        browser=p.chromium.launch(channel='msedge',headless=True)
        page=browser.new_page()
        page.route('**/*',lambda route:route.abort())
        page.set_content(html)
        rows=page.evaluate((ROOT/'assets/read_conversations.js').read_text('utf-8'))
        assert len(rows)==4,rows
        assert len([r for r in rows if r['kind']=='group'])==2
        assert rows[0]['url']=='https://www.douyin.com/user/peer_qa'
        assert rows[3]['kind']=='unknown'
        assert '聊天正文不能抓取' not in str(rows)
        assert page.locator('#editor').input_value()=='untouched'
        assert page.evaluate('window.sent')==0
        browser.close()
    print('PASS: visible private/group metadata, stable IDs, unknown profiles, same-name groups; no editor changes or network.')

if __name__=='__main__':main()
