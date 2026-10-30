"""Private-message state machine, independent from task scheduling and pacing."""
import time
from urllib.parse import urlparse
from spark_delivery import until, visible_editor, message_count, confirm_message, failure_snapshot, timing


async def send_private(page,friend,message,root,verify,journal,cancelled,first=False):
    submitted=False;started=time.monotonic()
    async def fail(state,note):
        await failure_snapshot(page,root,'private')
        return state,note
    try:
        if cancelled():return 'CANCELLED','未提交，任务已取消'
        sec_uid=friend['url'].rstrip('/').rsplit('/',1)[-1]
        for attempt in range(3):
            try:
                await page.goto(friend['url'],wait_until='domcontentloaded',timeout=30000 if first else 25000)
                target=urlparse(page.url)
                if target.scheme!='https' or target.hostname not in ('www.douyin.com','douyin.com') or target.path.rstrip('/')!='/user/'+sec_uid:
                    await page.wait_for_url('**/user/'+sec_uid+'**',timeout=2500)
                target=urlparse(page.url)
                if target.scheme!='https' or target.hostname not in ('www.douyin.com','douyin.com') or target.path.rstrip('/')!='/user/'+sec_uid:
                    raise ValueError('未到达准确的目标主页')
                break
            except Exception:
                if attempt==2:return await fail('ERROR','导航未到达目标主页，请核对链接或登录')
        if not await verify(page):return 'CAPTCHA','需要人工验证，当前任务已停止'
        body=await page.locator('body').inner_text(timeout=3000)
        if any(x in body for x in ['操作过于频繁','访问过于频繁']):return 'RATE_LIMIT','页面提示频繁，任务已停止，请稍后人工重试'
        if 'login' in page.url or 'passport' in page.url:return 'LOGIN_EXPIRED','请重新扫码登录'
        async def find_button():
            for node in [page.get_by_role('button',name='私信').first,page.locator('div[class*="button"]:has-text("私信")').first,page.locator('text="私信"').first]:
                try:
                    if await node.count() and await node.is_visible():return node
                except Exception:continue
        button=await until(find_button,25 if first else 20)
        if button is None:return await fail('NO_BTN','在等待期限内未出现私信按钮')
        await button.click(timeout=6000)
        editor=await visible_editor(page,20)
        if editor is None:
            await page.reload(wait_until='domcontentloaded',timeout=25000)
            button=await until(find_button,10)
            if button is not None:await button.click(timeout=6000)
            editor=await visible_editor(page,20)
        if editor is None:return await fail('NO_INPUT','20秒内没有可识别的输入框，已保存本机诊断截图')
        if cancelled():return 'CANCELLED','未提交，任务已取消'
        if not await verify(page):return 'CAPTCHA','需要人工验证'
        if cancelled():return 'CANCELLED','未提交，任务已取消'
        # The user opted to replace the selected private conversation's old draft.
        # Use the unique scoped editor; never send a global Select All/Delete key.
        try:
            draft=await editor.evaluate("el => el.value ?? el.innerText")
            await editor.fill('',timeout=5000)
            async def cleared():return not (await editor.evaluate("el => el.value ?? el.innerText")).strip()
            if not await until(cleared,2):return await fail('DRAFT_CLEAR_FAILED','旧草稿未清空，未填入或发送新消息，请在目标私信中检查')
        except Exception:
            return await fail('DRAFT_CLEAR_FAILED','无法清空目标私信输入框，未发送，请检查页面或重新登录')
        if draft.strip():print('INFO|DRAFT_CLEARED|已清空当前目标私信的旧草稿',flush=True)
        if cancelled():return 'CANCELLED','未提交，任务已取消'
        before=await message_count(page,message)
        await editor.fill(message,timeout=5000)
        async def filled():return (await editor.evaluate("el => el.value ?? el.innerText")).replace('\r\n','\n').rstrip('\n')==message.replace('\r\n','\n').rstrip('\n')
        if not await until(filled,2):return 'NO_INPUT','输入内容未核实，未提交'
        if cancelled():return 'CANCELLED','未按发送键，草稿保留'
        # Preserve the write-before-submit journal, including process interruptions.
        journal()
        submitted=True
        await editor.press('Enter')
        confirmed=await confirm_message(page,message,before,5)
        if not await verify(page):return 'UNCONFIRMED','提交后需要验证，请人工核对，不会自动重发'
        return ('OK','页面出现新增消息，仍请按实际送达核对') if confirmed else ('UNCONFIRMED','5秒内未取得新增消息证据，未自动重发')
    except Exception as ex:
        return await fail('UNCONFIRMED' if submitted else 'ERROR',str(ex)[:120])
    finally:timing(root,'private','contact',time.monotonic()-started,first=first,submitted=submitted)
