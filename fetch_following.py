# -*- coding: utf-8 -*-
"""
抖音关注列表抓取（最终版）
1. 复用软件所选浏览器的独立登录态打开 https://www.douyin.com/follow
2. 监听 following/list API，滚动左栏加载全部
3. 提取 nickname + sec_uid → following.json
"""
import sys, time, json
from pathlib import Path
from playwright.sync_api import sync_playwright
from douyin_spark import browser_targets, read_browser_choice

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

BASE = Path(__file__).parent
TARGET = browser_targets(read_browser_choice())
PROFILE = TARGET["profile"]
OUT = BASE / "following.json"


def log(*a):
    print(*a, flush=True)


all_users = {}
stats = {"last": 0, "stall": 0}

with sync_playwright() as p:
    ctx = p.chromium.launch_persistent_context(
        user_data_dir=str(PROFILE),
        executable_path=TARGET["exe"],
        channel=TARGET["channel"],
        headless=False,
        args=["--window-position=100,60", "--disable-blink-features=AutomationControlled"],
        viewport={"width": 1200, "height": 900},
    )
    page = ctx.pages[0] if ctx.pages else ctx.new_page()

    def on_resp(resp):
        if "/web/user/following/list/" in resp.url and resp.request.resource_type in ("xhr", "fetch"):
            try:
                j = resp.json()
                for u in (j.get("followings") or []):
                    sid = u.get("sec_uid") or ""
                    nick = (u.get("nickname") or "").strip()
                    if sid and sid not in all_users:
                        all_users[sid] = {"name": nick, "url": "https://www.douyin.com/user/" + sid}
            except Exception:
                pass

    page.on("response", on_resp)
    page.goto("https://www.douyin.com/follow", wait_until="domcontentloaded", timeout=30000)
    time.sleep(6)
    page.mouse.move(150, 400)
    for i in range(30):
        page.mouse.wheel(0, 600)
        time.sleep(1.3)
        if len(all_users) == stats["last"]:
            stats["stall"] += 1
            if stats["stall"] >= 4 and i > 5:
                break
        else:
            stats["stall"] = 0
            stats["last"] = len(all_users)
    ctx.close()

result = list(all_users.values())
OUT.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
log(f"FOUND {len(result)} 位关注对象")
for r in result[:15]:
    log("  ", r["name"], "|", r["url"][:60])
print("DONE")
