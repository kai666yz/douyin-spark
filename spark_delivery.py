"""Condition waits, one shared sending slot, daily phrase reservations and timing."""
import asyncio
from contextlib import asynccontextmanager
from datetime import datetime
import hashlib
import json
import os
from pathlib import Path
import random
import time
import uuid

from spark_support import acquire_task_lock, read_json, read_rows


async def until(check, timeout=20, interval=.3, stop=None):
    """Try immediately; use a bounded polling interval only while not ready."""
    deadline=time.monotonic()+timeout
    while True:
        if stop and stop():
            return None
        value=await check()
        if value:
            return value
        left=deadline-time.monotonic()
        if left<=0:
            return None
        await asyncio.sleep(min(interval,left))


async def visible_editor(page, timeout=20):
    async def check():
        found=[]
        for frame in page.frames:
            try:
                nodes=frame.locator('[contenteditable="true"]:visible, textarea[data-e2e*="chat"]:visible')
                if await nodes.count()==1:
                    found.append(nodes.first)
            except Exception:
                continue
        return found[0] if len(found)==1 else None
    return await until(check,timeout)


async def message_count(page, text):
    count=0
    for frame in page.frames:
        try:
            # Exclude the draft itself from the message evidence.
            nodes=frame.get_by_text(text,exact=True)
            for i in range(await nodes.count()):
                if await nodes.nth(i).is_visible() and await nodes.nth(i).evaluate("el => !el.closest('[contenteditable=true], textarea, input')"):
                    count+=1
        except Exception:
            continue
    return count


async def confirm_message(page,text,before,timeout=5):
    async def check():
        return await message_count(page,text)>before
    return bool(await until(check,timeout,.3))


async def warmup(page):
    """A streaming home page may never become network-idle; keep a bounded fallback."""
    start=time.monotonic()
    await page.goto('https://www.douyin.com/',wait_until='domcontentloaded',timeout=30000)
    try:
        await page.wait_for_load_state('networkidle',timeout=5000)
    except Exception:
        await page.locator('body').wait_for(state='visible',timeout=10000)
    return time.monotonic()-start


