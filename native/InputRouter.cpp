#include "InputRouter.h"
#include <atomic>
#include <algorithm>
#include <deque>
#include <mutex>
#include <MinHook.h>
namespace InputRouter {
namespace {
std::atomic<bool> active{},gui{};
std::atomic<WNDPROC> previous{};
std::atomic<HWND> attached{};
std::mutex inputMutex;
std::deque<skycraft::proto::InputEvent> events;
int width=1280,height=720;
float cursorX=640,cursorY=360;
wchar_t highSurrogate{};
int lookX{},lookY{};
void mouse(const RAWMOUSE& raw){
    std::scoped_lock lock(inputMutex);
    if(gui){
        if(raw.usFlags&MOUSE_MOVE_ABSOLUTE){
            cursorX=float(raw.lLastX)*float(width)/65535.f;
            cursorY=float(raw.lLastY)*float(height)/65535.f;
        }else{cursorX+=float(raw.lLastX);cursorY+=float(raw.lLastY);}
        cursorX=std::clamp(cursorX,0.f,float(width-1));cursorY=std::clamp(cursorY,0.f,float(height-1));
    }else if(!(raw.usFlags&MOUSE_MOVE_ABSOLUTE)){
        lookX+=raw.lLastX;lookY+=raw.lLastY;
    }
}
void push(uint16_t type,int value){
    std::scoped_lock lock(inputMutex);
    if(events.size()<256)events.push_back({type,0,value,0,0});
}
// CS2's SDL3 reads raw mouse input on its own thread through a hidden helper window (GetRawInputBuffer),
// so WM_INPUT may never reach the subclassed game window. Hooking the read APIs sees those packets too.
using GetRawInputBufferFn=UINT(WINAPI*)(PRAWINPUT,PUINT,UINT);
using GetRawInputDataFn=UINT(WINAPI*)(HRAWINPUT,UINT,LPVOID,PUINT,UINT);
GetRawInputBufferFn originalBuffer{};
GetRawInputDataFn originalData{};
void* bufferTarget{};
void* dataTarget{};
int hookStatus=-1;
std::atomic<uint64_t> windowPackets{},hookedPackets{};
bool focused(){HWND window=attached.load();return window&&GetForegroundWindow()==window;}
UINT readRawData(HRAWINPUT handle,UINT command,LPVOID data,PUINT size,UINT header){
    // The window procedure reads through the trampoline so its own packets are not counted twice by the hook.
    return originalData?originalData(handle,command,data,size,header):GetRawInputData(handle,command,data,size,header);
}
UINT WINAPI hookedBuffer(PRAWINPUT data,PUINT size,UINT header){
    UINT count=originalBuffer(data,size,header);
    if(data&&count!=UINT(-1)&&header==sizeof(RAWINPUTHEADER)&&active&&focused()){
        // NEXTRAWINPUTBLOCK needs an undeclared QWORD; x64 records are 8-byte aligned.
        auto address=reinterpret_cast<uintptr_t>(data);
        for(UINT i=0;i<count;i++){
            auto* record=reinterpret_cast<const RAWINPUT*>(address);
            if(record->header.dwType==RIM_TYPEMOUSE){mouse(record->data.mouse);hookedPackets++;}
            address=(address+record->header.dwSize+7)&~uintptr_t(7);
        }
    }
    return count;
}
UINT WINAPI hookedData(HRAWINPUT handle,UINT command,LPVOID data,PUINT size,UINT header){
    UINT bytes=originalData(handle,command,data,size,header);
    if(command==RID_INPUT&&data&&bytes!=UINT(-1)&&bytes>=sizeof(RAWINPUTHEADER)+sizeof(RAWMOUSE)
        &&header==sizeof(RAWINPUTHEADER)&&active&&focused()){
        auto* raw=static_cast<const RAWINPUT*>(data);
        if(raw->header.dwType==RIM_TYPEMOUSE){mouse(raw->data.mouse);hookedPackets++;}
    }
    return bytes;
}
LRESULT CALLBACK procedure(HWND window,UINT message,WPARAM w,LPARAM l){
    if(active && GetForegroundWindow()==window){
        if(message==WM_MOUSEWHEEL){push(3,static_cast<short>(HIWORD(w)));return 0;}
        if(message==WM_INPUT){
            RAWINPUT raw{};UINT bytes=sizeof raw;
            if(readRawData(reinterpret_cast<HRAWINPUT>(l),RID_INPUT,&raw,&bytes,sizeof(RAWINPUTHEADER))!=UINT(-1)
                &&raw.header.dwType==RIM_TYPEMOUSE){
                mouse(raw.data.mouse);windowPackets++;
                return DefWindowProcW(window,message,w,l);
            }
            if(gui)return DefWindowProcW(window,message,w,l);
        }
        switch(message){
        case WM_LBUTTONDOWN:case WM_LBUTTONUP:case WM_RBUTTONDOWN:case WM_RBUTTONUP:
        case WM_MBUTTONDOWN:case WM_MBUTTONUP:return 0;
        default:break;
        }
        if(gui){
            if(message==WM_CHAR){
                uint32_t ch=static_cast<uint32_t>(w);
                if(ch>=0xd800&&ch<=0xdbff){highSurrogate=static_cast<wchar_t>(ch);return 0;}
                if(ch>=0xdc00&&ch<=0xdfff&&highSurrogate){ch=0x10000+((uint32_t(highSurrogate)-0xd800)<<10)+(ch-0xdc00);}
                highSurrogate=0;
                if(ch>=32&&ch<=0x10ffff&&!(ch>=0xd800&&ch<=0xdfff))push(5,static_cast<int>(ch));
                return 0;
            }
            switch(message){
            case WM_KEYDOWN:case WM_KEYUP:case WM_LBUTTONDOWN:case WM_LBUTTONUP:
            case WM_RBUTTONDOWN:case WM_RBUTTONUP:case WM_MBUTTONDOWN:case WM_MBUTTONUP:return 0;
            default:break;
            }
        }
    }
    auto original=previous.load();return original?CallWindowProcW(original,window,message,w,l):DefWindowProcW(window,message,w,l);
}
}
void Attach(HWND window){
    if(!window||!IsWindow(window)||(attached&&IsWindow(attached)))return;
    auto original=reinterpret_cast<WNDPROC>(GetWindowLongPtrW(window,GWLP_WNDPROC));
    if(!original||original==procedure)return;
    previous=original;SetLastError(0);
    auto result=SetWindowLongPtrW(window,GWLP_WNDPROC,reinterpret_cast<LONG_PTR>(&procedure));
    if(result||GetLastError()==0)attached=window;
}
void Stop(){
    active=false;gui=false;
    if(attached&&IsWindow(attached)&&reinterpret_cast<WNDPROC>(GetWindowLongPtrW(attached,GWLP_WNDPROC))==procedure){
        SetWindowLongPtrW(attached,GWLP_WNDPROC,reinterpret_cast<LONG_PTR>(previous.load()));attached=nullptr;
    }
    // If another overlay chains us, remain dormant and resident until process exit.
    std::scoped_lock lock(inputMutex);events.clear();lookX=lookY=0;
}
int InstallHooks(){
    // Requires MH_Initialize. Trampolines are never removed, so in-flight SDL reads can always return.
    if(bufferTarget||dataTarget)return EnableHooks(true);
    if(MH_CreateHookApiEx(L"user32","GetRawInputBuffer",reinterpret_cast<LPVOID>(&hookedBuffer),
        reinterpret_cast<LPVOID*>(&originalBuffer),&bufferTarget)!=MH_OK){bufferTarget=nullptr;originalBuffer=nullptr;}
    if(MH_CreateHookApiEx(L"user32","GetRawInputData",reinterpret_cast<LPVOID>(&hookedData),
        reinterpret_cast<LPVOID*>(&originalData),&dataTarget)!=MH_OK){dataTarget=nullptr;originalData=nullptr;}
    return EnableHooks(true);
}
int EnableHooks(bool enable){
    int status=0;
    for(void* target:{bufferTarget,dataTarget}){
        if(!target){status|=enable?1:0;continue;}
        if((enable?MH_EnableHook(target):MH_DisableHook(target))!=MH_OK)status|=2;
    }
    hookStatus=enable?status:-1;
    return status;
}
void Stats(uint64_t& window,uint64_t& hooked,int& status){
    window=windowPackets.load();hooked=hookedPackets.load();status=hookStatus;
}
void Mode(bool enable,bool screen,int w,int h){
    std::scoped_lock lock(inputMutex);
    width=std::clamp(w,320,3840);height=std::clamp(h,200,2160);
    if(screen&&!gui){cursorX=float(width)/2;cursorY=float(height)/2;}
    if(!enable)events.clear();
    if(!enable||active!=enable||gui!=(enable&&screen))lookX=lookY=0;
    active=enable;gui=enable&&screen;
}
bool Cursor(int& x,int& y){
    std::scoped_lock lock(inputMutex);x=int(cursorX);y=int(cursorY);return active&&gui;
}
bool Look(int& x,int& y){
    std::scoped_lock lock(inputMutex);x=lookX;y=lookY;lookX=lookY=0;return active&&!gui;
}
void TestMouse(int x,int y){
    if(!wcsstr(GetCommandLineW(),L"-cs2craft-probe"))return;
    RAWMOUSE raw{};raw.lLastX=x;raw.lLastY=y;mouse(raw);
}
int SelfTest(){
    Mode(true,false,1280,720);RAWMOUSE raw{};raw.lLastX=90;raw.lLastY=-40;mouse(raw);
    int x{},y{};if(!Look(x,y)||x!=90||y!=-40)return 1;
    Look(x,y);if(x||y)return 2;
    mouse(raw);Mode(true,true,1280,720);Look(x,y);if(x||y)return 3;
    mouse(raw);if(!Cursor(x,y)||x!=730||y!=320)return 4;
    Mode(false,false,1280,720);if(Look(x,y)||x||y)return 5;
    return 0;
}
bool Pop(skycraft::proto::InputEvent& event){
    std::scoped_lock lock(inputMutex);if(events.empty())return false;
    event=events.front();events.pop_front();return true;
}
}
