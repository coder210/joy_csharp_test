using System;
using Joy.Api;

namespace JoyGame
{
    /// <summary>
    /// 运行期资源句柄。
    ///
    /// 字体走生成链路（Joy.Api.Bindings.FontCreate → Joy.Api.Font），句柄由句柄表
    /// 管理，GC 时自动 font_destroy。
    /// 图片没有走：生成链路里 image_draw1 是 jrect_t* 指针参数（L3 未开放），
    /// 拿不到绘制入口，精灵统一退化成纯色矩形（Painter.Sprite 处理）。
    /// </summary>
    internal sealed class Assets
    {
        // 仓内只保留含中文字形的字体。早先随模板分发的 ARIALNB.TTF 及 Arial 系列变体
        // 只有拉丁字形，中文码点查不到字形会落到 .notdef，渲染成豆腐块/乱码，已全部移除。
        // 底层 jfont.c 用 stbtt_InitFont(data, 0)，只稳妥支持 TrueType glyf 轮廓的
        // 单字体 .ttf，不要放 .ttc 字体集合或 CFF 轮廓的 .otf。
        // simsunb.ttf 是宋体扩展汉字补充，不保证覆盖常用字，故不列入 UI 字体候选。
        private static readonly string[] CjkFonts =
        {
            "assets/fonts/simhei.ttf",       // 仓内自带黑体，首选（模板自包含）
            "C:/Windows/Fonts/simhei.ttf",   // 系统兜底
            "C:/Windows/Fonts/Deng.ttf",
        };

        public Font FontSmall { get; private set; }
        public Font FontBig { get; private set; }

        /// <summary>是否成功加载到含中文字形的字体；界面文案据此选择中文或 ASCII 兜底。</summary>
        public bool HasCjkFont { get; private set; }

        public void Load(Jr2d r2d)
        {
            // FontCreate 在底层返回 NULL 时抛 InvalidOperationException（生成代码的约定），
            // 逐个候选 try 一次即可，命中后两个字号共用同一路径。
            foreach (string path in CjkFonts)
            {
                try
                {
                    FontSmall = Bindings.FontCreate(path, 12);
                    FontBig = Bindings.FontCreate(path, 22);
                    HasCjkFont = true;
                    break;
                }
                catch (InvalidOperationException)
                {
                    FontSmall = null;
                    FontBig = null;
                }
            }

            if (FontSmall == null) Joy.Host.log("[warn] no usable font, HUD text disabled");
        }
    }
}
