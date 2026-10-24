using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Windows.Forms;

namespace CsTrainer;

internal static class Game
{
    internal const string Config = @"D:\CS\Counter Strike\cstrike\addons\amxmodx\configs";
    internal static readonly string Legacy = Path.Combine(Config, "offline_trainer.ctrl");
    internal static readonly string Advanced = Path.Combine(Config, "offline_trainer_v2.ctrl");
    internal static readonly string Telemetry = Path.Combine(Config, "offline_trainer.telemetry");
    internal static long sequence = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    [DllImport("user32.dll")] internal static extern IntPtr FindWindow(string? cls, string title);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    internal static Func<IntPtr,bool> IsMenuWindow = _=>false;
    [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr h, ref PointI p);
    [DllImport("user32.dll", EntryPoint="GetWindowLongW")] internal static extern int GetWindowLong(IntPtr h, int n);
    [DllImport("user32.dll", EntryPoint="SetWindowLongW")] internal static extern int SetWindowLong(IntPtr h, int n, int v);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct PointI { internal int X, Y; }
    internal static string[] ReadParts(string path) { try { return File.ReadAllText(path).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries); } catch { return []; } }
    internal static void Write(string path, string payload)
    {
        var old = ReadParts(path);
        if (old.Length > 0 && long.TryParse(old[0], out long previous)) sequence = Math.Max(sequence, previous);
        sequence = Math.Max(sequence + 1, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var temp=path+".tmp";
        File.WriteAllText(temp, $"{sequence} {payload}\n");
        File.Move(temp,path,true);
    }
    internal static int ClientWidth()
    {
        var window=FindWindow(null,"Counter-Strike");
        return window!=IntPtr.Zero && GetClientRect(window,out var rect) ? Math.Clamp(rect.Right-rect.Left,320,7680) : 800;
    }
    internal static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);
    internal static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);
    internal static Frame? ReadFrame()
    {
        try
        {
            var lines = File.ReadAllLines(Telemetry);
            if (lines.Length == 0) return null;
            var p = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 13 || p[0] != "2") return null;
            var frame = new Frame(new Vec3(D(p[1]), D(p[2]), D(p[3])), D(p[4]), D(p[5]), D(p[6]), I(p[12]), p.Length>=14?D(p[13]):0);
            for (int n=1; n<lines.Length; n++)
            {
                var e = lines[n].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (e.Length < 15) continue;
                var player=new Player(I(e[0]), new Vec3(D(e[1]),D(e[2]),D(e[3])), new Vec3(D(e[4]),D(e[5]),D(e[6])), new Vec3(D(e[7]),D(e[8]),D(e[9])), I(e[10]), I(e[11]), I(e[12]), I(e[13]), e[14].Replace('_',' '));
                if(e.Length>=69)for(int j=15;j+2<e.Length;j+=3)player.Bones.Add(new Vec3(D(e[j]),D(e[j+1]),D(e[j+2])));
                frame.Players.Add(player);
            }
            return frame;
        }
        catch { return null; }
    }
}

internal readonly record struct Vec3(double X, double Y, double Z);
internal sealed record Player(int Id, Vec3 Origin, Vec3 Mins, Vec3 Maxs, int Health, int Clip, int Reserve, int Team, string Name) { internal List<Vec3> Bones {get;}=[]; }
internal sealed record Frame(Vec3 Eye, double Pitch, double Yaw, double Fov, int Team, double Timestamp) { internal List<Player> Players {get;} = []; }

