"""Assemble only public runtime files; never copy personal data from the working app."""
from pathlib import Path
import hashlib
import importlib.metadata
import json
import shutil
import sys
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
from spark_support import VERSION

def main():
    backend=ROOT/'dist'/'spark_backend'
    assert (backend/'spark_backend.exe').is_file(), 'Build the backend first.'
    output=ROOT/'dist'/f'douyin-spark-v{VERSION}-windows-x64.zip'
    with tempfile.TemporaryDirectory(prefix='spark-package-') as tmp:
        stage=Path(tmp)/f'抖音续火花_v{VERSION}'
        stage.mkdir()
        for name in ('抖音续火花.exe','icon.ico','LICENSE','README.md'):
            shutil.copy2(ROOT/name,stage/name)
        if (ROOT/'docs').is_dir():
            (stage/'docs').mkdir()
            for name in ('ui-light.png','ui-dark.png'):
                if (ROOT/'docs'/name).is_file():shutil.copy2(ROOT/'docs'/name,stage/'docs'/name)
        pool=json.loads((ROOT/'sentences_pool.json').read_text('utf-8-sig'))
        # Never publish local custom messages or a personal category selection.
        if 'categories' in pool:
            pool['categories']['自定义']=[]
            pool['active_categories']=['废话文学'] if '废话文学' in pool['categories'] else []
        (stage/'sentences_pool.json').write_text(json.dumps(pool,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
        (stage/'friends.json').write_text('[]\n',encoding='utf-8')
        (stage/'config.json').write_text('{"browser":"msedge","schedule":"","message":""}\n',encoding='utf-8')
        shutil.copytree(backend,stage/'spark_backend',ignore=shutil.ignore_patterns('__pycache__','direct_url.json'))
        licenses=stage/'third-party-licenses'
        licenses.mkdir()
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
        readme='''抖音续火花 v2.0.0 · Windows 64位便携版

完整解压后双击“抖音续火花.exe”。请保留 spark_backend 整个文件夹。
已包含独立后端及 Python/Playwright，不用另装 Python、豆包或浏览器内核。
需要 Windows 10/11、.NET Framework 4.x（推荐4.8），以及已安装的 Edge/Chrome/夸克之一。

首次选择浏览器、扫码登录并添加好友。本包的好友名单为空，不含任何登录状态。
添加好友时请在软件弹出的独立浏览器中扫码；日常浏览器的登录不会自动继承。
读取时可取消；失败、空结果和部分结果不覆盖关注缓存，更不会覆盖原好友名单。
部分结果需确认后勾选导入，未读取成功可查看诊断或手动添加。
固定消息：更多功能 → 固定消息设置。留空则使用语录池。
托盘：最小化或关闭窗口后继续在托盘运行；右键托盘选择退出。
历史：更多功能 → 导出历史 CSV / 清理旧历史。清理前备份并保留今日去重记录。
验证码需在浏览器中手动处理；程序不会自动解验证码或切换身份规避验证。
检查更新连接 GitHub Releases，确认后打开下载页，不自动覆盖个人数据。

从旧版迁移：先关闭旧程序。建议将 friends.json、config.json 和对应 browser_profile*、logs
复制到新版目录后使用，保留旧目录作为备份。不要用本包的空名单覆盖自己的名单。
完整功能和源码构建方法见 README.md。
'''
        (stage/'先读我.txt').write_text(readme,encoding='utf-8-sig')
        (stage/'启动到托盘.bat').write_text('@echo off\nchcp 65001 >nul\ncd /d "%~dp0"\nstart "" "%~dp0抖音续火花.exe" --minimized\n',encoding='utf-8')
        (stage/'运行环境检查.bat').write_text('@echo off\nchcp 65001 >nul\ncd /d "%~dp0"\nset "DOUYIN_SPARK_DATA_DIR=%~dp0"\n"%~dp0spark_backend\\spark_backend.exe" --runtime-check\npause\n',encoding='utf-8')
        manifest={str(p.relative_to(stage)).replace('\\','/'):hashlib.sha256(p.read_bytes()).hexdigest() for p in stage.rglob('*') if p.is_file()}
        (stage/'SHA256SUMS.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
        with zipfile.ZipFile(output,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
            for path in stage.rglob('*'):
                if path.is_file():z.write(path,path.relative_to(stage.parent))
    with zipfile.ZipFile(output) as z:
        assert z.testzip() is None
        assert not any(any(p.startswith(('browser_profile','wv_profile','ui_profile')) or p=='logs' for p in Path(n).parts) for n in z.namelist())
    print(output)
    print('SHA256 '+hashlib.sha256(output.read_bytes()).hexdigest())

if __name__=='__main__':main()
