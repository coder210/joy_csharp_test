#ifndef INPUT_SYSTEM_H
#define INPUT_SYSTEM_H

#include "../context.h"

/* SDL 事件 → C# 输入桥。
 * 宿主不解释输入语义，只把原始事件写进 csx 的输入桥，业务层通过 Joy.Input 轮询，
 * 因此 C# 不需要接触 SDL_Event。鼠标坐标先换算到逻辑分辨率，
 * C# 侧拿到的始终是逻辑坐标。 */
inline SDL_AppResult input_pump_system(Context* ctx, const SDL_Event* e)
{
    switch (e->type) {
    case SDL_EVENT_KEY_DOWN:
        csx_input_key((int)e->key.scancode, true);
        break;
    case SDL_EVENT_KEY_UP:
        csx_input_key((int)e->key.scancode, false);
        break;
    case SDL_EVENT_MOUSE_MOTION: {
        vec2_t p = r2d_coordinates_from_window(ctx->r2d, e->motion.x, e->motion.y);
        csx_input_mouse_move(p.x, p.y);
        break;
    }
    case SDL_EVENT_MOUSE_BUTTON_DOWN:
        csx_input_mouse_button((int)e->button.button, true);
        break;
    case SDL_EVENT_MOUSE_BUTTON_UP:
        csx_input_mouse_button((int)e->button.button, false);
        break;
    case SDL_EVENT_MOUSE_WHEEL:
        csx_input_wheel(e->wheel.y);
        break;
    case SDL_EVENT_QUIT:
        return SDL_APP_SUCCESS;
    default:
        break;
    }
    return SDL_APP_CONTINUE;
}

#endif
