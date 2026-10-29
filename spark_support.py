"""Portable paths, bounded logs, CSV history and manual verification support."""
import asyncio
import csv
import ctypes
from datetime import datetime, timedelta
import os
from pathlib import Path
import re
import shutil
import sys
import tempfile

VERSION = '2.2.0'
FIELDS = ['time', 'friend', 'url', 'message', 'status', 'note']

def acquire_task_lock(path):
    """The handle owns the OS lock; crashes automatically release it."""
    path.parent.mkdir(parents=True, exist_ok=True)
    handle = path.open('a+b')
    handle.seek(0, 2)
    if handle.tell() == 0:
        handle.write(b'0'); handle.flush()
    handle.seek(0)
    try:
        if os.name == 'nt':
            import msvcrt
            msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
        else:
            import fcntl
            fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except OSError as exc:
        handle.close()
        raise RuntimeError('已有发送或导入任务运行，请等它结束后再试。') from exc
    return handle

def data_dir():
    override = os.environ.get('DOUYIN_SPARK_DATA_DIR')
    if override:
        return Path(override).resolve()
    if getattr(sys, 'frozen', False):
        folder = Path(sys.executable).resolve().parent
        folder = folder.parent if folder.name == 'spark_backend' else folder
    else:
        folder = Path(__file__).resolve().parent
    return resolve_data_dir(folder)


def resolve_data_dir(folder, local_root=None, writable=None):
    """Match the desktop data routing, including a sticky per-location fallback."""
    import hashlib
    folder = Path(folder).resolve()
    local_root = Path(local_root or Path(os.environ.get('LOCALAPPDATA', str(Path.home()/'AppData/Local')))/'douyin-spark')
    identity = str(folder).rstrip('\\/').upper()
    fallback = local_root/'data'/hashlib.sha256(identity.encode('utf-8')).hexdigest()[:16]
    def probe(path):
        try:
            path.mkdir(parents=True, exist_ok=True)
            fd, name = tempfile.mkstemp(prefix='.spark-write-', dir=path)
            os.close(fd);os.unlink(name)
            return True
        except OSError:
            return False
    writable = writable or probe
    if not (fallback/'storage.ready').is_file() and writable(folder):
        return folder
    if not writable(fallback):
        raise RuntimeError('软件目录和本机用户数据目录均不可写，请解压到个人文件夹后重试。')
    fallback.mkdir(parents=True,exist_ok=True)
    if not (fallback/'storage.ready').is_file():
        pending = fallback/'migration.pending'
        resume = pending.is_file() and pending.read_text('utf-8-sig') == identity
        if not resume and any(fallback.iterdir()):
            raise RuntimeError('备用数据目录已有未确认文件，未覆盖：'+str(fallback))
        import json
        for name,kind in [('friends.json',list),('config.json',dict)]:
            source=folder/name
            if source.is_file() and not isinstance(read_json(source),kind):
                raise ValueError('原数据格式无效，未创建空配置：'+name)
        pending.write_text(identity,encoding='utf-8')
        def copy(source, target):
            if source.is_symlink() or getattr(source.stat(), 'st_file_attributes',0) & 0x400:
                raise RuntimeError('数据包含链接，请手动核对后迁移。')
            if source.is_dir():
                target.mkdir(exist_ok=True)
                for child in source.iterdir():
                    if child.is_dir() and child.name.lower() in {'cache','code cache','gpucache','shadercache','grshadercache','dawngraphitecache','dawnwebgpucache','browsermetrics','crashpad'}:
                        continue
                    if child.name.lower() == 'lockfile' or child.name.lower().startswith('singleton') or child.name.lower().endswith('.tmp'):
                        continue
                    copy(child,target/child.name)
            else:
                shutil.copyfile(source,target)
        for name in ('friends.json','config.json','following.json','sentences_pool.json','settings.json','conversations.json','conversation_candidates.json','logs','conversation_profile','browser_profile','browser_profile_edge','browser_profile_quark'):
            if (folder/name).exists():copy(folder/name,fallback/name)
        (fallback/'storage.ready').write_text(identity,encoding='utf-8')
        pending.unlink()
    return fallback


def read_json(path):
    """Accept BOM/trailing commas without modifying punctuation in JSON strings."""
    import json
    output=[];quoted=False;escape=False
    for ch in Path(path).read_text('utf-8-sig'):
        if not quoted and ch in ']}':
            i=len(output)-1
            while i>=0 and output[i].isspace():i-=1
            if i>=0 and output[i]==',':del output[i]
        output.append(ch)
        if quoted:
            if escape:escape=False
            elif ch=='\\':escape=True
            elif ch=='"':quoted=False
        elif ch=='"':quoted=True
    return json.loads(''.join(output))

