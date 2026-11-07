/* ============================================================
 * joy_csharp_test 宿主入口：C++ 侧只负责窗口 / 主循环 / 事件泵 / 渲染帧边界，
 * 业务逻辑全部在 C#（JoyGame.dll）。
 *
 * 每帧：r2d_begin → C# Host.OnFrame(dt) → r2d_end
 * 输入：SDL 事件经 systems/input_system.h 写入 csx 输入桥，C# 通过 Joy.Input 轮询
 * 帧率：正常模式用 SDL_MAIN_CALLBACK_RATE 把迭代固定在 60Hz，dt 取
 *       SDL_GetTicksNS() 的高精度真实间隔，游戏速度不随帧率变化；
 *       冒烟模式不限制迭代速率，只把 dt 固定成 1/60 以保证断言可复现。
 *
 * 参数：
 *   --smoke N       无窗口冒烟：dummy 视频驱动 + 固定步长，跑 N 帧后退出
 *   --assembly PATH 指定 C# 程序集（默认 JoyGame.dll，相对当前目录）
 * ============================================================ */
#define SDL_MAIN_USE_CALLBACKS 1
#include <SDL3/SDL_main.h>
#include <SDL3/SDL.h>
#include <joy/joy.h>

#include <cstdio>
#include <cstdlib>
#include <cstring>

#include "context.h"
#include "host/clr_host.h"
#include "systems/input_system.h"
#include "systems/smoke_system.h"

static Context* ctx;

static int find_arg(int argc, char* argv[], const char* name)
{
    for (int i = 1; i < argc; ++i) {
        if (strcmp(argv[i], name) == 0) return i;
    }
    return -1;
}

static void parse_args(int argc, char* argv[])
{
    int i = find_arg(argc, argv, "--smoke");
    if (i >= 0 && i + 1 < argc) ctx->smoke_frames = atoi(argv[i + 1]);
    i = find_arg(argc, argv, "--assembly");
    if (i >= 0 && i + 1 < argc) ctx->assembly = argv[i + 1];
}

SDL_AppResult SDL_AppInit(void** appstate, int argc, char* argv[])
{
    (void)appstate;

    ctx = new Context();
    parse_args(argc, argv);

    clr_host_prepare_dirs();

    if (ctx->smoke_frames > 0) {
        /* 自检先跑：三端绑定链路都不通就没必要进 CLR 建窗口。
         * dummy 视频 / 软件渲染的 hint 也在里面设，必须早于 create_window。 */
        if (!smoke_selftest_run()) return SDL_APP_FAILURE;
    } else {
        /* r2d_create() 建的 renderer 默认 vsync=0；不设这个 hint 时
         * SDL_AppIterate 会以尽可能快的速度被调用（几百~上千 FPS）。
         * "60" = 每秒迭代 60 次；Windows 走 SDL 的 generic main callbacks，
         * 其 AddHintCallback 会用当前值立即触发一次，因此这里设置有效。 */
        SDL_SetHint(SDL_HINT_MAIN_CALLBACK_RATE, "60");
    }

    ctx->window = create_window(APP_TITLE, APP_W, APP_H);
    if (!ctx->window) {
        fprintf(stderr, "[host] create_window failed: %s\n", SDL_GetError());
        return SDL_APP_FAILURE;
    }
    ctx->r2d = r2d_create(ctx->window);
    if (!ctx->r2d) {
        fprintf(stderr, "[host] r2d_create failed: %s\n", SDL_GetError());
        return SDL_APP_FAILURE;
    }
    r2d_set_blend_mode(ctx->r2d, JR2D_BLEND_BLEND);

    if (!clr_host_create(ctx)) return SDL_APP_FAILURE;
    clr_host_invoke_init(ctx);

    printf("[host] clr up, assembly=%s, smoke=%d\n", ctx->assembly, ctx->smoke_frames);
    fflush(stdout);
    ctx->last_tick_ns = SDL_GetTicksNS();
    return SDL_APP_CONTINUE;
}

SDL_AppResult SDL_AppEvent(void* appstate, SDL_Event* e)
{
    (void)appstate;
    return input_pump_system(ctx, e);
}

SDL_AppResult SDL_AppIterate(void* appstate)
{
    (void)appstate;

    float dt;
    if (ctx->smoke_frames > 0) {
        dt = 1.0f / 60.0f;          /* 冒烟用固定步长，结果可复现 */
    } else {
        /* 纳秒精度且不丢余量：SDL_GetTicks() 只有毫秒精度，帧间隔不足 1ms
         * 时会得到 dt=0；旧代码把 0 兜底成 1/60，等于凭空推进 16.7ms，
         * 帧率越高，游戏内时间被放大得越厉害（敌人/子弹/伤害全部变快）。 */
        Uint64 now = SDL_GetTicksNS();
        dt = (float)((double)(now - ctx->last_tick_ns) / (double)SDL_NS_PER_SECOND);
        ctx->last_tick_ns = now;
        if (dt < 0.0f) dt = 0.0f;   /* dt=0 就是这一帧没有时间推进 */
        if (dt > 0.1f) dt = 0.1f;   /* 断点/窗口拖动后不要一次跳太远 */
    }

    r2d_begin(ctx->r2d);
    clr_host_invoke_frame(ctx, dt);
    r2d_end(ctx->r2d);
    csx_input_end_frame();

    ctx->frame++;
    if (ctx->smoke_frames > 0 && ctx->frame >= ctx->smoke_frames) {
        printf("[host] smoke finished: frames=%d exit=%d\n", ctx->frame, csx_host_exit_code());
        fflush(stdout);
        return csx_host_exit_code() == 0 ? SDL_APP_SUCCESS : SDL_APP_FAILURE;
    }
    if (csx_host_quit_requested()) {
        return csx_host_exit_code() == 0 ? SDL_APP_SUCCESS : SDL_APP_FAILURE;
    }
    return SDL_APP_CONTINUE;
}

void SDL_AppQuit(void* appstate, SDL_AppResult result)
{
    (void)appstate;
    (void)result;

    if (!ctx) return;
    clr_host_destroy(ctx);
    if (ctx->r2d) {
        r2d_destroy(ctx->r2d);
        ctx->r2d = NULL;
    }
    if (ctx->window) {
        destroy_window(ctx->window);
        ctx->window = NULL;
    }
    delete ctx;
    ctx = NULL;
}
