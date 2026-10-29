import json
from pathlib import Path
import sys
import tempfile
import unittest
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
from spark_support import resolve_data_dir

class PortablePathsTests(unittest.TestCase):
    def test_fallback_and_sticky_route(self):
        with tempfile.TemporaryDirectory() as tmp:
            base=Path(tmp);source=base/'程序 中文';source.mkdir();local=base/'用户数据'
            (source/'friends.json').write_text('[{"name":"fake"}]',encoding='utf-8')
            (source/'logs').mkdir();(source/'logs/test.csv').write_text('fake history')
            target=resolve_data_dir(source,local,writable=lambda p:p!=source)
            self.assertNotEqual(target,source)
            self.assertEqual((target/'logs/test.csv').read_text(),'fake history')
            (target/'friends.json').write_text('[]')
            self.assertEqual(resolve_data_dir(source,local,writable=lambda p:True),target)
            self.assertEqual((target/'friends.json').read_text(),'[]')
            self.assertIn('fake',(source/'friends.json').read_text())

    def test_conflicting_fallback_is_not_overwritten(self):
        with tempfile.TemporaryDirectory() as tmp:
            source=Path(tmp)/'app';source.mkdir();local=Path(tmp)/'local'
            target=resolve_data_dir(source,local,writable=lambda p:p!=source)
            (target/'storage.ready').unlink();(target/'friends.json').write_text('preserve')
            with self.assertRaises(RuntimeError):resolve_data_dir(source,local,writable=lambda p:p!=source)
            self.assertEqual((target/'friends.json').read_text(),'preserve')

    def test_portable_folder_and_dual_write_failure(self):
        with tempfile.TemporaryDirectory() as tmp:
            source=Path(tmp)/'app';source.mkdir();local=Path(tmp)/'local'
            self.assertEqual(resolve_data_dir(source,local,writable=lambda p:True),source)
            with self.assertRaises(RuntimeError):resolve_data_dir(source,local,writable=lambda p:False)

if __name__=='__main__':unittest.main()
