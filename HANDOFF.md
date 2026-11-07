# 交接文档：joy 统一绑定层（IDL → 共享 C 运行层 → 三端薄适配）

> 生成：2026-11-07 · 工作区 `e:/c++/joy_csharp_test` · Windows / PowerShell / MSVC / .NET SDK 9 / Node 20
> 当前状态：主仓 `main` @ `97a4f50`，子模块 `third_party/joy` `main` @ `aca785a`，
> gitlink 对齐，两仓均已推送；接手时为 `8516ba4` / `00a85c42`。
> 2026-11-07 更新：P2-B 数据质量一刀 + P2-A 的 `checkbox` 域 L2 试点已完成、提交并推送；
> P4 前提诊断与真实调用验证见第 0 / 8 节（主仓有尚未提交的改动）。
> **权威细节在 `third_party/joy/api/README.md`；本文是接手导航，不替代它。**

---

## 0. 一句话现状

用户要的终态：**Lua / JS / C# 三个脚本层用基本一样的方式使用 `joy.h` 的绝大多数能力**，
不依赖某语言独有特性；脚本层看不到裸指针（`IntPtr` / lightuserdata / pointer），
C 层创建的内存自动释放；同时要求生成器出错概率可控、后续改得动。

| 目标 | 现状 |
|---|---|
| 三端用法一致、同名同参数、句柄自动释放 | **已达成并实测** |
| 出错概率可控（默认关闭、分级、CI 门禁、生成物入库） | **已达成** |
| 覆盖面 | **668 / 1155 = 57.8%**，未达成「绝大多数」 |
| 旧业务调用点迁移、删除旧胶水 | **已达成（2026-11-08，见第 8 节 P4；精灵/文字宽度有降级）** |
| L2 试点（`checkbox` 域 2 条） | **已打通，剩 72 条 L2** |
| `jmath.h` | **134 个 `JOY_INLINE` 中 114 个已自动生成 220 条包装；20 个 mat44 未接（见第 8 节 P5）** |
| L3 / X | **未处理** |

**不要把「三端接通」误读成「目标完成」**：管道通了，覆盖面还差四成。
另外，668 条里只有 13 条验证过真实返回值（4 条三端完整语义、`JpmWaveSeed` /
`Murmur3Hash`、7 条 `Jmath*` 数学函数，见第 7 节自检）；其余 655 条只验证
「注册成功 + argc 守卫生效」（其中 2 条 L2 另有「展开参数个数 + 类型守卫」专项自检）。

---

## 1. 硬性约束（动手前必读）

1. **两个 git 仓**：主仓 `https://github.com/coder210/joy_csharp_test.git`、
   子模块 `https://github.com/coder210/joy.git`。改子仓内容要在子仓提交推送，
   再回主仓提交更新后的 `third_party/joy` gitlink。只推一边会让 CI 拿错代码。
2. **生成物入库 + 门禁**：`api/generated/coverage.md`、`api/generated/thunks_generated.c`、
   `api/generated/jmath_wrappers.{h,c}`、`modules/cs/bindings/api/api_generated.c`、
   `modules/cs/scripts/Api.generated.cs` 都要提交，CI 跑 `--check` 校验无 diff。
   改生成器不重生成 = CI 红。
3. **生成 ≠ 启用**：`joy.idl` 默认 `status: off`；只有 `--enable` 逐条过闸才置 `on`。
   **开放 ≠ 业务已调用 / 语义已验证**。
4. **三端契约统一**：脚本侧一律 `Joy.Api.Bindings.<PascalName>`，参数顺序与 `joy.h`
   一致；句柄只以不透明对象出现，GC 时自动 `joy_obj_release()`。方法名只能来自
   `api/naming.js`，不许后端自己写一套 `pascal()`。
5. **旧胶水已删（2026-11-08）**：`modules/*/bindings/legacy/` 三个目录、
   `JOY_ENABLE_LEGACY_BINDINGS` 开关、`gen_csclib.js` 全部移除，脚本层只剩
   `Joy.Api.Bindings`；宿主接口 `Joy.Host` / `Joy.Input` 留在 `modules/cs/bindings/host/`
   并始终注册。Lua/JS 宿主仍创建空的全局 `joy` 表，只用于挂脚本自己的回调。
6. **不要手改生成物**：改 `api/emit_*.js` / `gen_bindings.js` → 重生成 → 一起提交。
7. **CRLF**：Windows `core.autocrlf` 会把 LF 转 CRLF；`--check` 用 `normEol()` 归一后比较。
   删掉它 CI 会误报。
8. **每刀必须跑第 7 节验证清单**，别只跑 `--check` 就说完成。

---

## 2. 目录地图

### `third_party/joy/api/`（契约与共享 C 层）

