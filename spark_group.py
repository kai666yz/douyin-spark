"""Exact-conversation group delivery via the app-owned WebView2 CDP target."""
import asyncio
from datetime import datetime
import json
from pathlib import Path
import re
import sys
import time
from urllib.parse import urlparse
import uuid

from spark_support import acquire_task_lock, read_json, wait_for_verification
from spark_delivery import (DailyPhrases, atomic_json, confirm_message, failure_snapshot,
    group_candidates, interruptible_delay, message_count, safety_stop, sending_slot, timing, until)

EXPLANATIONS={'GROUP_NOT_LOADED':'消息列表尚未加载目标群，请展开消息列表后重试',
    'AMBIGUOUS_GROUP':'存在重复群标识，未猜测目标，请重新导入核对',
    'WRONG_ACTIVE_GROUP':'当前输入框属于其他会话，未提交',
    'UNVERIFIED_ACTIVE_GROUP':'无法核实当前群标识，未提交',
    'AMBIGUOUS_EDITOR':'输入框未就绪或存在多个输入框',
    'DRAFT_PRESENT':'已有草稿，未覆盖；请先处理草稿后再发送',
    'INPUT_UNSUPPORTED':'当前输入框未接受消息，请人工检查',
    'SEND_BUTTON_UNAVAILABLE':'没有可核实的发送入口，未提交',
    'DRAFT_CHANGED':'输入内容或会话发生变化，未提交'}

def explain(value):return EXPLANATIONS.get(value,value)


def assets_root():
    return Path(sys.executable).resolve().parent.parent/'assets' if getattr(sys,'frozen',False) else Path(__file__).resolve().parent/'assets'


def history(root):
    folder=Path(root)/'logs/group_attempts';day=datetime.now().strftime('%Y%m%d')
    result=[]
    for file in folder.glob(day+'_*.json'):
        value=read_json(file)
        if not isinstance(value,dict) or not all(value.get(k) for k in ['id','group_id','time','status']):raise ValueError('群聊记录异常，原记录保留，未继续发送。')
        result.append(value)
    return result


def blocked(root,group_id):
    return any(x['group_id']==group_id and x['status'] in ['SUBMITTED','UNCONFIRMED','OK','CONFIRMED'] for x in history(root))


def record(root,group,status,note,entry=None,message=''):
    value=entry or {'id':datetime.now().strftime('%Y%m%d')+'_'+uuid.uuid4().hex,'group_id':group['id'],'name':group['name'],'time':datetime.now().isoformat(timespec='seconds')}
    value.update(status=status,note=note)
    if message:value['message_hash']=DailyPhrases.digest(message)
    if not re.fullmatch(r'\d{8}_[a-f0-9]{32}',value['id']):raise ValueError('记录标识无效')
    atomic_json(Path(root)/'logs/group_attempts'/(value['id']+'.json'),value)
    return value


def resolve(root,plan):
    ids=plan.get('targets')
    if not isinstance(ids,list) or not ids or any(not isinstance(x,str) for x in ids):raise ValueError('请选择目标群。')
    records=read_json(Path(root)/'conversations.json');groups={}
    for item in records:
        cid=item.get('conversation_id','')
        if item.get('kind')=='group' and re.fullmatch(r'[A-Za-z0-9_:.-]{1,240}',cid):
            groups['conversation:'+cid]={'id':'conversation:'+cid,'conversation_id':cid,'name':item.get('name','群聊')}
    if any(x not in groups for x in ids):raise ValueError('目标群标识失效或不属于群聊，请重新核对。')
    return [groups[x] for x in dict.fromkeys(ids)]


