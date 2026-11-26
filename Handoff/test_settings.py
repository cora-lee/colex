import unittest
from settings import DEFAULTS,validate,SCHEMA
from signals import Mapping
from test_signals import body

class SettingsTests(unittest.TestCase):
    def test_invalid_relationships_rejected(self):
        for changes in [{'smooth':float('nan')},{'hand_reset':.5},{'clap_close':1.1},{'jump_hand_reset':.8}]:
            with self.assertRaises(ValueError):validate(DEFAULTS|changes)

    def test_custom_hand_and_jump_thresholds(self):
        m=Mapping(validate(DEFAULTS|{'hand_trigger':.7,'jump_hand_trigger':1.3}));m.calibrate()
        for n in range(32):m.update([body(),body(),body()],n*.1)
        out=m.update([body(),body(leftHeight=.4),body(leftHeight=1,rightHeight=1)],4)
        self.assertEqual(out['lane'],0);self.assertEqual(out['jumps'],0)

    def test_bundle_matches_schema(self):
        import io,json,zipfile
        from handoff import make_bundle
        data,name=make_bundle(DEFAULTS)
        with zipfile.ZipFile(io.BytesIO(data)) as z:
            self.assertEqual(json.loads(z.read('settings.json')),DEFAULTS)
            manifest=json.loads(z.read('manifest.json'))
            self.assertEqual(len(manifest['parameters']),len(SCHEMA))
            self.assertEqual(manifest['version'],3)
            self.assertIn('grab',z.read('UNITY_HANDOFF.md').decode())

if __name__=='__main__':unittest.main()
