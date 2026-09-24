using System;
using System.Drawing;
using System.Windows.Forms;
namespace JevChat {
public static class FollowLayout {
    public static Rectangle Place(Rectangle target,Rectangle screen,Size desired) {
        int width=Math.Min(desired.Width,screen.Width);
        int height=Math.Min(Math.Max(620,target.Height),screen.Height);
        int x=target.Right+8;
        if(x+width>screen.Right) x=target.Left-width-8;
        // A maximized chat has no outside space: dock inside its right edge like a companion pane.
        if(x<screen.Left) x=target.Right-width-12;
        x=Math.Max(screen.Left,Math.Min(x,screen.Right-width));
        int y=Math.Max(screen.Top,Math.Min(target.Top,screen.Bottom-height));
        return new Rectangle(x,y,width,height);
    }
}
public class SideButton : Button {
    public bool Accent;
    public SideButton() { FlatStyle=FlatStyle.Flat; FlatAppearance.BorderSize=0; Cursor=Cursors.Hand; Height=32; BackColor=Palette.Hover; ForeColor=Palette.Text; DoubleBuffered=true; }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent.BackColor);
        using(var path=Palette.Round(new Rectangle(0,0,Math.Max(20,Width-1),Math.Max(20,Height-1)),7))
        using(var fill=new SolidBrush(Accent ? Color.FromArgb(40,100,78) : BackColor)) e.Graphics.FillPath(fill,path);
        TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Enabled ? (Accent ? Color.White : ForeColor) : Color.Gray,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        if(Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(3,3,Width-6,Height-6));
    }
}
}
