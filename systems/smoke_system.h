#ifndef SMOKE_SYSTEM_H
#define SMOKE_SYSTEM_H

#include <cstdio>
#include <cstring>

#include <SDL3/SDL.h>
#include <joy/api/joy_api.h>

#include "joy_lua.h"
#include "joy_js.h"

/* --smoke 的三端自检：同一份 thunk 注册表在 C / lua / js 上各跑一遍。
 * 只在冒烟模式调用，正常游戏路径完全不经过这里。
 * 返回 false 表示自检失败（原因已打印），调用方据此让进程以非 0 退出。 */
inline bool smoke_selftest_run(void)
{
    /* P1：共享绑定运行层自检（值容器 / 句柄 / 类型校验 / 自动释放）。
     * 只跑纯内存的 lcg / ticks API，依赖 joy.idl 里 status:on 的条目。 */
    const char* api_err = joy_api_selftest();
    if (api_err) {
        fprintf(stderr, "[api] selftest FAILED: %s\n", api_err);
        return false;
    }
    printf("[api] selftest PASS\n");

    /* 同一份注册表在 lua / js 上的端到端验证：调用、句柄创建、参数类型守卫、
     * GC 自动释放。模块关闭时返回 "lua/disabled" / "js/disabled"，视为跳过而非失败。 */
    const char* lua_err = luax_api_selftest();
    if (lua_err && strcmp(lua_err, "lua/disabled") == 0) {
        printf("[api] lua skipped (module off)\n");
    } else if (lua_err) {
        fprintf(stderr, "[api] lua selftest FAILED: %s\n", lua_err);
        return false;
    } else {
        printf("[api] lua selftest PASS\n");
    }

    const char* js_err = jsopen_api_selftest();
    if (js_err && strcmp(js_err, "js/disabled") == 0) {
        printf("[api] js skipped (module off)\n");
    } else if (js_err) {
        fprintf(stderr, "[api] js selftest FAILED: %s\n", js_err);
        return false;
    } else {
        printf("[api] js selftest PASS\n");
    }

    fflush(stdout);

    /* CI 无桌面会话：dummy 视频 + 软件渲染，保证 CreateWindow/Renderer 可用。
     * 冒烟不限制迭代速率，让固定步长的 N 帧尽快跑完。 */
    SDL_SetHint(SDL_HINT_VIDEO_DRIVER, "dummy");
    SDL_SetHint(SDL_HINT_RENDER_DRIVER, "software");
    return true;
}

#endif