internal sealed class Settings
{
    internal bool Health=true, Ammo=true, EnemyGlow=false, TeamGlow=false, Box=true, Skeleton=false, Names=true, AmmoText=true, Aim=false, Circle=true, AimThroughWall=false, Shadow=true;
    internal int RoundMinutes=5, Width=2, SkeletonWidth=2, Bar=4, AmmoBar=2, AimRadius=120, AimPart=0, Smooth=7, GlowBrightness=25;
    internal Color BoxColor=Color.White, SkeletonColor=Color.LimeGreen, EnemyGlowColor=Color.OrangeRed, TeamGlowColor=Color.DodgerBlue;
    internal int LastClientWidth;
    internal static Settings Load()
    {
        var s=new Settings(); var old=Game.ReadParts(Game.Legacy);
        if(old.Length>=7) {s.Health=old[1]=="1"; s.Ammo=old[2]=="1";s.RoundMinutes=int.TryParse(old[5],out int t)?Math.Clamp(t,1,60):5;}
        var a=Game.ReadParts(Game.Advanced);
        if(a.Length>=7) {s.EnemyGlow=a[1]=="1";s.TeamGlow=a[2]=="1";s.Aim=a[3]=="1";s.AimRadius=int.TryParse(a[4],out int r)?Math.Clamp(r,20,600):120;s.AimPart=int.TryParse(a[5],out int p)?Math.Clamp(p,0,8):0;s.Smooth=int.TryParse(a[6],out int m)?Math.Clamp(m,1,30):7;}
        if(a.Length>=9) s.AimThroughWall=a[8]=="1";
        if(a.Length>=8 && int.TryParse(a[7],out int savedWidth)) s.LastClientWidth=Math.Clamp(savedWidth,320,7680);
        if(a.Length>=16) {try{s.EnemyGlowColor=Color.FromArgb(Game.I(a[9]),Game.I(a[10]),Game.I(a[11]));s.TeamGlowColor=Color.FromArgb(Game.I(a[12]),Game.I(a[13]),Game.I(a[14]));s.GlowBrightness=Math.Clamp(Game.I(a[15]),1,100);}catch{}}
        try
        {
            var f=Path.Combine(Game.Config,"trainer_visual.ini");
            foreach(var line in File.ReadAllLines(f))
            {
                var v=line.Split('=',2);if(v.Length<2)continue;
                switch(v[0]){case "box":s.Box=v[1]=="1";break;case "skeleton":s.Skeleton=v[1]=="1";break;case "skeletonWidth":s.SkeletonWidth=Math.Clamp(int.Parse(v[1]),1,8);break;case "skeletonColor":s.SkeletonColor=ColorTranslator.FromHtml(v[1]);break;case "names":s.Names=v[1]=="1";break;case "ammo":s.AmmoText=v[1]=="1";break;case "circle":s.Circle=v[1]=="1";break;case "width":s.Width=Math.Clamp(int.Parse(v[1]),1,8);break;case "bar":s.Bar=Math.Clamp(int.Parse(v[1]),0,4);break;case "ammoBar":s.AmmoBar=Math.Clamp(int.Parse(v[1]),0,4);break;case "shadow":s.Shadow=v[1]=="1";break;case "color":s.BoxColor=ColorTranslator.FromHtml(v[1]);break;}
            }
        } catch {}
        return s;
    }
    internal void Save()
    {
        Directory.CreateDirectory(Game.Config);
        Game.Write(Game.Legacy,$"{(Health?1:0)} {(Ammo?1:0)} 0 {(Aim?1:0)} {RoundMinutes} 0");
        LastClientWidth=Game.ClientWidth();
        Game.Write(Game.Advanced,$"{(EnemyGlow?1:0)} {(TeamGlow?1:0)} {(Aim?1:0)} {AimRadius} {AimPart} {Smooth} {LastClientWidth} {(AimThroughWall?1:0)} {EnemyGlowColor.R} {EnemyGlowColor.G} {EnemyGlowColor.B} {TeamGlowColor.R} {TeamGlowColor.G} {TeamGlowColor.B} {GlowBrightness}");
        var visualPath=Path.Combine(Game.Config,"trainer_visual.ini");
        var visualTemp=visualPath+".tmp";
        File.WriteAllLines(visualTemp, ["box="+(Box?1:0),"skeleton="+(Skeleton?1:0),"skeletonWidth="+SkeletonWidth,"skeletonColor="+ColorTranslator.ToHtml(SkeletonColor),"names="+(Names?1:0),"ammo="+(AmmoText?1:0),"circle="+(Circle?1:0),"width="+Width,"bar="+Bar,"ammoBar="+AmmoBar,"shadow="+(Shadow?1:0),"color="+ColorTranslator.ToHtml(BoxColor)]);
        File.Move(visualTemp,visualPath,true);
    }
    internal void ResetRound() { Game.Write(Game.Legacy,$"{(Health?1:0)} {(Ammo?1:0)} 0 {(Aim?1:0)} {RoundMinutes} 1"); }
}
