# joy_csharp_test

joy 引擎的 **C# 脚本模板**：C 侧只留窗口 / 主循环 / 事件泵，**业务逻辑（场景、玩法、渲染调用）全部写在 C#**，通过仓内自带的 .NET 8 CoreCLR 运行时以 internal call 双向绑定。

一个可直接运行的最小可玩模板：菜单 → 战斗（移动/射击/敌人 AI/击杀）→ 死亡结算，并带无窗口冒烟测试与 Windows CI。

## 目录结构

```
main.c                    C 宿主：窗口 + 主循环 + SDL 事件泵；每帧回调 C# Host.OnFrame
CMakeLists.txt            仓库根构建入口（MODULES cs lua js 接入三端绑定模块 + 拷贝运行期产物）

csharp/                   C# 业务工程（程序集 JoyGame.dll，netstandard2.0）
  JoyGame.csproj          通过 ProjectReference 引用 joy 的 Joy.Bindings.csproj
  runtime/                模板运行时骨架
    Host.cs               C 宿主反射入口：OnInit / OnFrame / OnShutdown
    GameApp.cs / IScene.cs              应用外壳 / 场景接口
    InputState.cs / Assets.cs / Painter.cs / Keys.cs   joy API 门面与常量
  game/                   示例玩法
    MenuScene.cs / PlayScene.cs / GameOverScene.cs
    Player.cs / Enemy.cs / Bullet.cs
  test/
    SmokeScript.cs        无窗口冒烟脚本（模拟按键 + 断言）
    ApiBindingSmoke.cs    P1 新绑定链路自检（强类型对象 + 自动释放）

assets/                   模板资源（玩家/敌人精灵、字体）
third_party/joy/          joy 引擎（git submodule，提交号固定在 gitlink 里）
  modules/                可选脚本后端（统一契约：CMakeLists + 公开头 + bindings/ + scripts/ + stub.c）
    lua/ js/ cs/         CMakeLists.txt、joy_<name>.h/.c（宿主）、stub.c
      bindings/*.c        C 侧胶水，把 joy C API 暴露给该脚本语言
      scripts/            语言侧源码（cs 的 *.cs；lua/js 以后同理）
    cs/ 额外               Joy.Bindings.csproj + gen_csclib.js 留在模块根作构建入口
                           （导出 JOY_BINDINGS_CSPROJ，scripts/*.cs 由它编译）
  external/               第三方源码（参与编译）：lua/ lua_cjson/ duktape/ cgltf/ klib/ …
  runtimes/               预编译运行时（不参与编译）
    libs/ include/mono-2.0/   跨平台共享，唯一一份
    windows-x64/              平台原生二进制（后续 android-arm64/ 等）
```

## 模块归属

C# 绑定层（Mono 宿主、internal call 胶水、自动生成的 `Joy.InternalCalls`、手写声明）
全部属于 **joy 子项目**，本仓不再持有任何副本。joy 仓按三类组织：

| 类别 | 位置 | 内容 |
|---|---|---|
| joy 自己写的代码 | 根目录 / `modules/<name>/` | 核心、后端胶水与模块契约 |
| 第三方源码（要编译） | `external/` | Lua VM、duktape、lua_cjson、mongoose、kcp… |
| 预编译二进制（不编译） | `runtimes/` | CLR 原生库、BCL、Mono 头 |

- C 侧：三个后端遵循统一契约 —— 各自的 `CMakeLists.txt` 提供
  `joy_module_<name>`（INTERFACE），源码经 `target_sources(INTERFACE)` 直接编入
  宿主 target（不建库、不产 DLL），关闭时由模块自带 `stub.c` 提供空桩；
  `joy_add.cmake` 的循环不含任何模块名特例。
- C# 侧：joy 的 `modules/cs/Joy.Bindings.csproj` 单独编译成
  `Joy.Bindings.dll`，`JoyGame.csproj` 通过 `ProjectReference` 引用它。所有
  `Joy.*` 声明都在 joy，且必须是 `public` 才能跨程序集可见；本仓只保留玩法与
  运行时骨架。
- 宿主只 `#include "joy_csharp.h"`，注册入口、输入桥、宿主状态全在该头文件。

`JoyGame.csproj` 按职责显式分组，不再用一个 `game\**\*.cs` 一把梭：

| 目录 | 内容 | 归属 |
|---|---|---|
| `csharp/runtime/` | `Host` / `GameApp` / `IScene` 及 `Joy.*` 门面 | 本仓模板骨架 |
| `csharp/game/` | 场景、玩家、敌人、子弹 | 本仓示例玩法 |
| `csharp/test/` | `SmokeScript` | 本仓测试脚本 |

