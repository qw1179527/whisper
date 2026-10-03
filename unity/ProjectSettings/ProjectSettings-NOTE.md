# ProjectSettings 说明（本机无法生成完整设置文件）

Unity 的 `ProjectSettings/*.asset` 是二进制/YAML 资产，**必须由 Unity 编辑器生成**。
本机（Android/Termux 环境）没有 Unity，因此这里只放了 `ProjectVersion.txt`（声明目标版本），
其余设置文件请在有 Unity 的机器上首次打开工程时自动生成，或由 CI 首次构建时补齐。

关键设置（首次打开后需确认，或在 CI 前用 `-executeMethod` 脚本设置）：
- **Player Settings**：包名 `com.whisper.projectwhisper`、targetSdk 36（V9 §12）、ARM64、IL2CPP
- **Company/Product Name**：Whisper / Project Whisper（低语计划）
- **Graphics**：URP（V9 §12 技术栈）
- **Scripting Backend**：IL2CPP（V9 §13.8 包体与性能要求）
- **Managed Stripping Level**：Medium（配合 link.xml 保护三接口实现）
- **Boot 场景**：由 `Whisper/生成 Boot 场景` 菜单或 `EditorSceneBootstrap.EnsureBootScene()` 生成

这些设置的"唯一真源"是 `docs/` 与 CI 脚本，不靠手工勾选 —— 符合 V9 §19「代码优先」。
