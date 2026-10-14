using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace DuckStudio;

public sealed class OutfitEntry
{
    public string Slot { get; set; } = "jbib";
    public string AssetId { get; set; } = "";
    public int Texture { get; set; }
    public int BaseDrawable { get; set; }
    public bool Hidden { get; set; }
}
public sealed class OutfitPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New outfit";
    public string Ped { get; set; } = "mp_m_freemode_01";
    public List<OutfitEntry> Entries { get; set; } = [];
    public List<string> TattooIds { get; set; } = [];
    public override string ToString() => Name;
}

// The editor keeps immutable asset copies with each journal state. A replacement or
// live texture write can therefore be undone even after its original file changes.
public sealed class EditJournal
{
    private sealed record State(string Json, Dictionary<string,string> Assets);
    private readonly string root;
    private readonly List<State> states = [];
    private readonly Dictionary<string,(long Ticks,long Length,string Copy)> cache = new(StringComparer.OrdinalIgnoreCase);
    private int cursor = -1;
    public bool CanUndo => cursor > 0;
    public bool CanRedo => cursor >= 0 && cursor < states.Count - 1;
    public EditJournal(string root) { this.root = root; }
    public void Reset(ClothingProject project) { states.Clear(); cursor=-1; Observe(project); }
    public bool Observe(ClothingProject project)
    {
        var json = JsonSerializer.Serialize(project, ProjectIO.Json);
        var paths = ProjectBackups.Sources(project).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var assets = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var path in paths)
        {
            var info = new FileInfo(path);
            if(!cache.TryGetValue(path,out var existing) || existing.Ticks!=info.LastWriteTimeUtc.Ticks || existing.Length!=info.Length || !File.Exists(existing.Copy))
            {
                Directory.CreateDirectory(root);
                using var input = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                var destination=Path.Combine(root,Convert.ToHexString(SHA256.HashData(input))+Path.GetExtension(path)); input.Position=0;
                if(!File.Exists(destination)) { using var output=File.Create(destination); input.CopyTo(output); }
                existing=(info.LastWriteTimeUtc.Ticks,info.Length,destination);cache[path]=existing;
            }
            assets[path]=existing.Copy;
        }
        if(cursor>=0 && states[cursor].Json==json && assets.Count==states[cursor].Assets.Count && assets.All(x=>states[cursor].Assets.GetValueOrDefault(x.Key)==x.Value))return false;
        if(cursor<states.Count-1)states.RemoveRange(cursor+1,states.Count-cursor-1);
        states.Add(new(json,assets)); if(states.Count>101)states.RemoveAt(0);cursor=states.Count-1;return true;
    }
    public ClothingProject Undo() { if(!CanUndo)throw new InvalidOperationException("Nothing to undo.");return Restore(states[--cursor]); }
    public ClothingProject Redo() { if(!CanRedo)throw new InvalidOperationException("Nothing to redo.");return Restore(states[++cursor]); }
    private ClothingProject Restore(State state)
    {
        var project=JsonSerializer.Deserialize<ClothingProject>(state.Json,ProjectIO.Json)!;
        // Restore to derived paths, retaining original source links while their artwork
        // is unchanged. Changed sources use independent artwork to keep the undo stable.
        var working=Path.Combine(root,"Working",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(working);
        var map=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var pair in state.Assets)
        {
            var target=Path.Combine(working,map.Count.ToString("D4")+"_"+Path.GetFileName(pair.Key));File.Copy(pair.Value,target);map[pair.Key]=target;
        }
        foreach(var source in project.Items.SelectMany(a=>a.TextureSources.Values.Select(x=>x.SourcePath)).Concat(project.Tattoos.Select(x=>x.SourcePath)).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if(state.Assets.TryGetValue(source,out var saved) && SHA256.HashData(File.ReadAllBytes(source)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(saved))))map[source]=source;
        }
        ProjectAssets.Remap(project,p=>map.GetValueOrDefault(p,p));
        // The stored journal state reflects the new working paths, avoiding a phantom
        // edit on the next observation while preserving the redo branch.
        states[cursor]=new(JsonSerializer.Serialize(project,ProjectIO.Json),map.ToDictionary(x=>x.Value,x=>state.Assets[x.Key],StringComparer.OrdinalIgnoreCase));
        foreach(var pair in map){var info=new FileInfo(pair.Value);cache[pair.Value]=(info.LastWriteTimeUtc.Ticks,info.Length,state.Assets[pair.Key]);}
        return project;
    }
}

