using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Drawing;

namespace DouyinSpark
{
    partial class Program
    {
        static string FriendsPath { get { return Path.Combine(BaseDir, "friends.json"); } }

        static void ShowFriendStatus()
        {
            try
            {
                var sent = new Dictionary<string,string>();
                string csv = Path.Combine(BaseDir,"logs","spark_log.csv");
                if (File.Exists(csv)) foreach (var line in File.ReadAllLines(csv))
                {
                    var cells = ParseCsvLine(line);
                    if (cells.Length >= 5 && cells[0].StartsWith(DateTime.Today.ToString("yyyy-MM-dd"))) sent[cells[2].TrimEnd('/')] = cells[4];
                }
                var form = new ThemedForm { Text="好友今日状态", Size=new Size(760,460), StartPosition=FormStartPosition.CenterParent };
                var grid = new DataGridView { Dock=DockStyle.Fill, ReadOnly=true, AllowUserToAddRows=false, AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill };
                grid.Columns.Add("name","好友"); grid.Columns.Add("state","今日状态"); grid.Columns.Add("url","主页");
                foreach (var f in SparkStorage.ReadFriends(FriendsPath))
                {
                    string status;
                    string state = !FriendRecord.ValidUrl(f.url) ? "待补主页链接（发送时跳过）" : sent.TryGetValue(f.url.TrimEnd('/'),out status) ? status : "未发送";
                    grid.Rows.Add(f.name,state,f.url);
                }
                form.Controls.Add(grid); form.ShowDialog(mainForm);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message,"好友状态"); }
        }

        static string ResolveProfile(string text)
        {
            var direct = Regex.Match(text,@"https://www\.douyin\.com/user/[A-Za-z0-9_-]+/?");
            if (direct.Success) return direct.Value;
            var shortLink = Regex.Match(text,@"https://v\.douyin\.com/[A-Za-z0-9_-]+/?");
            if (!shortLink.Success) return null;
            var request = (HttpWebRequest)WebRequest.Create(shortLink.Value);
            request.Timeout=10000; request.AllowAutoRedirect=true;
            using (var response = (HttpWebResponse)request.GetResponse())
            {
                if (FriendRecord.ValidUrl(response.ResponseUri.AbsoluteUri)) return response.ResponseUri.AbsoluteUri;
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    direct = Regex.Match(reader.ReadToEnd(),@"https://www\.douyin\.com/user/[A-Za-z0-9_-]+/?");
                    return direct.Success ? direct.Value : null;
                }
            }
        }

        static void AddFriend()
        {
            try
            {
                var form = new ThemedForm { Text="管理好友", Size=new Size(620,540), StartPosition=FormStartPosition.CenterParent };
                var list = new ListBox { Location=new Point(16,16), Size=new Size(570,180) };
                foreach (var friend in SparkStorage.ReadFriends(FriendsPath)) list.Items.Add(friend);
                var delete = new Button { Text="删除选中", Location=new Point(16,205), Size=new Size(110,30) };
                delete.Click += delegate
                {
                    var friend = list.SelectedItem as FriendRecord;
                    if (friend == null || MessageBox.Show("删除此好友？","管理好友",MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                    try { SparkStorage.DeleteFriend(FriendsPath,friend); list.Items.Remove(friend); }
                    catch (Exception ex) { MessageBox.Show(ex.Message); }
                };
                var label = new Label { Text="每行填写：昵称|主页链接（也支持主页分享短链）\n缺失链接的旧条目会保留，请补好主页后重新添加。", Location=new Point(16,248), AutoSize=true };
                var input = new TextBox { Location=new Point(16,292), Size=new Size(570,130), Multiline=true, ScrollBars=ScrollBars.Vertical };
                var paste = new Button { Text="粘贴链接", Location=new Point(16,440), Size=new Size(110,32) };
                paste.Click += delegate { try { input.Text=Clipboard.GetText(); } catch { } };
                var save = new Button { Text="保存添加", Location=new Point(470,440), Size=new Size(110,32) };
                save.Click += delegate
                {
                    save.Enabled=false;
                    string text=input.Text;
                    new System.Threading.Thread(delegate()
                    {
                        try
                        {
                            var incoming=new List<FriendRecord>();
                            foreach (var line in text.Split('\n').Select(x=>x.Trim()).Where(x=>x.Length>0))
                            {
                                int split=line.IndexOf('|'); string name=split>=0?line.Substring(0,split).Trim():"抖音好友";
                                string url=ResolveProfile(split>=0?line.Substring(split+1).Trim():line);
                                if (!FriendRecord.ValidUrl(url)) throw new InvalidDataException("无法识别主页链接，请提供完整主页或个人主页分享短链。输入未保存。");
                                incoming.Add(new FriendRecord {name=name.Length>0?name:"抖音好友",url=url});
                            }
                            int count=SparkStorage.AddFriends(FriendsPath,incoming);
                            form.BeginInvoke((Action)delegate { MessageBox.Show("已添加 "+count+" 位好友；重复主页已跳过。"); form.Close(); });
                        }
                        catch(Exception ex) { form.BeginInvoke((Action)delegate { MessageBox.Show(ex.Message); save.Enabled=true; }); }
                    }) { IsBackground=true }.Start();
                };
                form.Controls.AddRange(new Control[]{list,delete,label,input,paste,save}); form.ShowDialog(mainForm);
            }
            catch(Exception ex) { MessageBox.Show(ex.Message,"管理好友"); }
        }

        static void ImportFollowing()
        {
            StartFollowingImport();
        }

        static void ShowFollowingPick(List<string[]> items, HashSet<string> exist)
        {
            using(var form=new FollowingPickForm(BaseDir,items,exist))form.ShowDialog(mainForm);
        }
    }
}
