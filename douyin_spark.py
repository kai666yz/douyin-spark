# -*- coding: utf-8 -*-
"""
抖音续火花本地脚本
- 遍历使用者配置的好友主页
- 每人发一句不重样的废话文学
- 结果写 CSV 日志
- 第一次运行扫码登录，之后复用浏览器profile
用法：
  python douyin_spark.py                # 正式跑
  python douyin_spark.py --dry-run      # 只抽句子不发
  python douyin_spark.py --friend 示例好友  # 只跑某个人
  python douyin_spark.py --no-sleep     # 不间隔（调试用，慎用）
"""
import json, csv, os, sys, time, random, argparse, re
from datetime import datetime
from pathlib import Path
from spark_delivery import (warmup, sending_slot, safety_stop, Pace, interruptible_delay, DailyPhrases, timing)
from spark_private import send_private
from spark_support import (VERSION, data_dir, history_paths, rotate_csv, export_history, clear_history, wait_for_verification, acquire_task_lock)

# 修复 exe 启动子进程时 GBK 编码崩溃（好友名含 emoji 如 🐷🦀）
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

BASE = data_dir()
PROFILE_DIR = BASE / "browser_profile"
CONFIG_FILE = BASE / "config.json"

def read_browser_choice():
    """读取浏览器选择：chrome / msedge，默认 chrome"""
    try:
        cfg = json.loads(CONFIG_FILE.read_text(encoding="utf-8"))
        b = str(cfg.get("browser", "chrome")).lower()
        return b if b in ("chrome", "msedge", "quark") else "chrome"
    except Exception:
        return "chrome"

def browser_targets(browser):
    """查找当前用户的浏览器安装，使用独立 profile 保存登录态。"""
    browser = browser if browser in ("chrome", "msedge", "quark") else "chrome"
    relative = {
        "msedge": Path("Microsoft/Edge/Application/msedge.exe"),
        "chrome": Path("Google/Chrome/Application/chrome.exe"),
        "quark": Path("Programs/Quark/quark.exe"),
    }[browser]
    roots = [os.environ.get(key) for key in ("LOCALAPPDATA", "ProgramFiles", "ProgramFiles(x86)")]
    import shutil
    exe = next((str(Path(d) / relative) for d in roots if d and (Path(d) / relative).is_file()), None)
    exe = exe or shutil.which({'msedge':'msedge.exe','chrome':'chrome.exe','quark':'quark.exe'}[browser])
    if browser == "quark" and not exe:
        raise RuntimeError("没有找到夸克浏览器，请安装夸克或切换至 Edge/Chrome。")
    return {
        "channel": None if browser == "quark" else browser,
        "exe": exe,
        "profile": BASE / {"chrome": "browser_profile", "msedge": "browser_profile_edge", "quark": "browser_profile_quark"}[browser],
        "title": {"chrome": "Google Chrome", "msedge": "Microsoft Edge", "quark": "Quark"}[browser],
    }

LOG_DIR = BASE / "logs"
FRIENDS_FILE = BASE / "friends.json"
POOL_FILE = BASE / "sentences_pool.json"
LOG_FILE = LOG_DIR / "spark_log.csv"

LOG_DIR.mkdir(exist_ok=True)

def show_chrome_window(title_key="Google Chrome"):
    """登录需要扫码时，把屏幕外的浏览器窗口移回屏幕中央（只移动，不影响 playwright）"""
    try:
        import ctypes
        from ctypes import wintypes
        user32 = ctypes.windll.user32

        @ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
        def callback(hwnd, lParam):
            if not user32.IsWindowVisible(hwnd):
                return True
            length = user32.GetWindowTextLengthW(hwnd)
            if length <= 0 or length > 100:
                return True
            buf = ctypes.create_unicode_buffer(length + 1)
            user32.GetWindowTextW(hwnd, buf, length + 1)
            title = buf.value
            if title_key in title and "about:blank" not in title:
                user32.SetWindowPos(hwnd, 0, 100, 100, 1200, 800, 0x0040)  # SWP_SHOWWINDOW
                return False
            return True

        user32.EnumWindows(callback, 0)
    except Exception:
        pass

def load_json(p):
    from spark_support import read_json
    return read_json(p)

