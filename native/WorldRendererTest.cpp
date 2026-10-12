#include "WorldRenderer.h"
#include <Windows.h>
#include <array>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <filesystem>
#include <limits>
namespace p=skycraft::proto;
using Microsoft::WRL::ComPtr;
namespace {
struct Target {
    ComPtr<ID3D11Device> device;ComPtr<ID3D11DeviceContext> context;
    ComPtr<ID3D11Texture2D> color,staging;ComPtr<ID3D11RenderTargetView> view;
    UINT width{},height{};
    bool Initialize(UINT w,UINT h){
        width=w;height=h;
        if(FAILED(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context)))return false;
        D3D11_TEXTURE2D_DESC desc{};desc.Width=w;desc.Height=h;desc.MipLevels=desc.ArraySize=1;
        desc.Format=DXGI_FORMAT_R8G8B8A8_UNORM;desc.SampleDesc.Count=1;desc.BindFlags=D3D11_BIND_RENDER_TARGET;
        if(FAILED(device->CreateTexture2D(&desc,nullptr,&color))||FAILED(device->CreateRenderTargetView(color.Get(),nullptr,&view)))return false;
        desc.BindFlags=0;desc.Usage=D3D11_USAGE_STAGING;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
        return SUCCEEDED(device->CreateTexture2D(&desc,nullptr,&staging));
    }
    void Clear(){const float clear[4]={0,0,0,1};context->ClearRenderTargetView(view.Get(),clear);}
    uint32_t Pixel(UINT x,UINT y){
        context->CopyResource(staging.Get(),color.Get());D3D11_MAPPED_SUBRESOURCE mapped{};
        if(FAILED(context->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped)))return 0;
        uint32_t rgba;memcpy(&rgba,static_cast<uint8_t*>(mapped.pData)+y*mapped.RowPitch+x*4,4);context->Unmap(staging.Get(),0);return rgba;
    }
    bool Save(const wchar_t* output){
        context->CopyResource(staging.Get(),color.Get());D3D11_MAPPED_SUBRESOURCE mapped{};
        if(FAILED(context->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped)))return false;
        std::ofstream file(std::filesystem::path(output),std::ios::binary);
        uint8_t header[18]={};header[2]=2;header[12]=uint8_t(width);header[13]=uint8_t(width>>8);
        header[14]=uint8_t(height);header[15]=uint8_t(height>>8);header[16]=32;header[17]=40;
        file.write(reinterpret_cast<char*>(header),18);
        for(UINT y=0;y<height;y++)for(UINT x=0;x<width;x++){
            auto* c=static_cast<uint8_t*>(mapped.pData)+y*mapped.RowPitch+x*4;uint8_t bgra[4]={c[2],c[1],c[0],c[3]};
            file.write(reinterpret_cast<char*>(bgra),4);
        }
        context->Unmap(staging.Get(),0);return bool(file);
    }
};
std::vector<uint8_t> section(float z,uint32_t color=0xffffffff,uint32_t flags=0){
    p::RenSection h{0,0,0,6};
    p::RenVertex vertices[]={
        {-1,-1,z,0.5f,0.5f,color,0xf0f,flags},{1,-1,z,0.5f,0.5f,color,0xf0f,flags},{1,1,z,0.5f,0.5f,color,0xf0f,flags},
        {-1,-1,z,0.5f,0.5f,color,0xf0f,flags},{1,1,z,0.5f,0.5f,color,0xf0f,flags},{-1,1,z,0.5f,0.5f,color,0xf0f,flags}};
    std::vector<uint8_t> data(sizeof h+sizeof vertices);memcpy(data.data(),&h,sizeof h);memcpy(data.data()+sizeof h,vertices,sizeof vertices);return data;
}
bool send(WorldRenderer& renderer,uint32_t type,const std::vector<uint8_t>& bytes){return renderer.Packet(type,bytes.data(),bytes.size());}
}
extern "C" __declspec(dllexport) int CS2Craft_WorldSelfTest(){
    Target target;if(!target.Initialize(128,128))return 1;
    WorldRenderer renderer;if(FAILED(renderer.Initialize(target.device.Get())))return 2;
    struct {p::RenAtlas h;uint32_t rgba;} atlas{{1,1},0xff0000ff};
    if(!renderer.Packet(p::kRenAtlas,&atlas,sizeof atlas))return 3;
    auto mesh=section(3);
    if(!send(renderer,p::kRenSection,mesh)||renderer.Sections()!=1)return 4;
    WorldRenderer::Camera camera{0,0,0,0,0,90};
    auto draw=[&](){target.Clear();renderer.Draw(target.view.Get(),128,128,camera,{},false);return target.Pixel(64,64);};
    if(draw()!=0xff0000ff)return 5; // Real texture + perspective + vertex layout.
    camera.yaw=90;if(draw()!=0xff000000)return 6;camera.yaw=0; // Turn away.
    auto bad=mesh;float nan=std::numeric_limits<float>::quiet_NaN();memcpy(bad.data()+sizeof(p::RenSection),&nan,4);
    if(send(renderer,p::kRenSection,bad)||draw()!=0xff0000ff)return 7; // Bad replacement preserves previous mesh.
    bad.resize(17);if(send(renderer,p::kRenSection,bad))return 8;
    struct {p::RenAtlasRegion h;uint32_t rgba;} update{{0,0,1,1},0xff00ff00};
    if(!renderer.Packet(p::kRenAtlasRegion,&update,sizeof update)||draw()!=0xff00ff00)return 9;
    update.h.x=1;if(renderer.Packet(p::kRenAtlasRegion,&update,sizeof update))return 10;
    float wall[]={-2,-2,1,2,-2,1,2,2,1,-2,-2,1,2,2,1,-2,2,1};
    if(!renderer.Occluder(0,0,0,wall,6)||draw()!=0xff000000)return 11;
    renderer.ClearOccluders();if(draw()!=0xff00ff00)return 12;
    p::RenSection remove{0,0,0,0};
    if(!renderer.Packet(p::kRenSection,&remove,sizeof remove)||renderer.Sections()!=0||draw()!=0xff000000)return 13;
    // Alpha test discards fully transparent texels.
    atlas.rgba=0x000000ff;renderer.Packet(p::kRenAtlas,&atlas,sizeof atlas);mesh=section(3,0xffffffff,1);
    if(!send(renderer,p::kRenSection,mesh)||draw()!=0xff000000)return 14;
    // Translucent packet uses straight alpha rather than treating it as an opaque surface.
    atlas.rgba=0x800000ff;renderer.Packet(p::kRenAtlas,&atlas,sizeof atlas);mesh=section(3,0xffffffff,2);send(renderer,p::kRenSection,mesh);
    uint32_t pixel=draw();if((pixel&255)<126||(pixel&255)>129||(pixel&0x00ffff00))return 15;
    // Entity texture/batches and avatar positioning use the same validated vertex format.
    renderer.Clear();struct {p::RenTexture h;uint32_t rgba;} skin{{7,1,1,0},0xffff0000};
    if(!renderer.Packet(p::kRenTexture,&skin,sizeof skin))return 16;
    p::RenAvatar av{1,6};p::RenBatch batch{7,0,6,0};mesh=section(3);
    std::vector<uint8_t> avatar(sizeof av+sizeof batch+6*sizeof(p::RenVertex));
    memcpy(avatar.data(),&av,sizeof av);memcpy(avatar.data()+sizeof av,&batch,sizeof batch);
    memcpy(avatar.data()+sizeof av+sizeof batch,mesh.data()+sizeof(p::RenSection),6*sizeof(p::RenVertex));
    if(!send(renderer,p::kRenAvatar,avatar))return 17;
    target.Clear();renderer.Draw(target.view.Get(),128,128,camera,{},true);if(target.Pixel(64,64)!=0xffff0000)return 18;
    batch.first=6;memcpy(avatar.data()+sizeof av,&batch,sizeof batch);if(send(renderer,p::kRenAvatar,avatar))return 19;
    if(renderer.Packet(p::kRenClearAll,nullptr,0)==false||renderer.GetStats().meshBytes||renderer.GetStats().textureBytes)return 20;
    // World entities (arrows, dropped items) and the block outline are drawn from the per-frame table.
    atlas.rgba=0xff0000ff;if(!renderer.Packet(p::kRenAtlas,&atlas,sizeof atlas))return 21;
    p::WorldEntity item{};item.kind=p::kWeItem;item.z=3;item.scale=2;item.uv[0][2]=item.uv[0][3]=1;
    renderer.SetWorldEntities(&item,1,nullptr);if(draw()!=0xff0000ff)return 22;
    p::WorldEntity arrow{};arrow.kind=p::kWeArrow;arrow.z=3;arrow.yaw=90;arrow.scale=1;arrow.uv[0][2]=arrow.uv[0][3]=1;
    p::WorldEntity broken=arrow;broken.x=nan;p::WorldEntity pair[]={broken,arrow};
    renderer.SetWorldEntities(pair,2,nullptr);if(draw()!=0xff0000ff)return 23; // NaN entity skipped, arrow's vertical plane drawn.
    float selection[]={-0.5f,-0.5f,2.5f,0.5f,0.5f,3.5f};
    renderer.SetWorldEntities(&item,1,selection);draw();bool outlined=false;
    for(UINT x=74;x<=80;x++){uint32_t c=target.Pixel(x,64);if((c&255)>0x80&&(c&255)<0xc0)outlined=true;}
    if(!outlined)return 24; // 40% black line over the red sprite near the box's projected edge.
    if(!renderer.Occluder(0,0,0,wall,6)||(renderer.SetWorldEntities(&item,1,nullptr),draw())!=0xff000000)return 25; // Walls hide entities.
    renderer.ClearOccluders();
    // A moving occluder (a CS2 player in front of a block) hides it; an empty update shows it again.
    if(!renderer.MovingOccluders(wall,6)||(renderer.SetWorldEntities(&item,1,nullptr),draw())!=0xff000000)return 26;
    if(!renderer.MovingOccluders(nullptr,0)||draw()!=0xff0000ff)return 27;
    wall[0]=nan;if(renderer.MovingOccluders(wall,6))return 28;
    return 0;
}
// Replay a bounded real-Minecraft packet capture into an offscreen image. This is not a CS2 screenshot.
extern "C" __declspec(dllexport) int CS2Craft_RenderFixture(const wchar_t* input,const wchar_t* output){
    std::ifstream file(std::filesystem::path(input),std::ios::binary);uint32_t magic{};
    file.read(reinterpret_cast<char*>(&magic),4);if(magic!=0x46523243)return 1;
    WorldRenderer::Camera camera;
    file.read(reinterpret_cast<char*>(&camera.x),8);file.read(reinterpret_cast<char*>(&camera.y),8);file.read(reinterpret_cast<char*>(&camera.z),8);
    file.read(reinterpret_cast<char*>(&camera.yaw),4);file.read(reinterpret_cast<char*>(&camera.pitch),4);file.read(reinterpret_cast<char*>(&camera.verticalFov),4);
    if(!file)return 2;
    Target target;if(!target.Initialize(1280,720))return 3;
    WorldRenderer renderer;if(FAILED(renderer.Initialize(target.device.Get())))return 4;
    uint64_t total=0;uint32_t count=0;
    while(file.peek()!=std::char_traits<char>::eof()){
        uint32_t type{},size{};file.read(reinterpret_cast<char*>(&type),4);file.read(reinterpret_cast<char*>(&size),4);
        if(!file||size>(64u<<20)||(total+=size)>(512ull<<20)||++count>16384)return 5;
        std::vector<uint8_t> bytes(size);if(size)file.read(reinterpret_cast<char*>(bytes.data()),size);
        if(!file||!send(renderer,type,bytes))return 6;
    }
    target.Clear();if(!renderer.Draw(target.view.Get(),1280,720,camera,{},false)||renderer.GetStats().draws==0)return 7;
    if(!target.Save(output))return 8;
    auto stats=renderer.GetStats();
    std::printf("Minecraft packet replay: %u packets, %zu sections, %llu draws, %llu mesh bytes\n",count,renderer.Sections(),stats.draws,stats.meshBytes);
    return 0;
}
