"""Guided, cancellable following import. Never sends messages or replaces friends.json."""
import json
import os
from pathlib import Path
import re
import tempfile
import time
from urllib.parse import urlparse
from spark_support import data_dir

EVENT_PREFIX = 'FOLLOWING|'


def atomic_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp = tempfile.mkstemp(prefix=path.name + '.', suffix='.tmp', dir=path.parent)
    try:
        with os.fdopen(fd, 'w', encoding='utf-8') as f:
            json.dump(value, f, ensure_ascii=False)
        os.replace(tmp, path)
    finally:
        if os.path.exists(tmp):
            os.unlink(tmp)


class FollowingState:
    """Only an explicit successful final API page can confirm completeness."""
    def __init__(self):
        self.users = {}
        self.api_ids = set()
        self.api_seen = False
        self.complete = False
        self.empty_confirmed = False
        self.errors = 0
        self.total = None
        self.login_required = False

    def accept(self, payload):
        self.complete = False
        self.empty_confirmed = False
        if not isinstance(payload, dict):
            self.errors += 1
            return
        code = payload.get('status_code')
        if not (type(code) is int and code == 0 or code == '0'):
            self.errors += 1
            if str(code) in ('8', '401'):
                self.login_required = True
            return
        rows = payload.get('followings')
        if not isinstance(rows, list):
            self.errors += 1
            return
        self.api_seen = True
        self.login_required = False
        valid_rows = 0
        for row in rows:
            if not isinstance(row, dict):
                self.errors += 1
                continue
            sid = row.get('sec_uid')
            if not isinstance(sid, str) or not re.fullmatch(r'[A-Za-z0-9_-]+', sid):
                self.errors += 1
                continue
            valid_rows += 1
            self.api_ids.add(sid)
            self.users[sid] = {'name': str(row.get('nickname') or '未命名好友'),
                               'url': 'https://www.douyin.com/user/' + sid}
        total = payload.get('total')
        if isinstance(total, int) and not isinstance(total, bool) and total >= 0:
            self.total = total
        more = payload.get('has_more')
        final = more is False or (type(more) is int and more == 0)
        if final and self.errors == 0 and self.total is not None and len(self.api_ids) == self.total:
            self.complete = bool(self.users) and valid_rows == len(rows)
            self.empty_confirmed = not self.users and not rows and self.total == 0

    def add_dom(self, rows):
        for row in rows:
            if not isinstance(row, dict):
                continue
            match = re.fullmatch(r'https://www\.douyin\.com/user/([A-Za-z0-9_-]+)/?', row.get('url', ''))
            if match and row.get('name'):
                sid = match.group(1)
                self.users.setdefault(sid, {'name': row['name'], 'url': 'https://www.douyin.com/user/' + sid})

    def result(self, reason=''):
        if self.complete:
            self.users = {sid: row for sid, row in self.users.items() if sid in self.api_ids}
        status = ('success' if self.complete else 'partial') if self.users else (
            'empty' if self.empty_confirmed else 'login_required' if self.login_required else 'failed')
        messages = {
            'success': '已读取关注列表，请勾选要添加的好友。',
            'partial': '仅获取到部分关注对象，完整性未确认。可导入已读取的对象，或稍后重试；原名单未覆盖。',
            'empty': '接口确认当前账号暂无关注对象，原名单已保留。',
            'login_required': '尚未完成软件内登录。请重试，并在弹出的独立浏览器中扫码登录。',
            'failed': '未能确认关注列表。请检查网络、登录状态，或手动打开浏览器中的关注列表后重试。',
        }
        diagnostic = '%s 接口解析异常次数：%d；接口报告总数：%s。' % (reason, self.errors, self.total if self.total is not None else '未知')
        return {'status': status, 'message': messages[status], 'reason': diagnostic.strip(),
                'items': list(self.users.values()), 'count': len(self.users),
                'expected_total': self.total, 'parse_errors': self.errors}


def event(stage, message, count=0):
    print(EVENT_PREFIX + json.dumps({'stage': stage, 'message': message, 'count': count}, ensure_ascii=False), flush=True)