| 文件 | 维护 | 作用 |
|---|---|---|
| `gen_bindings.js` | 人工 | 总入口：解析 `include/joy/{defs,api}.h` → 维护 IDL → 产出 4 个生成物；`--scan` / `--enable` / `--disable` / `--check`。零 npm 依赖 |
| `naming.js` | 人工 | **三端共用**的 `isOpaque` / `methodName`(PascalCase) / `handleClass` |
| `emit_thunks.js` | 人工 | 生成 `thunks_generated.c`：每条 on 的 L0/L1 一个纯 C thunk、`s_thunks[]` 注册表、`joy_api_selftest()`；导出 `canThunk` / `ownsIssue` |
| `emit_cs.js` | 人工 | 生成 cs 侧 C icall 与 C# 强类型包装；导出 `canEmitCs` |
| `joy.idl` | 工具初始化 + 人工 | 唯一真相源，一行一条 JSON（935 条） |
| `coverage.md` | 生成 | 分级统计、按域分布、所有权待裁决、L3 清单 |
| `thunks_generated.c` | 生成 | 446 个 thunk + `s_thunks[]` + 注册表自检 |
| `README.md` | 人工 | **权威操作手册**（字段、分级、三种操作、边界） |
| `runtime/joy_value.*` | 人工 | 统一值容器：`JOY_V_NULL/BOOL/I32/U32/I64/U64/F64/STR/HANDLE` |
| `runtime/joy_obj.*` | 人工 | 句柄表（generation + refcount + tag）、`joy_obj_register/deref/release`、live/destroy 计数 |
| `runtime/joy_thunk.*` | 人工 | `joy_thunk_fn` 签名、`joy_thunk_entry` 注册表结构、按名调用 `joy_api_call()` |
| `joy_api.h` | 人工 | 对外唯一入口 `const char* joy_api_selftest(void)` |

`joy_thunk_entry` 当前字段顺序（**改结构必须同步改 `emit_thunks.js` 的初始化**）：
```text
domain, name(C snake_case), script_name(PascalCase), fn, argc, arg_kinds[], ret_kind
```

### `third_party/joy/modules/<后端>/`

| 文件 | 作用 |
|---|---|
| `cs/bindings/api_generated.c` | 生成：Mono 值 → `joy_value[]` → `joy_api_call()`，注册 `Joy.Api.Native::*` |
| `cs/scripts/Api.generated.cs` | 生成：`Joy.Api.Bindings` / `Diagnostics` / 强类型句柄类，public API 无 `IntPtr` |
| `cs/jcsx.c` / `stub.c` / `joy_csharp.h` | `csopen_api()` 注册 + 关闭时空桩 |
| `lua/bindings/lapi.c` | **人工**：遍历注册表注册 `Joy.Api.Bindings.*`；句柄 = userdata + `__gc`；`luax_api_selftest()` |
| `js/bindings/japi.c` | **人工**：同上；句柄 = 普通对象 + 隐藏字段 + finalizer；`jsopen_api_selftest()` |
| `lua/jluax.c` / `js/jsx.c` | 宿主创建时各多调一次 `luax_open_api()` / `jsopen_api()` |
| `lua/joy_lua.h` / `js/joy_js.h` / 各自 `stub.c` | 新入口声明与空桩（用 `void*` 避免头文件依赖第三方 VM 头） |

### 主仓

| 文件 | 作用 |
|---|---|
| `main.cpp` | 入口薄层：参数解析 + `SDL_AppInit/AppEvent/AppIterate/AppQuit` |
| `context.h` | `struct Context`：窗口 / r2d / csx / Host 方法句柄 / 帧计时，不再散成 `static` 全局 |
| `host/clr_host.{h,cpp}` | CLR 装配：exe 目录 + `MONO_LIB_DIR`、程序集加载、`Host.OnInit/OnFrame/OnShutdown` |
| `systems/input_system.h` | SDL 事件 → `csx_input_*` 输入桥（鼠标坐标换算到逻辑分辨率） |
| `systems/smoke_system.h` | `--smoke` 的 C / lua / js 三段自检 + dummy 视频/软件渲染 hint |
| `CMakeLists.txt` | `set_property(GLOBAL PROPERTY JOY_REQUESTED_MODULES cs lua js)` + `add_subdirectory(joy)` + `joy::all`；宿主源码显式列在 `add_executable` |
| `csharp/test/ApiBindingSmoke.cs` | C# 侧自检（调用、强类型对象、GC 自动释放） |
| `csharp/test/SmokeScript.cs` | `ok &= ApiBindingSmoke.Passed`，输出 `[smoke] PASS api=True ...` |
| `.github/workflows/windows-build.yml` | Node 20 + `--check` 门禁 + 冒烟四条断言 |
| `README.md` | 对外说明（含绑定契约章节与 CI 步骤） |

---

## 3. 数据流

```text
                    include/joy/{defs,api}.h ──gen_bindings.js──> joy.idl ──> 4 个入库生成物
                                                  │
脚本层（三端同名）                                 │
Joy.Api.Bindings.GetTicks()                       │
  │                                              │
  ├─ Lua : lapi.c      VM值 ↔ joy_value          │
  ├─ JS  : japi.c      VM值 ↔ joy_value          │
  └─ C#  : api_generated.c + Api.generated.cs    │
              │                                  │
              └──> joy_api_call(name, args, n) ──┤
                         │                       │
                  joy_thunks_table()  ← thunks_generated.c
                         │
                  thunk（纯 C，不含任何 VM 类型）
                         │
              joy_obj_register / deref / release
                         │
                   真正的 joy C API
```