public static class ProjectAssets
{
    public static ClothingProject Clone(ClothingProject p)=>JsonSerializer.Deserialize<ClothingProject>(JsonSerializer.Serialize(p,ProjectIO.Json),ProjectIO.Json)!;
    public static void Remap(ClothingProject project,Func<string,string> map)
    {
        foreach(var item in project.Items)
        {
            item.TextureLabels=item.TextureLabels.ToDictionary(x=>map(x.Key),x=>x.Value,StringComparer.OrdinalIgnoreCase);
            item.TextureSources=item.TextureSources.ToDictionary(x=>map(x.Key),x=>new TextureSourceLink{SourcePath=map(x.Value.SourcePath),TextureName=x.Value.TextureName,MaxDimension=x.Value.MaxDimension,CompressDiffuse=x.Value.CompressDiffuse},StringComparer.OrdinalIgnoreCase);
            item.ModelPath=map(item.ModelPath);item.TexturePaths=item.TexturePaths.Select(map).ToList();item.FirstPersonPath=map(item.FirstPersonPath);item.PhysicsPath=map(item.PhysicsPath);item.PreviewPath=map(item.PreviewPath);
        }
        foreach(var tattoo in project.Tattoos){tattoo.SourcePath=map(tattoo.SourcePath);tattoo.TexturePath=map(tattoo.TexturePath);}
        project.MaleYmtPath=map(project.MaleYmtPath);project.FemaleYmtPath=map(project.FemaleYmtPath);project.MaleMetaPath=map(project.MaleMetaPath);project.FemaleMetaPath=map(project.FemaleMetaPath);project.SupplementalYmtPaths=project.SupplementalYmtPaths.Select(map).ToList();
    }
}

public sealed record ProjectDifference(string Item,string Change,string Before,string After)
{
    public override string ToString()=>$"{Item} - {Change}\nBefore: {Before}\nAfter: {After}";
}
public static class ProjectComparison
{
    public static List<ProjectDifference> Compare(ClothingProject before,ClothingProject after)
    {
        var changes=new List<ProjectDifference>();
        void Change(string item,string field,object? old,object? next){if(!Equals(old,next))changes.Add(new(item,field,old?.ToString()??"",next?.ToString()??""));}
        foreach(var property in new[]{"Name","Collection","Author","Version","Description","BuildTarget"})Change("Project",property,typeof(ClothingProject).GetProperty(property)!.GetValue(before),typeof(ClothingProject).GetProperty(property)!.GetValue(after));
        string Content(string path)=>File.Exists(path)?Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))):"missing";
        string FileDescription(string path)=>File.Exists(path)?$"{Path.GetFileName(path)} ({new FileInfo(path).Length:N0} bytes)":"Missing: "+path;
        void FileChange(string name,string field,string old,string next){if(Content(old)!=Content(next))Change(name,field,FileDescription(old),FileDescription(next));}
        foreach(var id in before.Items.Select(x=>x.Id).Union(after.Items.Select(x=>x.Id)))
        {
            var a=before.Items.FirstOrDefault(x=>x.Id==id);var b=after.Items.FirstOrDefault(x=>x.Id==id);
            if(a==null||b==null){changes.Add(new(b?.Name??a!.Name,a==null?"Added drawable":"Removed drawable",a?.Display??"",b?.Display??""));continue;}
            foreach(var prop in typeof(ClothingAsset).GetProperties().Where(p=>p.CanWrite && p.PropertyType.IsValueType || p.CanWrite && p.PropertyType==typeof(string)))
                if(!prop.Name.EndsWith("Path") && prop.Name!="GroupTitle")Change(b.Name,prop.Name,prop.GetValue(a),prop.GetValue(b));
            FileChange(b.Name,"Model",a.ModelPath,b.ModelPath);FileChange(b.Name,"First person model",a.FirstPersonPath,b.FirstPersonPath);FileChange(b.Name,"Physics",a.PhysicsPath,b.PhysicsPath);
            for(var i=0;i<Math.Max(a.TexturePaths.Count,b.TexturePaths.Count);i++)FileChange(b.Name,$"Texture {(char)('a'+i)}",a.TexturePaths.ElementAtOrDefault(i)??"",b.TexturePaths.ElementAtOrDefault(i)??"");
            for(var i=0;i<Math.Max(a.TexturePaths.Count,b.TexturePaths.Count);i++)
            {
                var oldPath=a.TexturePaths.ElementAtOrDefault(i)??"";var newPath=b.TexturePaths.ElementAtOrDefault(i)??"";
                FileChange(b.Name,$"Linked artwork {(char)('a'+i)}",a.TextureSources.GetValueOrDefault(oldPath)?.SourcePath??"",b.TextureSources.GetValueOrDefault(newPath)?.SourcePath??"");
            }
            Change(b.Name,"Texture labels",string.Join(", ",a.TexturePaths.Select(x=>a.TextureLabels.GetValueOrDefault(x,""))),string.Join(", ",b.TexturePaths.Select(x=>b.TextureLabels.GetValueOrDefault(x,""))));
        }
        foreach(var id in before.Tattoos.Select(x=>x.Id).Union(after.Tattoos.Select(x=>x.Id)))
        {
            var a=before.Tattoos.FirstOrDefault(x=>x.Id==id);var b=after.Tattoos.FirstOrDefault(x=>x.Id==id);
            if(a==null||b==null){changes.Add(new(b?.Name??a!.Name,a==null?"Added tattoo":"Removed tattoo",a?.Name??"",b?.Name??""));continue;}
            foreach(var property in typeof(TattooAsset).GetProperties().Where(p=>!p.Name.EndsWith("Path")))Change(b.Name,property.Name,property.GetValue(a),property.GetValue(b));
            FileChange(b.Name,"Tattoo artwork",a.SourcePath,b.SourcePath);
        }
        Change("Outfits","Saved presets",JsonSerializer.Serialize(before.Outfits),JsonSerializer.Serialize(after.Outfits));
        return changes;
    }
}
