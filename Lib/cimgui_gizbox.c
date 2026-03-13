#include <windows.h>
#include <stdint.h>
#include <stdlib.h>
#include <stdbool.h>

#include "dcimgui.h"

#ifndef GIZBOX_CIMGUI_API
#define GIZBOX_CIMGUI_API __declspec(dllexport)
#endif

typedef struct Gizbox_ImGuiBoolResult_t
{
    int32_t changed;
    int32_t value;
} Gizbox_ImGuiBoolResult;

typedef struct Gizbox_ImGuiFloatResult_t
{
    int32_t changed;
    float value;
} Gizbox_ImGuiFloatResult;

typedef struct Gizbox_ImGuiIntResult_t
{
    int32_t changed;
    int32_t value;
} Gizbox_ImGuiIntResult;

/// <summary>
/// 将 Gizbox 的宽字符串转换为 UTF-8。
/// 调用方负责 free 返回值。
/// </summary>
static char* gz_wide_to_utf8(const wchar_t* text)
{
    if (text == NULL)
        return NULL;

    int size = WideCharToMultiByte(CP_UTF8, 0, text, -1, NULL, 0, NULL, NULL);
    if (size <= 0)
        return NULL;

    char* buffer = (char*)malloc((size_t)size);
    if (buffer == NULL)
        return NULL;

    WideCharToMultiByte(CP_UTF8, 0, text, -1, buffer, size, NULL, NULL);
    return buffer;
}

/// <summary>
/// 将 UTF-8 字符串转换为宽字符串。
/// 返回值由 Gizbox 侧按普通字符串释放即可。
/// </summary>
static wchar_t* gz_utf8_to_wide_owned(const char* text)
{
    if (text == NULL)
        return NULL;

    int size = MultiByteToWideChar(CP_UTF8, 0, text, -1, NULL, 0);
    if (size <= 0)
        return NULL;

    wchar_t* buffer = (wchar_t*)malloc((size_t)size * sizeof(wchar_t));
    if (buffer == NULL)
        return NULL;

    MultiByteToWideChar(CP_UTF8, 0, text, -1, buffer, size);
    return buffer;
}

static inline ImGuiContext* gz_ctx(uint64_t p)
{
    return (ImGuiContext*)(uintptr_t)p;
}

static inline ImDrawList* gz_draw_list(uint64_t p)
{
    return (ImDrawList*)(uintptr_t)p;
}

GIZBOX_CIMGUI_API uint64_t Gizbox_ImGui_CreateContext(void)
{
    return (uint64_t)(uintptr_t)ImGui_CreateContext(NULL);
}

GIZBOX_CIMGUI_API void Gizbox_ImGui_DestroyContext(uint64_t ctx)
{
    ImGui_DestroyContext(gz_ctx(ctx));
}

GIZBOX_CIMGUI_API uint64_t Gizbox_ImGui_GetCurrentContext(void)
{
    return (uint64_t)(uintptr_t)ImGui_GetCurrentContext();
}

GIZBOX_CIMGUI_API void Gizbox_ImGui_SetCurrentContext(uint64_t ctx)
{
    ImGui_SetCurrentContext(gz_ctx(ctx));
}

GIZBOX_CIMGUI_API uint64_t Gizbox_ImGui_GetIO(void)
{
    return (uint64_t)(uintptr_t)ImGui_GetIO();
}

GIZBOX_CIMGUI_API uint64_t Gizbox_ImGui_GetDrawData(void)
{
    return (uint64_t)(uintptr_t)ImGui_GetDrawData();
}

GIZBOX_CIMGUI_API const wchar_t* Gizbox_ImGui_GetVersion(void)
{
    return gz_utf8_to_wide_owned(ImGui_GetVersion());
}

GIZBOX_CIMGUI_API void Gizbox_ImGui_StyleColorsDark(void)
{
    ImGui_StyleColorsDark(NULL);
}

GIZBOX_CIMGUI_API void Gizbox_ImGui_NewFrame(void)
{
    ImGui_NewFrame();
}

GIZBOX_CIMGUI_API void Gizbox_ImGui_EndFrame(void)
{
    ImGui_EndFrame();
}

GIZBOX_CIMGUI_API void Gizbox_ImGui_Render(void)
{
    ImGui_Render();
}

GIZBOX_CIMGUI_API void Gizbox_ImGui_ShowDemoWindow(void)
{
    ImGui_ShowDemoWindow(NULL);
}

GIZBOX_CIMGUI_API void Gizbox_ImGui_SetNextWindowPos(ImVec2 pos, int32_t cond)
{
    ImGui_SetNextWindowPos(pos, cond);
}

