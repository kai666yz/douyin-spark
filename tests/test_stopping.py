"""Exercise the actual send coordinator with local substitutes for browser I/O."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
HARNESS = r'''
import ast, asyncio, json, os, sys, types
from pathlib import Path
sys.path.insert(0, sys.argv[1])
root=Path(os.environ['DOUYIN_SPARK_DATA_DIR'])
tree=ast.parse((Path(sys.argv[1])/'douyin_spark.py').read_text('utf-8-sig'))
main=next(n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name=='main')
for index,node in enumerate(main.body):
    if isinstance(node,ast.AsyncFunctionDef) and node.name=='send_one_async':
        main.body[index]=ast.parse('async def send_one_async(page, friend, msg, cancel_file=None):\n    return await fake_send(friend, msg)').body[0]
ast.fix_missing_locations(tree)
calls=[]
async def fake_send(friend, message):
    calls.append(friend['name'])
    if os.environ.get('TEST_STOP')=='during':
        (root/'stop.flag').write_text('stop')
        await asyncio.sleep(0)  # stop occurs while the current contact is processing
    return ('OK','local fixture; no network')
class Page:
    url='https://www.douyin.com/'
    async def goto(self,*a,**kw): pass
    async def wait_for_load_state(self,*a,**kw): pass
    async def wait_for_timeout(self,*a): pass
    async def close(self): pass
    def set_default_timeout(self,*a): pass
class Context:
    def __init__(self): self.pages=[Page()]
    async def new_page(self): return Page()
    async def close(self): pass
class Playwright:
    def __init__(self): self.chromium=self
    async def launch_persistent_context(self,**kw): return Context()
    async def __aenter__(self): return self
    async def __aexit__(self,*a): pass
module=types.ModuleType('playwright.async_api');module.async_playwright=Playwright
sys.modules['playwright.async_api']=module
namespace={'__name__':'fixture','__file__':str(Path(sys.argv[1])/'douyin_spark.py'),'fake_send':fake_send}
exec(compile(tree,'send-fixture','exec'),namespace)
namespace['browser_targets']=lambda *_:{'profile':root/'fake_profile','exe':'never launched','channel':None,'title':'fake'}
sys.argv=['fixture','--message','test fixed','--no-sleep','--stop-after-current',str(root/'stop.flag')]
namespace['main']()
print('FIXTURE_CALLS='+json.dumps(calls))
'''

class StoppingTests(unittest.TestCase):
    def check_flow(self, mode):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder)
            friends=[{'name':str(i),'url':f'https://www.douyin.com/user/fixture_{i}'} for i in range(4)]
            (root/'friends.json').write_text(json.dumps(friends))
            if mode=='before': (root/'stop.flag').write_text('stop')
            run=subprocess.run([sys.executable,'-c',HARNESS,str(ROOT)],env=dict(os.environ,DOUYIN_SPARK_DATA_DIR=folder,TEST_STOP=mode),capture_output=True,text=True,encoding='utf-8',timeout=20)
            self.assertEqual(run.returncode,0,run.stderr)
            import csv
            with (root/'logs/spark_log.csv').open(encoding='utf-8-sig',newline='') as file: rows=list(csv.DictReader(file))
            return run.stdout,rows

    def test_stop_during_contact_keeps_current_and_cancels_rest(self):
        output,rows=self.check_flow('during')
        self.assertIn('FIXTURE_CALLS=["0"]',output)
        self.assertEqual([r['status'] for r in rows],['OK','CANCELLED','CANCELLED','CANCELLED'])
        self.assertIn('TASK|CANCELLED|3',output)

    def test_stop_before_first_contact_cancels_everyone(self):
        output,rows=self.check_flow('before')
        self.assertIn('FIXTURE_CALLS=[]',output)
        self.assertEqual([r['status'] for r in rows],['CANCELLED']*4)

    def test_normal_run_is_unchanged(self):
        output,rows=self.check_flow('none')
        self.assertEqual([r['status'] for r in rows],['OK']*4)
        self.assertNotIn('TASK|CANCELLED|',output)

if __name__=='__main__': unittest.main()
