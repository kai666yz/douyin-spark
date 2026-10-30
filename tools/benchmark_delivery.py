"""Compare real old/new state machines on identical local pages, never on Douyin."""
import argparse,ast,asyncio,csv,json,subprocess,sys,tempfile,time
from pathlib import Path
from datetime import datetime
from playwright.async_api import async_playwright

ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from spark_private import send_private
from spark_delivery import Pace,warmup

HTML='''<!doctype html><meta charset="utf-8"><button onclick="document.querySelector('#editor').style.display='block'">私信</button><div id="editor" contenteditable="true" style="display:none;height:80px"></div><script>document.addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();const text=editor.innerText;editor.innerText='';setTimeout(()=>{const p=document.createElement('p');p.textContent=text;document.body.appendChild(p);},25);}})</script>'''

async def main(args):
    work=Path(tempfile.mkdtemp(prefix='delivery-benchmark-',dir=args.work_dir));(work/'logs').mkdir()
    old=subprocess.check_output(['git','show',args.baseline+':douyin_spark.py'],cwd=ROOT).decode('utf-8-sig')
    tree=ast.parse(old);main_node=next(n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name=='main');node=next(n for n in main_node.body if isinstance(n,ast.AsyncFunctionDef) and n.name=='send_one_async')
    async def verify(*a,**k):return True
    records=[];env={'time':time,'Path':Path,'datetime':datetime,'halted':[False],'wait_for_verification':verify,'append_log':lambda rows:None,'LOG_DIR':work/'logs','show_chrome_window':lambda *_:None,'browser_targets':lambda *_:{'title':'fixture'},'read_browser_choice':lambda:'msedge'}
    exec(compile(ast.Module(body=[node],type_ignores=[]),'old-state-machine','exec'),env)
    intercepted=[0]
    async with async_playwright() as p:
        browser=await p.chromium.launch(channel='msedge',headless=True)
        async def route(request):intercepted[0]+=1;await request.fulfill(status=200,content_type='text/html',body=HTML)
        context=await browser.new_context();await context.route('**/*',route);page=await context.new_page();await warmup(page)
        for variant in ['baseline','optimized_round1','optimized_round2']:
            pace=Pace();start=time.monotonic();ok=0
            for i in range(args.targets):
                friend={'name':f'fixture_{i}','url':f'https://www.douyin.com/user/fixture_{i}'};text=f'仅本地模拟问候 {i:02d}'
                began=time.monotonic()
                if variant=='baseline':state,note=await env['send_one_async'](page,friend,text)
                else:state,note=await send_private(page,friend,text,work,lambda *_:verify(),lambda:None,lambda:False,i==0)
                if state!='OK':raise AssertionError((variant,i,state,note))
                ok+=1;records.append({'variant':variant,'index':i+1,'time':datetime.now().isoformat(timespec='milliseconds'),'seconds':round(time.monotonic()-began,4),'status':state})
                if i<args.targets-1:await asyncio.sleep(2 if variant=='baseline' else pace.observe(state))
            elapsed=time.monotonic()-start;records.append({'variant':variant,'index':'total','time':datetime.now().isoformat(timespec='milliseconds'),'seconds':round(elapsed,4),'status':f'{ok}/{args.targets}'})
            print(variant,round(elapsed,2),'seconds',flush=True)
        await browser.close()
    baseline=next(r['seconds'] for r in records if r['index']=='total' and r['variant']=='baseline')
    totals=[r for r in records if r['index']=='total'];result={'targets':args.targets,'baseline_commit':args.baseline,'results':totals,'reduction_percent':round(100*(1-totals[1]['seconds']/baseline),2),'scope':'identical intercepted local pages; no real account/messages or platform verification assessment','requests_intercepted':intercepted[0]}
    with (work/'timestamps.csv').open('w',encoding='utf-8-sig',newline='') as f:w=csv.DictWriter(f,fieldnames=['variant','index','time','seconds','status']);w.writeheader();w.writerows(records)
    (work/'result.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8');print(json.dumps(result,ensure_ascii=False));print('Artifacts:',work)

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--targets',type=int,default=25);parser.add_argument('--baseline',default='e0b6cc8');parser.add_argument('--work-dir',type=Path);asyncio.run(main(parser.parse_args()))
