# ============================================================
# C# 业务模板的 C++ 宿主（Windows / MSVC），仓库根即构建入口
#
# 结构：
#   main.c                        创建窗口 + 主循环 + 事件泵，每帧回调 C# Host.OnFrame
#   third_party/joy/modules/cs   joy 子项目提供的 C# 绑定模块（MODULES cs）
#   csharp/                       C# 业务程序集 JoyGame.dll（dotnet build 产出）
#
# 用法（在仓库根目录，joy 默认取仓内子模块 third_party/joy）：
#   cmake -S . -B build/app -A x64 \
#         -DMONO_INCLUDE_DIR=... -DMONO_LIBRARIES=...
#   cmake --build build/app --config Release
# ============================================================
cmake_minimum_required(VERSION 3.16)
project(joy_csharp_test_app C CXX)

set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")

# ---- joy 源码树（其下有 include/joy/joy.h）---------------------------------------------
# 默认用仓内子模块 third_party/joy（git submodule update --init），
# 也兼容与 joy 源码树同级、或从别的工程里复用 joy 的场景。
set(JOY_SOURCE_DIR "" CACHE PATH "joy 源码目录（其下须有 include/joy/joy.h）")
if(NOT EXISTS "${JOY_SOURCE_DIR}/include/joy/joy.h")
    foreach(_joy_cand
        "${CMAKE_CURRENT_SOURCE_DIR}/third_party/joy"
        "${CMAKE_CURRENT_SOURCE_DIR}/../joy"
        "${CMAKE_CURRENT_SOURCE_DIR}/../joy_topdown_template/third_party/joy")
        if(EXISTS "${_joy_cand}/include/joy/joy.h")
            set(JOY_SOURCE_DIR "${_joy_cand}" CACHE PATH "joy 源码目录（其下须有 include/joy/joy.h）" FORCE)
            break()
        endif()
    endforeach()
endif()
if(NOT EXISTS "${JOY_SOURCE_DIR}/include/joy/joy.h")
    message(FATAL_ERROR
        "joy_csharp_test_app: 找不到 joy 源码树，请设置 -DJOY_SOURCE_DIR=<含 include/joy/joy.h 的目录>")
endif()

# ---- C# 运行时：joy 子项目 runtimes/ 提供 -----------------------------------
# 该构建来自 dotnet/runtime：coreclr.dll 内静态链接了 Mono 运行时组件，
# 同时导出 coreclr_* 与 mono_* 两套宿主 API，因此 jcsx.c 与 bindings/ 胶水
# 仍可沿用 Mono 嵌入 API，无需重写宿主层。
#
# 目录约定：runtimes/ 根是跨平台共享的 BCL(libs/) 与头文件(include/mono-2.0)，
# runtimes/<platform>/ 只放该平台原生二进制（Windows: coreclr.dll + import lib；
# 后续 Android: libcoreclr.so）。换整套运行时用 -DJOY_CLR_ROOT=<目录>。
set(JOY_CLR_ROOT "${JOY_SOURCE_DIR}/runtimes"
    CACHE PATH "CoreCLR 运行时根目录（默认取 joy 子项目 runtimes/）")

# 平台目录自动推导，可 -DJOY_CLR_PLATFORM=<目录名> 覆盖
if(NOT JOY_CLR_PLATFORM)
    if(ANDROID)
        if(ANDROID_ABI STREQUAL "arm64-v8a")
            set(JOY_CLR_PLATFORM "android-arm64")
        elseif(ANDROID_ABI STREQUAL "armeabi-v7a")
            set(JOY_CLR_PLATFORM "android-armv7")
        elseif(ANDROID_ABI STREQUAL "x86_64")
            set(JOY_CLR_PLATFORM "android-x64")
        else()
            set(JOY_CLR_PLATFORM "android-${ANDROID_ABI}")
        endif()
    elseif(WIN32)
        set(JOY_CLR_PLATFORM "windows-x64")
    elseif(APPLE)
        set(JOY_CLR_PLATFORM "macos-arm64")
    else()
        set(JOY_CLR_PLATFORM "linux-x64")
    endif()
endif()

# 缓存里若还留着重构前已失效的旧路径，自动回退到 joy 子项目内置的那套
set(_clr_root "${JOY_SOURCE_DIR}/runtimes")
if(JOY_CLR_ROOT AND EXISTS "${JOY_CLR_ROOT}/libs/System.Private.CoreLib.dll")
    set(_clr_root "${JOY_CLR_ROOT}")
endif()
set(JOY_CLR_PLATFORM_DIR "${_clr_root}/${JOY_CLR_PLATFORM}")

