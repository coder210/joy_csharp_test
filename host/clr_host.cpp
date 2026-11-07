#include "clr_host.h"

#include <SDL3/SDL.h>
#include <cstdio>

#if defined(_WIN32)
#include <direct.h>
#else
#include <unistd.h>
#endif

/* 把工作目录切到 exe 所在目录：
 * JoyGame.dll 与 assets/ 都按相对路径加载，这样从任意目录启动（双击、
 * 快捷方式、CI 里在仓库根执行）都能找到资源。 */
static void chdir_to_exe_dir(void)
{
    /* SDL3 的 SDL_GetBasePath 返回 const char*，内存由 SDL 内部缓存管理，
     * 不要 SDL_free（否则退出时堆损坏）。 */
    const char* base = SDL_GetBasePath();
    if (!base) return;
#if defined(_WIN32)
    _chdir(base);
#else
    chdir(base);
#endif
}

/* 仓内 CoreCLR 运行时的 BCL 放在 exe 旁的 lib/。
 * joy 的 csx_setup_mono_dirs() 只认系统 Mono 安装路径或 MONO_LIB_DIR，
 * 因此必须在 csx_create() 之前显式把目录指过去。 */
static void setup_clr_dirs(void)
{
    const char* base = SDL_GetBasePath();
    if (!base) return;
    char lib[512];
    snprintf(lib, sizeof(lib), "%slib", base);
#if defined(_WIN32)
    _putenv_s("MONO_LIB_DIR", lib);
#else
    setenv("MONO_LIB_DIR", lib, 1);
#endif
}

void clr_host_prepare_dirs(void)
{
    chdir_to_exe_dir();
    setup_clr_dirs();
}

bool clr_host_create(Context* ctx)
{
    ctx->csx = csx_create();
    if (!ctx->csx) {
        fprintf(stderr, "[host] csx_create failed (CLR runtime unavailable?)\n");
        return false;
    }
    if (!csx_load_assembly(ctx->csx, ctx->assembly)) {
        fprintf(stderr, "[host] load assembly failed: %s\n", ctx->assembly);
        return false;
    }

    ctx->on_init = csx_find_method(ctx->csx, "JoyGame", "Host", "OnInit", 5);
    ctx->on_frame = csx_find_method(ctx->csx, "JoyGame", "Host", "OnFrame", 1);
    ctx->on_shutdown = csx_find_method(ctx->csx, "JoyGame", "Host", "OnShutdown", 0);
    if (!ctx->on_init || !ctx->on_frame) {
        fprintf(stderr, "[host] JoyGame.Host.OnInit/OnFrame not found\n");
        return false;
    }
    return true;
}

void clr_host_invoke_init(Context* ctx)
{
    void* win = (void*)ctx->window;
    void* r2d = (void*)ctx->r2d;
    int w = APP_W;
    int h = APP_H;
    int smoke = ctx->smoke_frames;
    void* args[5] = { &win, &r2d, &w, &h, &smoke };
    csx_invoke_method(ctx->csx, ctx->on_init, args);
}

void clr_host_invoke_frame(Context* ctx, float dt)
{
    void* args[1] = { &dt };
    csx_invoke_method(ctx->csx, ctx->on_frame, args);
}

void clr_host_destroy(Context* ctx)
{
    if (ctx->csx && ctx->on_shutdown) {
        csx_invoke_method(ctx->csx, ctx->on_shutdown, NULL);
    }
    if (ctx->on_init) csx_method_free(ctx->on_init);
    if (ctx->on_frame) csx_method_free(ctx->on_frame);
    if (ctx->on_shutdown) csx_method_free(ctx->on_shutdown);
    ctx->on_init = NULL;
    ctx->on_frame = NULL;
    ctx->on_shutdown = NULL;

    if (ctx->csx) {
        csx_release(ctx->csx);
        ctx->csx = NULL;
    }
}