class GroupSender:
    def __init__(self,page,root,stop):
        self.page=page;self.root=Path(root);self.stop=stop
        self.script=(assets_root()/'group_actions.js').read_text('utf-8')
        self.open_script=(assets_root()/'open_messages.js').read_text('utf-8')
        self.frame=None
    async def action(self,phase,group,message=''):
        result=None
        frames=[self.frame] if self.frame and phase not in ['select','scroll'] else self.page.frames
        for frame in frames:
            try:
                value=await frame.evaluate(self.script,{'phase':phase,'id':group['conversation_id'],'message':message})
                if value['status'] not in ['GROUP_NOT_LOADED','AMBIGUOUS_EDITOR','UNTRUSTED_PAGE']:
                    self.frame=frame;return value
                result=value
            except Exception:continue
        return result or {'status':'GROUP_NOT_LOADED'}
    async def send(self,group,message):
        entry=None;submitted=False;start=time.monotonic()
        async def locate():
            result=await self.action('select',group)
            if result['status'] in ['LOGIN_REQUIRED','VERIFICATION_REQUIRED']:
                print('ATTENTION|LOGIN|请在软件内完成扫码或安全验证，所有发送正在等待。',flush=True)
                async def clear():
                    value=await self.action('select',group)
                    return value if value['status'] not in ['LOGIN_REQUIRED','VERIFICATION_REQUIRED'] else None
                result=await until(clear,180,.3,self.stop)
                if result is None:
                    if not self.stop():safety_stop(self.root,'登录或验证等待超时')
                    raise RuntimeError('登录/验证未完成，未提交')
            if result['status']=='OPENED':return result
            if result['status']!='GROUP_NOT_LOADED':raise RuntimeError(result['status'])
            for frame in self.page.frames:
                try:await frame.evaluate(self.open_script)
                except Exception:pass
            await self.action('scroll',group)
        try:
            if await until(locate,20,.3,self.stop) is None:return 'CANCELLED' if self.stop() else 'FAILED','未定位到选中群',False
            async def editor():
                value=await self.action('inspect',group)
                if value['status']=='READY':return value
                if value['status'] in ['WRONG_ACTIVE_GROUP','UNVERIFIED_ACTIVE_GROUP']:raise RuntimeError(value['status'])
            if await until(editor,20,.3,self.stop) is None:return 'CANCELLED' if self.stop() else 'FAILED','输入框未就绪',False
            before=await message_count(self.page,message)
            print('UI|LOCK',flush=True)
            result=await self.action('fill',group,message)
            if result['status']!='FILLED':raise RuntimeError(result['status'])
            # No stop boundary between fill and submit: finish the current conversation.
            entry=record(self.root,group,'UNCONFIRMED','提交前记录，中断后需核对',message=message)
            result=await self.action('submit',group,message)
            if result['status']=='KEYBOARD_READY':
                if (await self.action('keyboard-verify',group,message))['status']!='KEYBOARD_READY':raise RuntimeError('DRAFT_CHANGED')
                await self.page.keyboard.press('Enter');result={'status':'SUBMITTED'}
            if result['status']!='SUBMITTED':
                record(self.root,group,'FAILED',explain(result['status']),entry)
                return 'FAILED',explain(result['status']),False
            submitted=True
            confirmed=await confirm_message(self.page,message,before,5)
            state='OK' if confirmed else 'SUBMITTED'
            note='页面出现新增消息，请以实际送达为准' if confirmed else '已提交，5秒内未取得新增消息证据，请人工核对'
            if not await wait_for_verification(self.page,lambda:None,timeout=180):
                safety_stop(self.root,'提交后验证未完成');state='UNCONFIRMED';note='提交后验证中断，请人工核对'
            record(self.root,group,state,note,entry)
            return state,note,True
        except Exception as ex:
            state='UNCONFIRMED' if submitted or entry else 'CANCELLED' if self.stop() else 'FAILED'
            record(self.root,group,state,explain(str(ex))[:160],entry)
            await failure_snapshot(self.page,self.root,'group')
            return state,explain(str(ex))[:160],bool(entry)
        finally:
            print('UI|UNLOCK',flush=True)
            timing(self.root,'group','contact',time.monotonic()-start,submitted=submitted)