`JoyGame.csproj` 用 `$(JoyCSharpDir)` 定位 joy 的绑定工程（默认
`../third_party/joy/modules/cs`），换用另一份 joy 源码树时可覆盖该属性。

运行时约束：`Joy.Bindings.dll` 必须部署到 CLR 搜索路径 `lib/`（宿主用
`mono_set_dirs` 指定，依赖程序集只从那里解析），只放在 exe 旁会加载失败，
`MONO_PATH` 对这套自定义运行时也无效。CMake 的 post-build 会在「清空并复制
BCL 到 `lib/`」**之后**把绑定 DLL 追加进去，顺序不能颠倒。

## 运行时架构

```
C 宿主 main.c                              C# 业务 JoyGame.dll
  SDL_AppInit   创建窗口 / r2d / CoreCLR ──► Host.OnInit(win, r2d, w, h, smoke)
  SDL_AppEvent  键鼠事件写输入桥        ──►  （C# 通过 Joy.Input 轮询）
  SDL_AppIterate r2d_begin            ──►  Host.OnFrame(dt) ──► 场景 Update/Render
                 r2d_end（present）
  SDL_AppQuit                        ──►  Host.OnShutdown()
```

- C 侧不写任何玩法代码，只负责"帧边界 + 事件泵 + CLR 生命周期"。
- C# 侧通过 internal call 调 joy 的 C API，天然与引擎逻辑同进程、零 IPC。
- 帧回调走 `csx_find_method` 缓存的方法句柄，不做每帧反射查找。
- `csx_register_internal_call("Joy.X::Y", ...)` 按托管类型全名匹配，不关心声明
  位于哪个程序集，因此绑定声明拆到 `Joy.Bindings.dll` 后注册代码无需改动。
- 正常模式用 `SDL_HINT_MAIN_CALLBACK_RATE=60` 把 `SDL_AppIterate` 固定在 60Hz
  （`r2d_create()` 建的 renderer 默认 vsync=0，不设会跑满 CPU），`dt` 取
  `SDL_GetTicksNS()` 的纳秒级真实间隔；C# 侧位移 / 冷却 / 生成计时都乘 `dt`，
  速度与帧率无关。冒烟模式只把 `dt` 固定成 `1/60` 保证断言可复现，不限制迭代速率。

## 依赖

| 依赖 | 说明 |
|---|---|
| joy 引擎 | 仓内 **git 子模块** `third_party/joy`（`git submodule update --init`） |
| 脚本绑定模块 | 同一子模块里的 `modules/{cs,lua,js}`，由 `MODULES cs lua js` 启用；C 侧源码编入宿主，C# 侧由 `Joy.Bindings.csproj` 产出独立程序集，本仓不含副本 |
| C# 运行时 | joy 子项目 `third_party/joy/runtimes/`：根下 `libs/`（.NET 8 BCL）与 `include/mono-2.0/` 跨平台共享；`windows-x64/coreclr.dll` + import lib 等平台原生库按 `JOY_CLR_PLATFORM` 选取，无需安装 Mono |
| .NET SDK 8 | 编译 `Joy.Bindings.dll` 与 `JoyGame.dll`（目标框架 netstandard2.0，由 CoreCLR 加载） |
| CMake ≥ 3.16 + MSVC | 构建宿主 |

`coreclr.dll` 是 `dotnet/runtime` 的自定义构建（CoreCLR + 静态链接 Mono 运行时组件），
同时导出 `coreclr_*` 与 `mono_*` 两套宿主 API，因此 `jcsx.c` 与 `bindings/` 胶水的
`csx_*` 嵌入代码无需改写。CMake 的 `JOY_CLR_ROOT` 默认指向 `runtimes/`，
`JOY_CLR_PLATFORM` 依据 `CMAKE_SYSTEM_NAME` / `ANDROID_ABI` 自动推导
（Windows → `windows-x64`，Android arm64 → `android-arm64`），也可 `-D` 覆盖；
共享的 BCL 与头文件取根，平台原生库取 `<platform>/`，若平台目录另放了
`libs/` 或 `include/` 则优先用平台版。post-build 会把 `coreclr.dll` 和
`libs/`（拷成 exe 旁的 `lib/`）放进输出目录，`main.c` 在 `csx_create()` 前用
`MONO_LIB_DIR` 把 BCL 目录指过去。换成另一套同布局的构建（含
`coreclr.import.lib` / `include/mono-2.0` / `libs`）用 `-DJOY_CLR_ROOT=<目录>`；
换回系统 Mono 则需同时改 CMake 的运行时段与 post-build 拷贝。
Android 的部署策略（原生库进 APK `lib/<abi>/`、BCL 放 assets 再解压）尚未实现，
本次只预留目录与变量。post-build 会先清空
输出目录的 `lib/` 再拷贝 BCL，避免裁剪后残留旧程序集，**最后**追加
`Joy.Bindings.dll`；这个顺序不能颠倒，否则绑定 DLL 会被清空步骤删掉。

