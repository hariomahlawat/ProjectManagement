"""Test only restore's byte encoder; never execute the restore or access a DB.

python ops/tests/test_restore_utf8.py --bash /bin/bash
Windows: use --bash "C:/Program Files/Git/bin/bash.exe".
"""
import argparse
import os
from pathlib import Path
import re
import subprocess
import unittest

parser = argparse.ArgumentParser()
parser.add_argument('--bash', required=True)
options, remaining = parser.parse_known_args()
script = (Path(__file__).resolve().parents[1] / 'restore-db.sh').read_text(encoding='utf-8')
# Extract this side-effect-free function instead of sourcing the restore script.
encoder = re.search(r'(?ms)^utf8_hex\(\) \{.*?^\}', script).group()


class RestoreUtf8(unittest.TestCase):
    def test_byte_encoding_in_c_and_utf8_locales(self):
        values = ['prism_test_ascii', 'space = db; "quote" \' apostrophe',
                  '\u00e9\u00e9', '\u4e2d\u6587', '\U0001f642',
                  'prism_test_utf8_' + '\u00e9' * 20, 'prism_test_' + 'x' * 52]
        passed = 0
        for locale in ('C', 'C.UTF-8'):
            for value in values:
                with self.subTest(locale=locale, value=value):
                    result = subprocess.run([options.bash, '-c', encoder + '\nutf8_hex "$PRISM_TEST_UTF8_VALUE"'],
                                            env=dict(os.environ, LC_ALL=locale, PRISM_TEST_UTF8_VALUE=value),
                                            capture_output=True, text=True, timeout=30)
                    self.assertEqual(result.returncode, 0, result.stderr)
                    self.assertEqual(result.stdout, value.encode('utf-8').hex().upper())
                    passed += 1
        print(f'Restore byte encoding: {passed}/14 cases passed.', flush=True)


if __name__ == '__main__':
    unittest.main(argv=[__file__] + remaining, verbosity=2)