def collect(cancel_file=None, login_timeout=180, load_timeout=120, browser_config=None, prepare_context=None):
    if cancel_file is not None and Path(cancel_file).exists():
        return {'status': 'cancelled', 'message': '已取消读取，原名单未修改。', 'items': [], 'count': 0}
    from playwright.sync_api import sync_playwright
    from douyin_spark import browser_targets, read_browser_choice
    state = FollowingState()
    target = browser_config or browser_targets(read_browser_choice())
    cancelled = lambda: cancel_file is not None and Path(cancel_file).exists()
    event('opening', '正在打开软件专用浏览器；它与日常浏览器使用不同的登录状态。')
    with sync_playwright() as p:
        ctx = p.chromium.launch_persistent_context(
            user_data_dir=str(target['profile']), executable_path=target['exe'],
            channel=target['channel'], headless=target.get('headless', False), viewport={'width': 1200, 'height': 900})
        try:
            if prepare_context:
                prepare_context(ctx)
            page = ctx.pages[0] if ctx.pages else ctx.new_page()
            def on_response(resp):
                parsed = urlparse(resp.url)
                if parsed.hostname and (parsed.hostname == 'douyin.com' or parsed.hostname.endswith('.douyin.com')) and '/user/following/list/' in parsed.path:
                    try:
                        state.accept(resp.json())
                    except Exception:
                        state.errors += 1
                    event('reading', '正在读取关注列表：已发现 %d 人。' % len(state.users), len(state.users))
            ctx.on('response', on_response)
            page.goto('https://www.douyin.com/follow', wait_until='domcontentloaded', timeout=30000)
            event('login', '请在弹出的浏览器中扫码登录；日常 Edge/夸克的登录不会自动继承。最多等待 3 分钟。')
            deadline = time.monotonic() + login_timeout
            load_deadline = None
            last_change = time.monotonic()
            last_count = 0
            login_visible = False
            while time.monotonic() < deadline:
                if cancelled():
                    return {'status': 'cancelled', 'message': '已取消读取，原名单未修改。', 'items': [], 'count': 0}
                pages = [x for x in ctx.pages if not x.is_closed()]
                if not pages:
                    return {'status': 'cancelled', 'message': '浏览器已关闭，读取已取消，原名单未修改。', 'items': [], 'count': 0}
                page = pages[-1]
                page.wait_for_timeout(500)
                if state.complete or state.empty_confirmed:
                    break
                login_visible = (page.get_by_text(re.compile(r'扫码登录|手机号登录|登录后查看|登录抖音')).first.is_visible()
                                 or page.locator('[data-e2e="login-mask"], [data-e2e="login-dialog"]').first.is_visible())
                verification = page.get_by_text(re.compile(r'拖动滑块|完成验证|安全验证')).first.is_visible()
                if verification:
                    event('verification', '检测到安全验证，请在浏览器中手动完成；软件不会自动解验证码。', len(state.users))
                    continue
                if login_visible:
                    state.login_required = True
                    continue
                if load_deadline is None:
                    load_deadline = time.monotonic() + load_timeout
                    event('reading', '若未自动显示名单，请在浏览器中打开自己的关注列表。正在等待数据…')
                rows = page.evaluate('''() => {
                    const roots = [...document.querySelectorAll('[data-e2e*="follow-list"], [data-e2e*="following-list"]')];
                    return roots.flatMap(root => [...root.querySelectorAll('a[href*="/user/"]')]
                        .map(a => ({url: a.href.split('?')[0], name: (a.innerText || a.getAttribute('title') || '').trim()})));
                }''')
                state.add_dom(rows)
                if len(state.users) != last_count:
                    last_count = len(state.users)
                    last_change = time.monotonic()
                    event('reading', '已发现 %d 人，正在继续加载…' % last_count, last_count)
                page.evaluate('''() => {
                    const nodes = [...document.querySelectorAll('[data-e2e*="follow-list"], [data-e2e*="following-list"], [data-e2e*="follow-list"] *, [data-e2e*="following-list"] *')];
                    const el = nodes.find(e => e.clientHeight > 100 && e.scrollHeight > e.clientHeight + 20 && /auto|scroll/.test(getComputedStyle(e).overflowY));
                    (el || document.scrollingElement).scrollBy(0, 600);
                }''')
                if time.monotonic() >= load_deadline or (state.users and time.monotonic() - last_change >= 15):
                    break
            if not state.api_seen and login_visible:
                state.login_required = True
            return state.result('等待结束；未收到明确的完整列表信号。' if not state.complete else '')
        except Exception as exc:
            if state.users:
                state.complete = False
                return state.result('读取中断：' + str(exc)[:1500])
            raise
        finally:
            ctx.close()


def main(result_path=None, cancel_file=None):
    try:
        result = collect(cancel_file)
    except Exception as exc:
        text = str(exc)
        if 'executable doesn' in text.lower():
            message = '未找到所选浏览器。请安装 Edge/Chrome，或在“更多功能 → 浏览器选择”中切换。'
        elif 'ProcessSingleton' in text or 'SingletonLock' in text or 'user data directory is already in use' in text:
            message = '软件专用浏览器正被占用。请关闭软件打开的私信/登录窗口，稍后重试。'
        elif 'closed' in text.lower():
            message = '读取期间浏览器被关闭。原名单已保留，请重试。'
        else:
            message = '读取失败，请检查网络及所选浏览器后重试；原名单已保留。'
        result = {'status': 'failed', 'message': message, 'items': [], 'count': 0, 'reason': text[:2000]}
    if cancel_file and Path(cancel_file).exists():
        result = {'status': 'cancelled', 'message': '已取消读取，原名单未修改。', 'items': [], 'count': 0}
    if result['status'] == 'success' and result['items']:
        atomic_json(data_dir() / 'following.json', result['items'])
    if result_path:
        atomic_json(result_path, result)
    event(result['status'], result['message'], result['count'])
    return result


if __name__ == '__main__':
    main()
