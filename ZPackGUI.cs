/*
 * ZPackGUI.cs -- WinForms GUI cho ZPackTool (C# 5 / .NET 4.x)
 * Giu nguyen ZPackTool.cs va UclNative.cs, chi them file nay.
 *
 * Build:
 *   csc ZPackGUI.cs ZPackTool.cs UclNative.cs /target:winexe /platform:x64
 *       /main:GuiProgram /r:System.Windows.Forms.dll /r:System.Drawing.dll
 */

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using UclCompression;

// Model trung gian cho GUI (doc lap voi console logic)
class GuiEntry
{
    public uint   Id;
    public string RelativePath;
    public int    OriginalSize;
    public int    CompressSize;
    public byte[] Data;
}

// I/O pack dung lai struct ZPackHeader, ZIndexInfo, ZHash, StructHelper tu ZPackTool.cs
static class GuiPackIO
{
    const byte T_NONE = 0x00, T_UCL = 0x01, T_BZIP2 = 0x02, T_FR = 0x10, T_FR2 = 0x20;

    public static List<GuiEntry> Load(string packFile, out string warning)
    {
        warning = null;
        var result  = new List<GuiEntry>();
        var hashMap = LoadHashList(packFile + ".hashlist.txt");

        using (var fs = new FileStream(packFile, FileMode.Open, FileAccess.Read))
        using (var br = new BinaryReader(fs))
        {
            var hdr = StructHelper.ReadStruct<ZPackHeader>(br);
            if (Encoding.ASCII.GetString(hdr.Signature) != "PACK")
                throw new InvalidDataException("Magic sai, day khong phai file PACK.");

            fs.Seek(hdr.IndexOffset, SeekOrigin.Begin);
            var idx = new ZIndexInfo[hdr.Count];
            for (int i = 0; i < (int)hdr.Count; i++)
                idx[i] = StructHelper.ReadStruct<ZIndexInfo>(br);

            var warns = new List<string>();
            for (int i = 0; i < (int)hdr.Count; i++)
            {
                byte ct  = (byte)((uint)idx[i].CompressSize >> 24);
                int  cl  = idx[i].CompressSize & 0x00FFFFFF;
                int  rl  = (ct == T_NONE) ? idx[i].Size : cl;

                fs.Seek(idx[i].Offset, SeekOrigin.Begin);
                byte[] raw = br.ReadBytes(rl);
                byte[] data;
                try
                {
                    if      (ct == T_NONE)                   data = raw;
                    else if (ct == T_UCL || ct == T_FR || ct == T_FR2)
                                                             data = Ucl.NRV2B_Decompress_8(raw, idx[i].Size);
                    else { warns.Add("Entry #" + i + " kieu 0x" + ct.ToString("X2") + " chua ho tro."); continue; }
                }
                catch (Exception ex) { warns.Add("Entry #" + i + " loi: " + ex.Message); continue; }

                string rel;
                if (!hashMap.TryGetValue(idx[i].Id, out rel)) rel = idx[i].Id.ToString("X8");
                result.Add(new GuiEntry {
                    Id = idx[i].Id, RelativePath = rel.Replace('\\','/'),
                    OriginalSize = idx[i].Size, CompressSize = idx[i].CompressSize, Data = data });
            }
            if (warns.Count > 0) warning = string.Join("\n", warns.ToArray());
        }
        return result;
    }

