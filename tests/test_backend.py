import asyncio
import csv
from datetime import datetime, timedelta
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import AsyncMock, Mock

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
import spark_support as support

class BackendTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.friends = [{'name': 'test1', 'url': 'https://www.douyin.com/user/local_example1'}, {'name': 'test2', 'url': 'https://www.douyin.com/user/local_example2'}]
        (self.root/'friends.json').write_text(json.dumps(self.friends), encoding='utf-8-sig')
        self.log = self.root/'logs'/'spark_log.csv'

    def tearDown(self):
        self.temp.cleanup()

    def run_cli(self, *args):
        env = dict(os.environ, DOUYIN_SPARK_DATA_DIR=str(self.root))
        return subprocess.run([sys.executable, str(ROOT/'douyin_spark.py'), *args], env=env, capture_output=True, text=True, encoding='utf-8')

    def test_fixed_message_does_not_require_pool(self):
        r = self.run_cli('--dry-run', '--message', '固定 "消息" 😀')
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual(r.stdout.count('固定 "消息" 😀'), 2)
        self.assertFalse(self.log.exists())

    def test_fixed_message_keeps_daily_dedup(self):
        support.write_rows(self.log, [dict(time=datetime.now().strftime('%Y-%m-%d %H:%M:%S'), friend='test1', url=self.friends[0]['url'], message='earlier', status='OK', note='')])
        r = self.run_cli('--dry-run', '--message', 'fixed')
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertIn('test1 今天已发送，跳过', r.stdout)
        self.assertEqual(r.stdout.count(' -> fixed'), 1)
        r = self.run_cli('--dry-run', '--message', 'fixed', '--force')
        self.assertEqual(r.stdout.count(' -> fixed'), 2)

    def test_invalid_links_are_skipped(self):
        (self.root/'friends.json').write_text(json.dumps(self.friends+[{'name':'missing','url':''}]),encoding='utf-8')
        r = self.run_cli('--dry-run','--message','fixed')
        self.assertEqual(r.returncode,0,r.stderr)
        self.assertIn('[待补主页]',r.stdout)
        self.assertEqual(r.stdout.count(' -> fixed'),2)

    def test_empty_message_rejected(self):
        self.assertNotEqual(self.run_cli('--dry-run','--message',' ').returncode,0)

    def test_overlapping_tasks_are_rejected(self):
        handle=support.acquire_task_lock(self.root/'task.lock')
        try:
            with self.assertRaises(RuntimeError):
                support.acquire_task_lock(self.root/'task.lock')
        finally:
            handle.close()
        support.acquire_task_lock(self.root/'task.lock').close()

    def test_archive_preserves_old_rows_and_is_idempotent(self):
        old=(datetime.now()-timedelta(days=40)).strftime('%Y-%m-%d %H:%M:%S')
        now=datetime.now().strftime('%Y-%m-%d %H:%M:%S')
        support.write_rows(self.log,[dict(time=old,message='old'),dict(time=now,message='today')])
        support.rotate_csv(self.log)
        support.rotate_csv(self.log)
        rows=[r for p in support.history_paths(self.log) for r in support.read_rows(p)]
        self.assertEqual(sorted(r['message'] for r in rows),['old','today'])
        self.assertEqual(len(support.read_rows(self.log)),1)

    def test_export_roundtrip_handles_multiline_and_formula(self):
        support.write_rows(self.log,[dict(time='2026-01-01',friend='=1+1',message='line1\nline2,"quote"')])
        target=self.root/'export.csv'
        support.export_history(self.log,target)
        rows=support.read_rows(target)
        self.assertEqual(rows[0]['message'],'line1\nline2,"quote"')
        self.assertEqual(rows[0]['friend'],"'=1+1")

    def test_history_cleanup_keeps_today_and_backup(self):
        support.write_rows(self.log,[dict(time='2020-01-01',message='old'),dict(time=datetime.now().strftime('%Y-%m-%d %H:%M:%S'),message='today',status='OK')])
        support.clear_history(self.log)
        self.assertEqual([r['message'] for r in support.read_rows(self.log)],['today'])
        self.assertTrue(list((self.log.parent/'history_backup').rglob('*.csv')))

    def test_captcha_requires_manual_wait(self):
        async def check():
            with unittest.mock.patch.object(support,'challenge_visible',AsyncMock(side_effect=[True,False,False,False])), unittest.mock.patch.object(support.asyncio,'sleep',AsyncMock()):
                page=Mock();page.is_closed.return_value=False
                show=Mock()
                self.assertTrue(await support.wait_for_verification(page,show))
                show.assert_called_once()
        asyncio.run(check())

    def test_captcha_cancel_stops(self):
        async def check():
            cancel=self.root/'cancel';cancel.touch()
            with unittest.mock.patch.object(support,'challenge_visible',AsyncMock(return_value=True)):
                page=Mock();page.is_closed.return_value=False
                self.assertFalse(await support.wait_for_verification(page,Mock(),cancel))
        asyncio.run(check())

if __name__ == '__main__':
    unittest.main()
