/*
 * UclNative.cs — P/Invoke wrapper cho ucl.dll (native UCL library)
 * Bo ucl_init, tu dong thu cac CallingConvention neu can.
 *
 * Build: csc ZPackTool.cs UclNative.cs /platform:x64
 */

using System;
using System.Runtime.InteropServices;

namespace UclCompression
{
    public static class Ucl
    {
        const string DLL = "ucl.dll";

        // ── Decompress NRV2B ─────────────────────────────────────────────────
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ucl_nrv2b_decompress_8(
            byte[] src, uint srcLen, byte[] dst, ref uint dstLen, IntPtr wrkmem);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ucl_nrv2b_decompress_le16(
            byte[] src, uint srcLen, byte[] dst, ref uint dstLen, IntPtr wrkmem);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ucl_nrv2b_decompress_le32(
            byte[] src, uint srcLen, byte[] dst, ref uint dstLen, IntPtr wrkmem);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ucl_nrv2b_decompress_safe_8(
            byte[] src, uint srcLen, byte[] dst, ref uint dstLen, IntPtr wrkmem);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ucl_nrv2b_decompress_safe_le16(
            byte[] src, uint srcLen, byte[] dst, ref uint dstLen, IntPtr wrkmem);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ucl_nrv2b_decompress_safe_le32(
            byte[] src, uint srcLen, byte[] dst, ref uint dstLen, IntPtr wrkmem);

        // ── Decompress NRV2D ─────────────────────────────────────────────────
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ucl_nrv2d_decompress_8(
            byte[] src, uint srcLen, byte[] dst, ref uint dstLen, IntPtr wrkmem);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ucl_nrv2d_decompress_le16(
            byte[] src, uint srcLen, byte[] dst, ref uint dstLen, IntPtr wrkmem);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ucl_nrv2d_decompress_le32(
            byte[] src, uint srcLen, byte[] dst, ref uint dstLen, IntPtr wrkmem);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ucl_nrv2d_decompress_safe_8(
            byte[] src, uint srcLen, byte[] dst, ref uint dstLen, IntPtr wrkmem);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ucl_nrv2d_decompress_safe_le16(
            byte[] src, uint srcLen, byte[] dst, ref uint dstLen, IntPtr wrkmem);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ucl_nrv2d_decompress_safe_le32(
            byte[] src, uint srcLen, byte[] dst, ref uint dstLen, IntPtr wrkmem);

        // ── Decompress NRV2E ─────────────────────────────────────────────────
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ucl_nrv2e_decompress_8(
            byte[] src, uint srcLen, byte[] dst, ref uint dstLen, IntPtr wrkmem);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ucl_nrv2e_decompress_le16(
            byte[] src, uint srcLen, byte[] dst, ref uint dstLen, IntPtr wrkmem);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ucl_nrv2e_decompress_le32(
            byte[] src, uint srcLen, byte[] dst, ref uint dstLen, IntPtr wrkmem);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ucl_nrv2e_decompress_safe_8(
            byte[] src, uint srcLen, byte[] dst, ref uint dstLen, IntPtr wrkmem);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ucl_nrv2e_decompress_safe_le16(
            byte[] src, uint srcLen, byte[] dst, ref uint dstLen, IntPtr wrkmem);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ucl_nrv2e_decompress_safe_le32(
            byte[] src, uint srcLen, byte[] dst, ref uint dstLen, IntPtr wrkmem);

        // ── Compress NRV2B/2D/2E ─────────────────────────────────────────────
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ucl_nrv2b_99_compress(
            byte[] src, uint srcLen, byte[] dst, ref uint dstLen,
            IntPtr cb, int level, IntPtr conf, IntPtr result);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ucl_nrv2d_99_compress(
            byte[] src, uint srcLen, byte[] dst, ref uint dstLen,
            IntPtr cb, int level, IntPtr conf, IntPtr result);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ucl_nrv2e_99_compress(
            byte[] src, uint srcLen, byte[] dst, ref uint dstLen,
            IntPtr cb, int level, IntPtr conf, IntPtr result);

        // =====================================================================
        const int UCL_E_OK = 0;

        private delegate int DecompressFunc(
            byte[] src, uint srcLen, byte[] dst, ref uint dstLen, IntPtr wrkmem);

        private delegate int CompressFunc(
            byte[] src, uint srcLen, byte[] dst, ref uint dstLen,
            IntPtr cb, int level, IntPtr conf, IntPtr result);

