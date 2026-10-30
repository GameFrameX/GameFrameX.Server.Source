#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
本地化资源一致性门禁（GFX-1156 / C426）。

默认只校验、非零退出；--fix 显式放行缺失键补写（[TODO]/[待翻译] 占位，
补写后仍需 auto_translate.py 或人工翻译，占位值存在时门禁保持非零）。

校验项：
  1. Keys.*.cs 常量键名合法性（XML 属性安全字符集），非法键名退出码 2；
  2. resx 重复 <data> 检出，退出码 2；
  3. 缺失键（C# 常量未在 resx 中定义）默认非零退出，--fix 时补写（quoteattr 转义）；
  4. [TODO]/[待翻译] 占位值存在时非零退出；
  5. EN 与 zh-CN 键集合一致（双语同步）；
  6. EN 与 zh-CN 占位符集合一致（sorted {n} 列表比对）；
  7. 调用点静态扫描：LocalizationService.GetString 实参个数必须 ∈ {EN, zh 模板所需实参数}
     （max 占位符下标 + 1 口径）；string.Format(GetString(key), ...) 消费与
     var 模板 + string.Format(var, ...) 模式豁免；动态键表达式无法静态解析时仅告警。

退出码：0 全部通过；1 一致性/缺失/占位/调用点违规；2 结构性错误（非法键名/重复键/XML）。
"""

import argparse
import re
import subprocess  # noqa: F401  (保留给未来 CI 集成扩展)
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

SCRIPT_DIR = Path(__file__).parent

# 键名合法字符集：XML 属性安全（不含 & < > " ' 与空白）
KEY_NAME_PATTERN = re.compile(r"[A-Za-z0-9_.\-]+")
# 占位符 {0} / {1} ...
PLACEHOLDER_PATTERN = re.compile(r"\{\d+\}")
# 待翻译占位标记
TODO_MARKERS = ("[TODO]", "[待翻译]")

# C# 常量键定义: public const string Name = "Key.Value";
CONST_PATTERN = re.compile(r'public\s+const\s+string\s+(\w+)\s*=\s*"((?:[^"\\]|\\.)*)"')
CLASS_PATTERN = re.compile(r"\bclass\s+(\w+)")


class GateFailure(Exception):
    """退出码 1：一致性/缺失/占位/调用点违规。"""

    def __init__(self, message):
        super().__init__(message)
        self.exit_code = 1


class StructureFailure(Exception):
    """退出码 2：结构性错误（非法键名/重复键/XML）。"""

    def __init__(self, message):
        super().__init__(message)
        self.exit_code = 2


def _parse_keys_cs_file(cs_file, keys, key_paths):
    """逐行解析单个 Keys.*.cs：收集常量键值，并记录 类嵌套全路径 → 键值（供调用点扫描解析用）。"""
    stack = []
    for line in cs_file.read_text(encoding="utf-8").splitlines():
        stripped = line.strip()
        m = CONST_PATTERN.search(stripped)
        if m and stack:
            raw = m.group(2)
            value = raw.encode().decode("unicode_escape") if "\\" in raw else raw
            keys.add(value)
            key_paths.append((".".join(stack + [m.group(1)]), value))
            continue
        m = CLASS_PATTERN.search(stripped)
        if m:
            stack.append(m.group(1))
            continue
        if stripped == "}" and stack:
            stack.pop()


def _validate_key_names(keys):
    """键名合法性：任何含 XML 特殊字符/空白的键名直接结构失败（--fix 也不得写入）。"""
    for key in sorted(keys):
        if not KEY_NAME_PATTERN.fullmatch(key):
            raise StructureFailure(f"非法资源键名（含 XML 特殊字符或空白）: {key!r}，请修正 Keys.*.cs 中的常量值")


def extract_keys_from_cs_files(keys_dir):
    """从所有 Keys.*.cs 文件中提取 key 定义；同时返回 类嵌套全路径 → 键值 映射（供调用点扫描解析用）。"""
    keys = set()
    key_paths = []  # [(全路径, 键值)]

    cs_files = sorted(keys_dir.glob("Keys.*.cs"))
    keys_cs = keys_dir / "Keys.cs"
    if keys_cs.exists():
        cs_files.append(keys_cs)

    for cs_file in cs_files:
        _parse_keys_cs_file(cs_file, keys, key_paths)

    _validate_key_names(keys)
    return keys, key_paths


def parse_resx(resx_path):
    """解析 resx：返回 键 → 值 字典；重复 <data> 与 XML 解析失败按结构失败处理。"""
    values = {}
    if not resx_path.exists():
        raise StructureFailure(f"resx 文件不存在: {resx_path}")
    try:
        root = ET.parse(resx_path).getroot()
    except ET.ParseError as e:
        raise StructureFailure(f"resx XML 解析失败: {resx_path}: {e}")

    for data in root.findall("data"):
        name = data.get("name")
        if not name:
            continue
        if name in values:
            raise StructureFailure(f"resx 存在重复 <data> 键: {name}（{resx_path.name}）")
        value_elem = data.find("value")
        values[name] = value_elem.text if value_elem is not None and value_elem.text else ""
    return values


def add_keys_to_resx(resx_path, missing_keys, is_chinese=False):
    """将缺失的 key 添加到 resx 文件中（键名已通过合法性校验；quoteattr 兜底转义）。"""
    from xml.sax.saxutils import quoteattr

    if not missing_keys:
        return 0

    if not resx_path.exists():
        print(f"  文件不存在: {resx_path}")
        return 0

    with open(resx_path, "r", encoding="utf-8") as f:
        content = f.read()

    lines = content.split("\n")
    root_end_index = -1
    for i, line in enumerate(lines):
        if line.strip() == "</root>":
            root_end_index = i
            break

    if root_end_index == -1:
        print("  无法找到 </root> 标签")
        return 0

    new_lines = []
    for key in sorted(missing_keys):
        if not KEY_NAME_PATTERN.fullmatch(key):
            print(f"  跳过非法键名（不写入）: {key!r}", file=sys.stderr)
            continue
        value = f"[待翻译] {key}" if is_chinese else f"[TODO] {key}"
        new_lines.append(f"  <data name={quoteattr(key)} xml:space=\"preserve\">")
        new_lines.append(f"    <value>{value}</value>")
        new_lines.append("  </data>")
        new_lines.append("")

    lines = lines[:root_end_index] + new_lines + lines[root_end_index:]

    with open(resx_path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))

    return len(new_lines) // 4


def print_key_list(keys, limit=10):
    """打印 key 列表，最多显示 limit 个，超出部分汇总提示"""
    sorted_keys = sorted(keys)
    for key in sorted_keys[:limit]:
        print(f"     - {key}")
    remaining = len(sorted_keys) - limit
    if remaining > 0:
        print(f"     ... 还有 {remaining} 个")


def placeholder_counts(value):
    """模板占位符的 sorted 列表（用于双语一致性比对）。"""
    return sorted(PLACEHOLDER_PATTERN.findall(value))


def placeholder_arg_count(value):
    """模板所需实参个数 = 最大占位符下标 + 1（{0} 复用只算一个参数）。"""
    indexes = [int(m.strip("{}")) for m in PLACEHOLDER_PATTERN.findall(value)]
    return (max(indexes) + 1) if indexes else 0


def check_placeholder_consistency(en_values, zh_values):
    """EN/zh 交集键占位符集合一致性 + 键集合一致性。"""
    problems = []
    en_keys, zh_keys = set(en_values), set(zh_values)
    only_en = en_keys - zh_keys
    only_zh = zh_keys - en_keys
    if only_en:
        problems.append(f"键集合不一致：default 有而 zh-CN 没有 {len(only_en)} 键:")
        print_key_list(only_en)
    if only_zh:
        problems.append(f"键集合不一致：zh-CN 有而 default 没有 {len(only_zh)} 键:")
        print_key_list(only_zh)
    mismatches = []
    for key in sorted(en_keys & zh_keys):
        if placeholder_counts(en_values[key]) != placeholder_counts(zh_values[key]):
            mismatches.append(
                f"     - {key}\n       default: {en_values[key]!r}\n       zh-CN:   {zh_values[key]!r}"
            )
    if mismatches:
        problems.append(f"占位符不一致 {len(mismatches)} 键:")
        print("\n".join(mismatches))
    return problems


# ---------------------------------------------------------------------------
# 调用点静态扫描
# ---------------------------------------------------------------------------

def _skip_string_literal(text, i):
    """i 位于引号开头，返回字符串字面量结束后下标。"""
    quote = text[i]
    i += 1
    while i < len(text):
        c = text[i]
        if c == "\\":
            i += 2
            continue
        if c == quote:
            if quote == '"' and i + 1 < len(text) and text[i + 1] == '"':
                i += 2
                continue
            return i + 1
        i += 1
    return i


def _skip_char_literal(text, i):
    """i 位于单引号开头，跳过字符字面量，返回结束后下标。"""
    j = i + 1
    n = len(text)
    while j < n:
        if text[j] == "\\":
            j += 2
            continue
        if text[j] == "'":
            break
        j += 1
    return j + 1


def _blank_span(out, start, end):
    """把 [start, end) 抹成空格（保留换行以维持行号）。"""
    for k in range(start, end):
        if out[k] != "\n":
            out[k] = " "


def _skip_comment_at(text, i):
    """text[i] 位于 '/'：若为注释开头返回跳过后下标，否则返回 None。"""
    nxt = text[i + 1] if i + 1 < len(text) else ""
    if nxt == "/":
        j = text.find("\n", i)
        return len(text) if j == -1 else j
    if nxt == "*":
        j = text.find("*/", i)
        return len(text) if j == -1 else j + 2
    return None


def _strip_comments(text):
    """字符串感知地抹掉注释内容（保持长度与行号不变）。"""
    out = list(text)
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if c == '"':
            i = _skip_string_literal(text, i)
            continue
        if c == "'":
            i = _skip_char_literal(text, i)
            continue
        if c == "/":
            j = _skip_comment_at(text, i)
            if j is not None:
                _blank_span(out, i, j)
                i = j
                continue
        i += 1
    return "".join(out)


def _read_balanced(text, start):
    """start 位于 '('，返回 (括号内内容, 结束后下标)；感知嵌套括号/字符串/注释。"""
    assert text[start] == "("
    depth = 0
    i = start
    while i < len(text):
        c = text[i]
        if c in "\"'":
            i = _skip_string_literal(text, i)
            continue
        if c == "/":
            j = _skip_comment_at(text, i)
            if j is not None:
                i = j
                continue
        if c == "(":
            depth += 1
        elif c == ")":
            depth -= 1
            if depth == 0:
                return text[start + 1:i], i + 1
        i += 1
    return text[start + 1:], len(text)


def _split_top_level(argtext):
    """顶层逗号切分（忽略嵌套括号/方括号/字符串）。"""
    parts = []
    depth = 0
    current = []
    i = 0
    while i < len(argtext):
        c = argtext[i]
        if c in "\"'":
            j = _skip_string_literal(argtext, i)
            current.append(argtext[i:j])
            i = j
            continue
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
        elif c == "," and depth == 0:
            parts.append("".join(current).strip())
            current = []
            i += 1
            continue
        current.append(c)
        i += 1
    last = "".join(current).strip()
    if last or parts:
        parts.append(last)
    return parts


class CallsiteScanner:
    """LocalizationService.GetString 调用点扫描器。

    键解析链：前缀剥离 → 全路径精确 → 键值精确 → 路径/键值后缀 → 末段 const 名 + 至少一段共有（模块类名 ≠ 键值前缀时兜底）。
    豁免：string.Format(GetString(key), ...) 按 string.Format 实参计；var 模板 + string.Format(var, ...) 模式豁免。
    """

    def __init__(self, key_paths, en_values, zh_values, repo_root, localization_dir):
        self.key_paths = key_paths
        self.en_values = en_values
        self.zh_values = zh_values
        self.repo_root = repo_root
        self.localization_dir = localization_dir.resolve()
        self.path_by_suffix = {}
        for path, value in key_paths:
            self.path_by_suffix.setdefault(path, value)
        self.value_set = {value for _, value in key_paths}

    def resolve_key(self, expr):
        expr = expr.strip()
        expr = re.sub(r"^GameFrameX\.Localization\.", "", expr)
        expr = re.sub(r"^Localization\.Keys\.", "", expr)
        expr = re.sub(r"^LocalizationKeys\.", "", expr)
        expr = re.sub(r"^Localization\.", "", expr)
        expr = re.sub(r"^Keys\.", "", expr)
        if expr in self.path_by_suffix:
            return self.path_by_suffix[expr]
        if expr in self.value_set:
            return expr
        candidates = set(v for p, v in self.key_paths if p.endswith("." + expr) or p == expr)
        candidates |= set(v for _, v in self.key_paths if v.endswith("." + expr))
        if len(candidates) == 1:
            return candidates.pop()
        segments = expr.split(".")
        last = segments[-1]
        rest = set(segments[:-1])
        fuzzy = set()
        for p, v in self.key_paths:
            ps = p.split(".")
            if ps[-1] == last and (set(ps[:-1]) & rest):
                fuzzy.add(v)
        if len(fuzzy) == 1:
            return fuzzy.pop()
        return None

    def _iter_callsite_files(self):
        """仓库内待扫描 .cs 文件（排除 obj/bin 与本地化目录自身）。"""
        return [
            p for p in self.repo_root.rglob("*.cs")
            if "/obj/" not in str(p) and "/bin/" not in str(p)
            and not p.parent.resolve().is_relative_to(self.localization_dir)
        ]

    def _format_wrapped_arg_count(self, text, m):
        """string.Format 包裹判定：回溯本次调用自身的限定符链后若位于某调用的首参位置，
        且外层 callee 为 string.Format，则返回 有效实参 = string.Format 总实参 - 1（格式串本身）；否则 None。"""
        p = m.start() - 1
        while p >= 0 and text[p] in " \t\r\n":
            p -= 1
        while p >= 0 and (text[p].isalnum() or text[p] in "._<>"):
            p -= 1
        if p < 0 or text[p] != "(":
            return None
        q = p - 1
        while q >= 0 and (text[q].isalnum() or text[q] in "._<>"):
            q -= 1
        callee = text[q + 1:p]
        if not (callee.endswith("string.Format") or callee == "Format"):
            return None
        format_inner, _ = _read_balanced(text, p)
        return len(_split_top_level(format_inner)) - 1

    def _is_template_variable(self, text, m):
        """豁免：赋值给局部变量后由 string.Format(var, ...) 消费（模板变量模式）。"""
        stmt_prefix = text[max(0, m.start() - 200):m.start()]
        var_match = re.search(r"(?:var|let)\s+(\w+)\s*=\s*$", stmt_prefix)
        return bool(var_match and re.search(
            r"string\.Format\s*\(\s*" + re.escape(var_match.group(1)) + r"\s*,", text
        ))

    def _check_callsite(self, text, m, path, violations, unresolved):
        """处理单个 LocalizationService.GetString 调用点。"""
        inner, _ = _read_balanced(text, m.end() - 1)
        args = _split_top_level(inner)
        if not args:
            return
        key = self.resolve_key(args[0])
        if key is None:
            unresolved.append((str(path.relative_to(self.repo_root)), args[0][:120]))
            return
        if key not in self.en_values or key not in self.zh_values:
            return
        en_args = placeholder_arg_count(self.en_values[key])
        zh_args = placeholder_arg_count(self.zh_values[key])
        arg_count = self._format_wrapped_arg_count(text, m)
        if arg_count is None:
            arg_count = len(args) - 1
        if arg_count in (en_args, zh_args):
            return
        if self._is_template_variable(text, m):
            return
        line = text.count("\n", 0, m.start()) + 1
        violations.append(
            f"     - {path.relative_to(self.repo_root)}:{line}  {key}  "
            f"实参={arg_count}  default 占位参数={en_args}  zh-CN 占位参数={zh_args}"
        )

    def scan(self):
        """返回 (违规列表, 未解析表达式列表)。"""
        violations = []
        unresolved = []
        total = 0
        for path in sorted(self._iter_callsite_files()):
            raw = path.read_text(encoding="utf-8", errors="replace")
            text = _strip_comments(raw)
            for m in re.finditer(r"LocalizationService\.GetString\s*\(", text):
                total += 1
                self._check_callsite(text, m, path, violations, unresolved)
        print(f"   调用点扫描: {total} 处 LocalizationService.GetString，"
              f"解析 {total - len(unresolved)}，无法静态解析 {len(unresolved)}（仅告警）")
        return violations, unresolved


def _check_language(lang, resx_path, cs_keys, fix):
    """检查单语言 resx：返回 (values, failure_messages)；--fix 时补写后重解析。"""
    messages = []
    print()
    print(f"2. 检查 {lang} 资源文件: {resx_path.name}")
    values = parse_resx(resx_path)
    print(f"   资源文件中有 {len(values)} 个 key")

    missing_keys = cs_keys - set(values)
    extra_keys = set(values) - cs_keys

    if missing_keys:
        print(f"   缺失 {len(missing_keys)} 个 key:")
        print_key_list(missing_keys)
        if fix:
            added = add_keys_to_resx(resx_path, missing_keys, lang == "zh-CN")
            print(f"   已补写 {added} 个缺失的 key 到 {resx_path.name}（占位值，需翻译）")
            values = parse_resx(resx_path)
        else:
            messages.append(f"{lang} 缺失 {len(missing_keys)} 个 key（--fix 显式补写）")
    else:
        print("   所有 key 都存在，无需补充")

    if extra_keys:
        print(f"   资源文件中有 {len(extra_keys)} 个多余的 key (在 C# 中未定义，仅提示)")
        print_key_list(extra_keys)

    todo_keys = [k for k, v in values.items() if any(marker in v for marker in TODO_MARKERS)]
    if todo_keys:
        print(f"   存在 {len(todo_keys)} 个待翻译占位值 ([TODO]/[待翻译]):")
        print_key_list(todo_keys)
        messages.append(f"{lang} 存在 {len(todo_keys)} 个待翻译占位值（运行 auto_translate.py 或人工翻译）")

    return values, messages


def main():
    parser = argparse.ArgumentParser(description="本地化资源一致性门禁")
    parser.add_argument("--fix", action="store_true", help="显式放行缺失键补写（[TODO]/[待翻译] 占位）")
    parser.add_argument("--localization-dir", type=Path, default=SCRIPT_DIR,
                        help="GameFrameX.Localization 目录（默认：脚本所在目录）")
    parser.add_argument("--repo-root", type=Path, default=None,
                        help="调用点扫描的仓库根（默认：localization-dir 的上级）")
    parser.add_argument("--skip-callsite-scan", action="store_true", help="跳过调用点静态扫描")
    args = parser.parse_args()

    localization_dir = args.localization_dir
    repo_root = args.repo_root if args.repo_root else localization_dir.parent
    resx_dir = localization_dir / "Localization" / "Messages"
    resx_files = {
        "default": resx_dir / "Resources.resx",
        "zh-CN": resx_dir / "Resources.zh-CN.resx",
    }

    print("=" * 60)
    print("Keys 检查脚本 - 本地化资源一致性门禁")
    print("=" * 60)
    print()

    failure_messages = []

    print("1. 从 Keys.*.cs 文件中提取 key...")
    try:
        cs_keys, key_paths = extract_keys_from_cs_files(localization_dir)
    except StructureFailure as e:
        print(f"[结构错误] {e}", file=sys.stderr)
        sys.exit(2)
    print(f"   找到 {len(cs_keys)} 个 key 定义")

    resx_values = {}
    for lang, resx_path in resx_files.items():
        try:
            values, messages = _check_language(lang, resx_path, cs_keys, args.fix)
        except StructureFailure as e:
            print(f"[结构错误] {e}", file=sys.stderr)
            sys.exit(2)
        resx_values[lang] = values
        failure_messages.extend(messages)

    print()
    print("3. EN/zh-CN 占位符与键集合一致性...")
    placeholder_problems = check_placeholder_consistency(resx_values["default"], resx_values["zh-CN"])
    if placeholder_problems:
        for problem in placeholder_problems:
            print(f"   [不一致] {problem}")
        failure_messages.extend(placeholder_problems)
    else:
        print("   交集键占位符一致，键集合一致")

    if not args.skip_callsite_scan:
        print()
        print("4. 调用点静态扫描（实参个数 vs 模板占位符）...")
        scanner = CallsiteScanner(key_paths, resx_values["default"], resx_values["zh-CN"], repo_root, localization_dir)
        violations, unresolved = scanner.scan()
        for item in unresolved:
            print(f"   [告警] 无法静态解析键表达式: {item[0]}  {item[1]}")
        if violations:
            print(f"   调用点违规 {len(violations)} 处:")
            print("\n".join(violations))
            failure_messages.append(f"调用点实参与模板占位符不匹配 {len(violations)} 处")
        else:
            print("   调用点实参全部匹配")

    print()
    print("=" * 60)
    if failure_messages:
        print("检查失败!")
        for message in failure_messages:
            print(f" - {message}")
        print("=" * 60)
        sys.exit(1)
    print("检查完成，全部通过!")
    print("=" * 60)
    sys.exit(0)


if __name__ == "__main__":
    main()
