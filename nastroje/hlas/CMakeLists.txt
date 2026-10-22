# EvitaHlas.dll – whisper.cpp (zo zdrojového kódu overeného commitu) + tenká vrstva evita_hlas.c v jednej knižnici.
# Bez GPU, bez OpenMP, bez sťahovania čohokoľvek počas zostavenia; behové knižnice C/C++ sú prilinkované staticky.
cmake_minimum_required(VERSION 3.21)
project(EvitaHlas C CXX)

set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded")
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(WHISPER_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
set(WHISPER_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(WHISPER_BUILD_SERVER OFF CACHE BOOL "" FORCE)
set(WHISPER_CURL OFF CACHE BOOL "" FORCE)
set(WHISPER_SDL2 OFF CACHE BOOL "" FORCE)
set(GGML_OPENMP OFF CACHE BOOL "" FORCE)
set(GGML_NATIVE OFF CACHE BOOL "" FORCE)
set(GGML_AVX ON CACHE BOOL "" FORCE)
set(GGML_AVX2 ON CACHE BOOL "" FORCE)
set(GGML_FMA ON CACHE BOOL "" FORCE)
set(GGML_F16C ON CACHE BOOL "" FORCE)
set(GGML_BMI2 ON CACHE BOOL "" FORCE)
set(GGML_CUDA OFF CACHE BOOL "" FORCE)
set(GGML_VULKAN OFF CACHE BOOL "" FORCE)

add_subdirectory(${WHISPER_ZDROJ} whisper EXCLUDE_FROM_ALL)

add_library(EvitaHlas SHARED evita_hlas.c)
target_link_libraries(EvitaHlas PRIVATE whisper)
