"""Tests for relay.py's routing (no sockets)."""
import hashlib
import hmac
import struct
import unittest

import relay as R


class Clock:
    def __init__(self):
        self.t = 1000.0

    def __call__(self):
        return self.t


def hello(name, prefer_host=False, version=R.VERSION):
    return R.header(R.HELLO, 0, 0) + struct.pack("<iB", version, 1 if prefer_host else 0) + R.short_string(name.encode())


def auth(nonce, password="pw"):
    return R.header(R.AUTH, 0, 0) + hmac.new(password.encode(), nonce, hashlib.sha256).digest()


def roster(pkt):
    n, pos, out = pkt[7], 8, []
    for _ in range(n):
        pid, role = pkt[pos], pkt[pos + 1]
        name, pos = R.read_string(pkt, pos + 2)
        out.append((pid, role, name))
    return out


class RelayTest(unittest.TestCase):
    def setUp(self):
        self.clock = Clock()
        self.r = R.Relay("pw", clock=self.clock, log=lambda m: None)

    def join(self, name, addr, prefer_host=False, password="pw"):
        out = self.r.handle(hello(name, prefer_host), addr)
        self.assertEqual(out[0][0][4], R.CHALLENGE)
        nonce = out[0][0][7:]
        return self.r.handle(auth(nonce, password), addr)

    def welcome_of(self, out, addr):
        pkt = next(p for p, a in out if a == addr and p[4] == R.WELCOME)
        _, my_id, host_id = struct.unpack_from("<iBB", pkt, 7)
        return my_id, host_id

    def test_first_in_hosts_others_guest(self):
        a, b = ("1.1.1.1", 5000), ("2.2.2.2", 5000)
        out = self.join("Alice", a)
        self.assertEqual(self.welcome_of(out, a), (1, 1))
        out = self.join("Friend", b)
        self.assertEqual(self.welcome_of(out, b), (2, 1))
        r = roster(next(p for p, to in out if to == a and p[4] == R.ROSTER))
        self.assertEqual(r, [(1, R.HOST, "Alice"), (2, R.GUEST, "Friend")])

    def test_wrong_password_and_strangers(self):
        a = ("1.1.1.1", 5000)
        out = self.join("Bad", a, password="nope")
        self.assertEqual(out[0][0][4], R.REJECT)
        self.assertEqual(self.r.players, {})
        state = R.header(R.STATE, 0, R.BROADCAST) + b"x" * 40
        self.assertEqual(self.r.handle(state, ("9.9.9.9", 1)), [], "unknown senders are ignored")
        self.assertEqual(self.r.handle(b"junk", a), [])

    def test_auth_without_challenge_or_replayed(self):
        a = ("1.1.1.1", 5000)
        self.assertEqual(self.r.handle(auth(b"0" * 16), a), [])
        out = self.r.handle(hello("x"), a)
        nonce = out[0][0][7:]
        self.r.handle(auth(nonce), a)
        b = ("6.6.6.6", 1)
        self.assertEqual(self.r.handle(auth(nonce), b), [], "a captured Auth is useless from elsewhere")

    def test_challenge_is_not_an_amplifier(self):
        h = hello("someone")
        out = self.r.handle(h, ("3.3.3.3", 1))
        self.assertLessEqual(len(out[0][0]), len(h) + 16)

    def test_version_mismatch(self):
        out = self.r.handle(hello("old", version=2), ("1.1.1.1", 1))
        self.assertEqual(out[0][0][4], R.REJECT)
        self.assertIn(b"version", out[0][0])

    def test_routing_and_sender_stamping(self):
        h, g1, g2 = ("1.1.1.1", 1), ("2.2.2.2", 2), ("3.3.3.3", 3)
        self.join("Host", h); self.join("G1", g1); self.join("G2", g2)
        state = R.header(R.STATE, 99, R.BROADCAST) + b"s" * 40   # forged sender 99
        out = self.r.handle(state, g1)
        self.assertEqual(sorted(a for _, a in out), sorted([h, g2]))
        self.assertTrue(all(p[5] == 2 for p, _ in out), "sender stamped as G1's real id")

        world = R.header(R.WORLD, 0, R.BROADCAST) + b"w" * 100
        self.assertEqual(sorted(a for _, a in self.r.handle(world, h)), sorted([g1, g2]))
        self.assertEqual(sorted(a for _, a in self.r.handle(world, g1)), sorted([h, g2]),
                         "any game can run its own area and share it")

        hit_to_host = R.header(R.HIT, 0, 1) + b"h" * 12
        self.assertEqual([a for _, a in self.r.handle(hit_to_host, g2)], [h], "hit goes to the owner named in to")
        hit_to_g2 = R.header(R.HIT, 0, 3) + b"h" * 12
        out = self.r.handle(hit_to_g2, g1)
        self.assertEqual([a for _, a in out], [g2], "guest-owned NPCs can be hit too")
        self.assertEqual(out[0][0][5], 2, "stamped with the shooter")
        self.assertEqual(self.r.handle(R.header(R.HIT, 0, 1) + b"h" * 12, h), [], "not to yourself")
        self.assertEqual(self.r.handle(R.header(R.HIT, 0, 77) + b"h" * 12, h), [], "unknown target dropped")

        action = R.header(R.ACTION, 0, 1) + b"a" * 7
        self.assertEqual([a for _, a in self.r.handle(action, g1)], [h], "actions go to the owner named in to")
        notice_one = R.header(R.NOTICE, 0, 2) + b"\x00\x02\x00hi"
        self.assertEqual([a for _, a in self.r.handle(notice_one, h)], [g1], "a notice for one player")
        notice_all = R.header(R.NOTICE, 0, R.BROADCAST) + b"\x00\x02\x00hi"
        self.assertEqual(sorted(a for _, a in self.r.handle(notice_all, h)), sorted([g1, g2]), "news for everyone else")
        objectives = R.header(R.OBJECTIVES, 0, R.BROADCAST) + bytes((0,))
        self.assertEqual(sorted(a for _, a in self.r.handle(objectives, h)), sorted([g1, g2]), "objectives to everyone else")

    def test_party_goes_to_everyone_else(self):
        h, g1, g2 = ("1.1.1.1", 1), ("2.2.2.2", 2), ("3.3.3.3", 3)
        self.join("Host", h); self.join("G1", g1); self.join("G2", g2)
        party = R.header(R.PARTY, 0, R.BROADCAST) + bytes((1,)) + b"\x07\x00\x00\x00" + bytes((0,)) + b"\x00\x00\x00\x00"
        out = self.r.handle(party, h)
        self.assertEqual(sorted(a for _, a in out), sorted([g1, g2]))
        self.assertTrue(all(p[5] == 1 for p, _ in out))

    def test_full_room(self):
        for i in range(4):
            self.join(f"P{i}", (f"10.0.0.{i}", 1))
        out = self.join("Fifth", ("10.0.0.9", 1))
        self.assertEqual(out[0][0][4], R.REJECT)

    def test_host_leaves_preferred_guest_takes_over(self):
        h, g1, g2 = ("1.1.1.1", 1), ("2.2.2.2", 2), ("3.3.3.3", 3)
        self.join("Host", h); self.join("G1", g1); self.join("Alice", g2, prefer_host=True)
        out = self.r.handle(R.header(R.BYE, 0, 0), h)
        r = roster(out[0][0])
        self.assertEqual(r, [(2, R.GUEST, "G1"), (3, R.HOST, "Alice")])
        self.assertEqual(sorted(a for _, a in out), sorted([g1, g2]))

    def test_host_leaves_oldest_guest_takes_over(self):
        h, g1, g2 = ("1.1.1.1", 1), ("2.2.2.2", 2), ("3.3.3.3", 3)
        self.join("Host", h); self.join("G1", g1); self.join("G2", g2)
        self.clock.t += 5
        self.r.handle(R.header(R.STATE, 0, R.BROADCAST) + b"s", g1)
        self.r.handle(R.header(R.STATE, 0, R.BROADCAST) + b"s", g2)
        self.clock.t += 6        # host silent for 11 s
        out = self.r.expire()
        self.assertEqual(roster(out[0][0])[0], (2, R.HOST, "G1"))

    def test_lost_welcome_is_resent(self):
        a = ("1.1.1.1", 1)
        self.join("Alice", a)
        out = self.r.handle(hello("Alice"), a)
        self.assertEqual(out[0][0][4], R.WELCOME)

    def test_ids_reused_after_leaving(self):
        a, b = ("1.1.1.1", 1), ("2.2.2.2", 2)
        self.join("A", a); self.join("B", b)
        self.r.handle(R.header(R.BYE, 0, 0), a)
        out = self.join("C", ("3.3.3.3", 3))
        self.assertEqual(self.welcome_of(out, ("3.3.3.3", 3))[0], 1)

    def test_spoofed_hello_flood_is_bounded(self):
        for i in range(2000):
            self.r.handle(hello(f"x{i}"), (f"10.{i // 250}.{i % 250}.1", 5000 + i))
        self.assertLessEqual(len(self.r.pending), R.MAX_PENDING)
        self.clock.t += 60
        self.r.expire()
        self.assertEqual(len(self.r.pending), 0, "expired logins go")
        self.assertEqual(len(self.r.hello_times), 0, "old rate-limit entries go")
        h = ("1.1.1.1", 1)
        self.join("Host", h)
        self.assertTrue(any(p.name == "Host" for p in self.r.players.values()), "real players still get in")

    def test_hello_flood_is_rate_limited(self):
        outs = [self.r.handle(hello("spam"), ("4.4.4.4", p)) for p in range(30)]
        self.assertEqual(sum(1 for o in outs if o), 20)


if __name__ == "__main__":
    unittest.main()
