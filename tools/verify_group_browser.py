"""Native WebView2 integration test; source copies run only against fake pages."""
from pathlib import Path
import shutil,subprocess
import argparse,tempfile
parser=argparse.ArgumentParser(description='Run the group UI/queue against intercepted local fixtures. Never use a real account.')
parser.add_argument('--work-dir',type=Path,default=None)
args=parser.parse_args()
SOURCE=Path(__file__).resolve().parents[1]
QA=Path(tempfile.mkdtemp(prefix='spark-group-pipeline-',dir=args.work_dir))
(QA/'src').mkdir(parents=True,exist_ok=True)
for name in ['ERROR.txt','PASS.txt']:(QA/name).unlink(missing_ok=True)
for name in ['Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll','icon.ico']:shutil.copy2(SOURCE/name,QA/name)
shutil.copytree(SOURCE/'assets',QA/'assets',dirs_exist_ok=True)
hook='''
                    web.CoreWebView2.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
                    web.CoreWebView2.WebResourceRequested+=delegate(object sender,CoreWebView2WebResourceRequestedEventArgs e){GroupPipelineQa.Intercepted++;byte[] bytes=Encoding.UTF8.GetBytes(GroupPipelineQa.Html);e.Response=web.CoreWebView2.Environment.CreateWebResourceResponse(new MemoryStream(bytes),200,"OK","Content-Type: text/html; charset=utf-8");};
'''
for path in (SOURCE/'src').glob('*.cs'):
    text=path.read_text('utf-8-sig')
    if path.name=='GroupTaskForm.cs':
        needle='await web.EnsureCoreWebView2Async(environment);'
        assert needle in text
        text=text.replace(needle,needle+hook)
    if path.name=='app.cs':
        needle='static void DoStart()\n        {'
        assert needle in text
        text=text.replace(needle,needle+'\n            if(GroupPipelineQa.QueueTesting){GroupPipelineQa.PrivateRuns++;btnStart.Enabled=false;return;}')
    (QA/'src'/path.name).write_text(text,encoding='utf-8-sig')
