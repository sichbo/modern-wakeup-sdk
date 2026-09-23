cmake_minimum_required(VERSION 3.20)
project(modernwakeup_sdk VERSION 0.13.0 LANGUAGES CXX)

if(NOT WIN32)
  message(FATAL_ERROR "Modern Wakeup supports Windows only")
endif()

add_library(modernwakeup STATIC src/modern_wakeup.cpp)
target_compile_features(modernwakeup PUBLIC cxx_std_20)
target_compile_definitions(modernwakeup PUBLIC _WIN32_WINNT=0x0602 WIN32_LEAN_AND_MEAN NOMINMAX)
target_include_directories(modernwakeup PUBLIC
  $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
  $<INSTALL_INTERFACE:include>)
target_link_libraries(modernwakeup PUBLIC PowrProf)

add_executable(modernwakeup_example example/modern_wakeup_example.cpp)
target_link_libraries(modernwakeup_example PRIVATE modernwakeup)

install(TARGETS modernwakeup EXPORT modernwakeup_targets)
install(FILES include/modernwakeup.hpp include/modernwakeup_abi.h DESTINATION include)
