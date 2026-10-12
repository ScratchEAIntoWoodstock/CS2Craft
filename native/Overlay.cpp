// CS2Craft Direct3D 11 compositor, loaded by the local server plugin.
// World depth uses extracted map physics; final CS2 render-view/depth integration remains experimental.
#include <Windows.h>
#include <d3d11_1.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <MinHook.h>
#include <atomic>
#include <cstdint>
#include <cstring>
#include <mutex>
#include <cmath>
#include <algorithm>
#include <fstream>
#include <filesystem>
#include <string>
#include <set>
#include "WorldRenderer.h"
#include "InputRouter.h"
#include "../third_party/skycraft_protocol_v10.h"
using Microsoft::WRL::ComPtr;
namespace p = skycraft::proto;
namespace {
using PresentFn=HRESULT(WINAPI*)(IDXGISwapChain*,UINT,UINT);
PresentFn original{};
void* target{};
std::mutex mutex;
std::atomic<bool> enabled{false};
std::atomic<uint64_t> packets{0},frames{0};
HANDLE mapping{};
uint8_t* memory{};
int front=2;
WorldRenderer world;
WorldRenderer::Camera camera;
uint64_t cameraTime{};
bool sceneEnabled{},occlusionEnabled{},showAvatar{};
DirectX::XMFLOAT3 avatarFeet{};
std::map<std::tuple<int,int,int>,std::vector<float>> hostGeometry;
bool geometryDirty{};
std::set<std::tuple<int,int,int>> geometryChanges;
uint64_t worldFrames{};
std::wstring capturePath;
int captureStatus{};

ComPtr<ID3D11Device> device;
ComPtr<ID3D11DeviceContext> context;
ComPtr<ID3D11DeviceContext1> context1;
ComPtr<ID3DDeviceContextState> cleanState;
ComPtr<ID3D11VertexShader> vs;
ComPtr<ID3D11PixelShader> ps;
ComPtr<ID3D11BlendState> blend;
ComPtr<ID3D11DepthStencilState> depth;
ComPtr<ID3D11RasterizerState> raster;
ComPtr<ID3D11SamplerState> sampler;
ComPtr<ID3D11Texture2D> texture;
ComPtr<ID3D11ShaderResourceView> textureView;
ComPtr<ID3D11Buffer> constants;
UINT texW{},texH{};
bool flip{},haveFrame{};
constexpr char shader[]=R"(
cbuffer Params:register(b0){float flipY;float cursorX;float cursorY;float cursorVisible;}
Texture2D image:register(t0);SamplerState pointSampler:register(s0);
struct V {float4 p:SV_Position;float2 uv:TEXCOORD0;};
V VS(uint id:SV_VertexID){
    V o;float2 uv=float2((id<<1)&2,id&2);
    o.p=float4(uv*float2(2,-2)+float2(-1,1),0,1);
    o.uv=uv;return o;
}
float4 PS(V i):SV_Target{
    if(cursorVisible>0.5){
        float2 delta=abs(i.p.xy-float2(cursorX,cursorY));
        if(max(delta.x,delta.y)<8&&min(delta.x,delta.y)<2.5)
            return min(delta.x,delta.y)<1.2?float4(1,1,1,1):float4(0,0,0,1);
    }
    if(flipY>0.5)i.uv.y=1-i.uv.y;
    return image.Sample(pointSampler,i.uv);
}
)";
template<class T>T read(uint64_t offset){T value;memcpy(&value,memory+offset,sizeof(value));return value;}
uint64_t load64(uint64_t offset){return static_cast<uint64_t>(InterlockedCompareExchange64(reinterpret_cast<volatile LONG64*>(memory+offset),0,0));}
uint32_t load32(uint64_t offset){return static_cast<uint32_t>(InterlockedCompareExchange(reinterpret_cast<volatile LONG*>(memory+offset),0,0));}
void closeLink(){
    if(memory){UnmapViewOfFile(memory);memory=nullptr;}
    if(mapping){CloseHandle(mapping);mapping=nullptr;}
    haveFrame=false;front=2;world.Clear();InputRouter::Mode(false,false,1280,720);
}
bool connect(){
    if(!memory){
        mapping=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,L"Local\\CS2Craft_v1");
        if(!mapping)return false;
        memory=static_cast<uint8_t*>(MapViewOfFile(mapping,FILE_MAP_ALL_ACCESS,0,0,p::kMappingBytes));
        if(!memory){CloseHandle(mapping);mapping=nullptr;return false;}
        if(read<uint32_t>(0)!=p::kMagic || read<uint32_t>(4)!=p::kVersion){closeLink();return false;}
    }
    const auto now=GetTickCount64(), beat=load64(0x10);
    if(beat==0 || now<beat || now-beat>3000){closeLink();return false;}
    return true;
}
HRESULT compile(const char* entry,const char* profile,ID3DBlob** result){
    ComPtr<ID3DBlob> errors;
    return D3DCompile(shader,sizeof(shader)-1,"CS2Craft HUD",nullptr,nullptr,entry,profile,
        D3DCOMPILE_OPTIMIZATION_LEVEL3,0,result,&errors);
}
HRESULT initialize(ID3D11Device* d){
    device=d;device->GetImmediateContext(&context);
    HRESULT hr=context.As(&context1);if(FAILED(hr))return hr;
    ComPtr<ID3D11Device1> d1;hr=device.As(&d1);if(FAILED(hr))return hr;
    D3D_FEATURE_LEVEL feature=D3D_FEATURE_LEVEL_11_0, chosen{};
    hr=d1->CreateDeviceContextState(0,&feature,1,D3D11_SDK_VERSION,__uuidof(ID3D11Device),&chosen,&cleanState);
    if(FAILED(hr))return hr;
    ComPtr<ID3DBlob> v,pixel;
    if(FAILED(hr=compile("VS","vs_5_0",&v)) || FAILED(hr=compile("PS","ps_5_0",&pixel)))return hr;
    if(FAILED(hr=device->CreateVertexShader(v->GetBufferPointer(),v->GetBufferSize(),nullptr,&vs)))return hr;
    if(FAILED(hr=device->CreatePixelShader(pixel->GetBufferPointer(),pixel->GetBufferSize(),nullptr,&ps)))return hr;
    D3D11_BLEND_DESC bd{};auto& rt=bd.RenderTarget[0];rt.BlendEnable=TRUE;
    rt.SrcBlend=D3D11_BLEND_ONE;rt.DestBlend=D3D11_BLEND_INV_SRC_ALPHA;rt.BlendOp=D3D11_BLEND_OP_ADD;
    rt.SrcBlendAlpha=D3D11_BLEND_ONE;rt.DestBlendAlpha=D3D11_BLEND_INV_SRC_ALPHA;rt.BlendOpAlpha=D3D11_BLEND_OP_ADD;
    rt.RenderTargetWriteMask=D3D11_COLOR_WRITE_ENABLE_ALL;
    if(FAILED(hr=device->CreateBlendState(&bd,&blend)))return hr;
    D3D11_DEPTH_STENCIL_DESC dd{};dd.DepthEnable=FALSE;dd.DepthWriteMask=D3D11_DEPTH_WRITE_MASK_ZERO;dd.DepthFunc=D3D11_COMPARISON_ALWAYS;
    if(FAILED(hr=device->CreateDepthStencilState(&dd,&depth)))return hr;
    D3D11_RASTERIZER_DESC rd{};rd.FillMode=D3D11_FILL_SOLID;rd.CullMode=D3D11_CULL_NONE;rd.DepthClipEnable=TRUE;
    if(FAILED(hr=device->CreateRasterizerState(&rd,&raster)))return hr;
    D3D11_SAMPLER_DESC sd{};sd.Filter=D3D11_FILTER_MIN_MAG_MIP_POINT;sd.AddressU=sd.AddressV=sd.AddressW=D3D11_TEXTURE_ADDRESS_CLAMP;
    sd.MaxLOD=D3D11_FLOAT32_MAX;
    if(FAILED(hr=device->CreateSamplerState(&sd,&sampler)))return hr;
    D3D11_BUFFER_DESC cb{};cb.ByteWidth=16;cb.Usage=D3D11_USAGE_DEFAULT;cb.BindFlags=D3D11_BIND_CONSTANT_BUFFER;
    if(FAILED(hr=device->CreateBuffer(&cb,nullptr,&constants)))return hr;
    geometryDirty=true;return world.Initialize(device.Get());
}
void resetGraphics(){
    world.Reset();
    constants.Reset();textureView.Reset();texture.Reset();sampler.Reset();raster.Reset();depth.Reset();
    blend.Reset();ps.Reset();vs.Reset();cleanState.Reset();context1.Reset();context.Reset();device.Reset();
    texW=texH=0;haveFrame=false;
}
bool drainRender(){
    uint64_t head=load64(p::kOffRenderRing),tail=load64(p::kOffRenderRing+64);
    constexpr uint64_t capacity=p::kRenderRingBytes-128;
    if(head<tail || head-tail>capacity)return false;
    for(int i=0;tail<head && i<4096;i++){
        const uint64_t pos=tail%capacity,offset=p::kOffRenderRing+128+pos;
        uint32_t type=read<uint32_t>(offset),size=read<uint32_t>(offset+4);
        if(type==0){uint64_t pad=capacity-pos;if(pad>head-tail)return false;tail+=pad;continue;}
        uint64_t total=(8ull+size+7)&~7ull;
        if(total>capacity || pos+total>capacity || total>head-tail)return false;
        world.Packet(type,memory+offset+8,size);
        tail+=total;packets++;
    }
    InterlockedExchange64(reinterpret_cast<volatile LONG64*>(memory+p::kOffRenderRing+64),static_cast<LONG64>(tail));
    return true;
}
bool upload(){
    if((load32(p::kOffOverlayCtl)&p::kOverlayDirty)==0)return haveFrame;
    LONG old=InterlockedExchange(reinterpret_cast<volatile LONG*>(memory+p::kOffOverlayCtl),front);
    front=old&3;if(front>2)return false;
    uint64_t h=p::kOffOverlaySlotHdr+uint64_t(front)*64;
    UINT w=read<UINT>(h),height=read<UINT>(h+4);
    if(w==0 || height==0 || w>p::kMaxOverlayW || height>p::kMaxOverlayH)return false;
    flip=(read<UINT>(h+8)&1)!=0;
    if(w!=texW || height!=texH || !texture){
        textureView.Reset();texture.Reset();texW=texH=0;
        D3D11_TEXTURE2D_DESC td{};td.Width=w;td.Height=height;td.MipLevels=1;td.ArraySize=1;
        td.Format=DXGI_FORMAT_R8G8B8A8_UNORM;td.SampleDesc.Count=1;td.Usage=D3D11_USAGE_DEFAULT;td.BindFlags=D3D11_BIND_SHADER_RESOURCE;
        if(FAILED(device->CreateTexture2D(&td,nullptr,&texture)) || FAILED(device->CreateShaderResourceView(texture.Get(),nullptr,&textureView)))return false;
        texW=w;texH=height;
    }
    context->UpdateSubresource(texture.Get(),0,nullptr,memory+p::kOffOverlayPixels+uint64_t(front)*p::kOverlaySlotBytes,w*4,0);
    frames++;haveFrame=true;return true;
}
void draw(ID3D11RenderTargetView* targetView,UINT w,UINT h){
    // Save/restore the whole D3D11 pipeline, including stages unused by our draw.
    ComPtr<ID3DDeviceContextState> previous;
    context1->SwapDeviceContextState(cleanState.Get(),&previous);
    context->OMSetRenderTargets(1,&targetView,nullptr);
    const float factor[4]={0,0,0,0};
    context->OMSetBlendState(blend.Get(),factor,0xFFFFFFFF);
    context->OMSetDepthStencilState(depth.Get(),0);
    context->RSSetState(raster.Get());
    D3D11_VIEWPORT viewport{0,0,float(w),float(h),0,1};context->RSSetViewports(1,&viewport);
    context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    context->VSSetShader(vs.Get(),nullptr,0);context->PSSetShader(ps.Get(),nullptr,0);
    int cursorX{},cursorY{};bool cursor=InputRouter::Cursor(cursorX,cursorY);
    float params[4]={flip?1.f:0.f,float(cursorX)*float(w)/float(texW?texW:w),float(cursorY)*float(h)/float(texH?texH:h),cursor?1.f:0.f};
    context->UpdateSubresource(constants.Get(),0,nullptr,params,0,0);
    ID3D11Buffer* cb=constants.Get();context->PSSetConstantBuffers(0,1,&cb);
    auto image=textureView.Get();context->PSSetShaderResources(0,1,&image);
    auto sample=sampler.Get();context->PSSetSamplers(0,1,&sample);
    context->Draw(3,0);
    context1->SwapDeviceContextState(previous.Get(),nullptr);
}
void drawWorld(ID3D11RenderTargetView* targetView,UINT w,UINT h){
    if(!sceneEnabled || GetTickCount64()-cameraTime>500)return;
    const uint64_t mcBeat=load64(0x18),now=GetTickCount64();
    if(!mcBeat || now<mcBeat || now-mcBeat>3000)return;
    if(geometryDirty){
        world.ClearOccluders();
        if(occlusionEnabled)for(auto& [key,vertices]:hostGeometry)
            world.Occluder(std::get<0>(key),std::get<1>(key),std::get<2>(key),vertices.data(),static_cast<uint32_t>(vertices.size()/3));
        geometryDirty=false;geometryChanges.clear();
    } else if(occlusionEnabled) {
        for(const auto& key:geometryChanges){
            auto it=hostGeometry.find(key);
            if(it==hostGeometry.end())world.Occluder(std::get<0>(key),std::get<1>(key),std::get<2>(key),nullptr,0);
            else world.Occluder(std::get<0>(key),std::get<1>(key),std::get<2>(key),it->second.data(),static_cast<uint32_t>(it->second.size()/3));
        }
        geometryChanges.clear();
    }
    // Camera entity snapshot shared with CS2; final client render interpolation can still differ.
    ComPtr<ID3DDeviceContextState> previous;
    context1->SwapDeviceContextState(cleanState.Get(),&previous);
    if(world.Draw(targetView,w,h,camera,avatarFeet,showAvatar))worldFrames++;
    context1->SwapDeviceContextState(previous.Get(),nullptr);
}
void captureBackbuffer(ID3D11Texture2D* buffer){
    if(capturePath.empty())return;
    std::wstring path=std::move(capturePath);capturePath.clear();captureStatus=-1;
    D3D11_TEXTURE2D_DESC desc{};buffer->GetDesc(&desc);
    bool bgra=desc.Format==DXGI_FORMAT_B8G8R8A8_UNORM||desc.Format==DXGI_FORMAT_B8G8R8A8_UNORM_SRGB;
    bool rgba=desc.Format==DXGI_FORMAT_R8G8B8A8_UNORM||desc.Format==DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;
    if((!bgra&&!rgba)||desc.SampleDesc.Count!=1){captureStatus=-2;return;}
    desc.BindFlags=0;desc.MiscFlags=0;desc.Usage=D3D11_USAGE_STAGING;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
    ComPtr<ID3D11Texture2D> staging;
    if(FAILED(device->CreateTexture2D(&desc,nullptr,&staging)))return;
    context->CopyResource(staging.Get(),buffer);D3D11_MAPPED_SUBRESOURCE mapped{};
    if(FAILED(context->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped)))return;
    std::ofstream file(std::filesystem::path(path),std::ios::binary);
    uint8_t header[18]={};header[2]=2;header[12]=uint8_t(desc.Width);header[13]=uint8_t(desc.Width>>8);
    header[14]=uint8_t(desc.Height);header[15]=uint8_t(desc.Height>>8);header[16]=32;header[17]=40;
    file.write(reinterpret_cast<char*>(header),18);
    std::vector<uint8_t> row(desc.Width*4);
    for(UINT y=0;y<desc.Height;y++){
        const auto* source=static_cast<const uint8_t*>(mapped.pData)+y*mapped.RowPitch;
        for(UINT x=0;x<desc.Width;x++){row[x*4]=source[x*4+(bgra?0:2)];row[x*4+1]=source[x*4+1];row[x*4+2]=source[x*4+(bgra?2:0)];row[x*4+3]=255;}
        file.write(reinterpret_cast<const char*>(row.data()),row.size());
    }
    context->Unmap(staging.Get(),0);captureStatus=file?1:-3;
}
HRESULT WINAPI present(IDXGISwapChain* swap,UINT interval,UINT flags){
    {
        std::scoped_lock lock(mutex);
        if(enabled && !(flags&DXGI_PRESENT_TEST) && connect()){
            ComPtr<ID3D11Device> d;
            if(SUCCEEDED(swap->GetDevice(IID_PPV_ARGS(&d)))){
                if(device.Get()!=d.Get()){resetGraphics();if(FAILED(initialize(d.Get())))resetGraphics();}
                DXGI_SWAP_CHAIN_DESC swapDesc{};if(SUCCEEDED(swap->GetDesc(&swapDesc)))InputRouter::Attach(swapDesc.OutputWindow);
                uint64_t mcBeat=load64(0x18),now=GetTickCount64();
                bool mcAlive=mcBeat&&now>=mcBeat&&now-mcBeat<3000;
                if(!mcAlive)InputRouter::Mode(false,false,1280,720);
                if(device && drainRender() && mcAlive && (load32(p::kOffSkyState+4)&p::kSkyInGame)){
                    bool hudReady=upload();
                    ComPtr<ID3D11Texture2D> buffer;ComPtr<ID3D11RenderTargetView> view;
                    if(SUCCEEDED(swap->GetBuffer(0,IID_PPV_ARGS(&buffer))) && SUCCEEDED(device->CreateRenderTargetView(buffer.Get(),nullptr,&view))){
                        D3D11_TEXTURE2D_DESC desc{};buffer->GetDesc(&desc);
                        drawWorld(view.Get(),desc.Width,desc.Height);
                        if(hudReady)draw(view.Get(),desc.Width,desc.Height);
                        captureBackbuffer(buffer.Get());
                    }
                }
            }
        }
    }
    return original(swap,interval,flags);
}
}
extern "C" __declspec(dllexport) int CS2Craft_Start(){
    // This library runs only when explicitly loaded by the local mod.
    if(!wcsstr(GetCommandLineW(),L"-insecure") || wcsstr(GetCommandLineW(),L"-dedicated"))return 1;
    std::scoped_lock lock(mutex);
    if(enabled)return 0;
    // Keep disabled trampolines alive across hot reloads: a callback already in flight can still call original.
    if(target && original){
        if(MH_EnableHook(target)!=MH_OK)return 6;
        InputRouter::InstallHooks();
        enabled=true;return 0;
    }
    HWND window=CreateWindowExW(0,L"STATIC",L"CS2Craft D3D probe",WS_OVERLAPPED,0,0,32,32,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
    if(!window)return 2;
    DXGI_SWAP_CHAIN_DESC desc{};desc.BufferCount=1;desc.BufferDesc.Format=DXGI_FORMAT_R8G8B8A8_UNORM;
    desc.BufferDesc.Width=32;desc.BufferDesc.Height=32;desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.OutputWindow=window;desc.SampleDesc.Count=1;desc.Windowed=TRUE;desc.SwapEffect=DXGI_SWAP_EFFECT_DISCARD;
    ComPtr<IDXGISwapChain> swap;ComPtr<ID3D11Device> dummy;ComPtr<ID3D11DeviceContext> dc;
    D3D_FEATURE_LEVEL fl{};
    HRESULT hr=D3D11CreateDeviceAndSwapChain(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&desc,&swap,&dummy,&fl,&dc);
    if(FAILED(hr)){DestroyWindow(window);return 3;}
    target=(*reinterpret_cast<void***>(swap.Get()))[8];
    auto status=MH_Initialize();
    if(status!=MH_OK && status!=MH_ERROR_ALREADY_INITIALIZED){DestroyWindow(window);return 4;}
    if(MH_CreateHook(target,reinterpret_cast<void*>(&present),reinterpret_cast<void**>(&original))!=MH_OK){target=nullptr;original=nullptr;DestroyWindow(window);return 5;}
    if(MH_EnableHook(target)!=MH_OK){MH_RemoveHook(target);target=nullptr;original=nullptr;DestroyWindow(window);return 6;}
    // Raw-input hooks are best effort: the window subclass still works if they fail (see CS2Craft_InputStats).
    InputRouter::InstallHooks();
    enabled=true;DestroyWindow(window);return 0;
}
extern "C" __declspec(dllexport) void CS2Craft_Stop(){
    enabled=false;
    if(target)MH_DisableHook(target);
    InputRouter::EnableHooks(false);
    std::scoped_lock lock(mutex);
    // Do not free the trampoline while a Present callback may be returning to it.
    InputRouter::Stop();
    closeLink();resetGraphics();hostGeometry.clear();geometryChanges.clear();sceneEnabled=false;capturePath.clear();
    // The host retains the DLL until process exit, so an in-flight Present can return safely.
}
extern "C" __declspec(dllexport) uint64_t CS2Craft_OverlayFrames(){return frames.load();}
extern "C" __declspec(dllexport) uint64_t CS2Craft_RenderPackets(){return packets.load();}
// WARP test: render a known RGBA pixel through the real shader and read it back.
extern "C" __declspec(dllexport) int CS2Craft_SelfTest(){
    std::scoped_lock lock(mutex);if(enabled)return 1;
    ComPtr<ID3D11Device> d;ComPtr<ID3D11DeviceContext> ctx;
    HRESULT hr=D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&d,nullptr,&ctx);
    if(FAILED(hr) || FAILED(initialize(d.Get()))){resetGraphics();return 2;}
    D3D11_TEXTURE2D_DESC td{};td.Width=td.Height=1;td.MipLevels=td.ArraySize=1;
    td.Format=DXGI_FORMAT_R8G8B8A8_UNORM;td.SampleDesc.Count=1;td.BindFlags=D3D11_BIND_SHADER_RESOURCE;
    uint32_t rgba=0xFF332211;D3D11_SUBRESOURCE_DATA initial{&rgba,4,0};
    if(FAILED(d->CreateTexture2D(&td,&initial,&texture)) || FAILED(d->CreateShaderResourceView(texture.Get(),nullptr,&textureView))){resetGraphics();return 3;}
    td.BindFlags=D3D11_BIND_RENDER_TARGET;
    ComPtr<ID3D11Texture2D> dest;ComPtr<ID3D11RenderTargetView> rtv;
    if(FAILED(d->CreateTexture2D(&td,nullptr,&dest)) || FAILED(d->CreateRenderTargetView(dest.Get(),nullptr,&rtv))){resetGraphics();return 4;}
    flip=false;draw(rtv.Get(),1,1);
    td.BindFlags=0;td.Usage=D3D11_USAGE_STAGING;td.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
    ComPtr<ID3D11Texture2D> staging;
    if(FAILED(d->CreateTexture2D(&td,nullptr,&staging))){resetGraphics();return 5;}
    ctx->CopyResource(staging.Get(),dest.Get());D3D11_MAPPED_SUBRESOURCE mapped{};
    if(FAILED(ctx->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped))){resetGraphics();return 6;}
    uint32_t result;memcpy(&result,mapped.pData,4);ctx->Unmap(staging.Get(),0);resetGraphics();
    return result==rgba?0:7;
}

