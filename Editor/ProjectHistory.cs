using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DuckStudio;

public sealed class ProjectRevision
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string Reason { get; set; } = "Checkpoint";
    public string ProjectName { get; set; } = "";
    public string SnapshotPath { get; set; } = "";
    public List<string> Changes { get; set; } = [];
    public Dictionary<string,string> State { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore] public bool CanRestore => File.Exists(SnapshotPath);
    public override string ToString() => CreatedUtc.ToLocalTime().ToString("MMM d, h:mm:ss tt") + "   " + Reason;
}

public sealed class ProjectHistory(string root)
{
    private readonly ProjectBackups store=new(root,100);
    private readonly SemaphoreSlim gate=new(1,1);
    public string Root => store.Root;
    private string Folder(string id)=>Path.Combine(Root,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..20]);
    public List<ProjectRevision> Read(string id)
    {
        var path=Path.Combine(Folder(id),"history.json");
        if(!File.Exists(path))return [];
        return JsonSerializer.Deserialize<List<ProjectRevision>>(File.ReadAllText(path),ProjectIO.Json)??[];
    }
    public async Task<ProjectRevision?> RecordAsync(ClothingProject snapshot,string reason)
    {
        if(string.IsNullOrWhiteSpace(snapshot.HistoryId))throw new InvalidOperationException("Project history identity is missing.");
        await gate.WaitAsync();
        try{return await Task.Run(()=>Record(snapshot,reason));}
        finally{gate.Release();}
    }
    private ProjectRevision? Record(ClothingProject snapshot,string reason)
    {
        var list=Read(snapshot.HistoryId);
        var state=Describe(snapshot);
        var snapshotPath=store.Capture(snapshot,snapshot.HistoryId);
        if(snapshotPath==null)
        {
            if(reason=="Automatic checkpoint")return null;
            snapshotPath=list.FirstOrDefault(r=>r.CanRestore)?.SnapshotPath;
        }
        if(snapshotPath==null)return null;
        var previous=list.FirstOrDefault()?.State??[];
        var revision=new ProjectRevision{Reason=reason,ProjectName=snapshot.Name,SnapshotPath=snapshotPath,State=state};
        foreach(var pair in state)
        {
            if(!previous.TryGetValue(pair.Key,out var old))revision.Changes.Add("Added " + pair.Key);
            else if(old!=pair.Value)revision.Changes.Add("Changed " + pair.Key);
        }
        foreach(var key in previous.Keys.Except(state.Keys))revision.Changes.Add("Removed " + key);
        if(revision.Changes.Count==0)revision.Changes.Add("Checkpoint of the current project and asset files.");
        list.Insert(0,revision);list=list.Take(300).ToList();
        var folder=Folder(snapshot.HistoryId);Directory.CreateDirectory(folder);
        var file=Path.Combine(folder,"history.json");var temp=file+".tmp";
        try{File.WriteAllText(temp,JsonSerializer.Serialize(list,ProjectIO.Json));File.Move(temp,file,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
        return revision;
    }
    private static Dictionary<string,string> Describe(ClothingProject project)
    {
        var state=new Dictionary<string,string>{{"project settings",JsonSerializer.Serialize(new{project.Name,project.Collection,project.Author,project.Version,project.Description,project.BuildTarget})}};
        foreach(var asset in project.Items)
        {
            var clone=JsonSerializer.Deserialize<ClothingAsset>(JsonSerializer.Serialize(asset,ProjectIO.Json),ProjectIO.Json)!;
            var pathStamps=asset.TexturePaths.Prepend(asset.ModelPath).Concat(asset.TextureSources.Values.Select(x=>x.SourcePath)).Select(p=>File.Exists(p)?p+"|"+File.GetLastWriteTimeUtc(p).Ticks+"|"+new FileInfo(p).Length:p+"|missing");
            state["drawable "+asset.Name+" ("+asset.Id[..Math.Min(6,asset.Id.Length)]+")"]=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(clone,ProjectIO.Json)+string.Join('|',pathStamps))));
        }
        state["outfit presets"]=JsonSerializer.Serialize(project.Outfits,ProjectIO.Json);
        foreach(var tattoo in project.Tattoos)state["tattoo "+tattoo.Name+" ("+tattoo.Id[..Math.Min(6,tattoo.Id.Length)]+")"]=JsonSerializer.Serialize(tattoo,ProjectIO.Json)+"|"+(File.Exists(tattoo.SourcePath)?File.GetLastWriteTimeUtc(tattoo.SourcePath).Ticks:0);
        return state;
    }
    public ClothingProject Restore(ProjectRevision revision)
    {
        if(!revision.CanRestore)throw new FileNotFoundException("This recovery point is no longer available.");
        var project=ProjectIO.Load(revision.SnapshotPath);
        var working=Path.Combine(Path.GetDirectoryName(Root)!,"RestoredProjects",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(working);
        var map=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var path in ProjectBackups.Sources(project).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var copy=Path.Combine(working,map.Count.ToString("D4")+"_"+Path.GetFileName(path));File.Copy(path,copy);map[path]=copy;
        }
        string Remap(string path)=>map.GetValueOrDefault(path,path);
        foreach(var item in project.Items)
        {
            item.ModelPath=Remap(item.ModelPath);item.TexturePaths=item.TexturePaths.Select(Remap).ToList();
            item.TextureLabels=item.TextureLabels.ToDictionary(p=>Remap(p.Key),p=>p.Value,StringComparer.OrdinalIgnoreCase);
            item.TextureSources=item.TextureSources.ToDictionary(p=>Remap(p.Key),p=>new TextureSourceLink{SourcePath=Remap(p.Value.SourcePath),TextureName=p.Value.TextureName,MaxDimension=p.Value.MaxDimension,CompressDiffuse=p.Value.CompressDiffuse},StringComparer.OrdinalIgnoreCase);
            item.PreviewPath=Remap(item.PreviewPath);item.PhysicsPath=Remap(item.PhysicsPath);item.FirstPersonPath=Remap(item.FirstPersonPath);
        }
        foreach(var tattoo in project.Tattoos){tattoo.SourcePath=Remap(tattoo.SourcePath);tattoo.TexturePath=Remap(tattoo.TexturePath);}
        project.MaleYmtPath=Remap(project.MaleYmtPath);project.FemaleYmtPath=Remap(project.FemaleYmtPath);project.MaleMetaPath=Remap(project.MaleMetaPath);project.FemaleMetaPath=Remap(project.FemaleMetaPath);project.SupplementalYmtPaths=project.SupplementalYmtPaths.Select(Remap).ToList();
        return project;
    }
}
