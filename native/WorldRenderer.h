#pragma once
#include <d3d11.h>
#include <wrl/client.h>
#include <DirectXMath.h>
#include <map>
#include <vector>
#include <cstdint>
#include <tuple>
#include "../third_party/skycraft_protocol_v10.h"

// Minecraft-space scene backend. Call under the compositor's mutex.
// Own depth buffer: Minecraft geometry + optional sampled host triangles, NOT CS2's depth buffer.
class WorldRenderer {
public:
    struct Camera { double x{},y{},z{}; float yaw{},pitch{},verticalFov{73.7398f}; };
    struct Stats { uint64_t accepted{},rejected{},draws{},meshBytes{},textureBytes{},occluderBytes{}; };
    HRESULT Initialize(ID3D11Device* device);
    void Reset();
    void Clear();
    bool Packet(uint32_t type,const void* payload,size_t bytes);
    bool Occluder(int x,int y,int z,const float* xyz,uint32_t vertexCount);
    void ClearOccluders();
    // Per-frame Minecraft things drawn without meshes: arrows, dropped items/blocks, mining cracks,
    // and the targeted-block outline (selection = min xyz, max xyz, or null).
    void SetWorldEntities(const skycraft::proto::WorldEntity* entities,uint32_t count,const float* selection);
    bool Draw(ID3D11RenderTargetView* target,UINT width,UINT height,const Camera& camera,
              const DirectX::XMFLOAT3& avatarFeet,bool showAvatar);
    Stats GetStats() const {return stats;}
    size_t Sections() const {return sections.size();}
    size_t Occluders() const {return occluders.size();}
private:
    using Key=std::tuple<int,int,int>;
    template<class T> using Com=Microsoft::WRL::ComPtr<T>;
    struct Mesh {
        Com<ID3D11Buffer> buffer;
        std::vector<skycraft::proto::RenBatch> batches;
        DirectX::XMFLOAT3 origin{};
        uint32_t vertices{};
    };
    struct Texture {Com<ID3D11Texture2D> texture;Com<ID3D11ShaderResourceView> view;UINT w{},h{};};
    // Depth-only host geometry: positions in absolute Minecraft space, 12 bytes per vertex.
    struct OccluderMesh {Com<ID3D11Buffer> buffer;uint32_t vertices{};};
    Com<ID3D11Device> device;
    Com<ID3D11DeviceContext> context;
    Com<ID3D11VertexShader> vs,depthVs;
    Com<ID3D11PixelShader> ps;
    Com<ID3D11InputLayout> layout,depthLayout;
    Com<ID3D11Buffer> constants;
    Com<ID3D11SamplerState> sampler;
    Com<ID3D11BlendState> alpha,opaque,depthOnly;
    Com<ID3D11DepthStencilState> writeDepth,readDepth;
    Com<ID3D11RasterizerState> raster;
    Com<ID3D11Texture2D> depthTexture;
    Com<ID3D11DepthStencilView> depthView;
    UINT depthW{},depthH{};
    std::map<Key,Mesh> sections;
    std::map<Key,OccluderMesh> occluders;
    std::map<uint32_t,Texture> textures;
    Mesh avatar,scene;
    // World entities: [cutout triangles | translucent crack triangles | outline lines], rebuilt per frame.
    std::vector<skycraft::proto::RenVertex> dynamicVertices;
    uint32_t cutoutVertices{},crackVertices{},lineVertices{},dynamicCapacity{};
    bool dynamicDirty{};
    Com<ID3D11Buffer> dynamicBuffer;
    Stats stats;
    bool UploadMesh(Mesh& destination,const skycraft::proto::RenVertex* vertices,uint32_t count,
                    std::vector<skycraft::proto::RenBatch> batches,DirectX::XMFLOAT3 origin);
    bool UploadTexture(uint32_t id,UINT width,UINT height,const void* rgba,size_t bytes);
    void DropMesh(Mesh& mesh);
    bool Consume(uint32_t type,const uint8_t* payload,size_t bytes);
    bool UploadDynamic();
};
