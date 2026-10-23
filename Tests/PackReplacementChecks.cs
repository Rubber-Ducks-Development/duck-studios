using System.IO;
using DuckStudio;

static class PackReplacementChecks
{
    public static void Run(string root,Action<bool,string> check)
    {
        var folder=Directory.CreateDirectory(Path.Combine(root,"PackReplacement")).FullName;
        var packs=Path.Combine(@"C:\Games\GTAV","update","x64","dlcpacks");
        var dlcs=PackReplacement.Parse([
            (Path.Combine(packs,"mpheist","dlc.rpf"),"mp_m_freemode_01_mp_m_heist.ymt"),
            (Path.Combine(packs,"mpheist","dlc.rpf"),"mp_f_freemode_01_mp_f_heist.ymt"),
            (Path.Combine(packs,"mpheist","dlc.rpf"),"mp_creaturemetadata_heist.ymt"),
            (Path.Combine(packs,"patchday3ng","dlc.rpf"),"mp_m_freemode_01_mp_m_heist.ymt"),
            (Path.Combine(packs,"patchday3ng","dlc.rpf"),"mp_f_freemode_01_mp_f_heist.ymt"),
            (Path.Combine(packs,"mpbeach","dlc.rpf"),"MP_M_FREEMODE_01_MP_M_BEACH.ymt"),
            (Path.Combine(packs,"mpbeach","dlc.rpf"),"mp_m_freemode_01_p_mp_m_beach.ymt")]);
        check(dlcs.Count==2&&dlcs.Any(x=>x==new DlcCollection("mpheist","mp_m_heist","mp_f_heist")),"DLC scan pairs male and female collections per pack and lists patched collections once");
        check(dlcs.Any(x=>x==new DlcCollection("mpbeach","mp_m_beach",""))&&dlcs.All(x=>!x.Male.StartsWith("p_")),"DLC scan keeps single-gender packs and ignores other YMTs");

        var model=Path.Combine(folder,"top.ydd");var texture=Path.Combine(folder,"top.ytd");File.WriteAllBytes(model,[1]);File.WriteAllBytes(texture,[2]);
        ClothingAsset Item(string ped,string slot,int position)=>new(){Name=ped+slot+position,Ped=ped,Slot=slot,Drawable=position,ModelPath=model,TexturePaths=[texture]};
        var project=new ClothingProject{Name="Heist swap",Collection="heist_swap",OutputFolder=folder,Items=[Item("mp_m_freemode_01","jbib",0),Item("mp_m_freemode_01","jbib",1),Item("mp_f_freemode_01","lowr",0),Item("mp_m_freemode_01","p_head",0)]};
        check(PackReplacement.Collection(project,"mp_m_freemode_01")=="mp_m_heist_swap","Add-on packs keep their own collection");
        check(PackBuilder.Manifest(project).Contains("data_file 'SHOP_PED_APPAREL_META_FILE' 'mp_m_freemode_01_mp_m_heist_swap_shop.meta'")&&PackBuilder.Manifest(project).Contains("'mp_f_freemode_01_mp_f_heist_swap_shop.meta'"),"Add-on manifest registers each gender's collection");
        project.PackMode=PackMode.ReplaceDlc;project.ReplaceDlc="mpheist";project.ReplaceMaleCollection="mp_m_heist";project.ReplaceFemaleCollection="mp_f_heist";
        check(!PackBuilder.Validate(project).Any(x=>x.Contains("collection")),"A complete DLC replacement validates");
        var male=PackBuilder.VariationXml(project,"mp_m_freemode_01");
        check(male.Root!.Attribute("name")!.Value=="mp_m_heist"&&male.Root.Element("dlcName")!.Value=="mp_m_heist","Replacement YMT uses the DLC's collection name");
        var shop=PackBuilder.ShopXml(project,"mp_f_freemode_01");
        check(shop.Root!.Element("dlcName")!.Value=="mp_f_heist"&&shop.Root.Element("fullDlcName")!.Value=="mp_f_freemode_01_mp_f_heist","Replacement shop meta uses the DLC's collection name");
        check(FiveMCompanion.Collection(project,"mp_f_freemode_01")=="mp_f_heist","Companion previews the replaced collection");
        var replacementManifest=PackBuilder.Manifest(project);
        check(!replacementManifest.Contains("data_file")&&!replacementManifest.Contains("_shop.meta")&&replacementManifest.Contains("fx_version 'cerulean'"),"Replacement manifest streams over the DLC without registering a second collection");

        project.ReplaceFemaleCollection="";
        check(PackBuilder.Validate(project).Any(x=>x.Contains("female collection to replace")),"Missing collection for a gender in the pack is rejected");
        project.ReplaceFemaleCollection="mp_m_heist";
        check(PackBuilder.Validate(project).Any(x=>x.Contains("other gender"))&&PackBuilder.Validate(project).Any(x=>x.Contains("must be different")),"Cross-gender and duplicate collection names are rejected");
        project.ReplaceFemaleCollection="mp_f_heist";
        check(PackReplacement.TargetIssue(project,BuildTarget.SinglePlayer)!=null&&PackReplacement.TargetIssue(project,BuildTarget.AltV)!=null&&PackReplacement.TargetIssue(project,BuildTarget.FiveM)==null&&PackReplacement.TargetIssue(project,BuildTarget.RawAssets)==null,"Replacement packs are limited to FiveM and raw assets");
        var refused=false;try{PackBuilder.Build(project,folder,new(Target:BuildTarget.SinglePlayer));}catch(InvalidOperationException ex){refused=ex.Message.Contains("Replacement packs");}
        check(refused&&!Directory.Exists(Path.Combine(folder,"heist_swap-project")),"Single-player replacement builds are refused before writing output");

        var saved=Path.Combine(folder,"swap.duckproj");ProjectIO.Save(project,saved);var reopened=ProjectIO.Load(saved);
        check(reopened.PackMode==PackMode.ReplaceDlc&&reopened.ReplaceDlc=="mpheist"&&reopened.ReplaceMaleCollection=="mp_m_heist","Pack mode survives save/reopen");
        check(ProjectComparison.Compare(new ClothingProject(),reopened).Any(x=>x.Change=="PackMode"),"Revision comparison reports pack mode changes");

        var custom=new ClothingProject{Name="Custom",Collection="custom",OutputFolder=folder,PackMode=PackMode.ReplaceCustom,ReplaceMaleCollection="mp_m_other_server",Items=[Item("mp_m_freemode_01","jbib",0)]};
        var window=new BuildWindow(custom);
        check(window.Mode==PackMode.ReplaceCustom&&window.ReplaceMale=="mp_m_other_server"&&window.ReplaceDlc=="","Build dialog restores a custom replacement");
        window.Close();
        var addon=new BuildWindow(new ClothingProject{Name="Addon",Collection="addon",OutputFolder=folder,ReplaceMaleCollection="stale"});
        check(addon.Mode==PackMode.Addon&&addon.ReplaceMale=="","Add-on builds ignore stale replacement names");
        addon.Close();
    }
}
