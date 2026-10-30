"""Assemble only public runtime files; never copy personal data from the working app."""
from pathlib import Path
import hashlib
import importlib.metadata
import json
import shutil
import sys
import tempfile
import zipfile
import os

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
from spark_support import VERSION

def main():
    backend=Path(os.environ.get('SPARK_BACKEND_DIR',ROOT/'dist'/'spark_backend'))
    assert (backend/'spark_backend.exe').is_file(), 'Build the backend first.'
    output_dir=Path(os.environ.get('SPARK_DIST_DIR',ROOT/'dist'))
    output_dir.mkdir(parents=True,exist_ok=True)
    output=output_dir/f'douyin-spark-v{VERSION}-windows-x64.zip'
    with tempfile.TemporaryDirectory(prefix='spark-package-') as tmp:
        stage=Path(tmp)/f'抖音续火花_v{VERSION}'
        stage.mkdir()
        for name in ('抖音续火花.exe','icon.ico','LICENSE','README.md'):
            shutil.copy2(ROOT/name,stage/name)
        for name in ('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll'):
            shutil.copy2(ROOT/name,stage/name)
        shutil.copytree(ROOT/'assets',stage/'assets')
        (stage/'runtime').mkdir()
        bootstrapper=ROOT/'runtime/MicrosoftEdgeWebview2Setup.exe'
        assert hashlib.sha256(bootstrapper.read_bytes()).hexdigest()=='13f6d9cdc8c3e7186e4b180db7f48e199ee97d631480e8391fb3106f338fb21a'
        shutil.copy2(bootstrapper,stage/'runtime'/bootstrapper.name)
        if (ROOT/'docs').is_dir():
            (stage/'docs').mkdir()
            for name in ('ui-light.png','ui-dark.png','ui-groups.png','import-fixture.png','environment.png','ui-tasks.png','ui-settings.png','ui-group-task.png','spark-icon.png','startup-stages.png','selection-following.png','selection-conversations.png','ui-group-random.png','ui-group-rotation.png','benchmark-v2.4.0.json','benchmark-v2.4.0.csv'):
                if (ROOT/'docs'/name).is_file():shutil.copy2(ROOT/'docs'/name,stage/'docs'/name)
        pool=json.loads((ROOT/'sentences_pool.json').read_text('utf-8-sig'))
        # Never publish local custom messages or a personal category selection.
        if 'categories' in pool:
            pool['categories']['自定义']=[]
            pool['active_categories']=['废话文学'] if '废话文学' in pool['categories'] else []
        (stage/'sentences_pool.json').write_text(json.dumps(pool,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
        (stage/'friends.json').write_text('[]\n',encoding='utf-8')
        (stage/'config.json').write_text('{"browser":"msedge","schedule":"","message":""}\n',encoding='utf-8')
        (stage/'runtime-versions.json').write_text(json.dumps({'application':VERSION,'python':sys.version.split()[0],'playwright':importlib.metadata.version('playwright'),'webview2_sdk':'1.0.4314.35','webview2_runtime':'system Evergreen; bootstrapper bundled, actual Runtime downloaded on request'},ensure_ascii=False,indent=2),encoding='utf-8')
        shutil.copytree(backend,stage/'spark_backend',ignore=shutil.ignore_patterns('__pycache__','direct_url.json'))
        licenses=stage/'third-party-licenses'
        licenses.mkdir()
        shutil.copy2(ROOT/'vendor/webview2/WebView2-LICENSE.txt',licenses/'WebView2-LICENSE.txt')
        shutil.copy2(ROOT/'vendor/webview2/WebView2-NOTICE.txt',licenses/'WebView2-NOTICE.txt')
        python_license=Path(sys.base_prefix)/'LICENSE.txt'
        if python_license.exists(): shutil.copy2(python_license,licenses/'Python-LICENSE.txt')
        for name in ('playwright','greenlet','pyee','typing_extensions','pyinstaller'):
            dist=importlib.metadata.distribution(name)
            for item in dist.files or []:
                if item.name.upper().startswith(('LICENSE','COPYING','NOTICE')):
                    source=Path(dist.locate_file(item))
                    if source.is_file():
                        target=licenses/name/item.name
                        target.parent.mkdir(parents=True,exist_ok=True)
                        shutil.copy2(source,target)
        readme=f'''抖音续火花 v{VERSION} · Windows 64位便携版

完整解压后双击“抖音续火花.exe”。请保留 spark_backend 整个文件夹。
已包含独立后端及 Python/Playwright，无需自行安装 Python、豆包或 Playwright。
需要 Windows 10/11、.NET Framework 4.6.2+（推荐4.8），以及已安装的 Edge/Chrome/夸克之一。

导入会话在软件内完成，内嵌网页需要 Microsoft Edge WebView2 Runtime。首次可能需要扫码，后续保留登录。
启动展示四阶段检查进度与后台运行环境校验，失败可重试并查看startup.log；缺少 WebView2 时点“一键准备”，由包内微软安装器联网安装，无需手动下载。
程序目录不可写时自动迁移到本机用户目录；环境与数据中可查看位置。原文件保留。
首次升级确认、移动目录或切换账户时，已有定时暂停；明确保存启用后只等下一次，不立即补发。
本软件当前未签名。请核对 GitHub 官方来源及 SHA256；不信任来源时不要绕过系统拦截。
日常浏览器、旧版发送浏览器、内嵌网页的登录状态彼此独立，不会复制或覆盖。
可直接选择上次读取的会话；新会话点登录/刷新，在软件内打开消息后读取。
群聊可在消息任务中独立选择、填写消息及启用定时；不会混入私信发送。
侧边栏群聊消息一屏选择目标、随机分类/固定消息/多条轮换、每日定时。
今日私信/今日群聊可切换；两类共用一个发送名额，每个会话结束后交替处理，不启用多路并发。
首页与消息任务页可停止发送：私信当前联系人结束后停止，剩余标已取消；停止后冷却10秒。
选择窗口支持整行勾选、全选/全不选、今日未发送筛选、Ctrl+A与实时已选数。
群间间隔可调15～600秒，默认60秒；群聊独立消息与每日定时。
群聊后端按会话ID核对软件网页，提交后轮询新增消息；页面确认不等于服务器送达或火花延续。
随机和轮换语录当天不重句，跨天重置；旧固定消息保持固定模式。出现安全暂停需人工处理后确认恢复。
网页未取得明确证据时记为待核对，不等于送达；结果不确定不自动重发，已有草稿保留。
本包的好友名单为空，不含任何登录状态。旧版关注导入及手动添加入口保留。
读取时可取消；失败、空结果和部分结果不覆盖关注缓存，更不会覆盖原好友名单。
部分结果需确认后勾选导入，未读取成功可查看诊断或手动添加。
私信固定消息：消息任务 → 私信问候 → 消息内容。留空则使用语录池。
托盘：最小化或关闭窗口后继续在托盘运行；右键托盘选择退出。
历史：设置 → 数据与记录。群聊发送记录独立核对及导出CSV。
验证码需在浏览器中手动处理；程序不会自动解验证码或切换身份规避验证。
检查更新连接 GitHub Releases，确认后打开下载页，不自动覆盖个人数据。

从旧版迁移：先关闭旧程序。保留 friends.json、config.json、conversations.json、
conversation_candidates.json、group_task.json、startup_state.json、conversation_profile、对应 browser_profile* 和 logs，
保留旧目录作为备份。不要用本包的空名单覆盖自己的名单。
完整功能和源码构建方法见 README.md。
'''
        (stage/'先读我.txt').write_text(readme,encoding='utf-8-sig')
        (stage/'启动到托盘.bat').write_text('@echo off\nchcp 65001 >nul\ncd /d "%~dp0"\nstart "" "%~dp0抖音续火花.exe" --minimized\n',encoding='utf-8')
        (stage/'运行环境检查.bat').write_text('@echo off\nchcp 65001 >nul\ncd /d "%~dp0"\nstart "" "%~dp0抖音续火花.exe" --check-environment\n',encoding='utf-8')
        manifest={str(p.relative_to(stage)).replace('\\','/'):hashlib.sha256(p.read_bytes()).hexdigest() for p in stage.rglob('*') if p.is_file()}
        (stage/'SHA256SUMS.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
        with zipfile.ZipFile(output,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
            for path in stage.rglob('*'):
                if path.is_file():z.write(path,path.relative_to(stage.parent))
    with zipfile.ZipFile(output) as z:
        assert z.testzip() is None
        assert not any(any(p.startswith(('browser_profile','wv_profile','ui_profile','conversation_profile')) or p=='logs' for p in Path(n).parts) for n in z.namelist())
        assert not any(Path(n).name in ('conversations.json','conversation_candidates.json') for n in z.namelist())
    print(output)
    print('SHA256 '+hashlib.sha256(output.read_bytes()).hexdigest())

if __name__=='__main__':main()
