"""Test draft replacement on intercepted Edge pages; never log in or send real messages."""
import argparse
import asyncio
import contextlib
import io
import json
from pathlib import Path
import sys
import tempfile

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
from playwright.async_api import async_playwright
from spark_private import send_private
from spark_delivery import visible_editor

HTML='''<!doctype html><meta charset="utf-8">
<button onclick="editor.style.display='block'">私信</button>
<div id="editor" contenteditable="true" style="display:none;min-height:60px"></div>
<section id="history"></section>
<script>
const scenario=__SCENARIO__;
if(scenario.wrongTarget)history.replaceState(null,'','/user/fixture_draft_other');
const oldDraft=scenario.draft;
const editor=document.querySelector('#editor');editor.innerText=oldDraft;
window.inputs=[];window.submissions=[];
editor.addEventListener('input',()=>{inputs.push(editor.innerText);if(scenario.rejectClear&&!editor.innerText.trim())editor.innerText=oldDraft;});
editor.addEventListener('keydown',event=>{if(event.key==='Enter'){
 event.preventDefault();const value=editor.innerText;submissions.push(value);editor.innerText='';
 const bubble=document.createElement('p');bubble.textContent=value;document.querySelector('#history').appendChild(bubble);
}});
</script>'''

async def main(args):
    work=Path(tempfile.mkdtemp(prefix='private-drafts-',dir=args.work_dir))
    cases=[
        {'name':'stale_draft','draft':'不应发出的旧草稿','expected':'OK'},
        {'name':'same_message_left_by_interruption','draft':'已保存消息 1','expected':'OK'},
        {'name':'empty_editor','draft':'','expected':'OK'},
        {'name':'clear_rejected','draft':'清空失败仍保留','rejectClear':True,'expected':'DRAFT_CLEAR_FAILED'},
        {'name':'stop_before_clear','draft':'停止时保留的草稿','stop':True,'expected':'CANCELLED'},
        {'name':'verification_before_clear','draft':'验证时不修改的草稿','verification':True,'expected':'CAPTCHA'},
        {'name':'wrong_profile_with_matching_prefix','draft':'错误目标不能清理','wrongTarget':True,'expected':'ERROR'},
    ]
    results=[];requests=0
    async with async_playwright() as pw:
        browser=await pw.chromium.launch(channel='msedge',headless=True)
        try:
            for case in cases:
                context=await browser.new_context()
                async def intercept(route):
                    nonlocal requests
                    requests+=1
                    await route.fulfill(status=200,content_type='text/html',body=HTML.replace('__SCENARIO__',json.dumps(case,ensure_ascii=False)))
                await context.route('**/*',intercept)
                page=await context.new_page();journal=[];stop=False;checks=0;output=io.StringIO()
                async def verify(_):
                    nonlocal stop,checks
                    checks+=1
                    if checks==2:
                        if case.get('stop'):stop=True
                        if case.get('verification'):return False
                    return True
                message='已保存消息 1'
                with contextlib.redirect_stdout(output):
                    status,note=await send_private(page,{'name':'本地测试','url':'https://www.douyin.com/user/fixture_draft'},message,work,verify,lambda:journal.append('before-submit'),lambda:stop)
                submitted=await page.evaluate('window.submissions')
                inputs=await page.evaluate('window.inputs')
                assert status==case['expected'],(case['name'],status,note)
                if status=='OK':
                    assert submitted==[message] and journal==['before-submit'],(case['name'],submitted,journal)
                    assert message in inputs,(case['name'],inputs)
                    if case['draft']:assert not inputs[0].strip(),(case['name'],inputs)
                    assert ('DRAFT_CLEARED' in output.getvalue())==bool(case['draft'])
                else:
                    assert submitted==[] and journal==[],(case['name'],submitted,journal)
                    assert await page.locator('#editor').inner_text()==case['draft']
                assert case['draft'] not in output.getvalue() if case['draft'] else True
                results.append({'case':case['name'],'status':status,'submitted':len(submitted)})
                await context.close()
            context=await browser.new_context();page=await context.new_page()
            await page.set_content('<div contenteditable="true">A</div><div contenteditable="true">B</div><iframe srcdoc=\'<div contenteditable="true">C</div>\'></iframe>')
            await page.locator('iframe').content_frame.locator('[contenteditable=true]').wait_for()
            assert await visible_editor(page,.05) is None,'ambiguous editors across frames must never select a clearing target'
            results.append({'case':'ambiguous_editors_across_frames','status':'NO_TARGET','submitted':0})
            await context.close()
        finally:
            await browser.close()
    result={'status':'PASS','cases':results,'requests_intercepted':requests,'scope':'all requests intercepted; no real account, messages, or personal data'}
    (work/'result.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(result,ensure_ascii=False));print('Artifacts:',work)

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--work-dir',type=Path)
    asyncio.run(main(parser.parse_args()))
