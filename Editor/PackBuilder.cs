using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DuckStudio;

public static class PackBuilder
{
    public sealed record BuildOptions(bool GroupStreamByType = true, BuildTarget Target = BuildTarget.FiveM);
    private static readonly string[] Skins = ["uni", "whi", "bla", "chi", "lat", "ara", "bal", "jam", "kor", "ita", "pak"];
    private static XElement V(string name, object value) => new(name, new XAttribute("value", value));
    private static XElement A(string name, string type, IEnumerable<XElement>? items = null) => new(name, new XAttribute("itemType", type), items);
    public static bool IsConfigured(ClothingProject p) => !string.IsNullOrWhiteSpace(p.Name) && Regex.IsMatch(p.Collection, "^[a-z][a-z0-9_]{1,30}$") && Directory.Exists(p.OutputFolder);
    public static List<string> Validate(ClothingProject p, Action<ClothingAsset, IReadOnlyList<string>>? assetStatus = null)
    {
        List<string> issues = [];
        var assetIssues = new Dictionary<ClothingAsset, List<string>>();
        if (!IsConfigured(p)) issues.Add("Set a project name, DLC name (2-31 lowercase letters, digits or underscores), and an existing output folder in Settings.");
        if(p.Collection.Equals("duckstudio_test",StringComparison.OrdinalIgnoreCase))issues.Add("Choose a different collection name: duckstudio_test is reserved for the FiveM test companion.");
        if (p.Items.Count == 0 && p.Tattoos.Count==0) issues.Add("Add at least one drawable or tattoo.");
        issues.AddRange(TattooBuilder.Validate(p));
        foreach (var a in p.Items)
        {
            var itemIssues = ValidateAsset(a, p.CodeWalkerCorePath);
            assetIssues[a] = itemIssues;
            issues.AddRange(itemIssues);
        }
        foreach (var g in p.Items.GroupBy(x => (x.Ped, x.Slot)))
        {
            if (g.Count() > 128)
            {
                var issue = $"{g.Key}: maximum 128 drawables per type per collection.";
                issues.Add(issue);
                foreach (var a in g) assetIssues[a].Add(issue);
            }
            if (!g.First().IsProp && g.Sum(x => Math.Max(1,x.TexturePaths.Count)) > 255)
            {
                var issue = $"{g.Key}: texture count exceeds the YMT byte limit of 255. Split this type into another project.";
                issues.Add(issue);
                foreach (var a in g) assetIssues[a].Add(issue);
            }
        }
        foreach (var g in p.Items.Where(x=>x.IsProp).GroupBy(x=>x.Ped))
            if(g.Count()>255)
            {
                var issue = $"{g.Key}: maximum 255 props per collection.";
                issues.Add(issue);
                foreach (var a in g) assetIssues[a].Add(issue);
            }
        foreach(var f in p.SupplementalYmtPaths) if(!File.Exists(f)) issues.Add($"Missing supplemental file: {f}");
        if (assetStatus != null)
            foreach (var a in p.Items) assetStatus(a, assetIssues[a]);
        return issues;
    }
    public static List<string> ValidateAsset(ClothingAsset a, string? coreDll = null)
    {
        List<string> issues = [];
        if (!File.Exists(a.ModelPath)) issues.Add($"Missing model: {a.Name}");
        else if (!Path.GetExtension(a.ModelPath).Equals(".ydd", StringComparison.OrdinalIgnoreCase)) issues.Add($"Model must be a .ydd file: {a.Name}");
        var codeWalkerCore = File.Exists(coreDll) ? coreDll! : Path.Combine(AppContext.BaseDirectory, "CodeWalker.Core.dll");
        if (a.TexturePaths.Count == 0 && !CodeWalkerPreview.HasEmbeddedDiffuseTexture(a.ModelPath, codeWalkerCore))
            issues.Add($"No diffuse texture variation for {a.Name}. Add a .ytd texture or embed the diffuse texture in the model.");
        if (!ProjectIO.Slots.Contains(a.Slot)) issues.Add($"Unsupported drawable type: {a.Slot}");
        if (a.Ped != "mp_m_freemode_01" && a.Ped != "mp_f_freemode_01") issues.Add($"Unsupported ped: {a.Ped}");
        if (a.Drawable < 0) issues.Add($"Position must be positive or zero: {a.Name}");
        if (a.TexturePaths.Count > 26) issues.Add($"{a.Name} has more than 26 texture variations.");
        foreach (var texture in a.TexturePaths)
            if (File.Exists(texture) && !Path.GetExtension(texture).Equals(".ytd", StringComparison.OrdinalIgnoreCase))
                issues.Add($"Texture must be a .ytd file: {texture}");
        if (File.Exists(a.FirstPersonPath) && !Path.GetExtension(a.FirstPersonPath).Equals(".ydd", StringComparison.OrdinalIgnoreCase))
            issues.Add($"First-person model must be a .ydd file: {a.FirstPersonPath}");
        if (File.Exists(a.PhysicsPath) && !Path.GetExtension(a.PhysicsPath).Equals(".yld", StringComparison.OrdinalIgnoreCase))
            issues.Add($"Mesh physics must be a .yld file: {a.PhysicsPath}");
        foreach (var f in a.TexturePaths.Concat(new[] { a.FirstPersonPath, a.PhysicsPath }.Where(x => x.Length > 0)))
            if (!File.Exists(f)) issues.Add($"Missing file: {f}");
        return issues;
    }
    private static string Dlc(ClothingProject p, string ped) => (ped.Contains("mp_f_") ? "mp_f_" : "mp_m_") + p.Collection;
    private static (int Id, string Name) Anchor(string slot) => slot switch { "p_head" => (0,"ANCHOR_HEAD"), "p_eyes" => (1,"ANCHOR_EYES"), "p_ears" => (2,"ANCHOR_EARS"), "p_lwrist" => (6,"ANCHOR_LEFT_WRIST"), _ => (7,"ANCHOR_RIGHT_WRIST") };
    private static string Suffix(ClothingAsset a) => Regex.Match(Path.GetFileName(a.ModelPath), @"_([urm])(?:_\d+)?\.ydd$",RegexOptions.IgnoreCase) is { Success:true } m ? m.Groups[1].Value.ToLowerInvariant() : "u";
    private static string Skin(string path) => Regex.Match(path,@"_([a-z]{3})\.ytd$",RegexOptions.IgnoreCase) is { Success:true } m && Skins.Contains(m.Groups[1].Value.ToLowerInvariant()) ? m.Groups[1].Value.ToLowerInvariant() : "uni";
    private static int TexCount(ClothingAsset a) => Math.Max(1,a.TexturePaths.Count);

