using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Media3D;
using System.Xml.Linq;

namespace DuckStudio;

public sealed record LodStatistics(string Level,int Vertices,int Triangles,int Meshes)
{
    public override string ToString()=>$"{Level}: {Triangles:N0} triangles / {Vertices:N0} vertices / {Meshes} meshes";
}
public sealed record ModelInspection(List<LodStatistics> Lods,int Bones,List<string> DiffuseNames,bool HasWeights);
public static partial class CodeWalkerPreview
{
    public static byte[] CompileDrawableXml(string xml,string inputFolder,string coreDll,string? preserveMaterialsFrom=null)
    {
        EnsureCore(coreDll);
        object? original=null;
        if(preserveMaterialsFrom!=null)
        {
            var fileType=core!.GetType("CodeWalker.GameFiles.YddFile",true)!;original=Activator.CreateInstance(fileType)!;fileType.GetMethod("Load",[typeof(byte[])])!.Invoke(original,[File.ReadAllBytes(preserveMaterialsFrom)]);
            // Some clothing embeds texture declarations without pixel data. Retain
            // the original binary material blocks instead of forcing a DDS round-trip.
            var document=XDocument.Parse(xml);document.Descendants("TextureDictionary").Remove();xml=document.ToString();
        }
        var type=core!.GetType("CodeWalker.GameFiles.XmlYdd",true)!;
        var file=type.GetMethod("GetYdd",[typeof(string),typeof(string)])!.Invoke(null,[xml,inputFolder])!;
        if(original!=null && Member(original,"Drawables") is Array source && Member(file,"DrawableDict") is {} dict && Member(dict,"Drawables") is {} entries && Member(entries,"data_items") is Array target)
            for(var i=0;i<Math.Min(source.Length,target.Length);i++)if(source.GetValue(i) is {} old && target.GetValue(i) is {} next)Set(next,"ShaderGroup",Member(old,"ShaderGroup"));
        return (byte[])file.GetType().GetMethod("Save",Type.EmptyTypes)!.Invoke(file,null)!;
    }
    public static List<string> TextureNames(string path,string coreDll)=>Textures(ReadYtd(path,coreDll)).Cast<object>().Select(t=>Member(t,"Name")?.ToString()??"").ToList();
    public static ModelInspection InspectModel(string path,string coreDll)
    {
        var doc=XDocument.Parse(ExportXmlFile(path,"",coreDll));
        var lods=new List<LodStatistics>();
        foreach(var level in ModelTools.Levels)
        {
            var meshes=doc.Descendants(level).Descendants("Geometries").Elements("Item").ToList();
            lods.Add(new(level.Replace("DrawableModels",""),meshes.Sum(m=>ModelTools.Rows(m).Length),meshes.Sum(m=>ModelTools.Indices(m).Length/3),meshes.Count));
        }
        return new(lods,doc.Descendants("Bones").Elements("Item").Count(),DiffuseTextureNames(path),doc.Descendants("Layout").Elements("BlendWeights").Any());
    }
}