**关键设计**：Lua / JS 的适配层遍历注册表动态注册，**不为每条 API 生成后端代码**。
新增 API 只重新生成 `thunks_generated.c`，两个后端自动跟随。
C# 例外：Mono internal call 必须静态绑定，所以由 `emit_cs.js` 生成静态 icall。

---

## 4. 命令速查（PowerShell，仓库根目录）

```powershell
# 契约（默认生成 4 个产物；--check 只读；--scan 会保留 status/owns/note）
node third_party/joy/api/tools/gen_bindings.js
node third_party/joy/api/tools/gen_bindings.js --scan
node third_party/joy/api/tools/gen_bindings.js --enable L0|L1|all|domain:<名>|name:<API>
node third_party/joy/api/tools/gen_bindings.js --disable <同样的选择子>
node third_party/joy/api/tools/gen_bindings.js --check

# 构建
dotnet build third_party/joy/modules/cs/Joy.Bindings.csproj -c Release -o build/csharp
dotnet build csharp/JoyGame.csproj -c Release -o build/csharp
cmake -S . -B build/app -A x64        # 首次可能需要 -DMONO_INCLUDE_DIR / -DMONO_LIBRARIES
cmake --build build/app --config Release --parallel

# 冒烟（180 帧无窗口）
Push-Location build/app/bin/Release
$env:SDL_VIDEODRIVER='dummy'
.\joy_csharp_test_app.exe --smoke 180
Pop-Location
```

`--enable` 逐条过闸，打印 `changed / skipped` 与每条跳过原因；`--disable` 对称回退。
`all` 目前只覆盖 L0/L1；`name:` 显式点名可豁免「返回句柄需人工确认所有权」这条软限制，
但**不能绕过 `owns` 类型严格匹配的硬校验**。

---

## 5. 当前数据（2026-11-07 实测，勿凭记忆改）

| 项 | 数量 |
|---|---|
| `include/joy/{defs,api}.h` 的 `JOY_API` 声明 / IDL 条目 | 1155（含 220 条 jmath 包装） |
| 已开放 `status: on` | **668**（含 L2 试点 2 条 + jmath 包装 220 条） |
| L0 / L1 / L2 / L3 / X | 264 / 538 / 74 / 229 / 50 |
| 所有权待裁决（`ownsIssue`） | 38 |
| 缺 `owns` | 38 |
| orphan | 0 |
| `include/joy/{defs,types}.h` 中值类型 `typedef struct` | 48 |

两个 38 是同一批条目：P2-B 数据质量一刀把 42 条错误推断逐条裁决完，
4 条补上合规 dtor（`create_window` / `simple_fps_create` / `r2d_wrap` /
`jtilemap_load_file_json`），其余 38 条确认没有可通过硬校验的单参数 dtor，
`owns` 置空、结论写进 `joy.idl` 的 `note`，并显示在 `coverage.md` 两张待办表里。

剩余 487 条的分解（复现 `canOpen` 统计）：

| 原因 | 数量 |
|---|---|
| 非 L0/L1（= L2 72 + L3 229 + X 50） | 351 |
| 返回句柄、批量模式需人工确认所有权 | 52 |
| 返回句柄但缺合规 `owns`（已人工裁决，见 `note`） | 36 |
| `void*` 缓冲参数需手写适配 | 31 |
| 返回 `char*`、所有权未标注 | 12 |
| 不支持的返回类型（枚举 / `fp_t` 等） | 5 |
| **合计** | **487** |

L2 试点开放了 `checkbox_set_colors` / `checkbox_set_text_color` 两条（原本属
「非 L0/L1」），其余 72 条 L2 仍未开放；`--enable all` 仍只含 L0/L1，L2 必须
`name:` 逐条点名。改掉错误 `owns` 推断后，原来的「`owns` 目标不是单参数 23 /
类型不匹配 15 / 缺 `owns` 2」三类合并成「缺合规 `owns` 36」；4 条修好的条目
落到了后面几道闸门。
L3 的 229 条成因：`非不透明指针 / 输出参数` 98、`未知类型需人工标注` 46、
`结构体指针参数` 35、`结构体按值返回` 23、`非 const char* 输出缓冲` 10、
`函数指针 / 回调` 8、`数组参数` 5、`可变参数` 4。

---

## 6. 已踩过的坑（别重犯）

**生成器**
- 块注释里出现 `modules/*/bindings/` 会提前闭合 JS 注释；`stripComments()` 必须保留换行，否则 `source` 行号漂移。
- `collectStructs()` 必须同时读 `defs.h` / `types.h` / `jmath.h`，否则 `vec2_t` 等被误判为 L3。
- `get_ticks` / `find_*` 这类通用动词开头的名字，domain 要用第二段（`GENERIC_FIRST`）。
- Windows CRLF：`--check` 比较前必须 `normEol()`，已验证模拟 CRLF checkout 通过且能抓到真实 diff。
- L2 展开试点：结构体重组必须按**字段名**赋值，不能按偏移 `memcpy`（padding 未知）；
  局部变量用 `= { 0 }` 初始化，否则漏字段会带脏值。`argc` / `arg_kinds[]` 一律用展开后
  的序列，三端适配层才能自动跟随，不必改 `lapi.c` / `japi.c`。

