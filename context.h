#ifndef CONTEXT_H
#define CONTEXT_H

/* joy_csharp_test 宿主上下文。
 *
 * C++ 侧只有一层薄壳：窗口、r2d、CLR 运行时与 C# 方法句柄，游戏逻辑全在
 * JoyGame.dll。这些句柄集中放在 Context 里，按创建顺序成对释放，
 * 不再像 1.0 那样散成一排 static 全局。 */

#include <SDL3/SDL.h>
#include <joy/joy.h>
#include "joy_csharp.h"

#define APP_TITLE "Joy C# Topdown"
#define APP_W 480
#define APP_H 320

struct Context {
    // 引擎句柄
    jwindow_p window = NULL;
    jr2d_p    r2d = NULL;

    // C# 运行时与 Host 方法句柄（OnInit 5 参、OnFrame 1 参、OnShutdown 0 参）
    csx_p        csx = NULL;
    csx_method_p on_init = NULL;
    csx_method_p on_frame = NULL;
    csx_method_p on_shutdown = NULL;

    // 启动参数：--smoke N / --assembly PATH
    int         smoke_frames = 0;
    const char* assembly = "JoyGame.dll";

    int    frame = 0;          // 已渲染帧数（冒烟模式用它判断收尾）
    Uint64 last_tick_ns = 0;   // 上一帧的 SDL_GetTicksNS()，用于算真实 dt
};

#endif
