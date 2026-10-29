import contextlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import fetch_following as f


def api(rows, total=None, more=False):
    value = {'status_code': 0, 'followings': [{'sec_uid': x, 'nickname': x} for x in rows], 'has_more': more}
    if total is not None:
        value['total'] = total
    return value


class FollowingTests(unittest.TestCase):
    def test_complete_pages_deduplicate(self):
        state = f.FollowingState()
        state.accept(api(['a', 'b'], 3, True))
        self.assertEqual(state.result()['status'], 'partial')
        state.accept(api(['b', 'c'], 3))
        self.assertEqual(state.result()['status'], 'success')
        self.assertEqual(state.result()['count'], 3)

    def test_empty_requires_explicit_evidence(self):
        for payload in ({'status_code': 0}, api([]), api([], 5), {'status_code': 8}):
            state = f.FollowingState()
            state.accept(payload)
            self.assertNotEqual(state.result()['status'], 'empty')
        state = f.FollowingState()
        state.accept(api([], 0))
        self.assertEqual(state.result()['status'], 'empty')

    def test_missing_page_schema_error_or_unknown_total_is_partial(self):
        for payload in (api(['a'], 3), api(['a'])):
            state = f.FollowingState()
            state.accept(payload)
            self.assertEqual(state.result()['status'], 'partial')
        state = f.FollowingState()
        state.accept({'status_code': 0, 'followings': [{'sec_uid': 'bad/url'}], 'has_more': 1})
        state.accept(api(['a'], 1))
        self.assertEqual(state.result()['status'], 'partial')

    def test_dom_never_proves_completeness_or_includes_foreign_urls(self):
        state = f.FollowingState()
        state.add_dom([{'name': '合法', 'url': 'https://www.douyin.com/user/a'}, {'name': '错', 'url': 'https://evil.test/user/b'}])
        self.assertEqual(state.result()['status'], 'partial')
        self.assertEqual(state.result()['count'], 1)
        state.accept(api(['b'], 2))
        self.assertEqual(state.result()['status'], 'partial')

    def test_failed_empty_partial_and_cancel_preserve_previous_files(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            old = b'[{"name":"keep","url":"https://www.douyin.com/user/old"}]'
            for name in ('friends.json', 'following.json'):
                (root / name).write_bytes(old)
            for status in ('failed', 'empty', 'partial', 'login_required', 'cancelled'):
                result = {'status': status, 'message': status, 'count': 0, 'items': []}
                with patch.object(f, 'collect', return_value=result), patch.object(f, 'data_dir', return_value=root), contextlib.redirect_stdout(io.StringIO()):
                    f.main(root / 'result.json')
                for name in ('friends.json', 'following.json'):
                    self.assertEqual((root / name).read_bytes(), old)
                self.assertEqual(json.loads((root / 'result.json').read_text('utf-8'))['status'], status)

    def test_success_updates_cache_only_and_cancel_wins(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            (root / 'friends.json').write_text('[]')
            state = f.FollowingState()
            state.accept(api(['a'], 1))
            with patch.object(f, 'collect', return_value=state.result()), patch.object(f, 'data_dir', return_value=root), contextlib.redirect_stdout(io.StringIO()):
                f.main(root / 'result.json')
                self.assertEqual((root / 'friends.json').read_text(), '[]')
                before = (root / 'following.json').read_bytes()
                (root / 'cancel').touch()
                result = f.main(root / 'result.json', root / 'cancel')
                self.assertEqual(result['status'], 'cancelled')
                self.assertEqual((root / 'following.json').read_bytes(), before)

    def test_exception_becomes_actionable_result(self):
        with tempfile.TemporaryDirectory() as d, patch.object(f, 'collect', side_effect=RuntimeError("Executable doesn't exist")), contextlib.redirect_stdout(io.StringIO()):
            result = f.main(Path(d) / 'result.json')
            self.assertEqual(result['status'], 'failed')
            self.assertIn('浏览器', result['message'])

    def test_later_error_invalidates_completeness(self):
        state = f.FollowingState()
        state.accept(api(['a'], 1))
        self.assertEqual(state.result()['status'], 'success')
        state.accept({'status_code': 0, 'unexpected': []})
        self.assertEqual(state.result()['status'], 'partial')


if __name__ == '__main__':
    unittest.main()
