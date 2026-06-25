/*
 * ZPackTool.cs — Pack / Unpack ZPackFile  (C# 5 compatible)
 * Ket hop voi UclNative.cs de ho tro nen/giai nen UCL qua ucl.dll
 *
 * Build:  csc ZPackTool.cs UclNative.cs /platform:x64
 * Yeu cau: ucl.dll nam cung thu muc voi ZPackTool.exe
 *
 * Dung:
 *   ZPackTool pack   <thu_muc_nguon> <output.pack> [ucl|frame2|none] [level 1-10]
 *   ZPackTool unpack <file.pack>     <thu_muc_output>
 *   ZPackTool list   <file.pack>
 *
 * Vi du:
 *   ZPackTool pack ./data game.pack           <- mac dinh: khong nen
 *   ZPackTool pack ./data game.pack none      <- khong nen ro rang
 *   ZPackTool pack ./data game.pack ucl       <- nen UCL (label 0x01), level 5 mac dinh
 *   ZPackTool pack ./data game.pack ucl 9     <- nen UCL, level 9
 *   ZPackTool pack ./data game.pack frame2    <- nen NRV2B label 0x20 (script_c.pak style), level 5
 *   ZPackTool pack ./data game.pack frame2 9  <- frame2, level 9
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UclCompression;

// ── Structs ──────────────────────────────────────────────────────────────────

[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct ZPackHeader   // 32 bytes
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public byte[] Signature;
    public uint   Count;
    public uint   IndexOffset;
    public uint   DataOffset;
    public uint   Crc32;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
    public byte[] Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct ZIndexInfo    // 16 bytes
{
    public uint Id;
    public uint Offset;
    public int  Size;
    public int  CompressSize;  // byte cao [31..24] = loai nen, [23..0] = kich thuoc nen
}

// ── Hash1 ────────────────────────────────────────────────────────────────────

static class ZHash
{
    public static uint Hash1(string fileName)
    {
        uint id    = 0;
        int  index = 0;
        foreach (char c in fileName)
        {
            char lower = (c >= 'A' && c <= 'Z') ? (char)(c + ('a' - 'A')) : c;
            id = (uint)(((id + (uint)(++index) * lower) % 0x8000000bU) * 0xffffffefU);
        }
        return id ^ 0x12345678u;
    }
}

// ── Marshal helpers ──────────────────────────────────────────────────────────

static class StructHelper
{
    public static T ReadStruct<T>(BinaryReader br) where T : struct
    {
        int    size  = Marshal.SizeOf(typeof(T));
        byte[] bytes = br.ReadBytes(size);
        if (bytes.Length < size) throw new EndOfStreamException();
        GCHandle h = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try   { return (T)Marshal.PtrToStructure(h.AddrOfPinnedObject(), typeof(T)); }
        finally { h.Free(); }
    }

    public static void WriteStruct<T>(BinaryWriter bw, T value) where T : struct
    {
        int    size  = Marshal.SizeOf(typeof(T));
        byte[] bytes = new byte[size];
        GCHandle h = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try   { Marshal.StructureToPtr(value, h.AddrOfPinnedObject(), false); }
        finally { h.Free(); }
        bw.Write(bytes);
    }
}

// ── Pack ─────────────────────────────────────────────────────────────────────

static class ZPacker
{
    const byte TYPE_NONE   = 0x00;
    const byte TYPE_UCL    = 0x01;
    const byte TYPE_FRAME2 = 0x20;  // NRV2B nguyen khoi, label 0x20 (dung trong script_c.pak)

    public static void Pack(string sourceDir, string outputFile, bool useUcl, int uclLevel, bool useFrame2 = false)
    {
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException("Khong tim thay thu muc: " + sourceDir);

        string[] files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
        Console.WriteLine("Tim thay " + files.Length + " file trong '" + sourceDir + "'");
        string modeLabel = !useUcl ? "Khong nen" : (useFrame2 ? "Frame2/NRV2B (label 0x20, level " + uclLevel + ")" : "UCL NRV2B (label 0x01, level " + uclLevel + ")");
        Console.WriteLine("Che do nen : " + modeLabel);
        Console.WriteLine();

        var relPaths    = new List<string>();
        var dataList    = new List<byte[]>();   // du lieu sau khi nen (hay khong nen)
        var origSizes   = new List<int>();       // kich thuoc goc truoc khi nen
        var compTypes   = new List<byte>();      // loai nen tung file

        foreach (string f in files)
        {
            string rel      = MakeRelativePath(sourceDir, f).Replace('\\', '/');
            byte[] raw      = File.ReadAllBytes(f);
            byte[] payload  = raw;
            byte   compType = TYPE_NONE;

            if (useUcl)
            {
                try
                {
                    byte[] compressed = Ucl.NRV2B_99_Compress(raw, uclLevel);
                    // Chi dung ban nen neu nho hon ban goc
                    if (compressed.Length < raw.Length)
                    {
                        payload  = compressed;
                        compType = useFrame2 ? TYPE_FRAME2 : TYPE_UCL;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  [WARN] Khong the nen '" + rel + "': " + ex.Message + " — luu khong nen.");
                }
            }

            relPaths.Add(rel);
            dataList.Add(payload);
            origSizes.Add(raw.Length);
            compTypes.Add(compType);

            string compLabel = (compType == TYPE_UCL) ? "[UCL]" : (compType == TYPE_FRAME2 ? "[Frame2]" : "[none]");
            Console.WriteLine("  + " + rel +
                              "  goc=" + raw.Length +
                              (compType != TYPE_NONE ? "  nen=" + payload.Length + "  " + compLabel : "  [none]"));
        }

        int  count       = relPaths.Count;
        int  headerSize  = Marshal.SizeOf(typeof(ZPackHeader));
        int  indexSize   = Marshal.SizeOf(typeof(ZIndexInfo)) * count;
        uint indexOffset = (uint)headerSize;
        uint dataOffset  = (uint)(headerSize + indexSize);

        using (var fs = new FileStream(outputFile, FileMode.Create, FileAccess.ReadWrite))
        using (var bw = new BinaryWriter(fs))
        {
            // Header
            var header = new ZPackHeader
            {
                Signature   = Encoding.ASCII.GetBytes("PACK"),
                Count       = (uint)count,
                IndexOffset = indexOffset,
                DataOffset  = dataOffset,
                Crc32       = 0,
                Reserved    = new byte[12]
            };
            StructHelper.WriteStruct(bw, header);

            // Index placeholder
            long indexPos = fs.Position;
            var  indexes  = new ZIndexInfo[count];
            for (int i = 0; i < count; i++)
                StructHelper.WriteStruct(bw, indexes[i]);

            // Data block
            uint cur = dataOffset;
            for (int i = 0; i < count; i++)
            {
                bw.Write(dataList[i]);
                // compress_size: byte cao = loai nen, 3 byte thap = kich thuoc payload
                int compressField = (int)(((uint)compTypes[i] << 24) | ((uint)dataList[i].Length & 0x00FFFFFF));
                indexes[i] = new ZIndexInfo
                {
                    Id           = ZHash.Hash1(relPaths[i]),
                    Offset       = cur,
                    Size         = origSizes[i],
                    CompressSize = compressField
                };
                cur += (uint)dataList[i].Length;
            }

            // Ghi lai index
            fs.Seek(indexPos, SeekOrigin.Begin);
            for (int i = 0; i < count; i++)
                StructHelper.WriteStruct(bw, indexes[i]);

            long total = new FileInfo(outputFile).Length;
            Console.WriteLine("\nDa tao '" + outputFile + "'  (" + total + " bytes)");

            // Thong ke
            int uclCount  = 0;
            int noneCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (compTypes[i] == TYPE_UCL) uclCount++;
                else noneCount++;
            }
            Console.WriteLine("  UCL nen  : " + uclCount + " file");
            Console.WriteLine("  Khong nen: " + noneCount + " file");

            // Ghi hashlist: <output>.hashlist.txt
            // Moi dong: <hash_hex> <duong_dan>
            string hashListPath = outputFile + ".hashlist.txt";
            using (var sw = new StreamWriter(hashListPath, false, Encoding.UTF8))
            {
                sw.WriteLine("# ZPackTool hashlist -- " + Path.GetFileName(outputFile));
                sw.WriteLine("# Tao luc: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sw.WriteLine("# So file: " + count);
                sw.WriteLine("# Dinh dang: <hash_hex> <duong_dan>");
                sw.WriteLine();
                for (int i = 0; i < count; i++)
                    sw.WriteLine(indexes[i].Id.ToString("X8") + " " + relPaths[i]);
            }
            Console.WriteLine("  Hashlist : " + hashListPath);
        }
    }

    static string MakeRelativePath(string fromDir, string toFile)
    {
        string dir  = Path.GetFullPath(fromDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string file = Path.GetFullPath(toFile);
        if (file.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
            return file.Substring(dir.Length);
        return file;
    }
}

// ── Unpack ────────────────────────────────────────────────────────────────────

static class ZUnpacker
{
    const byte TYPE_NONE  = 0x00;
    const byte TYPE_UCL   = 0x01;
    const byte TYPE_BZIP2 = 0x02;
    const byte TYPE_FRAME = 0x10;   // frame-based UCL (ZPackFile.h)
    const byte TYPE_FRAME2= 0x20;   // frame-based UCL variant (thuc te trong file pak)

    // Doc hashlist: moi dong la mot duong dan file, tinh Hash1() -> Dictionary<hash, path>
    static Dictionary<uint, string> LoadHashList(string hashListFile, string encOverride = null)
    {
        var dict = new Dictionary<uint, string>();
        if (string.IsNullOrEmpty(hashListFile) || !File.Exists(hashListFile))
            return dict;

        // Phat hien encoding: UTF-8 BOM -> UTF-8; nguoc lai dung GBK (pho bien nhat voi
        // game Trung Quoc).  Hash1() dung gia tri char (Unicode code point) giong VB AscW(),
        // nen can doc file voi encoding chinh xac de ky tu Hoa phuc hoi dung code point.
        byte[] raw = File.ReadAllBytes(hashListFile);
        Encoding enc;

        // Override thu cong: ZPackTool unpack game.pack out/ hash.txt gbk
        if (!string.IsNullOrEmpty(encOverride))
        {
            try   { enc = Encoding.GetEncoding(encOverride); }
            catch { enc = Encoding.GetEncoding(936); Console.WriteLine("  [WARN] Encoding '" + encOverride + "' khong hop le, dung GBK."); }
        }
        else if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
        {
            enc = Encoding.UTF8;  // UTF-8 BOM
        }
        else if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE)
        {
            enc = Encoding.Unicode;  // UTF-16 LE BOM
        }
        else if (raw.Length >= 2 && raw[0] == 0xFE && raw[1] == 0xFF)
        {
            enc = Encoding.BigEndianUnicode;  // UTF-16 BE BOM
        }
        else
        {
            // Khong co BOM: kiem tra co byte nao khong hop le trong UTF-8 khong
            // bang cach dung DecoderFallback strict
            bool isStrictUtf8 = true;
            try
            {
                var strictUtf8 = new UTF8Encoding(false, true);  // throwOnInvalidBytes = true
                strictUtf8.GetString(raw);
            }
            catch (DecoderFallbackException) { isStrictUtf8 = false; }

            if (isStrictUtf8)
            {
                // Kiem tra them: neu file co chua nhieu byte trong khoang 0x81-0xFE
                // lien tiep theo byte 0x40-0xFE thi kha nang cao la GBK
                // (GBK lead byte: 0x81-0xFE, trail byte: 0x40-0xFE)
                int gbkPairs = 0;
                for (int i = 0; i < raw.Length - 1; i++)
                {
                    if (raw[i] >= 0x81 && raw[i] <= 0xFE &&
                        raw[i+1] >= 0x40 && raw[i+1] <= 0xFE && raw[i+1] != 0x7F)
                    {
                        gbkPairs++;
                        i++;  // skip trail byte
                    }
                }
                // Neu so cap GBK nhieu -> file la GBK, khong phai UTF-8
                enc = (gbkPairs > 5) ? Encoding.GetEncoding(936) : Encoding.UTF8;
            }
            else
            {
                enc = Encoding.GetEncoding(936);  // GBK
            }
        }
        Console.WriteLine("  Encoding : " + enc.EncodingName);
        string[] lines = enc.GetString(raw).Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

        int loaded = 0, conflict = 0;
        foreach (string lineRaw in lines)
        {
            string line = lineRaw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

            uint   hash = 0;
            string path = null;

            // Phat hien dinh dang:
            // Format moi (tao boi ZPackTool pack): "XXXXXXXX duong/dan/tep"
            // Format cu (hashlist ngoai):          "\duong\dan\tep"
            string[] parts = line.Split(new char[]{' ', '\t'}, 2);
            bool parsed = false;
            if (parts.Length == 2 && parts[0].Length == 8)
            {
                try
                {
                    hash   = Convert.ToUInt32(parts[0], 16);
                    path   = parts[1].Trim().Replace('\\', '/').TrimStart('/');
                    parsed = true;
                }
                catch { parsed = false; }
            }

            if (!parsed)
            {
                // Format path thuan: tinh hash tu noi dung
                path = line.Replace('\\', '/').TrimStart('/');
                hash = ZHash.Hash1(path);
            }
            // satisfy compiler: hash/path always assigned via one of the two branches
            // but C# 5 flow analysis needs explicit init — done above via parsed flag

            if (!dict.ContainsKey(hash))
            {
                dict[hash] = path;
                loaded++;
            }
            else
            {
                conflict++;
            }
        }

        Console.WriteLine("Hashlist : " + hashListFile);
        Console.WriteLine("  Da nap : " + loaded + " muc" +
                          (conflict > 0 ? ",  [WARN] " + conflict + " collision (encoding sai hoac trung hash)" : ""));
        Console.WriteLine();
        return dict;
    }

    public static void Unpack(string packFile, string outputDir, string hashListFile = null, string encOverride = null)
    {
        Dictionary<uint, string> hashMap = LoadHashList(hashListFile, encOverride);
        if (!File.Exists(packFile))
            throw new FileNotFoundException("Khong tim thay file: " + packFile);

        Directory.CreateDirectory(outputDir);

        using (var fs = new FileStream(packFile, FileMode.Open, FileAccess.Read))
        using (var br = new BinaryReader(fs))
        {
            var    header = StructHelper.ReadStruct<ZPackHeader>(br);
            string sig    = Encoding.ASCII.GetString(header.Signature);
            if (sig != "PACK")
                throw new InvalidDataException("Chu ky khong hop le: '" + sig + "'");

            Console.WriteLine("Signature : " + sig);
            Console.WriteLine("So file   : " + header.Count);
            Console.WriteLine("IndexOff  : 0x" + header.IndexOffset.ToString("X8"));
            Console.WriteLine("DataOff   : 0x" + header.DataOffset.ToString("X8"));
            Console.WriteLine();

            fs.Seek(header.IndexOffset, SeekOrigin.Begin);
            var indexes = new ZIndexInfo[header.Count];
            for (int i = 0; i < header.Count; i++)
                indexes[i] = StructHelper.ReadStruct<ZIndexInfo>(br);

            int ok = 0, skipped = 0;
            for (int i = 0; i < header.Count; i++)
            {
                ZIndexInfo idx      = indexes[i];
                byte       compType = (byte)((uint)idx.CompressSize >> 24);
                int        compLen  = idx.CompressSize & 0x00FFFFFF;

                int readLen = (compType == TYPE_NONE) ? idx.Size : compLen;
                fs.Seek(idx.Offset, SeekOrigin.Begin);
                byte[] rawData = br.ReadBytes(readLen);

                byte[] outData;
                try
                {
                    if (compType == TYPE_NONE)
                    {
                        outData = rawData;
                    }
                    else if (compType == TYPE_UCL)
                    {
                        outData = Ucl.NRV2B_Decompress_8(rawData, idx.Size);
                    }
                    else if (compType == TYPE_FRAME || compType == TYPE_FRAME2)
                    {
                        // Frame-based UCL: toan bo payload la UCL NRV2B stream
                        // comp_len bytes -> giai nen ra size bytes
                        outData = Ucl.NRV2B_Decompress_8(rawData, idx.Size);
                    }
                    else if (compType == TYPE_BZIP2)
                    {
                        Console.WriteLine("  [#" + i + "] id=0x" + idx.Id.ToString("X8") + "  BZIP2 — chua ho tro, bo qua.");
                        skipped++;
                        continue;
                    }
                    else
                    {
                        Console.WriteLine("  [#" + i + "] id=0x" + idx.Id.ToString("X8") + "  Loai nen 0x" + compType.ToString("X2") + " — bo qua.");
                        skipped++;
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  [#" + i + "] id=0x" + idx.Id.ToString("X8") + "  LOI: " + ex.Message);
                    skipped++;
                    continue;
                }

                // Xac dinh ten file dau ra tu hashmap (neu co)
                string relPath;
                bool   nameResolved;
                if (hashMap.TryGetValue(idx.Id, out relPath))
                {
                    nameResolved = true;
                }
                else
                {
                    string detectedExt = DetectExtension(outData);
                    relPath      = idx.Id.ToString("X8") + detectedExt;
                    nameResolved = false;
                }

                // Chuan hoa duong dan cho he dieu hanh hien tai
                string safeRel = relPath.Replace('/', Path.DirectorySeparatorChar)
                                        .TrimStart(Path.DirectorySeparatorChar);
                string outPath = Path.Combine(outputDir, safeRel);

                // Tao thu muc cha neu chua ton tai
                string outDir2 = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(outDir2))
                    Directory.CreateDirectory(outDir2);

                File.WriteAllBytes(outPath, outData);
                Console.WriteLine("  [#" + i.ToString("D4") + "] id=0x" + idx.Id.ToString("X8") +
                                  "  size=" + idx.Size +
                                  "  type=" + CompTypeName(compType) +
                                  (nameResolved ? "" : "  [unknown]") +
                                  "  -> " + safeRel);
                ok++;
            }

            Console.WriteLine("\nXong: " + ok + " file xuat ra '" + outputDir + "'" +
                              (skipped > 0 ? ",  " + skipped + " file bi bo qua." : "."));
        }
    }

    public static void List(string packFile, string hashListFile = null, string encOverride = null)
    {
        if (!File.Exists(packFile))
            throw new FileNotFoundException("Khong tim thay file: " + packFile);

        Dictionary<uint, string> hashMap = LoadHashList(hashListFile, encOverride);

        using (var fs = new FileStream(packFile, FileMode.Open, FileAccess.Read))
        using (var br = new BinaryReader(fs))
        {
            var    header = StructHelper.ReadStruct<ZPackHeader>(br);
            string sig    = Encoding.ASCII.GetString(header.Signature);
            if (sig != "PACK")
                throw new InvalidDataException("Chu ky khong hop le: '" + sig + "'");

            Console.WriteLine("File     : " + packFile);
            Console.WriteLine("So entry : " + header.Count);
            bool hasHash = hashMap.Count > 0;
            Console.WriteLine(
                PadRight("#",          6) +
                PadRight("ID (hex)",  12) +
                PadRight("Offset",    12) +
                PadRight("Size goc",  12) +
                PadRight("Nen(byte)", 12) +
                PadRight("Kieu nen",  14) +
                PadRight("Dinh dang", 12) +
                (hasHash ? "Ten file" : ""));
            Console.WriteLine(new string('-', hasHash ? 114 : 86));

            // Doc toan bo index truoc
            fs.Seek(header.IndexOffset, SeekOrigin.Begin);
            var indexes2 = new ZIndexInfo[header.Count];
            for (int i = 0; i < header.Count; i++)
                indexes2[i] = StructHelper.ReadStruct<ZIndexInfo>(br);

            for (int i = 0; i < header.Count; i++)
            {
                ZIndexInfo idx      = indexes2[i];
                byte       compType = (byte)((uint)idx.CompressSize >> 24);
                int        compLen  = idx.CompressSize & 0x00FFFFFF;

                // Nhan dien dinh dang: doc toi da 512 byte dau cua data goc
                string detectedFmt = "?";
                try
                {
                    int readLen = (compType == TYPE_NONE) ? Math.Min(idx.Size, 512)
                                                          : Math.Min(compLen, 512);
                    if (readLen > 0)
                    {
                        fs.Seek(idx.Offset, SeekOrigin.Begin);
                        byte[] peek = br.ReadBytes(readLen);

                        if (compType == TYPE_NONE)
                        {
                            detectedFmt = DetectExtension(peek).TrimStart('.');
                        }
                        else
                        {
                            // Voi UCL/Frame: giai nen toan bo de detect chinh xac
                            try
                            {
                                byte[] full = null;
                                if (compType == TYPE_UCL || compType == TYPE_FRAME || compType == TYPE_FRAME2)
                                {
                                    fs.Seek(idx.Offset, SeekOrigin.Begin);
                                    byte[] compData = br.ReadBytes(compLen);
                                    full = Ucl.NRV2B_Decompress_8(compData, idx.Size);
                                }
                                detectedFmt = DetectExtension(full != null ? full : peek).TrimStart('.');
                            }
                            catch
                            {
                                detectedFmt = DetectExtension(peek).TrimStart('.');
                            }
                        }
                    }
                }
                catch { detectedFmt = "?"; }

                string fileName;
                if (!hashMap.TryGetValue(idx.Id, out fileName))
                    fileName = hasHash ? "[unknown]" : "";

                Console.WriteLine(
                    PadRight(i.ToString(),                      6) +
                    PadRight("0x" + idx.Id.ToString("X8"),     12) +
                    PadRight("0x" + idx.Offset.ToString("X8"), 12) +
                    PadRight(idx.Size.ToString(),               12) +
                    PadRight(compLen.ToString(),                12) +
                    PadRight(CompTypeName(compType),            14) +
                    PadRight(detectedFmt,                       12) +
                    fileName);
            }
        }
    }

    // ── Nhan dien dinh dang file qua magic bytes / noi dung ─────────────────────
    // Tra ve phan mo rong bao gom dau cham, vi du ".lua", ".xml", ".bin"
    static string DetectExtension(byte[] d)
    {
        int len = d.Length;
        if (len == 0) return ".bin";

        // Tien ich: so sanh magic tai offset bat ky
        // ------------------------------------------

        // ── 4-byte magic ────────────────────────────────────────────────────────
        if (len >= 4)
        {
            uint m4 = ((uint)d[0] << 24) | ((uint)d[1] << 16) | ((uint)d[2] << 8) | d[3];

            if (m4 == 0x89504E47) return ".png";   // PNG
            if (m4 == 0x47494638) return ".gif";   // GIF87a / GIF89a
            if (m4 == 0x49492A00 || m4 == 0x4D4D002A) return ".tif"; // TIFF LE/BE
            if (d[0] == 0x42 && d[1] == 0x4D)         return ".bmp";  // BMP 'BM'
            if (m4 == 0x44445320) return ".dds";   // DDS texture
            if (m4 == 0x4F676753) return ".ogg";   // OGG
            if (m4 == 0x664C6143) return ".flac";  // fLaC
            if (m4 == 0x52494646 && len >= 12 &&   // RIFF
                d[8]=='W' && d[9]=='A' && d[10]=='V' && d[11]=='E') return ".wav";
            if (m4 == 0x52494646 && len >= 12 &&
                d[8]=='A' && d[9]=='V' && d[10]=='I' && d[11]==' ') return ".avi";
            if (m4 == 0x504B0304) return ".zip";   // ZIP / JAR / XLSX / DOCX...
            if (m4 == 0x504B0506) return ".zip";   // ZIP empty
            if (m4 == 0x504B0708) return ".zip";
            if (d[0] == 0x1F && d[1] == 0x8B)        return ".gz";    // GZip
            if (m4 == 0x425A6839) return ".bz2";   // BZip2
            if (m4 == 0xFD377A58) return ".xz";    // XZ
            if (m4 == 0x28B52FFD) return ".zst";   // Zstandard
            if (m4 == 0x7F454C46) return ".elf";   // ELF binary
            if (d[0] == 0x4D && d[1] == 0x5A)        return ".exe";   // MZ / PE
            if (m4 == 0xCAFEBABE) return ".class"; // Java class / Mach-O fat
            if (m4 == 0x25504446) return ".pdf";   // %PDF
            if (m4 == 0xD0CF11E0) return ".doc";   // MS-CFB (doc/xls/ppt)
            if (m4 == 0x38425053) return ".psd";   // PSD
            if (m4 == 0x3026B275) return ".wmv";   // WMV / WMA (ASF)
            if (m4 == 0x00000100) return ".ico";   // ICO
            if (m4 == 0x53505200) return ".spr";   // SPR (nhieu game dung 'SPR\0')
            if (d[0]=='S' && d[1]=='P' && d[2]=='R') return ".spr"; // SPR header variant
        }

        // ── 3-byte magic ────────────────────────────────────────────────────────
        if (len >= 3)
        {
            if (d[0]==0xFF && d[1]==0xD8 && d[2]==0xFF) return ".jpg"; // JPEG
            if (d[0]==0xEF && d[1]==0xBB && d[2]==0xBF) // UTF-8 BOM -> text, kiem tra tiep
            {
                // co the la Lua / XML / INI voi BOM
                string inner = TryUtf8(d, 3, Math.Min(len - 3, 512));
                return SniffText(inner);
            }
            if (d[0]==0x49 && d[1]==0x44 && d[2]==0x33) return ".mp3"; // ID3
        }

        // ── 2-byte magic ────────────────────────────────────────────────────────
        if (len >= 2)
        {
            if (d[0]==0xFF && d[1]==0xFB) return ".mp3"; // MP3 frame sync
            if (d[0]==0xFF && d[1]==0xF3) return ".mp3";
            if (d[0]==0xFF && d[1]==0xF2) return ".mp3";
        }

        // ── Kiem tra van ban thuan (ASCII/UTF-8) ────────────────────────────────
        // Lay toi da 512 byte dau de sniffer nhanh
        string head = TryUtf8(d, 0, Math.Min(len, 512));
        if (head != null)
            return SniffText(head);

        return ".bin";
    }

    // Doc len bytes dau thanh string UTF-8, tra ve null neu co byte invalid
    static string TryUtf8(byte[] d, int start, int count)
    {
        try
        {
            var strict = new UTF8Encoding(false, true);
            return strict.GetString(d, start, count);
        }
        catch { return null; }
    }

    // Nhan dang dinh dang van ban tu noi dung
    static string SniffText(string s)
    {
        if (s == null) return ".bin";
        string t = s.TrimStart();
        if (t.Length == 0) return ".txt";

        // XML / HTML
        if (t.StartsWith("<?xml",  StringComparison.OrdinalIgnoreCase)) return ".xml";
        if (t.StartsWith("<html",  StringComparison.OrdinalIgnoreCase)) return ".html";
        if (t.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase)) return ".html";
        if (t.StartsWith("<",      StringComparison.OrdinalIgnoreCase) &&
            t.Contains(">"))                                             return ".xml";

        // Lua
        if (t.StartsWith("--"))                   return ".lua";  // Lua comment
        if (t.StartsWith("local "))               return ".lua";
        if (t.StartsWith("function "))            return ".lua";
        if (t.StartsWith("require"))              return ".lua";
        if (t.Contains("\nlocal ")   ||
            t.Contains("\nfunction "))            return ".lua";

        // INI / CFG
        if (t.StartsWith("[") && t.Contains("]\r") ||
            t.StartsWith("[") && t.Contains("]\n"))  return ".ini";
        if (t.Contains("=") &&
            !t.StartsWith("{"))
        {
            // Phan biet INI vs JSON-like / other
            int eq  = CountChar(t, '=');
            int nl  = CountChar(t, '\n');
            if (eq > 0 && eq >= nl / 2)           return ".ini";
        }

        // JSON
        if (t.StartsWith("{") || t.StartsWith("[")) return ".json";

        // CSV
        if (t.Contains(",") && CountChar(t, '\n') > 1 &&
            CountChar(t, ',') > CountChar(t, '\n'))  return ".csv";

        // Script / shader chung
        if (t.StartsWith("//") || t.StartsWith("/*")) return ".txt";

        // Mac dinh van ban
        return ".txt";
    }

    static int CountChar(string s, char c)
    {
        int n = 0;
        foreach (char ch in s) if (ch == c) n++;
        return n;
    }

    static string CompTypeName(byte t)
    {
        if (t == TYPE_NONE)   return "None";
        if (t == TYPE_UCL)    return "UCL";
        if (t == TYPE_BZIP2)  return "BZIP2";
        if (t == TYPE_FRAME)  return "Frame(0x10)";
        if (t == TYPE_FRAME2) return "Frame(0x20)";
        return "0x" + t.ToString("X2");
    }

    static string PadRight(string s, int width)
    {
        if (s.Length >= width) return s + " ";
        return s + new string(' ', width - s.Length);
    }
}

// ── Main ──────────────────────────────────────────────────────────────────────

class Program
{
    static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("==========================================");
        Console.WriteLine("   ZPackTool -- Pack/Unpack ZPackFile    ");
        Console.WriteLine("==========================================");
        Console.WriteLine();

        if (args.Length < 2) { PrintUsage(); return; }

        string command = args[0].ToLower();
        try
        {
            if (command == "pack")
            {
                if (args.Length < 3) { PrintUsage(); return; }
                string sourceDir  = args[1];
                string outputFile = args[2];
                bool   useUcl     = false;
                bool   useFrame2  = false;
                int    uclLevel   = 5;  // mac dinh level 5

                if (args.Length >= 4)
                {
                    string mode = args[3].ToLower();
                    if (mode == "ucl")         { useUcl = true;  useFrame2 = false; }
                    else if (mode == "frame2") { useUcl = true;  useFrame2 = true;  }
                    else if (mode == "none")   { useUcl = false; useFrame2 = false; }
                    else
                    {
                        Console.WriteLine("Tuy chon nen khong hop le: '" + args[3] + "' (dung 'ucl', 'frame2' hoac 'none')");
                        PrintUsage();
                        return;
                    }
                }

                if (args.Length >= 5)
                {
                    if (!int.TryParse(args[4], out uclLevel) || uclLevel < 1 || uclLevel > 10)
                    {
                        Console.WriteLine("Level UCL phai la so tu 1 den 10.");
                        return;
                    }
                }

                ZPacker.Pack(sourceDir, outputFile, useUcl, uclLevel, useFrame2);
            }
            else if (command == "unpack")
            {
                if (args.Length < 3) { PrintUsage(); return; }
                string hashListFile  = args.Length >= 4 ? args[3] : null;
                string encOverride   = args.Length >= 5 ? args[4] : null;  // e.g. "gbk" "utf-8" "big5"
                ZUnpacker.Unpack(args[1], args[2], hashListFile, encOverride);
            }
            else if (command == "hashtest")
            {
                // ZPackTool hashtest <path>
                // In hash cua path voi nhieu bien the chuan hoa de tim dung dang game dung
                if (args.Length < 2) { Console.WriteLine("Dung: ZPackTool hashtest <path>"); return; }
                string input = args[1];
                string[] variants = new string[]
                {
                    input,
                    input.Replace('\\', '/'),
                    input.Replace('\\', '/').TrimStart('/'),
                    input.Replace('/', '\\'),
                    input.TrimStart('\\').TrimStart('/'),
                    input.ToLower(),
                    input.Replace('\\', '/').TrimStart('/').ToLower(),
                };
                Console.WriteLine("Hash cac bien the cua: " + input);
                Console.WriteLine(new string('-', 60));
                var seen = new System.Collections.Generic.HashSet<string>();
                foreach (string v in variants)
                {
                    if (seen.Contains(v)) continue;
                    seen.Add(v);
                    Console.WriteLine("  0x" + ZHash.Hash1(v).ToString("X8") + "  '" + v + "'");
                }
            }
            else if (command == "dump")
            {
                // ZPackTool dump <file.pack> [so_entry]
                // In hash + offset cua N entry dau de doi chieu voi hashlist
                if (args.Length < 2) { Console.WriteLine("Dung: ZPackTool dump <file.pack> [N]"); return; }
                int n = args.Length >= 3 ? int.Parse(args[2]) : 20;
                using (var fs = new FileStream(args[1], FileMode.Open, FileAccess.Read))
                using (var br = new BinaryReader(fs))
                {
                    var hdr = StructHelper.ReadStruct<ZPackHeader>(br);
                    Console.WriteLine("So entry trong pak: " + hdr.Count);
                    Console.WriteLine("In " + Math.Min(n, (int)hdr.Count) + " entry dau:");
                    Console.WriteLine(new string('-', 40));
                    fs.Seek(hdr.IndexOffset, SeekOrigin.Begin);
                    for (int i = 0; i < Math.Min(n, (int)hdr.Count); i++)
                    {
                        var idx = StructHelper.ReadStruct<ZIndexInfo>(br);
                        Console.WriteLine("  [" + i + "] 0x" + idx.Id.ToString("X8") + "  size=" + idx.Size);
                    }
                }
                // Goi y: lay 1 hash tu dump, tim path tuong ung trong hashlist
                Console.WriteLine("\nGoi y: dung 'ZPackTool hashtest <path>' de kiem tra hash cua 1 path cu the.");
            }
            else if (command == "list")
            {
                string hashListFile  = args.Length >= 3 ? args[2] : null;
                string encOverride   = args.Length >= 4 ? args[3] : null;
                ZUnpacker.List(args[1], hashListFile, encOverride);
            }
            else
            {
                Console.WriteLine("Lenh khong hop le: '" + command + "'");
                PrintUsage();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("\nLoi: " + ex.Message);
            Environment.Exit(1);
        }
    }

    static void PrintUsage()
    {
        Console.WriteLine("Cach dung:");
        Console.WriteLine("  ZPackTool pack   <thu_muc> <output.pack> [none|ucl|frame2] [level]");
        Console.WriteLine("  ZPackTool unpack <file.pack> <thu_muc_output> [hashlist.txt] [encoding]");
        Console.WriteLine("  ZPackTool list   <file.pack> [hashlist.txt] [encoding]");
        Console.WriteLine();
        Console.WriteLine("Vi du:");
        Console.WriteLine("  ZPackTool pack ./_input Script.pak              <- khong nen (mac dinh)");
        Console.WriteLine("  ZPackTool pack ./_input Script.pak none         <- khong nen ro rang");
        Console.WriteLine("  ZPackTool pack ./_input Script.pak ucl          <- UCL label 0x01, level 5 (mac dinh)");
        Console.WriteLine("  ZPackTool pack ./_input Script.pak ucl 9        <- UCL label 0x01, level 9 (nen nhat)");
        Console.WriteLine("  ZPackTool pack ./_input Script.pak frame2        <- Frame2 label 0x20, level 5 (cho script_c.pak)");
        Console.WriteLine("  ZPackTool pack ./_input Script.pak frame2 9     <- Frame2 label 0x20, level 9");
        Console.WriteLine("  ZPackTool unpack ./Data/Script.pak ./_input");
        Console.WriteLine("  ZPackTool unpack ./Data/Script.pak ./_input Script.pak.txt        <- tu dong detect encoding");
        Console.WriteLine("  ZPackTool unpack ./Data/Script.pak ./_input Script.pak.txt gbk    <- ep dung GBK");
        Console.WriteLine("  ZPackTool unpack ./Data/Script.pak ./_input Script.pak.txt utf-8  <- ep dung UTF-8");
        Console.WriteLine("  ZPackTool list   ./Data/Script.pak Script.pak.txt gbk");
        Console.WriteLine();
        Console.WriteLine("Ghi chu:");
        Console.WriteLine("  - Level UCL: 1 (nhanh nhat) -> 10 (nen nhat), mac dinh = 5");
        Console.WriteLine("  - File nao nen UCL lon hon ban goc se tu dong luu khong nen");
        Console.WriteLine("  - Yeu cau: ucl.dll nam cung thu muc voi ZPackTool.exe");
    }
}