    public static void Save(string packFile, List<GuiEntry> entries, bool useUcl, bool fr2, int lvl)
    {
        if (lvl < 1) lvl = 1; if (lvl > 10) lvl = 10;
        int n = entries.Count;
        byte[][] pays = new byte[n][]; int[] origs = new int[n]; byte[] cts = new byte[n];
        for (int i = 0; i < n; i++)
        {
            byte[] raw = entries[i].Data ?? new byte[0];
            pays[i] = raw; origs[i] = raw.Length; cts[i] = T_NONE;
            if (useUcl && raw.Length > 0)
                try { byte[] c = Ucl.NRV2B_99_Compress(raw, lvl);
                      if (c.Length < raw.Length) { pays[i] = c; cts[i] = fr2 ? T_FR2 : T_UCL; } }
                catch {}
        }
        int  hs = Marshal.SizeOf(typeof(ZPackHeader));
        int  is2 = Marshal.SizeOf(typeof(ZIndexInfo)) * n;
        uint io = (uint)hs, doff = (uint)(hs + is2);
        using (var fs = new FileStream(packFile, FileMode.Create, FileAccess.ReadWrite))
        using (var bw = new BinaryWriter(fs))
        {
            StructHelper.WriteStruct(bw, new ZPackHeader {
                Signature = Encoding.ASCII.GetBytes("PACK"), Count = (uint)n,
                IndexOffset = io, DataOffset = doff, Crc32 = 0, Reserved = new byte[12] });
            long idxPos = fs.Position;
            var  ia = new ZIndexInfo[n];
            for (int i = 0; i < n; i++) StructHelper.WriteStruct(bw, ia[i]);
            uint cur = doff;
            for (int i = 0; i < n; i++)
            {
                bw.Write(pays[i]);
                ia[i] = new ZIndexInfo { Id = entries[i].Id, Offset = cur, Size = origs[i],
                    CompressSize = (int)(((uint)cts[i] << 24) | ((uint)pays[i].Length & 0x00FFFFFF)) };
                cur += (uint)pays[i].Length;
            }
            fs.Seek(idxPos, SeekOrigin.Begin);
            for (int i = 0; i < n; i++) StructHelper.WriteStruct(bw, ia[i]);
        }
        using (var sw = new StreamWriter(packFile + ".hashlist.txt", false, Encoding.UTF8))
        {
            sw.WriteLine("# ZPackGUI hashlist -- " + Path.GetFileName(packFile));
            sw.WriteLine("# " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  files=" + n);
            sw.WriteLine();
            for (int i = 0; i < n; i++) sw.WriteLine(entries[i].Id.ToString("X8") + " " + entries[i].RelativePath);
        }
    }

    static Dictionary<uint,string> LoadHashList(string path)
    {
        var d = new Dictionary<uint,string>();
        if (!File.Exists(path)) return d;
        try {
            foreach (string ln in File.ReadAllLines(path, Encoding.UTF8)) {
                string t = ln.Trim(); if (t.Length == 0 || t[0] == '#') continue;
                string[] p = t.Split(new char[]{' ','\t'}, 2);
                if (p.Length == 2 && p[0].Length == 8)
                    try { uint h = Convert.ToUInt32(p[0], 16);
                          if (!d.ContainsKey(h)) d[h] = p[1].Trim().Replace('\\','/').TrimStart('/'); }
                    catch {}
            }
        } catch {}
        return d;
    }

    public static string FmtBytes(long b)
    {
        if (b < 1024) return b + " B";
        if (b < 1048576) return (b/1024.0).ToString("F1") + " KB";
        return (b/1048576.0).ToString("F1") + " MB";
    }

    public static string CTypeName(byte t)
    {
        if (t == T_NONE) return "None";    if (t == T_UCL) return "UCL(01)";
        if (t == T_BZIP2) return "BZIP2";  if (t == T_FR)  return "Frame(10)";
        if (t == T_FR2)  return "Frame(20)"; return "0x"+t.ToString("X2");
    }

    public static string RelFrom(string dir, string file)
    {
        string d = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string f = Path.GetFullPath(file);
        return f.StartsWith(d, StringComparison.OrdinalIgnoreCase) ? f.Substring(d.Length) : Path.GetFileName(file);
    }
}

// ── MainForm ─────────────────────────────────────────────────────────────────
class MainForm : Form
{
    TreeView tv; ListView lv; ToolStripStatusLabel lblSt; ImageList imgList;
    string packPath; bool dirty; List<GuiEntry> entries = new List<GuiEntry>();

    public MainForm()
    {
        Text = "ZPack GUI"; Size = new Size(1050, 680); MinimumSize = new Size(700,460);
        StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI", 9f);
        MakeIcons(); BuildMenu(); BuildToolBar(); BuildLayout(); BuildStatusBar(); UpdateTitle();
    }

