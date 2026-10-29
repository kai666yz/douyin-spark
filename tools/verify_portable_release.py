"""Offline release smoke test with fake data; never logs in or sends messages."""
from pathlib import Path
import argparse
import csv
import hashlib
import json
import os
import subprocess
import tempfile
import zipfile
import sys

def main():
    if hasattr(sys.stdout,'reconfigure'):sys.stdout.reconfigure(encoding='utf-8')
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive',type=Path)
    parser.add_argument('--work-dir',type=Path,default=None)
    args=parser.parse_args()
    work=Path(tempfile.mkdtemp(prefix='便携验证 中文 空格 ',dir=args.work_dir))
    with zipfile.ZipFile(args.archive) as archive:
        assert archive.testzip() is None
        for name in archive.namelist():
            assert not Path(name).is_absolute() and '..' not in Path(name).parts
        archive.extractall(work)
    root=next(p for p in work.iterdir() if p.is_dir())
    manifest=json.loads((root/'SHA256SUMS.json').read_text('utf-8'))
    for name,digest in manifest.items():assert hashlib.sha256((root/name).read_bytes()).hexdigest()==digest,name
    versions=json.loads((root/'runtime-versions.json').read_text('utf-8'))
    assert json.loads((root/'friends.json').read_text('utf-8'))==[]
    assert json.loads((root/'config.json').read_text('utf-8'))=={'browser':'msedge','schedule':'','message':''}
    for name in ['logs','startup_state.json','conversations.json','conversation_candidates.json','conversation_profile','browser_profile','browser_profile_edge','browser_profile_quark']:
        assert not (root/name).exists(),name
    assert hashlib.sha256((root/'runtime/MicrosoftEdgeWebview2Setup.exe').read_bytes()).hexdigest()=='13f6d9cdc8c3e7186e4b180db7f48e199ee97d631480e8391fb3106f338fb21a'
    env=dict(os.environ)
    env['PATH']=os.environ['SystemRoot']+'\\System32;'+os.environ['SystemRoot']
    for key in ['PYTHONHOME','PYTHONPATH','DOUYIN_SPARK_DATA_DIR','DOUYIN_SPARK_PYTHON']:env.pop(key,None)
    cwd=work/'different working directory';cwd.mkdir()
    def run(*params,extra=None):
        e=dict(env);e.update(extra or {})
        result=subprocess.run([str(root/'spark_backend/spark_backend.exe'),*params],cwd=cwd,env=e,capture_output=True,text=True,encoding='utf-8',timeout=45)
        assert result.returncode==0,result.stderr
        return result.stdout
    assert run('--version').strip()==versions['application']
    diagnostics=json.loads(run('--environment-check'))
    assert diagnostics['python_version']==versions['python']=='3.12.14'
    assert diagnostics['playwright_version']==versions['playwright']=='1.64.1'
    assert diagnostics['frozen'] is True and Path(diagnostics['data_dir'])==root
    assert '运行环境检查通过' in run('--runtime-check')
    (root/'friends.json').write_text(json.dumps([{'name':'虚构😀','url':'https://www.douyin.com/user/qa_only'},{'name':'测试群','kind':'group','url':'https://www.douyin.com/user/qa_group'}],ensure_ascii=False),encoding='utf-8-sig')
    text='只预览 "引号" \\ 中文😀'
    assert run('--dry-run','--message',text).count(' -> '+text)==1
    (root/'logs').mkdir(exist_ok=True)
    with (root/'logs/spark_log.csv').open('w',encoding='utf-8-sig',newline='') as f:
        writer=csv.DictWriter(f,fieldnames=['time','friend','url','message','status','note']);writer.writeheader();writer.writerow({'time':'2026-10-29 10:00:00','friend':'虚构😀','message':'测试历史','status':'OK'})
    exported=work/'历史 导出😀.csv';run('--export-history',str(exported));assert '测试历史' in exported.read_text('utf-8-sig')
    old=b'[{"name":"fake cached","url":"https://www.douyin.com/user/qa_cached"}]'
    (root/'following.json').write_bytes(old);flag=work/'cancel.flag';flag.touch();report=work/'import.json'
    run('--fetch-following','--following-result',str(report),'--cancel-file',str(flag))
    assert json.loads(report.read_text('utf-8'))['status']=='cancelled'
    (root/'config.json').write_text('{"browser":"quark","schedule":""}',encoding='utf-8')
    hidden={key:str(work/'no-browser') for key in ['LOCALAPPDATA','ProgramFiles','ProgramFiles(x86)']}
    run('--fetch-following','--following-result',str(report),extra=hidden)
    result=json.loads(report.read_text('utf-8'));assert result['status']=='failed' and result['reason']
    assert (root/'following.json').read_bytes()==old
    moved=work/'再次移动😀';root.rename(moved);root=moved
    assert run('--version').strip()==versions['application']
    assert text in run('--dry-run','--message',text)
    assert not (cwd/'friends.json').exists()
    summary={'status':'PASS','files_verified':len(manifest),'versions':versions,'sha256':hashlib.sha256(args.archive.read_bytes()).hexdigest(),'bytes':args.archive.stat().st_size,'isolated_root':str(root),'checks':['zip_crc','file_hashes','no_private_data','microsoft_bootstrapper_hash','no_python_path','frozen_versions','cwd_independence','local_edge_runtime','unicode_message','groups_excluded','csv_export','cancel_preserves_cache','missing_browser_preserves_cache','move_after_use'],'scope':'one Windows host; no login, no messages, no clean VM'}
    output=work/'result.json';output.write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(summary,ensure_ascii=False))

if __name__=='__main__':main()