class Pace:
    """Application pacing, not a promise to avoid the platform's verification."""
    def __init__(self): self.streak=0; self.seconds=1.5
    def observe(self,status):
        if status=='OK':
            self.streak+=1
            self.seconds=max(.8,1.5-.15*(self.streak//3))
        else:
            self.streak=0;self.seconds=2.5
        return self.seconds


async def interruptible_delay(seconds,stop):
    deadline=time.monotonic()+seconds
    while time.monotonic()<deadline:
        if stop and stop(): return False
        await asyncio.sleep(min(.3,max(0,deadline-time.monotonic())))
    return not (stop and stop())


@asynccontextmanager
async def sending_slot(root,stop):
    """Private and group backends share exactly one OS-owned slot across processes."""
    handle=None
    while handle is None:
        if stop and stop():
            yield False
            return
        if (Path(root)/'.spark-safety-stop.json').exists():raise RuntimeError('安全暂停，请人工处理后恢复。')
        try: handle=acquire_task_lock(Path(root)/'.spark-send-slot.lock')
        except RuntimeError: await asyncio.sleep(.1)
    try:
        if (Path(root)/'.spark-safety-stop.json').exists():raise RuntimeError('安全暂停尚未解除，本轮未继续发送。')
        if stop and stop(): yield False
        else: yield True
    finally: handle.close()


def safety_stop(root,reason):
    atomic_json(Path(root)/'.spark-safety-stop.json',{'time':datetime.now().isoformat(timespec='seconds'),'reason':reason})
    print('ATTENTION|PAUSED|'+reason+'；其他任务也已暂停，请人工处理后确认恢复。',flush=True)


def atomic_json(path,value):
    path=Path(path);path.parent.mkdir(parents=True,exist_ok=True)
    tmp=path.with_name(path.name+'.'+uuid.uuid4().hex+'.tmp')
    try:
        tmp.write_text(json.dumps(value,ensure_ascii=False),encoding='utf-8')
        os.replace(tmp,path)
    finally: tmp.unlink(missing_ok=True)


class DailyPhrases:
    def __init__(self,root,day=None):
        self.root=Path(root);self.fixed_day=day is not None;self.day=day or datetime.now().strftime('%Y-%m-%d')
        self.path=self.root/'logs'/('phrase_usage_'+self.day+'.json')
    @staticmethod
    def digest(text): return hashlib.sha256(text.encode('utf-8')).hexdigest()
    def state(self):
        if not self.fixed_day:
            self.day=datetime.now().strftime('%Y-%m-%d');self.path=self.root/'logs'/('phrase_usage_'+self.day+'.json')
        state=read_json(self.path) if self.path.exists() else {}
        if not isinstance(state,dict): raise ValueError('语录使用记录损坏，原文件保留，未猜测空记录。')
        # Import existing successful/uncertain private records when upgrading.
        for row in read_rows(self.root/'logs/spark_log.csv'):
            if row.get('time','').startswith(self.day) and row.get('status') in ('OK','UNCONFIRMED'):
                state.setdefault(self.digest(row.get('message','')),{'state':'used','owner':'history'})
        for path in (self.root/'logs/group_attempts').glob(self.day.replace('-','')+'_*.json'):
            entry=read_json(path)
            if entry.get('status') in ('OK','SUBMITTED','UNCONFIRMED','CONFIRMED') and entry.get('message_hash'):
                state.setdefault(entry['message_hash'],{'state':'used','owner':'group_history'})
        return state
    def reserve(self,candidates,mode='random'):
        # Sending-slot ownership ensures cross-process phrase transactions are serialized.
        state=self.state();available=list(dict.fromkeys(s.strip() for s in candidates if isinstance(s,str) and s.strip() and len(s)<=2000))
        available=[text for text in available if self.digest(text) not in state]
        if not available: raise ValueError('所选语录今天已用完；请增加分类或语录，不会重复凑数。')
        text=available[0] if mode=='rotation' else random.choice(available)
        owner=self.day+'_'+uuid.uuid4().hex;state[self.digest(text)]={'state':'reserved','owner':owner}
        atomic_json(self.path,state)
        return text,owner
    def finish(self,text,owner,submitted):
        if not self.fixed_day or owner[:10]!=self.day:
            return DailyPhrases(self.root,owner[:10]).finish(text,owner,submitted)
        state=self.state();key=self.digest(text)
        if state.get(key,{}).get('owner')!=owner:return
        if submitted: state[key]['state']='used'
        else: state.pop(key,None)
        atomic_json(self.path,state)


def group_candidates(root,plan):
    mode=plan.get('message_mode') or ('fixed' if plan.get('message','').strip() else 'random')
    if mode=='fixed':
        text=plan.get('message','').strip()
        if not text or len(text)>2000:raise ValueError('请填写1～2000字的固定消息。')
        return mode,[text]
    if mode=='rotation':
        rows=plan.get('custom_messages',[])
        if not isinstance(rows,list):raise ValueError('自定义语录必须为多条文本。')
        values=list(dict.fromkeys(x.strip() for x in rows if isinstance(x,str) and x.strip() and len(x)<=2000))
    elif mode=='random':
        pool=read_json(Path(root)/'sentences_pool.json');cats=pool.get('categories',{})
        selected=plan.get('categories') or [x for x in ['废话文学','早安问候'] if x in cats]
        if not isinstance(selected,list) or any(x not in cats for x in selected):raise ValueError('群聊语录分类无效，请重新勾选。')
        values=list(dict.fromkeys(text for key in selected for text in cats[key] if isinstance(text,str) and text.strip() and len(text)<=2000))
    else:raise ValueError('群聊消息模式无效。')
    if not values:raise ValueError('所选分类或自定义语录为空。')
    return mode,values


def timing(root,kind,stage,seconds,**details):
    record={'time':datetime.now().isoformat(timespec='milliseconds'),'kind':kind,'stage':stage,'seconds':round(seconds,4),**details}
    path=Path(root)/'logs/performance.jsonl';path.parent.mkdir(parents=True,exist_ok=True)
    # Best effort telemetry must not change a delivery result.
    try:
        if path.exists() and path.stat().st_size>2*1024*1024:
            os.replace(path,path.with_suffix('.jsonl.1'))
        with path.open('a',encoding='utf-8') as file:file.write(json.dumps(record,ensure_ascii=False)+'\n')
    except OSError:pass


async def failure_snapshot(page,root,kind):
    folder=Path(root)/'logs'/('debug_'+kind);folder.mkdir(parents=True,exist_ok=True)
    try:
        for old in sorted(folder.glob('*.png'),key=lambda p:p.stat().st_mtime)[:-19]:old.unlink()
        await page.screenshot(path=str(folder/(uuid.uuid4().hex+'.png')))
    except Exception:pass
