using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace DropShift {
 static class Theme {
  public static readonly Color Ink=Color.FromArgb(65,43,33), Muted=Color.FromArgb(132,103,83), Paper=Color.FromArgb(255,249,238), Accent=Color.FromArgb(219,104,56);
  public static GraphicsPath Round(RectangleF r,float radius) { var p=new GraphicsPath(); float d=radius*2; p.AddArc(r.X,r.Y,d,d,180,90); p.AddArc(r.Right-d,r.Y,d,d,270,90); p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); p.AddArc(r.X,r.Bottom-d,d,d,90,90); p.CloseFigure(); return p; }
  public static void Text(Graphics g,string text,RectangleF rect,float size,Color color,FontStyle style) { using(var f=new Font("Segoe UI",size,style)) using(var b=new SolidBrush(color)) using(var sf=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter}) g.DrawString(text,f,b,rect,sf); }
  public static GraphicsPath Slice(int index,int count) { float sweep=360f/count,start=-90-sweep/2+index*sweep+2.5f; var p=new GraphicsPath(); p.AddArc(14,14,332,332,start,sweep-5); p.AddArc(113,113,134,134,start+sweep-5,-sweep+5); p.CloseFigure(); return p; }
  public static int Hit(Point p,int count) { double x=p.X-180,y=p.Y-180,r=x*x+y*y; if(count==0 || r<73*73 || r>166*166) return -1; double sweep=360.0/count,a=(Math.Atan2(y,x)*180/Math.PI+90+sweep/2+360)%360; double edge=a%sweep; return edge<2.5 || edge>sweep-2.5 ? -1 : (int)(a/sweep); }
  public static void Ring(Graphics g,string[] targets,int hot,string center,string detail) {
   g.SmoothingMode=SmoothingMode.AntiAlias;
   using(var brush=new LinearGradientBrush(new Rectangle(0,0,360,360),Color.FromArgb(248,220,170),Color.FromArgb(183,138,95),45)) g.FillEllipse(brush,3,3,354,354);
   using(var pen=new Pen(Color.FromArgb(255,239,201),2)) g.DrawEllipse(pen,6,6,348,348);
   int count=targets.Length;
   for(int i=0;i<count;i++) {
    using(var p=Slice(i,count)) using(var b=new LinearGradientBrush(new Rectangle(0,0,360,360),i==hot ? Color.FromArgb(255,187,115) : Color.FromArgb(255,239,201),i==hot ? Color.FromArgb(237,119,69) : Color.FromArgb(247,202,161),90)) { g.FillPath(b,p); using(var pen=new Pen(Color.FromArgb(180,145,105),1.5f)) { pen.LineJoin=LineJoin.Round; g.DrawPath(pen,p); } }
    double angle=(-90+i*360.0/count)*Math.PI/180; float x=180+(float)Math.Cos(angle)*119,y=180+(float)Math.Sin(angle)*119;
    Text(g,targets[i],new RectangleF(x-44,y-20,88,40),count>6?11:14,Ink,FontStyle.Bold);
   }
   using(var b=new LinearGradientBrush(new Rectangle(110,110,140,140),Color.FromArgb(255,220,160),Color.FromArgb(240,163,105),90)) g.FillEllipse(b,115,115,130,130);
   using(var pen=new Pen(Color.FromArgb(255,235,194),2)) g.DrawEllipse(pen,117,117,126,126);
   Text(g,center,new RectangleF(119,142,122,36),15,Ink,FontStyle.Bold);
   Text(g,detail,new RectangleF(120,177,120,39),8.5f,Ink,FontStyle.Regular);
  }
 }
 sealed class WarmButton:Button {
  public bool Primary;
  public WarmButton() { FlatStyle=FlatStyle.Flat; FlatAppearance.BorderSize=0; Cursor=Cursors.Hand; }
  protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias; using(var p=Theme.Round(new RectangleF(1,1,Width-2,Height-2),12)) using(var b=new SolidBrush(Primary?Theme.Accent:Color.FromArgb(242,225,204))) e.Graphics.FillPath(b,p); Theme.Text(e.Graphics,Text,new RectangleF(4,0,Width-8,Height),10,Primary?Color.White:Theme.Ink,FontStyle.Bold); if(Focused) ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(7,7,Width-14,Height-14)); }
 }
 sealed class WheelPreview:Control {
  public WheelPreview(){DoubleBuffered=true;Size=new Size(260,260);}
  protected override void OnPaint(PaintEventArgs e){ e.Graphics.ScaleTransform(Width/360f,Height/360f); Theme.Ring(e.Graphics,new[]{"PDF","JPG","WebP","PNG"},1,"DropShift","Drag. Shift. Convert."); }
 }
}
