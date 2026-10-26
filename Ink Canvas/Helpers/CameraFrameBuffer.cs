#pragma warning disable IDE0130 // 命名空间与文件夹结构不匹配（项目约定使用 Ink_Canvas）
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ink_Canvas.Helpers
{
    public sealed class CameraFrameBuffer
    {
        /// <summary>缓冲体积上限，防止异常分辨率导致超大分配（约可容纳 4K BGRA 一帧）。</summary>
        private const long MaxBufferBytes = 64L * 1024 * 1024;

        private byte[] _pixels;
        private int _width;
        private int _height;
        private int _bytesPerPixel; // 源位图每像素字节数（3 或 4）
        private int _stride;        // 目标行字节数（BGRA32，恒为正）
        private WriteableBitmap _bitmap;

        /// <summary>供 Image.Source 复用的位图，尺寸变化时会被替换。</summary>
        public WriteableBitmap Bitmap
        {
            get { return _bitmap; }
        }

        public int Width
        {
            get { return _width; }
        }

        public int Height
        {
            get { return _height; }
        }

        /// <summary>
        /// 确保像素缓冲能容纳指定尺寸与像素格式的帧。尺寸与格式未变时不重新分配，
        /// 因此稳态下每帧零分配。可在任意线程调用。
        /// </summary>
        private bool EnsurePixelBuffer(int width, int height, int bytesPerPixel)
        {
            if (width <= 0 || height <= 0) return false;
            if (bytesPerPixel != 3 && bytesPerPixel != 4) return false;

            if (_pixels != null && _width == width && _height == height && _bytesPerPixel == bytesPerPixel)
            {
                return true;
            }

            long needed = (long)width * 4 * height;
            if (needed <= 0 || needed > MaxBufferBytes) return false;

            _pixels = new byte[needed];
            _width = width;
            _height = height;
            _bytesPerPixel = bytesPerPixel;
            _stride = width * 4;
            return true;
        }

        /// <summary>
        /// 把 System.Drawing 位图的像素拷入可复用缓冲。可在任意线程调用；
        /// 调用方需自行保证位图在此期间不被其它线程释放或修改。
        /// </summary>
        public void CopyFrom(Bitmap frame)
        {
            if (frame == null) return;

            BitmapData data = null;
            try
            {
                var rect = new Rectangle(0, 0, frame.Width, frame.Height);
                data = frame.LockBits(rect, ImageLockMode.ReadOnly, frame.PixelFormat);

                int bpp = System.Drawing.Image.GetPixelFormatSize(data.PixelFormat) / 8;
                if (bpp != 3 && bpp != 4) return;
                if (!EnsurePixelBuffer(frame.Width, frame.Height, bpp)) return;

                byte[] pixels = _pixels;
                int width = _width;
                int height = _height;
                int dstStride = _stride;
                int srcStride = data.Stride;
                bool bottomUp = srcStride < 0;
                int absSrcStride = Math.Abs(srcStride);
                int rowBytes = width * bpp;
                IntPtr scan0 = data.Scan0;

                if (bpp == 4 && !bottomUp && absSrcStride == dstStride)
                {
                    // 最常见路径：32bpp 且行宽一致且为 top-down，整块直拷
                    Marshal.Copy(scan0, pixels, 0, dstStride * height);
                }
                else if (bpp == 4)
                {
                    for (int y = 0; y < height; y++)
                    {
                        int srcRow = bottomUp ? (height - 1 - y) : y;
                        IntPtr src = IntPtr.Add(scan0, srcRow * absSrcStride);
                        Marshal.Copy(src, pixels, y * dstStride, rowBytes);
                    }
                }
                else
                {
                    // 24bpp BGR -> 32bpp BGRA：逐像素扩展，alpha 置 255
                    byte[] row = new byte[absSrcStride];
                    for (int y = 0; y < height; y++)
                    {
                        int srcRow = bottomUp ? (height - 1 - y) : y;
                        IntPtr src = IntPtr.Add(scan0, srcRow * absSrcStride);
                        Marshal.Copy(src, row, 0, absSrcStride);

                        int di = y * dstStride;
                        for (int si = 0; si < rowBytes; si += 3)
                        {
                            pixels[di] = row[si];
                            pixels[di + 1] = row[si + 1];
                            pixels[di + 2] = row[si + 2];
                            pixels[di + 3] = 255;
                            di += 4;
                        }
                    }
                }

                // 32bppRgb 等不含 alpha 的格式，其第 4 字节内容未定义，直接当 BGRA 解释会全透明
                if (bpp == 4 && !System.Drawing.Image.IsAlphaPixelFormat(data.PixelFormat))
                {
                    ForceOpaque(pixels);
                }
            }
            catch
            {
                // 单帧拷贝失败不应影响后续帧，保持上一帧内容
            }
            finally
            {
                if (data != null)
                {
                    try { frame.UnlockBits(data); } catch { }
                }
            }
        }

        private void ForceOpaque(byte[] pixels)
        {
            for (int i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;
            }
        }

        /// <summary>
        /// 把缓冲内容写入 WriteableBitmap。必须在 UI 线程调用。
        /// 尺寸变化时在此重建 WriteableBitmap（仍在 UI 线程，符合 DispatcherObject 线程亲和性）。
        /// </summary>
        public bool Commit(int width, int height)
        {
            byte[] pixels = _pixels;
            if (pixels == null || width <= 0 || height <= 0) return false;
            if (width != _width || height != _height) return false;

            try
            {
                if (_bitmap == null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
                {
                    _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                }

                _bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, _stride, 0);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>释放缓冲与位图，在关闭摄像头或清理画布时调用。</summary>
        public void Reset()
        {
            _pixels = null;
            _bitmap = null;
            _width = 0;
            _height = 0;
            _bytesPerPixel = 0;
            _stride = 0;
        }
    }
}
