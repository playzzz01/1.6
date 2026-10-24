using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace CsTrainer;

internal sealed class RoundedButton : Button
{
    public RoundedButton() => SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using var path=Round(new Rectangle(0,0,Width-1,Height-1),9);
        using var fill=new SolidBrush(BackColor);
        e.Graphics.FillPath(fill,path);
        var alignment=TextAlign switch
        {
            ContentAlignment.MiddleLeft or ContentAlignment.TopLeft or ContentAlignment.BottomLeft => TextFormatFlags.Left,
            ContentAlignment.MiddleRight or ContentAlignment.TopRight or ContentAlignment.BottomRight => TextFormatFlags.Right,
            _ => TextFormatFlags.HorizontalCenter
        };
        var textArea=new Rectangle(Padding.Left,0,Math.Max(0,Width-Padding.Left-Padding.Right),Height);
        TextRenderer.DrawText(e.Graphics,Text,Font,textArea,ForeColor,
            alignment|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
    }
    internal static GraphicsPath Round(Rectangle r,int radius)
    {
        var path=new GraphicsPath();int d=Math.Min(radius*2,Math.Min(r.Width,r.Height));
        path.AddArc(r.Left,r.Top,d,d,180,90);path.AddArc(r.Right-d,r.Top,d,d,270,90);
        path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.Left,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;
    }
}

internal sealed class SwitchControl : Control
{
    bool value;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Checked { get=>value; set { if(this.value==value)return;this.value=value;Invalidate();CheckedChanged?.Invoke(this,EventArgs.Empty); } }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color OnColor {get;set;}=Color.FromArgb(78,128,250);
    public event EventHandler? CheckedChanged;
    public SwitchControl() { Cursor=Cursors.Hand;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true); }
    protected override void OnClick(EventArgs e){base.OnClick(e);Checked=!Checked;}
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using var track=RoundedButton.Round(new Rectangle(1,1,Width-2,Height-2),Height/2);
        using var bg=new SolidBrush(Checked?OnColor:Color.FromArgb(74,79,92));
        e.Graphics.FillPath(bg,track);
        int diameter=Height-8,x=Checked?Width-diameter-4:4;
        using var knob=new SolidBrush(Color.White);
        e.Graphics.FillEllipse(knob,x,4,diameter,diameter);
    }
}
