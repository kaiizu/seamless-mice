// Seamless Mice native background engine. Win32 + the C runtime only.
#define UNICODE
#define _UNICODE
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <shellapi.h>
#include <wtsapi32.h>
#include <wincrypt.h>
#include <climits>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <cwchar>
#include "geometry.hpp"

namespace {
constexpr wchar_t window_class[] = L"SeamlessMice.Engine.v1";
constexpr UINT tray_message = WM_APP + 1, reload_message = WM_APP + 2, pause_message = WM_APP + 3,
    exit_message = WM_APP + 4, settings_message = WM_APP + 5, resume_message = WM_APP + 6, query_message = WM_APP + 8;
constexpr int max_screens = 16, max_calibrations = 64;
struct Calibration { wchar_t id[256]; double width, height, top, left; bool width_set, height_set, left_set; };
struct Config { bool enabled, pause_fullscreen; int count; Calibration entries[max_calibrations]; };
struct State {
    HWND window;
    HHOOK hook;
    NOTIFYICONDATAW tray;
    sm::Panel panels[max_screens];
    Config config;
    int count, source;
    sm::Point last;
    bool warping, paused, user_paused, locked, hotkey, tray_added;
    UINT explorer_restart;
    std::uint32_t mouse_events;
    wchar_t folder[32768], config_path[32768], status[256];
} g = {};

bool configuration_path(const wchar_t* folder, const wchar_t* profile, wchar_t* destination, std::size_t capacity) {
    wchar_t marker[32768];
    if (std::swprintf(marker, 32768, L"%lsinstalled.flag", folder) < 0) return false;
    if (GetFileAttributesW(marker) != INVALID_FILE_ATTRIBUTES) {
        if (!profile || !profile[0]) return false;
        return std::swprintf(destination, capacity, L"%ls\\SeamlessMice\\seamless-mice.ini", profile) >= 0;
    }
    return std::swprintf(destination, capacity, L"%lsseamless-mice.ini", folder) >= 0;
}

void set_status(const wchar_t* value) { lstrcpynW(g.status, value, 256); }
void update_tray() {
    wchar_t text[512];
    std::swprintf(text, 512, L"Seamless Mice: %ls%ls", g.status, g.hotkey ? L"" : L" (recovery shortcut unavailable)");
    lstrcpynW(g.tray.szTip, text, 128);
    Shell_NotifyIconW(NIM_MODIFY, &g.tray);
}
void stop_hook() {
    if (g.hook) { UnhookWindowsHookEx(g.hook); g.hook = nullptr; }
    g.source = -1;
}
void pause() {
    stop_hook(); g.paused = true; g.user_paused = true;
    set_status(L"Paused - Windows cursor behavior"); update_tray();
}
bool decode_identity(const char* text, wchar_t* output) {
    BYTE decoded[1024]; DWORD size = sizeof(decoded);
    if (!CryptStringToBinaryA(text, 0, CRYPT_STRING_BASE64 | CRYPT_STRING_STRICT, decoded, &size, nullptr, nullptr) || size == 0) return false;
    const int length = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, reinterpret_cast<const char*>(decoded), static_cast<int>(size), output, 255);
    if (length <= 0) return false;
    for (int i = 0; i < length; ++i) if (output[i] == 0) return false;
    output[length] = 0;
    return true;
}
char* trim(char* text) {
    while (*text == ' ' || *text == '\t' || *text == '\r') ++text;
    char* end = text + std::strlen(text);
    while (end > text && (end[-1] == ' ' || end[-1] == '\t' || end[-1] == '\r')) --end;
    *end = 0; return text;
}
bool parse_bool(const char* text, bool& value) {
    if (std::strcmp(text, "true") == 0) { value = true; return true; }
    if (std::strcmp(text, "false") == 0) { value = false; return true; }
    return false;
}
bool parse_config(Config& config) {
    std::memset(&config, 0, sizeof(config)); config.pause_fullscreen = true;
    HANDLE file = CreateFileW(g.config_path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) {
        if (GetLastError() == ERROR_FILE_NOT_FOUND || GetLastError() == ERROR_PATH_NOT_FOUND) return true;
        set_status(L"Paused: cannot read the settings file"); return false;
    }
    LARGE_INTEGER size;
    char buffer[65537]; DWORD read = 0;
    bool ok = GetFileSizeEx(file, &size) && size.QuadPart >= 0 && size.QuadPart <= 65536;
    if (ok) ok = ReadFile(file, buffer, static_cast<DWORD>(size.QuadPart), &read, nullptr) != FALSE && static_cast<LONGLONG>(read) == size.QuadPart;
    CloseHandle(file);
    if (!ok) { set_status(L"Paused: invalid or oversized settings file"); return false; }
    buffer[read] = 0;
    char* cursor = buffer;
    if (read >= 3 && static_cast<unsigned char>(buffer[0]) == 0xef && static_cast<unsigned char>(buffer[1]) == 0xbb && static_cast<unsigned char>(buffer[2]) == 0xbf) cursor += 3;
    Calibration* current = nullptr;
    int line_count = 0;
    while (*cursor && ok) {
        char* line = cursor;
        char* newline = std::strchr(cursor, '\n');
        if (newline) { *newline = 0; cursor = newline + 1; } else cursor += std::strlen(cursor);
        if (++line_count > 512) { ok = false; break; }
        line = trim(line);
        if (!*line || *line == '#') continue;
        const std::size_t length = std::strlen(line);
        if (std::strncmp(line, "[screen:", 8) == 0 && length > 9 && line[length - 1] == ']') {
            if (config.count == max_calibrations) { ok = false; break; }
            line[length - 1] = 0;
            current = &config.entries[config.count];
            if (!decode_identity(line + 8, current->id)) { ok = false; break; }
            for (int i = 0; i < config.count; ++i) if (lstrcmpiW(config.entries[i].id, current->id) == 0) ok = false;
            ++config.count; continue;
        }
        char* equals = std::strchr(line, '=');
        if (!equals) { ok = false; break; }
        *equals = 0;
        const char* key = trim(line);
        char* value = trim(equals + 1);
        if (!current) {
            if (std::strcmp(key, "enabled") == 0) ok = parse_bool(value, config.enabled);
            else if (std::strcmp(key, "pause_fullscreen") == 0) ok = parse_bool(value, config.pause_fullscreen);
            else if (std::strcmp(key, "version") == 0) ok = std::strcmp(value, "1") == 0;
        } else {
            char* end = nullptr;
            const double number = std::strtod(value, &end);
            if (end == value || *end != 0 || !std::isfinite(number)) { ok = false; break; }
            if (std::strcmp(key, "width_mm") == 0) { current->width = number; current->width_set = true; }
            else if (std::strcmp(key, "height_mm") == 0) { current->height = number; current->height_set = true; }
            else if (std::strcmp(key, "top_mm") == 0) current->top = number;
            else if (std::strcmp(key, "left_mm") == 0) { current->left = number; current->left_set = true; }
        }
    }
    for (int i = 0; i < config.count; ++i) {
        const Calibration& c = config.entries[i];
        if (!c.width_set || !c.height_set || c.width < 50 || c.width > 5000 || c.height < 50 || c.height > 5000 || std::abs(c.top) > 10000 || (c.left_set && std::abs(c.left) > 10000)) ok = false;
    }
    if (!ok) set_status(L"Paused: invalid settings; open the editor to repair them");
    return ok;
}
bool physical_identity(const wchar_t* adapter, wchar_t* identity) {
    DISPLAY_DEVICEW device = {}; device.cb = sizeof(device);
    if (!EnumDisplayDevicesW(adapter, 0, &device, EDD_GET_DEVICE_INTERFACE_NAME)) return false;
    if (std::wcsncmp(device.DeviceID, L"\\\\?\\", 4) != 0) { lstrcpynW(identity, device.DeviceID, 256); return identity[0] != 0; }
    wchar_t* first = std::wcschr(device.DeviceID, L'#');
    if (!first) return false;
    wchar_t* second = std::wcschr(first + 1, L'#');
    if (!second) return false;
    wchar_t* third = std::wcschr(second + 1, L'#');
    if (!third) return false;
    *second = 0; *third = 0;
    return std::swprintf(identity, 256, L"DISPLAY\\%ls\\%ls", first + 1, second + 1) > 0;
}
BOOL CALLBACK enumerate_monitor(HMONITOR monitor, HDC, LPRECT, LPARAM data) {
    bool& ok = *reinterpret_cast<bool*>(data);
    if (g.count == max_screens) { ok = false; set_status(L"Paused: more than sixteen screens"); return FALSE; }
    MONITORINFOEXW info = {}; info.cbSize = sizeof(info);
    wchar_t identity[256];
    if (!GetMonitorInfoW(monitor, reinterpret_cast<MONITORINFO*>(&info)) || !physical_identity(info.szDevice, identity)) {
        ok = false; set_status(L"Paused: a screen could not be identified"); return FALSE;
    }
    const Calibration* match = nullptr;
    for (int i = 0; i < g.config.count; ++i) if (lstrcmpiW(identity, g.config.entries[i].id) == 0) { match = &g.config.entries[i]; break; }
    if (!match) { ok = false; set_status(L"Paused: a connected screen needs calibration"); return FALSE; }
    sm::Panel& p = g.panels[g.count++];
    p = { { static_cast<int>(info.rcMonitor.left), static_cast<int>(info.rcMonitor.top), static_cast<int>(info.rcMonitor.right), static_cast<int>(info.rcMonitor.bottom) }, match->width, match->height, match->top, 0, 0 };
    if (match->left_set) p.left_mm = match->left;
    if (!sm::valid_panel(p)) { ok = false; set_status(L"Paused: invalid screen dimensions"); return FALSE; }
    sm::prepare(p);
    return TRUE;
}
bool load_layout() {
    bool ok = true; g.count = 0;
    const BOOL enumerated = EnumDisplayMonitors(nullptr, nullptr, enumerate_monitor, reinterpret_cast<LPARAM>(&ok));
    if (!ok) return false;
    if (!enumerated || g.count == 0) { set_status(L"Paused: cannot enumerate screens"); return false; }
    for (int i = 1; i < g.count; ++i) {
        const sm::Panel p = g.panels[i]; int j = i;
        while (j > 0 && g.panels[j - 1].pixels.left > p.pixels.left) { g.panels[j] = g.panels[j - 1]; --j; }
        g.panels[j] = p;
    }
    double left = 0;
    for (int i = 0; i < g.count; ++i) {
        if (std::isnan(g.panels[i].left_mm)) g.panels[i].left_mm = left;
        left = g.panels[i].left_mm + g.panels[i].width_mm;
        const auto& a = g.panels[i];
        for (int j = 0; j < i; ++j) {
            const auto& b = g.panels[j];
            const double overlap_x = (a.left_mm + a.width_mm < b.left_mm + b.width_mm ? a.left_mm + a.width_mm : b.left_mm + b.width_mm) - (a.left_mm > b.left_mm ? a.left_mm : b.left_mm);
            const double overlap_y = (a.top_mm + a.height_mm < b.top_mm + b.height_mm ? a.top_mm + a.height_mm : b.top_mm + b.height_mm) - (a.top_mm > b.top_mm ? a.top_mm : b.top_mm);
            if (overlap_x > 0.01 && overlap_y > 0.01) { set_status(L"Paused: screen rectangles overlap; adjust the layout"); return false; }
        }
    }
    return true;
}
bool should_bypass(const sm::Rect& screen) {
    RECT clip;
    if (GetClipCursor(&clip)) {
        LONG left = LONG_MAX, top = LONG_MAX, right = LONG_MIN, bottom = LONG_MIN;
        for (int i = 0; i < g.count; ++i) {
            const sm::Rect& p = g.panels[i].pixels;
            if (p.left < left) left = p.left;
            if (p.top < top) top = p.top;
            if (p.right > right) right = p.right;
            if (p.bottom > bottom) bottom = p.bottom;
        }
        if (clip.left > left || clip.top > top || clip.right < right || clip.bottom < bottom) return true;
    }
    if (!g.config.pause_fullscreen) return false;
    HWND foreground = GetForegroundWindow();
    if (!foreground || foreground == GetShellWindow() || foreground == GetDesktopWindow()) return false;
    RECT client; POINT origin = {};
    if (!GetClientRect(foreground, &client) || !ClientToScreen(foreground, &origin)) return false;
    return origin.x <= screen.left && origin.y <= screen.top &&
        static_cast<std::int64_t>(origin.x) + client.right >= screen.right && static_cast<std::int64_t>(origin.y) + client.bottom >= screen.bottom;
}
LRESULT CALLBACK mouse_proc(int code, WPARAM message, LPARAM data) {
    if (code < 0 || message != WM_MOUSEMOVE || g.warping || g.paused) return CallNextHookEx(nullptr, code, message, data);
    const MSLLHOOKSTRUCT& input = *reinterpret_cast<const MSLLHOOKSTRUCT*>(data);
    ++g.mouse_events;
    const sm::Point point = { static_cast<int>(input.pt.x), static_cast<int>(input.pt.y) };
    if ((input.flags & LLMHF_INJECTED) != 0) { g.source = -1; return CallNextHookEx(nullptr, code, message, data); }
    if (g.source < 0) { g.source = sm::find(g.panels, g.count, point); g.last = point; return CallNextHookEx(nullptr, code, message, data); }
    const sm::Rect& bounds = g.panels[g.source].pixels;
    sm::Point probe = point;
    if (point.x == bounds.right - 1 && (g.last.x < point.x || (g.last.x == point.x && g.last.y == point.y))) ++probe.x;
    else if (point.x == bounds.left && (g.last.x > point.x || (g.last.x == point.x && g.last.y == point.y))) --probe.x;
    if (point.y == bounds.bottom - 1 && (g.last.y < point.y || (g.last.x == point.x && g.last.y == point.y))) ++probe.y;
    else if (point.y == bounds.top && (g.last.y > point.y || (g.last.x == point.x && g.last.y == point.y))) --probe.y;
    if (bounds.contains(probe)) { g.last = point; return CallNextHookEx(nullptr, code, message, data); }
    if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0 || should_bypass(bounds)) {
        g.source = sm::find(g.panels, g.count, point); g.last = point; return CallNextHookEx(nullptr, code, message, data);
    }
    const sm::Transition move = sm::move(g.panels, g.count, g.source, probe);
    if (!move.reposition || (move.point.x == point.x && move.point.y == point.y)) { g.last = point; return CallNextHookEx(nullptr, code, message, data); }
    g.warping = true;
    const BOOL success = SetCursorPos(move.point.x, move.point.y);
    g.warping = false;
    if (!success) { g.source = -1; return CallNextHookEx(nullptr, code, message, data); }
    g.source = move.screen; g.last = move.point;
    return 1;
}
void install_hook() {
    stop_hook(); g.paused = true;
    POINT position;
    if (GetCursorPos(&position)) { g.last = { static_cast<int>(position.x), static_cast<int>(position.y) }; g.source = sm::find(g.panels, g.count, g.last); }
    g.hook = SetWindowsHookExW(WH_MOUSE_LL, mouse_proc, GetModuleHandleW(nullptr), 0);
    if (!g.hook) set_status(L"Paused: could not install the mouse hook");
    else { g.paused = false; set_status(L"Active - physical height correction"); }
}
void reload() {
    stop_hook(); g.paused = true;
    if (!parse_config(g.config)) { update_tray(); return; }
    if (!g.config.enabled) set_status(L"Paused - open settings to enable");
    else if (!load_layout()) { /* The loader supplied a specific status. */ }
    else if (g.count < 2) set_status(L"Waiting for a second screen");
    else if (g.user_paused) set_status(L"Paused - Windows cursor behavior");
    else if (g.locked) set_status(L"Paused while the session is locked");
    else install_hook();
    update_tray();
}
void schedule_reload() {
    stop_hook(); g.paused = true; set_status(L"Waiting for display changes"); update_tray();
    if (!SetTimer(g.window, 1, 600, nullptr)) { set_status(L"Paused: re-detect screens from the tray menu"); update_tray(); }
}
void open_settings() {
    wchar_t path[32768];
    if (std::swprintf(path, 32768, L"%lsSeamlessMice.Settings.exe", g.folder) < 0) return;
    // Launch the known executable directly: no shell activation or COM work in the engine.
    STARTUPINFOW startup = {}; startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESHOWWINDOW; startup.wShowWindow = SW_SHOWNORMAL;
    PROCESS_INFORMATION process = {};
    const bool restore = !g.paused;
    if (restore) { stop_hook(); g.paused = true; }
    const BOOL opened = CreateProcessW(path, nullptr, nullptr, nullptr, FALSE, 0, nullptr, g.folder, &startup, &process);
    if (opened) { CloseHandle(process.hThread); CloseHandle(process.hProcess); }
    if (restore) { install_hook(); update_tray(); }
    if (!opened) MessageBoxW(g.window, L"The settings executable could not be opened. Run build.cmd again.", L"Seamless Mice", MB_ICONERROR);
}
void show_menu() {
    HMENU menu = CreatePopupMenu(); if (!menu) return;
    AppendMenuW(menu, MF_GRAYED, 0, g.status); AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(menu, MF_STRING, 1, L"Settings...");
    AppendMenuW(menu, MF_STRING, 2, g.paused ? L"Resume correction" : L"Pause correction");
    AppendMenuW(menu, MF_STRING, 3, L"Re-detect screens / repair hook");
    AppendMenuW(menu, MF_SEPARATOR, 0, nullptr); AppendMenuW(menu, MF_STRING, 4, L"Exit");
    POINT point = {}; GetCursorPos(&point); SetForegroundWindow(g.window);
    const UINT choice = static_cast<UINT>(TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, point.x, point.y, 0, g.window, nullptr));
    DestroyMenu(menu);
    if (choice == 1) open_settings();
    else if (choice == 2) { if (g.paused) { g.user_paused = false; reload(); } else pause(); }
    else if (choice == 3) reload();
    else if (choice == 4) DestroyWindow(g.window);
    PostMessageW(g.window, WM_NULL, 0, 0);
}
LRESULT CALLBACK window_proc(HWND window, UINT message, WPARAM wp, LPARAM lp) {
    if (g.explorer_restart != 0 && message == g.explorer_restart) { g.tray_added = Shell_NotifyIconW(NIM_ADD, &g.tray) != FALSE; return 0; }
    switch (message) {
    case query_message: return wp == 1 ? static_cast<LRESULT>(g.mouse_events) : g.paused ? (g.user_paused ? 2 : 0) : 1;
    case reload_message: g.user_paused = false; reload(); return 0;
    case pause_message: pause(); return 0;
    case resume_message: g.user_paused = false; reload(); return 0;
    case settings_message: open_settings(); return 0;
    case exit_message: DestroyWindow(window); return 0;
    case WM_HOTKEY: if (g.paused) { g.user_paused = false; reload(); } else pause(); return 0;
    case tray_message:
        if (LOWORD(lp) == WM_LBUTTONDBLCLK) open_settings();
        else if (LOWORD(lp) == WM_RBUTTONUP || LOWORD(lp) == WM_CONTEXTMENU) show_menu();
        return 0;
    case WM_DISPLAYCHANGE: case WM_DEVICECHANGE: schedule_reload(); return 0;
    case WM_TIMER: if (wp == 1) { KillTimer(window, 1); reload(); } return 0;
    case WM_POWERBROADCAST:
        if (wp == PBT_APMSUSPEND) { stop_hook(); g.paused = true; set_status(L"Paused while sleeping"); update_tray(); }
        else if (wp == PBT_APMRESUMEAUTOMATIC || wp == PBT_APMRESUMESUSPEND) schedule_reload();
        return TRUE;
    case WM_WTSSESSION_CHANGE:
        if (wp == WTS_SESSION_LOCK) { g.locked = true; stop_hook(); g.paused = true; set_status(L"Paused while the session is locked"); update_tray(); }
        else if (wp == WTS_SESSION_UNLOCK) { g.locked = false; schedule_reload(); }
        return 0;
    case WM_CLOSE: DestroyWindow(window); return 0;
    case WM_DESTROY:
        stop_hook(); KillTimer(window, 1); UnregisterHotKey(window, 1); WTSUnRegisterSessionNotification(window);
        if (g.tray_added) Shell_NotifyIconW(NIM_DELETE, &g.tray);
        PostQuitMessage(0); return 0;
    default: return DefWindowProcW(window, message, wp, lp);
    }
}
} // namespace

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int) {
    // Manifest opts into PMv2 DPI awareness before any display coordinates are read.
    UINT command = settings_message; bool background = false;
    int argc = 0; LPWSTR* argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    if (!argv) return 1;
    bool valid = true;
    for (int i = 1; i < argc; ++i) {
        if (lstrcmpW(argv[i], L"--background") == 0) background = true;
        else if (lstrcmpW(argv[i], L"--exit") == 0) command = exit_message;
        else if (lstrcmpW(argv[i], L"--pause") == 0) command = pause_message;
        else if (lstrcmpW(argv[i], L"--resume") == 0) command = resume_message;
        else if (lstrcmpW(argv[i], L"--settings") != 0) valid = false;
    }
    LocalFree(argv); if (!valid) return 2;
    HWND existing = FindWindowW(window_class, nullptr);
    if (existing) { if (!background) PostMessageW(existing, command, 0, 0); return 0; }
    if (command != settings_message) return 0;
    HANDLE mutex = CreateMutexW(nullptr, TRUE, L"Local\\SeamlessMice.Engine.v1");
    if (!mutex) return 1;
    if (GetLastError() == ERROR_ALREADY_EXISTS) { CloseHandle(mutex); return 0; }
    const DWORD length = GetModuleFileNameW(nullptr, g.folder, 32768);
    if (length == 0 || length >= 32768) { CloseHandle(mutex); return 1; }
    wchar_t* filename = std::wcsrchr(g.folder, L'\\');
    if (!filename) { CloseHandle(mutex); return 1; }
    filename[1] = 0;
    wchar_t profile[32768] = {}; const DWORD profile_length = GetEnvironmentVariableW(L"APPDATA", profile, 32768);
    if (profile_length >= 32768 || !configuration_path(g.folder,profile,g.config_path,32768)) { CloseHandle(mutex); return 1; }
    g.paused = true; g.source = -1;
    WNDCLASSW cls = {}; cls.lpfnWndProc = window_proc; cls.hInstance = instance; cls.lpszClassName = window_class;
    if (!RegisterClassW(&cls)) { CloseHandle(mutex); return 1; }
    g.window = CreateWindowExW(0, window_class, L"Seamless Mice", 0, 0, 0, 0, 0, nullptr, nullptr, instance, nullptr);
    if (!g.window) { CloseHandle(mutex); return 1; }
    g.explorer_restart = RegisterWindowMessageW(L"TaskbarCreated");
    g.tray.cbSize = sizeof(g.tray); g.tray.hWnd = g.window; g.tray.uID = 1;
    g.tray.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP; g.tray.uCallbackMessage = tray_message;
    g.tray.hIcon = LoadIconW(instance, MAKEINTRESOURCEW(101)); lstrcpynW(g.tray.szTip, L"Seamless Mice", 128);
    g.tray_added = Shell_NotifyIconW(NIM_ADD, &g.tray) != FALSE;
    g.hotkey = RegisterHotKey(g.window, 1, MOD_NOREPEAT | MOD_CONTROL | MOD_ALT | MOD_SHIFT, 'M') != FALSE;
    WTSRegisterSessionNotification(g.window, NOTIFY_FOR_THIS_SESSION); reload();
    if (!background) open_settings();
    MSG message; int result;
    while ((result = static_cast<int>(GetMessageW(&message, nullptr, 0, 0))) > 0) { TranslateMessage(&message); DispatchMessageW(&message); }
    if (result < 0) DestroyWindow(g.window);
    ReleaseMutex(mutex); CloseHandle(mutex);
    return result < 0 ? 1 : 0;
}