**所有权**
- 早期只校验「`owns` 是单指针」不够：L1 里 39 条把 `void*` 配给 `jwindow_p` 的
  destroy，dtor 会拿错类型，运行时才崩。现在 `ownsIssue()` 要求 **dtor 参数类型
  == create 返回类型**，`--check` 与 `buildThunks` 双重拦截。
- 已实测：把 `get_raw_window` 强行设 `on`，门禁报「所有权不可信」并拒绝生成。
- `get_*` / `wrap_*` 可能返回**借用指针**，类型匹配也不代表可以自动释放；批量模式
  一律跳过返回句柄的条目，只能 `name:` 点名。
- `inferOwns()` 曾有两个系统性错判：`name.replace(/_create[\s\S]*$/, ...)` 对不含
  `_create` 的名字是恒等变换，`owns` 会填成自己；`domain_destroy` 兜底又把
  `r3d_destroy(jr3d_p)` 套到返回 `void*` 的 GPU 对象上。现在推断时就用
  `ownsIssue()` 的同一套硬校验过滤候选。
- **`--scan` 永不覆盖 `owns` / `status` / `note`**（`mergeIdl()` 视其为人工字段）：
  修生成器只影响今后新增 API 的初值，已有错误必须逐条改 `joy.idl` 再重生成。
- 借用指针能通过类型校验但不能自动释放：`scene_node_destroy(scene_node_p)`、
  `scene_destroy(scene_p)` 对 `scene_find_node_by_name` /
  `scene_manager_get_current` 类型完全匹配，填上去就是双重释放，已逐条核对实现并留
  `owns:null`。

**C# / Mono**
- `void` 返回不要声明未使用的 `joy_value_t r`（MSVC 告警）。
- `const char*` 参数要 `mono_string_to_utf8` → 调用 → `mono_free`；`char*` 返回
  因所有权未标注被 `canEmitCs` 拒绝。
- `[MethodImpl(InternalCall)]` 的方法可以是 `internal`（Mono 按名查找，不要求 public）。
- `Api.generated.cs` 的 `Native` 是 internal，句柄只以 `ulong` 出现在那里；public API 无 `IntPtr`。

**C / 生成器类型**
- thunk 里的 `char*` 参数必须声明成 `const char*`，否则 MSVC `C4090`（曾出现 16 处）。
- `bool` 内部用 `int32_t` 承载。

**Lua**
- 新链路全局是 `Joy`（大写），旧宿主是 `joy`（小写），刻意并存别合并。
- 句柄用 userdata + `luaL_newmetatable` + `__gc`；`collectgarbage('collect')` 或 `lua_close` 触发释放。

**JS / duktape**
- `DUK_ERR_INTERNAL_ERROR` 常量**不存在**，用 `DUK_ERR_ERROR`。
- **plain buffer 不是 object**，`duk_set_finalizer()` 会抛 `object required, found [buffer:8]`。
  句柄用普通对象 + 两个隐藏 32 位字段（`\xFF joylo` / `\xFF joyhi`）拼回 `uint64_t`。
- 参数转换用 `duk_has_prop_string` 做类型守卫；`duk_gc(ctx, 0)` + `duk_destroy_heap` 触发 finalizer。

**构建 / git**
- `modules/<name>/CMakeLists.txt` 用 `file(GLOB bindings/*.c)`，新建适配 .c 会自动被收集，不必手改 CMake。
- `joy_add_sources(... MODULES ...)` 的 `MODULES` 是 multi-value，可写 `cs lua js`。
- 两仓分别提交推送；主仓 gitlink 必须指向子仓实际 HEAD。
- `--smoke` 在模块关闭时 stub 返回 `"lua/disabled"` / `"js/disabled"`，`systems/smoke_system.h` 把它当「跳过」而非失败。

---

## 7. 验证清单（每刀结束必跑）

```text
1. node third_party/joy/api/tools/jmath_wrap.js                   → "220 wrappers generated"
   （改过 jmath.h 或增删契约头时才需要；生成 api/generated/jmath_wrappers.{h,c}）
2. node third_party/joy/api/tools/gen_bindings.js --scan          → 契约头有变更时才跑
3. node third_party/joy/api/tools/gen_bindings.js --check          → "check OK: 1155 IDL entries ..."
   单一门禁，已含 jmath_wrappers.{h,c}、include/joy/joy.h 的 include 行、报告与三端胶水
4. dotnet build Joy.Bindings.csproj -c Release -o build/csharp → 0 warning / 0 error
5. dotnet build csharp/JoyGame.csproj -c Release -o build/csharp → 0 warning / 0 error
6. cmake -S . -B build → cmake --build build --config Release --parallel
   → 0 error；新增 .c 不应产生 C4090 等告警（源码清单改过必须重新 configure）
7. --smoke 180（dummy 驱动）→ exit 0 且必须同时出现：
      [api] selftest PASS
      [api] lua selftest PASS
      [api] js selftest PASS
      [api.cs] PASS ... destroyed=1 real=True opaque=True vec2=4,6 dot=11
      [smoke] PASS api=True play=2 gameover=True moved=161.0 fired=9 kills=1 enemies=3
   （`abi=` 项随旧胶水删除：结构体按值 ABI 探针原在 `csclib_draw.c`，生成链路不传结构体）
8. legacy 开关已随胶水删除，本项作废；改动 `modules/` 结构后只跑第 6/7 项即可
9. CI 门禁篡改测试（可选但建议）：手改任一生成物 → --check 必须失败；恢复后必须通过
10. git status 两仓 + gitlink 对齐；分别 push
```

