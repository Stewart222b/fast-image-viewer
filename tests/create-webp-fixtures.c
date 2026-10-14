// Optional fixture generator, built with -DFIV_WEBP_BUILD_FIXTURES=ON.
// Run from tests/fixtures/webp. The DLL/app never includes an encoder.
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "webp/encode.h"
static void write32(FILE* f, uint32_t n) {
  uint8_t b[4] = { n, n >> 8, n >> 16, n >> 24 }; fwrite(b, 1, 4, f);
}
static void write24(FILE* f, uint32_t n) {
  uint8_t b[3] = { n, n >> 8, n >> 16 }; fwrite(b, 1, 3, f);
}
static void save(const char* name, const uint8_t* bytes, size_t count) {
  FILE* f = fopen(name, "wb"); if (!f) exit(1); fwrite(bytes, 1, count, f); fclose(f);
}
int main(void) {
  uint8_t rgba[16] = { 255, 0, 0, 255, 0, 255, 0, 128, 0, 0, 255, 0, 255, 255, 255, 255 };
  uint8_t* lossless; size_t count = WebPEncodeLosslessRGBA(rgba, 2, 2, 8, &lossless);
  uint8_t* lossy; size_t lossy_count = WebPEncodeRGBA(rgba, 2, 2, 8, 90, &lossy);
  uint8_t* large = (uint8_t*)malloc(6000ULL * 6000 * 4); uint8_t* encoded; size_t large_count;
  FILE* f; int i; size_t chunk_size;
  if (!count || !lossy_count || !large) return 1;
  save("lossless.webp", lossless, count); save("lossy.webp", lossy, lossy_count);
  {
    uint8_t exif[26] = { 'I','I',42,0,8,0,0,0,1,0,18,1,3,0,1,0,0,0,6,0,0,0,0,0,0,0 };
    f = fopen("oriented.webp", "wb"); if (!f) return 1;
    fwrite("RIFF", 1, 4, f); write32(f, (uint32_t)(4 + 18 + count - 12 + 34));
    fwrite("WEBPVP8X", 1, 8, f); write32(f, 10); write32(f, 0x18); write24(f, 1); write24(f, 1);
    fwrite(lossless + 12, 1, count - 12, f); fwrite("EXIF", 1, 4, f); write32(f, 26); fwrite(exif, 1, 26, f); fclose(f);
  }
  memset(large, 255, 6000ULL * 6000 * 4);
  large_count = WebPEncodeLosslessRGBA(large, 6000, 6000, 24000, &encoded); free(large);
  if (!large_count) return 1; save("large.webp", encoded, large_count); WebPFree(encoded);
  // Two frames on a 4x4 canvas. First 2x2 frame at (2,2), second at (0,0).
  // This deliberately tests offset/transparent canvas and first-frame selection.
  chunk_size = count - 12;
  f = fopen("animated.webp", "wb"); if (!f) return 1;
  fwrite("RIFF", 1, 4, f); write32(f, (uint32_t)(4 + 18 + 14 + 2 * (24 + chunk_size)));
  fwrite("WEBPVP8X", 1, 8, f); write32(f, 10); write32(f, 0x12); write24(f, 3); write24(f, 3);
  fwrite("ANIM", 1, 4, f); write32(f, 6); write32(f, 0); fputc(0, f); fputc(0, f);
  for (i = 0; i < 2; ++i) {
    fwrite("ANMF", 1, 4, f); write32(f, (uint32_t)(16 + chunk_size));
    write24(f, i == 0 ? 1 : 0); write24(f, i == 0 ? 1 : 0);
    write24(f, 1); write24(f, 1); write24(f, 100); fputc(2, f);
    fwrite(lossless + 12, 1, chunk_size, f);
  }
  fclose(f); WebPFree(lossless); WebPFree(lossy); return 0;
}
