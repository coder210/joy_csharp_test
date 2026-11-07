using System;
using Joy.Api;

namespace JoyGame
{
    /// <summary>
    /// C# 侧的绘制门面，全部走生成链路（Joy.Api.Bindings）。
    ///
    /// 生成链路里 R2dSetColorf 是「设当前色」，ShapeDrawRectangle / ShapeDrawLineF
    /// 都用这个当前色，所以每个绘制函数先设色再画。
    /// 文字宽度测量（jtext_measurex）是 L3 的输出参数，尚未开放，居中用估算；
    /// 文本长度必须传 UTF-8 字节数（jtext_printx 的 len 是字节长度，不是字符数）。
    /// </summary>
    internal static class Painter
    {
        private const int SmallSize = 12;
        private const int BigSize = 22;

        public static void Fill(GameApp app, float x, float y, float w, float h,
                                int r, int g, int b, int a = 255)
        {
            Bindings.R2dSetColorf(app.R2d, r / 255f, g / 255f, b / 255f, a / 255f);
            Bindings.ShapeDrawRectangle(app.R2d, "fill", x, y, w, h);
        }

        public static void Stroke(GameApp app, float x, float y, float w, float h,
                                  int r, int g, int b, int a = 255)
        {
            Bindings.R2dSetColorf(app.R2d, r / 255f, g / 255f, b / 255f, a / 255f);
            Bindings.ShapeDrawRectangle(app.R2d, "line", x, y, w, h);
        }

        public static void Line(GameApp app, float x1, float y1, float x2, float y2,
                                int r, int g, int b, int a = 255)
        {
            Bindings.R2dSetColorf(app.R2d, r / 255f, g / 255f, b / 255f, a / 255f);
            Bindings.ShapeDrawLineF(app.R2d, x1, y1, x2, y2);
        }

        public static void Text(GameApp app, string text, float x, float y,
                                int r = 232, int g = 236, int b = 245)
        {
            Font font = app.Assets.FontSmall;
            if (font == null) return;
            Bindings.JtextPrintx(app.R2d, font, text, Utf8Length(text),
                                 (uint)r, (uint)g, (uint)b, 255u, x, y);
        }

        public static void TextCenter(GameApp app, string text, float cx, float cy, bool big = false,
                                      int r = 232, int g = 236, int b = 245)
        {
            Font font = big ? app.Assets.FontBig : app.Assets.FontSmall;
            if (font == null) return;

            int size = big ? BigSize : SmallSize;
            float w = text.Length * size * 0.58f;
            Bindings.JtextPrintx(app.R2d, font, text, Utf8Length(text),
                                 (uint)r, (uint)g, (uint)b, 255u,
                                 cx - w * 0.5f, cy - size * 0.5f);
        }

        public static int TextWidth(GameApp app, string text)
        {
            if (app.Assets.FontSmall == null) return 0;
            return (int)(text.Length * SmallSize * 0.58f);
        }

        /// <summary>
        /// 以 (cx, cy) 为中心画精灵。
        /// 生成链路里 image_draw1 是 jrect_t* 指针参数（L3，未开放），
        /// 所以统一画成同尺寸色块，与 cpp / lua / js 三份示例的行为一致。
        /// </summary>
        public static void Sprite(GameApp app, float cx, float cy, float size,
                                  int r, int g, int b)
        {
            Fill(app, cx - size * 0.5f, cy - size * 0.5f, size, size, r, g, b);
        }

        /// <summary>UTF-8 字节长度：jtext_printx 的 len 是字节数，中文每字 3 字节。</summary>
        private static int Utf8Length(string text)
        {
            int n = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c < 0x80) n += 1;
                else if (c < 0x800) n += 2;
                else if (char.IsHighSurrogate(c) && i + 1 < text.Length
                         && char.IsLowSurrogate(text[i + 1]))
                {
                    n += 4;
                    i++;
                }
                else n += 3;
            }
            return n;
        }
    }
}