`joy_api_selftest()` 对每条开放 API 故意传错 `argc` 调一次 `joy_api_call()`：
thunk 第一行就返回，**不执行任何 joy 业务函数**，因此批量开放有副作用 / 需要上下文的
API 不会在自检里被真的调用。别把这个机制改成「真的调用每条 API」。
对开放的 L2 试点条目，另用「展开后的正确 argc + 全 `JOY_V_NULL` 参数」调一次，
期望 `JOY_ERR_TYPE`：验证展开序列与 `arg_kinds[]` 生效，同样不执行业务函数。
C# 侧的 `ApiBindingSmoke` 另做真实断言：调用 `JpmWaveSeed` / `Murmur3Hash` /
`JmathVec2*` 校验返回值，并反射确认 public API 无 `IntPtr`；Lua 侧的
`luax_api_selftest()` 也真的调用 `JmathVec2*` 校验 vec2 加法。它们并入
`[api.cs] PASS` / `[api] lua selftest PASS` 与 `[smoke] PASS`，是「真实调用语义」
与「argc 守卫」的分界线。

---

## 8. 未完成工作与建议顺序

> 除标注「已完成」的条目外都是**建议，尚未实施**；动手前先跟用户确认优先级。

**P2-A：L2 结构体按值展开（试点 2 条已完成，剩 72 条；绘制/数学类 API 最需要）**
试点已落地（`checkbox` 域 `jcolor_t`）：新增 `api/structs.js` 统一解析值类型布局，
`gen_bindings.js` / `emit_thunks.js` / `emit_cs.js` 共用；POD 结构体参数按字段展开成
标量序列，thunk 里按字段名重新组装 C 结构体。`--enable all` 仍只含 L0/L1，L2 必须
`name:` 点名；`check()` / `buildThunks()` / `canThunk()` / `canEmitCs()` 只放行
「字段全是已知标量」的可展开 L2，返回句柄的 L2 也暂不支持。

**已知偏离**：试点阶段脚本侧传的是展开后的标量序列（如 `CheckboxSetColors(cb,
r,g,b,a, ...)` 共 13 个值），不是单个对象，参数个数与 `joy.h` 不一致；三端仍同名、
同顺序、同类型。要回到「脚本传一个对象」需下一刀做记录类型：

- `runtime/joy_value.*`：增加 `JOY_V_RECORD`（字节块 + `sizeof` 静态断言）。
- `runtime/joy_thunk.h`：`joy_thunk_entry` 增加字段 schema（字段 kind、偏移、结构 tag），
  并同步改 `emit_thunks.js` 的 `s_thunks[]` 初始化。
- `lapi.c` / `japi.c` / C# 包装：结构体对象 ↔ `joy_value` 记录的转换。
- 其余 72 条 L2 还有 `vec2_t` / `vec3_t` 等（`jmath.h`，`ft_t` 未进映射表），
  以及含数组 / 嵌套 / 指针字段的结构体，当前一律不展开。
- 约束：展开后参数个数不能超过 `LUAX_MAX_ARGS` / `JSAPI_MAX_ARGS`（都是 16）；
  逐条点名开放，跑完第 7 节清单再批量放。

**P2-B：136 条被闸门跳过的条目（数据质量一刀已完成）**
已做：修 `inferOwns()` 两个系统性错判；42 条 `ownsIssue` 逐条核对实现裁决，
4 条补合规 dtor、38 条留空并在 `note` 写明「借用 / 双参数 dtor / 无合规 dtor」；
`coverage.md` 两张待办表新增 `note` 列。**该刀本身未开放任何 API（当时 446 条不变）**。
剩余按原因分批处理：52 条返回句柄需 `name:` 点名确认、36 条缺合规 `owns`
（要手写 thunk 或引入显式 `borrowed` 标记才能开放）、31 条 `void*` 缓冲、
12 条 `char*` 返回（标注谁 free）、5 条未知返回类型。

**P3：L3 229 条**
按第 5 节成因分类手写 thunk：输出参数 98 条最有希望做成通用模式；
回调 8 条、varargs 4 条、数组 5 条建议逐个人工处理。

**P4：业务收尾（已完成 2026-11-08）**
- 三个 `bindings/legacy/` 目录、`JOY_ENABLE_LEGACY_BINDINGS` 开关、`gen_csclib.js`
  与 `scripts/InternalCalls.cs` 全部删除；`Joy.Api.Bindings` 是脚本层唯一绑定入口。
