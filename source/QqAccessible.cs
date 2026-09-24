using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
namespace JevChat {
public static class QqAccessible {
    static readonly Dictionary<string,Task<TextFeed>> pending=new Dictionary<string,Task<TextFeed>>();
    static readonly object sync=new object();
    sealed class Binding {public AutomationElement Root,Source,Header;}
    static readonly Dictionary<string,Binding> bindings=new Dictionary<string,Binding>();
    static string Key(ChatWindow target,string title) {return target.ProcessId+":"+target.Handle+":"+title;}
    public static bool IsMessageImage(string name,Rectangle image,Rectangle area) {
        if(image.IsEmpty || !area.IntersectsWith(image)) return false;
        if(!String.IsNullOrWhiteSpace(name) && (name.Contains("图片") || name.Contains("表情") || name.Contains("动画"))) return true;
        // Unnamed square icons near either edge are avatars, not message attachments.
        bool edge=image.Left-area.Left<area.Width*.09 || area.Right-image.Right<area.Width*.09;
        bool avatar=image.Width<=area.Width*.12 && image.Height<=area.Width*.12 && Math.Abs(image.Width-image.Height)<8;
        return !(edge && avatar) && image.Width>=24 && image.Height>=24;
    }
    static Rectangle Box(AutomationElement element) { var r=element.Cached.BoundingRectangle;return r.IsEmpty?Rectangle.Empty:Rectangle.FromLTRB((int)r.Left,(int)r.Top,(int)r.Right,(int)r.Bottom); }
    public static string AvatarRole(Rectangle avatar,Rectangle list) {
        if(avatar.IsEmpty || avatar.Width>list.Width*.25) return "【身份不明】";
        double center=(avatar.Left+avatar.Width/2.0-list.Left)/list.Width;
        return center<.22?"【对方】":center>.78?"【我】":"【身份不明】";
    }
    static void Trace(string message) {try {System.IO.File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"qq-diagnostic.txt"),DateTime.Now.ToString("s")+" "+message);}catch{}}
    public static bool IsReading(ChatWindow target) {
        string key=target.ProcessId+":"+target.Handle+":"+ChatWindows.TitleOf(target);
        lock(sync) {Task<TextFeed> task;return pending.TryGetValue(key,out task);}
    }
    public static async Task<TextFeed> TryRead(ChatWindow target,CancellationToken ct) {
        Task<TextFeed> task;string key=target.ProcessId+":"+target.Handle+":"+ChatWindows.TitleOf(target);
        lock(sync) {
            foreach(var old in pending.Where(p=>p.Key!=key && p.Value.IsCompleted).Select(p=>p.Key).ToArray()) pending.Remove(old);
            if(!pending.TryGetValue(key,out task)) {
                if(pending.Count>=8) return null;
                task=Task.Run(()=>Read(target));pending[key]=task;
            }
        }
        if(await Task.WhenAny(task,Task.Delay(4000,ct))!=task) {ct.ThrowIfCancellationRequested();Trace("uia-timeout");return null;}
        try {ct.ThrowIfCancellationRequested();var result=await task;if(result==null) lock(sync) bindings.Remove(key);return result;} catch(OperationCanceledException) {throw;} catch {lock(sync) bindings.Remove(key);Trace("uia-unavailable");return null;} finally {lock(sync) {pending.Remove(key);}}
    }
    static IEnumerable<AutomationElement> Descendants(AutomationElement element) {
        var children=element.CachedChildren;
        if(children==null) yield break;
        foreach(AutomationElement child in children) {
            yield return child;
            foreach(var nested in Descendants(child)) yield return nested;
        }
    }
    static TextFeed Read(ChatWindow target) {
        string originalTitle=ChatWindows.TitleOf(target);
        ChatWindows.Validate(target);
        var cache=new CacheRequest {TreeScope=TreeScope.Subtree,TreeFilter=Automation.ControlViewCondition};
        cache.Add(AutomationElement.NameProperty);cache.Add(AutomationElement.ControlTypeProperty);
        cache.Add(AutomationElement.BoundingRectangleProperty);cache.Add(AutomationElement.IsOffscreenProperty);
        cache.Add(AutomationElement.IsPasswordProperty);
        var timer=System.Diagnostics.Stopwatch.StartNew();
        string bindingKey=Key(target,originalTitle);Binding binding;
        lock(sync) bindings.TryGetValue(bindingKey,out binding);
        var root=binding==null?AutomationElement.FromHandle(target.Handle):binding.Root;
        var source=binding==null?root.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.NameProperty,"消息列表")):binding.Source;
        var headerCache=new CacheRequest {TreeScope=TreeScope.Element};
        headerCache.Add(AutomationElement.NameProperty);headerCache.Add(AutomationElement.BoundingRectangleProperty);
        string beforeHeader=binding==null || binding.Header==null?null:binding.Header.GetUpdatedCache(headerCache).Cached.Name;
        if(source==null) {Trace("no-message-container ms="+timer.ElapsedMilliseconds);return null;}
        long locateMs=timer.ElapsedMilliseconds;
        // Cache only the conversation subtree, excluding contact/member lists and toolbars.
        var list=source.GetUpdatedCache(cache);
        long snapshotMs=timer.ElapsedMilliseconds;
        if(list==null || list.Cached.IsOffscreen) {Trace("no-message-container");return null;}
        Rectangle area=Box(list);if(area.Width<100 || area.Height<60) return null;
        string title=originalTitle;
        bool headerFound=false;AutomationElement selectedHeader=null;
        if(binding!=null && binding.Header!=null) {
            var currentHeader=binding.Header.GetUpdatedCache(headerCache);
            string name=currentHeader.Cached.Name;var r=Box(currentHeader);
            if(name!=beforeHeader) return null;
            if(!String.IsNullOrWhiteSpace(name) && !r.IsEmpty && r.Left>=area.Left && r.Left<area.Left+area.Width*.6 && r.Bottom<=area.Top && area.Top-r.Bottom<100) {
                title=name;headerFound=true;selectedHeader=binding.Header;
            }
        }
        var conditions=new List<Condition> {new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button)};
        foreach(var excluded in new[]{"","关闭","最小化","最大化","返回","消息","联系人","更多","QQ空间"})
            conditions.Add(new NotCondition(new PropertyCondition(AutomationElement.NameProperty,excluded)));
        // FindFirst stops at the top header instead of enumerating controls inside every message.
        for(int attempt=0;attempt<6 && !headerFound;attempt++) {
            AutomationElement button;
            using(headerCache.Activate()) button=root.FindFirst(TreeScope.Descendants,new AndCondition(conditions.ToArray()));
            if(button==null) break;
            string name=button.Cached.Name;var r=Box(button);
            if(!r.IsEmpty && r.Left>=area.Left && r.Left<area.Left+area.Width*.6 && r.Bottom<=area.Top && area.Top-r.Bottom<100) {title=name;headerFound=true;selectedHeader=button;break;}
            conditions.Add(new NotCondition(new PropertyCondition(AutomationElement.NameProperty,name)));
        }
        if(!headerFound && (title.Contains("等") && title.EndsWith("个会话") || title=="QQ" || title=="QQNT")) {Trace("waiting-header");return null;}
        long headerMs=timer.ElapsedMilliseconds;
        var rows=new List<string>();string role="【身份不明】";
        var children=Descendants(list).Where(e=>e.Cached.ControlType!=ControlType.Text && e.Cached.ControlType!=ControlType.Image &&
            !Descendants(e).Any(n=>n.Cached.ControlType!=ControlType.Text && n.Cached.ControlType!=ControlType.Image &&
                Descendants(n).Any(l=>l.Cached.ControlType==ControlType.Text || l.Cached.ControlType==ControlType.Image))).ToList();
        for(int i=Math.Max(0,children.Count-160);i<children.Count;i++) {
            var child=children[i]; if(child.Cached.IsOffscreen) continue;
            var leaves=Descendants(child).Where(e=>e.Cached.ControlType==ControlType.Text || e.Cached.ControlType==ControlType.Image);
            var visible=new List<AutomationElement>();
            foreach(AutomationElement leaf in leaves) if(!leaf.Cached.IsOffscreen && !leaf.Cached.IsPassword && area.IntersectsWith(Box(leaf))) visible.Add(leaf);
            if(child.Cached.ControlType==ControlType.Text) continue;
            if(visible.Count==0 && !String.IsNullOrWhiteSpace(child.Cached.Name)) {role=AvatarRole(Box(child),area);continue;}
            var body=new List<string>();Rectangle bounds=Rectangle.Empty;
            foreach(var leaf in visible.OrderBy(e=>Box(e).Top).ThenBy(e=>Box(e).Left)) {
                var r=Box(leaf);string text=leaf.Cached.Name ?? "";
                if(leaf.Cached.ControlType==ControlType.Image) {if(!IsMessageImage(text,r,area)) continue; text="[图片或表情，内容未识别]";}
                if(String.IsNullOrWhiteSpace(text) || MessageGeometry.Timeline(text,r,area)) continue;
                body.Add(text.Replace("【我】","〔原文：我〕").Replace("【对方】","〔原文：对方〕"));bounds=bounds.IsEmpty?r:Rectangle.Union(bounds,r);
            }
            if(body.Count==0) {role="【身份不明】";continue;}
            string sender=role=="【身份不明】"?MessageGeometry.Side(bounds,area):role;
            rows.Add(sender+String.Join(" / ",body));role="【身份不明】";
        }
        ChatWindows.Validate(target);
        if(ChatWindows.TitleOf(target)!=originalTitle) return null;
        lock(sync) {
            if(bindings.Count>=16) bindings.Clear();
            bindings[bindingKey]=new Binding {Root=root,Source=source,Header=selectedHeader};
        }
        Trace("uia-ok cached="+(binding!=null)+" ms="+timer.ElapsedMilliseconds+" locate="+locateMs+" snapshot="+snapshotMs+" header="+headerMs+" rows="+rows.Count+" unknown="+rows.Count(r=>r.StartsWith("【身份不明】")));
        return new TextFeed {Text=String.Join("\n",rows),Conversation=title,Identity="qq-uia:"+target.Handle.ToInt64()+":"+title};
    }
}
}