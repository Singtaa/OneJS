cmake_minimum_required(VERSION 3.16)
project(quickjs_unity C)

set(CMAKE_C_STANDARD 11)

# Options
option(BUILD_SHARED_LIBS "Build shared library" ON)

# Configure quickjs-ng (from submodule) to build as static library
set(BUILD_SHARED_LIBS_SAVED ${BUILD_SHARED_LIBS})
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)

add_subdirectory(quickjs-ng)

set(BUILD_SHARED_LIBS ${BUILD_SHARED_LIBS_SAVED} CACHE BOOL "" FORCE)

# Build quickjs_unity as shared library
add_library(quickjs_unity SHARED
    src/quickjs_unity.c
)

target_include_directories(quickjs_unity PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/quickjs-ng
)

target_link_libraries(quickjs_unity PRIVATE qjs)

# Windows-specific settings
if(WIN32)
    target_compile_definitions(quickjs_unity PRIVATE
        _CRT_SECURE_NO_WARNINGS
        WIN32_LEAN_AND_MEAN
    )
endif()

# Install
install(TARGETS quickjs_unity
    RUNTIME DESTINATION bin
    LIBRARY DESTINATION lib
)