set(MONO_INCLUDE_DIR "${_clr_root}/include/mono-2.0")
set(JOY_CLR_LIB_DIR  "${_clr_root}/libs")
# 共享物默认取根；平台目录里若另放一份（如 AOT 过的 CoreLib）则优先用平台版
if(EXISTS "${JOY_CLR_PLATFORM_DIR}/libs/System.Private.CoreLib.dll")
    set(JOY_CLR_LIB_DIR "${JOY_CLR_PLATFORM_DIR}/libs")
endif()
if(EXISTS "${JOY_CLR_PLATFORM_DIR}/include/mono-2.0")
    set(MONO_INCLUDE_DIR "${JOY_CLR_PLATFORM_DIR}/include/mono-2.0")
endif()

if(WIN32)
    set(MONO_LIBRARIES   "${JOY_CLR_PLATFORM_DIR}/coreclr.import.lib")
    set(MONO_RUNTIME_DLL "${JOY_CLR_PLATFORM_DIR}/coreclr.dll")
else()
    # Android / Linux 的部署方式不同（原生库进 APK lib/<abi>/ 或系统库路径），
    # 这里只给出路径变量，实际打包逻辑待对应平台落地时补。
    set(MONO_LIBRARIES   "${JOY_CLR_PLATFORM_DIR}/libcoreclr.so")
    set(MONO_RUNTIME_DLL "${JOY_CLR_PLATFORM_DIR}/libcoreclr.so")
endif()

# ---- C# 业务程序集 ---------------------------------------------------------
set(JOY_GAME_DLL "${CMAKE_CURRENT_SOURCE_DIR}/build/csharp/JoyGame.dll"
    CACHE FILEPATH "JoyGame.dll（dotnet build -o build/csharp 的产物）")
# joy 的绑定程序集：JoyGame.csproj 通过 ProjectReference 引用它，
# dotnet build -o build/csharp 会把两个 DLL 一起放进该目录。
set(JOY_BINDINGS_DLL "${CMAKE_CURRENT_SOURCE_DIR}/build/csharp/Joy.Bindings.dll"
    CACHE FILEPATH "Joy.Bindings.dll（joy 子项目 Joy.Bindings.csproj 的产物）")

# ---- joy 依赖的第三方工程 ---------------------------------------------------
# 三个第三方子工程先建 target，随后 joy 作为子目录引入构建契约（joy::all）；
# joy/CMakeLists.txt 在子目录模式下不会重复 add_subdirectory 它们。
add_subdirectory("${JOY_SOURCE_DIR}/msgpack" "${CMAKE_BINARY_DIR}/msgpack")
add_subdirectory("${JOY_SOURCE_DIR}/spine"   "${CMAKE_BINARY_DIR}/spine")
add_subdirectory("${JOY_SOURCE_DIR}/SDL"     "${CMAKE_BINARY_DIR}/SDL")

# ---- joy 本体：源码注入（joy::all INTERFACE + 显式清单）---------------------
# MODULES 通过全局属性传给三个后端模块（默认全部关闭）。启用 lua / js 是为了
# 让 Joy.Api.Bindings.* 在三个后端都有真实可跑的验证，而不是只写不编。
set_property(GLOBAL PROPERTY JOY_REQUESTED_MODULES cs lua js)
add_subdirectory("${JOY_SOURCE_DIR}" "${CMAKE_BINARY_DIR}/joy")

# ---- 宿主可执行文件 --------------------------------------------------------
add_executable(joy_csharp_test_app
    main.cpp                        # 入口薄层：SDL_AppInit/AppEvent/AppIterate/AppQuit
    context.h                       # 宿主上下文（窗口/r2d/csx/方法句柄/帧计时）
    host/clr_host.h
    host/clr_host.cpp               # CLR 装配：运行目录、程序集、Host.OnInit/OnFrame
    systems/input_system.h          # SDL 事件 → csx 输入桥
    systems/smoke_system.h          # --smoke 的 C / lua / js 三段自检
    )
target_link_libraries(joy_csharp_test_app PRIVATE joy::all)

