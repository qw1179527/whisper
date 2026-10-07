extends Node3D
# 纯 GDScript 探针：**不依赖 C# 程序集**。
# 目的：把"黑屏"拆成两种可能 ——
#   ① 渲染/管线层面根本没出画面（那这个 GDScript 探针也会黑）
#   ② C# 程序集没加载（那这个探针会正常显示，而 C# 的 MainProbe 不会）
# 判据写在屏幕上 + 同时写文件到应用自己的外部目录（那里有写权限，且我能读）。

func _ready() -> void:
	var lines := PackedStringArray()
	lines.append("Godot GDScript 探针（不依赖 C#）")
	lines.append("引擎 %s" % Engine.get_version_info()["string"])
	var vp := get_viewport()
	lines.append("视口 %dx%d" % [vp.get_visible_rect().size.x, vp.get_visible_rect().size.y])
	lines.append("C# 可用: %s" % str(ClassDB.class_exists("GodotSharp")))

	# 场景内容：亮背景 + 灯 + 方块 + 相机（保证"一定有东西可看"）
	var env := Environment.new()
	env.background_mode = Environment.BGMode.COLOR
	env.background_color = Color(0.15, 0.25, 0.45)      # 明显的蓝底，黑屏一眼可辨
	env.ambient_light_source = Environment.AmbientSource.COLOR
	env.ambient_light_color = Color(1, 1, 1)
	env.ambient_light_energy = 1.0
	var we := WorldEnvironment.new(); we.environment = env; add_child(we)

	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-55, -35, 0)
	add_child(sun)

	var box := MeshInstance3D.new()
	box.mesh = BoxMesh.new()
	box.position = Vector3(0, 0, -4)
	add_child(box)

	var cam := Camera3D.new()
	cam.position = Vector3(0, 1, 2)
	cam.current = true
	add_child(cam)

	# 文字标签（大号，截图可直接读）
	var label := Label.new()
	label.text = "\n".join(lines)
	label.position = Vector2(24, 24)
	label.add_theme_font_size_override("font_size", 34)
	label.add_theme_color_override("font_color", Color(1, 1, 1))
	add_child(label)

	# 写文件到**应用自己的外部目录**（有权限，且我能读）
	lines.append("写文件…")
	var dir := "user://"           # 保底：应用私有目录
	var out := ""
	for cand in ["/storage/emulated/0/Android/data/com.whisper.projectwhisper/files",
				 "/sdcard/Android/data/com.whisper.projectwhisper/files"]:
		if DirAccess.dir_exists_absolute(cand):
			out = cand + "/probe-report.txt"
			break
	if out == "":
		out = ProjectSettings.globalize_path("user://probe-report.txt")
	var f := FileAccess.open(out, FileAccess.WRITE)
	if f:
		f.store_string("\n".join(lines))
		f.close()
		print("[probe] 已写 ", out)
	else:
		print("[probe] 写失败 ", out)
	for l in lines:
		print("[probe] ", l)

	# 等若干帧后读回帧缓冲（判"到底有没有出像素"）
	await get_tree().create_timer(1.5).timeout
	var img := get_viewport().get_texture().get_image()
	if img == null:
		print("[probe] 帧缓冲读回失败")
		return
	var n := 0; var sum := 0.0
	for y in range(0, img.get_height(), 8):
		for x in range(0, img.get_width(), 8):
			var c := img.get_pixel(x, y)
			sum += 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b
			n += 1
	var mean := sum / max(n, 1)
	print("[probe] 帧缓冲 %dx%d 平均亮度 %.1f" % [img.get_width(), img.get_height(), mean])
	var png := out.get_basename() + "-frame.png"
	img.save_png(png)
	print("[probe] 帧已存 ", png)
