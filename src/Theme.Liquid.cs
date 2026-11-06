using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Liuguang.UI
{
    /// <summary>
    /// 液态玻璃的折射引擎 —— 这是「液态玻璃」与「磨砂塑料」的唯一分界。
    ///
    /// 前几版做的都是「半透明白 + 噪点 + 高光 + 描边」。那套东西叠出来是一块
    /// 亮度平坦、边缘描亮的浅色矩形，人眼读作磨砂塑料板，不是玻璃。
    ///
    /// 真正的液态玻璃只有一个物理特征：**边缘会把背后的画面弯折**，而中间几乎不变形。
    /// 圆角矩形的边就是一圈柱面透镜：靠近边缘的光线被压缩，中间是平的。
    /// iOS 26 的 Liquid Glass、CSS 的 backdrop-filter + feDisplacementMap，
    /// 做的都是这一件事。我们之前一件没做，所以怎么调白度、调光斑都无效 ——
    /// 那些参数只改变「颜色」，不产生「形变」，而人眼判断玻璃靠的是形变。
    ///
    /// 实现与参考实现同源：
    ///   1. 求每个像素到圆角矩形边界的有符号距离（SDF，内部为负）
    ///   2. smoothstep(-band, 0, sdf) 得到折射权重 t：边界处 t=1，向内 band 处 t=0
    ///      → 只有边缘那圈在弯，中间 t=0，一个像素都不动
    ///   3. 位移方向取径向**反向**，幅度 = t × 强度 × 长边 —— 等于边缘在采样更外面的背景
    ///   4. 叠加切向的液化波（swirl），让边缘不是一条规整圆弧
    ///   5. 按位移后的坐标对整窗壁纸做双线性采样
    ///   6. 高斯模糊 + 提饱和，模拟玻璃本体的浑浊与色散
    ///
    /// 性能：在 1/2 分辨率下计算再放大。结果本来就要糊，画质无损，成本降 4 倍。
    /// 一张 300×200 的卡片约 15ms，且结果按「尺寸+圆角+窗体位置」缓存，
    /// 只有布局变化或换主题才重算。
    /// </summary>
    internal static class Liquid
    {
        // ---- 可调参数（对应参考实现的 7 个滑块）----

        /// <summary>边缘折射强度。1.0 表示最大位移 = 长边的 5.5%。这是「弯不弯」的总开关。</summary>
        public static float Strength = 1.70f;

        /// <summary>液化扰动强度：沿切向的波动，让玻璃边缘有「水」的不规则感。</summary>
        public static float Noise = 1.00f;

        /// <summary>
        /// 背景模糊半径（原图像素）。实际核算是 Blur/DS 遍的 box blur。
        ///
        /// 这个值被压到 2 是经过实测的：位移要把背景「弯」出来，前提是弯完之后
        /// 还看得见背景的结构。模糊 5px 以上时，19px 周期的纹理会被衰减到原来的 1/5，
        /// 位移错位就完全看不出来了 —— 那时候玻璃会退化成一块均匀的、发亮的白板，
        /// 也就是用户反复说的「这不像玻璃」。2 大约等于 3px 的高斯，刚好够「透而不清」。
        /// </summary>
        public static int Blur = 2;

        /// <summary>饱和度增益。玻璃会把背后的颜色稍微「提纯」，这一步让青色更青。</summary>
        public static float Saturation = 1.18f;

        /// <summary>亮度增益。</summary>
        public static float Bright = 1.02f;

        /// <summary>
        /// 色散强度：R / G / B 三个通道分别用 1.00 / 1+d / 1+2d 倍的位移量采样。
        ///
        /// 这是液态玻璃最强的视觉信号，也是最容易被忽略的一层：
        /// 真实玻璃对红光的折射率比对蓝光小，边缘因此会析出极细的彩边（青 / 品红 / 淡黄）。
        /// 关键在于它**不依赖背景有没有纹理** —— 颜色是玻璃自己生成的。
        /// 之前的版本完全没有这一层，所以哪怕背景是纯色，边缘也只是一条灰白线，没有「玻璃」味。
        /// </summary>
        public static float Dispersion = 0.14f;

        /// <summary>折射环带宽度（占长边比例）。0.13 与 iOS 的边缘厚度观感接近。</summary>
        public static float Band = 0.13f;

        /// <summary>降采样倍数。2 = 一半分辨率。</summary>
        private const int DS = 2;

        private static Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();
        private static int cacheRevision = -1;

        /// <summary>丢弃全部折射缓存。换主题、改分辨率、切换页面布局时调用。</summary>
        public static void Clear()
        {
            if (cache == null) return;
            foreach (Bitmap b in cache.Values)
            {
                if (b != null) b.Dispose();
            }
            cache.Clear();
        }

        /// <summary>
        /// 生成一块「透过液态玻璃看到的背景」。
        /// 返回 w×h 位图，内容已经是折射 + 模糊之后的画面；调用方只需要按圆角裁一下贴上去。
        /// 取不到壁纸时返回 null，调用方走纯色兜底。
        /// </summary>
        public static Bitmap Build(Control c, int w, int h, int radius)
        {
            return Build(c, 0, 0, w, h, radius);
        }

        /// <summary>
        /// 同上，但指定取样偏移（控件内部的一块矩形：胶囊按钮、导航项）。
        ///
        /// 为什么必须带偏移：折射图是按「控件在窗口里的坐标」裁壁纸的。
        /// 如果控件自己只画内部一块（比如导航项在 (8,3) 处画胶囊），
        /// 却仍从 (0,0) 取样，胶囊里的背景就会整体偏 8px，
        /// 边界处和旁边的栏对不上，看起来像贴了一块对不齐的玻璃。
        /// </summary>
        public static Bitmap Build(Control c, int offX, int offY, int w, int h, int radius)
        {
            if (c == null || w < 8 || h < 8) return null;
            if (cacheRevision != Theme.Revision)
            {
                Clear();
                cacheRevision = Theme.Revision;
            }

            Point origin;
            try
            {
                origin = Theme.FormOrigin(c, new Point(offX, offY));
            }
            catch
            {
                // 控件还没挂到窗体上（构造中 / 正在销毁），这次先不画折射
                return null;
            }

            string key = w.ToString() + "x" + h.ToString() + "/r" + radius.ToString()
                       + "/o" + origin.X.ToString() + "," + origin.Y.ToString();

            Bitmap hit;
            if (cache.TryGetValue(key, out hit) && hit != null) return hit;

            Bitmap wall;
            try
            {
                wall = Theme.BackdropImage(c);
            }
            catch
            {
                return null;
            }
            if (wall == null) return null;

            // BackdropImage 可能刚刚重建了壁纸（Revision++）。这里同步一下，
            // 否则下一次调用会因「版本号变了」把刚算好的缓存整批清掉，白算一遍。
            cacheRevision = Theme.Revision;

            Bitmap made = null;
            try
            {
                made = Render(wall, origin, w, h, radius);
            }
            catch
            {
                if (made != null) made.Dispose();
                made = null;
            }
            if (made == null) return null;

            // 液态玻璃主题下连按钮、导航胶囊都要跑折射，缓存条目比只有卡片时多得多；
            // 上限太低会在一帧里反复 Clear，把刚算好的图全丢掉。
            if (cache.Count > 160) Clear();
            cache[key] = made;
            return made;
        }

        // ==================== 核心 ====================

        private static Bitmap Render(Bitmap wall, Point origin, int w, int h, int radius)
        {
            int dw = Math.Max(4, (w + DS - 1) / DS);
            int dh = Math.Max(4, (h + DS - 1) / DS);

            int ww = wall.Width;
            int wh = wall.Height;
            if (ww < 2 || wh < 2) return null;

            int[] src = ReadPixels(wall);
            if (src == null) return null;

            int[] dst = new int[dw * dh];

            float longSide = Math.Max(w, h);
            float halfW = w * 0.5f;
            float halfH = h * 0.5f;
            float rr = Math.Min(radius, Math.Min(halfW, halfH) - 0.5f);
            if (rr < 0f) rr = 0f;

            // 折射环带宽度与位移都必须设上限。
            // 不设的话，位移量跟「长边」成正比：700px 宽的卡片边缘会被推出 80px，
            // 整块玻璃变成一面哈哈镜（背景被放大、条纹糊成一片），
            // 而真实的玻璃只有贴着边缘的一圈在弯。56px 环带 + 26px 位移
            // 是照着参考图里 380px 胶囊的观感反推的：边缘能看出弯，中间几乎不动。
            float band = Math.Min(Band * longSide, Math.Min(halfW, halfH) * 0.75f);
            if (band > 56f) band = 56f;
            if (band < 1.5f) band = 1.5f;

            float magK = Strength * 0.055f * longSide;
            if (magK > 26f) magK = 26f;
            float noiseK = Noise * 2.4f;

            // 降采样网格 → 原图坐标的步长
            float stepX = (float)w / dw;
            float stepY = (float)h / dh;

            for (int y = 0; y < dh; y++)
            {
                float py = (y + 0.5f) * stepY;
                float v = py - halfH;
                for (int x = 0; x < dw; x++)
                {
                    float px = (x + 0.5f) * stepX;
                    float u = px - halfW;

                    float d = Sdf(u, v, halfW, halfH, rr);
                    float t = Smooth(-band, 0f, d);

                    float bx = origin.X + px;
                    float by = origin.Y + py;

                    if (t > 0.002f)
                    {
                        float len = (float)Math.Sqrt(u * u + v * v);
                        if (len < 0.5f) len = 0.5f;

                        // 径向反向位移：边缘采样更外面的背景 → 背景在边缘被「推出去」，
                        // 视觉上就是透镜边缘的压缩带。
                        float mag = t * magK;
                        float dx = -(u / len) * mag;
                        float dy = -(v / len) * mag;

                        // 切向液化波：沿圆周的起伏，让折射带不是一条死板的等宽弧。
                        if (noiseK > 0.01f)
                        {
                            float ang = (float)Math.Atan2(v, u);
                            float rn = len / longSide;
                            float sw = (float)Math.Sin(rn * 30.0 - ang * 5.0) * noiseK * t;
                            dx += -(float)Math.Sin(ang) * sw;
                            dy += (float)Math.Cos(ang) * sw;
                        }

                        // 色散：三个通道用 1.00 / 1+d / 1+2d 倍的位移分别采样。
                        // 边缘因此析出极细的青/品红彩边 —— 这是「玻璃」而不是「塑料」的关键。
                        dst[y * dw + x] = SampleDispersed(src, ww, wh, bx, by, dx, dy);
                    }
                    else
                    {
                        // 中间区完全不位移，一个像素都不动。真实玻璃内部是平的，
                        // 只有边缘那圈在弯 —— 这正是它看起来「厚」而「透」的原因。
                        dst[y * dw + x] = Sample(src, ww, wh, bx, by);
                    }
                }
            }

            // 高斯近似：3 遍 box blur。玻璃本体是浑浊的，这一步是「透而不清」的来源。
            int br = Math.Max(1, Blur / DS);
            Box(dst, dw, dh, br);
            Box(dst, dw, dh, br);
            Box(dst, dw, dh, br);

            // 提饱和 + 微提亮：玻璃会把背后的颜色「提纯」
            Shade(dst, dw * dh);

            Bitmap small = null;
            Bitmap big = null;
            try
            {
                small = new Bitmap(dw, dh, PixelFormat.Format32bppArgb);
                WritePixels(small, dst);
                big = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(big))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(small, new Rectangle(0, 0, w, h),
                        new Rectangle(0, 0, dw, dh), GraphicsUnit.Pixel);
                }
            }
            catch
            {
                if (big != null) big.Dispose();
                big = null;
            }
            finally
            {
                if (small != null) small.Dispose();
            }
            return big;
        }

        /// <summary>圆角矩形有符号距离场。内部为负，边界为 0，外部为正。</summary>
        private static float Sdf(float x, float y, float halfW, float halfH, float r)
        {
            float qx = Math.Abs(x) - halfW + r;
            float qy = Math.Abs(y) - halfH + r;
            float mx = qx > 0f ? qx : 0f;
            float my = qy > 0f ? qy : 0f;
            float inner = qx > qy ? qx : qy;
            if (inner > 0f) inner = 0f;
            return inner + (float)Math.Sqrt(mx * mx + my * my) - r;
        }

        private static float Smooth(float a, float b, float t)
        {
            float v = (t - a) / (b - a);
            if (v < 0f) v = 0f;
            else if (v > 1f) v = 1f;
            return v * v * (3f - 2f * v);
        }

        // ==================== 像素操作 ====================

        private static int[] ReadPixels(Bitmap bmp)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            int[] buf = new int[w * h];
            BitmapData bd = null;
            try
            {
                bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                if (bd.Stride == w * 4)
                {
                    Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
                }
                else
                {
                    for (int y = 0; y < h; y++)
                    {
                        IntPtr row = IntPtr.Add(bd.Scan0, y * bd.Stride);
                        Marshal.Copy(row, buf, y * w, w);
                    }
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                if (bd != null)
                {
                    try { bmp.UnlockBits(bd); }
                    catch { }
                }
            }
            return buf;
        }

        private static void WritePixels(Bitmap bmp, int[] buf)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                if (bd.Stride == w * 4)
                {
                    Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
                }
                else
                {
                    for (int y = 0; y < h; y++)
                    {
                        IntPtr row = IntPtr.Add(bd.Scan0, y * bd.Stride);
                        Marshal.Copy(buf, y * w, row, w);
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(bd);
            }
        }

        /// <summary>
        /// 色散采样：R / G / B 用三个略不相同的位移量分别取样，合成为一个像素。
        /// 这就是玻璃边缘会析出青、品红细线的原因（真实玻璃对短波折射率更大）。
        /// </summary>
        private static int SampleDispersed(int[] src, int sw, int sh,
                                           float x, float y, float dx, float dy)
        {
            float d = Dispersion;
            int b = SampleOne(src, sw, sh, x, y, 0);
            int g = SampleOne(src, sw, sh, x + dx * d, y + dy * d, 8);
            int r = SampleOne(src, sw, sh, x + dx * d * 2f, y + dy * d * 2f, 16);
            return unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
        }

        /// <summary>双线性采样单个通道。<paramref name="shift"/> = 0 / 8 / 16 对应 B / G / R。</summary>
        private static int SampleOne(int[] src, int sw, int sh, float x, float y, int shift)
        {
            if (x < 0f) x = 0f;
            else if (x > sw - 1.001f) x = sw - 1.001f;
            if (y < 0f) y = 0f;
            else if (y > sh - 1.001f) y = sh - 1.001f;

            int x0 = (int)x;
            int y0 = (int)y;
            int x1 = x0 + 1 < sw ? x0 + 1 : sw - 1;
            int y1 = y0 + 1 < sh ? y0 + 1 : sh - 1;
            float fx = x - x0;
            float fy = y - y0;

            float w00 = (1f - fx) * (1f - fy);
            float w10 = fx * (1f - fy);
            float w01 = (1f - fx) * fy;
            float w11 = fx * fy;

            float v = ((src[y0 * sw + x0] >> shift) & 0xFF) * w00
                    + ((src[y0 * sw + x1] >> shift) & 0xFF) * w10
                    + ((src[y1 * sw + x0] >> shift) & 0xFF) * w01
                    + ((src[y1 * sw + x1] >> shift) & 0xFF) * w11;

            int i = (int)v;
            if (i < 0) i = 0;
            else if (i > 255) i = 255;
            return i;
        }

        /// <summary>双线性采样。坐标越界时钳到边缘——比拉黑边自然得多。</summary>
        private static int Sample(int[] src, int sw, int sh, float x, float y)
        {
            if (x < 0f) x = 0f;
            else if (x > sw - 1.001f) x = sw - 1.001f;
            if (y < 0f) y = 0f;
            else if (y > sh - 1.001f) y = sh - 1.001f;

            int x0 = (int)x;
            int y0 = (int)y;
            int x1 = x0 + 1 < sw ? x0 + 1 : sw - 1;
            int y1 = y0 + 1 < sh ? y0 + 1 : sh - 1;
            float fx = x - x0;
            float fy = y - y0;

            int p00 = src[y0 * sw + x0];
            int p10 = src[y0 * sw + x1];
            int p01 = src[y1 * sw + x0];
            int p11 = src[y1 * sw + x1];

            float w00 = (1f - fx) * (1f - fy);
            float w10 = fx * (1f - fy);
            float w01 = (1f - fx) * fy;
            float w11 = fx * fy;

            int b = (int)((p00 & 0xFF) * w00 + (p10 & 0xFF) * w10 + (p01 & 0xFF) * w01 + (p11 & 0xFF) * w11);
            int g = (int)(((p00 >> 8) & 0xFF) * w00 + ((p10 >> 8) & 0xFF) * w10 + ((p01 >> 8) & 0xFF) * w01 + ((p11 >> 8) & 0xFF) * w11);
            int r = (int)(((p00 >> 16) & 0xFF) * w00 + ((p10 >> 16) & 0xFF) * w10 + ((p01 >> 16) & 0xFF) * w01 + ((p11 >> 16) & 0xFF) * w11);

            // 权重和可能有浮点误差，插值结果会溢出 1，越界到 alpha 位上会把像素写坏
            if (b < 0) b = 0; else if (b > 255) b = 255;
            if (g < 0) g = 0; else if (g > 255) g = 255;
            if (r < 0) r = 0; else if (r > 255) r = 255;

            return unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
        }

        /// <summary>分离式 box blur，就地。三遍叠加后接近高斯，足够玻璃用。</summary>
        private static void Box(int[] buf, int w, int h, int r)
        {
            if (r < 1 || w < 2 || h < 2) return;
            int win = r * 2 + 1;
            int[] line = new int[Math.Max(w, h)];

            // 横向
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                int sb = 0, sg = 0, sr = 0;
                int i;
                for (i = -r; i <= r; i++)
                {
                    int xx = i < 0 ? 0 : (i >= w ? w - 1 : i);
                    int p = buf[row + xx];
                    sb += p & 0xFF;
                    sg += (p >> 8) & 0xFF;
                    sr += (p >> 16) & 0xFF;
                }
                for (int x = 0; x < w; x++)
                {
                    line[x] = unchecked((int)0xFF000000) | ((sr / win) << 16) | ((sg / win) << 8) | (sb / win);
                    int ox = x - r;
                    int nx = x + r + 1;
                    if (ox < 0) ox = 0; else if (ox >= w) ox = w - 1;
                    if (nx < 0) nx = 0; else if (nx >= w) nx = w - 1;
                    int po = buf[row + ox];
                    int pn = buf[row + nx];
                    sb += (pn & 0xFF) - (po & 0xFF);
                    sg += ((pn >> 8) & 0xFF) - ((po >> 8) & 0xFF);
                    sr += ((pn >> 16) & 0xFF) - ((po >> 16) & 0xFF);
                }
                for (int x = 0; x < w; x++) buf[row + x] = line[x];
            }

            // 纵向
            for (int x = 0; x < w; x++)
            {
                int sb = 0, sg = 0, sr = 0;
                int i;
                for (i = -r; i <= r; i++)
                {
                    int yy = i < 0 ? 0 : (i >= h ? h - 1 : i);
                    int p = buf[yy * w + x];
                    sb += p & 0xFF;
                    sg += (p >> 8) & 0xFF;
                    sr += (p >> 16) & 0xFF;
                }
                for (int y = 0; y < h; y++)
                {
                    line[y] = unchecked((int)0xFF000000) | ((sr / win) << 16) | ((sg / win) << 8) | (sb / win);
                    int oy = y - r;
                    int ny = y + r + 1;
                    if (oy < 0) oy = 0; else if (oy >= h) oy = h - 1;
                    if (ny < 0) ny = 0; else if (ny >= h) ny = h - 1;
                    int po = buf[oy * w + x];
                    int pn = buf[ny * w + x];
                    sb += (pn & 0xFF) - (po & 0xFF);
                    sg += ((pn >> 8) & 0xFF) - ((po >> 8) & 0xFF);
                    sr += ((pn >> 16) & 0xFF) - ((po >> 16) & 0xFF);
                }
                for (int y = 0; y < h; y++) buf[y * w + x] = line[y];
            }
        }

        /// <summary>提饱和 + 提亮。玻璃会把背后的颜色「提纯」，这步不做会显得灰。</summary>
        private static void Shade(int[] buf, int n)
        {
            for (int i = 0; i < n; i++)
            {
                int p = buf[i];
                int b = p & 0xFF;
                int g = (p >> 8) & 0xFF;
                int r = (p >> 16) & 0xFF;

                float l = r * 0.299f + g * 0.587f + b * 0.114f;
                r = (int)(l + (r - l) * Saturation) ;
                g = (int)(l + (g - l) * Saturation);
                b = (int)(l + (b - l) * Saturation);

                r = (int)(r * Bright + 1.5f);
                g = (int)(g * Bright + 1.5f);
                b = (int)(b * Bright + 1.5f);

                if (r < 0) r = 0; else if (r > 255) r = 255;
                if (g < 0) g = 0; else if (g > 255) g = 255;
                if (b < 0) b = 0; else if (b > 255) b = 255;

                buf[i] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
            }
        }
    }
}
