package main

import (
	"encoding/json"
	"fmt"
	"os"
)

type cfg struct {
	StimulusSources map[string]json.RawMessage `json:"stimulusSources"`
	Monsters map[string]json.RawMessage `json:"monsters"`
	Sanity   map[string]json.RawMessage `json:"sanity"`
}

func main() {
	b, err := os.ReadFile("data/config.json")
	if err != nil {
		fmt.Println("读取配置失败:", err)
		os.Exit(1)
	}
	var c cfg
	if err := json.Unmarshal(b, &c); err != nil {
		fmt.Println("解析失败:", err)
		os.Exit(1)
	}
	type src struct {
		Intensity int      `json:"intensity"`
		RadiusM   *float64 `json:"radiusM"`
		Label     string   `json:"label"`
	}
	real := 0
	for _, raw := range c.StimulusSources {
		var s src
		if err := json.Unmarshal(raw, &s); err != nil || s.Label == "" {
			continue // 跳过 _ 前缀注释键（如 _balanceNote）
		}
		real++
	}
	fmt.Printf("Go 直读 data/config.json 成功：声纹源 %d 项（含注释键）/ %d 项有效，怪物 %d 项，理智分组 %d 项\n",
		len(c.StimulusSources), real, len(c.Monsters), len(c.Sanity))
	for _, k := range []string{"voice_whisper", "voice_normal", "voice_shout", "run_footstep", "crouch_footstep"} {
		var s src
		if err := json.Unmarshal(c.StimulusSources[k], &s); err != nil {
			continue
		}
		r := "全图"
		if s.RadiusM != nil {
			r = fmt.Sprintf("%.0f 米", *s.RadiusM)
		}
		fmt.Printf("  %-10s 强度 %3d 半径 %s\n", s.Label, s.Intensity, r)
	}
}
