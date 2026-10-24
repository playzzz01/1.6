#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <gl/GL.h>
#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>
#include "MinHook.h"
#include "imgui.h"
#include "backends/imgui_impl_win32.h"
#include "backends/imgui_impl_opengl2.h"

#pragma comment(lib,"opengl32.lib")
extern IMGUI_IMPL_API LRESULT ImGui_ImplWin32_WndProcHandler(HWND,UINT,WPARAM,LPARAM);

namespace {
using SwapFn=BOOL (WINAPI*)(HDC);
SwapFn wglOriginal=nullptr, gdiOriginal=nullptr;
thread_local bool insideSwap=false;
using GlEnableFn=void (APIENTRY*)(GLenum);
GlEnableFn glEnableOriginal=nullptr;
thread_local bool drawingThroughWall=false;
WNDPROC wndOriginal=nullptr;
HWND gameWindow=nullptr;
std::atomic<bool> menuOpen=false;
bool ready=false;
DWORD lastInsertTick=0;
std::string configDir="D:/CS/Counter Strike/cstrike/addons/amxmodx/configs/";
int activeTab=1;
unsigned long long sequence=0;

struct Options {
    bool health=true,ammo=true,enemyGlow=false,teamGlow=false,box=true,skeleton=false,names=true,ammoText=true;
    bool aim=false,circle=true,throughWall=false,shadow=true;
    int minutes=5,boxWidth=2,skeletonWidth=2,bar=4,ammoBar=2,radius=120,part=0,smooth=7,brightness=25;
    float enemy[3]={1,.25f,.15f},team[3]={.18f,.47f,1},boxColor[3]={1,1,1},skeletonColor[3]={.2f,1,.4f};
} o;

struct Vec { float x,y,z; };
struct Person {
    int id,health,clip,reserve,team;
    Vec origin,mins,maxs;
    std::string name;
    std::array<Vec,18> bones{};
    bool hasBones=false;
};
struct Frame {
    Vec eye{};float pitch=0,yaw=0,fov=90,stamp=0;int team=0;
    std::vector<Person> players;
};
Frame frame;
double lastTelemetry=0;
FILETIME telemetryTime{};

void rgb(const float* c,int &r,int &g,int &b) {
    r=int(c[0]*255+.5f);g=int(c[1]*255+.5f);b=int(c[2]*255+.5f);
}
std::string colorText(const float* c) {
    char s[16];std::snprintf(s,sizeof(s),"#%02X%02X%02X",int(c[0]*255),int(c[1]*255),int(c[2]*255));return s;
}
void parseColor(const std::string& s,float* c) {
    unsigned r,g,b;
    if (std::sscanf(s.c_str(),"#%02x%02x%02x",&r,&g,&b)==3) {c[0]=r/255.f;c[1]=g/255.f;c[2]=b/255.f;}
}
void readSettings() {
    std::ifstream a(configDir+"offline_trainer_v2.ctrl");
    long long seq=0;int enemy,team,aim,radius,part,smooth,width,wall,er,eg,eb,tr,tg,tb,brightness;
    if(a>>seq>>enemy>>team>>aim>>radius>>part>>smooth>>width>>wall>>er>>eg>>eb>>tr>>tg>>tb>>brightness) {
        sequence=std::max<unsigned long long>(sequence,seq);
        o.enemyGlow=enemy;o.teamGlow=team;o.aim=aim;o.radius=radius;o.part=part;o.smooth=smooth;o.throughWall=wall;
        o.enemy[0]=er/255.f;o.enemy[1]=eg/255.f;o.enemy[2]=eb/255.f;
        o.team[0]=tr/255.f;o.team[1]=tg/255.f;o.team[2]=tb/255.f;o.brightness=brightness;
    }
    std::ifstream l(configDir+"offline_trainer.ctrl");int h,am,g,aa,m,reset;
    if(l>>seq>>h>>am>>g>>aa>>m>>reset) {sequence=std::max<unsigned long long>(sequence,seq);o.health=h;o.ammo=am;o.minutes=m;}
    std::ifstream v(configDir+"trainer_visual.ini");std::string line;
    while(std::getline(v,line)) {
        auto p=line.find('=');if(p==std::string::npos)continue;
        auto key=line.substr(0,p), val=line.substr(p+1);
        if(key=="box")o.box=val=="1";else if(key=="skeleton")o.skeleton=val=="1";
        else if(key=="skeletonWidth")o.skeletonWidth=std::clamp(std::atoi(val.c_str()),1,8);
        else if(key=="skeletonColor")parseColor(val,o.skeletonColor);
        else if(key=="names")o.names=val=="1";else if(key=="ammo")o.ammoText=val=="1";
        else if(key=="circle")o.circle=val=="1";else if(key=="width")o.boxWidth=std::clamp(std::atoi(val.c_str()),1,8);
        else if(key=="bar")o.bar=std::clamp(std::atoi(val.c_str()),0,4);
        else if(key=="ammoBar")o.ammoBar=std::clamp(std::atoi(val.c_str()),0,4);
        else if(key=="shadow")o.shadow=val=="1";else if(key=="color")parseColor(val,o.boxColor);
    }
}
void diagnostic(const char* message) {
    std::ofstream log(configDir+"trainer_native.log",std::ios::app);
    if(log)log<<GetTickCount64()<<' '<<message<<'\n';
}
void replaceFile(const char* name,const std::string& contents) {
    std::string path=configDir+name,temp=path+".tmp";
    std::ofstream f(temp,std::ios::binary|std::ios::trunc);if(!f)return;
    f<<contents;f.close();
    MoveFileExA(temp.c_str(),path.c_str(),MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH);
}
void save(bool reset=false) {
    sequence=std::max<unsigned long long>(sequence+1,GetTickCount64());
    RECT r{};GetClientRect(gameWindow,&r);
    int width=std::clamp(int(r.right-r.left),320,7680);
    int er,eg,eb,tr,tg,tb;rgb(o.enemy,er,eg,eb);rgb(o.team,tr,tg,tb);
    std::ostringstream a,l,v;
    a<<sequence<<' '<<o.enemyGlow<<' '<<o.teamGlow<<' '<<o.aim<<' '<<o.radius<<' '<<o.part<<' '<<o.smooth<<' '<<width<<' '<<o.throughWall<<' '<<er<<' '<<eg<<' '<<eb<<' '<<tr<<' '<<tg<<' '<<tb<<' '<<o.brightness<<'\n';
    l<<sequence<<' '<<o.health<<' '<<o.ammo<<" 0 "<<o.aim<<' '<<o.minutes<<' '<<reset<<'\n';
    v<<"box="<<o.box<<"\nskeleton="<<o.skeleton<<"\nskeletonWidth="<<o.skeletonWidth<<"\nskeletonColor="<<colorText(o.skeletonColor)
     <<"\nnames="<<o.names<<"\nammo="<<o.ammoText<<"\ncircle="<<o.circle<<"\nwidth="<<o.boxWidth<<"\nbar="<<o.bar
     <<"\nammoBar="<<o.ammoBar<<"\nshadow="<<o.shadow<<"\ncolor="<<colorText(o.boxColor)<<'\n';
    replaceFile("offline_trainer_v2.ctrl",a.str());replaceFile("offline_trainer.ctrl",l.str());replaceFile("trainer_visual.ini",v.str());
}
void readFrame() {
    WIN32_FILE_ATTRIBUTE_DATA info{};
    std::string telemetry=configDir+"offline_trainer.telemetry";
    if(!GetFileAttributesExA(telemetry.c_str(),GetFileExInfoStandard,&info))return;
    if(CompareFileTime(&info.ftLastWriteTime,&telemetryTime)==0)return;
    std::ifstream f(configDir+"offline_trainer.telemetry");
    if(!f)return;std::string line;if(!std::getline(f,line))return;
    std::istringstream h(line);int version,health,weapon,clip,reserve,minutes;
    Frame next;
    if(!(h>>version>>next.eye.x>>next.eye.y>>next.eye.z>>next.pitch>>next.yaw>>next.fov>>health>>weapon>>clip>>reserve>>minutes>>next.team>>next.stamp)||version!=2)return;
    while(std::getline(f,line)) {
        std::istringstream s(line);Person p;
        if(!(s>>p.id>>p.origin.x>>p.origin.y>>p.origin.z>>p.mins.x>>p.mins.y>>p.mins.z>>p.maxs.x>>p.maxs.y>>p.maxs.z>>p.health>>p.clip>>p.reserve>>p.team>>p.name))continue;
        p.hasBones=true;for(auto& bone:p.bones)if(!(s>>bone.x>>bone.y>>bone.z)){p.hasBones=false;break;}
        std::replace(p.name.begin(),p.name.end(),'_',' ');next.players.push_back(p);
    }
    frame=std::move(next);lastTelemetry=ImGui::GetTime();telemetryTime=info.ftLastWriteTime;
}
// GoldSrc's studio callback belongs to the actual player model renderer.
// A second pass with the depth test disabled keeps the complete model visible
// through world geometry. The callback is replaced only after the client has
// returned its interface; no target EXE bytes or client DLL on disk are edited.
struct StudioInterface {
    int version;
    int (__cdecl* drawModel)(int);
    int (__cdecl* drawPlayer)(int,void*);
};
StudioInterface* studio=nullptr;
int (__cdecl* originalDrawPlayer)(int,void*)=nullptr;
using StudioFactory=int (__cdecl*)(int,StudioInterface**,void*);
StudioFactory originalFactory=nullptr;
using ClientExportFactory=void (__cdecl*)(void*);
ClientExportFactory originalClientExportFactory=nullptr;
constexpr int studioCallbackSlot=39; // GoldSrc cldll_func_t::pStudioInterface
bool loggedClientFactory=false, loggedStudioRequest=false;
using GetProcFn=FARPROC (WINAPI*)(HMODULE,LPCSTR);
GetProcFn originalGetProc=nullptr;
using GetProcAddressForCallerFn=FARPROC (WINAPI*)(HMODULE,LPCSTR,void*);
GetProcAddressForCallerFn originalGetProcForCaller=nullptr;
bool installedStudio=false;
bool teamFor(int id,bool& sameTeam) {
    for(const auto& p:frame.players)if(p.id==id){sameTeam=p.team==frame.team;return true;}
    return false;
}
int __cdecl drawPlayerHook(int flags,void* state) {
    if(!originalDrawPlayer)return 0;
    int result=originalDrawPlayer(flags,state);
    if(!state||!wglGetCurrentContext()||!(flags&1))return result;
    static bool sawPlayer=false;
    if(!sawPlayer){diagnostic("StudioDrawPlayer invoked");sawPlayer=true;}
    int id=*reinterpret_cast<int*>(static_cast<char*>(state)+4);
    bool ally=false;
    if(id<1||id>32||!teamFor(id,ally)||(ally?!o.teamGlow:!o.enemyGlow))return result;
    static bool sawGlow=false;
    if(!sawGlow){diagnostic("Glow depth pass invoked");sawGlow=true;}
    GLboolean oldDepth=glIsEnabled(GL_DEPTH_TEST);
    GLboolean oldMask;glGetBooleanv(GL_DEPTH_WRITEMASK,&oldMask);
    glDisable(GL_DEPTH_TEST);glDepthMask(GL_FALSE);
    drawingThroughWall=true;
    // Keep the model's own textures. Its native GlowShell supplies color and
    // brightness, while this pass removes geometry occlusion.
    originalDrawPlayer(flags&~2,state);
    drawingThroughWall=false;
    if(oldDepth)glEnable(GL_DEPTH_TEST);else glDisable(GL_DEPTH_TEST);
    glDepthMask(oldMask);
    return result;
}
void APIENTRY glEnableHook(GLenum capability) {
    if(drawingThroughWall&&capability==GL_DEPTH_TEST)return;
    glEnableOriginal(capability);
}
int __cdecl factoryHook(int version,StudioInterface** out,void* api) {
    if(!loggedStudioRequest){diagnostic("Studio interface requested");loggedStudioRequest=true;}
    int result=originalFactory(version,out,api);
    if(result&&version==1&&out&&*out&&(*out)->version==1&&(*out)->drawPlayer&&!installedStudio) {
        studio=*out;
        DWORD old=0;
        if(VirtualProtect(studio,sizeof(*studio),PAGE_READWRITE,&old)) {
            originalDrawPlayer=studio->drawPlayer;
            studio->drawPlayer=drawPlayerHook;
            DWORD ignored;VirtualProtect(studio,sizeof(*studio),old,&ignored);
            installedStudio=true;
            diagnostic("StudioDrawPlayer installed");
        }
    }
    return result;
}
void __cdecl clientExportFactoryHook(void* table) {
    originalClientExportFactory(table);
    if(!table||installedStudio)return;
    auto callbacks=static_cast<void**>(table);
    auto candidate=reinterpret_cast<StudioFactory>(callbacks[studioCallbackSlot]);
    if(!candidate||candidate==factoryHook)return;
    originalFactory=candidate;
    callbacks[studioCallbackSlot]=reinterpret_cast<void*>(factoryHook);
    diagnostic("GoldSrc F studio callback installed");
}
FARPROC WINAPI getProcHook(HMODULE module,LPCSTR name) {
    FARPROC found=originalGetProc(module,name);
    if(name&&reinterpret_cast<uintptr_t>(name)>0xFFFF&&lstrcmpA(name,"F")==0&&found) {
        if(!loggedClientFactory){diagnostic("GoldSrc F export resolved");loggedClientFactory=true;}
        originalClientExportFactory=reinterpret_cast<ClientExportFactory>(found);
        return reinterpret_cast<FARPROC>(clientExportFactoryHook);
    }
    if(name&&reinterpret_cast<uintptr_t>(name)>0xFFFF&&
        lstrcmpA(name,"HUD_GetStudioModelInterface")==0&&found&&!installedStudio) {
        originalFactory=reinterpret_cast<StudioFactory>(found);
        return reinterpret_cast<FARPROC>(factoryHook);
    }
    return found;
}
FARPROC WINAPI getProcForCallerHook(HMODULE module,LPCSTR name,void* caller) {
    FARPROC found=originalGetProcForCaller(module,name,caller);
    if(name&&reinterpret_cast<uintptr_t>(name)>0xFFFF&&lstrcmpA(name,"F")==0&&found) {
        if(!loggedClientFactory){diagnostic("GoldSrc F export resolved");loggedClientFactory=true;}
        originalClientExportFactory=reinterpret_cast<ClientExportFactory>(found);
        return reinterpret_cast<FARPROC>(clientExportFactoryHook);
    }
    if(name&&reinterpret_cast<uintptr_t>(name)>0xFFFF&&
        lstrcmpA(name,"HUD_GetStudioModelInterface")==0&&found&&!installedStudio) {
        originalFactory=reinterpret_cast<StudioFactory>(found);
        return reinterpret_cast<FARPROC>(factoryHook);
    }
    return found;
}
bool project(Vec v,ImVec2& p,float w,float h) {
    float dx=v.x-frame.eye.x,dy=v.y-frame.eye.y,dz=v.z-frame.eye.z;
    float yaw=frame.yaw*.0174532925f,pitch=frame.pitch*.0174532925f;
    float cy=cosf(yaw),sy=sinf(yaw),cp=cosf(pitch),sp=sinf(pitch);
    float forward=dx*cp*cy+dy*cp*sy-dz*sp;if(forward<5)return false;
    float right=dx*sy-dy*cy,up=dx*sp*cy+dy*sp*sy+dz*cp;
    float focal=w/(2*tanf(frame.fov*.00872664626f));
    p={w*.5f+right*focal/forward,h*.5f-up*focal/forward};return true;
}
bool projectClipped(Vec v,ImVec2& p,float w,float h,float& depth) {
    float dx=v.x-frame.eye.x,dy=v.y-frame.eye.y,dz=v.z-frame.eye.z;
    float yaw=frame.yaw*.0174532925f,pitch=frame.pitch*.0174532925f;
    float cy=cosf(yaw),sy=sinf(yaw),cp=cosf(pitch),sp=sinf(pitch);
    depth=dx*cp*cy+dy*cp*sy-dz*sp;
    if(depth<1.0f)return false;
    float right=dx*sy-dy*cy,up=dx*sp*cy+dy*sp*sy+dz*cp;
    float focal=w/(2*tanf(frame.fov*.00872664626f));
    p={w*.5f+right*focal/depth,h*.5f-up*focal/depth};
    return std::isfinite(p.x)&&std::isfinite(p.y);
}
ImU32 color(const float* c,int alpha=255){return IM_COL32(int(c[0]*255),int(c[1]*255),int(c[2]*255),alpha);}
void meter(ImDrawList* d,int position,float ratio,ImVec2 a,ImVec2 b,ImU32 fill,float offset=0) {
    ratio=std::clamp(ratio,0.f,1.f);float x=a.x,y=a.y,w=b.x-a.x,h=b.y-a.y;
    ImVec2 t,u;bool vert=false;
    if(position==1){t={x,y-9-offset};u={b.x,y-3-offset};}
    else if(position==2){t={x,b.y+7+offset};u={b.x,b.y+13+offset};}
    else if(position==3){t={b.x+6+offset,y};u={b.x+12+offset,b.y};vert=true;}
    else if(position==4){t={x-12-offset,y};u={x-6-offset,b.y};vert=true;}
    else return;
    d->AddRectFilled(t,u,IM_COL32(0,0,0,190),2);
    if(vert)t.y=u.y-h*ratio;else u.x=t.x+w*ratio;
    d->AddRectFilled(t,u,fill,2);
}
void drawEsp(float w,float h) {
    if(ImGui::GetTime()-lastTelemetry>.3)return;
    ImDrawList* d=ImGui::GetBackgroundDrawList();
    if(o.aim&&o.circle)d->AddCircle({w/2,h/2},float(o.radius),IM_COL32(255,224,70,225),96,1.5f);
    static constexpr std::array<std::pair<int,int>,17> segments={{{0,1},{1,2},{2,3},{1,4},{4,5},{5,6},{6,7},{1,8},{8,9},{9,10},{10,11},{3,12},{12,13},{13,14},{3,15},{15,16},{16,17}}};
    for(const auto& p:frame.players) {
        if(p.team==frame.team)continue;
        if(o.skeleton&&p.hasBones) {
            std::array<ImVec2,18> screen{};std::array<bool,18> visible{};
            for(int i=0;i<18;i++)visible[i]=project(p.bones[i],screen[i],w,h);
            for(auto [a,b]:segments)if(visible[a]&&visible[b])d->AddLine(screen[a],screen[b],color(o.skeletonColor),float(o.skeletonWidth));
            // Draw the head joint independently so a missing/invalid neck
            // segment cannot make the head disappear from the skeleton view.
            if(visible[0])d->AddCircle(screen[0],5.0f,color(o.skeletonColor),16,float(o.skeletonWidth));
        }
        if(!o.box)continue;
        float x0=1e9f,y0=1e9f,x1=-1e9f,y1=-1e9f;int count=0;
        // Clip the 3-D box edges against the near plane. Requiring all eight
        // corners to be in front dropped the entire ESP box when its feet or
        // head crossed the camera plane, which looked like rapid flicker.
        static constexpr std::array<std::pair<int,int>,12> edges={{{0,1},{0,2},{0,4},{1,3},{1,5},{2,3},{2,6},{3,7},{4,5},{4,6},{5,7},{6,7}}};
        std::array<Vec,8> corners{};std::array<float,8> depth{};std::array<ImVec2,8> projected{};std::array<bool,8> valid{};
        for(float x:{p.mins.x,p.maxs.x})for(float y:{p.mins.y,p.maxs.y})for(float z:{p.mins.z,p.maxs.z}) {
            int i=count;corners[i]={p.origin.x+x,p.origin.y+y,p.origin.z+z};valid[i]=projectClipped(corners[i],projected[i],w,h,depth[i]);count++;
        }
        count=0;
        auto include=[&](ImVec2 q){x0=std::min(x0,q.x);y0=std::min(y0,q.y);x1=std::max(x1,q.x);y1=std::max(y1,q.y);count++;};
        for(int i=0;i<8;i++)if(valid[i])include(projected[i]);
        for(auto [a,b]:edges)if(valid[a]!=valid[b]){float t=(1.0f-depth[a])/(depth[b]-depth[a]);Vec v{corners[a].x+(corners[b].x-corners[a].x)*t,corners[a].y+(corners[b].y-corners[a].y)*t,corners[a].z+(corners[b].z-corners[a].z)*t};ImVec2 q;float clippedDepth;if(projectClipped(v,q,w,h,clippedDepth))include(q);}
        if(count<2||x1-x0<3||y1-y0<3||x1-x0>3000||y1-y0>3000)continue;
        x0=std::clamp(x0,-100.f,w+100.f);x1=std::clamp(x1,-100.f,w+100.f);y0=std::clamp(y0,-100.f,h+100.f);y1=std::clamp(y1,-100.f,h+100.f);
        if(x1<=x0||y1<=y0)continue;
        ImVec2 a{x0,y0},b{x1,y1};
        if(o.shadow)d->AddRect(a,b,IM_COL32(0,0,0,240),0,0,float(o.boxWidth+3));
        d->AddRect(a,b,color(o.boxColor),0,0,float(o.boxWidth));
        if(o.names)d->AddText({x0,y0-18},IM_COL32_WHITE,p.name.c_str());
        meter(d,o.bar,p.health/100.f,a,b,IM_COL32(55,225,90,255));
        meter(d,o.ammoBar,p.clip/30.f,a,b,IM_COL32(52,120,255,255),13);
        if(o.ammoText){char text[32];std::snprintf(text,sizeof(text),"%d/%d",p.clip,p.reserve);d->AddText({x0,y1+2},IM_COL32_WHITE,text);}
    }
}
bool toggle(const char* name,bool& v) {
    ImGui::PushID(name);ImGui::TextUnformatted(name);ImGui::SameLine(365);
    ImVec2 pos=ImGui::GetCursorScreenPos();ImGui::InvisibleButton("switch",{46,24});bool changed=ImGui::IsItemClicked();if(changed)v=!v;
    auto* d=ImGui::GetWindowDrawList();d->AddRectFilled(pos,{pos.x+46,pos.y+24},v?IM_COL32(68,138,255,255):IM_COL32(75,80,95,255),12);
    d->AddCircleFilled({pos.x+(v?34.f:12.f),pos.y+12},9,IM_COL32_WHITE,20);
    ImGui::PopID();return changed;
}
void menu() {
    if(!menuOpen)return;
    ImGui::SetNextWindowSize({580,580},ImGuiCond_FirstUseEver);
    ImGui::SetNextWindowBgAlpha(.93f);
    ImGui::Begin("CS 1.6  /  OFFLINE",nullptr,ImGuiWindowFlags_NoCollapse|ImGuiWindowFlags_NoSavedSettings);
    bool changed=false,reset=false;
    if(ImGui::Button("PLAY",{135,34}))activeTab=0;ImGui::SameLine();
    if(ImGui::Button("VISUAL",{135,34}))activeTab=1;ImGui::SameLine();
    if(ImGui::Button("AIM",{135,34}))activeTab=2;
    ImGui::Separator();
    if(activeTab==0) {
        changed|=toggle("无限生命",o.health);changed|=toggle("无限弹药",o.ammo);
        changed|=ImGui::SliderInt("回合时间",&o.minutes,1,60);
        reset=ImGui::Button("重置对局",{160,36});
    } else if(activeTab==1) {
        ImGui::TextDisabled("人物发光");
        changed|=toggle("敌方发光",o.enemyGlow);changed|=toggle("己方发光",o.teamGlow);
        changed|=ImGui::ColorEdit3("敌方颜色",o.enemy,ImGuiColorEditFlags_NoInputs);
        changed|=ImGui::ColorEdit3("己方颜色",o.team,ImGuiColorEditFlags_NoInputs);
        changed|=ImGui::SliderInt("发光亮度",&o.brightness,1,100);
        ImGui::Separator();ImGui::TextDisabled("方框透视");
        changed|=toggle("方框",o.box);changed|=toggle("阴影",o.shadow);
        changed|=toggle("名字",o.names);changed|=toggle("弹药数字",o.ammoText);
        changed|=ImGui::ColorEdit3("线条颜色",o.boxColor,ImGuiColorEditFlags_NoInputs);
        changed|=ImGui::SliderInt("线条粗细",&o.boxWidth,1,8);
        const char* positions[]={"关闭","顶部","底部","右侧","左侧"};
        changed|=ImGui::Combo("血量位置",&o.bar,positions,5);changed|=ImGui::Combo("弹药位置",&o.ammoBar,positions,5);
        ImGui::Separator();ImGui::TextDisabled("骨骼透视");
        changed|=toggle("骨骼线条",o.skeleton);
        changed|=ImGui::ColorEdit3("骨骼颜色",o.skeletonColor,ImGuiColorEditFlags_NoInputs);
        changed|=ImGui::SliderInt("骨骼粗细",&o.skeletonWidth,1,8);
    } else {
        changed|=toggle("自动吸附",o.aim);changed|=toggle("隔墙吸附",o.throughWall);
        changed|=ImGui::SliderInt("吸附范围",&o.radius,20,600);
        const char* parts[]={"头部","脖子","左手","右手","胸部","左腿","右腿","身体","随机"};
        changed|=ImGui::Combo("吸附部位",&o.part,parts,9);
        changed|=ImGui::SliderInt("吸附平滑度",&o.smooth,1,30);
        changed|=toggle("显示范围圆圈",o.circle);
    }
    ImGui::End();if(changed||reset)save(reset);
}
LRESULT CALLBACK windowProc(HWND h,UINT m,WPARAM w,LPARAM l) {
    // Toggle is handled only by the SwapBuffers polling path below. Toggling
    // here as well caused one physical INS press to open and immediately close
    // the menu when WM_KEYDOWN and GetAsyncKeyState were observed in one frame.
    if((m==WM_KEYDOWN||m==WM_KEYUP)&&w==VK_INSERT)return 0;
    if(menuOpen&&ImGui::GetCurrentContext()) {
        ImGui_ImplWin32_WndProcHandler(h,m,w,l);
        auto& io=ImGui::GetIO();
        if((m>=WM_MOUSEFIRST&&m<=WM_MOUSELAST&&io.WantCaptureMouse) ||
           ((m==WM_KEYDOWN||m==WM_KEYUP||m==WM_CHAR)&&io.WantCaptureKeyboard))return 1;
    }
    return CallWindowProcW(wndOriginal,h,m,w,l);
}
void initialize(HWND window) {
    gameWindow=window;wndOriginal=reinterpret_cast<WNDPROC>(SetWindowLongPtrW(window,GWLP_WNDPROC,reinterpret_cast<LONG_PTR>(windowProc)));
    ImGui::CreateContext();ImGuiIO& io=ImGui::GetIO();io.IniFilename=nullptr;
    if(GetFileAttributesA("C:/Windows/Fonts/msyh.ttc")!=INVALID_FILE_ATTRIBUTES)
        io.Fonts->AddFontFromFileTTF("C:/Windows/Fonts/msyh.ttc",17,nullptr,io.Fonts->GetGlyphRangesChineseFull());
    ImGui_ImplWin32_InitForOpenGL(window);ImGui_ImplOpenGL2_Init();
    ImGui::StyleColorsDark();
    auto& style=ImGui::GetStyle();style.WindowRounding=14;style.ChildRounding=10;style.FrameRounding=9;
    style.PopupRounding=9;style.GrabRounding=9;style.ScrollbarRounding=9;style.WindowPadding={18,16};
    style.Colors[ImGuiCol_WindowBg]={.065f,.078f,.11f,.96f};
    style.Colors[ImGuiCol_Button]={.13f,.18f,.28f,1};style.Colors[ImGuiCol_ButtonHovered]={.22f,.34f,.55f,1};
    style.Colors[ImGuiCol_FrameBg]={.11f,.14f,.2f,1};style.Colors[ImGuiCol_SliderGrab]={.31f,.57f,1,1};
    readSettings();ready=true;diagnostic("ImGui initialized in hl.exe");
}
void renderFrame(HDC dc) {
    static bool sawSwap=false;
    if(!sawSwap){diagnostic("SwapBuffers invoked");sawSwap=true;}
    static DWORD lastFrameLog=0;
    DWORD now=GetTickCount();
    if(now-lastFrameLog>5000){lastFrameLog=now;diagnostic("SwapBuffers rendering active");}
    HWND h=WindowFromDC(dc);
    static bool sawWindow=false;
    if(h&&!sawWindow){diagnostic("SwapBuffers window found");sawWindow=true;}
    if(!ready&&h&&wglGetCurrentContext())initialize(h);
    if(ready&&h==gameWindow&&wglGetCurrentContext()) {
        static bool sawMenuRender=false;
        if(!sawMenuRender){diagnostic("ImGui frame rendering active");sawMenuRender=true;}
        static bool insertDown=false;
        bool down=(GetAsyncKeyState(VK_INSERT)&0x8000)!=0;
        if(down&&!insertDown&&GetForegroundWindow()==gameWindow&&now-lastInsertTick>180){
            menuOpen=!menuOpen;lastInsertTick=now;diagnostic(menuOpen?"INS menu opened":"INS menu closed");
        }
        insertDown=down;
        ImGui_ImplOpenGL2_NewFrame();ImGui_ImplWin32_NewFrame();ImGui::NewFrame();
        readFrame();RECT r{};GetClientRect(gameWindow,&r);
        drawEsp(float(r.right-r.left),float(r.bottom-r.top));menu();
        ImGui::Render();ImGui_ImplOpenGL2_RenderDrawData(ImGui::GetDrawData());
    }
}
BOOL WINAPI wglSwapHook(HDC dc) {
    bool nested=insideSwap;insideSwap=true;
    if(!nested)renderFrame(dc);
    BOOL result=wglOriginal(dc);insideSwap=nested;return result;
}
BOOL WINAPI gdiSwapHook(HDC dc) {
    bool nested=insideSwap;insideSwap=true;
    if(!nested)renderFrame(dc);
    BOOL result=gdiOriginal(dc);insideSwap=nested;return result;
}
DWORD WINAPI install(void*) {
    diagnostic("native DLL loaded");
    HMODULE kernel=GetModuleHandleW(L"kernel32.dll");
    void* resolver=reinterpret_cast<void*>(GetProcAddress(kernel,"GetProcAddress"));
    if(MH_Initialize()!=MH_OK)return 2;
    if(resolver&&MH_CreateHook(resolver,getProcHook,reinterpret_cast<void**>(&originalGetProc))==MH_OK)
        MH_EnableHook(resolver);
    void* resolverForCaller=reinterpret_cast<void*>(GetProcAddress(kernel,"GetProcAddressForCaller"));
    if(resolverForCaller&&MH_CreateHook(resolverForCaller,getProcForCallerHook,reinterpret_cast<void**>(&originalGetProcForCaller))==MH_OK)
        MH_EnableHook(resolverForCaller);
    for(int n=0;n<200;n++) {
        HMODULE gl=GetModuleHandleW(L"opengl32.dll");
        if(gl) {
            void* target=reinterpret_cast<void*>(GetProcAddress(gl,"wglSwapBuffers"));
            if(target&&MH_CreateHook(target,wglSwapHook,reinterpret_cast<void**>(&wglOriginal))==MH_OK) {
                MH_EnableHook(target);diagnostic("wglSwapBuffers hooked");
            }
            HMODULE gdi=GetModuleHandleW(L"gdi32.dll");
            void* gdiTarget=gdi?reinterpret_cast<void*>(GetProcAddress(gdi,"SwapBuffers")):nullptr;
            if(gdiTarget&&MH_CreateHook(gdiTarget,gdiSwapHook,reinterpret_cast<void**>(&gdiOriginal))==MH_OK) {
                MH_EnableHook(gdiTarget);diagnostic("gdi32 SwapBuffers hooked");
            }
            void* enable=reinterpret_cast<void*>(GetProcAddress(gl,"glEnable"));
            if(enable&&MH_CreateHook(enable,glEnableHook,reinterpret_cast<void**>(&glEnableOriginal))==MH_OK){
                MH_EnableHook(enable);diagnostic("glEnable depth guard installed");
            }
            if(wglOriginal||gdiOriginal)return 0;
        }
        Sleep(100);
    }
    return 1;
}
}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID) {
    if(reason==DLL_PROCESS_ATTACH){DisableThreadLibraryCalls(instance);HANDLE t=CreateThread(nullptr,0,install,nullptr,0,nullptr);if(t)CloseHandle(t);}
    return TRUE;
}
