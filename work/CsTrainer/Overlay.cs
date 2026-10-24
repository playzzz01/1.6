using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
namespace CsTrainer;

internal sealed class Overlay : Form
{
    // Ordered bone list from the server: head, neck, torso, pelvis, left arm,
    // right arm, left leg, right leg. Each segment joins actual model bones.
    static readonly (int A,int B)[] SkeletonSegments = [(0,1),(1,2),(2,3),
        (1,4),(4,5),(5,6),(6,7),(1,8),(8,9),(9,10),(10,11),
        (3,12),(12,13),(13,14),(3,15),(15,16),(16,17)];
    readonly Settings settings;
    readonly System.Windows.Forms.Timer timer = new() { Interval=16 };
    Frame? frame; IntPtr game; long received;
    readonly Font font = new("Segoe UI", 10, FontStyle.Bold);
    internal Overlay(Settings settings)
    {
        this.settings=settings;
        FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;BackColor=Color.Magenta;TransparencyKey=Color.Magenta;
        Bounds=Rectangle.Empty; DoubleBuffered=true;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        timer.Tick += (_,_)=> {
            game=Game.FindWindow(null,"Counter-Strike");
            if(game==IntPtr.Zero){if(Visible)Hide();return;}
            if(!Visible)Show();
            if(game!=IntPtr.Zero && Game.GetClientRect(game,out var client))
            {
                var topLeft=new Game.PointI();
                if(Game.ClientToScreen(game,ref topLeft))
                {
                    var target=new Rectangle(topLeft.X,topLeft.Y,client.Right-client.Left,client.Bottom-client.Top);
                    if(target.Width>100 && target.Height>100 && target!=Bounds)Bounds=target;
                }
            }
            var next=Game.ReadFrame();
            if(next!=null && (frame==null || next.Timestamp!=frame.Timestamp))
            {
                frame=next;received=Stopwatch.GetTimestamp();
            }
            Invalidate();
        };
        timer.Start();
    }
    protected override CreateParams CreateParams
    {
        get { var cp=base.CreateParams;cp.ExStyle|=0x20|0x80|0x80000;return cp; }
    }
    protected override bool ShowWithoutActivation => true;
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if(game==IntPtr.Zero||frame==null)return;
        if(Stopwatch.GetElapsedTime(received).TotalSeconds>0.4)return;
        var foreground=Game.GetForegroundWindow();
        if(foreground!=game && !Game.IsMenuWindow(foreground))return;
        var current=frame;
        if(!Game.GetClientRect(game,out var rect))return;
        var point=new Game.PointI();if(!Game.ClientToScreen(game,ref point))return;
        double width=rect.Right-rect.Left,height=rect.Bottom-rect.Top;
        if(width<100||height<100)return;
        double centerX=point.X+width/2-Bounds.Left, centerY=point.Y+height/2-Bounds.Top;
        // GoldSrc reports horizontal FOV. The vertical focal length equals the horizontal one.
        double scale=width/(2*Math.Tan(current.Fov*Math.PI/360.0));
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        if(settings.Aim&&settings.Circle)using(var pen=new Pen(Color.FromArgb(220,255,230,60),1.5f)){float r=settings.AimRadius;e.Graphics.DrawEllipse(pen,(float)(centerX-r),(float)(centerY-r),2*r,2*r);}
        using var boxPen=new Pen(settings.BoxColor,settings.Width);
        using var backdrop=new SolidBrush(Color.FromArgb(145,0,0,0));
        using var textBrush=new SolidBrush(Color.White);
        foreach(var player in current.Players)
        {
            bool teammate=player.Team==current.Team;
            bool box=!teammate&&settings.Box;
            bool skeleton=!teammate&&settings.Skeleton;
            if(!box&&!skeleton)continue;
            if(skeleton && player.Bones.Count>=18)
            {
                var projected=new PointF?[18];
                for(int i=0;i<18;i++)projected[i]=Project(player.Bones[i],current,centerX,centerY,scale);
                using var line=new Pen(settings.SkeletonColor,settings.SkeletonWidth)
                { StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round };
                foreach(var (a,b) in SkeletonSegments)
                    if(projected[a] is PointF pa && projected[b] is PointF pb)
                        e.Graphics.DrawLine(line,pa,pb);
            }
            if(!box)continue;
            var points=new System.Collections.Generic.List<PointF>(8);
            bool clipped=false;
            foreach(double x in new[]{player.Mins.X,player.Maxs.X})
            foreach(double y in new[]{player.Mins.Y,player.Maxs.Y})
            foreach(double z in new[]{player.Mins.Z,player.Maxs.Z})
            {
                var xyz=new Vec3(player.Origin.X+x,player.Origin.Y+y,player.Origin.Z+z);
                var p=Project(xyz,current,centerX,centerY,scale);
                if(p==null){clipped=true;continue;}
                points.Add(p.Value);
            }
            if(clipped||points.Count!=8)continue;
            float x0=points.Min(p=>p.X),x1=points.Max(p=>p.X),y0=points.Min(p=>p.Y),y1=points.Max(p=>p.Y);
            if(player.Bones.Count>=12)
            {
                var bonePoints=new System.Collections.Generic.List<PointF>(player.Bones.Count);
                foreach(var bone in player.Bones)
                {
                    var projected=Project(bone,current,centerX,centerY,scale);
                    if(projected!=null)bonePoints.Add(projected.Value);
                }
                if(bonePoints.Count>=8)
                {
                    float bx0=bonePoints.Min(p=>p.X),bx1=bonePoints.Max(p=>p.X),by0=bonePoints.Min(p=>p.Y),by1=bonePoints.Max(p=>p.Y);
                    float marginX=Math.Clamp((bx1-bx0)*.12f,4,20),marginY=Math.Clamp((by1-by0)*.08f,5,20);
                    x0=Math.Min(x0,bx0-marginX);x1=Math.Max(x1,bx1+marginX);
                    y0=Math.Min(y0,by0-marginY);y1=Math.Max(y1,by1+marginY);
                }
            }
            if(x1-x0<3||y1-y0<3||x1-x0>3000||y1-y0>3000)continue;
            if(settings.Shadow)using(var shadow=new Pen(Color.FromArgb(220,0,0,0),settings.Width+3))e.Graphics.DrawRectangle(shadow,x0,y0,x1-x0,y1-y0);
            e.Graphics.DrawRectangle(boxPen,x0,y0,x1-x0,y1-y0);
            if(settings.Names)
            {
                string name=player.Name;var m=e.Graphics.MeasureString(name,font);
                e.Graphics.FillRectangle(backdrop,x0,y0-m.Height-2,m.Width,m.Height);
                e.Graphics.DrawString(name,font,textBrush,x0,y0-m.Height-2);
            }
            if(settings.AmmoBar>0)DrawAmmo(e.Graphics,player.Clip,player.Reserve,x0,y0,x1,y1);
            if(settings.AmmoText)
            {
                string ammo=$"{Math.Max(0,player.Clip)}/{Math.Max(0,player.Reserve)}";
                e.Graphics.FillRectangle(backdrop,x0,y1+2,Math.Max(60,ammo.Length*10),20);
                e.Graphics.DrawString(ammo,font,textBrush,x0,y1+2);
            }
            if(settings.Bar>0)DrawHealth(e.Graphics,player.Health,x0,y0,x1,y1);
        }
    }
    void DrawAmmo(Graphics g,int clip,int reserve,float x0,float y0,float x1,float y1)
    {
        float ratio=Math.Clamp(clip/Math.Max(1f,Math.Max(clip,30)),0,1);
        DrawMeter(g,settings.AmmoBar,ratio,Color.DodgerBlue,x0,y0,x1,y1,13);
    }
    internal static PointF? Project(Vec3 world,Frame frame,double centerX,double centerY,double scale)
    {
        double dx=world.X-frame.Eye.X,dy=world.Y-frame.Eye.Y,dz=world.Z-frame.Eye.Z;
        double yaw=frame.Yaw*Math.PI/180,pitch=frame.Pitch*Math.PI/180;
        double cy=Math.Cos(yaw),sy=Math.Sin(yaw),cp=Math.Cos(pitch),sp=Math.Sin(pitch);
        double forward=dx*cp*cy+dy*cp*sy-dz*sp;if(forward<5)return null;
        double right=dx*sy-dy*cy,up=dx*sp*cy+dy*sp*sy+dz*cp;
        return new PointF((float)(centerX+right*scale/forward),(float)(centerY-up*scale/forward));
    }
    void DrawHealth(Graphics g,int health,float x0,float y0,float x1,float y1)
    {
        float ratio=Math.Clamp(health/100f,0,1);
        DrawMeter(g,settings.Bar,ratio,Color.FromArgb((int)(255*(1-ratio)),(int)(255*ratio),45),x0,y0,x1,y1,0);
    }
    static void DrawMeter(Graphics g,int where,float ratio,Color color,float x0,float y0,float x1,float y1,float offset)
    {
        using var bg=new SolidBrush(Color.FromArgb(190,0,0,0));using var fill=new SolidBrush(color);
        float w=x1-x0,h=y1-y0;
        switch(where)
        {
            case 1:g.FillRectangle(bg,x0,y0-9-offset,w,6);g.FillRectangle(fill,x0,y0-9-offset,w*ratio,6);break;
            case 2:g.FillRectangle(bg,x0,y1+7+offset,w,6);g.FillRectangle(fill,x0,y1+7+offset,w*ratio,6);break;
            case 3:g.FillRectangle(bg,x1+6+offset,y0,6,h);g.FillRectangle(fill,x1+6+offset,y1-h*ratio,6,h*ratio);break;
            case 4:g.FillRectangle(bg,x0-12-offset,y0,6,h);g.FillRectangle(fill,x0-12-offset,y1-h*ratio,6,h*ratio);break;
        }
    }
}

