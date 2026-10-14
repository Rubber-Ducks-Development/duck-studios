using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using N=System.Numerics;

namespace DuckStudio;

public sealed class StudioScene : IDisposable
{
    public NativePreviewResult Result { get; }
    public string Folder { get; }
    public StudioScene(NativePreviewResult result,string folder){Result=result;Folder=folder;}
    public void Dispose(){try{Directory.Delete(Folder,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
}
public static class StudioSceneService
{
    private static readonly SemaphoreSlim indexGate=new(1,1);
    public static string DataRoot=>Environment.GetEnvironmentVariable("WEFT_STUDIO_DATA_HOME")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WeftStudio");
    public static async Task RunAsync(string rage,string home,IEnumerable<string> args,CancellationToken token)
    {
        var start=new ProcessStartInfo(rage){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};start.Environment["HOME"]=home;start.ArgumentList.Add("--no-update-check");foreach(var arg in args)start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new IOException("Could not start the renderer.");var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        try{await process.WaitForExitAsync(token);}catch(OperationCanceledException){try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}await process.WaitForExitAsync();throw;}
        var log=await stdout+await stderr;if(process.ExitCode!=0)throw new IOException(log);
    }
    public static async Task EnsureIndexAsync(string rage,string home,string game,CancellationToken token)
    {
        await indexGate.WaitAsync(token);
        try
        {
            Directory.CreateDirectory(home);var exe=Path.Combine(game,"GTA5.exe");var stamp=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(rage)))+"|"+Path.GetFullPath(exe)+"|"+File.GetLastWriteTimeUtc(exe).Ticks;var file=Path.Combine(home,"renderer-index.txt");
            if(File.Exists(file)&&File.ReadAllText(file)==stamp)return;
            await RunAsync(rage,home,["--exe",exe,"index","build"],token);File.WriteAllText(file,stamp);
        }
        finally{indexGate.Release();}
    }
    public static async Task<StudioScene> LoadOutfitAsync(ClothingProject project,OutfitPreset outfit,string game,string core,CancellationToken token)
    {
        if(!File.Exists(Path.Combine(game,"GTA5.exe")))throw new FileNotFoundException("Link your GTA V installation in Settings to preview outfits.");
        var rage=Path.Combine(AppContext.BaseDirectory,"rage.exe");var home=DataRoot;var folder=Path.Combine(home,"scenes",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var args=new List<string>{"--exe",Path.Combine(game,"GTA5.exe"),"ped-scene","--ped",outfit.Ped,"-o",folder};var overrides=new List<object>();
            foreach(var entry in outfit.Entries.Where(x=>!x.Slot.StartsWith("p_")))
            {
                if(entry.Hidden){args.AddRange(["--component",entry.Slot+"=none"]);continue;}
                if(string.IsNullOrEmpty(entry.AssetId)){args.AddRange(["--component",$"{entry.Slot}={entry.BaseDrawable}:{entry.Texture}"]);continue;}
                var asset=project.Items.FirstOrDefault(x=>x.Id==entry.AssetId)??throw new InvalidDataException("Outfit item was removed: "+entry.Slot);
                if(asset.Ped!=outfit.Ped||asset.Slot!=entry.Slot)throw new InvalidDataException("Outfit item does not match its gender or slot: "+asset.Name);
                overrides.Add(new{slot=entry.Slot,model=asset.ModelPath,texture=asset.TexturePaths.ElementAtOrDefault(entry.Texture)});
            }
            var spec=Path.Combine(folder,"outfit.json");File.WriteAllText(spec,JsonSerializer.Serialize(overrides));args.AddRange(["--outfit",spec]);
            await EnsureIndexAsync(rage,home,game,token);await RunAsync(rage,home,args,token);
            token.ThrowIfCancellationRequested();var result=PedScenePreview.Load(folder);var group=(Model3DGroup)result.Model;
            foreach(var entry in outfit.Entries.Where(x=>x.Slot.StartsWith("p_")&&!x.Hidden&&!string.IsNullOrEmpty(x.AssetId)))
            {
                var asset=project.Items.FirstOrDefault(x=>x.Id==entry.AssetId)??throw new InvalidDataException("Outfit prop was removed.");
                var propFolder=Path.Combine(folder,entry.Slot);Directory.CreateDirectory(propFolder);var propArgs=new List<string>{"asset-scene","--ydd",asset.ModelPath,"-o",propFolder};if(asset.TexturePaths.ElementAtOrDefault(entry.Texture) is {} texture)propArgs.AddRange(["--ytd",texture]);
                await RunAsync(rage,home,propArgs,token);token.ThrowIfCancellationRequested();var prop=PedScenePreview.Load(propFolder).Model;
                var skeleton=PreviewPoses.Geometry(group).Select(g=>g.GetValue(PreviewPoses.SkinProperty) as SkinMesh).FirstOrDefault(s=>s?.Bones.Length>0)?.Bones??[];
                var anchor=entry.Slot switch{"p_lwrist"=>"SKEL_L_Hand","p_rwrist"=>"SKEL_R_Hand",_=>"SKEL_Head"};
                var bone=skeleton.FirstOrDefault(x=>x.Name.Equals(anchor,StringComparison.OrdinalIgnoreCase));
                if(bone!=null)
                {
                    var bind=PreviewPoses.BindTransforms(skeleton)[bone.Index];
                    foreach(var geometry in PreviewPoses.Geometry(prop))
                    {
                        if(geometry.Geometry is not MeshGeometry3D mesh)continue;
                        // Streamed freemode props are rigid meshes in the anchor's local
                        // coordinates. Place them in the PED bind pose, then skin rigidly.
                        mesh.Positions=new Point3DCollection(mesh.Positions.Select(p=>{var v=N.Vector3.Transform(new((float)p.X,(float)-p.Z,(float)p.Y),bind);return new Point3D(v.X,v.Z,-v.Y);}));
                        mesh.Normals=new Vector3DCollection(mesh.Normals.Select(n=>{var v=N.Vector3.TransformNormal(new((float)n.X,(float)-n.Z,(float)n.Y),bind);return new Vector3D(v.X,v.Z,-v.Y);}));
                        var count=mesh.Positions.Count;var indices=new int[count*4];var weights=new float[count*4];for(var i=0;i<count;i++){indices[i*4]=bone.Index;weights[i*4]=1;}
                        geometry.SetValue(PreviewPoses.SkinProperty,new SkinMesh(mesh,skeleton,[],indices,weights));geometry.SetValue(PedScenePreview.SourceSlotProperty,entry.Slot);
                    }
                }
                group.Children.Add(prop);
            }
            return new(result,folder);
        }
        catch{try{Directory.Delete(folder,true);}catch(IOException){}throw;}
    }
}
