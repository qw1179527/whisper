package main

import "C"
import (
	"fmt"
	"os"
	"time"
)

//export WhisperProbeRun
func WhisperProbeRun() *C.char {
	msgs := []string{
		"低语计划 · 原生探针（Go c-shared / 无 dex）",
		fmt.Sprintf("时间戳 %d", time.Now().Unix()),
		fmt.Sprintf("配置可读: %v", fileExists("data/config.json")),
	}
	out := ""
	for _, m := range msgs {
		out += m + "\n"
	}
	return C.CString(out)
}

func fileExists(p string) bool { _, err := os.Stat(p); return err == nil }

func main() {}