public static class ModelTools
{
    public static readonly string[] Levels=["DrawableModelsHigh","DrawableModelsMedium","DrawableModelsLow","DrawableModelsVeryLow"];
    public static string[] Rows(XElement mesh)=>(mesh.Element("VertexBuffer")?.Element("Data")??mesh.Element("VertexBuffer")?.Element("Data2"))?.Value.Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)??[];
    public static int[] Indices(XElement mesh)=>(mesh.Element("IndexBuffer")?.Element("Data")?.Value??"").Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
    public static int GenerateLods(string source,string destination,string core,double medium=.65,double low=.35,double veryLow=.15)
    {
        if(!(medium>low && low>veryLow && veryLow>0 && medium<1))throw new InvalidDataException("LOD ratios must decrease from Medium to Very low and remain between 0 and 1.");
        var temp=Path.Combine(Path.GetDirectoryName(destination)!,"xml-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try
        {
            var doc=XDocument.Parse(CodeWalkerPreview.ExportXmlFile(source,temp,core));int removed=0;
            foreach(var high in doc.Descendants(Levels[0]).ToArray())
            {
                if(high.Parent==null || !high.Elements("Item").Any())continue;
                var owner=high.Parent;
                foreach(var (name,distance) in new[]{("LodDistHigh",30),("LodDistMed",60),("LodDistLow",120),("LodDistVlow",180)})owner.Element(name)?.SetAttributeValue("value",distance);
                foreach(var flag in new[]{"FlagsMed","FlagsLow","FlagsVlow"})owner.Element(flag)?.SetAttributeValue("value",owner.Element("FlagsHigh")?.Attribute("value")?.Value??"1");
                for(var i=1;i<4;i++)
                {
                    var level=new XElement(high){Name=Levels[i]};var ratio=new[]{1,medium,low,veryLow}[i];
                    foreach(var mesh in level.Descendants("Geometries").Elements("Item"))removed+=Simplify(mesh,ratio);
                    high.Parent.Element(Levels[i])?.Remove();high.Parent.Add(level);
                }
            }
            var bytes=CodeWalkerPreview.CompileDrawableXml(doc.ToString(),temp,core,source);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.WriteAllBytes(destination,bytes);
            var checkedModel=CodeWalkerPreview.InspectModel(destination,core);
            if(checkedModel.Lods[0].Triangles==0 || checkedModel.Lods.Any(x=>x.Triangles==0))throw new InvalidDataException("Generated LOD verification failed.");
            return removed;
        }
        finally{Directory.Delete(temp,true);}
    }
    // Conservative vertex clustering: lock open boundaries, preserve exact skin
    // weights/indices and other packed attributes, and never cross a material mesh.
    // Ratios are budgets, not a promise; seams and boundaries may limit reduction.
    public static int Simplify(XElement mesh,double ratio)
    {
        var rows=Rows(mesh);var old=Indices(mesh);if(rows.Length<4||old.Length<6)return 0;
        var layout=mesh.Element("VertexBuffer")?.Element("Layout")?.Elements().Select(x=>x.Name.LocalName).ToArray()??[];
        if(layout.FirstOrDefault()!="Position")return 0;
        var values=rows.Select(r=>Regex.Split(r.Trim(),@"\s{2,}")).ToArray();
        if(values.Any(v=>v.Length!=layout.Length))throw new InvalidDataException("Unsupported vertex layout in model LOD generation.");
        var points=values.Select(v=>v[0].Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(s=>double.Parse(s,CultureInfo.InvariantCulture)).ToArray()).ToArray();
        var edges=new Dictionary<(int,int),int>();
        for(var i=0;i+2<old.Length;i+=3)for(var j=0;j<3;j++){var a=old[i+j];var b=old[i+(j+1)%3];var key=(Math.Min(a,b),Math.Max(a,b));edges[key]=edges.GetValueOrDefault(key)+1;}
        var boundary=edges.Where(x=>x.Value==1).SelectMany(x=>new[]{x.Key.Item1,x.Key.Item2}).ToHashSet();
        var extent=Enumerable.Range(0,3).Max(k=>points.Max(p=>p[k])-points.Min(p=>p[k]));if(extent<=0)return 0;
        int[] best=old;int[] bestMap=Enumerable.Range(0,rows.Length).ToArray();
        for(var attempt=0;attempt<10;attempt++)
        {
            var cell=extent*Math.Pow(1.55,attempt)/100;var lookup=new Dictionary<string,int>();var map=new int[rows.Length];
            for(var v=0;v<rows.Length;v++)
            {
                if(boundary.Contains(v)){map[v]=v;continue;}
                var key=string.Join(',',points[v].Take(3).Select(p=>Math.Floor(p/cell).ToString(CultureInfo.InvariantCulture)));
                for(var k=1;k<layout.Length;k++)
                {
                    var attribute=values[v][k];
                    if(layout[k].StartsWith("TexCoord")||layout[k] is "Normal" or "Tangent")
                    {
                        var precision=layout[k].StartsWith("TexCoord")?Math.Max(8,128/Math.Pow(1.4,attempt)):12d;
                        attribute=string.Join(',',attribute.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(s=>Math.Round(double.Parse(s,CultureInfo.InvariantCulture)*precision).ToString(CultureInfo.InvariantCulture)));
                    }
                    key+="|"+attribute;
                }
                if(!lookup.TryGetValue(key,out var representative)){representative=v;lookup[key]=v;}map[v]=representative;
            }
            var result=new List<int>();var unique=new HashSet<(int,int,int)>();
            for(var i=0;i+2<old.Length;i+=3)
            {
                var a=map[old[i]];var b=map[old[i+1]];var c=map[old[i+2]];if(a==b||b==c||a==c)continue;
                if(!unique.Add((a,b,c)))continue;result.AddRange([a,b,c]);
            }
            // Avoid destroying a small or delicate mesh while trying to reach a budget.
            if(result.Count>=3 && result.Count<best.Length){best=result.ToArray();bestMap=map;}
            if(best.Length<=old.Length*ratio)break;
        }
        if(best.Length==old.Length)return 0;
        var used=best.Distinct().Order().ToArray();var compact=used.Select((v,i)=>(v,i)).ToDictionary(x=>x.v,x=>x.i);
        var data=mesh.Element("VertexBuffer")!.Element("Data")??mesh.Element("VertexBuffer")!.Element("Data2")!;
        data.Value="\n"+string.Join('\n',used.Select(v=>rows[v]))+"\n";
        mesh.Element("IndexBuffer")!.Element("Data")!.Value=string.Join(' ',best.Select(v=>compact[v]));
        return (old.Length-best.Length)/3;
    }
}