    void MakeIcons()
    {
        imgList = new ImageList { ImageSize = new Size(16,16), ColorDepth = ColorDepth.Depth32Bit };
        imgList.Images.Add(Icon16(Color.FromArgb(80,180,100)));  // 0=root
        imgList.Images.Add(Icon16(Color.FromArgb(255,200,60)));  // 1=folder
        imgList.Images.Add(Icon16(Color.FromArgb(90,150,220)));  // 2=file
    }
    static Bitmap Icon16(Color c)
    {
        var b = new Bitmap(16,16);
        using (var g = Graphics.FromImage(b)) {
            g.Clear(Color.Transparent);
            using (var br = new SolidBrush(c)) g.FillRectangle(br, 1,2,14,12);
            using (var p = new Pen(Color.FromArgb(80,0,0,0))) g.DrawRectangle(p, 1,2,13,11);
        }
        return b;
    }

    void BuildMenu()
    {
        var ms = new MenuStrip();
        var mF = new ToolStripMenuItem("&File");
        mF.DropDownItems.Add(MI("&Mo pack...  Ctrl+O",     (s,e)=>DoOpen()));
        mF.DropDownItems.Add(MI("&Tao pack moi  Ctrl+N",   (s,e)=>DoNew()));
        mF.DropDownItems.Add(new ToolStripSeparator());
        mF.DropDownItems.Add(MI("&Luu  Ctrl+S",            (s,e)=>DoSave()));
        mF.DropDownItems.Add(MI("Luu &As...",              (s,e)=>DoSaveAs()));
        mF.DropDownItems.Add(new ToolStripSeparator());
        mF.DropDownItems.Add(MI("Giai nen tat ca...",      (s,e)=>DoUnpackAll()));
        mF.DropDownItems.Add(new ToolStripSeparator());
        mF.DropDownItems.Add(MI("Thoat",                   (s,e)=>Close()));
        var mE = new ToolStripMenuItem("&Chinh sua");
        mE.DropDownItems.Add(MI("Them tep...  Ins",        (s,e)=>DoAddFiles()));
        mE.DropDownItems.Add(MI("Them thu muc...",         (s,e)=>DoAddFolder()));
        mE.DropDownItems.Add(new ToolStripSeparator());
        mE.DropDownItems.Add(MI("Thay the tep...",         (s,e)=>DoReplace()));
        mE.DropDownItems.Add(new ToolStripSeparator());
        mE.DropDownItems.Add(MI("Xoa muc chon  Del",       (s,e)=>DoDelete()));
        mE.DropDownItems.Add(new ToolStripSeparator());
        mE.DropDownItems.Add(MI("Giai nen muc chon...",    (s,e)=>DoExtractSel()));
        ms.Items.Add(mF); ms.Items.Add(mE);
        Controls.Add(ms); MainMenuStrip = ms;
        KeyPreview = true;
        KeyDown += (s,e) => {
            if (e.Control && e.KeyCode==Keys.O){DoOpen();e.Handled=true;}
            if (e.Control && e.KeyCode==Keys.N){DoNew();e.Handled=true;}
            if (e.Control && e.KeyCode==Keys.S){DoSave();e.Handled=true;}
            if (e.KeyCode==Keys.Insert){DoAddFiles();e.Handled=true;}
            if (e.KeyCode==Keys.Delete){DoDelete();e.Handled=true;}
        };
    }
    static ToolStripMenuItem MI(string t, EventHandler h)
    { var i=new ToolStripMenuItem(t.Trim()); i.Click+=h; return i; }