extern "C" __declspec(dllexport) void CS2Craft_SetCamera(double x,double y,double z,float yaw,float pitch,float verticalFov,uint32_t flags){
    std::scoped_lock lock(mutex);
    bool active=(flags&1)!=0;
    bool occlusion=(flags&2)!=0;
    if(occlusion!=occlusionEnabled){occlusionEnabled=occlusion;geometryDirty=true;}
    if(!active){sceneEnabled=false;hostGeometry.clear();geometryChanges.clear();world.ClearOccluders();return;}
    if(!std::isfinite(x)||!std::isfinite(y)||!std::isfinite(z)||!std::isfinite(yaw)||!std::isfinite(pitch)
       ||!std::isfinite(verticalFov)||verticalFov<5||verticalFov>170)return;
    camera={x,y,z,yaw,std::clamp(pitch,-89.9f,89.9f),verticalFov};cameraTime=GetTickCount64();sceneEnabled=true;
}
extern "C" __declspec(dllexport) void CS2Craft_SetOccluder(int x,int y,int z,const float* vertices,uint32_t count){
    std::scoped_lock lock(mutex);
    if(!sceneEnabled||count>393216||count%3||(!vertices&&count))return;
    auto key=std::make_tuple(x,y,z);
    if(!count){hostGeometry.erase(key);geometryChanges.insert(key);return;}
    for(uint32_t i=0;i<count*3;i++)if(!std::isfinite(vertices[i]))return;
    // Keep at most 128 regions, evicting the farthest sampled region when roaming.
    if(!hostGeometry.contains(key)&&hostGeometry.size()>=128){
        auto farthest=hostGeometry.begin();double maximum=-1;
        for(auto it=hostGeometry.begin();it!=hostGeometry.end();++it){
            auto [rx,ry,rz]=it->first;double dx=rx*8.0+4-camera.x,dy=ry*8.0+4-camera.y,dz=rz*8.0+4-camera.z;
            double distance=dx*dx+dy*dy+dz*dz;if(distance>maximum){maximum=distance;farthest=it;}
        }
        geometryChanges.insert(farthest->first);hostGeometry.erase(farthest);
    }
    hostGeometry[key]=std::vector<float>(vertices,vertices+count*3);geometryChanges.insert(key);
}
extern "C" __declspec(dllexport) uint64_t CS2Craft_WorldFrames(){std::scoped_lock lock(mutex);return worldFrames;}
extern "C" __declspec(dllexport) uint64_t CS2Craft_WorldSections(){std::scoped_lock lock(mutex);return world.Sections();}
extern "C" __declspec(dllexport) uint64_t CS2Craft_RejectedPackets(){std::scoped_lock lock(mutex);return world.GetStats().rejected;}

