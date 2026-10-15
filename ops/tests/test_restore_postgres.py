"""Disposable, offline PostgreSQL integration tests for the actual restore SQL.

Windows: python ops/tests/test_restore_postgres.py --pg-bin "C:/Program Files/PostgreSQL/18/bin"
Creates its own UTF-8 cluster on an unused loopback port, never attaches to a
running server. Every created database name starts prism_test_. Stops only that
cluster and removes only its own temporary directory. No packages are required.
"""
import argparse
import os
from pathlib import Path
import re
import socket
import subprocess
import tempfile
import unittest
from urllib.parse import quote

from test_restore_scripts import HOSTS, OPS

parser = argparse.ArgumentParser()
parser.add_argument("--pg-bin", required=True)
options, remaining = parser.parse_known_args()
PG = Path(options.pg_bin).resolve()


class RealRestore(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(prefix="prism-restore-pg-tests-")
        cls.root = Path(cls.temp.name).resolve()
        cls.data = cls.root / "cluster"
        with socket.socket() as sock:
            sock.bind(("127.0.0.1", 0))
            cls.port = str(sock.getsockname()[1])
        cls.env = dict(os.environ, PGHOST="127.0.0.1", PGPORT=cls.port,
                       PGUSER="prism_test_admin", PGDATABASE="postgres", PGCONNECT_TIMEOUT="5")
        for name in ("PGSERVICE", "PGSERVICEFILE", "PGHOSTADDR", "PGPASSWORD", "PGOPTIONS"):
            cls.env.pop(name, None)
        subprocess.run([str(PG / "initdb.exe"), "-D", str(cls.data), "--username=prism_test_admin",
                        "--auth=trust", "--encoding=UTF8", "--locale=C"], env=cls.env, capture_output=True, check=True, timeout=60)
        # File handles, not PIPEs: Windows server descendants may otherwise keep
        # pg_ctl's redirected pipe open after pg_ctl itself has already exited.
        with (cls.root / "control.log").open("w") as control:
            result = subprocess.run([str(PG / "pg_ctl.exe"), "-D", str(cls.data), "-l", str(cls.root / "server.log"),
                                     "-o", "-h 127.0.0.1 -p " + cls.port, "-w", "start"],
                                    env=cls.env, stdout=control, stderr=subprocess.STDOUT, timeout=60)
        if result.returncode:
            raise RuntimeError((cls.root / "control.log").read_text() + (cls.root / "server.log").read_text())
        cls.started = True
        cls.addClassCleanup(cls.cleanup_cluster)
        cls.sql("postgres", 'CREATE DATABASE prism_test_source TEMPLATE template0;')
        cls.sql("prism_test_source", '''CREATE TABLE "Projects" (id int);
            CREATE TABLE "AspNetUsers" (id int); CREATE TABLE "__EFMigrationsHistory" (id int);
            INSERT INTO "Projects" VALUES (42);''')
        cls.dump = cls.root / "input dump.dump"
        subprocess.run([str(PG / "pg_dump.exe"), "--format=custom", "--file=" + str(cls.dump),
                        "prism_test_source"], env=cls.env, capture_output=True, check=True)
        cls.sql("postgres", 'CREATE DATABASE prism_test_invalid TEMPLATE template0;')
        cls.invalid_dump = cls.root / "invalid.dump"
        subprocess.run([str(PG / "pg_dump.exe"), "--format=custom", "--file=" + str(cls.invalid_dump),
                        "prism_test_invalid"], env=cls.env, capture_output=True, check=True)

    @classmethod
    def cleanup_cluster(cls):
        # The -D path is generated here, never supplied by the operator.
        if cls.data.parent != cls.root or not cls.root.name.startswith("prism-restore-pg-tests-"):
            raise RuntimeError("Refusing unexpected cluster cleanup path")
        result = subprocess.run([str(PG / "pg_ctl.exe"), "-D", str(cls.data), "-m", "fast", "-w", "stop"],
                                env=cls.env, capture_output=True, text=True, timeout=60)
        if result.returncode:
            raise RuntimeError("Private test cluster could not stop; retained: " + str(cls.root))
        cls.temp.cleanup()

    @classmethod
    def sql(cls, database, sql, check=True, extra_env=None):
        env = dict(cls.env, PGDATABASE=database)
        if extra_env:
            env.update(extra_env)
        result = subprocess.run([str(PG / "psql.exe"), "--dbname=postgresql:///" + quote(database, safe=""), "-X", "--set=ON_ERROR_STOP=1", "--tuples-only",
                                 "--no-align", "--file=-"], input=sql, env=env, capture_output=True,
                                text=True, encoding="utf-8", timeout=30)
        if check and result.returncode:
            raise AssertionError(result.stdout + result.stderr)
        return result

    @staticmethod
    def identifier(value):
        assert value.startswith("prism_test_"), "Only disposable test databases allowed"
        return '"' + value.replace('"', '""') + '"'

    def restore(self, host, target, dump=None):
        assert target.startswith("prism_test_")
        dump = dump or self.dump
        name, binary = host
        env = dict(self.env, PGDATABASE=target)
        if name == "bash":
            env.update(PGBIN=PG.as_posix(), PM_DUMP_FILE=dump.as_posix(), PGRESTORE_USER="prism_test_admin")
            command = [str(binary), str(OPS / "restore-db.sh")]
        else:
            command = [str(binary), "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
                       str(OPS / "restore-db.ps1"), "-PgBin", str(PG), "-DumpFile", str(dump), "-PgHost", "127.0.0.1",
                       "-Port", self.port, "-User", "prism_test_admin", "-DbName", target]
        return subprocess.run(command, env=env, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=120)

    def test_real_restore_contract(self):
        for index, host in enumerate(HOSTS):
            # Includes whitespace, both kinds of quotes, semicolon and '='. The
            # last would be a libpq connstring if passed directly through psql -d.
            target = 'prism_test_' + str(index) + ' odd = db; "q" \' apostrophe'
            quoted = self.identifier(target)
            with self.subTest(host=host[0], scenario="existing target and retention"):
                self.sql("postgres", "CREATE DATABASE " + quoted + " TEMPLATE template0;")
                self.sql(target, "CREATE TABLE old_sentinel (id int); INSERT INTO old_sentinel VALUES (7);")
                result = self.restore(host, target)
                output = result.stdout + result.stderr
                self.assertEqual(result.returncode, 0, output)
                self.assertIn("Database restore completed", output)
                previous = re.search(r"Retained previous database: ([A-Za-z0-9_]+)", output).group(1)
                self.assertTrue(previous.startswith("prism_test_"))
                self.assertEqual(self.sql(previous, "SELECT id FROM old_sentinel;").stdout.strip(), "7")
                self.assertEqual(self.sql(target, 'SELECT id FROM "Projects";').stdout.strip(), "42")
                print("PASS real " + host[0] + ": cutover, unusual name, old data retained", flush=True)
            with self.subTest(host=host[0], scenario="invalid schema protects target"):
                result = self.restore(host, target, self.invalid_dump)
                self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertNotIn("Database restore completed", result.stdout + result.stderr)
                self.assertEqual(self.sql(target, 'SELECT id FROM "Projects";').stdout.strip(), "42")
                print("PASS real " + host[0] + ": invalid schema leaves target intact", flush=True)
            with self.subTest(host=host[0], scenario="active connection protects target"):
                # Keep a real session idle without terminating it from the restore.
                session = subprocess.Popen([str(PG / "psql.exe"), "-X", "--set=ON_ERROR_STOP=1", "--file=-"],
                                           env=dict(self.env, PGDATABASE=target), stdin=subprocess.PIPE,
                                           stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
                try:
                    session.stdin.write("SELECT 'connected';\n")
                    session.stdin.flush()
                    # psql interactive output may buffer; server query is authoritative.
                    import time
                    for attempt in range(50):
                        count = self.sql("postgres", "SELECT count(*) FROM pg_stat_activity WHERE datname = '" + target.replace("'", "''") + "';").stdout.strip()
                        if int(count) > 0:
                            break
                        time.sleep(0.05)
                    else:
                        self.fail("Test session failed to connect")
                    result = self.restore(host, target)
                    self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
                    self.assertIn("Active sessions", result.stdout + result.stderr)
                    self.assertNotIn("Database restore completed", result.stdout + result.stderr)
                    self.assertIsNone(session.poll(), "Restore terminated the existing connection")
                    self.assertEqual(self.sql(target, 'SELECT id FROM "Projects";').stdout.strip(), "42")
                    print("PASS real " + host[0] + ": active session refused, session survives", flush=True)
                finally:
                    session.communicate("\\q\n", timeout=15)
            with self.subTest(host=host[0], scenario="absent target"):
                absent = "prism_test_absent_" + str(index)
                result = self.restore(host, absent)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertEqual(self.sql(absent, 'SELECT id FROM "Projects";').stdout.strip(), "42")
                print("PASS real " + host[0] + ": absent target activated", flush=True)
            with self.subTest(host=host[0], scenario="UTF-8 identifier"):
                unicode_target = "prism_test_utf8_" + str(index) + "_" + "é" * 20
                self.assertLessEqual(len(unicode_target.encode("utf-8")), 63)
                result = self.restore(host, unicode_target)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                # Check the catalog with UTF-8 SQL, independently of libpq's
                # environment database selection used by the restore script.
                self.assertEqual(self.sql("postgres", "SELECT count(*) FROM pg_database WHERE datname = '" + unicode_target + "';").stdout.strip(), "1")
                print("PASS real " + host[0] + ": UTF-8 database identifier", flush=True)

    def test_sql_guards_and_recovery(self):
        # Execute the scripts' actual SQL blocks, checking platform parity too.
        ps = (OPS / "restore-db.ps1").read_text(encoding="utf-8")
        sh = (OPS / "restore-db.sh").read_text(encoding="utf-8")
        blocks = {}
        for name in ("Prefix", "Preflight", "Identity", "Validate", "Cutover", "Rollback"):
            block = re.search(r"\$sql" + name + r" = @'\n(.*?)'@", ps, re.S).group(1)
            bash_block = re.search(r"SQL_" + name.upper() + r"=\$\(cat <<'SQL'\n(.*?)SQL\n\)", sh, re.S).group(1)
            self.assertEqual(block, bash_block, "SQL parity: " + name)
            blocks[name] = block
        target, stage, previous = "prism_test_atomic_old", "prism_test_atomic_stage", "prism_test_atomic_previous"
        for name in (target, stage):
            self.sql("postgres", "CREATE DATABASE " + self.identifier(name) + " TEMPLATE template0;")
        ids = self.sql("postgres", "SELECT oid FROM pg_database WHERE datname IN ('" + target + "','" + stage + "') ORDER BY datname;").stdout.splitlines()
        env = dict(PRISM_RESTORE_TARGET=target, PRISM_RESTORE_STAGE=stage, PRISM_RESTORE_PREVIOUS=previous,
                   PRISM_RESTORE_TARGET_HEX=target.encode("utf-8").hex(),
                   PRISM_RESTORE_TARGET_OID=ids[0], PRISM_RESTORE_STAGE_OID=ids[1])
        snapshot_sql = "SELECT datname, oid FROM pg_database WHERE datname LIKE 'prism_test_atomic_%' ORDER BY datname;"
        before = self.sql("postgres", snapshot_sql).stdout
        cutover = blocks["Prefix"] + blocks["Cutover"]
        # Native SQL failure immediately before first and second ALTER, and a
        # failing postcondition, must each roll the entire transaction back.
        variants = {
            "target rename failure": cutover.replace('ALTER DATABASE :"target" RENAME', 'SELECT 1/0;\nALTER DATABASE :"target" RENAME', 1),
            "activation failure rolls back first rename": cutover.replace('ALTER DATABASE :"stage" RENAME', 'SELECT 1/0;\nALTER DATABASE :"stage" RENAME', 1),
            "post-rename identity mismatch": cutover.replace("SELECT (COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'target'), 0) = :'stage_oid'::bigint", "SELECT (false AND COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'target'), 0) = :'stage_oid'::bigint", 1),
        }
        for label, sql in variants.items():
            with self.subTest(scenario=label):
                failed = self.sql("postgres", sql, check=False, extra_env=env)
                self.assertNotEqual(failed.returncode, 0, failed.stdout + failed.stderr)
                self.assertEqual(before, self.sql("postgres", snapshot_sql).stdout)
                print("PASS real SQL: " + label, flush=True)
        # Stale identities must refuse mutation, and existing generated names
        # must be refused by preflight instead of being overwritten.
        failed = self.sql("postgres", cutover, check=False, extra_env=dict(env, PRISM_RESTORE_TARGET_OID="1"))
        self.assertNotEqual(failed.returncode, 0)
        self.assertEqual(before, self.sql("postgres", snapshot_sql).stdout)
        failed = self.sql("postgres", blocks["Prefix"] + blocks["Preflight"], check=False, extra_env=env)
        self.assertNotEqual(failed.returncode, 0)
        self.assertEqual(before, self.sql("postgres", snapshot_sql).stdout)
        print("PASS real SQL: stale identities and generated-name collision refused", flush=True)
        # A committed activation followed by verification failure uses the
        # conditional rollback block and preserves the restored copy as staging.
        self.sql("postgres", cutover, extra_env=env)
        self.sql("postgres", blocks["Prefix"] + blocks["Rollback"], extra_env=env)
        self.assertEqual(before, self.sql("postgres", snapshot_sql).stdout)
        print("PASS real SQL: postcommit rollback restores original identities", flush=True)


if __name__ == "__main__":
    unittest.main(argv=[__file__] + remaining, verbosity=2)