    void BuildToolBar()
    {
        var tb = new ToolStrip { GripStyle=ToolStripGripStyle.Hidden, Padding=new Padding(4,2,0,2) };
        tb.Items.Add(TB("Mo",         "Mo file .pack",          (s,e)=>DoOpen()));
        tb.Items.Add(TB("Moi",        "Tao pack rong moi",      (s,e)=>DoNew()));
        tb.Items.Add(TB("Luu",        "Luu pack",               (s,e)=>DoSave()));
        tb.Items.Add(new ToolStripSeparator());
        tb.Items.Add(TB("+ Tep",      "Them tep vao pack",      (s,e)=>DoAddFiles()));
        tb.Items.Add(TB("+ Thu muc",  "Them ca thu muc",        (s,e)=>DoAddFolder()));
        tb.Items.Add(TB("Thay the",   "Thay the tep chon",      (s,e)=>DoReplace()));
        tb.Items.Add(TB("Xoa",        "Xoa muc chon",           (s,e)=>DoDelete()));
        tb.Items.Add(new ToolStripSeparator());
        tb.Items.Add(TB("Giai nen",   "Giai nen muc chon",      (s,e)=>DoExtractSel()));
        tb.Items.Add(TB("Giai nen *", "Giai nen toan bo pack",  (s,e)=>DoUnpackAll()));
        Controls.Add(tb);
    }
    static ToolStripButton TB(string t, string tip, EventHandler h)
    { var b=new ToolStripButton(t){ToolTipText=tip,DisplayStyle=ToolStripItemDisplayStyle.Text}; b.Click+=h; return b; }

    void BuildStatusBar()
    {
        var ss = new StatusStrip();
        lblSt = new ToolStripStatusLabel("San sang") { Spring=true, TextAlign=ContentAlignment.MiddleLeft };
        ss.Items.Add(lblSt); Controls.Add(ss);
    }

    void BuildLayout()
    {
        var sp = new SplitContainer { Dock=DockStyle.Fill, SplitterWidth=5 };
        // --- tree ---
        tv = new TreeView { Dock=DockStyle.Fill, ImageList=imgList, ShowLines=true, HideSelection=false,
                             Font=new Font("Segoe UI",9.5f) };
        tv.AfterSelect += OnTreeSel;
        tv.NodeMouseDoubleClick += (s,e) => { if (e.Node!=null && e.Node.Tag is GuiEntry) DoExtractOne((GuiEntry)e.Node.Tag); };
        tv.KeyDown += (s,e) => { if (e.KeyCode==Keys.Delete){DoDelete();e.Handled=true;} };
        var ctx1 = new ContextMenuStrip();
        ctx1.Items.Add("Them tep...",    null,(s,e)=>DoAddFiles());
        ctx1.Items.Add("Them thu muc...",null,(s,e)=>DoAddFolder());
        ctx1.Items.Add(new ToolStripSeparator());
        ctx1.Items.Add("Thay the...",    null,(s,e)=>DoReplace());
        ctx1.Items.Add(new ToolStripSeparator());
        ctx1.Items.Add("Giai nen...",    null,(s,e)=>DoExtractSel());
        ctx1.Items.Add(new ToolStripSeparator());
        ctx1.Items.Add("Xoa",            null,(s,e)=>DoDelete());
        tv.ContextMenuStrip = ctx1;
        var lblT = new Label { Text="Cay thu muc pack:", Dock=DockStyle.Top,
                               Font=new Font("Segoe UI",8.5f,FontStyle.Bold),
                               Height=22, TextAlign=ContentAlignment.MiddleLeft, Padding=new Padding(4,0,0,0) };
        sp.Panel1.Controls.Add(tv); sp.Panel1.Controls.Add(lblT);

        // --- listview ---
        lv = new ListView { Dock=DockStyle.Fill, View=View.Details, FullRowSelect=true,
                             GridLines=true, MultiSelect=true, HideSelection=false,
                             Font=new Font("Consolas",8.5f) };
        lv.Columns.Add("Ten tep",    200);
        lv.Columns.Add("Duong dan",  230);
        lv.Columns.Add("Kich thuoc", 90,  HorizontalAlignment.Right);
        lv.Columns.Add("Nen",        80,  HorizontalAlignment.Right);
        lv.Columns.Add("Kieu nen",   80,  HorizontalAlignment.Center);
        lv.Columns.Add("Hash ID",   100,  HorizontalAlignment.Center);
        lv.KeyDown += (s,e) => { if (e.KeyCode==Keys.Delete){DoDelete();e.Handled=true;} };
        var ctx2 = new ContextMenuStrip();
        ctx2.Items.Add("Giai nen muc chon...", null,(s,e)=>DoExtractSel());
        ctx2.Items.Add("Thay the tep...",       null,(s,e)=>DoReplace());
        ctx2.Items.Add(new ToolStripSeparator());
        ctx2.Items.Add("Xoa muc chon",         null,(s,e)=>DoDelete());
        lv.ContextMenuStrip = ctx2;
        var lblL = new Label { Text="Chi tiet tep:", Dock=DockStyle.Top,
                               Font=new Font("Segoe UI",8.5f,FontStyle.Bold),
                               Height=22, TextAlign=ContentAlignment.MiddleLeft, Padding=new Padding(4,0,0,0) };
        sp.Panel2.Controls.Add(lv); sp.Panel2.Controls.Add(lblL);
        Controls.Add(sp);
        sp.Panel1MinSize = 180;
        sp.Panel2MinSize = 300;
        Load += (s,e) => {
            int d = Math.Min(320, sp.Width - sp.Panel2MinSize - sp.SplitterWidth - 10);
            if (d > sp.Panel1MinSize) sp.SplitterDistance = d;
        };
    }