async def run_group(root,request,stop_file=None):
    from playwright.async_api import async_playwright
    root=Path(root);plan=request['plan'];groups=resolve(root,plan);mode,candidates=group_candidates(root,plan)
    endpoint=request.get('endpoint','');url=urlparse(endpoint)
    if url.scheme!='http' or url.hostname!='127.0.0.1' or not url.port or url.path not in ('','/') or url.username or url.query or url.fragment:raise ValueError('仅允许连接软件提供的本机网页端点。')
    target_id=request.get('target_id','')
    if not re.fullmatch(r'[A-Za-z0-9_-]{1,128}',target_id):raise ValueError('软件网页目标无效。')
    stop=lambda:bool(stop_file and Path(stop_file).exists())
    spacing=int(plan.get('interval_seconds',60))
    if not 15<=spacing<=600:raise ValueError('群间间隔须为15～600秒。')
    print(f'TASK|PLANNED|{len(groups)}',flush=True)
    done=0;processed=set();phrases=DailyPhrases(root);batch=time.monotonic()
    async with async_playwright() as p:
        browser=await p.chromium.connect_over_cdp(endpoint,timeout=15000)
        page=None
        for context in browser.contexts:
            for candidate in context.pages:
                session=await context.new_cdp_session(candidate)
                try:
                    value=await session.send('Target.getTargetInfo')
                    if value['targetInfo']['targetId']==target_id:page=candidate
                finally:await session.detach()
        if page is None:raise ValueError('未找到本软件的指定网页，不会向其他窗口发送。')
        sender=GroupSender(page,root,stop)
        for group in groups:
            if stop():break
            if blocked(root,group['id']):
                processed.add(group['id']);done+=1
                print(f"PROGRESS|{done}|{len(groups)}|{group['name']}|SKIPPED",flush=True);continue
            if done and not await interruptible_delay(spacing,stop):break
            async with sending_slot(root,stop) as acquired:
                if not acquired:break
                owner=None;message='';submitted=False
                print('TASK|ACTIVE|1',flush=True)
                try:
                    message=candidates[0] if mode=='fixed' else ''
                    if mode!='fixed':message,owner=phrases.reserve(candidates,mode)
                    state,note,submitted=await sender.send(group,message)
                except Exception as ex:
                    state,note='FAILED',str(ex)[:160];record(root,group,state,note)
                finally:
                    if owner:phrases.finish(message,owner,submitted)
                    print('TASK|ACTIVE|0',flush=True)
            if state=='CANCELLED':
                if not any(x['group_id']==group['id'] and x['status']=='CANCELLED' for x in history(root)):record(root,group,'CANCELLED','用户停止，未开始提交')
                processed.add(group['id']);break
            processed.add(group['id']);done+=1
            print(f"RESULT|{group['id']}|{state}",flush=True)
            print(f"PROGRESS|{done}|{len(groups)}|{group['name']}|{state}",flush=True)
            if (root/'.spark-safety-stop.json').exists():break
        if stop():
            remaining=[g for g in groups if g['id'] not in processed and not blocked(root,g['id'])]
            for group in remaining:record(root,group,'CANCELLED','用户停止，未开始处理')
            print(f'TASK|CANCELLED|{len(remaining)}',flush=True)
        # Do not close the host's browser: leaving Playwright disconnects our CDP session.
    timing(root,'group','batch',time.monotonic()-batch,targets=len(groups),concurrency=1)
    report=root/'logs/日报'/(datetime.now().strftime('%Y-%m-%d')+'_群聊.txt');report.parent.mkdir(parents=True,exist_ok=True)
    entries=history(root);latest={}
    for item in sorted(entries,key=lambda x:(x['time'],x['status'] in ('OK','SUBMITTED','UNCONFIRMED','CONFIRMED'))):latest[item['group_id']]=item
    report.write_text('群聊日报（页面确认不等于服务器送达）\n'+'\n'.join(x['name']+' | '+x['status']+' | '+x.get('note','') for x in latest.values()),encoding='utf-8')


def main(root,request_file,stop_file):
    request=read_json(request_file)
    with acquire_task_lock(Path(root)/'.spark-group-run.lock'):
        asyncio.run(run_group(root,request,stop_file))
