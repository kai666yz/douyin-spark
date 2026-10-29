using System;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using DouyinSpark;
class MakeIcon
{
    static void Main(string[] args)
    {
        int[] sizes={16,24,32,48,64,128,256};var images=new List<byte[]>();
        foreach(int size in sizes)using(var image=new Bitmap(size,size)){using(var g=Graphics.FromImage(image)){g.Clear(Color.Transparent);SparkBrand.Paint(g,new RectangleF(0,0,size,size));}using(var stream=new MemoryStream()){image.Save(stream,ImageFormat.Png);images.Add(stream.ToArray());}}
        using(var writer=new BinaryWriter(File.Create(args[0]))){writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;for(int i=0;i<sizes.Length;i++){writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(images[i].Length);writer.Write(offset);offset+=images[i].Length;}foreach(var bytes in images)writer.Write(bytes);}
        using(var image=new Bitmap(512,512)){using(var g=Graphics.FromImage(image)){g.Clear(Color.Transparent);SparkBrand.Paint(g,new RectangleF(0,0,512,512));}image.Save(args[1],ImageFormat.Png);}
        Console.WriteLine("Generated 7-resolution Spark bubble icon.");
    }
}