    // --- Rebuild tree/list ---
    void RebuildUI()
    {
        tv.BeginUpdate(); tv.Nodes.Clear();
        string pname = packPath != null ? Path.GetFileName(packPath) : "(pack moi)";
        var root = new TreeNode(pname, 0, 0) { Tag="__ROOT__" };
        var dirs = new Dictionary<string,TreeNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            string[] pts = e.RelativePath.Replace('\\','/').Split('/');
            var par = root; string cum = "";
            for (int i = 0; i < pts.Length-1; i++)
            {
                cum = cum.Length==0 ? pts[i] : cum+"/"+pts[i];
                if (!dirs.ContainsKey(cum))
                { var dn=new TreeNode(pts[i],1,1){Tag="__DIR__:"+cum}; par.Nodes.Add(dn); dirs[cum]=dn; }
                par = dirs[cum];
            }
            par.Nodes.Add(new TreeNode(pts[pts.Length-1],2,2){Tag=e});
        }
        tv.Nodes.Add(root); root.Expand(); tv.EndUpdate();
        PopList(entries);
        St("Tong: " + entries.Count + " tep.");
    }

    void PopList(List<GuiEntry> list)
    {
        lv.BeginUpdate(); lv.Items.Clear();
        foreach (var e in list)
        {
            byte ct = (byte)((uint)e.CompressSize >> 24);
            int  cl = e.CompressSize & 0x00FFFFFF;
            var lvi = new ListViewItem(Path.GetFileName(e.RelativePath));
            lvi.SubItems.Add(e.RelativePath);
            lvi.SubItems.Add(GuiPackIO.FmtBytes(e.OriginalSize));
            lvi.SubItems.Add(ct==0 ? "-" : GuiPackIO.FmtBytes(cl));
            lvi.SubItems.Add(GuiPackIO.CTypeName(ct));
            lvi.SubItems.Add("0x"+e.Id.ToString("X8"));
            lvi.Tag = e; lv.Items.Add(lvi);
        }
        lv.EndUpdate();
    }

    void OnTreeSel(object sender, TreeViewEventArgs e)
    {
        if (e.Node==null) return;
        if (e.Node.Tag is GuiEntry) { var ge=(GuiEntry)e.Node.Tag; PopList(new List<GuiEntry>{ge}); }
        else if (e.Node.Tag is string) {
            string tag=(string)e.Node.Tag;
            if (tag=="__ROOT__") PopList(entries);
            else if (tag.StartsWith("__DIR__:")) {
                string pf = tag.Substring("__DIR__:".Length)+"/";
                PopList(entries.FindAll(x=>x.RelativePath.Replace('\\','/').StartsWith(pf,StringComparison.OrdinalIgnoreCase)));
            }
        }
    }

