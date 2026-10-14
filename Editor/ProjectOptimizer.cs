using System.IO;
using System.Security.Cryptography;

namespace DuckStudio;

public sealed record OptimizationReport(string Name,long Bytes,int Textures,int LargestDimension,string Finding)
{
    public override string ToString()=>Name+"   /   "+(Bytes/1048576d).ToString("0.00")+" MB   /   "+Textures+" maps   /   "+LargestDimension+" px   /   "+Finding;
}
public static class ProjectOptimizer
{
    public static List<OptimizationReport> Analyze(ClothingProject project,string core,int maxDimension)
    {
        var results=new List<OptimizationReport>();var hashes=new Dictionary<string,string>();
        foreach(var path in project.Items.SelectMany(a=>a.TexturePaths).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if(!File.Exists(path)){results.Add(new(Path.GetFileName(path),0,0,0,"Missing file"));continue;}
            try
            {
                var info=CodeWalkerPreview.InspectTextures(path,core);
                using var stream=File.OpenRead(path);var hash=Convert.ToHexString(SHA256.HashData(stream));
                var finding=hashes.TryGetValue(hash,out var original)?"Same content as "+original:info.LargestDimension>maxDimension?"Can downsize":"Within size limit";
                hashes.TryAdd(hash,Path.GetFileName(path));
                results.Add(new(Path.GetFileName(path),new FileInfo(path).Length,info.Textures,info.LargestDimension,finding));
            }
            catch(Exception ex){results.Add(new(Path.GetFileName(path),new FileInfo(path).Length,0,0,(ex.InnerException??ex).Message));}
        }
        return results;
    }
    public static (long Before,long After,int Files,int Removed) Apply(ClothingProject project,string core,int maxDimension,bool compress,bool removeDuplicateVariations)
    {
        var replacements=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);long before=0,after=0;int removed=0;
        // All conversions finish before changing the project. Source files never change.
        foreach(var path in project.Items.SelectMany(a=>a.TexturePaths).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var destination=Path.Combine(Path.GetDirectoryName(TextureImport.NewDestination(path))!,Path.GetFileName(path));
            CodeWalkerPreview.OptimizeTextures(path,destination,core,maxDimension,compress);
            replacements[path]=destination;before+=new FileInfo(path).Length;after+=new FileInfo(destination).Length;
        }
        foreach(var asset in project.Items)
        {
            var labels=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var links=new Dictionary<string,TextureSourceLink>(StringComparer.OrdinalIgnoreCase);var textures=new List<string>();var hashes=new HashSet<string>();
            foreach(var old in asset.TexturePaths)
            {
                var next=replacements[old];
                if(removeDuplicateVariations){using var input=File.OpenRead(next);if(!hashes.Add(Convert.ToHexString(SHA256.HashData(input)))){removed++;continue;}}
                textures.Add(next);if(asset.TextureLabels.TryGetValue(old,out var label))labels[next]=label;
                if(asset.TextureSources.TryGetValue(old,out var link))links[next]=new TextureSourceLink{SourcePath=link.SourcePath,TextureName=link.TextureName,MaxDimension=maxDimension,CompressDiffuse=compress};
            }
            asset.TexturePaths=textures;asset.TextureLabels=labels;asset.TextureSources=links;
        }
        return(before,after,replacements.Count,removed);
    }
}
