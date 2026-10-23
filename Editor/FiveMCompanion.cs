using System.IO;
using System.Text.Json;

namespace DuckStudio;

public sealed record GameOutfitPart(string slot,bool prop,int component,string collection,int drawable,int texture,bool hidden);
public sealed record GameOutfit(string id,string name,string ped,List<GameOutfitPart> parts,List<string> tattoos);
public static class FiveMCompanion
{
    public static int Component(string slot)=>slot switch{"p_head"=>0,"p_eyes"=>1,"p_ears"=>2,"p_lwrist"=>6,"p_rwrist"=>7,_=>Array.IndexOf(ProjectIO.Slots,slot)};
    public static string Collection(ClothingProject p,string ped)=>PackReplacement.Collection(p,ped);
    public static int LocalIndex(ClothingProject p,ClothingAsset asset)=>p.Items.Where(x=>x.Ped==asset.Ped&&x.Slot==asset.Slot).OrderBy(x=>x.Drawable).ToList().FindIndex(x=>x.Id==asset.Id);
    public static List<GameOutfit> Outfits(ClothingProject p)=>p.Outfits.Select(o=>new GameOutfit(o.Id,o.Name,o.Ped,o.Entries.Select(e=>
    {
        var asset=p.Items.FirstOrDefault(x=>x.Id==e.AssetId);var prop=e.Slot.StartsWith("p_");
        if(!string.IsNullOrEmpty(e.AssetId)&&asset==null)throw new InvalidDataException("Outfit "+o.Name+" references a removed item. Update the preset before exporting.");
        if(asset!=null&&(asset.Slot!=e.Slot||asset.Ped!=o.Ped||e.Texture<0||e.Texture>=Math.Max(1,asset.TexturePaths.Count)))throw new InvalidDataException("Outfit "+o.Name+" contains an incompatible item or texture.");
        return new GameOutfitPart(e.Slot,prop,Component(e.Slot),asset==null?"":Collection(p,o.Ped),asset==null?e.BaseDrawable:LocalIndex(p,asset),e.Texture,e.Hidden);
    }).ToList(),o.TattooIds.Where(id=>p.Tattoos.Any(t=>t.Id==id)).ToList())).ToList();
    public static void WriteCatalog(ClothingProject p,string folder)
    {
        Directory.CreateDirectory(folder);
        var clothing=p.Items.Select(a=>new{id=a.Id,name=a.Name,ped=a.Ped,slot=a.Slot,prop=a.IsProp,component=Component(a.Slot),collection=Collection(p,a.Ped),drawable=LocalIndex(p,a),textures=Math.Max(1,a.TexturePaths.Count)}).ToList();
        var tattoos=p.Tattoos.Select(t=>new{id=t.Id,name=t.Name,collection=TattooBuilder.Collection(p),male=TattooBuilder.Sexes(t).Contains(false)?TattooBuilder.Preset(t,false):null,female=TattooBuilder.Sexes(t).Contains(true)?TattooBuilder.Preset(t,true):null});
        File.WriteAllText(Path.Combine(folder,"catalog.json"),JsonSerializer.Serialize(new{resource=p.Collection,clothing,tattoos,outfits=Outfits(p)},ProjectIO.Json));
        File.WriteAllText(Path.Combine(folder,"outfits.json"),JsonSerializer.Serialize(Outfits(p),ProjectIO.Json));
    }
    public static void Export(ClothingProject project,string parent)
    {
        var folder=Path.Combine(parent,"duckstudio_test");
        if(Directory.Exists(folder)&&!File.Exists(Path.Combine(folder,"duckstudio-companion.json")))throw new IOException("The companion folder already exists and is not a Duck Studio export.");
        Directory.CreateDirectory(folder);WriteCatalog(project,folder);
        foreach(var name in new[]{"client.lua","index.html"})
        {
            using var input=typeof(FiveMCompanion).Assembly.GetManifestResourceStream("DuckStudio.Companion."+name)??throw new IOException("Companion template is missing.");using var output=File.Create(Path.Combine(folder,name));input.CopyTo(output);
        }
        File.WriteAllText(Path.Combine(folder,"fxmanifest.lua"),"fx_version 'cerulean'\ngame 'gta5'\nui_page 'index.html'\nfiles { 'index.html', 'catalog.json', 'outfits.json' }\nclient_script 'client.lua'\n");
        File.WriteAllText(Path.Combine(folder,"duckstudio-companion.json"),JsonSerializer.Serialize(new{generator="Duck Studio",collection=project.Collection}));
        File.WriteAllText(Path.Combine(folder,"README.txt"),$"Build the FiveM clothing/tattoo resource first. Copy {project.Collection} and duckstudio_test into a development server's resources. Add 'ensure {project.Collection}' followed by 'ensure duckstudio_test' to server.cfg.\nRun /duckstudio to open the browser. Choose clothing, texture, saved outfits, tattoos and exact GTA poses on a local preview PED. Escape/Close deletes the preview PED and camera. The player PED is never changed. Re-export and restart duckstudio_test after rebuilding the pack.\nCollection identifiers come from the build's sorted positions, not the global drawable number.\n");
    }
}
