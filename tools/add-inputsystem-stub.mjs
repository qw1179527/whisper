// 给 UnityStubs 追加 UnityEngine.InputSystem 命名空间（主界面按钮要同时读新旧两套输入）。
// 用追加而不是插入：桩文件已反复被我改坏，**只追加**是最安全的形态（不动既有内容）。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'native/unity-stubs/UnityStubs.cs');
let s = fs.readFileSync(P, 'utf8');

if (s.includes('namespace UnityEngine.InputSystem')) {
  console.log('  · 已存在，跳过');
} else {
  s = s.trimEnd() + `

namespace UnityEngine.InputSystem
{
    using UnityEngine;

    /// <summary>按键控件（出处 com.unity.inputsystem 手册 Controls）。主界面按钮同时读新旧两套输入 ——
    /// 本工程装了 Input System 包，而 activeInputHandler=0（旧系统），哪套在收事件只能实测。</summary>
    public class ButtonControl
    {
        public bool wasPressedThisFrame => false;
        public bool wasReleasedThisFrame => false;
        public bool isPressed => false;
    }

    /// <summary>二维轴控件（出处同上）。指针/触摸坐标。</summary>
    public class Vector2Control { public Vector2 ReadValue() => new Vector2(0f, 0f); }

    /// <summary>触摸点控件（press + position）。</summary>
    public class TouchControl
    {
        public ButtonControl press { get; set; }
        public Vector2Control position { get; set; }
    }

    /// <summary>触摸屏设备（出处 Touchscreen.current）。</summary>
    public class Touchscreen
    {
        public static Touchscreen current => null;
        public TouchControl primaryTouch { get; set; }
    }

    /// <summary>鼠标设备（出处 Mouse.current）。</summary>
    public class Mouse
    {
        public static Mouse current => null;
        public ButtonControl leftButton { get; set; }
        public Vector2Control position { get; set; }
    }
}
`;
  fs.writeFileSync(P, s, 'utf8');
  console.log('  ✓ 已追加 UnityEngine.InputSystem');
}
const open = (s.match(/\{/g) || []).length, close = (s.match(/\}/g) || []).length;
console.log(`  · 行数 ${s.split('\n').length} · 花括号 { ${open} / } ${close} ${open === close ? '平衡 OK' : '不平衡 BAD'}`);