GIZBOX_CIMGUI_API void Gizbox_ImGui_SetNextWindowSize(ImVec2 size, int32_t cond)
{
    ImGui_SetNextWindowSize(size, cond);
}

GIZBOX_CIMGUI_API void Gizbox_ImGui_SetDisplaySize(ImVec2 size, int32_t cond)
{
    ImGuiIO* io = ImGui_GetIO();
	io->DisplaySize = size;
}

GIZBOX_CIMGUI_API int32_t Gizbox_ImGui_Begin(const wchar_t* name, int32_t flags)
{
    char* name_utf8 = gz_wide_to_utf8(name);
    bool opened = ImGui_Begin(name_utf8, NULL, flags);
    free(name_utf8);
    return opened ? 1 : 0;
}

GIZBOX_CIMGUI_API void Gizbox_ImGui_End(void)
{
    ImGui_End();
}

GIZBOX_CIMGUI_API int32_t Gizbox_ImGui_BeginChild(const wchar_t* str_id, ImVec2 size, int32_t child_flags, int32_t window_flags)
{
    char* id_utf8 = gz_wide_to_utf8(str_id);
    bool opened = ImGui_BeginChild(id_utf8, size, child_flags, window_flags);
    free(id_utf8);
    return opened ? 1 : 0;
}

GIZBOX_CIMGUI_API void Gizbox_ImGui_EndChild(void)
{
    ImGui_EndChild();
}

GIZBOX_CIMGUI_API void Gizbox_ImGui_Text(const wchar_t* text)
{
    char* text_utf8 = gz_wide_to_utf8(text);
    ImGui_TextUnformattedEx(text_utf8, NULL);
    free(text_utf8);
}

GIZBOX_CIMGUI_API int32_t Gizbox_ImGui_Button(const wchar_t* label)
{
    char* label_utf8 = gz_wide_to_utf8(label);
    bool pressed = ImGui_ButtonEx(label_utf8, (ImVec2) { 0.0f, 0.0f });
    free(label_utf8);
    return pressed ? 1 : 0;
}

GIZBOX_CIMGUI_API int32_t Gizbox_ImGui_ButtonEx(const wchar_t* label, ImVec2 size)
{
    char* label_utf8 = gz_wide_to_utf8(label);
    bool pressed = ImGui_ButtonEx(label_utf8, size);
    free(label_utf8);
    return pressed ? 1 : 0;
}

GIZBOX_CIMGUI_API Gizbox_ImGuiBoolResult Gizbox_ImGui_Checkbox(const wchar_t* label, int32_t value)
{
    char* label_utf8 = gz_wide_to_utf8(label);
    bool v = value != 0;
    bool changed = ImGui_Checkbox(label_utf8, &v);
    free(label_utf8);

    Gizbox_ImGuiBoolResult result;
    result.changed = changed ? 1 : 0;
    result.value = v ? 1 : 0;
    return result;
}

GIZBOX_CIMGUI_API Gizbox_ImGuiFloatResult Gizbox_ImGui_InputFloat(const wchar_t* label, float value)
{
    char* label_utf8 = gz_wide_to_utf8(label);
    float v = value;
    bool changed = ImGui_InputFloatEx(label_utf8, &v, 0.0f, 0.0f, "%.3f", 0);
    free(label_utf8);

    Gizbox_ImGuiFloatResult result;
    result.changed = changed ? 1 : 0;
    result.value = v;
    return result;
}

GIZBOX_CIMGUI_API Gizbox_ImGuiIntResult Gizbox_ImGui_InputInt(const wchar_t* label, int32_t value)
{
    char* label_utf8 = gz_wide_to_utf8(label);
    int v = value;
    bool changed = ImGui_InputIntEx(label_utf8, &v, 1, 100, 0);
    free(label_utf8);

    Gizbox_ImGuiIntResult result;
    result.changed = changed ? 1 : 0;
    result.value = v;
    return result;
}

GIZBOX_CIMGUI_API Gizbox_ImGuiFloatResult Gizbox_ImGui_SliderFloat(const wchar_t* label, float value, float min_value, float max_value)
{
    char* label_utf8 = gz_wide_to_utf8(label);
    float v = value;
    bool changed = ImGui_SliderFloatEx(label_utf8, &v, min_value, max_value, "%.3f", 0);
    free(label_utf8);

    Gizbox_ImGuiFloatResult result;
    result.changed = changed ? 1 : 0;
    result.value = v;
    return result;
}