(QA/'src/GroupPipelineQa.cs').write_text(r'''
using System;using System.IO;using System.Linq;using System.Reflection;using System.Threading.Tasks;using System.Windows.Forms;using DouyinSpark;using Microsoft.Web.WebView2.WinForms;
class GroupPipelineQa {
 public static int Intercepted,PrivateRuns;public static bool QueueTesting;
 public static string Html="<!doctype html><meta charset='utf-8'><div data-e2e='im-dialog'><div data-e2e='conversation-item' data-conversation-id='qa_A' data-type='group' onclick=\"document.querySelector('#panel').dataset.conversationId='qa_A'\">QA group</div><div data-e2e='conversation-item' data-conversation-id='qa_B' data-type='group' onclick=\"document.querySelector('#panel').dataset.conversationId='qa_B'\">QA group</div></div><section id='panel' data-conversation-id='qa_A' data-type='group'><div contenteditable='true' style='height:90px'></div><button onclick=\"window.testSent.push(1);document.querySelector('[contenteditable]').innerHTML=''\">发送</button></section><script>window.testSent=[]</script>";
 static object Field(object o,string n){return o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
 static void Assert(bool ok,string text){if(!ok)throw new Exception(text);}
 static void Setup(string root){Directory.CreateDirectory(root);ConversationStore.Merge(Path.Combine(root,"conversations.json"),new[]{new ConversationRecord{name="同名群",kind="group",conversation_id="qa_A"},new ConversationRecord{name="同名群",kind="group",conversation_id="qa_B"}});new GroupTaskSettings{message="仅本地模拟消息😀",targets=new System.Collections.Generic.List<string>{"conversation:qa_A","conversation:qa_B"},schedule="13:10",paused=false,interval_seconds=15}.Save(root);}
 [STAThread]static void Main(){Application.EnableVisualStyles();var host=new Form{ShowInTaskbar=false,WindowState=FormWindowState.Minimized};host.Shown+=async delegate{try{await Test();File.WriteAllText("PASS.txt","PASS: actual WebView2 + group form, two exact-ID same-name groups, local submissions, separate journal, uncertain status, dedup and cancel. Every HTTP request intercepted; no real account or send.");}catch(Exception e){File.WriteAllText("ERROR.txt",e.ToString());}finally{host.Close();}};Application.Run(host);}
 static async Task Test(){string originalHtml=Html;string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"fake-data-"+Guid.NewGuid().ToString("N"));Setup(root);
  using(var form=new GroupTaskForm(root,AppDomain.CurrentDomain.BaseDirectory,true)){
   var done=new TaskCompletionSource<bool>();form.RunningChanged=delegate(bool v){if(!v)done.TrySetResult(true);};form.Show();Assert(await Task.WhenAny(done.Task,Task.Delay(45000))==done.Task,"pipeline timeout");
   var entries=GroupJournal.Read(root);Assert(entries.Count==2&&entries.All(e=>e.status=="SUBMITTED"),"two submitted pending results");Assert(Intercepted>0,"network hook missing");
   var web=(WebView2)Field(form,"web");int sent=SparkStorage.Json().Deserialize<int>(await web.ExecuteScriptAsync("window.testSent.length"));Assert(sent==2,"local submit count");
   var summary=GroupSummary.Build(new[]{"conversation:qa_A","conversation:qa_B"},entries,DateTime.Now);Assert(summary.Unconfirmed==2&&summary.Succeeded==0,"submission must not be counted delivered");
   await (Task)form.GetType().GetMethod("RunAsync",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{false});Assert(GroupJournal.Read(root).Count==2,"daily repeat journal");Assert(SparkStorage.Json().Deserialize<int>(await web.ExecuteScriptAsync("window.testSent.length"))==2,"daily repeat send");form.Close();
  }
  string keyboard=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"keyboard-data-"+Guid.NewGuid().ToString("N"));Setup(keyboard);
  Html=Html.Replace("<section id='panel'","<section class='messageMsgInputcontainer' id='panel'").Replace("contenteditable='true'","contenteditable='true' data-slate-editor='true'");int start=Html.IndexOf("<button"),end=Html.IndexOf("</button>")+9;Html=Html.Remove(start,end-start)+"<script>document.addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();window.testSent.push(1);document.querySelector('[contenteditable]').innerHTML='';}})</script>";
  using(var form=new GroupTaskForm(keyboard,AppDomain.CurrentDomain.BaseDirectory,true)){
   var done=new TaskCompletionSource<bool>();form.RunningChanged=delegate(bool v){if(!v)done.TrySetResult(true);};form.Show();Assert(await Task.WhenAny(done.Task,Task.Delay(45000))==done.Task,"keyboard pipeline timeout");var entries=GroupJournal.Read(keyboard);Assert(entries.Count==2&&entries.All(e=>e.status=="SUBMITTED"),"keyboard pending results");var web=(WebView2)Field(form,"web");Assert(SparkStorage.Json().Deserialize<int>(await web.ExecuteScriptAsync("window.testSent.length"))==2,"scoped native Enter submission");form.Close();
  }
  string cancel=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"cancel-data-"+Guid.NewGuid().ToString("N"));Setup(cancel);Html="<!doctype html><div data-e2e='login-dialog'>Local fake login only</div>";
  using(var form=new GroupTaskForm(cancel,AppDomain.CurrentDomain.BaseDirectory,true)){
   var done=new TaskCompletionSource<bool>();form.RunningChanged=delegate(bool v){if(!v)done.TrySetResult(true);};form.Show();await Task.Delay(1700);form.RequestStopAndClose();Assert(await Task.WhenAny(done.Task,Task.Delay(10000))==done.Task,"cancellation timeout");Assert(GroupJournal.Read(cancel).All(x=>x.status=="CANCELLED"),"cancelled login must not record submission");
  }
  Html=originalHtml;await TestQueues();
 }
 static async Task TestQueues(){
  string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"queue-data-"+Guid.NewGuid().ToString("N"));Setup(root);File.WriteAllText(Path.Combine(root,"config.json"),"{\"browser\":\"msedge\"}");
  Type p=typeof(DouyinSpark.Program);var flags=BindingFlags.Static|BindingFlags.NonPublic;Action<string,object> set=delegate(string n,object v){p.GetField(n,flags).SetValue(null,v);};Func<string,object> get=n=>p.GetField(n,flags).GetValue(null);Action<string,DateTime> call=delegate(string n,DateTime now){p.GetMethod(n,flags).Invoke(null,new object[]{now});};
  var button=new RoundButton{Enabled=true};DateTime at=DateTime.Now;
  set("BaseDir",root);set("btnStart",button);set("schedulePaused",false);set("scheduleTime",at.ToString("HH:mm"));set("scheduleLastTrigger","");set("nextScheduled",at);set("groupPlan",GroupTaskSettings.Read(root));set("nextGroupScheduled",at);set("privateQueueUntil",DateTime.MinValue);set("groupQueueUntil",DateTime.MinValue);set("groupTaskRunning",false);QueueTesting=true;
  call("TickSchedule",at);call("TickGroupSchedule",at);Assert(PrivateRuns==1&&get("groupWindow")==null&&(DateTime)get("groupQueueUntil")>at,"simultaneous schedules must queue group behind private");
  button.Enabled=true;call("TickGroupSchedule",at.AddSeconds(5));DateTime limit=DateTime.Now.AddSeconds(40);while(DateTime.Now<limit&&((bool)get("groupTaskRunning")||GroupJournal.Read(root).Count<2))await Task.Delay(250);
  Assert(GroupJournal.Read(root).Count==2&&GroupJournal.Read(root).All(x=>x.status=="SUBMITTED"),"queued group actually dispatched after private completes");((Form)get("groupWindow")).Close();
  DateTime tomorrow=at.AddDays(1);button.Enabled=true;set("nextScheduled",tomorrow);set("groupTaskRunning",true);call("TickSchedule",tomorrow);Assert(PrivateRuns==1&&(DateTime)get("privateQueueUntil")>tomorrow,"private schedule waits for active group");set("groupTaskRunning",false);call("TickSchedule",tomorrow.AddSeconds(5));Assert(PrivateRuns==2,"queued private dispatch after group completes");QueueTesting=false;
 }
}
''',encoding='utf-8-sig')
compiler=Path('C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe')
refs=['System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','System.Management.dll','System.Data.dll',str(compiler.parent/'Microsoft.VisualBasic.dll'),str(compiler.parent/'WPF/WindowsFormsIntegration.dll'),str(QA/'Microsoft.Web.WebView2.Core.dll'),str(QA/'Microsoft.Web.WebView2.WinForms.dll')]
subprocess.run([str(compiler),'/nologo','/codepage:65001','/target:winexe','/platform:x64','/main:GroupPipelineQa','/out:'+str(QA/'GroupPipelineQa.exe')]+['/r:'+x for x in refs]+[str(p) for p in (QA/'src').glob('*.cs')],check=True)
subprocess.run([str(QA/'GroupPipelineQa.exe')],cwd=QA,check=True,timeout=150)
assert not (QA/'ERROR.txt').exists(),(QA/'ERROR.txt').read_text('utf-8-sig') if (QA/'ERROR.txt').exists() else ''
print((QA/'PASS.txt').read_text('utf-8-sig'))