- 主仓业务迁移完毕：`Painter.cs` 改走 `R2dSetColorf` + `ShapeDrawRectangle` /
  `ShapeDrawLineF` / `JtextPrintx`，`Assets.cs` 改走 `FontCreate`，`GameApp.R2d`
  从 `IntPtr` 变 `Jr2d`（经 `HostHandles.R2d()` 借用宿主渲染器）。
  **已知降级**：`image_draw1` 是 `jrect_t*` 参数（L3 未开放），精灵统一画色块；
  `jtext_measurex` 同为 L3，居中按「字符数 × 字号 × 0.58」估算。
  文本长度改传 UTF-8 字节数（`jtext_printx` 的 `len` 是字节数，旧代码传 strlen，
  迁到 C# 后若照抄 `text.Length` 会把中文截成 1/3）。
- 结构体按值 ABI 探针（`abi_vec2_sum` / `abi_rect_area`）随 `csclib_draw.c` 删除：
  生成链路不跨 ABI 传结构体，探针失去被测对象；冒烟输出不再有 `abi=` 项。
- 后续若要恢复精灵绘制 / 精确文字宽度，需要给 `image_draw1`、`jtext_measurex`
  手写 thunk（P3），三端会同时受益。

2026-11-07 核对结论：**旧胶水是 cs 模块私有 internal call（`Joy.Draw` / `Input` /
`Host` / `Image` / `Font`），与 `joy.h` 导出的 `JOY_API` 不是同一组函数**。现有 448 条里
没有与之同名或签名等价的替代品：绘制 / 图像 / 字体类多数仍是 `status:off` 或 L3；
`Joy.Host.log` / `set_exit_code` / `quit` 是宿主回调，压根没有对应 JOY_API；业务层还
持有 `IntPtr`（如 `Assets.cs` 的 `app.R2d` / `Font`），缺少到 `Jr2d` 等强类型句柄的
public 转换入口。所以 P4 不是「换名」，而是「先补能力、再重写调用点」：

1. 按业务实际需要点名开放对应 API / 为 L3 手写 thunk（给 P2-B、P3 具体输入）。
2. 补 `IntPtr` → 强类型句柄的 public 转换，或让业务从窗口 / r2d 创建起全程用新链路对象。
3. 逐处替换调用点，旧胶水留作安全网，跑完第 7 节清单后再删。

本刀已完成两个前置检查（`csharp/test/ApiBindingSmoke.cs`）：
- `CheckRealCalls()` 真正调用 `JpmWaveSeed(12345, 2)` 断言精确值 2012351、
  `Murmur3Hash` 断言确定性 / 区分性 / 非 0；这是 C# 端首次验证已开放 L0 API 的
  真实返回值（此前只有 argc 守卫）。
- `PublicApiHasNoRawPointers()` 反射遍历 `Joy.Api.Bindings` 的 public static 方法，
  断言参数 / 返回类型里没有 `IntPtr`。
`--smoke 180` 输出 `[api.cs] PASS ... real=True opaque=True`，两项并入 `[smoke] PASS`。

**P5：`jmath.h`（已完成：自动生成器覆盖 114 / 134 个函数）**
`jmath.h` 的 134 个 `JOY_INLINE` 是静态内联定义，没有外部符号，生成器只认
`JOY_API` 声明。新增 `api/tools/jmath_wrap.js`：解析 `jmath.h` 的函数签名与 10 个值类型
布局，生成 `api/generated/jmath_wrappers.{h,c}` 共 **220 条全标量包装**
（`--scan` → `--enable domain:jmath` → 开放 461 → 668）。`joy.h` 只留一行
`#include "../../api/generated/jmath_wrappers.h"`，`gen_bindings.js` 把该头当作第二个
契约输入源；`--check` 直接调用包装模块的 `check()`，不再起子进程。
规则：结构体按值入参按字段展开；标量返回生成 1 条；结构体返回拆成分量函数
`_<field>`，`vec2_t` 额外生成打包进 `uint64_t` 的版本。运行层、三端适配层
**零改动**。

**剩余 20 个函数全是 mat44 / mat44f**（`mat44_mul` / `mat44_perspective` /
`mat44_identity` 等）：4×4 矩阵有 16 个分量，展开后超过 `LUAX_MAX_ARGS` /
`JSAPI_MAX_ARGS`（16），结构体返回也无法用现有 `joy_value` 表示。要接它们、并让
脚本侧传 / 收单个 vec2 / mat44 对象而不是一长串标量，仍需 P2-A 正式版的
`JOY_V_RECORD` + 字段 schema + 三端对象转换。

**P6：源码组织（本刀已完成 A/B 步，零行为变化）**
根因：旧 `joy_add.cmake` 一条 `file(GLOB "*.h" "*.c")` 把根目录 28 个自有 `.c`、
`external/` 顶层 vendor `.c`、`test/*.c` 一起塞进宿主 exe，永远不需要显式列清单，
于是目录结构长不出来，生成物、测试、第三方与手写源码长期混放。

