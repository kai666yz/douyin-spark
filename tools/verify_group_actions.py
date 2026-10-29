"""Offline group action fixtures: all HTTP fulfilled locally; no account or real send."""
from pathlib import Path
import json
import sys
from playwright.sync_api import sync_playwright
ROOT=Path(__file__).resolve().parents[1]
HTML='''<!doctype html><meta charset="utf-8"><style>[data-e2e=conversation-item]{padding:12px}#panel{padding:20px}#editor{border:1px solid;height:80px}</style>
<div data-e2e="im-dialog"><div data-e2e="conversation-item" data-conversation-id="group:111" data-type="group" onmousedown="openGroup('group:111')">同名群</div>
<div data-e2e="conversation-item" data-conversation-id="group:222" data-type="group" onclick="openGroup('group:222')">同名群</div>
<div data-e2e="conversation-item" data-conversation-id="private:333" data-type="private">同名群</div></div>
<section id="panel" data-conversation-id="group:111" data-type="group"><div id="editor" contenteditable="true"></div><button id="send" onclick="window.testSent.push({id:panel.dataset.conversationId,text:editor.innerText});editor.innerHTML=''">发送</button></section>
<script>window.testSent=[];window.openGroup=id=>{document.querySelector('#panel').dataset.conversationId=id};</script>'''
def main():
    sys.stdout.reconfigure(encoding='utf-8')
    script=(ROOT/'assets/group_actions.js').read_text('utf-8')
    with sync_playwright() as p:
        browser=p.chromium.launch(channel='msedge',headless=True)
        page=browser.new_page();page.route('**/*',lambda r:r.fulfill(status=200,content_type='text/html',body=HTML));page.goto('https://www.douyin.com/')
        def call(phase,id='group:111',message='你好\n测试 "引号" \\ 😀'):
            return page.evaluate(script,dict(phase=phase,id=id,message=message))['status']
        assert call('select')=='OPENED'
        assert call('inspect')=='READY'
        assert call('fill')=='FILLED'
        assert call('submit')=='SUBMITTED'
        assert page.evaluate('window.testSent.length')==1
        assert call('submit')=='DRAFT_CHANGED'
        assert page.evaluate('window.testSent.length')==1
        assert call('select','group:222')=='OPENED'
        assert call('fill','group:111')=='WRONG_ACTIVE_GROUP'
        assert call('inspect','group:222')=='READY'
        page.eval_on_selector('#editor',"e=>e.innerText='用户原有草稿'")
        assert call('fill','group:222')=='DRAFT_PRESENT'
        assert page.locator('#editor').inner_text()=='用户原有草稿'
        page.eval_on_selector('#editor',"e=>e.innerHTML=''")
        page.eval_on_selector('#panel',"e=>e.removeAttribute('data-conversation-id')")
        assert call('fill','group:222')=='UNVERIFIED_ACTIVE_GROUP'
        page.eval_on_selector('#panel',"e=>e.dataset.conversationId='group:222'")
        page.eval_on_selector('#editor',"e=>{const conversation=Object.create({id:'private:333',type:1});Object.defineProperty(conversation,'messages',{get(){throw Error('forbidden')}});e.__reactFiber$qa={memoizedProps:{conversation}};}")
        assert call('fill','group:222')=='WRONG_ACTIVE_GROUP'
        page.eval_on_selector('#editor',"e=>{delete e.__reactFiber$qa;e.__reactFiber$qa={memoizedProps:{curConversation:Object.create({id:'group:222',type:2})}}}")
        assert call('fill','group:222')=='FILLED'
        page.eval_on_selector('#send',"e=>e.disabled=true")
        assert call('submit','group:222')=='SEND_BUTTON_UNAVAILABLE'
        assert call('select','private:333')=='GROUP_NOT_LOADED'
        page.evaluate("document.body.insertAdjacentHTML('beforeend','<div data-e2e=conversation-item data-type=group data-conversation-id=group:222>重复标识</div>')")
        assert call('select','group:222')=='AMBIGUOUS_GROUP'
        page.goto('https://example.com/')
        assert call('fill')=='UNTRUSTED_PAGE'
        browser.close()
    print('PASS: exact group IDs, same names, private exclusion, composer identity, stale React context, draft protection, one submission, missing/disabled button, ambiguous IDs and untrusted origin; all traffic intercepted.')
if __name__=='__main__':main()