def pick_sentences(n=23, avoid_days=1, on_date=None):
    """抽句子：按启用的分类抽，当天有节日主题则优先用节日句"""
    pool = load_json(POOL_FILE)
    # 兼容旧格式：无 categories 时用 sentences
    cats = pool.get("categories", {})
    active = pool.get("active_categories", [])
    if cats and active:
        sentences = []
        for c in active:
            sentences.extend(cats.get(c, []))
        if not sentences:
            sentences = pool.get("sentences", [])
    else:
        sentences = pool.get("sentences", [])
    holiday = pool.get("holiday", {})
    d = on_date or datetime.now()
    today_key = d.strftime("%m-%d")
    used = set()
    cutoff = datetime.now().timestamp() - avoid_days * 86400
    for history in history_paths(LOG_FILE):
        with open(history, "r", encoding="utf-8-sig", newline="") as f:
            for row in csv.DictReader(f):
                try:
                    ts = datetime.strptime(row["time"], "%Y-%m-%d %H:%M:%S").timestamp()
                    if (row["time"].startswith(d.strftime("%Y-%m-%d")) if avoid_days==1 else ts>=cutoff):
                        used.add(row["message"])
                except (ValueError, KeyError):
                    pass
    avail = [s for s in sentences if s not in used]
    random.seed()
    random.shuffle(avail)
    picks = []
    themed = holiday.get(today_key, [])
    if themed:
        themed_avail = [s for s in themed if s not in used]
        random.shuffle(themed_avail)
        picks.extend(themed_avail[:n])
        if themed_avail:
            print(f"  [节日] 今天是 {d.strftime('%m-%d')}，用节日主题句", flush=True)
    if len(picks) < n:
        picks.extend(avail[: n - len(picks)])
    return picks[:n]

def already_sent_today():
    """返回今天已发送成功的好友 url 集合（查重用）"""
    sent = set()
    if not LOG_FILE.exists():
        return sent
    today = datetime.now().strftime("%Y-%m-%d")
    try:
        with open(LOG_FILE, "r", encoding="utf-8-sig", newline="") as f:
            for row in csv.DictReader(f):
                try:
                    if row.get("time", "").startswith(today) and row.get("status", "") in ("OK", "UNCONFIRMED"):
                        sent.add(row.get("url", "").rstrip("/"))
                except Exception:
                    continue
    except Exception:
        pass
    return sent

def append_log(rows):
    rotate_csv(LOG_FILE)
    new = not LOG_FILE.exists()
    with open(LOG_FILE, "a", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f)
        if new:
            w.writerow(["time", "friend", "url", "message", "status", "note"])
        for r in rows:
            w.writerow(r)

def write_daily_report(rows):
    """发送完成后生成每日日报（logs/日报/YYYY-MM-DD.txt）"""
    try:
        d = LOG_DIR / "日报"
        d.mkdir(exist_ok=True)
        today = datetime.now().strftime("%Y-%m-%d")
        fp = d / (today + ".txt")
        ok_rows = [r for r in rows if r[4] == "OK"]
        fail_rows = [r for r in rows if r[4] != "OK"]
        lines = []
        lines.append("抖音续火花 每日发送日报")
        lines.append("时间：" + datetime.now().strftime("%Y-%m-%d %H:%M:%S"))
        lines.append("=" * 42)
        lines.append(f"总人数：{len(rows)}    成功：{len(ok_rows)}    失败/异常：{len(fail_rows)}")
        lines.append("")
        lines.append("【成功发送】")
        for r in ok_rows:
            lines.append(f"  {r[1]:<18}{r[3]}")
        if fail_rows:
            lines.append("")
            lines.append("【失败/异常】")
            for r in fail_rows:
                lines.append(f"  {r[1]:<18}[{r[4]}] {r[5]}")
        with open(fp, "w", encoding="utf-8") as f:
            f.write("\n".join(lines))
        print(f"日报已生成：{fp}")
    except Exception as e:
        print(f"日报生成失败: {e}")

