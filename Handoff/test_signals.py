import unittest
from types import SimpleNamespace
from signals import Mapping,observe
from settings import DEFAULTS


def body(height=-.8,leftHeight=-.5,rightHeight=-.5,hipY=.55,leftFootY=.9,rightFootY=.9,handDistance=1.2,jumpValid=True,clapValid=True):
    return dict(height=height,leftHeight=leftHeight,rightHeight=rightHeight,hipY=hipY,leftFootY=leftFootY,rightFootY=rightFootY,torso=.25,handDistance=handDistance,jumpValid=jumpValid,clapValid=clapValid)


class Tests(unittest.TestCase):
    def ready(self,mode='team',config=None):
        m=Mapping(config or DEFAULTS,mode);m.calibrate()
        for n in range(32): m.update([body(),body(),body()],n*.1)
        self.assertIsNotNone(m.neutral)
        return m

    def test_left_right_hands_and_rearm(self):
        m=self.ready();m.update([body(),body(leftHeight=.5),body()],4)
        self.assertEqual(m.lane,-1)
        m.update([body(),body(rightHeight=.5),body()],4.2)
        self.assertEqual(m.lane,-1)
        m.update([body(),body(),body()],4.3)
        m.update([body(),body(rightHeight=.5),body()],4.4)
        self.assertEqual(m.lane,0)

    def test_both_hands_and_tracking_recovery_do_not_turn(self):
        m=self.ready();m.update([body(),body(leftHeight=1,rightHeight=1),body()],4)
        self.assertEqual(m.lane,0)
        m.update([body(),None,body()],4.1)
        m.update([body(),body(rightHeight=1),body()],4.2)
        self.assertEqual(m.lane,0)

    def test_jump_requires_both_hands_and_lowering(self):
        for kwargs in [dict(leftHeight=1.2),dict(rightHeight=1.2),dict(hipY=.45,leftFootY=.8,rightFootY=.8)]:
            m=self.ready();out=m.update([body(),body(),body(**kwargs)],4)
            self.assertEqual(out['jumps'],0)
        m=self.ready();up=body(leftHeight=1.2,rightHeight=1.2)
        out=m.update([body(),body(),up],4)
        self.assertEqual(out['jumps'],1)
        for t in [5,6,7]:m.update([body(),body(),up],t)
        self.assertEqual(m.jump_count,1)
        m.update([body(),body(),body()],8)
        m.update([body(),body(),up],9)
        self.assertEqual(m.jump_count,2)

    def test_standing_clap_and_simultaneous_jump_grab(self):
        m=self.ready();out=m.update([body(),body(),body(handDistance=.2)],4)
        self.assertEqual(out['grabs'],1);self.assertEqual(out['jumps'],0)
        m.update([body(),body(),body(handDistance=.2)],5)
        self.assertEqual(m.grab_count,1)
        m.update([body(),body(),body()],6)
        out=m.update([body(),body(),body(leftHeight=1.2,rightHeight=1.2,handDistance=.2)],7)
        self.assertEqual(set(out['events']),{'jump','grab'})
        self.assertEqual(out['grabs'],2)

    def test_invalid_jump_tracking_disarms_jump(self):
        m=self.ready();out=m.update([body(),body(),body(jumpValid=False,handDistance=.2)],4)
        self.assertEqual(out['grabs'],1);self.assertEqual(out['jumps'],0)

    def test_loss_disarms_clap(self):
        m=self.ready();m.update([body(),body(),None],4)
        out=m.update([body(),body(),body(handDistance=.2)],5)
        self.assertEqual(out['grabs'],0)

    def test_speed_unchanged_and_loss_decay(self):
        m=self.ready()
        for n in range(25):m.update([body(height=1),body(),body()],4+n*.1)
        self.assertGreater(m.speed,.99)
        for n in range(30):m.update([None,body(),body()],7+n*.1)
        self.assertLess(m.speed,.01)

    def test_solo_and_team_calibration(self):
        for index,mode in enumerate(['speed','direction','jump']):
            m=Mapping(DEFAULTS,mode);m.calibrate();obs=[None]*3;obs[index]=body()
            for n in range(32):out=m.update(obs,n*.1)
            self.assertTrue(out['calibrated'])
            out=m.update([body(height=1),body(leftHeight=1),body(leftHeight=1.2,rightHeight=1.2,handDistance=.2)],4)
            if index!=0:self.assertEqual(out['speed'],0)
            if index!=1:self.assertEqual(out['lane'],0)
            if index!=2:self.assertEqual(out['jumps']+out['grabs'],0)
        m=Mapping();m.calibrate()
        for n in range(50):out=m.update([body(),body(),body(jumpValid=False)],n*.1)
        self.assertFalse(out['calibrated'])

    def test_new_calibration_changes_session_and_resets_counts(self):
        m=self.ready();old=m.session_id;m.calibrate()
        self.assertNotEqual(old,m.session_id);self.assertEqual(m.grab_count,0)
        self.assertIsNone(m.neutral)

    def test_upper_body_only_can_calibrate_and_jump(self):
        p=[SimpleNamespace(x=.8,y=.5,visibility=0,presence=0) for _ in range(33)]
        for i,x,y in [(11,.75,.4),(12,.85,.4),(15,.75,.6),(16,.85,.6)]:
            p[i]=SimpleNamespace(x=x,y=y,visibility=1,presence=1)
        m=Mapping(mode='jump');m.calibrate()
        for n in range(32):out=m.update(observe([p]),n*.1)
        self.assertTrue(out['calibrated']);self.assertTrue(out['jumpReady'])
        p[15].y=p[16].y=.25
        out=m.update(observe([p]),4)
        self.assertEqual(out['jumps'],1)
        m.update([None,None,None],5)
        out=m.update(observe([p]),6)
        self.assertEqual(out['jumps'],1)

    def test_mirrored_screen_hand_assignment(self):
        p=[SimpleNamespace(x=.5,y=.5,visibility=1,presence=1) for _ in range(33)]
        p[11].x=.55;p[12].x=.45 # anatomical labels may reverse after mirroring
        p[15].y=.7;p[16].y=.3
        b=observe([p])[1]
        self.assertGreater(b['leftHeight'],0)
        self.assertLess(b['rightHeight'],0)

if __name__=='__main__':unittest.main()
