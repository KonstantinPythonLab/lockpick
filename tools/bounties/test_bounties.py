"""Tests for the bounty tooling. Run: python -m unittest discover tools/bounties"""
import copy
import io
import json
import sys
import unittest
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import bounties  # noqa: E402

FIXTURES = bounties.ROOT / "tests" / "Lockpick.Tests" / "Fixtures"


def make_zip(trace: str, builds=None, events: str = "") -> bytes:
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w") as z:
        z.writestr("trace-1.txt", trace)
        z.writestr("events-1.txt", events)
        z.writestr("capture-info.json", json.dumps({"Builds": builds or []}))
        z.writestr("../evil.txt", "ignored: not an expected member name")
    return buffer.getvalue()


BASE = ("[10:00:00.000] stage=0x0, room=0x6, camera=0x0, hp=0x8C, maxHp=0x8C, character=0x0, gameTimer=0x100, "
        "mainMenu=0x0, inv0=0x1, inv1=0xF02, equippedSlot=0x1, inventoryPtr=0xBE9944, box0=0xF0B | timer=0x100 pos=0x0,0x0\n")


class LiveTraceTests(unittest.TestCase):
    def test_chris_trace_matches_manual_verification(self):
        found, facts = bounties.detect(bounties.read_capture(FIXTURES / "re1-live-chris-death-and-restart.trace.txt"))
        for detector in ("chris_game", "room_changes", "death", "new_game_after_death"):
            self.assertTrue(found[detector].auto, detector)
        self.assertNotIn("jill_game", found)
        self.assertEqual(facts["rooms"], {1: [0x04, 0x05, 0x06]})

    def test_jill_trace_matches_manual_verification(self):
        found, facts = bounties.detect(bounties.read_capture(FIXTURES / "re1-live-jill-death-load.trace.txt"))
        for detector in ("jill_game", "death", "new_game_after_death", "save_loaded", "second_floor"):
            self.assertTrue(found[detector].auto, detector)
        self.assertIn("fire_and_reload", found)
        self.assertFalse(found["healing"].auto)  # healing needs a human to say which item
        self.assertIn(0x03, facts["rooms"][2] or [])  # room 203
        self.assertEqual(facts["new_item_ids"], [])


class ItemBoxSessionTests(unittest.TestCase):
    def test_item_box_session_evidence(self):
        # Live Jill session (2026-10-13): item box used, all 8 slots filled, weapons switched, shotgun fired.
        found, facts = bounties.detect(bounties.read_capture(FIXTURES / "re1-live-jill-itembox.trace.txt"))
        for detector in ("item_box_changed", "full_inventory", "weapon_switching", "fire_and_reload", "healing"):
            self.assertIn(detector, found)
            self.assertFalse(found[detector].auto, detector)  # all need a maintainer's review
        self.assertIn("jill_game", found)
        self.assertIn(0x0B, facts["rooms"][2])  # room 20B


class MemoryLogTests(unittest.TestCase):
    """region-log-sample.bin is written by the C# RegionLogWriter (RegionLogTests); this pins the shared format."""

    def setUp(self):
        payload = (FIXTURES / "region-log-sample.bin").read_bytes()
        self.session = bounties.parse_memory("sample", payload, bounties.load_profile_fields("re1-gog-usa-hotfix4"))

    def test_decodes_profile_fields_from_the_memory_log(self):
        first = self.session.states[0]
        self.assertEqual((first["room"], first["hp"], first["maxHp"], first["character"]), (6, 96, 96, 1))
        self.assertEqual((first["enemy0Flags"], first["enemy0Type"], first["enemy0Hp"]), (1, 2, 30))
        self.assertEqual(self.session.states[-1]["box5"], 0)

    def test_enemies_kills_flags_and_item_box_are_detected(self):
        capture = bounties.Capture("sample.zip", [self.session])
        found, facts = bounties.detect(capture)
        self.assertIn("Cerberus", found["enemies_dog_crow"].text)
        self.assertIn("106", found["enemies_dog_crow"].text)
        self.assertIn("×1", found["enemy_killed"].text)
        # The leftover slot cleared during the room load is neither an encounter nor a kill.
        self.assertNotIn("type 37", found["enemy_killed"].text)
        self.assertNotIn("type 37", json.dumps(facts["enemies"]))
        self.assertIn("flagLocks0", found["lock_flags"].text)
        self.assertIn("item box entry 6", found["item_box_changed"].text)
        self.assertTrue(all(not f.auto for k, f in found.items() if k.startswith(("enemies", "enemy_", "lock"))))

    def test_capture_zip_prefers_the_memory_log(self):
        buffer = io.BytesIO()
        with zipfile.ZipFile(buffer, "w") as z:
            z.writestr("trace-1.txt", "")
            z.writestr("memory-1.bin", (FIXTURES / "region-log-sample.bin").read_bytes())
            z.writestr("capture-info.json", json.dumps({"Builds": [{"ProfileId": "re1-gog-usa-hotfix4"}]}))
        capture = bounties.read_capture_zip("with-memory.zip", buffer.getvalue())
        self.assertGreater(len(capture.sessions[0].states), 1)
        self.assertIn("enemy0Type", capture.sessions[0].states[0])


