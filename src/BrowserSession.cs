using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace DouyinSpark
{
    static class BrowserSession
    {
        static readonly Dictionary<string,int> Ports=new Dictionary<string,int>();
        static readonly Dictionary<string,Task<CoreWebView2Environment>> Environments=new Dictionary<string,Task<CoreWebView2Environment>>();
        public static int Port(string root){if(!Ports.ContainsKey(root)){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();Ports[root]=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();}return Ports[root];}
        public static Task<CoreWebView2Environment> EnvironmentAsync(string root)
        {
            Task<CoreWebView2Environment> task;
            if(!Environments.TryGetValue(root,out task)||task.IsFaulted){
                var options=new CoreWebView2EnvironmentOptions{AdditionalBrowserArguments="--remote-debugging-address=127.0.0.1 --remote-debugging-port="+Port(root)};
                task=CoreWebView2Environment.CreateAsync(null,System.IO.Path.Combine(root,"conversation_profile"),options);Environments[root]=task;
            }
            return task;
        }
    }
}
