import os
import stat
import tempfile
import time
import unittest

import rooms_agent as A


class AgentTest(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.mkdtemp()
        A.CONF_DIR, A.STATUS_DIR = self.dir, self.dir
        self.active, self.status = set(), {}
        A.unit = lambda action, port: self._unit(action, port)
        A.running = lambda port: int(port) in self.active
        A.room_status = lambda port: self.status.get(int(port), {})
        A.free_mb = lambda: 500

    def _unit(self, action, port):
        if action == "start": self.active.add(int(port))
        if action == "stop": self.active.discard(int(port))
        return type("R", (), {"returncode": 0, "stdout": "", "stderr": ""})()

    def test_start_takes_a_free_port_and_keeps_the_password_private(self):
        rooms = {}
        r = A.start({"id": "j1", "owner": "u1", "owner_name": "Arthur", "name": "Arthur's ~r~posse", "password": "secret"}, rooms, [27461, 27462])
        self.assertEqual((r["ok"], r["port"]), (True, 27461))
        self.assertEqual(rooms["27461"]["name"], "Arthur's rposse")
        mode = stat.S_IMODE(os.stat(os.path.join(self.dir, "27461.pass")).st_mode)
        self.assertEqual(mode & 0o077, 0)
        self.assertEqual(open(os.path.join(self.dir, "27461.pass")).read(), "secret")

    def test_one_room_each_and_none_when_full(self):
        rooms = {}
        A.start({"id": "a", "owner": "u1", "name": "A", "password": "pass1"}, rooms, [27461])
        self.assertIn("already", A.start({"id": "b", "owner": "u1", "name": "B", "password": "pass2"}, rooms, [27461, 27462])["why"])
        self.assertIn("in use", A.start({"id": "c", "owner": "u2", "name": "C", "password": "pass3"}, rooms, [27461])["why"])

    def test_password_rules(self):
        self.assertFalse(A.start({"id": "x", "owner": "u", "name": "N", "password": "abc"}, {}, [27461])["ok"])
        self.assertFalse(A.start({"id": "x", "owner": "u", "name": "N", "password": "a" * 65}, {}, [27461])["ok"])

    def test_no_new_room_when_memory_is_short(self):
        A.free_mb = lambda: A.MIN_FREE_MB - 1
        r = A.start({"id": "a", "owner": "u1", "name": "A", "password": "pass1"}, {}, [27461])
        self.assertEqual((r["ok"], "memory" in r["why"]), (False, True))

    def test_idle_and_time_up_rooms_stop(self):
        rooms = {}
        A.start({"id": "a", "owner": "u1", "name": "A", "password": "pass1"}, rooms, [27461, 27462])
        A.start({"id": "b", "owner": "u2", "name": "B", "password": "pass2"}, rooms, [27461, 27462])
        rooms["27461"]["started"] = time.time() - 3600
        self.status[27461] = {"players": 0, "empty_since": time.time() - 31 * 60}
        rooms["27462"]["started"] = time.time() - 13 * 3600
        self.status[27462] = {"players": 2, "empty_since": None}
        ended = {p: w for p, _, w in A.housekeeping(rooms, {"idle_minutes": 30, "max_hours": 12})}
        self.assertEqual(ended, {"27461": "idle", "27462": "time"})
        self.assertEqual(rooms, {})
        self.assertFalse(os.path.exists(os.path.join(self.dir, "27461.pass")))

    def test_a_busy_room_keeps_going(self):
        rooms = {}
        A.start({"id": "a", "owner": "u1", "name": "A", "password": "pass1"}, rooms, [27461])
        self.status[27461] = {"players": 1, "empty_since": None}
        self.assertEqual(A.housekeeping(rooms, {"idle_minutes": 30, "max_hours": 12}), [])


if __name__ == "__main__":
    unittest.main()
