#ifndef PSPMAN_ARCHIVE_BRIDGE_H
#define PSPMAN_ARCHIVE_BRIDGE_H
#include <stdint.h>
#ifdef _WIN32
#define PSP_API __declspec(dllexport)
#define PSP_CALL __cdecl
#else
#define PSP_API __attribute__((visibility("default")))
#define PSP_CALL
#endif
#ifdef __cplusplus
extern "C" {
#endif
/* ABI 1; all strings are length-delimited UTF-8, borrowed for the call only.
   Callbacks return 0 on success, 1 on cancel, 2 on I/O failure;
   open may return 6 (missing), password 10 (input required), begin 11 (skip).
   No callback may throw. Only cancel may run concurrently with a session call.
   Source handles belong to the caller, close is invoked exactly once after open.
   Other session calls are serialized by the caller; the caller must await
   native return before destroy. Destroy does not join an active native call. */
typedef struct {
  uint32_t size, abi;
  void *context;
  int32_t (PSP_CALL *open)(void *, const uint8_t *, uint32_t, uint64_t *);
  int32_t (PSP_CALL *read)(void *, uint64_t, void *, uint32_t, uint32_t *);
  int32_t (PSP_CALL *seek)(void *, uint64_t, int64_t, uint32_t, uint64_t *);
  void (PSP_CALL *close)(void *, uint64_t);
  int32_t (PSP_CALL *password)(void *, uint8_t *, uint32_t, uint32_t *);
  int32_t (PSP_CALL *begin)(void *, uint32_t);
  int32_t (PSP_CALL *write)(void *, uint32_t, const void *, uint32_t, uint32_t *);
  int32_t (PSP_CALL *end)(void *, uint32_t, int32_t);
  int32_t (PSP_CALL *progress)(void *, uint64_t, uint64_t);
} psp_callbacks;
typedef struct {
  uint32_t index, flags; /* directory=1, link=2, encrypted=4, size known=8 */
  uint64_t size;
} psp_entry;
/* Results: 0 success, 1 cancel, 2 callback I/O, 3 wrong password,
   4 password-or-damage indeterminate, 5 corrupt, 6 missing part,
   7 unsupported, 8 invalid argument/ABI, 9 engine failure, 10 password required. */
PSP_API uint32_t PSP_CALL psp_abi(void);
PSP_API uint32_t PSP_CALL psp_engine_version(void); /* pinned source version 2606 */
PSP_API int32_t PSP_CALL psp_create(const uint8_t *engine_path, uint32_t length,
    const psp_callbacks *, void **session);
/* format: ZIP=1, RAR4=3, RAR5=0xCC; name is source leaf name, never destination. */
PSP_API int32_t PSP_CALL psp_open(void *, uint32_t format, const uint8_t *name, uint32_t length);
PSP_API int32_t PSP_CALL psp_count(void *, uint32_t *);
/* Required path capacity returned through length, excluding terminator. */
PSP_API int32_t PSP_CALL psp_metadata(void *, uint32_t, psp_entry *, uint8_t *path, uint32_t capacity, uint32_t *length);
/* Sorted, distinct indices; count UINT32_MAX selects all. Zero selects none. */
PSP_API int32_t PSP_CALL psp_extract(void *, const uint32_t *indices, uint32_t count);
PSP_API void PSP_CALL psp_cancel(void *);
PSP_API void PSP_CALL psp_destroy(void *);
#ifdef __cplusplus
}
#endif
#endif
