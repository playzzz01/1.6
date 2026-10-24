#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <tlhelp32.h>
#include <cstdio>
#include <cwchar>
#include <vector>

static HWND game_window(DWORD pid) {
    struct Search { DWORD pid; HWND window; RECT rect; } search{pid,nullptr,{}};
    EnumWindows([](HWND h,LPARAM value)->BOOL {
        auto* s=reinterpret_cast<Search*>(value);DWORD owner=0;GetWindowThreadProcessId(h,&owner);
        if(owner==s->pid&&IsWindowVisible(h)&&!GetWindow(h,GW_OWNER)&&GetWindowRect(h,&s->rect)&&
           s->rect.right-s->rect.left>=640&&s->rect.bottom-s->rect.top>=400){s->window=h;return FALSE;}
        return TRUE;
    },reinterpret_cast<LPARAM>(&search));
    return search.window;
}
static bool has_module(DWORD pid,const wchar_t* wanted) {
    HANDLE snap=CreateToolhelp32Snapshot(TH32CS_SNAPMODULE|TH32CS_SNAPMODULE32,pid);
    if(snap==INVALID_HANDLE_VALUE)return false;
    MODULEENTRY32W entry{sizeof(entry)};bool found=false;
    if(Module32FirstW(snap,&entry))do{if(_wcsicmp(entry.szModule,wanted)==0){found=true;break;}}while(Module32NextW(snap,&entry));
    CloseHandle(snap);return found;
}
static DWORD find_game() {
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return 0;
    PROCESSENTRY32W entry{sizeof(entry)};
    std::vector<DWORD> candidates;
    if (Process32FirstW(snap, &entry)) do {
        if (_wcsicmp(entry.szExeFile, L"hl.exe") == 0) candidates.push_back(entry.th32ProcessID);
    } while (Process32NextW(snap, &entry));
    CloseHandle(snap);
    for(DWORD pid:candidates)if(game_window(pid)&&has_module(pid,L"opengl32.dll")&&has_module(pid,L"mp.dll"))return pid;
    for(DWORD pid:candidates)if(game_window(pid))return pid;
    return candidates.empty()?0:candidates.front();
}

static uintptr_t remote_kernel32(DWORD pid) {
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
    if (snap == INVALID_HANDLE_VALUE) return 0;
    MODULEENTRY32W entry{sizeof(entry)};
    uintptr_t base = 0;
    if (Module32FirstW(snap, &entry)) do {
        if (_wcsicmp(entry.szModule, L"KERNEL32.DLL") == 0) { base = reinterpret_cast<uintptr_t>(entry.modBaseAddr); break; }
    } while (Module32NextW(snap, &entry));
    CloseHandle(snap);
    return base;
}

int wmain() {
    wchar_t path[MAX_PATH]{};
    GetModuleFileNameW(nullptr, path, MAX_PATH);
    wchar_t* slash = wcsrchr(path, static_cast<wchar_t>(92));
    if (!slash) return 2;
    wcscpy_s(slash + 1, MAX_PATH - (slash + 1 - path), L"CsInternal.dll");
    if (GetFileAttributesW(path) == INVALID_FILE_ATTRIBUTES) { wprintf(L"DLL 未找到: %ls\n", path); return 3; }
    DWORD pid = find_game();
    if (!pid) { puts("请先启动 hl.exe 并进入地图。"); return 4; }
    HANDLE process = OpenProcess(PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ, FALSE, pid);
    if (!process) { printf("OpenProcess 失败: %lu\n", GetLastError()); return 5; }
    uintptr_t local = reinterpret_cast<uintptr_t>(GetModuleHandleW(L"kernel32.dll"));
    uintptr_t remote = remote_kernel32(pid);
    uintptr_t load = reinterpret_cast<uintptr_t>(GetProcAddress(reinterpret_cast<HMODULE>(local), "LoadLibraryW"));
    if (!remote || !load) { puts("无法定位目标进程 kernel32。"); CloseHandle(process); return 6; }
    SIZE_T bytes = (wcslen(path) + 1) * sizeof(wchar_t);
    void* memory = VirtualAllocEx(process, nullptr, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!memory || !WriteProcessMemory(process, memory, path, bytes, nullptr)) {
        printf("写入 DLL 路径失败: %lu\n", GetLastError()); CloseHandle(process); return 7;
    }
    auto start = reinterpret_cast<LPTHREAD_START_ROUTINE>(remote + load - local);
    HANDLE thread = CreateRemoteThread(process, nullptr, 0, start, memory, 0, nullptr);
    if (!thread) { printf("CreateRemoteThread 失败: %lu\n", GetLastError()); VirtualFreeEx(process, memory, 0, MEM_RELEASE); CloseHandle(process); return 8; }
    WaitForSingleObject(thread, 10000);
    DWORD module = 0; GetExitCodeThread(thread, &module);
    printf("游戏 PID=%lu，内部模块=%08lX。INS 显示菜单。\n", pid, module);
    CloseHandle(thread); VirtualFreeEx(process, memory, 0, MEM_RELEASE); CloseHandle(process);
    return module ? 0 : 9;
}
