"""Offline restore regressions. Windows: python ops/tests/test_restore_scripts.py.

Uses harmless native executable shims in both Windows PowerShell hosts and Git Bash.
No PostgreSQL connection, package installation or network access is performed.
For real SQL semantics use test_restore_postgres.py against its private temporary cluster.
"""
import base64
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from urllib.parse import unquote

OPS = Path(__file__).resolve().parents[1]
HOSTS = [
    ("powershell51", Path(os.environ.get("WINDIR", "C:/Windows")) / "System32/WindowsPowerShell/v1.0/powershell.exe"),
    ("powershell7", Path("C:/Program Files/PowerShell/7/pwsh.exe")),
    ("bash", Path("C:/Program Files/Git/bin/bash.exe")),
]


class RestoreScripts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(prefix="prism-restore-stub-tests-")
        cls.root = Path(cls.temp.name)
        compiler = Path(os.environ.get("WINDIR", "C:/Windows")) / "Microsoft.NET/Framework64/v4.0.30319/csc.exe"
        source = cls.root / "RestoreUtilityStub.cs"
        shutil.copyfile(OPS / "tests/stubs/RestoreUtilityStub.cs.txt", source)
        cls.stub = cls.root / "stub.exe"
        subprocess.run([str(compiler), "/nologo", "/target:exe", "/out:" + str(cls.stub), str(source)], check=True)
        for _, host in HOSTS:
            if not host.is_file():
                raise RuntimeError("Required test host missing: " + str(host))

    @classmethod
    def tearDownClass(cls):
        cls.temp.cleanup()

    def invoke(self, host, failure="", missing="", target='prism_test_odd = db; "quote" \' apostrophe', alias=False, default_host=False):
        case = Path(tempfile.mkdtemp(prefix="case-", dir=self.root))
        bindir = case / "stub bin"
        bindir.mkdir()
        for tool in ("dropdb", "createdb", "pg_restore", "psql"):
            if tool != missing:
                shutil.copyfile(self.stub, bindir / (tool + ".exe"))
        dump = case / "input dump.dump"
        if missing != "dump":
            dump.write_text("not a real archive", encoding="utf-8")
        log = case / "calls.log"
        env = dict(os.environ, PRISM_TEST_NATIVE_LOG=str(log), PRISM_TEST_FAIL_PHASE=failure,
                   PGSERVICE="must-not-override-db", PGHOSTADDR="192.0.2.1", PGDATABASE=target)
        name, binary = host
        if name == "bash":
            # Git Bash converts drive-letter paths passed to its script. Native shims
            # also verify the complete unusual database name survives the environment.
            env.update(PGBIN=bindir.as_posix(), PGHOST="localhost" if default_host else "lan-db", PGPORT="5544", PGRESTORE_USER="test operator", PM_DUMP_FILE=dump.as_posix())
            command = [str(binary), str(OPS / "restore-db.sh")]
        else:
            command = [str(binary), "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
                       str(OPS / "restore-db.ps1"), "-PgBin", str(bindir), "-DumpFile", str(dump),
                       "-Port", "5544", "-User", "test operator", "-DbName", target]
            if not default_host:
                command += ["-Host" if alias else "-PgHost", "lan-db"]
        result = subprocess.run(command, env=env, capture_output=True, text=True, errors="replace", timeout=120)
        calls = []
        if log.exists():
            for line in log.read_text(encoding="utf-8-sig").splitlines():
                fields = line.split("\t")
                values = [base64.b64decode(x).decode("utf-8") for x in fields[2:]]
                calls.append(dict(tool=fields[0], phase=fields[1], target=values[0], stage=values[1], previous=values[2],
                                  database=values[3], service=values[4], sql=values[5], hostaddr=values[6], args=values[7:]))
        return result, calls, dump

    def test_restore_boundaries(self):
        # Fail every required native phase, including conditional rollback.
        scenarios = [("success", "", ""), ("alias", "", ""), ("default host and 63-byte target", "", ""),
                     ("postgres maintenance database", "", ""),
                     ("missing dump", "", "dump"), ("missing pg_restore", "", "pg_restore"),
                     ("missing psql", "", "psql"), ("missing createdb", "", "createdb"),
                     ("missing dropdb", "", "dropdb")]
        scenarios += [(phase + " failure", phase, "") for phase in
                      ("list", "preflight", "createdb", "identity", "restore", "validate", "cutover", "activated")]
        scenarios += [("rollback failure", "activated,rollback", "")]
        for host in HOSTS:
            for label, failure, missing in scenarios:
                with self.subTest(host=host[0], scenario=label):
                    extra = {}
                    if label == "default host and 63-byte target":
                        extra = dict(default_host=True, target="prism_test_" + "x" * 52)
                    if label == "postgres maintenance database":
                        extra = dict(target="postgres")
                    result, calls, dump = self.invoke(host, failure, missing, alias=label == "alias", **extra)
                    output = result.stdout + result.stderr
                    phases = [c["phase"] for c in calls]
                    diagnostic = f"{phases}\n{output}"
                    success = not (failure or missing)
                    self.assertEqual(result.returncode == 0, success, diagnostic)
                    self.assertEqual("Database restore completed" in output, success, diagnostic)
                    if missing:
                        self.assertEqual(calls, [], diagnostic)
                        print(f"PASS {host[0]}: {label}", flush=True)
                        continue
                    expected = ["list", "preflight", "createdb", "identity", "restore", "validate", "cutover", "activated"]
                    if failure:
                        first = failure.split(",")[0]
                        expected = expected[:expected.index(first) + 1]
                        if first == "activated":
                            expected += ["rollback"]
                    self.assertEqual(phases, expected, diagnostic)
                    for call in calls:
                        self.assertIn("stub stdout: " + call["phase"], output, diagnostic)
                        self.assertIn("stub stderr: " + call["phase"], output, diagnostic)
                        # Native argv fidelity and literal database targeting.
                        if call["phase"] != "list":
                            expected_host = "localhost" if extra.get("default_host") else "lan-db"
                            for arg in ("--host=" + expected_host, "--port=5544", "--username=test operator"):
                                self.assertIn(arg, call["args"], diagnostic)
                        self.assertEqual(call["service"], "", diagnostic)
                        self.assertEqual(call["hostaddr"], "", diagnostic)
                        if call["stage"]:
                            self.assertLessEqual(len(call["stage"].encode()), 63)
                            self.assertLessEqual(len(call["previous"].encode()), 63)
                            self.assertNotEqual(call["stage"], call["previous"])
                        if call["tool"] == "dropdb":
                            self.assertEqual(call["args"][-1], call["stage"], diagnostic)
                            self.assertNotEqual(call["args"][-1], call["target"], diagnostic)
                        if call["phase"] == "restore":
                            self.assertIn("--dbname=" + call["stage"], call["args"])
                            self.assertIn("--single-transaction", call["args"])
                            self.assertIn("--exit-on-error", call["args"])
                            self.assertNotIn("--clean", call["args"])
                            self.assertNotIn("--create", call["args"])
                        if call["tool"] == "psql":
                            self.assertIn("--set=ON_ERROR_STOP=1", call["args"])
                            self.assertIn("-X", call["args"])
                            self.assertIn("--file=-", call["args"])
                            # Either conventional or fully percent-encoded URI
                            # components are valid; decode to check exact name.
                            uri = next(arg.split("=", 1)[1] for arg in call["args"] if arg.startswith("--dbname="))
                            self.assertEqual(unquote(uri.removeprefix("postgresql:///")), call["database"])
                        if call["phase"] in ("validate", "identity"):
                            self.assertEqual(call["database"], call["stage"])
                        if call["phase"] in ("preflight", "cutover", "rollback"):
                            self.assertEqual(call["database"], "template1" if call["target"] == "postgres" else "postgres")
                        if call["phase"] == "activated":
                            self.assertEqual(call["database"], call["target"])
                        if call["phase"] in ("cutover", "rollback"):
                            self.assertIn("BEGIN;", call["sql"])
                            self.assertIn("COMMIT;", call["sql"])
                            self.assertNotIn("pg_terminate_backend", call["sql"])
                    if success:
                        cutover = next(c for c in calls if c["phase"] == "cutover")
                        self.assertIn(cutover["target"], output)
                        self.assertIn(cutover["previous"], output)
                        self.assertIn("Retained previous database", output)
                    elif failure in ("restore", "validate"):
                        self.assertIn("cleanup deferred for identity safety", output)
                        self.assertNotIn("dropdb", phases)
                    print(f"PASS {host[0]}: {label}", flush=True)


if __name__ == "__main__":
    unittest.main(verbosity=2)
