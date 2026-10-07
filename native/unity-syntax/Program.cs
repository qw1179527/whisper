// unity-syntax — 对引用 UnityEngine 的源文件做**语法与语义检查**（本机无 Unity 时的替代手段）
//
// 原理：用 Roslyn 解析 + 编译，允许"Unity 类型未解析"这一类错误（CS0246/CS0234/CS0103/CS1061… 中
// 由缺失 Unity 程序集引起的），但**其它任何错误都必须失败**。
// 这样既能在本机拦住真实笔误（如引用不存在的 DesignTokens.ColorConcrete、调用不存在的方法），
// 又不必伪造一整套 UnityEngine 存根。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// 用法：unity-syntax <要检查的 Unity 依赖文件...> --refs <全部非 Unity 源文件...>
// 为什么必须把非 Unity 源一起编译：只喂 Unity 文件时，项目内类型（如 DesignTokens）解析不了，
// 笔误会被降级成"类型缺失"从而漏过（我实测过：DesignTokens.ColorConcrete 竟被判通过）。
var targets = new List<string>();
var refFiles = new List<string>();
bool afterRefs = false;
foreach (var a in args)
{
    if (a == "--refs") { afterRefs = true; continue; }
    if (!a.EndsWith(".cs")) continue;
    (afterRefs ? refFiles : targets).Add(a);
}
if (targets.Count == 0) { Console.Error.WriteLine("用法: unity-syntax <file.cs>... [--refs <non-unity.cs>...]"); return 2; }

string Normalize(string p)
{
    if (string.IsNullOrEmpty(p)) return null;
    try { return Path.GetFullPath(p); } catch { return p; }
}

var targetFiles = new HashSet<string>(targets.Select(Normalize));
var files = targets.Concat(refFiles).ToArray();
var trees = files.Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), path: f)).ToArray();