    public static XDocument VariationXml(ClothingProject p, string ped)
    {
        var items=p.Items.Where(x=>x.Ped==ped).ToList();
        var groups=items.Where(x=>!x.IsProp).GroupBy(x=>x.Slot).OrderBy(x=>Array.IndexOf(ProjectIO.Slots,x.Key)).ToList();
        var avail=Enumerable.Repeat(255,12).ToArray();
        List<XElement> components=[], infos=[], props=[], anchors=[];
        foreach(var g in groups)
        {
            int slot=Array.IndexOf(ProjectIO.Slots,g.Key); avail[slot]=components.Count;
            var rows=g.OrderBy(x=>x.Drawable).ToList();
            var drawables = new List<XElement>();
            foreach (var a in rows)
            {
                var tex = Enumerable.Range(0,TexCount(a)).Select(i => new XElement("Item", V("texId", i<a.TexturePaths.Count ? Array.IndexOf(Skins,Skin(a.TexturePaths[i])) : 0),V("distribution",255)));
                drawables.Add(new XElement("Item",V("propMask",Suffix(a)=="r"?17:Suffix(a)=="m"?33:1),V("numAlternatives",a.FirstPersonPath.Length>0?1:0),A("aTexData","CPVTextureData",tex),new XElement("clothData",V("ownsCloth",a.PhysicsPath.Length>0))));
            }
            components.Add(new XElement("Item",V("numAvailTex",rows.Sum(TexCount)),A("aDrawblData3","CPVDrawblData",drawables)));
            for(int i=0;i<rows.Count;i++) infos.Add(new XElement("Item",new XElement("pedXml_audioID",rows[i].Audio),new XElement("pedXml_audioID2"),new XElement("pedXml_expressionMods","0 0 0 0 0"),V("flags",rows[i].BehaviourFlags),new XElement("inclusions",0),new XElement("exclusions",0),new XElement("pedXml_vfxComps","PV_COMP_HEAD"),V("pedXml_flags",0),V("pedXml_compIdx",slot),V("pedXml_drawblIdx",i)));
        }
        foreach(var g in items.Where(x=>x.IsProp).GroupBy(x=>x.Slot).OrderBy(x=>Anchor(x.Key).Id))
        {
            var rows=g.OrderBy(x=>x.Drawable).ToList(); var anchor=Anchor(g.Key);
            anchors.Add(new XElement("Item",new XElement("props",string.Join(" ",rows.Select(TexCount))),new XElement("anchor",anchor.Name)));
            for(int i=0;i<rows.Count;i++) props.Add(new XElement("Item",new XElement("audioId",rows[i].Audio=="none"?"":rows[i].Audio),new XElement("expressionMods","0 0 0 0 0"),A("texData","CPedPropTexData",Enumerable.Range(0,TexCount(rows[i])).Select(t=>new XElement("Item",new XElement("inclusions",0),new XElement("exclusions",0),V("texId",t),V("inclusionId",0),V("exclusionId",0),V("distribution",255)))),new XElement("renderFlags"),V("propFlags",rows[i].BehaviourFlags),V("flags",0),V("anchorId",anchor.Id),V("propId",i),V("stickyness",0)));
        }
        return new XDocument(new XElement("CPedVariationInfo",new XAttribute("name",Dlc(p,ped)),V("bHasTexVariations",false),V("bHasDrawblVariations",false),V("bHasLowLODs",items.Any(x=>!x.IsProp)&&items.Where(x=>!x.IsProp).All(x=>x.GeneratedLods)),V("bIsSuperLOD",false),new XElement("availComp",string.Join(" ",avail)),A("aComponentData3","CPVComponentData",components),A("aSelectionSets","CPedSelectionSet"),A("compInfos","CComponentInfo",infos),new XElement("propInfo",V("numAvailProps",props.Count),A("aPropMetaData","CPedPropMetaData",props),A("aAnchors","CAnchorProps",anchors)),new XElement("dlcName",Dlc(p,ped))));
    }
    public static XDocument ShopXml(ClothingProject p,string ped)
    {
        List<XElement> components=[],props=[];
        foreach(var group in p.Items.Where(x=>x.Ped==ped).GroupBy(x=>x.Slot))
        {
            int index=0;
            foreach(var a in group.OrderBy(x=>x.Drawable))
            {
                for(int t=0;t<TexCount(a);t++)
                {
                    var item=new XElement("Item",new XElement("lockHash"),V("cost",0),new XElement("textLabel"),new XElement("uniqueNameHash",$"CLO_{Dlc(p,ped)}_{a.Slot}_{index}_{t}".ToUpperInvariant()),new XElement("eShopEnum",a.Shop),V("locate",-99),V("scriptSaveData",0),new XElement("restrictionTags",a.RestrictionTags.Split([',',' ',';'],StringSplitOptions.RemoveEmptyEntries).Select(x=>new XElement("Item",x))),new XElement("forcedComponents"));
                    if(a.IsProp) item.Add(new XElement("forcedProps"));
                    item.Add(new XElement("variantComponents"));
                    if(a.IsProp) item.Add(new XElement("variantProps"));
                    item.Add(V(a.IsProp?"propIndex":"drawableIndex",0),V(a.IsProp?"localPropIndex":"localDrawableIndex",index),new XElement(a.IsProp?"eAnchorPoint":"eCompType",a.IsProp?Anchor(a.Slot).Name:"PV_COMP_"+a.Slot.ToUpperInvariant()),V("textureIndex",t),V("isInOutfit",false));
                    (a.IsProp?props:components).Add(item);
                }
                index++;
            }
        }
        return new XDocument(new XElement("ShopPedApparel",new XElement("pedName",ped),new XElement("dlcName",Dlc(p,ped)),new XElement("fullDlcName",ped+"_"+Dlc(p,ped)),new XElement("eCharacter",ped.Contains("mp_f_")?"SCR_CHAR_MULTIPLAYER_F":"SCR_CHAR_MULTIPLAYER"),new XElement("creatureMetaData","mp_creaturemetadata_"+p.Collection),new XElement("pedOutfits"),new XElement("pedComponents",components),new XElement("pedProps",props)));
    }
    public static string Build(ClothingProject p,string parent,BuildOptions? options=null)
    {
        options ??= new BuildOptions();
        if(!Enum.IsDefined(options.Target))throw new InvalidDataException("Unknown build target.");
        var imageCore=File.Exists(p.CodeWalkerCorePath)?p.CodeWalkerCorePath:Path.Combine(AppContext.BaseDirectory,"CodeWalker.Core.dll");
        foreach(var asset in p.Items)
            foreach(var pair in asset.TextureSources.Where(x=>asset.TexturePaths.Contains(x.Key)))
                pair.Value.TextureName=TextureImport.Convert(pair.Value.SourcePath,pair.Key,imageCore,asset.ModelPath,pair.Key,pair.Value.TextureName,pair.Value.MaxDimension,pair.Value.CompressDiffuse);
        TattooBuilder.Prepare(p,imageCore);
        var issues=Validate(p); if(issues.Count>0) throw new InvalidOperationException(string.Join(Environment.NewLine,issues));
        var modelErrors=ProjectValidation.Analyze(p,imageCore).Where(f=>f.Severity=="Error").ToList();
        if(modelErrors.Count>0)throw new BuildValidationException(modelErrors);
        var target=Path.GetFullPath(Path.Combine(parent,p.Collection+"-project"));
        var marker=Path.Combine(target,"Project","weft-build.json");
        if(Directory.Exists(target)&&!File.Exists(marker)) throw new IOException("The export folder already exists and was not generated by Duck Studio. Select another output folder to protect it.");
        Directory.CreateDirectory(parent);
        var stage=Path.Combine(Path.GetFullPath(parent),".weft-stage-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            var resource=Directory.CreateDirectory(Path.Combine(stage,"FiveM",p.Collection)).FullName;
            var projectDir=Directory.CreateDirectory(Path.Combine(stage,"Project")).FullName;
            var assetsDir=Directory.CreateDirectory(Path.Combine(stage,"Assets")).FullName;
            var stream=Directory.CreateDirectory(Path.Combine(resource,"stream")).FullName;
            List<string> metas=[];
            var core=File.Exists(p.CodeWalkerCorePath)?p.CodeWalkerCorePath:Path.Combine(AppContext.BaseDirectory,"CodeWalker.Core.dll");
            foreach(var ped in p.Items.Select(x=>x.Ped).Distinct())
            {
                var stem=ped+"_"+Dlc(p,ped);
                File.WriteAllBytes(Path.Combine(stream,stem+".ymt"),CodeWalkerPreview.CompileMetadata(VariationXml(p,ped).ToString(),core));
                var meta=stem+"_shop.meta";ShopXml(p,ped).Save(Path.Combine(resource,meta));metas.Add(meta);
                foreach(var group in p.Items.Where(x=>x.Ped==ped).GroupBy(x=>x.Slot))
                {
                    var drawableStream=options.GroupStreamByType
                        ? Directory.CreateDirectory(Path.Combine(stream,group.First().IsProp?ped+"_p_"+Dlc(p,ped):stem,group.Key)).FullName
                        : stream;
                    int n=0;
                    foreach(var a in group.OrderBy(x=>x.Drawable))
                    {
                        var prefix=(a.IsProp?ped+"_p_"+Dlc(p,ped):stem)+"^";
                        var model=$"{a.Slot}_{n:000}"+(a.IsProp?"":"_"+Suffix(a));
                        File.Copy(a.ModelPath,Path.Combine(drawableStream,prefix+model+".ydd"));
                        if(a.FirstPersonPath.Length>0)File.Copy(a.FirstPersonPath,Path.Combine(drawableStream,prefix+model+"_1.ydd"));
                        if(a.PhysicsPath.Length>0)File.Copy(a.PhysicsPath,Path.Combine(drawableStream,prefix+model+".yld"));
                        for(int t=0;t<a.TexturePaths.Count;t++)File.Copy(a.TexturePaths[t],Path.Combine(drawableStream,$"{prefix}{a.Slot}_diff_{n:000}_{(char)('a'+t)}"+(a.IsProp?"":"_"+Skin(a.TexturePaths[t]))+".ytd"));
                        n++;
                    }
                }
            }
            var creatureName="mp_creaturemetadata_"+p.Collection+".ymt";
            if(!p.SupplementalYmtPaths.Any(x=>Path.GetFileName(x).Equals(creatureName,StringComparison.OrdinalIgnoreCase)))
            {
                var creature=new XElement("CCreatureMetaData",A("shaderVariableComponents","CShaderVariableComponent"),A("pedPropExpressions","CPedPropExpressionData"),A("pedCompExpressions","CPedCompExpressionData"));
                File.WriteAllBytes(Path.Combine(stream,creatureName),CodeWalkerPreview.CompileMetadata(creature.ToString(),core));
            }
            TattooBuilder.Export(p,resource,stream);
            foreach(var extra in p.SupplementalYmtPaths) File.Copy(extra,Path.Combine(stream,Path.GetFileName(extra)));
            var manifest=new StringBuilder("-- Built with Duck Studio\nfx_version 'cerulean'\ngame 'gta5'\n\n");
            foreach(var pair in new[]{("author",p.Author),("version",p.Version),("description",p.Description)})if(pair.Item2.Length>0)manifest.AppendLine($"{pair.Item1} '{ManifestValue(pair.Item2)}'");
            manifest.AppendLine("\nfiles {");foreach(var m in metas)manifest.AppendLine($"    '{m}',");manifest.AppendLine("}");foreach(var m in metas)manifest.AppendLine($"data_file 'SHOP_PED_APPAREL_META_FILE' '{m}'");
            if(p.Tattoos.Count>0) manifest.AppendLine($"files {{ '{TattooBuilder.Collection(p)}.xml', 'shop_tattoo.meta' }}\ndata_file 'PED_OVERLAY_FILE' '{TattooBuilder.Collection(p)}.xml'\ndata_file 'TATTOO_SHOP_DLC_FILE' 'shop_tattoo.meta'");
            File.WriteAllText(Path.Combine(resource,"fxmanifest.lua"),manifest.ToString());
            FiveMCompanion.WriteCatalog(p,resource);
            if(options.Target==BuildTarget.FiveM)FiveMCompanion.Export(p,Path.Combine(stage,"FiveM"));
            BuildTargets.Export(p,stage,resource,options.Target,core);
            File.WriteAllText(Path.Combine(projectDir,"weft-build.json"),JsonSerializer.Serialize(new{generator="Duck Studio",project=p.Name,collection=p.Collection,drawables=p.Items.Count,tattoos=p.Tattoos.Count,target=options.Target.ToString(),builtUtc=DateTime.UtcNow},ProjectIO.Json));
            File.WriteAllText(Path.Combine(projectDir,"creators.json"),JsonSerializer.Serialize(p.Items.Select(x=>new{x.Name,x.CreatorName,x.Website,x.SupportUrl,x.Notes}),ProjectIO.Json));
            // Keep an independent, relocatable copy of every input used to rebuild the resource.
            var portable=JsonSerializer.Deserialize<ClothingProject>(JsonSerializer.Serialize(p,ProjectIO.Json),ProjectIO.Json)!;
            var copied=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            string BundleFile(string path)
            {
                if(string.IsNullOrWhiteSpace(path)) return "";
                var full=Path.GetFullPath(path);
                if(copied.TryGetValue(full,out var existing)) return existing;
                var currentAssets=Path.Combine(target,"Assets")+Path.DirectorySeparatorChar;
                var assetName=full.StartsWith(currentAssets,StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileName(path) : $"{copied.Count+1:0000}_{Path.GetFileName(path)}";
                File.Copy(full,Path.Combine(assetsDir,assetName));
                return copied[full]=Path.Combine("..","Assets",assetName);
            }
            foreach(var item in portable.Items)
            {
                var oldPaths=item.TexturePaths.ToList();
                item.TextureSources=item.TextureSources.ToDictionary(x=>BundleFile(x.Key),x=>new TextureSourceLink { SourcePath=File.Exists(x.Value.SourcePath)?BundleFile(x.Value.SourcePath):x.Value.SourcePath, TextureName=x.Value.TextureName, MaxDimension=x.Value.MaxDimension, CompressDiffuse=x.Value.CompressDiffuse },StringComparer.OrdinalIgnoreCase);
                item.ModelPath=BundleFile(item.ModelPath);
                item.TexturePaths=item.TexturePaths.Select(BundleFile).ToList();
                var labels=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                for(int i=0;i<oldPaths.Count;i++)
                    if(item.TextureLabels.TryGetValue(oldPaths[i],out var label))labels[item.TexturePaths[i]]=label;
                item.TextureLabels=labels;
                item.FirstPersonPath=BundleFile(item.FirstPersonPath);
                item.PhysicsPath=BundleFile(item.PhysicsPath);
                item.PreviewPath=File.Exists(item.PreviewPath)?BundleFile(item.PreviewPath):"";
            }
            foreach(var tattoo in portable.Tattoos) { tattoo.SourcePath=File.Exists(tattoo.SourcePath)?BundleFile(tattoo.SourcePath):""; tattoo.TexturePath=File.Exists(tattoo.TexturePath)?BundleFile(tattoo.TexturePath):""; }
            portable.BuildTarget=options.Target;
            portable.MaleYmtPath=File.Exists(portable.MaleYmtPath)?BundleFile(portable.MaleYmtPath):"";
            portable.FemaleYmtPath=File.Exists(portable.FemaleYmtPath)?BundleFile(portable.FemaleYmtPath):"";
            portable.MaleMetaPath=File.Exists(portable.MaleMetaPath)?BundleFile(portable.MaleMetaPath):"";
            portable.FemaleMetaPath=File.Exists(portable.FemaleMetaPath)?BundleFile(portable.FemaleMetaPath):"";
            portable.SupplementalYmtPaths=portable.SupplementalYmtPaths.Select(BundleFile).ToList();
            portable.SourceResourcePath="";
            portable.CodeWalkerPath="";
            portable.CodeWalkerCorePath="";
            portable.GtaFolderPath="";
            portable.OutputFolder=Path.Combine("..","..");
            ProjectIO.Save(portable,Path.Combine(projectDir,p.Collection+ProjectFiles.Extension));
            File.WriteAllText(Path.Combine(projectDir,"README.txt"),$"Deployment: {BuildTargets.OutputFolder(options.Target)}/{p.Collection}. See its target README for installation steps.\nOpen {p.Collection}{ProjectFiles.Extension} in Duck Studio to edit or rebuild; Assets contains the original input files, including linked tattoo artwork.\n");
            var backup=target+".weft-backup-"+Guid.NewGuid().ToString("N");
            if(Directory.Exists(target))Directory.Move(target,backup);
            try { Directory.Move(stage,target); } catch { if(Directory.Exists(backup))Directory.Move(backup,target);throw; }
            if(Directory.Exists(backup))Directory.Delete(backup,true);
            return target;
        }
        finally { if(Directory.Exists(stage))Directory.Delete(stage,true); }
    }
    private static string ManifestValue(string s)=>s.Replace("\\","/").Replace("'"," ").Replace('\r',' ').Replace('\n',' ');
    public static void BuildZip(ClothingProject p,string path,BuildOptions? options=null)
    {
        var temp=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,".weft-zip-"+Guid.NewGuid().ToString("N"));
        try{Directory.CreateDirectory(temp);var source=Build(p,temp,options);var zip=Path.Combine(temp,"project.zip");ZipFile.CreateFromDirectory(source,zip,CompressionLevel.Optimal,false);File.Move(zip,path,true);}
        finally{if(Directory.Exists(temp))Directory.Delete(temp,true);}
    }
}



