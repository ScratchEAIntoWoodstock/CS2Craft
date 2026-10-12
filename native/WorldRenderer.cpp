#include "WorldRenderer.h"
#include <d3dcompiler.h>
#include <algorithm>
#include <cmath>
#include <cstring>
#include <limits>
using namespace DirectX;
namespace p=skycraft::proto;
namespace {
constexpr uint64_t meshLimit=128ull<<20,textureLimit=256ull<<20;
constexpr uint32_t vertexLimit=1u<<20;
constexpr char shader[]=R"(
cbuffer Params:register(b0){row_major float4x4 viewProjection;float4 origin;}
Texture2D image:register(t0);SamplerState pointSampler:register(s0);
struct Input{float3 p:POSITION;float2 uv:TEXCOORD;float4 color:COLOR;uint light:LIGHT;uint flags:FLAGS;};
struct Vertex{float4 p:SV_Position;float2 uv:TEXCOORD;float4 color:COLOR;nointerpolation uint flags:FLAGS;};
Vertex VS(Input i){
    Vertex o;o.p=mul(float4(i.p+origin.xyz,1),viewProjection);o.uv=i.uv;o.flags=i.flags;
    float light=0.2+0.8*max(i.light&255,(i.light>>8)&255)/15.0;
    uint face=(i.flags>>4)&7;
    float shade=face==1?0.5:face==2?1:face==3||face==4?0.8:face==5||face==6?0.6:1;
    o.color=float4(i.color.rgb*saturate(light)*shade,i.color.a);return o;
}
float4 PS(Vertex i):SV_Target{
    float4 value=image.Sample(pointSampler,i.uv)*i.color;
    clip(value.a-((i.flags&1)!=0?0.1:0.003));
    return value;
}
)";
template<class T>T copy(const uint8_t* data){T result{};memcpy(&result,data,sizeof result);return result;}
bool finite(float v){return std::isfinite(v);}
bool finite(XMFLOAT3 p){return finite(p.x)&&finite(p.y)&&finite(p.z);}
struct alignas(16) Params {XMFLOAT4X4 matrix;XMFLOAT4 origin;};
}
HRESULT WorldRenderer::Initialize(ID3D11Device* d){
    Reset();device=d;device->GetImmediateContext(&context);
    Com<ID3DBlob> vb,pb,errors;HRESULT hr;
    if(FAILED(hr=D3DCompile(shader,sizeof(shader)-1,"CS2Craft world",nullptr,nullptr,"VS","vs_5_0",D3DCOMPILE_OPTIMIZATION_LEVEL3,0,&vb,&errors)))return hr;
    errors.Reset();
    if(FAILED(hr=D3DCompile(shader,sizeof(shader)-1,"CS2Craft world",nullptr,nullptr,"PS","ps_5_0",D3DCOMPILE_OPTIMIZATION_LEVEL3,0,&pb,&errors)))return hr;
    if(FAILED(hr=device->CreateVertexShader(vb->GetBufferPointer(),vb->GetBufferSize(),nullptr,&vs)))return hr;
    if(FAILED(hr=device->CreatePixelShader(pb->GetBufferPointer(),pb->GetBufferSize(),nullptr,&ps)))return hr;
    const D3D11_INPUT_ELEMENT_DESC elements[]={
        {"POSITION",0,DXGI_FORMAT_R32G32B32_FLOAT,0,0,D3D11_INPUT_PER_VERTEX_DATA,0},
        {"TEXCOORD",0,DXGI_FORMAT_R32G32_FLOAT,0,12,D3D11_INPUT_PER_VERTEX_DATA,0},
        {"COLOR",0,DXGI_FORMAT_R8G8B8A8_UNORM,0,20,D3D11_INPUT_PER_VERTEX_DATA,0},
        {"LIGHT",0,DXGI_FORMAT_R32_UINT,0,24,D3D11_INPUT_PER_VERTEX_DATA,0},
        {"FLAGS",0,DXGI_FORMAT_R32_UINT,0,28,D3D11_INPUT_PER_VERTEX_DATA,0}};
    if(FAILED(hr=device->CreateInputLayout(elements,static_cast<UINT>(std::size(elements)),vb->GetBufferPointer(),vb->GetBufferSize(),&layout)))return hr;
    D3D11_BUFFER_DESC cb{};cb.ByteWidth=sizeof(Params);cb.Usage=D3D11_USAGE_DEFAULT;cb.BindFlags=D3D11_BIND_CONSTANT_BUFFER;
    if(FAILED(hr=device->CreateBuffer(&cb,nullptr,&constants)))return hr;
    D3D11_SAMPLER_DESC sd{};sd.Filter=D3D11_FILTER_MIN_MAG_MIP_POINT;
    sd.AddressU=sd.AddressV=sd.AddressW=D3D11_TEXTURE_ADDRESS_CLAMP;sd.MaxLOD=D3D11_FLOAT32_MAX;
    if(FAILED(hr=device->CreateSamplerState(&sd,&sampler)))return hr;
    D3D11_BLEND_DESC bd{};auto& rt=bd.RenderTarget[0];rt.RenderTargetWriteMask=D3D11_COLOR_WRITE_ENABLE_ALL;
    if(FAILED(hr=device->CreateBlendState(&bd,&opaque)))return hr;
    rt.RenderTargetWriteMask=0;
    if(FAILED(hr=device->CreateBlendState(&bd,&depthOnly)))return hr;
    rt.RenderTargetWriteMask=D3D11_COLOR_WRITE_ENABLE_ALL;rt.BlendEnable=TRUE;
    rt.SrcBlend=D3D11_BLEND_SRC_ALPHA;rt.DestBlend=D3D11_BLEND_INV_SRC_ALPHA;rt.BlendOp=D3D11_BLEND_OP_ADD;
    rt.SrcBlendAlpha=D3D11_BLEND_ONE;rt.DestBlendAlpha=D3D11_BLEND_INV_SRC_ALPHA;rt.BlendOpAlpha=D3D11_BLEND_OP_ADD;
    if(FAILED(hr=device->CreateBlendState(&bd,&alpha)))return hr;
    D3D11_DEPTH_STENCIL_DESC dd{};dd.DepthEnable=TRUE;dd.DepthFunc=D3D11_COMPARISON_LESS_EQUAL;dd.DepthWriteMask=D3D11_DEPTH_WRITE_MASK_ALL;
    if(FAILED(hr=device->CreateDepthStencilState(&dd,&writeDepth)))return hr;
    dd.DepthWriteMask=D3D11_DEPTH_WRITE_MASK_ZERO;
    if(FAILED(hr=device->CreateDepthStencilState(&dd,&readDepth)))return hr;
    D3D11_RASTERIZER_DESC rd{};rd.FillMode=D3D11_FILL_SOLID;rd.CullMode=D3D11_CULL_NONE;rd.DepthClipEnable=TRUE;
    return device->CreateRasterizerState(&rd,&raster);
}
void WorldRenderer::DropMesh(Mesh& mesh){stats.meshBytes-=uint64_t(mesh.vertices)*sizeof(p::RenVertex);mesh={};}
void WorldRenderer::Clear(){
    for(auto& [key,m]:sections)DropMesh(m);sections.clear();
    DropMesh(avatar);DropMesh(scene);textures.clear();stats.textureBytes=0;
}
void WorldRenderer::ClearOccluders(){for(auto& [key,m]:occluders)DropMesh(m);occluders.clear();}
void WorldRenderer::Reset(){
    Clear();ClearOccluders();depthView.Reset();depthTexture.Reset();depthW=depthH=0;
    raster.Reset();readDepth.Reset();writeDepth.Reset();depthOnly.Reset();opaque.Reset();alpha.Reset();
    sampler.Reset();constants.Reset();layout.Reset();ps.Reset();vs.Reset();context.Reset();device.Reset();stats={};
}
bool WorldRenderer::UploadMesh(Mesh& out,const p::RenVertex* vertices,uint32_t count,std::vector<p::RenBatch> batches,XMFLOAT3 origin){
    if(!finite(origin)||count>vertexLimit||count%3)return false;
    for(auto b:batches)if(b.first>count||b.count>count-b.first||b.first%3||b.count%3)return false;
    const uint64_t bytes=uint64_t(count)*sizeof(p::RenVertex);
    if(stats.meshBytes-uint64_t(out.vertices)*sizeof(p::RenVertex)+bytes>meshLimit)return false;
    for(uint32_t i=0;i<count;i++){
        const auto& v=vertices[i];
        if(!finite(v.x)||!finite(v.y)||!finite(v.z)||!finite(v.u)||!finite(v.v)
            ||std::abs(v.x)>1e6f||std::abs(v.y)>1e6f||std::abs(v.z)>1e6f)return false;
    }
    Mesh next;next.origin=origin;next.vertices=count;next.batches=std::move(batches);
    if(count){
        D3D11_BUFFER_DESC bd{};bd.ByteWidth=static_cast<UINT>(bytes);bd.Usage=D3D11_USAGE_IMMUTABLE;bd.BindFlags=D3D11_BIND_VERTEX_BUFFER;
        D3D11_SUBRESOURCE_DATA initial{vertices,0,0};
        if(FAILED(device->CreateBuffer(&bd,&initial,&next.buffer)))return false;
    }
    DropMesh(out);out=std::move(next);stats.meshBytes+=bytes;return true;
}
bool WorldRenderer::UploadTexture(uint32_t id,UINT w,UINT h,const void* pixels,size_t bytes){
    const uint64_t size=uint64_t(w)*h*4;
    if(!w||!h||w>8192||h>8192||size!=bytes)return false;
    auto old=textures.find(id);uint64_t previous=old==textures.end()?0:uint64_t(old->second.w)*old->second.h*4;
    if(stats.textureBytes-previous+size>textureLimit||(old==textures.end()&&textures.size()>=512))return false;
    Texture t;t.w=w;t.h=h;
    D3D11_TEXTURE2D_DESC td{};td.Width=w;td.Height=h;td.MipLevels=td.ArraySize=1;
    td.Format=DXGI_FORMAT_R8G8B8A8_UNORM;td.SampleDesc.Count=1;td.BindFlags=D3D11_BIND_SHADER_RESOURCE;td.Usage=D3D11_USAGE_DEFAULT;
    D3D11_SUBRESOURCE_DATA initial{pixels,w*4,0};
    if(FAILED(device->CreateTexture2D(&td,&initial,&t.texture))||FAILED(device->CreateShaderResourceView(t.texture.Get(),nullptr,&t.view)))return false;
    textures[id]=std::move(t);stats.textureBytes=stats.textureBytes-previous+size;return true;
}
bool WorldRenderer::Packet(uint32_t type,const void* data,size_t bytes){
    if(!device||(!data&&bytes)){stats.rejected++;return false;}
    bool ok=Consume(type,static_cast<const uint8_t*>(data),bytes);
    if(ok)stats.accepted++;else stats.rejected++;
    return ok;
}
bool WorldRenderer::Consume(uint32_t type,const uint8_t* data,size_t bytes){
    switch(type){
    case p::kRenClearAll: if(bytes)return false;Clear();return true;
    case p::kRenAtlas:{
        if(bytes<sizeof(p::RenAtlas))return false;
        auto h=copy<p::RenAtlas>(data);return UploadTexture(0,h.width,h.height,data+sizeof h,bytes-sizeof h);
    }
    case p::kRenTexture:{
        if(bytes<sizeof(p::RenTexture))return false;
        auto h=copy<p::RenTexture>(data);
        return h.id&&UploadTexture(h.id,h.width,h.height,data+sizeof h,bytes-sizeof h);
    }
    case p::kRenAtlasRegion:{
        if(bytes<sizeof(p::RenAtlasRegion))return false;
        auto h=copy<p::RenAtlasRegion>(data);auto it=textures.find(0);
        if(it==textures.end()||!h.width||!h.height||h.x>it->second.w||h.y>it->second.h
          ||h.width>it->second.w-h.x||h.height>it->second.h-h.y||uint64_t(h.width)*h.height*4!=bytes-sizeof h)return false;
        D3D11_BOX box{h.x,h.y,0,h.x+h.width,h.y+h.height,1};
        context->UpdateSubresource(it->second.texture.Get(),0,&box,data+sizeof h,h.width*4,0);return true;
    }
    case p::kRenSection:{
        if(bytes<sizeof(p::RenSection))return false;
        auto h=copy<p::RenSection>(data);
        if(h.vertexCount>vertexLimit||h.vertexCount%3||sizeof h+uint64_t(h.vertexCount)*sizeof(p::RenVertex)!=bytes
           ||std::abs(int64_t(h.sx))>62500||std::abs(int64_t(h.sy))>62500||std::abs(int64_t(h.sz))>62500)return false;
        Key key{h.sx,h.sy,h.sz};auto it=sections.find(key);
        if(!h.vertexCount){if(it!=sections.end()){DropMesh(it->second);sections.erase(it);}return true;}
        if(it==sections.end()&&sections.size()>=4096)return false;
        // Preserve order within each pass; put opaque triangles before transparent triangles.
        std::vector<p::RenVertex> input(h.vertexCount),sorted;memcpy(input.data(),data+sizeof h,h.vertexCount*sizeof(p::RenVertex));sorted.reserve(h.vertexCount);
        for(uint32_t i=0;i<h.vertexCount;i+=3)if(!(input[i].flags&2))sorted.insert(sorted.end(),input.begin()+i,input.begin()+i+3);
        uint32_t solid=static_cast<uint32_t>(sorted.size());
        for(uint32_t i=0;i<h.vertexCount;i+=3)if(input[i].flags&2)sorted.insert(sorted.end(),input.begin()+i,input.begin()+i+3);
        std::vector<p::RenBatch> batches;
        if(solid)batches.push_back({0,0,solid,0});
        if(solid<h.vertexCount)batches.push_back({0,solid,h.vertexCount-solid,1});
        Mesh next;
        // Replacing a section must not reserve twice its memory budget.
        auto& destination=sections[key];
        bool result=UploadMesh(destination,sorted.data(),h.vertexCount,std::move(batches),{float(h.sx)*16,float(h.sy)*16,float(h.sz)*16});
        if(!result&&destination.vertices==0)sections.erase(key);
        return result;
    }
    case p::kRenAvatar:
    case p::kRenScene:{
        bool isScene=type==p::kRenScene;size_t header=isScene?sizeof(p::RenScene):sizeof(p::RenAvatar);
        if(bytes<header)return false;
        uint32_t bc,vc;XMFLOAT3 origin{};
        if(isScene){auto h=copy<p::RenScene>(data);bc=h.batchCount;vc=h.vertexCount;
            if(!std::isfinite(h.originX)||!std::isfinite(h.originY)||!std::isfinite(h.originZ)
              ||std::abs(h.originX)>1e6||std::abs(h.originY)>1e6||std::abs(h.originZ)>1e6)return false;
            origin={float(h.originX),float(h.originY),float(h.originZ)};}
        else{auto h=copy<p::RenAvatar>(data);bc=h.batchCount;vc=h.vertexCount;}
        if(vc>vertexLimit||bc>4096||header+uint64_t(bc)*sizeof(p::RenBatch)+uint64_t(vc)*sizeof(p::RenVertex)!=bytes)return false;
        std::vector<p::RenBatch> batches(bc);std::vector<p::RenVertex> vertices(vc);
        if(bc)memcpy(batches.data(),data+header,bc*sizeof(p::RenBatch));
        if(vc)memcpy(vertices.data(),data+header+bc*sizeof(p::RenBatch),vc*sizeof(p::RenVertex));
        return UploadMesh(isScene?scene:avatar,vertices.data(),vc,std::move(batches),origin);
    }
    // Known optional physics/lighting channels are consumed but not used by this renderer.
    case p::kRenLights:case p::kRenSolids:case p::kRenRagdoll:return true;
    default:return false;
    }
}
bool WorldRenderer::Occluder(int x,int y,int z,const float* xyz,uint32_t count){
    if(!device||count>393216||count%3||(!xyz&&count))return false;
    Key key{x,y,z};auto it=occluders.find(key);
    if(!count){if(it!=occluders.end()){DropMesh(it->second);occluders.erase(it);}return true;}
    if(it==occluders.end()&&occluders.size()>=128)return false;
    std::vector<p::RenVertex> vertices(count);
    for(uint32_t i=0;i<count;i++)vertices[i]={xyz[i*3],xyz[i*3+1],xyz[i*3+2],0,0,0xffffffff,0xf0f,0};
    auto& destination=occluders[key];
    bool ok=UploadMesh(destination,vertices.data(),count,{{0,0,count,0}},{});
    if(!ok&&destination.vertices==0)occluders.erase(key);
    return ok;
}
bool WorldRenderer::Draw(ID3D11RenderTargetView* target,UINT w,UINT h,const Camera& camera,const XMFLOAT3& feet,bool showAvatar){
    if(!device||!w||!h||w>8192||h>8192||!std::isfinite(camera.x)||!std::isfinite(camera.y)||!std::isfinite(camera.z)
      ||!finite(camera.yaw)||!finite(camera.pitch)||!finite(camera.verticalFov)||camera.verticalFov<5||camera.verticalFov>170)return false;
    if(w!=depthW||h!=depthH){
        depthView.Reset();depthTexture.Reset();depthW=depthH=0;
        D3D11_TEXTURE2D_DESC td{};td.Width=w;td.Height=h;td.MipLevels=td.ArraySize=1;td.SampleDesc.Count=1;
        td.Format=DXGI_FORMAT_D32_FLOAT;td.BindFlags=D3D11_BIND_DEPTH_STENCIL;
        if(FAILED(device->CreateTexture2D(&td,nullptr,&depthTexture))||FAILED(device->CreateDepthStencilView(depthTexture.Get(),nullptr,&depthView)))return false;
        depthW=w;depthH=h;
    }
    float yaw=XMConvertToRadians(camera.yaw),pitch=XMConvertToRadians(camera.pitch);
    XMVECTOR direction=XMVectorSet(-std::sin(yaw)*std::cos(pitch),-std::sin(pitch),std::cos(yaw)*std::cos(pitch),0);
    // Shift origins relative to the camera before float projection to retain precision far from (0,0,0).
    XMMATRIX vp=XMMatrixLookToRH(XMVectorZero(),direction,XMVectorSet(0,1,0,0))
        *XMMatrixPerspectiveFovRH(XMConvertToRadians(camera.verticalFov),float(w)/float(h),0.025f,2048.f);
    Params params;XMStoreFloat4x4(&params.matrix,vp);
    context->OMSetRenderTargets(1,&target,depthView.Get());context->ClearDepthStencilView(depthView.Get(),D3D11_CLEAR_DEPTH,1,0);
    context->RSSetState(raster.Get());D3D11_VIEWPORT viewport{0,0,float(w),float(h),0,1};context->RSSetViewports(1,&viewport);
    context->IASetInputLayout(layout.Get());context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    context->VSSetShader(vs.Get(),nullptr,0);context->PSSetShader(ps.Get(),nullptr,0);
    auto cb=constants.Get();context->VSSetConstantBuffers(0,1,&cb);auto sample=sampler.Get();context->PSSetSamplers(0,1,&sample);
    struct Item {Mesh* mesh;XMFLOAT3 origin;float distance;};
    std::vector<Item> items;items.reserve(sections.size()+2);
    for(auto& [key,m]:sections)items.push_back({&m,m.origin,0});
    items.push_back({&scene,scene.origin,0});if(showAvatar)items.push_back({&avatar,feet,0});
    for(auto& i:items){float dx=i.origin.x-float(camera.x),dy=i.origin.y-float(camera.y),dz=i.origin.z-float(camera.z);i.distance=dx*dx+dy*dy+dz*dz;}
    std::sort(items.begin(),items.end(),[](auto& a,auto& b){return a.distance>b.distance;});
    const float blendFactor[4]={};
    auto bind=[&](Mesh& m,XMFLOAT3 origin){
        UINT stride=sizeof(p::RenVertex),offset=0;auto buffer=m.buffer.Get();context->IASetVertexBuffers(0,1,&buffer,&stride,&offset);
        params.origin={float(double(origin.x)-camera.x),float(double(origin.y)-camera.y),float(double(origin.z)-camera.z),0};
        context->UpdateSubresource(constants.Get(),0,nullptr,&params,0,0);
    };
    context->OMSetBlendState(depthOnly.Get(),blendFactor,0xffffffff);context->OMSetDepthStencilState(writeDepth.Get(),0);
    context->PSSetShader(nullptr,nullptr,0);
    for(auto& [key,m]:occluders)if(m.vertices){bind(m,m.origin);context->Draw(m.vertices,0);stats.draws++;}
    context->PSSetShader(ps.Get(),nullptr,0);
    for(int pass=0;pass<2;pass++){
        context->OMSetBlendState(pass?alpha.Get():opaque.Get(),blendFactor,0xffffffff);
        context->OMSetDepthStencilState(pass?readDepth.Get():writeDepth.Get(),0);
        for(auto item:items)if(item.mesh->vertices){
            bind(*item.mesh,item.origin);
            for(auto b:item.mesh->batches){
                if(bool(b.flags&1)!=bool(pass))continue;
                auto tex=textures.find(b.texture);if(tex==textures.end())continue;
                auto view=tex->second.view.Get();context->PSSetShaderResources(0,1,&view);
                context->Draw(b.count,b.first);stats.draws++;
            }
        }
    }
    ID3D11ShaderResourceView* noView=nullptr;context->PSSetShaderResources(0,1,&noView);
    return true;
}