`libs/` 只保留 **4 个文件 / 约 4.5 MB** 的语言与数据结构核心：
`System.Private.CoreLib.dll`（基础类型、`List<T>`、`Dictionary<,>`、委托、字符串，
占绝大部分体积，由运行时内部加载），以及 `netstandard.dll` /
`System.Runtime.dll` / `System.Collections.dll` 三个 facade。`JoyGame.dll` 以
netstandard2.0 编译，元数据只引用这三个程序集，逐个移出实测都会导致冒烟失败，
因此这是当前编译目标下的下限。原先附带的 `mscorlib.dll`、`System.dll`、
`System.Core.dll`、`System.AppContext.dll`、`System.Runtime.Extensions.dll`、
`System.Runtime.InteropServices.dll`、
`System.Runtime.CompilerServices.Unsafe.dll`、
`Microsoft.NETCore.App.runtimeconfig.json` 均无引用，已实测删除。
网络（`System.Net.*`）、线程 / Task、IO / Console、反射、Globalization、正则、
Linq、Xml / Json、数据、序列化、加密、VB / C# 动态等全部移除；
`Microsoft.NETCore.App.deps.json` 也已移除（实测运行时不依赖）。
运行时根目录那份重复的 `System.Private.CoreLib.dll` 构建时从不拷贝，一并删除。
CoreLib 独占 4.48 MB，再减体积只能靠 IL trimming 或重建 CoreLib，删 facade 无效。

因此 C# 业务侧只能用最基础的语法与集合类型，`Console` / `File` / LINQ /
`Task` / `Thread` 等不可用，需要的能力由业务侧自行封装；也可把对应程序集从
`build/clr-trim-backup/`（本地备份，未纳入 git）放回 `libs/`，再重新
`cmake --build` 即可。

该目录位于 **git 子模块** `third_party/joy` 内：运行时文件属于 joy 仓库，主仓只通过
gitlink 记录 joy 的提交号。因此它们必须在 joy 仓库提交并推送、主仓随后更新 gitlink，
`git clone --recursive` 才能拿到；只提交主仓不会带走这些文件。

注意：这不是微软官方分发包（版本 .NET 8.0.17，且 .NET 8 支持已于 2026-11-10 结束），
没有官方补丁渠道，升级需要从 `dotnet/runtime` 自行重建这套二进制。

## 获取代码

```powershell
git clone --recursive https://github.com/coder210/joy_csharp_test.git
# 已 clone 过（或想同步 joy）：
git submodule update --init --recursive
```

`third_party/joy` 指向 `coder210/joy` 的固定提交，构建脚本默认就用它，无需再传 `-DJOY_SOURCE_DIR`。
要临时换用另一份 joy 源码树时，仍可 `-DJOY_SOURCE_DIR=<含 include/joy/joy.h 的目录>` 覆盖。

## 构建与运行（Windows）

```powershell
# 1) joy 的绑定程序集 + 本仓业务程序集（都输出到 build/csharp）
dotnet build third_party/joy/modules/cs/Joy.Bindings.csproj -c Release -o build/csharp
dotnet build csharp/JoyGame.csproj -c Release -o build/csharp

# 2) C 宿主（CMake 自动把 assets / JoyGame.dll / coreclr.dll + BCL 拷到输出目录，
#    并把 Joy.Bindings.dll 追加到 lib/）
cmake -S . -B build/app -A x64
cmake --build build/app --config Release --parallel

# 3) 运行（也可先 cd 到 build/app/bin/Release）
.\build\app\bin\Release\joy_csharp_test_app.exe
```

第二条 `dotnet build` 也会通过 `ProjectReference` 连带构建绑定项目；显式跑第一条
是为了确保 `Joy.Bindings.dll` 一定落在 `build/csharp/`，供 CMake post-build 取用
（`JOY_BINDINGS_DLL` 的默认值就指向这里）。改完 joy 侧的绑定声明后，两条都要重跑，
再生成宿主以刷新 `lib/` 里的 DLL。