已落地：
- **显式清单 + 源码注入**：新建 `cmake/JoySources.cmake`（显式清单替代 GLOB），
  `cmake/JoyTargets.cmake` 只定义一个 `joy::all` **INTERFACE** target，用
  `target_sources(INTERFACE)` 把清单里的 `.c` 编进宿主。主仓从
  `include(joy_add.cmake)` + `joy_add_sources()` 改成 `add_subdirectory` +
  `target_link_libraries(... joy::all)`；`joy_add.cmake` 保留为兼容 shim。
  注入语义与旧方式一致：宿主编译定义照常作用于 joy 源码。
  **曾拆成 `joy_core` / `joy_api` 两个 STATIC 库，已撤回**：`defs.h:52-65` 的
  `PIXELS_PER_METER` 可经 `-D` 覆盖、换算函数是 `static inline`（每 TU 各固化
  一份），静态库会让库内按默认 50、宿主按 `joy_topdown_template` 覆盖的 100，
  同一进程两套坐标换算且编译全绿不报错。这类编译期可配置 inline 契约要支持
  静态库，必须先把配置项做成 PUBLIC / option 显式传递；收益又主要在多宿主场景，
  本仓只有一个 exe，不值得让外部项目一起承担切换成本。
- **GLOB 下线**：清单显式列出每个 `.c`；空的 `jphysics.c`、条件空单元
  `jgltf.c` / `jspine.c`（改为 `JOY_ENABLE_GLTF` / `JOY_ENABLE_SPINE` option）、
  `test/*.c` 全部出宿主。`jimpl.c`（stb/dr 全仓唯一实现 TU）显式登记，不再靠巧合。
- **LUT 归位**：`jlut.h` 原来把 `SIN_LUT` 与 `tan_lut` 两张各 205887 项的
  `static long long`（共 5.44 MB）经 `jmath.h` → `joy.h` 复制进每个 TU；现在改为
  `extern const` 声明 + `src/jlut.c` 唯一定义（77 份 → 1 份）。`jlut.c` 是纯数据
  TU，只依赖 `jlut.h` 的 extern 声明与长度宏，不反向 include 消费方 `jmath.h`；
  长度由 `jlut.h` 的 `JOY_SIN_LUT_LENGTH` / `JOY_TAN_LUT_LENGTH` 显式给出。
  （曾放在数据 TU 内、需 include `jmath.h` 才能取到 `FP_LUT_SIZE` 的编译期
  断言因形成反向依赖已移除。）
- **生成物归位**：jmath 包装的声明与实现从 `joy.h` 里 222 行生成块 + 根目录
  `jmath_api.c` 移到 `api/generated/jmath_wrappers.{h,c}`，主头不含生成代码。
  `--check` 直接调用包装模块的 `check()` 汇总错误，去掉 `child_process`。

**P6-C：目录分层（本刀完成）**
- **`api/` 三分**：`tools/`（6 个生成器）、`idl/joy.idl`（唯一真相源）、
  `generated/`（`coverage.md`、`thunks_generated.c`、`jmath_wrappers.{h,c}`），
  `runtime/` 保持原样。CI / README / HANDOFF 的命令统一指向
  `node api/tools/gen_bindings.js --check`。
- **`src/` 平铺**：27 个 `.c` 全部平铺在 `src/` 一层，不再按域切子目录。
  原因：早先的域是按 `j*.c` 文件名前缀猜的，未做符号级核对，内容与目录名
  多处不符（`jprocmap.c` 是程序化地图生成却放 core，`jui.c` / `jtilemap.c`
  是 UI 与地图解析却放 render）。目录一旦命名就承诺语义，承诺不兑现比平铺
  更误导。现在归属只由 `cmake/JoySources.cmake` 的分组表达，且分组仅用于
  阅读与 IDE 展示，不表示模块边界。
  `jimpl.c` 改名 `src/vendor_impl.c`（stb/dr 唯一实现 TU 显式化）；
  0 字节的 `jphysics.c` 删除；`JOY_ENABLE_PLUGINS` 接线到 `src/jmodule.c`。
  人工 / 生成 / 第三方三类源码仍各有目录，互不混放。
- **第三方目录保持根布局（曾收进 `vendor/`，已撤回）**：`external/`、`spine/`、
  `msgpack/`、`SDL/` 仍平铺在 joy 根。撤回原因：`joy_topdown_template` 这类
  外部仓库把 `third_party/joy/SDL/android-project` 直接写进 Android CI（`cp` /
  `cd` / `sed` 的目录层数 / APK 产物路径共 6 处），并把
  `${JOY_REPO_DIR}/msgpack|spine|SDL` 写进自己的 `add_subdirectory`；目录一移，
  对方各平台构建全断，而收益只有本仓目录整洁。转发 CMakeLists 救不了物理目录，
  git 软链在 Windows checkout 又不可靠。结论：第三方与引擎平级，不进 `vendor/`。
- **`bindings/` 分链路**：每个后端拆成 `bindings/api/`（新链路）、
  `bindings/host/`（仅 cs：宿主入口 `main.cpp` / `systems/input_system.h` 直接调用的
  `csx_input_*` / `csx_host_*`，以及把宿主自建窗口/渲染器登记成借用句柄的 `csopen_borrow`）。
  2026-11-08 起 `bindings/legacy/`（旧手写胶水）与 `JOY_ENABLE_LEGACY_BINDINGS`
  开关整体删除，`jluax.c` / `jsx.c` / `jcsx.c` 的旧注册块一并移除。
  host/ 是关掉开关后实测暴露出来的真实依赖：宿主入口不属于旧业务胶水，必须始终编译。