# ---- Visual Studio：把 C# 工程一并挂进生成的解决方案 -----------------------
# 仅 VS 生成器生效，其它生成器忽略。C# 仍由 dotnet build 产出 JoyGame.dll，
# 这里只为在 VS 里同时浏览 / 生成 C# 业务代码，不改变 cmake --build 的行为。
if(CMAKE_GENERATOR MATCHES "Visual Studio")
    include_external_msproject(
        JoyGame
        "${CMAKE_CURRENT_SOURCE_DIR}/csharp/JoyGame.csproj"
        TYPE "FAE04EC0-301F-11D3-BF4B-00C04F79EFBC"
        PLATFORM "Any CPU")
    # C# 工程只有 Debug/Release 两个配置；把 CMake 的 MinSizeRel /
    # RelWithDebInfo 映射过去，否则 VS 会提示「配置映射不正确」。
    set_property(TARGET JoyGame PROPERTY MAP_IMPORTED_CONFIG_MINSIZEREL Release)
    set_property(TARGET JoyGame PROPERTY MAP_IMPORTED_CONFIG_RELWITHDEBINFO Release)

    # Joy.Bindings 只是 JoyGame 的 ProjectReference，MSBuild 会隐式构建它，
    # 但不会把它列进解决方案，VS 解决方案资源管理器里看不到、也没有项目上下文。
    # 显式挂进来才能在 VS 里浏览绑定源码，并让「生成解决方案」先构建它。
    # 路径由 joy 的 cs 模块 CMakeLists 导出，主仓不再硬编码模块内部目录
    set(_joy_bindings_csproj "${JOY_BINDINGS_CSPROJ}")
    if(EXISTS "${_joy_bindings_csproj}")
        include_external_msproject(
            Joy.Bindings
            "${_joy_bindings_csproj}"
            TYPE "FAE04EC0-301F-11D3-BF4B-00C04F79EFBC"
            PLATFORM "Any CPU")
        set_property(TARGET Joy.Bindings PROPERTY MAP_IMPORTED_CONFIG_MINSIZEREL Release)
        set_property(TARGET Joy.Bindings PROPERTY MAP_IMPORTED_CONFIG_RELWITHDEBINFO Release)
    endif()
endif()

# ---- 运行期产物：assets/ + JoyGame.dll + CoreCLR 运行时与 BCL ---------------
set(_app_out "${CMAKE_RUNTIME_OUTPUT_DIRECTORY}/$<CONFIG>")

add_custom_command(TARGET joy_csharp_test_app POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_directory
            "${CMAKE_CURRENT_SOURCE_DIR}/assets" "${_app_out}/assets"
    COMMENT "Copying assets")

if(EXISTS "${JOY_GAME_DLL}")
    add_custom_command(TARGET joy_csharp_test_app POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different "${JOY_GAME_DLL}" "${_app_out}/JoyGame.dll"
        COMMENT "Copying JoyGame.dll")
else()
    message(WARNING
        "未找到 JoyGame.dll（${JOY_GAME_DLL}）：先执行 "
        "dotnet build csharp/JoyGame.csproj -c Release -o build/csharp")
endif()

if(EXISTS "${MONO_RUNTIME_DLL}")
    add_custom_command(TARGET joy_csharp_test_app POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different "${MONO_RUNTIME_DLL}" "${_app_out}/coreclr.dll"
        COMMENT "Copying coreclr.dll")
else()
    message(WARNING "未找到运行时 ${MONO_RUNTIME_DLL}：coreclr.dll 需与 exe 同目录")
endif()

if(EXISTS "${JOY_CLR_LIB_DIR}")
    add_custom_command(TARGET joy_csharp_test_app POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E remove_directory "${_app_out}/lib"
        COMMAND ${CMAKE_COMMAND} -E copy_directory "${JOY_CLR_LIB_DIR}" "${_app_out}/lib"
        COMMENT "Copying .NET BCL (lib/)")
else()
    message(WARNING "未找到 BCL 目录 ${JOY_CLR_LIB_DIR}：运行时需要 lib/ 与 exe 同目录")
endif()

# JoyGame.dll 通过 ProjectReference 依赖 Joy.Bindings.dll，而这套自定义运行时
# 只从 mono_set_dirs 指定的 lib/ 解析依赖程序集（放 exe 旁无效，MONO_PATH 也无效）。
# 因此绑定 DLL 必须落进 lib/，且拷贝必须排在上面「清空 lib/ + 复制 BCL」之后；
# 同一 target 的 POST_BUILD 命令按添加顺序执行。
if(EXISTS "${JOY_BINDINGS_DLL}")
    add_custom_command(TARGET joy_csharp_test_app POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
                "${JOY_BINDINGS_DLL}" "${_app_out}/lib/Joy.Bindings.dll"
        COMMENT "Copying Joy.Bindings.dll into CLR lib/")
else()
    message(WARNING
        "未找到 Joy.Bindings.dll（${JOY_BINDINGS_DLL}）：JoyGame.dll 依赖它，先执行 "
        "dotnet build third_party/joy/modules/cs/Joy.Bindings.csproj -c Release -o build/csharp")
endif()

# ---- 冒烟测试 --------------------------------------------------------------
enable_testing()
if(EXISTS "${JOY_GAME_DLL}")
    add_test(NAME joy_csharp_test_smoke COMMAND joy_csharp_test_app --smoke 180)
    set_tests_properties(joy_csharp_test_smoke PROPERTIES
        WORKING_DIRECTORY "${_app_out}"
        ENVIRONMENT "SDL_VIDEODRIVER=dummy")
endif()
