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
        # Regression: actual PC IM rows have no IDs/links in HTML. Their model
        # lives on the row's React parent fiber and uses inherited SDK getters.
        page.set_content('''<div data-e2e="im-dialog" style="display:none">
        <div data-e2e="conversation-item" id="private"><div class="conversationConversationItemtitle">真实结构私信</div><pre>禁止读取的聊天正文</pre></div>
        <div data-e2e="conversation-item" id="group"><div class="conversationConversationItemtitle">同名群</div><a href="https://www.douyin.com/user/wrong_group_peer">成员主页</a></div>
        <div data-e2e="conversation-item" id="group2"><div class="conversationConversationItemtitle">同名群</div></div>
        <div data-e2e="conversation-item" id="pending"><div class="conversationConversationItemtitle">待补主页</div></div>
        <div data-e2e="conversation-item" data-index="99"><div class="conversationConversationItemtitle">仅昵称不可导入</div></div>
        </div><div data-e2e="conversation-item" id="unrelated" style="display:none"><div class="conversationConversationItemtitle">无关隐藏项</div></div><textarea id="editor">untouched</textarea>''')
        page.evaluate('''() => {
            window.privateBodyReads=0;window.credentialReads=0;
            const attach=(id,cid,type,sec)=>{
                const proto={get type(){return type},get lastMessage(){window.privateBodyReads++;throw Error('message body access')},get ticket(){window.credentialReads++;throw Error('credential access')}};
                const inherited=Object.create(proto);Object.defineProperty(inherited,'id',{get:()=>cid});
                const model=Object.create(inherited);Object.defineProperty(model,'toParticipantSecUserId',{get:()=>sec});
                document.getElementById(id).__reactFiber$fixture={memoizedProps:{children:[]},return:{memoizedProps:{conversation:model}}};
            };
            attach('private','0:1:9007199254740993123:200',1,'sec_real_private');
            attach('group','9007199254740993124',2,'should_never_be_a_private_target');
            attach('group2','9007199254740993125',2,'');
            attach('pending','pending_model',1,'');attach('unrelated','unrelated_model',1,'hidden_peer');
        }''')
        rows=page.evaluate((ROOT/'assets/read_conversations.js').read_text('utf-8'))
        assert len(rows)==4,rows
        assert rows[0]['url']=='https://www.douyin.com/user/sec_real_private'
        assert rows[0]['conversation_id']=='0:1:9007199254740993123:200'
        assert len([r for r in rows if r['kind']=='group' and r['url']==''])==2
        assert len(set(r['conversation_id'] for r in rows))==4
        assert rows[3]['kind']=='unknown'
        report=page.evaluate('window.__sparkConversationReadReport')
        assert report['hiddenLoaded']==5 and report['missingIdentity']==1,report
        assert page.evaluate('window.privateBodyReads + window.credentialReads')==0
        assert page.locator('#editor').input_value()=='untouched'
        assert '禁止读取的聊天正文' not in str(rows)
        browser.close()
    print('PASS: visible DOM and actual PC IM structure; hidden mounted rows, inherited getters, exact long IDs, groups, unknown profiles; no message/credential reads, editor changes or network.')

if __name__=='__main__':main()
