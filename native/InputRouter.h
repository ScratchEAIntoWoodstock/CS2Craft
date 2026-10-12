#pragma once
#include <Windows.h>
#include "../third_party/skycraft_protocol_v10.h"
namespace InputRouter {
void Attach(HWND window);
void Stop();
// Hooks GetRawInputBuffer/GetRawInputData (SDL3 reads raw mouse input off the game window). 0 = both active.
int InstallHooks();
int EnableHooks(bool enable);
void Stats(uint64_t& window,uint64_t& hooked,int& status);
void Mode(bool active,bool gui,int width,int height);
// A key CS2 must not see while Minecraft plays (its buy menu key; Minecraft opens its own). 0 = none.
void BlockKey(int vk);
bool Look(int& x,int& y);
int SelfTest();
void TestMouse(int x,int y);
bool Cursor(int& x,int& y);
bool Pop(skycraft::proto::InputEvent& event);
}
