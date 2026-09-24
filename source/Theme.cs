using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace JevChat {
static class Palette {
    public static readonly Color Background=Color.FromArgb(24,25,28);
    public static readonly Color Sidebar=Color.FromArgb(18,19,22);
    public static readonly Color Surface=Color.FromArgb(32,34,38);
    public static readonly Color Hover=Color.FromArgb(43,46,51);
    public static readonly Color Border=Color.FromArgb(51,54,60);
    public static readonly Color Text=Color.FromArgb(237,237,240);
    public static readonly Color Muted=Color.FromArgb(160,166,176);
    public static GraphicsPath Round(Rectangle rect,int radius) {
        var path=new GraphicsPath(); int d=radius*2;
        path.AddArc(rect.Left,rect.Top,d,d,180,90); path.AddArc(rect.Right-d,rect.Top,d,d,270,90);
        path.AddArc(rect.Right-d,rect.Bottom-d,d,d,0,90); path.AddArc(rect.Left,rect.Bottom-d,d,d,90,90); path.CloseFigure(); return path;
    }
}
public class SurfacePanel : Panel {
    public SurfacePanel() { DoubleBuffered=true; SetStyle(ControlStyles.ResizeRedraw,true); }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var path=Palette.Round(new Rectangle(0,0,Math.Max(20,Width-1),Math.Max(20,Height-1)),12))
        using(var pen=new Pen(Palette.Border)) e.Graphics.DrawPath(pen,path);
    }
}
public class ModernButton : Button {
    public bool Primary;
    public bool Selected;
    public string Subtitle;
    public string Glyph;
    bool hover;
    public ModernButton() {
        FlatStyle=FlatStyle.Flat; FlatAppearance.BorderSize=0; Cursor=Cursors.Hand;
        BackColor=Palette.Surface; ForeColor=Palette.Text; DoubleBuffered=true;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
    }
    protected override void OnMouseEnter(EventArgs e) { hover=true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover=false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.Clear(Parent==null ? Palette.Background : Parent.BackColor);
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        Color fill=Primary ? (hover ? Color.White : Palette.Text) : hover || Selected ? Palette.Hover : BackColor;
        Color foreground=Enabled ? Primary ? Palette.Background : Palette.Text : Palette.Muted;
        if(!Enabled) fill=Palette.Surface;
        using(var path=Palette.Round(new Rectangle(1,1,Math.Max(20,Width-3),Math.Max(20,Height-3)),9)) {
            using(var brush=new SolidBrush(fill)) e.Graphics.FillPath(brush,path);
            using(var pen=new Pen(Selected ? Color.FromArgb(150,150,160) : Primary ? fill : Palette.Border)) e.Graphics.DrawPath(pen,path);
        }
        if(!String.IsNullOrEmpty(Subtitle)) {
            var titleRect=new Rectangle(24,Height/2-30,Width-48,32);
            TextRenderer.DrawText(e.Graphics,Text,Font,titleRect,foreground,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);
            using(var small=new Font("Microsoft YaHei UI",10)) TextRenderer.DrawText(e.Graphics,Subtitle,small,new Rectangle(24,Height/2+7,Width-48,26),Palette.Muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            if(Selected) using(var small=new Font("Segoe UI",11)) TextRenderer.DrawText(e.Graphics,"✓",small,new Rectangle(Width-40,15,24,24),Palette.Text,TextFormatFlags.HorizontalCenter);
        } else {
            TextFormatFlags flags=TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis;
            flags|=TextAlign==ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
            int inset=Width<60 ? 2 : 12;
            TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(inset,0,Width-inset*2,Height),foreground,flags);
        }
        if(Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(6,6,Width-12,Height-12),foreground,fill);
    }
    public override Size GetPreferredSize(Size proposedSize) {
        var size=TextRenderer.MeasureText(Text,Font); return new Size(Math.Max(MinimumSize.Width,size.Width+32),Math.Max(MinimumSize.Height,38));
    }
}
public class ModernSelect : ModernButton {
    public readonly ArrayList Items=new ArrayList();
    int selected=-1;
    public event EventHandler SelectedIndexChanged;
    public int SelectedIndex { get { return selected; } set {
        if(value< -1 || value>=Items.Count) throw new ArgumentOutOfRangeException("value");
        if(selected==value) return; selected=value; Text=(value<0 ? "请选择" : Convert.ToString(Items[value]))+"   ▾";
        Invalidate(); if(SelectedIndexChanged!=null) SelectedIndexChanged(this,EventArgs.Empty);
    } }
    public object SelectedItem { get { return selected<0 ? null : Items[selected]; } }
    public ModernSelect() { Height=38; TextAlign=ContentAlignment.MiddleLeft; ContextMenuStrip=new OwnedPopup(this); }
    protected override void OnClick(EventArgs e) {
        var menu=ContextMenuStrip;
        if(menu.Visible) { menu.Close(); return; }
        while(menu.Items.Count>0) { var old=menu.Items[0]; menu.Items.RemoveAt(0); old.Dispose(); }
        menu.Font=Font;
        for(int i=0;i<Items.Count;i++) { int index=i; var item=new ToolStripMenuItem(Convert.ToString(Items[i])) { ForeColor=Palette.Text,Padding=new Padding(12,8,12,8) }; item.Click+=delegate { SelectedIndex=index; }; menu.Items.Add(item); }
        menu.Show(this,new Point(0,Height)); base.OnClick(e);
    }
    protected override bool IsInputKey(Keys keyData) { if(keyData==Keys.Up || keyData==Keys.Down) return true; return base.IsInputKey(keyData); }
    protected override void OnKeyDown(KeyEventArgs e) {
        if(e.KeyCode==Keys.Down && Items.Count>0) { SelectedIndex=Math.Min(Items.Count-1,SelectedIndex+1); e.Handled=true; }
        else if(e.KeyCode==Keys.Up && Items.Count>0) { SelectedIndex=Math.Max(0,SelectedIndex-1); e.Handled=true; }
        base.OnKeyDown(e);
    }
}
// Closing is part of WinForms' reparent operation; release only with the owner.
public class OwnedPopup : ContextMenuStrip {
    public OwnedPopup(Control owner) {
        BackColor=Palette.Surface; ForeColor=Palette.Text; ShowImageMargin=false;
        Font=owner.Font; Renderer=new DarkMenuRenderer();
        owner.Disposed+=delegate { Dispose(); };
    }
}
class DarkMenuRenderer : ToolStripProfessionalRenderer {
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
        using(var brush=new SolidBrush(e.Item.Selected ? Palette.Hover : Palette.Surface)) e.Graphics.FillRectangle(brush,new Rectangle(Point.Empty,e.Item.Size));
    }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { using(var pen=new Pen(Palette.Border)) e.Graphics.DrawRectangle(pen,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1); }
}
public class PageHost : Panel {
    readonly List<Panel> pages=new List<Panel>(); int selected;
    public event EventHandler SelectedIndexChanged;
    public int SelectedIndex { get { return selected; } set {
        if(value<0 || value>=pages.Count) return; selected=value;
        for(int i=0;i<pages.Count;i++) pages[i].Visible=i==selected;
        pages[selected].BringToFront(); if(SelectedIndexChanged!=null) SelectedIndexChanged(this,EventArgs.Empty);
    } }
    public void Add(Panel page) { page.Dock=DockStyle.Fill; pages.Add(page); Controls.Add(page); page.Visible=pages.Count==1; }
}
public class PaddedTextBox : TextBox {
    [StructLayout(LayoutKind.Sequential)] struct TextRect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wparam,ref TextRect rect);
    void Inset() { if(!IsHandleCreated || !Multiline) return; var rect=new TextRect { Left=12,Top=10,Right=Math.Max(20,ClientSize.Width-12),Bottom=Math.Max(20,ClientSize.Height-10) }; SendMessage(Handle,0xB3,IntPtr.Zero,ref rect); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Inset(); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); Inset(); }
}
}
