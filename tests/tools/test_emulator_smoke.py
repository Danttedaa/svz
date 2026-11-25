"""Exercise smoke failure gates without an emulator or game assets."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


class EmulatorSmoke(unittest.TestCase):
    def run_smoke(self, mode):
        with tempfile.TemporaryDirectory() as tmp:
            tmp = Path(tmp)
            adb = tmp / 'adb'
            adb.write_text('''#!/bin/bash
case "$*" in
  'shell getprop sys.boot_completed') [ "$MODE" = boot ] && echo 0 || echo 1 ;;
  'shell getprop ro.product.cpu.abilist') echo x86_64 ;;
  'shell monkey '*) echo 'launcher output'; [ "$MODE" != launcher ] ;;
  'shell pidof '*) [ "$MODE" != died ] && echo 1234 ;;
  'shell dumpsys activity exit-info '*) echo 'process exit reason: test crash' ;;
  'logcat -d') printf '[svz-fps] settings: target=120\\n[svz] data: 4373 files\\n' ;;
  *) exit 0 ;;
esac
''')
            adb.chmod(0o755)
            sleep = tmp / 'sleep'
            sleep.write_text('#!/bin/sh\nexit 0\n')
            sleep.chmod(0o755)
            env = dict(os.environ, PATH=f'{tmp}:{os.environ["PATH"]}',
                       ANDROID_HOME=str(tmp / 'no-sdk'), MODE=mode)
            out = tmp / 'out'
            result = subprocess.run(['bash', str(ROOT / 'tools/binpatch/emulator_smoke.sh'),
                                     'unused.apk', 'test.svz', str(out), '0'],
                                    env=env, capture_output=True, text=True, timeout=15)
            files = {p.name: p.read_text() for p in out.glob('*.txt')}
            return result, files

    def test_missing_variants_cannot_pass_with_startup_logs(self):
        result, files = self.run_smoke('variants')
        self.assertNotEqual(result.returncode, 0)
        self.assertTrue(files['smoke.txt'].startswith('FAIL:'))
        for stage in ('store', 'store-hd', 'store-native', 'battle'):
            self.assertIn(f'{stage}: could not build the required', files['smoke.txt'])

    def test_boot_timeout_stops_before_launch(self):
        result, files = self.run_smoke('boot')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('did not finish booting', files['smoke.txt'])
        self.assertNotIn('launch-start.txt', files)

    def test_launcher_failure_is_reported_even_with_startup_logs(self):
        result, files = self.run_smoke('launcher')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('launcher failed', files['smoke.txt'])
        self.assertIn('launcher output', files['launch-start.txt'])
        self.assertIn('test crash', files['exit-start.txt'])

    def test_dead_process_preserves_exit_reason(self):
        result, files = self.run_smoke('died')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('app not running', files['smoke.txt'])
        self.assertIn('test crash', files['exit-start.txt'])


if __name__ == '__main__':
    unittest.main()
