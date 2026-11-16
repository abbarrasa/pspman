#include "bridge.h"
#include "CPP/Common/MyInitGuid.h"
#include "CPP/Common/MyCom.h"
#include "CPP/7zip/Archive/IArchive.h"
#include "CPP/7zip/IPassword.h"
#include "C/7zVersion.h"
#include <atomic>
#include <climits>
#include <cstring>
#include <limits>
#include <stdexcept>
#include <mutex>
#include <string>
#include <vector>
#ifndef _WIN32
#include <dlfcn.h>
#endif
static_assert(MY_VER_MAJOR == 26 && MY_VER_MINOR == 6, "Pinned engine source required");
namespace {
std::wstring wide(const std::string &s) {
  std::wstring result;
  for (size_t i = 0; i < s.size();) {
    const auto first = static_cast<unsigned char>(s[i++]);
    uint32_t point = first;
    unsigned remaining = 0;
    uint32_t minimum = 0;
    if (first >= 0xC2 && first <= 0xDF) { point &= 0x1F; remaining = 1; minimum = 0x80; }
    else if (first >= 0xE0 && first <= 0xEF) { point &= 0x0F; remaining = 2; minimum = 0x800; }
    else if (first >= 0xF0 && first <= 0xF4) { point &= 7; remaining = 3; minimum = 0x10000; }
    else if (first >= 0x80) throw std::invalid_argument("Invalid UTF-8");
    while (remaining--) {
      if (i == s.size()) throw std::invalid_argument("Truncated UTF-8");
      const auto next = static_cast<unsigned char>(s[i++]);
      if ((next & 0xC0) != 0x80) throw std::invalid_argument("Invalid UTF-8 continuation");
      point = (point << 6) | (next & 0x3F);
    }
    if (point < minimum || point > 0x10FFFF || (point >= 0xD800 && point <= 0xDFFF))
      throw std::invalid_argument("Invalid Unicode scalar");
    // 7-Zip UString/BSTR retains UTF-16 units even in Linux 32-bit wchar_t.
    if (point > 0xFFFF) {
      point -= 0x10000;
      result.push_back(static_cast<wchar_t>(0xD800 + (point >> 10)));
      result.push_back(static_cast<wchar_t>(0xDC00 + (point & 0x3FF)));
      continue;
    }
    result.push_back(static_cast<wchar_t>(point));
  }
  return result;
}
std::string utf8(const wchar_t *s, size_t length = SIZE_MAX) {
  std::string result;
  if (!s) return result;
  const wchar_t *end = s + (length == SIZE_MAX ? std::wcslen(s) : length);
  while (s != end) {
    uint32_t point = static_cast<uint32_t>(*s++);
    if (point >= 0xD800 && point <= 0xDBFF) {
      if (s == end) throw std::invalid_argument("Truncated UTF-16");
      const uint32_t low = static_cast<uint32_t>(*s++);
      if (low < 0xDC00 || low > 0xDFFF) throw std::invalid_argument("Invalid UTF-16");
      point = 0x10000 + ((point - 0xD800) << 10) + low - 0xDC00;
    }
    if (point > 0x10FFFF || (point >= 0xD800 && point <= 0xDFFF)) throw std::invalid_argument("Invalid Unicode scalar");
    if (point < 0x80) result.push_back(static_cast<char>(point));
    else {
      if (point < 0x800) result.push_back(static_cast<char>(0xC0 | (point >> 6)));
      else {
        if (point < 0x10000) result.push_back(static_cast<char>(0xE0 | (point >> 12)));
        else {
          result.push_back(static_cast<char>(0xF0 | (point >> 18)));
          result.push_back(static_cast<char>(0x80 | ((point >> 12) & 0x3F)));
        }
        result.push_back(static_cast<char>(0x80 | ((point >> 6) & 0x3F)));
      }
      result.push_back(static_cast<char>(0x80 | (point & 0x3F)));
    }
  }
  return result;
}
struct Variant : PROPVARIANT {
  Variant() { std::memset(static_cast<void *>(this), 0, sizeof(*this)); }
  ~Variant() { VariantClear(this); }
};
struct Session {
  psp_callbacks cb;
#ifdef _WIN32
  HMODULE library = nullptr;
#else
  void *library = nullptr;
#endif
  CMyComPtr<IInArchive> archive;
  std::atomic<bool> cancelled{false};
  std::atomic<int> failure{0};
  bool passwordRequested = false, missing = false;
  std::string name;
  uint64_t decoded = 0, written = 0;
  std::mutex progressMutex;
  explicit Session(const psp_callbacks &c) : cb(c) {}
  ~Session() {
    if (archive) { archive->Close(); archive.Release(); }
#ifdef _WIN32
    if (library) FreeLibrary(library);
#else
    if (library) dlclose(library);
#endif
  }
  HRESULT check() { return cancelled.load() || failure.load() ? E_ABORT : S_OK; }
  HRESULT callback(int code) {
    if (code) { int expected = 0; failure.compare_exchange_strong(expected, code == 1 ? 1 : code == 10 ? 10 : 2); }
    return code ? E_ABORT : check();
  }
  int result(HRESULT hr) {
    if (failure.load()) return failure.load();
    if (cancelled.load()) return 1;
    if (hr == S_OK) return 0;
    if (missing) return 6;
    if (passwordRequested && hr == S_FALSE) return 4;
    return hr == S_FALSE ? 5 : 9;
  }
  HRESULT progress() {
    std::lock_guard<std::mutex> lock(progressMutex);
    if (check() != S_OK) return E_ABORT;
    return callback(cb.progress(cb.context, decoded, written));
  }
  HRESULT password(BSTR *value) {
    if (check() != S_OK) return E_ABORT;
    passwordRequested = true;
    std::vector<uint8_t> buffer(65536);
    uint32_t length = 0;
    const int code = cb.password(cb.context, buffer.data(), static_cast<uint32_t>(buffer.size()), &length);
    if (code || length > buffer.size()) return callback(code ? code : 2);
    std::wstring text;
    try { text = wide(std::string(reinterpret_cast<char *>(buffer.data()), length)); }
    catch (...) { std::fill(buffer.begin(), buffer.end(), 0); return callback(2); }
    std::fill(buffer.begin(), buffer.end(), 0);
    *value = SysAllocStringLen(text.data(), static_cast<UINT>(text.size()));
    std::fill(text.begin(), text.end(), 0);
    return *value ? check() : E_OUTOFMEMORY;
  }
  bool flag(uint32_t index, PROPID id) {
    Variant v;
    if (archive->GetProperty(index, id, &v) != S_OK) { failure.store(9); return false; }
    return v.vt == VT_BOOL && v.boolVal != VARIANT_FALSE;
  }
  bool link(uint32_t index) {
    for (auto id : {kpidSymLink, kpidHardLink}) {
      Variant v;
      if (archive->GetProperty(index, id, &v) != S_OK) { failure.store(9); return false; }
      if (v.vt == VT_BSTR && v.bstrVal && *v.bstrVal) return true;
    }
    Variant v;
    if (archive->GetProperty(index, kpidPosixAttrib, &v) != S_OK) { failure.store(9); return false; }
    return v.vt == VT_UI4 && (v.ulVal & 0170000) == 0120000;
  }
};
class Input final : public IInStream, public CMyUnknownImp {
  public: Z7_COM_UNKNOWN_IMP_1(IInStream)
  Session &s; uint64_t handle;
public:
  Input(Session &session, uint64_t h) : s(session), handle(h) {}
  ~Input() { s.cb.close(s.cb.context, handle); }
  Z7_COM7F_IMF(Read(void *data, UInt32 size, UInt32 *processed)) override {
    if (processed) *processed = 0;
    if (s.check() != S_OK) return E_ABORT;
    uint32_t n = 0;
    const int code = s.cb.read(s.cb.context, handle, data, size, &n);
    if (n > size) return s.callback(2);
    if (processed) *processed = n;
    return s.callback(code);
  }
  Z7_COM7F_IMF(Seek(Int64 offset, UInt32 origin, UInt64 *position)) override {
    if (s.check() != S_OK) return E_ABORT;
    uint64_t pos = 0;
    const int code = s.cb.seek(s.cb.context, handle, offset, origin, &pos);
    if (position) *position = pos;
    return s.callback(code);
  }
};
HRESULT input(Session &s, const std::string &name, IInStream **stream) {
  *stream = nullptr;
  if (s.check() != S_OK) return E_ABORT;
  uint64_t handle = 0;
  const int code = s.cb.open(s.cb.context, reinterpret_cast<const uint8_t *>(name.data()), static_cast<uint32_t>(name.size()), &handle);
  if (code == 6) { s.missing = true; return S_FALSE; }
  if (code) return s.callback(code);
  try {
    auto *p = new Input(s, handle);
    *stream = p; (*stream)->AddRef();
    return S_OK;
  } catch (...) { s.cb.close(s.cb.context, handle); return E_OUTOFMEMORY; }
}
class OpenCallback final : public IArchiveOpenCallback, public IArchiveOpenVolumeCallback,
  public ICryptoGetTextPassword, public CMyUnknownImp {
  public: Z7_COM_UNKNOWN_IMP_3(IArchiveOpenCallback, IArchiveOpenVolumeCallback, ICryptoGetTextPassword)
  Session &s;
public:
  explicit OpenCallback(Session &session) : s(session) {}
  Z7_COM7F_IMF(SetTotal(const UInt64 *, const UInt64 *)) override { return s.check(); }
  Z7_COM7F_IMF(SetCompleted(const UInt64 *, const UInt64 *)) override { return s.check(); }
  Z7_COM7F_IMF(CryptoGetTextPassword(BSTR *p)) override { return s.password(p); }
  Z7_COM7F_IMF(GetProperty(PROPID id, PROPVARIANT *v)) override {
    VariantClear(v); v->vt = VT_EMPTY;
    if (id == kpidName) {
      try {
        const auto w = wide(s.name);
        v->bstrVal = SysAllocStringLen(w.data(), static_cast<UINT>(w.size()));
        if (!v->bstrVal) return E_OUTOFMEMORY;
        v->vt = VT_BSTR;
      } catch (...) { return E_FAIL; }
    }
    return s.check();
  }
  Z7_COM7F_IMF(GetStream(const wchar_t *name, IInStream **p)) override {
    try { return input(s, utf8(name), p); } catch (...) { return E_FAIL; }
  }
};
class Output final : public ISequentialOutStream, public CMyUnknownImp {
  public: Z7_COM_UNKNOWN_IMP_1(ISequentialOutStream)
  Session &s; uint32_t index; bool skip;
public:
  Output(Session &session, uint32_t i, bool discard) : s(session), index(i), skip(discard) {}
  Z7_COM7F_IMF(Write(const void *data, UInt32 size, UInt32 *processed)) override {
    if (processed) *processed = 0;
    if (s.check() != S_OK) return E_ABORT;
    uint32_t n = size;
    {
      std::lock_guard<std::mutex> lock(s.progressMutex);
      if (s.decoded > UINT64_MAX - size) return s.callback(2);
      s.decoded += size;
    }
    int code = 0;
    if (!skip) { n = 0; code = s.cb.write(s.cb.context, index, data, size, &n); }
    if (n > size || (!code && n != size)) return s.callback(2);
    {
      std::lock_guard<std::mutex> lock(s.progressMutex);
      if (!skip) s.written += n;
    }
    if (processed) *processed = n;
    if (code) return s.callback(code);
    return s.progress();
  }
};
class ExtractCallback final : public IArchiveExtractCallback, public ICryptoGetTextPassword,
  public CMyUnknownImp {
  public: Z7_COM_UNKNOWN_IMP_2(IArchiveExtractCallback, ICryptoGetTextPassword)
  Z7_IFACE_COM7_IMP(IProgress)
  Session &s; uint32_t index = 0; bool active = false;
public:
  explicit ExtractCallback(Session &session) : s(session) {}
  ~ExtractCallback() { finish(s.failure.load() ? s.failure.load() : 1); }
  void finish(int result) {
    if (active) {
      active = false;
      if (s.failure.load()) result = s.failure.load();
      else if (s.cancelled.load()) result = 1;
      s.callback(s.cb.end(s.cb.context, index, result));
    }
  }
  Z7_COM7F_IMF(CryptoGetTextPassword(BSTR *p)) override { return s.password(p); }
  Z7_COM7F_IMF(GetStream(UInt32 i, ISequentialOutStream **p, Int32 mode)) override {
    *p = nullptr;
    if (s.check() != S_OK) return E_ABORT;
    if (mode != NArchive::NExtract::NAskMode::kExtract) return S_OK;
    index = i;
    const int code = s.cb.begin(s.cb.context, i);
    if (code != 0 && code != 11) return s.callback(code);
    active = true;
    try {
      if (s.flag(i, kpidIsDir)) return S_OK;
      auto *out = new Output(s, i, code == 11);
      *p = out; (*p)->AddRef(); return S_OK;
    } catch (...) { return s.callback(2); }
  }
  Z7_COM7F_IMF(PrepareOperation(Int32)) override { return s.check(); }
  Z7_COM7F_IMF(SetOperationResult(Int32 result)) override {
    int code = 0;
    if (result != NArchive::NExtract::NOperationResult::kOK) {
      if (result == NArchive::NExtract::NOperationResult::kWrongPassword) code = 3;
      else if (result == NArchive::NExtract::NOperationResult::kUnsupportedMethod) code = 7;
      else if ((result == NArchive::NExtract::NOperationResult::kDataError ||
          result == NArchive::NExtract::NOperationResult::kCRCError ||
          result == NArchive::NExtract::NOperationResult::kHeadersError) && s.flag(index, kpidEncrypted)) code = 4;
      else code = s.missing ? 6 : 5;
      int expected = 0; s.failure.compare_exchange_strong(expected, code);
    }
    finish(code);
    return s.check();
  }
};
Z7_COM7F_IMF(ExtractCallback::SetTotal(UInt64)) { return s.check(); }
Z7_COM7F_IMF(ExtractCallback::SetCompleted(const UInt64 *)) { return s.progress(); }
}
extern "C" {
uint32_t PSP_CALL psp_abi() { return 1; }
uint32_t PSP_CALL psp_engine_version() { return 2606; }
int32_t PSP_CALL psp_create(const uint8_t *path, uint32_t length, const psp_callbacks *cb, void **session) {
  if (!session) return 8;
  *session = nullptr;
  if (!path || !length || !cb || cb->size != sizeof(psp_callbacks) || cb->abi != 1 ||
      !cb->open || !cb->read || !cb->seek || !cb->close || !cb->password || !cb->begin || !cb->write || !cb->end || !cb->progress) return 8;
  try {
    const std::string name(reinterpret_cast<const char *>(path), length);
    if (name.find('\0') != std::string::npos) return 8;
    auto *s = new Session(*cb);
#ifdef _WIN32
    s->library = LoadLibraryExW(wide(name).c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
#else
    if (name.front() != '/') { delete s; return 8; }
    s->library = dlopen(name.c_str(), RTLD_NOW | RTLD_LOCAL);
#endif
    if (!s->library) { delete s; return 9; }
    *session = s; return 0;
  } catch (...) { return 9; }
}
int32_t PSP_CALL psp_open(void *session, uint32_t format, const uint8_t *name, uint32_t length) {
  if (!session || !name || !length || (format != 1 && format != 3 && format != 0xCC)) return 8;
  auto &s = *static_cast<Session *>(session);
  try {
    if (s.archive) return 8;
    s.name.assign(reinterpret_cast<const char *>(name), length);
    if (s.name.find('\0') != std::string::npos) return 8;
    const GUID clsid = {0x23170F69, 0x40C1, 0x278A, {0x10,0,0,1,0x10,static_cast<unsigned char>(format),0,0}};
    typedef HRESULT (WINAPI *Factory)(const GUID *, const GUID *, void **);
#ifdef _WIN32
    auto factory = reinterpret_cast<Factory>(GetProcAddress(s.library, "CreateObject"));
#else
    auto factory = reinterpret_cast<Factory>(dlsym(s.library, "CreateObject"));
#endif
    if (!factory) return 9;
    HRESULT hr = factory(&clsid, &IID_IInArchive, reinterpret_cast<void **>(&s.archive));
    if (hr != S_OK) return 9;
    CMyComPtr<IInStream> stream;
    hr = input(s, s.name, &stream);
    if (hr != S_OK) return s.missing ? 6 : s.result(hr);
    CMyComPtr<IArchiveOpenCallback> cb = new OpenCallback(s);
    const UInt64 limit = 0;
    hr = s.archive->Open(stream, &limit, cb);
    if (hr == S_OK) {
      Variant v;
      hr = s.archive->GetArchiveProperty(kpidErrorFlags, &v);
      if (hr == S_OK && v.vt == VT_UI4 && v.ulVal != 0) return s.missing ? 6 : 5;
    }
    return s.result(hr);
  } catch (...) { return s.result(E_FAIL); }
}
int32_t PSP_CALL psp_count(void *session, uint32_t *count) {
  if (!session || !count) return 8;
  auto &s = *static_cast<Session *>(session);
  if (!s.archive) return 8;
  try { return s.result(s.archive->GetNumberOfItems(count)); } catch (...) { return 9; }
}
int32_t PSP_CALL psp_metadata(void *session, uint32_t index, psp_entry *entry, uint8_t *path, uint32_t capacity, uint32_t *length) {
  if (!session || !entry || !length) return 8;
  auto &s = *static_cast<Session *>(session);
  if (!s.archive) return 8;
  try {
    uint32_t count = 0;
    if (s.archive->GetNumberOfItems(&count) != S_OK || index >= count) return 8;
    entry->index = index; entry->size = 0;
    entry->flags = (s.flag(index, kpidIsDir) ? 1 : 0) | (s.link(index) ? 2 : 0) | (s.flag(index, kpidEncrypted) ? 4 : 0);
    if (s.failure.load()) return s.failure.load();
    Variant size;
    if (s.archive->GetProperty(index, kpidSize, &size) != S_OK) return 9;
    if (size.vt == VT_UI8) { entry->flags |= 8; entry->size = size.uhVal.QuadPart; }
    Variant p;
    if (s.archive->GetProperty(index, kpidPath, &p) != S_OK) return 9;
    const auto text = p.vt == VT_BSTR ? utf8(p.bstrVal, SysStringLen(p.bstrVal)) : std::string();
    if (text.size() > UINT32_MAX) return 9;
    *length = static_cast<uint32_t>(text.size());
    if (capacity < text.size() || (!path && !text.empty())) return 8;
    if (!text.empty()) std::memcpy(path, text.data(), text.size());
    return 0;
  } catch (...) { return 9; }
}
int32_t PSP_CALL psp_extract(void *session, const uint32_t *indices, uint32_t count) {
  if (!session || (count && count != UINT32_MAX && !indices)) return 8;
  auto &s = *static_cast<Session *>(session);
  if (!s.archive) return 8;
  try {
    uint32_t total = 0;
    if (s.archive->GetNumberOfItems(&total) != S_OK) return 9;
    if (count != UINT32_MAX) for (uint32_t i = 0; i < count; ++i)
      if (indices[i] >= total || (i && indices[i] <= indices[i-1])) return 8;
    s.decoded = s.written = 0;
    CMyComPtr<IArchiveExtractCallback> cb = new ExtractCallback(s);
    const HRESULT hr = s.archive->Extract(indices, count, 0, cb);
    cb.Release();
    return s.result(hr);
  } catch (...) { return s.result(E_FAIL); }
}
void PSP_CALL psp_cancel(void *session) { if (session) static_cast<Session *>(session)->cancelled.store(true); }
void PSP_CALL psp_destroy(void *session) { delete static_cast<Session *>(session); }
}
