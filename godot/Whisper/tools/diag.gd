extends SceneTree
# 诊断：把 Godot 看到的 dotnet 项目设置打出来（GDScript 不依赖程序集，能绕过加载问题）
func _initialize() -> void:
	print("=== Godot 的 dotnet 项目设置 ===")
	for k in ["dotnet/project/assembly_name", "dotnet/project/solution_directory",
			  "dotnet/project/place_solution_and_project_in_same_directory"]:
		if ProjectSettings.has_setting(k):
			print("  %s = %s" % [k, str(ProjectSettings.get_setting(k))])
		else:
			print("  %s = (未设置)" % k)
	print("res:// 实际路径: ", ProjectSettings.globalize_path("res://"))
	for f in ["res://Whisper.csproj", "res://Whisper.sln"]:
		print("  %s 存在: %s" % [f, str(FileAccess.file_exists(f))])
	quit()
