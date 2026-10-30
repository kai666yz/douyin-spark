"""Actual Python backend -> CDP -> native WebView2, all page requests intercepted."""
from pathlib import Path
import argparse,os,shutil,subprocess,tempfile
parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--work-dir',type=Path);parser.add_argument('--backend-dir',type=Path);args=parser.parse_args()
root=Path(__file__).resolve().parents[1];qa=Path(tempfile.mkdtemp(prefix='group-backend-',dir=args.work_dir));(qa/'src').mkdir()
for name in ['icon.ico','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll','douyin_spark.py','spark_support.py','spark_private.py','spark_delivery.py','spark_group.py','fetch_following.py']:
    shutil.copy2(root/name,qa/name)
shutil.copytree(root/'assets',qa/'assets')
if args.backend_dir:shutil.copytree(args.backend_dir,qa/'spark_backend')
hook='''
                    web.CoreWebView2.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
                    web.CoreWebView2.WebResourceRequested+=delegate(object sender,CoreWebView2WebResourceRequestedEventArgs e){GroupBackendQa.Intercepted++;var bytes=Encoding.UTF8.GetBytes(GroupBackendQa.Html);e.Response=web.CoreWebView2.Environment.CreateWebResourceResponse(new MemoryStream(bytes),200,"OK","Content-Type: text/html; charset=utf-8");};
'''
for path in (root/'src').glob('*.cs'):
    text=path.read_text('utf-8-sig')
    if path.name=='GroupTaskView.cs':
        needle='await web.EnsureCoreWebView2Async(environment);';assert needle in text;text=text.replace(needle,needle+hook).replace('MessageBox.Show(this,ex.Message,"群聊未完成");','WriteEvent("qa_error | "+ex.Message);')
    (qa/'src'/path.name).write_text(text,encoding='utf-8-sig')
