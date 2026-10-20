using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DuckStudio;

static class ClothingExportChecks
{
    public static void Run(string root,Action<bool,string> check)
    {
        var model=Environment.GetEnvironmentVariable("DUCK_STUDIO_QA_MODEL");
        var original=Environment.GetEnvironmentVariable("DUCK_STUDIO_QA_YTD");
        if(model==null||original==null)return;
        var core=Path.Combine(AppContext.BaseDirectory,"CodeWalker.Core.dll");
        var folder=Directory.CreateDirectory(Path.Combine(root,"ClothingExport")).FullName;
        var dct=Path.Combine(folder,"export-case.dctproj");File.WriteAllText(dct,"Adjacent resource supplies inputs");
        var resource=Directory.CreateDirectory(Path.Combine(folder,"clothproject_export-case","built")).FullName;
        File.WriteAllText(Path.Combine(resource,"fxmanifest.lua"),"fx_version 'cerulean'\ngame 'gta5'");
        var stream=Directory.CreateDirectory(Path.Combine(resource,"stream")).FullName;
        File.Copy(model,Path.Combine(stream,"mp_m_freemode_01_mp_m_export_case^task_037_u.ydd"));
        for(var i=0;i<26;i++)File.Copy(original,Path.Combine(stream,$"mp_m_freemode_01_mp_m_export_case^task_diff_037_{(char)('a'+i)}_uni.ytd"));
        var project=ResourceImporter.ImportDurtyProject(dct);project.Name="Export case";project.Collection="export_case";project.OutputFolder=folder;project.CodeWalkerCorePath=core;
        var asset=project.Items.Single();
        var edited=new[]{1,7,24,25}; // B, H, Y and Z, including the last allowed variation.
        check(asset.TexturePaths.Count==26&&!PackBuilder.ValidateAsset(asset,core).Any(),"All 26 imported A-Z variations are accepted");
        asset.TexturePaths.Add(original);
        check(PackBuilder.ValidateAsset(asset,core).Any(x=>x.Contains("more than 26")),"A 27th variation is rejected without changing the supported maximum");
        asset.TexturePaths.RemoveAt(26);
        foreach(var i in edited)PutDiffuseFirst(asset.TexturePaths[i],core);
        var originalHashes=asset.TexturePaths.Select(File.ReadAllBytes).ToArray();
        var window=new MainWindow();const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        typeof(MainWindow).GetField("project",flags)!.SetValue(window,project);
        typeof(MainWindow).GetField("selected",flags)!.SetValue(window,asset);
        var colors=new[]{Color.FromRgb(230,15,20),Color.FromRgb(15,225,25),Color.FromRgb(25,20,230),Color.FromRgb(230,200,15)};
        for(int i=0;i<4;i++)
        {
            var image=Path.Combine(folder,$"edit-{i}.png");Write(image,colors[i]);
            var task=(Task<int>)typeof(MainWindow).GetMethod("ImportTexturesAsync",flags)!.Invoke(window,[asset,new[]{image},edited[i]])!;
            typeof(RealPreviewChecks).GetMethod("Pump",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[task]);check(task.Result==edited[i],"DCT variation replacement selects edited row "+(edited[i]));
        }
        typeof(MainWindow).GetMethod("WindowClosed",flags)!.Invoke(window,[window,EventArgs.Empty]);
        foreach(var i in edited)check(CodeWalkerPreview.TextureNames(asset.TexturePaths[i],core).First()==CodeWalkerPreview.TextureNames(original,core).First(),"Editing keeps the intended diffuse first for variation "+i);
        foreach(var i in edited)check(Keys(asset.TexturePaths[i],core).SequenceEqual(Keys(Path.Combine(stream,$"mp_m_freemode_01_mp_m_export_case^task_diff_037_{(char)('a'+i)}_uni.ytd"),core)),"Editing preserves imported dictionary lookup aliases "+i);
        var saved=Path.Combine(folder,"saved.duckproj");ProjectIO.Save(project,saved);project=ProjectIO.Load(saved);project.CodeWalkerCorePath=core;project.OutputFolder=folder;
        // Simulate a cache from an earlier importer with an auxiliary entry first.
        foreach(var i in edited)Rotate(project.Items.Single().TexturePaths[i],core);
        var output=PackBuilder.Build(project,folder);var built=Path.Combine(output,"FiveM","export_case");
        var imported=ResourceImporter.Import(built);var exported=imported.Items.Single();
        check(exported.TexturePaths.Count==26,"FiveM export retains all 26 variations after four replacements");
        foreach(var i in Enumerable.Range(0,26).Except(edited))check(File.ReadAllBytes(exported.TexturePaths[i]).SequenceEqual(originalHashes[i]),"Untouched Durty variation remains byte-for-byte unchanged in stream "+(char)('a'+i));
        for(int i=0;i<4;i++)
        {
            var color=Pixel(CodeWalkerPreview.LoadTextureFile(exported.TexturePaths[edited[i]],core,modelPath:exported.ModelPath)!);
            check(color.R==colors[i].R&&color.G==colors[i].G&&color.B==colors[i].B,"Exported FiveM variation contains distinct edited pixels "+(edited[i]));
            var first=CodeWalkerPreview.TextureNames(exported.TexturePaths[edited[i]],core).First();
            var streamed=Pixel(CodeWalkerPreview.LoadTextureFile(exported.TexturePaths[edited[i]],core,textureName:first)!);
            check(streamed.R==colors[i].R&&streamed.G==colors[i].G&&streamed.B==colors[i].B,"First streamed YTD entry contains the edit, not the template/default pixels "+(edited[i]));
            check(Pixels(exported.TexturePaths[edited[i]],core,1).SequenceEqual(Pixels(Path.Combine(stream,$"mp_m_freemode_01_mp_m_export_case^task_diff_037_{(char)('a'+edited[i])}_uni.ytd"),core,1)),"Auxiliary map pixel bytes remain unchanged after edit and export "+(edited[i]));
            check(Keys(exported.TexturePaths[edited[i]],core).SequenceEqual(Keys(Path.Combine(stream,$"mp_m_freemode_01_mp_m_export_case^task_diff_037_{(char)('a'+edited[i])}_uni.ytd"),core)),"Build repairs entry order while retaining original lookup aliases "+(edited[i]));
            check(File.ReadAllBytes(Path.Combine(stream,$"mp_m_freemode_01_mp_m_export_case^task_diff_037_{(char)('a'+edited[i])}_uni.ytd")).SequenceEqual(originalHashes[edited[i]]),"Editing and export leave original DCT YTD untouched "+(edited[i]));
        }
        var assembly=Assembly.LoadFrom(core);var pedType=assembly.GetType("CodeWalker.GameFiles.PedFile",true)!;
        var rpfType=assembly.GetType("CodeWalker.GameFiles.RpfFile",true)!;
        var binary=Directory.GetFiles(built,"mp_m_freemode_01_mp_m_export_case.ymt",SearchOption.AllDirectories).Single();
        var ped=rpfType.GetMethod("GetResourceFile")!.MakeGenericMethod(pedType).Invoke(null,[File.ReadAllBytes(binary)])!;
        var variation=pedType.GetProperty("VariationInfo")!.GetValue(ped)!;
        var component=variation.GetType().GetMethod("GetComponentData")!.Invoke(variation,[9])!;
        var drawables=(Array)component.GetType().GetProperty("DrawblData3")!.GetValue(component)!;var drawable=drawables.GetValue(0)!;
        // CodeWalker leaves the first component's type at zero; supply the
        // known slot before asking it to resolve the decoded texture suffixes.
        drawable.GetType().GetProperty("ComponentType")!.SetValue(drawable,9);
        for(int i=0;i<26;i++)
        {
            var name=(string)drawable.GetType().GetMethod("GetTextureName")!.Invoke(drawable,[i])!;
            check(exported.TexturePaths.Any(p=>Path.GetFileNameWithoutExtension(p).EndsWith("^"+name)),"Binary YMT resolves actual exported variation filename "+i+" ("+name+"; actual "+Path.GetFileName(exported.TexturePaths[i])+")");
        }
    }
    static Color Pixel(BitmapSource bitmap){var b=new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0);var p=new byte[b.PixelWidth*b.PixelHeight*4];b.CopyPixels(p,b.PixelWidth*4,0);return Color.FromArgb(p[3],p[2],p[1],p[0]);}
    static void Write(string path,Color c){var p=Enumerable.Range(0,64*64).SelectMany(_=>new[]{c.B,c.G,c.R,(byte)255}).ToArray();var b=BitmapSource.Create(64,64,96,96,PixelFormats.Bgra32,null,p,256);var e=new PngBitmapEncoder();e.Frames.Add(BitmapFrame.Create(b));using var s=File.Create(path);e.Save(s);}
    static object Read(string path,string core)
    {
        var type=Assembly.LoadFrom(core).GetType("CodeWalker.GameFiles.YtdFile",true)!;
        var file=Activator.CreateInstance(type)!;type.GetMethod("Load",[typeof(byte[])])!.Invoke(file,[File.ReadAllBytes(path)]);return file;
    }
    static object Get(object value,string name)=>value.GetType().GetProperty(name)!.GetValue(value)!;
    static byte[] Pixels(string path,string core,int index)
    {
        var entries=(Array)Get(Get(Get(Read(path,core),"TextureDict"),"Textures"),"data_items");return (byte[])Get(Get(entries.GetValue(index)!,"Data"),"FullData");
    }
    static uint[] Keys(string path,string core)=>(uint[])Get(Get(Get(Read(path,core),"TextureDict"),"TextureNameHashes"),"data_items");
    static void Rotate(string path,string core)
    {
        var file=Read(path,core);var dict=Get(file,"TextureDict");var entries=Get(dict,"Textures");var array=(Array)Get(entries,"data_items");
        var first=array.GetValue(0);array.SetValue(array.GetValue(1),0);array.SetValue(first,1);
        var keys=(uint[])Get(Get(dict,"TextureNameHashes"),"data_items");(keys[0],keys[1])=(keys[1],keys[0]);
        File.WriteAllBytes(path,(byte[])file.GetType().GetMethod("Save",Type.EmptyTypes)!.Invoke(file,null)!);
    }
    static void PutDiffuseFirst(string path,string core)
    {
        var a=Assembly.LoadFrom(core);var ytdType=a.GetType("CodeWalker.GameFiles.YtdFile",true)!;
        object Load(){var f=Activator.CreateInstance(ytdType)!;ytdType.GetMethod("Load",[typeof(byte[])])!.Invoke(f,[File.ReadAllBytes(path)]);return f;}
        var file=Load();var dictionary=ytdType.GetProperty("TextureDict")!.GetValue(file)!;
        var collection=dictionary.GetType().GetProperty("Textures")!.GetValue(dictionary)!;
        var entries=(Array)collection.GetType().GetProperty("data_items")!.GetValue(collection)!;
        var diffuse=entries.GetValue(0)!;var type=diffuse.GetType();var colorHash=(uint)type.GetProperty("NameHash")!.GetValue(diffuse)!;
        var normalFile=Load();var normalDict=ytdType.GetProperty("TextureDict")!.GetValue(normalFile)!;
        var normalCollection=normalDict.GetType().GetProperty("Textures")!.GetValue(normalDict)!;
        var normal=((Array)normalCollection.GetType().GetProperty("data_items")!.GetValue(normalCollection)!).GetValue(0)!;
        var hash=a.GetTypes().First(t=>t.Name=="JenkHash").GetMethod("GenHash",[typeof(string)])!;
        string name;uint normalHash;var i=0;
        do{name="export_fixture_"+(i++)+"_normal";normalHash=(uint)hash.Invoke(null,[name])!;}while(normalHash>=colorHash);
        type.GetProperty("Name")!.SetValue(normal,name);type.GetProperty("NameHash")!.SetValue(normal,normalHash);
        var usage=type.GetProperty("Usage")!;usage.SetValue(normal,Enum.Parse(usage.PropertyType,"NORMAL"));
        var ordered=Array.CreateInstance(type,2);ordered.SetValue(diffuse,0);ordered.SetValue(normal,1);
        collection.GetType().GetProperty("data_items")!.SetValue(collection,ordered);
        var hashes=dictionary.GetType().GetProperty("TextureNameHashes")!.GetValue(dictionary)!;
        // Imported lookup keys need not equal the image's display-name hash.
        hashes.GetType().GetProperty("data_items")!.SetValue(hashes,new[]{colorHash^0x80000000u,normalHash});
        File.WriteAllBytes(path,(byte[])ytdType.GetMethod("Save",Type.EmptyTypes)!.Invoke(file,null)!);
    }
}
