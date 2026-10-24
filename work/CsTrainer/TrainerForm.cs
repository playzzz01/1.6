using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace CsTrainer;

internal sealed class TrainerForm : Form
{
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    readonly Settings s;
    readonly Overlay overlay;
    readonly Panel content=new();
    readonly System.Windows.Forms.Timer hotkeyTimer=new(){Interval=40};
    readonly System.Windows.Forms.Timer widthTimer=new(){Interval=500};
    bool insertWasDown;
    bool gameSeen;
    readonly Font labelFont=new("Segoe UI",10);
    readonly Font titleFont=new("Segoe UI Semibold",14,FontStyle.Bold);
    int row;
    readonly Color surface=Color.FromArgb(18,20,27), card=Color.FromArgb(30,33,43), accent=Color.FromArgb(78,128,250);

    TrainerForm(Settings settings)
    {
        s=settings;overlay=new Overlay(s);
        Text="CS 1.6 单机修改器";FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.CenterScreen;
        Size=new Size(675,555);BackColor=surface;ForeColor=Color.White;Opacity=.94;TopMost=true;ShowInTaskbar=false;
        WindowState=FormWindowState.Normal;
        DoubleBuffered=true;SetStyle(ControlStyles.OptimizedDoubleBuffer,true);
        var header=new Panel{Dock=DockStyle.Top,Height=54,BackColor=Color.FromArgb(12,14,19)};Controls.Add(header);
        header.Controls.Add(new Label{Text="◉  CS 1.6   /   设置",ForeColor=Color.White,Font=new Font("Segoe UI Semibold",11,FontStyle.Bold),Location=new Point(20,16),AutoSize=true});
        header.Controls.Add(new Label{Text="INS 切换",ForeColor=Color.FromArgb(143,149,164),Font=labelFont,Location=new Point(553,18),AutoSize=true});
        var close=Button("×",Color.FromArgb(65,30,40),new Rectangle(638,10,26,30));close.Click+=(_,_)=>Hide();header.Controls.Add(close);
        header.MouseDown+=(_,e)=>{if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,2,0);}};
        var sidebar=new Panel{Dock=DockStyle.Left,Width=156,BackColor=Color.FromArgb(22,25,33)};Controls.Add(sidebar);
        content.Dock=DockStyle.Fill;content.AutoScroll=true;content.BackColor=surface;Controls.Add(content);content.BringToFront();
        sidebar.Controls.Add(Nav("游玩",0,()=>Gameplay()));
        sidebar.Controls.Add(Nav("视觉",1,()=>Visuals()));
        sidebar.Controls.Add(Nav("瞄准",2,()=>Aiming()));
        Visuals();
        Shown+=(_,_)=>{overlay.Show();Hide();};FormClosed+=(_,_)=>overlay.Close();
        hotkeyTimer.Tick+=(_,_)=>{
            bool running=Process.GetProcessesByName("hl").Length>0;
            if(running)gameSeen=true;
            if(gameSeen&&!running){Close();return;}
            bool down=(GetAsyncKeyState(0x2D)&0x8000)!=0;
            if(down&&!insertWasDown){if(Visible)Hide();else{Show();Activate();}}
            insertWasDown=down;
        };
        hotkeyTimer.Start();
        widthTimer.Tick+=(_,_)=>{if(s.Aim && Game.ClientWidth()!=s.LastClientWidth) Save();};
        widthTimer.Start();
        Resize+=(_,_)=>{if(Width<28||Height<28)return;using var path=Rounded(ClientRectangle,14);var previous=Region;Region=new Region(path);previous?.Dispose();};
        using(var path=Rounded(ClientRectangle,14))Region=new Region(path);
    }
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h,uint msg,IntPtr w,IntPtr l);
    static GraphicsPath Rounded(Rectangle r,int radius)
    {
        var p=new GraphicsPath();int d=radius*2;
        p.AddArc(r.Left,r.Top,d,d,180,90);p.AddArc(r.Right-d,r.Top,d,d,270,90);
        p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.Left,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
    }
    Button Nav(string name,int index,Action action)
    {
        var b=Button(name,card,new Rectangle(12,26+index*56,132,42));
        b.TextAlign=ContentAlignment.MiddleLeft;b.Padding=new Padding(12,0,0,0);b.Click+=(_,_)=>action();return b;
    }
    Button Button(string text,Color background,Rectangle bounds)
    {
        var b=new RoundedButton{Text=text,Bounds=bounds,BackColor=background,ForeColor=Color.White,FlatStyle=FlatStyle.Flat,Font=labelFont,Cursor=Cursors.Hand,TextAlign=ContentAlignment.MiddleCenter};
        b.FlatAppearance.BorderSize=0;return b;
    }
    void Begin(string title)
    {
        content.Controls.Clear();row=18;
        content.Controls.Add(new Label{Text=title,ForeColor=Color.White,Font=titleFont,Bounds=new Rectangle(22,row,470,34)});row+=48;
    }
    void Section(string title)
    {
        content.Controls.Add(new Label{Text=title,ForeColor=Color.FromArgb(135,155,200),Font=new Font("Segoe UI Semibold",10,FontStyle.Bold),Bounds=new Rectangle(22,row+8,460,24)});row+=39;
    }
    void Toggle(string name,bool value,Action<bool> changed)
    {
        content.Controls.Add(new Label{Text=name,ForeColor=Color.White,Font=labelFont,Bounds=new Rectangle(24,row+8,310,25)});
        var b=new SwitchControl{Checked=value,Bounds=new Rectangle(405,row+2,60,30),OnColor=accent};
        b.CheckedChanged+=(_,_)=>{changed(b.Checked);Save();};
        content.Controls.Add(b);row+=38;
    }
    void Number(string name,int value,int min,int max,Action<int> changed)
    {
        content.Controls.Add(new Label{Text=name,ForeColor=Color.White,Font=labelFont,Bounds=new Rectangle(24,row+8,310,25)});
        var n=new NumericUpDown{Minimum=min,Maximum=max,Value=Math.Clamp(value,min,max),Bounds=new Rectangle(372,row+3,96,28),BackColor=card,ForeColor=Color.White,BorderStyle=BorderStyle.None,Font=labelFont};
        n.ValueChanged+=(_,_)=>{changed((int)n.Value);Save();};content.Controls.Add(n);row+=38;
    }
    void Combo(string name,string[] choices,int initial,Action<int> changed)
    {
        content.Controls.Add(new Label{Text=name,ForeColor=Color.White,Font=labelFont,Bounds=new Rectangle(24,row+8,235,25)});
        var c=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Bounds=new Rectangle(276,row+2,192,29),BackColor=card,ForeColor=Color.White,FlatStyle=FlatStyle.Flat,Font=labelFont};
        c.Items.AddRange(choices);c.SelectedIndex=Math.Clamp(initial,0,choices.Length-1);
        c.SelectedIndexChanged+=(_,_)=>{changed(c.SelectedIndex);Save();};content.Controls.Add(c);row+=38;
    }
    void ColorChoice(string name,Color initial,Action<Color> changed)
    {
        content.Controls.Add(new Label{Text=name,ForeColor=Color.White,Font=labelFont,Bounds=new Rectangle(24,row+8,300,25)});
        var b=Button("",initial,new Rectangle(391,row+3,77,28));
        b.Click+=(_,_)=>{using var d=new ColorDialog{Color=b.BackColor,FullOpen=true};if(d.ShowDialog(this)==DialogResult.OK){changed(d.Color);b.BackColor=d.Color;Save();}};
        content.Controls.Add(b);row+=38;
    }
    void Gameplay()
    {
        Begin("游玩");Section("基础");
        Toggle("无限生命",s.Health,v=>s.Health=v);Toggle("无限弹药",s.Ammo,v=>s.Ammo=v);
        Number("回合时间",s.RoundMinutes,1,60,v=>s.RoundMinutes=v);
        var reset=Button("重置对局",card,new Rectangle(24,row+12,444,37));reset.Click+=(_,_)=>s.ResetRound();content.Controls.Add(reset);
    }
    void Visuals()
    {
        Begin("视觉");Section("人物发光");
        Toggle("敌方发光",s.EnemyGlow,v=>s.EnemyGlow=v);Toggle("己方发光",s.TeamGlow,v=>s.TeamGlow=v);
        ColorChoice("敌方颜色",s.EnemyGlowColor,v=>s.EnemyGlowColor=v);ColorChoice("己方颜色",s.TeamGlowColor,v=>s.TeamGlowColor=v);
        Number("发光亮度",s.GlowBrightness,1,100,v=>s.GlowBrightness=v);
        Section("方框透视");Toggle("方框",s.Box,v=>s.Box=v);Toggle("阴影",s.Shadow,v=>s.Shadow=v);
        Toggle("名字",s.Names,v=>s.Names=v);Toggle("弹药数字",s.AmmoText,v=>s.AmmoText=v);
        ColorChoice("线条颜色",s.BoxColor,v=>s.BoxColor=v);Number("线条粗细",s.Width,1,8,v=>s.Width=v);
        Combo("血量位置",["关闭","顶部","底部","右侧","左侧"],s.Bar,v=>s.Bar=v);
        Combo("弹药位置",["关闭","顶部","底部","右侧","左侧"],s.AmmoBar,v=>s.AmmoBar=v);
        Section("骨骼透视");Toggle("骨骼线条",s.Skeleton,v=>s.Skeleton=v);
        ColorChoice("骨骼颜色",s.SkeletonColor,v=>s.SkeletonColor=v);Number("骨骼粗细",s.SkeletonWidth,1,8,v=>s.SkeletonWidth=v);
    }
    void Aiming()
    {
        Begin("瞄准");Section("自动吸附");
        Toggle("自动吸附",s.Aim,v=>s.Aim=v);Toggle("隔墙吸附",s.AimThroughWall,v=>s.AimThroughWall=v);
        Number("吸附范围",s.AimRadius,20,600,v=>s.AimRadius=v);
        Combo("吸附部位",["头部","脖子","左手","右手","胸部","左腿","右腿","身体","随机"],s.AimPart,v=>s.AimPart=v);
        Number("吸附平滑度",s.Smooth,1,30,v=>s.Smooth=v);Toggle("显示范围圆圈",s.Circle,v=>s.Circle=v);
    }
    void Save(){try{s.Save();}catch(Exception ex){MessageBox.Show(this,ex.Message,"设置保存失败");}}
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Game.IsMenuWindow=h=>h==Handle;}
    internal static void Launch()=>Application.Run(new TrainerForm(Settings.Load()));
}