GIZBOX_CIMGUI_API void Gizbox_ImGui_Separator(void)
{
    ImGui_Separator();
}

GIZBOX_CIMGUI_API void Gizbox_ImGui_SameLine(float offset_from_start_x, float spacing)
{
    ImGui_SameLineEx(offset_from_start_x, spacing);
}

GIZBOX_CIMGUI_API void Gizbox_ImGui_NewLine(void)
{
    ImGui_NewLine();
}

GIZBOX_CIMGUI_API void Gizbox_ImGui_Dummy(ImVec2 size)
{
    ImGui_Dummy(size);
}

GIZBOX_CIMGUI_API ImVec2 Gizbox_ImGui_GetContentRegionAvail(void)
{
    return ImGui_GetContentRegionAvail();
}

GIZBOX_CIMGUI_API ImVec2 Gizbox_ImGui_GetCursorScreenPos(void)
{
    return ImGui_GetCursorScreenPos();
}

GIZBOX_CIMGUI_API void Gizbox_ImGui_SetCursorScreenPos(ImVec2 pos)
{
    ImGui_SetCursorScreenPos(pos);
}

GIZBOX_CIMGUI_API int32_t Gizbox_ImGui_IsItemHovered(int32_t flags)
{
    return ImGui_IsItemHovered(flags) ? 1 : 0;
}

GIZBOX_CIMGUI_API uint32_t Gizbox_ImGui_GetColorU32(ImVec4 color)
{
    return ImGui_GetColorU32ImVec4(color);
}

GIZBOX_CIMGUI_API ImVec2 Gizbox_ImGui_CalcTextSize(const wchar_t* text)
{
    char* text_utf8 = gz_wide_to_utf8(text);
    ImVec2 size = ImGui_CalcTextSizeEx(text_utf8, NULL, false, -1.0f);
    free(text_utf8);
    return size;
}

GIZBOX_CIMGUI_API uint64_t Gizbox_ImGui_GetWindowDrawList(void)
{
    return (uint64_t)(uintptr_t)ImGui_GetWindowDrawList();
}

GIZBOX_CIMGUI_API uint64_t Gizbox_ImGui_GetForegroundDrawList(void)
{
    return (uint64_t)(uintptr_t)ImGui_GetForegroundDrawList();
}

GIZBOX_CIMGUI_API uint64_t Gizbox_ImGui_GetBackgroundDrawList(void)
{
    return (uint64_t)(uintptr_t)ImGui_GetBackgroundDrawList();
}

GIZBOX_CIMGUI_API void Gizbox_ImDrawList_AddLine(uint64_t draw_list, ImVec2 p1, ImVec2 p2, uint32_t col, float thickness)
{
    ImDrawList_AddLineEx(gz_draw_list(draw_list), p1, p2, col, thickness);
}

GIZBOX_CIMGUI_API void Gizbox_ImDrawList_AddRect(uint64_t draw_list, ImVec2 p_min, ImVec2 p_max, uint32_t col, float rounding, int32_t flags, float thickness)
{
    ImDrawList_AddRectEx(gz_draw_list(draw_list), p_min, p_max, col, rounding, flags, thickness);
}

GIZBOX_CIMGUI_API void Gizbox_ImDrawList_AddRectFilled(uint64_t draw_list, ImVec2 p_min, ImVec2 p_max, uint32_t col, float rounding, int32_t flags)
{
    ImDrawList_AddRectFilledEx(gz_draw_list(draw_list), p_min, p_max, col, rounding, flags);
}

GIZBOX_CIMGUI_API void Gizbox_ImDrawList_AddCircle(uint64_t draw_list, ImVec2 center, float radius, uint32_t col, int32_t num_segments, float thickness)
{
    ImDrawList_AddCircleEx(gz_draw_list(draw_list), center, radius, col, num_segments, thickness);
}

GIZBOX_CIMGUI_API void Gizbox_ImDrawList_AddCircleFilled(uint64_t draw_list, ImVec2 center, float radius, uint32_t col, int32_t num_segments)
{
    ImDrawList_AddCircleFilled(gz_draw_list(draw_list), center, radius, col, num_segments);
}

GIZBOX_CIMGUI_API void Gizbox_ImDrawList_AddText(uint64_t draw_list, ImVec2 pos, uint32_t col, const wchar_t* text)
{
    char* text_utf8 = gz_wide_to_utf8(text);
    ImDrawList_AddTextEx(gz_draw_list(draw_list), pos, col, text_utf8, NULL);
    free(text_utf8);
}