using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
namespace JevChat {
public static class MessageGeometry {
    class Part { public Rectangle Box; public int Pixels; }
    class Row { public Rectangle Body; public int Top; public string Role; public List<string> Text=new List<string>(); }
    static int Distance(Color a,Color b) { return Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B); }
    static List<Part> Parts(Bitmap source,Rectangle region) {
        const int step=3;
        int w=(region.Width+step-1)/step,h=(region.Height+step-1)/step;
        var colors=new Color[w*h]; var hist=new Dictionary<int,int>();
        using(var image=new Bitmap(source.Width,source.Height,PixelFormat.Format32bppArgb)) {
            using(var g=Graphics.FromImage(image)) g.DrawImageUnscaled(source,0,0);
            var bits=image.LockBits(new Rectangle(Point.Empty,image.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            try {
                var bytes=new byte[Math.Abs(bits.Stride)*image.Height]; Marshal.Copy(bits.Scan0,bytes,0,bytes.Length);
                for(int y=0;y<h;y++) for(int x=0;x<w;x++) {
                    int offset=(region.Top+y*step)*bits.Stride+(region.Left+x*step)*4;
                    var c=Color.FromArgb(bytes[offset+2],bytes[offset+1],bytes[offset]); colors[y*w+x]=c;
                    int key=(c.R/8<<10)|(c.G/8<<5)|(c.B/8); int count; hist.TryGetValue(key,out count); hist[key]=count+1;
                }
            } finally { image.UnlockBits(bits); }
        }
        int dominant=hist.OrderByDescending(k=>k.Value).First().Key;
        var background=Color.FromArgb(((dominant>>10)&31)*8+4,((dominant>>5)&31)*8+4,(dominant&31)*8+4);
        var mask=new bool[w*h]; for(int i=0;i<mask.Length;i++) mask[i]=Distance(colors[i],background)>45;
        // Close small glyph gaps without bridging the normal avatar/bubble gap.
        var filled=(bool[])mask.Clone();
        for(int y=1;y<h-1;y++) for(int x=1;x<w-1;x++) {
            int i=y*w+x; if((mask[i-1]&&mask[i+1])||(mask[i-w]&&mask[i+w])) filled[i]=true;
        }
        var result=new List<Part>(); var queue=new Queue<int>();
        for(int start=0;start<filled.Length;start++) {
            if(!filled[start]) continue;
            filled[start]=false; queue.Enqueue(start); int l=w,t=h,r=0,b=0,count=0;
            while(queue.Count>0) {
                int i=queue.Dequeue(),x=i%w,y=i/w; l=Math.Min(l,x);r=Math.Max(r,x);t=Math.Min(t,y);b=Math.Max(b,y);count++;
                for(int direction=0;direction<4;direction++) {
                    int n=direction==0?(x>0?i-1:-1):direction==1?(x<w-1?i+1:-1):direction==2?(y>0?i-w:-1):(y<h-1?i+w:-1);
                    if(n>=0&&filled[n]) {filled[n]=false;queue.Enqueue(n);}
                }
            }
            if(count>=16) result.Add(new Part {Box=Rectangle.Intersect(region,new Rectangle(region.Left+l*step,region.Top+t*step,(r-l+1)*step,(b-t+1)*step)),Pixels=count});
        }
        return result;
    }
    public static string Side(Rectangle body,Rectangle region) {
        int left=body.Left-region.Left,right=region.Right-body.Right;
        // Compare the whole bubble's outside edges, never the first word or its color.
        if(body.Width>region.Width*.92 || left<0 || right<0) return "【身份不明】";
        if(left<=region.Width*.20 && right-left>=region.Width*.22) return "【对方】";
        if(right<=region.Width*.20 && left-right>=region.Width*.22) return "【我】";
        return "【身份不明】";
    }
    public static bool Timeline(string text,Rectangle box,Rectangle region) {
        double center=(box.Left+box.Width/2.0-region.Left)/region.Width;
        return center>.32 && center<.68 && Regex.IsMatch(Regex.Replace(text,@"\s+",""),@"^(?:(?:昨天|今天|前天|星期[一二三四五六日天]|周[一二三四五六日天]|\d{1,4}[年/.-]\d{1,2}(?:[月/.-]\d{1,2}日?)?)?(?:上午|下午|晚上|凌晨)?\d{1,2}[:：]\d{2}|昨天|今天|以下为新消息)$");
    }
    public static string Read(OcrLayout layout,Rectangle region,Bitmap bitmap) {
        region=Rectangle.Intersect(region,new Rectangle(Point.Empty,bitmap.Size)); if(region.Width<30||region.Height<30) return "";
        var parts=Parts(bitmap,region);
        var avatars=parts.Where(p=>p.Box.Width>=22 && p.Box.Width<=Math.Min(85,region.Width*.14) && p.Box.Height>=22 && p.Box.Height<=85 && p.Box.Width/(double)p.Box.Height>.70 && p.Box.Width/(double)p.Box.Height<1.4 && (p.Box.Right<region.Left+region.Width*.15 || p.Box.Left>region.Right-region.Width*.15)).OrderBy(p=>p.Box.Top).ToList();
        var rows=new List<Row>(); var used=new HashSet<Part>();
        foreach(var avatar in avatars) {
            bool own=avatar.Box.Left>region.Left+region.Width/2;
            var candidates=parts.Where(p=>p!=avatar && !avatars.Contains(p) && p.Box.Width>=18 && p.Box.Height>=15 && Math.Abs(p.Box.Top-avatar.Box.Top)<Math.Max(24,avatar.Box.Height*.8) && (own ? p.Box.Right<=avatar.Box.Left+3 && avatar.Box.Left-p.Box.Right<55 : p.Box.Left>=avatar.Box.Right-3 && p.Box.Left-avatar.Box.Right<55)).ToList();
            if(candidates.Count==0) continue;
            var part=candidates.OrderByDescending(p=>p.Pixels).First(); if(used.Contains(part)) continue; used.Add(part);
            rows.Add(new Row {Top=part.Box.Top,Body=part.Box,Role=own?"【我】":"【对方】"});
        }
        foreach(var line in layout.Lines.OrderBy(l=>l.Bounds.Top).ThenBy(l=>l.Bounds.Left)) {
            var box=line.Bounds; string text=(line.Text??"").Trim(); var center=new Point(box.Left+box.Width/2,box.Top+box.Height/2);
            if(text.Length==0 || !region.Contains(center)) continue;
            var row=rows.FirstOrDefault(r=> {var area=r.Body;area.Inflate(6,6);return area.Contains(center);});
            if(row==null && Timeline(text,box,region)) continue;
            if(row==null && avatars.Any(a=>a.Box.Contains(center))) continue;
            if(row==null) {
                var part=parts.Where(p=>p.Box.Contains(center)&&p.Box.Width>=box.Width*.8).OrderBy(p=>p.Box.Width*p.Box.Height).FirstOrDefault();
                var body=part==null?box:part.Box;
                row=rows.FirstOrDefault(r=>r.Body==body);
                if(row==null) {row=new Row {Top=body.Top,Body=body,Role=part==null?"【身份不明】":Side(body,region)};rows.Add(row);}
            }
            // Keep sender labels inside screenshots or quoted text as content, not protocol markers.
            row.Text.Add(text.Replace("【我】","〔原文：我〕").Replace("【对方】","〔原文：对方〕"));
        }
        return String.Join("\n",rows.OrderBy(r=>r.Top).ThenBy(r=>r.Body.Left).Select(r=>r.Role+(r.Text.Count==0?"[非文字消息，内容未识别]":String.Join(" / ",r.Text))));
    }
}
}
