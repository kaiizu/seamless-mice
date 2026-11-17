// Exercise the actual native parser and installed-mode path selection without starting
// a window, installing a hook, changing the running instance, or writing outside argv[1].
#include <cstdio>
#include "engine.cpp"

namespace {
int checks = 0;

bool expect(bool value, const char* name) {
    ++checks;
    if (!value) std::fprintf(stderr, "FAIL: %s\n", name);
    return value;
}

bool write_fixture(const wchar_t* path, const char* text) {
    HANDLE file = CreateFileW(path, GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    const DWORD length = static_cast<DWORD>(std::strlen(text));
    DWORD written = 0;
    const BOOL success = WriteFile(file, text, length, &written, nullptr);
    CloseHandle(file);
    return success && written == length;
}
} // namespace

int wmain(int argc, wchar_t** argv) {
    if (argc != 2) return 2;
    wchar_t folder[32768], marker[32768], config_file[32768], result[32768], expected[32768];
    if (std::swprintf(folder, 32768, L"%ls\\", argv[1]) < 0) return 2;
    CreateDirectoryW(argv[1], nullptr);
    std::swprintf(marker, 32768, L"%lsinstalled.flag", folder);
    std::swprintf(config_file, 32768, L"%lsfixture.ini", folder);
    DeleteFileW(marker);

    bool ok = expect(configuration_path(folder, L"X:\\Profile", result, 32768), "portable path resolves");
    std::swprintf(expected, 32768, L"%lsseamless-mice.ini", folder);
    ok &= expect(std::wcscmp(expected, result) == 0, "portable calibration is beside executable");
    ok &= expect(write_fixture(marker, "installed"), "create sandbox-local installed marker");
    ok &= expect(configuration_path(folder, L"X:\\Profile", result, 32768), "installed path resolves");
    ok &= expect(std::wcscmp(result, L"X:\\Profile\\SeamlessMice\\seamless-mice.ini") == 0, "installed calibration uses the supplied user profile without writing Program Files");
    ok &= expect(!configuration_path(folder, L"", result, 32768), "missing profile fails safely in installed mode");

    lstrcpynW(g.config_path, config_file, 32768);
    const char* valid = "version=1\nenabled=true\n[screen:c2NyZWVu]\nwidth_mm=345\nheight_mm=215\ntop_mm=60.5\nleft_mm=-12.5\n";
    ok &= expect(write_fixture(config_file, valid), "write parser fixture");
    Config parsed = {};
    ok &= expect(parse_config(parsed) && parsed.enabled && parsed.count == 1 && parsed.entries[0].left_set && parsed.entries[0].left == -12.5, "actual native parser preserves dragged coordinates");
    write_fixture(config_file, "version=1\nenabled=true\n[screen:c2NyZWVu]\nwidth_mm=345\nheight_mm=215\ntop_mm=0\n");
    ok &= expect(parse_config(parsed) && !parsed.entries[0].left_set, "legacy portable layouts still load");
    write_fixture(config_file, "enabled=true\n[screen:c2NyZWVu]\nwidth_mm=NaN\nheight_mm=215\n");
    ok &= expect(!parse_config(parsed), "non-finite dimensions are rejected");
    write_fixture(config_file, "enabled=true\n[screen:c2NyZWVu]\nwidth_mm=345\nheight_mm=215\nleft_mm=Infinity\n");
    ok &= expect(!parse_config(parsed), "non-finite dragged coordinates are rejected");
    write_fixture(config_file, "version=2\n");
    ok &= expect(!parse_config(parsed), "unknown schema version is rejected");
    write_fixture(config_file, "enabled=maybe\n");
    ok &= expect(!parse_config(parsed), "invalid activation state is rejected");

    DeleteFileW(config_file);
    DeleteFileW(marker);
    ok &= expect(parse_config(parsed) && !parsed.enabled, "first installed run stays inactive until Apply");
    std::printf("%s: %d native settings-path and parser checks.\n", ok ? "PASS" : "FAIL", checks);
    return ok ? 0 : 1;
}