extern "C" __declspec(dllexport) void CS2Craft_RequestCapture(const wchar_t* path){
    std::scoped_lock lock(mutex);if(path){capturePath=path;captureStatus=0;}
}
extern "C" __declspec(dllexport) int CS2Craft_CaptureStatus(){std::scoped_lock lock(mutex);return captureStatus;}

extern "C" __declspec(dllexport) void CS2Craft_InputMode(int active,int gui,int width,int height){InputRouter::Mode(active!=0,gui!=0,width,height);}
extern "C" __declspec(dllexport) int CS2Craft_Cursor(int* x,int* y){return x&&y&&InputRouter::Cursor(*x,*y)?1:0;}
extern "C" __declspec(dllexport) int CS2Craft_PollInput(p::InputEvent* event){return event&&InputRouter::Pop(*event)?1:0;}

extern "C" __declspec(dllexport) void CS2Craft_SetAvatar(double x,double y,double z,int show){
    std::scoped_lock lock(mutex);
    showAvatar=show&&std::isfinite(x)&&std::isfinite(y)&&std::isfinite(z);
    avatarFeet={float(x),float(y),float(z)};
}

extern "C" __declspec(dllexport) int CS2Craft_Look(int* x,int* y){return x&&y&&InputRouter::Look(*x,*y)?1:0;}
extern "C" __declspec(dllexport) int CS2Craft_InputSelfTest(){return InputRouter::SelfTest();}
extern "C" __declspec(dllexport) void CS2Craft_InputStats(uint64_t* window,uint64_t* hooked,int* status){
    if(window&&hooked&&status)InputRouter::Stats(*window,*hooked,*status);
}

extern "C" __declspec(dllexport) void CS2Craft_TestMouse(int x,int y){InputRouter::TestMouse(x,y);}
