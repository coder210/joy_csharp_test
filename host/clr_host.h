#ifndef CLR_HOST_H
#define CLR_HOST_H

#include "../context.h"

/* CLR 宿主装配层：把 joy 的 cs 模块（csx_*）按「运行目录 → 运行时 → 程序集 →
 * Host 方法句柄」的顺序装起来，业务的创建/销毁细节不外泄给入口文件。 */

// 把工作目录切到 exe 目录，并把 MONO_LIB_DIR 指向旁边的 lib/
void clr_host_prepare_dirs(void);

// 创建 csx、加载程序集、缓存 JoyGame.Host.OnInit/OnFrame/OnShutdown
bool clr_host_create(Context* ctx);

// 调用 C# Host.OnInit，把窗口、r2d、逻辑宽高、冒烟帧数交给业务层
void clr_host_invoke_init(Context* ctx);

// 每帧调用 C# Host.OnFrame(dt)
void clr_host_invoke_frame(Context* ctx, float dt);

// 调 Host.OnShutdown，再按创建逆序释放方法句柄与运行时
void clr_host_destroy(Context* ctx);

#endif
