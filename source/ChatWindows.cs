using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace JevChat {
public class ChatWindow {
    public IntPtr Handle;
    public uint ProcessId;
    public string Title;
    public string Platform;
    public override string ToString() { return Title+"   ·   PID "+ProcessId; }
}
public static class ChatWindows {
    [ThreadStatic] public static string CaptureIssue;
    delegate bool EnumProc(IntPtr window,IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr parameter);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window,IntPtr dc,uint flags);
    public static Bitmap CaptureClient(ChatWindow target) {
        Validate(target); if(IsIconic(target.Handle)) return null;
        var client=ClientBounds(target); if(client.Width<100 || client.Height<100 || (long)client.Width*client.Height>12000000) return null;
        var bitmap=new Bitmap(client.Width,client.Height); bool rendered;
        try {
        using(var g=Graphics.FromImage(bitmap)) { var dc=g.GetHdc(); try { rendered=PrintWindow(target.Handle,dc,3); } finally { g.ReleaseHdc(dc); } }
        if(rendered) {
            var first=bitmap.GetPixel(0,0).ToArgb(); int varied=0;
            for(int y=0;y<bitmap.Height;y+=40) for(int x=0;x<bitmap.Width;x+=40) if(bitmap.GetPixel(x,y).ToArgb()!=first) varied++;
            if(varied>20) return bitmap;
        }
        bitmap.Dispose(); return null;
        } catch { bitmap.Dispose(); throw; }
    }
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr WindowStyle64(IntPtr window,int index);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int WindowStyle32(IntPtr window,int index);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr GetTopWindow(IntPtr parent);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window,uint command);
    [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")] static extern int DwmInt(IntPtr window,int attribute,out int value,int size);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window,StringBuilder text,int capacity);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window,ref Point point);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out Rect rect,int size);

    public static string PlatformFor(string process) {
        switch((process ?? "").ToLowerInvariant()) {
            case "wechat": case "weixin": return "微信";
            case "qq": case "qqnt": return "QQ";
            default: return null;
        }
    }
    public static List<ChatWindow> Find(string platform) {
        var list=new List<ChatWindow>();
        EnumWindows(delegate(IntPtr window,IntPtr parameter) {
            try {
                if(!IsWindowVisible(window)) return true;
                var text=new StringBuilder(1024); GetWindowText(window,text,text.Capacity);
                uint pid; GetWindowThreadProcessId(window,out pid);
                using(var process=Process.GetProcessById((int)pid)) {
                    if(PlatformFor(process.ProcessName)!=platform) return true;
                    string title=text.ToString();
                    if(String.IsNullOrWhiteSpace(title)) {Rect rect;if(!GetWindowRect(window,out rect) || rect.Right-rect.Left<360 || rect.Bottom-rect.Top<260) return true;title=platform+"（未命名窗口）";}
                    list.Add(new ChatWindow { Handle=window,ProcessId=pid,Title=title,Platform=platform });
                }
            } catch(ArgumentException) {} catch(InvalidOperationException) {} catch(System.ComponentModel.Win32Exception) {}
            return true;
        },IntPtr.Zero);
        return list;
    }
    public static void Validate(ChatWindow target) {
        if(target==null || !IsWindow(target.Handle)) throw new InvalidOperationException("聊天窗口已关闭，请回到「选择窗口」重新选择。");
        uint pid; GetWindowThreadProcessId(target.Handle,out pid);
        if(pid!=target.ProcessId) throw new InvalidOperationException("窗口所属进程已变化，请重新选择。");
        try { using(var p=Process.GetProcessById((int)pid)) { if(PlatformFor(p.ProcessName)!=target.Platform) throw new InvalidOperationException("窗口身份已变化，请重新选择。"); } }
        catch(ArgumentException) { throw new InvalidOperationException("聊天软件已退出，请重新选择。"); }
    }
    public static async Task<Bitmap> Capture(ChatWindow target) {
        Validate(target);
        if(IsIconic(target.Handle)) ShowWindow(target.Handle,9);
        SetForegroundWindow(target.Handle);
        await Task.Delay(450);
        Validate(target);
        if(GetForegroundWindow()!=target.Handle) throw new InvalidOperationException("无法将聊天窗口显示到前台。请手动打开该窗口，或使用「框选聊天」。");
        Rect rect;
        if(DwmGetWindowAttribute(target.Handle,9,out rect,Marshal.SizeOf(typeof(Rect)))!=0 && !GetWindowRect(target.Handle,out rect))
            throw new InvalidOperationException("无法读取窗口位置，请使用框选聊天。");
        var bounds=Rectangle.FromLTRB(rect.Left,rect.Top,rect.Right,rect.Bottom);
        if(bounds.Width<50 || bounds.Height<50 || !SystemInformation.VirtualScreen.Contains(bounds))
            throw new InvalidOperationException("聊天窗口未完整显示在屏幕内，请移动到屏幕内再读取。");
        if((long)bounds.Width*bounds.Height>40000000) throw new InvalidOperationException("窗口过大，请缩小窗口后读取。");
        var image=new Bitmap(bounds.Width,bounds.Height);
        try {
            using(var g=Graphics.FromImage(image)) g.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size);
            if(GetForegroundWindow()!=target.Handle) throw new InvalidOperationException("读取时前台窗口发生变化，本次截图已丢弃，请重试。");
            return image;
        } catch { image.Dispose(); throw; }
    }
    public static Rectangle BoundsOf(ChatWindow target) {
        Validate(target); Rect rect;
        if(!GetWindowRect(target.Handle,out rect)) throw new InvalidOperationException("无法读取窗口位置。");
        return Rectangle.FromLTRB(rect.Left,rect.Top,rect.Right,rect.Bottom);
    }
    public static IntPtr Foreground { get { return GetForegroundWindow(); } }
    // Used only for positioning an already-bound window; no process enumeration on the UI thread.
    public static bool TryFollowBounds(ChatWindow target,out Rectangle bounds) {
        bounds=Rectangle.Empty; if(target==null || !IsWindowVisible(target.Handle) || IsIconic(target.Handle)) return false;
        uint pid; GetWindowThreadProcessId(target.Handle,out pid); if(pid!=target.ProcessId) return false;
        Rect rect; if(!GetWindowRect(target.Handle,out rect)) return false;
        bounds=Rectangle.FromLTRB(rect.Left,rect.Top,rect.Right,rect.Bottom);
        return bounds.Width>0 && bounds.Height>0;
    }
    public static Rectangle ClientBounds(ChatWindow target) {
        Validate(target); Rect rect; var origin=Point.Empty;
        if(!GetClientRect(target.Handle,out rect) || !ClientToScreen(target.Handle,ref origin)) return Rectangle.Empty;
        return new Rectangle(origin,new Size(rect.Right-rect.Left,rect.Bottom-rect.Top));
    }
    public static bool Minimized(ChatWindow target) { Validate(target); return IsIconic(target.Handle); }
    public static async Task PrepareSelection(ChatWindow target) {
        Validate(target); if(IsIconic(target.Handle)) ShowWindow(target.Handle,9);
        SetForegroundWindow(target.Handle); await Task.Delay(350); Validate(target);
    }
    // Keep the user's four margins as the chat window is moved or resized.
    public static Rectangle FitRegion(Rectangle offset,Size original,Rectangle current) {
        if(original.Width<=0 || original.Height<=0 || offset.Width<20 || offset.Height<20 || !new Rectangle(Point.Empty,original).Contains(offset)) return Rectangle.Empty;
        int width=current.Width-offset.Left-(original.Width-offset.Right);
        int height=current.Height-offset.Top-(original.Height-offset.Bottom);
        if(width<20 || height<20) return Rectangle.Empty;
        return new Rectangle(current.X+offset.X,current.Y+offset.Y,width,height);
    }
    internal static bool Uncovered(ChatWindow target,Rectangle region) {
        if(IsIconic(target.Handle) || !IsWindowVisible(target.Handle)) return false;
        int cloaked;
        if(DwmInt(target.Handle,14,out cloaked,4)==0 && cloaked!=0) return false;
        var window=GetTopWindow(IntPtr.Zero);
        for(int i=0;i<1500 && window!=IntPtr.Zero;i++,window=GetWindow(window,2)) {
            if(window==target.Handle) return true;
            if(!IsWindowVisible(window) || IsIconic(window)) continue;
            long style=IntPtr.Size==8 ? WindowStyle64(window,-20).ToInt64() : WindowStyle32(window,-20);
            // Click-through layered overlays (cursor/crosshair overlays) are not opaque windows.
            if((style&0x80020)==0x80020) continue;
            if(DwmInt(window,14,out cloaked,4)==0 && cloaked!=0) continue;
            Rect rect;
            if(DwmGetWindowAttribute(window,9,out rect,Marshal.SizeOf(typeof(Rect)))!=0 && !GetWindowRect(window,out rect)) continue;
            if(Rectangle.FromLTRB(rect.Left,rect.Top,rect.Right,rect.Bottom).IntersectsWith(region)) {
                uint pid; GetWindowThreadProcessId(window,out pid);
                try { using(var p=Process.GetProcessById((int)pid)) CaptureIssue="overlap:"+p.ProcessName+" style:"+style; } catch { CaptureIssue="overlap"; }
                return false;
            }
        }
        return false;
    }
    public static string TitleOf(ChatWindow target) {
        Validate(target); var value=new StringBuilder(1024); GetWindowText(target.Handle,value,value.Capacity); return value.ToString();
    }
    public static Bitmap ObserveRegion(ChatWindow target,Rectangle offset,Size originalSize) {
        CaptureIssue="bounds/minimized";
        Validate(target);
        var bounds=BoundsOf(target);
        var region=FitRegion(offset,originalSize,bounds);
        CaptureIssue="region="+region+" screen="+SystemInformation.VirtualScreen;
        if(region.IsEmpty || !SystemInformation.VirtualScreen.Contains(region) || !Uncovered(target,region)) return null;
        if((long)region.Width*region.Height>12000000) throw new InvalidOperationException("消息区域过大，请缩小聊天窗口或重新选择区域。");
        var image=new Bitmap(region.Width,region.Height);
        try {
            using(var g=Graphics.FromImage(image)) g.CopyFromScreen(region.Location,Point.Empty,region.Size);
            if(BoundsOf(target)!=bounds || !Uncovered(target,region)) { image.Dispose(); return null; }
            return image;
        } catch { image.Dispose(); throw; }
    }
}
}
