#include "geometry.hpp"
#include <cstdio>
#include <climits>
#include <chrono>

namespace {
int assertions = 0;
bool check(bool value, const char* label) {
    ++assertions;
    if (!value) std::fprintf(stderr, "FAIL: %s\n", label);
    return value;
}
}
int main() {
    sm::Panel panels[] = {
        { { -2560, 300, 0, 1900 }, 344.6, 215.4, 100, 0, 0 },
        { { 0, 0, 2560, 1440 }, 597.7, 336.2, 0, 0, 0 }
    };
    panels[0].left_mm = 0; panels[1].left_mm = 344.6;
    for (auto& p : panels) sm::prepare(p);
    bool ok = check(sm::valid_panel(panels[0]), "valid laptop geometry");
    ok &= check(!sm::move(panels, 2, 0, { -100, 900 }).reposition, "interior passes through");
    auto move = sm::move(panels, 2, 0, { 0, 1100 });
    ok &= check(move.reposition && move.screen == 1 && move.point.x == 0, "right crossing");
    const auto back = sm::move(panels, 2, 1, { -1, move.point.y });
    ok &= check(back.screen == 0 && std::abs(back.point.y - 1100) <= 2, "round trip");
    move = sm::move(panels, 2, 1, { -1, 10 });
    ok &= check(move.reposition && move.screen == 1 && move.point.x == 0, "exposed edge blocks");
    move = sm::move(panels, 2, 0, { INT_MAX, 1100 });
    ok &= check(move.screen == 1 && move.point.x == 2559, "positive overshoot clamps");
    move = sm::move(panels, 2, 1, { INT_MIN, 800 });
    ok &= check(move.screen == 0 && move.point.x == -2560, "negative overshoot clamps");
    ok &= check(sm::move(panels, 1, 0, { 0, 1100 }).screen == 0, "single monitor cannot cross");
    ok &= check(!sm::move(panels, 2, -1, { 0, 1100 }).reposition, "unknown source");
    sm::Panel vertical[] = {
        { {0,0,1000,1000},200,200,0,0,0,0 },
        { {1000,0,2000,1000},200,200,250,0,0,0 }
    };
    for (auto& p : vertical) sm::prepare(p);
    auto down = sm::move(vertical,2,0,{500,1000});
    ok &= check(down.screen == 1 && down.point.x == 1500 && down.point.y == 0,"vertical crossing follows dragged physical position");
    auto up = sm::move(vertical,2,1,{1500,-1});
    ok &= check(up.screen == 0 && up.point.x == 500 && up.point.y == 999,"reverse vertical crossing");
    auto reversed = panels[0]; reversed.left_mm = 1000;
    sm::Panel reordered[] = {reversed,panels[1]};
    ok &= check(sm::move(reordered,2,0,{-2561,1100}).screen == 1,"horizontal position overrides pixel monitor order");
    for (int source = 0; source < 2; ++source) {
        const auto& from = panels[source]; const auto& to = panels[1 - source];
        for (int y = from.pixels.top; y < from.pixels.bottom; ++y) {
            const double mm = from.top_mm + (y - from.pixels.top) * from.mm_per_y;
            move = sm::move(panels, 2, source, { source == 0 ? from.pixels.right : from.pixels.left - 1, y });
            ok &= check(panels[move.screen].pixels.contains(move.point), "correction lands inside a screen");
            if (mm >= to.top_mm && mm < to.top_mm + to.height_mm) {
                ok &= check(move.screen == 1 - source, "connected physical edge crosses");
                const double actual = to.top_mm + (move.point.y - to.pixels.top) * to.mm_per_y;
                ok &= check(std::abs(actual - mm) < to.mm_per_y + 0.000001, "physical height error below one target pixel");
            } else ok &= check(move.screen == source, "disconnected physical edge blocks");
        }
    }
    if (!ok) return 1;
    std::printf("PASS: %d native geometry assertions.\n", assertions);
    volatile std::uint64_t checksum = 0;
    const auto start = std::chrono::steady_clock::now();
    constexpr int count = 10000000;
    for (int i = 0; i < count; ++i) {
        const int source = i & 1;
        const sm::Point proposed = source == 0 ? sm::Point{ i & 7, 300 + i % 1600 } : sm::Point{ -1 - (i & 7), i % 1440 };
        const auto result = sm::move(panels, 2, source, proposed);
        // Vary height/direction and consume all result coordinates so the compiler cannot
        // replace the benchmark with repeated addition of a constant seam height.
        checksum = checksum + static_cast<std::uint64_t>(result.point.x + result.point.y + result.screen + (result.reposition ? 1 : 0));
    }
    const auto elapsed = std::chrono::steady_clock::now() - start;
    std::printf("Native geometry seam: %.1f ns/event; checksum %llu. Excludes Win32 input delivery.\n",
        std::chrono::duration<double, std::nano>(elapsed).count() / count, static_cast<unsigned long long>(checksum));
    return 0;
}
