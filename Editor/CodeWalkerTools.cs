using System.IO;
using ImageMagick;

namespace DuckStudio;

public static partial class CodeWalkerPreview
{
    public sealed record TextureInspection(int Textures, int LargestDimension, long PixelBytes, List<string> Names);
    private static void Set(object target, string property, object? value) => target.GetType().GetProperty(property)!.SetValue(target,value);
    private static object ReadYtd(string path,string coreDll)
    {
        EnsureCore(coreDll);
        var type=core!.GetType("CodeWalker.GameFiles.YtdFile",true)!;
        var file=Activator.CreateInstance(type)!;
        type.GetMethod("Load",[typeof(byte[])])!.Invoke(file,[File.ReadAllBytes(path)]);
        return file;
    }
    private static Array Textures(object ytd) => (Array)Member(Member(Member(ytd,"TextureDict")!,"Textures")!,"data_items")!;
    private static void WriteYtd(object ytd,string path)
    {
        var bytes=(byte[])ytd.GetType().GetMethod("Save",Type.EmptyTypes)!.Invoke(ytd,null)!;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { File.WriteAllBytes(temp,bytes);File.Move(temp,path,true); }
        finally { if(File.Exists(temp))File.Delete(temp); }
    }
    public static void RenameSingleTexture(string source,string destination,string name,string coreDll)
    {
        var file=ReadYtd(source,coreDll);var list=Textures(file);
        if(list.Length!=1) throw new InvalidDataException("Tattoo YTD must contain exactly one image. Import a PNG or PSD instead for dictionaries with multiple images.");
        var texture=list.GetValue(0)!;Set(texture,"Name",name);
        var hash=core!.GetTypes().First(t=>t.Name=="JenkHash").GetMethod("GenHash",[typeof(string)])!.Invoke(null,[name.ToLowerInvariant()]);Set(texture,"NameHash",hash);
        var typed=Activator.CreateInstance(typeof(List<>).MakeGenericType(texture.GetType()))!;
        typed.GetType().GetMethod("Add")!.Invoke(typed,[texture]);
        Member(file,"TextureDict")!.GetType().GetMethod("BuildFromTextureList")!.Invoke(Member(file,"TextureDict"),[typed]);
        WriteYtd(file,destination);
    }
    public static (int Textures,int LargestDimension,long PixelBytes) InspectTextures(string path,string coreDll)
    {
        var result = InspectTextureDictionary(path, coreDll);
        return (result.Textures, result.LargestDimension, result.PixelBytes);
    }
    public static TextureInspection InspectTextureDictionary(string path, string coreDll)
    {
        var file=ReadYtd(path,coreDll);var textures=Textures(file);int largest=0;long bytes=0;
        var names = new List<string>();
        foreach(var texture in textures)
        {
            if(texture==null)continue;
            names.Add(Member(texture, "Name")?.ToString() ?? "");
            largest=Math.Max(largest,Math.Max(Convert.ToInt32(Member(texture,"Width")),Convert.ToInt32(Member(texture,"Height"))));
            if(Member(texture,"Data") is { } data && Member(data,"FullData") is byte[] raw)bytes+=raw.Length;
        }
        return new(textures.Length,largest,bytes,names);
    }
    public static int OptimizeTextures(string source,string destination,string coreDll,int maxDimension,bool compress)
    {
        var file=ReadYtd(source,coreDll);
        var ddsType=core!.GetTypes().First(t=>t.Name=="DDSIO");int modified=0;
        foreach(var texture in Textures(file))
        {
            if(texture==null)continue;
            var usage=Member(texture,"Usage")?.ToString()??"";
            var format=Member(texture,"Format")?.ToString()??"";
            // Normal, specular and packed data maps remain byte-for-byte unchanged.
            if(usage.Contains("NORMAL") || usage.Contains("SPECULAR") || format.Contains("ATI") || format.Contains("BC7"))continue;
            var bytes=(byte[])ddsType.GetMethod("GetDDSFile")!.Invoke(null,[texture])!;
            using var image=new MagickImage(bytes,new MagickReadSettings{FrameIndex=0,FrameCount=1});
            var largest=Math.Max(image.Width,image.Height);
            if(largest>(uint)maxDimension) { var ratio=(double)maxDimension/largest;image.Resize(new MagickGeometry(Math.Max(1,(uint)(image.Width*ratio)),Math.Max(1,(uint)(image.Height*ratio))){IgnoreAspectRatio=true}); }
            image.Depth=8;
            image.Settings.SetDefine(MagickFormat.Dds,"compression",compress?"dxt5":"none");
            var mipCount=(int)Math.Floor(Math.Log2(Math.Max(image.Width,image.Height)));
            image.Settings.SetDefine(MagickFormat.Dds,"mipmaps",mipCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var decoded=ddsType.GetMethod("GetTexture",[typeof(byte[])])!.Invoke(null,[image.ToByteArray(MagickFormat.Dds)])!;
            foreach(var property in new[]{"Width","Height","Depth","Levels","Format","Stride","Data"})Set(texture,property,Member(decoded,property));
            modified++;
        }
        WriteYtd(file,destination);return modified;
    }
    public static void CreateRpfFromDirectory(string directory,string destination,string coreDll)
    {
        EnsureCore(coreDll);
        var type=core!.GetType("CodeWalker.GameFiles.RpfFile",true)!;
        var encryption=core.GetType("CodeWalker.GameFiles.RpfEncryption",true)!;
        var dirType=core.GetType("CodeWalker.GameFiles.RpfDirectoryEntry",true)!;
        var open=Enum.Parse(encryption,"OPEN");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        var rpf=type.GetMethod("CreateNew",[typeof(string),typeof(string),encryption])!.Invoke(null,[Path.GetDirectoryName(Path.GetFullPath(destination))!,Path.GetFileName(destination),open])!;
        void Fill(object parent,string source)
        {
            foreach(var folder in Directory.GetDirectories(source).Order(StringComparer.OrdinalIgnoreCase))
            {
                var name=Path.GetFileName(folder);
                if(name.EndsWith(".rpf",StringComparison.OrdinalIgnoreCase))
                {
                    var nested=type.GetMethod("CreateNew",[dirType,typeof(string),encryption])!.Invoke(null,[parent,name,open])!;
                    Fill(Member(nested,"Root")!,folder);
                }
                else Fill(type.GetMethod("CreateDirectory",[dirType,typeof(string)])!.Invoke(null,[parent,name])!,folder);
            }
            foreach(var file in Directory.GetFiles(source).Order(StringComparer.OrdinalIgnoreCase))
                type.GetMethod("CreateFile",[dirType,typeof(string),typeof(byte[]),typeof(bool)])!.Invoke(null,[parent,Path.GetFileName(file),File.ReadAllBytes(file),true]);
        }
        Fill(Member(rpf,"Root")!,directory);
    }
}
