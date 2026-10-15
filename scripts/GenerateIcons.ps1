$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
public static class CrtIconGenerator
{
    static GraphicsPath Round(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath();
        p.AddArc(x,y,r,r,180,90); p.AddArc(x+w-r,y,r,r,270,90);
        p.AddArc(x+w-r,y+h-r,r,r,0,90); p.AddArc(x,y+h-r,r,r,90,90); p.CloseFigure();
        return p;
    }
    static byte[] Frame(int size, bool paused)
    {
        using (var bmp = new Bitmap(size,size,PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent); g.ScaleTransform(size/256f,size/256f);
            using (var body = Round(12,24,232,178,38))
            using (var b = new SolidBrush(Color.FromArgb(28,39,56))) g.FillPath(b,body);
            using (var rim = Round(27,38,202,145,30))
            using (var b = new SolidBrush(paused ? Color.FromArgb(160,171,185) : Color.FromArgb(98,245,192))) g.FillPath(b,rim);
            using (var tube = Round(38,49,180,123,23))
            using (var b = new SolidBrush(Color.FromArgb(10,26,36))) g.FillPath(b,tube);
            for (int y=64; y<=158; y+=16)
                using (var p = new Pen(Color.FromArgb(55,paused ? Color.Gray : Color.Aquamarine),4)) g.DrawLine(p,49,y,207,y);
            using (var p = new Pen(paused ? Color.FromArgb(210,218,226) : Color.FromArgb(111,255,197),9))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                if (paused) { g.DrawLine(p,111,82,111,139); g.DrawLine(p,145,82,145,139); }
                else g.DrawLines(p,new PointF[]{new PointF(55,125),new PointF(87,125),new PointF(107,87),new PointF(133,145),new PointF(157,108),new PointF(202,108)});
            }
            using (var b = new SolidBrush(Color.FromArgb(28,39,56))) { g.FillRectangle(b,108,199,40,20); using (var foot=Round(76,216,104,16,12)) g.FillPath(b,foot); }
            using (var b = new SolidBrush(paused ? Color.Gray : Color.FromArgb(98,245,192))) g.FillEllipse(b,204,187,7,7);
            using (var ms = new MemoryStream()) { bmp.Save(ms,ImageFormat.Png); return ms.ToArray(); }
        }
    }
    public static void Write(string path, bool paused)
    {
        int[] sizes = {16,20,24,32,40,48,64,128,256};
        byte[][] frames = new byte[sizes.Length][];
        for(int i=0;i<sizes.Length;i++) frames[i]=Frame(sizes[i],paused);
        using(var stream=File.Create(path)) using(var w=new BinaryWriter(stream))
        {
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
            int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++)
            {
                w.Write((byte)(sizes[i]==256?0:sizes[i])); w.Write((byte)(sizes[i]==256?0:sizes[i]));
                w.Write((byte)0); w.Write((byte)0); w.Write((ushort)1); w.Write((ushort)32);
                w.Write(frames[i].Length); w.Write(offset); offset+=frames[i].Length;
            }
            foreach(var data in frames) w.Write(data);
        }
    }
}
'@
$assets = Join-Path (Split-Path $PSScriptRoot -Parent) 'Assets'
[IO.Directory]::CreateDirectory($assets) | Out-Null
[CrtIconGenerator]::Write((Join-Path $assets 'crt.ico'), $false)
[CrtIconGenerator]::Write((Join-Path $assets 'crt-paused.ico'), $true)
