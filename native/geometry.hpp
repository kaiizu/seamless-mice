#pragma once
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <limits>

namespace sm {
struct Point { int x, y; };
struct Rect {
    int left, top, right, bottom;
    int width() const { return right - left; }
    int height() const { return bottom - top; }
    bool contains(Point p) const { return p.x >= left && p.x < right && p.y >= top && p.y < bottom; }
};
struct Panel {
    Rect pixels;
    double width_mm, height_mm, top_mm;
    double mm_per_x, mm_per_y;
    double left_mm = std::numeric_limits<double>::quiet_NaN();
};
struct Transition { bool reposition; int screen; Point point; };
inline int clamp(int value, int low, int high) { return value < low ? low : value > high ? high : value; }
inline bool valid_panel(const Panel& p) {
    return p.pixels.width() > 1 && p.pixels.height() > 1 &&
        std::isfinite(p.width_mm) && std::isfinite(p.height_mm) && std::isfinite(p.top_mm) &&
        p.width_mm >= 50 && p.height_mm >= 50 && p.width_mm <= 5000 && p.height_mm <= 5000 &&
        std::abs(p.top_mm) <= 10000;
}
inline void prepare(Panel& p) {
    p.mm_per_x = p.width_mm / p.pixels.width();
    p.mm_per_y = p.height_mm / p.pixels.height();
}
inline int find(const Panel* panels, int count, Point point) {
    for (int i = 0; i < count; ++i) if (panels[i].pixels.contains(point)) return i;
    return -1;
}
// Independent of Win32. No allocation, IO, synchronization, polling or mutable global state.
inline Transition move(const Panel* panels, int count, int source, Point proposed) {
    if (source < 0 || source >= count) return { false, find(panels, count, proposed), proposed };
    const Panel& from = panels[source];
    const Rect& bounds = from.pixels;
    if (bounds.contains(proposed)) return { false, source, proposed };
    const int direction = proposed.x < bounds.left ? 0 : proposed.x >= bounds.right ? 1 : proposed.y < bounds.top ? 2 : 3;
    const bool horizontal = direction < 2;
    const double orthogonal = horizontal ? from.top_mm + (static_cast<double>(proposed.y) - bounds.top) * from.mm_per_y : from.left_mm + (static_cast<double>(proposed.x) - bounds.left) * from.mm_per_x;
    int next = -1; double nearest = std::numeric_limits<double>::max();
    for (int i = 0; i < count; ++i) {
        if (i == source) continue;
        const Panel& candidate = panels[i];
        const double start = horizontal ? candidate.top_mm : candidate.left_mm;
        const double extent = horizontal ? candidate.height_mm : candidate.width_mm;
        if (orthogonal < start || orthogonal >= start + extent) continue;
        const double gap = direction == 0 ? from.left_mm - candidate.left_mm - candidate.width_mm :
            direction == 1 ? candidate.left_mm - from.left_mm - from.width_mm :
            direction == 2 ? from.top_mm - candidate.top_mm - candidate.height_mm : candidate.top_mm - from.top_mm - from.height_mm;
        if (gap >= -0.01 && gap < nearest) { nearest = gap; next = i; }
    }
    if (next < 0) {
        const Point blocked = { clamp(proposed.x, bounds.left, bounds.right - 1), clamp(proposed.y, bounds.top, bounds.bottom - 1) };
        return { blocked.x != proposed.x || blocked.y != proposed.y, source, blocked };
    }
    const Panel& to = panels[next];
    const double overshoot = direction == 0 ? static_cast<double>(bounds.left) - 1 - proposed.x : direction == 1 ? static_cast<double>(proposed.x) - bounds.right : direction == 2 ? static_cast<double>(bounds.top) - 1 - proposed.y : static_cast<double>(proposed.y) - bounds.bottom;
    double scaled = overshoot * (horizontal ? from.mm_per_x / to.mm_per_x : from.mm_per_y / to.mm_per_y);
    const int dimension = horizontal ? to.pixels.width() : to.pixels.height();
    if (scaled < 0) scaled = 0;
    if (scaled > dimension - 1) scaled = dimension - 1;
    const int step = static_cast<int>(scaled);
    const int x = horizontal ? (direction == 1 ? to.pixels.left + step : to.pixels.right - 1 - step) : clamp(static_cast<int>(std::floor(to.pixels.left + (orthogonal - to.left_mm) / to.mm_per_x)), to.pixels.left, to.pixels.right - 1);
    const int y = horizontal ? clamp(static_cast<int>(std::floor(to.pixels.top + (orthogonal - to.top_mm) / to.mm_per_y)), to.pixels.top, to.pixels.bottom - 1) : (direction == 3 ? to.pixels.top + step : to.pixels.bottom - 1 - step);
    return { true, next, { x, y } };
}
} // namespace sm