def read_rows(path):
    if not path.exists():
        return []
    with path.open(encoding='utf-8-sig', newline='') as f:
        return list(csv.DictReader(f))

def write_rows(path, rows):
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp = tempfile.mkstemp(prefix=path.name+'.', suffix='.tmp', dir=path.parent)
    try:
        with os.fdopen(fd, 'w', encoding='utf-8-sig', newline='') as f:
            w = csv.DictWriter(f, fieldnames=FIELDS, extrasaction='ignore')
            w.writeheader()
            w.writerows(rows)
        os.replace(tmp, path)
    finally:
        if os.path.exists(tmp):
            os.unlink(tmp)

def history_paths(log_file):
    archive = log_file.parent/'archive'/'spark_log'
    return sorted(archive.glob('*.csv')) + ([log_file] if log_file.exists() else [])

def rotate_csv(log_file, days=14):
    """Keep recent rows active; archive older rows by month without losing dedup history."""
    rows = read_rows(log_file)
    cutoff = (datetime.now()-timedelta(days=days)).strftime('%Y-%m-%d')
    recent, groups = [], {}
    for row in rows:
        stamp = row.get('time', '')
        if re.match(r'^\d{4}-\d{2}-\d{2}', stamp) and stamp[:10] < cutoff:
            groups.setdefault(stamp[:7], []).append(row)
        else:
            recent.append(row)
    for month, moved in groups.items():
        target = log_file.parent/'archive'/'spark_log'/(month+'.csv')
        previous = read_rows(target)
        seen = {tuple(r.get(k,'') for k in FIELDS) for r in previous}
        previous.extend(r for r in moved if tuple(r.get(k,'') for k in FIELDS) not in seen)
        write_rows(target, previous)
    if groups:
        write_rows(log_file, recent)

def export_history(log_file, target):
    rows = [r for p in history_paths(log_file) for r in read_rows(p)]
    # Prevent formulas when CSV files are opened in a spreadsheet application.
    for row in rows:
        for key in ('friend','url','message','note'):
            if row.get(key,'').lstrip().startswith(('=','+','-','@')):
                row[key] = "'"+row[key]
    write_rows(Path(target), rows)
    print(f'已导出 {len(rows)} 条历史记录。', flush=True)

def clear_history(log_file):
    """Back up records; keep today's rows so deleting history cannot cause a resend."""
    paths = history_paths(log_file)
    today = datetime.now().strftime('%Y-%m-%d')
    rows = [r for p in paths for r in read_rows(p)]
    backup = log_file.parent/'history_backup'/datetime.now().strftime('%Y%m%d_%H%M%S_%f')
    backup.mkdir(parents=True, exist_ok=True)
    for i, path in enumerate(paths):
        shutil.copy2(path, backup/f'{i}_{path.name}')
    write_rows(log_file, [r for r in rows if r.get('time','').startswith(today)])
    for path in paths:
        if path != log_file:
            path.unlink()
    print('历史已清理并备份，保留今日记录以维持发送去重。', flush=True)

CAPTCHA_SELECTORS = 'iframe[src*="captcha"], [id*="captcha"], [class*="captcha"], [id*="verify-slide"]'
CAPTCHA_TEXT = re.compile('请完成.*验证|拖动滑块|完成下方验证|请通过.*验证|安全验证')

async def challenge_visible(page):
    for frame in page.frames:
        try:
            nodes = frame.locator(CAPTCHA_SELECTORS)
            for i in range(min(await nodes.count(), 10)):
                if await nodes.nth(i).is_visible():
                    return True
            body = await frame.locator('body').inner_text(timeout=1000)
            if CAPTCHA_TEXT.search(body):
                return True
        except Exception:
            pass
    return False

async def wait_for_verification(page, show_window, cancel_file=None, timeout=300):
    if not await challenge_visible(page):
        return True
    print('ATTENTION|CAPTCHA|检测到安全验证，发送已暂停，请在浏览器中手动完成验证。', flush=True)
    show_window()
    try:
        ctypes.windll.user32.MessageBeep(0x30)
    except Exception:
        pass
    end = asyncio.get_running_loop().time()+timeout
    clear = 0
    while asyncio.get_running_loop().time() < end:
        if cancel_file and Path(cancel_file).exists():
            return False
        if page.is_closed():
            return False
        clear = 0 if await challenge_visible(page) else clear+1
        if clear >= 3:
            print('安全验证已解除，继续当前任务。', flush=True)
            return True
        await asyncio.sleep(1)
    print('安全验证等待超时，本次任务停止；请处理后手动重试。', flush=True)
    return False
