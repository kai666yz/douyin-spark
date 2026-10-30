import asyncio
import ast
from datetime import datetime
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import AsyncMock,patch

from spark_delivery import DailyPhrases,Pace,group_candidates,sending_slot,until
from spark_support import write_rows
from spark_support import read_json


class DeliveryTests(unittest.TestCase):
    def setUp(self):self.tmp=tempfile.TemporaryDirectory();self.root=Path(self.tmp.name)
    def tearDown(self):self.tmp.cleanup()
    def test_ready_condition_has_no_initial_sleep(self):
        async def check():
            condition=AsyncMock(return_value='ready')
            self.assertEqual(await until(condition,20,.3),'ready');self.assertEqual(condition.await_count,1)
        asyncio.run(check())
    def test_condition_timeout_is_bounded(self):
        async def check():
            self.assertIsNone(await until(AsyncMock(return_value=False),.02,.01))
        asyncio.run(check())
    def test_pacing_floor_and_failure_reset(self):
        p=Pace()
        self.assertEqual(p.observe('ERROR'),2.5)
        values=[p.observe('OK') for _ in range(30)]
        self.assertTrue(all(v>=.8 for v in values));self.assertEqual(values[-1],.8)
        self.assertEqual(p.observe('UNCONFIRMED'),2.5)
    def test_daily_reservation_is_shared_and_resets_by_calendar_day(self):
        a=DailyPhrases(self.root,'2026-10-30');text,owner=a.reserve(['A','B'],'rotation');self.assertEqual(text,'A')
        b=DailyPhrases(self.root,'2026-10-30');self.assertEqual(b.reserve(['A','B'],'rotation')[0],'B')
        with self.assertRaises(ValueError):a.reserve(['A','B'])
        self.assertEqual(DailyPhrases(self.root,'2026-10-31').reserve(['A','B'],'rotation')[0],'A')
    def test_unsubmitted_phrase_can_be_reused_but_uncertain_cannot(self):
        p=DailyPhrases(self.root);text,owner=p.reserve(['A']);p.finish(text,owner,False)
        text,owner=p.reserve(['A']);p.finish(text,owner,True)
        with self.assertRaises(ValueError):p.reserve(['A'])
    def test_a_batch_crossing_midnight_resets_the_day(self):
        with patch('spark_delivery.datetime') as clock:
            clock.now.return_value=datetime(2026,10,30,23,59,59);p=DailyPhrases(self.root);text,owner=p.reserve(['A'])
            clock.now.return_value=datetime(2026,10,31,0,0,1);new_text,new_owner=p.reserve(['A']);self.assertEqual(new_text,'A');p.finish(text,owner,True)
            self.assertEqual(json.loads((self.root/'logs/phrase_usage_2026-10-30.json').read_text())['559aead08264d5795d3909718cdd05abd49572e84fe55590eef31a88a08fdffd']['state'],'used')
    def test_existing_private_journal_participates_in_phrase_dedup(self):
        write_rows(self.root/'logs/spark_log.csv',[{'time':datetime.now().strftime('%Y-%m-%d %H:%M:%S'),'message':'A','status':'UNCONFIRMED'}])
        self.assertEqual(DailyPhrases(self.root).reserve(['A','B'])[0],'B')
    def test_private_holiday_priority_survives_the_shared_phrase_ledger(self):
        script=Path(__file__).resolve().parents[1]/'douyin_spark.py'
        tree=ast.parse(script.read_text('utf-8-sig'));main=next(n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name=='main');node=next(n for n in main.body if isinstance(n,ast.FunctionDef) and n.name=='message_candidates')
        pool=self.root/'sentences_pool.json';pool.write_text(json.dumps({'categories':{'regular':['normal']},'active_categories':['regular'],'holiday':{datetime.now().strftime('%m-%d'):['holiday']}}))
        phrases=DailyPhrases(self.root);scope={'load_json':read_json,'POOL_FILE':pool,'phrases':phrases,'datetime':datetime}
        exec(compile(ast.Module(body=[node],type_ignores=[]),'private-candidates','exec'),scope)
        self.assertEqual(scope['message_candidates'](),['holiday']);text,owner=phrases.reserve(['holiday']);phrases.finish(text,owner,True);self.assertEqual(scope['message_candidates'](),['normal'])
    def test_group_default_categories_are_neutral_and_modes_work(self):
        (self.root/'sentences_pool.json').write_text(json.dumps({'categories':{'废话文学':['A'],'土味情话':['not_default']}}),encoding='utf-8')
        self.assertEqual(group_candidates(self.root,{}),('random',['A']))
        self.assertEqual(group_candidates(self.root,{'message':'legacy'}),('fixed',['legacy']))
        self.assertEqual(group_candidates(self.root,{'message_mode':'rotation','custom_messages':['A','A','B']}),('rotation',['A','B']))
    def test_private_and_group_share_one_slot(self):
        async def check():
            active=0;maximum=0;seen=[]
            async def job(kind):
                nonlocal active,maximum
                async with sending_slot(self.root,lambda:False) as acquired:
                    self.assertTrue(acquired);active+=1;maximum=max(maximum,active);seen.append(kind);await asyncio.sleep(.02);active-=1
            await asyncio.gather(job('private'),job('group'));self.assertEqual(maximum,1);self.assertEqual(len(seen),2)
        asyncio.run(check())
    def test_stop_while_waiting_for_slot_never_acquires(self):
        async def check():
            async with sending_slot(self.root,lambda:True) as acquired:self.assertFalse(acquired)
        asyncio.run(check())
    def test_safety_latch_prevents_another_task_from_starting(self):
        (self.root/'.spark-safety-stop.json').write_text('{}')
        async def check():
            with self.assertRaises(RuntimeError):
                async with sending_slot(self.root,lambda:False):self.fail('must not send')
        asyncio.run(check())

if __name__=='__main__':unittest.main()
