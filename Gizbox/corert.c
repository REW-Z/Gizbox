#include <stdio.h>
#include <wchar.h>
#include <stdint.h>
#include <inttypes.h>
#include <string.h>
#include <stdlib.h>

#define WIDEN2(x) L##x
#define WIDEN(x) WIDEN2(x)
#define WPRId64 WIDEN("%" PRId64)

#define COUNT_OF(a) (sizeof(a) / sizeof((a)[0]))

//double ctest2(int a, long b, float c, double d);
//
//double ctest(int a, long b, float c, double d)
//{
//	int local1 = ctest2(1, 2L, 3.0F, 4.0D);
//    local1++;
//	a += 2;
//    b += 2L;
//    c += 2.0F;
//    d += 2.0D;
//
//    int local2 = ctest2(a, b, c, d);
//
//	return local1 + local2;
//}
//double ctest2(int a, long b, float c, double d)
//{
//    return a + b * c / d;
//}

// 输出未格式化的 Unicode 宽字符串，不附加换行
void Console__Print(const wchar_t* text)
{
    if (!text) return;
    fputws(text, stdout);
    fflush(stdout);
}

// 输出未格式化的 Unicode 宽字符串，换行
void Console__PrintLn(const wchar_t* text)
{
    // 如果有文本则先输出文本
    if (text && *text)
    {
        fputws(text, stdout);
    }
    // 输出宽字符换行并刷新
    fputwc(L'\n', stdout);
    fflush(stdout);
}

// 获取一行输入 Unicode 宽字符  
const wchar_t* Console__ReadLn()
{
    static wchar_t buf[1024];
    if (fgetws(buf, (int)COUNT_OF(buf), stdin) == NULL)
        return NULL; // EOF 或错误

    size_t len = wcslen(buf);
    if (len > 0 && buf[len - 1] == L'\n')
        buf[len - 1] = L'\0';
    return buf;
}

// Int32 -> 字符串
const wchar_t* Core__Extern__IntToString(int32_t value)
{
    static wchar_t buf[32];
    swprintf_s(buf, COUNT_OF(buf), L"%d", value);
    buf[COUNT_OF(buf) - 1] = L'\0';
    return buf;
}

// Int64 -> 字符串
const wchar_t* Core__Extern__LongToString(int64_t value)
{
    static wchar_t buf[32];
    swprintf_s(buf, COUNT_OF(buf), WPRId64, value);
    buf[COUNT_OF(buf) - 1] = L'\0';
    return buf;
}

// float -> 字符串（9 位有效数字）
const wchar_t* Core__Extern__FloatToString(float value)
{
    static wchar_t buf[64];
    swprintf_s(buf, COUNT_OF(buf), L"%.9g", (double)value);
    buf[COUNT_OF(buf) - 1] = L'\0';
    return buf;
}

// double -> 字符串（17 位有效数字）
const wchar_t* Core__Extern__DoubleToString(double value)
{
    static wchar_t buf[64];
    swprintf_s(buf, COUNT_OF(buf), L"%.17g", value);
    buf[COUNT_OF(buf) - 1] = L'\0';
    return buf;
}

// Bool -> 字符串
const wchar_t* Core__Extern__BoolToString(int32_t value)
{
    // 直接返回静态常量宽字符串（已以 L'\0' 结尾）
    return value ? L"True" : L"False";
}

// Char -> 字符串
const wchar_t* Core__Extern__CharToString(int32_t value)
{
    static wchar_t buf[2];
    buf[0] = (wchar_t)value;
    buf[1] = L'\0';
    return buf;
}

// Concat string a + b -> 字符串
const wchar_t* Core__Extern__Concat(const wchar_t* a, const wchar_t* b)
{
    static wchar_t* s_buf = NULL;
    static size_t s_cap = 0;

    size_t la = (a != NULL) ? wcslen(a) : 0;
    size_t lb = (b != NULL) ? wcslen(b) : 0;
    size_t need = la + lb + 1; // 包含结尾的 L'\0'

    if (need > s_cap)
    {
        wchar_t* newBuf = (wchar_t*)realloc(s_buf, need * sizeof(wchar_t));
        if (!newBuf)
        {
            // 分配失败时返回空串
            static const wchar_t empty[] = L"";
            return empty;
        }
        s_buf = newBuf;
        s_cap = need;
    }

    if (la > 0) wmemcpy(s_buf, a, la);
    if (lb > 0) wmemcpy(s_buf + la, b, lb);
    s_buf[la + lb] = L'\0';

    return s_buf;
}