class AttractDemoTests(unittest.TestCase):
    def test_attract_mode_demo_produces_no_evidence(self):
        # Live capture (2026-10-13) of the title-screen demo: Chris in room 107 with a preset inventory and item box,
        # HP left at 0. It is not gameplay and must not count towards any bounty.
        found, facts = bounties.detect(bounties.read_capture(FIXTURES / "re1-live-attract-demo.trace.txt"))
        self.assertEqual(found, {})
        self.assertEqual(facts["rooms"], {})


class CaptureZipTests(unittest.TestCase):
    def test_a_single_rebecca_signal_needs_review(self):
        trace = BASE + "[10:00:05.000] character=0x3 | timer=0x200 pos=0x0,0x0\n"
        found, _ = bounties.detect(bounties.read_capture_zip("one.zip", make_zip(trace)))
        self.assertFalse(found["rebecca"].auto)

    def test_captures_with_trainer_writes_never_verify_automatically(self):
        trace = BASE + "[10:00:05.000] character=0x3, inventoryPtr=0xBE9950 | timer=0x200 pos=0x0,0x0\n"
        buffer = io.BytesIO()
        with zipfile.ZipFile(buffer, "w") as z:
            z.writestr("trace-1.txt", trace)
            z.writestr("capture-info.json", json.dumps({"Builds": [], "TrainerWrites": 2}))
        found, _ = bounties.detect(bounties.read_capture_zip("trainer.zip", buffer.getvalue()))
        self.assertTrue(found)
        self.assertFalse(any(f.auto for f in found.values()))

    def test_rebecca_is_verified_automatically_and_item_box_needs_review(self):
        trace = BASE + "[10:00:05.000] character=0x3, inventoryPtr=0xBE9950 | timer=0x200 pos=0x0,0x0\n" \
                       "[10:00:09.000] box0=0x0 | timer=0x300 pos=0x0,0x0\n"
        capture = bounties.read_capture_zip("test.zip", make_zip(trace))
        found, _ = bounties.detect(capture)
        self.assertTrue(found["rebecca"].auto)
        self.assertFalse(found["item_box_changed"].auto)

        data = copy.deepcopy(bounties.load_data())
        for b in bounties.all_bounties(data):  # independent of what the live data has verified so far
            if b["id"] in ("B007", "B102"):
                b["status"], b["evidence"] = "needed", []
        changes = bounties.apply(data, found, "test.zip", "2026-10-13")
        status = {b["id"]: b for b in bounties.all_bounties(data)}
        self.assertEqual(status["B007"]["status"], "done")
        self.assertEqual(status["B102"]["status"], "partial")
        self.assertIn("needs review", status["B102"]["evidence"][-1])
        self.assertTrue(any(c.startswith("B007") for c in changes))

    def test_unsupported_build_is_reported(self):
        builds = [{"FileName": "Biohazard.exe", "FileSize": 123, "Sha256": "AB" * 32, "ProfileId": None}]
        capture = bounties.read_capture_zip("jp.zip", make_zip("", builds))
        found, facts = bounties.detect(capture)
        self.assertIn("other_build", found)
        report = bounties.report(bounties.load_data(), capture, found, facts)
        self.assertIn("not supported yet", report)

    def test_oversized_members_are_rejected(self):
        original = bounties.MAX_MEMBER_BYTES
        bounties.MAX_MEMBER_BYTES = 10
        try:
            with self.assertRaises(ValueError):
                bounties.read_capture_zip("big.zip", make_zip(BASE))
        finally:
            bounties.MAX_MEMBER_BYTES = original

    def test_only_github_attachment_urls_are_accepted(self):
        body = ("see https://github.com/user-attachments/files/123/LockpickCapture-1.zip and "
                "https://evil.example.com/x.zip and https://github.com/user-attachments/files/9/run.exe")
        self.assertEqual(bounties.ATTACHMENT_PATTERN.findall(body),
                         ["https://github.com/user-attachments/files/123/LockpickCapture-1.zip"])


class RenderTests(unittest.TestCase):
    def test_document_is_in_sync_with_data(self):
        self.assertEqual(bounties.DOC.read_text(encoding="utf-8").replace("\r\n", "\n"), bounties.render(bounties.load_data()))

    def test_every_bounty_has_a_unique_id_and_valid_status(self):
        ids = [b["id"] for b in bounties.all_bounties(bounties.load_data())]
        self.assertEqual(len(ids), len(set(ids)))
        for b in bounties.all_bounties(bounties.load_data()):
            self.assertIn(b["status"], bounties.STATUS_ICON)


if __name__ == "__main__":
    unittest.main()
