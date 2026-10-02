#include <aaudio/AAudio.h>
#include <stdio.h>
#include <string.h>

static int opened = 0;
static void try_open(AAudioStreamBuilder *b, aaudio_direction_t dir, const char *tag) {
  AAudioStream *s = NULL;
  aaudio_result_t r = AAudioStreamBuilder_openStream(b, &s);
  printf("%-24s open=%d (%s)\n", tag, r, r == AAUDIO_OK ? "OK" : AAudio_convertResultToText(r));
  if (r == AAUDIO_OK && s) { opened++; AAudioStream_close(s); }
}

int main(void) {
  printf("低语计划 · 原生 AAudio 探针（无 JVM/无 dex，纯 bionic）\n");
  printf("AAudio 版本: %d\n", AAudio_convertResultToText ? 1 : 0);
  AAudioStreamBuilder *b = NULL;
  aaudio_result_t r = AAudio_createStreamBuilder(&b);
  printf("createStreamBuilder=%d\n", r);
  if (r != AAUDIO_OK) { printf("AAudio 不可用，此路径终止\n"); return 1; }
  AAudioStreamBuilder_setSampleRate(b, 16000);
  AAudioStreamBuilder_setChannelCount(b, 1);
  AAudioStreamBuilder_setFormat(b, AAUDIO_FORMAT_PCM_I16);
  AAudioStreamBuilder_setDirection(b, AAUDIO_DIRECTION_INPUT);
  try_open(b, AAUDIO_DIRECTION_INPUT, "input(default)");
  AAudioStreamBuilder_setInputPreset(b, AAUDIO_INPUT_PRESET_VOICE_COMMUNICATION);
  try_open(b, AAUDIO_DIRECTION_INPUT, "input(voice_comm/AEC)");
  AAudioStreamBuilder_setInputPreset(b, AAUDIO_INPUT_PRESET_CAMCORDER);
  try_open(b, AAUDIO_DIRECTION_INPUT, "input(camcorder)");
  AAudioStreamBuilder_delete(b);
  printf("成功打开 %d 路输入流\n", opened);
  return 0;
}
