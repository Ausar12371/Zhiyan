using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
namespace JevChat {
public static class WechatVision {
    static void Trace(string text) { try { System.IO.File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"wechat-diagnostic.txt"),DateTime.Now.ToString("s")+" "+text); } catch { } }
    public static Rectangle Region(OcrLayout layout,Size size,out string conversation,int composerTop=0,bool mainWindow=false) {
        conversation=null; if(size.Width<360 || size.Height<300) return Rectangle.Empty;
        OcrBox send=null, search=null, header=null;
        foreach(var line in layout.Lines) {
            string name=(line.Text ?? "").Replace(" ","");
            if((name=="发送" || (name.StartsWith("发送") && name.Length<10) || name.Equals("send",StringComparison.OrdinalIgnoreCase)) && line.Bounds.Top>size.Height*.72 && line.Bounds.Left>size.Width*.65) send=line;
            if(name.Contains("搜索") && line.Bounds.Top<size.Height*.18 && line.Bounds.Left<size.Width*.35) search=line;
        }
        if(send==null && composerTop==0) { Trace("no-send"); return Rectangle.Empty; }
        int left=mainWindow ? (int)(size.Width*.34) : search==null ? 12 : Math.Max((int)(size.Width*.30),search.Bounds.Right+20);
        foreach(var line in layout.Lines) {
            var r=line.Bounds;
            if(r.Left<left || r.Top<20 || r.Bottom>Math.Min(100,size.Height*.17) || r.Width<30) continue;
            if(header==null || r.Left<header.Bounds.Left) header=line;
        }
        if(header==null) { Trace("no-header"); return Rectangle.Empty; }
        conversation=header.Text.Trim();
        if(search!=null || mainWindow) left=Math.Max(left,header.Bounds.Left-10);
        int top=header.Bounds.Bottom+15;
        // Keep a generous composer exclusion; user's drafts must never become incoming messages.
        int bottom=composerTop>0 ? composerTop-6 : Math.Min((int)(size.Height*.77),send.Bounds.Top-80);
        return bottom>top+60 ? Rectangle.FromLTRB(left,top,size.Width-12,bottom) : Rectangle.Empty;
    }
    public static string Messages(OcrLayout layout,Rectangle region,Bitmap bitmap) {
        return MessageGeometry.Read(layout,region,bitmap);
    }
    static int ComposerBoundary(Bitmap bitmap) {
        int green=0;
        for(int y=bitmap.Height/6;y<bitmap.Height*3/4;y+=3) for(int x=bitmap.Width/2;x<bitmap.Width-20;x+=3) {
            var c=bitmap.GetPixel(x,y); if(c.G>100 && c.G>c.R*1.4 && c.G>c.B*1.15) green++;
        }
        if(green<80) return 0;
        for(int y=(int)(bitmap.Height*.52);y<(int)(bitmap.Height*.87);y++) {
            int edges=0,total=0;
            for(int x=(int)(bitmap.Width*.40);x<(int)(bitmap.Width*.94);x+=4) {
                var a=bitmap.GetPixel(x,y); var b=bitmap.GetPixel(x,y-2);
                if(Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B)>=18) edges++;
                total++;
            }
            if(edges>total*.88) return y;
        }
        return 0;
    }
    public static Rectangle QqRegion(OcrLayout layout,Size size,string windowTitle,out string conversation) {
        conversation=windowTitle;
        var region=AutomaticLayout.MessageRegion(layout,size,windowTitle);
        if(region.IsEmpty) return region;
        if(windowTitle=="QQ" || windowTitle=="QQNT") {
            var header=layout.Lines.Where(l=>l.Bounds.Left>size.Width*.20 && l.Bounds.Top>=20 && l.Bounds.Bottom<Math.Min(110,size.Height*.18) && !String.IsNullOrWhiteSpace(l.Text) && l.Text!="QQ" && !l.Text.Contains("搜索") && !l.Text.Contains("消息") && l.Text!="联系人").OrderBy(l=>l.Bounds.Left).FirstOrDefault();
            if(header==null) return Rectangle.Empty;
            conversation=header.Text.Trim();
            region=Rectangle.FromLTRB(Math.Max(0,header.Bounds.Left-20),Math.Max(region.Top,header.Bounds.Bottom+12),region.Right,region.Bottom);
        }
        return region.Height>=60 ? region : Rectangle.Empty;
    }
    static async Task<TextFeed> ReadQq(ChatWindow target,LocalOcr ocr,CancellationToken ct) {
        var controls=await QqAccessible.TryRead(target,ct); if(controls!=null) return controls; if(QqAccessible.IsReading(target)) return null;
        var outer=ChatWindows.BoundsOf(target);var client=ChatWindows.ClientBounds(target);
        var offset=new Rectangle(client.X-outer.X,client.Y-outer.Y,client.Width,client.Height);
        // QQ's renderer can block PrintWindow indefinitely during tab switches.
        // UIA works when covered; the fallback reads only an unobstructed screen region.
        using(var bitmap=ChatWindows.ObserveRegion(target,offset,outer.Size)) {
            if(bitmap==null) return null;
            var layout=await ocr.ReadLayout(bitmap,ct); string conversation;
            var region=QqRegion(layout,bitmap.Size,target.Title,out conversation);
            if(region.IsEmpty) return null;
            using(var body=bitmap.Clone(region,bitmap.PixelFormat)) {
                layout=await ocr.ReadLayout(body,ct);
                foreach(var line in layout.Lines) line.Bounds.Offset(region.Location);
            }
            return new TextFeed {Text=MessageGeometry.Read(layout,region,bitmap),Conversation=conversation,Identity="qq-ocr:"+target.Handle.ToInt64()+":"+conversation};
        }
    }
    public static async Task<TextFeed> Read(ChatWindow target,LocalOcr ocr,CancellationToken ct) {
        if(target.Platform=="QQ") return await ReadQq(target,ocr,ct);
        Trace("capture-start");
        bool mainWindow=target.Title=="微信" || target.Title=="Weixin" || target.Title=="WeChat";
        var outer=ChatWindows.BoundsOf(target); var client=ChatWindows.ClientBounds(target);
        client=Rectangle.Intersect(client,System.Windows.Forms.SystemInformation.VirtualScreen);
        if(client.IsEmpty) return null;
        var offset=new Rectangle(client.X-outer.X,client.Y-outer.Y,client.Width,client.Height);
        using(var bitmap=ChatWindows.CaptureClient(target) ?? ChatWindows.ObserveRegion(target,offset,outer.Size)) {
            if(bitmap==null) { Trace("capture-failed "+ChatWindows.CaptureIssue); throw new InvalidOperationException("微信被其他窗口遮挡，暂不能识别"); }
            var layout=await ocr.ReadLayout(bitmap,ct); ct.ThrowIfCancellationRequested();
            string title; var region=Region(layout,bitmap.Size,out title,0,mainWindow);
            if(region.IsEmpty) {
                using(var boosted=new Bitmap(bitmap.Width,bitmap.Height))
                using(var graphics=Graphics.FromImage(boosted))
                using(var attributes=new System.Drawing.Imaging.ImageAttributes()) {
                    attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(new float[][] {
                        new float[]{4,0,0,0,0},new float[]{0,4,0,0,0},new float[]{0,0,4,0,0},new float[]{0,0,0,1,0},new float[]{-.1f,-.1f,-.1f,0,1}}));
                    graphics.DrawImage(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height),0,0,bitmap.Width,bitmap.Height,GraphicsUnit.Pixel,attributes);
                    var enhanced=await ocr.ReadLayout(boosted,ct);
                    region=Region(enhanced,bitmap.Size,out title,0,mainWindow);
                }
            }
            if(region.IsEmpty) {
                var crop=new Rectangle((int)(bitmap.Width*.85),(int)(bitmap.Height*.90),(int)(bitmap.Width*.15),bitmap.Height-(int)(bitmap.Height*.90));
                using(var zoom=new Bitmap(crop.Width*4,crop.Height*4))
                using(var graphics=Graphics.FromImage(zoom))
                using(var attributes=new System.Drawing.Imaging.ImageAttributes()) {
                    attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(new float[][] {
                        new float[]{-4,0,0,0,0},new float[]{0,-4,0,0,0},new float[]{0,0,-4,0,0},new float[]{0,0,0,1,0},new float[]{1.1f,1.1f,1.1f,0,1}}));
                    graphics.DrawImage(bitmap,new Rectangle(0,0,zoom.Width,zoom.Height),crop.X,crop.Y,crop.Width,crop.Height,GraphicsUnit.Pixel,attributes);
                    var buttons=await ocr.ReadLayout(zoom,ct);
                    foreach(var line in buttons.Lines) layout.Lines.Add(new OcrBox {Text=line.Text,Bounds=new Rectangle(crop.X+line.Bounds.X/4,crop.Y+line.Bounds.Y/4,line.Bounds.Width/4,line.Bounds.Height/4)});
                    region=Region(layout,bitmap.Size,out title,0,mainWindow);
                    if(region.IsEmpty) Trace("button-boxes="+buttons.Lines.Count);
                }
            }
            if(region.IsEmpty) region=Region(layout,bitmap.Size,out title,ComposerBoundary(bitmap),mainWindow);
            if(region.IsEmpty) { throw new InvalidOperationException("尚未定位微信标题和发送区（本地识别行数："+layout.Lines.Count+"）"); }
            int composer=ComposerBoundary(bitmap);
            if(composer>region.Top+60) region=Rectangle.FromLTRB(region.Left,region.Top,region.Right,composer-6);
            // Re-read the isolated message pane so full-window OCR cannot drop small bottom messages.
            using(var body=bitmap.Clone(region,bitmap.PixelFormat)) {
                var bodyLayout=await ocr.ReadLayout(body,ct);
                foreach(var line in bodyLayout.Lines) line.Bounds.Offset(region.Location);
                layout=bodyLayout;
            }
            string messages=Messages(layout,region,bitmap);
            Trace("success chars="+messages.Length+" region="+region);
            return new TextFeed {Text=messages,Conversation=title,Identity="wechat-ocr:"+target.Handle.ToInt64()+":"+title};
        }
    }
}
}