操作：`WASD` 移动 · `Space` 或鼠标左键射击 · `Esc` 返回菜单 / 退出。

### 参数

| 参数 | 说明 |
|---|---|
| `--smoke N` | 无窗口冒烟：dummy 视频驱动 + 固定 `1/60` 步长（不限制迭代速率），跑 N 帧后按断言结果退出 |
| `--assembly PATH` | 指定程序集（默认 `JoyGame.dll`，相对当前工作目录） |

### 冒烟测试

```powershell
cd build/app/bin/Release
$env:SDL_VIDEODRIVER="dummy"
.\joy_csharp_test_app.exe --smoke 180    # 退出码 0 = 通过
```

脚本模拟按键跑完 **Menu → Play → GameOver → 重开 Play**，断言：

- IDL 新绑定链路：Lua / JS / C# 三个后端都能调用 `Joy.Api.Bindings.*`，
  句柄为不透明对象、GC 后自动析构（`api=True`）
- 结构体按值传递的 ABI 正确（`abi=True`）
- 场景切换发生 2 次（菜单进游戏、死亡后重开）
- 输入真的驱动了移动、射击
- 子弹命中并击杀敌人
- 敌人 AI 确实在靠近玩家

输出 `[api] selftest PASS`（C 层）、`[api] lua selftest PASS`、
`[api] js selftest PASS`、`[api.cs] PASS ...` 与
`[smoke] PASS api=True abi=True play=2 ...` 并退出；失败则 `[smoke] FAIL ...` 且退出码为 1。

## 在 Visual Studio 中开发

C 宿主由 CMake 构建，C# 业务是独立的 SDK 风格工程。根 `CMakeLists.txt` 用
`include_external_msproject` 把 `csharp/JoyGame.csproj` 挂进 CMake 生成的 VS
解决方案（仅 Visual Studio 生成器生效，其它生成器忽略），所以不需要再手写一个
与 CMake 并列的 `.sln`：

1. 先按「构建与运行」执行一次 `cmake -S . -B build/app -A x64`。
2. 用 VS 打开 `build/app/joy_csharp_test_app.sln`，解决方案里同时有
   `joy_csharp_test_app`（C 宿主）和 `JoyGame`（C# 工程），C# 侧有完整
   IntelliSense，可直接编辑与生成。CMake 已把该工程映射到 `Any CPU` 平台，
   并把 `MinSizeRel` / `RelWithDebInfo` 映射到 `Release`，无需再到
   Configuration Manager 手动修复映射（手动改也会被下次 configure 覆盖）。
3. 宿主运行加载的是 `build/csharp/JoyGame.dll`（拷到 exe 目录），
   `Joy.Bindings.dll` 则拷进 `lib/`。VS 里生成 `JoyGame` 工程时，MSBuild 输出到
   `csharp/bin/x64/<配置>/netstandard2.0/`，**不会**自动更新 `build/csharp/`；
   改完 C# 后请执行「构建与运行」里的两条 `dotnet build ... -o build/csharp`，
   再生成宿主。

若用 VS 的「打开文件夹」以 CMake 工作区方式打开仓库根，只会加载 C/C++ 目标，
C# 代码没有项目上下文；这时可另外打开 `csharp/JoyGame.csproj`。

## 字体与中文界面

底层文字渲染是支持 Unicode 的：`text_createx()` 先用 `utf8_decode_all` 把 UTF-8
解成 codepoint，再用 `stbtt_GetCodepointHMetrics` / `stbtt_MakeCodepointBitmap`
按码点取字形。所以中文能否显示只取决于字体有没有 CJK 字形——早先随模板分发的
`ARIALNB.TTF`（Arial Narrow Bold）只有拉丁字形，中文码点会落到 `.notdef` 变豆腐块。
这类不支持中文的字体（Arial / Arial Narrow 共 9 个变体）已全部移除。

`assets/fonts/` 现在只保留含中文字形的 TTF：

| 文件 | 说明 |
|---|---|
| `simhei.ttf` | 黑体，覆盖简体中文常用字，UI 主字体（约 9.3 MB） |
| `simsunb.ttf` | 宋体扩展汉字补充（约 20.9 MB），不保证覆盖常用字，不作为 UI 字体候选 |