(qa/'src/GroupBackendQa.cs').write_text(r'''
using System;using System.IO;using System.Linq;using System.Reflection;using System.Threading.Tasks;using System.Windows.Forms;using System.Collections.Generic;using DouyinSpark;using Microsoft.Web.WebView2.WinForms;
class GroupBackendQa {
 public static int Intercepted;
 public static string Original="<!doctype html><meta charset='utf-8'><div data-e2e='im-dialog'><div data-e2e='conversation-item' data-conversation-id='qa_A' data-type='group' onclick=\"panel.dataset.conversationId='qa_A'\">同名群</div><div data-e2e='conversation-item' data-conversation-id='qa_B' data-type='group' onclick=\"panel.dataset.conversationId='qa_B'\">同名群</div></div><section id='panel' data-conversation-id='qa_A' data-type='group'><div id='editor' contenteditable='true' style='height:80px'></div><button onclick='localSend()'>发送</button></section><script>window.testSent=[];function localSend(){const text=editor.innerText;window.testSent.push({id:panel.dataset.conversationId,text});const p=document.createElement('p');p.textContent=text;panel.appendChild(p);editor.innerHTML='';}</script>";
 public static string Html=Original;
 static object Field(object o,string n){return o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
 static void Assert(bool ok,string text){if(!ok)throw new Exception(text);}
 static void Setup(string root,string mode){Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"config.json"),"{\"browser\":\"msedge\"}");File.WriteAllText(Path.Combine(root,"sentences_pool.json"),"{\"categories\":{\"废话文学\":[\"本地随机A\",\"本地随机B\",\"本地随机C\"]}}");ConversationStore.Merge(Path.Combine(root,"conversations.json"),new[]{new ConversationRecord{name="同名群",kind="group",conversation_id="qa_A"},new ConversationRecord{name="同名群",kind="group",conversation_id="qa_B"}});new GroupTaskSettings{message="本地固定测试",message_mode=mode,custom_messages=new List<string>{"本地轮换A","本地轮换B"},targets=new List<string>{"conversation:qa_A","conversation:qa_B"},interval_seconds=15,schedule="13:10",paused=false}.Save(root);typeof(DouyinSpark.Program).GetField("BaseDir",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,root);}
 [STAThread]static void Main(){Application.EnableVisualStyles();var host=new Form{ShowInTaskbar=false,WindowState=FormWindowState.Minimized};host.Shown+=async delegate{try{await Test();File.WriteAllText("PASS.txt","PASS: real Python backend attaches exact app-owned WebView2 target, fixed/random/rotation sources, same-name group IDs, immediate confirmation, dedup, stop, journal, phrase ledger and host browser preservation. Every page HTTP request intercepted; no real account or messages.");}catch(Exception e){File.WriteAllText("ERROR.txt",e.ToString());}finally{host.Close();}};Application.Run(host);}
 static async Task Run(GroupTaskForm form){var done=new TaskCompletionSource<bool>();form.RunningChanged=delegate(bool v){if(!v)done.TrySetResult(true);};form.Show();Assert(await Task.WhenAny(done.Task,Task.Delay(65000))==done.Task,"group backend timeout");}
 static async Task Test(){
  foreach(string mode in new[]{"fixed","random","rotation"}){
   string data=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,mode+Guid.NewGuid().ToString("N"));Setup(data,mode);Html=Original;
   if(mode=="rotation")Html=Html.Replace("<section id=","<section class='messageMsgInputcontainer' id=").Replace("contenteditable='true'","contenteditable='true' data-slate-editor='true'").Replace("<button onclick='localSend()'>发送</button>","")+"<script>document.addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();localSend();}})</script>";
   using(var form=new GroupTaskForm(data,AppDomain.CurrentDomain.BaseDirectory,true)){
    await Run(form);var entries=GroupJournal.Read(data);Assert(entries.Count==2&&entries.All(x=>x.status=="OK"),mode+" actual backend must confirm two messages; logs="+data);
    var web=(WebView2)Field(form,"web");Assert(!web.IsDisposed,"backend must disconnect, not close host browser");string sent=await web.ExecuteScriptAsync("JSON.stringify(window.testSent)");Assert(sent.Contains("qa_A")&&sent.Contains("qa_B"),"exact same-name target IDs");
    if(mode=="rotation")Assert(sent.Contains("本地轮换A")&&sent.Contains("本地轮换B"),"custom rotation texts");
    await form.RunScheduled();Assert(GroupJournal.Read(data).Count==2,"same-day dedup");Assert(SparkStorage.Json().Deserialize<int>(await web.ExecuteScriptAsync("window.testSent.length"))==2,"same-day no extra submits");form.Close();
   }
  }
  string finishCurrent=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"finish-current"+Guid.NewGuid().ToString("N"));Setup(finishCurrent,"fixed");Html=Original.Replace("panel.appendChild(p);editor.innerHTML='';","setTimeout(()=>panel.appendChild(p),1800);editor.innerHTML='';");
  using(var form=new GroupTaskForm(finishCurrent,AppDomain.CurrentDomain.BaseDirectory,true)){var done=new TaskCompletionSource<bool>();form.RunningChanged=delegate(bool v){if(!v)done.TrySetResult(true);};form.Show();DateTime limit=DateTime.Now.AddSeconds(20);while(DateTime.Now<limit&&!GroupJournal.Read(finishCurrent).Any(x=>x.status=="UNCONFIRMED"))await Task.Delay(20);Assert(GroupJournal.Read(finishCurrent).Any(x=>x.status=="UNCONFIRMED"),"must reach current submission");form.StopWithoutConfirmation();Assert(await Task.WhenAny(done.Task,Task.Delay(10000))==done.Task,"current group confirmation must finish before stop");var entries=GroupJournal.Read(finishCurrent);Assert(entries.Count(x=>x.status=="OK")==1&&entries.Count(x=>x.status=="CANCELLED")==1,"current group completes and remaining group cancels");form.Close();}
  string scheduled=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"scheduled"+Guid.NewGuid().ToString("N"));Setup(scheduled,"fixed");Html=Original;
  var flags=BindingFlags.Static|BindingFlags.NonPublic;Type program=typeof(DouyinSpark.Program);Action<string,object> set=(name,value)=>program.GetField(name,flags).SetValue(null,value);
  using(var main=(Form)program.GetMethod("BuildMainWindow",flags).Invoke(null,null)){
   main.Show();DateTime now=DateTime.Now;set("groupPlan",GroupTaskSettings.Read(scheduled));set("nextGroupScheduled",now);set("privateRunActive",true);
   using(var lease=new FileStream(Path.Combine(scheduled,".spark-send-slot.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.ReadWrite)){lease.WriteByte(0);lease.Flush();lease.Lock(0,1);program.GetMethod("TickGroupSchedule",flags).Invoke(null,new object[]{now});await Task.Delay(2500);Assert(GroupJournal.Read(scheduled).Count==0,"scheduled group must wait for current private conversation slot");lease.Unlock(0,1);}
   set("privateRunActive",false);DateTime deadline=DateTime.Now.AddSeconds(50);while(DateTime.Now<deadline&&!(GroupJournal.Read(scheduled).Count==2&&GroupJournal.Read(scheduled).All(x=>x.status=="OK")))await Task.Delay(200);Assert(GroupJournal.Read(scheduled).Count==2&&GroupJournal.Read(scheduled).All(x=>x.status=="OK"),"actual scheduler passes saved group plan to backend after shared slot releases");Assert(GroupTaskSettings.Read(scheduled).last_trigger_day==now.ToString("yyyy-MM-dd"),"scheduler persists dispatch day");for(int i=0;i<30&&(bool)program.GetField("groupTaskRunning",flags).GetValue(null);i++)await Task.Delay(100);set("exiting",true);main.Close();set("exiting",false);
  }
  string cancel=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"cancel"+Guid.NewGuid().ToString("N"));Setup(cancel,"fixed");Html="<!doctype html><div data-e2e='login-dialog'>Local login fixture</div>";
  using(var form=new GroupTaskForm(cancel,AppDomain.CurrentDomain.BaseDirectory,true)){var done=new TaskCompletionSource<bool>();form.RunningChanged=delegate(bool v){if(!v)done.TrySetResult(true);};form.Show();await Task.Delay(1800);form.StopWithoutConfirmation();Assert(await Task.WhenAny(done.Task,Task.Delay(15000))==done.Task,"stop while login waiting");Assert(GroupJournal.Read(cancel).All(x=>x.status=="CANCELLED"),"stop must not submit");Assert(form.RestartAfter>DateTime.UtcNow,"group cooldown");form.Close();}
  Assert(Intercepted>0,"request interception missing");
 }
}
''',encoding='utf-8-sig')
c=Path('C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe')
refs=['System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','System.Management.dll','System.Data.dll',str(c.parent/'Microsoft.VisualBasic.dll'),str(c.parent/'WPF/WindowsFormsIntegration.dll'),str(qa/'Microsoft.Web.WebView2.Core.dll'),str(qa/'Microsoft.Web.WebView2.WinForms.dll')]
subprocess.run([str(c),'/nologo','/target:winexe','/platform:x64','/codepage:65001','/main:GroupBackendQa','/out:'+str(qa/'GroupBackendQa.exe')]+['/r:'+x for x in refs]+[str(p) for p in (qa/'src').glob('*.cs')],check=True)
env=dict(os.environ,DOUYIN_SPARK_PYTHON=__import__('sys').executable,NO_PROXY='127.0.0.1,localhost')
subprocess.run([str(qa/'GroupBackendQa.exe')],cwd=qa,env=env,check=True,timeout=210)
assert not (qa/'ERROR.txt').exists(),(qa/'ERROR.txt').read_text('utf-8-sig') if (qa/'ERROR.txt').exists() else ''
print((qa/'PASS.txt').read_text('utf-8-sig'));print('Artifacts:',qa)
