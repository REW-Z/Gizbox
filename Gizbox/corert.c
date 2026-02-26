#include <stdio.h>
#include <wchar.h>
#include <stdint.h>
#include <inttypes.h>
#include <string.h>
#include <stdlib.h>
#include <math.h>

#define WIDEN2(x) L##x
#define WIDEN(x) WIDEN2(x)
#define WPRId64 WIDEN("%" PRId64)

#define COUNT_OF(a) (sizeof(a) / sizeof((a)[0]))

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

// Object -> Int64地址
const int64_t Core__Extern__AddrOfClassObject(void* value)
{
	return (int64_t)(intptr_t)value;
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

int32_t Core__Extern__StringLength(const wchar_t* v)
{
    if (!v) return 0;
    return (int32_t)wcslen(v);
}

int32_t Core__Extern__StringCompare(const wchar_t* a, const wchar_t* b)
{
    if (a == NULL && b == NULL) return 0;
    if (a == NULL) return -1;
    if (b == NULL) return 1;
    return wcscmp(a, b);
}

const wchar_t* Core__Extern__StringClone(const wchar_t* v)
{
    if (!v)
        return NULL;
    size_t len = wcslen(v);
    wchar_t* buf = (wchar_t*)malloc((len + 1) * sizeof(wchar_t));
    if (!buf)
        return NULL;
    wmemcpy(buf, v, len);
    buf[len] = L'\0';
    return buf;
}

void Core__Extern__StringFree(wchar_t* v)
{
    if (v)
        free(v);
}

const wchar_t* Core__Extern__StringConcatNew(const wchar_t* a, const wchar_t* b)
{
    size_t la = (a != NULL) ? wcslen(a) : 0;
    size_t lb = (b != NULL) ? wcslen(b) : 0;
    wchar_t* buf = (wchar_t*)malloc((la + lb + 1) * sizeof(wchar_t));
    if (!buf)
        return NULL;
    if (la > 0) wmemcpy(buf, a, la);
    if (lb > 0) wmemcpy(buf + la, b, lb);
    buf[la + lb] = L'\0';
    return buf;
}

const wchar_t* Core__Extern__StringSubstr(const wchar_t* v, int32_t start, int32_t length)
{
    if (!v || length <= 0)
        return Core__Extern__StringClone(L"");

    size_t len = wcslen(v);
    if (start < 0) start = 0;
    if ((size_t)start >= len)
        return Core__Extern__StringClone(L"");

    size_t maxLen = len - (size_t)start;
    size_t copyLen = (size_t)length > maxLen ? maxLen : (size_t)length;
    wchar_t* buf = (wchar_t*)malloc((copyLen + 1) * sizeof(wchar_t));
    if (!buf)
        return NULL;
    wmemcpy(buf, v + start, copyLen);
    buf[copyLen] = L'\0';
    return buf;
}

int32_t Core__Extern__StringIndexOf(const wchar_t* v, const wchar_t* target)
{
    if (!v || !target)
        return -1;
    const wchar_t* found = wcsstr(v, target);
    if (!found)
        return -1;
    return (int32_t)(found - v);
}

int32_t Core__Extern__StringIndexOfChar(const wchar_t* v, int32_t target)
{
    if (!v)
        return -1;
    const wchar_t* found = wcschr(v, (wchar_t)target);
    if (!found)
        return -1;
    return (int32_t)(found - v);
}

float Core__Extern__Sin(float v)
{
    return (float)sin((double)v);
}

float Core__Extern__Cos(float v)
{
    return (float)cos((double)v);
}

int32_t IO__FileWriteAllText(const wchar_t* path, const wchar_t* text)
{
    if (!path)
        return 0;
    FILE* fp = _wfopen(path, L"wb");
    if (!fp)
        return 0;
    if (text)
        fputws(text, fp);
    fclose(fp);
    return 1;
}

const wchar_t* IO__FileReadAllText(const wchar_t* path)
{
    if (!path)
        return NULL;
    FILE* fp = _wfopen(path, L"rb");
    if (!fp)
        return NULL;
    fseek(fp, 0, SEEK_END);
    long size = ftell(fp);
    fseek(fp, 0, SEEK_SET);
    if (size <= 0)
    {
        fclose(fp);
        return Core__Extern__StringClone(L"");
    }

    size_t count = (size_t)size / sizeof(wchar_t);
    wchar_t* buf = (wchar_t*)malloc((count + 1) * sizeof(wchar_t));
    if (!buf)
    {
        fclose(fp);
        return NULL;
    }
    size_t readCount = fread(buf, sizeof(wchar_t), count, fp);
    buf[readCount] = L'\0';
    fclose(fp);
    return buf;
}

int32_t IO__FileWriteAllBytes(const wchar_t* path, const wchar_t* data)
{
    if (!path)
        return 0;
    FILE* fp = _wfopen(path, L"wb");
    if (!fp)
        return 0;
    if (data)
        fwrite(data, sizeof(wchar_t), wcslen(data), fp);
    fclose(fp);
    return 1;
}

const wchar_t* IO__FileReadAllBytes(const wchar_t* path)
{
    return IO__FileReadAllText(path);
}

int64_t IO__FileOpen(const wchar_t* path, const wchar_t* mode)
{
    if (!path || !mode)
        return 0;
    FILE* fp = _wfopen(path, mode);
    return (int64_t)(intptr_t)fp;
}

void IO__FileClose(int64_t handle)
{
    if (handle == 0)
        return;
    FILE* fp = (FILE*)(intptr_t)handle;
    fclose(fp);
}

void IO__FileFlush(int64_t handle)
{
    if (handle == 0)
        return;
    FILE* fp = (FILE*)(intptr_t)handle;
    fflush(fp);
}

int32_t IO__FileWrite(int64_t handle, const wchar_t* text)
{
    if (handle == 0 || !text)
        return 0;
    FILE* fp = (FILE*)(intptr_t)handle;
    return fputws(text, fp) >= 0 ? 1 : 0;
}

const wchar_t* IO__FileReadLine(int64_t handle)
{
    if (handle == 0)
        return NULL;
    FILE* fp = (FILE*)(intptr_t)handle;
    wchar_t buf[1024];
    if (!fgetws(buf, (int)COUNT_OF(buf), fp))
        return NULL;
    size_t len = wcslen(buf);
    if (len > 0 && buf[len - 1] == L'\n')
        buf[len - 1] = L'\0';
    return Core__Extern__StringClone(buf);
}

const wchar_t* IO__FileReadAll(int64_t handle)
{
    if (handle == 0)
        return NULL;
    FILE* fp = (FILE*)(intptr_t)handle;
    long pos = ftell(fp);
    fseek(fp, 0, SEEK_END);
    long size = ftell(fp);
    fseek(fp, pos, SEEK_SET);
    if (size <= pos)
        return Core__Extern__StringClone(L"");
    size_t count = (size_t)(size - pos) / sizeof(wchar_t);
    wchar_t* buf = (wchar_t*)malloc((count + 1) * sizeof(wchar_t));
    if (!buf)
        return NULL;
    size_t readCount = fread(buf, sizeof(wchar_t), count, fp);
    buf[readCount] = L'\0';
    return buf;
}