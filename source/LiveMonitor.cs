using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace JevChat {
// A local snapshot debounce; no chat data is persisted.
public class ChangeGate {
    public string Current="";
    string delivered="";
    DateTime changed=DateTime.MinValue, last=DateTime.MinValue;
    public int Revision { get; private set; }
    public static string Normalize(string text) { return (text ?? "").Replace("\r","").Trim(); }
    public bool Observe(string text,DateTime now) {
        text=Normalize(text);
        if(text==Current) return false;
        Current=text; changed=now; Revision++; return true;
    }
    public bool Ready(DateTime now) { return Current.Length>0 && Current!=delivered && (now-changed).TotalSeconds>=3 && (now-last).TotalSeconds>=10; }
    public int Claim(DateTime now) { delivered=Current; last=now; return Revision; }
    public void Abandon(string text) { if(delivered==text) delivered=""; }
    public bool IsCurrent(int revision) { return revision==Revision && Current.Length>0; }
}
// Only join snapshots with a multi-line suffix/prefix overlap. Never stitch disjoint chats.
public sealed class ConversationBuffer {
    string identity;
    string[] lines=new string[0];
    public string Observe(string key,string snapshot) {
        var next=(snapshot ?? "").Split(new[]{'\n'},StringSplitOptions.RemoveEmptyEntries);
        if(key!=identity || next.Length==0) { identity=key; lines=next; return String.Join("\n",lines); }
        int overlap=0;
        for(int n=Math.Min(lines.Length,next.Length);n>=2;n--) {
            bool same=true; for(int i=0;i<n;i++) if(lines[lines.Length-n+i]!=next[i]) { same=false; break; }
            if(same) { overlap=n; break; }
        }
        if(overlap>0) { var joined=new List<string>(lines); for(int i=overlap;i<next.Length;i++) joined.Add(next[i]); lines=joined.ToArray(); }
        else lines=next;
        if(lines.Length>100) { var tail=new string[100]; Array.Copy(lines,lines.Length-100,tail,0,100); lines=tail; }
        return String.Join("\n",lines);
    }
}
public class TextFeed {
    public string Text, Identity, Conversation;
}
public static class AccessibleChat {
      public static TextFeed Read(ChatWindow target) {
          ChatWindows.Validate(target);
          TextFeed result=null;
          if(target.Platform=="微信") {
              try { result=ReadElement(AutomationElement.FromHandle(target.Handle),true); } catch { }
          }
          // Chromium chat windows may expose MSAA before the managed UIA bridge is ready.
          if(result==null) try { result=LegacyMessages.Read(target.Handle,target.Platform=="微信"); } catch { }
          if(result==null) {
              var root=AutomationElement.FromHandle(target.Handle);
              result=ReadElement(root,target.Platform=="微信");
          }
          ChatWindows.Validate(target); return result;
  }
  internal static class LegacyMessages {
      [System.Runtime.InteropServices.DllImport("oleacc.dll")]
      static extern int AccessibleObjectFromWindow(IntPtr hwnd,uint id,ref Guid iid,[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Interface)] out Accessibility.IAccessible value);
      [System.Runtime.InteropServices.DllImport("oleacc.dll")]
      static extern int AccessibleChildren(Accessibility.IAccessible parent,int start,int count,[System.Runtime.InteropServices.Out,System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray,SizeParamIndex=2)] object[] children,out int obtained);
      static IEnumerable<object> Children(Accessibility.IAccessible node) {
          int count=Math.Min(node.accChildCount,600); if(count<=0) yield break;
          var values=new object[count]; int obtained;
          if(AccessibleChildren(node,0,count,values,out obtained)<0) yield break;
          for(int i=0;i<obtained;i++) yield return values[i];
      }
      static Accessibility.IAccessible Find(Accessibility.IAccessible node,int depth,ref int budget,bool wechat) {
          if(node==null || depth>24 || --budget<=0) return null;
          string name=node.get_accName(0) ?? "";
          int role=Convert.ToInt32(node.get_accRole(0));
          if((name=="消息列表" || name=="聊天内容" || name=="Messages" || name=="Message list" || (wechat && name=="消息" && role==33)) && role!=43 && role!=42) return node;
          foreach(var child in Children(node)) {
              var found=Find(child as Accessibility.IAccessible,depth+1,ref budget,wechat);
              if(found!=null) return found;
          }
          return null;
      }
      static void Collect(Accessibility.IAccessible node,int depth,StringBuilder output,ref int budget) {
          if(depth>20 || --budget<=0 || output.Length>=16000) return;
          foreach(var child in Children(node)) {
              var nested=child as Accessibility.IAccessible;
              if(nested!=null) {
                  int role=Convert.ToInt32(nested.get_accRole(0));
                  if(role==42 || (Convert.ToInt32(nested.get_accState(0))&0x20000000)!=0) continue;
                  if(nested.accChildCount>0) Collect(nested,depth+1,output,ref budget);
                  else { var name=nested.get_accName(0); if(!String.IsNullOrWhiteSpace(name)) output.AppendLine(name); }
              } else if(child is int) {
                  int role=Convert.ToInt32(node.get_accRole(child));
                  if(role==42 || (Convert.ToInt32(node.get_accState(child))&0x20000000)!=0) continue;
                  var name=node.get_accName(child); if(!String.IsNullOrWhiteSpace(name)) output.AppendLine(name);
              }
          }
      }
      internal static TextFeed Read(IntPtr hwnd,bool wechat=false) {
          Guid iid=new Guid("618736e0-3c3d-11cf-810c-00aa00389b71"); Accessibility.IAccessible root;
          if(AccessibleObjectFromWindow(hwnd,0xfffffffc,ref iid,out root)<0 || root==null) return null;
          int budget=2500; var list=Find(root,0,ref budget,wechat); if(list==null) return null;
          var text=new StringBuilder(); budget=1800; Collect(list,0,text,ref budget);
          return new TextFeed { Text=text.ToString(),Identity="msaa:"+hwnd.ToInt64() };
      }
  }
    internal static TextFeed ReadElement(AutomationElement root,bool wechat=false) {
        // Do not read the whole window: contact lists and draft editors are not messages.
        var names=wechat ? new[]{"消息列表","聊天内容","Messages","Message list","消息"} : new[]{"消息列表","聊天内容","Messages","Message list"};
        AutomationElement list=null;
        foreach(var name in names) {
            var candidates=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.NameProperty,name));
            foreach(AutomationElement candidate in candidates) {
                var type=candidate.Current.ControlType;
                if(IsMessageContainer(type) && !candidate.Current.IsOffscreen && (name!="消息" || type==ControlType.List)) { list=candidate; break; }
            }
            if(list!=null) break;
        }
        if(list==null) throw new InvalidOperationException("此窗口未开放可读取的消息文本。请选择「本地视觉」监测；无需逐次截图。");
        var output=new StringBuilder(); object pattern;
        if(list.TryGetCurrentPattern(TextPattern.Pattern,out pattern)) output.Append(((TextPattern)pattern).DocumentRange.GetText(16000));
        else {
            var entries=list.FindAll(TreeScope.Children,Condition.TrueCondition);
            for(int i=Math.Max(0,entries.Count-80);i<entries.Count;i++) {
                var item=entries[i]; if(item.Current.ControlType==ControlType.Edit || item.Current.IsPassword) continue;
                string value=item.Current.Name;
                if(String.IsNullOrWhiteSpace(value)) {
                    var texts=item.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Text));
                    var row=new StringBuilder(); foreach(AutomationElement text in texts) { if(!text.Current.IsPassword) row.Append(text.Current.Name).Append(" "); }
                    value=row.ToString();
                }
                if(!String.IsNullOrWhiteSpace(value)) output.AppendLine(value);
            }
        }
        string result=output.ToString(); if(result.Length>16000) result=result.Substring(result.Length-16000);
        string conversation=wechat ? WechatHeader(root,list) : null;
        return new TextFeed { Text=result,Identity=String.Join(".",list.GetRuntimeId())+"|"+conversation,Conversation=conversation };
    }
    static string WechatHeader(AutomationElement root,AutomationElement list) {
        var bounds=list.Current.BoundingRectangle;
        if(bounds.IsEmpty) return null;
        // Only inspect header text directly above the message pane, never the contact list or composer.
        var candidates=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Text));
        string title=null; double nearest=Double.MaxValue;
        foreach(AutomationElement item in candidates) {
            if(item.Current.IsOffscreen || item.Current.IsPassword) continue;
            var rect=item.Current.BoundingRectangle; string name=item.Current.Name;
            if(String.IsNullOrWhiteSpace(name) || name.Length>100 || name=="微信" || name=="Weixin" || name=="WeChat") continue;
            if(rect.IsEmpty || rect.Left<bounds.Left || rect.Right>bounds.Right || rect.Bottom>bounds.Top || bounds.Top-rect.Bottom>100) continue;
            double distance=bounds.Top-rect.Bottom;
            if(distance<nearest) { nearest=distance; title=name.Trim(); }
        }
        return title;
    }
    public static bool IsMessageContainer(ControlType type) {
        return type==ControlType.List || type==ControlType.Document || type==ControlType.Pane || type==ControlType.Custom || type==ControlType.Group;
    }
}
public static class ChatRedaction {
    public static string Apply(string text) {
        return System.Text.RegularExpressions.Regex.Replace(text ?? "",@"\bsk-(?:or-v1-)?[A-Za-z0-9_-]{16,}","[已隐藏 API 密钥]");
    }
}
public sealed class LocalOcr : IDisposable {
    Process worker;
    async Task<string> Line(CancellationToken ct) {
        var read=worker.StandardOutput.ReadLineAsync();
        var deadline=Task.Delay(12000,ct);
        if(await Task.WhenAny(read,deadline)!=read) { Dispose(); ct.ThrowIfCancellationRequested(); throw new InvalidOperationException("本地 OCR 超时，监测已停止。"); }
        ct.ThrowIfCancellationRequested();
        var line=await read;
        if(line==null) throw new InvalidOperationException("本地 OCR 已退出，请检查 Windows 中文 OCR 组件。");
        return line;
    }
    public async Task Start(CancellationToken ct) {
        if(worker!=null) return;
        var path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"LocalOcr.ps1");
        if(!File.Exists(path)) throw new InvalidOperationException("缺少 LocalOcr.ps1，请使用完整软件包。");
        worker=new Process { StartInfo=new ProcessStartInfo {
            FileName=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell\\v1.0\\powershell.exe"),
            Arguments="-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+path+"\"",
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
            StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8
        } };
        worker.Start(); worker.BeginErrorReadLine();
        string ready=await Line(ct);
        if(ready!="READY") { Dispose(); throw new InvalidOperationException("Windows 中文 OCR 不可用，请检查中文语言的 OCR 组件。"); }
    }
    public async Task<string> Read(Bitmap bitmap,CancellationToken ct) {
        return (await ReadLayout(bitmap,ct)).Text;
    }
    public async Task<OcrLayout> ReadLayout(Bitmap bitmap,CancellationToken ct) {
        if(worker==null) await Start(ct);
        double scale=Math.Min(1.0,2600.0/Math.Max(bitmap.Width,bitmap.Height));
        using(var memory=new MemoryStream()) {
            if(scale<1) using(var reduced=new Bitmap(bitmap,new Size(Math.Max(1,(int)(bitmap.Width*scale)),Math.Max(1,(int)(bitmap.Height*scale))))) reduced.Save(memory,ImageFormat.Png);
            else bitmap.Save(memory,ImageFormat.Png);
            await worker.StandardInput.WriteLineAsync(Convert.ToBase64String(memory.ToArray()));
            await worker.StandardInput.FlushAsync();
        }
        string response=await Line(ct); var json=Json.Read(response);
        if(json.ContainsKey("error")) throw new InvalidOperationException(Convert.ToString(json["error"]));
        var layout=new OcrLayout { Text=Convert.ToString(json["text"]) };
        object values;
        if(json.TryGetValue("lines",out values) && values is object[]) foreach(var item in (object[])values) {
            var row=Json.Map(item);
            layout.Lines.Add(new OcrBox { Text=Convert.ToString(row["text"]),Bounds=new Rectangle((int)(Convert.ToDouble(row["x"])/scale),(int)(Convert.ToDouble(row["y"])/scale),(int)(Convert.ToDouble(row["width"])/scale),(int)(Convert.ToDouble(row["height"])/scale)) });
        }
        return layout;
    }
    public void Dispose() {
        var process=worker; worker=null; if(process==null) return;
        try { if(!process.HasExited) process.Kill(); } catch(InvalidOperationException) {} finally { process.Dispose(); }
    }
}
}
