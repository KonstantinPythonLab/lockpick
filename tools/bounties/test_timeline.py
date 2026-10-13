"""Tests for the capture timeline and the room index. Run: python -m unittest discover tools/bounties"""
import json
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import bounties  # noqa: E402
import timeline  # noqa: E402

FIXTURES = bounties.ROOT / "tests" / "Lockpick.Tests" / "Fixtures"


class RoomIndexTests(unittest.TestCase):
    """Flag numbers from the game's room files, checked against what live captures showed (docs/MEMORY_MAP.md)."""

    @classmethod
    def setUpClass(cls):
        cls.names = timeline.Names()

    def item(self, room, item):
        return [e for e in self.names.index["items"] if e["room"] == room and e["item"] == item]

    def test_pickup_flags_match_live_captures(self):
        # flagRoomItems1 bit 0x80000 cleared when the blue jewel was taken in the dining room.
        self.assertEqual(self.item("105", 33)[0]["flag"], timeline.flag_index(1, 19))
        # flagRoomItems4 bit 0x4000: the 1F map in 107; flagRoomItems5 bit 0x20000: the wind crest in 10D.
        self.assertEqual(self.item("107", 78)[0]["flag"], timeline.flag_index(4, 14))
        self.assertEqual(self.item("10D", 41)[0]["flag"], timeline.flag_index(5, 17))

    def test_kill_and_lock_flags_match_live_captures(self):
        dogs = sorted(e["flag"] for e in self.names.index["enemies"] if e["room"] == "108" and e["type"] == 2)
        self.assertEqual(dogs, [timeline.flag_index(0, 31), timeline.flag_index(0, 30)])  # flagEnemies0 0x80000000, 0x40000000
        door = [d for d in self.names.index["doors"] if d["room"] == "206" and d["target"] == "207"]
        self.assertEqual(door[0]["lockFlag"], timeline.flag_index(0, 10))  # flagLocks0 0x400 when it was unlocked

    def test_index_holds_only_ids_and_numbers(self):
        for kind in ("items", "enemies", "doors"):
            for entry in self.names.index[kind]:
                self.assertTrue(all(isinstance(v, (int, type(None))) or k in ("room", "player", "target")
                                    for k, v in entry.items()), entry)


class TimelineTests(unittest.TestCase):
    def test_golden_sample_reads_as_events(self):
        session = bounties.parse_memory("sample", (FIXTURES / "region-log-sample.bin").read_bytes(),
                                        bounties.load_profile_fields("re1-gog-usa-hotfix4"))
        text = timeline.render(bounties.Capture("sample.zip", [session]), timeline.Names())
        self.assertIn("continued at 0:33 as Jill", text)
        self.assertIn("killed Cerberus (dog)", text)
        self.assertIn("unlocked door", text)
        self.assertNotIn("Kenneth", text)  # the leftover slot during the room load is not reported

    def test_markers_are_placed_by_time(self):
        capture = bounties.Capture("m.zip", [], markers=bounties.parse_markers(json.dumps(
            {"number": 1, "at": "2026-10-13T10:00:00-07:00", "room": "106", "gameSeconds": 1.0, "note": "hello"})))
        self.assertIn("Marker #1** 10:00:00 room 106: hello", timeline.render(capture, timeline.Names()))


if __name__ == "__main__":
    unittest.main()