def send_one(page, friend, msg, dry=False, cancel_file=None):
    name, url = friend["name"], friend["url"]
    try:
        if cancel_file and Path(cancel_file).exists():
            return ("CANCELLED", "已取消后续发送")
        page.goto(url, wait_until="domcontentloaded", timeout=30000)
        page.wait_for_timeout(2500)
        cur = page.url
        if "login" in cur or "passport" in cur:
            return ("LOGIN_EXPIRED", "登录态失效，请重新扫码")
        # 找私信按钮（主页上的，非聊天窗内）
        btn = None
        for loc in [
            page.get_by_role("button", name="私信"),
            page.locator('div[class*="button"]:has-text("私信"):visible'),
            page.locator('text="私信"').first,
        ]:
            try:
                if loc.count() > 0 and loc.first.is_visible():
                    btn = loc.first
                    break
            except Exception:
                continue
        if btn is None:
            return ("NO_BTN", "没找到私信按钮")
        if dry:
            return ("OK_DRY", msg)

        def _find_editor(timeout_s=16):
            """跨所有 frame 轮询找可见输入框"""
            deadline = time.monotonic() + timeout_s
            while time.monotonic() < deadline:
                for f in page.frames:
                    try:
                        ed = f.locator('div[contenteditable="true"]:visible')
                        if ed.count() > 0:
                            return ed.first
                    except Exception:
                        continue
                page.wait_for_timeout(1200)
            return None

        def _open_chat():
            # 点私信按钮，若被遮挡则重试一次
            try:
                btn.click(timeout=5000)
            except Exception:
                try:
                    btn.click(force=True, timeout=5000)
                except Exception:
                    page.reload(wait_until="domcontentloaded")
                    page.wait_for_timeout(3000)
                    b2 = page.get_by_role("button", name="私信")
                    b2.first.click(timeout=8000)
            return _find_editor(16)

        editor = _open_chat()
        if editor is None:
            # 第一次没开出来：刷新页面再试一次
            page.reload(wait_until="domcontentloaded")
            page.wait_for_timeout(3000)
            b3 = page.get_by_role("button", name="私信")
            if b3.count() > 0:
                b3.first.click(timeout=8000)
            editor = _find_editor(16)
        if editor is None:
            return ("NO_INPUT", "聊天窗未弹出或找不到输入框")
        if cancel_file and Path(cancel_file).exists():
            return ("CANCELLED", "已取消后续发送")
        editor.click()
        page.wait_for_timeout(500)
        editor.type(msg, delay=40)
        page.wait_for_timeout(400)
        if cancel_file and Path(cancel_file).exists():
            return ("CANCELLED", "已取消，未按下发送键")
        page.keyboard.press("Enter")
        page.wait_for_timeout(2200)
        return ("OK", msg)
    except Exception as e:
        return ("ERROR", str(e)[:120])

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", action="version", version=VERSION)
    ap.add_argument("--message", help="对所有选中好友使用固定消息，跳过句子池与句子去重；仍保留当天好友去重")
    ap.add_argument("--fetch-following", action="store_true", help="读取关注列表，不发送消息")
    ap.add_argument("--following-result", type=Path, help="本次关注列表读取结果文件")
    ap.add_argument("--export-history", type=Path, help="导出全部 CSV 历史记录，不发送消息")
    ap.add_argument("--clear-history", action="store_true", help="清理旧历史并备份，保留今日记录")
    ap.add_argument("--runtime-check", action="store_true", help="只用本地 HTML 检查浏览器运行环境")
    ap.add_argument("--environment-check", action="store_true", help="输出后端、数据目录与浏览器依赖信息，不登录、不发送")
    ap.add_argument("--dry-run", action="store_true", help="只抽句子不发")
    ap.add_argument("--selftest", action="store_true", help="打开浏览器登录+验证选择器，不发消息")
    ap.add_argument("--friend", help="只跑指定好友名")
    ap.add_argument("--force", action="store_true", help="忽略当天查重，强制重发")
    ap.add_argument("--no-sleep", action="store_true", help="不等待")
    ap.add_argument("--friends-file", type=Path, default=FRIENDS_FILE, help="本次选中的好友名单")
    ap.add_argument("--cancel-file", type=Path, help="存在时停止后续发送")
    ap.add_argument("--group-run", type=Path, help="软件生成的群聊会话任务（仅本机网页）")
    ap.add_argument("--preview-group", action="store_true", help="只预览群聊消息来源，不启动网页")
    ap.add_argument("--stop-after-current", type=Path, help="当前联系人处理完后停止，剩余联系人记录为已取消")
    args = ap.parse_args()
    if args.message is not None and not args.message.strip():
        ap.error("--message 不能是空白文本")
    if args.group_run:
        from spark_group import main as group_main
        group_main(BASE,args.group_run,args.stop_after_current)
        return
    if args.preview_group:
        from spark_delivery import group_candidates
        plan=load_json(BASE / "group_task.json")
        mode,candidates=group_candidates(BASE,plan)
        print(json.dumps({"mode":mode,"sample":candidates[:3],"count":len(candidates)},ensure_ascii=False))
        return
    task_handle = None
    if not args.dry_run and not args.runtime_check and not args.export_history and not args.environment_check:
        try:
            task_handle = acquire_task_lock(BASE / ('.spark-task.lock' if args.fetch_following else '.spark-private-run.lock'))
        except RuntimeError as exc:
            ap.error(str(exc))
    if args.fetch_following:
        from fetch_following import main as fetch_main
        fetch_main(args.following_result, args.cancel_file)
        return
    if args.export_history:
        export_history(LOG_FILE, args.export_history)
        return
    if args.environment_check:
        import sys
        import importlib.metadata
        browser = read_browser_choice()
        try:
            target=browser_targets(browser)
            browser_found=bool(target['exe'])
        except RuntimeError:
            browser_found=False
        print(json.dumps({'app_version':VERSION,'python_version':sys.version.split()[0], 'playwright_version':importlib.metadata.version('playwright'),'data_dir':str(BASE),'browser':browser,'browser_found':browser_found,'frozen':bool(getattr(sys,'frozen',False))},ensure_ascii=False))
        return
    if args.clear_history:
        clear_history(LOG_FILE)
        return
    if args.runtime_check:
        from playwright.sync_api import sync_playwright
        with sync_playwright() as p:
            tg = browser_targets(read_browser_choice())
            options = {"headless": True}
            if tg['channel']:
                options['channel'] = tg['channel']
            if tg['exe']:
                options['executable_path'] = tg['exe']
            browser = p.chromium.launch(**options)
            page = browser.new_page()
            page.set_content('<h1>Local runtime check</h1>')
            assert page.locator('h1').inner_text() == 'Local runtime check'
            browser.close()
        print('运行环境检查通过：未访问抖音，未发送消息。')
        return
    if not args.dry_run and not args.selftest and (BASE/".spark-safety-stop.json").exists():
        raise RuntimeError("发送已安全暂停，请在软件中完成人工检查并确认恢复。")
    if not args.dry_run:
        rotate_csv(LOG_FILE)
    if not args.friends_file.exists():
        print("好友名单不存在，请先在软件中添加好友，或创建 friends.json。")
        return
    friends = load_json(args.friends_file)
    if not isinstance(friends, list):
        raise ValueError("好友名单必须是 JSON 数组，原文件未修改")
    valid = []
    for friend in friends:
        if (isinstance(friend, dict) and friend.get('kind') != 'group' and isinstance(friend.get('name'), str)
                and isinstance(friend.get('url'), str)
                and re.fullmatch(r'https://www\.douyin\.com/user/[A-Za-z0-9_-]+/?', friend['url'])):
            valid.append(friend)
        else:
            label = friend.get('name','未命名') if isinstance(friend,dict) else '无效条目'
            print(f'[待补主页] {label}：本次跳过，不猜测链接。', flush=True)
    friends = valid
    if not friends:
        if args.selftest:
            friends = [{'name': '登录检查', 'url':'https://www.douyin.com/'}]
        else:
            print("没有可发送的好友，请先添加完整主页链接。")
            return
    if args.cancel_file and args.cancel_file.exists():
        print("任务已取消。")
        return
    # 好友去重：按主页链接去重，防重复发送同一人
    seen_url = set()
    dedup = []
    for f in friends:
        u = f["url"].rstrip("/")
        if u in seen_url:
            print(f"  [去重] 跳过重复好友：{f['name']}")
            continue
        seen_url.add(u)
        dedup.append(f)
    friends = dedup
    if args.friend:
        friends = [f for f in friends if args.friend in f["name"]]
        if not friends:
            print(f"没找到好友：{args.friend}")
            return
    # 当天查重：已成功发过的好友跳过（--friend 单发补发 / --force 除外）
    if not args.friend and not args.force:
        sent_today = already_sent_today()
        if sent_today:
            remain = [f for f in friends if f["url"].rstrip("/") not in sent_today]
            for f in friends:
                if f["url"].rstrip("/") in sent_today:
                    print(f"  [查重] {f['name']} 今天已发送，跳过")
            friends = remain
            if not friends:
                print("\n今天已全部发送完成，无需重复发送。")
                return

    if not args.dry_run:
        print(f"TASK|PLANNED|{len(friends)}", flush=True)
    msgs = [args.message] * len(friends) if args.message is not None else pick_sentences(n=len(friends))
    if len(msgs) < len(friends):
        print(f"警告：句子池只剩 {len(msgs)} 句，不够 {len(friends)} 人")

    print("=" * 50)
    print(f"时间：{datetime.now():%Y-%m-%d %H:%M:%S}")
    print(f"人数：{len(friends)}  模式：{'DRY-RUN' if args.dry_run else '正式发送'}")
    print("=" * 50)
    for f, m in zip(friends, msgs):
        print(f"  {f['name']:<16} -> {m}")

    if args.dry_run:
        print("\n[dry-run] 不启动浏览器，不发送。")
        return

    phrase_candidates=pick_sentences(n=100000) if args.message is None else []
    if args.message is None and not args.selftest:
        if not phrase_candidates: raise ValueError("今天可用语录为空，请增加分类或使用固定消息。")
        msgs=[""]*len(friends)

    from playwright.sync_api import sync_playwright
    if args.selftest:
        with sync_playwright() as p:
            tg = browser_targets(read_browser_choice())
            launch_kw = dict(
                user_data_dir=str(tg["profile"]),
                executable_path=tg["exe"],
                headless=False,
                viewport={"width": 1280, "height": 800},
                args=["--disable-blink-features=AutomationControlled", "--window-position=-32000,-32000"],
            )
            if tg["channel"]:
                launch_kw["channel"] = tg["channel"]
            ctx = p.chromium.launch_persistent_context(**launch_kw)
            page = ctx.pages[0] if ctx.pages else ctx.new_page()
            page.goto("https://www.douyin.com/", wait_until="domcontentloaded")
            print("浏览器已打开。如果跳到登录页，请扫码登录。")
            # 自动等登录完成（最多120秒）
            need_login = False
            for _ in range(120):
                if "login" not in page.url and "passport" not in page.url:
                    break
                need_login = True
                page.wait_for_timeout(1000)
            if need_login:
                print("登录状态: NEED_LOGIN", flush=True)
                show_chrome_window(tg["title"])
                print("已把浏览器移到屏幕中央，请扫码登录（最多等待90秒）", flush=True)
                for _ in range(90):
                    if "login" not in page.url and "passport" not in page.url:
                        need_login = False
                        break
                    page.wait_for_timeout(1000)
            if not need_login:
                print("登录状态: OK", flush=True)
            test_url = friends[0]["url"]
            print(f"\n导航到 {friends[0]['name']} 主页: {test_url}")
            page.goto(test_url, wait_until="domcontentloaded")
            page.wait_for_timeout(3000)
            print(f"当前URL: {page.url}")
            selectors = {
                "button[私信]": page.get_by_role("button", name="私信").count(),
                "div[私信文本]": page.locator('div:has-text("私信"):visible').count(),
                "contenteditable": page.locator('div[contenteditable="true"]:visible').count(),
            }
            for k, v in selectors.items():
                print(f"  选择器 {k}: {v} 个命中")
            shot = LOG_DIR / "selftest.png"
            page.screenshot(path=str(shot))
            print(f"\n截图已存: {shot}")
            print("自检完成，浏览器保持打开3秒后关闭...")
            page.wait_for_timeout(3000)
            ctx.close()
        return

    # ---------- 正式发送：逐位处理，支持当前联系人结束后停止 ----------
    import asyncio
    from playwright.async_api import async_playwright

    CONCURRENCY = 1  # 遇到验证时整批任务暂停，避免其他并发任务继续发送
    halted = [False]
    done_count = [0]            # 已处理人数（进度上报）
    total_count = [len(friends)]  # 总人数（第一轮）

    first_contact=[True]
    pace=Pace()
    phrases=DailyPhrases(BASE)
    def message_candidates():
        pool=load_json(POOL_FILE);cats=pool.get('categories',{});active=pool.get('active_categories',[])
        normal=[s for key in active for s in cats.get(key,[])] if cats and active else pool.get('sentences',[])
        normal=normal or pool.get('sentences',[])
        used=phrases.state()
        themed=pool.get('holiday',{}).get(datetime.now().strftime('%m-%d'),[])
        themed=[s for s in themed if isinstance(s,str) and phrases.digest(s) not in used]
        return themed or normal
    async def send_one_async(page, friend, msg, cancel_file=None):
        async def verify(current):
            result=await wait_for_verification(current,lambda:show_chrome_window(browser_targets(read_browser_choice())['title']),cancel_file)
            if not result:safety_stop(BASE,'安全验证未完成')
            return result
        def journal():
            append_log([[f"{datetime.now():%Y-%m-%d %H:%M:%S}",friend['name'],friend['url'],msg,'UNCONFIRMED','提交前记录；中断后请人工确认']])
        first=first_contact[0];first_contact[0]=False
        return await send_private(page,friend,msg,BASE,verify,journal,lambda:bool(cancel_file and Path(cancel_file).exists()),first)

    def soft_stopped():
        return bool(args.stop_after_current and args.stop_after_current.exists())

    async def worker(ctx, group, cancel_file=None, delay=0):
        if delay > 0:
            await asyncio.sleep(delay)
        page = await ctx.new_page()
        page.set_default_timeout(15000)
        out = []
        for f, m in group:
            if halted[0] or soft_stopped() or (BASE/".spark-safety-stop.json").exists():
                break
            if cancel_file and Path(cancel_file).exists():
                break
            async with sending_slot(BASE,lambda:soft_stopped() or (BASE/".spark-safety-stop.json").exists()) as acquired:
                if not acquired:break
                owner=None
                try:
                    if args.message is None:m,owner=phrases.reserve(message_candidates())
                    status,note=await send_one_async(page,f,m,cancel_file)
                except Exception as ex:status,note='ERROR',str(ex)[:120]
                finally:
                    if owner:phrases.finish(m,owner,status in ('OK','UNCONFIRMED'))
            out.append((f, m, status, note))
            append_log([[f"{datetime.now():%Y-%m-%d %H:%M:%S}", f['name'], f['url'], m, status, note]])
            print(f"RESULT|{f['url']}|{status}", flush=True)
            flag = "✓" if status == "OK" else "✗"
            print(f"  {flag} {f['name']:<16} [{status}] {note}", flush=True)
            done_count[0] += 1
            print(f"PROGRESS|{done_count[0]}|{total_count[0]}|{f['name']}|{status}", flush=True)
            if status in ("LOGIN_EXPIRED", "CAPTCHA", "RATE_LIMIT", "STOPPED"):
                halted[0] = True
                if status in ("RATE_LIMIT","CAPTCHA","LOGIN_EXPIRED"):safety_stop(BASE,"页面需要人工处理："+status)
                break
            delay=pace.observe(status)
            timing(BASE,'private','result',0,status=status,delay=delay,done=done_count[0])
            if not args.no_sleep and done_count[0]<total_count[0]:
                if not await interruptible_delay(delay,soft_stopped):break
        try:
            await page.close()
        except Exception:
            pass
        return out

    async def retry_failed(ctx, failed, cancel_file=None):
        """只对未提交的失败执行有限退避，不重发结果不确定的消息。"""
        out = []
        for f, m, old_status, old_note in failed:
            status, note = old_status, old_note
            for attempt in range(2):
                if halted[0] or soft_stopped() or (cancel_file and Path(cancel_file).exists()):
                    break
                print(f"[重试 {attempt+1}/2] 等待 {2**(attempt+1)} 秒：{f['name']}", flush=True)
                await asyncio.sleep(2**(attempt+1))
                if soft_stopped():
                    break
                async with sending_slot(BASE,lambda:soft_stopped() or (BASE/".spark-safety-stop.json").exists()) as acquired:
                    if not acquired:break
                    page = await ctx.new_page();page.set_default_timeout(15000);owner=None
                    try:
                        if args.message is None:m,owner=phrases.reserve(message_candidates())
                        status,note=await send_one_async(page,f,m,cancel_file)
                    except Exception as ex:status,note='ERROR',str(ex)[:120]
                    finally:
                        if owner:phrases.finish(m,owner,status in ('OK','UNCONFIRMED'))
                        await page.close()
                append_log([[f"{datetime.now():%Y-%m-%d %H:%M:%S}", f['name'], f['url'], m, status, note]])
                print(f"RESULT|{f['url']}|{status}", flush=True)
                if status not in ('ERROR','NO_BTN','NO_INPUT'):
                    break
            out.append((f,m,status,note))
        return out

    async def run():
        async with async_playwright() as p:
            tg = browser_targets(read_browser_choice())
            launch_kw = dict(
                user_data_dir=str(tg["profile"]),
                executable_path=tg["exe"],
                headless=False,
                viewport={"width": 1200, "height": 760},
                args=["--disable-blink-features=AutomationControlled", "--window-position=-32000,-32000"],
            )
            if tg["channel"]:
                launch_kw["channel"] = tg["channel"]
            ctx = await p.chromium.launch_persistent_context(**launch_kw)
            login_page = ctx.pages[0] if ctx.pages else await ctx.new_page()
            warmed=await warmup(login_page)
            timing(BASE,"private","warmup",warmed)
            if "login" in login_page.url or "passport" in login_page.url:
                print("\n*** 需要登录：已把 Chrome 窗口移到屏幕中央，请扫码登录，程序会自动继续 ***", flush=True)
                show_chrome_window(tg["title"])
                for _ in range(180):
                    if soft_stopped() or (args.cancel_file and args.cancel_file.exists()):
                        break
                    if "login" not in login_page.url and "passport" not in login_page.url:
                        break
                    await login_page.wait_for_timeout(1000)
                if "login" in login_page.url or "passport" in login_page.url:
                    raise RuntimeError("登录未完成，未开始发送；请完成扫码后重试。")
                print("登录检测通过，开始发送。", flush=True)
            await login_page.close()
            # 窗口已通过 --window-position 置于屏幕外，全程不抢前台

            # 保持单路处理；停止请求只在联系人边界生效
            groups = [[] for _ in range(CONCURRENCY)]
            for i, (f, m) in enumerate(zip(friends, msgs)):
                groups[i % CONCURRENCY].append((f, m))
            results = await asyncio.gather(*[
                worker(ctx, g, args.cancel_file, delay=i * 3.0) for i, g in enumerate(groups)
            ])

            # 第一轮失败的好友自动补发一轮（登录失效/取消除外）
            failed = [
                (f, m, s, n)
                for ch in results for f, m, s, n in ch
                if s in ("ERROR", "NO_BTN", "NO_INPUT")
            ]
            if failed and not halted[0] and not soft_stopped():
                print(f"\n第一轮完成，{len(failed)} 人未成功，开始补发...", flush=True)
                retried = await retry_failed(ctx, failed, args.cancel_file)
                flat = [item for ch in results for item in ch]
                retry_map = {f["url"].rstrip("/"): (f, m, s, n) for f, m, s, n in retried}
                flat = [retry_map.get(item[0]["url"].rstrip("/"), item) for item in flat]
                results = [flat]

            if soft_stopped():
                completed = {f["url"].rstrip("/") for chunk in results for f, _, _, _ in chunk}
                cancelled = [(f, m, "CANCELLED", "用户停止，未开始处理") for f, m in zip(friends, msgs) if f["url"].rstrip("/") not in completed]
                for f, m, state, note in cancelled:
                    append_log([[f"{datetime.now():%Y-%m-%d %H:%M:%S}", f["name"], f["url"], m, state, note]])
                results.append(cancelled)
                print(f"TASK|CANCELLED|{len(cancelled)}", flush=True)
            await ctx.close()
            return results

    batch_started=time.monotonic()
    results = asyncio.run(run())
    timing(BASE,"private","batch",time.monotonic()-batch_started,targets=len(friends),concurrency=1)
    rows = []
    ok, fail, cancelled = 0, 0, 0
    for chunk in results:
        for f, m, status, note in chunk:
            ts = f"{datetime.now():%Y-%m-%d %H:%M:%S}"
            rows.append([ts, f["name"], f["url"], m, status, note])
            if status == "OK":
                ok += 1
            elif status == "CANCELLED":
                cancelled += 1
            else:
                fail += 1
    write_daily_report(rows)

    print("\n" + "=" * 50)
    print(f"完成：成功 {ok}，失败 {fail}，已取消 {cancelled}")
    print(f"日志：{LOG_FILE}")

if __name__ == "__main__":
    main()
