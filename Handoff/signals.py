"""Gesture mappings v3: mirrored hand steering, raised-hands jump, visual clap/grab."""
import math
import uuid
from settings import DEFAULTS


def observe(poses, config=None):
    config = config or DEFAULTS
    zones = [[], [], []]
    for p in poses:
        def good(ids):
            return all(min(p[i].visibility, p[i].presence) >= config['confidence'] and
                       math.isfinite(p[i].x) and math.isfinite(p[i].y) and
                       0 <= p[i].x <= 1 and 0 <= p[i].y <= 1 for i in ids)
        if not good([11,12]): continue
        x=(p[11].x+p[12].x)/2
        if not 0 <= x < 1: continue
        zone=int(x*3)
        if min(abs(x-1/3),abs(x-2/3)) < config['margin']: continue
        sy=(p[11].y+p[12].y)/2
        width=max(abs(p[11].x-p[12].x),.035)
        hands=good([15,16])
        left,right=(15,16) if p[11].x < p[12].x else (16,15)
        ls,rs=(11,12) if left==15 else (12,11)
        value=dict(height=(sy-(p[15].y+p[16].y)/2)/width if hands else 0.,
                   leftHeight=(p[ls].y-p[left].y)/width if hands else 0.,
                   rightHeight=(p[rs].y-p[right].y)/width if hands else 0.,
                   handsValid=hands, jumpValid=hands, clapValid=hands,
                   handDistance=math.hypot(p[15].x-p[16].x,p[15].y-p[16].y)/width if hands else 0.)
        valid = good([0,15,16]) if zone==0 else hands if zone==1 else hands
        zones[zone].append(value if valid else None)
    return [items[0] if len(items)==1 else None for items in zones]


class Mapping:
    def __init__(self, config=None, mode='team'):
        if mode not in ('team','speed','direction','jump'): raise ValueError('Unknown test mode')
        self.session_id=uuid.uuid4().hex
        self.mode=mode
        self.required=[0,1,2] if mode=='team' else [{'speed':0,'direction':1,'jump':2}[mode]]
        self.config=dict(config or DEFAULTS)
        self.neutral=None
        self.samples=[]
        self.calibrating=False
        self.speed=self.travel_speed=0.
        self.lane=0
        self.direction_ready=self.jump_ready=self.grab_ready=False
        self.jump_at=self.grab_at=-100.
        self.jump_count=self.grab_count=0
        self.last=None

    def calibrate(self):
        # Reset outputs and event counters so a new session has a clear baseline.
        cfg,mode=self.config,self.mode
        self.__init__(cfg,mode)
        self.calibrating=True

    def update(self, obs, now):
        cfg=self.config
        obs=[o if i in self.required else None for i,o in enumerate(obs)]
        a,b,c=obs
        dt=min(.2,max(0.,now-self.last)) if self.last is not None else .03
        self.last=now
        ready=all(obs[i] is not None for i in self.required)
        if 2 in self.required: ready=ready and bool(c and c['jumpValid'] and c['clapValid'])
        if self.calibrating:
            if ready:
                self.samples.append((now,dict(c) if c else {}))
                if now-self.samples[0][0]>=cfg['calibration']:
                    self.neutral={} # Calibration validates upper-body tracking, not floor height.
                    self.calibrating=False
            else:self.samples=[]
        active=self.neutral is not None
        target=max(0.,min(1.,(a['height']-cfg['speed_low'])/(cfg['speed_high']-cfg['speed_low']))) if active and a else 0.
        alpha=1-math.exp(-dt/cfg['smooth'])
        self.speed+=(target-self.speed)*alpha
        travel_target=cfg['min_speed']+self.speed*(cfg['max_speed']-cfg['min_speed']) if active and a else 0.
        self.travel_speed+=(travel_target-self.travel_speed)*alpha
        events=[]
        if not active or b is None:self.direction_ready=False
        elif max(b['leftHeight'],b['rightHeight'])<=cfg['hand_reset']:self.direction_ready=True
        elif self.direction_ready:
            left=b['leftHeight']>=cfg['hand_trigger'];right=b['rightHeight']>=cfg['hand_trigger']
            if left and right:self.direction_ready=False # both raised: no arbitrary direction
            elif left or right:
                self.lane=max(-1,min(1,self.lane+(-1 if left else 1)))
                self.direction_ready=False;events.append('left' if left else 'right')
        jump_hands=[c['leftHeight'],c['rightHeight']] if active and c and c['jumpValid'] else None
        if jump_hands is None:self.jump_ready=False
        elif now-self.jump_at>=cfg['jump_duration']:
            if max(jump_hands)<=cfg['jump_hand_reset']:self.jump_ready=True
            elif self.jump_ready and min(jump_hands)>=cfg['jump_hand_trigger']:
                self.jump_at=now;self.jump_count+=1;self.jump_ready=False;events.append('jump')
        if not active or not c or not c['clapValid']:self.grab_ready=False
        elif c['handDistance']>=cfg['clap_open']:self.grab_ready=True
        elif c['handDistance']<=cfg['clap_close'] and self.grab_ready and now-self.grab_at>=cfg['clap_cooldown']:
            self.grab_at=now;self.grab_count+=1;self.grab_ready=False;events.append('grab')
        phase=(now-self.jump_at)/cfg['jump_duration']
        return dict(protocolVersion=3,sessionId=self.session_id,mode=self.mode,required=self.required,
                    visible=[o is not None for o in obs],calibrated=active,calibrating=self.calibrating,
                    calibrationReady=ready,
                    countdown=max(0.,cfg['calibration']-(now-self.samples[0][0])) if self.samples and self.calibrating else cfg['calibration'],
                    speed=round(self.speed,3),travelSpeed=self.travel_speed,handHeight=a['height'] if a else None,
                    leftHand=b['leftHeight'] if b else None,rightHand=b['rightHeight'] if b else None,
                    directionReady=self.direction_ready,lane=self.lane,
                    jumpHeight=max(0.,4*phase*(1-phase)) if 0<=phase<=1 else 0.,
                    jumpReady=self.jump_ready,jumps=self.jump_count,jumpHandHeight=min(jump_hands) if jump_hands else None,
                    jumpTracked=bool(c and c['jumpValid']),clapTracked=bool(c and c['clapValid']),
                    handDistance=c['handDistance'] if c and c['clapValid'] else None,
                    grabReady=self.grab_ready,grabs=self.grab_count,grabActive=active and now-self.grab_at<.5,
                    events=events,event=events[-1] if events else '')
