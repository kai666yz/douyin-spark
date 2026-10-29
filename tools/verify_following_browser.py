"""Local browser smoke tests. All HTTP requests are intercepted; no Douyin login or sending."""
import contextlib
import io
import json
from pathlib import Path
import sys
import tempfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from fetch_following import collect


def run():
    with tempfile.TemporaryDirectory(prefix='spark-import-qa-') as folder:
        root = Path(folder)
        for mode, expected in [('success', 'success'), ('empty', 'empty'), ('partial', 'partial'),
                               ('login', 'login_required'), ('schema', 'failed'), ('cancel', 'cancelled')]:
            cancel = root / (mode + '.cancel')
            if mode == 'cancel':
                cancel.touch()
            calls = []
            def prepare(ctx):
                def route(request):
                    url = request.request.url
                    calls.append(url)
                    if '/user/following/list/' in url:
                        payload = {'status_code': 0, 'followings': [] if mode == 'empty' else [{'sec_uid': 'qa_only', 'nickname': '测试关注'}],
                                   'total': 0 if mode == 'empty' else 2 if mode == 'partial' else 1, 'has_more': False}
                        if mode == 'schema':
                            payload = {'status_code': 0, 'changed_schema': []}
                        request.fulfill(status=200, content_type='application/json', body=json.dumps(payload))
                    else:
                        body = '<html><body>扫码登录</body></html>' if mode == 'login' else '<html><body>本地测试<script>fetch("/aweme/v1/web/user/following/list/")</script></body></html>'
                        request.fulfill(status=200, content_type='text/html; charset=utf-8', body=body)
                ctx.route('**/*', route)
            config = {'profile': root / mode, 'exe': None, 'channel': 'msedge', 'headless': True}
            with contextlib.redirect_stdout(io.StringIO()):
                result = collect(cancel, login_timeout=2, load_timeout=1, browser_config=config, prepare_context=prepare)
            assert result['status'] == expected, (mode, result)
            assert bool(calls) == (mode != 'cancel'), mode
            print(mode + ': ' + expected)
        print('PASS: all browser traffic intercepted; no account login or message send.')


if __name__ == '__main__':
    run()