`Assets.Load` 按顺序尝试 `assets/fonts/simhei.ttf` → `C:/Windows/Fonts/simhei.ttf` →
`C:/Windows/Fonts/Deng.ttf`，命中即用同一路径建 12 / 22 两个字号。裁剪后的 BCL 没有
`File.Exists`，靠 `font_create` 返回 `IntPtr.Zero` 判断候选是否可用。三个候选都含
CJK 字形，正常环境下界面始终显示中文；`runtime/Texts.cs` 的 `Texts.Pick` 仍保留
ASCII 降级分支，以便将来换回非 CJK 字体时不会乱码。

想换字体：替换 `simhei.ttf`，或改 `csharp/runtime/Assets.cs` 里的 `CjkFonts` 顺序。
请用 TrueType `glyf` 轮廓的单字体 `.ttf`，不要用 `.ttc` 字体集合或 CFF 轮廓的
`.otf`——`jfont.c` 用 `stbtt_InitFont(data, 0)`，对这两类支持有限。

## 绑定体系

绑定层全部位于 joy 子项目 `third_party/joy/modules/cs/`：

| 层 | 内容 | 维护位置 |
|---|---|---|
| 自动生成（白名单） | 仅 `font_create` / `image_create` 两个 joy C API | `modules/cs/gen_csclib.js` 的 `ALLOW_NAMES` → 生成 `bindings/csclib_generated.c` 与 `scripts/InternalCalls.cs` |
| 手写热点 | `Joy.Draw`（矩形、线、文字、贴图）、`Joy.Input`（键鼠轮询）、`Joy.Host`（日志 / 退出） | C 实现：`modules/cs/bindings/csclib_{draw,input,host}.c`；C# 声明：`modules/cs/scripts/NativeCalls.cs` |
| IDL 驱动（P1/P2，三端） | 三端同名 `Joy.Api.Bindings.*`；C# 无 `IntPtr`，Lua / JS 句柄为不透明对象 | `api/tools/gen_bindings.js` → 共享 thunk + `api_generated.c` / `lapi.c` / `japi.c` |
| C# 程序集 | 上述几类 `public` 声明统一编入 `Joy.Bindings.dll` | `modules/cs/Joy.Bindings.csproj`（显式列出 `scripts/*.cs`） |

旧链路只暴露业务实际调用的 internal call，由 `ALLOW_NAMES` 白名单控制，
未登记的 joy.h API 不会透给 C#；所有 wrapper 只调用 `joy.h` 声明的函数与 C
标准库，不直接访问 `SDL_*` / `ikcp_*` / mongoose / spine 等第三方接口。早期的
SDL / KCP / socket / mathx / collision / utils 绑定文件已删除。

重新生成（改动落在 joy 子项目里，需在 joy 仓库提交后更新本仓 gitlink）：

```powershell
node third_party/joy/modules/cs/gen_csclib.js
# 产出：modules/cs/bindings/csclib_generated.c 与 scripts/InternalCalls.cs
# 默认解析 joy 源码树自身；也可用 --joy <目录> 或环境变量 JOY_DIR 覆盖
```

生成后重跑「构建与运行」的两条 `dotnet build`，再 `cmake --build` 刷新 `lib/`。

生成器规则：

- 白名单：`ALLOW_NAMES` 只登记业务需要的 joy.h API，新增 C# 接口须先在此登记
- `public`：生成的类都是 `public static class`，因为声明编入独立的 `Joy.Bindings.dll`，业务程序集要跨程序集访问
- 标量 → 同名 C# 类型；`const char*` 入参 → `string`；`*_p` / `void*` → `IntPtr`
- **小结构体**（字段全标量、总大小 ≤ 16 字节，如 `vec2_t` / `jrect_t`）→ C# `[StructLayout(Sequential)]` 结构体，按值传参；`EXTRA_STRUCTS` 强制输出手写 ABI 自检所需的结构体
- 跳过：结构体指针、结构体返回值、数组、函数指针、可变参数、输出参数（`T*`）——这些热点由 `modules/cs/bindings/csclib_draw.c` 的扁平包装覆盖

绘制为什么还要手写一层：`image_draw1` / `jtext_printx` 这类 API 走的是结构体指针 + 颜色结构体，C# 侧传 `ref` 既啰嗦又容易踩 ABI 坑；扁平成 `float/int/string` 后业务代码更好写。

## 绑定 API 契约（P0 / P1 / P2）

joy 子项目的 `api/` 放一份与脚本语言无关的接口清单，让 Lua / JS / C# 三个后端
共用同一套对象模型与所有权规则：

