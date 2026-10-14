// Thin ABI wrapper around Google's libwebp 1.6.0 decoder and demuxer.
// First animation frame is rendered onto a transparent canvas, as in
// libwebp's WebPAnimDecoder. No animation playback or color profile conversion.
#include <stdint.h>
#include <string.h>
#include "webp/decode.h"
#include "webp/demux.h"
#define API __declspec(dllexport)

static uint32_t read_number(const uint8_t* p, int bytes, int little) {
  uint32_t result = 0; int i;
  for (i = 0; i < bytes; ++i) result |= (uint32_t)p[i] << (8 * (little ? i : bytes - i - 1));
  return result;
}

static int orientation_of(WebPDemuxer* demux) {
  WebPChunkIterator chunk; int result = 1;
  if (WebPDemuxGetChunk(demux, "EXIF", 1, &chunk)) {
    const uint8_t* p = chunk.chunk.bytes; size_t size = chunk.chunk.size;
    int little; uint32_t offset, count, i;
    if (size >= 6 && memcmp(p, "Exif\0\0", 6) == 0) { p += 6; size -= 6; }
    if (size < 8 || !((p[0] == 'I' && p[1] == 'I') || (p[0] == 'M' && p[1] == 'M'))) goto done;
    little = p[0] == 'I';
    if (read_number(p + 2, 2, little) != 42) goto done;
    offset = read_number(p + 4, 4, little);
    if ((uint64_t)offset + 2 > size) goto done;
    count = read_number(p + offset, 2, little);
    if ((uint64_t)offset + 2 + (uint64_t)count * 12 > size) goto done;
    for (i = 0; i < count; ++i) {
      const uint8_t* entry = p + offset + 2 + i * 12;
      if (read_number(entry, 2, little) == 274 && read_number(entry + 2, 2, little) == 3 &&
          read_number(entry + 4, 4, little) == 1) {
        uint32_t value = read_number(entry + 8, 2, little);
        if (value >= 1 && value <= 8) result = (int)value;
        break;
      }
    }
done:
    WebPDemuxReleaseChunkIterator(&chunk);
  }
  return result;
}

API int fiv_webp_info(const uint8_t* data, size_t size, int* width, int* height, int* orientation) {
  WebPData input = { data, size };
  WebPDemuxer* demux = WebPDemux(&input);
  if (!demux) return 0;
  *width = (int)WebPDemuxGetI(demux, WEBP_FF_CANVAS_WIDTH);
  *height = (int)WebPDemuxGetI(demux, WEBP_FF_CANVAS_HEIGHT);
  *orientation = orientation_of(demux);
  WebPDemuxDelete(demux);
  return *width > 0 && *height > 0;
}

API int fiv_webp_decode(const uint8_t* data, size_t size, uint8_t* output,
                        size_t output_size, int width, int height) {
  WebPData input = { data, size };
  WebPDemuxer* demux;
  WebPIterator frame;
  WebPDecoderConfig config;
  int canvas_width, canvas_height, x, y, right, bottom, result = 0;
  size_t stride;
  if (!data || !output || width <= 0 || height <= 0 || width > 32000000 ||
      (uint64_t)width * height > 32000000) return 0;
  stride = (size_t)width * 4;
  if (stride * height > output_size) return 0;
  demux = WebPDemux(&input);
  if (!demux) return 0;
  if (!WebPDemuxGetFrame(demux, 1, &frame)) goto cleanup;
  canvas_width = (int)WebPDemuxGetI(demux, WEBP_FF_CANVAS_WIDTH);
  canvas_height = (int)WebPDemuxGetI(demux, WEBP_FF_CANVAS_HEIGHT);
  if (canvas_width <= 0 || canvas_height <= 0 || !frame.complete ||
      frame.x_offset < 0 || frame.y_offset < 0 || frame.width <= 0 || frame.height <= 0 ||
      (int64_t)frame.x_offset + frame.width > canvas_width ||
      (int64_t)frame.y_offset + frame.height > canvas_height) goto frame_cleanup;
  // Map both edges of the first frame to the scaled canvas. Integer ceil
  // preserves small fragments; the clipped edges remain inside the canvas.
  x = (int)((int64_t)frame.x_offset * width / canvas_width);
  y = (int)((int64_t)frame.y_offset * height / canvas_height);
  right = (int)(((int64_t)(frame.x_offset + frame.width) * width + canvas_width - 1) / canvas_width);
  bottom = (int)(((int64_t)(frame.y_offset + frame.height) * height + canvas_height - 1) / canvas_height);
  if (!WebPInitDecoderConfig(&config)) goto frame_cleanup;
  memset(output, 0, stride * height);
  config.output.colorspace = MODE_BGRA;
  config.output.is_external_memory = 1;
  config.output.u.RGBA.rgba = output + y * stride + x * 4;
  config.output.u.RGBA.stride = (int)stride;
  config.output.u.RGBA.size = output_size - y * stride - x * 4;
  config.options.use_scaling = (right - x != frame.width || bottom - y != frame.height);
  config.options.scaled_width = right - x;
  config.options.scaled_height = bottom - y;
  result = WebPDecode(frame.fragment.bytes, frame.fragment.size, &config) == VP8_STATUS_OK;
  WebPFreeDecBuffer(&config.output);
frame_cleanup:
  WebPDemuxReleaseIterator(&frame);
cleanup:
  WebPDemuxDelete(demux);
  return result;
}