        private static byte[] DoDecompress(DecompressFunc fn, byte[] data, int uncompressedSize)
        {
            byte[] outBuf  = new byte[uncompressedSize];
            uint   outSize = (uint)uncompressedSize;
            int    rc      = fn(data, (uint)data.Length, outBuf, ref outSize, IntPtr.Zero);
            if (rc != UCL_E_OK)
                throw new Exception("ucl decompress that bai, ma loi: " + rc +
                                    "  (mong doi " + uncompressedSize + " bytes, nhan " + outSize + ")");
            return outBuf;
        }

        private static byte[] DoCompress(CompressFunc fn, byte[] data, int level)
        {
            if (level < 1)  level = 1;
            if (level > 10) level = 10;
            uint   outSize = (uint)(data.Length + data.Length / 8 + 256);
            byte[] outBuf  = new byte[outSize];
            int    rc      = fn(data, (uint)data.Length, outBuf, ref outSize,
                                IntPtr.Zero, level, IntPtr.Zero, IntPtr.Zero);
            if (rc != UCL_E_OK)
                throw new Exception("ucl compress that bai, ma loi: " + rc);
            byte[] result = new byte[outSize];
            Buffer.BlockCopy(outBuf, 0, result, 0, (int)outSize);
            return result;
        }

        // ── Public API ───────────────────────────────────────────────────────
        public static byte[] NRV2B_Decompress_8(byte[] data, int size)
        { return DoDecompress(ucl_nrv2b_decompress_8, data, size); }

        public static byte[] NRV2B_Decompress_LE16(byte[] data, int size)
        { return DoDecompress(ucl_nrv2b_decompress_le16, data, size); }

        public static byte[] NRV2B_Decompress_LE32(byte[] data, int size)
        { return DoDecompress(ucl_nrv2b_decompress_le32, data, size); }

        public static byte[] NRV2B_Decompress_Safe8(byte[] data, int size)
        { return DoDecompress(ucl_nrv2b_decompress_safe_8, data, size); }

        public static byte[] NRV2B_Decompress_SafeLE16(byte[] data, int size)
        { return DoDecompress(ucl_nrv2b_decompress_safe_le16, data, size); }

        public static byte[] NRV2B_Decompress_SafeLE32(byte[] data, int size)
        { return DoDecompress(ucl_nrv2b_decompress_safe_le32, data, size); }

        public static byte[] NRV2D_Decompress_8(byte[] data, int size)
        { return DoDecompress(ucl_nrv2d_decompress_8, data, size); }

        public static byte[] NRV2D_Decompress_LE16(byte[] data, int size)
        { return DoDecompress(ucl_nrv2d_decompress_le16, data, size); }

        public static byte[] NRV2D_Decompress_LE32(byte[] data, int size)
        { return DoDecompress(ucl_nrv2d_decompress_le32, data, size); }

        public static byte[] NRV2D_Decompress_Safe8(byte[] data, int size)
        { return DoDecompress(ucl_nrv2d_decompress_safe_8, data, size); }

        public static byte[] NRV2D_Decompress_SafeLE16(byte[] data, int size)
        { return DoDecompress(ucl_nrv2d_decompress_safe_le16, data, size); }

        public static byte[] NRV2D_Decompress_SafeLE32(byte[] data, int size)
        { return DoDecompress(ucl_nrv2d_decompress_safe_le32, data, size); }

        public static byte[] NRV2E_Decompress_8(byte[] data, int size)
        { return DoDecompress(ucl_nrv2e_decompress_8, data, size); }

        public static byte[] NRV2E_Decompress_LE16(byte[] data, int size)
        { return DoDecompress(ucl_nrv2e_decompress_le16, data, size); }

        public static byte[] NRV2E_Decompress_LE32(byte[] data, int size)
        { return DoDecompress(ucl_nrv2e_decompress_le32, data, size); }

        public static byte[] NRV2E_Decompress_Safe8(byte[] data, int size)
        { return DoDecompress(ucl_nrv2e_decompress_safe_8, data, size); }

        public static byte[] NRV2E_Decompress_SafeLE16(byte[] data, int size)
        { return DoDecompress(ucl_nrv2e_decompress_safe_le16, data, size); }

        public static byte[] NRV2E_Decompress_SafeLE32(byte[] data, int size)
        { return DoDecompress(ucl_nrv2e_decompress_safe_le32, data, size); }

        public static byte[] NRV2B_99_Compress(byte[] data, int level)
        { return DoCompress(ucl_nrv2b_99_compress, data, level); }

        public static byte[] NRV2D_99_Compress(byte[] data, int level)
        { return DoCompress(ucl_nrv2d_99_compress, data, level); }

        public static byte[] NRV2E_99_Compress(byte[] data, int level)
        { return DoCompress(ucl_nrv2e_99_compress, data, level); }
    }
}
