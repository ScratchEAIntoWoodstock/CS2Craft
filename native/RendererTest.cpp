#include <cstdio>
extern "C" __declspec(dllimport) int CS2Craft_SelfTest();
extern "C" __declspec(dllimport) int CS2Craft_WorldSelfTest();
extern "C" __declspec(dllimport) int CS2Craft_RenderFixture(const wchar_t*,const wchar_t*);
extern "C" __declspec(dllimport) int CS2Craft_InputSelfTest();
int wmain(int argc,wchar_t** argv){
    if(argc==3){int r=CS2Craft_RenderFixture(argv[1],argv[2]);std::printf("Real Minecraft packet replay: %s (%d)\n",r?"FAIL":"PASS",r);return r;}
    std::setvbuf(stdout,nullptr,_IONBF,0);
    int hud=CS2Craft_SelfTest();
    std::printf("D3D11 WARP HUD shader/readback test: %s (code %d)\n",hud?"FAIL":"PASS",hud);
    int world=CS2Craft_WorldSelfTest();
    std::printf("D3D11 WARP world packet/projection/texture/depth/readback tests: %s (code %d)\n",world?"FAIL":"PASS",world);
    int input=CS2Craft_InputSelfTest();
    std::printf("Relative mouse routing / GUI / release tests: %s (%d)\n",input?"FAIL":"PASS",input);
    return hud?hud:world?world:input;
}