- **宿主对象 → 脚本句柄的桥**：`bindings/host/csclib_borrow.c` 按 C 类型名把宿主裸指针
  登记成 `joy_obj` 借用句柄（dtor=NULL，所有权仍在宿主），C# 侧经
  `modules/cs/scripts/HostHandles.cs`（`Joy.Api.HostHandles.R2d(IntPtr)`）拿到
  `Jr2d` 强类型句柄。主仓业务因此可以在保留「C 宿主建窗口/渲染器」架构的前提下
  全程走 `Joy.Api.Bindings` 绘制。
- **`examples/` 正名**：`test/` 里 5 个文件全是被注释的示例片段、无 main、从未被
  任何 target 编译，改名 `examples/` 并明确不参与构建。
- **拆 `joy.h`**：2585 行伞形头按行段切成 `include/joy/{defs,types,api}.h`
  （326 / 587 / 1687 行），`joy.h` 只剩 16 行聚合门面；每个分域头自带 guard 与
  `extern "C"`，系统头仍在 `extern "C"` 之外。`gen_bindings.js` 的
  `DECL_SOURCES` 改为三个契约头（`defs.h` 里也夹着 18 条 `JOY_API` 声明，
  不能只扫 `api.h`），`structs.js` 改从 `defs.h` + `types.h` + `jmath.h` 读类型。
  `source` 分布 18 / 917 / 220，`--scan` 后 IDL 仍是 1155 条、开放 668 条不变。
  83 处 `<joy/...>`、27 处 `"joy.h"`、45 处 `"external/..."` 一行未改：靠
  include dir 顺序兼容。
- **公开头全部归位**：根目录剩下的 9 个头一起搬进 `include/joy/`，根目录再无
  散头，`include/joy/` 成为唯一公开头目录。随后清掉 4 个零引用头：
  `janim.h`（孤儿）、`jgltf.h`（自称「说明性头」）、`jgltf.hpp` / `jspine.hpp`
  （自称「仅供 C++/ECS 项目 include」的可选 RAII 包装，本仓没有 C++ TU）。
  删除前全仓校验：0 处 `#include` 引用、生成器输入不含、CMake 的 `*.h` GLOB
  全在 `external/`、`msgpack/`、`spine/`、`SDL/` 这些第三方目录，与自有头无关；`src/jgltf.c` / `jspine.c`
  引用的是 `joy.h` 且默认因 `JOY_ENABLE_GLTF` / `JOY_ENABLE_SPINE=OFF` 不编译，
  与这 4 个头无依赖。最终 `include/joy/` 8 个头，全部在编译链上
  （49 个文件引用）。
  配套 4 处改动：`include/joy/joy.h` 的生成头 include 改成相对路径
  `../../api/generated/jmath_wrappers.h`（不再依赖 PRIVATE include 根）；
  `jmath_wrap.js` 同步 `INCLUDE_LINE` 与 `JMATH` / `JOYH` 路径；
  `gen_bindings.js` / `structs.js` 的 `jmath.h` 路径；
  `JoyTargets.cmake` 加 `${JOY_ROOT}/include/joy` 到 PUBLIC，让 49 处
  无前缀 include 一行不改。
  **踩过的坑**：主仓 `CMakeLists.txt` 原用 `EXISTS "${JOY_SOURCE_DIR}/joy.h"`
  探测 joy 源码树，`joy.h` 一搬走探测就失败，缓存里的旧值
  （`../joy_topdown_template/third_party/joy`）被重新选中，导致
  `msgpack` 找不到、configure 直接报错。已把探测标志改成新结构
  独有的 `${JOY_SOURCE_DIR}/include/joy/joy.h`；自动探测会 FORCE 修正缓存，
  换用旧版 joy 源码树时会明确报「找不到 joy 源码树」而不是静默用错目录。

**验证**：`--check OK: 1155 IDL entries`；两个 Release `dotnet build` 0/0；
`cmake --build` 0 error；`--smoke 180` exit 0；`JOY_ENABLE_LEGACY_BINDINGS=OFF`
单独全量编译通过（69 个 legacy `.c` 退出构建），恢复 ON 后冒烟全绿。

**剩余（D/E）**：`JOY_V_RECORD` + 字段 schema（可让 `jmath_wrappers.*` 整层消失、
接上 20 个 mat44、解开 P4 迁移）；把 1687 行的 `api.h` 再按域细拆（需先人工补齐
分节标记）；`JOY_ENABLE_LEGACY_BINDINGS=OFF` 下业务 C# 仍调用旧 `Joy.*` API，
要等 P4 迁移完才能真正关掉旧胶水；`examples/` 若要编译验证需先补 main。

---

## 9. 接手第一步建议

1. 读 `third_party/joy/api/README.md`（权威）+ 本文第 1 节。
2. 跑一次第 7 节清单，确认基线是绿的（不要跳过，环境可能变）。
3. 跟用户确认下一刀（P2-A / P2-B 剩余条目 / P4）；**不要自行全量开放剩余 API**。
4. 每刀结束：重生成 → 验证 → 子仓提交推送 → 主仓提交推送（含 gitlink）→ 更新本文第 5 节数字。