// 语法错误：一律失败
int syntaxErrors = 0;
foreach (var t in trees)
    foreach (var d in t.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
    {
        Console.WriteLine($"  ✗ 语法错误 {Path.GetFileName(t.FilePath)}({d.Location.GetLineSpan().StartLinePosition.Line + 1}): {d.GetMessage()}");
        syntaxErrors++;
    }
if (syntaxErrors > 0) { Console.WriteLine($"语法错误 {syntaxErrors} 个 ✗"); return 1; }

// 语义：允许 Unity 缺失引起的错误；其余失败
var allowed = new HashSet<string> {
    "CS0246", // 找不到类型或命名空间（UnityEngine 未引用）
    "CS0234", // 命名空间中不存在该类型（UnityEngine.X）
    "CS0103", // 当前上下文中不存在名称（Unity 静态成员）
    "CS1061", // 类型不含该成员（Unity 类型上的成员）
    "CS0117", // 类型不含该定义（枚举值等）
    "CS0122", // 保护级别（Unity 内部 API）
    "CS0400", // 全局命名空间中找不到类型
    "CS0616", // 不是特性类（Unity 特性）
    "CS1729", // 不含接受该参数的构造函数（Unity 类型）
    "CS0029", "CS1503", "CS0019", "CS0021", "CS1501", // 由 Unity 类型缺失级联出的转换/重载错误
    "CS0121", "CS0411", "CS0305", "CS0308",
};
// 必须给**完整框架引用集**：只给 System.Private.CoreLib 时连 string/Math 都解析不了，
// 于是满屏"类型缺失"把真实笔误淹没（我实测：DesignTokens.ColorConcrete 被漏过）。
var refs = Basic.Reference.Assemblies.Net80.References.All;
var comp = CSharpCompilation.Create("unity-syntax-check", trees, refs,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

var errors = comp.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

// 精确规则：**项目代码（Whisper.*）上的成员/类型错误一律失败**，无论错误码是否在允许表里。
// 这条是踩出来的：CS0117「DesignTokens does not contain a definition for 'ColorConcrete'」
// 曾被"允许表"整体放过——允许表按错误码一刀切，会把项目自身的笔误一并放行。
// 判据用诊断消息里是否出现本项目命名空间前缀，比按错误码猜可靠。
// ── 项目源码里声明过的全部类型名（供 CS0103 判据用；见 TouchesProjectCode 的注释）──
var projectTypeNames = new HashSet<string>(StringComparer.Ordinal);
foreach (var tree in trees)   // trees: 本工程的全部语法树（目标 + 上下文）
{
    var root = tree.GetRoot();
    foreach (var node in root.DescendantNodes())
    {
        if (node is Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax bt)
            projectTypeNames.Add(bt.Identifier.ValueText);
        else if (node is Microsoft.CodeAnalysis.CSharp.Syntax.EnumDeclarationSyntax en)
            projectTypeNames.Add(en.Identifier.ValueText);
    }
}
Console.WriteLine($"[unity-syntax] 项目声明类型名 {projectTypeNames.Count} 个（CS0103 判据用）");

bool TouchesProjectCode(Diagnostic d)
{
    // 判据（踩了三次才定下来，越简单越可靠）：
    //   错误位置落在**目标文件**内，且错误码属于"成员/重载/类型不存在"这一类 → 项目代码错误。
    //   为什么不用"消息里是否含 Whisper."：诊断消息里的类型名常常**未限定**（'DesignTokens' ...），会漏。
    //   为什么不用语义爬升：标识符节点上取到的往往是外层方法符号，拿不到被引用的类型。
    //   为什么不用文件路径含 /unity/Assets/：调用方可能传相对路径（实测踩过）。
    var file = Normalize(d.Location.SourceTree?.FilePath);
    if (Environment.GetEnvironmentVariable("UNITYSYNTAX_DEBUG2") == "1" && (d.Id == "CS0117" || d.Id == "CS1061"))
        Console.WriteLine($"        [dbg2] {d.Id} file={file} inTargets={targetFiles.Contains(file)} targets={string.Join("|", targetFiles)}");
    if (file == null || !targetFiles.Contains(file)) return false;
    // 【假绿修复 · 真实事故】CS0103（名称不存在）本不在下面那张"项目错误码"表里，
    // 因为 Unity 全局静态成员（DestroyImmediate/Shader/Color/Input…）也报 CS0103。
    // 但代价是：**同一个文件里的作用域错误也被放过了** —— 实测踩到：
    //   GameBootstrap 把 spawnX/spawnZ 声明在 ④ 的 try 块内、⑤ 处引用，
    //   编译器报 CS0103，门禁却列为"[允许] Unity 缺失"并通过。
    // 现在加一条针对性判据：**若缺失的名字在本文件里被声明过**（局部变量/参数/out 参数），
    // 那它就是作用域错误而不是 Unity 缺失 —— Unity 的静态成员不会在该文件里被声明。
    if (d.Id == "CS0103")
    {
        var m = System.Text.RegularExpressions.Regex.Match(d.GetMessage(), @"The name '([^']+)' does not exist");
        if (!m.Success) return false;
        var name = System.Text.RegularExpressions.Regex.Escape(m.Groups[1].Value);
        var text = d.Location.SourceTree is null ? null : File.ReadAllText(d.Location.SourceTree.FilePath);
        if (text is null) return false;
        var declaredInFile =
            System.Text.RegularExpressions.Regex.IsMatch(text, $@"\bvar\s+{name}\b")
            || System.Text.RegularExpressions.Regex.IsMatch(text, $@"\b(?:out|ref)\s+{name}\b")
            || System.Text.RegularExpressions.Regex.IsMatch(text, $@"\b[A-Za-z_]\w*(?:<[^>]*>)?(?:\[\])?\s+{name}\s*[=;,)]")
            || System.Text.RegularExpressions.Regex.IsMatch(text, $@"\b{name}\s*=[^=]");
        if (declaredInFile) return true;   // 本文件声明过却"不存在" → 作用域/拼写错误，真错误

        // ══════════════════════════════════════════════════════════════════════════════
        // 【假绿修复 · 2026-10-07 真实构建事故】**本项目其它文件声明过的名字**也判红
        // ══════════════════════════════════════════════════════════════════════════════
        // 事故原文（两个出包工作流同时失败）：
        //   Assets/Scripts/Runtime/GameBootstrap.Hud.cs(76,17): error CS0103: The name 'Services' does not exist
        //   Assets/Scripts/Runtime/GameBootstrap.Hud.cs(81,17): error CS0103: The name 'LevelBuilder' does not exist
        // 原因：我把 `Update()` 拆进新 partial 文件时**漏搬 using**。
        // 而本门禁把 CS0103 当"Unity 缺失"放行（判据只看"本文件是否声明过该名字"），
        // `Services` / `LevelBuilder` 都是**别处声明的类型** ⇒ 漏判 ⇒ 本机 21 步全绿而真实构建失败。
        //
        // 新判据：**若缺失的名字出现在"本项目全部源文件里声明过的类型名"集合中**，
        //   那它显然是本项目的东西（Unity 的静态成员不会在我们的源码里被声明）⇒ 判红。
        // 这与上面那条"本文件声明过"是同一思路，只是把范围从**一个文件**扩到**整个项目** ——
        // 而那正是 partial 拆分 / 跨文件引用的失效模式。
        if (projectTypeNames.Contains(m.Groups[1].Value)) return true;

        return false;                      // 项目里也没有 → 大概率是 Unity 静态成员
    }
    // 只保留**明确指向某个被引用符号**的错误码。
    return d.Id == "CS0117" ||   // 类型不含该成员（项目类型笔误的主信号）
           d.Id == "CS1061" ||   // 类型不含该成员（实例调用）
           d.Id == "CS1501" ||   // 方法无此重载
           d.Id == "CS1729" ||   // 无此构造函数
           d.Id == "CS0122" ||   // 保护级别不可访问
           d.Id == "CS0111" ||   // 重复成员定义
           d.Id == "CS7036";     // 缺少必需参数
}


var projectErrors = errors.Where(TouchesProjectCode).ToList();
var real = errors.Where(e => !allowed.Contains(e.Id)).Concat(projectErrors).Distinct().ToList();
var unity = errors.Where(e => allowed.Contains(e.Id) && !TouchesProjectCode(e)).ToList();

Console.WriteLine($"[unity-syntax] 检查 {targets.Count} 个目标文件（附 {refFiles.Count} 个非 Unity 源作为类型上下文）");
Console.WriteLine($"  Unity 缺失引起的错误（允许）: {unity.Count} 条");
// 全量输出（原先 Take(6)/Take(12) 只给样本，排查桩缺口时必须看全）
foreach (var e in unity) Console.WriteLine($"      [允许] {e.Id} {Path.GetFileName(e.Location.SourceTree?.FilePath)}({e.Location.GetLineSpan().StartLinePosition.Line + 1}): {e.GetMessage()}");
if (real.Count > 0)
{
    Console.WriteLine($"  ✗ 真实错误 {real.Count} 条（与 Unity 缺失无关，必须修）:");
    foreach (var e in real)
        Console.WriteLine($"      {e.Id} {Path.GetFileName(e.Location.SourceTree?.FilePath)}({e.Location.GetLineSpan().StartLinePosition.Line + 1}): {e.GetMessage()}");
    return 1;
}
Console.WriteLine("  ✓ 无与 Unity 缺失无关的真实错误（语法 + 语义通过）");
return 0;