```powershell
node third_party/joy/api/tools/gen_bindings.js             # 重生成全部 4 个生成物
node third_party/joy/api/tools/gen_bindings.js --enable L1  # 按级别批量开放（逐条过闸）
node third_party/joy/api/tools/gen_bindings.js --scan       # 从分域头更新 joy.idl
node third_party/joy/api/tools/gen_bindings.js --check      # CI 校验，不写文件
```

`joy.idl`（每条 API 一行、人工可改）与 4 个生成物都入库；`--scan` 只刷新自动字段，
保留 `status` / `owns` / `note`。当前 935 条里开放 446 条，`--enable` 只放行通过
类型、所有权与缓冲校验的条目，跳过原因逐条打印，`git diff` 可审查。
字段含义、分级与操作见 [`third_party/joy/api/README.md`](third_party/joy/api/README.md)。

共享 C 运行层（统一值容器、句柄表、自动析构）、IDL 驱动的 thunk 生成器，
以及 **cs 后端薄适配**已落地：

```text
Joy.Api.Bindings.*（强类型，无 IntPtr） → Joy.Api.Native.*（internal call）
  → api_generated.c（Mono 值转 joy_value[]） → joy_api_call() → thunks_generated.c
  → joy C API + joy_obj 句柄（finalizer 触发自动析构）
```

`joy_thunk_entry` 带 `script_name` / `arg_kinds` / `ret_kind`，三端共用同一张类型
契约表；方法名由 `naming.js` 统一生成。三个后端都在旧注册入口后多调一次新入口
（`csopen_api()` / `luax_open_api()` / `jsopen_api()`），旧白名单胶水与 C# 业务
调用点一行未改。Lua / JS 适配层遍历注册表动态注册，新增 API 不需要改后端代码。

`joy_api_selftest()` 对每条开放 API 故意传错 `argc` 验证注册表与守卫，不执行任何
业务函数；`--smoke` 另外跑三端各自的端到端自检：
`[api] selftest PASS` → `[api] lua selftest PASS` → `[api] js selftest PASS`
→ `[api.cs] PASS ... destroyed=1`，后两者计入 `[smoke] PASS` 断言。

**仍未完成**：L2 结构体展开 74 条、L3 手写 229 条、42 条所有权待裁决、
`jmath.h` 记录映射，以及旧 `Joy.*` 调用点向 `Joy.Api.Bindings.*` 的迁移与旧胶水删除。

## 扩展指引

- **加一个场景**：实现 `runtime/IScene.cs` 的 `IScene`（`Enter/Update/Render`），在 `game/` 下新建场景并用 `app.ChangeScene(...)` 切换。
- **加一种敌人**：复制 `game/Enemy.cs` 的字段，在 `game/PlayScene.cs` 的 `UpdateSpawn` 里决定生成规则，`Render` 里选精灵。
- **加绘制**：优先用 `runtime/Painter.cs`；需要新 API 时在 joy 子项目 `modules/cs/bindings/csclib_draw.c` 加扁平包装 + `modules/cs/scripts/NativeCalls.cs` 的 `public` 声明 + 注册一行，然后重新生成 / 构建。
- **加输入**：joy 子项目 `modules/cs/bindings/csclib_input.c` 已经透传全部 SDL scancode（宿主写入），C# 侧直接用 `runtime/Keys.cs` 的常量或数字。

## CI

`.github/workflows/windows-build.yml`（仅 Windows x64）：

1. `checkout` 时递归拉取子模块（joy 提交号由 gitlink 固定，绑定模块随之就位）
2. 装 Node 20 与 .NET 8（C# 运行时由 joy 子模块提供，无需安装 Mono）
3. `node third_party/joy/api/tools/gen_bindings.js --check` 校验 IDL 与 4 个生成物同步（不写文件）
4. `dotnet build` 依次构建 joy 的 `Joy.Bindings.csproj` 与本仓 `JoyGame.csproj`，都输出到 `build/csharp`
5. CMake + MSVC 构建宿主（`MODULES cs lua js` 编入 joy 的三端绑定源码；post-build 把 `Joy.Bindings.dll` 追加进 `lib/`）
6. `joy_csharp_test_app.exe --smoke 180` 无窗口冒烟（必须 `[api] selftest PASS`、`[api] lua selftest PASS`、`[api] js selftest PASS`、`[api.cs] PASS`、`[smoke] PASS`）
7. 上传 exe + dll + `lib/**` + assets 产物

## License

MIT，见 [LICENSE](LICENSE)。
