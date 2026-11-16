// Test-local native harness; compiled separately, never part of the bridge DLL.
#include "bridge.cpp"
#include <thread>
#include <cstdlib>
static void require(bool condition) { if (!condition) std::abort(); }
struct ProgressState {
  std::atomic<unsigned> active{0}, calls{0};
  std::atomic<bool> overlap{false};
};
static int32_t PSP_CALL progress_probe(void *context, uint64_t, uint64_t) {
  auto &state = *static_cast<ProgressState *>(context);
  if (state.active.fetch_add(1) != 0) state.overlap.store(true);
  std::this_thread::yield();
  state.calls.fetch_add(1);
  state.active.fetch_sub(1);
  return 0;
}
int main() {
  const auto text = std::string("nested/\xE6\x97\xA5\xE6\x9C\xAC-\xF0\x9F\x8E\xAE.iso");
  require(utf8(wide(text).c_str()) == text);
  const auto embedded = std::string("a\0b", 3);
  const auto w = wide(embedded);
  require(utf8(w.data(), w.size()) == embedded);
  for (auto invalid : {std::string("\xC0\xAF"), std::string("\xED\xA0\x80"), std::string("\xF0\x9F")}) {
    bool rejected = false;
    try { wide(invalid); } catch (const std::invalid_argument &) { rejected = true; }
    require(rejected);
  }
  ProgressState state;
  psp_callbacks cb{};
  cb.context = &state; cb.progress = progress_probe;
  Session session(cb);
  auto send = [&] { for (unsigned i = 0; i < 1000; ++i) require(session.progress() == S_OK); };
  std::thread first(send), second(send);
  first.join(); second.join();
  require(state.calls.load() == 2000 && !state.overlap.load());
  psp_cancel(&session);
  require(session.progress() == E_ABORT);
  require(state.calls.load() == 2000);
  return 0;
}
