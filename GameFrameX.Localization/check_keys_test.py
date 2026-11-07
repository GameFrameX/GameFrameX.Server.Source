#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
check_keys.py 门禁自测（GFX-1156 / C426）。

用临时目录构造最小 fixture，以子进程方式断言门禁行为：
  1. 占位符不一致（{0} vs {0},{1}）→ 退出码 1；
  2. 非法键名（a"b / x&y）→ 退出码 2 且 --fix 下 resx 字节不变；
  3. resx 重复 <data> → 退出码 2；
  4. 缺键默认退出 1，--fix 补写后（含 [TODO] 占位）仍退出 1；
  5. 调用点实参不匹配 → 退出码 1，匹配后退出 0；
  6. 干净 fixture → 退出码 0。

运行：python3 GameFrameX.Localization/check_keys_test.py
"""

import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).parent / "check_keys.py"

KEYS_CS = """\
public static partial class Keys
{
    public static class Test
    {
        public const string Hello = "Test.Hello";
        public const string World = "Test.World";
    }
}
"""

RESX_EN = """\

  <data name="Test.Hello" xml:space="preserve">
    <value>Hello {0}.</value>
  </data>
  <data name="Test.World" xml:space="preserve">
    <value>World.</value>
  </data>
</root>
"""

RESX_ZH = """\

  <data name="Test.Hello" xml:space="preserve">
    <value>你好 {0}。</value>
  </data>
  <data name="Test.World" xml:space="preserve">
    <value>世界。</value>
  </data>
</root>
"""

RESX_HEADER = '<?xml version="1.0" encoding="utf-8"?>\n<root>\n'

CALLER_BAD = """\
using GameFrameX.Foundation.Localization.Core;

public static class Caller
{
    public static string Bad() => LocalizationService.GetString(Localization.Keys.Test.Hello);
}
"""

CALLER_GOOD = """\
using GameFrameX.Foundation.Localization.Core;

public static class Caller
{
    public static string Good() => LocalizationService.GetString(Localization.Keys.Test.Hello, "arg");
}
"""


def build_fixture(root, keys_cs=KEYS_CS, resx_en=RESX_EN, resx_zh=RESX_ZH, caller=None):
    loc = root / "loc"
    messages = loc / "Localization" / "Messages"
    messages.mkdir(parents=True)
    (loc / "Keys.Test.cs").write_text(keys_cs, encoding="utf-8")
    (messages / "Resources.resx").write_text(RESX_HEADER + resx_en, encoding="utf-8")
    (messages / "Resources.zh-CN.resx").write_text(RESX_HEADER + resx_zh, encoding="utf-8")
    if caller is not None:
        (root / "src").mkdir()
        (root / "src" / "Caller.cs").write_text(caller, encoding="utf-8")
    return loc


def run_gate(root, loc, *extra):
    return subprocess.run(
        [sys.executable, str(SCRIPT), "--localization-dir", str(loc), "--repo-root", str(root), *extra],
        capture_output=True,
        text=True,
    )


class PlaceholderConsistencyGateTest(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.mkdtemp(prefix="check_keys_test_")
        self.root = Path(self._tmp)

    def tearDown(self):
        shutil.rmtree(self._tmp, ignore_errors=True)

    def test_placeholder_mismatch_reports_error(self):
        zh_mismatch = RESX_ZH.replace("你好 {0}。", "你好 {0} {1}。")
        loc = build_fixture(self.root, resx_zh=zh_mismatch, caller=CALLER_GOOD)
        result = run_gate(self.root, loc)
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("占位符不一致", result.stdout)

    def test_key_set_mismatch_reports_error(self):
        zh_missing_world = RESX_ZH.replace(
            '  <data name="Test.World" xml:space="preserve">\n    <value>世界。</value>\n  </data>\n', ""
        )
        loc = build_fixture(self.root, resx_zh=zh_missing_world, caller=CALLER_GOOD)
        result = run_gate(self.root, loc)
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("键集合不一致", result.stdout)

    def test_illegal_key_name_fails_and_resx_untouched(self):
        keys_illegal = KEYS_CS.replace(
            'public const string World = "Test.World";',
            'public const string World = "a\\"b";\n        public const string BadAmp = "x&y";',
        )
        loc = build_fixture(self.root, keys_cs=keys_illegal, caller=CALLER_GOOD)
        en_path = loc / "Localization" / "Messages" / "Resources.resx"
        before = en_path.read_bytes()
        result = run_gate(self.root, loc, "--fix")
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        self.assertIn("非法资源键名", result.stderr)
        self.assertEqual(before, en_path.read_bytes(), "非法键名场景 resx 必须保持字节不变")

    def test_duplicate_data_fails(self):
        en_duplicate = RESX_EN.replace(
            "</root>\n",
            '  <data name="Test.World" xml:space="preserve">\n    <value>World again.</value>\n  </data>\n</root>\n',
        )
        loc = build_fixture(self.root, resx_en=en_duplicate, caller=CALLER_GOOD)
        result = run_gate(self.root, loc)
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        self.assertIn("重复", result.stdout + result.stderr)

    def test_missing_key_default_fails_then_fix_writes_todo(self):
        keys_extra = KEYS_CS.replace(
            'public const string World = "Test.World";',
            'public const string World = "Test.World";\n        public const string Extra = "Test.Extra";',
        )
        loc = build_fixture(self.root, keys_cs=keys_extra, caller=CALLER_GOOD)
        result = run_gate(self.root, loc)
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("缺失", result.stdout)

        en_path = loc / "Localization" / "Messages" / "Resources.resx"
        fixed = run_gate(self.root, loc, "--fix")
        self.assertIn("Test.Extra", en_path.read_text(encoding="utf-8"), "--fix 应补写缺失键")
        self.assertIn("[TODO]", en_path.read_text(encoding="utf-8"), "补写值为 [TODO] 占位")
        # 补写后占位值存在 → 门禁仍保持非零
        self.assertEqual(fixed.returncode, 1, result.stdout + fixed.stderr)
        self.assertIn("待翻译占位值", fixed.stdout)

    def test_callsite_arg_mismatch_fails(self):
        loc = build_fixture(self.root, caller=CALLER_BAD)
        result = run_gate(self.root, loc)
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("调用点违规", result.stdout)
        self.assertIn("Caller.cs", result.stdout)

    def test_callsite_arg_match_passes(self):
        loc = build_fixture(self.root, caller=CALLER_GOOD)
        result = run_gate(self.root, loc)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("全部通过", result.stdout)


if __name__ == "__main__":
    unittest.main(verbosity=2)