    // --- Actions ---
    void DoOpen()
    {
        if (!OkDiscard()) return;
        using (var d = new OpenFileDialog{Title="Mo file pack",Filter="Pack files (*.pack;*.pak)|*.pack;*.pak|All (*.*)|*.*"})
        {
            if (d.ShowDialog()!=DialogResult.OK) return;
            try {
                string w; entries=GuiPackIO.Load(d.FileName, out w);
                packPath=d.FileName; dirty=false; RebuildUI(); UpdateTitle();
                St("Da mo '"+Path.GetFileName(d.FileName)+"': "+entries.Count+" tep.");
                if (w!=null) MessageBox.Show("Canh bao:\n"+w,"Canh bao",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            } catch(Exception ex){Err("Loi mo pack:\n"+ex.Message);}
        }
    }

    void DoNew()
    {
        if (!OkDiscard()) return;
        packPath=null; dirty=false; entries=new List<GuiEntry>();
        RebuildUI(); UpdateTitle(); St("Pack rong moi. Them tep de bat dau.");
    }

    void DoSave()   { if (packPath==null){DoSaveAs();return;} SaveTo(packPath); }
    void DoSaveAs()
    {
        using (var d = new SaveFileDialog{Title="Luu pack",Filter="Pack (*.pack)|*.pack|Pak (*.pak)|*.pak|All (*.*)|*.*",
                FileName=packPath!=null?Path.GetFileName(packPath):"output.pack"})
        { if (d.ShowDialog()!=DialogResult.OK) return; packPath=d.FileName; SaveTo(packPath); }
    }

    void SaveTo(string path)
    {
        using (var od = new SaveOptsDlg())
        {
            if (od.ShowDialog()!=DialogResult.OK) return;
            try { GuiPackIO.Save(path,entries,od.UseUcl,od.Fr2,od.Level);
                  dirty=false; UpdateTitle(); St("Da luu '"+Path.GetFileName(path)+"' - "+entries.Count+" tep."); }
            catch(Exception ex){Err("Loi luu:\n"+ex.Message);}
        }
    }

    void DoAddFiles()
    {
        using (var d = new OpenFileDialog{Title="Chon tep",Multiselect=true,Filter="All (*.*)|*.*"})
        {
            if (d.ShowDialog()!=DialogResult.OK) return;
            using (var pd = new InputDlg("Tien to duong dan trong pack (de trong = them vao root):",""))
            {
                if (pd.ShowDialog()!=DialogResult.OK) return;
                string pre = pd.Value.Replace('\\','/').Trim('/');
                foreach (string f in d.FileNames)
                    Upsert(string.IsNullOrEmpty(pre)?Path.GetFileName(f):pre+"/"+Path.GetFileName(f), File.ReadAllBytes(f));
                Dirty(); RebuildUI(); St("Da them "+d.FileNames.Length+" tep.");
            }
        }
    }

    void DoAddFolder()
    {
        using (var d = new FolderBrowserDialog{Description="Chon thu muc"})
        {
            if (d.ShowDialog()!=DialogResult.OK) return;
            using (var pd = new InputDlg("Tien to trong pack (de trong = khong co tien to):",""))
            {
                if (pd.ShowDialog()!=DialogResult.OK) return;
                string pre = pd.Value.Replace('\\','/').Trim('/');
                string[] fs2 = Directory.GetFiles(d.SelectedPath,"*",SearchOption.AllDirectories);
                foreach (string f in fs2)
                {
                    string rel = GuiPackIO.RelFrom(d.SelectedPath,f).Replace('\\','/');
                    Upsert(string.IsNullOrEmpty(pre)?rel:pre+"/"+rel, File.ReadAllBytes(f));
                }
                Dirty(); RebuildUI(); St("Da them "+fs2.Length+" tep.");
            }
        }
    }

    void DoReplace()
    {
        var sel=Sel(); if(sel.Count!=1){MessageBox.Show("Chon dung 1 tep de thay the.","TB");return;}
        var e=sel[0];
        using (var d=new OpenFileDialog{Title="Chon tep thay the cho '"+e.RelativePath+"'",Filter="All (*.*)|*.*"})
        {
            if (d.ShowDialog()!=DialogResult.OK) return;
            byte[] data=File.ReadAllBytes(d.FileName);
            e.Data=data; e.OriginalSize=data.Length; e.CompressSize=data.Length;
            Dirty(); RebuildUI(); St("Da thay the '"+e.RelativePath+"'.");
        }
    }

    void DoDelete()
    {
        var sel=Sel(); if(sel.Count==0)return;
        string msg=sel.Count==1?"Xoa '"+sel[0].RelativePath+"' khoi pack?":"Xoa "+sel.Count+" muc?";
        if (MessageBox.Show(msg,"Xac nhan",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes) return;
        foreach (var ge in sel) entries.Remove(ge);
        Dirty(); RebuildUI(); St("Da xoa "+sel.Count+" muc.");
    }

    void DoExtractSel()
    {
        var sel = Sel();
        if (sel.Count==0) {
            var node=tv.SelectedNode;
            if (node!=null && node.Tag is string && ((string)node.Tag).StartsWith("__DIR__:")) {
                string pf=((string)node.Tag).Substring("__DIR__:".Length)+"/";
                sel=entries.FindAll(x=>x.RelativePath.Replace('\\','/').StartsWith(pf,StringComparison.OrdinalIgnoreCase));
            }
        }
        if (sel.Count==0){MessageBox.Show("Chua chon tep nao.","TB");return;}
        using (var d=new FolderBrowserDialog{Description="Chon thu muc dau ra"})
        { if (d.ShowDialog()!=DialogResult.OK) return; Extract(sel,d.SelectedPath); }
    }

    void DoUnpackAll()
    {
        if (entries.Count==0){MessageBox.Show("Pack dang rong.","TB");return;}
        using (var d=new FolderBrowserDialog{Description="Chon thu muc giai nen"})
        { if (d.ShowDialog()!=DialogResult.OK) return; Extract(entries,d.SelectedPath); }
    }

    void DoExtractOne(GuiEntry e)
    {
        using (var d=new SaveFileDialog{Title="Luu tep",FileName=Path.GetFileName(e.RelativePath),Filter="All (*.*)|*.*"})
        { if (d.ShowDialog()!=DialogResult.OK) return;
          File.WriteAllBytes(d.FileName,e.Data??new byte[0]); St("Da luu '"+Path.GetFileName(d.FileName)+"'."); }
    }

    void Extract(List<GuiEntry> list, string outDir)
    {
        int ok=0, fail=0;
        foreach (var e in list) try {
            string safe=e.RelativePath.Replace('/',Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            string dest=Path.Combine(outDir,safe);
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            File.WriteAllBytes(dest,e.Data??new byte[0]); ok++;
        } catch {fail++;}
        string msg="Xong: "+ok+" tep"+(fail>0?", "+fail+" loi.":".");
        St(msg); MessageBox.Show(msg,"Giai nen",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }

    // --- Helpers ---
    void Upsert(string rel, byte[] data)
    {
        rel=rel.Replace('\\','/');
        int i=entries.FindIndex(x=>string.Equals(x.RelativePath,rel,StringComparison.OrdinalIgnoreCase));
        var e=new GuiEntry{Id=ZHash.Hash1(rel),RelativePath=rel,Data=data,OriginalSize=data.Length,CompressSize=data.Length};
        if(i>=0) entries[i]=e; else entries.Add(e);
    }

    List<GuiEntry> Sel()
    {
        var r=new List<GuiEntry>();
        foreach (ListViewItem li in lv.SelectedItems) if(li.Tag is GuiEntry) r.Add((GuiEntry)li.Tag);
        if(r.Count>0) return r;
        if(tv.SelectedNode!=null && tv.SelectedNode.Tag is GuiEntry) r.Add((GuiEntry)tv.SelectedNode.Tag);
        return r;
    }

    void Dirty()       { dirty=true; UpdateTitle(); }
    void UpdateTitle() { Text="ZPack GUI  -  "+(packPath??"(chua luu)")+(dirty?" *":""); }
    void St(string m)  { lblSt.Text=m; }
    void Err(string m) { MessageBox.Show(m,"Loi",MessageBoxButtons.OK,MessageBoxIcon.Error); }

    bool OkDiscard()
    {
        if (!dirty) return true;
        return MessageBox.Show("Co thay doi chua luu. Thoat khong luu?","Xac nhan",
                    MessageBoxButtons.YesNoCancel,MessageBoxIcon.Warning)==DialogResult.Yes;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    { if(!OkDiscard()) e.Cancel=true; base.OnFormClosing(e); }
}

// ── Dialog nhap text ─────────────────────────────────────────────────────────
class InputDlg : Form
{
    TextBox tb;
    public string Value { get { return tb.Text.Trim(); } }
    public InputDlg(string prompt, string def)
    {
        Text="Nhap"; Size=new Size(450,148); FormBorderStyle=FormBorderStyle.FixedDialog;
        StartPosition=FormStartPosition.CenterParent; MinimizeBox=MaximizeBox=false;
        var lbl=new Label{Text=prompt,Left=12,Top=12,Width=418,Height=32};
        tb=new TextBox{Left=12,Top=46,Width=418,Text=def};
        var ok=new Button{Text="OK",  Left=242,Top=78,Width=82,DialogResult=DialogResult.OK};
        var cx=new Button{Text="Huy", Left=332,Top=78,Width=82,DialogResult=DialogResult.Cancel};
        AcceptButton=ok; CancelButton=cx;
        Controls.AddRange(new Control[]{lbl,tb,ok,cx});
    }
}

// ── Dialog tuy chon nen ──────────────────────────────────────────────────────
class SaveOptsDlg : Form
{
    ComboBox cb; TrackBar tk; Label lbl;
    public bool UseUcl {get;private set;} public bool Fr2 {get;private set;} public int Level {get;private set;}
    public SaveOptsDlg()
    {
        Text="Tuy chon luu Pack"; Size=new Size(370,228); FormBorderStyle=FormBorderStyle.FixedDialog;
        StartPosition=FormStartPosition.CenterParent; MinimizeBox=MaximizeBox=false;
        var grp=new GroupBox{Text="Che do nen",Left=12,Top=8,Width=334,Height=148};
        var lm=new Label{Text="Kieu nen:",Left=10,Top=24,AutoSize=true};
        cb=new ComboBox{Left=84,Top=20,Width=236,DropDownStyle=ComboBoxStyle.DropDownList};
        cb.Items.AddRange(new object[]{"Khong nen (None)","UCL NRV2B  (nhan 0x01)","Frame2 NRV2B  (nhan 0x20)"});
        cb.SelectedIndex=0;
        cb.SelectedIndexChanged+=(s,e)=>{bool on=cb.SelectedIndex>0;tk.Enabled=on;lbl.Enabled=on;};
        var ll=new Label{Text="Level UCL (1-10):",Left=10,Top=60,AutoSize=true};
        tk=new TrackBar{Left=10,Top=78,Width=310,Minimum=1,Maximum=10,Value=5,TickFrequency=1,Enabled=false};
        lbl=new Label{Left=10,Top=118,AutoSize=true,Text="Level: 5",Enabled=false};
        tk.ValueChanged+=(s,e)=>lbl.Text="Level: "+tk.Value;
        grp.Controls.AddRange(new Control[]{lm,cb,ll,tk,lbl});
        var ok=new Button{Text="Luu",Left=168,Top=162,Width=84,DialogResult=DialogResult.OK};
        var cx=new Button{Text="Huy",Left=260,Top=162,Width=84,DialogResult=DialogResult.Cancel};
        ok.Click+=(s,e)=>{UseUcl=cb.SelectedIndex>=1;Fr2=cb.SelectedIndex==2;Level=tk.Value;};
        AcceptButton=ok; CancelButton=cx;
        Controls.AddRange(new Control[]{grp,ok,cx});
    }
}

// ── Entry point GUI (ZPackTool.cs cung co class Program nen phai dung /main:GuiProgram) ──
class GuiProgram
{
    [STAThread]
    static void Main()
    {
        try {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        } catch (Exception ex) {
            MessageBox.Show("Loi khoi dong:\n\n" + ex.ToString(), "ZPackGUI Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
