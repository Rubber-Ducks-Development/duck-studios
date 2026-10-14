using System.IO;
using System.Text;
using System.Xml.Linq;

namespace DuckStudio;

public enum BuildTarget { FiveM, AltV, SinglePlayer, RawAssets }

public static class BuildTargets
{
    public static readonly string[] Labels=["FiveM resource","alt:V DLC resource","Single-player DLC (dlc.rpf)","Raw game assets"];
    public static string OutputFolder(BuildTarget target)=>target switch{BuildTarget.AltV=>"AltV",BuildTarget.SinglePlayer=>"SinglePlayer",BuildTarget.RawAssets=>"RawAssets",_=>"FiveM"};
    public static void Export(ClothingProject project,string stage,string fiveMResource,BuildTarget target,string core)
    {
        if(target==BuildTarget.FiveM)return;
        var output=Directory.CreateDirectory(Path.Combine(stage,OutputFolder(target),project.Collection)).FullName;
        foreach(var data in Directory.GetFiles(fiveMResource,"*.json"))File.Copy(data,Path.Combine(output,Path.GetFileName(data)));
        var files=Directory.GetFiles(Path.Combine(fiveMResource,"stream"),"*",SearchOption.AllDirectories);
        var metadata=Directory.GetFiles(fiveMResource).Where(f=>Path.GetExtension(f) is ".meta" or ".xml").ToArray();
        if(target==BuildTarget.RawAssets)
        {
            foreach(var file in files.Concat(metadata))File.Copy(file,Path.Combine(output,Path.GetFileName(file)));
            File.WriteAllText(Path.Combine(output,"README.txt"),"Raw YDD, YTD, YMT and metadata assets. Mount these through your own game/resource tooling. This folder is not a server resource.\n");
        }
        else if(target==BuildTarget.AltV)
        {
            var stream=Directory.CreateDirectory(Path.Combine(output,"stream")).FullName;
            var assets=Directory.CreateDirectory(Path.Combine(stream,"assets")).FullName;
            var archives=new HashSet<string>();
            foreach(var file in files)
            {
                var name=Path.GetFileName(file);var gender=name.StartsWith("mp_f_")?"female":"male";
                var isClothes=name.StartsWith("mp_m_freemode_01")||name.StartsWith("mp_f_freemode_01");
                if(!isClothes){File.Copy(file,Path.Combine(assets,name));continue;}
                var rpfName=project.Collection+"_"+gender+".rpf";archives.Add(rpfName);
                var rpf=Directory.CreateDirectory(Path.Combine(stream,rpfName)).FullName;
                if(name.Contains('^'))
                {
                    var parts=name.Split('^',2);var folder=Directory.CreateDirectory(Path.Combine(rpf,parts[0])).FullName;File.Copy(file,Path.Combine(folder,parts[1]));
                }
                else File.Copy(file,Path.Combine(rpf,name));
            }
            foreach(var file in metadata)File.Copy(file,Path.Combine(stream,Path.GetFileName(file)));
            File.WriteAllText(Path.Combine(output,"resource.toml"),"type = 'dlc'\nmain = 'stream.toml'\nclient-files = [ 'stream/**/*' ]\n");
            var toml=new StringBuilder("files = [\n  'stream/assets/*',\n");
            foreach(var archive in archives.Order())toml.AppendLine($"  'stream/{archive}/*',");
            toml.AppendLine("]\n\n[meta]");
            foreach(var file in metadata)toml.AppendLine($"'stream/{Path.GetFileName(file)}' = '{MetaType(file)}'");
            File.WriteAllText(Path.Combine(output,"stream.toml"),toml.ToString());
            File.WriteAllText(Path.Combine(output,"README.txt"),$"Copy {project.Collection} into your alt:V server resources folder. Add '{project.Collection}' to resources in server.toml. Freemode add-on clothes and tattoo metadata are included.\n");
        }
        else BuildSinglePlayer(project,output,files,metadata,core);
        // The selected platform is the only deployment directory. Project and Assets
        // still keep a portable rebuildable workspace.
        Directory.Delete(Path.Combine(stage,"FiveM"),true);
    }
    public static string MetaType(string path)=>Path.GetFileName(path)=="shop_tattoo.meta"?"TATTOO_SHOP_DLC_FILE":Path.GetExtension(path)==".xml"?"PED_OVERLAY_FILE":"SHOP_PED_APPAREL_META_FILE";
    private static void BuildSinglePlayer(ClothingProject project,string output,string[] files,string[] metadata,string core)
    {
        var tree=Directory.CreateDirectory(Path.Combine(output,".rpf-content")).FullName;
        try
        {
            var pedRoot=Directory.CreateDirectory(Path.Combine(tree,"x64","models","cdimages","peds.rpf")).FullName;
            foreach(var file in files)
            {
                var name=Path.GetFileName(file);
                if(name.Contains('^')) { var parts=name.Split('^',2);var dir=Directory.CreateDirectory(Path.Combine(pedRoot,parts[0])).FullName;File.Copy(file,Path.Combine(dir,parts[1])); }
                else File.Copy(file,Path.Combine(pedRoot,name));
            }
            var data=Directory.CreateDirectory(Path.Combine(tree,"common","data")).FullName;
            foreach(var file in metadata)File.Copy(file,Path.Combine(data,Path.GetFileName(file)));
            string device="dlc_"+project.Collection;
            var registrations=new List<(string Path,string Type)> { ($"{device}:/x64/models/cdimages/peds.rpf","RPF_FILE") };
            registrations.AddRange(metadata.Select(f=>($"{device}:/common/data/{Path.GetFileName(f)}",MetaType(f))));
            var content=new XDocument(new XElement("CDataFileMgr__ContentsOfDataFileXml",new XElement("disabledFiles"),new XElement("includedXmlFiles"),new XElement("includedDataFiles"),new XElement("dataFiles",registrations.Select(r=>new XElement("Item",new XElement("filename",r.Path),new XElement("fileType",r.Type),new XElement("overlay",new XAttribute("value",false)),new XElement("disabled",new XAttribute("value",true)),new XElement("persistent",new XAttribute("value",true))))),new XElement("contentChangeSets",new XElement("Item",new XElement("changeSetName",project.Collection+"_CONTENT"),new XElement("filesToInvalidate"),new XElement("filesToDisable"),new XElement("filesToEnable",registrations.Select(r=>new XElement("Item",r.Path))),new XElement("txdToLoad"),new XElement("txdToUnload"),new XElement("residentResources"),new XElement("unregisterResources"),new XElement("requiresLoadingScreen",new XAttribute("value",true)),new XElement("loadingScreenContext",new XAttribute("value",0)),new XElement("useCacheLoader",new XAttribute("value",false))))));
            content.Save(Path.Combine(tree,"content.xml"));
            new XDocument(new XElement("SSetupData",new XElement("deviceName",device),new XElement("datFile","content.xml"),new XElement("timeStamp","10/13/2026 00:00:00"),new XElement("nameHash",project.Collection),new XElement("contentChangeSetGroups",new XElement("Item",new XElement("NameHash",project.Collection+"_GROUP"),new XElement("ContentChangeSets",new XElement("Item",project.Collection+"_CONTENT")))),new XElement("type","EXTRACONTENT_COMPAT_PACK"),new XElement("order",new XAttribute("value",100)),new XElement("minorOrder",new XAttribute("value",0)),new XElement("isLevelPack",new XAttribute("value",false)))).Save(Path.Combine(tree,"setup2.xml"));
            CodeWalkerPreview.CreateRpfFromDirectory(tree,Path.Combine(output,"dlc.rpf"),core);
            File.WriteAllText(Path.Combine(output,"README.txt"),$"Install for single-player only. Copy this folder to mods/update/x64/dlcpacks/{project.Collection}/ using OpenIV. Add <Item>dlcpacks:/{project.Collection}/</Item> to mods/update/update.rpf/common/data/dlclist.xml. Freemode ped clothing and tattoos require a trainer/script to select the generated collections/presets; this is not a replacement for story character clothes.\n");
        }
        finally{if(Directory.Exists(tree))Directory.Delete(tree,true);}
    }
}
